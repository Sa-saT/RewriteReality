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
