using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 単位正方形 → 四隅クアッドの射影変換（Heckbert）を解く純粋な数学ヘルパー。
    /// 埋め込み合成（<see cref="Compositor"/>）と出力変形（<see cref="OutputWarp"/>）が共有し、
    /// 射影数学の二重定義を防ぐ。状態を持たない（GC フリー・呼び出し側でループ可）。
    /// 対応: P0=BL(0,0) P1=BR(1,0) P2=TR(1,1) P3=TL(0,1)。
    /// </summary>
    public static class WarpMath
    {
        /// <summary>単位正方形→四隅 の射影係数。</summary>
        public struct Homography { public float a, b, c, d, e, f, g, h; }

        /// <summary>四隅（正規化座標）から射影係数を解く。退化時はアフィン（平行四辺形）にフォールバック。</summary>
        public static Homography Solve(Vector2 bl, Vector2 br, Vector2 tr, Vector2 tl)
        {
            float x0 = bl.x, y0 = bl.y;
            float x1 = br.x, y1 = br.y;
            float x2 = tr.x, y2 = tr.y;
            float x3 = tl.x, y3 = tl.y;

            float sx = x0 - x1 + x2 - x3;
            float sy = y0 - y1 + y2 - y3;

            Homography m;
            if (Mathf.Abs(sx) < 1e-6f && Mathf.Abs(sy) < 1e-6f)
            {
                // アフィン（平行四辺形）
                m.a = x1 - x0; m.b = x3 - x0; m.c = x0;
                m.d = y1 - y0; m.e = y3 - y0; m.f = y0;
                m.g = 0f; m.h = 0f;
            }
            else
            {
                float dx1 = x1 - x2, dx2 = x3 - x2;
                float dy1 = y1 - y2, dy2 = y3 - y2;
                float den = dx1 * dy2 - dx2 * dy1;
                if (Mathf.Abs(den) < 1e-9f) den = 1e-9f;
                m.g = (sx * dy2 - sy * dx2) / den;
                m.h = (dx1 * sy - dy1 * sx) / den;
                m.a = x1 - x0 + m.g * x1; m.b = x3 - x0 + m.h * x3; m.c = x0;
                m.d = y1 - y0 + m.g * y1; m.e = y3 - y0 + m.h * y3; m.f = y0;
            }
            return m;
        }

        /// <summary>
        /// ローカル座標 (lx, ly)∈[0,1]² を射影変換し、スクリーン位置 (outX, outY) と同次分母 w を返す。
        /// 射影補間は呼び出し側で uvq=(u*w, v*w, w) を作り、フラグメントで /w する（CornerPin シェーダ）。
        /// </summary>
        public static void Project(in Homography m, float lx, float ly,
                                   out float outX, out float outY, out float w)
        {
            float xp = m.a * lx + m.b * ly + m.c;
            float yp = m.d * lx + m.e * ly + m.f;
            float wp = m.g * lx + m.h * ly + 1f;
            if (wp < 1e-5f) wp = 1e-5f;
            outX = xp / wp;
            outY = yp / wp;
            w = wp;
        }

        /// <summary>
        /// N×M 制御点グリッドを等間隔（ワープ無し）で埋める。row-major（j*cols+i）、各点は [0,1]²。
        /// <see cref="Compositor"/> と <see cref="Surface"/> が同じ規約でグリッドを生成するため共有する。
        /// </summary>
        public static void FillRegularGrid(Vector2[] pts, int cols, int rows)
        {
            if (pts == null || cols < 2 || rows < 2) return;
            float invCx = 1f / (cols - 1), invCy = 1f / (rows - 1);
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                    pts[j * cols + i] = new Vector2(i * invCx, j * invCy);
        }

        // ---- Bezier（Catmull-Rom）グリッド評価（#34・MadMapper GridGenerator 手本）----
        //
        // 制御点グリッドを bicubic Catmull-Rom で補間し、任意の parametric (u,v)∈[0,1]² のローカル位置を返す。
        // Catmull-Rom は「制御点を通り」「C1 連続」なので、点を1つ動かすと折れ線の皺ではなく滑らかな膨らみになる
        // （区分線形＝bilinear の Grid を Bezier 面へ一本化）。細分化して評価すれば滑らかなワープメッシュが得られる。
        // 端は最近傍クランプ。2×n / n×2 は接線＝弦になり厳密に線形へ縮退する（ワープ無し・従来 4pin と同結果＝後方互換）。
        // すべて struct 演算で GC フリー（呼び出し側で細分化ループ可）。

        static float Cr(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1)
                         + (-p0 + p2) * t
                         + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                         + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static Vector2 Cr(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
            => new Vector2(Cr(p0.x, p1.x, p2.x, p3.x, t), Cr(p0.y, p1.y, p2.y, p3.y, t));

        // 範囲外（±1 まで）は端点の点対称で外挿する: P(-1)=2·P(0)−P(1) / P(n)=2·P(n−1)−P(n−2)。
        // クランプ複製（旧実装）だと端セグメントの接線が弦の半分になり、**等間隔グリッド＝ワープ無し**でも
        // 恒等写像にならず画の端が縮む（グリッド解像度を変えるだけで画像の比率が変わって見えるバグの原因）。
        // 点対称外挿なら等間隔グリッドはどの解像度でも厳密に線形＝恒等になる。
        static Vector2 GridPointX(Vector2[] pts, int cols, int j, int i)
        {
            if (i < 0)     return 2f * pts[j * cols] - pts[j * cols + 1];
            if (i >= cols) return 2f * pts[j * cols + (cols - 1)] - pts[j * cols + (cols - 2)];
            return pts[j * cols + i];
        }

        // 行 j（範囲内であること）の i-1..i+2 を u 方向に Catmull-Rom 補間（i は外挿対応）
        static Vector2 RowCr(Vector2[] pts, int cols, int j, int i, float tx)
            => Cr(GridPointX(pts, cols, j, i - 1),
                  GridPointX(pts, cols, j, i),
                  GridPointX(pts, cols, j, i + 1),
                  GridPointX(pts, cols, j, i + 2), tx);

        /// <summary>
        /// 制御点グリッド（row-major・各点 [0,1]²）を bicubic Catmull-Rom 補間して parametric (u,v)∈[0,1]² の
        /// ローカル位置を返す。制御点を通る滑らかな面（Bezier 相当）。細分化した各頂点でこれを評価する。
        /// 等間隔グリッドは厳密に恒等（端は点対称外挿・上のコメント参照）。
        /// </summary>
        public static Vector2 SampleGridSmooth(Vector2[] pts, int cols, int rows, float u, float v)
        {
            if (pts == null || cols < 2 || rows < 2) return new Vector2(u, v);

            float fx = Mathf.Clamp01(u) * (cols - 1);
            int i = Mathf.Min((int)fx, cols - 2);
            float tx = fx - i;
            float fy = Mathf.Clamp01(v) * (rows - 1);
            int j = Mathf.Min((int)fy, rows - 2);
            float ty = fy - j;

            // 行方向も点対称外挿（Cr は制御点に線形なので、行の評価結果を外挿してよい）
            Vector2 r1 = RowCr(pts, cols, j,     i, tx);
            Vector2 r2 = RowCr(pts, cols, j + 1, i, tx);
            Vector2 r0 = (j - 1 >= 0)   ? RowCr(pts, cols, j - 1, i, tx) : 2f * r1 - r2;
            Vector2 r3 = (j + 2 < rows) ? RowCr(pts, cols, j + 2, i, tx) : 2f * r2 - r1;
            return Cr(r0, r1, r2, r3, ty);
        }

        /// <summary>
        /// 制御点グリッドを bilinear（区分線形）補間して parametric (u,v) のローカル位置を返す。
        /// Bezier OFF（§7b Mesh Warping の Linear）用。制御点間は直線＝MadMapper の Bezier 無効時と同じ。
        /// </summary>
        public static Vector2 SampleGridLinear(Vector2[] pts, int cols, int rows, float u, float v)
        {
            if (pts == null || cols < 2 || rows < 2) return new Vector2(u, v);

            float fx = Mathf.Clamp01(u) * (cols - 1);
            int i = Mathf.Min((int)fx, cols - 2);
            float tx = fx - i;
            float fy = Mathf.Clamp01(v) * (rows - 1);
            int j = Mathf.Min((int)fy, rows - 2);
            float ty = fy - j;

            Vector2 p00 = pts[j * cols + i],       p10 = pts[j * cols + i + 1];
            Vector2 p01 = pts[(j + 1) * cols + i], p11 = pts[(j + 1) * cols + i + 1];
            return Vector2.LerpUnclamped(Vector2.LerpUnclamped(p00, p10, tx),
                                         Vector2.LerpUnclamped(p01, p11, tx), ty);
        }

        /// <summary>bezier=true なら Catmull-Rom 面、false なら bilinear。呼び出し側の分岐を 1 箇所に。</summary>
        public static Vector2 SampleGrid(Vector2[] pts, int cols, int rows, float u, float v, bool bezier)
            => bezier ? SampleGridSmooth(pts, cols, rows, u, v)
                      : SampleGridLinear(pts, cols, rows, u, v);

        /// <summary>Grid(Bezier) 細分化の 1 辺あたり頂点数（制御 n → 細分後 (n-1)*sub+1）。</summary>
        public static int FineCount(int n, int sub) => (Mathf.Max(2, n) - 1) * Mathf.Max(1, sub) + 1;
    }
}
