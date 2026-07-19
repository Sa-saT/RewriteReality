using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// タイムラインの Audio トラックを内部再生するシンク（M13・#28a）。
    /// SourceVideo と対称の opt-in 設計＝ShowTimeline の _audioSink に割り当てたときだけ機能する。
    /// AudioListener 経由の最終ミックスへ自動的に乗るため、AudioAnalyzer の解析対象にもなる（docs/05）。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class SourceAudio : MonoBehaviour
    {
        [SerializeField] AudioClip _clip;
        [SerializeField] bool _loop = true;
        [Tooltip("再生速度（1=等速）。Master Speed から供給・AudioSource.pitch に反映するため音高も変わる（v1 仕様）。")]
        [Range(0.1f, 4f)]
        [SerializeField] float _speed = 1f;
        [Range(0f, 1f)]
        [SerializeField] float _volume = 1f;
        [SerializeField] bool _mute = false;
        [Tooltip("Volume 変更時の実際の AudioSource.volume 追従速度（/秒）。プチノイズ対策の内蔵フェード。")]
        [SerializeField] float _fadeSpeed = 12f;

        AudioSource _source;
        bool _wantPlaying = true;   // 再生意図（OnEnable の Play と一致・SetPlaying の差分判定用）
        float _targetVolume = 1f;
        bool _audioResyncPending;   // Seek/Rewind 直後の time 再同期要求（ShowTimeline から一度だけ立てる）

        /// <summary>現在の再生位置（秒）。</summary>
        public double Time => _source != null ? (double)_source.time : 0d;

        /// <summary>クリップ尺（秒）。</summary>
        public double Duration => _source != null && _source.clip != null ? _source.clip.length : 0d;

        /// <summary>ループ再生。差分時のみ AudioSource へ実代入。</summary>
        public bool Loop
        {
            get => _loop;
            set
            {
                if (_loop == value) return;
                _loop = value;
                if (_source != null) _source.loop = value;
            }
        }

        /// <summary>再生速度（0.1..4・AudioSource.pitch に反映）。Master Speed のバインド先。
        /// 差分時のみ実代入（毎フレーム呼ばれても無駄がない）。</summary>
        public float Speed
        {
            get => _speed;
            set
            {
                float v = Mathf.Clamp(value, 0.1f, 4f);
                if (Mathf.Approximately(_speed, v)) return;
                _speed = v;
                if (_source != null) _source.pitch = _speed;
            }
        }

        /// <summary>目標音量（0..1）。実際の AudioSource.volume は Update() でここへ線形追従する
        /// （プチノイズ対策の内蔵フェード）。</summary>
        public float Volume
        {
            get => _targetVolume;
            set => _targetVolume = Mathf.Clamp01(value);
        }

        /// <summary>ミュート。差分時のみ実代入。</summary>
        public bool Mute
        {
            get => _mute;
            set
            {
                if (_mute == value) return;
                _mute = value;
                if (_source != null) _source.mute = value;
            }
        }

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.loop = _loop;
            _source.pitch = _speed;
            _source.mute = _mute;
            _targetVolume = _volume;
            _source.volume = _volume;
            if (_clip != null) _source.clip = _clip;
        }

        void OnEnable()
        {
            // 再生意図を尊重（既定 true＝従来どおり自動再生・トランスポート一時停止中の再有効化では回さない）。
            if (_source != null && _wantPlaying) _source.Play();
        }

        void OnDisable()
        {
            if (_source != null && _source.isActiveAndEnabled) _source.Pause();
        }

        void Update()
        {
            if (_source == null) return;
            if (!Mathf.Approximately(_source.volume, _targetVolume))
                _source.volume = Mathf.MoveTowards(_source.volume, _targetVolume, _fadeSpeed * UnityEngine.Time.deltaTime);

            if (_audioResyncPending)
            {
                _audioResyncPending = false;
                // 呼び出し側（ShowTimeline）が RequestResync 直後に SetClip/SetTime で位置を合わせる想定。
                // ここでは消費のみ（フラグの二重処理防止）。
            }
        }

        /// <summary>
        /// 再生するクリップを差し替える（タイムラインのクリップ・バインド用）。
        /// 同一クリップなら無視。localTime で頭出し位置を指定（playhead 同期・#28a）。
        /// </summary>
        public void SetClip(AudioClip clip, double localTime = 0d)
        {
            if (_source == null) return;
            if (_source.clip == clip)
            {
                SetTime(localTime);
                return;
            }
            _clip = clip;
            _source.clip = clip;
            if (clip != null) SetTime(localTime);
            if (isActiveAndEnabled && clip != null && _wantPlaying) _source.Play();
        }

        /// <summary>再生位置を秒で指定（0..clip.length にクランプ）。Seek/Rewind 後の再同期用。</summary>
        public void SetTime(double seconds)
        {
            if (_source == null || _source.clip == null) return;
            _source.time = (float)Mathf.Clamp((float)seconds, 0f, _source.clip.length);
        }

        /// <summary>再生位置を先頭へ戻して即再生する（同一クリップの再発火頭出し用）。</summary>
        public void Restart()
        {
            if (_source == null) return;
            _source.time = 0f;
            _wantPlaying = true;
            if (isActiveAndEnabled) _source.Play();
        }

        /// <summary>タイムライン・トランスポートの再生/一時停止を音声へ反映する。差分時のみ実操作。</summary>
        public void SetPlaying(bool on)
        {
            if (_source == null || _wantPlaying == on) return;
            _wantPlaying = on;
            if (on) { if (isActiveAndEnabled) _source.Play(); }
            else    { if (_source.isActiveAndEnabled) _source.Pause(); }
        }

        /// <summary>Seek/Rewind 直後に呼ぶ（次の SetClip/SetTime で位置合わせすることを示すフラグ）。</summary>
        public void RequestResync() => _audioResyncPending = true;
    }
}
