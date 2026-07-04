using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 背景（ベース動画）＋カメラを「4点トラッキング＋多pin手動ワープ」の2段でメッシュ合成する。
    /// 段1: 追従 <see cref="Corners"/>(4点) からホモグラフィ H(単位正方形→四隅) を解く。
    /// 段2: 埋め込みを N×M の制御点グリッドで手動ワープ。Grid モードは <see cref="WarpMath.SampleGridSmooth"/>
    /// （Catmull-Rom＝Bezier 相当）で細分化して滑らかな面にし、各頂点のローカル座標を H で射影する（#34）。
    /// 2×2・手動ワープ無しなら従来の 4pin と同結果（後方互換）。四隅の出所は ICornerSource 経由で知らない。
    /// </summary>
    public sealed class Compositor : MonoBehaviour, IWarpTarget
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
        Material _runtimeMat;

        // Grid(Bezier) モードの 1 セルあたり細分化数（#34）。制御 n → (n-1)*Sub+1 頂点。
        // 2×2（cols<3 && rows<3）は Catmull-Rom が線形へ縮退するため細分化しない（sub=1・従来 4pin と同一）。
        const int GridSubdiv = 8;

        // メッシュ位相プール（#34・改善②）: (vertsCols,vertsRows) ごとに Mesh＋頂点バッファをキャッシュして使い回す。
        // 複数 surface が Mask(制御解像度) と Grid(細分解像度) を混在させても位相ごとに引くので、
        // 毎フレームのメッシュ再構築（スラッシング）が起きない。位相は解像度が新規のときだけ構築する。
        sealed class MeshEntry { public Mesh mesh; public Vector3[] verts; public Vector3[] uvq; }
        readonly Dictionary<int, MeshEntry> _meshPool = new Dictionary<int, MeshEntry>();

        static readonly int MainTexID = Shader.PropertyToID("_MainTex");
        static readonly int OpacityID = Shader.PropertyToID("_Opacity");

        /// <summary>合成結果（EffectChain への入力）。</summary>
        public RenderTexture SceneTexture => _sceneRT;

        // ---- 多pin手動ワープ用の公開 API（UI/#18 から使う）----
        public int WarpCols => _warpCols;
        public int WarpRows => _warpRows;
        public Vector2 GetWarpPoint(int i, int j) => _warpPoints[j * _warpCols + i];
        public void SetWarpPoint(int i, int j, Vector2 local) => _warpPoints[j * _warpCols + i] = local;
        public void ResetWarp() { EnsureWarpPoints(); WarpMath.FillRegularGrid(_warpPoints, _warpCols, _warpRows); }
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
            _warpPoints = null; // 次の EnsureWarpPoints で等間隔生成（メッシュは GetMesh が位相ごとにプール）
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
        /// 背景とカメラを合成して内部の sceneRT を返す（単一 surface・従来経路）。
        /// RT/メッシュは使い回す（毎フレーム生成しない）。
        /// </summary>
        public RenderTexture Composite(Texture baseTex, Texture camTex, in Corners corners)
        {
            EnsureSceneRT(baseTex);
            BlitBase(baseTex);

            var mat = WarpMat;
            if (mat != null && camTex != null)
            {
                EnsureWarpPoints(); // 組込み単一 surface の制御点を保証
                DrawContent(camTex, corners, _warpPoints, _warpCols, _warpRows, mat, false, Vector2.zero, 1f, 1f);
            }
            return _sceneRT;
        }

        /// <summary>
        /// 複数 Input Surface を合成して sceneRT を返す（多surface対応・M11・<see cref="SurfaceManager"/> 駆動）。
        /// 各 surface の content（現状 Camera=<paramref name="cameraTex"/>）を四隅＋メッシュワープしてベース上へ重ねる。
        /// <paramref name="effectChain"/> があれば、その surface に割り当てた範囲エフェクトを content へ先に掛ける
        /// （content が RenderTexture のときのみ・docs/07b §3.6）。従来の単一 Composite は温存。
        /// </summary>
        public RenderTexture Composite(Texture baseTex, SurfaceManager surfaces, Texture cameraTex,
                                       double time, EffectChain effectChain, in AudioFeatures audio)
        {
            EnsureSceneRT(baseTex);
            BlitBase(baseTex);

            var mat = WarpMat;
            if (mat != null && surfaces != null)
            {
                var list = surfaces.Surfaces;
                for (int s = 0; s < list.Count; s++)
                {
                    var surf = list[s];
                    if (surf == null || !surf.Enabled) continue;

                    Texture content = null;
                    if (surf.Content == Surface.ContentKind.Camera)       content = cameraTex;
                    else if (surf.Content == Surface.ContentKind.Pattern) content = TestPattern.Texture; // 校正（#34）
                    if (content == null) continue; // Video/None content は段階的（将来）

                    // 範囲=Surface のエフェクトを content に先掛け（RenderTexture のときのみ）
                    if (effectChain != null && content is RenderTexture crt && effectChain.HasSurfaceEffects(surf.Id))
                        content = effectChain.ProcessSurface(crt, surf.Id, audio);

                    var corners = surf.UpdateCorners(time);
                    bool mask = surf.Fit == Surface.FitMode.Mask;
                    DrawContent(content, corners, surf.WarpPoints, surf.WarpCols, surf.WarpRows, mat, mask,
                                surf.ContentOffset, surf.ContentZoom, surf.Opacity);
                }
            }
            return _sceneRT;
        }

        /// <summary>背景を sceneRT へ（無ければクリア）。</summary>
        void BlitBase(Texture baseTex)
        {
            if (baseTex != null) Graphics.Blit(baseTex, _sceneRT);
            else                 ClearSceneRT();
        }

        /// <summary>
        /// content を四隅＋多pin メッシュで sceneRT へ上書き描画する（surface 共通）。
        /// <paramref name="mask"/>=true なら Mask/Crop（内容を等倍のまま窓抜き・歪ませない・制御解像度メッシュ）、
        /// false なら Grid（Bezier 面へ射影で流し込む・<see cref="GridSubdiv"/> で細分化した滑らかメッシュ・#34）。
        /// </summary>
        void DrawContent(Texture content, in Corners corners, Vector2[] points, int cols, int rows, Material mat,
                         bool mask, Vector2 contentOffset, float contentZoom, float opacity)
        {
            MeshEntry e;
            if (mask)
            {
                e = GetMesh(cols, rows);
                WriteMeshMask(e, corners, points, cols, rows, contentOffset, contentZoom);
            }
            else
            {
                int sub = (cols >= 3 || rows >= 3) ? GridSubdiv : 1;   // 2×2 は線形＝細分化不要
                int fc = WarpMath.FineCount(cols, sub), fr = WarpMath.FineCount(rows, sub);
                e = GetMesh(fc, fr);
                WriteMeshGrid(e, corners, points, cols, rows, fc, fr);
            }
            mat.SetTexture(MainTexID, content);
            mat.SetFloat(OpacityID, opacity);

            var prev = RenderTexture.active;
            RenderTexture.active = _sceneRT;
            GL.PushMatrix();
            GL.LoadOrtho();          // 0..1 正規化空間（左下原点）→ sceneRT 全面
            mat.SetPass(0);
            Graphics.DrawMeshNow(e.mesh, Matrix4x4.identity);
            GL.PopMatrix();
            RenderTexture.active = prev;
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

        /// <summary>
        /// (cols,rows) 位相の <see cref="MeshEntry"/> をプールから取得（無ければ 1 度だけ構築）。
        /// 三角形インデックス（セルごと2枚）と頂点/uvq バッファを確保する。頂点値は毎フレーム WriteMesh* で上書き。
        /// </summary>
        MeshEntry GetMesh(int cols, int rows)
        {
            cols = Mathf.Max(2, cols);
            rows = Mathf.Max(2, rows);
            int key = (cols << 16) | rows;
            if (_meshPool.TryGetValue(key, out var e)) return e;

            var mesh = new Mesh { name = $"warpMesh_{cols}x{rows}" };
            mesh.MarkDynamic();

            int verts = cols * rows;
            e = new MeshEntry { mesh = mesh, verts = new Vector3[verts], uvq = new Vector3[verts] };

            int cells = (cols - 1) * (rows - 1);
            var tris = new int[cells * 6];
            int t = 0;
            for (int j = 0; j < rows - 1; j++)
            {
                for (int i = 0; i < cols - 1; i++)
                {
                    int v00 = j * cols + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + cols;
                    int v11 = v01 + 1;
                    tris[t++] = v00; tris[t++] = v10; tris[t++] = v11;
                    tris[t++] = v00; tris[t++] = v11; tris[t++] = v01;
                }
            }

            mesh.SetVertices(e.verts);   // verts 個ぶんの頂点を確保（初期 0・以後 WriteMesh* で更新）
            mesh.SetUVs(0, e.uvq);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(10f, 10f, 10f));
            _meshPool[key] = e;
            return e;
        }

        /// <summary>
        /// Mask（窓抜き）: 各制御点を H 射影して窓（クアッド）を作り、content UV = スクリーン位置に content 変形
        /// （zoom 中心 0.5・pan=offset）を適用（q=1）。content は全面に等倍で貼られ窓が切り抜くだけ＝内容は歪まない。
        /// 制御解像度メッシュ（細分化しない・窓形状は制御点で決まる）。
        /// </summary>
        void WriteMeshMask(MeshEntry e, in Corners c, Vector2[] points, int cols, int rows,
                           Vector2 contentOffset, float contentZoom)
        {
            var hmg = WarpMath.Solve(c.BottomLeft, c.BottomRight, c.TopRight, c.TopLeft);
            float invZoom = 1f / Mathf.Max(0.0001f, contentZoom);

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    int idx = j * cols + i;
                    Vector2 p = points[idx];
                    WarpMath.Project(hmg, p.x, p.y, out float xp, out float yp, out float wp);
                    e.verts[idx] = new Vector3(xp, yp, 0f);
                    float u = (xp - 0.5f) * invZoom + 0.5f - contentOffset.x;
                    float v = (yp - 0.5f) * invZoom + 0.5f - contentOffset.y;
                    e.uvq[idx] = new Vector3(u, v, 1f);
                }
            }

            e.mesh.SetVertices(e.verts);
            e.mesh.SetUVs(0, e.uvq);
        }

        /// <summary>
        /// Grid（Bezier 流し込み・#34）: parametric (a,b)∈[0,1]² を fc×fr に細分化。各頂点で制御グリッドを
        /// <see cref="WarpMath.SampleGridSmooth"/>（Catmull-Rom）評価して滑らかなローカル位置を得 → H で射影。
        /// content UV = parametric × 同次分母 w'（frag で /q で perspective-correct）。制御点を動かすと折れ線でなく
        /// 滑らかな膨らみになる（MadMapper GridGenerator 手本）。2×2 は線形へ縮退（fc=cols・従来と同一）。
        /// </summary>
        void WriteMeshGrid(MeshEntry e, in Corners c, Vector2[] points, int cols, int rows, int fc, int fr)
        {
            var hmg = WarpMath.Solve(c.BottomLeft, c.BottomRight, c.TopRight, c.TopLeft);
            float invFx = 1f / (fc - 1), invFy = 1f / (fr - 1);

            for (int j = 0; j < fr; j++)
            {
                for (int i = 0; i < fc; i++)
                {
                    int idx = j * fc + i;
                    float a = i * invFx, b = j * invFy;                       // parametric [0,1]²
                    Vector2 p = WarpMath.SampleGridSmooth(points, cols, rows, a, b);   // Bezier 面上のローカル位置
                    WarpMath.Project(hmg, p.x, p.y, out float xp, out float yp, out float wp);
                    e.verts[idx] = new Vector3(xp, yp, 0f);
                    e.uvq[idx]   = new Vector3(a * wp, b * wp, wp);
                }
            }

            e.mesh.SetVertices(e.verts);
            e.mesh.SetUVs(0, e.uvq);
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
            foreach (var e in _meshPool.Values) if (e.mesh != null) Destroy(e.mesh);
            _meshPool.Clear();
            if (_runtimeMat != null) Destroy(_runtimeMat);
        }
    }
}
