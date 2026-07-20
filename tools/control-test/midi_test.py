#!/usr/bin/env python3
"""RewriteReality MIDI 送信テスター（#M7 / docs/07 §2 の検証用）。

物理 MIDI コントローラなしで MIDI 経路を確認する。macOS の IAC ドライバ（仮想 MIDI バス）へ
CC / Note を送ると、Minis がそれを実機と同じ MIDI デバイスとして受け、MidiControl 経由で
ControlHub に届く。

前提:
  1) pip install mido python-rtmidi   （requirements.txt）
  2) macOS「Audio MIDI設定」→ IACドライバを「装置はオンライン」に（README 参照）

使い方:
  python3 midi_test.py --list                 # 出力ポート一覧（IAC を確認）
  python3 midi_test.py --cc 1 64              # CC#1 に値 64（0..127）を送る
  python3 midi_test.py --sweep-cc 1           # CC#1 を 0→127→0 掃引（ラーン後の可変確認）
  python3 midi_test.py --note 60              # Note#60 を on→off（エフェクト ON/OFF 確認）
  python3 midi_test.py --learn-cc 1           # ラーン用: まず単発→3秒後に掃引（下の手順参照）
  python3 midi_test.py                        # ガイド付きデモ
  オプション: --port-match IAC （既定・ポート名の部分一致で選ぶ）

MIDI ラーンの流れ（Unity 側）:
  1) 再生中、operator UI かキーボードで対象エフェクト/パラメータを選択
     （KeyboardControl: 数字=エフェクト選択 / 上下=パラメータ選択）
  2) ControlHub の Inspector 右クリック →「Begin MIDI Learn」
  3) ここで `--cc <n> 64` を 1 回送る → その CC が選択パラメータに割当
  4) `--sweep-cc <n>` で値が動くことを確認
"""
import argparse
import sys
import time

try:
    import mido
except ImportError:
    sys.exit("mido が未導入です。`pip install mido python-rtmidi`（または requirements.txt）を実行してください。")


def pick_port(match):
    names = mido.get_output_names()
    if not names:
        sys.exit("MIDI 出力ポートが見つかりません。IAC ドライバをオンラインにしてください（README 参照）。")
    for n in names:
        if match.lower() in n.lower():
            return n
    print("一致するポートが無いため先頭を使用:", names[0])
    print("（候補:", names, "）")
    return names[0]


def send_cc(out, cc, value, channel=0):
    value = max(0, min(127, int(value)))
    out.send(mido.Message("control_change", control=cc, value=value, channel=channel))
    print(f"→ CC#{cc} = {value}")


def sweep_cc(out, cc, seconds=4.0, fps=30, channel=0):
    n = int(seconds * fps)
    print(f"掃引 CC#{cc}（{seconds}s・0→127→0）… Ctrl+C で中断")
    try:
        for i in range(n + 1):
            t = i / n
            v = int((1.0 - abs(2.0 * t - 1.0)) * 127)  # 三角波
            out.send(mido.Message("control_change", control=cc, value=v, channel=channel))
            time.sleep(1.0 / fps)
    except KeyboardInterrupt:
        print("\n中断しました。")
    out.send(mido.Message("control_change", control=cc, value=0, channel=channel))


def send_note(out, note, velocity=100, hold=0.4, channel=0):
    out.send(mido.Message("note_on", note=note, velocity=velocity, channel=channel))
    print(f"→ Note#{note} on")
    time.sleep(hold)
    out.send(mido.Message("note_off", note=note, velocity=0, channel=channel))
    print(f"→ Note#{note} off")


def learn_cc(out, cc):
    print(f"ラーン用送信: まず CC#{cc} を 1 回送ります。")
    print("→ 今のうちに Unity の ControlHub で『Begin MIDI Learn』を実行し、対象を選択済みにしておくこと。")
    input("準備できたら Enter を押すと単発送信します…")
    send_cc(out, cc, 64)
    print("3 秒後に掃引します（割当が効いていれば値が動く）…")
    time.sleep(3.0)
    sweep_cc(out, cc)


def guided_demo(out):
    print("=== ガイド付きデモ ===")
    print("Unity を再生し、MidiControl を配置してから実行してください。")
    print("CC は既定では未割当なので、まず Unity 側でラーンして割当を作ってから使うのが基本です。\n")
    print("[1] Note#60 を on/off（--learn で Note を割当済みならエフェクトが ON/OFF）")
    send_note(out, 60)
    time.sleep(0.5)
    print("[2] CC#1 を掃引（ラーン済みならそのパラメータが動く）")
    sweep_cc(out, 1, seconds=3.0)
    print("\nデモ終了。MidiControl の _log を ON にすると受信 CC/Note が Console に出ます。")


def main():
    ap = argparse.ArgumentParser(description="RewriteReality MIDI テスト送信（IAC 仮想バス）")
    ap.add_argument("--list", action="store_true", help="出力ポート一覧")
    ap.add_argument("--port-match", default="IAC", help="使うポート名の部分一致（既定 IAC）")
    ap.add_argument("--cc", nargs=2, type=int, metavar=("NUM", "VALUE"), help="CC 単発（0..127）")
    ap.add_argument("--sweep-cc", type=int, metavar="NUM", help="CC を 0→127→0 掃引")
    ap.add_argument("--note", type=int, metavar="NUM", help="Note を on→off")
    ap.add_argument("--learn-cc", type=int, metavar="NUM", help="ラーン用（単発→掃引）")
    args = ap.parse_args()

    if args.list:
        names = mido.get_output_names()
        print("MIDI 出力ポート:")
        for n in names:
            print("  -", n)
        if not names:
            print("  （なし。IAC ドライバをオンラインにしてください）")
        return

    port_name = pick_port(args.port_match)
    print("使用ポート:", port_name)
    with mido.open_output(port_name) as out:
        if args.cc:
            send_cc(out, args.cc[0], args.cc[1])
        elif args.sweep_cc is not None:
            sweep_cc(out, args.sweep_cc)
        elif args.note is not None:
            send_note(out, args.note)
        elif args.learn_cc is not None:
            learn_cc(out, args.learn_cc)
        else:
            guided_demo(out)


if __name__ == "__main__":
    main()
