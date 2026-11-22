using System;
using UnityEngine;
using UnityEngine.Video;

namespace RewriteReality
{
    /// <summary>
    /// 映像トラック ×N ＋ Short 同時押しの重ね合成ドライバ（#41・docs/07b §3.5.3）。
    /// <see cref="ShowTimeline"/> の解決済み再生ヘッドを読み、VideoPlayer 群（子 GameObject・Awake で事前生成）で
    /// 各 Video トラックを同時再生して <c>LayerBlend</c> シェーダで下から順に重ねる（base RT）。
    /// Short は押下中だけ <see cref="ApplyShorts"/> でシーン RT の上へ全画面で重ねる。
    /// opt-in：シーン未配置なら <see cref="Manager"/> は従来の単一 SourceVideo 経路のまま。
    /// 有効な間は <see cref="ShowTimeline.ExternalVideoDriver"/> を立て、内蔵の単一 sink バインドを止める。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class TimelineVideoLayers : MonoBehaviour
    {
        [SerializeField] ShowTimeline _timeline;

        [Tooltip("同時に重ねる Video トラックの最大数（i 番目の Video トラック＝下から i 層目）")]
        [SerializeField, Min(1)] int _maxSequenceLayers = 4;
        [Tooltip("Prepare して待機させる Short の最大数")]
        [SerializeField, Min(0)] int _maxShortLayers = 8;
        [Tooltip("各レイヤー RT の解像度")]
        [SerializeField] Vector2Int _resolution = new Vector2Int(1920, 1080);
        [Tooltip("false＝Surface 合成の後・Global エフェクトの前に Short を重ねる / true＝エフェクトの後・Master の前")]
        [SerializeField] bool _shortsAfterEffects = false;
        [Tooltip("Rate==1 のときだけ、再生位置が再生ヘッドからこれ以上ズレたら再同期（秒）")]
        [SerializeField] float _driftThreshold = 0.15f;

        const float FrozenRate = 0.01f;       // これ未満の Rate は Pause（VideoPlayer は速度 0 不可）
        const float MinOpacity = 0.001f;
        const float SeekCooldown = 0.5f;      // シーク直後はドリフト判定をしない（秒・連続シーク防止）

        static readonly int BaseTexID = Shader.PropertyToID("_BaseTex");
        static readonly int OpacityID = Shader.PropertyToID("_Opacity");
        static readonly int ModeID    = Shader.PropertyToID("_Mode");

        sealed class Slot
        {
            public VideoPlayer vp;
            public RenderTexture rt;
            public VideoClip clip;
            public bool active;          // 直近フレームで「再生すべき」と判定されたか（復帰時の頭出し判定用）
            public bool playing;         // Play/Pause の意図（差分ガード）
            public float speed = 1f;
            public float opacity;        // 今フレームの実効 opacity（Composite 用）
            public int blend;            // 今フレームのブレンドモード
            public bool loop;
            public float noDriftUntil;   // この unscaledTime までドリフト補正を行わない
            public int shortIndex = -1;  // Short スロットのみ
            public bool heldNow;         // Short スロットのみ（毎フレーム再計算）
        }

        Slot[] _seq;
        Slot[] _shorts;
        int[] _shortSlotOf = new int[0];   // Short index → _shorts スロット（-1=未割当・再構築時のみ拡張）

        RenderTexture _ppA, _ppB;          // base の ping-pong
        RenderTexture _spA, _spB;          // Short 重ね用 ping-pong（scene サイズ追従）
        Material _mat;
        bool _shaderMissingLogged;

        int _lastTickFrame = -1;
        int _seenResync = int.MinValue;
        int _seenShortsVersion = int.MinValue;
        bool _hasBase;
        double _baseTime;
        Texture _baseTex;

        /// <summary>下から重ねた結果の base（HasBase==false のときは null）。</summary>
        public Texture BaseTexture => _baseTex;
        /// <summary>有効で準備済みのレイヤーが 1 枚以上あるか（false なら Manager は従来の単一経路へ）。</summary>
        public bool HasBase => _hasBase;
        /// <summary>最下段の有効レイヤーの VideoPlayer.time（四隅追従の時刻元・無ければ 0）。</summary>
        public double BaseTime => _baseTime;
        /// <summary>true＝Short を Global エフェクトの後に重ねる。</summary>
        public bool ShortsAfterEffects => _shortsAfterEffects;

        // ---- ライフサイクル ----

        void Awake()
        {
            if (_timeline == null) _timeline = FindFirstObjectByType<ShowTimeline>();

            var sh = Shader.Find("Hidden/RewriteReality/LayerBlend");
            if (sh != null) _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            else LogShaderMissing();

            int w = Mathf.Max(16, _resolution.x), h = Mathf.Max(16, _resolution.y);
            _ppA = NewRT("layers_base_a", w, h);
            _ppB = NewRT("layers_base_b", w, h);

            _seq = new Slot[Mathf.Max(1, _maxSequenceLayers)];
            for (int i = 0; i < _seq.Length; i++) _seq[i] = CreateSlot("SeqLayer" + i, w, h);
            _shorts = new Slot[Mathf.Max(0, _maxShortLayers)];
            for (int i = 0; i < _shorts.Length; i++) _shorts[i] = CreateSlot("ShortLayer" + i, w, h);
        }

        void OnEnable()
        {
            if (_timeline == null) _timeline = FindFirstObjectByType<ShowTimeline>();
            if (_timeline == null) return;
            _timeline.ExternalVideoDriver = true;
            _timeline.ShortFired += OnShortFired;
            _seenResync = int.MinValue;
            _seenShortsVersion = int.MinValue;   // 次の Tick で Short プールを再構築
        }

        void OnDisable()
        {
            if (_timeline != null)
            {
                _timeline.ExternalVideoDriver = false;
                _timeline.ShortFired -= OnShortFired;
            }
            PauseAll();
            _hasBase = false;
            _baseTex = null;
            _lastTickFrame = -1;
        }

        void OnDestroy()
        {
            ReleaseSlots(_seq);
            ReleaseSlots(_shorts);
            ReleaseRT(ref _ppA); ReleaseRT(ref _ppB);
            ReleaseRT(ref _spA); ReleaseRT(ref _spB);
            if (_mat != null) { Destroy(_mat); _mat = null; }
        }

        void LateUpdate() => Tick();

        // ---- 毎フレーム ----

        /// <summary>
        /// 1 フレーム分の更新（再生状態の同期＋base 合成）。同一フレームでの重複呼び出しは無視する
        /// （<see cref="Manager"/> が実行順を確定させるため先に呼ぶ）。
        /// </summary>
        public void Tick()
        {
            if (_lastTickFrame == Time.frameCount) return;
            _lastTickFrame = Time.frameCount;

            _hasBase = false;
            _baseTex = null;
            _baseTime = 0d;
            if (_timeline == null || _seq == null || !isActiveAndEnabled) return;

            if (_seenShortsVersion != _timeline.ShortsVersion)
            {
                _seenShortsVersion = _timeline.ShortsVersion;
                RebuildShorts();
            }
            UpdateShortSlots();

            bool resync = _seenResync != _timeline.ResyncSerial;
            _seenResync = _timeline.ResyncSerial;

            var seq = _timeline.PlayheadSequence;
            double t = _timeline.PlayheadLocalTime;
            float rate = _timeline.Rate;
            bool playing = _timeline.Playing;

            // Sequence レイヤー：i 番目の Video トラック → スロット i
            int slotIdx = 0;
            if (seq != null && seq.tracks != null)
            {
                for (int k = 0; k < seq.tracks.Count && slotIdx < _seq.Length; k++)
                {
                    var tr = seq.tracks[k];
                    if (tr == null || tr.kind != ShowTimeline.TrackKind.Video) continue;
                    UpdateSeqSlot(_seq[slotIdx], tr, t, rate, playing, resync, slotIdx);
                    slotIdx++;
                }
            }
            for (; slotIdx < _seq.Length; slotIdx++) Deactivate(_seq[slotIdx]);

            Composite(seq);
        }

        void UpdateSeqSlot(Slot s, ShowTimeline.Track tr, double t, float rate, bool playing, bool resync, int layerIndex)
        {
            var c = ShowTimeline.ClipAt(tr, t);
            var asset = c != null ? _timeline.ResolveVideoAsset(c.sourceId) : null;
            float opacity = (tr.enabled && c != null) ? tr.opacity * ShowTimeline.Envelope(c, t) : 0f;
            if (asset == null || opacity < MinOpacity) { Deactivate(s); return; }

            double target = Math.Max(0.0, t - c.start);
            double maxT = Math.Max(0.0, asset.length - 0.05);
            if (target > maxT) target = maxT;

            bool needSeek = false;
            if (s.clip != asset)
            {
                s.vp.clip = asset;
                s.clip = asset;
                s.playing = false;      // clip 差し替えで VideoPlayer は停止する → 下の SetPlaying で再 Play
                s.vp.Prepare();         // 一時停止中（Rate≈0）でも最初のフレームが出るよう準備
                needSeek = true;
            }
            if (!s.active || resync) needSeek = true;   // 非活性からの復帰・不連続ジャンプは頭出し
            s.active = true;
            s.opacity = opacity;
            s.blend = (int)tr.blend;

            if (s.loop) { s.loop = false; s.vp.isLooping = false; }

            bool frozen = !playing || rate < FrozenRate;
            if (!frozen)
            {
                float spd = Mathf.Clamp(rate, FrozenRate, 10f);
                if (!Mathf.Approximately(s.speed, spd)) { s.speed = spd; s.vp.playbackSpeed = spd; }
            }

            if (needSeek)
            {
                s.vp.time = target;
                s.noDriftUntil = UnityEngine.Time.unscaledTime + SeekCooldown;
            }
            else if (!frozen && Mathf.Approximately(rate, 1f) && s.vp.isPrepared
                     && UnityEngine.Time.unscaledTime >= s.noDriftUntil
                     && Math.Abs(s.vp.time - target) > _driftThreshold)
            {
                s.vp.time = target;   // Rate==1 のときだけドリフト補正（Rate≠1 は意図的なズレ）
                s.noDriftUntil = UnityEngine.Time.unscaledTime + SeekCooldown;
            }

            SetPlaying(s, !frozen);
        }

        void Composite(ShowTimeline.Sequence seq)
        {
            if (_mat == null) return;

            Texture cur = null;
            RenderTexture dst = _ppA;
            int slotIdx = 0;
            if (seq == null || seq.tracks == null) return;
            for (int k = 0; k < seq.tracks.Count && slotIdx < _seq.Length; k++)
            {
                var tr = seq.tracks[k];
                if (tr == null || tr.kind != ShowTimeline.TrackKind.Video) continue;
                var s = _seq[slotIdx++];
                if (!s.active || !s.vp.isPrepared) continue;

                float opacity = s.opacity;
                if (opacity < MinOpacity) continue;

                if (cur == null)
                {
                    // 最下段の有効層＝下地。opacity==1 なら RT をそのまま使う（Blit 不要）。無ければ黒の上に載せる。
                    _baseTime = s.vp.time;
                    if (opacity >= 1f - MinOpacity) { cur = s.rt; continue; }
                    Blend(s.rt, Texture2D.blackTexture, dst, opacity, 0);
                    cur = dst;
                    dst = _ppB;
                    continue;
                }

                Blend(s.rt, cur, dst, opacity, s.blend);
                cur = dst;
                dst = (dst == _ppA) ? _ppB : _ppA;
            }

            if (cur == null) return;
            _hasBase = true;
            _baseTex = cur;
        }

        void Blend(Texture layer, Texture baseTex, RenderTexture dst, float opacity, int mode)
        {
            _mat.SetTexture(BaseTexID, baseTex);
            _mat.SetFloat(OpacityID, opacity);
            _mat.SetFloat(ModeID, mode);
            Graphics.Blit(layer, dst, _mat);
        }

        // ---- Short ----

        void RebuildShorts()
        {
            int n = _timeline.ShortCount;
            if (_shortSlotOf.Length < n) _shortSlotOf = new int[n];   // 構成変化時のみ確保
            for (int i = 0; i < _shortSlotOf.Length; i++) _shortSlotOf[i] = -1;

            int used = 0;
            for (int i = 0; i < n && used < _shorts.Length; i++)
            {
                var sh = _timeline.GetShort(i);
                if (sh == null || sh.clip == null) continue;
                if (sh.pad < 0 && string.IsNullOrEmpty(sh.key)) continue;     // pad/key 未割当は対象外
                var asset = _timeline.ResolveVideoAsset(sh.clip.sourceId);
                if (asset == null) continue;

                var s = _shorts[used];
                _shortSlotOf[i] = used;
                s.shortIndex = i;
                s.loop = sh.holdLoop;
                s.vp.isLooping = sh.holdLoop;
                if (s.clip != asset)
                {
                    s.vp.Stop();
                    s.vp.clip = asset;
                    s.clip = asset;
                    s.vp.time = 0;
                    s.playing = false;
                    s.vp.Prepare();       // 押下→即表示のため先読みして待機
                }
                used++;
            }
            for (; used < _shorts.Length; used++)
            {
                var s = _shorts[used];
                s.shortIndex = -1;
                if (s.clip != null || s.playing)
                {
                    s.vp.Stop();
                    s.vp.clip = null;
                    s.clip = null;
                    s.playing = false;
                }
            }
        }

        void UpdateShortSlots()
        {
            for (int i = 0; i < _shorts.Length; i++) _shorts[i].heldNow = false;
            int held = _timeline.HeldCount;
            for (int k = 0; k < held; k++)
            {
                int si = _timeline.GetHeldShortIndex(k);
                if (si >= 0 && si < _shortSlotOf.Length && _shortSlotOf[si] >= 0)
                    _shorts[_shortSlotOf[si]].heldNow = true;
            }

            for (int i = 0; i < _shorts.Length; i++)
            {
                var s = _shorts[i];
                if (s.clip == null) continue;
                var sh = s.shortIndex >= 0 ? _timeline.GetShort(s.shortIndex) : null;
                if (sh != null && s.loop != sh.holdLoop) { s.loop = sh.holdLoop; s.vp.isLooping = sh.holdLoop; }

                if (s.heldNow)
                {
                    if (!s.playing) { s.vp.time = 0; SetPlaying(s, true); }   // 発火イベントを取り逃した場合の保険
                }
                else if (s.playing)
                {
                    SetPlaying(s, false);   // 離したら Pause＋頭へ戻して待機
                    s.vp.time = 0;
                }
            }
        }

        void OnShortFired(int shortIndex)
        {
            if (_shorts == null || shortIndex < 0 || shortIndex >= _shortSlotOf.Length) return;
            int si = _shortSlotOf[shortIndex];
            if (si < 0) return;
            var s = _shorts[si];
            if (s.clip == null) return;
            s.vp.time = 0;           // 新規発火＝頭出し
            SetPlaying(s, true);
        }

        /// <summary>
        /// 押下中の Short をシーン RT の上へ全画面・opacity 1・Normal で重ねて返す。
        /// 押下ゼロ（または準備済み Short なし）なら scene をそのまま返す（Blit しない）。
        /// </summary>
        public RenderTexture ApplyShorts(RenderTexture scene)
        {
            if (scene == null || _timeline == null || _mat == null || _shorts == null || !isActiveAndEnabled) return scene;
            int held = _timeline.HeldCount;
            if (held == 0) return scene;

            Texture cur = scene;
            RenderTexture dst = null;
            for (int k = 0; k < held; k++)
            {
                int si = _timeline.GetHeldShortIndex(k);
                if (si < 0 || si >= _shortSlotOf.Length || _shortSlotOf[si] < 0) continue;
                var s = _shorts[_shortSlotOf[si]];
                if (s.clip == null || !s.vp.isPrepared) continue;

                if (dst == null) { EnsureShortRTs(scene); dst = _spA; }
                Blend(s.rt, cur, dst, 1f, 0);
                cur = dst;
                dst = (dst == _spA) ? _spB : _spA;
            }
            return cur == scene ? scene : (RenderTexture)cur;
        }

        void EnsureShortRTs(RenderTexture reference)
        {
            if (_spA != null && _spA.width == reference.width && _spA.height == reference.height) return;
            ReleaseRT(ref _spA); ReleaseRT(ref _spB);
            _spA = NewRT("layers_short_a", reference.width, reference.height);
            _spB = NewRT("layers_short_b", reference.width, reference.height);
        }

        // ---- 補助 ----

        static void SetPlaying(Slot s, bool want)
        {
            if (s.playing == want) return;
            s.playing = want;
            if (want) s.vp.Play(); else s.vp.Pause();
        }

        static void Deactivate(Slot s)
        {
            if (s.active) s.active = false;
            SetPlaying(s, false);
        }

        void PauseAll()
        {
            if (_seq != null) for (int i = 0; i < _seq.Length; i++) { _seq[i].active = false; SetPlaying(_seq[i], false); }
            if (_shorts != null)
                for (int i = 0; i < _shorts.Length; i++)
                {
                    var s = _shorts[i];
                    if (s.playing) { SetPlaying(s, false); if (s.clip != null) s.vp.time = 0; }
                }
        }

        Slot CreateSlot(string name, int w, int h)
        {
            var go = new GameObject(name);
            go.SetActive(false);                       // 設定完了前に playOnAwake で走らせない
            go.transform.SetParent(transform, false);
            var vp = go.AddComponent<VideoPlayer>();
            var rt = NewRT(name + "_rt", w, h);
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rt;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.skipOnDrop = true;
            vp.isLooping = false;
            vp.aspectRatio = VideoAspectRatio.Stretch;
            go.SetActive(true);
            return new Slot { vp = vp, rt = rt };
        }

        static RenderTexture NewRT(string name, int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = name };
            rt.Create();
            return rt;
        }

        static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        static void ReleaseSlots(Slot[] slots)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                if (s.vp != null) { s.vp.targetTexture = null; }
                ReleaseRT(ref s.rt);
            }
        }

        void LogShaderMissing()
        {
            if (_shaderMissingLogged) return;
            _shaderMissingLogged = true;
            Debug.LogWarning("[TimelineVideoLayers] Hidden/RewriteReality/LayerBlend が見つかりません。レイヤー合成は無効です。");
        }
    }
}
