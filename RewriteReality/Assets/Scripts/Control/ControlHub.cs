using System.Collections.Generic;
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
        [Tooltip("Master Speed を song トランスポートへ反映する先（未設定なら自動取得）")]
        [SerializeField] ShowTimeline _timeline;

        static readonly IReadOnlyList<EffectBase> _empty = new EffectBase[0];

        void Awake()
        {
            if (_effectChain == null) _effectChain = FindFirstObjectByType<EffectChain>();
            if (_timeline == null) _timeline = FindFirstObjectByType<ShowTimeline>();
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
    }
}
