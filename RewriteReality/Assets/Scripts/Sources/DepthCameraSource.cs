using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 【将来・M9】深度カメラ（深度センサー・例: Orbbec Femto / Intel RealSense）の色＋深度を供給する。
    /// 接続は ① USB 直結（各社 SDK で色/深度をテクスチャ化）または ② NDI 経由（送出機→NDI-in）。
    /// `IDepthSource` で差替可能ゆえコアは無改修。現状は未実装スタブ（深度なし）。
    /// 旧案の iPhone Pro LiDAR（Rcam3 方式・NDI）は Pro 機がある場合の参考実装で非前提。
    /// </summary>
    public sealed class DepthCameraSource : MonoBehaviour, IDepthSource
    {
        public bool HasDepth => false;
        public Texture DepthTexture => null;

        // TODO(M9): 深度カメラ SDK（Orbbec/RealSense 等）or KlakNDI Receiver で色/深度を受信し、
        //           デコードして DepthTexture を更新する。機種は M9 着手時に選定。
    }
}
