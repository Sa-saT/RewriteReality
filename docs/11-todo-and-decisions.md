# 11. 後日の実装タスク & 未決の選択（質問）

実装を再開するときの起点。**A=やることリスト**、**B=選定結果（確定済み）**。
**B は 2026-06-23 に①〜⑩すべて確定**（下記）。次回は A の M0 から実装に入る。

---

## A. 実装タスク（マイルストン別チェックリスト）

`docs/08-roadmap.md` の詳細版。上から順に。

### M0. 環境・依存（土台）※今ここ
- [ ] Klak パッケージ投入（git URL）: KlakSyphon / KlakNDI
- [ ] VFX Graph / Shader Graph をパッケージ追加
- [ ] OscJack / Minis（MIDI）を追加
- [ ] 各パッケージの最小サンプルが mac arm64 で動作することを確認
- **メモ**: 初期は方式C（ベイク）採用。**OpenCvSharp/arm64 は M0 では扱わない**
  （将来 `LiveCvCornerSource` で着手）。KlakHap は任意・M8 で検討（初期は通常 mp4＝VideoPlayer）。

### M1. ソース表示
- [ ] `SourceVideo`: 動画 → RenderTexture（必要なら KlakHap）
- [ ] `SourceCamera`: WebCamTexture 取得・表示

### M2. 合成（トラッキング無し仮置き）
- [ ] `Compositor`: 固定四角にカメラを**射影補間**でワープ合成
- [ ] 四隅を手動ドラッグで指定

### M3. トラッキング（**初期は方式C＝ベイクで実装。実行時 CV は将来オプション**）
- [ ] `ICornerSource` IF ＋ `BakedCornerSource`（`track.json` 読込）
- [ ] オフラインで四隅をベイク（Python OpenCV / Mocha）→ `track.json` 出力（**fps=60、ソース動画と一致**）
- [ ] 四隅 → 合成へ接続、平滑化 ＆ `visible:false` 時のフェード
- [ ] （将来・ライブ実景が必要な時のみ）`LiveCvCornerSource`: OpenCvSharp で
      ArUco 検出（ワーカースレッド + AsyncGPUReadback）→ ここで初めて arm64 ビルドに着手

### M4. エフェクト基盤＋第一弾
- [ ] `EffectBase` ＋ `EffectChain`（RenderTexture ping-pong / Graphics.Blit）
- [ ] RGB シフト / ブロックグリッチ / 色調 / フィードバック
- [ ] 実行時に順序・ON/OFF・mix 変更

### M5. オーディオリアクティブ
- [ ] `AudioAnalyzer`: GetSpectrumData で帯域・ビート・RMS
- [ ] 音成分 → エフェクト/VFX パラメータのマッピング

### M6. 出力
- [ ] KlakSyphon サーバ配信
- [ ] フルスクリーン/プロジェクタ割当（マルチディスプレイ）
- [ ] KlakNDI 送信

### M7. 操作・運用
- [ ] パラメータの ScriptableObject 化＋オペレータ UI
- [ ] MIDI ラーン（Minis）/ OSC 受信（OscJack, BPM/シーン）
- [ ] プリセット保存/読込・シーン切替

### M8. 仕上げ
- [ ] パフォーマンス/GC チューニング（60fps 維持）
- [ ] VFX Graph パーティクル・データモッシュ拡充
- [ ] `.app` ビルド・素材同梱（StreamingAssets）・現場手順書
- [ ] （任意）Unity Recorder で録画、Windows 対応(KlakSpout)

### M9.（将来オプション）iPhone LiDAR 深度レイヤー
> コア完成後に追加。新規依存は実質ゼロ（KlakNDI を流用）。参照実装は手元の `Rcam3`。
> 端末要件: **iPhone 12 Pro 以降の Pro 系が下限、理想は 15 Pro 以降**（11 Pro 以前・無印/Plus/SE は不可）。
- [ ] `IDepthSource` IF を定義（無ければ深度エフェクト無効、コアは無改修）
- [ ] `RcamDepthSource`: Rcam3 Controller(iPhone, ARKit sceneDepth) → NDI → Unity NDI-in で受信
- [ ] 深度キー合成 / オクルージョン / 深度ドリブン VFX を `04` のチェーンへ
- [ ] 限界（256×192・〜5m・エッジノイズ）と遅延/同期のフェイルセーフ確認

---

## B. 選定結果（2026-06-23 確定）

**①〜⑩すべて確定済み**。各項目の **採用値＝太字**。番号は選定セッションの①〜⑩に対応。

### B1. 埋め込み領域 ＝ ④【確定】
- 同時に埋め込む領域は **まず1領域**（複数対応は後で）。
- ArUco 辞書は **保留**（実行時 CV は方式C採用により将来 `LiveCvCornerSource` 時に決定）。
- マーカー無し素材（特徴点）対応は **M3 以降の後回し**。

### B2. ソース素材 ＝ ①③【確定】
- 形式: **mp4（H.264）**。高解像度/高ビットレート時のみ HAP/`.mov`＋KlakHap を **M8** で検討。
- 解像度/フレームレート: 内部処理 **1080p / 60fps**（レンダ目標60fpsと揃え 1描画=1動画でジャダー回避）。
- カメラ入力: **まず1台**（ソース＝内蔵/USB/キャプチャ/Syphon-in は実装時に確定）。

### B3. オーディオ ＝ ⑤【確定】
- 音源/解析: **アプリ内 FFT 解析＋簡易 onset でビート検出**（外部依存なしで完結）。
- 外部 OSC（BPM/トリガ）受信は **将来オプション**。

### B4. 出力の優先順位 ＝ ⑥【確定】
- 通す順: **フルスクリーン → Syphon → NDI**。
- プロジェクタ台形補正（**出力段コーナーピン＝4頂点ワープ）を入れる**。

### B5. 操作（コントローラ）＝ ⑦【確定】
- MIDI 機種: **未定/未所持** → **コントローラ非依存の抽象マッピング層**で設計
  （CC番号を直書きせず「操作アクション」を Input System Action Map / ScriptableObject でバインド差替可能に）。
- **早期は キーボード/GUI で操作**。機種購入後に Minis で MIDI バインドを足すだけ（nanoKONTROL2/APC mini どちらでも載る）。
- OSC 連携先（Ableton/Max/TouchOSC）は **未定**。

### B6. リポジトリ / 構成 ＝ ⑧【確定】
- `RewriteRealityProject/RewriteReality/` の **1階層ネストのまま**。
- 動画素材は **リポジトリに入れず StreamingAssets をローカル管理**、dylib は将来 **Git LFS**。

### B7. エフェクトの初期実装範囲 ＝ ⑨【確定】
- 初期4種: **RGB シフト / ブロックグリッチ / フィードバック / 色調**。
- パーティクル（VFX Graph）は **M4 後半〜M8**。

### B8. iPhone LiDAR 深度（将来）＝ ⑩【確定】
- **M9 のオプション**。コアは深度無しで完成させる。
- 用途（深度キー合成/オクルージョン/深度ドリブン VFX）と固定カメラ前提は M9 着手時に確定。

---

## メモ
- 設計の詳細根拠は `docs/00`〜`10`。本書はそこからの「実行用の抜き出し＋確定した選定」。
- B は確定済み。A の各 M を上から実装していく（次は M0）。
