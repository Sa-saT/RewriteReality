# 02. 技術スタック・開発環境（Unity）

## ベース

- **Unity 6 LTS**（無料 Personal ライセンス）/ **URP**（Universal Render Pipeline）
- 言語: **C#**
- **macOS / Apple Silicon**（このマシンは Darwin arm64。Metal バックエンド）
- 出力: **ネイティブ単体アプリ（.app）** としてビルド（Web 非依存）

> URP を選ぶ理由: Blit ベースのカスタムエフェクト・Shader Graph・VFX Graph・Fullscreen
> シェーダが扱いやすく、Metal で軽快。HDRP は重く今回はオーバースペック。

## セキュリティパッチ（Unity Runtime 脆弱性・2026-07 告知）

Unity から Runtime の脆弱性が告知されている
（[Unity Platform Protection – Take Immediate Action](https://discussions.unity.com/t/unity-platform-protection-take-immediate-action-to-protect-your-games-and-apps/1688031)）。
Editor 側の修正ではなく **ビルド成果物（.app）に対する後処理パッチ**のため、スタンドアロン
ビルドのたびに適用が要る。
- パッチ本体「**Unity Patcher 1.3.3 macOS**」はダウンロード済み。
- 適用タイミング＝**ビルド後**（.app 生成後にパッチを当てる）。配布/実機テストの前に必須。
- 既存のビルド確認フロー（`docs/M0-test-procedure.md`・#23 ビルド準備）に、この後処理ステップを
  組み込むこと。

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
| **KlakSpout** | `https://github.com/keijiro/KlakSpout.git` | Spout(**Win 専用・本プロジェクト対象外**。将来 Win 対応時のみ) |
| **KlakHap** | `https://github.com/keijiro/KlakHap.git` | HAP 動画再生（**任意・初期は通常 mp4＝VideoPlayer で代替**。M8 で検討） |
| **Minis** | `https://github.com/keijiro/Minis.git` | MIDI 入力（新 Input System） |
| **OscJack** | `https://github.com/keijiro/OscJack.git` | OSC 送受信 |

> ⚠️ Klak 各パッケージの **Unity 6 / Apple Silicon 対応**はバージョンで差がある。
> 導入直後に最小サンプルで動作確認するのが最初の関門（`08` M0）。

### CV（無料・要セットアップ）

> 🟢 **初期構成（方式C＝四隅オフライン・ベイク）では、この節は丸ごと不要。**
> アプリ内に OpenCvSharp を入れないので arm64 ネイティブの課題も発生しない（`01`/`03`/`12`）。
> 以下は将来 `LiveCvCornerSource`（ライブ実景へのリアルタイム合成）に進む時だけ着手する。

| パッケージ | 用途 | 注意 |
|---|---|---|
| **OpenCvSharp4**（shimat, BSD） | ArUco マーカー検出・findHomography | ネイティブ lib を Apple Silicon で通す必要 |

> ⚠️ **2026-06 実地調査の結論（重要・方針更新）**:
> shimat 本家の **公式 NuGet ネイティブランタイムには macOS arm64 が無い**
> （公式は Windows x64/arm64・Linux x64/arm64・WASM のみ）。
> NuGet の `OpenCvSharp4.runtime.osx_arm64` は **第三者(grinay)製の `4.8.1-rc`(2023-11)**
> で停滞気味、かつ **ArUco(contrib) 同梱が不明**。
> → **「NuGet ランタイムを入れて終わり」は当てにできない。自前ビルドを正本に据える。**
> 詳細は `12-feasibility-audit-2026-06.md`。

OpenCvSharp 導入ルート（**推奨順を更新**）:
1. **自前ビルド（本命・推奨）**: macOS で OpenCV ＋ **opencv_contrib（aruco 必須）** ＋
   OpenCvSharpExtern を **arm64** で cmake ビルド → `.dylib` を `Assets/Plugins/` に配置、
   Inspector で arm64 / macOS スタンドアロンに設定。管理層は NuGet の `OpenCvSharp4`（最新 4.13 系）。
2. 第三者 `runtime.osx_arm64` を試す（**先に aruco 同梱を検証**。当たれば最速、外れたら無駄足）。
3. 最終手段: **Intel ビルド＋Rosetta**（性能低下。リアルタイム VJ には不利）。

> ArUco は OpenCV の contrib モジュール。`OpenCvSharp.Aruco` 名前空間で使う
> （OpenCV 4.7+ で detector-class API に移行済み）。
> 詳細手順とフォールバックは `03-tracking-compositing.md`、調査根拠は `12`。

## ローカルツールチェーン（mac / Homebrew・確認済み 2026-06-23）

このマシンに **brew で導入済み**で、本プロジェクトに効くもの（`brew list` 確認）:

| ツール | 用途 | 備考 |
|---|---|---|
| **ffmpeg** | ①ソース動画を mp4/H.264/**1080p/60fps** にエンコード・変換 | 将来 HAP/.mov 化(M8)も ffmpeg |
| **python@3.12–3.14 / pyenv** | ②方式C（四隅ベイク）スクリプト実行の土台 | **venv で構築予定**（下記） |
| **numpy** | ベイク/画像処理の数値計算 | OpenCV と併用 |
| **libndi** | NDI **ランタイム本体**（受信/動作確認・OBS 等の外側用） | **Unity 送出は KlakNDI 同梱で別層**。下注記参照 |
| **distroav** | OBS 用 NDI プラグイン（受け側テストに有用） | 任意 |

### NDI は「層が違う」— libndi と KlakNDI の関係（混同しない）
- **Unity からの NDI 送受信は `KlakNDI`**（Unity package）を使う。KlakNDI は **NDI ランタイムを同梱**するため、
  **Unity 側だけなら brew の `libndi` は必須ではない**（`06` 参照）。
- brew の **`libndi` は Unity の外側**（OBS/distroav・NDI Tools での受信確認）で使う**受け側/確認用**ランタイム。
- 結論: **Unity=KlakNDI、確認/受け側=libndi。両立する（競合しない）**。arm64 実機確認は M6。

### Python ベイク環境は venv で構築（方式C / M3）
- システム/brew の Python を汚さず、**使い捨て venv** に OpenCV(contrib) を入れる。実際の着手は M3。
```
python3.12 -m venv ~/.venvs/rr-bake
~/.venvs/rr-bake/bin/pip install opencv-contrib-python numpy
# → 四隅ベイクスクリプトで track.json（fps=60, ソース動画と一致）を出力（03 / 11-M3）
```
> ⚠️ この OpenCV は **ベイク（オフライン）専用**で Python 側。Unity 内の OpenCvSharp/arm64（将来 `LiveCvCornerSource`）とは**別物**。

## 開発環境セットアップ手順

```
1. Unity Hub をインストール → Unity 6 LTS（Apple Silicon 版）を入れる
2. 新規プロジェクトを URP テンプレートで作成（出力先: RewriteRealityProject/RewriteReality/, `11` B6）
3. Package Manager:
   - Visual Effect Graph / Shader Graph / Input System を Add
   - Klak 各パッケージを git URL で Add（初期は Syphon/NDI/Minis/OscJack。Hap/Spout は任意）
4. 各機能の最小動作確認（VideoPlayer / WebCamTexture / Syphon / NDI）
5. Player Settings: Metal, Mac standalone, fullscreen 設定を整える
   # 将来 LiveCvCornerSource に進む時のみ: OpenCvSharp を導入し native(.dylib, arm64) を Plugins に配置・設定
```

## ディレクトリ構成（提案）

```
RewriteRealityProject/        # 本リポジトリ
├── CLAUDE.md
├── docs/                 # 本設計ドキュメント
├── RewriteReality/       # Unity プロジェクト（`11` B6 の推奨ネスト）
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── Sources/   # SourceVideo, SourceCamera
│   │   │   ├── Tracking/  # ICornerSource / BakedCornerSource（将来 LiveCvCornerSource）
│   │   │   ├── Compositing/ # Compositor
│   │   │   ├── Effects/   # EffectChain + 各 Effect
│   │   │   ├── Audio/     # AudioAnalyzer
│   │   │   ├── Output/    # OutputManager
│   │   │   └── Control/   # ControlHub, Preset(SO)
│   │   ├── Shaders/       # .shader / Shader Graph / VFX Graph
│   │   ├── Plugins/       # （将来）OpenCvSharp native (.dylib, arm64)
│   │   ├── Settings/      # URP Asset, Renderer
│   │   └── StreamingAssets/
│   │       ├── videos/    # ベース動画 (mp4 / HAP)
│   │       ├── tracks/    # ベイクした四隅 track.json（方式C）
│   │       └── markers/   # ArUco マーカー画像（将来の実行時CV用）
│   └── ProjectSettings/
└── assets/               # 素材の元データ
```

## ライセンスの整理（無料の根拠）

- **Unity Personal**: 直近12ヶ月の売上/調達が **$20万未満**なら無料で商用利用可
- **Klak 各種**: OSS（多くは Unlicense/MIT 系）— 無料
- **OpenCvSharp**: BSD — 無料（同梱する OpenCV も Apache-2.0）
- 有料アセットは不使用

> 規模が大きくなり Unity Personal の条件を超える場合のみ有料化を検討。
