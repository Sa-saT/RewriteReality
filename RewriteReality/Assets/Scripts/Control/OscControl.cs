using System.Collections.Concurrent;
using OscJack;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// OSC 入力（OscJack）を <see cref="ControlHub"/> へ橋渡しする（M7・docs/07 §3）。
    /// Max/MSP・Ableton・TouchOSC 等から本アプリのパラメータを遠隔操作するための受け口。
    /// KeyboardControl / MidiControl と同じく opt-in（この MonoBehaviour を置いたときだけ有効）＝非破壊。
    ///
    /// アドレス規約（先頭 <c>/rr/</c>）:
    ///   <c>/rr/master</c> 0..1 ／ <c>/rr/fade</c> 0..1 ／ <c>/rr/bpm</c> 実BPM ／ <c>/rr/speed</c> 0..4
    ///   <c>/rr/fx/&lt;slug&gt;/&lt;param&gt;</c> 0..1 ／ <c>/rr/fx/&lt;slug&gt;/enabled</c> 0|1
    ///   &lt;slug&gt; は EffectBase.Name を小文字化しスペースを '-' に（<see cref="ControlHub.Slugify"/>）。
    ///
    /// スレッド境界: OscJack の <see cref="OscServer"/> は専用ワーカースレッドでメッセージを受け、
    /// コールバックもそのスレッドで発火する（<see cref="OscDataHandle"/> はコールバック内でのみ有効な
    /// 共有バッファ）。よってコールバックでは値を即 float へ取り出してキューへ積むだけにし、
    /// Unity オブジェクトへの反映は <see cref="Update"/>（メインスレッド）でドレインして行う。
    /// </summary>
    public sealed class OscControl : MonoBehaviour
    {
        [SerializeField] ControlHub _hub;
        [Tooltip("待ち受け UDP ポート（TouchOSC 等の送信先と一致させる）")]
        [SerializeField] int _port = 9000;
        [Tooltip("受信内容を Console に出す（現場での配線確認用）")]
        [SerializeField] bool _log = false;

        OscServer _server;
        // ワーカースレッド → メインスレッドの受け渡し（address と第1引数 float のみ・ハンドルは持ち越さない）。
        readonly ConcurrentQueue<(string address, float value)> _queue
            = new ConcurrentQueue<(string, float)>();

        void Awake()
        {
            if (_hub == null) _hub = FindFirstObjectByType<ControlHub>();
        }

        void OnEnable()
        {
            try
            {
                _server = new OscServer(_port);
                // 空アドレス = 全メッセージのモニタ（OscJack は完全一致 dispatch のため、
                // 任意 slug の /rr/fx/... を拾うには monitor で受けて自前で振り分ける）。
                _server.MessageDispatcher.AddCallback(string.Empty, OnMessage);
                if (_log) Debug.Log($"[OscControl] listening on udp:{_port}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[OscControl] ポート {_port} を開けませんでした: {e.Message}");
                _server = null;
            }
        }

        void OnDisable()
        {
            if (_server != null)
            {
                _server.MessageDispatcher.RemoveCallback(string.Empty, OnMessage);
                _server.Dispose();
                _server = null;
            }
            while (_queue.TryDequeue(out _)) { }
        }

        // ワーカースレッドで発火。値だけ取り出してキューへ（ハンドルはこの呼び出し内でのみ有効）。
        void OnMessage(string address, OscDataHandle data)
        {
            float v = data.GetElementCount() > 0 ? data.GetElementAsFloat(0) : 0f;
            _queue.Enqueue((address, v));
        }

        void Update()
        {
            if (_hub == null) { while (_queue.TryDequeue(out _)) { } return; }
            while (_queue.TryDequeue(out var msg)) Route(msg.address, msg.value);
        }

        // "/rr/master" → global / "/rr/fx/<slug>/<param>" → fx。/rr/ 以外は無視。
        void Route(string address, float value)
        {
            if (string.IsNullOrEmpty(address)) return;
            var segs = address.Split('/');   // "/rr/x" → ["", "rr", "x"]（OSC は低頻度のため split alloc は許容）
            if (segs.Length < 3 || segs[1] != "rr") return;

            bool ok;
            if (segs[2] == "fx" && segs.Length >= 5)
                ok = _hub.ApplyOscFx(segs[3], segs[4], value);
            else
                ok = _hub.ApplyOscGlobal(segs[2], value);

            if (_log) Debug.Log($"[OscControl] {address} = {value:F3} ({(ok ? "ok" : "unresolved")})");
        }
    }
}
