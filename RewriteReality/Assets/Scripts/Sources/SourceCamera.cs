using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// ライブカメラ入力を取得し Texture として供給する。初期は内蔵/USB カメラ1台。
    /// 将来は Syphon-in などの差し替えも想定（IF 化は必要になった時点で）。
    /// </summary>
    public sealed class SourceCamera : MonoBehaviour
    {
        [SerializeField] int _deviceIndex = 0;
        [SerializeField] int _requestedWidth = 1920;
        [SerializeField] int _requestedHeight = 1080;
        [SerializeField] int _requestedFps = 60;

        WebCamTexture _webCam;

        /// <summary>現在のカメラ映像（未起動なら null）。</summary>
        public Texture Texture => _webCam;

        /// <summary>映像が有効に流れているか。</summary>
        public bool IsPlaying => _webCam != null && _webCam.isPlaying;

        void OnEnable()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogWarning("[SourceCamera] カメラデバイスが見つかりません。");
                return;
            }

            int index = Mathf.Clamp(_deviceIndex, 0, devices.Length - 1);
            _webCam = new WebCamTexture(devices[index].name,
                                        _requestedWidth, _requestedHeight, _requestedFps);
            _webCam.Play();
        }

        void OnDisable()
        {
            if (_webCam == null) return;
            _webCam.Stop();
            Destroy(_webCam);
            _webCam = null;
        }

        /// <summary>毎フレームの更新フック（WebCamTexture は自走するため現状処理なし）。</summary>
        public void Tick()
        {
            // TODO: デバイス切替・解像度再要求などをここで反映する。
        }
    }
}
