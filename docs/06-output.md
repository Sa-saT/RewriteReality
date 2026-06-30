# 06. 出力 — フルスクリーン / Syphon / NDI（Unity）

最終 RenderTexture を複数の出力先へ同時配信する `OutputManager`。

## 同時出力の全体像

```csharp
void Publish(RenderTexture finalRT){
    // 1) 画面/プロジェクター（出力カメラ or Blit to backbuffer）
    Graphics.Blit(finalRT, (RenderTexture)null); // バックバッファへ

    // 2) Syphon（KlakSyphon の SyphonServer に finalRT を割当）
    //    SyphonServer コンポーネントの sourceTexture = finalRT

    // 3) NDI（KlakNDI の NdiSender に finalRT を割当）
    //    NdiSender.sourceTexture = finalRT
}
```

KlakSyphon / KlakNDI は **RenderTexture を直接渡すだけ**で配信できる（GPU 共有）。

## 1. フルスクリーン / プロジェクター

- ビルド設定で **Standalone(mac)**・フルスクリーン
- **マルチディスプレイ**: 操作用 GUI ウィンドウ（オペレータ画面）と投影用を分ける
  - `Display.displays[i].Activate()` で複数ディスプレイ出力
  - もしくは投影は本アプリ、操作は別画面の UI に分離
- 投影先解像度に合わせる。内部処理解像度（1080p 等）と分離し最終段でスケール
- **プロジェクションマッピング的な出力補正**＝**Output Surface（出力変形）**。最終段にもう一段
  メッシュ/コーナーピン補正を掛ける。**M10・バックエンド実装済**（`OutputWarp`＋`OutputManager` 配線・Inspector 駆動／編集UIは #22）。
  ※ これは**埋め込み領域のワープ（`Compositor`・実装済）とは別段**＝完成映像“全体”をプロジェクタ面に合わせる。
  射影数学は両者で `WarpMath` を共有（二重定義を排除）。

## 2. Syphon（KlakSyphon・macOS）

- Resolume / MadMapper / VDMX 等へ **GPU 共有**で映像を渡す（Keijiro・現役保守）
- `SyphonServer` コンポーネントに `finalRT` を割り当てるだけ
- **入力**にも使える（`SyphonClient`）→ 他ソフトの映像をベース/カメラ代わりに取り込み可
- 「本アプリをエフェクタにし、合成は Resolume 側」という運用も可能

## 3. NDI（KlakNDI）

- ネットワーク越しに別 PC / 別ソフト（OBS, vMix, TouchDesigner 等）へ送信（Keijiro・現役保守）
- `NdiSender` に `finalRT` を割り当て。**SDK は KlakNDI が同梱/自動取得**（oF より導入が楽）
  - → **Unity 側は KlakNDI で完結**。brew の `libndi` は受け側/確認用の別層で必須ではない（`02` ローカルツールチェーン）
- 送信解像度・fps を抑えると帯域に優しい
- 用途: 配信 PC へ送る・現場の別マシンで受ける・バックアップ収録

## 録画（任意・将来）

- Unity **Recorder** パッケージ（無料）で最終出力を mp4/ProRes 書き出し
- リアルタイム録画は負荷が高い → 解像度/コーデックでフレーム落ち回避

## 出力解像度の設計

| 段 | 解像度 | 理由 |
|---|---|---|
| トラッキング | 640×360 程度 | 検出は低解像度で十分・高速 |
| 合成/エフェクト内部 | 1920×1080 | 品質と速度のバランス |
| 画面/Syphon/NDI 出力 | 投影先に合わせる | 最終段でスケール |

## 落とし穴

- **Syphon は macOS 専用**。Windows 対応もするなら **KlakSpout** に分岐（同じ Keijiro 製で楽）
- NDI は送信時の変換負荷に注意。送信解像度/fps を絞る
- フルスクリーンと Syphon/NDI を同時に回すと fill-rate を二重に食う → 解像度調整
- KlakSyphon/KlakNDI の **Unity 6 / Apple Silicon 対応バージョン**を M0 で確認

## 出力品質（既存アプリに引けを取らない設計）【確定 2026-06-27】

見た目・投影品質で既存 VJ アプリ（Resolume / MadMapper 等）に劣らないことを目標とする。
機能より**画質設定と作り込み**の差が効くので、以下をレバーとして押さえる。

- **色管理**：Linear color space ＋ 適切なガンマ。最終8bit化の前に**ディザリング**を入れて
  バンディング（縞）を防ぐ。
- **RT フォーマット**：エフェクトが多段で重なる箇所は **ARGBHalf（16bit）** にして色破綻・帯を回避
  （現状は ARGB32）。最終段で8bit化＋ディザ。
- **解像度/リサンプル最小化**：1080p（将来4K）をフル解像度で通し、無駄な縮小拡大を避ける。
  バイリニア、必要ならバイキュービック。
- **アンチエイリアス**：コーナーピン/メッシュ縁のジャギ対策（MSAA か縁のフェザー＝CornerPin の `_Feather`）。
- **60fps 維持＋フレームペーシング**：動画60fps＝描画60fps で揃える。GC スパイク回避を継続。
- **投影補正（Output Surface）**：完成映像“全体”をプロジェクタ面に合わせる出力段の変形＝**M10・バックエンド実装済**
  （`OutputWarp`・`08`/`11` B9／編集UIは #22）。技術（コーナーピン＋多pin メッシュワープ）は `Compositor`（埋め込み=Input Surface）と
  **`WarpMath` を共有**。今後 **エッジブレンド/フェザー・マスク**を足して MadMapper 相当のマッピング品質を担保。
- **出力経路**：プロジェクタ直結フルスクリーン（UI 非表示・操作画面と分離）＋ Syphon/NDI をフル品質で。
- **演出系（M4〜）**：VFX Graph / Shader Graph、プリセット/シーン切替、クロスフェード、
  Preview/Program、BPM 同期 が「操作して気持ちいい・本番で映える」体感を作る。

> 操作UI側の方針（Swift 不要・UI Toolkit 本命）は `07-control-ui.md` を参照。
