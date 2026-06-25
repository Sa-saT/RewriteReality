using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 【将来・M9】iPhone LiDAR の色＋深度を NDI-in で受信し供給する（Rcam3 方式の流用）。
    /// 新規依存は実質ゼロ（KlakNDI を流用）。現状は未実装スタブ（深度なし）。
    /// </summary>
    public sealed class RcamDepthSource : MonoBehaviour, IDepthSource
    {
        public bool HasDepth => false;
        public Texture DepthTexture => null;

        // TODO(M9): KlakNDI Receiver で Rcam3 のメタデータ付きフレームを受信し、
        //           色/深度をデコードして DepthTexture を更新する。
    }
}
