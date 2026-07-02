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
│ ICornerSource │             │
│ 初期: Baked    │             │
│ (track.json)  │             │
│ 将来: LiveCv   │             │
│ (ArUco/別ｽﾚｯﾄﾞ)             │
└──────┬───────┘             │
       │ 貼り先四隅 Corners     │
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
  │ Control: Operator UI / Minis(MIDI) │
  │          / OscJack(OSC)            │ ──► 全モジュールのパラメータ
  └──────────────────────────────────┘
```

すべて **RenderTexture（GPU）** で受け渡す。初期（方式C）は CPU↔GPU 転送すら不要
（`track.json` を読むだけ）。将来 `LiveCvCornerSource` を使う時のみ、トラッキング用の
縮小読み出し（AsyncGPUReadback）を足す。

## モジュール設計（C# / MonoBehaviour）

| クラス | 役割 | 主な依存 |
|---|---|---|
| `SourceVideo` | ベース動画の再生・ループ・スクラブ・**ライブ速度可変（`PlaybackSpeed`・映像のみ）** → RenderTexture | VideoPlayer / KlakHap |
| `SourceCamera` | カメラ入力取得 → Texture | WebCamTexture / (任意)Syphon-in |
| `ICornerSource`(IF) | 四隅 `Corners` を供給する共通 IF。Compositor は出所を知らない | — |
| `BakedCornerSource` | 事前ベイクした `track.json` を読み四隅を返す（**初期推奨**） | Unity 標準のみ |
| `LiveCvCornerSource` | 実行時 ArUco 検出で四隅を返す（**将来オプション**） | OpenCvSharp(Aruco), AsyncGPUReadback |
| `Compositor` | 背景＋カメラを四隅メッシュで合成し 1 枚の RT に。四隅は `ICornerSource` 経由 | Material/Shader, CommandBuffer |
| `WarpMath`(static) | 単位正方形→四隅 の射影変換（Heckbert）の純粋数学。`Compositor`/`OutputWarp` が共有 | — |
| `OutputWarp` | （M10）finalRT 全体を四隅キーストーン＋多pin メッシュで出力変形（既定OFF=素通し） | CornerPin shader / `WarpMath` |
| `IDepthSource`(IF) | （任意）深度マップを供給。無ければ深度エフェクトを無効化 | — |
| `DepthCameraSource` | （将来）深度カメラ(Orbbec/RealSense等)の色＋深度を USB直結SDK or NDI で受信し供給 | 各社 SDK / (任意)KlakNDI |
| `EffectChain` | エフェクトを順に適用。範囲別に `Process`(Global=finalRT) / `ProcessSurface`(Surface単位) | `EffectBase` |
| `EffectBase`(抽象) | `Apply(src, dst, audio)` の共通 IF＋範囲 `scope`(Global/Surface)＋`targetSurfaceId`（M11） | Material + Graphics.Blit |
| `AudioAnalyzer` | FFT・ビート・帯域別エネルギー算出 | GetSpectrumData / Microphone |
| `OutputManager` | Fullscreen / Syphon / NDI へ配信 | KlakSyphon, KlakNDI |
| `ControlHub` | UI/MIDI/OSC を受けてパラメータを一元管理 | Minis, OscJack, ScriptableObject |
| `Preset`(SO) | シーン設定の保存/読込 | ScriptableObject / JSON |
| `AppMode` | （M11・実装済 backend）準備 Edit / 本番 Live のモード状態。本番は構成固定・`GuardStructuralEdit` でロック | — |
| `SurfaceManager` | （M11・実装済 backend）複数 Input Surface（`Surface`）を管理。`Compositor` が surface 単位に合成 | `Compositor` / `AppMode` |
| `Surface` | （M11・実装済）1埋め込み面の構成（追従四隅＋多pin warp＋content＋opacity）。warp API は `Compositor` と同規約 | `ICornerSource` / `WarpMath` |
| `Timeline` | （将来・M12/13）マルチトラック（映像/音声）の時間軸・再生ヘッド・クリップ配置 | `SourceVideo`/`track.json` 整合 |
| `AudioMixer`/`AudioTrack` | （将来・M13）音声トラックの内部再生＋ミックス（fade/mute/音量）。解析は最終ミックス | AudioSource / AudioListener |

> **AV ショー化の拡張（M10〜M13・段階的・2026-06-30）**: 上表の `AppMode`/`SurfaceManager`/`Surface`/`Timeline`/
> `AudioMixer` は「カメラ埋め込み VJ」→「タイムライン＋音声ミックスの AV ショー・ツール」拡張ぶん。
> **M10（`OutputWarp`）／M11 backend（`AppMode`/`SurfaceManager`/`Surface`＋`EffectBase.scope`）は実装済**
> （2026-07-01）。`EffectBase` は範囲指定 **scope（Global / Surface）＋targetSurfaceId** を持ち、`EffectChain` が
> `Process`(Global) / `ProcessSurface`(Surface) で範囲別適用。`Manager` は `SurfaceManager` 配置時のみ多surface経路、
> 未配置なら従来の単一 surface 経路にフォールバック（非破壊）。**UI（Surface一覧/選択/追加削除/per-surface プロパティ/
> モード切替/WARP ドラッグ）は #21・#22 で実装済**（Input/Output 分割ペインの見た目は page 切替 #31 へ）。
> `OutputManager` は**出力変形（Output Surface）**を持つ（旧 B4 出力段コーナーピンを内包）。仕様＝`07b`、決定＝`11` B9。

> **シーン配置（`Main.unity`・2026-07-03）**: `AppMode` と `SurfaceManager` を1つの GameObject **`Surfaces`** に
> 同居させて配置。`SurfaceManager.Mode` ← 同 GameObject の `AppMode`、`Manager.Surfaces` ← `SurfaceManager` を配線。
> `SurfaceManager.Surfaces` リストが**空のうちは Count==0 で従来の単一 Compositor 経路にフォールバック**（＝挙動不変）。
> オペレータUI の **＋Surface**（`SurfaceManager.Add`・準備 Edit のみ）で面を足すと多surface 合成に切替。
> 本番用に面を固定したい場合は停止状態で `SurfaceManager.Surfaces` に追加して保存（Play 中の追加は非永続）。
> `OperatorUI` は `SurfaceManager`/`AppMode`/`Compositor` を未指定時 `FindFirstObjectByType` で自動取得する。

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
推奨は「**専用の処理用 Camera は持たず、`CornerSource`→`Compositor`→`EffectChain` を
自前の `Manager` が毎フレーム RenderTexture 上で実行**」する構成（VJ的に制御しやすい）。

```
Manager.LateUpdate():
  source.Tick()                 // VideoPlayer / WebCamTexture 更新
  // audio は別経路で常時解析
  cornerSource.TryGetCorners(out corners)        // 初期:Baked=track.json読込 / 将来:LiveCv
  compositor.Composite(baseRT, camTex, corners) -> sceneRT
  effectChain.Process(sceneRT, audio)           -> finalRT
  output.Publish(finalRT)        // 画面 + Syphon + NDI
```

## パフォーマンス設計の原則

1. **全処理を RenderTexture 上で完結**。（将来の実行時CV時のみ）OpenCV に渡すのは AsyncGPUReadback で取った縮小フレームのみ。
2. **トラッキングは間引く**（実行時CV時のみ）: 毎フレームでなく数フレームに1回検出、間は前回四隅を補間/予測（KLT）。初期の方式Cは事前ベイク済みのため該当しない。
3. **HAP は KlakHap** で GPU デコード（4K でも軽い）。通常の mp4 は VideoPlayer 標準。
4. **RenderTexture は使い回す**（毎フレーム生成しない）。
5. **GC 対策**: 毎フレームの `new`/LINQ/ボクシングを避け、配列・バッファを再利用。
6. 目標 **60fps**。重ければトラッキング解像度・エフェクト段数・出力解像度を落とす。
