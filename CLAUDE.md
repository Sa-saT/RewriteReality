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
  → **次回ここから**: **Compositor のコーナーピン合成**（カメラを四隅へ射影合成・Task#5）
  → **初期エフェクトのシェーダ**を1つ実装（RGBShift か ColorGrade・Task#6）。
  以降の関門は **OpenCvSharp の arm64 ビルド**（go/no-go・`docs/12`・方式C採用でM0対象外）。
- 次の一手（`docs/08` M0 の続き）:
  1. **Compositor コーナーピン合成**（_warpMaterial+四隅クアッドで camTex を sceneRT へ）← 次回ここから
  2. **初期エフェクトのシェーダ**実装（まず1種）→ 残り3種
  3. **OpenCvSharp の arm64 動作確認**（将来 LiveCv 時の関門・go/no-go）
     → 公式 NuGet に macOS arm64 ネイティブは無い。**contrib(aruco)込みの自前ビルドが本命**（`docs/12`）
  3. C# スケルトン生成（`docs/01` のモジュール構成）
- 同じ親フォルダにある `My project`(HDRP/2022.3) は**本プロジェクトとは別物**。
- `Rcam3`（Keijiro, iPhone LiDAR→NDI 深度 VFX）も別プロジェクトだが、**将来の深度レイヤー(M9)の参照実装**
  として流用予定（`docs/04`・`08`・`11`・`12`）。新規依存は実質ゼロ（KlakNDI を流用）。
- **操作UI = UI Toolkit（#20 土台 完了・2026-06-28）**: `Assets/UI/`（RewriteReality.uss=DESIGN.md
  トークン→USS／OperatorShell.uxml／FxRow・ParamRow.uxml 行テンプレ）＋ `OperatorUI.cs`
  （ControlHub/EffectParameter 双方向バインド・preview=EffectChain.FinalTexture・FPS）。配線=
  PanelSettings(Scale Mode=**Constant Pixel Size**)→UIDocument(Source=OperatorShell.uxml)→OperatorUI。
  IMGUI 版 OperatorGui は併存。
  - **ワークフロー＝UI Builder 主体**（見た目=UXML/USS／挙動=薄い C#。行も UXML テンプレ化）。
  - **⚠ 見た目・レイアウトはユーザーが UI Builder で自身で作成・確定する**。agent は UXML/USS の足場と
    新コントロール用テンプレ＋バインドを用意する役で、**ビジュアルの作り込みはユーザーに委ねる**。
    **デザイン確定（タスク#24）後に #21→#22→#18（領域マッピングUI）へ着手**。詳細＝`docs/07-control-ui.md`。

## 作業上の注意

- 最新・最強の Claude モデルを前提に開発（モデル ID は claude-api スキル参照）。
- リアルタイム 60fps 維持が目標。GC スパイク（毎フレーム new/LINQ）を避ける。
- 全処理は RenderTexture（GPU）上で完結。CV へ渡すのは縮小フレームのみ。
