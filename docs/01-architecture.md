# 01. システム構成・データフロー（Unity / URP）

## 全体データフロー

```
┌──────────────┐     ┌──────────────┐
│ ベース動画      │     │ ライブカメラ    │
│ VideoPlayer   │     │ WebCamTexture │
│ (+KlakHap)    │     │               │
│ → RenderTex   │     │ → Texture     │
└──────┬───────┘     └──────┬───────┘
       │ 背景フレーム          │ カメラ
       │                     │
       ▼                     │
┌──────────────┐             │
│ Tracker       │             │
│ OpenCvSharp   │             │
│ ArUco→四隅→H  │             │
│ (別スレッド+    │             │
│ AsyncGPUReadback)           │
└──────┬───────┘             │
       │ 貼り先四隅(H)          │
       ▼                     ▼
┌──────────────────────────────────┐
│ Compositor (RenderTexture)         │
│ 背景を描画 → カメラを四隅メッシュ      │
│ (射影補間)でワープ合成               │
└──────────────┬───────────────────┘
               │ sceneRT
               ▼
┌──────────────────────────────────┐
│ EffectChain (Graphics.Blit ping-pong)│
│ [Glitch]→[ColorGrade]→[Feedback]  │
│ →[Distort] (+VFX Graph パーティクル) │ ◄─ AudioFeatures
└──────────────┬───────────────────┘   (BPM/低音/高音/RMS)
               │ finalRT
        ┌──────┴──────┬──────────┐
        ▼             ▼          ▼
  ┌──────────┐  ┌─────────┐ ┌─────────┐
  │Fullscreen│  │KlakSyphon│ │ KlakNDI │
  │/Projector│  │ Server  │ │ Sender  │
  └──────────┘  └─────────┘ └─────────┘

  ┌──────────────────────────────────┐
  │ Control: ofxGui相当=UI / Minis(MIDI)│
  │          / OscJack(OSC)            │ ──► 全モジュールのパラメータ
  └──────────────────────────────────┘
```

すべて **RenderTexture（GPU）** で受け渡し、CPU↔GPU 転送はトラッキング用の
縮小読み出し（AsyncGPUReadback）だけに限定する。

## モジュール設計（C# / MonoBehaviour）

| クラス | 役割 | 主な依存 |
|---|---|---|
| `SourceVideo` | ベース動画の再生・ループ・スクラブ → RenderTexture | VideoPlayer / KlakHap |
| `SourceCamera` | カメラ入力取得 → Texture | WebCamTexture / (任意)Syphon-in |
| `ICornerSource`(IF) | 四隅 `Corners` を供給する共通 IF。Compositor は出所を知らない | — |
| `BakedCornerSource` | 事前ベイクした `track.json` を読み四隅を返す（**初期推奨**） | Unity 標準のみ |
| `LiveCvCornerSource` | 実行時 ArUco 検出で四隅を返す（**将来オプション**） | OpenCvSharp(Aruco), AsyncGPUReadback |
| `Compositor` | 背景＋カメラを四隅メッシュで合成し 1 枚の RT に。四隅は `ICornerSource` 経由 | Material/Shader, CommandBuffer |
| `IDepthSource`(IF) | （任意）深度マップを供給。無ければ深度エフェクトを無効化 | — |
| `RcamDepthSource` | （将来）iPhone LiDAR の色＋深度を NDI-in で受信し供給 | KlakNDI / Rcam3 方式 |
| `EffectChain` | エフェクトを順に適用するパイプライン | `EffectBase` |
| `EffectBase`(抽象) | `Apply(src, dst, audio, params)` の共通 IF | Material + Graphics.Blit |
| `AudioAnalyzer` | FFT・ビート・帯域別エネルギー算出 | GetSpectrumData / Microphone |
| `OutputManager` | Fullscreen / Syphon / NDI へ配信 | KlakSyphon, KlakNDI |
| `ControlHub` | UI/MIDI/OSC を受けてパラメータを一元管理 | Minis, OscJack, ScriptableObject |
| `Preset`(SO) | シーン設定の保存/読込 | ScriptableObject / JSON |

> **四隅供給の抽象化（重要な設計判断・2026-06）**: 旧 `Tracker` は `ICornerSource` に一般化。
> ベース動画は事前固定なので、初期は **`BakedCornerSource`（オフラインで焼いた `track.json` を読むだけ）**
> を採用し、**アプリ内から OpenCvSharp を外す**＝ Apple Silicon arm64 ネイティブの go/no-go を発生させない。
> 実行時依存は Unity＋Klak だけになる。将来ライブ実景へリアルタイムに貼る要件が出たら
> `LiveCvCornerSource` を1クラス足すだけ（Compositor 以降は無改修）。詳細は `03`・`12`。

### エフェクト拡張の肝（`EffectBase`）

新エフェクトを「クラスを1つ足すだけ」で追加できるようにする。
「今後追加したい」要望の技術的担保。

```csharp
public abstract class EffectBase : MonoBehaviour {
    public bool enabled = true;
    [Range(0,1)] public float mix = 1f;   // ドライ/ウェット
    protected Material mat;                 // 専用シェーダ
    public abstract string Name { get; }
    // src を読み dst に書く。audio は音声特徴
    public abstract void Apply(RenderTexture src, RenderTexture dst,
                               in AudioFeatures audio);
}
```

`EffectChain` は `List<EffectBase>` を持ち、2 枚の RenderTexture を
**ping-pong** しながら順に `Apply`（中身は `Graphics.Blit(src, dst, mat)`）を呼ぶ。
順序入替・ON/OFF・mix も実行時に変更可能。

## メインループ（実行順序の制御）

Unity の `Update` / `LateUpdate` ＋ カメラの `OnRenderImage` か、
**URP の ScriptableRendererFeature / Blit** で合成チェーンを駆動する。
推奨は「**専用の処理用 Camera は持たず、`Tracker`→`Compositor`→`EffectChain` を
自前の `Manager` が毎フレーム RenderTexture 上で実行**」する構成（VJ的に制御しやすい）。

```
Manager.LateUpdate():
  source.Tick()                 // VideoPlayer / WebCamTexture 更新
  // audio は別経路で常時解析
  tracker.Tick(baseRT)          // H を更新（間引き・別スレッド）
  compositor.Composite(baseRT, camTex, H) -> sceneRT
  effectChain.Process(sceneRT, audio)      -> finalRT
  output.Publish(finalRT)        // 画面 + Syphon + NDI
```

## パフォーマンス設計の原則

1. **全処理を RenderTexture 上で完結**。OpenCV に渡すのは AsyncGPUReadback で取った縮小フレームのみ。
2. **トラッキングは間引く**: 毎フレームでなく数フレームに1回検出、間は前回 `H` を補間/予測（KLT）。
3. **HAP は KlakHap** で GPU デコード（4K でも軽い）。通常の mp4 は VideoPlayer 標準。
4. **RenderTexture は使い回す**（毎フレーム生成しない）。
5. **GC 対策**: 毎フレームの `new`/LINQ/ボクシングを避け、配列・バッファを再利用。
6. 目標 **60fps**。重ければトラッキング解像度・エフェクト段数・出力解像度を落とす。
