# M0 動作確認手順（タスク #4）

> M0 の関門。**「Console エラー0 ＋ 実際に映像が出る/受かる」**をゴールに、軽い順で確認する。
> パッケージ(#1〜3)は Scoped Registry 経由で導入済み（`RewriteReality/Packages/manifest.json`）。
> 各 Klak 系はサンプルシーン同梱なので、それを使うのが最短。

## チェックリスト

- [ ] 1. VideoPlayer（ソース動画 → RT）
- [ ] 2. WebCamTexture（カメラ入力・mac カメラ許可）
- [ ] 3. KlakSyphon（出力・mac 専用）
- [ ] 4. KlakNDI（出力・ネットワーク）

4つすべて「エラー0 ＋ 映像が出る/受かる」なら **#4 完了 → M0 突破（go）**。

---

## 準備（共通）
1. テスト用シーンを新規作成（`Assets/_Test/M0.unity` など）。
2. `Assets/_Test/` に **RenderTexture** を1枚作成（右クリック → Create → Render Texture、1920×1080）。出力テストの共通ソースにする。
3. `Window → Console` を開いて常時監視（赤エラーが出たら即対応）。

---

## 1. VideoPlayer（ソース動画 → RT）
1. 短い mp4 を1本 `Assets/_Test/` に置く。
2. 空の GameObject → Inspector で **Add Component → Video Player**。
3. `Source = Video Clip`（置いた mp4 を割当）、`Render Mode = Render Texture`、`Target Texture = 作った RT`。
4. RT を見るために Hierarchy に **UI → Raw Image** を作り `Texture = RT` を割当。
5. ▶ Play → 動画が Game ビューに出ればOK。

✅ 再生される / Console エラーなし

---

## 2. WebCamTexture（カメラ入力）
1. `Edit → Project Settings → Player → Other Settings` の **Camera Usage Description** に一文入れる
   （例: `Live camera input for VJ`）。macOS の権限ダイアログに必要。
2. 下の最小スクリプトを GameObject に付け、別の Raw Image に表示。

```csharp
using UnityEngine;
using UnityEngine.UI;

public class WebCamTest : MonoBehaviour {
    public RawImage target;
    void Start() {
        var cam = new WebCamTexture();
        target.texture = cam;
        cam.Play();
    }
}
```
3. ▶ Play → macOS のカメラ許可ダイアログで**許可** → カメラ映像が出ればOK。

✅ 許可後に映像が出る / arm64 Editor でクラッシュしない

---

## 3. KlakSyphon（出力・mac 専用）
1. Package Manager で **KlakSyphon** を選択 → **Samples** タブ → サンプルを **Import**。
2. インポートされたサンプルシーンを開いて ▶ Play。
3. 受け側として **別の Syphon 対応アプリ**で受信確認（OBS＋Syphon など）。
   サンプルが「Syphon サーバを立てる」内容なので、受け側に映像が来ればOK。

✅ Syphon サーバ名が受け側に見える / 映像が届く

---

## 4. KlakNDI（出力・ネットワーク）
1. Package Manager で **KlakNDI** → **Samples** → Import。
2. サンプルの **NDI Sender** シーンを ▶ Play。
3. 受け側で確認（いずれか）:
   - **OBS + distroav**（brew 導入済み）でソース追加 → NDI ソースに出てくるか。
   - または NDI 公式の受信ツール。
4. 余裕があれば **NDI Receiver** サンプルも別途確認。

✅ 受け側の NDI ソース一覧に出る / 映像が受かる
⚠️ ここが「KlakNDI 同梱ランタイムが arm64 で動くか」の実機確認ポイント（メモリ: local-toolchain-and-ndi）。

---

## 仕上げ
- 4つすべて OK なら **#4 完了 → go**。次は **#5 C# スケルトン生成**（`docs/01` 構成）。
- 詰まったら **Console のエラー全文**を控える/共有 → 原因を切り分け。
- 特に **3・4（Syphon/NDI）が arm64 の関門**。ここを抜ければ M0 はほぼ突破。
