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
  - **ワークフロー＝UI Builder 主体**（見た目=UXML/USS／挙動=薄い C#。行も UXML テンプレ化）。
  - **⚠ 見た目・レイアウトはユーザーが UI Builder で自身で作成・確定する**。agent は UXML/USS の足場と
    新コントロール用テンプレ＋バインドを用意する役で、**ビジュアルの作り込みはユーザーに委ねる**。
    **デザイン確定（タスク#24）後に #21→#22→#18（領域マッピングUI）へ着手**。詳細＝`docs/07-control-ui.md`。
  - **デザインブリーフ＝`docs/07b-operator-ui-brief.md`**（#24 用・Claude Design に貼る一枚仕様・MadMapper 参照）。
  - **Claude Design 連携**（見た目の前段ツール・`claude.ai/design`）: 使い方・扱い・合意フローは
    **`docs/07-control-ui.md`「見た目の前段に Claude Design を使う」**＋**`docs/07b` §0/§7**（貼付プロンプト雛形）が正本。
    運用＝**大改修は Claude Design で生成→UXML/USS へ移植／微調整は Unity 直**。DesignSync MCP（`/design-login`）で
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
  - **次の一手（マッピング品質・決定 2026-07-03）**: **#34 Grid/Bezier モード**（Project 2×2 を廃止し「歪ませる面」を
    Bezier グリッドに一本化＝MadMapper GridGenerator 手本・テストパターン校正→カメラ差替）＋**#35 OUTPUT グリッド校正**
    （格子オーバーレイ＋投影キャリブレーション表示）。故意の局所歪みは **#33**（パペットpin エフェクト）。詳細＝`docs/03`・`06`。

## 作業上の注意

- 最新・最強の Claude モデルを前提に開発（モデル ID は claude-api スキル参照）。
- リアルタイム 60fps 維持が目標。GC スパイク（毎フレーム new/LINQ）を避ける。
- 全処理は RenderTexture（GPU）上で完結。CV へ渡すのは縮小フレームのみ。
