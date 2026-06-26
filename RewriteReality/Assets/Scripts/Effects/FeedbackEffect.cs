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

        [Range(0.9f, 1.1f)]
        [Tooltip("履歴のズーム（<1 で内側へ吸い込み、>1 で外へ広がる）")]
        public float zoom = 1.0f;

        [Tooltip("履歴の回転量（ラジアン/フレーム）")]
        public float rotate = 0f;

        [Tooltip("低域で回転量を上乗せ")]
        [SerializeField] float _audioRotate = 0f;

        RenderTexture _history;

        static readonly int HistoryID = Shader.PropertyToID("_HistoryTex");
        static readonly int DecayID   = Shader.PropertyToID("_Decay");
        static readonly int ZoomID    = Shader.PropertyToID("_Zoom");
        static readonly int RotateID  = Shader.PropertyToID("_Rotate");
        static readonly int MixID     = Shader.PropertyToID("_Mix");

        public override string Name => "Feedback";

        public override void Apply(RenderTexture src, RenderTexture dst, in AudioFeatures audio)
        {
            EnsureHistory(src);

            if (mat == null)
            {
                var sh = Shader.Find("Hidden/RewriteReality/Feedback");
                if (sh == null) { Graphics.Blit(src, dst); Graphics.Blit(dst, _history); return; }
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }

            mat.SetTexture(HistoryID, _history);
            mat.SetFloat(DecayID, decay);
            mat.SetFloat(ZoomID, zoom);
            mat.SetFloat(RotateID, rotate + _audioRotate * audio.Low);
            mat.SetFloat(MixID, mix);

            Graphics.Blit(src, dst, mat); // dst = trail(src, history)
            Graphics.Blit(dst, _history); // 履歴を更新
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
