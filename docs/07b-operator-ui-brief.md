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
│ TIMELINE  マルチトラック（映像 ×N / 音声 ×N）           168px+ │
│   再生ヘッド・スクラブ／各トラック ON-OFF／音声 fade・mute      │
├───────────────────────────────────────────────────────────────┤
│ MODE [ 準備 Edit | 本番 Live ]  ·  TABS SOURCE·TRACK·FX·AUDIO·OUTPUT·SCENES │
└───────────────────────────────────────────────────────────────┘
```

- **下部＝タイムライン**（§5）。従来の **FX 一覧は FX ページ／右ドックへ移設**。
- 左端に **モード切替（準備 Edit / 本番 Live・§2.5）**。ページタブで右ドック/ビューポートの中身を切替。
- 既存実装の対応：preview=`OperatorUI` が `_chain.FinalTexture` を流す。FX 行=`FxRow.uxml`、パラメータ行=`ParamRow.uxml`。

---

## 2.5 2 モード（準備 Edit / 本番 Live）

ワークフローを **準備（オフライン仕込み）** と **本番（ライブ）** の 2 モードに分ける。上部バーで切替。

| モード | やること | 主に編集する対象 |
|---|---|---|
| **準備 Edit** | surface 配置・メッシュワープ・**エフェクト範囲（surface 指定/全体）の割当**・タイムライン構成（映像/音声トラック）・出力変形 | レイアウト/ルーティング/尺 |
| **本番 Live** | 準備した surface に**ライブ動画を埋め込み**、タイムライン再生＋エフェクトを**リアルタイム微調整**、シーン発火 | 値の即時操作のみ（構成は固定） |

- **準備で決める**：どの surface に何を流すか、エフェクトを **surface 単位 or 全体** のどちらに掛けるか（§3.6）、タイムラインの並び、出力サーフェスの形。
- **本番で動かす**：mix/強度/onset 連動・fade/mute・再生位置・シーン切替。**構成自体は本番中に壊さない**（誤操作防止）。

---

## 3. マッピング画面（TRACK ページ・**MadMapper 参照**）★今回の主眼

埋め込み（ベース動画の追従領域へライブカメラを流し込む）を MadMapper 流の **Surface 操作**で行う。

### 3.1 Surface モデル（2 種類）

| Surface | 役割 | 対応コード | 段 |
|---|---|---|---|
| **Input Surface（埋め込み）** | ベース動画の領域にカメラを射影＋メッシュワープ | `Compositor` の多pinワープ | 前段 |
| **Output Surface（出力変形）** | 完成映像“全体”をプロジェクタ等の面に合わせて変形（MadMapper 流） | 出力段コーナーピン＋メッシュ | 最終段 |

> **Input Surface は複数可**（準備で配置）。**Output Surface は正式機能**（“将来”から格上げ・2026-06-30）＝
> 出力映像も MadMapper のようにメッシュ/コーナーピンで変形する。両者は同じ「Surface」UI で扱い、
> Input/Output 分割ビュー（§3.2）で切替える。

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

## 3.5 タイムライン（マルチトラック・映像＋音声）★画面下部

下部ドック＝**マルチトラックのタイムライン**。準備で構成し、本番で再生＋微調整する。

```
       0:00      0:30      1:00      1:30
       ├─────────┼─────────┼─────────┼────
[▶][⏸] 再生ヘッド ▮ ───────────────────────  スクラブ
─────────────────────────────────────────────
VID 1  [ clipA ][   clipB   ][ clipC ]   ●ON  opacity
VID 2     [ overlay ]            [ … ]    ●ON
─────────────────────────────────────────────
AUD 1  [ track ▮▮▮▮▮▮▮▮▮▮ ]   ●ON  ░fade░  🔊/🔇mute
AUD 2     [ sfx ]   [ sfx ]    ●ON         🔊/🔇
```

- **映像トラック ×N**：ベース動画/画像のクリップを時系列配置。トラック ON-OFF・opacity・重ね順（合成は `Compositor`→`EffectChain`）。
- **音声トラック ×N**：クリップ配置。**fade（in/out）・mute・音量**。詳細は §3.5.1。
- **再生ヘッド/スクラブ**：全トラック共通の時間軸。`SourceVideo.Time` と整合（追従ベイク `track.json` も同じ時間軸）。
- **発火モデル＝C（song＋short）確定**：詳細は §3.5.2。実装の入口はリニア通し（song）、その上に short のホールド発火を載せる。

### 3.5.1 オーディオの扱い（**内部再生＋外部解析の両対応**・確定 2026-06-30）

- **内部再生（マスター）**：タイムラインの音声トラックを**アプリが再生・ミックス**。トラック毎に **fade/mute/音量**。
- **外部解析**：DJ/Ableton 等の外部入力も受け、**映像を音に反応**させられる（現 `AudioAnalyzer` の延長）。
- **本番構成で切替**：①内部音源で完結 ②外部音源＋解析のみ ③両方（内部再生＋外部解析）。
- 解析対象は**最終ミックス（AudioListener）**＝内部再生でも外部入力でも同じ FFT/onset/帯域が効く。
- **外部モード（MIDI/音楽アプリ）でも画像エフェクトと音連動できる**（2系統・併用可）：**Path 1**＝スペクトル/onset 連動
  （外部音を **BlackHole/Loopback** で `MicInput` に取り込めば現アーキで効く）。**Path 2**＝MIDI/OSC 連動
  （BPM 同期・ノート発火・CC＝`ControlHub` 経由・M7）。詳細＝`docs/05`。

### 3.5.2 発火モデル：song（リニア通し）＋ short（ホールド発火）【確定 2026-06-30】

C（両方）の具体形。**song** が本流として流れ続け、**short** をキーで瞬間的に最上位へ差し込む。

- **song（A）**：タイムラインの通し再生。裏で再生ヘッドが進み続ける（short の有無に依らない）。
  - **動画だけライブ速度可変【確定 2026-06-30】**：本番中、**song の“映像”の再生速度をインタラクティブに変える**
    （`SourceVideo` が `PlaybackSpeed` を公開＝VideoPlayer.playbackSpeed）。**ボタン/スクロール（ジョグ）に割当**
    （`ControlHub`・将来 MIDI ジョグも同形）。速度範囲は **0=フリーズ〜Nx**（逆再生は VideoPlayer 非対応＝将来フレームステップで検討）。
  - **音は別系統**：音声トラック（`AudioMixer`）は**通常速度のまま** → 映像だけ速度変化＝**意図的にズレる VJ 表現**。
  - **追従は自動整合**：四隅は video time にキー（`BakedCornerSource`）なので、速度を変えても埋め込み領域は崩れない。
- **short（B）**：各 short を**キーに割当**。**押下中だけアクティブ＝最上位レイヤー**で合成、**離すと非表示**で song に戻る。
  - トグルではなく **ホールド（ゲート）発火**＝オルガン鍵盤／サンプラーパッド／Resolume の「Piano」トリガースタイル。
- **既定挙動（要確認・変更可）**：
  1. **複数キー同時押し**＝複数 short が重なる（**後押しが上**）。
  2. 押した瞬間に short を**頭出し再生**、離すと非表示（再押下で再び頭から）。
  3. short は既定で**全画面の最上位**（将来「特定 surface に出す」も選択可）。
  4. song は short に影響されず進み続ける。
- **実装の足場**：合成は `Compositor`→`EffectChain` の最終段に **short レイヤー合成**を1段足す。キー割当は
  `ControlHub`（コントローラ非依存の抽象マッピング層）に short トリガーを追加（KB→将来 MIDI パッドも同形で載る）。
- **段階**：M12 で song（リニア通し）、続けて short のホールド発火を載せる（M12 後半〜）。

---

## 3.6 エフェクト範囲（surface 指定 / 全体・準備で割当）

エフェクトの**適用範囲**を準備段階で決める。

| 範囲 | 意味 | 適用箇所 |
|---|---|---|
| **指定 surface のみ** | その埋め込み面だけに掛かる | `Compositor` の surface 合成前後 |
| **全体（フレーム）** | 合成後の画面全体に掛かる | 現 `EffectChain`（finalRT 系） |

- 準備で各エフェクトに **範囲（surface/全体）** を割り当て → 本番では強度/mix/onset 連動だけ動かす。
- データモデル：`EffectBase` に **target（Global / Surface[id]）** を持たせ、`EffectChain` が範囲別に適用（実装は段階的）。

---

## 4. その他ページ（最小要件）

- **SOURCE**：ベース動画 選択/再生/ループ/スクラブ、カメラ選択（`SourceVideo`/`SourceCamera`）。
- **FX**：エフェクト一覧（順序/ON-OFF/mix）＋選択エフェクトのパラメータ（`EffectChain`/`EffectBase.Parameters`）。
- **AUDIO**：入力選択、感度、帯域/RMS/onset メーター、音→パラメータのマッピング（`AudioAnalyzer`）。
- **OUTPUT**：FS/Syphon/NDI の ON/OFF・解像度、**Output Surface（出力変形＝MadMapper 流のメッシュ/コーナーピン）**。
- **SCENES**：プリセット保存/読込・次/前、**シーン発火**（クリップ/シーンのライブ起動・将来畳み込み）（`Preset`）。

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
> 画面：① Console Layout（上部バー＋モード切替[準備/本番]＋左ドック＋中央 preview＋右 Inspector＋
> **下部＝マルチトラック・タイムライン（映像 ×N／音声 ×N・音声は fade/mute）**＋下端に6ページタブ
> SOURCE/TRACK/FX/AUDIO/OUTPUT/SCENES）。② TRACK ページは MadMapper 風の領域マッピング
> （左 Surface 一覧＋中央 Input/Output 分割キャンバス＝N×M 制御点をドラッグしてメッシュワープ＋右 per-surface プロパティ）。
> ③ OUTPUT は**出力映像自体も MadMapper 風に変形**（メッシュ/コーナーピン）。
> ブランド色は希少な Amber、選択は Blue、数値は等幅。影は使わずヘアラインで奥行き。HTML/CSS（flex・grid 不使用）で。
