# #38 シーン（プリセット）操作手順 — Master / Fade to Black・Scene 保存/発火

実装＝`SceneState` / `Preset` / `SceneBank` / `MasterOut`（設計は `07-control-ui.md`「実装（#38）」）。
ここは**実機での操作手順とハマりどころ**の手順書（`M0-test-procedure.md` と同じ位置づけ）。

対象シーン: `Assets/Scenes/Main.unity`。検証日: 2026-09-22（B〜F は実機確認済み）。

---

## A. 事前確認（1 回だけ）

1. Unity で `RewriteReality` を開き、Console にコンパイルエラーが無いこと。
2. **Project Settings → Graphics → Always Included Shaders** に `Hidden/RewriteReality/MasterOut`
   が入っていること（コミット済み・追加作業は不要）。

## B. Master / Fade to Black（SceneBank 無しで確認できる）

1. `Main.unity` を Play。
2. 右 Inspector を**無選択**にすると `Master / Program` が出る。
3. **Fade to Black** を上げると出力・プレビューが黒へ、**Master** を下げると全体が暗くなる。
   - ここが効けば `Manager → MasterOut` の経路は OK（`Manager` の Control 欄は空でよい＝自動取得）。
   - OSC でも同じ: `python3 tools/control-test/osc_test.py /rr/fade 1`

## C. SceneBank を置く（**必ず Play を止めた状態で**）

1. Hierarchy の `Control`（または新規の空 GameObject）を選択。
2. **Add Component → Scene Bank**。`Hub` は空でよい（`ControlHub` を自動取得）。
3. `⌘S` でシーンを保存。

| 項目 | 既定 | 意味 |
|---|---|---|
| Scenes | 空 | シーン一覧（D で追加） |
| Auto Load On Start | OFF | 起動時に `scenes.json` を読む |
| Auto Save On Quit | OFF | 終了時に `scenes.json` へ保存 |
| File Name | `scenes.json` | 保存名（`Application.persistentDataPath` 配下） |
| Enable Triggers | ON | キー/パッド発火を受け付ける |
| Log Fire | ON | 発火を Console に出す（本番は OFF 推奨） |

> **⚠ Play 中に AddComponent しない**。Play 停止でコンポーネントごと消えるうえ、
> 以前は `OperatorUI` / `ControlHub` が Awake でしか参照を解決せず「UI に出ない」症状になった
> （現在は 1 秒おきの遅延検出で拾うが、設定が消える問題は残るため Edit モードで置く）。

## D. シーンを作る（Play 中）

1. Play → 作りたい見た目にする（FX の ON/OFF・パラメータ・Master・BPM・Speed）。
2. `Scene Bank` の **⋮ → `Add Scene From Current`** → 左ドック **Scenes** に行が出る。
3. Inspector の `Scenes → Element n` で編集（**変更は左ドックへ自動反映**）:
   - `Name` … 表示名
   - `Key` … Input System のキー名（`Digit1` `Q` `A` `Space` …）。**発火はここが正**
   - `Pad` … 0〜15（表示用。手入力では Key は自動設定されない）
   - `Fade In` / `Fade Out` … 秒
   - `Hold` … ON なら押下中だけ適用し、離すと直前の状態へ戻る
4. 手動で UI を更新したいときは ⋮ → `Notify UI (Scenes Changed)`。

## E. 発火

- 左ドック Scenes の**行クリックは選択のみ**（誤爆防止。発火しない）。
- 右 Inspector の **Fire**、または割り当てたキー押下 → `Fade Out 秒で黒 → 適用 → Fade In 秒で復帰`。
- **Fire / Save は押下後 1.2 秒だけラベルが `Fired` / `Saved` に変わり緑で点灯**（効いたことの確認用）。
  Console にも `[SceneBank] …` が出る。
- **Save は準備 Edit のみ**（本番 Live では disabled）。Fire は Live でも可。

## F. 保存は 2 系統ある（混同しやすい）

| | 保存先 | 操作 | 消えるタイミング |
|---|---|---|---|
| **シーン一覧の本体** | `Main.unity`（SceneBank のフィールド） | **Edit モード**で編集 → `⌘S` | シーン未保存なら戻る。**Play 中の編集は Play 停止で消える** |
| **`scenes.json`** | `Application.persistentDataPath`（macOS は `~/Library/Application Support/<Company>/<Product>/`） | ⋮ → `Save Scenes` / `Load Scenes` | 上書きするまで残る |

- Inspector の **`Save` ボタンはファイル保存ではない**。「いまの見た目をこのシーンへ上書き」。
- ファイルへ残すのは ⋮ → **`Save Scenes`**（Console に保存先パスと件数が出る）。
- **Load の確かめ方**: `Save Scenes` → Inspector で名前をわざと変える → `Load Scenes` →
  名前が戻り左ドックも更新されれば成功。
- 常用するなら `Auto Load On Start` / `Auto Save On Quit` を ON（**自動 Load は 2026-09-22 に実機確認済み**）。

## G. OSC から発火（任意・ハードウェア不要）

```bash
python3 -m venv tools/control-test/.venv
source tools/control-test/.venv/bin/activate
pip install -r tools/control-test/requirements.txt

python3 tools/control-test/osc_test.py /rr/scene 0          # 1 番目のシーンを発火
python3 tools/control-test/osc_test.py /rr/scene/intro 1    # 名前 slug で発火
python3 tools/control-test/osc_test.py /rr/fade 1           # 黒へ
```

受け口は `OscControl`（Port 既定 9000・`Log` を ON にすると受信が Console に出る）。

## H. 仕様メモ（覚えておく点）

- **Fade to Black はシーンに保存されない**。ライブの安全操作であり「見た目」ではないため、
  シーン復元で勝手に暗転しない。
- シーン復元は**エフェクトの名前**で突き合わせる。エフェクトを足しても既存シーンは壊れず、
  見つからない分は飛ばして適用される。
- 左ドック Scenes は「Play 開始時」「`ScenesChanged` 発火時」「一覧の指紋（件数・name・key・pad）の
  変化を検知したとき」に作り直される。
