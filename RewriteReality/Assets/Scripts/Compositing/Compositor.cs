using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 背景（ベース動画）＋カメラを四隅メッシュ（射影補間）で 1 枚の RenderTexture に合成する。
    /// 四隅の出所は <see cref="ICornerSource"/> 経由で、Compositor は出所を知らない。
    /// </summary>
    public sealed class Compositor : MonoBehaviour
    {
        [Tooltip("カメラを四隅へ射影合成するマテリアル（CornerPin シェーダ）。未設定なら実行時に自動生成する。")]
        [SerializeField] Material _warpMaterial;

        RenderTexture _sceneRT;
        Mesh _quad;                 // カメラを貼る四隅クアッド（使い回す）
        Material _runtimeMat;       // _warpMaterial 未設定時のフォールバック

        // GC 回避のため使い回すバッファ（毎フレーム new しない）。
        readonly List<Vector3> _verts = new List<Vector3>(4) { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
        readonly List<Vector3> _uvq   = new List<Vector3>(4) { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
        static readonly int[] _tris = { 0, 1, 2, 0, 2, 3 };
        static readonly int MainTexID = Shader.PropertyToID("_MainTex");

        /// <summary>合成結果（EffectChain への入力）。</summary>
        public RenderTexture SceneTexture => _sceneRT;

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

            // 2) カメラを四隅へ射影ワープして上書き合成
            var mat = WarpMat;
            if (mat != null && camTex != null)
            {
                EnsureQuad();
                UpdateQuad(corners);
                mat.SetTexture(MainTexID, camTex);

                var prev = RenderTexture.active;
                RenderTexture.active = _sceneRT;
                GL.PushMatrix();
                GL.LoadOrtho();          // 0..1 正規化空間（左下原点）→ sceneRT 全面
                mat.SetPass(0);
                Graphics.DrawMeshNow(_quad, Matrix4x4.identity);
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
            _sceneRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "sceneRT",
            };
            _sceneRT.Create();
        }

        void EnsureQuad()
        {
            if (_quad != null) return;
            _quad = new Mesh { name = "cornerPinQuad" };
            _quad.MarkDynamic();
            _quad.SetVertices(_verts);
            _quad.SetUVs(0, _uvq);
            _quad.SetTriangles(_tris, 0);
            // DrawMeshNow が境界で切られないよう、bounds を広めに固定。
            _quad.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(10f, 10f, 10f));
        }

        /// <summary>
        /// Corners（正規化 0..1, 左下原点）から頂点位置と射影補間用 uvq を更新する。
        /// 四隅クアッドの対角線交点までの距離比から各頂点の同次座標 q を求める。
        /// </summary>
        void UpdateQuad(in Corners c)
        {
            Vector2 bl = c.BottomLeft, br = c.BottomRight, tr = c.TopRight, tl = c.TopLeft;

            float qBL = 1f, qBR = 1f, qTR = 1f, qTL = 1f;

            // 対角線 BL–TR と BR–TL の交点（中心）を求める。
            Vector2 r = tr - bl;
            Vector2 s = tl - br;
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) > 1e-6f)
            {
                Vector2 d = br - bl;
                float t = (d.x * s.y - d.y * s.x) / denom;
                Vector2 center = bl + t * r;

                float d0 = Vector2.Distance(center, bl);
                float d2 = Vector2.Distance(center, tr);
                float d1 = Vector2.Distance(center, br);
                float d3 = Vector2.Distance(center, tl);
                if (d0 > 1e-6f && d1 > 1e-6f && d2 > 1e-6f && d3 > 1e-6f)
                {
                    qBL = (d0 + d2) / d2;
                    qTR = (d0 + d2) / d0;
                    qBR = (d1 + d3) / d3;
                    qTL = (d1 + d3) / d1;
                }
            }

            // 頂点位置（z=0）。GL.LoadOrtho 下で 0..1 が画面全面に対応。
            _verts[0] = new Vector3(bl.x, bl.y, 0f);
            _verts[1] = new Vector3(br.x, br.y, 0f);
            _verts[2] = new Vector3(tr.x, tr.y, 0f);
            _verts[3] = new Vector3(tl.x, tl.y, 0f);

            // UV(0..1) に q を乗じて渡す（フラグメントで /q して射影補間）。
            _uvq[0] = new Vector3(0f,        0f,        qBL); // BL (0,0)
            _uvq[1] = new Vector3(1f * qBR,  0f,        qBR); // BR (1,0)
            _uvq[2] = new Vector3(1f * qTR,  1f * qTR,  qTR); // TR (1,1)
            _uvq[3] = new Vector3(0f,        1f * qTL,  qTL); // TL (0,1)

            _quad.SetVertices(_verts);
            _quad.SetUVs(0, _uvq);
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
            if (_sceneRT != null)
            {
                _sceneRT.Release();
                _sceneRT = null;
            }
            if (_quad != null) Destroy(_quad);
            if (_runtimeMat != null) Destroy(_runtimeMat);
        }
    }
}
