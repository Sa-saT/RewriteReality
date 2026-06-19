# RewriteReality

リアルタイム・カメラ埋め込み VJ アプリ。事前に用意したベース動画の「指定した箇所」
（マーカー/トラッキングで追従する領域）に、ライブカメラ映像をリアルタイム合成し、
グリッチ等のエフェクトをかけて、フルスクリーン/プロジェクター＋Syphon/NDI へ同時出力する。

思想的ベース: Rhizomatiks "Terminal Slam" (Squarepusher)
https://research.rhizomatiks.com/s/works/squarepusher/en.html

## リポジトリ

- GitHub: `git@github.com:Sa-saT/RewriteReality.git`
- ローカル: `~/Documents/Unity/RewriteRealityProject/`
- （Obsidian Vault から本リポジトリへ移行済み）

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

## 現在の状況 / 次の一手

- docs・設計は確定。リポジトリ移行済み。
- **Unity プロジェクト本体（Assets/Packages/ProjectSettings）は未作成**。
  → 次は `docs/08` の **M0**: Unity Hub で URP / `6000.0.33f1` のプロジェクトを
  本フォルダに作成し、Klak 等のパッケージ投入と **OpenCvSharp の arm64 動作確認**（最大の関門）。
- 同じ親フォルダにある `My project`(HDRP/2022.3) と `Rcam3` は**本プロジェクトとは別物**。

## 作業上の注意

- 最新・最強の Claude モデルを前提に開発（モデル ID は claude-api スキル参照）。
- リアルタイム 60fps 維持が目標。GC スパイク（毎フレーム new/LINQ）を避ける。
- 全処理は RenderTexture（GPU）上で完結。CV へ渡すのは縮小フレームのみ。
