using UnityEngine;

namespace RewriteReality
{
    /// <summary>
    /// 出力直前のマスター段（#38）。<see cref="ControlHub.Master"/>（明度）と
    /// <see cref="ControlHub.FadeToBlack"/>（黒フェード）を最終 RT に掛ける。
    /// MonoBehaviour ではなく <see cref="Manager"/> が保持するプレーンクラス＝シーン配線不要。
    /// gain がほぼ 1 のとき（Master=1 / Fade=0）は Blit 自体を行わず src をそのまま返す（非破壊・無コスト）。
    /// </summary>
    public sealed class MasterOut
    {
        const float Epsilon = 0.001f;

        static readonly int MasterID = Shader.PropertyToID("_Master");
        static readonly int FadeID   = Shader.PropertyToID("_Fade");

        Material _mat;
        RenderTexture _rt;
        bool _shaderMissingLogged;

        /// <summary>直近の適用結果（素通し時は src と同じ参照）。</summary>
        public RenderTexture Output { get; private set; }

        /// <summary>この段が実際に描画を行ったか（UI のインジケータ用）。</summary>
        public bool Active { get; private set; }

        public RenderTexture Apply(RenderTexture src, float master, float fade)
        {
            Active = false;
            Output = src;
            if (src == null) return null;

            master = Mathf.Clamp01(master);
            fade   = Mathf.Clamp01(fade);
            float gain = master * (1f - fade);
            if (gain >= 1f - Epsilon) return src;   // 素通し（従来挙動と同一）

            if (_mat == null)
            {
                var sh = Shader.Find("Hidden/RewriteReality/MasterOut");
                if (sh == null)
                {
                    if (!_shaderMissingLogged)
                    {
                        _shaderMissingLogged = true;
                        Debug.LogWarning("[MasterOut] Hidden/RewriteReality/MasterOut が見つかりません。Master/Fade は無効です。");
                    }
                    return src;
                }
                _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }

            EnsureRT(src);
            _mat.SetFloat(MasterID, master);
            _mat.SetFloat(FadeID, fade);
            Graphics.Blit(src, _rt, _mat);

            Active = true;
            Output = _rt;
            return _rt;
        }

        void EnsureRT(RenderTexture reference)
        {
            if (_rt != null && _rt.width == reference.width && _rt.height == reference.height) return;
            Release();
            _rt = new RenderTexture(reference.width, reference.height, 0, RenderTextureFormat.ARGB32) { name = "master_out" };
            _rt.Create();
        }

        public void Release()
        {
            if (_rt != null) { _rt.Release(); _rt = null; }
            Output = null;
            Active = false;
        }

        public void Dispose()
        {
            Release();
            if (_mat != null) { Object.DestroyImmediate(_mat); _mat = null; }
        }
    }
}
