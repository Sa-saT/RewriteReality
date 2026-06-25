using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// エフェクトの共通抽象基盤。「クラスを1つ足すだけ」で新エフェクトを追加できる拡張の肝。
    /// 有効/無効は MonoBehaviour 標準の <see cref="Behaviour.enabled"/> を使う。
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
