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
        Mesh _mesh;
        Material _runtimeMat;

        // GC 回避のため使い回すバッファ（グリッド再構成時のみ作り直す）。
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _uvq   = new List<Vector3>();
        int[] _tris;
        int _builtCols, _builtRows;

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
                EnsureGrid(); // 組込み単一 surface の制御点を保証
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

                    Texture content = surf.Content == Surface.ContentKind.Camera ? cameraTex : null;
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
        /// <paramref name="mask"/>=true なら Mask/Crop（内容を等倍のまま窓抜き・歪ませない）、
        /// false なら Project（内容をクアッドへ射影で流し込む）。
        /// </summary>
        void DrawContent(Texture content, in Corners corners, Vector2[] points, int cols, int rows, Material mat,
                         bool mask, Vector2 contentOffset, float contentZoom, float opacity)
        {
            EnsureMeshTopology(cols, rows);
            WriteMesh(corners, points, cols, rows, mask, contentOffset, contentZoom);
            mat.SetTexture(MainTexID, content);
            mat.SetFloat(OpacityID, opacity);

            var prev = RenderTexture.active;
            RenderTexture.active = _sceneRT;
            GL.PushMatrix();
            GL.LoadOrtho();          // 0..1 正規化空間（左下原点）→ sceneRT 全面
            mat.SetPass(0);
            Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
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

        /// <summary>組込み単一 surface の制御点＋メッシュ位相を保証する（従来経路）。</summary>
        void EnsureGrid()
        {
            EnsureWarpPoints(); // 制御点が未生成/解像度不一致なら等間隔で確保
            EnsureMeshTopology(_warpCols, _warpRows);
        }

        /// <summary>
        /// cols×rows のメッシュ位相（頂点数＋三角形インデックス）を構築する（解像度が変わった時だけ）。
        /// surface ごとに解像度が異なる場合はこの切替時にだけ作り直す（同一なら再利用・GC 回避）。
        /// </summary>
        void EnsureMeshTopology(int cols, int rows)
        {
            cols = Mathf.Max(2, cols);
            rows = Mathf.Max(2, rows);
            if (_mesh != null && _builtCols == cols && _builtRows == rows) return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "warpMesh" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();

            int verts = cols * rows;
            _verts.Clear(); _uvq.Clear();
            for (int k = 0; k < verts; k++) { _verts.Add(Vector3.zero); _uvq.Add(Vector3.zero); }

            // 三角形インデックス（セルごとに2枚）
            int cells = (cols - 1) * (rows - 1);
            _tris = new int[cells * 6];
            int t = 0;
            for (int j = 0; j < rows - 1; j++)
            {
                for (int i = 0; i < cols - 1; i++)
                {
                    int v00 = j * cols + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + cols;
                    int v11 = v01 + 1;
                    _tris[t++] = v00; _tris[t++] = v10; _tris[t++] = v11;
                    _tris[t++] = v00; _tris[t++] = v11; _tris[t++] = v01;
                }
            }

            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvq);
            _mesh.SetTriangles(_tris, 0);
            _mesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(10f, 10f, 10f));
            _builtCols = cols;
            _builtRows = rows;
        }

        /// <summary>
        /// Corners から H を解き、各制御点を「手動ワープ → H 射影」して頂点位置と uvq を書き込む。
        /// Project（<paramref name="mask"/>=false）: content UV は規則グリッド → クアッドへ射影で流し込む
        /// （q は H の同次分母 w'・frag で /q で perspective-correct）。
        /// Mask（<paramref name="mask"/>=true）: content UV = 頂点のスクリーン位置(q=1) → content を
        /// sceneRT 全面に等倍で貼り、窓（クアッド）が切り抜くだけ＝内容は歪まない。位相は事前に EnsureMeshTopology。
        /// </summary>
        void WriteMesh(in Corners c, Vector2[] points, int cols, int rows, bool mask,
                       Vector2 contentOffset, float contentZoom)
        {
            // 単位正方形→四隅 の射影変換（Heckbert）。数学は WarpMath に共通化（OutputWarp と共有）。
            var hmg = WarpMath.Solve(c.BottomLeft, c.BottomRight, c.TopRight, c.TopLeft);

            float invCx = 1f / (cols - 1), invCy = 1f / (rows - 1);
            float invZoom = 1f / Mathf.Max(0.0001f, contentZoom);

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    int idx = j * cols + i;

                    Vector2 p = points[idx];        // 手動ワープ後のローカル座標
                    WarpMath.Project(hmg, p.x, p.y, out float xp, out float yp, out float wp);

                    _verts[idx] = new Vector3(xp, yp, 0f);            // スクリーン位置（0..1）
                    if (mask)
                    {
                        // 窓抜き: content UV = スクリーン位置に content 変形（zoom 中心=0.5・pan=offset）を適用。
                        // 内容は歪まず、枠内で見せる箇所だけ動く（q=1）。
                        float u = (xp - 0.5f) * invZoom + 0.5f - contentOffset.x;
                        float v = (yp - 0.5f) * invZoom + 0.5f - contentOffset.y;
                        _uvq[idx] = new Vector3(u, v, 1f);
                    }
                    else
                    {
                        // 射影流し込み: content UV = 規則グリッド × 同次分母（frag で /q）
                        float gu = i * invCx, gv = j * invCy;
                        _uvq[idx] = new Vector3(gu * wp, gv * wp, wp);
                    }
                }
            }

            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvq);
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
