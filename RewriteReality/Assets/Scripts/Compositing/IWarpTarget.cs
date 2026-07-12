using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 多pin メッシュワープの制御点を編集させる共通の口。埋め込み合成（<see cref="Compositor"/>）と
    /// 出力変形（<see cref="OutputWarp"/>）が同じ規約（[0,1]² ローカル座標・row-major: j*cols+i）で実装し、
    /// マッピングUI（<see cref="WarpCanvas"/>・#21）が対象を差し替えて同じ操作でドラッグできるようにする。
    /// </summary>
    public interface IWarpTarget
    {
        int WarpCols { get; }
        int WarpRows { get; }
        Vector2 GetWarpPoint(int i, int j);
        void SetWarpPoint(int i, int j, Vector2 local);
        void ResetWarp();
        /// <summary>制御点グリッドの解像度を変更（等間隔で再生成・#34/§7b Mesh Warping）。</summary>
        void SetGridResolution(int cols, int rows);
        /// <summary>true=Bezier（Catmull-Rom 面・滑らか）/ false=Linear（区分線形）。§7b Mesh Warping。</summary>
        bool BezierInterp { get; set; }
        /// <summary>制御点配列を保証（未生成/解像度不一致なら等間隔で確保）。UI 読み取り前に呼ぶ。</summary>
        void EnsureWarpPoints();
    }
}
