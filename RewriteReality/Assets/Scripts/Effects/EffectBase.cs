using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// エフェクトの共通抽象基盤。「クラスを1つ足すだけ」で新エフェクトを追加できる拡張の肝。
    /// 有効/無効は MonoBehaviour 標準の <see cref="Behaviour.enabled"/> を使う。
    /// 調整可能パラメータは <see cref="Parameters"/> で自己記述し、操作層（ControlHub）が一律に扱う。
    /// </summary>
    public abstract class EffectBase : MonoBehaviour
    {
        [Range(0f, 1f)]
        [Tooltip("ドライ/ウェットの混合比（1=エフェクト全適用）")]
        public float mix = 1f;

        /// <summary>専用シェーダのマテリアル（派生クラスが用意）。</summary>
        protected Material mat;

        /// <summary>UI 表示・プリセット保存用の名前。</summary>
        public abstract string Name { get; }

        List<EffectParameter> _parameters;

        /// <summary>ライブ操作可能なパラメータ列（Mix は基底で自動追加）。初回アクセス時に一度だけ構築。</summary>
        public IReadOnlyList<EffectParameter> Parameters => _parameters ??= BuildParameters();

        List<EffectParameter> BuildParameters()
        {
            var list = new List<EffectParameter>(4)
            {
                new EffectParameter("Mix", 0f, 1f, () => mix, v => mix = v),
            };
            CollectParameters(list);
            return list;
        }

        /// <summary>派生クラスが調整可能パラメータを追加する（Mix は基底で自動追加済み）。</summary>
        protected virtual void CollectParameters(List<EffectParameter> list) { }

        /// <summary>
        /// <paramref name="src"/> を読み <paramref name="dst"/> に書く。<paramref name="audio"/> は音声特徴。
        /// </summary>
        public abstract void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio);

        protected virtual void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
