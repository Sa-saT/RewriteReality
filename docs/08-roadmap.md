# 08. 実装ロードマップ（Unity）

小さく動かして積み上げる。各マイルストンで「画面に出る」ことを確認しながら進む。

## M0. 環境構築（土台 ＋ 関門の早期検証）
- [ ] Unity Hub → Unity 6 LTS（Apple Silicon）導入
- [ ] URP テンプレートで `app/` プロジェクト作成、本リポジトリに配置
- [ ] VFX Graph / Shader Graph / Input System を Package Manager で追加
- [ ] Klak（Syphon/NDI/Hap/Minis/OscJack）を git URL で追加し最小動作確認
- [ ] **OpenCvSharp を導入し ArUco 検出が macOS arm64 で動くことを確認**（最大の関門）
- **完了条件**: 各パッケージのサンプルが mac arm64 で動く

## M1. ソース表示
- [ ] `SourceVideo`: 動画を VideoPlayer → RenderTexture で全画面表示（HAP は KlakHap）
- [ ] `SourceCamera`: WebCamTexture を取得して表示
- **完了条件**: 動画とカメラが別々に映る

## M2. 合成（トラッキング無しの仮置き）
- [ ] `Compositor`: 画面内の**固定四角**にカメラ映像を**射影補間**でワープ合成
- [ ] 手動で四隅をドラッグ移動できる（`07` の手動領域指定）
- **完了条件**: 動画の上の好きな四角にライブカメラが嵌まる（核が見える）

## M3. トラッキング
- [ ] OpenCvSharp で ArUco 検出（ワーカースレッド ＋ AsyncGPUReadback）
- [ ] マーカー四隅 → ホモグラフィ → 合成へ接続
- [ ] 平滑化 ＆ ロスト時の KLT/外挿フォールバック
- **完了条件**: マーカーを動かすとカメラ映像が追従する

## M4. エフェクト基盤＋第一弾
- [ ] `EffectBase` ＋ `EffectChain`（RenderTexture ping-pong / Graphics.Blit）
- [ ] RGB シフト / ブロックグリッチ / 色調 / フィードバック を実装
- [ ] 実行時にエフェクトを足し引き・順序変更
- **完了条件**: エフェクトを足し引きでき、見た目が変わる

## M5. オーディオリアクティブ
- [ ] `AudioAnalyzer`: GetSpectrumData で帯域・ビート・RMS
- [ ] 音成分 → エフェクト/VFX パラメータのマッピング
- **完了条件**: 音に合わせてグリッチ等が反応する

## M6. 出力
- [ ] KlakSyphon サーバ配信
- [ ] フルスクリーン/プロジェクタ割当（マルチディスプレイ）
- [ ] KlakNDI 送信
- **完了条件**: 投影と他ソフト/別PCへ同時に出る

## M7. 操作・運用
- [ ] パラメータの ScriptableObject 化＋オペレータ UI
- [ ] MIDI ラーン（Minis）
- [ ] OSC 受信（OscJack, BPM/シーン）
- [ ] プリセット保存/読込・シーン切替
- **完了条件**: コントローラだけでライブ操作できる

## M8. 仕上げ
- [ ] パフォーマンス/GC チューニング（60fps 維持、解像度設計の最適化）
- [ ] VFX Graph パーティクル・データモッシュ等エフェクト拡充
- [ ] `.app` ビルド・素材同梱（StreamingAssets）・現場用の最小手順書
- [ ] （任意）Unity Recorder で録画、Windows 対応(KlakSpout) 検討

## M9.（将来オプション）iPhone LiDAR 深度レイヤー
- [ ] `IDepthSource`（任意・差し替え可能）を定義。深度が無ければ深度エフェクトは無効、コア無改修
- [ ] `RcamDepthSource`: Rcam3 Controller(iPhone, ARKit sceneDepth/LiDAR) → NDI → Unity NDI-in
- [ ] 深度キー合成 / オクルージョン / 深度ドリブン VFX（`04` の第5系統）
- **完了条件**: 深度付きカメラが NDI で届き、奥行きで合成/遮蔽/VFX が反応する
- **メモ**: 新規依存は実質ゼロ（KlakNDI 流用）。参照実装は手元の `Rcam3`。限界 256×192・〜5m

## 想定リスクと対策

| リスク | 対策 |
|---|---|
| OpenCvSharp が mac arm64 で通らない | M0 で最優先検証。自前ビルド or Intel+Rosetta フォールバック |
| Klak が Unity6/arm64 非対応バージョン | M0 で確認。対応版/コミットを選ぶ |
| トラッキングが不安定 | ArUco 主軸＋平滑化＋KLT 補間。素材撮影時にマーカー仕込み |
| 60fps 出ない | トラッキング低解像度化・間引き、KlakHap、RT 使い回し、出力解像度調整 |
| GC スパイクでカクつき | 毎フレーム new/LINQ 回避、NativeArray/バッファ再利用 |
| 音と映像の同期遅延 | OSC で BPM 外部供給＋拍同期 LFO で予測同期 |

## まず最初にやること
**M0 で OpenCvSharp の arm64 動作と Klak 動作を先に潰す** → M1 → M2。
M2 まで行けば「動画の指定箇所にライブカメラが嵌まる」というアプリの核が動いて見える。
そこを最短で目指す。
