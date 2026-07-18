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
        [Tooltip("再生速度（1=等速）。オペレータ UI の Speed(JOG) から可変・本番 Live 中は armed（ハンドオフ §4c）")]
        [Range(0.1f, 4f)]
        [SerializeField] float _speed = 1f;

        VideoPlayer _player;
        bool _wantPlaying = true;   // 再生意図（OnEnable の Play と一致・SetPlaying の差分判定用・#27c）

        /// <summary>出力先 RenderTexture（合成の背景フレーム）。</summary>
        public RenderTexture TargetTexture => _targetTexture;

        /// <summary>現在の再生位置（秒）。CornerSource の時刻引数に渡す。</summary>
        public double Time => _player != null ? _player.time : 0d;

        /// <summary>クリップ尺（秒）。Inspector の Duration 表示用。</summary>
        public double Duration => _player != null && _player.clip != null ? _player.clip.length : 0d;

        /// <summary>ループ再生。差分時のみ VideoPlayer へ実代入（毎フレーム呼ばれても無駄がない・#27c）。</summary>
        public bool Loop
        {
            get => _loop;
            set
            {
                if (_loop == value) return;
                _loop = value;
                if (_player != null) _player.isLooping = value;
            }
        }

        /// <summary>再生速度（0.1..4・VideoPlayer.playbackSpeed に反映）。Speed(JOG) のバインド先。
        /// 差分時のみ実代入（Master Speed から毎フレーム呼ばれても無駄がない・#27c）。</summary>
        public float Speed
        {
            get => _speed;
            set
            {
                float v = Mathf.Clamp(value, 0.1f, 4f);
                if (Mathf.Approximately(_speed, v)) return;
                _speed = v;
                if (_player != null) _player.playbackSpeed = _speed;
            }
        }

        void Awake()
        {
            _player = GetComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = _loop;
            _player.audioOutputMode = VideoAudioOutputMode.None; // APAC等の音声で詰まらせない
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.playbackSpeed = _speed;
            if (_clip != null) _player.clip = _clip;
            if (_targetTexture != null) _player.targetTexture = _targetTexture;
        }

        void OnEnable()
        {
            // 再生意図を尊重（既定 true＝従来どおり自動再生・トランスポート一時停止中の再有効化では回さない・#27c）。
            if (_player != null && _wantPlaying) _player.Play();
        }

        void OnDisable()
        {
            // Play モード終了/破棄時は VideoPlayer が先に無効化され得るため、
            // 有効な時のみ Pause する（"Cannot Pause a disabled VideoPlayer" 回避）。
            if (_player != null && _player.isActiveAndEnabled) _player.Pause();
        }

        /// <summary>
        /// 再生するクリップを差し替える（タイムラインのクリップ・バインド用・#27）。
        /// 同一クリップなら無視（VideoPlayer の再 Prepare によるヒッチ/GC を避ける）。
        /// </summary>
        public void SetClip(VideoClip clip)
        {
            if (_player == null) return;
            if (_player.clip == clip) return;
            _clip = clip;
            _player.clip = clip;
            if (isActiveAndEnabled && clip != null) _player.Play();
        }

        /// <summary>
        /// 再生位置を先頭へ戻して即再生する（Short の再発火・#27c）。SetClip は同一クリップ参照なら
        /// 無視するため、同じ Short を連続で発火したときに頭出ししたい場合はこちらを明示的に呼ぶ。
        /// </summary>
        public void Restart()
        {
            if (_player == null) return;
            _player.time = 0d;
            _wantPlaying = true;
            if (isActiveAndEnabled) _player.Play();
        }

        /// <summary>タイムライン・トランスポートの再生/一時停止を映像へ反映する（#27c）。差分時のみ実操作
        /// （毎フレーム呼ばれても無駄がない）。一時停止時はフリーズ（VideoPlayer 無効時の Pause は回避）。</summary>
        public void SetPlaying(bool on)
        {
            if (_player == null || _wantPlaying == on) return;
            _wantPlaying = on;
            if (on) { if (isActiveAndEnabled) _player.Play(); }
            else    { if (_player.isActiveAndEnabled) _player.Pause(); }
        }

        /// <summary>毎フレームの更新フック（現状 VideoPlayer が自走するため処理なし）。</summary>
        public void Tick()
        {
            // TODO: スクラブ/速度変更/頭出しなどの VJ 操作をここで反映する。
        }
    }
}
