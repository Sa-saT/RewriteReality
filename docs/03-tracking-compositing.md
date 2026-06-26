# 03. トラッキング & 合成（Unity / OpenCvSharp）

「指定した箇所」へカメラ映像を埋め込む中核。動く対象に追従させる。

## 方式の選定

ベース動画は**事前に用意した固定の映像**。トラッキング対象は2系統：

1. **マーカー方式（主軸・推奨）**
   ベース動画の撮影時、埋め込みたい場所に **ArUco マーカー**を置いて撮影しておく。
   再生時にマーカーを検出 → その四隅にカメラ映像をピタッと貼る。
   - 長所: 高速・高精度・回転/遠近に強い・実装が枯れている
   - 短所: 撮影時の仕込みが必要

2. **特徴点トラッキング方式（補助）**
   マーカーが使えない既存映像向け。最初に領域を手動指定し、特徴点(ORB)/KLT で追跡。
   - 短所: テクスチャの乏しい面・速い動き・遮蔽に弱い

> 推奨運用: **ArUco を主軸**、ロスト時のフォールバックに KLT 補間。マーカー不可素材は特徴点方式。

### ⭐ 方式C: 四隅供給の抽象化＋オフライン・ベイク（**初期推奨・2026-06 追記**）

上の1・2は**実行時に検出を回す**前提。だが **ベース動画は事前に固定**なので、
検出結果も毎回同じ。**実行時に CV を回す必然性が無い**。そこで四隅の「供給元」を
抽象化し、初期は**オフラインで焼いた軌跡を読むだけ**にする。

```
〔事前/オフライン〕ベース動画 → Python(OpenCV+contrib, pipで一発) か Mocha でトラッキング
                              → 四隅を per-frame 書き出し（track.json, 正規化座標）
〔実行時/Unity〕track.json を読むだけ → ICornerSource → Compositor
```

- **最大の利点**: アプリ内に **OpenCvSharp が不要** → **Apple Silicon arm64 ネイティブの go/no-go が消える**。
  実行時依存は Unity＋Klak だけ（すべて arm64 対応確認済、`12`）。
- 高精度トラッカー（Mocha プレーナー）＋**手修正**が使える → 本番で破綻しにくい。
- 実行時の検出コスト・AsyncGPUReadback 遅延がゼロ → **60fps が楽**。
- マーカーを消した綺麗な最終ベース動画を別途用意できる。

```csharp
public interface ICornerSource { bool TryGetCorners(out Corners c); }
// BakedCornerSource : track.json を読む（初期）
// LiveCvCornerSource: 実行時 ArUco（将来、ライブ実景が必要になった時だけ）
```

`track.json`（正規化座標・出力解像度非依存）:
```json
{ "fps": 30, "frames": [
  { "i": 0, "visible": true,  "quad": [[x,y],[x,y],[x,y],[x,y]] },
  { "i": 1, "visible": false, "quad": null }
]}
```
`visible:false` は下記「ロスト対策」（フェード）にそのまま接続。

> **使い分け**: 埋め込み先がベース動画内（事前確定）なら **方式C で完結**し arm64 を回避。
> 将来「ライブの実景/プロジェクタ越しの実景にリアルタイムで貼る」要件が出た時のみ、
> `LiveCvCornerSource` を足して下記の実行時 ArUco（OpenCvSharp）に取り組む。

## OpenCvSharp のセットアップ（**実行時 CV を使う場合のみ＝将来オプション**）

> ⚠️ 初期推奨の**方式C（ベイク）を採るなら、この節は実装不要**。
> 以下は `LiveCvCornerSource`（ライブ実景対応）に進む将来時点で着手する。

- パッケージ: **OpenCvSharp4**（BSD, 管理層）＋ ネイティブランタイム（`.dylib`）
- macOS Apple Silicon の native(`.dylib`) を `Assets/Plugins/` に置き、Inspector で
  **arm64 / macOS スタンドアロン**にチェック
- ArUco は contrib モジュール → `OpenCvSharp.Aruco`

> ⚠️ **2026-06 調査の修正（重要）**: 公式 NuGet には **macOS arm64 ネイティブが無い**。
> → **arm64 ランタイムは「入手できない場合の対策」ではなく、最初から自前ビルドが本命**。
> 1. **本命**: OpenCV ＋ opencv_contrib(aruco) ＋ OpenCvSharpExtern を **arm64 で自前ビルド**
> 2. 第三者 `runtime.osx_arm64`(4.8.1-rc) を試す（aruco 同梱を先に検証）
> 3. 最終手段: Intel ビルド＋Rosetta（暫定・性能低下）
>
> 詳細根拠と各 I/O の対応状況は `12-feasibility-audit-2026-06.md`。

> **初期（方式C）は不要。** 将来 `LiveCvCornerSource`（ライブ実景）に進む時に限り、
> ここを最優先で潰す（**その時点での go/no-go**）。動かないまま実行時 CV 機能を広げない。
> 初期のコア開発（M0〜M8）はこの関門に依存しない。

## 背景フレームを CV に渡す（GPU→CPU を最小化）

VideoPlayer の出力（RenderTexture）から、トラッキング用に**縮小したピクセル**だけ取り出す。
同期読み出し（`ReadPixels`）はスタールするので **`AsyncGPUReadback`** を使う。

```csharp
// 縮小用 RT（例 640x360）に Blit してから非同期読み出し
Graphics.Blit(baseRT, smallRT);
AsyncGPUReadback.Request(smallRT, 0, TextureFormat.RGBA32, OnReadback);

void OnReadback(AsyncGPUReadbackRequest req){
    if(req.hasError) return;
    var data = req.GetData<byte>();      // NativeArray, GC負荷小
    // data → OpenCvSharp Mat（再利用バッファに詰める）
    detectQueue.Enqueue(matCopy);        // 別スレッドへ
}
```

## ArUco 検出 → 四隅（別スレッド）

検出はメインスレッドを止めないようワーカースレッドで実行し、結果（四隅）だけ受け取る。

```csharp
using OpenCvSharp;
using OpenCvSharp.Aruco;

var dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
var param = DetectorParameters.Create();

// ワーカースレッド内
CvAruco.DetectMarkers(mat, dict, out Point2f[][] corners, out int[] ids, param);
// 目的の id のマーカー corners[k] = tl,tr,br,bl（4点）
```

検出された四隅 = カメラ映像を貼り込む先の四角形。座標は縮小フレーム基準なので
元解像度・正規化座標へスケールする。

## ホモグラフィの計算

カメラ映像（矩形）→ ベース動画上の四角形 へのワープ行列 `H`。

```csharp
Point2f[] src = { new(0,0), new(1,0), new(1,1), new(0,1) }; // 正規化
Point2f[] dst = markerCorners;                              // 貼り先四隅
Mat H = Cv2.FindHomography(InputArray.Create(src), InputArray.Create(dst));
```

> **補足（まどろっこしさ回避）**: 合成の描画は **貼り先4頂点メッシュ＋射影UV補間だけで完結し、`H` は不要**。
> 射影補間の重み `q` は4隅（対角線交点）から直接求まる（下記「射影補間のやり方」）。
> `H`（`FindHomography`）を計算するのは **領域内マスク生成など補助用途が要る時だけ**。
> 不要なら `FindHomography` の呼び出し自体を省略してよい。

## ロスト対策（KLT 補間 / 予測 / 平滑化）

- 検出成功時の四隅を保持
- 未検出フレームは **Lucas-Kanade（`Cv2.CalcOpticalFlowPyrLK`）** で前回四隅を移動推定、
  または直近速度で外挿
- N フレーム連続ロストでフェードアウト
- 四隅を**時間方向に平滑化**（指数移動平均）してジッタ抑制

```csharp
smoothed = Vector2.Lerp(smoothed, detected, 0.5f);
```

## 合成（Compositor）— 四隅メッシュ＋射影補間

カメラ映像を「貼り先4頂点のクワッド」として RenderTexture に描く。
GPU の透視補正に任せれば自然な遠近ワープになるが、**ただの四角ポリゴンだと
アフィン歪み（折れ目）**が出る。正しくは**射影テクスチャ補間**にする。

### 射影補間のやり方
四角形の対角線交点から各頂点の重み `q` を求め、UV を `(u*q, v*q, q)` として
頂点に渡し、フラグメントで `uv.xy / uv.z` でサンプリングする（古典的な手法）。

```csharp
// 頂点: 貼り先4隅(クリップ/RT座標), UV3: (u*qi, v*qi, qi)
// フラグメント:
//   float2 uv = i.uvq.xy / i.uvq.z;
//   col = tex2D(_CamTex, uv);
```

### Compositor の処理（CommandBuffer か手描画で RT へ）

```csharp
void Composite(Texture baseTex, Texture camTex, Corners c, RenderTexture outRT){
    var prev = RenderTexture.active;
    RenderTexture.active = outRT;
        // 1) 背景を全面描画
        Graphics.Blit(baseTex, outRT);
        // 2) カメラを四隅メッシュ(射影UV)で重ね描き
        warpMat.SetTexture("_CamTex", camTex);
        warpMat.SetPass(0);
        Graphics.DrawMeshNow(quadMesh, Matrix4x4.identity); // 頂点=四隅, UV3=射影
    RenderTexture.active = prev;
}
```

### 馴染ませ
- 縁を**フェザリング**（アルファのソフトエッジ）
- 領域が矩形でなければ**マスク**テクスチャを乗算
- カメラ側に色調整（露出・WB）を一段かけて背景と馴染ませる

## トラッキング負荷の最適化

- 検出は**縮小フレーム**（640×360 等）で行い座標をスケール
- 検出は**ワーカースレッド**、メインは最新結果を読むだけ
- 検出は**数フレームに1回**、間は KLT/外挿で補間
- これで 4K 背景でも 60fps を狙える

## 多pin メッシュワープ（4点トラッキング＋手動ワープの2段）【確定 2026-06-27・実装済】

埋め込み合成は **2段構成**（決定A）。`Compositor` で実装済み。

- **段1 = 4点トラッキング**：マーカー追従（ArUco→ホモグラフィ）が平面の四隅 `Corners` を出す。
  平面追従は数学的に4点で決まるのでここは4点のまま（`track.json` も4点）。
- **段2 = 多pin 手動ワープ**：MadMapper の Grid/Mesh 相当。埋め込みクアッドの**ローカル正規化
  空間 [0..1]²** に N×M 制御点グリッドを持ち、ユーザーが点を動かして曲面/微調整を作る。
  追従とは独立に保存する。

合成手順：
1. 追従の4点 `Corners` から **ホモグラフィ H（単位正方形→四隅, Heckbert）** を解く。
2. 埋め込みを**細分化メッシュ**で描く。各頂点の **ローカル座標 p = 手動ワープグリッドの補間**、
   **スクリーン座標 = H·p（同次）**、camera UV = 規則グリッドUV。
3. **2×2・手動ワープ無し = 従来の4pin と同結果**（後方互換）。q は H の同次分母 w'。

操作UI（制御点ドラッグ／Surface 操作・MadMapper 参照）は `07-control-ui.md`。

## 関連
- 合成後フレームは `04-effects.md` のエフェクトチェーンへ
- 手動領域指定 UI は `07-control-ui.md`
