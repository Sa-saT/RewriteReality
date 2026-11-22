using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// タイムラインの音声トラック ×N を同時に鳴らして加算ミックスする（M13・#40・docs/07b §3.5.1「内部ミックスの確定仕様」）。
    /// 旧実装（解決できた最上位トラック 1 本だけを <see cref="SourceAudio"/> へ流す）を置き換える opt-in コンポーネント。
    /// 有効な間は <see cref="ShowTimeline.ExternalAudioDriver"/> を立てて内蔵の単一 sink バインドを止める
    /// （未配置/無効なら従来経路のまま＝非破壊）。
    /// ボイス i ＝ <see cref="ShowTimeline.PlayheadSequence"/> の i 番目の Audio トラック。各ボイスは専用 AudioSource を持ち、
    /// AudioListener 上で加算ミックスされる。<see cref="AudioAnalyzer"/> は AudioListener.GetSpectrumData で最終ミックスを
    /// 解析するため無改修で内部ミックスが解析対象になる。
    /// 音は Master Speed（Rate）の影響を受けない＝pitch は常に 1（B9/B10）。Short は音声レイヤーを持たない。
    /// </summary>
    public sealed class TimelineAudioMixer : MonoBehaviour
    {
        [Tooltip("駆動元のタイムライン。null なら実行時に FindFirstObjectByType で探す。")]
        [SerializeField] ShowTimeline _timeline;
        [Tooltip("同時に鳴らせる音声トラック数（事前生成する AudioSource の数・1..32）。超過トラックは鳴らさない。")]
        [SerializeField] int _maxVoices = 8;
        [Tooltip("再生位置が playhead からこの秒数を超えてズレたら再同期する（秒）。")]
        [SerializeField] float _driftThreshold = 0.12f;
        [Tooltip("実音量が目標ゲインへ追従する速度（/秒・プチノイズ対策）。")]
        [SerializeField] float _gainSlew = 12f;

        const float Epsilon = 0.02f;        // クリップ末尾の安全マージン（秒）
        const float SilentVolume = 0.001f;  // これ未満は無音扱い（Pause 判定）

        // ボイス状態（配列で保持・プールは Awake 時のみ構築＝以後 new しない）。
        AudioSource[] _src;
        AudioClip[] _clip;   // 現在 AudioSource.clip に載せているクリップ（差分ガード用）
        bool[] _running;     // 再生中（Play 済みで Pause/Stop していない）
        int _voiceCount;

        int _lastSerial;
        bool _driverOn;
        bool _warnedOverflow;
        bool _warnedNoTimeline;

        void Awake()
        {
            _voiceCount = Mathf.Clamp(_maxVoices, 1, 32);
            _src = new AudioSource[_voiceCount];
            _clip = new AudioClip[_voiceCount];
            _running = new bool[_voiceCount];
            for (int i = 0; i < _voiceCount; i++)
            {
                var go = new GameObject("AudioVoice " + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.loop = false;
                s.pitch = 1f;   // 常に等速（Rate は映像専用）
                s.volume = 0f;
                _src[i] = s;
            }
        }

        void OnEnable()
        {
            _warnedNoTimeline = false;
            AttachDriver();
        }

        void OnDisable()
        {
            if (_timeline != null && _driverOn) _timeline.ExternalAudioDriver = false;
            _driverOn = false;
            StopAll();
        }

        void AttachDriver()
        {
            if (_timeline == null) _timeline = FindFirstObjectByType<ShowTimeline>();
            if (_timeline == null) return;
            _timeline.ExternalAudioDriver = true;
            _driverOn = true;
            _lastSerial = _timeline.ResyncSerial;
        }

        void StopAll()
        {
            if (_src == null) return;
            for (int i = 0; i < _voiceCount; i++)
            {
                if (_src[i] == null) continue;
                _src[i].Stop();
                _src[i].clip = null;
                _src[i].volume = 0f;
                _clip[i] = null;
                _running[i] = false;
            }
        }

        void LateUpdate()
        {
            if (_src == null) return;
            if (_timeline == null) _driverOn = false;   // タイムライン破棄（Unity null）→ 再探索
            if (!_driverOn)
            {
                AttachDriver();
                if (!_driverOn)
                {
                    if (!_warnedNoTimeline) { Debug.LogWarning("[TimelineAudioMixer] ShowTimeline が見つかりません。"); _warnedNoTimeline = true; }
                    return;
                }
            }

            var seq = _timeline.PlayheadSequence;   // null 可（空 Song 等）→ 全ボイス無音
            double t = _timeline.PlayheadLocalTime;
            bool playing = _timeline.Playing;
            int serial = _timeline.ResyncSerial;
            bool serialChanged = serial != _lastSerial;
            _lastSerial = serial;
            float dt = Time.deltaTime;

            // ボイス v ＝ seq.tracks から kind==Audio だけを数えた v 番目（trackIndex は単調増加＝全体で O(トラック数)）。
            int trackIndex = 0;
            int audioCount = 0;
            bool hasTracks = seq != null && seq.tracks != null;
            for (int v = 0; v < _voiceCount; v++)
            {
                ShowTimeline.Track tr = null;
                if (hasTracks)
                {
                    while (trackIndex < seq.tracks.Count)
                    {
                        var cand = seq.tracks[trackIndex++];
                        if (cand != null && cand.kind == ShowTimeline.TrackKind.Audio) { tr = cand; audioCount++; break; }
                    }
                }
                UpdateVoice(v, tr, t, playing, serialChanged, dt);
            }

            // 超過分（ボイス数より Audio トラックが多い）は鳴らさず警告を 1 回だけ。
            if (hasTracks && !_warnedOverflow)
            {
                while (trackIndex < seq.tracks.Count)
                {
                    var cand = seq.tracks[trackIndex++];
                    if (cand != null && cand.kind == ShowTimeline.TrackKind.Audio) audioCount++;
                }
                if (audioCount > _voiceCount)
                {
                    Debug.LogWarning("[TimelineAudioMixer] Audio トラックが " + audioCount + " 本ありますが同時ボイスは " +
                                     _voiceCount + " です（超過分は鳴りません・Max Voices を増やしてください）。");
                    _warnedOverflow = true;
                }
            }
        }

        void UpdateVoice(int v, ShowTimeline.Track tr, double t, bool playing, bool serialChanged, float dt)
        {
            var src = _src[v];

            // 一時停止中：全ボイス Pause。再開時は _running=false なので位置を合わせ直してから Play し直す。
            if (!playing)
            {
                if (_running[v]) { src.Pause(); _running[v] = false; }
                return;
            }

            var c = tr != null ? ShowTimeline.ClipAt(tr, t) : null;
            var ac = c != null ? _timeline.ResolveAudioAsset(c.sourceId) : null;

            double offset = c != null ? t - c.start : 0.0;
            // AudioClip が Clip.duration より短い場合は末尾以降を無音にする（ループしない）。
            bool inRange = ac != null && offset >= 0.0 && offset < ac.length - Epsilon;

            // 実効ゲイン＝(enabled && !muted ? volume : 0) × Envelope。
            float target = 0f;
            if (inRange && tr.enabled && !tr.muted)
                target = Mathf.Clamp01(tr.volume) * ShowTimeline.Envelope(c, t);

            // クリップ差し替え（clip 代入は再生を止めるので running を落として下の起動経路へ）。
            if (!ReferenceEquals(ac, _clip[v]))
            {
                src.Stop();
                src.clip = ac;
                _clip[v] = ac;
                _running[v] = false;
            }

            // 実音量を目標へ線形追従（差分ガード＝同値なら代入しない）。
            float vol = src.volume;
            if (vol != target)
            {
                vol = Mathf.MoveTowards(vol, target, _gainSlew * dt);
                src.volume = vol;
            }

            // 目標 0 かつ実音量 ≈0 になったら Pause（無駄に回さない）。
            bool want = inRange && (target > 0f || vol > SilentVolume);
            if (!want)
            {
                if (_running[v]) { src.Pause(); _running[v] = false; }
                return;
            }

            if (!_running[v])
            {
                src.time = Mathf.Clamp((float)offset, 0f, ac.length - Epsilon);
                src.Play();
                _running[v] = true;
            }
            else if (serialChanged || Mathf.Abs(src.time - (float)offset) > _driftThreshold)
            {
                src.time = Mathf.Clamp((float)offset, 0f, ac.length - Epsilon);
            }
        }
    }
}
