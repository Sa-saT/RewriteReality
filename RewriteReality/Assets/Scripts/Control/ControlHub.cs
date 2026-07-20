using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// UI / MIDI(Minis) / OSC(OscJack) を受けてパラメータを一元管理する操作層。
    /// コントローラ非依存の抽象マッピング層（CC 番号やキーを直書きせず、エフェクトが自己記述する
    /// <see cref="EffectParameter"/> を介して読み書きする・docs/07）。
    /// キーボード(#16)/GUI(#17)/将来 MIDI は、この Hub のメソッドと EffectParameter にバインドする。
    /// </summary>
    public sealed class ControlHub : MonoBehaviour
    {
        [SerializeField] EffectChain _effectChain;
        [Tooltip("Master Speed を sequence トランスポートへ反映する先（未設定なら自動取得）")]
        [SerializeField] ShowTimeline _timeline;
        [Tooltip("起動時に MIDI マップ（CC/Note 割当）を自動読込する（opt-in・#M7）")]
        [SerializeField] bool _autoLoadMidiMap = false;

        static readonly IReadOnlyList<EffectBase> _empty = new EffectBase[0];

        void Awake()
        {
            if (_effectChain == null) _effectChain = FindFirstObjectByType<EffectChain>();
            if (_timeline == null) _timeline = FindFirstObjectByType<ShowTimeline>();
            if (_autoLoadMidiMap) LoadMidiMap();
        }

        // ---- Master/Program（右 Inspector 無選択時・§4a・U1）----
        // 実効果は段階的（当面 UI 値の保持のみ。Master/FadeToBlack は将来 EffectChain 側の合成に反映）。
        float _master = 1f;
        float _fadeToBlack = 0f;
        float _masterSpeed = 1f;
        float _bpm = 128f;

        public float Master { get => _master; set => _master = Mathf.Clamp01(value); }
        public float FadeToBlack { get => _fadeToBlack; set => _fadeToBlack = Mathf.Clamp01(value); }
        public float Bpm { get => _bpm; set => _bpm = Mathf.Clamp(value, 20f, 300f); }

        /// <summary>本番全体の再生速度（0..4）。song トランスポート（ShowTimeline.Rate）へ反映する。</summary>
        public float MasterSpeed
        {
            get => _masterSpeed;
            set
            {
                _masterSpeed = Mathf.Clamp(value, 0f, 4f);
                if (_timeline != null) _timeline.Rate = _masterSpeed;
            }
        }

        /// <summary>対象エフェクト列（順序＝適用順）。</summary>
        public IReadOnlyList<EffectBase> Effects => _effectChain != null ? _effectChain.Effects : _empty;
        public int Count => Effects.Count;

        /// <summary>キーボード操作などの「現在の操作対象」。</summary>
        public int SelectedEffect { get; private set; }
        public int SelectedParam { get; private set; }

        public EffectBase GetEffect(int i) => (i >= 0 && i < Count) ? Effects[i] : null;

        // ---- 選択 ----
        public void SelectEffect(int i)
        {
            SelectedEffect = Count > 0 ? Mathf.Clamp(i, 0, Count - 1) : 0;
            SelectedParam = 0;
        }

        public void SelectParam(int i)
        {
            var fx = GetEffect(SelectedEffect);
            int n = fx != null ? fx.Parameters.Count : 0;
            SelectedParam = n > 0 ? Mathf.Clamp(i, 0, n - 1) : 0;
        }

        public void CycleEffect(int dir)  { if (Count > 0) SelectEffect((SelectedEffect + dir + Count) % Count); }
        public void CycleParam(int dir)
        {
            var fx = GetEffect(SelectedEffect);
            int n = fx != null ? fx.Parameters.Count : 0;
            if (n > 0) SelectedParam = (SelectedParam + dir + n) % n;
        }

        // ---- ON/OFF ----
        public void ToggleEffect(int i)            { var fx = GetEffect(i); if (fx != null) fx.enabled = !fx.enabled; }
        public void SetEffectEnabled(int i, bool on){ var fx = GetEffect(i); if (fx != null) fx.enabled = on; }
        public void ToggleSelected()               => ToggleEffect(SelectedEffect);

        // ---- パラメータ取得/操作 ----
        public EffectParameter GetParameter(int effectIndex, int paramIndex)
        {
            var fx = GetEffect(effectIndex);
            if (fx == null) return null;
            var ps = fx.Parameters;
            return (paramIndex >= 0 && paramIndex < ps.Count) ? ps[paramIndex] : null;
        }

        public EffectParameter SelectedParameter => GetParameter(SelectedEffect, SelectedParam);

        /// <summary>選択中パラメータを正規化値で delta 増減（キーボード微調整・MIDI 相対操作用）。</summary>
        public void NudgeSelected(float deltaNormalized) => SelectedParameter?.NudgeNormalized(deltaNormalized);

        /// <summary>選択中パラメータに正規化値を設定（スライダ・MIDI 絶対値用）。</summary>
        public void SetSelectedNormalized(float normalized)
        {
            var p = SelectedParameter;
            if (p != null) p.Normalized = normalized;
        }

        // ===== MIDI 抽象マッピング（M7・CC/Note ラーン・docs/07 §2）=====
        // コントローラ非依存の原則を保つため、CC/Note 番号は「番号→(effect,param)」のマップとしてのみ
        // 扱い、エフェクト側には一切埋め込まない。ラーンで割当を作る（現場で任意 CC を任意パラメータへ）。

        [Serializable] public struct MidiCcBinding   { public int cc;   public int effect; public int param; }
        [Serializable] public struct MidiNoteBinding { public int note; public int effect; }

        [Serializable]
        sealed class MidiMapState
        {
            public List<MidiCcBinding> cc = new List<MidiCcBinding>();
            public List<MidiNoteBinding> note = new List<MidiNoteBinding>();
        }

        readonly Dictionary<int, (int fx, int param)> _ccMap = new Dictionary<int, (int, int)>();
        readonly Dictionary<int, int> _noteMap = new Dictionary<int, int>();
        bool _learning;

        /// <summary>MIDI ラーン待機中か（次に触れた CC/Note を現在の選択へ割り当てる）。</summary>
        public bool IsLearning => _learning;

        /// <summary>ラーン状態 / マップが変わったとき発火（UI 表示更新用）。</summary>
        public event Action MidiMapChanged;

        /// <summary>ラーン開始：次に受けた CC を選択中パラメータへ、Note を選択中エフェクトの ON/OFF へ割当。</summary>
        public void BeginMidiLearn() { _learning = true; MidiMapChanged?.Invoke(); }

        /// <summary>ラーン中止。</summary>
        public void CancelMidiLearn() { if (!_learning) return; _learning = false; MidiMapChanged?.Invoke(); }

        /// <summary>CC を受信（MidiControl から・値は 0..1）。ラーン中なら現在の選択へ割当、以後はマップに従う。</summary>
        public void ApplyMidiCc(int cc, float normalized)
        {
            if (_learning)
            {
                _ccMap[cc] = (SelectedEffect, SelectedParam);
                _learning = false;
                MidiMapChanged?.Invoke();
            }
            if (_ccMap.TryGetValue(cc, out var b))
            {
                var p = GetParameter(b.fx, b.param);
                if (p != null) p.Normalized = normalized;
            }
        }

        /// <summary>Note を受信（MidiControl から）。ラーン中の note-on は現在の選択エフェクトへ割当、
        /// 以後は note-on でそのエフェクトを ON/OFF トグルする（note-off は無視）。</summary>
        public void ApplyMidiNote(int note, bool on)
        {
            if (!on) return;
            if (_learning)
            {
                _noteMap[note] = SelectedEffect;
                _learning = false;
                MidiMapChanged?.Invoke();
                return;
            }
            if (_noteMap.TryGetValue(note, out int fx)) ToggleEffect(fx);
        }

        /// <summary>CC 割当を解除。</summary>
        public void ClearMidiCc(int cc) { if (_ccMap.Remove(cc)) MidiMapChanged?.Invoke(); }

        /// <summary>全 MIDI 割当を解除。</summary>
        public void ClearAllMidiBindings()
        {
            if (_ccMap.Count == 0 && _noteMap.Count == 0) return;
            _ccMap.Clear(); _noteMap.Clear();
            MidiMapChanged?.Invoke();
        }

        // ===== OSC 受信（M7・OscControl から・docs/07 §3）=====
        // アドレス例: /rr/master 0.8 / /rr/fade 0 / /rr/bpm 128 / /rr/speed 1
        //            /rr/fx/<slug>/<paramslug> 0.7 / /rr/fx/<slug>/enabled 1
        // <slug> は EffectBase.Name を小文字化しスペースを '-' に（<paramslug> も同様）。

        /// <summary>グローバル OSC 値を反映（address 末尾セグメントで分岐）。既知キーなら true。
        /// master/fade は 0..1、speed は 0..4、bpm は実 BPM をそのまま受ける。</summary>
        public bool ApplyOscGlobal(string key, float value)
        {
            switch (key)
            {
                case "master": Master = value; return true;
                case "fade":   FadeToBlack = value; return true;
                case "bpm":    Bpm = value; return true;
                case "speed":  MasterSpeed = value; return true;
                default: return false;
            }
        }

        /// <summary>/rr/fx/&lt;slug&gt;/&lt;param&gt; を反映。paramSlug=="enabled" は ON/OFF（value>=0.5）、
        /// それ以外は同名（slug 一致）パラメータへ正規化値を設定。解決できたら true。</summary>
        public bool ApplyOscFx(string fxSlug, string paramSlug, float value)
        {
            var fx = FindEffectBySlug(fxSlug);
            if (fx == null) return false;
            if (paramSlug == "enabled") { fx.enabled = value >= 0.5f; return true; }
            var ps = fx.Parameters;
            for (int i = 0; i < ps.Count; i++)
                if (Slugify(ps[i].Name) == paramSlug) { ps[i].Normalized = value; return true; }
            return false;
        }

        EffectBase FindEffectBySlug(string slug)
        {
            for (int i = 0; i < Count; i++)
            {
                var fx = Effects[i];
                if (fx != null && Slugify(fx.Name) == slug) return fx;
            }
            return null;
        }

        /// <summary>名前をアドレス用スラグに（小文字・スペース→'-'）。OSC 送信側と規約を合わせる。</summary>
        public static string Slugify(string s) => string.IsNullOrEmpty(s) ? "" : s.ToLowerInvariant().Replace(' ', '-');

        // ===== MIDI マップの永続化（opt-in・現場ごとのコントローラ割当を保存）=====
        static string MidiMapPath => Path.Combine(Application.persistentDataPath, "midimap.json");

        [ContextMenu("Save MIDI Map")]
        public void SaveMidiMap()
        {
            var st = new MidiMapState();
            foreach (var kv in _ccMap)   st.cc.Add(new MidiCcBinding { cc = kv.Key, effect = kv.Value.fx, param = kv.Value.param });
            foreach (var kv in _noteMap) st.note.Add(new MidiNoteBinding { note = kv.Key, effect = kv.Value });
            File.WriteAllText(MidiMapPath, JsonUtility.ToJson(st));
        }

        [ContextMenu("Load MIDI Map")]
        public void LoadMidiMap()
        {
            if (!File.Exists(MidiMapPath)) return;
            MidiMapState st;
            try { st = JsonUtility.FromJson<MidiMapState>(File.ReadAllText(MidiMapPath)); }
            catch (Exception e) { Debug.LogWarning($"[ControlHub] LoadMidiMap 失敗: {e.Message}"); return; }
            if (st == null) return;
            _ccMap.Clear(); _noteMap.Clear();
            if (st.cc != null)   foreach (var b in st.cc)   _ccMap[b.cc] = (b.effect, b.param);
            if (st.note != null) foreach (var b in st.note) _noteMap[b.note] = b.effect;
            MidiMapChanged?.Invoke();
        }
    }
}
