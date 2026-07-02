using UnityEngine;
using UnityEngine.UIElements;

namespace RewriteReality
{
    /// <summary>
    /// メッシュワープの編集オーバーレイ（#21）。制御点グリッドの線とハンドルを
    /// <see cref="MeshGenerationContext.painter2D"/> で描画し、ポインタでハンドルをドラッグして
    /// <see cref="IWarpTarget.SetWarpPoint"/> に書き戻す。対象は Compositor / OutputWarp を差し替え可能。
    ///
    /// 座標系: warp のローカル座標は [0,1]²・原点=左下（GL.LoadOrtho と同じ）。UI Toolkit の y は下向きなので
    /// px.y = (1 - local.y) * h で変換する。キャンバスはビューポート全面に重なる（プレビューの letterbox とは
    /// 厳密一致しない＝既知の近似・16:9 同士なら実害小。厳密化は #22 の Input/Output 分割で扱う）。
    /// 再描画は「ドラッグ時のみ」＝毎フレーム GC なし。
    /// </summary>
    public sealed class WarpCanvas : VisualElement
    {
        IWarpTarget _target;
        int _dragIndex = -1;
        int _selected = -1;

        const float HandleHalf = 5f;    // ハンドル正方形の半辺（px）
        const float HitRadius = 14f;    // 掴み判定半径（px）

        // 色は DESIGN.md トークンと対応（ランタイム描画のため var() ではなく直値）。
        static readonly Color LineCol    = new Color(0.29f, 0.62f, 0.85f, 0.55f); // selection blue 55%
        static readonly Color HandleCol  = new Color(0.29f, 0.62f, 0.85f, 1f);    // selection blue
        static readonly Color HandleSel  = new Color(1.00f, 0.36f, 0.10f, 1f);    // live amber
        static readonly Color HandleEdge = new Color(0.086f, 0.082f, 0.071f, 1f); // canvas（縁取り）

        public int SelectedIndex => _selected;

        public WarpCanvas()
        {
            pickingMode = PickingMode.Position;
            style.position = Position.Absolute;
            style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        /// <summary>編集対象を設定（null で解除）。制御点を確保してから再描画する。</summary>
        public void Bind(IWarpTarget target)
        {
            _target = target;
            _target?.EnsureWarpPoints();
            _selected = -1;
            _dragIndex = -1;
            MarkDirtyRepaint();
        }

        static Vector2 ToPixel(Vector2 local, float w, float h)
            => new Vector2(local.x * w, (1f - local.y) * h);

        static Vector2 ToLocal(Vector2 px, float w, float h)
            => new Vector2(
                Mathf.Clamp01(w > 0f ? px.x / w : 0f),
                Mathf.Clamp01(h > 0f ? 1f - px.y / h : 0f));

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (_target == null) return;
            _target.EnsureWarpPoints();
            int cols = _target.WarpCols, rows = _target.WarpRows;
            if (cols < 2 || rows < 2) return;

            var r = contentRect;
            float w = r.width, h = r.height;
            if (w <= 1f || h <= 1f) return;

            var p = mgc.painter2D;

            // メッシュ線（水平＋垂直）
            p.lineWidth = 1f;
            p.strokeColor = LineCol;
            for (int j = 0; j < rows; j++)
            {
                p.BeginPath();
                for (int i = 0; i < cols; i++)
                {
                    var px = ToPixel(_target.GetWarpPoint(i, j), w, h);
                    if (i == 0) p.MoveTo(px); else p.LineTo(px);
                }
                p.Stroke();
            }
            for (int i = 0; i < cols; i++)
            {
                p.BeginPath();
                for (int j = 0; j < rows; j++)
                {
                    var px = ToPixel(_target.GetWarpPoint(i, j), w, h);
                    if (j == 0) p.MoveTo(px); else p.LineTo(px);
                }
                p.Stroke();
            }

            // ハンドル（正方形・選択はアンバー＋やや大きく）
            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    int idx = j * cols + i;
                    bool sel = idx == _selected;
                    float hh = sel ? HandleHalf + 2f : HandleHalf;
                    var c = ToPixel(_target.GetWarpPoint(i, j), w, h);

                    p.fillColor = sel ? HandleSel : HandleCol;
                    p.strokeColor = HandleEdge;
                    p.lineWidth = 1.5f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(c.x - hh, c.y - hh));
                    p.LineTo(new Vector2(c.x + hh, c.y - hh));
                    p.LineTo(new Vector2(c.x + hh, c.y + hh));
                    p.LineTo(new Vector2(c.x - hh, c.y + hh));
                    p.ClosePath();
                    p.Fill();
                    p.Stroke();
                }
            }
        }

        int PickHandle(Vector2 m, float w, float h)
        {
            int cols = _target.WarpCols, rows = _target.WarpRows;
            int best = -1;
            float bestD = HitRadius * HitRadius;
            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    var c = ToPixel(_target.GetWarpPoint(i, j), w, h);
                    float d = (c - m).sqrMagnitude;
                    if (d <= bestD) { bestD = d; best = j * cols + i; }
                }
            }
            return best;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (_target == null) return;
            _target.EnsureWarpPoints();
            var r = contentRect;
            int hit = PickHandle((Vector2)evt.localPosition, r.width, r.height);
            if (hit < 0) return;

            _dragIndex = hit;
            _selected = hit;
            this.CapturePointer(evt.pointerId);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (_dragIndex < 0 || _target == null) return;
            var r = contentRect;
            var local = ToLocal((Vector2)evt.localPosition, r.width, r.height);
            int cols = _target.WarpCols;
            int i = _dragIndex % cols, j = _dragIndex / cols;
            _target.SetWarpPoint(i, j, local);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (_dragIndex < 0) return;
            _dragIndex = -1;
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }
    }
}
