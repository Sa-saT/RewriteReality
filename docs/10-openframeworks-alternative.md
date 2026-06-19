# 10. openFrameworks を軸にした場合の代替設計（参考）

土台は **Unity に確定**（`00`〜`09`）。本書は「もし oF を軸にしたら」を気になる点として
まとめた**参考資料**。最終採用ではない。

## oF を選ぶ動機（向いているケース）

- **完全 OSS・純 C++** で、エンジンのブラックボックスを持ちたくない
- GPU/メモリを**直接最適化**して限界性能を出したい
- 軽量な**単体 .app** を素朴に配りたい（Unity ランタイムの重さを避けたい）
- メディアアートの伝統的ワークフローに乗りたい

## oF を避けたい理由（=今回 Unity にした理由）

- 主要アドオンの**メンテが止まりがち**（`ofxCv` / `ofxAruco` / `ofxHapPlayer` /
  `ofxAudioAnalyzer` など）。oF 0.12 / Apple Silicon 対応が不揃い
- Syphon/Hap 系アドオンの保守が弱点。導入時のビルド地獄が起きやすい
- realtime 映像の主流から外れつつあり、情報・事例が Unity/TD より薄い

> ただし「アドオン経由をやめ、**OpenCV 本体や NDI SDK を直リンク**する」設計にすれば
> 保守リスクはかなり下げられる（下記）。

## oF 版アーキテクチャ（Unity 版と対応）

データフロー・モジュール分割は `01` と同じ思想。実装手段だけ置換する。

| 役割 | Unity 版 | oF 版 |
|---|---|---|
| 動画再生 | VideoPlayer / KlakHap | `ofVideoPlayer` / `ofxHapPlayer`(要保守確認) |
| カメラ | WebCamTexture | `ofVideoGrabber` |
| マーカー/H | OpenCvSharp(Aruco) | **OpenCV を直リンク**（aruco contrib）/ ofxCv は補助 |
| 合成 | RT＋射影メッシュ | `ofFbo`＋`ofShader`（射影 UV メッシュ） |
| エフェクト | Graphics.Blit chain | `ofFbo` ping-pong ＋ `ofShader` |
| パーティクル | VFX Graph | Transform Feedback / ping-pong FBO 自前実装 |
| 音声FFT | GetSpectrumData | `ofSoundStream`＋`ofxFft`（or 自前 FFT） |
| Syphon | KlakSyphon | `ofxSyphon`（保守弱め・要確認） |
| NDI | KlakNDI | `ofxNDI`＋**NDI SDK 直リンク** |
| MIDI/OSC | Minis / OscJack | `ofxMidi` / `ofxOsc`（oF 標準同梱で比較的安定） |
| GUI | uGUI/UI Toolkit | `ofxGui` |

## 保守リスクを下げる oF 戦略

1. **CV はアドオンに頼らず OpenCV 本体を直接リンク**
   - Homebrew 等で現行 OpenCV(contrib 込み)を入れ、`opencv2/aruco.hpp` を直接使う
   - `ofxCv` は型変換ヘルパ程度に留める（`ofPixels`↔`cv::Mat`）
2. **NDI は公式 SDK を直リンク**（`ofxNDI` はラッパとして薄く使う）
3. **Syphon は ofxSyphon に依存**するしかない（mac の弱点）→ 動かない場合は
   出力を NDI に寄せる、または最終ミックスを別ソフトに委ねる
4. **HAP が不安定なら ProRes/通常コーデック**で妥協、または GPU デコードを諦め解像度を下げる

## oF 版の実装難所（Unity 比）

- パーティクルや FFT を**自前で書く比重が増える**（Unity は VFX Graph / 標準 FFT で済む）
- Syphon/Hap/Aruco の**ビルド通しが最初の山**
- ノードエディタが無く、エフェクトの試行錯誤は**コード往復**で遅め

## 結論（参考）

- oF は「**純 OSS・最大自由度・軽量配布**」が欲しい人向け。性能上限は高い。
- だが今回の優先（**無料＋保守性＋実装速度**）では **Unity が優位**。
- oF を採るなら「**アドオン依存を最小化し OpenCV/NDI を直リンク**」を鉄則にすること。

> 必要になれば、この方針で oF 版の詳細設計（`01`〜`08` 相当）も別途起こせる。
