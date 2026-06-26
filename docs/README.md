# docs — 設計ドキュメント索引

RewriteReality（リアルタイム・カメラ埋め込み VJ アプリ／Unity 6・URP・macOS Apple Silicon）の
設計ドキュメント一覧。全体像はまず [`00-overview.md`](00-overview.md)、実装の進め方は
[`08-roadmap.md`](08-roadmap.md) を参照。プロジェクト方針の最新状況はリポジトリ直下の `CLAUDE.md`。

## 設計ドキュメント（00–12）

| ファイル | 内容 |
|---|---|
| [00-overview.md](00-overview.md) | 全体像・無料構成・方針 |
| [01-architecture.md](01-architecture.md) | C# モジュール設計・データフロー・`EffectBase` 拡張基盤 |
| [02-tech-stack.md](02-tech-stack.md) | Unity / パッケージ / 環境セットアップ |
| [03-tracking-compositing.md](03-tracking-compositing.md) | ArUco・ホモグラフィ・射影合成（4点＋多pin メッシュワープ） |
| [04-effects.md](04-effects.md) | URP エフェクトパイプライン |
| [05-audio-reactive.md](05-audio-reactive.md) | FFT / ビート → エフェクト連動 |
| [06-output.md](06-output.md) | フルスクリーン / Syphon / NDI 出力・**出力品質の設計** |
| [07-control-ui.md](07-control-ui.md) | GUI / MIDI / OSC・プリセット・**操作UI方針（UI Toolkit 本命）** |
| [08-roadmap.md](08-roadmap.md) | 実装ステップ（M0〜M8） |
| [09-platform-comparison.md](09-platform-comparison.md) | 土台選定の経緯（業界マップ） |
| [10-openframeworks-alternative.md](10-openframeworks-alternative.md) | oF 代替設計（参考） |
| [11-todo-and-decisions.md](11-todo-and-decisions.md) | 実装タスク（M別）＋確定した選定①〜⑩ |
| [12-feasibility-audit-2026-06.md](12-feasibility-audit-2026-06.md) | 実現可能性監査（OpenCvSharp / arm64・方式C） |

## 実機・運用手順

| ファイル | 内容 |
|---|---|
| [M0-test-procedure.md](M0-test-procedure.md) | M0 動作確認手順（VideoPlayer / WebCamTexture / Syphon / NDI・実機メモ） |

## 関連（リポジトリ直下）

| ファイル | 内容 |
|---|---|
| [`../DESIGN.md`](../DESIGN.md) | オペレータUIデザインシステム・トークン（`07` と対） |
| [`../README.md`](../README.md) | プロジェクト概要・OBS での Syphon/NDI 受信手順 |
| [`../CLAUDE.md`](../CLAUDE.md) | 構成・現在の状況・次の一手（最新状況の正本） |
