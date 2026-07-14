using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

namespace RewriteReality
{
    /// <summary>
    /// AV ショーのタイムライン再生バックエンド（docs/07b §3.5・M12・#27／MPC 流バンク再編＝§7c・#29 U11）。
    /// Sequence=リニア通し（トランスポートで進行・可変速・旧称 Song）。Short=キー割当のホールド発火（§3.5.2・
    /// 押下中だけ最上位レイヤー・離すと sequence に戻る＝Resolume「Piano」／複数同時押しは後押しが上）。
    /// Song=Sequence の並び（セットリスト・×N repeat・MPC 流）。
    /// データ（Sequence/Track/Clip・Short バンク/パッド・Song/SongStep）＋トランスポート・クロック
    /// （Play/Pause/Loop/Rate・playhead 時刻）を保持し、UI（<see cref="OperatorUI"/>）は読み取りと発火通知だけ行う
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
            [Range(0f, 1f)]
            [Tooltip("重ね合成の不透明度（U3・現状は UI 表示のみ・パイプライン未結線）")]
            public float opacity = 1f;
            [Tooltip("音声トラックのミュート（U3・現状は UI 表示のみ）")]
            public bool muted = false;
            public List<Clip> clips = new List<Clip>();
        }

        /// <summary>1 Sequence（マルチトラックバンク・尺・トラック列・旧称 Song／§7c で改名）。</summary>
        [Serializable]
        public sealed class Sequence
        {
            public string name = "Seq 01";
            [Tooltip("Sequence 全体の尺（秒）")]
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

        /// <summary>Song の 1 ステップ＝参照する Sequence（名前ベース・タブ名で解決）＋繰り返し回数。</summary>
        [Serializable]
        public sealed class SongStep
        {
            [Tooltip("参照する Sequence の名前（Sequence タブ名と一致・#29 U11）")]
            public string sequenceName = "";
            [Tooltip("繰り返し回数（×N・最小 1）")]
            public int repeat = 1;
        }

        /// <summary>1 Song（タブ 1 枚）＝Sequence を並べたセットリスト（MPC 流・§7c・#29 U11）。
        /// 本体は持たず SongStep 列で Sequence を参照・反復するだけ（非破壊・Sequence 側は変更しない）。</summary>
        [Serializable]
        public sealed class Song
        {
            public string name = "Song 01";
            public List<SongStep> steps = new List<SongStep>();
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

        [Header("Sequence（マルチトラックバンク・リニア通し・旧称 Song）")]
        [Tooltip("Sequence バンク。空なら実行時に既定の 1 本を生成する。")]
        [SerializeField] List<Sequence> _sequences = new List<Sequence>();
        [SerializeField] int _activeSequence = 0;
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

        [Header("Song（Sequence セットリスト・§7c・#29 U11）")]
        [Tooltip("Song 一覧（各 1 セットリスト・タブ 1 枚）。空でもよい（未使用なら生成しない）。")]
        [SerializeField] List<Song> _songs = new List<Song>();
        [Tooltip("いま編集/表示中の Song（タブ選択）")]
        [SerializeField] int _activeSongIndex = 0;

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
        /// <summary>実効の映像クリップ（sequence or 最上位 short）が変わったとき発火。</summary>
        public event Action<Clip> ActiveVideoClipChanged;

        public Sequence ActiveSequence =>
            (_sequences != null && _activeSequence >= 0 && _activeSequence < _sequences.Count) ? _sequences[_activeSequence] : null;

        /// <summary>いま編集/表示中の Sequence の index（範囲外は -1）。</summary>
        public int ActiveSequenceIndex =>
            (_sequences != null && _activeSequence >= 0 && _activeSequence < _sequences.Count) ? _activeSequence : -1;

        /// <summary>Sequence の総数（タブ数）。</summary>
        public int SequenceCount => _sequences != null ? _sequences.Count : 0;

        /// <summary>index の Sequence（範囲外は null）。</summary>
        public Sequence GetSequence(int index) =>
            (_sequences != null && index >= 0 && index < _sequences.Count) ? _sequences[index] : null;

        /// <summary>index の Sequence を選択（頭出しはしない）。</summary>
        public void SelectSequence(int index)
        {
            if (_sequences == null || index < 0 || index >= _sequences.Count) return;
            _activeSequence = index;
        }

        // ---- タブ操作（動的タブバー・07-10 App.jsx／§7c で Sequence/Short/Song の3種へ）----
        public enum TabKind { Sequence, Short, Song }

        /// <summary>Sequence / Short / Song の合計タブ数。</summary>
        public int TabCount => SequenceCount + ShortCount + SongCount;

        /// <summary>新しい Sequence を追加して index を返す。</summary>
        public int AddSequence()
        {
            if (_sequences == null) _sequences = new List<Sequence>();
            int n = _sequences.Count + 1;
            _sequences.Add(new Sequence { name = "Seq " + n.ToString("00"), length = 200.0 });
            return _sequences.Count - 1;
        }

        /// <summary>新しい Short を追加して index を返す（空きパッド/キーを自動割当）。</summary>
        public int AddShort()
        {
            if (_shorts == null) _shorts = new List<Short>();
            int pad = 0;
            while (pad < PadCount && ShortForPad(pad) >= 0) pad++;
            if (pad >= PadCount) pad = -1;
            char letter = (char)('A' + _shorts.Count);
            _shorts.Add(new Short
            {
                name = "Short " + letter,
                pad = pad,
                key = PadKeyName(pad),
                holdLoop = true,
                clip = new Clip { name = "SHORT " + (_shorts.Count + 1), duration = 8 }
            });
            return _shorts.Count - 1;
        }

        /// <summary>新しい Song（セットリスト）を追加して index を返す。</summary>
        public int AddSong()
        {
            if (_songs == null) _songs = new List<Song>();
            int n = _songs.Count + 1;
            _songs.Add(new Song { name = "Song " + n.ToString("00") });
            return _songs.Count - 1;
        }

        /// <summary>アクティブ Sequence に新規トラックを追加（+ Track・U3）。VID n/AUD n を自動採番し、
        /// クリップ 1 本（ファイル名ラベル・全尺）を仮配置する。Sequence が無ければ null。</summary>
        public Track AddTrack(TrackKind kind, string fileLabel)
        {
            var seq = ActiveSequence;
            if (seq == null) return null;

            int n = 1;
            for (int i = 0; i < seq.tracks.Count; i++)
                if (seq.tracks[i] != null && seq.tracks[i].kind == kind) n++;

            var track = new Track { name = (kind == TrackKind.Video ? "VID " : "AUD ") + n, kind = kind };
            track.clips.Add(new Clip { name = fileLabel, start = 0, duration = seq.length });
            seq.tracks.Add(track);
            return track;
        }

        /// <summary>タブを 1 枚閉じる（合計 1 枚のときは不可）。閉じたら true。</summary>
        public bool RemoveTab(TabKind kind, int index)
        {
            if (TabCount <= 1) return false;
            switch (kind)
            {
                case TabKind.Sequence:
                    if (_sequences == null || index < 0 || index >= _sequences.Count) return false;
                    _sequences.RemoveAt(index);
                    if (_activeSequence >= _sequences.Count) _activeSequence = Math.Max(0, _sequences.Count - 1);
                    return true;
                case TabKind.Song:
                    if (_songs == null || index < 0 || index >= _songs.Count) return false;
                    _songs.RemoveAt(index);
                    if (_activeSongIndex >= _songs.Count) _activeSongIndex = Math.Max(0, _songs.Count - 1);
                    return true;
                default:
                    if (_shorts == null || index < 0 || index >= _shorts.Count) return false;
                    HoldReleaseAll();
                    _shorts.RemoveAt(index);
                    if (_activeShort >= _shorts.Count) _activeShort = Math.Max(0, _shorts.Count - 1);
                    return true;
            }
        }

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

        /// <summary>いま編集/表示中の Song（タブ選択）。</summary>
        public Song ActiveSong =>
            (_songs != null && _activeSongIndex >= 0 && _activeSongIndex < _songs.Count) ? _songs[_activeSongIndex] : null;

        /// <summary>いま編集/表示中の Song の index（範囲外は -1）。</summary>
        public int ActiveSongIndex =>
            (_songs != null && _activeSongIndex >= 0 && _activeSongIndex < _songs.Count) ? _activeSongIndex : -1;

        /// <summary>Song の総数（タブ数）。</summary>
        public int SongCount => _songs != null ? _songs.Count : 0;

        /// <summary>index の Song（範囲外は null）。</summary>
        public Song GetSong(int index) =>
            (_songs != null && index >= 0 && index < _songs.Count) ? _songs[index] : null;

        /// <summary>index の Song を選択。</summary>
        public void SelectSong(int index)
        {
            if (_songs == null || index < 0 || index >= _songs.Count) return;
            _activeSongIndex = index;
        }

        /// <summary>アクティブ Song の末尾に、指定 Sequence を参照するステップを追加（既定 ×1）。</summary>
        public void AddSongStep(string sequenceName)
        {
            var song = ActiveSong;
            if (song == null || string.IsNullOrEmpty(sequenceName)) return;
            song.steps.Add(new SongStep { sequenceName = sequenceName, repeat = 1 });
        }

        /// <summary>アクティブ Song のステップを削除。</summary>
        public void RemoveSongStep(int index)
        {
            var song = ActiveSong;
            if (song == null || index < 0 || index >= song.steps.Count) return;
            song.steps.RemoveAt(index);
        }

        /// <summary>アクティブ Song のステップを並べ替え（dir=-1 で前へ・+1 で後ろへ）。</summary>
        public void MoveSongStep(int index, int dir)
        {
            var song = ActiveSong;
            if (song == null) return;
            int j = index + dir;
            if (index < 0 || index >= song.steps.Count || j < 0 || j >= song.steps.Count) return;
            (song.steps[index], song.steps[j]) = (song.steps[j], song.steps[index]);
        }

        /// <summary>アクティブ Song のステップの繰り返し回数を設定（最小 1）。</summary>
        public void SetSongStepRepeat(int index, int repeat)
        {
            var song = ActiveSong;
            if (song == null || index < 0 || index >= song.steps.Count) return;
            song.steps[index].repeat = Math.Max(1, repeat);
        }

        /// <summary>現在 Sequence の尺（秒・最小 1）。</summary>
        public double Length => ActiveSequence != null ? Math.Max(1.0, ActiveSequence.length) : 1.0;
        public double Time => _time;
        public double Remaining => Math.Max(0.0, Length - _time);
        public bool Playing => _playing;
        public bool Loop { get => _loop; set => _loop = value; }

        /// <summary>再生速度（0..4・sequence 進行と将来の映像速度に効かせる・#27）。</summary>
        public float Rate
        {
            get => _rate;
            set => _rate = Mathf.Clamp(value, 0f, 4f);
        }

        /// <summary>playhead の 0..1 正規化位置（UI の左%・ルーラ位置に使う）。</summary>
        public float NormalizedTime => (float)(_time / Length);

        /// <summary>何か Short を発火中か（最上位 short が sequence に優先している）。</summary>
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

        /// <summary>いま画面に出すべき映像クリップ（最上位 short → 無ければ sequence の映像クリップ）。</summary>
        public Clip ActiveVideoClip
        {
            get
            {
                var top = TopShort;
                if (top != null) return top.clip;
                return SequenceClipAt(_time, TrackKind.Video);
            }
        }

        /// <summary>いま鳴らすべき音声クリップ（sequence の音声トラック・将来の内部再生 M13 用に公開）。</summary>
        public Clip ActiveAudioClip => SequenceClipAt(_time, TrackKind.Audio);

        void Awake()
        {
            EnsureSeeded();
        }

        /// <summary>既定の Sequence/Short を用意（空なら 1 枚ずつ。Song は未使用なら生成しない）。
        /// Awake タイミングに依存させないため UI 側からも呼べる
        /// （GameObject 非アクティブ等で Awake 未実行でもタブが出るように）。</summary>
        public void EnsureSeeded()
        {
            if (_sequences == null || _sequences.Count == 0) _sequences = new List<Sequence> { DefaultSequence() };
            if (_shorts == null || _shorts.Count == 0) _shorts = new List<Short> { DefaultShort() };
            if (_songs == null) _songs = new List<Song>();
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

        Clip SequenceClipAt(double t, TrackKind kind)
        {
            var seq = ActiveSequence;
            if (seq == null) return null;
            Clip found = null;   // 同時刻に複数あれば上のトラック（後勝ち）を採用
            for (int ti = 0; ti < seq.tracks.Count; ti++)
            {
                var track = seq.tracks[ti];
                if (track == null || !track.enabled || track.kind != kind) continue;
                for (int ci = 0; ci < track.clips.Count; ci++)
                {
                    var c = track.clips[ci];
                    if (c != null && t >= c.start && t < c.End) { found = c; break; }
                }
            }
            return found;
        }

        // ---- 既定 Sequence（スタンドアロンで即動くよう・U3 で VID2/AUD2 も追加し動的化のデモを充実）----
        static Sequence DefaultSequence()
        {
            var s = new Sequence { name = "Seq 01", length = 200.0 };
            var v1 = new Track { name = "VID 1", kind = TrackKind.Video, opacity = 1f };
            v1.clips.Add(new Clip { name = "CLIP A", start = 0,   duration = 52 });
            v1.clips.Add(new Clip { name = "CLIP B", start = 54,  duration = 68 });
            v1.clips.Add(new Clip { name = "CLIP C", start = 124, duration = 40 });
            var v2 = new Track { name = "VID 2", kind = TrackKind.Video, opacity = 0.72f };
            v2.clips.Add(new Clip { name = "OVERLAY", start = 24,  duration = 44 });
            v2.clips.Add(new Clip { name = "LOWERTHIRD", start = 132, duration = 48 });
            var a1 = new Track { name = "AUD 1", kind = TrackKind.Audio };
            a1.clips.Add(new Clip { name = "MASTER", start = 0, duration = 164 });
            var a2 = new Track { name = "AUD 2", kind = TrackKind.Audio, muted = true };
            a2.clips.Add(new Clip { name = "SFX", start = 28, duration = 20 });
            a2.clips.Add(new Clip { name = "SFX", start = 80, duration = 24 });
            s.tracks.Add(v1);
            s.tracks.Add(v2);
            s.tracks.Add(a1);
            s.tracks.Add(a2);
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
