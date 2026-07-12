# RewriteReality Operator UI — Unity 移植ハンドオフ（Claude Code 用）

**2026-07-05 時点の確定仕様。** HTML モック `ui_kits/operator/index.html` が正（見た目・挙動とも）。
移植先: Unity 6 / UI Toolkit（UXML/USS + C#）。**flex のみ（USS は CSS grid 非対応）／JS 挙動なし（操作は C#）／トークンは DESIGN.md → USS 変数**。

モックのファイル対応:

| モック | 内容 | Unity 対応 |
|---|---|---|
| `App.jsx` | シェル・ページ・選択状態・OUTPUT ルート | `OperatorShell.uxml` + PageController / ControlHub |
| `LeftDock.jsx` | 左ドック（ライブラリ/Surface 一覧） | LibraryView（Foldout + ListView） |
| `Inspector.jsx` | 右 Inspector（Item/Track/Surface） | InspectorView（kind でビルダ分岐） |
| `Timeline.jsx` | タイムライン（Song/Short タブ・track 選択・pad 割当） | TimelineView |
| `CenterStage.jsx` `MappingCanvas.jsx` | プレビュー / WARP エディタ（pin ドラッグ） | Viewport + WarpEditorView（`Compositor`/`OutputWarp` API） |

---

## 1. ページ IA（2 ページ・確定）

ページは「**中央ワークスペースが変わる場合のみ**」存在（DaVinci 原則）。旧 SOURCE/FX/AUDIO/SCENES/OUTPUT は選択→Inspector 連動・上バー OUTPUT トグル・WARP エディタ内切替に吸収済み。

| ページ | 中央 | 左ドック |
|---|---|---|
| `PERFORM`（既定） | ライブプレビュー（EffectChain.FinalTexture） | ライブラリ: Sources / Audio / Scenes（折りたたみセクション） |
| `MAPPING` | WARP エディタ（**EMBED ⇄ OUTPUT** セグメント） | Surfaces + Input + Output Surface |

- PageController は 2 状態。下端タブ＝ページ切替（active タブは上端 2px を段色で点灯）。
- タブ左に **モード切替 `準備 Edit | 本番 Live`**（Live 選択時は Live Amber 充填）。

## 2. 上部バー（48px）

`brand · project名 | 残り時間 | → OUTPUT ルートトグル | FPS`

- **transport は置かない**（タイムライン側に一本化・1 機能 1 箇所）。時間表示は**残り時間**（例 `-02:07.60`・mono）。
- **OUTPUT ルート直接トグル**（`OutputManager` 同期）:
  - `OUTPUT | Full Syphon NDI` — ラベルのみ（ドットなし）。**ON = ラベル文字が semantic-live 緑 / OFF = muted-soft 灰**。「OUTPUT」見出しは body 色。
  - `HasX == false`（コンポーネント未割当）は 35% 減光 + disabled。
  - **誤操作防止**: 本番 Live 中のクリックは即トグルせず**確認ポップ**（`Syphon → OFF` + Cancel / Turn Off・ON 側確定ボタンは primary）。準備 Edit は即トグル。popover は軽量（modal 禁止）。
  - バインド: `FullscreenEnabled / SyphonEnabled / NdiEnabled`（setter 即反映）・可否は `HasFullscreen/HasSyphon/HasNdi`。

## 3. 汎用セレクションモデル（UI の核）

**「何かをアクティブにすると、右 Inspector がそれ専用の表示に切り替わる」**を全域で統一。

- 選択可能: 左ドック全項目（`surface / source-video / source-camera / source-ext / fx / audio-input / mapping / scene`）、タイムライン track、WARP キャンバスのペイン（クリックで該当 Surface 選択・MadMapper 式）。
- 選択は排他: ドック項目 ⇄ track。同一項目再クリック or `Deselect` で解除。ページ切替でドック選択クリア（track 選択は保持）。
- track のみ **⌘/Ctrl/Shift+クリックで複数選択**。行の ON/OFF Toggle は選択に伝播させない（`evt.StopPropagation()`）。
- C# イベントは単一: `SelectionChanged(SelectionRef)` — `SelectionRef { SelectionKind kind, string id, IReadOnlyList<TrackId> tracks }`。InspectorView が kind でディスパッチ。
- 視覚: 選択行 = `--rr-surface-raised` 背景 + 左 2px `--rr-selection`。hover = 背景のみ。影/グロー禁止（ヘアライン原則）。行高 28px（track ヘッダ 34px）。

## 4. 右 Inspector の表示内容

### 4a. 無選択（PERFORM 既定）= Master / Program

Master(CC 1) / Fade to Black / 解像度 / BPM ＋ **FX CHAIN · PROGRAM**（グローバル FX 一覧）。
グローバル FX Chain は「Program という object へのカスタム」なのでここが正位置（左ドックには置かない）。FX 行クリック → その FX の表示へ。

### 4b. ドック項目別

| type | 表示内容 |
|---|---|
| `fx` | Enabled / Amount (MIDI chip) / Audio Gain / Mix / Scope / OSC (`/rr/fx/<slug>/amount`) |
| `surface` | §5 の SurfaceInspector |
| `source-video` | Speed (JOG・Live中 armed) / Loop / Time / Duration |
| `source-camera` | Resolution / Exposure / Zoom / Embed |
| `audio-input` | リアルタイムメーター群 / Sensitivity / RMS / Source |
| `mapping` | Band / Target / Amount / Smoothing / Curve |
| `scene` | Fade In・Out / Trigger (Key, Hold) / **Fire**(primary)・Save |

見出し = 項目名 + 該当 StagePill。本番 Live 中は主要パラメータの fill/値が Live Amber（armed）。

### 4c. Track（タイムライン選択）

- **video 単選択**: Role / Opacity / Blend / `Track FX`（行: 有効ドット・名前・値 mono・Toggle）/ `+ Effect`
- **audio 単選択**: Role / Volume / Fade / メーター群 / `Audio Mappings` / `+ Mapping`
- **複数選択**: 一覧（kind 色ドット+名前+role）+ Group 操作（Opacity=mixed / Mute All / Deselect）
- トラックメタは ScriptableObject 供給、FX は EffectChain の per-track スタック参照。

## 5. Surface（07b §3.2.1 Fit モード対応）

- **Input Surface**: `Fit Mode` セグメント **MASK | GRID**（射影 2×2 旧 Project は廃止）。
  - **Mask（既定・#32）**: SHAPE（Scale/Feather=窓形状）+ CONTENT（Zoom/Pan=DRAG）。窓抜き＝内容は歪まない。
  - **Grid（#34）**: Interp=BEZIER / Smoothing / **Test Pattern トグル** / Reset Warp / + Row/Col。
- **Output Surface（#25/#35）**: Mode=**PLAIN + NUDGE** / **Grid Overlay トグル** / Calibrate=GRID PROJ（格子投影校正）/ Edge Blend。Mask/Crop は出さない。
- バインド: `Surface.FitMode` enum。セグメントは `.rr-seg`（inset 溝 + raised 選択）。Test Pattern は Compositor の内蔵パターン RT 差替。

## 6. WARP エディタ（MAPPING 中央・docs/06 #25）

- 上部セグメント **EMBED ⇄ OUTPUT**（EMBED=tracking mint ドット / OUTPUT=output gold ドット）。
- **EMBED**: Input（camera UV）/ Output（composite）分割ビュー。N×M pin ドラッグ（`Compositor.Get/SetWarpPoint`）。選択 pin = Live Amber、他 = Selection Blue。
- **OUTPUT**: OutputWarp の四隅/メッシュ直接編集。**編集中はプレビュー = 変形後 RT（WYSIWYG）** — モックは「WYSIWYG · preview = warped RT」ラベルで明示。
- ペインクリック = Surface 選択（枠が Selection Blue）。pin ドラッグと干渉しない。

## 7. タイムライン（下部ドック・§3.5）

- 最上段に**ブラウザ式タブバー**: `Song`（緑・リニア通し）/ `Short`（琥珀・ホールド発火）バンク。`＋`で New Song / New Short 追加、`×`で閉じる（最後の1枚は不可）。構成は永続化。
- **Song**: マルチトラック（VID×N + AUD×N）。ruler / 再生ヘッド（Selection Blue）/ クリップは段色 / AUD は波形 + FADE + mute。トラックヘッダ（96px）クリック = track 選択（§3）。
- **Short**: 1 Short = **編集可能なバンク**（ベースクリップ + `+ Track` 追加レーン）。**Song 同様に ruler + 再生ヘッド + Time 表示**を持つ。**per-Short パッド/キー割当**は Key 行の keycap ボタン（`[⌨ Q ▾]` / MIDI 時 `[PAD n ▾]`）→ 4×4 マトリクス（自分=琥珀 / 他 Short 使用中=ローズドット・クリックで奪取）。`HOLD-LOOP` トグル＝本番でキー押下中ループ。旧 `GATE hold-fire` 表記・`KEY` ラベルは削除（2026-07-10）。
- **transport（共通ヘッダ）**: `+ Track`（Song は共通ヘッダ・Short は Key 行左端）→ 前 / 再生-停止 / ループ + Time `01:12.40 / 03:20.00`（Song / Short 共通表示）。
- **`+ Track` ボタン（2026-07-05 追加）**: Song / Short 両方の左端に配置。押下でファイル参照 popover（上方向・VIDEO / AUDIO にグループ化したライブラリ一覧、ファイル名 mono + 尺）。選択すると該当種別のトラック行を末尾に追加（`VID n` / `AUD n` 自動採番、クリップはファイル名ラベル・audio は波形表示）。Song の追加行は §3 の track 選択・ON/OFF・mute に従う。Short の追加行は各レーンがホールド発火可（ゲート）。
  - Unity 実装: ボタンは `.rr-add-track`（ghost + hairline 枠・uppercase）。popover は軽量 ListView（modal 禁止）。実装ではネイティブのファイルダイアログ（`EditorUtility.OpenFilePanel` 相当は使わず、ランタイムは `UnityEngine.Windows.File` / NSOpenPanel ブリッジ）→ `TimelineModel.AddTrack(kind, path)`。対応拡張子: video `.mov/.mp4`, audio `.wav/.aiff`。
- キー割当は `ControlHub` の抽象マッピング（KB→将来 MIDI パッド同形）。

## 7b. MadMapper 参考の採用機能（2026-07-12）

[MadMapper 6 docs](https://docs.madmapper.com/madmapper/6/discover-the-interface) を精査し、本アプリに合うものだけ採用（DMX / Laser / Materials Library / Code Editor / 3D Surface は対象外）。

- **Mesh Warping（Video Surfaces 準拠）**:
  - WARP エディタ上部に **グリッド解像度ステッパー `X [−] 4 [+] · Y [−] 3 [+]`**（2–8、変更で全ポイント再生成）＋ **BEZIER トグル** ＋ **Reset**。
  - Bezier ON でメッシュ線が **Catmull-Rom → cubic Bezier のスムーズ曲線**描画（OFF は直線セグメント）。Unity: `Compositor.SetMeshResolution(x,y)` / `MeshInterp.Bezier|Linear`、線描画は `UIToolkit Painter2D.BezierCurveTo`。
  - Surface Inspector の Grid モードに **Mesh Warping セクション**（Enabled / Grid X·Y / Bezier / Smoothing / Test Pattern / Reset / + Row/Col）。
- **Views 切替（Input and Stage Views 準拠）**: EMBED 編集時に `INPUT | SPLIT | OUTPUT` セグメントで表示切替（片側最大化）。Unity: WarpEditorView の 2 ペインを flex-grow 切替。
- **Surface リストの Show/Hide + Lock（Surfaces List 準拠）**: MAPPING 左ドックの各 Surface 行末に eye / lock アイコン。lock 中は warp 編集を拒否（誤操作防止・warn 色）。Unity: `Surface.visible` / `Surface.locked`、locked は WarpEditor で pin 非表示＋ドラッグ無効。
- **メディア使用数バッジ（Media Bin 準拠）**: Sources の各行に `×N` チップ（N Surface で使用中）。クリックで該当 Surface 選択は将来対応。Unity: `MediaPool.UsageCount(mediaId)`。
- **Master Speed（Master Settings 準拠）**: Master/Program Inspector に `Speed`（ParamRow・Live 中 armed）。Unity: `ControlHub.MasterSpeed`。
- **Freeze トグルは削除（2026-07-12）**: 出力フレーム全体を静止させる機能だったが、再生停止（Timeline 再生ボタン）や Fade to Black と役割が重複し混同を招くため撤去。Unity 側でも `FreezeEngine` は実装不要（もし着手済みなら削除）。空間的な追従（マスター動画内の領域に Surface を貼り付け・追尾）は別概念であり、この Freeze とは無関係。
- **不採用**: DMX/Laser 系、Materials/ISF ライブラリ、AI/Code Editor、Quartz Composer、3D Surface（.OBJ）、Soft-Edge（単一プロジェクタ運用のため現状不要 — マルチプロジェクタ化する際に再検討）。

### タイムラインのトラック縦スクロール（2026-07-13）

- **トラック行が増えて下端が見切れる問題に対応**: Song / Short とも、トラック行の領域だけを縦スクロール可能に。**ルーラー（時間軸）と再生ヘッドは固定**（スクロールしない）。
- **スクロールバーは非表示**（`.rr-noscroll`: `scrollbar-width: none` + `::-webkit-scrollbar { display: none }`）。ホイール/トラックパッドでスクロール。
- Unity: TimelineView のトラック領域を `ScrollView`（`verticalScrollerVisibility = Hidden`、`mode = Vertical`）でラップ。ルーラーとプレイヘッドは ScrollView の外（オーバーレイ）に配置し固定。

### 発火バインドのキーボードフォールバック（MadMapper 流・2026-07-10）

- **MIDI パッド接続時は `PAD n`、未接続時はキーボードキーにフォールバック**（MadMapper と同じ思想）。UI は `MIDI_PRESENT` フラグで表示を切替（モックは `false`＝キーボード表示）。
- **パッド↔キーの対応**（4×4）: `Q W E R / A S D F / Z X C V / 1 2 3 4`（pad index 1–16 の順）。アサインマトリクスは各セルにキー文字（主）＋ pad index（小・隅）を表示、`no MIDI pad — keyboard fallback` 注記付き。
- **Short タブに割当を常時表示**: 種別アイコン（⚡）＋タブ名の右に**キーキャップ型チップ**（mono・下辺 2px の物理キー風）で `Q 1`（キー文字＋パッド番号）を表示。MIDI 接続時は `PAD 1` 等。未割当は `·`（薄色）。アクティブタブは Live Amber。タブは高さ 32px・アイコン付きで視認性を強化。Key 行のボタンも同ラベル＋キーボード/パッドアイコン（`bindIcon()`）で一致。
- Unity 実装: `TriggerBinding { BindKind kind (Pad|Key), int padIndex, KeyCode key }`。`InputRouter` が MIDI デバイス有無で解決先を切替（デバイス着脱で live 再バインド）。キーキャップ USS は `.rr-keycap`（border-bottom 2px で打鍵感）。ラベルは `InputRouter.LabelFor(binding)` に集約。

### アプリ終了 UX（2026-07-06）

- **常設の終了ボタンは置かない**（ライブ卓では誤爆リスクが最大）。左上**ブランドロゴをメニュー化**（`About / Preferences… / Quit RewriteReality…`）。
- **Quit は常に確認**（modal + overlay）。**本番 Live 中 or 出力ルート ON 中**は警告を強化：`ON AIR` バッジ＋有効ルート（"Full · Syphon"）を record 色で提示し、確定ボタンは `Stop Output & Quit`（record 色）。危険がなければ通常の `Quit`（primary）。
- ショートカット **⌘Q** も同じ確認フローを通す（即時終了させない）。Unity 実装: `Application.wantsToQuit` をフックし、確認ダイアログ経由でのみ `Application.Quit()`。出力停止は `OutputManager.DisableAll()` を Quit 前に実行。

### 精査反映（2026-07-06）— 表示の整理

- **Short 発火はバンク単位**: 1 パッド = その Short 全体を発火。**発火トリガーは割当パッド/キーのみ（UI に FIRE ボタンは置かない・2026-07-09 削除）** — 画面は割当・内容編集・発火状態表示（held 時に全レーンが Live Amber 点灯）に徹する。レーンは内容表示のみ（個別トリガなし）。**Hold-Loop トグル**＝本番でキー押下中ループするか（per-Short）。Short でも transport（前/再生/ループ）は表示（作成・プレビュー用）。
- **性能表示を上部バー右に集約**（fps + frame ms + gc、常時監視）。下端バーの frame/gc は削除。
- **プレビュー（PERFORM）整理**: コーナーピン＋安全枠は削除（編集は MAPPING の WARP エディタのみ）。左下は解像度のみ（fps は上部バーと重複のため削除）。
- **VID トラック右端は Opacity 実値**（mono）。意味のない "OPACITY" 固定ラベルは廃止。
- **Surfaces 重複解消**: 左ドックの Surfaces は Input surface のみ。Output Surface は別セクション（Projector A の二重掲載を解消）。
- **タブの種別は SONG/SHORT バッジのみ**（色ドットは削除、Output トグルの「ドット不要」判断と一貫）。

## 8. モックとの差分許容

- FX 行 Toggle・pad 発火・確認ポップ後の配信停止はモックでは見た目のみ。実装は EffectChain / ControlHub / OutputManager を書き換える。
- Group 操作（Mute All / mixed）はプレースホルダ。mixed 判定は実装側。
- モックの色バー映像はダミー。実装は FinalTexture / camera RT。

## 9. トークン早見（USS 変数化）

| 用途 | トークン |
|---|---|
| 選択/フォーカス/pin/再生ヘッド | `--rr-selection` #4a9ed8 |
| armed/live/record/確定 CTA | `--rr-primary` #ff5c1a（希少に） |
| ルート ON / 接続 OK | `--rr-semantic-live` #46b888 |
| OFF/未割当 | `--rr-muted-soft` #5e5a50 |
| 行 hover/選択背景 | `--rr-surface-raised` #2c2820 |
| 数値/座標/OSC | JetBrains Mono（`.rr-mono`） |

段色（Source peach / Tracking mint / FX lavender / Audio blue / Output gold / Scene rose）は状態表示専用。
