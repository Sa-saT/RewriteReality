using UnityEngine;

namespace RewriteReality
{
    /// <summary>色調補正（露出/コントラスト/彩度/色相）。RMS や低域で揺らす想定。</summary>
    public sealed class ColorGradeEffect : EffectBase
    {
        public override string Name => "Color Grade";

        public override void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio)
        {
            // TODO: 専用シェーダで露出/コントラスト/彩度/色相を適用。mix と audio.Rms/Low で変調。
            Graphics.Blit(src, dst); // 暫定: 素通し
        }
    }
}
