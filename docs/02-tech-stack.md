# 02. 技術スタック・開発環境（Unity）

## ベース

- **Unity 6 LTS**（無料 Personal ライセンス）/ **URP**（Universal Render Pipeline）
- 言語: **C#**
- **macOS / Apple Silicon**（このマシンは Darwin arm64。Metal バックエンド）
- 出力: **ネイティブ単体アプリ（.app）** としてビルド（Web 非依存）

> URP を選ぶ理由: Blit ベースのカスタムエフェクト・Shader Graph・VFX Graph・Fullscreen
> シェーダが扱いやすく、Metal で軽快。HDRP は重く今回はオーバースペック。

## 導入パッケージ（すべて無料）

### Unity 公式（Package Manager から）
| パッケージ | 用途 |
|---|---|
| Universal RP | レンダリング基盤 |
| Visual Effect Graph | GPU パーティクル |
| Shader Graph | ノードでエフェクトシェーダ作成 |
| Input System | 入力（Minis が依存） |

### Keijiro Takahashi 製 OSS（現役保守・無料）
Package Manager の **Add package from git URL** で導入。

| パッケージ | git URL（例） | 用途 |
|---|---|---|
| **KlakSyphon** | `https://github.com/keijiro/KlakSyphon.git` | Syphon 入出力(mac) |
| **KlakNDI** | `https://github.com/keijiro/KlakNDI.git` | NDI 入出力 |
| **KlakSpout** | `https://github.com/keijiro/KlakSpout.git` | Spout(Win, 任意) |
| **KlakHap** | `https://github.com/keijiro/KlakHap.git` | HAP 動画再生 |
| **Minis** | `https://github.com/keijiro/Minis.git` | MIDI 入力（新 Input System） |
| **OscJack** | `https://github.com/keijiro/OscJack.git` | OSC 送受信 |

> ⚠️ Klak 各パッケージの **Unity 6 / Apple Silicon 対応**はバージョンで差がある。
> 導入直後に最小サンプルで動作確認するのが最初の関門（`08` M0）。

### CV（無料・要セットアップ）
| パッケージ | 用途 | 注意 |
|---|---|---|
| **OpenCvSharp4**（shimat, BSD） | ArUco マーカー検出・findHomography | ネイティブ lib を Apple Silicon で通す必要 |

OpenCvSharp 導入ルート（いずれか）:
1. **NuGetForUnity** で `OpenCvSharp4` ＋ ランタイム（`OpenCvSharp4.runtime.osx` 系）を取得し、
   `.dylib` を `Assets/Plugins/` に配置、arm64 用に Import 設定。
2. ランタイムに arm64 が無い場合は **自前で OpenCV(contrib 込み)＋OpenCvSharp ネイティブをビルド**。
3. 最終手段: **Intel ビルド＋Rosetta** で動かす（性能は落ちる）。

> ArUco は OpenCV の contrib モジュール。`OpenCvSharp.Aruco` 名前空間で使う。
> 詳細手順とフォールバックは `03-tracking-compositing.md`。

## 開発環境セットアップ手順

```
1. Unity Hub をインストール → Unity 6 LTS（Apple Silicon 版）を入れる
2. 新規プロジェクトを URP テンプレートで作成（出力先: vfx/app/）
3. Package Manager:
   - Visual Effect Graph / Shader Graph / Input System を Add
   - Klak 各パッケージを git URL で Add
4. OpenCvSharp を NuGetForUnity 等で導入、Plugins に native を配置、arm64 設定
5. 各機能の最小動作確認（VideoPlayer / WebCamTexture / Syphon / NDI / OpenCvSharp）
6. Player Settings: Metal, Mac standalone, fullscreen 設定を整える
```

## ディレクトリ構成（提案）

```
vfx/
├── CLAUDE.md
├── docs/                 # 本設計ドキュメント
├── app/                  # Unity プロジェクト
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── Sources/   # SourceVideo, SourceCamera
│   │   │   ├── Tracking/  # Tracker
│   │   │   ├── Compositing/ # Compositor
│   │   │   ├── Effects/   # EffectChain + 各 Effect
│   │   │   ├── Audio/     # AudioAnalyzer
│   │   │   ├── Output/    # OutputManager
│   │   │   └── Control/   # ControlHub, Preset(SO)
│   │   ├── Shaders/       # .shader / Shader Graph / VFX Graph
│   │   ├── Plugins/       # OpenCvSharp native (.dylib, arm64)
│   │   ├── Settings/      # URP Asset, Renderer
│   │   └── StreamingAssets/
│   │       ├── videos/    # ベース動画 (mp4 / HAP)
│   │       └── markers/   # ArUco マーカー画像
│   └── ProjectSettings/
└── assets/               # 素材の元データ
```

## ライセンスの整理（無料の根拠）

- **Unity Personal**: 直近12ヶ月の売上/調達が **$20万未満**なら無料で商用利用可
- **Klak 各種**: OSS（多くは Unlicense/MIT 系）— 無料
- **OpenCvSharp**: BSD — 無料（同梱する OpenCV も Apache-2.0）
- 有料アセットは不使用

> 規模が大きくなり Unity Personal の条件を超える場合のみ有料化を検討。
