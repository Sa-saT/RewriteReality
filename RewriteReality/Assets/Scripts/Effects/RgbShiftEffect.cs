using UnityEngine;

namespace RewriteReality
{
    /// <summary>RGB チャンネルを横/縦にずらすグリッチ。音の高域で振幅を煽る想定。</summary>
    public sealed class RgbShiftEffect : EffectBase
    {
        public override string Name => "RGB Shift";

        public override void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio)
        {
            // TODO: 専用シェーダで R/G/B を別オフセットでサンプル。mix と audio.High で振幅制御。
            Graphics.Blit(src, dst); // 暫定: 素通し
        }
    }
}
