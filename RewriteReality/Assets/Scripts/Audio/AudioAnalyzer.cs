using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// アプリ内 FFT 解析。最終ミックス（AudioListener）からスペクトルを取り、
    /// 帯域別エネルギー・RMS・簡易 onset を算出する（外部依存なしで完結・docs/05）。
    /// バッファは使い回し、毎フレームの確保を避ける。
    /// </summary>
    public sealed class AudioAnalyzer : MonoBehaviour
    {
        const int SpectrumSize = 1024;

        [Tooltip("低域の上限（スペクトル bin 比 0..1）")]
        [SerializeField] float _lowSplit = 0.08f;
        [Tooltip("中域の上限（スペクトル bin 比 0..1）")]
        [SerializeField] float _midSplit = 0.35f;
        [Tooltip("onset 検出のしきい値（前フレーム比の増加量）")]
        [SerializeField] float _onsetThreshold = 0.04f;

        [Tooltip("デバッグ: 解析レベルを定期的に Console へ出す（確認用・本番は OFF）")]
        [SerializeField] bool _logLevels;

        readonly float[] _spectrum = new float[SpectrumSize];
        float _prevFlux;
        int _logCounter;
        AudioFeatures _features;

        /// <summary>最新の解析結果（<c>in</c> 渡しで参照）。</summary>
        public ref readonly AudioFeatures Features => ref _features;

        /// <summary>毎フレーム呼び出して解析を更新する。</summary>
        public void Tick()
        {
            AudioListener.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

            int lowEnd = Mathf.Max(1, (int)(SpectrumSize * _lowSplit));
            int midEnd = Mathf.Max(lowEnd + 1, (int)(SpectrumSize * _midSplit));

            float low = 0f, mid = 0f, high = 0f, sumSq = 0f, flux = 0f;
            for (int i = 0; i < SpectrumSize; i++)
            {
                float v = _spectrum[i];
                sumSq += v * v;
                flux  += v;
                if (i < lowEnd)      low  += v;
                else if (i < midEnd) mid  += v;
                else                 high += v;
            }

            _features.Low  = low;
            _features.Mid  = mid;
            _features.High = high;
            _features.Rms  = Mathf.Sqrt(sumSq / SpectrumSize);

            // 簡易 onset: スペクトル総和の急増を検出
            float delta = flux - _prevFlux;
            _features.Onset = delta > _onsetThreshold;
            _prevFlux = flux;

            // TODO: onset 間隔から BPM 推定（自己相関 / IOI ヒストグラム）。現状 0。
            _features.Bpm = 0f;

            if (_logLevels && (++_logCounter % 15) == 0)
                Debug.Log($"[AudioAnalyzer] Rms={_features.Rms:F4} Low={low:F4} Mid={mid:F4} High={high:F4} Onset={_features.Onset}");
        }
    }
}
