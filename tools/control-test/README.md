# #M7 MIDI / OSC 実機確認 手順書

物理 MIDI コントローラを買わずに、**macOS 標準機能＋無料スクリプトだけ**で
MIDI（Minis）と OSC（OscJack）の入力経路を検証するための手順。

対象実装（branch `feat/m7-midi-osc-control`）:
- `ControlHub`（CC/Note マッピング・MIDI ラーン・OSC 解決・midimap.json 永続化）
- `MidiControl`（Minis → ControlHub）／`OscControl`（OscJack → ControlHub）

---

## 0. 事前準備（Python 送信元）

```bash
cd <repo>/RewriteRealityProject
python3 -m venv tools/control-test/.venv
source tools/control-test/.venv/bin/activate
pip install -r tools/control-test/requirements.txt
```

- `osc_test.py` … OSC 送信（python-osc）
- `midi_test.py` … MIDI 送信（mido + python-rtmidi・IAC 仮想バス経由）

---

## 1. Unity シーン側の配線（共通）

1. `Main.unity` を開く。
2. 空の GameObject を作り（例: `Control`）、以下を **必要な方だけ** AddComponent:
   - `MidiControl` … `_hub` は空でも可（未設定なら `ControlHub` を自動取得）。`_log` を ON にすると受信を Console 表示。
   - `OscControl` … `_port` を送信側と一致させる（既定 9000）。`_log` を ON 推奨。
3. シーンに `ControlHub` と `EffectChain`（初期 4 エフェクト）が居ることを確認（既存の `Main.unity` にあり）。
4. 再生（Play）。

> 補足: `MidiControl`/`OscControl` は **opt-in**。置かなければ一切影響しない（従来動作）。

---

## 2. OSC の確認（最短・ハードウェア完全不要）

Unity を再生した状態で、別ターミナルから:

```bash
source tools/control-test/.venv/bin/activate

# ガイド付きデモ（master → RGB Shift を自動で動かす）
python3 tools/control-test/osc_test.py

# 単発
python3 tools/control-test/osc_test.py /rr/master 0.5
python3 tools/control-test/osc_test.py /rr/fx/rgb-shift/enabled 1
python3 tools/control-test/osc_test.py /rr/fx/rgb-shift/amount 0.8

# 0→1→0 掃引（値が動くのを目視）
python3 tools/control-test/osc_test.py --sweep /rr/fx/rgb-shift/amount

# アドレス早見
python3 tools/control-test/osc_test.py --list-addresses
```

**期待挙動**: `OscControl._log` を ON にしていれば Console に `[OscControl] /rr/... = 値 (ok)` が出る。
`ok` = ControlHub がアドレスを解決できた。`unresolved` の場合は slug 名やアドレスを確認。

- グローバル: `/rr/master`(0..1) `/rr/fade`(0..1) `/rr/bpm`(実BPM) `/rr/speed`(0..4)
- エフェクト: `/rr/fx/<slug>/<param>`（値は正規化 0..1）／`/rr/fx/<slug>/enabled`（0 か 1）
  - slug 例: `rgb-shift` `color-grade` `block-glitch` `feedback`
  - param 例: `mix` `amount` `angle` `exposure` `saturation` `intensity` `decay` `zoom`

---

## 3. MIDI の確認（IAC 仮想バス・ハードウェア不要）

### 3-1. macOS 仮想 MIDI バスを有効化

1. 「**Audio MIDI設定**」を開く（アプリケーション → ユーティリティ）。
2. メニュー「ウインドウ → **MIDIスタジオを表示**」。
3. 「**IACドライバ**」をダブルクリック → 「**装置はオンライン**」にチェック。
4. ポート（`Bus 1`）が 1 つある状態にする（無ければ「ポート」で追加）。

確認:
```bash
python3 tools/control-test/midi_test.py --list
# 出力に "IAC Driver Bus 1" 等が出れば OK
```

### 3-2. Note でエフェクト ON/OFF（ラーン）

1. Unity 再生中、`ControlHub` の Inspector を右クリック → **Begin MIDI Learn**。
2. 直後に Note を 1 回送る:
   ```bash
   python3 tools/control-test/midi_test.py --note 60
   ```
   → Note#60 が「選択中エフェクト」の ON/OFF に割当される。
3. 以後、同じ `--note 60` を送るたびに、そのエフェクトが ON/OFF トグルする。

### 3-3. CC で連続パラメータ（ラーン）

1. 対象を選択する。`KeyboardControl` があれば **数字キー=エフェクト選択 / 上下=パラメータ選択**。
2. `ControlHub` 右クリック → **Begin MIDI Learn**。
3. CC を 1 回送って割当:
   ```bash
   python3 tools/control-test/midi_test.py --cc 1 64
   ```
4. 値を掃引して動作確認:
   ```bash
   python3 tools/control-test/midi_test.py --sweep-cc 1
   ```
   → 選択していたパラメータが 0→最大→0 で動けば OK。

補助コマンド（上記を続けて実行）:
```bash
python3 tools/control-test/midi_test.py --learn-cc 1
```

### 3-4. 割当の保存/復元

- `ControlHub` 右クリック → **Save MIDI Map** … `~/Library/Application Support/<company>/<product>/midimap.json`（`Application.persistentDataPath`）へ保存。
- **Load MIDI Map** で復元。`_autoLoadMidiMap` を ON にすると起動時に自動読込。
- 全消去は **Clear All MIDI Bindings**。

---

## 4. うまくいかないとき

| 症状 | 確認 |
|---|---|
| OSC が届かない | `OscControl._port` と送信側 `--port` が一致しているか。ファイアウォールで UDP がブロックされていないか。 |
| `unresolved` ログ | slug / param 名が実装と一致しているか（§2 の一覧）。`enabled` は 0/1。 |
| MIDI ポートが出ない | IAC ドライバが「オンライン」か。`--list` で名前確認し `--port-match` を合わせる。 |
| ラーンが効かない | Learn 実行 → **その後に** CC/Note を送っているか（順序が逆だと割当されない）。対象エフェクト/パラメータを選択済みか。 |
| CC を動かしても無反応 | まだ割当していない CC 番号かも。ラーンで割当してから掃引する。 |

---

## 5. メモ

- `MidiControl`/`OscControl` は opt-in・未配置なら非破壊。実機コントローラ購入後もこの経路はそのまま使える
  （Minis は CoreMIDI 経由で実機も IAC も同様に見える）。
- スレッド境界: OscJack はワーカースレッドで受信 → メインスレッド（`Update`）で反映。Minis は
  InputSystem のイベントで発火（will イベントの引数値を使用）。詳細は各 `.cs` の doc コメント参照。
