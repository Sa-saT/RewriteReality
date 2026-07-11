# ClaudeDesign ↔ Unity 同期（design-sync）

**正本**: claude.ai/design「RewriteReality Design System」`ui_kits/operator/`
（HTML モック `index.html` ＝見た目・挙動の正／`UNITY-HANDOFF.md` ＝仕様の文章正本）

**このフォルダ**: リモートの**ミラー（スナップショット）**。
「最後に Unity へ反映した時点」のリモート内容を保持し、次回同期の差分検出基準にする。
**手で編集しない**（リモートから取得した内容のみを置く）。

- 最終同期: 2026-07-11（remote updatedAt 2026-07-10T00:39Z 時点の内容）

## 同期手順（Claude Code が行う）

1. `DesignSync list_projects` で `updatedAt` を確認（上の「最終同期」より新しければ差分あり）
2. `ui_kits/operator/` の各ファイルを `get_file` で取得し、**このミラーと diff**
3. 差分を UXML/USS/C# へ反映（**App.jsx＝シェル全体を必ず照合**。.md に書かれない変更が
   jsx 側にだけ入ることがある — 例: 2026-07-10 の KEY 割当は Timeline.jsx が先行）
4. ミラーを取得内容で更新し、**実装と同じコミット**に含める（コミット＝ここまで反映済みの印）
5. 反映しない/できない差分は**タスク化して報告**（黙って落とさない）

## 運用ルール（ユーザー側）

- 見た目・UX の変更は **claude.ai/design で行う**（Unity 側の UXML/USS を直接いじって分岐
  させない。微細な px/整列の詰めのみ UI Builder 可＝docs/07 の確定ワークフロー）
- 大きめの変更をしたら、ClaudeDesign に **UNITY-HANDOFF.md へ日付付きの追記**もさせる
  （「§n に YYYY-MM-DD 付きで差分を追記して」と頼む）— .md が差分の一次情報になる
- 変更したら Claude Code に「**ClaudeDesign 更新した**」と一言（それで再同期が走る）
