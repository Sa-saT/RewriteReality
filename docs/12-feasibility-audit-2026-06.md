# 12. 実現可能性 監査（2026-06 実地調査）

各依存パッケージの **2026年6月時点の最新状況** をネット上の一次情報で確認した記録。
結論: **実現可能。** さらに **初期構成で四隅をオフライン・ベイク（方式C, `01`/`03`）にすれば、
アプリ内から OpenCvSharp が外れ、唯一の関門だった macOS arm64 ネイティブの go/no-go は発生しない。**
実行時依存は Unity＋Klak のみ（すべて arm64 対応確認済）。
arm64 ビルドは「ライブ実景へのリアルタイム合成」を将来やる時だけ再浮上する後回し課題。

> 以下の 🔴 判定は **実行時 CV（`LiveCvCornerSource`）を使う場合のみ**該当。
> ベイク採用時は無効化され、CV ネイティブ行は対象外になる。

## 監査サマリ表

| 要素 | 採用 | 確認した最新状況 | 判定 |
|---|---|---|---|
| エンジン/描画 | Unity 6000.0.33f1 / URP 17.0.3 | Apple Silicon ネイティブ正式。Unity は **6.6 以降 Intel Mac を非推奨化** → arm64 が本流 | 🟢 |
| Syphon 出力 | KlakSyphon | **1.0.4（2025-11）** Unity 6.3 対応。Metal 必須＝Apple Silicon 可 | 🟢 |
| NDI 出力 | KlakNDI | **2.1.6（2025-12）** 要件に「macOS x64 or arm64(M1), Metal」明記、Unity 6.3 対応 | 🟢 |
| HAP 動画 | KlakHap | **1.0.0（2026-02）** native 修正版。2022.3+/64bit。arm64 明記は薄い | 🟡 要確認（任意機能） |
| MIDI/OSC | Minis / OscJack | 純 C#＋Input System ベース。ネイティブ非依存でアーキ無関係 | 🟢 |
| CV 管理層 | OpenCvSharp4 | **4.13.0（2026-06）** 現役活発。ArUco は 4.7+ detector-class API へ移行済み | 🟢 |
| **CV ネイティブ** | **OpenCvSharpExtern (arm64 dylib)** | **公式 NuGet は Win x64/arm64・Linux x64/arm64・WASM のみ。macOS arm64 は公式に無い** | 🔴 **唯一の関門** |

## 最大の関門：OpenCvSharp macOS arm64

- shimat 本家の **公式 NuGet ネイティブランタイムに macOS arm64 が存在しない**。
- NuGet の `OpenCvSharp4.runtime.osx_arm64` は **第三者(grinay)製 `4.8.1-rc`（2023-11, prerelease）**。
  停滞気味で **ArUco(contrib) 同梱が不明**。
- → docs/02・03 の旧「ルート1: NuGet ランタイムを入れて終わり」は **当てにできない**。

### 現実的な選択肢（推奨順）
1. **自前ビルド（本命）**: macOS で OpenCV ＋ opencv_contrib（**aruco 必須**）＋
   OpenCvSharpExtern を **arm64** で cmake ビルド → `.dylib` を `Assets/Plugins/` に配置。
   管理層は NuGet の最新 `OpenCvSharp4`。想定工数 半日〜1日。
2. 第三者 `runtime.osx_arm64`(4.8.1-rc) を試す（**先に aruco 同梱を検証**してから採否判断）。
3. Intel ビルド＋Rosetta（暫定・性能低下、リアルタイム VJ には不利）。

## 性能（60fps）の見立て

アーキテクチャは妥当（docs/01・03）。RenderTexture 完結／`AsyncGPUReadback` で縮小フレームのみ
CPU 転送／検出はワーカースレッド＋数フレーム間引き＋KLT 補間。Keijiro 系の定番で、
内部 1080p・Apple Silicon なら 60fps は現実的。フルスクリーン＋Syphon＋NDI 同時は
fill-rate 二重消費に注意（解像度で調整）。

## 推奨アクション（リスク順）

1. **OpenCvSharpExtern(arm64, contrib/aruco込み) を自前ビルドし ArUco 検出が通ることを最優先で実証**（= go/no-go）。
2. KlakHap の arm64 dylib を最小サンプルで確認（不可でも通常 mp4＝VideoPlayer で代替可。必須ではない）。
3. KlakSyphon/KlakNDI は Keijiro scoped registry から最新（1.0.4 / 2.1.6）を入れ最小配信確認。
4. 以降 `08-roadmap.md` の M1 へ。

## 将来オプション: 深度レイヤー（M9・深度カメラ）

- **メリット**: 深度キー合成 / オクルージョン / 深度ドリブン VFX。Terminal Slam の ML 深度の役割をセンサーで。
- **取得経路**: **深度カメラ（深度センサー）**の色＋深度を ① USB 直結 SDK でテクスチャ化、または ② NDI 経由で受信。
- **候補機材**: **Orbbec Femto Bolt/Mega**（Apple Silicon 向け SDK・Unity 連携あり＝macOS では本命）／**Intel RealSense D4xx**（arm64 は librealsense 自前ビルド要）。
- **設計**: 任意・差し替え可能な `IDepthSource`。無ければ深度エフェクト無効、コア(M2〜M6)は無改修。
- **限界**: センサー依存（概ね 〜数 m・低解像度・エッジノイズ）、転送/同期の運用増。
- **撤回事項**: 旧案の **iPhone/iPad Pro LiDAR 前提は撤回**（Pro 機なし）。**Rcam3 方式（iPhone ARKit sceneDepth → NDI）は Pro 機がある場合の参考実装**に格下げ。
- **判定**: 🟢 後付け可能・コアは無改修。**コア完成後（M8以降）のオプション**として、機種選定の上で導入。

## 参照（一次情報）

- NuGet: OpenCvSharp4.runtime.osx_arm64（4.8.1-rc, grinay） — https://www.nuget.org/packages/OpenCvSharp4.runtime.osx_arm64/
- NuGet: OpenCvSharp4 4.13.0.20260602 — https://www.nuget.org/packages/OpenCvSharp4/
- GitHub: shimat/opencvsharp — https://github.com/shimat/opencvsharp
- GitHub: grinay/osx_arm_builds — https://github.com/grinay/osx_arm_builds
- GitHub: keijiro/KlakSyphon（1.0.4） — https://github.com/keijiro/KlakSyphon
- GitHub: keijiro/KlakNDI（2.1.6） — https://github.com/keijiro/KlakNDI
- GitHub: keijiro/KlakHap（1.0.0） — https://github.com/keijiro/KlakHap
- Unity Discussions: Intel Mac 非推奨化（6.6〜） — https://discussions.unity.com/t/unity-to-deprecate-intel-based-mac-support-starting-with-unity-6-6/1721740
- GitHub: keijiro/Rcam3（iPhone LiDAR 深度 → NDI/VFX） — https://github.com/keijiro/Rcam3
- GitHub: keijiro/Rcam2（深度 over NDI） — https://github.com/keijiro/Rcam2
