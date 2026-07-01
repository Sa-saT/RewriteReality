using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RewriteReality
{
    /// <summary>
    /// UI Toolkit 製オペレータUIの土台（#20）。DESIGN.md トークン→USS(RewriteReality.uss)・
    /// Console Layout シェル(OperatorShell.uxml) の上に、<see cref="ControlHub"/> のエフェクト一覧/
    /// 選択/ON-OFF と <see cref="EffectParameter"/> を双方向バインドし、preview に最終 RT を表示する。
    /// 動的な中身（FX 行・パラメータ行）だけをコードで生成し、配色/レイアウトは USS が持つ。
    /// 現状の IMGUI <see cref="OperatorGui"/> は確認用として併存。機能等価になったら置換（docs/07）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OperatorUI : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] ControlHub _hub;
        [Tooltip("preview に映す最終 RT の供給元（未指定なら自動取得）")]
        [SerializeField] EffectChain _chain;

        [Header("Assets (UIDocument に未設定なら使うフォールバック)")]
        [SerializeField] VisualTreeAsset _shellUxml;
        [SerializeField] StyleSheet _styleSheet;

        [Header("Row templates (UI Builder で見た目を編集・未指定ならコード生成)")]
        [Tooltip("FX チェーン1行の UXML（FxRow.uxml）")]
        [SerializeField] VisualTreeAsset _fxRowTemplate;
        [Tooltip("パラメータ1行の UXML（ParamRow.uxml）")]
        [SerializeField] VisualTreeAsset _paramRowTemplate;

        [Header("UI")]
        [SerializeField] bool _visible = true;
        [Tooltip("表示/非表示トグルキー（IMGUI と同じ H）")]
        [SerializeField] Key _toggleVisibilityKey = Key.H;

        UIDocument _doc;
        VisualElement _root;
        Image _preview;
        Label _fps;
        Label _inspectorTitle;
        ScrollView _fxList;
        ScrollView _inspector;
        bool _built;

        // FX 行・パラメータ行のバインド保持（再構築判定/毎フレーム同期用）
        readonly List<FxRow> _fxRows = new List<FxRow>();
        readonly List<ParamRow> _paramRows = new List<ParamRow>();
        int _builtEffectCount = -1;
        int _inspectorEffect = -1;
        float _smoothedDt;

        sealed class FxRow { public VisualElement root; public Toggle toggle; public Label name; }
        sealed class ParamRow { public VisualElement root; public Slider slider; public Label value; public EffectParameter param; }

        void Awake()
        {
            if (_hub == null) _hub = FindFirstObjectByType<ControlHub>();
            if (_chain == null) _chain = FindFirstObjectByType<EffectChain>();
        }

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            BuildShell();
        }

        void BuildShell()
        {
            if (_doc == null) return;
            _root = _doc.rootVisualElement;
            if (_root == null) return;

            // UIDocument に Source Asset が無い場合は、シリアライズした UXML を流し込む。
            if (_root.Q<VisualElement>("rr-root") == null && _shellUxml != null)
                _shellUxml.CloneTree(_root);
            if (_styleSheet != null && !_root.styleSheets.Contains(_styleSheet))
                _root.styleSheets.Add(_styleSheet);

            _preview = _root.Q<Image>("rr-preview");
            if (_preview != null) _preview.scaleMode = ScaleMode.ScaleToFit;
            _fps = _root.Q<Label>("rr-fps");
            _inspectorTitle = _root.Q<Label>("rr-inspector-title");
            _fxList = _root.Q<ScrollView>("rr-fx-list");
            _inspector = _root.Q<ScrollView>("rr-inspector");

            if (_fxList == null || _inspector == null)
            {
                Debug.LogWarning("[OperatorUI] シェルが見つかりません。UIDocument の Source Asset に " +
                                 "OperatorShell.uxml を割り当てるか、OperatorUI に Shell Uxml を設定してください。");
                return;
            }

            _built = true;
            _builtEffectCount = -1;   // 次の LateUpdate で FX 一覧を構築
            _inspectorEffect = -1;
            ApplyVisibility();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[_toggleVisibilityKey].wasPressedThisFrame)
            {
                _visible = !_visible;
                ApplyVisibility();
            }
        }

        void LateUpdate()
        {
            // UIDocument が rootVisualElement を構築するタイミングは OnEnable より後になり得るため、
            // OnEnable の初回 BuildShell が空振り（_root 未構築）でも、_built になるまで毎フレーム再試行する。
            if (!_built) { BuildShell(); if (!_built) return; }

            // preview / fps は ControlHub 非依存で常に更新する
            // （動画プレビュー確認が ControlHub の配線に依存しないよう堅牢化）。
            UpdatePreview();
            UpdateFps();

            if (_hub == null) return; // 以降（FX 一覧 / inspector）は ControlHub が必要

            // FX 一覧はエフェクト数が変わった時だけ再構築（毎フレーム new を避ける）
            if (_hub.Count != _builtEffectCount) RebuildFxList();
            // inspector は選択エフェクトが変わった時だけ再構築
            if (_hub.SelectedEffect != _inspectorEffect) RebuildInspector();

            SyncFxRows();
            SyncParamRows();
        }

        // -------------------------------------------------- preview / fps
        void UpdatePreview()
        {
            if (_preview == null || _chain == null) return;
            var rt = _chain.FinalTexture;
            if (rt != null && _preview.image != rt) _preview.image = rt;
        }

        void UpdateFps()
        {
            if (_fps == null) return;
            _smoothedDt = Mathf.Lerp(_smoothedDt, Time.unscaledDeltaTime, 0.1f);
            float fps = _smoothedDt > 0f ? 1f / _smoothedDt : 0f;
            _fps.text = Mathf.RoundToInt(fps) + " FPS";
            EnableClass(_fps, "rr-fps--warn", fps < 58f);
        }

        // -------------------------------------------------- FX chain list
        void RebuildFxList()
        {
            _fxList.Clear();
            _fxRows.Clear();

            var effects = _hub.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                var fx = effects[i];
                if (fx == null) continue;
                int index = i;

                // テンプレート(UI Builder 編集可) があれば clone、無ければコード生成
                VisualElement row = null;
                Toggle toggle = null;
                Label name = null;
                if (_fxRowTemplate != null)
                {
                    row = _fxRowTemplate.Instantiate().Q("fx-item");
                    toggle = row?.Q<Toggle>("fx-toggle");
                    name = row?.Q<Label>("fx-name");
                }
                if (row == null || toggle == null || name == null)
                {
                    row = new VisualElement(); row.AddToClassList("rr-fx-item");
                    toggle = new Toggle(); toggle.AddToClassList("rr-fx-toggle");
                    name = new Label(); name.AddToClassList("rr-fx-name");
                    row.Add(toggle); row.Add(name);
                }

                toggle.SetValueWithoutNotify(fx.enabled);
                toggle.RegisterValueChangedCallback(evt => _hub.SetEffectEnabled(index, evt.newValue));
                name.text = $"{index + 1}. {fx.Name}";
                name.RegisterCallback<MouseDownEvent>(_ => _hub.SelectEffect(index));

                _fxList.Add(row);
                _fxRows.Add(new FxRow { root = row, toggle = toggle, name = name });
            }
            _builtEffectCount = _hub.Count;
            _inspectorEffect = -1; // 一覧が変わったら inspector も作り直す
        }

        void SyncFxRows()
        {
            var effects = _hub.Effects;
            for (int i = 0; i < _fxRows.Count && i < effects.Count; i++)
            {
                var fx = effects[i];
                if (fx == null) continue;
                var r = _fxRows[i];

                if (r.toggle.value != fx.enabled) r.toggle.SetValueWithoutNotify(fx.enabled);
                EnableClass(r.root, "rr-fx-item--selected", i == _hub.SelectedEffect);
                EnableClass(r.name, "rr-fx-name--disabled", !fx.enabled && i != _hub.SelectedEffect);
            }
        }

        // -------------------------------------------------- inspector (param rows)
        void RebuildInspector()
        {
            _inspector.Clear();
            _paramRows.Clear();

            var fx = _hub.GetEffect(_hub.SelectedEffect);
            _inspectorEffect = _hub.SelectedEffect;
            if (_inspectorTitle != null) _inspectorTitle.text = fx != null ? fx.Name : "Inspector";
            if (fx == null) return;

            var ps = fx.Parameters;
            for (int j = 0; j < ps.Count; j++)
            {
                var p = ps[j];
                int pIndex = j;

                VisualElement row = null;
                Label label = null;
                Slider slider = null;
                Label value = null;
                if (_paramRowTemplate != null)
                {
                    row = _paramRowTemplate.Instantiate().Q("param-row");
                    label = row?.Q<Label>("param-label");
                    slider = row?.Q<Slider>("param-slider");
                    value = row?.Q<Label>("param-value");
                }
                if (row == null || label == null || slider == null || value == null)
                {
                    row = new VisualElement(); row.AddToClassList("rr-param-row");
                    label = new Label(); label.AddToClassList("rr-param-label");
                    slider = new Slider(); slider.AddToClassList("rr-param-slider");
                    value = new Label(); value.AddToClassList("rr-param-value"); value.AddToClassList("rr-mono");
                    row.Add(label); row.Add(slider); row.Add(value);
                }

                label.text = p.Name;
                slider.lowValue = p.Min;
                slider.highValue = p.Max;
                slider.SetValueWithoutNotify(p.Value);
                slider.RegisterValueChangedCallback(evt =>
                {
                    p.Value = evt.newValue;
                    _hub.SelectEffect(_inspectorEffect);
                    _hub.SelectParam(pIndex);
                });
                slider.RegisterCallback<MouseDownEvent>(_ =>
                {
                    _hub.SelectEffect(_inspectorEffect);
                    _hub.SelectParam(pIndex);
                });
                value.text = p.Value.ToString("F2");

                _inspector.Add(row);
                _paramRows.Add(new ParamRow { root = row, slider = slider, value = value, param = p });
            }
        }

        void SyncParamRows()
        {
            for (int j = 0; j < _paramRows.Count; j++)
            {
                var r = _paramRows[j];
                float v = r.param.Value;
                // ドラッグ中（フォーカス中）は外部同期で値を奪わない
                if (r.slider.focusController == null || r.slider.focusController.focusedElement != r.slider)
                {
                    if (!Mathf.Approximately(r.slider.value, v)) r.slider.SetValueWithoutNotify(v);
                }
                r.value.text = v.ToString("F2");
                EnableClass(r.root, "rr-param-row--selected", j == _hub.SelectedParam);
            }
        }

        // -------------------------------------------------- helpers
        void ApplyVisibility()
        {
            if (_root != null) _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void EnableClass(VisualElement ve, string cls, bool on)
        {
            if (on) { if (!ve.ClassListContains(cls)) ve.AddToClassList(cls); }
            else    { if (ve.ClassListContains(cls)) ve.RemoveFromClassList(cls); }
        }
    }
}
