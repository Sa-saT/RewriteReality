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
    }
}
