using UnityEngine;
using UnityEngine.InputSystem;

namespace RewriteReality
{
    /// <summary>
    /// 最小オペレータ GUI（IMGUI）。エフェクト一覧・ON/OFF・mix/パラメータのスライダをライブ表示/操作する。
    /// <see cref="ControlHub"/>/<see cref="EffectParameter"/> をそのまま読むだけ。キーボード(#16)と状態を共有。
    /// 出力(Syphon/NDI/finalRT)はテクスチャキャプチャなので、この画面オーバーレイは Game ビューにのみ出る。
    /// 正式な DESIGN.md 準拠 UI(UI Toolkit) への置き換えは将来。まずは操作確認用。
    /// </summary>
    public sealed class OperatorGui : MonoBehaviour
    {
        [SerializeField] ControlHub _hub;
        [SerializeField] bool _visible = true;
        [Tooltip("表示/非表示のトグルキー")]
        [SerializeField] Key _toggleVisibilityKey = Key.H;

        // DESIGN.md 寄りの最小配色（暖色ダーク＋Live Amber 希少・Selection Blue）
        static readonly Color Amber  = new Color(1.00f, 0.36f, 0.10f);
        static readonly Color Blue   = new Color(0.29f, 0.62f, 0.85f);
        static readonly Color Ink    = new Color(0.93f, 0.91f, 0.88f);
        static readonly Color Dim    = new Color(0.55f, 0.53f, 0.50f);

        GUIStyle _box, _title, _name, _param;
        bool _stylesReady;

        void Awake()
        {
            if (_hub == null) _hub = FindFirstObjectByType<ControlHub>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[_toggleVisibilityKey].wasPressedThisFrame) _visible = !_visible;
        }

        void EnsureStyles()
        {
            if (_stylesReady) return;
            _box = new GUIStyle(GUI.skin.box);
            var bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0.086f, 0.082f, 0.071f, 0.94f)); bg.Apply();
            _box.normal.background = bg;

            _title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _title.normal.textColor = Ink;
            _name = new GUIStyle(GUI.skin.label);
            _param = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _param.normal.textColor = Ink;
            _stylesReady = true;
        }

        void OnGUI()
        {
            if (!_visible || _hub == null) return;
            EnsureStyles();

            GUILayout.BeginArea(new Rect(12, 12, 340, Screen.height - 24), _box);
            GUILayout.Label("RewriteReality — Operator", _title);
            GUILayout.Space(4);

            var effects = _hub.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                var fx = effects[i];
                if (fx == null) continue;
                bool selected = i == _hub.SelectedEffect;

                GUILayout.BeginHorizontal();
                bool en = GUILayout.Toggle(fx.enabled, GUIContent.none, GUILayout.Width(18));
                if (en != fx.enabled) fx.enabled = en;

                _name.normal.textColor = selected ? Amber : (fx.enabled ? Ink : Dim);
                _name.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                if (GUILayout.Button($"{i + 1}. {fx.Name}", _name)) _hub.SelectEffect(i);
                GUILayout.EndHorizontal();

                if (!selected) continue;

                // 選択中エフェクトのパラメータをスライダ表示
                var ps = fx.Parameters;
                for (int j = 0; j < ps.Count; j++)
                {
                    var p = ps[j];
                    bool selParam = j == _hub.SelectedParam;

                    GUILayout.BeginHorizontal();
                    _param.normal.textColor = selParam ? Blue : Ink;
                    GUILayout.Label(p.Name, _param, GUILayout.Width(86));

                    float v = GUILayout.HorizontalSlider(p.Value, p.Min, p.Max, GUILayout.Width(150));
                    if (!Mathf.Approximately(v, p.Value)) { p.Value = v; _hub.SelectEffect(i); _hub.SelectParam(j); }

                    GUILayout.Label(p.Value.ToString("F2"), _param, GUILayout.Width(42));
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(4);
            }

            GUILayout.FlexibleSpace();
            _param.normal.textColor = Dim;
            GUILayout.Label("[1-9]選択  [ ]送り  ↑↓ param  ←→ 値  Space ON/OFF  H 表示切替", _param);
            GUILayout.EndArea();
        }
    }
}
