# RewriteReality

リアルタイム・カメラ埋め込み VJ アプリ。事前に用意したベース動画の「指定した箇所」
（マーカー/トラッキングで追従する領域）に、ライブカメラ映像をリアルタイム合成し、
グリッチ等のエフェクトをかけて、フルスクリーン/プロジェクター＋Syphon/NDI へ同時出力する。

## リポジトリ / 構成

- ローカル: `~/Documents/Unity/RewriteRealityProject/`（Obsidian Vault から移行済み）

```
RewriteRealityProject/        ← git repo ルート
├── docs/                     ← 設計ドキュメント（00-12）
├── DESIGN.md                 ← オペレータUIデザインシステム（DaVinci系ダーク×Cursor抑制）
├── CLAUDE.md / .gitignore
└── RewriteReality/           ← Unity プロジェクト本体（1階層ネスト）
    ├── Assets/ Packages/ ProjectSettings/   → 追跡
    ├── Library/ Temp/ Logs/ UserSettings/   → .gitignore で除外
    └── .env                                 → 秘密情報・**コミット禁止**（除外済み）
```

## 技術スタック（すべて無料・ネイティブ）

- **Unity 6 LTS（`6000.0.33f1`）/ URP** — C# / macOS Apple Silicon / Metal
- **Klak**（Keijiro, 現役保守）: KlakSyphon / KlakNDI / KlakHap
- **VFX Graph**（GPU パーティクル）/ **Shader Graph**
- **Input System** ＋ **Minis**（MIDI）/ **OscJack**（OSC）
- **OpenCvSharp**（BSD・無料）: ArUco マーカー検出 + findHomography
- 有料アセットは使わない方針。土台選定の経緯は `docs/09`、oF 代替案は `docs/10`

## 設計ドキュメント（`docs/`）

| ファイル | 内容 |
|---|---|
| `docs/00-overview.md` | 全体像・無料構成・方針 |
| `docs/01-architecture.md` | C# モジュール設計・データフロー・`EffectBase` 拡張基盤 |
| `docs/02-tech-stack.md` | Unity/パッケージ/環境セットアップ |
| `docs/03-tracking-compositing.md` | ArUco・ホモグラフィ・射影合成 |
| `docs/04-effects.md` | URP エフェクトパイプライン |
| `docs/05-audio-reactive.md` | FFT/ビート → エフェクト連動 |
| `docs/06-output.md` | フルスクリーン/Syphon/NDI 出力 |
| `docs/07-control-ui.md` | GUI/MIDI/OSC・プリセット |
| `docs/07b-operator-ui-brief.md` | オペレータUI デザインブリーフ（#24・Claude Design 用・MadMapper 参照・正本） |
| `docs/08-roadmap.md` | 実装ステップ（M0〜M8） |
| `docs/09-platform-comparison.md` | 土台選定の経緯（業界マップ） |
| `docs/10-openframeworks-alternative.md` | oF 代替設計（参考） |
| `docs/11-todo-and-decisions.md` | 実装タスク（M別）＋確定した選定①〜⑩ |
| `docs/12-feasibility-audit-2026-06.md` | 実現可能性監査（OpenCvSharp/arm64・方式C） |
| `DESIGN.md`（ルート） | オペレータUIデザインシステム・トークン（`docs/07` と対） |

## 現在の状況 / 次の一手

- docs・設計は確定。リポジトリ移行済み。
- **Unity プロジェクト作成済み**: `RewriteReality/`（**URP 17.0.3 / `6000.0.33f1`**、Input System 同梱）。
- **選定フェーズ完了（2026-06-23）**: 動画=mp4/1080p/**60fps**、四隅=方式C(ベイク)、オーディオ=アプリ内FFT+簡易onset、
  出力=FS→Syphon→NDI＋コーナーピン、操作=抽象マッピング層(当面KB/GUI)。詳細は **`docs/11-todo-and-decisions.md`**。
- **M0 ほぼ突破（go）**: #1〜3 パッケージ導入 ＋ **#4 動作確認(2026-06-25)完了**。
  VideoPlayer / WebCamTexture / KlakSyphon / KlakNDI を **Apple Silicon 実機で確認・全 OK**
  （Syphon=OBS Syphon Client、NDI=OBS+distroav で受信確認。bundle は arm64 universal）。
  実機メモ・詰まり対処は **`docs/M0-test-procedure.md`** 末尾に記録。
- **#5 C# スケルトン生成 完了**（branch `feat/m0-csharp-skeleton`・コンパイル0エラー確認）。
  `Assets/Scripts/` に `docs/01` のモジュール構成どおりの骨格（Manager / Source* / *CornerSource /
  Compositor / EffectBase+Chain+初期4種 / AudioAnalyzer / OutputManager / ControlHub / Preset）。
  中身は空実装＋TODO（エフェクトは素通し、Compositor のワープ・各シェーダは未実装）。
- **シーン配線＋パイプ疎通 完了**（`Assets/Scenes/Main.unity`・commit b14d48c）。
  Manager/Source*/Compositor/EffectChain/AudioAnalyzer/OutputManager を配置・配線し、
  **動画→合成(背景のみ)→エフェクト(素通し)→出力(FS/Syphon/NDI) が1本通るのを実機確認**。
  fix: SourceVideo.OnDisable の停止時 Pause ガード済み。`baseRT` 追加。
  ※ Main.unity の clip は `_Test/IMG_0016.MOV`(gitignore)参照のためクローン環境では未解決。
  ※ track.json 未配置のため BakedCornerSource は毎回 FullFrame 据え置き（警告は正常）。
- **M0 コア完了**: Compositor のコーナーピン合成（四隅＋多pin メッシュ）＋カスタムシェーダ5本
  （CornerPin/RgbShift/ColorGrade/BlockGlitch/Feedback）を実装・実機確認済。現在の「次の一手」は
  下部 §「次の一手（マッピング品質）」の **#34/#35** を参照（M10/M11 まで実装済み）。
  - 残る将来関門: **OpenCvSharp の arm64 ビルド**（LiveCv 時・go/no-go・`docs/12`）。
    公式 NuGet に macOS arm64 ネイティブは無く、**contrib(aruco)込みの自前ビルドが本命**（方式C採用でコアは対象外）。
- 同じ親フォルダにある `My project`(HDRP/2022.3) は**本プロジェクトとは別物**。
- **将来の深度レイヤー(M9)＝深度カメラ（深度センサー・例: Orbbec Femto / RealSense）**を `IDepthSource`
  （`DepthCameraSource`）で供給（`docs/04`・`08`・`11`・`12`）。**旧案の iPhone/iPad Pro LiDAR 前提は撤回**
  （Pro 機なし・2026-06-30）。`Rcam3`（Keijiro, iPhone LiDAR→NDI）は Pro 機がある場合の参照実装に格下げ。
  コアは深度無しで完成（M9 は後付け・本体無改修）。
- **操作UI = UI Toolkit（#20 土台 完了・2026-06-28）**: `Assets/UI/`（RewriteReality.uss=DESIGN.md
  トークン→USS／OperatorShell.uxml／FxRow・ParamRow.uxml 行テンプレ）＋ `OperatorUI.cs`
  （ControlHub/EffectParameter 双方向バインド・preview=EffectChain.FinalTexture・FPS）。配線=
  PanelSettings(Scale Mode=**Constant Pixel Size**)→UIDocument(Source=OperatorShell.uxml)→OperatorUI。
  IMGUI 版 OperatorGui は併存。
  - **ワークフロー確定（2026-07-04）＝UI/UX 変更は ClaudeDesign の「Unity 共有ドキュメント」経由が正本**。
    ClaudeDesign（`ui_kits/operator/UNITY-HANDOFF-*.md`）を DesignSync で読み UXML/USS＋C# バインドへ反映する。
    **UI Builder は微細変更（px/色/整列）と mock 確認に用途を絞る**（構造変更・新コントロール・ページ IA は
    ハンドオフ .md 経由）。詳細＝`docs/07-control-ui.md`「UI/UX 変更のワークフロー確定」。挙動=薄い C#・見た目=UXML/USS。
  - **⚠ 見た目・レイアウトはユーザーが UI Builder で自身で作成・確定する**。agent は UXML/USS の足場と
    新コントロール用テンプレ＋バインドを用意する役で、**ビジュアルの作り込みはユーザーに委ねる**。
    **デザイン確定（タスク#24）後に #21→#22→#18（領域マッピングUI）へ着手**。詳細＝`docs/07-control-ui.md`。
  - **デザインブリーフ＝`docs/07b-operator-ui-brief.md`**（#24 用・Claude Design に貼る一枚仕様・MadMapper 参照）。
  - **Claude Design 連携**（見た目の前段ツール・`claude.ai/design`）: 使い方・扱い・合意フローは
    **`docs/07-control-ui.md`「見た目の前段に Claude Design を使う」**＋**`docs/07b` §0/§7**（貼付プロンプト雛形）が正本。
    運用＝**UI/UX 変更は ClaudeDesign の Unity 共有ドキュメント経由→UXML/USS へ反映／微細な詰めのみ UI Builder 直**
    （上の「ワークフロー確定（2026-07-04）」が正）。DesignSync MCP（`/design-login`）で
    プロジェクトを読み書きでき、#24 で operator console キットを取り込み移植済（現デザイン再現に新規生成は不要）。
- **AV ショー化の拡張（決定 2026-06-30・段階的実装）**: 「カメラ埋め込み VJ」→ **タイムライン＋音声ミックスを持つ
  AV ショー・ツール**へ拡張。①下部＝**マルチトラック・タイムライン**（映像/音声）②音声＝**内部再生＋外部解析の両対応**
  （fade/mute・解析は最終ミックス）③**準備 Edit / 本番 Live の 2 モード**（準備で surface・エフェクト範囲[surface/全体]・
  尺・出力変形を仕込み、本番でライブ動画を埋め込み＋値のみ操作）④**Output Surface（出力変形）を正式機能に格上げ**
  （MadMapper 流メッシュ/コーナーピン）。**実装はコア完成後に段階的**（M10 出力変形→M11 2モード→M12 タイムライン→
  M13 マルチトラック/音声・タスク#25〜28）。仕様=`docs/07b`、決定=`docs/11` B9、モジュール=`docs/01`。
  タイムラインの動き＝**C（song＋short）確定**：song=リニア通し（裏で進行）＋short=キー割当の**ホールド発火**
  （押下中だけ最上位レイヤー・離すと song に戻る＝Resolume「Piano」）。複数同時押しは後押しが上。詳細=`07b` §3.5.2。
  - **進捗**: **#23 ビルド準備＝完了（2026-07-01）**：カスタムシェーダ5本を Always Included＋Mic Usage Desc。
    **実機スタンドアロンビルドで実マイク音連動＋シェーダ描画を確認**。Build シーンは `Main.unity` に修正済（旧=空の SampleScene）。
    **#25 M10 出力変形＝バックエンド実装**（`OutputWarp`＋`OutputManager` 配線・既定OFF素通し・射影数学は `WarpMath` を
    `Compositor` と共有）。**ビルド成功＝Compositor の WarpMath リファクタは無回帰**を確認。OutputWarp の出力変形“見た目”は
    未検証（scene に OutputWarp を配線＋ON で確認）。編集UIは #22。
    **#26 M11＝バックエンド実装（2026-07-01）**：`AppMode`（準備 Edit/本番 Live・`GuardStructuralEdit` で構成ロック）、
    `SurfaceManager`＋`Surface`（複数 Input Surface・各面が追従四隅＋多pin warp＋content＋opacity を保持）、
    `Compositor` に多surface合成経路を追加（`DrawContent` に共通化・従来の単一 surface 経路は温存）、
    `EffectBase.scope`(Global/Surface)＋`targetSurfaceId` と `EffectChain.Process`/`ProcessSurface` で範囲別適用。
    `Manager` は `SurfaceManager` 配置時のみ多surface経路・未配置なら従来経路にフォールバック（**非破壊**）。
    warp グリッド生成は `WarpMath.FillRegularGrid` に統一（Compositor/Surface/OutputWarp）。
    **Unityコンパイル/シーン配線は未検証**（シーンに SurfaceManager 配置＋surface 追加は #22 で）。UI は #22。
  - **オペレータUI＋マッピング機能 実装＆実機検証済（2026-07-02〜03・すべて push 済）**:
    **#21** WARP 制御点ドラッグ＋メッシュ線（`WarpCanvas`＋`IWarpTarget`）。**#22** M11 UI（Surface 一覧/選択/追加削除/
    per-surface プロパティ/準備 Edit・本番 Live モード切替）＋シーンに `Surfaces`(AppMode＋SurfaceManager) 配置。
    **#25** 出力変形を UI 編集（EMBED⇄OUTPUT・WYSIWYG）。**#32** Surface **Mask/Crop モード**（既定＝歪めない窓抜き）
    ＋Surface/Content 変形（SHAPE=窓移動/Scale・CONTENT=枠内映像 pan/Zoom）。設計判断＝Surface は「歪めない Mask 」が本命、
    歪みは別建て（`surface-mask-vs-warp` メモ・`docs/03`）。
    **アイコン**＝`RrIcon`（painter2D ベクター描画・`[UxmlElement]`・素材同梱ゼロ）でトランスポート/mute の絵文字・
    文字グリフを置換（色は継承 color でボタン状態[通常/hover/active]に追従・UI Builder で配置/リサイズ可・2026-07-04）。
    **タイムライン song/short タブ UI＝移植済（2026-07-04・Claude Design Timeline.jsx→UXML/USS）**: タブバー
    （SONG=緑/SHORT=アンバー・バッジ・P1 パッドチップ）＋タブ切替（OperatorUI）＋short ボディ（KEY 行＋⚡ホールド
    発火レーン＝押下中クリップ全幅のプレビュー表現）。バンク追加(+)/パッド割当マトリクス/実再生は #27（現状 disabled）。
  - **#34 Grid/Bezier ワープ＝バックエンド完了（2026-07-04・commit e27871f）**: `WarpMath.SampleGridSmooth`
    （bicubic Catmull-Rom・制御点を通る C1 連続）＋`Compositor` のパッチ細分化描画＋メッシュ位相プールキャッシュ。
    `Surface.FitMode.Project` は `Grid` に統一（2×2 は線形へ縮退＝旧4pinと後方互換）。`OutputWarp`/`WarpCanvas` も同じ
    細分化に統一（WYSIWYG）。**Unity コンパイル/実機は未検証**。**#35 OUTPUT グリッド校正**（格子オーバーレイ＋投影
    キャリブレーション表示の UI 足場＝GRID/TEST トグル）は先行実装済み。故意の局所歪みは **#33**（パペットpin エフェクト、未着手）。
    詳細＝`docs/03`・`06`。
  - **ページ IA 3→2（PERFORM/MAPPING）＋左ドック/ワープエディタの機能化（2026-07-05〜06・完了）**:
    ClaudeDesign UNITY-HANDOFF 反映で旧 OUTPUT タブを廃止し **PERFORM（既定・ライブプレビュー＋ライブラリ左ドック）/
    MAPPING（WARP エディタ起動＋Surfaces 左ドック）** の2ページに整理（`SelectPage` が機能・見た目の両方を駆動）。
    Surface Fit は単一チップ→**MASK|GRID セグメント UI**（モード別セクション出し分け）。上バーは transport を撤去し
    タイムライン側に一本化（1機能1箇所）、OUTPUT はラベルのみトグル（Full/Syphon/NDI・ON=緑/OFF=灰）に簡素化。
    **出力ルートは既定 OFF**に変更（コード既定＋Main.unity 直列化値、起動時誤配信防止）。左ドックのライブラリ
    （Sources/Audio/Scenes）は静的プレースホルダで実データ未連動（汎用セレクションモデル=#3 待ち）。
  - **#27 タイムライン再生バックエンド（2026-07-06 着手・commit 57d3ccf・2026-07-19 時点で大半完了）**: `ShowTimeline`
    （Sequence/Track/Clip データ＋Play/Pause/Loop/Rewind/Seek のトランスポート・クロック）を `OperatorUI` の
    上部トランスポート＋タイムライン表示と接続（Sequence リニア再生・playhead 反映）。実クリップの
    source バインド（`_videoSink`＋`ClipAsset` ライブラリ・opt-in）・Short ホールド発火（§3.5.2・
    `PollShortInput`→`HoldStart/HoldEnd`・TopShort 優先の `ActiveVideoClip`）・Master Speed（`Rate`）配線・
    シーン配置（`Main.unity` の `Timeline` GameObject）は実装済み（#27b/#27c で仕上げ・下記）。
    残り：audio 内部再生（M13・タイムラインの音声トラックをアプリ内で実際に鳴らす経路）。
  - **#29/U8＝Freeze撤去＋タイムライン トラック行の縦スクロール化（2026-07-14・commit 77e6dee・実機確認済）**:
    UNITY-HANDOFF §7b 07-12/07-13 反映。`FreezeEngine`（`Manager`/`ControlHub`/`OperatorUI`）を撤去
    （Timeline 再生停止・Fade to Black と役割重複のため。Master Speed のみ残置）。Song/Short のトラック行を
    `ScrollView`（`vertical-scroller-visibility=Hidden`）でラップし、ルーラー・再生ヘッドは ScrollView 外に
    固定（トラック行が増えても見切れない・WYSIWYG）。`Inspector.jsx` ローカルミラーの Freeze 行も削除。
  - **#29/U9〜U11＝Short Loop 重複解消＋Banks＋MPC 流バンク再編（2026-07-14・コンパイル確認済）**:
    UNITY-HANDOFF §7b/§7c 07-13/07-14 反映。**U9**＝Short タブ表示中は共通トランスポートの Loop ボタンを
    非表示（`Hold-Loop` と役割重複のため）。Song/Sequence は維持（`TransportGlyph({ showLoop })` 相当）。
    **U10**＝PERFORM 左ドックに `Banks` セクション（保存済み Sequence/Short/Song を種別ドット付きで一覧・
    クリックで該当タブを開く）。タブバーを `ScrollView(Horizontal, scroller Hidden)` 化しタブ多数時も横スクロール。
    **U11（最大）**＝`ShowTimeline.cs` を全面改名：旧 `Song`（マルチトラックバンク）→ `Sequence` に改名
    （`ActiveSequence`/`SequenceCount`/`GetSequence`/`SelectSequence`/`AddSequence`）。新たに `Song`＝Sequence
    を並べる MPC 流セットリスト（`SongStep{ sequenceName, repeat }`・`AddSongStep`/`RemoveSongStep`/
    `MoveSongStep`/`SetSongStepRepeat`）を追加。タブは Sequence/Short/Song の3種（`TabKind` 拡張・アイコン=
    `RrIcon.Kind.ListMusic` 新設・色=live/primary/selection で判別）。Song タブ本文は2ペイン
    （左=ステップ列＋並べ替え/×N repeat/削除/+ Step・右=選択ステップの読み取り専用プレビュー＋
    「Edit Sequence →」ジャンプ）。`OperatorUI` の `_shortView`(bool) は `_viewKind`(TabKind) に統合。
    `LeftDock.jsx`/`Timeline.jsx` ローカルミラーもリモート最新へ追随。**Unity batchmode コンパイル 0 エラー
    確認済み・実機/UI Builder での見た目確認は未（見た目の作り込みはユーザー側・DESIGN.md ワークフロー）**。
  - **#29/U12＝Song タブを MPC 流 横ストリップ UI へ全面刷新（2026-07-18・コンパイル確認済）**:
    UNITY-HANDOFF §7c「Song の横ストリップ UI（2026-07-18）」反映。U11 の**2ペイン**（左=縦ステップリスト＋
    上方向ポップオーバー・右=読み取り専用プレビュー）を**廃止**し、横並びステップカード列に刷新（縦リストが
    高さ0に潰れて追加ステップが見切れるバグも構造ごと解消）。構成＝集計ヘッダー（`PLAY ORDER · N steps · M plays`＝
    Σ×N＋選択 Sequence への `Edit <Seq> →` ジャンプ）／**左固定 Add Sequence レール**（全 Sequence 常時一覧・
    1クリックで末尾追加・破線枠は UI Toolkit 非対応のため実線ヘアラインで代替）／**横スクロールのステップカード列**
    （各カード幅158px＝番号／Sequence 名／Edit ペン→該当 Sequence タブ／×削除／`− ×N +` ステッパー／`‹ ›` 並べ替え・
    カード間に `→`）。`OperatorUI.cs`＝`RebuildSongSteps` を横ストリップ生成へ書き換え（`BuildSongCard`/
    `RebuildSongRail`/`UpdateSongSummary`/`JumpToSequenceByName`）、旧 `RefreshSongPreview`/`RebuildSongAddStepMenu`/
    `BuildSongStepRow`/`FindSequenceByName` と関連フィールドを撤去。`OperatorShell.uxml`＝`rr-tl-songlist` を
    head＋rail＋strip 構造へ差し替え。`RewriteReality.uss`＝`.rr-song-step*/.rr-song-preview*` を
    `.rr-song-head/rail/card` 群へ置換（横 ScrollView は content-viewport にも `flex-direction:row` を明示＝
    `.rr-tl-tablist` と同じ罠）。`RrIcon.cs`＝`Plus`/`SquarePen` を追加。`ShowTimeline.cs` は API 変更なし
    （既存の `AddSongStep`/`RemoveSongStep`/`MoveSongStep`/`SetSongStepRepeat`/`GetSequence` を流用）。
    **Unity 同梱 Roslyn（csc）で Assembly-CSharp を全 define 付きコンパイル＝0 エラー確認済み・実機/UI Builder での
    見た目確認は未（見た目の作り込みはユーザー側・DESIGN.md ワークフロー）**。ローカルミラー
    `UNITY-HANDOFF.md`/`Timeline.jsx` もリモート 07-18 へ追随。
  - **#27b/#27c＝Song 通し再生＋Short Hold-Loop 実挙動（2026-07-19・コンパイル確認済）**:
    `ShowTimeline` の再生クロックが Sequence 単体前提だった残課題を解消。**#27b**＝`PlaybackContext`
    （Sequence|Song・`SelectSequence`/`SelectSong` で切替・`SelectShort` は不変）を追加し、`ResolvePlayhead()`
    で再生ヘッド解決を一本化：Song コンテキストは `Song.steps` を先頭から歩き、解決可能な step（参照 Sequence が
    見つかる）の Σ(length×repeat) を積算して該当 Sequence とローカル時刻（repeat 内で mod）を求める（未解決
    step は時間軸に寄与せずスキップ）。`Length` は Song コンテキストで `SongTotalLength()`（空/全未解決なら 1.0）
    を返すよう拡張、`ActiveVideoClip`/`ActiveAudioClip` は解決済みヘッド（`ResolvedClipAt`）を参照。
    `CurrentSongStep` を公開し、`OperatorUI` が Song タブ表示中に変化を検知して再生中カードへ
    `rr-song-card--playing` を付与（`_songCards` キャッシュの class トグルのみ・再構築なし）。空 Song 再生時は
    警告ログを 1 回だけ出す（`SelectSong` で再アーム）。**#27c**＝`Short.holdLoop` が再生側で未消費・同一 Short
    の再発火で頭出しされない・`Rate`（Master Speed）が映像速度に効かない、の3点を修正。`SourceVideo` に
    `Restart()`（time=0+Play）を追加し `Loop`/`Speed` setter に差分ガードを付与。`ShowTimeline.ApplyBinding` は
    Short 表示中に `holdLoop` を sink の `Loop` へ反映（Sequence 側の元設定は退避→離脱時に復元）・`Speed` へ
    `Rate` を毎フレーム反映・`HoldStart` が「新規発火」を検知したときのみ `Restart()` を呼ぶ（同一クリップ
    参照でも頭出し）。**Unity 同梱 Roslyn（csc）で Assembly-CSharp を全 define 付きコンパイル＝0 エラー確認済み・
    実機での見た目/挙動確認は未**。
  - **#28a/#28b＝タイムライン音声の内部再生（M13・2026-07-20）**: `SourceVideo` と対称の opt-in シンク
    `SourceAudio`（`Assets/Scripts/Sources/`）を新設（Loop/Speed/Volume/Mute・差分ガード・`_wantPlaying` に
    よる再生意図管理・Volume はプチノイズ対策の内蔵フェード追従）。`ShowTimeline` に `ClipAsset.audio`・
    `_audioSink`（opt-in・未設定なら無音）を追加し、`ApplyBinding` から独立した `ApplyAudioBinding` を毎フレーム
    実行（映像クリップ変更の有無に関わらず Loop/Speed/再生状態を反映）。供給元は解決済み再生ヘッドの Audio
    トラック（`ResolvedClipAt(Audio)`）＝**Short 押下中も音声は Sequence/Song 側を継続**（Short は映像専用
    レイヤーのため holdLoop 退避/頭出しは適用しない）。`Rewind`/`SeekNormalized` は `_audioResyncPending` を
    立て、次の `ApplyAudioBinding` で同一クリップでも `AudioSource.time` を playhead へ再同期する。
    **#28b**＝`Track.muted` を `ResolvedClipAt`/`ResolvedAudioTrack` の解決段階で除外（mute したトラックは
    実際に鳴らない・上のトラックが mute なら次の Audio トラックが解決される）。`Track.volume` を新設し
    解決トラックの音量を毎フレーム `_audioSink.Volume` へ反映。解析（`AudioAnalyzer.Tick`）は
    `AudioListener.GetSpectrumData` のため**コード変更不要**で内部再生音を自動的に最終ミックスとして拾う
    （docs/05 の「解析は最終ミックス」を充足）。**Unity 同梱 Roslyn（csc）で Assembly-CSharp を全 define 付き
    コンパイル＝0 エラー確認済み・実機での音出し確認は未**（エディタ検証時は MicInput 無効で行うこと・
    Bluetooth が HFP に落ちてピッチが変わる既知の罠）。`Main.unity` への `_audioSink`/`_library.audio` 配線・
    `Track.volume` の Inspector 調整はユーザー側。
  - **#36＝ショーデータの JSON 永続化（2026-07-20）**: `ShowTimeline` に `ShowState`（Sequence/Short/Song
    バンク＋各 active index・再生コンテキスト・loop/rate。`_library` の実アセット参照は対象外＝シーン持ちの
    まま）と `SaveShow`/`LoadShow`（既定パス＝`Application.persistentDataPath/show.json`・JsonUtility 往復）
    を追加。`LoadShow` は index を範囲内へ clamp・`_held` クリア・保存時の再生コンテキストへ
    `SelectSequence`/`SelectSong` で復帰し、`StructureChanged` イベントを発火する。`OperatorUI` がこれを
    購読し（`OnTimelineStructureChanged`）タブ/トラック行/ステップ列/Banks 一覧を全面再構築。
    自動化は `_autoLoadOnStart`/`_autoSaveOnQuit`（既定 false・opt-in・非破壊）。エディタ確認用に
    `[ContextMenu("Save Show"/"Load Show")]` を用意（**UI ボタン化は ClaudeDesign ハンドオフ待ち**）。
    **Unity 同梱 Roslyn（csc）で Assembly-CSharp を全 define 付きコンパイル＝0 エラー確認済み・実機確認は未**。
  - **#37＝PERFORM左ドックのライブラリを_library実データ連動（最小版・2026-07-20）**: `ShowTimeline` に
    `LibraryCount`/`GetLibraryItem(i)`（読み取り）＋`AssignShortSource(id)`（アクティブ Short の
    `clip.sourceId` 設定・`_appliedClip` 無効化で次フレーム再バインド）を追加。`OperatorShell.uxml` の
    Sources/Audio Foldout 内の静的プレースホルダ行を `rr-lib-sources-list`/`rr-lib-audio-list` という
    named コンテナで包み（既存の行構造・USS クラスはそのまま流用・新規コントロールなし）、`OperatorUI.
    RebuildLibraryDock()` が `_library` に video/audio 実体があるセクションだけをそのコンテナへ実データで
    再構築する（該当種別が 0 件のセクションは従来のプレースホルダを残す＝空白化回避）。選択ハイライトは
    既存の汎用選択機構（`WireDockItems`/`SelectionModel`・#3 スライス）をそのまま再利用（新規実装なし）。
    Short タブ表示中に実ライブラリ行（`_library` に実在する id のみ・プレースホルダ行は無視）を選ぶと
    `OnSelectionChanged` から `AssignShortSource` を呼びアクティブ Short へ割当、それ以外のタブでは
    選択ハイライトのみ（割当はしない）。**Unity 同梱 Roslyn（csc）で Assembly-CSharp を全 define 付き
    コンパイル＝0 エラー確認済み・実機/UI Builder での見た目確認は未**。`Main.unity` の `_library` への
    クリップ登録はユーザー側。
  - **#M7＝MIDI(Minis)/OSC(OscJack) 実機コントロール入力層（2026-07-20・branch `feat/m7-midi-osc-control`）**:
    本番のライブ操作（docs/07 §2/§3）。コントローラ非依存の抽象マッピングを `ControlHub` に実装し、
    Minis/OscJack の入力を橋渡しする **opt-in コンポーネント**（`KeyboardControl` と同型・シーン未配置なら
    非破壊）を追加。**`ControlHub`**＝CC/Note マッピング表（番号→(effect,param)/(effect)・エフェクト側に
    番号を埋めない）＋**MIDI ラーン**（`BeginMidiLearn`→次に触れた CC を選択中パラメータへ、Note を選択中
    エフェクトの ON/OFF へ割当）、`ApplyMidiCc`/`ApplyMidiNote`、OSC 解決ヘルパ `ApplyOscGlobal`
    （master/fade/bpm/speed）/`ApplyOscFx`（`/rr/fx/<slug>/<param>`・`enabled`）、`Slugify`（Name→slug）、
    MIDI マップの JSON 保存/読込（`persistentDataPath/midimap.json`・`_autoLoadMidiMap` opt-in・ContextMenu）。
    **`MidiControl`**＝`InputSystem.onDeviceChange` で Minis `MidiDevice` を発見し `onWillControlChange`/
    `onWillNoteOn`/`onWillNoteOff` を購読（値は will イベント引数の float を使う＝コントロール状態は確定前・
    Minis 実装準拠・デバイス着脱に live 追従）。**`OscControl`**＝`OscServer(port)` を待ち受け、OscJack は
    ワーカースレッドで dispatch かつ完全一致のみのため **monitor コールバック（空アドレス）で全受信→
    (address,float) をスレッドセーフキューへ積み、`Update()`（メインスレッド）でドレインして `/rr/...` を
    自前ルーティング**（`OscDataHandle` はコールバック内でのみ有効な共有バッファのため float を即取り出す）。
    **Unity 同梱 Roslyn（csc）で Assembly-CSharp を全 define 付きコンパイル＝0 エラー確認済み・実機
    （MIDI コントローラ/OSC 送信元）検証は未**。シーンへの `MidiControl`/`OscControl` 配置はユーザー側。

## 作業上の注意

- 最新・最強の Claude モデルを前提に開発（モデル ID は claude-api スキル参照）。
- リアルタイム 60fps 維持が目標。GC スパイク（毎フレーム new/LINQ）を避ける。
- 全処理は RenderTexture（GPU）上で完結。CV へ渡すのは縮小フレームのみ。
