using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 内蔵テストパターン（チェッカー＋格子＋中心十字/円・#34/#35）。
    /// マッピング校正用に Surface の content（<see cref="Surface.ContentKind.Pattern"/>）と
    /// <see cref="OutputManager"/> の校正出力（CalibrationEnabled）が共有する。
    /// 初回アクセス時に一度だけ CPU 生成→RT へ焼き、以後は静的 RT を使い回す（アプリ存命中保持）。
    /// </summary>
    public static class TestPattern
    {
        const int W = 1920, H = 1080;
        const int Cell = 120;            // 格子間隔（16×9 セル）

        static RenderTexture _rt;

        /// <summary>校正パターン RT（初回のみ生成）。</summary>
        public static RenderTexture Texture
        {
            get
            {
                if (_rt == null) Build();
                return _rt;
            }
        }

        static void Build()
        {
            // 投影して物理面と整列させる用途なので、アプリ配色より高コントラスト。
            var bgA    = new Color32(32, 32, 32, 255);
            var bgB    = new Color32(44, 44, 44, 255);
            var line   = new Color32(236, 233, 224, 255);  // off-white（--rr-text 相当）
            var cross  = new Color32(74, 158, 216, 255);   // selection blue（中心線）
            var border = new Color32(255, 92, 26, 255);    // live amber（画面端の見切れ確認）

            var px = new Color32[W * H];
            int cx = W / 2, cy = H / 2;
            const float ringR = 400f, ringT = 2.5f;        // 円＝アスペクト/歪み確認

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    Color32 c = (((x / Cell) + (y / Cell)) & 1) == 0 ? bgA : bgB;

                    if (x % Cell < 2 || y % Cell < 2) c = line;                      // 格子
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (Mathf.Abs(d - ringR) < ringT) c = line;                      // 円
                    if (Mathf.Abs(x - cx) < 2 || Mathf.Abs(y - cy) < 2) c = cross;   // 中心十字
                    if (x < 6 || x >= W - 6 || y < 6 || y >= H - 6) c = border;      // 外周

                    px[y * W + x] = c;
                }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false, false);

            _rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32)
            { name = "testPatternRT", hideFlags = HideFlags.HideAndDontSave };
            _rt.Create();
            Graphics.Blit(tex, _rt);
            Object.Destroy(tex);
        }
    }
}
