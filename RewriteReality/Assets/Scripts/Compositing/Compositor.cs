using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 背景（ベース動画）＋カメラを「4点トラッキング＋多pin手動ワープ」の2段でメッシュ合成する。
    /// 段1: 追従 <see cref="Corners"/>(4点) からホモグラフィ H(単位正方形→四隅) を解く。
    /// 段2: 埋め込みを N×M の制御点グリッドで細分化し、各頂点のローカル座標を手動ワープ → H で射影。
    /// 2×2・手動ワープ無しなら従来の 4pin と同結果（後方互換）。四隅の出所は ICornerSource 経由で知らない。
    /// </summary>
    public sealed class Compositor : MonoBehaviour
    {
        [Tooltip("カメラを射影合成するマテリアル（CornerPin シェーダ）。未設定なら実行時に自動生成する。")]
        [SerializeField] Material _warpMaterial;

        [Header("Warp grid（多pin手動ワープ）")]
        [Tooltip("制御点グリッドの列数（X方向・最小2）")]
        [SerializeField] int _warpCols = 2;
        [Tooltip("制御点グリッドの行数（Y方向・最小2）")]
        [SerializeField] int _warpRows = 2;
        [Tooltip("制御点のローカル位置（[0..1]², row-major: j*cols+i）。既定は等間隔＝ワープ無し。")]
        [SerializeField] Vector2[] _warpPoints;

        RenderTexture _sceneRT;
        Mesh _mesh;
        Material _runtimeMat;

        // GC 回避のため使い回すバッファ（グリッド再構成時のみ作り直す）。
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _uvq   = new List<Vector3>();
        int[] _tris;
        int _builtCols, _builtRows;

        static readonly int MainTexID = Shader.PropertyToID("_MainTex");

        /// <summary>合成結果（EffectChain への入力）。</summary>
        public RenderTexture SceneTexture => _sceneRT;

        // ---- 多pin手動ワープ用の公開 API（UI/#18 から使う）----
        public int WarpCols => _warpCols;
        public int WarpRows => _warpRows;
        public Vector2 GetWarpPoint(int i, int j) => _warpPoints[j * _warpCols + i];
        public void SetWarpPoint(int i, int j, Vector2 local) => _warpPoints[j * _warpCols + i] = local;
        public void ResetWarp() { if (_warpPoints != null) FillRegular(_warpPoints, _warpCols, _warpRows); }
        public void SetGridResolution(int cols, int rows)
        {
            _warpCols = Mathf.Max(2, cols);
            _warpRows = Mathf.Max(2, rows);
            _warpPoints = null; // 次の EnsureGrid で等間隔生成＋メッシュ再構成
        }

        /// <summary>四隅ワープ用マテリアル。Inspector 未設定なら CornerPin シェーダから自動生成。</summary>
        Material WarpMat
        {
            get
            {
                if (_warpMaterial != null) return _warpMaterial;
                if (_runtimeMat == null)
                {
                    var sh = Shader.Find("Hidden/RewriteReality/CornerPin");
                    if (sh != null)
                        _runtimeMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                    else
                        Debug.LogWarning("[Compositor] CornerPin シェーダが見つかりません（背景のみ合成）。");
                }
                return _runtimeMat;
            }
        }

        /// <summary>
        /// 背景とカメラを合成して内部の sceneRT を返す。RT/メッシュは使い回す（毎フレーム生成しない）。
        /// </summary>
        public RenderTexture Composite(Texture baseTex, Texture camTex, in Corners corners)
        {
            EnsureSceneRT(baseTex);

            // 1) 背景を sceneRT へ
            if (baseTex != null) Graphics.Blit(baseTex, _sceneRT);
            else                 ClearSceneRT();

            // 2) カメラをメッシュワープして上書き合成
            var mat = WarpMat;
            if (mat != null && camTex != null)
            {
                EnsureGrid();
                UpdateMesh(corners);
                mat.SetTexture(MainTexID, camTex);

                var prev = RenderTexture.active;
                RenderTexture.active = _sceneRT;
                GL.PushMatrix();
                GL.LoadOrtho();          // 0..1 正規化空間（左下原点）→ sceneRT 全面
                mat.SetPass(0);
                Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
                GL.PopMatrix();
                RenderTexture.active = prev;
            }

            return _sceneRT;
        }

        void EnsureSceneRT(Texture reference)
        {
            int w = reference != null ? reference.width  : 1920;
            int h = reference != null ? reference.height : 1080;
            if (_sceneRT != null && _sceneRT.width == w && _sceneRT.height == h) return;

            if (_sceneRT != null) _sceneRT.Release();
            _sceneRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "sceneRT" };
            _sceneRT.Create();
        }

        /// <summary>制御点グリッドとメッシュのインデックスを（解像度が変わった時だけ）構築する。</summary>
        void EnsureGrid()
        {
            _warpCols = Mathf.Max(2, _warpCols);
            _warpRows = Mathf.Max(2, _warpRows);

            // 制御点が未生成/解像度不一致なら等間隔で作り直す
            int need = _warpCols * _warpRows;
            if (_warpPoints == null || _warpPoints.Length != need)
            {
                _warpPoints = new Vector2[need];
                FillRegular(_warpPoints, _warpCols, _warpRows);
            }

            if (_mesh != null && _builtCols == _warpCols && _builtRows == _warpRows) return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "warpMesh" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();

            int verts = _warpCols * _warpRows;
            _verts.Clear(); _uvq.Clear();
            for (int k = 0; k < verts; k++) { _verts.Add(Vector3.zero); _uvq.Add(Vector3.zero); }

            // 三角形インデックス（セルごとに2枚）
            int cells = (_warpCols - 1) * (_warpRows - 1);
            _tris = new int[cells * 6];
            int t = 0;
            for (int j = 0; j < _warpRows - 1; j++)
            {
                for (int i = 0; i < _warpCols - 1; i++)
                {
                    int v00 = j * _warpCols + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + _warpCols;
                    int v11 = v01 + 1;
                    _tris[t++] = v00; _tris[t++] = v10; _tris[t++] = v11;
                    _tris[t++] = v00; _tris[t++] = v11; _tris[t++] = v01;
                }
            }

            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvq);
            _mesh.SetTriangles(_tris, 0);
            _mesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(10f, 10f, 10f));
            _builtCols = _warpCols;
            _builtRows = _warpRows;
        }

        /// <summary>
        /// Corners から H を解き、各制御点を「手動ワープ → H 射影」して頂点位置と射影補間 uvq を更新する。
        /// camera UV は規則グリッド（手動ワープに依らない）。q は H の同次分母 w'。
        /// </summary>
        void UpdateMesh(in Corners c)
        {
            // 単位正方形→四隅 のホモグラフィ係数（Heckbert）。P0=BL(0,0) P1=BR(1,0) P2=TR(1,1) P3=TL(0,1)
            float x0 = c.BottomLeft.x,  y0 = c.BottomLeft.y;
            float x1 = c.BottomRight.x, y1 = c.BottomRight.y;
            float x2 = c.TopRight.x,    y2 = c.TopRight.y;
            float x3 = c.TopLeft.x,     y3 = c.TopLeft.y;

            float sx = x0 - x1 + x2 - x3;
            float sy = y0 - y1 + y2 - y3;

            float a, b, cc, d, e, f, g, h;
            if (Mathf.Abs(sx) < 1e-6f && Mathf.Abs(sy) < 1e-6f)
            {
                // アフィン（平行四辺形）
                a = x1 - x0; b = x3 - x0; cc = x0;
                d = y1 - y0; e = y3 - y0; f = y0;
                g = 0f; h = 0f;
            }
            else
            {
                float dx1 = x1 - x2, dx2 = x3 - x2;
                float dy1 = y1 - y2, dy2 = y3 - y2;
                float den = dx1 * dy2 - dx2 * dy1;
                if (Mathf.Abs(den) < 1e-9f) den = 1e-9f;
                g = (sx * dy2 - sy * dx2) / den;
                h = (dx1 * sy - dy1 * sx) / den;
                a = x1 - x0 + g * x1; b = x3 - x0 + h * x3; cc = x0;
                d = y1 - y0 + g * y1; e = y3 - y0 + h * y3; f = y0;
            }

            int cols = _warpCols, rows = _warpRows;
            float invCx = 1f / (cols - 1), invCy = 1f / (rows - 1);

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    int idx = j * cols + i;
                    float gu = i * invCx;          // camera UV（規則グリッド）
                    float gv = j * invCy;

                    Vector2 p = _warpPoints[idx];   // 手動ワープ後のローカル座標
                    float lx = p.x, ly = p.y;

                    float xp = a * lx + b * ly + cc;
                    float yp = d * lx + e * ly + f;
                    float wp = g * lx + h * ly + 1f;
                    if (wp < 1e-5f) wp = 1e-5f;

                    _verts[idx] = new Vector3(xp / wp, yp / wp, 0f);   // スクリーン位置（0..1）
                    _uvq[idx]   = new Vector3(gu * wp, gv * wp, wp);   // 射影補間（frag で /q）
                }
            }

            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvq);
        }

        static void FillRegular(Vector2[] pts, int cols, int rows)
        {
            float invCx = 1f / (cols - 1), invCy = 1f / (rows - 1);
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                    pts[j * cols + i] = new Vector2(i * invCx, j * invCy);
        }

        void ClearSceneRT()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _sceneRT;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }

        void OnDestroy()
        {
            if (_sceneRT != null) { _sceneRT.Release(); _sceneRT = null; }
            if (_mesh != null) Destroy(_mesh);
            if (_runtimeMat != null) Destroy(_runtimeMat);
        }
    }
}
