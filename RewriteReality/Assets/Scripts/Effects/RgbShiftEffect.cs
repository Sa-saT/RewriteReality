using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>RGB チャンネルを横/縦にずらすグリッチ。音の高域で振幅を煽る。</summary>
    public sealed class RgbShiftEffect : EffectBase
    {
        [Tooltip("基本ずらし量（UV 単位・0 で無効）")]
        [SerializeField] float _amount = 0.004f;

        [Tooltip("高域エネルギーで上乗せするずらし量")]
        [SerializeField] float _audioGain = 0.03f;

        [Tooltip("ずらし方向（度・0=水平）")]
        [Range(0f, 360f)]
        [SerializeField] float _angle = 0f;

        static readonly int OffsetID = Shader.PropertyToID("_Offset");

        public override string Name => "RGB Shift";

        protected override void CollectParameters(List<EffectParameter> list)
        {
            list.Add(new EffectParameter("Amount",     0f, 0.05f, () => _amount,    v => _amount = v));
            list.Add(new EffectParameter("Audio Gain", 0f, 0.1f,  () => _audioGain, v => _audioGain = v));
            list.Add(new EffectParameter("Angle",      0f, 360f,  () => _angle,     v => _angle = v));
        }

        public override void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio)
        {
            if (mat == null)
            {
                var sh = Shader.Find("Hidden/RewriteReality/RgbShift");
                if (sh == null) { Graphics.Blit(src, dst); return; } // シェーダ未検出時は素通し
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }

            // mix=0 で amt=0 → 完全素通し（ドライ）。mix と高域で振幅を制御。
            float amt = mix * (_amount + _audioGain * audio.High);
            float rad = _angle * Mathf.Deg2Rad;
            mat.SetVector(OffsetID, new Vector4(Mathf.Cos(rad) * amt, Mathf.Sin(rad) * amt, 0f, 0f));

            Graphics.Blit(src, dst, mat);
        }
    }
}
