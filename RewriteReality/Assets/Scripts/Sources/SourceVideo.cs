using UnityEngine;
using UnityEngine.Video;

namespace RewriteReality
{
    /// <summary>
    /// ベース動画の再生・ループ・スクラブを担い、RenderTexture へ出力する。
    /// 通常 mp4 は VideoPlayer 標準、高解像度時は KlakHap（.mov/HAP, M8）へ差し替え予定。
    /// iPhone 撮影 MOV 対策として音声は既定で無効（Audio Output Mode = None）。
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class SourceVideo : MonoBehaviour
    {
        [SerializeField] VideoClip _clip;
        [SerializeField] RenderTexture _targetTexture;
        [SerializeField] bool _loop = true;

        VideoPlayer _player;

        /// <summary>出力先 RenderTexture（合成の背景フレーム）。</summary>
        public RenderTexture TargetTexture => _targetTexture;

        /// <summary>現在の再生位置（秒）。CornerSource の時刻引数に渡す。</summary>
        public double Time => _player != null ? _player.time : 0d;

        void Awake()
        {
            _player = GetComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = _loop;
            _player.audioOutputMode = VideoAudioOutputMode.None; // APAC等の音声で詰まらせない
            _player.renderMode = VideoRenderMode.RenderTexture;
            if (_clip != null) _player.clip = _clip;
            if (_targetTexture != null) _player.targetTexture = _targetTexture;
        }

        void OnEnable()
        {
            if (_player != null) _player.Play();
        }

        void OnDisable()
        {
            if (_player != null) _player.Pause();
        }

        /// <summary>毎フレームの更新フック（現状 VideoPlayer が自走するため処理なし）。</summary>
        public void Tick()
        {
            // TODO: スクラブ/速度変更/頭出しなどの VJ 操作をここで反映する。
        }
    }
}
