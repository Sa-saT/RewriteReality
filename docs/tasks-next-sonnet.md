# 次期実装タスク（Sonnet 向け・2026-07-19 作成）

**✅ 全タスク完了・commit・push 済み（2026-07-20）**。Unity 同梱 Roslyn 直コンパイルで各タスクとも
0 エラー確認済み。実機/UI Builder での見た目・挙動確認は未（ユーザー側）。

| Task | 状態 | commit |
|---|---|---|
| Task 1（#28a） | ✅ 完了 | `f7aa3aa` |
| Task 2（#28b） | ✅ 完了 | `4ce2afc` |
| Task 3（#36） | ✅ 完了 | `2624690` |
| Task 4（#37） | ✅ 完了 | `0462225` |

想定ゴール＝**「1本のショーを準備（Edit）→本番（Live）で通せる」状態**。
現状のギャップは①タイムラインの音声が鳴らない（M13）②Play 中に組んだバンクが保存されない
③ライブラリ左ドックが実データ未連動、の3点。これを Task 1→4 で埋める。

Task 1→2 は依存関係あり（この順で）。Task 3・4 は独立（どちらが先でも可）。
**タスクは1つ完了するごとにコミットしてから次へ進む。**

---

## 共通ルール（全タスク厳守）

- **コミット**: タスク完了ごとに都度コミット。`git add` は**変更した明示ファイルのみ**
  （`-A`/`.` 禁止）。`push` は**ユーザーが明示したときのみ**。コミット末尾に
  `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`。
- **触らない**: `Unity Patcher 1.3.3 macOS/`・`err-images/`・`.env`。
  実行中の Unity エディタ（`Temp/UnityLockfile` あり）は**絶対に kill しない**。
- **検証＝Unity 同梱 Roslyn 直コンパイル**（エディタがロック中でも可能）:
  1. `RewriteReality/Assembly-CSharp.csproj` から `<HintPath>`（→ `-r:`）・
     `<Compile Include>`（→ ソース）・`<DefineConstants>`（`;` 区切り→各 `-define:`）を抽出し
     response file を生成。
  2. `"/Applications/Unity/Hub/Editor/6000.0.33f1/Unity.app/Contents/NetCoreRuntime/dotnet" "/Applications/Unity/Hub/Editor/6000.0.33f1/Unity.app/Contents/DotNetSdkRoslyn/csc.dll" -noconfig @rsp`
     に `-target:library -nostdlib+ -langversion:9.0 -out:<scratchpad>/rr.dll` を付けて実行。
  3. 出力に `error CS` が **0 件**なら合格。CS0649（SerializeField 未代入）警告は既存・無視。
  - **zsh の罠**: `IFS=';' read -ra` は使えない（"bad option: -a"）。
    `printf '%s\n' "$DEF" | tr ';' '\n' | while read -r d` を使う。define は約 124 個出るのが正常。
- **UI の原則**: 見た目=UXML/USS・挙動=薄い C#。**UI の構造変更・新コントロール追加は
  ClaudeDesign ハンドオフ経由が正本なので勝手にやらない**。本タスク群の UI 作業は
  「既存構造へのデータ結線・既存クラスの流用」に限定してある（Task 4 参照）。
  見た目の作り込み・実機確認はユーザー側（DESIGN.md ワークフロー）。
- **コード品質**: 60fps 維持＝毎フレームの `new`/LINQ/文字列生成禁止。差分ガード
  （同値なら実代入しない）は `SourceVideo.Loop/Speed` のパターンを踏襲。
  既存機能は**非破壊**（新規参照は SerializeField opt-in・未設定なら従来動作）。
- 完了したら `CLAUDE.md` の進捗ログへ 1 エントリ追記（既存エントリの書式に合わせる）。

---

## Task 1（#28a）✅ 完了（commit `f7aa3aa`）: タイムライン音声の内部再生 — `SourceAudio` シンク＋バインド

**目的**: `ShowTimeline` の Audio トラックのクリップをアプリ内で実際に鳴らす（M13 の核）。
映像の `_videoSink` と対称の opt-in 設計。

### 1-1. 新規 `RewriteReality/Assets/Scripts/Sources/SourceAudio.cs`

`SourceVideo.cs` を手本に、`[RequireComponent(typeof(AudioSource))] sealed class`：

- `Awake`: `playOnAwake=false`・`spatialBlend=0`・`loop=_loop`。
- プロパティ（すべて差分ガード付き・毎フレーム呼ばれる前提）:
  - `Loop`（`AudioSource.loop`）
  - `Speed`（0.1..4 clamp → `AudioSource.pitch`。※ピッチも変わるのは v1 仕様として許容・コメントに明記）
  - `Volume`（0..1 → 後述のフェード目標値 `_targetVolume`）
  - `Mute`（`AudioSource.mute`）
- `SetClip(AudioClip clip, double localTime)`: 同一参照なら無視。差し替え時は
  `clip` 代入→ `time = clamp(localTime, 0, clip.length)` → 再生意図があれば `Play()`。
- `Restart()` / `SetPlaying(bool)` / `_wantPlaying` / `OnEnable`/`OnDisable`:
  `SourceVideo` の #27c 実装（`_wantPlaying` 差分・無効時 Pause 回避）をそのまま踏襲。
- **プチノイズ対策の内蔵フェード**: `_targetVolume` へ `AudioSource.volume` を
  `Update()` で線形追従（追従速度 `[SerializeField] float _fadeSpeed = 12f` /秒）。
  `SetPlaying(false)` は即 Pause でよい（v1）。

### 1-2. `ShowTimeline.cs` の音声バインド経路

- `ClipAsset` に `public AudioClip audio;` を追加（`video` と並記）。
- `[SerializeField] SourceAudio _audioSink;`（Header「バインド」内・Tooltip も映像に倣う）。
- `ResolveAudio(Clip)` を `ResolveVideo` と対で追加。`_appliedAudioClip` で差分管理。
- `ApplyBinding` に audio 経路を追加。**仕様の要点**:
  - 供給元は `ActiveAudioClip`（＝Sequence/Song 側の解決済みヘッド）。
    **Short 押下中も音声は Sequence/Song 側を継続**（Short は映像の最上位レイヤーであり
    音声レイヤーを持たない・docs/07b §3.5.2 の整合）。よって Short 用の
    Loop 退避/Restart ロジックは audio には**適用しない**。
  - `shouldPlay` は `_playing && _rate > 0.0001f`（Short 項は含めない）。
  - `Speed` へ毎フレーム `Rate` を反映（差分ガードは sink 側にある）。
  - クリップ差し替え時は `SetClip(ac, _phLocalTime - clip.start)` で**頭出し位置を
    playhead に同期**。毎フレームのドリフト補正は**しない**（v1・コメントで明記）。
- Seek/Rewind 後の再同期: クリップが同一でも playhead ジャンプ時は time を合わせ直す
  必要がある。`Seek`/`Rewind` 側に「audio 再同期要求」フラグ（`_audioResyncPending` 等）を
  立て、`ApplyBinding` で消費して `AudioSource.time` を合わせる（映像は VideoPlayer が
  自走のままで v1 は触らない）。

### 完了条件

Roslyn 直コンパイル 0 エラー。`_audioSink` 未設定なら一切挙動が変わらないこと
（コードパスを目視確認）。コミット例: `feat(#28a): タイムライン音声の内部再生（SourceAudio＋バインド）`

---

## Task 2（#28b）✅ 完了（commit `4ce2afc`）: トラック mute の実効化＋最終ミックス解析の整合

**目的**: Audio トラックの `muted` を再生へ実際に効かせ、音連動（AudioAnalyzer）が
内部再生音を拾う構成であることをコード上で担保する。

- `ShowTimeline` のクリップ解決（`ResolvedClipAt` が使う Sequence 内探索）で、
  **`TrackKind.Audio` かつ `muted` なトラックは解決対象から除外**する
  （＝mute したトラックのクリップは鳴らない。上のトラックが mute なら次の Audio トラックが
  解決される、という素直な仕様）。Video 側の既存挙動は変えない。
- `Track.opacity` の音声流用（音量）は**やらない**（映像用フィールドのため）。代わりに
  `Track` に `[Range(0,1)] float volume = 1f`（Tooltip: 音声トラック音量・M13）を追加し、
  解決したトラックの `volume` を `_audioSink.Volume` へ毎フレーム反映（差分ガードは sink 側）。
- **解析はコード変更不要**であることを確認して完了報告に書く:
  `AudioAnalyzer.Tick()` は `AudioListener.GetSpectrumData` なので、`SourceAudio` の
  AudioSource 出力は自動的に最終ミックス解析へ乗る（docs/05 の「解析は最終ミックス」を充足）。
- エディタでの音出し検証はユーザー側。**既知の罠**（memory: editor-audio-test-gotcha）:
  マイク（MicInput）を開くと Bluetooth が HFP に落ちてピッチが変わる。エディタ検証は
  MicInput 無効で行う旨を CLAUDE.md 追記に含める。

### 完了条件

Roslyn 直コンパイル 0 エラー。コミット例: `feat(#28b): Audioトラックmute/volumeの実効化`

---

## Task 3（#36）✅ 完了（commit `2624690`）: ショーデータ永続化 — バンクの JSON 保存/読込

**目的**: Play 中に組んだ Sequence/Short/Song バンクがアプリ終了で消える問題を解消。
シーン（SerializeField）とは別に JSON で保存/復元する。

- `ShowTimeline` に `[Serializable] sealed class ShowState`（`List<Sequence> sequences;
  int activeSequence; List<Short> shorts; int activeShort; List<Song> songs;
  int activeSongIndex; bool loop; float rate;`）を追加。
  `Clip.sourceId` は文字列参照なので JsonUtility でそのまま往復可能。
  **`_library`（VideoClip/AudioClip 実参照）は保存対象外**（シーン持ちのまま）。
- API: `public void SaveShow(string path = null)` / `public bool LoadShow(string path = null)`。
  既定パス＝`Application.persistentDataPath + "/show.json"`。Load はファイルが無ければ
  false を返すだけ（例外にしない）。Load 成功時は index を範囲内へ clamp し、
  `SelectSequence(activeSequence)`（または Song が active なら `SelectSong`）で
  再生コンテキストを整える。`_held` はクリア。
- opt-in 自動化: `[SerializeField] bool _autoLoadOnStart = false;`（`Start()` の
  seed 前に Load 試行・成功なら seed 不要）＋ `[SerializeField] bool _autoSaveOnQuit = false;`
  （`OnApplicationQuit` と `OnDisable` で Save。二重保存は無害なので許容）。
  **既定 false＝非破壊**。
- エディタ動作確認用に `[ContextMenu("Save Show")]` / `[ContextMenu("Load Show")]` を付与。
  **UI ボタンは追加しない**（新コントロールは ClaudeDesign 経由が必要なため。
  完了報告に「Save/Load ボタンの UI 化は ClaudeDesign ハンドオフ待ち」と明記）。
- **調査ステップ（実装前に必ず）**: `OperatorUI` がタブ列・トラック行をいつ再構築するかを
  読み、Load 後に UI が追随するか確認する。追随しない場合は `ShowTimeline` に
  `public event Action StructureChanged;` を追加して Load 成功時に発火し、
  `OperatorUI` 側で購読→既存の再構築メソッドを呼ぶ（既存イベント
  `ActiveVideoClipChanged` のパターンに合わせる）。

### 完了条件

Roslyn 直コンパイル 0 エラー。既定（両フラグ false）で挙動不変。
コミット例: `feat(#36): ショーデータのJSON保存/読込（opt-in自動Load/Save）`

---

## Task 4（#37）✅ 完了（commit `0462225`）: PERFORM 左ドック・ライブラリの実データ連動（最小版）

**目的**: 静的プレースホルダの Sources/Audio リストを `ShowTimeline._library` の実データで
生成し、クリックで**選択中 Short の `clip.sourceId` へ割当**できるようにする
（汎用セレクションモデル #3 の本実装までの最小つなぎ）。

- **調査ステップ（実装前に必ず）**: `OperatorShell.uxml` の PERFORM 左ドック
  （Sources/Audio/Scenes セクション）の現行構造と USS クラス名を確認。
  **既存の行構造・クラスをそのまま流用**し、UXML の静的行を C# 生成に置き換えるだけに
  留める（新しい見た目・新コントロールは作らない）。
- `ShowTimeline` に読み取り API を追加: `public int LibraryCount` /
  `public ClipAsset GetLibraryItem(int i)`（範囲外 null）。
- `OperatorUI`: 左ドックのライブラリ行を `_library` から再構築
  （`RebuildSongRail` のパターン踏襲・video 持ちは Sources、audio 持ちは Audio セクションへ）。
  行クリック時:
  - Short タブ表示中（`_viewKind == TabKind.Short`）→ アクティブ Short の
    `clip.sourceId` へその id を代入し、行に選択ハイライト（既存の selected 系クラス流用）。
    `ShowTimeline` に `public void AssignShortSource(string id)`（アクティブ Short の
    sourceId 設定・`_appliedClip` 無効化で次フレーム再バインド）を追加して経由する。
  - それ以外のタブ → 選択ハイライトのみ（割当はしない・v1）。
- Scenes セクションは現状プレースホルダのまま**触らない**。
- ライブラリが空のときは従来のプレースホルダ表示を残す（見た目の空白化を避ける）。

### 完了条件

Roslyn 直コンパイル 0 エラー。`_library` 空なら見た目・挙動とも従来どおり。
コミット例: `feat(#37): 左ドックライブラリを_library実データ連動＋Short割当（最小版）`

---

## 保留（このバッチではやらない・参考）

- **#33** パペット pin エフェクト（故意の局所歪み・docs/03）。
- **M7 残り**: MIDI ラーン（Minis）/ OSC 受信（OscJack）→ ControlHub の抽象マッピング層へ。
- **M8**: パフォーマンス計測・`.app` ビルド・素材同梱・現場手順書。
- **ユーザー側のエディタ作業**（agent はやらない）: `Main.unity` で `Timeline` の
  `_videoSink`/`_audioSink` 配線・`_library` へのクリップ登録・シーン再保存
  （U11 改名で残った旧フィールド名 `_songs`/`_activeSong` の直列化リフレッシュ）。
