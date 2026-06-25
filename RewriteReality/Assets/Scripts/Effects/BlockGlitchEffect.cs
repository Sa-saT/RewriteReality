using UnityEngine;

namespace RewriteReality
{
    /// <summary>矩形ブロック単位のズレ/差し替えグリッチ。ビート(onset)でトリガする想定。</summary>
    public sealed class BlockGlitchEffect : EffectBase
    {
        public override string Name => "Block Glitch";

        public override void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio)
        {
            // TODO: 専用シェーダでブロック単位の UV ずらし/フリーズ。audio.Onset でブロック更新。
            Graphics.Blit(src, dst); // 暫定: 素通し
        }
    }
}
