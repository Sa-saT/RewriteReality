# RewriteReality — UI Design System

> 方向性: **DaVinci Resolve 系のダーク・高密度プロツール**を基調に、**Cursor の editorial な抑制**
> （希少アクセント・温かいニュートラル・ヘアライン深度・タイムライン由来のパステル）を取り込むハイブリッド。
> 対象は **オペレータUI（操作画面）**であり、投影/Syphon/NDI へ出す本編映像ではない（`docs/07`）。
> 書式は提示された Cursor の DESIGN.md を踏襲。フォントは未ライセンス前提で **OSS 代替**を正本にする。

## Overview

RewriteReality は暗いライブ会場で、片手に MIDI・目線はプロジェクション、というモードで操作する。
だから UI は **暗所で眩しくない・一目で値が読める・密度が高いが散らからない** ことを最優先にする。
これは DaVinci Resolve / Fusion のような **制御卓（control surface）**の思想で、Cursor のマーケサイトの
明るい余白とは正反対だ。一方で Cursor から受け継ぐのは「**派手にしない**」という編集者的抑制——
ブランド電圧（アクセント）を**希少に**使い、ディスプレイ書体は **400 のまま太らせず**、奥行きは
**ヘアラインのみ**（ドロップシャドウ無し）。

基調は **暖色ダーク**: 純黒(#000)ではなく**温かい近黒** (`{colors.canvas}` — #161512)。テキストは
冷たい白ではなく**温かいオフホワイト** (`{colors.text}` — #ece9e0)。Cursor の「warm, not pure black」を
**ダーク側に反転**した形だ。

ブランド電圧は **Live Amber** (`{colors.primary}` — #ff5c1a)。Cursor Orange の系譜で、ダークでも沈まないよう
明度を上げてある。Primary CTA・**armed/live/record** といった「いま効いている」状態にだけ**希少に**使う。
DaVinci 由来の **選択ブルー** (`{colors.selection}` — #4a9ed8) は *システムの選択/フォーカス* 専用で、
ブランドアクションではない（ここがハイブリッドの肝＝ブランド色は1つに保ちつつ、選択色は別系統に逃がす）。

最大の視覚的署名は **パイプライン・ステージ・ピル**: Cursor の「AIタイムライン5パステル」を、本アプリの
**6つのパイプライン段（Source / Tracking / FX / Audio / Output / Scene）**の状態表示へ転用したもの。
ダーク地で発光して見えるようパステルを少し持ち上げてチューニングしてある。**状態表示専用**で、汎用の
システムアクション色には使わない。

数値・座標・コード・パラメータ値は **JetBrains Mono**。VJ卓は半分が「数字を読む」面なので、可変幅では
桁がガタつく。等幅でカラム整列させる。

**Key Characteristics:**
- 暖色ダーク基調。純黒でなく温かい近黒 (#161512)、テキストは温かいオフホワイト (#ece9e0)。
- ブランドアクション色は **Live Amber 1色**のみ。希少に。選択/フォーカスは別系統の **Selection Blue**。
- ディスプレイ書体は **weight 400** 固定。太らせない（editorial voice）。
- パイプライン6ステージ・パステルピル（Cursor タイムライン由来をダーク用に再調整）。
- 数値/値/コードは全面 **JetBrains Mono** で等幅整列。
- **ヘアラインのみの奥行き**。ドロップシャドウ無し。パネル塗りの微差＋1px線で層を作る。
- **高密度・コンパクト半径**（2–6px）。マーケ的な広い余白ではなく制御卓のリズム（8–12px ガター）。
- 暗所運用前提。最大輝度を抑え、発光は状態ピルとアクティブ値に限定。

## Colors

### Brand & Accent
- **Live Amber** (`{colors.primary}` — #ff5c1a): Primary CTA・armed/live/record・「いま効いている」強調。希少に。
- **Live Amber Active** (`{colors.primary-active}` — #d8480f): 押下/強発光時。
- **Selection Blue** (`{colors.selection}` — #4a9ed8): 選択行・フォーカスリング・スクラブヘッド。*システム選択専用*（ブランド色ではない）。

### Surface（暖色ダークの階調）
- **Canvas** (`{colors.canvas}` — #161512): 温かい近黒のアプリ床。
- **Canvas Soft** (`{colors.canvas-soft}` — #1c1a15): プレビュー外周・最下層パネル。
- **Surface Panel** (`{colors.surface-panel}` — #232019): ドック/パネル本体。
- **Surface Raised** (`{colors.surface-raised}` — #2c2820): パラメータ行ホバー・選択前面・ポップオーバー。
- **Surface Inset** (`{colors.surface-inset}` — #121009): スライダ溝・メーター背景・コード溝（沈み込み）。

### Hairlines
- **Hairline** (`{colors.hairline}` — #353128): 1px 標準ディバイダ。
- **Hairline Soft** (`{colors.hairline-soft}` — #2a2720): 控えめな区切り。
- **Hairline Strong** (`{colors.hairline-strong}` — #454034): パネル外周・ドック境界。

### Text（温かいオフホワイト系）
- **Text** (`{colors.text}` — #ece9e0): 主要ラベル・値・見出し。温かいオフホワイト。
- **Body** (`{colors.body}` — #b8b3a6): 既定の本文/説明。
- **Muted** (`{colors.muted}` — #878275): サブラベル・単位・補助。
- **Muted Soft** (`{colors.muted-soft}` — #5e5a50): 無効/プレースホルダ。
- **On Primary** (`{colors.on-primary}` — #161512): Live Amber 上の文字（近黒で乗せる）。

### Pipeline Stages（署名・状態表示専用）
> Cursor の AI タイムライン5パステルを **本アプリの6パイプライン段**へ転用。ダーク地で読めるよう調整。
> **状態ピル/段ラベル/段アクセント以外には使わない。**
- **Source** (`{colors.stage-source}` — #e8b08a): ピーチ。ソース動画/カメラ段。
- **Tracking** (`{colors.stage-tracking}` — #93c9a0): ミント。四隅/トラッキング段。
- **Effects** (`{colors.stage-effects}` — #b9a6e0): ラベンダー。エフェクトチェーン段。
- **Audio** (`{colors.stage-audio}` — #8fb8e6): パステルブルー。オーディオ解析段。
- **Output** (`{colors.stage-output}` — #d6a44a): 金。Syphon/NDI/フルスクリーン段。
- **Scene** (`{colors.stage-scene}` — #d98fae): ローズ。プリセット/シーン段。

### Semantic
- **Live / Success** (`{colors.semantic-live}` — #46b888): 配信中/接続OK/確定。
- **Record / Error** (`{colors.semantic-record}` — #e2476a): 録画/検証エラー/切断。
- **Warn** (`{colors.semantic-warn}` — #e0a23a): ドロップフレーム/過負荷/注意。
- **Meter Gradient**: 低 #46b888 → 中 #e0a23a → ピーク #e2476a（オーディオ/負荷メーター）。

## Typography

### Font Family
表示/UI は **Inter**（CursorGothic の OSS 代替・weight 400 基調、letter-spacing -1.5% を display に）。
値/座標/コード面は **JetBrains Mono**。Fallback: `system-ui, "Helvetica Neue", Arial, sans-serif`。
> マーケ系のような editorial 寄りが欲しい箇所（起動スプラッシュ/About）は GT Sectra 系も可だが UI 本体は Inter。

### Hierarchy（制御卓向けに Cursor より小さく密に）

| Token | Size | Weight | Line Height | Letter Spacing | Use |
|---|---|---|---|---|---|
| `{typography.display}` | 22px | 400 | 1.25 | -0.33px | モーダル/スプラッシュ見出し |
| `{typography.title}` | 15px | 600 | 1.3 | 0 | パネルタイトル・ドック見出し |
| `{typography.subtitle}` | 13px | 600 | 1.3 | 0 | グループ小見出し |
| `{typography.body}` | 13px | 400 | 1.45 | 0 | 既定の説明文 |
| `{typography.label}` | 12px | 500 | 1.3 | 0 | パラメータ行ラベル |
| `{typography.label-upper}` | 11px | 600 | 1.3 | 0.8px | 段ラベル・ピル・セクション見出し（uppercase） |
| `{typography.value}` | 12px | 400 | 1.2 | 0 | 数値/座標/単位 — **JetBrains Mono** |
| `{typography.value-lg}` | 15px | 400 | 1.1 | 0 | 主要数値（BPM/FPS など大表示）— JetBrains Mono |
| `{typography.code}` | 12px | 400 | 1.5 | 0 | OSC アドレス/JSON/ログ — JetBrains Mono |
| `{typography.button}` | 12px | 500 | 1.0 | 0 | ボタンラベル |
| `{typography.tab}` | 11px | 600 | 1.0 | 0.6px | 下部ページタブ（uppercase） |

### Principles
- **ディスプレイは 400 固定。** タイトル/ラベルは 500–600 まで。700+ は使わない。
- **値・座標・OSC・コードは必ず JetBrains Mono** で等幅整列（桁ガタ防止）。
- 段ラベル/ピル/タブは **uppercase + tracking** で「制御卓ラベル」の語彙を出す。

## Layout

### Spacing System
- **Base unit:** 4px。
- **Tokens:** `{spacing.xxs}` 2px · `{spacing.xs}` 4px · `{spacing.sm}` 8px · `{spacing.base}` 12px · `{spacing.md}` 16px · `{spacing.lg}` 24px · `{spacing.xl}` 32px。
- **パネル内パディング:** 12px。**ドックガター:** 8px。マーケ的 80px リズムは使わない（制御卓は密に詰める）。

### Console Layout（DaVinci 系ドック構成）
```
┌─────────────────────────────────────────────────────────┐
│ top-bar: ロゴ / プロジェクト名 / トランスポート / 出力状態 / FPS │
├──────────┬──────────────────────────────┬───────────────┤
│ 左ドック  │      preview-viewport         │  右ドック       │
│ Source/  │  （投影プレビュー＋safe-area    │  inspector     │
│ Scene    │   ＋コーナーピン4点ハンドル）     │ （選択対象の     │
│ ブラウザ  │                              │  パラメータ）    │
├──────────┴──────────────────────────────┴───────────────┤
│ 下ドック: エフェクトチェーン / オーディオメーター / トランスポート │
├─────────────────────────────────────────────────────────┤
│ page-tabs: SOURCE  TRACK  FX  AUDIO  OUTPUT  SCENES        │
└─────────────────────────────────────────────────────────┘
```
- **下部ページタブ**が DaVinci の「ページ」に相当。クリックで右/下ドックの内容がその段に切替。各タブは
  対応する **Pipeline Stage 色**の細いトップボーダーで識別。
- **中央 preview-viewport** が常時主役（投影の実フレーム）。左=ソース/シーン、右=選択中対象の inspector。
- ドックは**リサイズ可・折りたたみ可**。本番中は preview を最大化し右ドックだけ残す運用も想定。

### Density
- パラメータ行高 **28px**（タッチ要時 32px）。ノブ/フェーダは 1 行内完結。
- 数値は右寄せ・等幅でカラム整列。ラベルは左 truncate。

## Elevation & Depth

**ヘアライン＋塗りの微差のみ**でレイヤーを作る。ドロップシャドウは使わない（Cursor 原則をダークへ継承）。
発光は「状態」を表す時だけ（armed/selected/live）に限定し、暗所で眩しくしない。

| Level | Treatment | Use |
|---|---|---|
| Floor | `{colors.canvas}` (#161512) | アプリ床・タブ列 |
| Panel | `{colors.surface-panel}` (#232019) | ドック/パネル本体 |
| Raised | `{colors.surface-raised}` (#2c2820) | ホバー行・ポップオーバー・選択前面 |
| Inset | `{colors.surface-inset}` (#121009) | スライダ溝・メーター/コード溝（沈み） |
| Hairline | 1px `{colors.hairline}` | パネル外周・行区切り |
| Glow (状態) | 1px `{colors.primary}` / `{colors.selection}` リング | armed / selected のみ。控えめに |

### Decorative Depth
- **preview-viewport** が唯一の「主役」面。実フレーム＋ safe-area の破線＋コーナーピン4点ハンドル。
- **ステージ・パステルピル**が色の奥行きを担う（面の段差を増やさず色で状態を伝える）。

## Shapes

### Border Radius Scale（制御卓向けにコンパクト）

| Token | Value | Use |
|---|---|---|
| `{rounded.none}` | 0px | パネル境界・ドック・ビューポート（直角で密に） |
| `{rounded.xs}` | 2px | 行ホバー・小トグル |
| `{rounded.sm}` | 4px | ボタン・入力・ノブ台座 |
| `{rounded.md}` | 6px | カード/グループ枠・ポップオーバー |
| `{rounded.lg}` | 8px | モーダル・シーンクリップ（最大でもここまで） |
| `{rounded.pill}` | 9999px | ステージピル・バッジ・状態インジケータ |

> Cursor の 8/12px より一段詰める。パネルや大枠はむしろ **0px（直角）**で制御卓の硬質感を出す。

## Components

### Top Bar / Transport

**`top-bar`** — Background `{colors.canvas}`, 高さ 48px, 下端 1px `{colors.hairline-strong}`。
左: ロゴ＋プロジェクト名。中央: トランスポート（play/pause/loop/scrub・`{typography.value}`）。
右: 出力状態（Syphon/NDI/Full）＋ **FPS 大表示** `{typography.value-lg}`（60 を下回ると `{colors.semantic-warn}`）。

**`transport-button`** — アイコンボタン。Background transparent → hover `{colors.surface-raised}`,
active は `{colors.primary}`。rounded `{rounded.sm}`。32×32px。

### Page Tabs

**`page-tab`** — 下部ページ切替。Background `{colors.canvas}`, text `{colors.muted}` `{typography.tab}` (uppercase)。
選択タブは text `{colors.text}` ＋ 上端 2px ボーダーを対応 **Pipeline Stage 色**で点灯。高さ 36px。

### Docks & Panels

**`dock`** — リサイズ/折りたたみ可能なコンテナ。Background `{colors.surface-panel}`,
境界 1px `{colors.hairline-strong}`, rounded `{rounded.none}`。
**`panel-header`** — `{typography.title}` (15/600), text `{colors.text}`, 高さ 32px, 下端 1px `{colors.hairline}`。
**`panel-body`** — padding 12px。

### Parameter Row（卓の主役）

**`param-row`** — 高さ 28px。左: ラベル `{typography.label}` `{colors.body}`（truncate）。
中: スライダ/ノブ。右: 値 `{typography.value}`（JetBrains Mono, 右寄せ, `{colors.text}`）。
hover で Background `{colors.surface-raised}`。selected で左端 2px `{colors.selection}`。

**`slider`** — 溝 `{colors.surface-inset}`, 充填 `{colors.muted}`（armed 時 `{colors.primary}`),
つまみ 10px `{colors.text}`, rounded `{rounded.sm}`。
**`knob`** — 円形, 軌道 `{colors.surface-inset}`, 指針 `{colors.text}`（armed `{colors.primary}`）。直径 32px。
**`toggle`** — OFF: `{colors.surface-inset}` / ON: `{colors.semantic-live}`。rounded `{rounded.pill}`。
**`arm-button`** — 瞬発トリグ/録画アーム。idle `{colors.surface-raised}`, armed `{colors.primary}`＋微グロー。

**`midi-learn-indicator`** — learn 待ち受け中は対象 row が `{colors.selection}` の点滅リング。
バインド済みは行右端に小さな mono バッジ（例 `CC 12`）`{colors.muted}`。

### Preview Viewport

**`preview-viewport`** — 投影プレビュー。Background `{colors.canvas-soft}`, 境界 1px `{colors.hairline-strong}`,
rounded `{rounded.none}`。内側に safe-area 破線 `{colors.hairline}`。
**`cornerpin-handle`** — 四隅/コーナーピン4点。8px の四角ハンドル `{colors.selection}`,
ドラッグ中 `{colors.primary}`。接続線は 1px `{colors.selection}` 破線。

### Effect Chain

**`fx-chain`** — 並べ替え可能な縦リスト。各 **`fx-item`**: ドラッグハンドル＋名前＋ON/OFFトグル＋mix スライダ＋
段アクセント（左端 2px `{colors.stage-effects}`）。selected は `{colors.surface-raised}`＋`{colors.selection}` 左線。

### Scene / Preset Grid

**`scene-clip`** — クリップランチャ的タイル。Background `{colors.surface-panel}`, rounded `{rounded.lg}`,
境界 1px `{colors.hairline}`。アクティブシーンは境界 `{colors.primary}`＋左上に `{colors.stage-scene}` ドット。
ラベル `{typography.label-upper}`。サムネ領域 16:9。

### Meters

**`audio-meter`** — 縦バー。Background `{colors.surface-inset}`, 充填は **Meter Gradient**（低緑→中黄→ピーク赤）。
ピークホールド線 1px `{colors.text}`。
**`fft-bands`** — FFT 帯域バー群。同 gradient。ビート検出時に `{colors.stage-audio}` で 1 フレーム点灯。
**`load-meter`** — フレーム時間/GC。16.6ms 超で `{colors.semantic-warn}`、33ms 超で `{colors.semantic-record}`。

### Pipeline Stage Pills（署名）

**`stage-pill-source`** — Background `{colors.stage-source}`, text `{colors.canvas}`,
`{typography.label-upper}` (uppercase), rounded `{rounded.pill}`, padding 3px × 8px。「SOURCE」段の状態表示。
**`stage-pill-tracking`** — Background `{colors.stage-tracking}`。「TRACK」。
**`stage-pill-effects`** — Background `{colors.stage-effects}`。「FX」。
**`stage-pill-audio`** — Background `{colors.stage-audio}`。「AUDIO」。
**`stage-pill-output`** — Background `{colors.stage-output}`。「OUTPUT」。
**`stage-pill-scene`** — Background `{colors.stage-scene}`。「SCENE」。
> 全て text は `{colors.canvas}`（近黒）。状態（active/idle/error）は枠でなく**不透明度**で表す（idle 40%）。

### Forms / Code

**`text-input`** — Background `{colors.surface-inset}`, text `{colors.text}`, 境界 1px `{colors.hairline}`,
focus 時 `{colors.selection}` リング, rounded `{rounded.sm}`, 高さ 28px, padding 6px × 10px。
**`numeric-input`** — 同上だが `{typography.value}`（JetBrains Mono, 右寄せ）。ドラッグでスクラブ可。
**`code-surface`** — OSC アドレス/JSON/ログ表示。Background `{colors.surface-inset}`, text `{colors.text}`
`{typography.code}`, rounded `{rounded.md}`, padding 12px, 境界 1px `{colors.hairline}`。
**`badge`** — Background `{colors.surface-raised}`, text `{colors.muted}`, `{typography.label-upper}`,
rounded `{rounded.pill}`, padding 3px × 8px。

### Dialog

**`modal`** — Background `{colors.surface-panel}`, 境界 1px `{colors.hairline-strong}`, rounded `{rounded.lg}`,
padding 24px。見出し `{typography.display}`。背後は `{colors.canvas}` 70% のオーバーレイ（シャドウは使わない）。

## Do's and Don'ts

### Do
- `{colors.primary}`（Live Amber）は primary/armed/live にだけ**希少に**使う。
- 選択/フォーカスは `{colors.selection}`（Blue）で、ブランドアクションと**役割を分離**する。
- ステージ・パステルは **段ラベル/ピル/段アクセント**だけに使う（汎用 UI に散らさない）。
- 値・座標・OSC・コードは**必ず JetBrains Mono** で等幅整列。
- 奥行きは**ヘアライン＋塗りの微差**で。発光は状態（armed/selected/live）に限定。
- 暗所運用前提で**最大輝度を抑える**。大面積を高輝度にしない。

### Don't
- ブランドアクション色を増やさない。Live Amber 1色。Blue は「選択」であって action ではない。
- ディスプレイ/見出しを 700+ に太らせない（editorial voice を保つ）。
- **ドロップシャドウを足さない**。ヘアラインと塗り差で層を作る。
- ステージ・パステルを非ステージ UI（ボタン/トグル等）に流用しない。
- 純黒 #000 / 純白 #fff を使わない。温かい近黒・温かいオフホワイトで統一。
- マーケ的な広い余白（80px リズム）を持ち込まない。制御卓は密に。

## Display & Multi-Monitor

| 状況 | 挙動 |
|---|---|
| オペレータ画面（主） | フルドック構成。preview＋左右＋下ドック＋ページタブ。 |
| 本番フォーカス | 左/下ドックを畳み、preview 最大化＋右 inspector のみ。トランスポートは top-bar に残す。 |
| 投影/Syphon/NDI 出力 | **UI を一切出さない**。本編フレームのみ（safe-area/ハンドルも非表示）。 |
| サブモニタ運用 | オペUIを操作画面、本編を投影機へ（マルチディスプレイ割当, `docs/06`）。 |

- タッチ操作時はパラメータ行高/ハンドルを 28→32px、ハンドル 8→12px に拡大。
- 最小作業解像度 1280×800 を下限に、ドックは折りたたみで吸収。

## Iteration Guide

1. 1 コンポーネントずつ。まず `param-row` と `preview-viewport`（卓の主役2つ）。
2. 既定半径: コントロール `{rounded.sm}`(4px)・グループ `{rounded.md}`(6px)・大枠/ビューポートは `{rounded.none}`。
3. バリアントは `Components:` 配下に別エントリで足す。
4. 色は**必ずトークン参照**。hex 直書きしない。
5. hover/active は本書で定義した状態色のみ。新しい発光を勝手に足さない。
6. Inter 400=display / 500–600=label・title。値/コードは JetBrains Mono。
7. Live Amber は希少に。Selection Blue と役割を混ぜない。
8. ステージ・パステルはパイプライン状態に限定。

## Known Gaps

- CursorGothic 相当は未採用。**Inter ＋ JetBrains Mono** を正本とする（ライセンス安全）。
- Unity 実装系（**UI Toolkit** USS か uGUI か）は未確定。トークンは USS 変数へ写像する前提（`docs/07`）。
- アニメーション（ピル点灯・ドック開閉・メーター減衰）の時定数は別途。
- 投影本編側のルック（グリッチ等）は本書の対象外＝エフェクト設計 `docs/04`。
- カラーマネジメント（HDR/広色域）は将来課題。現状 sRGB 前提。
