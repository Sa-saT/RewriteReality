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
        [Tooltip("上バー OUTPUT メニューの対象（未指定なら自動取得）")]
        [SerializeField] OutputManager _output;
        [Tooltip("WARP 編集オーバーレイの対象（埋め込み合成・未指定なら自動取得）")]
        [SerializeField] Compositor _compositor;
        [Tooltip("複数 Input Surface（M11・未配置なら単一 Compositor 経路にフォールバック）")]
        [SerializeField] SurfaceManager _surfaces;
        [Tooltip("出力変形（M10・OUTPUT warp モードの対象・未指定なら自動取得）")]
        [SerializeField] OutputWarp _outputWarp;
        [Tooltip("準備 Edit / 本番 Live のモード状態（未指定なら自動取得）")]
        [SerializeField] AppMode _appMode;

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

        // 上バー OUTPUT メニュー＋状態チップ（Fu/Sy/ND）
        Button _outputBtn;
        VisualElement _outputMenu;
        Button _chipFs, _chipSyphon, _chipNdi;   // OUTPUT 右の状態チップ（クリックで切替）
        Toggle _outFs, _outSyphon, _outNdi;       // メニュー内トグル

        // WARP 編集オーバーレイ（#21・メッシュ制御点ドラッグ）
        WarpCanvas _warpCanvas;
        Button _warpToggle, _warpReset, _warpTargetBtn, _warpEditModeBtn;
        Button _warpGridBtn, _warpTestBtn;   // GRID=格子オーバーレイ / TEST・CALIB=テストパターン校正（#34/#35）
        bool _warpEditing;
        bool _warpOutputMode;   // true=OUTPUT(出力変形) を編集 / false=EMBED(埋め込み)
        bool _warpContentMode;  // true=CONTENT(枠内映像 pan) / false=SHAPE(窓の形)
        bool _warpShowGrid;     // 細分化格子オーバーレイの表示状態
        readonly List<VisualElement> _cornerPins = new List<VisualElement>();  // 装飾ピン（編集中は隠す）
        IWarpTarget _warpTarget;   // 現在の warp 編集対象（選択 surface or Compositor）

        // Surface パネル（左ドック・#22）＋モード切替
        VisualElement _surfaceList, _surfaceProps;
        Label _surfaceEmpty;
        Button _surfaceAdd, _surfContent, _surfFit, _surfRemove;
        Button _surfColsDec, _surfColsInc, _surfRowsDec, _surfRowsInc;
        Button _surfScaleDec, _surfScaleInc;
        Label _surfColsVal, _surfRowsVal, _surfOpacityVal, _surfZoomVal;
        Toggle _surfEnabled;
        Slider _surfOpacity, _surfZoom;
        VisualElement _modeEdit, _modeLive;
        readonly List<SurfRow> _surfRows = new List<SurfRow>();
        int _builtSurfaceCount = -1;
        int _selectedSurfaceId = int.MinValue;   // 選択反映の変化検出用

        sealed class SurfRow
        {
            public VisualElement root; public VisualElement dot; public Label name; public Label meta; public int id;
            // 毎フレームの文字列生成を避けるための直近値キャッシュ（変化時のみ .text 更新）
            public int lastId = int.MinValue, lastCols = -1, lastRows = -1; public string lastName;
        }

        // FX 行・パラメータ行のバインド保持（再構築判定/毎フレーム同期用）
        readonly List<FxRow> _fxRows = new List<FxRow>();
        readonly List<ParamRow> _paramRows = new List<ParamRow>();
        int _builtEffectCount = -1;
        int _inspectorEffect = -1;
        float _smoothedDt;
        int _lastFpsShown = -1;

        sealed class FxRow { public VisualElement root; public Toggle toggle; public Label name; }
        sealed class ParamRow { public VisualElement root; public Slider slider; public Label value; public EffectParameter param; public int lastCenti = int.MinValue; }

        void Awake()
        {
            if (_hub == null) _hub = FindFirstObjectByType<ControlHub>();
            if (_chain == null) _chain = FindFirstObjectByType<EffectChain>();
            if (_output == null) _output = FindFirstObjectByType<OutputManager>();
            if (_compositor == null) _compositor = FindFirstObjectByType<Compositor>();
            if (_surfaces == null) _surfaces = FindFirstObjectByType<SurfaceManager>();
            if (_appMode == null) _appMode = FindFirstObjectByType<AppMode>();
            if (_outputWarp == null) _outputWarp = FindFirstObjectByType<OutputWarp>();
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

            BuildOutputControls();
            BuildWarpEditor();
            BuildSurfacePanel();
            BuildModeSwitch();

            _built = true;
            _builtEffectCount = -1;   // 次の LateUpdate で FX 一覧を構築
            _builtSurfaceCount = -1;
            _selectedSurfaceId = int.MinValue;
            _inspectorEffect = -1;
            ApplyVisibility();
        }

        // -------------------------------------------------- output routes (top bar)
        // ルート番号: 0=Fullscreen, 1=Syphon, 2=NDI
        void BuildOutputControls()
        {
            _outputBtn = _root.Q<Button>("rr-output-btn");
            _outputMenu = _root.Q<VisualElement>("rr-output-menu");
            _chipFs = _root.Q<Button>("rr-out-chip-fs");
            _chipSyphon = _root.Q<Button>("rr-out-chip-syphon");
            _chipNdi = _root.Q<Button>("rr-out-chip-ndi");
            _outFs = _root.Q<Toggle>("rr-out-fs");
            _outSyphon = _root.Q<Toggle>("rr-out-syphon");
            _outNdi = _root.Q<Toggle>("rr-out-ndi");

            if (_outputMenu != null) _outputMenu.style.display = DisplayStyle.None;
            if (_outputBtn != null) _outputBtn.clicked += ToggleOutputMenu;

            // チップ・メニュートグルの両方から同じルートを切替（状態は共有）
            if (_chipFs != null)     _chipFs.clicked     += () => ToggleRoute(0);
            if (_chipSyphon != null) _chipSyphon.clicked += () => ToggleRoute(1);
            if (_chipNdi != null)    _chipNdi.clicked    += () => ToggleRoute(2);
            if (_outFs != null)     _outFs.RegisterValueChangedCallback(evt => SetRoute(0, evt.newValue));
            if (_outSyphon != null) _outSyphon.RegisterValueChangedCallback(evt => SetRoute(1, evt.newValue));
            if (_outNdi != null)    _outNdi.RegisterValueChangedCallback(evt => SetRoute(2, evt.newValue));

            RefreshOutputStatus();
        }

        void ToggleOutputMenu()
        {
            if (_outputMenu == null) return;
            bool show = _outputMenu.style.display == DisplayStyle.None;
            _outputMenu.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        bool RouteAvailable(int route) => _output != null &&
            (route == 0 ? _output.HasFullscreen : route == 1 ? _output.HasSyphon : _output.HasNdi);

        bool RouteEnabled(int route) => _output != null &&
            (route == 0 ? _output.FullscreenEnabled : route == 1 ? _output.SyphonEnabled : _output.NdiEnabled);

        void ToggleRoute(int route)
        {
            if (!RouteAvailable(route)) return;
            SetRoute(route, !RouteEnabled(route));
        }

        void SetRoute(int route, bool on)
        {
            if (_output == null) return;
            if (route == 0) _output.FullscreenEnabled = on;
            else if (route == 1) _output.SyphonEnabled = on;
            else _output.NdiEnabled = on;
            RefreshOutputStatus();
        }

        void RefreshOutputStatus()
        {
            ApplyRoute(_chipFs, _outFs, 0);
            ApplyRoute(_chipSyphon, _outSyphon, 1);
            ApplyRoute(_chipNdi, _outNdi, 2);
        }

        void ApplyRoute(Button chip, Toggle t, int route)
        {
            bool available = RouteAvailable(route);
            bool on = available && RouteEnabled(route);
            if (chip != null)
            {
                chip.SetEnabled(available);
                EnableClass(chip, "rr-out-chip--on", on);
                EnableClass(chip, "rr-out-chip--off", !on);
            }
            if (t != null)
            {
                t.SetEnabled(available);
                t.SetValueWithoutNotify(on);
            }
        }

        // -------------------------------------------------- warp editor (#21)
        // ビューポート上に WarpCanvas を重ね、WARP トグルで編集モードに入る（既定はプレビュー）。
        void BuildWarpEditor()
        {
            var viewport = _root.Q<VisualElement>("rr-viewport");
            _warpToggle = _root.Q<Button>("rr-warp-toggle");
            _warpReset = _root.Q<Button>("rr-warp-reset");
            _warpTargetBtn = _root.Q<Button>("rr-warp-target");
            _warpEditModeBtn = _root.Q<Button>("rr-warp-editmode");
            _warpGridBtn = _root.Q<Button>("rr-warp-grid");
            _warpTestBtn = _root.Q<Button>("rr-warp-test");
            if (viewport == null) return;

            if (_warpCanvas == null)
            {
                _warpCanvas = new WarpCanvas { name = "rr-warp-canvas" };
                viewport.Add(_warpCanvas);            // 最前面（ピン等より上）に重ねる
                // WARP/RESET ボタンはキャンバスより前面に保つ（編集中でも押せるように）
                _warpToggle?.parent?.BringToFront();
            }

            // 装飾のコーナーピン（静的・非ドラッグ）は編集中に隠す。実ハンドルと紛らわしいため。
            _cornerPins.Clear();
            viewport.Query<VisualElement>(className: "rr-corner-pin").ForEach(e => _cornerPins.Add(e));
            _warpCanvas.ContentPan = OnContentPan;     // CONTENT モードのドラッグ → 選択 surface の content pan
            SetWarpTarget(ResolveWarpTarget());       // 選択 surface or Compositor
            _warpEditing = false;
            ApplyWarpEditing();

            if (_warpToggle != null) _warpToggle.clicked += ToggleWarpEditing;
            if (_warpReset != null)  _warpReset.clicked  += () => { _warpTarget?.ResetWarp(); _warpCanvas?.MarkDirtyRepaint(); };
            if (_warpTargetBtn != null) _warpTargetBtn.clicked += ToggleWarpOutputMode;
            if (_warpEditModeBtn != null) _warpEditModeBtn.clicked += ToggleWarpEditMode;
            if (_warpGridBtn != null) _warpGridBtn.clicked += ToggleWarpGrid;
            if (_warpTestBtn != null) _warpTestBtn.clicked += ToggleWarpTest;
            RefreshWarpTargetBtn();
            RefreshWarpEditModeBtn();
            RefreshWarpGridBtn();
            RefreshWarpTestBtn();
        }

        // CONTENT モードのドラッグ量（正規化 delta）を選択 surface の content offset に反映（Mask のみ意味を持つ）。
        void OnContentPan(Vector2 delta)
        {
            if (_warpOutputMode) return;
            var s = _surfaces?.Active;
            if (s == null) return;
            s.ContentOffset += delta;
        }

        // SHAPE（窓の形）⇄ CONTENT（枠内映像 pan）を切替。
        void ToggleWarpEditMode()
        {
            _warpContentMode = !_warpContentMode;
            _warpCanvas?.SetEditMode(_warpContentMode ? WarpCanvas.EditMode.Content : WarpCanvas.EditMode.Shape);
            RefreshWarpEditModeBtn();
        }

        void RefreshWarpEditModeBtn()
        {
            if (_warpEditModeBtn == null) return;
            _warpEditModeBtn.text = _warpContentMode ? "CONTENT" : "SHAPE";
            EnableClass(_warpEditModeBtn, "rr-warp-toggle--content", _warpContentMode);
        }

        /// <summary>EMBED（埋め込み）⇄ OUTPUT（出力変形）を切替。OUTPUT では OutputWarp を有効化する。</summary>
        void ToggleWarpOutputMode()
        {
            if (_outputWarp == null) return;
            _warpOutputMode = !_warpOutputMode;
            if (_warpOutputMode)
            {
                _outputWarp.SetEnabled(true);   // 見たまま反映されるよう有効化
                if (!_warpShowGrid) ToggleWarpGrid();   // OUTPUT 編集は格子オーバーレイ常時表示（#35）
            }
            SetWarpTarget(ResolveWarpTarget());
            RefreshWarpTargetBtn();
            RefreshWarpTestBtn();   // TEST⇄CALIB のラベル/状態はモード依存
        }

        /// <summary>細分化格子オーバーレイの表示を切替（#34/#35）。</summary>
        void ToggleWarpGrid()
        {
            _warpShowGrid = !_warpShowGrid;
            _warpCanvas?.SetLattice(_warpShowGrid);
            RefreshWarpGridBtn();
        }

        /// <summary>
        /// テストパターン校正の切替（#34/#35）。EMBED=選択 surface の content をパターン⇄カメラ、
        /// OUTPUT=出力全体をパターンへ差替（実際に投影して物理面と整列する定番手順）。
        /// </summary>
        void ToggleWarpTest()
        {
            if (_warpOutputMode)
            {
                if (_output == null) return;
                _output.CalibrationEnabled = !_output.CalibrationEnabled;
            }
            else
            {
                var s = _surfaces?.Active;
                if (s == null) return;
                s.Content = s.Content == Surface.ContentKind.Pattern
                    ? Surface.ContentKind.Camera : Surface.ContentKind.Pattern;
                SyncSurfaceProps();   // 左ドックの content チップ表示を追従
            }
            RefreshWarpTestBtn();
        }

        bool WarpTestOn => _warpOutputMode
            ? _output != null && _output.CalibrationEnabled
            : _surfaces?.Active != null && _surfaces.Active.Content == Surface.ContentKind.Pattern;

        void RefreshWarpGridBtn()
        {
            if (_warpGridBtn == null) return;
            EnableClass(_warpGridBtn, "rr-warp-toggle--grid", _warpShowGrid);
        }

        void RefreshWarpTestBtn()
        {
            if (_warpTestBtn == null) return;
            _warpTestBtn.text = _warpOutputMode ? "CALIB" : "TEST";
            _warpTestBtn.SetEnabled(_warpOutputMode ? _output != null : _surfaces?.Active != null);
            EnableClass(_warpTestBtn, "rr-warp-toggle--test", WarpTestOn);
        }

        void RefreshWarpTargetBtn()
        {
            if (_warpTargetBtn == null) return;
            _warpTargetBtn.SetEnabled(_outputWarp != null);
            _warpTargetBtn.text = _warpOutputMode ? "OUTPUT" : "EMBED";
            EnableClass(_warpTargetBtn, "rr-warp-toggle--output", _warpOutputMode);
        }

        /// <summary>現在の warp 編集対象。OUTPUT モード=OutputWarp、そうでなければ選択 surface or 単一 Compositor。</summary>
        IWarpTarget ResolveWarpTarget()
        {
            if (_warpOutputMode && _outputWarp != null) return _outputWarp;
            if (_surfaces != null && _surfaces.Count > 0 && _surfaces.Active != null)
                return _surfaces.Active;
            return _compositor;
        }

        void SetWarpTarget(IWarpTarget t)
        {
            if (ReferenceEquals(_warpTarget, t)) return;
            _warpTarget = t;
            _warpCanvas?.Bind(t);
        }

        void ToggleWarpEditing()
        {
            _warpEditing = !_warpEditing;
            ApplyWarpEditing();
        }

        void ApplyWarpEditing()
        {
            if (_warpCanvas != null)
            {
                _warpCanvas.style.display = _warpEditing ? DisplayStyle.Flex : DisplayStyle.None;
                _warpCanvas.pickingMode = _warpEditing ? PickingMode.Position : PickingMode.Ignore;
            }
            if (_warpToggle != null) EnableClass(_warpToggle, "rr-warp-toggle--active", _warpEditing);
            if (_warpReset != null)  _warpReset.style.display = _warpEditing ? DisplayStyle.Flex : DisplayStyle.None;
            if (_warpTargetBtn != null) _warpTargetBtn.style.display = _warpEditing ? DisplayStyle.Flex : DisplayStyle.None;
            if (_warpEditModeBtn != null) _warpEditModeBtn.style.display = _warpEditing ? DisplayStyle.Flex : DisplayStyle.None;
            if (_warpGridBtn != null) _warpGridBtn.style.display = _warpEditing ? DisplayStyle.Flex : DisplayStyle.None;
            if (_warpTestBtn != null) _warpTestBtn.style.display = _warpEditing ? DisplayStyle.Flex : DisplayStyle.None;

            if (!_warpEditing && _warpContentMode)   // 編集終了時は SHAPE に戻す
            {
                _warpContentMode = false;
                _warpCanvas?.SetEditMode(WarpCanvas.EditMode.Shape);
                RefreshWarpEditModeBtn();
            }

            // 編集終了時はテストパターンを自動で戻す（隠れたトグルの裏でパターンが出続けるのを防ぐ）
            if (!_warpEditing)
            {
                if (_output != null && _output.CalibrationEnabled) _output.CalibrationEnabled = false;
                var s = _surfaces?.Active;
                if (s != null && s.Content == Surface.ContentKind.Pattern)
                {
                    s.Content = Surface.ContentKind.Camera;
                    SyncSurfaceProps();
                }
                RefreshWarpTestBtn();
            }

            // 装飾コーナーピンは編集中のみ隠す（プレビュー時は Claude Design 通り残す）
            for (int i = 0; i < _cornerPins.Count; i++)
                _cornerPins[i].style.display = _warpEditing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // -------------------------------------------------- surface panel (#22)
        void BuildSurfacePanel()
        {
            _surfaceList = _root.Q<VisualElement>("rr-surface-list");
            _surfaceEmpty = _root.Q<Label>("rr-surface-empty");
            _surfaceProps = _root.Q<VisualElement>("rr-surface-props");
            _surfaceAdd = _root.Q<Button>("rr-surface-add");
            _surfEnabled = _root.Q<Toggle>("rr-surf-enabled");
            _surfOpacity = _root.Q<Slider>("rr-surf-opacity");
            _surfOpacityVal = _root.Q<Label>("rr-surf-opacity-val");
            _surfContent = _root.Q<Button>("rr-surf-content");
            _surfFit = _root.Q<Button>("rr-surf-fit");
            _surfColsDec = _root.Q<Button>("rr-surf-cols-dec");
            _surfColsInc = _root.Q<Button>("rr-surf-cols-inc");
            _surfColsVal = _root.Q<Label>("rr-surf-cols-val");
            _surfRowsDec = _root.Q<Button>("rr-surf-rows-dec");
            _surfRowsInc = _root.Q<Button>("rr-surf-rows-inc");
            _surfRowsVal = _root.Q<Label>("rr-surf-rows-val");
            _surfScaleDec = _root.Q<Button>("rr-surf-scale-dec");
            _surfScaleInc = _root.Q<Button>("rr-surf-scale-inc");
            _surfZoom = _root.Q<Slider>("rr-surf-zoom");
            _surfZoomVal = _root.Q<Label>("rr-surf-zoom-val");
            _surfRemove = _root.Q<Button>("rr-surf-remove");

            if (_surfaceAdd != null) _surfaceAdd.clicked += () =>
            {
                if (_surfaces == null) return;
                if (_surfaces.Add("Surface") != null) _builtSurfaceCount = -1; // 再構築＋選択反映
            };
            if (_surfRemove != null) _surfRemove.clicked += () =>
            {
                if (_surfaces?.Active != null && _surfaces.Remove(_surfaces.Active)) _builtSurfaceCount = -1;
            };
            if (_surfEnabled != null) _surfEnabled.RegisterValueChangedCallback(evt =>
            { if (_surfaces?.Active != null) _surfaces.Active.Enabled = evt.newValue; });
            if (_surfOpacity != null) _surfOpacity.RegisterValueChangedCallback(evt =>
            {
                if (_surfaces?.Active != null) _surfaces.Active.Opacity = evt.newValue;
                if (_surfOpacityVal != null) _surfOpacityVal.text = evt.newValue.ToString("F2");
            });
            if (_surfContent != null) _surfContent.clicked += CycleContent;
            if (_surfFit != null) _surfFit.clicked += ToggleFit;
            if (_surfColsDec != null) _surfColsDec.clicked += () => NudgeGrid(-1, 0);
            if (_surfColsInc != null) _surfColsInc.clicked += () => NudgeGrid(+1, 0);
            if (_surfRowsDec != null) _surfRowsDec.clicked += () => NudgeGrid(0, -1);
            if (_surfRowsInc != null) _surfRowsInc.clicked += () => NudgeGrid(0, +1);
            if (_surfScaleDec != null) _surfScaleDec.clicked += () => ScaleWarpTarget(1f / 1.1f);
            if (_surfScaleInc != null) _surfScaleInc.clicked += () => ScaleWarpTarget(1.1f);
            if (_surfZoom != null) _surfZoom.RegisterValueChangedCallback(evt =>
            {
                if (_surfaces?.Active != null) _surfaces.Active.ContentZoom = evt.newValue;
                if (_surfZoomVal != null) _surfZoomVal.text = evt.newValue.ToString("F1");
            });
        }

        /// <summary>現在の warp 対象（選択 surface / Compositor）の制御点を重心中心に拡大縮小（窓自体のスケール）。</summary>
        void ScaleWarpTarget(float factor)
        {
            var t = _warpTarget;
            if (t == null) return;
            t.EnsureWarpPoints();
            int cols = t.WarpCols, rows = t.WarpRows;

            Vector2 c = Vector2.zero; int n = 0;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++) { c += t.GetWarpPoint(i, j); n++; }
            if (n == 0) return;
            c /= n;

            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    Vector2 p = t.GetWarpPoint(i, j);
                    Vector2 np = c + (p - c) * factor;
                    t.SetWarpPoint(i, j, new Vector2(Mathf.Clamp01(np.x), Mathf.Clamp01(np.y)));
                }
            _warpCanvas?.MarkDirtyRepaint();
        }

        void CycleContent()
        {
            var s = _surfaces?.Active;
            if (s == null) return;
            s.Content = (Surface.ContentKind)(((int)s.Content + 1) % 4);   // Camera→Video→None→Pattern
            if (_surfContent != null) _surfContent.text = s.Content.ToString().ToUpperInvariant();
            RefreshWarpTestBtn();   // TEST トグルの ON 表示は content=Pattern と連動
        }

        // Mask（歪まない窓抜き）⇄ Project（射影で流し込む）を切替
        void ToggleFit()
        {
            var s = _surfaces?.Active;
            if (s == null) return;
            s.Fit = s.Fit == Surface.FitMode.Mask ? Surface.FitMode.Project : Surface.FitMode.Mask;
            RefreshFitBtn(s);
        }

        void RefreshFitBtn(Surface s)
        {
            if (_surfFit == null) return;
            bool project = s != null && s.Fit == Surface.FitMode.Project;
            _surfFit.text = project ? "PROJECT" : "MASK";
            EnableClass(_surfFit, "rr-surf-chip--project", project);   // Project=歪む=アンバー強調
        }

        void NudgeGrid(int dCols, int dRows)
        {
            var s = _surfaces?.Active;
            if (s == null) return;
            s.SetGridResolution(s.WarpCols + dCols, s.WarpRows + dRows);
            if (_surfColsVal != null) _surfColsVal.text = s.WarpCols.ToString();
            if (_surfRowsVal != null) _surfRowsVal.text = s.WarpRows.ToString();
            _warpCanvas?.Bind(s);   // グリッド解像度が変わったので再バインド＋再描画
        }

        void SyncSurfaces()
        {
            int count = _surfaces != null ? _surfaces.Count : 0;
            if (count != _builtSurfaceCount) RebuildSurfaceList();

            int activeId = _surfaces?.Active?.Id ?? int.MinValue;
            if (activeId != _selectedSurfaceId)
            {
                _selectedSurfaceId = activeId;
                SyncSurfaceProps();
                SetWarpTarget(ResolveWarpTarget());   // 選択が変われば WARP 対象も切替
                RefreshWarpTestBtn();                 // TEST の ON 表示は選択 surface の content 依存
            }
            SyncSurfaceRows();
        }

        void RebuildSurfaceList()
        {
            _surfRows.Clear();
            if (_surfaceList != null) _surfaceList.Clear();

            int count = _surfaces != null ? _surfaces.Count : 0;
            if (_surfaceEmpty != null) _surfaceEmpty.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (_surfaceList != null && _surfaces != null)
            {
                var list = _surfaces.Surfaces;
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (s == null) continue;
                    int index = i;

                    var row = new VisualElement(); row.AddToClassList("rr-list-item");
                    var dot = new VisualElement(); dot.AddToClassList("rr-list-dot"); dot.AddToClassList("rr-list-dot--tracking");
                    var name = new Label(); name.AddToClassList("rr-list-label");
                    var meta = new Label(); meta.AddToClassList("rr-list-meta"); meta.AddToClassList("rr-mono");
                    row.Add(dot); row.Add(name); row.Add(meta);
                    row.RegisterCallback<MouseDownEvent>(_ => { if (_surfaces != null) _surfaces.ActiveIndex = index; });

                    _surfaceList.Add(row);
                    _surfRows.Add(new SurfRow { root = row, dot = dot, name = name, meta = meta, id = s.Id });
                }
            }
            _builtSurfaceCount = count;
            _selectedSurfaceId = int.MinValue; // 次の同期で props/target を作り直す
        }

        void SyncSurfaceRows()
        {
            if (_surfaces == null) return;
            var list = _surfaces.Surfaces;
            int active = _surfaces.ActiveIndex;
            for (int i = 0; i < _surfRows.Count && i < list.Count; i++)
            {
                var s = list[i];
                if (s == null) continue;
                var r = _surfRows[i];
                if (r.lastId != s.Id || !ReferenceEquals(r.lastName, s.Name))
                {
                    r.name.text = $"{s.Id + 1}. {s.Name}";
                    r.lastId = s.Id; r.lastName = s.Name;
                }
                if (r.lastCols != s.WarpCols || r.lastRows != s.WarpRows)
                {
                    r.meta.text = $"{s.WarpCols}×{s.WarpRows}";
                    r.lastCols = s.WarpCols; r.lastRows = s.WarpRows;
                }
                EnableClass(r.root, "rr-list-item--active", i == active);
                EnableClass(r.name, "rr-list-label--off", !s.Enabled && i != active);
            }
        }

        void SyncSurfaceProps()
        {
            var s = _surfaces?.Active;
            bool show = s != null;
            if (_surfaceProps != null) _surfaceProps.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            if (_surfEnabled != null) _surfEnabled.SetValueWithoutNotify(s.Enabled);
            if (_surfOpacity != null) _surfOpacity.SetValueWithoutNotify(s.Opacity);
            if (_surfOpacityVal != null) _surfOpacityVal.text = s.Opacity.ToString("F2");
            if (_surfContent != null) _surfContent.text = s.Content.ToString().ToUpperInvariant();
            RefreshFitBtn(s);
            if (_surfColsVal != null) _surfColsVal.text = s.WarpCols.ToString();
            if (_surfRowsVal != null) _surfRowsVal.text = s.WarpRows.ToString();
            if (_surfZoom != null) _surfZoom.SetValueWithoutNotify(s.ContentZoom);
            if (_surfZoomVal != null) _surfZoomVal.text = s.ContentZoom.ToString("F1");
        }

        // -------------------------------------------------- mode switch (#22)
        void BuildModeSwitch()
        {
            _modeEdit = _root.Q<VisualElement>("rr-mode-edit");
            _modeLive = _root.Q<VisualElement>("rr-mode-live");
            if (_modeEdit != null) _modeEdit.RegisterCallback<MouseDownEvent>(_ => _appMode?.SetMode(AppMode.Mode.Edit));
            if (_modeLive != null) _modeLive.RegisterCallback<MouseDownEvent>(_ => _appMode?.SetMode(AppMode.Mode.Live));
            if (_appMode != null) { _appMode.ModeChanged -= OnModeChanged; _appMode.ModeChanged += OnModeChanged; }
            RefreshModeUI();
        }

        void OnModeChanged(AppMode.Mode m) => RefreshModeUI();

        void RefreshModeUI()
        {
            bool edit = _appMode == null || _appMode.IsEdit;
            if (_modeEdit != null) EnableClass(_modeEdit, "rr-mode-opt--active", edit);
            if (_modeLive != null) EnableClass(_modeLive, "rr-mode-opt--live-active", !edit);
            // 構成変更（追加/削除）は準備 Edit のみ許可
            if (_surfaceAdd != null) _surfaceAdd.SetEnabled(edit);
            if (_surfRemove != null) _surfRemove.SetEnabled(edit);
        }

        void OnDisable()
        {
            if (_appMode != null) _appMode.ModeChanged -= OnModeChanged;
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
            SyncSurfaces();

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
            if (_preview == null) return;
            // OUTPUT warp 編集中は、見たまま調整できるよう変形後 RT を表示（無ければ Final RT）。
            Texture rt = null;
            if (_warpOutputMode && _outputWarp != null && _outputWarp.Active) rt = _outputWarp.Output;
            if (rt == null && _chain != null) rt = _chain.FinalTexture;
            if (rt != null && _preview.image != rt) _preview.image = rt;
        }

        void UpdateFps()
        {
            if (_fps == null) return;
            _smoothedDt = Mathf.Lerp(_smoothedDt, Time.unscaledDeltaTime, 0.1f);
            float fps = _smoothedDt > 0f ? 1f / _smoothedDt : 0f;
            int shown = Mathf.RoundToInt(fps);
            if (shown != _lastFpsShown) { _fps.text = shown + " FPS"; _lastFpsShown = shown; }
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
                // F2 表示（小数2桁）は centi 単位で変化した時だけ更新（毎フレームの ToString を避ける）
                int centi = Mathf.RoundToInt(v * 100f);
                if (centi != r.lastCenti) { r.value.text = v.ToString("F2"); r.lastCenti = centi; }
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
