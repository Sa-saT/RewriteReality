# 07. 操作・パラメータ制御（Unity）

VJ はライブで操作する。GUI・MIDI・OSC の3経路でパラメータを動かす。
すべて `ControlHub` が受けて一元管理し、各モジュールへ配る。

## パラメータの一元管理

- パラメータは **ScriptableObject（プリセット）** ＋ ランタイムの状態として保持
- これで **プリセット保存/読込（アセット or JSON）・シーン切替**が効く
- シーン切替 = 1曲・1場面分の設定をワンボタンで適用

```csharp
[CreateAssetMenu]
public class Preset : ScriptableObject {
    public float glitchAmount;
    public bool  feedbackOn;
    public List<EffectSetting> effects; // 順序・ON/OFF・mix
    // ...各モジュールの公開パラメータ
}
```

## 1. GUI（オペレータ画面）

- 制作・リハ用のオンスクリーン UI（スライダ/トグル/プリセット選択）
- **UI Toolkit** か **uGUI** で実装。投影出力には出さず操作画面側にだけ表示
- エフェクトの順序入替・ON/OFF・mix もここから
- 開発中は Inspector でも十分（パラメータを `[SerializeField]` 公開）

### 見た目（デザインシステム）

- **方向性 = DaVinci Resolve 系のダーク・高密度プロツール ＋ Cursor の editorial な抑制**のハイブリッド。
- 色・タイポ・コンポーネント・トークンの正本は **ルートの `DESIGN.md`**。実装時はそのトークンを
  UI Toolkit の **USS 変数**（または uGUI のテーマ）へ写像する。
- 要点: 暖色ダーク基調 / ブランド色は **Live Amber 1色（希少）**・選択は別系統の Blue /
  値・座標・OSC は **JetBrains Mono** / 奥行きはヘアラインのみ（影なし） /
  Cursor のタイムライン5パステルを **6パイプライン段（Source/Tracking/FX/Audio/Output/Scene）の状態ピル**へ転用。
- レイアウトは下部「ページタブ」＋中央 preview＋左右/下ドックの制御卓構成（`DESIGN.md` の Console Layout）。

## 2. MIDI（Minis / Keijiro・新 Input System）

- 本番のライブ操作。nanoKONTROL / APC mini / Launch 系
- ノブ → 連続パラメータ、ボタン → グリッチ瞬発トリガ/シーン切替
- 新 Input System のデバイスとして MIDI が見えるので Action にバインド
- **MIDI ラーン**（任意の CC を任意パラメータに割当）を実装すると現場で強い

```csharp
// Minis: MIDI CC を Input Action 経由で受け、learn 中のパラメータにバインド
void OnCc(InputAction.CallbackContext ctx){
    float v = ctx.ReadValue<float>();   // 0..1
    hub.ApplyMidi(currentCcNumber, v);
}
```

## 3. OSC（OscJack / Keijiro）

- Max/MSP・Ableton・別アプリ・スマホ（TouchOSC）から制御
- 用途:
  - **BPM/拍を外部から受ける**（`05` の予測同期）
  - 他ソフトと**シーン同期**
  - リモートからのパラメータ操作
- アドレス設計例: `/fx/glitch/amount 0.7`, `/scene/load 3`, `/audio/bpm 128`

## 操作対象（最低限）

| 対象 | 操作 |
|---|---|
| ソース | ベース動画の選択・再生/一時停止・ループ・スクラブ、カメラ選択 |
| トラッキング | マーカー/特徴点の切替、手動で領域四隅を指定、平滑化量 |
| エフェクト | 各 ON/OFF・強度・mix・順序、グリッチ瞬発トリガ |
| オーディオ | 入力選択、感度、音成分→パラメータのマッピング |
| 出力 | フルスクリーン切替、Syphon/NDI の ON/OFF、出力解像度 |
| シーン | プリセット保存/読込、次/前シーン |

## 手動での領域指定 UI（マーカー無し素材用）

- 投影プレビュー上で**四隅をドラッグ**して貼り先矩形を決める
  （`RectTransform` ハンドル or スクリーン座標のドラッグ処理）
- 決めた4点を特徴点トラッカーの初期領域として渡す（`03` 参照）
- 複数領域に対応するなら矩形を複数管理（将来）

## 操作UIの実装方針（Swift 不要・UI Toolkit 本命）【確定 2026-06-27】

操作画面は **Unity の UI Toolkit（USS/UXML）で実装**する。Swift/SwiftUI は使わない。
理由と方針：

- **UI Toolkit は Unity エディタ自身の基盤**＝プロ用ツールUIを作れる。見た目・操作性とも
  既存 VJ アプリ（Resolume / MadMapper 等）に引けを取らないものを作れる、と判断。
- `DESIGN.md` のトークン（暖色ダーク／Live Amber 希少／Selection Blue／JetBrains Mono／
  ヘアライン）を **USS 変数に写像**してスタイリング。
- **カスタムコントロール**（ノブ・XYパッド・縦フェーダー・メーター・波形）を自作。
  USS transition で滑らかなホバー/選択。
- **データバインディング**：`EffectParameter`（自己記述パラメータ）に双方向バインド。
- **RT プレビュー埋め込み**：base/カメラ/合成/最終 RT をパネルに直接表示（1プロセスなので容易）。
  MadMapper の Input/Output 分割や領域マッピングUIもこれで実現。
- 現状の最小 IMGUI（`OperatorGui`）は確認用。本UIは UI Toolkit へ置き換える。
- **Swift を検討した結論**：ネイティブな質感は上がるが、Unity が Metal 描画プロセスを握るため
  統合（UaaL / IPC）と RT プレビュー共有のコストが大きく、見返りが小さい。**不採用**。
  どうしても“ネイティブ操作卓/別マシン操作”が欲しくなったら、**OSC（OscJack）経由の外部
  コントロールサーフェス**（Swift アプリ / iPad TouchOSC）として後付けする。`ControlHub` は
  コントローラ非依存の抽象層なので、外部サーフェスは「もう一つのコントローラ」として乗る。

### 見た目はユーザーが UI Builder で作成・確定する【確定 2026-06-28】

- **役割分担**: 見た目/レイアウト（UXML/USS）は **ユーザー自身が Unity の UI Builder で直接オーサリング**
  して確定する。Claude（agent）は **UXML/USS の足場**（シェル・行テンプレ・USS トークン）と、新コントロールの
  **UXML テンプレ＋C# バインド**を用意する役。ビジュアルの作り込みはユーザーが行う。
- **単一ソース**: 見た目の正本は `Assets/UI/` の UXML/USS。**C# には見た目を書かない**
  （`OperatorUI.cs` は `Q<>()`＋`EffectParameter` バインドのみ）。新しい操作部品は
  「**まず UXML テンプレ → C# はバインドだけ**」の順で足す。
- **手順**: `Window > UI Toolkit > UI Builder` で `OperatorShell.uxml`／`FxRow.uxml`／`ParamRow.uxml`／
  `RewriteReality.uss` を開いて編集（ライブプレビュー・再コンパイル不要）。`PanelSettings` は
  **Scale Mode = Constant Pixel Size** で Game ビューに等倍表示（Scale With Screen Size だと固定 px 設計がはみ出す）。
- **進行**: デザイン確定後に領域マッピングUI（`03` の多pin メッシュワープ・MadMapper 参照）の実装へ進む。
- 現状の IMGUI 版 `OperatorGui` は確認用の併存（H で表示切替）。本UIは上記 UI Toolkit へ集約していく。
