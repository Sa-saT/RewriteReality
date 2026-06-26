using System.Collections.Generic;
using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// <see cref="EffectBase"/> を順に適用するパイプライン。2 枚の RenderTexture を
    /// ping-pong しながら <c>Apply</c> を呼ぶ。順序入替・ON/OFF・mix は実行時変更可。
    /// </summary>
    public sealed class EffectChain : MonoBehaviour
    {
        [Tooltip("適用順のエフェクト列（上から順に適用）")]
        [SerializeField] List<EffectBase> _effects = new List<EffectBase>();

        RenderTexture _a, _b;

        /// <summary>最終出力（最後の Process 結果）。</summary>
        public RenderTexture FinalTexture { get; private set; }

        /// <summary>適用順のエフェクト列（操作層 ControlHub / UI が参照する）。</summary>
        public IReadOnlyList<EffectBase> Effects => _effects;

        /// <summary>
        /// 入力 <paramref name="src"/> にエフェクト列を適用した結果 RT を返す。RT は使い回す。
        /// 有効なエフェクトが無ければ src をそのまま返す。
        /// </summary>
        public RenderTexture Process(RenderTexture src, in AudioFeatures audio)
        {
            if (src == null)
            {
                FinalTexture = null;
                return null;
            }

            EnsurePingPong(src);

            RenderTexture read = src;
            RenderTexture write = _a;

            int applied = 0;
            for (int i = 0; i < _effects.Count; i++)
            {
                var fx = _effects[i];
                if (fx == null || !fx.isActiveAndEnabled) continue;

                fx.Apply(read, write, audio);

                // ping-pong: 次段の読み先を今書いた方へ
                read = write;
                write = (write == _a) ? _b : _a;
                applied++;
            }

            FinalTexture = (applied == 0) ? src : read;
            return FinalTexture;
        }

        void EnsurePingPong(RenderTexture reference)
        {
            int w = reference.width, h = reference.height;
            if (_a != null && _a.width == w && _a.height == h) return;

            ReleaseRT(ref _a);
            ReleaseRT(ref _b);
            _a = NewRT(w, h, "fx_a");
            _b = NewRT(w, h, "fx_b");
        }

        static RenderTexture NewRT(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = name };
            rt.Create();
            return rt;
        }

        static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            rt = null;
        }

        void OnDestroy()
        {
            ReleaseRT(ref _a);
            ReleaseRT(ref _b);
        }
    }
}
