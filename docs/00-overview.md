# 00. 概要 — リアルタイム・カメラ埋め込み VJ アプリ（Unity版）

## 作るもの

事前に用意した**ベース動画**の中の「指定した箇所」（マーカー/トラッキングで追従する領域）に、
**ライブカメラ映像**をリアルタイムで合成し、グリッチ等の**エフェクト**をかけて、
**フルスクリーン/プロジェクター**と **Syphon/NDI** へ同時出力するネイティブ VJ アプリ。

### 一言で言うと
> 「動く窓に、今この瞬間のカメラ映像を流し込み、加工して投影するライブ映像装置」

## 参照作品 — Rhizomatiks "Terminal Slam" (Squarepusher)

CLAUDE.md のリンク先。本アプリの思想的ベース。

- 街の映像の中の**広告領域を ML で検出・マスク**し、別映像で**置き換え**る
- 検出: YOLO（物体検出）/ YOLACT（セグメンテーション）/ DeepLab（深度）
- **グリッチエフェクト**を領域選択的に適用（人物の光学迷彩=プライバシー保護も兼ねる）
- **オーディオリアクティブ**: Spleeter で drums/bass を分離 → Max パッチ "Glitch Composer" で音と映像を同期

本アプリは、この「領域に別映像を流し込み、エフェクトをかける」中核を、
**リアルタイム・ライブカメラ入力**版として再構成したもの。

## 確定した方針

| 項目 | 選択 | 理由 |
|---|---|---|
| 技術スタック | **Unity 6 LTS（URP）** | 無料(Personal)・ネイティブ単体アプリ化・realtime 業界で広く使用・保守が厚い |
| 埋め込み方式 | **四隅トラッキング追従**（初期＝オフライン・ベイク） | 動く対象にカメラ映像を貼る。初期は事前ベイクした四隅(`track.json`)を読む（方式C, `03`）。将来ライブ実景は ArUco/特徴点を実行時検出 |
| 出力 | **フルスクリーン/プロジェクター ＋ Syphon/NDI** | ライブ投影と他 VJ ソフト連携を両立 |
| エフェクト | グリッチ/データモッシュ、オーディオリアクティブ、色調/フィードバック/歪み、パーティクル | URP の Blit/Shader Graph＋VFX Graph。拡張可能なチェーン基盤で後から追加可能に |

> **拡張方針（2026-06-30）**: コア（カメラ埋め込み VJ）の完成後、**タイムライン＋音声ミックスを持つ
> AV ショー・ツール**へ段階的に拡張する（出力変形／準備・本番 2 モード／song+short タイムライン／マルチトラック・音声）。
> 決定＝`11` B9、ロードマップ＝`08` M10〜M13、操作UI仕様＝`07b`。コアは無改修で土台の上に積む。

### なぜ Unity か（保守性・無料・ネイティブの3条件）

ユーザの懸念は「**メンテの止まった個人アドオンに依存したくない**」こと。
Unity では必要機能の大半が **Unity 本体（Unity 社が保守）** か
**Keijiro Takahashi 氏の現役メンテ済み OSS（Klak シリーズ）** で賄え、
しかもすべて**無料**・**Web 非依存のネイティブビルド**が可能。
→ 土台選定の経緯は `09-platform-comparison.md`、oF を軸にした代替案は `10-openframeworks-alternative.md`。

## 無料構成（依存とライセンス）

| 機能 | 実現手段 | 保守 | 無料 |
|---|---|---|---|
| エンジン | Unity 6 LTS（Personal） | Unity 社 | ◎（売上 $20万未満は無料） |
| 動画再生 | VideoPlayer ＋（HAP は **KlakHap**） | Unity / Keijiro | ◎ |
| ライブカメラ | WebCamTexture | Unity | ◎ |
| Syphon | **KlakSyphon** | Keijiro | ◎ OSS |
| NDI | **KlakNDI** | Keijiro | ◎ OSS |
| Spout(Win) | **KlakSpout** | Keijiro | ◎ OSS |
| パーティクル | **VFX Graph** | Unity | ◎ |
| エフェクト | URP Blit / Shader Graph / Fullscreen | Unity | ◎ |
| 音声FFT | AudioSource/Listener.GetSpectrumData | Unity | ◎ |
| MIDI | **Minis**（新 Input System） | Keijiro | ◎ OSS |
| OSC | **OscJack** | Keijiro | ◎ OSS |
| 四隅トラッキング（初期） | **オフライン・ベイク**（`track.json` を読むだけ） | 自前/Unity 標準 | ◎ |
| 〃（将来・ライブ実景） | **OpenCvSharp**（ArUco + findHomography） | shimat | ◎ OSS（BSD） |

> 有料アセット（OpenCV for Unity 等）は使わない。
> **初期は四隅をオフラインでベイクする方式C（`01`/`03`）を採り、アプリ内から OpenCvSharp を外す。**
> → 実行時依存は Unity＋Klak のみ（すべて arm64 対応確認済, `12`）。最難関だった
> 「OpenCvSharp ネイティブを Apple Silicon で通す」課題は**初期には発生しない**。
> ライブ実景へリアルタイム合成する将来要件が出た時だけ OpenCvSharp に取り組む（`02`/`03`/`12`）。

## ドキュメント構成

| ファイル | 内容 |
|---|---|
| `00-overview.md` | 本書。全体像と方針 |
| `01-architecture.md` | システム構成・データフロー・C# モジュール設計 |
| `02-tech-stack.md` | Unity / パッケージ / 開発環境セットアップ |
| `03-tracking-compositing.md` | ArUco 検出・ホモグラフィ・射影合成 |
| `04-effects.md` | URP エフェクトパイプライン・各エフェクト |
| `05-audio-reactive.md` | 音声解析（FFT/ビート）とエフェクト連動 |
| `06-output.md` | フルスクリーン / Syphon / NDI 出力 |
| `07-control-ui.md` | パラメータ制御（GUI / MIDI / OSC）・プリセット |
| `08-roadmap.md` | 実装ステップとマイルストン |
| `09-platform-comparison.md` | 土台選定の経緯（VFX/realtime 業界マップ） |
| `10-openframeworks-alternative.md` | oF を軸にした場合の代替設計（参考） |

## 実現可能性の結論

**実現可能。** I/O はすべて無料・現役保守のパッケージで揃う。
初期構成（方式C＝四隅オフライン・ベイク）では実行時依存が Unity＋Klak のみになり、
最難関だった **OpenCvSharp の Apple Silicon ネイティブ導入は発生しない**（`12`）。
残る難所は (1) **ベイクした四隅軌跡の品質**、(2) **60fps を維持する GPU/GC 最適化**の2点。
将来ライブ実景へリアルタイム合成する場合のみ (3) OpenCvSharp arm64 が再浮上する。対策は各章に記載。
