# 11. 後日の実装タスク & 未決の選択（質問）

実装を再開するときの起点。**A=やることリスト**、**B=決めるべき選択（質問）**。
B を先に決めると A の実装方針が固まる。

---

## A. 実装タスク（マイルストン別チェックリスト）

`docs/08-roadmap.md` の詳細版。上から順に。

### M0. 環境・依存（土台）※今ここ
- [ ] Klak パッケージ投入（git URL）: KlakSyphon / KlakNDI / KlakHap
- [ ] VFX Graph / Shader Graph をパッケージ追加
- [ ] OscJack / Minis（MIDI）を追加
- [ ] **OpenCvSharp を arm64 で導入し ArUco 検出が動くことを確認**（最大の関門）
- [ ] 各パッケージの最小サンプルが mac arm64 で動作することを確認

### M1. ソース表示
- [ ] `SourceVideo`: 動画 → RenderTexture（必要なら KlakHap）
- [ ] `SourceCamera`: WebCamTexture 取得・表示

### M2. 合成（トラッキング無し仮置き）
- [ ] `Compositor`: 固定四角にカメラを**射影補間**でワープ合成
- [ ] 四隅を手動ドラッグで指定

### M3. トラッキング（**初期は方式C＝ベイクで実装。実行時 CV は将来オプション**）
- [ ] `ICornerSource` IF ＋ `BakedCornerSource`（`track.json` 読込）
- [ ] オフラインで四隅をベイク（Python OpenCV / Mocha）→ `track.json` 出力
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
- [ ] `IDepthSource` IF を定義（無ければ深度エフェクト無効、コアは無改修）
- [ ] `RcamDepthSource`: Rcam3 Controller(iPhone, ARKit sceneDepth) → NDI → Unity NDI-in で受信
- [ ] 深度キー合成 / オクルージョン / 深度ドリブン VFX を `04` のチェーンへ
- [ ] 限界（256×192・〜5m・エッジノイズ）と遅延/同期のフェイルセーフ確認

---

## B. 決めるべき選択（質問）

実装前に確定したい項目。**太字が暫定の推奨**。

### B1. 埋め込み領域
- 同時に埋め込む領域は **1つ** か、複数か？ → 暫定: **まず1つ**、後で複数対応
- ArUco 辞書: **`DICT_4X4_50`** で十分か（マーカー種類数）？
- マーカー無し素材（特徴点トラッキング）への対応は **M3 以降の後回し**でよいか？

### B2. ソース素材
- ベース動画の形式: 通常 mp4 か **HAP**（高解像度で軽い）か？ → 解像度・尺次第
- 想定解像度: 内部処理 **1080p** / 出力は投影先次第、で問題ないか？
- カメラ入力: 内蔵 / 外部USB / キャプチャ / Syphon-in のどれを主に使う？複数台は？

### B3. オーディオ
- 音源: **ライブ入力（マイク/IF）** か、アプリ内再生か、外部から **OSC で BPM 供給**か？
- ビート同期の精度要求は？（簡易 onset で足りるか、OSC 必須か）

### B4. 出力の優先順位
- 最初に通すのは **フルスクリーン** → **Syphon** → NDI の順でよいか？
- 投影のプロジェクションマッピング補正（出力側コーナーピン）は要るか？

### B5. 操作（コントローラ）
- 使う MIDI コントローラの機種は？（nanoKONTROL2 / APC mini など）→ マッピング設計に直結
- OSC 連携先（Ableton / Max / TouchOSC）はあるか？

### B6. リポジトリ / 構成
- 現状 `RewriteRealityProject/RewriteReality/` の **1階層ネストのまま**でよいか（推奨）、
  フラット化するか？
- 大容量バイナリ（動画素材・OpenCvSharp の .dylib）は **Git LFS** を使うか？
  → 暫定: **動画素材はリポジトリに入れず StreamingAssets をローカル管理**、dylib は LFS 検討

### B7. エフェクトの初期実装範囲
- 最初に実装するエフェクトは **RGB シフト / グリッチ / フィードバック / 色調** の4種でよいか？
- パーティクル（VFX Graph）は **M4 後半〜M8** でよいか？

### B8. iPhone LiDAR 深度（将来）
- 深度レイヤーを導入するか？ → 暫定: **M9 のオプション**。コアは深度無しで完成させる
- 用途は **深度キー合成 / オクルージョン / 深度ドリブン VFX** のどれを優先？
- 撮影/運用は **固定カメラ**前提でよいか（Rcam3 が得意な構成）？

---

## メモ
- 設計の詳細根拠は `docs/00`〜`10`。本書はそこからの「実行用の抜き出し＋未決事項」。
- B が固まり次第、A の各 M を上から実装していく。
