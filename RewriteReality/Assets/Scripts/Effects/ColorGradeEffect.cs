using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>色調補正（露出/コントラスト/彩度/色相）。RMS や低域で揺らす。</summary>
    public sealed class ColorGradeEffect : EffectBase
    {
        [Range(0f, 3f)]
        [Tooltip("露出（明るさ倍率）")]
        [SerializeField] float _exposure = 1f;

        [Range(0f, 3f)]
        [Tooltip("コントラスト")]
        [SerializeField] float _contrast = 1f;

        [Range(0f, 3f)]
        [Tooltip("彩度")]
        [SerializeField] float _saturation = 1f;

        [Tooltip("色相回転（ラジアン）")]
        [SerializeField] float _hue = 0f;

        [Tooltip("音で彩度(Rms)・色相(Low)を煽る量")]
        [SerializeField] float _audioGain = 0f;

        static readonly int ExposureID   = Shader.PropertyToID("_Exposure");
        static readonly int ContrastID   = Shader.PropertyToID("_Contrast");
        static readonly int SaturationID = Shader.PropertyToID("_Saturation");
        static readonly int HueID        = Shader.PropertyToID("_Hue");
        static readonly int MixID        = Shader.PropertyToID("_Mix");

        public override string Name => "Color Grade";

        protected override void CollectParameters(List<EffectParameter> list)
        {
            list.Add(new EffectParameter("Exposure",   0f,        3f,       () => _exposure,   v => _exposure = v));
            list.Add(new EffectParameter("Contrast",   0f,        3f,       () => _contrast,   v => _contrast = v));
            list.Add(new EffectParameter("Saturation", 0f,        3f,       () => _saturation, v => _saturation = v));
            list.Add(new EffectParameter("Hue",        -Mathf.PI, Mathf.PI, () => _hue,        v => _hue = v));
            list.Add(new EffectParameter("Audio Gain", 0f,        2f,       () => _audioGain,  v => _audioGain = v));
        }

        public override void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio)
        {
            if (mat == null)
            {
                var sh = Shader.Find("Hidden/RewriteReality/ColorGrade");
                if (sh == null) { Graphics.Blit(src, dst); return; }
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }

            float sat = _saturation + _audioGain * audio.Rms;
            float hue = _hue + _audioGain * audio.Low;
            mat.SetFloat(ExposureID, _exposure);
            mat.SetFloat(ContrastID, _contrast);
            mat.SetFloat(SaturationID, sat);
            mat.SetFloat(HueID, hue);
            mat.SetFloat(MixID, mix);

            Graphics.Blit(src, dst, mat);
        }
    }
}
