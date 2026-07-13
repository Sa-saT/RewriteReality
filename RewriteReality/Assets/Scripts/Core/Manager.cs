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

        [Header("M11（任意）— 未配置なら単一 surface 経路")]
        [Tooltip("複数 Input Surface（配置時は多surface合成に切替）")]
        [SerializeField] SurfaceManager _surfaces;

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

            // 2) 音声特徴（合成・エフェクト双方で使う）
            var audio = _audio != null ? _audio.Features : AudioFeatures.Silent;

            // 3) 合成
            if (_compositor == null) return;
            double time = _video != null ? _video.Time : 0d;
            var baseTex = _video != null ? _video.TargetTexture : null;
            var camTex  = _camera != null ? _camera.Texture : null;

            RenderTexture sceneRT;
            if (_surfaces != null && _surfaces.Count > 0)
            {
                // 多surface経路（M11）：surface 各々が自前の追従四隅を更新する
                sceneRT = _compositor.Composite(baseTex, _surfaces, camTex, time, _effectChain, audio);
            }
            else
            {
                // 従来の単一 surface 経路：四隅取得（失敗時は直前値を据え置き）
                if (_cornerSource != null && _cornerSource.TryGetCorners(time, out var corners))
                    _lastCorners = corners;
                sceneRT = _compositor.Composite(baseTex, camTex, _lastCorners);
            }

            // 4) エフェクト（全体＝Global 範囲を finalRT に適用）
            var finalRT = _effectChain != null ? _effectChain.Process(sceneRT, audio) : sceneRT;

            // 5) 出力（FS → Syphon → NDI）
            if (_output != null) _output.Publish(finalRT);
        }
    }
}
