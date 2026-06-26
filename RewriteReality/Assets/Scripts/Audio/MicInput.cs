using System.Collections;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// マイク/ライン入力を AudioSource に流し込み、AudioListener 経由の FFT 解析
    /// (<see cref="AudioAnalyzer"/>) の入力にする。デバイスは Inspector で選択可（空=OS 既定）。
    /// 起動時に利用可能な入力デバイス一覧を Console へ出力する。
    /// 別アプリの音を取り込むには、BlackHole 等の仮想オーディオデバイスを入力に選ぶ。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MicInput : MonoBehaviour
    {
        [Tooltip("入力デバイス名（空=OS 既定）。Console の一覧から正確な名前をコピーする。")]
        [SerializeField] string _deviceName = "";

        [Tooltip("録音サンプルレート")]
        [SerializeField] int _sampleRate = 48000;

        [Tooltip("リングバッファ長（秒）")]
        [SerializeField] int _lengthSec = 1;

        [Tooltip("モニタ音量。マイク＋スピーカーでハウリングする時は下げる（0 だと解析値も 0 になる）。")]
        [Range(0f, 1f)]
        [SerializeField] float _monitorVolume = 1f;

        AudioSource _source;
        string _active;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f; // 2D（距離減衰させない＝AudioListener に等倍で届く）
            _source.volume = _monitorVolume;
        }

        void OnEnable()
        {
            LogDevices();
            StartCoroutine(StartMicRoutine());
        }

        void OnDisable() => StopMic();

        void LogDevices()
        {
            var devs = Microphone.devices;
            if (devs == null || devs.Length == 0)
            {
                Debug.LogWarning("[MicInput] 入力デバイスが見つかりません。OS の入力権限/接続を確認してください。");
                return;
            }
            Debug.Log($"[MicInput] 利用可能な入力デバイス({devs.Length}): {string.Join(" | ", devs)}");
        }

        IEnumerator StartMicRoutine()
        {
            string dev = string.IsNullOrEmpty(_deviceName) ? null : _deviceName; // null=OS 既定
            _active = dev;

            var clip = Microphone.Start(dev, true, _lengthSec, _sampleRate);
            if (clip == null)
            {
                Debug.LogError($"[MicInput] Microphone.Start 失敗 (device={dev ?? "OS既定"})。デバイス名/権限を確認。");
                yield break;
            }

            // 書き込みが始まるまで待ってから再生（空再生・無駄なレイテンシ回避）
            while (Microphone.GetPosition(dev) <= 0) yield return null;

            _source.clip = clip;
            _source.Play();
            Debug.Log($"[MicInput] 録音開始 device={dev ?? "OS既定"} rate={_sampleRate}Hz");
        }

        void StopMic()
        {
            if (_source != null && _source.isPlaying) _source.Stop();
            if (Microphone.IsRecording(_active)) Microphone.End(_active);
        }
    }
}
