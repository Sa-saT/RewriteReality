# 07b. オペレータUI デザインブリーフ（#24・Claude Design 用）

操作UIの見た目/レイアウトを確定するための仕様書。**ユーザーが Claude Design → UI Builder で見た目を確定**し、
agent は本ブリーフに沿って UXML/USS の足場とバインドを用意する（役割分担は `07-control-ui.md`）。
このページは **Claude Design に貼ってモック生成の起点**にできるよう、必要な画面・要素・状態・制約を一枚にまとめる。

> 正本トークン＝ルート `DESIGN.md`（暖色ダーク／Live Amber 希少／Selection Blue／JetBrains Mono／ヘアライン深度）。
> 実装＝UI Toolkit（USS/UXML）。**CSS グリッド不可→flex**、**JS 挙動は使わない**（操作は C# の `ControlHub`/`OperatorUI`）。

---

## 0. 使い方（Claude Design フロー）

1. Claude Design で新規プロジェクト → `DESIGN.md` を Design System として読ませる（GitHub import / `/design-sync`）。
2. 本ブリーフ（07b）と既存 `RewriteReality/Assets/UI/RewriteReality.uss` を渡し、§2–§4 の画面を生成。
3. キャンバスで反復 → 「Send to Claude Code」でハンドオフ → Claude が `Assets/UI/` の UXML/USS に移植。
4. ユーザーが UI Builder で最終調整＋Play 確認 → #24 完了。以後 #21/#22 の実装へ。

---

## 1. 設計原則

- **制作卓（プロツール）の密度**：常時表示の制御卓。モーダルを増やさず、左右ドック＋下部リストで一望。
- **本番映像には出さない**：UI は操作画面（Game ビュー）専用。Syphon/NDI 出力には乗らない。
- **ブランド色は希少**：Live Amber は「実行中/録画/重要トグル」だけ。選択は Selection Blue で別系統。
- **6 パイプライン段の色**：Source / Tracking / FX / Audio / Output / Scene を `DESIGN.md` のステージ色で識別。
- **数値は等幅**：座標・値・FPS・OSC は JetBrains Mono。

---

## 2. 全体レイアウト（Console Layout・既存 `OperatorShell.uxml` を踏襲）

```
┌───────────────────────────────────────────────────────────────┐
│ TOP BAR  brand · project名 · [Syphon] [NDI] バッジ ·      FPS  │  48px
├──────────┬──────────────────────────────────┬─────────────────┤
│ LEFT DOCK│            VIEWPORT               │  RIGHT DOCK      │
│ 200px    │   ライブ preview (最終RT)         │  280px           │
│ ソース/  │   ＝ EffectChain.FinalTexture     │  Inspector       │
│ シーン等 │   （マッピング時はキャンバス重畳） │  (選択対象の      │
│          │                                  │   パラメータ)     │
├──────────┴──────────────────────────────────┴─────────────────┤
│ BOTTOM DOCK  FX 一覧（順序/ON-OFF/mix）                  168px  │
├───────────────────────────────────────────────────────────────┤
│ PAGE TABS  SOURCE · TRACK · FX · AUDIO · OUTPUT · SCENES   36px │
└───────────────────────────────────────────────────────────────┘
```

- ページタブで右ドック/ビューポートの中身を切替（FX 選択中は FX 一覧＋パラメータ、TRACK 選択中はマッピング）。
- 既存実装の対応：preview=`OperatorUI` が `_chain.FinalTexture` を流す。FX 行=`FxRow.uxml`、パラメータ行=`ParamRow.uxml`。

---

## 3. マッピング画面（TRACK ページ・**MadMapper 参照**）★今回の主眼

埋め込み（ベース動画の追従領域へライブカメラを流し込む）を MadMapper 流の **Surface 操作**で行う。

### 3.1 Surface モデル（2 種類）

| Surface | 役割 | 対応コード | 段 |
|---|---|---|---|
| **Input Surface（埋め込み）** | ベース動画の領域にカメラを射影＋メッシュワープ | `Compositor` の多pinワープ | 前段 |
| **Output Surface（台形補正）** | 完成映像“全体”をプロジェクタ歪みに合わせる | 出力段コーナーピン（未実装/将来） | 最終段 |

> まずは Input Surface（1 面）を確定。Output Surface は同じ UI に**後から畳み込む**（#22）。

### 3.2 画面構成（Input/Output 分割ビュー）

```
┌──────────────┬────────────────────────────────┬──────────────┐
│ SURFACE LIST │   MAPPING CANVAS                │ PROPERTIES   │
│ + Surface    │  ┌──────────┬──────────┐        │ 選択 Surface  │
│ ▸ Surf 1 ●   │  │  INPUT   │  OUTPUT  │        │ ・名前/可視   │
│   Surf 2     │  │ (camera) │ (合成先) │        │ ・グリッド    │
│              │  │  ● ● ●   │  ● ● ●   │        │   N×M        │
│              │  │  ● ● ●   │  ● ● ●   │ ←制御点 │ ・平滑化      │
│              │  │  ● ● ●   │  ● ● ●   │  ドラッグ│ ・マスク      │
│              │  └──────────┴──────────┘        │ ・mix/opacity│
└──────────────┴────────────────────────────────┴──────────────┘
```

- **制御点ドラッグ**：N×M グリッドの各 pin を直接ドラッグしてメッシュワープ（`Compositor.GetWarpPoint/SetWarpPoint`）。
- **グリッド解像度**：2×2（四隅）〜任意（`Compositor.SetGridResolution`）。`ResetWarp` で等間隔に戻す。
- **Input/Output 分割**：左にカメラ素地（UV）、右に合成先（ベース動画上）の対応を見せる（MadMapper の Input/Output）。
- **マスク**（将来）：矩形フェザリング → ベジェマスク。`docs/03`/`07` のフェザリングに接続。
- **状態**：選択 pin（Selection Blue）、追従中(Tracking 緑)、手動ワープ適用中のバッジ。

### 3.3 MadMapper から取り込む要素（参照）

- Surface 単位で**コンテンツ/エフェクト/プロパティを独立**して持つ。
- **メッシュワープ**（freeform 制御点）で素材を面に合わせ込む。
- **マスク/ブレンド**で境界を整える。
- 入力ソースは **動画/ライブカメラ/Syphon-in** を許容。

---

## 4. その他ページ（最小要件）

- **SOURCE**：ベース動画 選択/再生/ループ/スクラブ、カメラ選択（`SourceVideo`/`SourceCamera`）。
- **FX**：エフェクト一覧（順序/ON-OFF/mix）＋選択エフェクトのパラメータ（`EffectChain`/`EffectBase.Parameters`）。
- **AUDIO**：入力選択、感度、帯域/RMS/onset メーター、音→パラメータのマッピング（`AudioAnalyzer`）。
- **OUTPUT**：FS/Syphon/NDI の ON/OFF・解像度、Output Surface（台形補正）。
- **SCENES**：プリセット保存/読込・次/前（`Preset`・将来）。

---

## 5. バインド境界（agent が用意・見た目はユーザー）

- 見た目＝UXML/USS（UI Builder）。**C# には見た目を書かない**（`OperatorUI` は `Q<>()`＋`EffectParameter` バインドのみ）。
- 新コントロール（ノブ/XYパッド/縦フェーダー/メーター/マッピング pin）は **まず UXML テンプレ → C# はバインドだけ**。
- データ源：`ControlHub`（選択/ON-OFF/Nudge）、`EffectParameter`（name/min/max/Value/Normalized）、
  `EffectChain.FinalTexture`（preview）、`Compositor` warp API（マッピング）。

---

## 6. 制約・チェックリスト

- [ ] レイアウトは **flex**（CSS grid 不可）。固定 px 設計＝`PanelSettings` Scale Mode は **Constant Pixel Size**。
- [ ] 色/タイポ/角丸/ヘアラインは `DESIGN.md` トークン → USS 変数のみ（生値を散らさない）。
- [ ] ブランド色（Live Amber）は希少使用、選択は Selection Blue。
- [ ] 数値表示は JetBrains Mono（`.rr-mono`）。
- [ ] JS 挙動は不使用（操作は C#）。Web コントロール→UI Toolkit コントロールへ読み替え可能な構成にする。

---

## 7. Claude Design へ貼るプロンプト（雛形）

> 添付の DESIGN.md をデザインシステムとして使用。Unity 風ではなく、DaVinci Resolve 系のダーク高密度プロツール×
> Cursor の editorial な抑制。リアルタイム VJ アプリ「RewriteReality」のオペレータ画面を作って。
> 画面：① Console Layout（上部バー＋左ドック＋中央 preview＋右 Inspector＋下部 FX 一覧＋下端に6ページタブ
> SOURCE/TRACK/FX/AUDIO/OUTPUT/SCENES）。② TRACK ページは MadMapper 風の領域マッピング
> （左 Surface 一覧＋中央 Input/Output 分割キャンバス＝N×M 制御点をドラッグしてメッシュワープ＋右 per-surface プロパティ）。
> ブランド色は希少な Amber、選択は Blue、数値は等幅。影は使わずヘアラインで奥行き。HTML/CSS（flex・grid 不使用）で。
