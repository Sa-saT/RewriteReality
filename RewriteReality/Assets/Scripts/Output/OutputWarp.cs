using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 【M10】出力変形（Output Surface）。完成映像（finalRT）“全体”を、プロジェクタ面に合わせて
    /// 四隅キーストーン＋多pin メッシュで変形する出力段。<see cref="OutputManager"/> から
    /// <see cref="Apply"/> を呼び、変形後 RT を FS/Syphon/NDI へ配信させる。
    ///
    /// 埋め込み（<see cref="Compositor"/>）と同じ射影数学（<see cref="WarpMath"/>）と CornerPin シェーダを使う。
    /// 違いは「背景なし・ソース=finalRT 全面・四隅は追従でなくユーザー指定（キーストーン）」。
    /// 既定は <c>_enabled=false</c> ＝ 完全素通し（土台を壊さない）。
    ///
    /// ※ 現状はバックエンド（Inspector 駆動）。編集UIは #22（Surface UI）で本UIへ畳み込む。
    /// 将来 M11 の SurfaceManager で Compositor と統合予定。
    /// </summary>
    public sealed class OutputWarp : MonoBehaviour, IWarpTarget
    {
        [Tooltip("出力変形を有効化する。OFF なら finalRT を素通し（既定）。")]
        [SerializeField] bool _enabled = false;

        [Tooltip("変形マテリアル（CornerPin シェーダ）。未設定なら実行時に自動生成。")]
        [SerializeField] Material _warpMaterial;

        [Header("出力キーストーン四隅（正規化スクリーン 0..1）")]
        [SerializeField] Vector2 _bl = new Vector2(0f, 0f);
        [SerializeField] Vector2 _br = new Vector2(1f, 0f);
        [SerializeField] Vector2 _tr = new Vector2(1f, 1f);
        [SerializeField] Vector2 _tl = new Vector2(0f, 1f);

        [Header("変形グリッド（多pin）")]
        [Tooltip("制御点グリッドの列数（X方向・最小2）")]
        [SerializeField] int _warpCols = 2;
        [Tooltip("制御点グリッドの行数（Y方向・最小2）")]
        [SerializeField] int _warpRows = 2;
        [Tooltip("制御点のローカル位置（[0..1]², row-major: j*cols+i）。既定は等間隔＝ワープ無し。")]
        [SerializeField] Vector2[] _warpPoints;
        [Tooltip("Grid の補間: ON=Bezier（滑らか）/ OFF=Linear（区分線形）。§7b Mesh Warping")]
        [SerializeField] bool _bezier = true;

        // Grid(Bezier) 細分化数（#34・Compositor と同値）。制御 n → (n-1)*Sub+1 頂点。
        // 2×2（cols<3 && rows<3）は Catmull-Rom が線形へ縮退するため細分化しない（sub=1・従来キーストーンと同一）。
        const int GridSubdiv = 8;

        RenderTexture _outRT;
        Mesh _mesh;
        Material _runtimeMat;
        bool _warnedNoShader;

        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _uvq   = new List<Vector3>();
        int[] _tris;
        int _builtCols, _builtRows;   // 直近に構築した“細分後”位相の頂点数（fc, fr）

        static readonly int MainTexID = Shader.PropertyToID("_MainTex");
        static readonly int OpacityID = Shader.PropertyToID("_Opacity");

        // ---- 公開 API（OutputManager / 将来 UI から使う）----
        public bool Active => _enabled;
        public void SetEnabled(bool on) => _enabled = on;
        /// <summary>直近の変形結果 RT（プレビュー用・未適用/無効なら null）。</summary>
        public RenderTexture Output => _outRT;
        public int WarpCols => _warpCols;
        public int WarpRows => _warpRows;
        public Vector2 GetWarpPoint(int i, int j) => _warpPoints[j * _warpCols + i];
        public void SetWarpPoint(int i, int j, Vector2 local) => _warpPoints[j * _warpCols + i] = local;

        /// <summary>キーストーン四隅を設定（0=BL,1=BR,2=TR,3=TL）。正規化スクリーン座標。</summary>
        public void SetCorner(int index, Vector2 p)
        {
            switch (index)
            {
                case 0: _bl = p; break;
                case 1: _br = p; break;
                case 2: _tr = p; break;
                case 3: _tl = p; break;
            }
        }

        public void ResetWarp()
        {
            _bl = new Vector2(0f, 0f); _br = new Vector2(1f, 0f);
            _tr = new Vector2(1f, 1f); _tl = new Vector2(0f, 1f);
            EnsureWarpPoints();
            WarpMath.FillRegularGrid(_warpPoints, _warpCols, _warpRows);
        }

        /// <summary>制御点配列を保証（未生成/解像度不一致なら等間隔で確保）。UI 読み取り前に呼ぶ。</summary>
        public void EnsureWarpPoints()
        {
            _warpCols = Mathf.Max(2, _warpCols);
            _warpRows = Mathf.Max(2, _warpRows);
            int need = _warpCols * _warpRows;
            if (_warpPoints == null || _warpPoints.Length != need)
            {
                _warpPoints = new Vector2[need];
                WarpMath.FillRegularGrid(_warpPoints, _warpCols, _warpRows);
            }
        }

        public void SetGridResolution(int cols, int rows)
        {
            _warpCols = Mathf.Max(2, cols);
            _warpRows = Mathf.Max(2, rows);
            _warpPoints = null; // 次の EnsureGrid で等間隔生成＋メッシュ再構成
        }
        public bool BezierInterp { get => _bezier; set => _bezier = value; }
        public bool Locked => false;   // 出力変形にロック概念は無い（Surface のみ・U4）

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
                    else if (!_warnedNoShader)
                    {
                        Debug.LogWarning("[OutputWarp] CornerPin シェーダが見つかりません（出力変形なし）。");
                        _warnedNoShader = true;
                    }
                }
                return _runtimeMat;
            }
        }

        /// <summary>
        /// finalRT を出力変形して返す。無効/未対応なら src をそのまま返す（素通し）。RT は使い回す。
        /// </summary>
        public RenderTexture Apply(RenderTexture src)
        {
            if (!_enabled || src == null) return src;

            var mat = WarpMat;
            if (mat == null) return src;

            EnsureOutRT(src);
            EnsureGrid();
            UpdateMesh();
            mat.SetTexture(MainTexID, src);
            mat.SetFloat(OpacityID, 1f);   // 出力全体は常に不透明（Compositor と material 共有時の漏れ防止）

            var prev = RenderTexture.active;
            RenderTexture.active = _outRT;
            GL.Clear(true, true, Color.black);   // 変形先の外側は黒
            GL.PushMatrix();
            GL.LoadOrtho();                       // 0..1 正規化空間（左下原点）
            mat.SetPass(0);
            Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
            GL.PopMatrix();
            RenderTexture.active = prev;

            return _outRT;
        }

        void EnsureOutRT(Texture reference)
        {
            int w = reference != null ? reference.width  : 1920;
            int h = reference != null ? reference.height : 1080;
            if (_outRT != null && _outRT.width == w && _outRT.height == h) return;

            if (_outRT != null) _outRT.Release();
            _outRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "outputWarpRT" };
            _outRT.Create();
        }

        /// <summary>細分後（Bezier）位相の頂点数を返す。2×2 は sub=1（線形＝従来キーストーンと同一）。</summary>
        void FineDims(out int fc, out int fr)
        {
            int sub = (_warpCols >= 3 || _warpRows >= 3) ? GridSubdiv : 1;
            fc = WarpMath.FineCount(_warpCols, sub);
            fr = WarpMath.FineCount(_warpRows, sub);
        }

        void EnsureGrid()
        {
            EnsureWarpPoints(); // 制御点が未生成/解像度不一致なら等間隔で確保
            FineDims(out int fc, out int fr);

            if (_mesh != null && _builtCols == fc && _builtRows == fr) return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "outputWarpMesh" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();

            int verts = fc * fr;
            _verts.Clear(); _uvq.Clear();
            for (int k = 0; k < verts; k++) { _verts.Add(Vector3.zero); _uvq.Add(Vector3.zero); }

            int cells = (fc - 1) * (fr - 1);
            _tris = new int[cells * 6];
            int t = 0;
            for (int j = 0; j < fr - 1; j++)
            {
                for (int i = 0; i < fc - 1; i++)
                {
                    int v00 = j * fc + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + fc;
                    int v11 = v01 + 1;
                    _tris[t++] = v00; _tris[t++] = v10; _tris[t++] = v11;
                    _tris[t++] = v00; _tris[t++] = v11; _tris[t++] = v01;
                }
            }

            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvq);
            _mesh.SetTriangles(_tris, 0);
            _mesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(10f, 10f, 10f));
            _builtCols = fc;
            _builtRows = fr;
        }

        /// <summary>
        /// キーストーン四隅から H を解き、制御グリッドを <see cref="WarpMath.SampleGridSmooth"/>（Bezier・#34）で
        /// 細分化評価 → 各頂点を射影してスクリーン位置と射影補間 uvq を更新する。ソース（finalRT）UV は parametric。
        /// </summary>
        void UpdateMesh()
        {
            var hmg = WarpMath.Solve(_bl, _br, _tr, _tl);
            FineDims(out int fc, out int fr);
            float invFx = 1f / (fc - 1), invFy = 1f / (fr - 1);

            for (int j = 0; j < fr; j++)
            {
                for (int i = 0; i < fc; i++)
                {
                    int idx = j * fc + i;
                    float a = i * invFx, b = j * invFy;                              // parametric [0,1]²
                    Vector2 p = WarpMath.SampleGrid(_warpPoints, _warpCols, _warpRows, a, b, _bezier);
                    WarpMath.Project(hmg, p.x, p.y, out float xp, out float yp, out float wp);

                    _verts[idx] = new Vector3(xp, yp, 0f);
                    _uvq[idx]   = new Vector3(a * wp, b * wp, wp);
                }
            }

            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvq);
        }

        void OnDestroy()
        {
            if (_outRT != null) { _outRT.Release(); _outRT = null; }
            if (_mesh != null) Destroy(_mesh);
            if (_runtimeMat != null) Destroy(_runtimeMat);
        }
    }
}
