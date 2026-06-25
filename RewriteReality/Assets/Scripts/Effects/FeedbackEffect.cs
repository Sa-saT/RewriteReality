using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// フィードバック（前フレームを減衰させて重ねる残像/トレイル）。
    /// 履歴 RT を 1 枚保持して使い回す（毎フレーム生成しない）。
    /// </summary>
    public sealed class FeedbackEffect : EffectBase
    {
        [Range(0f, 0.99f)]
        [Tooltip("前フレームの残り具合（大きいほど尾を引く）")]
        public float decay = 0.9f;

        RenderTexture _history;

        public override string Name => "Feedback";

        public override void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio)
        {
            EnsureHistory(src);

            // TODO: 専用シェーダで dst = lerp(src, history, decay) を行い、結果を history へ退避。
            //       現状は素通し（履歴は確保のみ）。
            Graphics.Blit(src, dst);
            Graphics.Blit(dst, _history);
        }

        void EnsureHistory(RenderTexture reference)
        {
            int w = reference.width, h = reference.height;
            if (_history != null && _history.width == w && _history.height == h) return;

            if (_history != null) _history.Release();
            _history = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "fx_feedback_history" };
            _history.Create();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_history != null)
            {
                _history.Release();
                _history = null;
            }
        }
    }
}
