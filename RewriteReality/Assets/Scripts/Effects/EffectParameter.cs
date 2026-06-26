using System;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// ライブ操作可能な float パラメータの抽象。コントローラ非依存の操作層（ControlHub）が
    /// 名前・範囲・get/set を通じて一律に読み書きする（CC 番号やキーを直書きしない）。
    /// キーボード/GUI/将来 MIDI(Minis) は、この型を介して同じパラメータへバインドする。
    /// </summary>
    public sealed class EffectParameter
    {
        public readonly string Name;
        public readonly float Min;
        public readonly float Max;

        readonly Func<float> _get;
        readonly Action<float> _set;

        public EffectParameter(string name, float min, float max, Func<float> get, Action<float> set)
        {
            Name = name; Min = min; Max = max; _get = get; _set = set;
        }

        /// <summary>実値（範囲でクランプして設定）。</summary>
        public float Value
        {
            get => _get();
            set => _set(Mathf.Clamp(value, Min, Max));
        }

        /// <summary>0..1 正規化値（MIDI CC / スライダ用）。</summary>
        public float Normalized
        {
            get => Max > Min ? Mathf.InverseLerp(Min, Max, _get()) : 0f;
            set => _set(Mathf.Lerp(Min, Max, Mathf.Clamp01(value)));
        }

        /// <summary>正規化値を delta 分だけ増減（キーボードの微調整など）。</summary>
        public void NudgeNormalized(float deltaNormalized) => Normalized = Normalized + deltaNormalized;
    }
}
