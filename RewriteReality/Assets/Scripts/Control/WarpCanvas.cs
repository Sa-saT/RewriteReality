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
        /// <summary>編集対象。Shape=窓の形（ハンドル＋窓ごと移動）／Content=枠内映像の pan。</summary>
        public enum EditMode { Shape, Content }

        IWarpTarget _target;
        int _dragIndex = -1;
        int _selected = -1;

        EditMode _mode = EditMode.Shape;
        enum Drag { None, Handle, MoveAll, Content }
        Drag _drag = Drag.None;
        Vector2 _lastNorm;              // 直前ポインタ（正規化・delta 用）

        /// <summary>Content モードのドラッグ量（画面正規化 delta）を通知。OperatorUI が surface へ反映。</summary>
        public System.Action<Vector2> ContentPan;

        const float HandleHalf = 5f;    // ハンドル正方形の半辺（px）
        const float HitRadius = 14f;    // 掴み判定半径（px）

        // 格子オーバーレイ（#34/#35）: 制御グリッドを Bezier（Catmull-Rom）評価した細かい格子。
        // レンダー（Compositor.WriteMeshGrid）と同じ WarpMath.SampleGridSmooth を使うので見た目が一致（WYSIWYG）。
        // ワープの掛かり具合を面として視認する（描画はドラッグ/トグル時のみ＝毎フレームコスト無し）。
        const int LatticeLines = 12;    // 縦横の格子線本数（等間隔 u/v）
        const int LatticeSteps = 24;    // 1 本あたりのサンプル数（パッチに沿わせる）
        bool _showLattice;
        Vector2[] _snap;                // 平滑サンプル用の制御点スナップショット（repaint 時のみ更新）

        // 色は DESIGN.md トークンと対応（ランタイム描画のため var() ではなく直値）。
        static readonly Color LineCol    = new Color(0.29f, 0.62f, 0.85f, 0.55f); // selection blue 55%
        static readonly Color LatticeCol = new Color(0.29f, 0.62f, 0.85f, 0.22f); // selection blue 22%（格子）
        static readonly Color HandleCol  = new Color(0.29f, 0.62f, 0.85f, 1f);    // selection blue
        static readonly Color HandleSel  = new Color(1.00f, 0.36f, 0.10f, 1f);    // live amber
        static readonly Color HandleEdge = new Color(0.086f, 0.082f, 0.071f, 1f); // canvas（縁取り）

        public int SelectedIndex => _selected;
        public bool ShowLattice => _showLattice;

        public void SetEditMode(EditMode m) { _mode = m; MarkDirtyRepaint(); }
        public void SetLattice(bool on) { _showLattice = on; MarkDirtyRepaint(); }

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
            _drag = Drag.None;
            MarkDirtyRepaint();
        }

        static Vector2 ToPixel(Vector2 local, float w, float h)
            => new Vector2(local.x * w, (1f - local.y) * h);

        static Vector2 ToLocal(Vector2 px, float w, float h)
            => new Vector2(
                Mathf.Clamp01(w > 0f ? px.x / w : 0f),
                Mathf.Clamp01(h > 0f ? 1f - px.y / h : 0f));

        // クランプ無し（delta 計算用・y 上向き正規化）
        static Vector2 ToNorm(Vector2 px, float w, float h)
            => new Vector2(w > 0f ? px.x / w : 0f, h > 0f ? 1f - px.y / h : 0f);

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

            // 格子オーバーレイ（制御グリッドより淡く・先に描いて制御線を上に）
            if (_showLattice) DrawLattice(p, cols, rows, w, h);

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

        /// <summary>制御点を _snap に写して WarpMath.SampleGridSmooth の入力にする（レンダーと同じ Bezier 面）。</summary>
        void SnapshotPoints(int cols, int rows)
        {
            int need = cols * rows;
            if (_snap == null || _snap.Length != need) _snap = new Vector2[need];
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                    _snap[j * cols + i] = _target.GetWarpPoint(i, j);
        }

        /// <summary>細分化格子（#34/#35）。等間隔の u/v 線を Bezier 面に沿ってサンプルし歪みを可視化する（レンダーと一致）。</summary>
        void DrawLattice(UnityEngine.UIElements.Painter2D p, int cols, int rows, float w, float h)
        {
            p.lineWidth = 1f;
            p.strokeColor = LatticeCol;
            SnapshotPoints(cols, rows);

            for (int l = 0; l <= LatticeLines; l++)
            {
                float t = (float)l / LatticeLines;

                // 縦線（u=t）
                p.BeginPath();
                for (int s = 0; s <= LatticeSteps; s++)
                {
                    var px = ToPixel(WarpMath.SampleGrid(_snap, cols, rows, t, (float)s / LatticeSteps, _target.BezierInterp), w, h);
                    if (s == 0) p.MoveTo(px); else p.LineTo(px);
                }
                p.Stroke();

                // 横線（v=t）
                p.BeginPath();
                for (int s = 0; s <= LatticeSteps; s++)
                {
                    var px = ToPixel(WarpMath.SampleGrid(_snap, cols, rows, (float)s / LatticeSteps, t, _target.BezierInterp), w, h);
                    if (s == 0) p.MoveTo(px); else p.LineTo(px);
                }
                p.Stroke();
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
            Vector2 px = (Vector2)evt.localPosition;
            _lastNorm = ToNorm(px, r.width, r.height);

            if (_mode == EditMode.Content)
            {
                _drag = Drag.Content;                 // ドラッグで枠内映像を pan
            }
            else
            {
                int hit = PickHandle(px, r.width, r.height);
                if (hit >= 0) { _dragIndex = hit; _selected = hit; _drag = Drag.Handle; }
                else          { _drag = Drag.MoveAll; }   // 何もない所＝窓ごと移動
            }

            this.CapturePointer(evt.pointerId);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (_drag == Drag.None || _target == null) return;
            var r = contentRect;
            Vector2 px = (Vector2)evt.localPosition;

            if (_drag == Drag.Handle)
            {
                var local = ToLocal(px, r.width, r.height);
                int cols = _target.WarpCols;
                _target.SetWarpPoint(_dragIndex % cols, _dragIndex / cols, local);
            }
            else if (_drag == Drag.MoveAll)
            {
                Vector2 nrm = ToNorm(px, r.width, r.height);
                TranslateAll(nrm - _lastNorm);
                _lastNorm = nrm;
            }
            else // Content
            {
                Vector2 nrm = ToNorm(px, r.width, r.height);
                ContentPan?.Invoke(nrm - _lastNorm);
                _lastNorm = nrm;
            }
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (_drag == Drag.None) return;
            _drag = Drag.None;
            _dragIndex = -1;
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        /// <summary>全制御点を delta（正規化）だけ平行移動（窓ごと移動）。各点は [0,1] にクランプ。</summary>
        void TranslateAll(Vector2 d)
        {
            int cols = _target.WarpCols, rows = _target.WarpRows;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    Vector2 p = _target.GetWarpPoint(i, j);
                    _target.SetWarpPoint(i, j, new Vector2(Mathf.Clamp01(p.x + d.x), Mathf.Clamp01(p.y + d.y)));
                }
        }
    }
}
