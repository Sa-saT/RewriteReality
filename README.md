# RewriteReality

リアルタイム・カメラ埋め込み VJ アプリ。事前に用意したベース動画の「指定した箇所」
（マーカー/トラッキングで追従する領域）に、ライブカメラ映像をリアルタイム合成し、
グリッチ等のエフェクトをかけて、**フルスクリーン / プロジェクター＋Syphon / NDI** へ同時出力する。

- **環境**: Unity 6 LTS（`6000.0.33f1`）/ URP / macOS Apple Silicon（Metal）
- **構成**: リポジトリ直下に `docs/`（設計）・`DESIGN.md`（UI）・`RewriteReality/`（Unity 本体）
- 詳細な設計は [`docs/`](docs/) を参照（全体像は [`docs/00-overview.md`](docs/00-overview.md)）。

## 出力経路

最終映像（`finalRT`）を `OutputManager` が **フルスクリーン → Syphon → NDI** の順に同時配信する。

| 出力 | 用途 | 受信側の例 |
|---|---|---|
| フルスクリーン / プロジェクター | 本番投影 | ディスプレイ／プロジェクタ直結 |
| **Syphon**（mac 専用） | 同一 Mac 内の他アプリへ低遅延で渡す | OBS / Resolume / MadMapper など |
| **NDI**（ネットワーク） | LAN 経由で別マシン／別アプリへ | OBS（DistroAV）/ NDI Tools など |

> Syphon サーバ名・NDI 名は、シーン内 `Output` オブジェクトの
> **`Syphon Server` の `Server Name`** ／ **`NDI Sender` の `NDI Name`** で決まる（既定値: `RewriteReality`）。
> `OutputManager` が実行時に `Capture Method = Texture` と `finalRT` を自動設定するので、
> これらの欄は触らなくてよい。

---

## OBS で受信する

Unity 側を ▶ Play した状態で、OBS のソースとして取り込む。

### 前提
- OBS Studio（macOS 版）
- **NDI を使う場合のみ**: OBS の NDI プラグイン **DistroAV**（旧 obs-ndi）を導入しておく
  （Homebrew 等で導入済みの想定。未導入なら [DistroAV](https://github.com/DistroAV/DistroAV) を入れる）

### A. Syphon で受信（同一 Mac・低遅延・推奨）
1. OBS で「ソース」パネルの **`＋`** をクリック。
2. **`Syphon Client`**（macOS 版 OBS に標準搭載）を選択 → 名前を付けて OK。
3. プロパティの **`Source`（Syphon サーバ一覧）** から **`RewriteReality`** を選択。
4. プレビューに Unity の最終映像が出れば成功。

> `Syphon Client` がソース一覧に出ない場合、その OBS ビルドが Syphon 非対応の可能性。
> その場合は B（NDI）を使うか、Syphon 対応のビューア／ビルドを使う。

### B. NDI で受信（ネットワーク・別マシンも可）
1. OBS で「ソース」パネルの **`＋`** をクリック。
2. **`NDI™ Source`**（DistroAV）を選択 → 名前を付けて OK。
3. プロパティの **`Source name`** 一覧から **`RewriteReality`**（= NDI Name）を選択。
4. 必要に応じて以下を調整:
   - **Bandwidth**: `Highest`（画質優先）／低遅延が欲しければ下げる
   - **Latency**: `Low`（VJ 用途は低遅延推奨）
   - **Sync**: 映像優先なら `Source Timing`
5. プレビューに映像が出れば成功。

> 別マシンで受ける場合は、送信機と受信機が **同一 LAN（同一サブネット）** にあること。
> 出てこない時はファイアウォールで OBS / Unity のネットワークアクセスを許可する。

### うまく出ない時のチェック
- Unity が **▶ Play 中**か（停止中は配信されない）。
- `Output` オブジェクトに **`Syphon Server` / `NDI Sender`** が付き、`OutputManager` に割り当て済みか。
- 受信側の **Source 名が `RewriteReality`** と一致しているか（名前を変えたなら新しい名前を選ぶ）。
- NDI が一覧に出ない → DistroAV 未導入／別サブネット／ファイアウォール遮断を疑う。

---

## 動作確認（M0）

VideoPlayer / WebCamTexture / Syphon / NDI の最小確認手順は
[`docs/M0-test-procedure.md`](docs/M0-test-procedure.md) を参照（Apple Silicon arm64 で確認済み）。

## ライセンス / 方針

有料アセットは使わない方針（すべて無料・ネイティブ）。土台選定の経緯は
[`docs/09-platform-comparison.md`](docs/09-platform-comparison.md)、
実装ロードマップは [`docs/08-roadmap.md`](docs/08-roadmap.md) を参照。
