using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 【将来オプション】実行時 ArUco 検出で四隅を返す。OpenCvSharp(Aruco) + AsyncGPUReadback。
    /// これを使う時のみ Apple Silicon arm64 ネイティブの go/no-go が発生する（docs/12）。
    /// Compositor 以降は無改修で差し替え可能（ICornerSource のため）。
    /// 現状は未実装スタブ（常に取得失敗を返す）。
    /// </summary>
    public sealed class LiveCvCornerSource : MonoBehaviour, ICornerSource
    {
        public bool TryGetCorners(double time, out Corners corners)
        {
            // TODO(M後半/将来):
            //  1) AsyncGPUReadback で縮小フレームを CPU へ
            //  2) OpenCvSharp Aruco でマーカー検出 → findHomography
            //  3) 検出は数フレームに1回へ間引き、間は前回四隅を補間/予測(KLT)
            corners = Corners.FullFrame;
            return false;
        }
    }
}
