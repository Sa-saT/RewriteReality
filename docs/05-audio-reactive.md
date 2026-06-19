# 05. オーディオリアクティブ（Unity）

音に映像を反応させる。Terminal Slam の Spleeter + "Glitch Composer" 的な
「音の成分でグリッチを駆動する」発想を、リアルタイム解析で再現する。

## 入力ソース

- **ライブ入力**: マイク / オーディオIF（`Microphone.Start` → AudioClip）
- **内部再生音**: VJ で流している曲（`AudioSource` 再生）
- **外部から数値で受ける**: Ableton/Max から **OSC（OscJack）**で BPM・帯域を送ってもらう
  （最も安定。`07` 参照）

## 解析（AudioAnalyzer クラス）

Unity は FFT を標準提供（`GetSpectrumData`）。これで帯域エネルギーを取る。

```csharp
public struct AudioFeatures {
    public float rms;       // 全体音量
    public float bass;      // 低域 (20–150Hz)
    public float mid;       // 中域
    public float high;      // 高域 (4k–16kHz)
    public float beat;      // ビート時 1→減衰するエンベロープ
    public float bpm;       // 推定 or OSC 供給
    public float[] spectrum;// バンド別（シェーダ/VFXへ）
}
```

### 算出方法

```csharp
// 512〜1024 サンプルのスペクトルを取得
audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
// 周波数ビン → bass/mid/high に積算（サンプリングレートからビン幅算出）
// rms は GetOutputData の二乗平均
```

- **平滑化**: 各値を指数移動平均（`v = Mathf.Lerp(v, raw, k)`）
- **ビート検出**: 低域エネルギーの**移動平均との比**が閾値超えで onset 判定（簡易）
- **エンベロープ**: ビート時 `beat=1`、毎フレーム `beat *= 0.9f` で減衰 → パルス表現
- マイク入力時は `Microphone.Start` した AudioClip を無音 AudioSource 経由で解析

> Unity 標準で完結（追加パッケージ不要）。より高精度な BPM/onset が要るなら OSC で外部供給。

## エフェクトへの接続（マッピング）

`AudioFeatures` を各 `EffectBase.Apply` に渡し、Material/VFX の uniform を変調する。
**マッピングは ControlHub で設定可能に**（どの音成分 → どのパラメータか）。

| 音の成分 | 接続先（例） |
|---|---|
| `beat` | RGB シフト量パルス・パーティクル放出・グリッチ瞬発 |
| `bass` | フィードバックのズーム量・歪み強度・画面シェイク |
| `high` | ブロックノイズ密度・色収差・きらめき |
| `rms` | 全体のエフェクト mix・明度 |
| `spectrum[]` | シェーダ/VFX に配列で渡しスペクトラム表示／UV 変調 |

```csharp
// 例: RGB シフトをビートで蹴る
float shift = baseAmt + audio.beat * gain;     // gain は GUI/MIDI で調整
rgbShift.mat.SetVector("_Amount", new Vector2(shift, 0));
```

VFX Graph へは Exposed Property に `SetFloat`/`SetVector` で渡す。

## スレッド注意
- マイク/解析は基本メインスレッドだが、重い処理を別スレッド化する場合は
  `AudioFeatures` への書込みを **ダブルバッファ / lock** で保護
- draw 側は**最新スナップショットを読むだけ**

## 同期の質を上げる
- ライブ解析は数十 ms 遅れる。**OSC で BPM/拍を外部から受ける**と予測同期できる
- BPM ベースの LFO（拍同期 sin/saw）を内部生成し、エフェクトをリズミカルに動かす
  土台にすると VJ らしくなる
