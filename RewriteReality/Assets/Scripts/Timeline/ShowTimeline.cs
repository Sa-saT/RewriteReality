using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

namespace RewriteReality
{
    /// <summary>
    /// AV ショーのタイムライン再生バックエンド（docs/07b §3.5・M12・#27）。
    /// Song=リニア通し（トランスポートで進行・可変速）。Short=キー割当のホールド発火（§3.5.2・
    /// 押下中だけ最上位レイヤー・離すと song に戻る＝Resolume「Piano」／複数同時押しは後押しが上）。
    /// データ（Song/Track/Clip・Short バンク/パッド）＋トランスポート・クロック（Play/Pause/Loop/Rate・
    /// playhead 時刻）を保持し、UI（<see cref="OperatorUI"/>）は読み取りと発火通知だけ行う
    /// （見た目=UXML/USS・挙動=薄い C# の原則）。
    /// 実クリップのバインドは opt-in（<see cref="_videoSink"/> ＋ライブラリ登録時のみ）で、
    /// 未設定なら純粋なトランスポート（従来動作）＝非破壊。
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
            [Tooltip("参照するライブラリ項目 id（source/scene・#27 バインド）")]
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

        /// <summary>
        /// 1 Short（タブ 1 枚）。§7 精査(07-06)：1 Short = 1 クリップ＋per-Short のパッド/キー割当＋Hold-Loop。
        /// ホールド発火はバンド（Short）単位＝そのパッドを押下中だけ最上位レイヤーで clip を発火する。
        /// </summary>
        [Serializable]
        public sealed class Short
        {
            public string name = "Short A";
            [Tooltip("発火キー（Input System の Key 名・例: Digit1 / A / Numpad1）")]
            public string key = "";
            [Tooltip("4×4 パッド割当（0..15・-1=未割当）")]
            public int pad = -1;
            [Tooltip("本番でキー押下中ループするか（per-Short）")]
            public bool holdLoop = true;
            public Clip clip = new Clip();
        }

        /// <summary>4×4 パッドマトリクスのパッド総数（§7）。</summary>
        public const int PadCount = 16;

        /// <summary>ライブラリ項目 id → 実アセットの対応（#27 バインド・当面はここに直接登録）。
        /// 将来の汎用セレクションモデル（#3）が入れば、これはそのビューに置き換わる。</summary>
        [Serializable]
        public sealed class ClipAsset
        {
            public string id = "";
            public VideoClip video;
        }

        [Header("Song（リニア通し）")]
        [Tooltip("Song バンク。空なら実行時に既定の 1 曲を生成する。")]
        [SerializeField] List<Song> _songs = new List<Song>();
        [SerializeField] int _activeSong = 0;
        [Tooltip("末尾で先頭へループする")]
        [SerializeField] bool _loop = true;
        [Tooltip("開始時に自動再生する")]
        [SerializeField] bool _playOnStart = false;
        [Tooltip("再生速度（1=等速・0 で一時停止相当）")]
        [Range(0f, 4f)]
        [SerializeField] float _rate = 1f;

        [Header("Short（ホールド発火・§3.5.2 / §7）")]
        [Tooltip("Short 一覧（各 1 クリップ・タブ 1 枚）。空なら既定 1 枚を生成。")]
        [SerializeField] List<Short> _shorts = new List<Short>();
        [Tooltip("いま編集/表示中の Short（タブ選択）")]
        [SerializeField] int _activeShort = 0;

        [Header("バインド（opt-in・未設定なら純トランスポート）")]
        [Tooltip("アクティブな映像クリップを流し込む先。null なら映像を差し替えない。")]
        [SerializeField] SourceVideo _videoSink;
        [Tooltip("クリップの sourceId → 実アセットの対応表。")]
        [SerializeField] List<ClipAsset> _library = new List<ClipAsset>();

        bool _playing;
        double _time;   // playhead 時刻（秒）

        // Short ホールド状態：発火中の Short index を押下順に保持（末尾＝最上位）。
        readonly List<int> _held = new List<int>();
        // key 文字列 → Key の解決キャッシュ（毎フレームの Enum.Parse を避ける）。
        readonly Dictionary<string, Key> _keyCache = new Dictionary<string, Key>();

        // バインドの適用済み参照（差分時のみ差し替え＝ヒッチ/GC 回避）。
        Clip _appliedClip;

        /// <summary>再生状態が変わったとき発火（UI のボタン表示更新用）。</summary>
        public event Action PlayStateChanged;
        /// <summary>Short のホールド状態（何か押下中か）が変わったとき発火。UI のプレビュー用。</summary>
        public event Action ShortStateChanged;
        /// <summary>実効の映像クリップ（song or 最上位 short）が変わったとき発火。</summary>
        public event Action<Clip> ActiveVideoClipChanged;

        public Song ActiveSong =>
            (_songs != null && _activeSong >= 0 && _activeSong < _songs.Count) ? _songs[_activeSong] : null;

        /// <summary>いま編集/表示中の Short（タブ選択）。</summary>
        public Short ActiveShort =>
            (_shorts != null && _activeShort >= 0 && _activeShort < _shorts.Count) ? _shorts[_activeShort] : null;

        /// <summary>いま編集/表示中の Short の index（範囲外は -1）。</summary>
        public int ActiveShortIndex =>
            (_shorts != null && _activeShort >= 0 && _activeShort < _shorts.Count) ? _activeShort : -1;

        /// <summary>Short の総数（タブ数）。</summary>
        public int ShortCount => _shorts != null ? _shorts.Count : 0;

        /// <summary>index の Short（範囲外は null）。</summary>
        public Short GetShort(int index) =>
            (_shorts != null && index >= 0 && index < _shorts.Count) ? _shorts[index] : null;

        /// <summary>現在曲の尺（秒・最小 1）。</summary>
        public double Length => ActiveSong != null ? Math.Max(1.0, ActiveSong.length) : 1.0;
        public double Time => _time;
        public double Remaining => Math.Max(0.0, Length - _time);
        public bool Playing => _playing;
        public bool Loop { get => _loop; set => _loop = value; }

        /// <summary>再生速度（0..4・song 進行と将来の映像速度に効かせる・#27）。</summary>
        public float Rate
        {
            get => _rate;
            set => _rate = Mathf.Clamp(value, 0f, 4f);
        }

        /// <summary>playhead の 0..1 正規化位置（UI の左%・ルーラ位置に使う）。</summary>
        public float NormalizedTime => (float)(_time / Length);

        /// <summary>何か Short を発火中か（最上位 short が song に優先している）。</summary>
        public bool AnyShortHeld => _held.Count > 0;

        /// <summary>最上位（最後に押した）Short。無ければ null。</summary>
        public Short TopShort
        {
            get
            {
                if (_shorts == null || _held.Count == 0) return null;
                int idx = _held[_held.Count - 1];
                return (idx >= 0 && idx < _shorts.Count) ? _shorts[idx] : null;
            }
        }

        /// <summary>いま画面に出すべき映像クリップ（最上位 short → 無ければ song の映像クリップ）。</summary>
        public Clip ActiveVideoClip
        {
            get
            {
                var top = TopShort;
                if (top != null) return top.clip;
                return SongClipAt(_time, TrackKind.Video);
            }
        }

        /// <summary>いま鳴らすべき音声クリップ（song の音声トラック・将来の内部再生 M13 用に公開）。</summary>
        public Clip ActiveAudioClip => SongClipAt(_time, TrackKind.Audio);

        void Awake()
        {
            if (_songs == null || _songs.Count == 0) _songs = new List<Song> { DefaultSong() };
            if (_shorts == null || _shorts.Count == 0) _shorts = new List<Short> { DefaultShort() };
        }

        void Start()
        {
            if (_playOnStart) Play();
            ApplyBinding(force: true);
        }

        void Update()
        {
            if (_playing)
            {
                _time += UnityEngine.Time.deltaTime * Mathf.Max(0f, _rate);
                if (_time >= Length)
                {
                    if (_loop) _time %= Length;   // 先頭へ巻き戻し
                    else { _time = Length; SetPlaying(false); }
                }
            }

            PollShortInput();
            ApplyBinding(force: false);
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

        // ---- Short ホールド発火（§3.5.2 / §7）----
        // 押下順の Short index リストで「後押しが上」を表現。UI/キー入力/パッドの三経路から叩ける。
        // 発火はバンド（Short）単位＝1 Short = 1 クリップを押下中だけ最上位で流す。

        /// <summary>shortIndex の Short を発火（既に発火中なら最上位へ繰り上げ）。</summary>
        public void HoldStart(int shortIndex)
        {
            if (_shorts == null || shortIndex < 0 || shortIndex >= _shorts.Count) return;
            _held.Remove(shortIndex);     // 重複除去（最上位へ繰り上げ）
            _held.Add(shortIndex);
            ShortStateChanged?.Invoke();
        }

        /// <summary>shortIndex の発火を解除（離す）。</summary>
        public void HoldEnd(int shortIndex)
        {
            if (_held.Remove(shortIndex)) ShortStateChanged?.Invoke();
        }

        /// <summary>全 Short を離す。</summary>
        public void HoldReleaseAll()
        {
            if (_held.Count == 0) return;
            _held.Clear();
            ShortStateChanged?.Invoke();
        }

        /// <summary>表示/編集する Short タブを切り替える（発火状態は保持）。</summary>
        public void SelectShort(int index)
        {
            if (_shorts == null || index < 0 || index >= _shorts.Count) return;
            _activeShort = index;
        }

        /// <summary>pad(0..15) に割り当てられた Short index を返す（無ければ -1）。</summary>
        public int ShortForPad(int pad)
        {
            if (_shorts == null) return -1;
            for (int i = 0; i < _shorts.Count; i++)
                if (_shorts[i] != null && _shorts[i].pad == pad) return i;
            return -1;
        }

        /// <summary>
        /// パッド割当＝キーボードキー割当（07-10・MIDI 不在時のフォールバック）。
        /// 4×4 の各スロットに固定キーを割り当てる（Timeline.jsx の PAD_KEYS と一致）。
        /// </summary>
        static readonly string[] _padGlyphs =
            { "Q", "W", "E", "R", "A", "S", "D", "F", "Z", "X", "C", "V", "1", "2", "3", "4" };

        /// <summary>pad(0..15) の表示グリフ（キー文字）。範囲外は空。</summary>
        public static string PadGlyph(int pad) =>
            (pad >= 0 && pad < _padGlyphs.Length) ? _padGlyphs[pad] : "";

        /// <summary>pad(0..15) の Input System Key 名（数字は Digit* へ）。範囲外は空。</summary>
        public static string PadKeyName(int pad)
        {
            var g = PadGlyph(pad);
            if (string.IsNullOrEmpty(g)) return "";
            return char.IsDigit(g[0]) ? "Digit" + g : g;   // "1"→"Digit1" / "Q"→"Q"
        }

        /// <summary>shortIndex に pad を割り当てる（他 Short が同 pad を持てば奪取＝未割当化）。
        /// pad→key も同時に設定し、キーボード（MIDI 不在時）で発火できるようにする（07-10）。</summary>
        public void AssignPad(int shortIndex, int pad)
        {
            if (_shorts == null || shortIndex < 0 || shortIndex >= _shorts.Count) return;
            if (pad >= 0)
                for (int i = 0; i < _shorts.Count; i++)
                    if (i != shortIndex && _shorts[i] != null && _shorts[i].pad == pad)
                    { _shorts[i].pad = -1; _shorts[i].key = ""; }   // 奪取された側は key も外す
            var sh = _shorts[shortIndex];
            sh.pad = pad;
            sh.key = PadKeyName(pad);
        }

        void PollShortInput()
        {
            var kb = Keyboard.current;
            if (_shorts == null || kb == null) return;

            for (int i = 0; i < _shorts.Count; i++)
            {
                var sh = _shorts[i];
                if (sh == null || !TryResolveKey(sh.key, out var key)) continue;
                var ctrl = kb[key];
                if (ctrl == null) continue;
                if (ctrl.wasPressedThisFrame)  HoldStart(i);
                if (ctrl.wasReleasedThisFrame) HoldEnd(i);
            }
        }

        bool TryResolveKey(string s, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrEmpty(s)) return false;
            if (_keyCache.TryGetValue(s, out key)) return key != Key.None;
            Enum.TryParse(s, true, out key);  // 失敗時は Key.None のままキャッシュ（以後スキップ）
            _keyCache[s] = key;
            return key != Key.None;
        }

        // ---- クリップ・バインド（#27・opt-in）----
        // 実効の映像クリップが変わったら、その sourceId をライブラリ経由で VideoClip に解決し sink へ流す。
        void ApplyBinding(bool force)
        {
            var clip = ActiveVideoClip;
            if (!force && ReferenceEquals(clip, _appliedClip)) return;
            _appliedClip = clip;

            ActiveVideoClipChanged?.Invoke(clip);

            if (_videoSink == null) return;               // opt-out：純トランスポート
            var vc = ResolveVideo(clip);
            if (vc != null) _videoSink.SetClip(vc);       // 未解決時は現状維持（差し替えない）
        }

        VideoClip ResolveVideo(Clip clip)
        {
            if (clip == null || string.IsNullOrEmpty(clip.sourceId) || _library == null) return null;
            for (int i = 0; i < _library.Count; i++)
            {
                var a = _library[i];
                if (a != null && a.id == clip.sourceId) return a.video;
            }
            return null;
        }

        Clip SongClipAt(double t, TrackKind kind)
        {
            var song = ActiveSong;
            if (song == null) return null;
            Clip found = null;   // 同時刻に複数あれば上のトラック（後勝ち）を採用
            for (int ti = 0; ti < song.tracks.Count; ti++)
            {
                var track = song.tracks[ti];
                if (track == null || !track.enabled || track.kind != kind) continue;
                for (int ci = 0; ci < track.clips.Count; ci++)
                {
                    var c = track.clips[ci];
                    if (c != null && t >= c.start && t < c.End) { found = c; break; }
                }
            }
            return found;
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

        // 既定 Short：UI のホールドボタンが即動くよう 1 枚用意（key は未割当＝既定でキーボードを
        // 占有しない。pad=0 に割当・バインドも sourceId 空で無効）。
        static Short DefaultShort()
        {
            return new Short { name = "Short A", pad = 0, key = PadKeyName(0), holdLoop = true,
                               clip = new Clip { name = "SHORT 1", duration = 8 } };
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
