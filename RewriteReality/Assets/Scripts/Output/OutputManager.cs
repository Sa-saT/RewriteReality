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

            // 重要: KlakSyphon の SyphonServer は CaptureMethod / SourceTexture の setter が
            // 呼ばれるたびに TeardownPlugin()（Metal IOSurface テクスチャを破棄→再生成）する。
            // そのため毎フレーム代入すると外部 Metal テクスチャを毎フレーム作り直し、
            // レンダースレッドと競合して segv（Unity/OBS ごとクラッシュ）する。
            // → 値が実際に変わった時だけ代入すること。Capture Method=Texture は Inspector の
            //   serialized 値で固定し（OnValidate も Texture を維持）、ここでは保険的に補正する。

            // 1) フルスクリーン表示（参照が変わった時だけ）
            if (_fullscreenTarget != null && finalRT != _current)
                _fullscreenTarget.texture = finalRT;

            // 2) Syphon サーバ（setter は teardown を伴うので差分時のみ）
            if (_syphonServer != null)
            {
                if (_syphonServer.CaptureMethod != Klak.Syphon.CaptureMethod.Texture)
                    _syphonServer.CaptureMethod = Klak.Syphon.CaptureMethod.Texture;
                if (_syphonServer.SourceTexture != finalRT)
                    _syphonServer.SourceTexture = finalRT;
            }

            // 3) NDI センダー（同様に差分時のみ）
            if (_ndiSender != null)
            {
                if (_ndiSender.captureMethod != Klak.Ndi.CaptureMethod.Texture)
                    _ndiSender.captureMethod = Klak.Ndi.CaptureMethod.Texture;
                if (_ndiSender.sourceTexture != finalRT)
                    _ndiSender.sourceTexture = finalRT;
            }

            _current = finalRT;

            // TODO(docs/06): コーナーピン補正（最終 RT に 4 頂点ワープでプロジェクタ台形補正）。
        }
    }
}
