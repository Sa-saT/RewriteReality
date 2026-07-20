#!/usr/bin/env python3
"""RewriteReality OSC 送信テスター（#M7 / docs/07 §3 の検証用）。

OscControl コンポーネント（既定ポート 9000）へ /rr/... メッセージを送る。
物理コントローラなしで OSC 経路を確認するための最小送信元。

前提: pip install python-osc  （tools/control-test/requirements.txt）

使い方:
  python3 osc_test.py                      # ガイド付きデモ（master/fade/fx を順に動かす）
  python3 osc_test.py /rr/master 0.5       # 単発送信
  python3 osc_test.py --sweep /rr/fx/rgb-shift/amount   # 0→1→0 を数秒かけて掃引
  python3 osc_test.py --list-addresses     # よく使うアドレス早見を表示
  オプション: --host 127.0.0.1 --port 9000
"""
import argparse
import sys
import time

try:
    from pythonosc.udp_client import SimpleUDPClient
except ImportError:
    sys.exit("python-osc が未導入です。`pip install python-osc`（または requirements.txt）を実行してください。")

# ControlHub / 各エフェクトから導出したアドレス早見（slug = Name を小文字化しスペースを '-'）。
KNOWN_ADDRESSES = [
    ("/rr/master", "0..1", "マスター（Master）"),
    ("/rr/fade", "0..1", "Fade to Black"),
    ("/rr/bpm", "実BPM 例:128", "BPM"),
    ("/rr/speed", "0..4", "Master Speed（ShowTimeline.Rate へ）"),
    ("/rr/fx/rgb-shift/mix", "0..1", "RGB Shift ミックス"),
    ("/rr/fx/rgb-shift/amount", "0..1(正規化)", "RGB Shift 量"),
    ("/rr/fx/rgb-shift/angle", "0..1(正規化)", "RGB Shift 角度"),
    ("/rr/fx/rgb-shift/enabled", "0 か 1", "RGB Shift ON/OFF"),
    ("/rr/fx/color-grade/exposure", "0..1(正規化)", "Color Grade 露出"),
    ("/rr/fx/color-grade/saturation", "0..1(正規化)", "Color Grade 彩度"),
    ("/rr/fx/block-glitch/intensity", "0..1(正規化)", "Block Glitch 強度"),
    ("/rr/fx/block-glitch/enabled", "0 か 1", "Block Glitch ON/OFF"),
    ("/rr/fx/feedback/decay", "0..1(正規化)", "Feedback 減衰"),
    ("/rr/fx/feedback/zoom", "0..1(正規化)", "Feedback ズーム"),
]
# 注: OSC の値は「正規化 0..1」で送ると ApplyOscFx が実レンジへ内部変換する。
#     enabled は value>=0.5 で ON。master/fade は 0..1、speed は 0..4、bpm は実 BPM をそのまま送る。


def send_one(client, address, value):
    client.send_message(address, float(value))
    print(f"→ {address} {value}")


def sweep(client, address, seconds=4.0, fps=30):
    """0→1→0 を seconds 秒かけて送る（Unity 側でスライダが動くのを目視確認）。"""
    n = int(seconds * fps)
    print(f"掃引 {address}（{seconds}s・0→1→0）… Ctrl+C で中断")
    try:
        for i in range(n + 1):
            t = i / n
            v = 1.0 - abs(2.0 * t - 1.0)  # 三角波 0→1→0
            client.send_message(address, float(v))
            time.sleep(1.0 / fps)
    except KeyboardInterrupt:
        print("\n中断しました。")
    client.send_message(address, 0.0)


def guided_demo(client):
    print("=== ガイド付きデモ ===")
    print("Unity を再生し、OscControl（ポート一致）を配置してから実行してください。")
    print("各ステップで Unity 側の値が動けば OSC 経路 OK。\n")
    steps = [
        ("/rr/master", "マスターを 1.0 → 0.5 → 1.0"),
        ("/rr/fx/rgb-shift/enabled", "RGB Shift を ON(1)"),
        ("/rr/fx/rgb-shift/amount", "RGB Shift 量を掃引"),
        ("/rr/fx/rgb-shift/enabled", "RGB Shift を OFF(0)"),
    ]
    send_one(client, "/rr/master", 1.0); time.sleep(0.6)
    send_one(client, "/rr/master", 0.5); time.sleep(0.6)
    send_one(client, "/rr/master", 1.0); time.sleep(0.6)
    send_one(client, "/rr/fx/rgb-shift/enabled", 1); time.sleep(0.6)
    sweep(client, "/rr/fx/rgb-shift/amount", seconds=3.0)
    send_one(client, "/rr/fx/rgb-shift/enabled", 0)
    print("\nデモ終了。値が動いていれば OSC は疎通しています。")


def main():
    ap = argparse.ArgumentParser(description="RewriteReality OSC テスト送信")
    ap.add_argument("address", nargs="?", help="OSC アドレス（例 /rr/master）")
    ap.add_argument("value", nargs="?", type=float, help="送る値")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=9000)
    ap.add_argument("--sweep", metavar="ADDR", help="指定アドレスを 0→1→0 掃引")
    ap.add_argument("--list-addresses", action="store_true", help="アドレス早見を表示")
    args = ap.parse_args()

    if args.list_addresses:
        print("アドレス早見（値は特記なき限り正規化 0..1）:")
        for a, rng, desc in KNOWN_ADDRESSES:
            print(f"  {a:36s} {rng:14s} {desc}")
        return

    client = SimpleUDPClient(args.host, args.port)
    print(f"送信先 udp:{args.host}:{args.port}")

    if args.sweep:
        sweep(client, args.sweep)
    elif args.address is not None and args.value is not None:
        send_one(client, args.address, args.value)
    elif args.address is not None:
        sys.exit("値も指定してください（例: python3 osc_test.py /rr/master 0.5）")
    else:
        guided_demo(client)


if __name__ == "__main__":
    main()
