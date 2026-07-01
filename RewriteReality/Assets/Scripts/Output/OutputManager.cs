using UnityEngine;
using UnityEngine.UI;
using Klak.Syphon;
using Klak.Ndi;

namespace RewriteReality
{
    /// <summary>
    /// 最終 RT を Fullscreen / Syphon / NDI へ同時配信する（通す順: FS → Syphon → NDI・docs/06）。
    /// 各 Klak コンポーネントは Capture Method = Texture で SourceTexture に finalRT を流す。
    /// </summary>
    public sealed class OutputManager : MonoBehaviour
    {
        [Header("Fullscreen / Projector")]
        [Tooltip("最終映像を表示する全画面 RawImage（プロジェクタ/フルスクリーン表示用）")]
        [SerializeField] RawImage _fullscreenTarget;

        [Header("Syphon (mac)")]
        [SerializeField] SyphonServer _syphonServer;

        [Header("NDI")]
        [SerializeField] NdiSender _ndiSender;

        [Header("Output Surface（M10・出力変形）")]
        [Tooltip("出力段のメッシュ/コーナーピン変形。未設定 or 無効なら finalRT を素通し。")]
        [SerializeField] OutputWarp _outputWarp;

        [Header("出力ルート ON/OFF（UI から切替・上バー OUTPUT メニュー）")]
        [SerializeField] bool _fullscreenEnabled = true;
        [SerializeField] bool _syphonEnabled = true;
        [SerializeField] bool _ndiEnabled = true;

        RenderTexture _current;

        // ---- ルートの利用可否（コンポーネントが割り当てられているか）----
        public bool HasFullscreen => _fullscreenTarget != null;
        public bool HasSyphon     => _syphonServer != null;
        public bool HasNdi        => _ndiSender != null;

        // ---- ルートの ON/OFF（UI トグル）。実際に配信を止める/再開する ----
        public bool FullscreenEnabled { get => _fullscreenEnabled; set => _fullscreenEnabled = value; }
        public bool SyphonEnabled     { get => _syphonEnabled;     set => _syphonEnabled = value; }
        public bool NdiEnabled        { get => _ndiEnabled;        set => _ndiEnabled = value; }

        /// <summary>有効なルート名の要約（例 "Full · Syphon · NDI"）。1つも無ければ空文字。</summary>
        public string ActiveRoutesSummary()
        {
            var sb = _sb;
            sb.Clear();
            if (HasFullscreen && _fullscreenEnabled) Append(sb, "Full");
            if (HasSyphon && _syphonEnabled)         Append(sb, "Syphon");
            if (HasNdi && _ndiEnabled)               Append(sb, "NDI");
            return sb.ToString();
        }
        static readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(32);
        static void Append(System.Text.StringBuilder sb, string s)
        {
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(s);
        }

        /// <summary>毎フレーム、最終 RT を各出力へ反映する。</summary>
        public void Publish(RenderTexture finalRT)
        {
            if (finalRT == null) return;

            // M10: 出力変形（Output Surface）。無効/未設定なら finalRT を素通し（参照そのまま）。
            // 有効時は OutputWarp 内の永続 RT を返すので、下の差分代入ロジックはそのまま成立する。
            RenderTexture outRT = _outputWarp != null ? _outputWarp.Apply(finalRT) : finalRT;

            // 重要: KlakSyphon の SyphonServer は CaptureMethod / SourceTexture の setter が
            // 呼ばれるたびに TeardownPlugin()（Metal IOSurface テクスチャを破棄→再生成）する。
            // そのため毎フレーム代入すると外部 Metal テクスチャを毎フレーム作り直し、
            // レンダースレッドと競合して segv（Unity/OBS ごとクラッシュ）する。
            // → 値が実際に変わった時だけ代入すること。ルート OFF 時はコンポーネントを無効化して配信停止。

            // 1) フルスクリーン表示（OFF なら texture を外す・参照が変わった時だけ代入）
            if (_fullscreenTarget != null)
            {
                var fsTex = _fullscreenEnabled ? outRT : null;
                if (_fullscreenTarget.texture != fsTex) _fullscreenTarget.texture = fsTex;
            }

            // 2) Syphon サーバ（OFF ならコンポーネント無効化＝配信停止。ON 時のみ差分代入）
            if (_syphonServer != null)
            {
                if (_syphonServer.enabled != _syphonEnabled) _syphonServer.enabled = _syphonEnabled;
                if (_syphonEnabled)
                {
                    if (_syphonServer.CaptureMethod != Klak.Syphon.CaptureMethod.Texture)
                        _syphonServer.CaptureMethod = Klak.Syphon.CaptureMethod.Texture;
                    if (_syphonServer.SourceTexture != outRT)
                        _syphonServer.SourceTexture = outRT;
                }
            }

            // 3) NDI センダー（同様に OFF で無効化・ON 時のみ差分代入）
            if (_ndiSender != null)
            {
                if (_ndiSender.enabled != _ndiEnabled) _ndiSender.enabled = _ndiEnabled;
                if (_ndiEnabled)
                {
                    if (_ndiSender.captureMethod != Klak.Ndi.CaptureMethod.Texture)
                        _ndiSender.captureMethod = Klak.Ndi.CaptureMethod.Texture;
                    if (_ndiSender.sourceTexture != outRT)
                        _ndiSender.sourceTexture = outRT;
                }
            }

            _current = outRT;
        }
    }
}
