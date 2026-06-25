using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// （任意）深度マップを供給する共通インターフェース。
    /// 実装が無い／無効なら、深度依存エフェクトは無効化される（コアは深度なしで完成・M9）。
    /// </summary>
    public interface IDepthSource
    {
        /// <summary>深度を供給できる状態か。</summary>
        bool HasDepth { get; }

        /// <summary>最新の深度テクスチャ（無ければ null）。</summary>
        Texture DepthTexture { get; }
    }
}
