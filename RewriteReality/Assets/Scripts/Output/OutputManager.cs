using UnityEngine;
using UnityEngine.UI;
using Klak.Syphon;
using Klak.Ndi;

namespace RewriteReality
{
    /// <summary>
    /// 最終 RT を Fullscreen / Syphon / NDI へ同時配信する（通す順: FS → Syphon → NDI・docs/06）。
    /// 各 Klak コンポーネントは Capture Method = Texture で SourceTexture に finalRT を流す。
    /// </summary>
    public sealed class OutputManager : MonoBehaviour
    {
        [Header("Fullscreen / Projector")]
        [Tooltip("最終映像を表示する全画面 RawImage（プロジェクタ/フルスクリーン表示用）")]
        [SerializeField] RawImage _fullscreenTarget;

        [Header("Syphon (mac)")]
        [SerializeField] SyphonServer _syphonServer;

        [Header("NDI")]
        [SerializeField] NdiSender _ndiSender;

        RenderTexture _current;

        /// <summary>毎フレーム、最終 RT を各出力へ反映する。</summary>
        public void Publish(RenderTexture finalRT)
        {
            if (finalRT == null) return;

            // 同じ参照なら再設定を省く（GC・無駄な代入を避ける）
            bool changed = finalRT != _current;
            _current = finalRT;

            // 1) フルスクリーン表示
            if (_fullscreenTarget != null && changed)
                _fullscreenTarget.texture = finalRT;

            // 2) Syphon サーバ
            if (_syphonServer != null && changed)
            {
                _syphonServer.CaptureMethod = Klak.Syphon.CaptureMethod.Texture;
                _syphonServer.SourceTexture = finalRT;
            }

            // 3) NDI センダー
            if (_ndiSender != null && changed)
            {
                _ndiSender.captureMethod = Klak.Ndi.CaptureMethod.Texture;
                _ndiSender.sourceTexture = finalRT;
            }

            // TODO(docs/06): コーナーピン補正（最終 RT に 4 頂点ワープでプロジェクタ台形補正）。
        }
    }
}
