using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// オーディオ解析の1フレーム分の特徴量。エフェクトへ <c>in</c> 渡しする値型
    /// （毎フレームのヒープ確保・ボクシングを避けるため struct）。
    /// </summary>
    public struct AudioFeatures
    {
        public float Rms;       // 全体音量（0..1 目安）
        public float Low;       // 低域エネルギー（0..1）
        public float Mid;       // 中域エネルギー（0..1）
        public float High;      // 高域エネルギー（0..1）
        public float Bpm;       // 推定 BPM
        public bool  Onset;     // このフレームでビート/オンセットを検出したか

        /// <summary>無音相当の既定値。</summary>
        public static AudioFeatures Silent => default;
    }
}
