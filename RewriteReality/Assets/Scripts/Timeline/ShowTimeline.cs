using System;
using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// AV ショーのタイムライン再生バックエンド（docs/07b §3.5・M12・#27）。
    /// Song=リニア通し（トランスポートで進行）。Short（ホールド発火）は将来ここに畳み込む（§3.5.2）。
    /// データ（Song/Track/Clip）＋トランスポート・クロック（Play/Pause/Loop・playhead 時刻）を保持し、
    /// UI（<see cref="OperatorUI"/>）は読み取りだけ行う（見た目=UXML/USS・挙動=薄い C# の原則）。
    /// 実クリップ（source/scene）へのバインドは段階的（現状は時刻進行＋playhead まで）。
    /// </summary>
    public sealed class ShowTimeline : MonoBehaviour
    {
        public enum TrackKind { Video, Audio }

        /// <summary>1 本のクリップ（開始・尺・参照 source/scene の id）。</summary>
        [Serializable]
        public sealed class Clip
        {
            public string name = "Clip";
            public double start;        // 秒
            public double duration = 8; // 秒
            [Tooltip("参照するライブラリ項目 id（source/scene・将来バインド）")]
            public string sourceId = "";
            public double End => start + duration;
        }

        /// <summary>1 本のトラック（映像/音声・クリップ列）。</summary>
        [Serializable]
        public sealed class Track
        {
            public string name = "VID 1";
            public TrackKind kind = TrackKind.Video;
            public bool enabled = true;
            public List<Clip> clips = new List<Clip>();
        }

        /// <summary>1 曲（尺・トラック列）。</summary>
        [Serializable]
        public sealed class Song
        {
            public string name = "Song 01";
            [Tooltip("曲全体の尺（秒）")]
            public double length = 200.0;   // 3:20
            public List<Track> tracks = new List<Track>();
        }

        [Tooltip("Song バンク（リニア通し）。空なら実行時に既定の 1 曲を生成する。")]
        [SerializeField] List<Song> _songs = new List<Song>();
        [SerializeField] int _activeSong = 0;
        [Tooltip("末尾で先頭へループする")]
        [SerializeField] bool _loop = true;
        [Tooltip("開始時に自動再生する")]
        [SerializeField] bool _playOnStart = false;

        bool _playing;
        double _time;   // playhead 時刻（秒）

        /// <summary>再生状態が変わったとき発火（UI のボタン表示更新用）。</summary>
        public event Action PlayStateChanged;

        public Song ActiveSong =>
            (_songs != null && _activeSong >= 0 && _activeSong < _songs.Count) ? _songs[_activeSong] : null;

        /// <summary>現在曲の尺（秒・最小 1）。</summary>
        public double Length => ActiveSong != null ? Math.Max(1.0, ActiveSong.length) : 1.0;
        public double Time => _time;
        public double Remaining => Math.Max(0.0, Length - _time);
        public bool Playing => _playing;
        public bool Loop { get => _loop; set => _loop = value; }

        /// <summary>playhead の 0..1 正規化位置（UI の左%・ルーラ位置に使う）。</summary>
        public float NormalizedTime => (float)(_time / Length);

        void Awake()
        {
            if (_songs == null || _songs.Count == 0) _songs = new List<Song> { DefaultSong() };
        }

        void Start()
        {
            if (_playOnStart) Play();
        }

        void Update()
        {
            if (!_playing) return;
            _time += UnityEngine.Time.deltaTime;
            if (_time >= Length)
            {
                if (_loop) _time %= Length;   // 先頭へ巻き戻し
                else { _time = Length; SetPlaying(false); }
            }
        }

        // ---- トランスポート API（UI から）----
        public void Play()  => SetPlaying(true);
        public void Pause() => SetPlaying(false);
        public void TogglePlay() => SetPlaying(!_playing);

        /// <summary>先頭へ（既に先頭付近なら据え置き）。UI の ⏮ 相当。</summary>
        public void Rewind() { _time = 0.0; }

        /// <summary>0..1 で頭出し（ドラッグ/クリックシーク用）。</summary>
        public void SeekNormalized(float t) { _time = Mathf.Clamp01(t) * Length; }

        void SetPlaying(bool on)
        {
            if (_playing == on) return;
            _playing = on;
            PlayStateChanged?.Invoke();
        }

        // ---- 既定曲（スタンドアロンで即動くよう）----
        static Song DefaultSong()
        {
            var s = new Song { name = "Song 01", length = 200.0 };
            var v1 = new Track { name = "VID 1", kind = TrackKind.Video };
            v1.clips.Add(new Clip { name = "CLIP A", start = 0,   duration = 52 });
            v1.clips.Add(new Clip { name = "CLIP B", start = 54,  duration = 68 });
            v1.clips.Add(new Clip { name = "CLIP C", start = 124, duration = 40 });
            var a1 = new Track { name = "AUD 1", kind = TrackKind.Audio };
            a1.clips.Add(new Clip { name = "MASTER", start = 0, duration = 164 });
            s.tracks.Add(v1);
            s.tracks.Add(a1);
            return s;
        }

        /// <summary>mm:ss.cc 形式（負値は絶対値・呼び出し側で符号付与）。UI 表示用。</summary>
        public static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)seconds;
            int m = total / 60;
            int sec = total % 60;
            int cc = (int)((seconds - total) * 100.0);
            return $"{m:00}:{sec:00}.{cc:00}";
        }
    }
}
