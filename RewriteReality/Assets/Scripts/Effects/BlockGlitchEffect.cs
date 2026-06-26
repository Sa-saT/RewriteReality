using UnityEngine;

namespace RewriteReality
{
    /// <summary>矩形ブロック単位のズレ/差し替えグリッチ。ビート(onset)で柄を更新する。</summary>
    public sealed class BlockGlitchEffect : EffectBase
    {
        [Tooltip("ブロック格子の数（X, Y）")]
        [SerializeField] Vector2 _blocks = new Vector2(24, 14);

        [Range(0f, 1f)]
        [Tooltip("常時グリッチさせるブロック比率（0=音だけで駆動）")]
        [SerializeField] float _intensity = 0f;

        [Tooltip("高域エネルギーで上乗せするグリッチ比率")]
        [SerializeField] float _audioGain = 0.6f;

        [Range(0f, 0.5f)]
        [Tooltip("ブロックの最大ずらし量（UV）")]
        [SerializeField] float _amount = 0.12f;

        [Tooltip("ビート(onset)でグリッチ柄を更新する（OFF で柄固定）")]
        [SerializeField] bool _retriggerOnOnset = true;

        static readonly int BlocksID    = Shader.PropertyToID("_Blocks");
        static readonly int IntensityID = Shader.PropertyToID("_Intensity");
        static readonly int AmountID    = Shader.PropertyToID("_Amount");
        static readonly int SeedID      = Shader.PropertyToID("_Seed");

        float _seed;

        public override string Name => "Block Glitch";

        public override void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio)
        {
            if (mat == null)
            {
                var sh = Shader.Find("Hidden/RewriteReality/BlockGlitch");
                if (sh == null) { Graphics.Blit(src, dst); return; }
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (_retriggerOnOnset && audio.Onset) _seed = Random.value * 100f;

            // mix=0 で intensity=0 → 素通し。高域でグリッチ比率を煽る。
            float intensity = Mathf.Clamp01(mix * (_intensity + _audioGain * audio.High));
            mat.SetVector(BlocksID, _blocks);
            mat.SetFloat(IntensityID, intensity);
            mat.SetFloat(AmountID, _amount);
            mat.SetFloat(SeedID, _seed);

            Graphics.Blit(src, dst, mat);
        }
    }
}
