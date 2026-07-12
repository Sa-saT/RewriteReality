# UI 実装ノート（UXML/USS + C# への移植規約・落とし穴）

ClaudeDesign モック（`docs/design-sync/operator/*.jsx`）を Unity UI Toolkit へ移植するときの
**必読ガイド**。ここに書かれた罠は全部実際に踏んだもの。タスク実装前に読むこと。

## 移植規約

- **見た目=UXML/USS・挙動=薄い C#**。構造・スタイルを C# で組まない（動的リストの行生成は例外）。
- **`name` 属性は C# の `Q<>()` キー＝契約**。既存 name は変えない。新規は `rr-` プレフィクス。
- 色・寸法は **USS 変数（`--rr-*`）** を使う。生の hex を USS に直書きしない（トークンは
  `RewriteReality.uss` 冒頭の `:root` 定義を参照）。
- モックの lucide アイコンは `RrIcon`（painter2D）で代替。無い形状は `RrIcon.Kind` に追加して描く
  （絵文字・画像アセットは使わない）。
- 動的リスト（FX 行・Surface 行・タブ等）は C# で行を生成し、**クラス名は静的 UXML の行と同じ**にする。

## UI Toolkit の罠（実際に踏んだもの）

1. **`color: inherit` は書けない** — USS の color にキーワードを与えると実行時に
   "Trying to read value of type Color while reading a value of type Keyword"。color は既定で継承される
   ので単に書かない。
2. **Button に `RegisterCallback<MouseDownEvent>` は発火しない** — Button の Clickable が PointerDown を
   消費するため。Button は **`clicked`** を使い、親要素への選択伝播を止めたいときは
   `RegisterCallback<PointerDownEvent>(e => e.StopPropagation())` を併用する。
3. **`worldBound` はパネル座標** — オーバーレイ配置は `_root.WorldToLocal()` で変換してから
   `style.left/top` に入れる（直代入だとズレて他パネルの裏に隠れる）。
4. **popover/menu は `_root` 直下に reparent して `BringToFront()`** — 兄弟要素の描画順・クリップで
   隠れるため（タイムライン内に置いた menu は本体の裏に潜る）。
5. **UXML 変更が画面に出ないことがある** — エディタの取りこぼし。要素取得に失敗したら
   `Debug.LogWarning` で「Assets/UI を Reimport」と出す自己診断を必ず入れる（既存例:
   タブ tablist / BEZIER ボタン）。
6. **`Awake` 順に依存しない** — 他コンポーネントの初期化（シード等）を UI 構築が前提にするときは
   `EnsureXxx()` を明示的に呼ぶ（例: `ShowTimeline.EnsureSeeded()`）。
7. **毎フレームの文字列生成禁止** — 表示更新は「値が変わった時だけ整形」（`_last*` 比較）。
   60fps 維持・GC スパイク回避（CLAUDE.md 方針）。
8. **USS は flex のみ**（CSS grid 非対応）。`::before` 等の擬似要素も無い（色ドットは子要素 or
   border-left で表現）。

## 実装後の必須手順

1. 波括弧バランス等の静的チェック（`tr -cd '{' | wc -c`）。
2. ユーザーに Unity での確認ポイントを**箇条書きで**提示（Reimport 必要性を明記）。
3. 確認 OK 後にコミット（1 タスク=1 コミット・ミラー更新があれば同コミット）。
4. 反映しない差分はタスク化して残す（黙って落とさない）。

## 参照

- 同期プロトコル: `docs/design-sync/README.md`
- モック（正）: `docs/design-sync/operator/`（App.jsx=シェル / Inspector.jsx=右ドック /
  Timeline.jsx=下部 / LeftDock.jsx=左ドック / CenterStage.jsx+MappingCanvas.jsx=中央）
- 仕様の文章正本: `docs/design-sync/operator/UNITY-HANDOFF.md`
