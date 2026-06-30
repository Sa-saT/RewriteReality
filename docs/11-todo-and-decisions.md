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

### M9.（将来オプション）深度レイヤー（深度カメラ）
> コア完成後に追加。`IDepthSource` で差替可能ゆえ本体は無改修。
> 機材要件: **深度カメラ（深度センサー）**。候補=Orbbec Femto（Apple Silicon SDK あり）/ RealSense D4xx。
> 旧案の iPhone Pro LiDAR（Rcam3 方式）は Pro 機がある場合の参考実装で**非前提**（iPad/iPhone Pro 縛りは撤回）。
- [ ] `IDepthSource` IF を定義（無ければ深度エフェクト無効、コアは無改修）
- [ ] `DepthCameraSource`: 深度カメラの色＋深度を USB 直結 SDK or NDI で受信して供給
- [ ] 深度キー合成 / オクルージョン / 深度ドリブン VFX を `04` のチェーンへ
- [ ] 限界（センサー依存・〜数 m・低解像度・エッジノイズ）と遅延/同期のフェイルセーフ確認

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

### B8. 深度レイヤー（将来・深度カメラ）＝ ⑩【更新 2026-06-30】
- **M9 のオプション**。コアは深度無しで完成させる。
- 取得源は **深度カメラ（深度センサー・例: Orbbec Femto / RealSense）**。`IDepthSource` で差替可能。
- 旧案の **iPhone/iPad Pro LiDAR 縛りは撤回**（Pro 機なし）。Rcam3 方式は Pro 機がある場合の参考実装に格下げ。
- 用途（深度キー合成/オクルージョン/深度ドリブン VFX）と固定カメラ前提・機種は M9 着手時に確定。

### B9. AV ショー化の拡張【決定 2026-06-30・段階的実装】
「カメラ埋め込み VJ」から **タイムライン＋音声ミックスを持つ AV ショー・ツール**へ拡張。設計（`07b`/docs）に正式反映し、
**実装は段階的**（土台を壊さず積む）。詳細仕様＝[`07b-operator-ui-brief.md`](07b-operator-ui-brief.md)。

- **画面下部＝マルチトラック・タイムライン**（映像 ×N／音声 ×N）。従来の FX 一覧は FX ページ/右ドックへ移設。
- **音声＝内部再生＋外部解析の両対応**：タイムライン音声トラックを**アプリが再生・ミックス**（fade/mute/音量）。
  外部（DJ/Ableton）入力の**解析のみ**運用も可。本番構成で①内部完結②外部解析③両方を切替。解析は最終ミックス(AudioListener)。
- **2 モード（準備 Edit / 本番 Live）**：準備で surface 配置・**エフェクト範囲（surface 指定/全体）**・タイムライン・出力変形を仕込み、
  本番は**準備した surface にライブ動画を埋め込み**＋値の即時操作（構成は固定）。
- **Output Surface（出力変形）を正式機能に格上げ**：出力映像も MadMapper 流にメッシュ/コーナーピンで変形（旧 B4 の出力段コーナーピンを内包）。
- **エフェクト範囲**：`EffectBase` に target（Global / Surface[id]）を持たせ範囲別適用（段階的）。
- **タイムラインの動き＝C（song＋short）確定**：**song**＝リニア通し再生（裏で進み続ける）＋**short**＝キー割当の
  **ホールド発火**（押下中だけ最上位レイヤー、離すと song に戻る＝Resolume「Piano」トリガー）。複数同時押しは後押しが上。
  実装の入口はリニア（song）→ short のホールド合成を最終段に1段足す（`ControlHub` に short トリガー追加）。詳細＝`07b` §3.5.2。
- **実装順（確定）**：コア完成 → 出力変形 → 準備/本番 2 モード → タイムライン → マルチトラック/音声。

---

## メモ
- 設計の詳細根拠は `docs/00`〜`10`。本書はそこからの「実行用の抜き出し＋確定した選定」。
- B は確定済み。A の各 M を上から実装していく（次は M0）。
