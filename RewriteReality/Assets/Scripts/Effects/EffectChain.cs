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

        RenderTexture _a, _b;     // Global（全体）パス用 ping-pong
        RenderTexture _sa, _sb;   // Surface（surface 単位）パス用 ping-pong（サイズ差でのスラッシュ回避に分離）

        /// <summary>最終出力（最後の Process 結果）。</summary>
        public RenderTexture FinalTexture { get; private set; }

        /// <summary>適用順のエフェクト列（操作層 ControlHub / UI が参照する）。</summary>
        public IReadOnlyList<EffectBase> Effects => _effects;

        /// <summary>
        /// 合成後の画面全体（finalRT）にエフェクト列を適用した結果 RT を返す。RT は使い回す。
        /// 適用対象は <see cref="EffectScope.Global"/> のエフェクトのみ（Surface 指定は
        /// <see cref="ProcessSurface"/> で合成段に掛ける・docs/07b §3.6）。既定は全て Global＝従来と同一挙動。
        /// </summary>
        public RenderTexture Process(RenderTexture src, in AudioFeatures audio)
        {
            if (src == null)
            {
                FinalTexture = null;
                return null;
            }

            EnsurePingPong(ref _a, ref _b, src, "fx_a", "fx_b");

            RenderTexture read = src;
            RenderTexture write = _a;

            int applied = 0;
            for (int i = 0; i < _effects.Count; i++)
            {
                var fx = _effects[i];
                if (fx == null || !fx.isActiveAndEnabled) continue;
                if (fx.scope != EffectScope.Global) continue; // 範囲=Surface は合成段（ProcessSurface）で適用

                fx.Apply(read, write, audio);

                // ping-pong: 次段の読み先を今書いた方へ
                read = write;
                write = (write == _a) ? _b : _a;
                applied++;
            }

            FinalTexture = (applied == 0) ? src : read;
            return FinalTexture;
        }

        /// <summary>
        /// 指定 surface（<paramref name="surfaceId"/>）に割り当てられたエフェクトだけを
        /// <paramref name="src"/>（その surface の埋め込み内容）に適用して返す（範囲別適用・M11）。
        /// Global パスとは独立の ping-pong を使う。対象が無ければ src をそのまま返す。
        /// </summary>
        public RenderTexture ProcessSurface(RenderTexture src, int surfaceId, in AudioFeatures audio)
        {
            if (src == null) return null;

            EnsurePingPong(ref _sa, ref _sb, src, "fx_sa", "fx_sb");

            RenderTexture read = src;
            RenderTexture write = _sa;

            int applied = 0;
            for (int i = 0; i < _effects.Count; i++)
            {
                var fx = _effects[i];
                if (fx == null || !fx.isActiveAndEnabled) continue;
                if (fx.scope != EffectScope.Surface || fx.targetSurfaceId != surfaceId) continue;

                fx.Apply(read, write, audio);
                read = write;
                write = (write == _sa) ? _sb : _sa;
                applied++;
            }

            return (applied == 0) ? src : read;
        }

        /// <summary>指定 surface に割り当てられた有効なエフェクトが1つでもあるか（合成段の適用要否判定）。</summary>
        public bool HasSurfaceEffects(int surfaceId)
        {
            for (int i = 0; i < _effects.Count; i++)
            {
                var fx = _effects[i];
                if (fx == null || !fx.isActiveAndEnabled) continue;
                if (fx.scope == EffectScope.Surface && fx.targetSurfaceId == surfaceId) return true;
            }
            return false;
        }

        static void EnsurePingPong(ref RenderTexture a, ref RenderTexture b, RenderTexture reference, string nameA, string nameB)
        {
            int w = reference.width, h = reference.height;
            if (a != null && a.width == w && a.height == h) return;

            ReleaseRT(ref a);
            ReleaseRT(ref b);
            a = NewRT(w, h, nameA);
            b = NewRT(w, h, nameB);
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
            ReleaseRT(ref _sa);
            ReleaseRT(ref _sb);
        }
    }
}
