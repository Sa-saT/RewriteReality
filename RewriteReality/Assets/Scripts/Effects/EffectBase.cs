using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// エフェクトの適用範囲（M11・準備 Edit で割当・docs/07b §3.6）。
    /// <see cref="Global"/>=合成後の画面全体（既定・現行の finalRT パス）。
    /// <see cref="Surface"/>=指定 surface の埋め込み内容だけに掛ける（範囲別適用・段階的）。
    /// </summary>
    public enum EffectScope { Global, Surface }

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

        [Header("適用範囲（M11・準備 Edit で割当）")]
        [Tooltip("Global=合成後の画面全体／Surface=指定 surface のみ（範囲別適用・docs/07b §3.6）")]
        public EffectScope scope = EffectScope.Global;

        [Tooltip("scope=Surface のとき対象にする Surface の Id")]
        public int targetSurfaceId = 0;

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
