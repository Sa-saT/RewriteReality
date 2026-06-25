using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// メインループ駆動役。専用の処理用 Camera は持たず、
    /// CornerSource → Compositor → EffectChain → Output を毎フレーム RenderTexture 上で実行する（docs/01）。
    /// </summary>
    public sealed class Manager : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] SourceVideo _video;
        [SerializeField] SourceCamera _camera;

        [Tooltip("ICornerSource を実装した MonoBehaviour（初期: BakedCornerSource）")]
        [SerializeField] MonoBehaviour _cornerSourceBehaviour;

        [Header("Pipeline")]
        [SerializeField] Compositor _compositor;
        [SerializeField] EffectChain _effectChain;
        [SerializeField] AudioAnalyzer _audio;
        [SerializeField] OutputManager _output;

        ICornerSource _cornerSource;
        Corners _lastCorners = Corners.FullFrame;

        void Awake()
        {
            _cornerSource = _cornerSourceBehaviour as ICornerSource;
            if (_cornerSourceBehaviour != null && _cornerSource == null)
                Debug.LogError($"[Manager] {_cornerSourceBehaviour.GetType().Name} は ICornerSource を実装していません。");
        }

        void LateUpdate()
        {
            // 1) ソース更新
            if (_video != null) _video.Tick();
            if (_camera != null) _camera.Tick();
            if (_audio != null) _audio.Tick();

            // 2) 四隅取得（失敗時は直前値を据え置き）
            double time = _video != null ? _video.Time : 0d;
            if (_cornerSource != null && _cornerSource.TryGetCorners(time, out var corners))
                _lastCorners = corners;

            // 3) 合成
            if (_compositor == null) return;
            var baseTex = _video != null ? _video.TargetTexture : null;
            var camTex  = _camera != null ? _camera.Texture : null;
            var sceneRT = _compositor.Composite(baseTex, camTex, _lastCorners);

            // 4) エフェクト
            var audio = _audio != null ? _audio.Features : AudioFeatures.Silent;
            var finalRT = _effectChain != null ? _effectChain.Process(sceneRT, audio) : sceneRT;

            // 5) 出力（FS → Syphon → NDI）
            if (_output != null) _output.Publish(finalRT);
        }
    }
}
