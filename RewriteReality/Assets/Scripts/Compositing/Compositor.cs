using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 背景（ベース動画）＋カメラを四隅メッシュ（射影補間）で 1 枚の RenderTexture に合成する。
    /// 四隅の出所は <see cref="ICornerSource"/> 経由で、Compositor は出所を知らない。
    /// </summary>
    public sealed class Compositor : MonoBehaviour
    {
        [Tooltip("カメラを四隅へ射影合成するマテリアル（コーナーピン用シェーダ。未設定なら背景のみ）")]
        [SerializeField] Material _warpMaterial;

        RenderTexture _sceneRT;
        Mesh _quad; // カメラを貼る四隅クアッド（使い回す）

        /// <summary>合成結果（EffectChain への入力）。</summary>
        public RenderTexture SceneTexture => _sceneRT;

        /// <summary>
        /// 背景とカメラを合成して内部の sceneRT を返す。RT は使い回す（毎フレーム生成しない）。
        /// </summary>
        public RenderTexture Composite(Texture baseTex, Texture camTex, in Corners corners)
        {
            EnsureSceneRT(baseTex);

            // 1) 背景を sceneRT へ
            if (baseTex != null) Graphics.Blit(baseTex, _sceneRT);
            else                 ClearSceneRT();

            // 2) カメラを四隅へワープ合成
            // TODO: コーナーピン用シェーダ + corners から作る射影クアッドに camTex を描画。
            //       _warpMaterial と _quad を使い、RenderTexture.active を切り替えて DrawMeshNow。
            //       現状はスケルトンのため背景のみ（corners/camTex 未使用）。

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
        }
    }
}
