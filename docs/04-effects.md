# 04. エフェクトパイプライン（Unity / URP）

合成済みフレームに、シェーダのチェーンでエフェクトをかける。
**「クラスを足すだけで増やせる」拡張基盤**が要件（今後追加したい）。

## パイプライン構造（RenderTexture ping-pong）

`Graphics.Blit(src, dst, material)` を連鎖させる。各エフェクトは Material（=シェーダ）1つ。

```csharp
public RenderTexture Process(RenderTexture input, in AudioFeatures audio){
    var a = rtA; var b = rtB;
    Graphics.Blit(input, a);
    foreach(var fx in effects){
        if(!fx.enabled) continue;
        fx.Apply(a, b, audio);   // 中身は Graphics.Blit(a, b, fx.mat)
        (a, b) = (b, a);         // 出力を次の入力へ
    }
    return a;
}
```

- 段数・順序・ON/OFF・mix(dry/wet) を実行時に変更可能
- 音声特徴 `AudioFeatures`（`05`）を各エフェクトの uniform に供給
- `rtA/rtB` は使い回し（毎フレーム生成しない＝GC 回避）

### dry/wet（mix）
各シェーダ末尾で `lerp(src, effected, _Mix)`。`_Mix` は GUI/MIDI で調整。

## 実装するエフェクト（要望の4系統）

### 1. グリッチ / データモッシュ
- **RGB シフト**: チャンネルごとに UV をずらす（量を音量で変調）
- **ブロックずらし**: 画面を短冊/ブロックに分け行ごとにランダム水平シフト
- **データモッシュ風**: 前フレームを保持し、動きベクトル方向に画素を引き伸ばす／
  前フレーム残留で I フレーム破損を模倣（`Feedback` と併用）
- **量子化/ビットクラッシュ**: 色を粗い階調に丸める
- トリガは音 or MIDI ボタンで瞬間的に強める（Terminal Slam の "Glitch Composer" 的運用）

```hlsl
// RGB shift (fragment, 抜粋)
float2 amt = _Amount;            // 音量で変調
float r = tex2D(_MainTex, uv + amt).r;
float g = tex2D(_MainTex, uv).g;
float b = tex2D(_MainTex, uv - amt).b;
return float4(r, g, b, 1);
```

### 2. オーディオリアクティブ
- 各エフェクトの強度に `audio.bass / high / rms / beat` を接続
- ビートで RGB シフトがパルス、低音で歪み増大、高音でブロックノイズ
- 詳細は `05-audio-reactive.md`

### 3. 色調 / フィードバック / 歪み
- **色調**: HSV 変換・LUT・ポスタライズ・コントラスト/ガンマ
- **フィードバック**: 前フレーム出力を `feedbackRT` に保持、ズーム/回転/移動して再合成
  （残像トレイル・無限ズーム）
- **歪み**: ノイズ場/サイン波による UV ディスプレイス、レンズ/色収差、kaleidoscope

```hlsl
// feedback (fragment, 抜粋)
float3 cur  = tex2D(_MainTex, uv).rgb;
float2 wuv  = mul(_Warp, uv - 0.5) + 0.5;   // ズーム/回転
float3 prev = tex2D(_PrevTex, wuv).rgb * _Decay;
return float4(max(cur, prev), 1);
```

### 4. パーティクル（オーバーレイ）
- **VFX Graph**（GPU パーティクル）を最終合成の上に重ねる
- 発生をビートに同期、輝度の高い領域から放出、フローに沿って流す
- VFX Graph に `AudioFeatures` を **Exposed Property** で渡して駆動
- 合成結果（finalRT）をカメラに描き、その手前に VFX Graph を重ねて出力にまとめる

### 5. （将来オプション）深度ドリブン — iPhone LiDAR / `IDepthSource`

`IDepthSource`（任意・差し替え可能な深度供給）が在る時のみ有効化する上位レイヤー。
深度マップが付くと、現状のRGBチェーンでは出来ない以下が解禁される。
Terminal Slam が ML（DeepLab 深度）でやっていた役割を、**推論でなくセンサー**で得る発想。

- **深度キー合成**: クロマキー不要で奥行きにより背景を抜く／前後で合成
- **オクルージョン**: 埋め込み映像の手前を横切る人/物が映像を自然に隠す
- **深度ドリブン VFX**: VFX Graph のパーティクル放出・ディスプレイスを深度で駆動（立体的な歪み）

> 取得経路: 深度は iPhone Pro 側にあり本体は macOS → **Rcam3 方式**で渡す。
> iPhone(ARKit sceneDepth, LiDAR)→ 色＋深度＋メタを **NDI** で送信 → Unity が NDI-in で受信。
> 参照実装は手元の `Rcam3`（Keijiro, 固定カメラ用途向け）。転送は既存スタックの **KlakNDI** を流用。
> 限界: 深度は約 256×192・〜5m・エッジノイズ → エフェクト/オクルージョン向き（精密計測は不向き）。
> 端末要件: **iPhone 12 Pro 以降の Pro 系が下限、理想は 15 Pro 以降**（11 Pro 以前・無印/Plus/SE は LiDAR 非搭載で不可）。
> 位置づけ: **コア（M2〜M6）には不要の追加レイヤー。導入は M8 以降のオプション**（`08`/`11`/`12`）。

## エフェクトの登録・拡張

```csharp
// EffectChain に並べる（Inspector or コード）
chain.Add<RgbShift>();
chain.Add<BlockGlitch>();
chain.Add<Feedback>();
chain.Add<ColorGrade>();
chain.Add<Displace>();
// 追加したくなったら → EffectBase を継承したクラス＋専用シェーダを書いて Add するだけ
```

各エフェクトは `EffectBase`（`01` 参照）を継承。シェーダは Shader Graph か手書き HLSL。

## シェーダ運用
- 手書き `.shader`（Unlit/Blit 用）か **Fullscreen Shader Graph**（Unity 6）で作成
- 共通 uniform: `_MainTex`, `_Time`(Unity 提供), `_Audio*`（音声）, `_Resolution`
- エディタ実行中にマテリアルパラメータを動かして即確認できる（VJ 制作が速い）

## パフォーマンス指針
- 段数が増えるほど fill-rate を食う → **内部処理 1080p、最終出力で拡大**も可
- ブラー系は**縮小バッファ**で処理して戻す
- 不要な Blit/クリアを避け ping-pong を徹底
- 毎フレームの `new`/LINQ を避けて GC スパイクを防ぐ
