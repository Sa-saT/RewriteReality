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

        [Tooltip("タイムライン再生バックエンド（Song トランスポート・playhead）")]
        [SerializeField] ShowTimeline _timeline;

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
        Label _perf2;           // 上部バー右・2 段目「X.X ms · gc N」（フレーム時間＋GC 収集回数）
        int _lastGcShown = -1;
        int _lastFrameMsShown = -1;

        // 汎用セレクションモデル（§3・#15）
        readonly SelectionModel _selection = new SelectionModel();
        readonly List<(VisualElement item, SelectionKind kind, string id)> _dockItems
            = new List<(VisualElement, SelectionKind, string)>();

        // 終了 UX（§7・#12）
        VisualElement _brandMenu, _quitOverlay, _quitOnAir;
        Label _quitRoutes, _quitTitle, _quitMsg;
        Button _quitCancel, _quitConfirm;
        bool _quitConfirmed;
        bool _quitHooked;
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

        // タイムライン song/short タブ（07b §3.5.2・#27 足場＝タブ切替とホールド表現のみ）
        VisualElement _tlTablist, _tlSong, _tlShort, _shortClip, _tlAddMenu;
        Button _tlAddButton;        // タブバーの + （追加メニューのアンカー）
        bool _shortView;            // いま short タブを表示中か
        Button _shortPadBtn;              // KEY 行の [PAD n] ＝マトリクスのトグル
        VisualElement _padMatrix;         // 4×4 パッド割当マトリクス（§7・#13）
        readonly Button[] _padCells = new Button[16];
        Toggle _holdLoopToggle;           // per-Short の Hold-Loop

        // タイムライン再生（Song トランスポート・playhead・時間表示）
        Button _tlPrev, _tlPlay, _tlLoop;
        Label _tlCur, _tlTotal, _remain;
        VisualElement _playhead;
        RrIcon _tlPlayIcon;   // 再生/一時停止アイコン切替
        double _lastTimeValue = -1.0;   // 時刻が変わった時だけ整形（停止中は毎フレーム alloc しない）
        // 再生ヘッドをクリップ・レーンに合わせる際の左右オフセット（USS .rr-track-head / .rr-track-tail 幅）。
        const float TimelineLaneLeft = 96f;
        const float TimelineLaneRight = 74f;

        // Surface パネル（左ドック・#22）＋モード切替
        VisualElement _surfaceList, _surfaceProps;
        Label _surfaceEmpty;
        Button _surfaceAdd, _surfContent, _surfRemove;
        Button _surfFitMask, _surfFitGrid, _surfResetWarp;   // Fit Mode セグメント＋Grid の Reset Warp（§5）
        VisualElement _surfMaskSec, _surfGridSec;            // Mask/Grid で出し分けるセクション
        Button _surfColsDec, _surfColsInc, _surfRowsDec, _surfRowsInc;
        Button _surfScaleDec, _surfScaleInc;
        Label _surfColsVal, _surfRowsVal, _surfOpacityVal, _surfZoomVal;
        Toggle _surfEnabled, _surfTestPat;
        Slider _surfOpacity, _surfZoom;
        VisualElement _modeEdit, _modeLive;

        // ページタブ（2ページ IA・PERFORM/MAPPING・UNITY-HANDOFF §1）
        // 中央ワークスペースが変わる場合のみページが存在する（DaVinci 原則）。旧 OUTPUT は MAPPING の
        // EMBED⇄OUTPUT セグメント＋上バーのルートトグルへ吸収。現状は active 状態の切替のみ（中央スワップは #2）。
        Label _pagePerform, _pageMapping;
        int _page;   // 0=PERFORM, 1=MAPPING
        VisualElement _dockPerform, _dockMapping;   // 左ドックのページ依存差替（PERFORM=ライブラリ / MAPPING=Surfaces+Input）

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
            _perf2 = _root.Q<Label>("rr-perf2");
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
            BuildPageTabs();
            BuildTimelineTransport();   // 先に _timeline を解決（Tabs 側の Short 発火が参照する）
            BuildTimelineTabs();
            BuildExitUx();
            BuildDockSelection();

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
            _surfFitMask = _root.Q<Button>("rr-surf-fit-mask");
            _surfFitGrid = _root.Q<Button>("rr-surf-fit-grid");
            _surfMaskSec = _root.Q<VisualElement>("rr-surf-mask-sec");
            _surfGridSec = _root.Q<VisualElement>("rr-surf-grid-sec");
            _surfTestPat = _root.Q<Toggle>("rr-surf-testpat");
            _surfResetWarp = _root.Q<Button>("rr-surf-resetwarp");
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
            if (_surfFitMask != null) _surfFitMask.clicked += () => SetFit(false);
            if (_surfFitGrid != null) _surfFitGrid.clicked += () => SetFit(true);
            if (_surfResetWarp != null) _surfResetWarp.clicked += () =>
            {
                var s = _surfaces?.Active;
                if (s == null) return;
                s.ResetWarp();
                _warpCanvas?.MarkDirtyRepaint();
            };
            if (_surfTestPat != null) _surfTestPat.RegisterValueChangedCallback(evt =>
            {
                var s = _surfaces?.Active;
                if (s == null) return;
                s.Content = evt.newValue ? Surface.ContentKind.Pattern : Surface.ContentKind.Camera;   // §5 校正
                if (_surfContent != null) _surfContent.text = s.Content.ToString().ToUpperInvariant();
                RefreshWarpTestBtn();   // ビューポート TEST ボタンと状態を一致
            });
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
            if (_surfTestPat != null) _surfTestPat.SetValueWithoutNotify(s.Content == Surface.ContentKind.Pattern);
            RefreshWarpTestBtn();   // TEST トグルの ON 表示は content=Pattern と連動
        }

        // Fit Mode セグメント（MASK|GRID・UNITY-HANDOFF §5）。Mask=歪まない窓抜き（既定）、
        // Grid=Bezier グリッドで歪ませて流し込む（#34）。選択でモード別セクションを出し分ける。
        void SetFit(bool grid)
        {
            var s = _surfaces?.Active;
            if (s == null) return;
            s.Fit = grid ? Surface.FitMode.Grid : Surface.FitMode.Mask;
            RefreshFit(s);
        }

        void RefreshFit(Surface s)
        {
            bool grid = s != null && s.Fit == Surface.FitMode.Grid;
            if (_surfFitMask != null) EnableClass(_surfFitMask, "rr-seg__btn--active", !grid);
            if (_surfFitGrid != null) EnableClass(_surfFitGrid, "rr-seg__btn--active", grid);
            if (_surfMaskSec != null) _surfMaskSec.style.display = grid ? DisplayStyle.None : DisplayStyle.Flex;
            if (_surfGridSec != null) _surfGridSec.style.display = grid ? DisplayStyle.Flex : DisplayStyle.None;
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
            RefreshFit(s);
            if (_surfTestPat != null) _surfTestPat.SetValueWithoutNotify(s.Content == Surface.ContentKind.Pattern);
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

        // -------------------------------------------------- page tabs (2-page IA・UNITY-HANDOFF §1)
        // PERFORM=ライブプレビュー+ライブラリ / MAPPING=WARP エディタ(EMBED⇄OUTPUT)+Surfaces。
        // 現状はタブの active 状態のみ切替（中央/ドックのページ依存スワップは #2 で接続）。
        void BuildPageTabs()
        {
            _pagePerform = _root.Q<Label>("rr-tab-perform");
            _pageMapping = _root.Q<Label>("rr-tab-mapping");
            _dockPerform = _root.Q<VisualElement>("rr-dock-perform");
            _dockMapping = _root.Q<VisualElement>("rr-dock-mapping");

            _pagePerform?.RegisterCallback<MouseDownEvent>(_ => SelectPage(0));
            _pageMapping?.RegisterCallback<MouseDownEvent>(_ => SelectPage(1));

            // MAPPING がワープ編集を制御するので、ビューポート内の独立 ◇ WARP トグルは隠す（§6・1機能1箇所）。
            if (_warpToggle != null) _warpToggle.style.display = DisplayStyle.None;

            SelectPage(0);
        }

        // PERFORM=ライブプレビュー / MAPPING=WARP エディタ（#34 の多pin メッシュ編集・EMBED⇄OUTPUT）。
        // ページ切替でワープ編集の ON/OFF を駆動する（§1/§6）。左ドックのライブラリ⇄Surfaces 差替は将来分。
        void SelectPage(int page)
        {
            _page = page;
            // ページ切替でドック選択はクリア（track 選択は保持・§3）。
            if (_selection.Current.Kind != SelectionKind.Track) _selection.Deselect();
            if (_pagePerform != null) EnableClass(_pagePerform, "rr-page-tab--active", page == 0);
            if (_pageMapping != null) EnableClass(_pageMapping, "rr-page-tab--active", page == 1);

            bool mapping = page == 1;

            // 左ドックの差替（PERFORM=ライブラリ / MAPPING=Surfaces+Input+Output Surface）
            if (_dockPerform != null) _dockPerform.style.display = mapping ? DisplayStyle.None : DisplayStyle.Flex;
            if (_dockMapping != null) _dockMapping.style.display = mapping ? DisplayStyle.Flex : DisplayStyle.None;

            if (mapping) SetWarpTarget(ResolveWarpTarget());   // 現在の選択 surface / Compositor に束ねる
            if (_warpEditing != mapping)
            {
                _warpEditing = mapping;
                ApplyWarpEditing();
            }
        }

        // -------------------------------------------------- timeline song/short tabs (07b §3.5.2 / §7・#13)
        // タブ切替（song=リニア通し / short=ホールド発火）＋ Short の 4×4 パッド割当マトリクス／Hold-Loop。
        void BuildTimelineTabs()
        {
            _tlTablist  = _root.Q<VisualElement>("rr-tl-tablist");
            _tlSong     = _root.Q<VisualElement>("rr-tl-song");
            _tlShort    = _root.Q<VisualElement>("rr-tl-short");
            _shortClip  = _root.Q<VisualElement>("rr-short-clip");
            _shortPadBtn = _root.Q<Button>("rr-short-pad");
            _padMatrix   = _root.Q<VisualElement>("rr-pad-matrix");
            _holdLoopToggle = _root.Q<Toggle>("rr-short-holdloop");

            // 動的タブバー（Song/Short 追加・削除・切替・07-10 App.jsx）
            _tlAddMenu = _root.Q<VisualElement>("rr-tl-addmenu");
            _tlAddButton = _root.Q<Button>("rr-tl-add");
            if (_tlAddButton != null) _tlAddButton.clicked += ToggleAddMenu;
            var addSong = _root.Q<Button>("rr-tl-add-song");
            var addShort = _root.Q<Button>("rr-tl-add-short");
            if (addSong != null) addSong.clicked += () =>
            {
                HideAddMenu();
                if (_timeline == null) return;
                SelectTab(ShowTimeline.TabKind.Song, _timeline.AddSong());
            };
            if (addShort != null) addShort.clicked += () =>
            {
                HideAddMenu();
                if (_timeline == null) return;
                SelectTab(ShowTimeline.TabKind.Short, _timeline.AddShort());
            };
            if (_tlTablist == null || _tlAddMenu == null)
                Debug.LogWarning($"[OperatorUI] タイムラインのタブ要素が見つかりません（tablist={_tlTablist != null} addmenu={_tlAddMenu != null}）。" +
                                 "OperatorShell.uxml が最新にリインポートされているか確認してください。");
            RebuildTimelineTabs();

            BuildPadMatrix();

            // KEY 行の [PAD n] ＝マトリクスの開閉トグル。
            if (_shortPadBtn != null)
                _shortPadBtn.clicked += TogglePadMatrix;

            // Hold-Loop（per-Short・本番でキー押下中ループするか）。
            if (_holdLoopToggle != null)
                _holdLoopToggle.RegisterValueChangedCallback(evt =>
                {
                    var sh = _timeline?.ActiveShort;
                    if (sh != null) sh.holdLoop = evt.newValue;
                });

            // 発火トリガーは割当パッド/キーのみ（UI に FIRE ボタンは置かない・2026-07-09 設計更新）。
            // ここでは発火状態の反映だけ購読し、held 中はレーンを Live Amber 点灯する（RefreshShortHeld）。
            if (_timeline != null)
            {
                _timeline.ShortStateChanged -= RefreshShortHeld;
                _timeline.ShortStateChanged += RefreshShortHeld;
            }
            RefreshShortHeld();
            RefreshShortAssignment();
        }

        int ShownShortIndex()
        {
            int i = _timeline != null ? _timeline.ActiveShortIndex : -1;
            return i < 0 ? 0 : i;
        }

        // 4×4 パッドセルを生成（1 度だけ）。クリックで表示中 Short に割当（他 Short は奪取＝未割当化）。
        // 4×4 マトリクス。各セルは割当キー（Q/W/…/1/2/3/4）を大きく＋pad 番号を小さく表示（07-10）。
        // パッド割当＝キー割当（MIDI 不在時のキーボード発火）。自分=琥珀 / 他 Short 使用中=ローズ点。
        void BuildPadMatrix()
        {
            if (_padMatrix == null) return;
            _padMatrix.Clear();

            var header = new Label("ASSIGN KEY");
            header.AddToClassList("rr-pad-matrix__head");
            _padMatrix.Add(header);

            for (int r = 0; r < 4; r++)
            {
                var row = new VisualElement();
                row.AddToClassList("rr-pad-matrix__row");
                for (int c = 0; c < 4; c++)
                {
                    int idx = r * 4 + c;
                    var cell = new Button();
                    cell.AddToClassList("rr-pad-cell");

                    var glyph = new Label(ShowTimeline.PadGlyph(idx));
                    glyph.AddToClassList("rr-pad-cell__glyph");
                    glyph.AddToClassList("rr-mono");
                    cell.Add(glyph);

                    var sub = new Label((idx + 1).ToString());
                    sub.AddToClassList("rr-pad-cell__idx");
                    sub.AddToClassList("rr-mono");
                    cell.Add(sub);

                    cell.clicked += () =>
                    {
                        _timeline?.AssignPad(ShownShortIndex(), idx);
                        RefreshShortAssignment();
                        RebuildTimelineTabs();   // タブの keycap チップも追従
                    };
                    _padCells[idx] = cell;
                    row.Add(cell);
                }
                _padMatrix.Add(row);
            }
        }

        void TogglePadMatrix()
        {
            if (_padMatrix == null) return;
            bool show = _padMatrix.style.display == DisplayStyle.None;
            _padMatrix.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) RefreshShortAssignment();
        }

        // パッド割当・割当キーラベル・タブチップ・Hold-Loop を表示中 Short に同期。
        void RefreshShortAssignment()
        {
            var sh = _timeline?.ActiveShort;
            int mine = sh != null ? sh.pad : -1;

            for (int i = 0; i < _padCells.Length; i++)
            {
                var cell = _padCells[i];
                if (cell == null) continue;
                int owner = _timeline != null ? _timeline.ShortForPad(i) : -1;
                EnableClass(cell, "rr-pad-cell--mine", i == mine);
                EnableClass(cell, "rr-pad-cell--used", i != mine && owner >= 0);
            }

            // 割当ボタン＝割当キー（グリフ）＋開閉手がかり ⌄。未割当は UNASSIGNED（減光）。
            if (_shortPadBtn != null)
            {
                _shortPadBtn.text = mine >= 0 ? ShowTimeline.PadGlyph(mine) + "  ⌄" : "UNASSIGNED  ⌄";
                EnableClass(_shortPadBtn, "rr-short-pad--unassigned", mine < 0);
            }

            if (_holdLoopToggle != null && sh != null) _holdLoopToggle.SetValueWithoutNotify(sh.holdLoop);
        }

        // ---- 動的タブバー（Song/Short・07-10 App.jsx）----
        // ShowTimeline の songs+shorts からタブを生成。クリックで切替、× で閉じる、+ で New Song/Short。
        void RebuildTimelineTabs()
        {
            if (_tlTablist == null) return;
            _tlTablist.Clear();
            if (_timeline == null) return;
            _timeline.EnsureSeeded();   // Awake 未実行でもタブが出るよう明示シード

            for (int i = 0; i < _timeline.SongCount; i++)
                _tlTablist.Add(BuildTab(ShowTimeline.TabKind.Song, i));
            for (int i = 0; i < _timeline.ShortCount; i++)
                _tlTablist.Add(BuildTab(ShowTimeline.TabKind.Short, i));

            ApplyTimelineView();
        }

        VisualElement BuildTab(ShowTimeline.TabKind kind, int index)
        {
            bool isSong = kind == ShowTimeline.TabKind.Song;
            bool active = isSong ? (!_shortView && _timeline.ActiveSongIndex == index)
                                 : (_shortView && _timeline.ActiveShortIndex == index);

            var tab = new VisualElement();
            tab.AddToClassList("rr-tl-tab");
            tab.AddToClassList(isSong ? "rr-tl-tab--song" : "rr-tl-tab--short");
            EnableClass(tab, "rr-tl-tab--active", active);

            var icon = new RrIcon { Icon = isSong ? RrIcon.Kind.AudioLines : RrIcon.Kind.Zap };
            icon.AddToClassList("rr-tl-tab__icon");
            tab.Add(icon);

            var name = new Label(isSong ? _timeline.GetSong(index).name : _timeline.GetShort(index).name);
            name.AddToClassList("rr-tl-tab__name");
            tab.Add(name);

            // SONG/SHORT バッジは廃止（2026-07-12・kind アイコンで種別は分かるため、
            // タブ幅を Key 割当（keycap）に譲る＝視認性優先）。
            if (!isSong)
            {
                // keycap 型チップ（07-10）: 割当キー（主）＋pad 番号（小）。未割当は「·」薄色。
                int pad = _timeline.GetShort(index).pad;
                var keycap = new VisualElement();
                keycap.AddToClassList("rr-keycap");
                if (pad >= 0)
                {
                    var glyph = new Label(ShowTimeline.PadGlyph(pad));
                    glyph.AddToClassList("rr-keycap__glyph");
                    glyph.AddToClassList("rr-mono");
                    keycap.Add(glyph);
                    var idx = new Label((pad + 1).ToString());
                    idx.AddToClassList("rr-keycap__idx");
                    idx.AddToClassList("rr-mono");
                    keycap.Add(idx);
                }
                else
                {
                    keycap.AddToClassList("rr-keycap--empty");
                    var dot = new Label("·");
                    dot.AddToClassList("rr-keycap__glyph");
                    keycap.Add(dot);
                }
                EnableClass(keycap, "rr-keycap--active", active);
                tab.Add(keycap);
            }

            // 閉じる（合計 1 枚のときは出さない）
            if (_timeline.TabCount > 1)
            {
                var close = new Button { text = "×" };
                close.AddToClassList("rr-tl-tab__close");
                // Button は Clickable が PointerDown を消費するため MouseDown 登録では発火しない。
                // clicked で閉じ、PointerDown の伝播だけ止めて親タブの選択（MouseDown）を防ぐ。
                close.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                close.clicked += () =>
                {
                    if (_timeline.RemoveTab(kind, index))
                    {
                        // 表示中の種別が空になったら、残っている種別へ切り替える。
                        if (_shortView && _timeline.ShortCount == 0) _shortView = false;
                        else if (!_shortView && _timeline.SongCount == 0) _shortView = true;
                        RebuildTimelineTabs();
                        RefreshShortAssignment();
                    }
                };
                tab.Add(close);
            }

            tab.RegisterCallback<MouseDownEvent>(_ => SelectTab(kind, index));
            return tab;
        }

        void SelectTab(ShowTimeline.TabKind kind, int index)
        {
            if (_timeline == null) return;
            if (kind == ShowTimeline.TabKind.Song) { _timeline.SelectSong(index); _shortView = false; }
            else { _timeline.SelectShort(index); _shortView = true; }
            RebuildTimelineTabs();
            RefreshShortAssignment();
            if (_tlTotal != null) _tlTotal.text = "/ " + ShowTimeline.FormatTime(_timeline.Length);
        }

        // song/short の中央ビューを _shortView に合わせる（Time 表示は共通・GATE 表記は廃止＝07-10）。
        void ApplyTimelineView()
        {
            if (_tlSong != null)  _tlSong.style.display  = _shortView ? DisplayStyle.None : DisplayStyle.Flex;
            if (_tlShort != null) _tlShort.style.display = _shortView ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // 追加メニューは _root 直下のオーバーレイに出す（タイムライン本体の裏に隠れる/クリップされるのを防ぐ）。
        void ToggleAddMenu()
        {
            if (_tlAddMenu == null) return;
            bool show = _tlAddMenu.style.display == DisplayStyle.None;
            if (!show) { HideAddMenu(); return; }

            if (_root != null && _tlAddMenu.parent != _root) _root.Add(_tlAddMenu);
            if (_tlAddButton != null && _root != null)
            {
                // worldBound はパネル空間の座標。_root の局所座標系へ変換してから配置する
                // （そのまま left/top へ入れると _root にオフセットがある場合にズレて画面外/
                // 他パネルの裏に出て「見えない」原因になる）。
                var b = _tlAddButton.worldBound;
                var topLeft = _root.WorldToLocal(new Vector2(b.xMin, b.yMax + 2f));
                _tlAddMenu.style.position = Position.Absolute;
                _tlAddMenu.style.left = topLeft.x;
                _tlAddMenu.style.top = topLeft.y;
            }
            _tlAddMenu.style.display = DisplayStyle.Flex;
            _tlAddMenu.BringToFront();
        }
        void HideAddMenu() { if (_tlAddMenu != null) _tlAddMenu.style.display = DisplayStyle.None; }

        // Short のホールド状態を UI に反映（発火は割当パッド/キー経由。ここは表示だけ）。
        // held 中はレーンのクリップを Live Amber 点灯＋全幅プレビュー（§7 #8・07-09）。
        void RefreshShortHeld()
        {
            bool held = _timeline != null && _timeline.AnyShortHeld;
            if (_shortClip != null)
            {
                EnableClass(_shortClip, "rr-short-clip--held", held);
                _shortClip.style.width = Length.Percent(held ? 100f : 26f);
            }
        }

        // -------------------------------------------------- timeline transport（Song 再生・#5 B案）
        // トランスポート（前/再生-停止/ループ）を ShowTimeline に接続し、playhead と時間表示を毎フレーム追従。
        void BuildTimelineTransport()
        {
            // シーン配置の ShowTimeline を使う（未配線でも拾う）。実行時 AddComponent は
            // Inspector 構成が保存されないため廃止（#27）。見つからなければトランスポートは無効。
            if (_timeline == null) _timeline = FindFirstObjectByType<ShowTimeline>();
            if (_timeline == null)
                Debug.LogWarning("[OperatorUI] ShowTimeline がシーンにありません。トランスポートは無効です。");

            _tlPrev  = _root.Q<Button>("rr-tl-prev");
            _tlPlay  = _root.Q<Button>("rr-tl-play");
            _tlLoop  = _root.Q<Button>("rr-tl-loop");
            _tlCur   = _root.Q<Label>("rr-tl-cur");
            _tlTotal = _root.Q<Label>("rr-tl-total");
            _remain  = _root.Q<Label>("rr-remain");
            _playhead = _root.Q<VisualElement>("rr-playhead");
            _tlPlayIcon = _tlPlay?.Q<RrIcon>();

            if (_tlPrev != null) _tlPrev.clicked += () => _timeline?.Rewind();
            if (_tlPlay != null) _tlPlay.clicked += () => _timeline?.TogglePlay();
            if (_tlLoop != null) _tlLoop.clicked += () =>
            {
                if (_timeline == null) return;
                _timeline.Loop = !_timeline.Loop;
                EnableClass(_tlLoop, "rr-transport-btn--active", _timeline.Loop);
            };

            if (_timeline != null)
            {
                _timeline.PlayStateChanged -= RefreshTransport;
                _timeline.PlayStateChanged += RefreshTransport;
                if (_tlTotal != null) _tlTotal.text = "/ " + ShowTimeline.FormatTime(_timeline.Length);
                if (_tlLoop != null)  EnableClass(_tlLoop, "rr-transport-btn--active", _timeline.Loop);
            }
            RefreshTransport();
        }

        void RefreshTransport()
        {
            bool playing = _timeline != null && _timeline.Playing;
            if (_tlPlayIcon != null) _tlPlayIcon.Icon = playing ? RrIcon.Kind.Pause : RrIcon.Kind.Play;
            if (_tlPlay != null) EnableClass(_tlPlay, "rr-transport-btn--active", playing);
        }

        // playhead と時間表示を更新（時刻が変わった時だけ整形＝停止中は毎フレーム alloc しない）。
        void UpdateTimeline()
        {
            if (_timeline == null) return;
            double t = _timeline.Time;
            if (t == _lastTimeValue) return;
            _lastTimeValue = t;

            // 再生ヘッドはクリップ・レーンの座標系で置く（本体全幅 % だとトラックヘッダ分ずれ、
            // t=0 が 0:00 目盛りに揃わない）。left = ヘッダ幅 + 正規化位置 × レーン実幅。
            if (_playhead != null && _tlSong != null)
            {
                float w = _tlSong.resolvedStyle.width;
                float usable = w - TimelineLaneLeft - TimelineLaneRight;
                if (usable > 0f)
                    _playhead.style.left = TimelineLaneLeft + _timeline.NormalizedTime * usable;
            }
            if (_tlCur != null)  _tlCur.text  = ShowTimeline.FormatTime(t);
            if (_remain != null) _remain.text = "-" + ShowTimeline.FormatTime(_timeline.Remaining);
        }

        // -------------------------------------------------- 終了 UX（§7・#12）
        // ライブ卓では誤爆リスクが最大なので常設の終了ボタンは置かず、ブランドロゴをメニュー化。
        // Quit は常に確認 modal を通す（⌘Q も Application.wantsToQuit 経由で同フロー）。
        void BuildExitUx()
        {
            _brandMenu   = _root.Q<VisualElement>("rr-brand-menu");
            _quitOverlay = _root.Q<VisualElement>("rr-quit-overlay");
            _quitOnAir   = _root.Q<VisualElement>("rr-quit-onair");
            _quitRoutes  = _root.Q<Label>("rr-quit-routes");
            _quitTitle   = _root.Q<Label>("rr-quit-title");
            _quitMsg     = _root.Q<Label>("rr-quit-msg");
            _quitCancel  = _root.Q<Button>("rr-quit-cancel");
            _quitConfirm = _root.Q<Button>("rr-quit-confirm");

            var brand = _root.Q<VisualElement>(className: "rr-brand-group");
            brand?.RegisterCallback<MouseDownEvent>(_ => ToggleBrandMenu());

            var about = _root.Q<Button>("rr-brand-about");
            var prefs = _root.Q<Button>("rr-brand-prefs");
            about?.SetEnabled(false);   // プレースホルダ（機能は将来）
            prefs?.SetEnabled(false);
            var quit = _root.Q<Button>("rr-brand-quit");
            if (quit != null) quit.clicked += () => { HideBrandMenu(); ShowQuitModal(); };

            if (_quitCancel != null) _quitCancel.clicked += HideQuitModal;
            if (_quitConfirm != null) _quitConfirm.clicked += ConfirmQuit;

            if (!_quitHooked) { Application.wantsToQuit += OnWantsToQuit; _quitHooked = true; }
        }

        void ToggleBrandMenu()
        {
            if (_brandMenu == null) return;
            bool show = _brandMenu.style.display == DisplayStyle.None;
            _brandMenu.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }
        void HideBrandMenu() { if (_brandMenu != null) _brandMenu.style.display = DisplayStyle.None; }

        void ShowQuitModal()
        {
            if (_quitOverlay == null) return;
            // 危険判定：本番 Live 中 or 出力ルート ON → ON AIR 警告＋出力停止して終了。
            bool live = _appMode != null && _appMode.IsLive;
            bool routing = _output != null && _output.AnyEnabled;
            bool danger = live || routing;

            if (_quitOnAir != null) _quitOnAir.style.display = danger ? DisplayStyle.Flex : DisplayStyle.None;
            if (danger && _quitRoutes != null)
                _quitRoutes.text = _output != null ? _output.ActiveRoutesSummary() : "";

            if (_quitMsg != null)
                _quitMsg.text = danger ? "出力を停止してから終了します。" : "アプリを終了します。";

            if (_quitConfirm != null)
            {
                _quitConfirm.text = danger ? "Stop Output & Quit" : "Quit";
                EnableClass(_quitConfirm, "rr-modal-btn--danger", danger);
                EnableClass(_quitConfirm, "rr-modal-btn--primary", !danger);
            }
            _quitOverlay.style.display = DisplayStyle.Flex;
        }

        void HideQuitModal() { if (_quitOverlay != null) _quitOverlay.style.display = DisplayStyle.None; }

        void ConfirmQuit()
        {
            if (_output != null) _output.DisableAll();   // 配信を止めてから
            _quitConfirmed = true;
            HideQuitModal();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // ⌘Q / Application.Quit を横取り。確認前は false を返して終了を止め、modal を出す。
        // overlay を出せない状況（未構築等）では終了をブロックしない（安全側）。
        bool OnWantsToQuit()
        {
            if (_quitConfirmed) return true;
#if UNITY_EDITOR
            // Editor では Play 終了時に呼ばれ、false は無視されて警告になるだけなので横取りしない。
            // 終了確認の見た目はブランドメニューの Quit から ShowQuitModal で確認できる。
            return true;
#else
            if (_quitOverlay == null) return true;
            ShowQuitModal();
            return false;
#endif
        }

        // -------------------------------------------------- 汎用セレクション（§3・#15・最初のスライス）
        // 左ドック PERFORM ライブラリ（Sources/Audio/Scenes）を選択可能化し、単一 SelectionModel で駆動。
        // 選択→行ハイライト＋ Inspector タイトル反映。per-kind Inspector 本体・track/WARP は後続スライス。
        void BuildDockSelection()
        {
            _dockItems.Clear();
            var perform = _root.Q<VisualElement>("rr-dock-perform");
            if (perform != null)
            {
                perform.Query<Foldout>().ForEach(f =>
                {
                    SelectionKind kind = f.text switch
                    {
                        "Sources" => SelectionKind.SourceVideo,   // 動画/カメラ細分は後続スライス
                        "Audio"   => SelectionKind.AudioInput,
                        "Scenes"  => SelectionKind.Scene,
                        _         => SelectionKind.None,
                    };
                    if (kind != SelectionKind.None) WireDockList(f, kind);
                });
            }

            _selection.Changed -= OnSelectionChanged;
            _selection.Changed += OnSelectionChanged;
        }

        void WireDockList(VisualElement container, SelectionKind kind)
        {
            container.Query<VisualElement>(className: "rr-list-item").ForEach(item =>
            {
                var lbl = item.Q<Label>(className: "rr-list-label");
                string id = lbl != null ? lbl.text : "";
                _dockItems.Add((item, kind, id));
                item.RegisterCallback<MouseDownEvent>(_ => _selection.Select(kind, id));
            });
        }

        void OnSelectionChanged(SelectionRef sel)
        {
            for (int i = 0; i < _dockItems.Count; i++)
            {
                var d = _dockItems[i];
                EnableClass(d.item, "rr-list-item--active", sel.SameItem(d.kind, d.id));
            }
            RebuildInspector();   // 選択に応じて Inspector を出し分け（無選択＝FX/Program）
        }

        // ドック項目（track 以外の単一選択）が Inspector を占有するか。
        bool DockSelectionActive()
        {
            var k = _selection.Current.Kind;
            return k != SelectionKind.None && k != SelectionKind.Track;
        }

        // ドック選択の簡易 Inspector（per-kind の本体は §4 後続スライス）。
        void BuildDockInspector(SelectionRef sel)
        {
            if (_inspector == null) return;
            _inspector.Clear();
            _paramRows.Clear();
            _inspectorEffect = -1;
            if (_inspectorTitle != null) _inspectorTitle.text = sel.Id;

            var kindLabel = new Label(KindLabel(sel.Kind));
            kindLabel.AddToClassList("rr-hint");
            _inspector.Add(kindLabel);
            var todo = new Label("Inspector controls: §4 後続スライスで実装");
            todo.AddToClassList("rr-hint");
            _inspector.Add(todo);
        }

        static string KindLabel(SelectionKind k) => k switch
        {
            SelectionKind.SourceVideo  => "Source · Video",
            SelectionKind.SourceCamera => "Source · Camera",
            SelectionKind.SourceExt    => "Source · External",
            SelectionKind.Fx           => "FX",
            SelectionKind.AudioInput   => "Audio Input",
            SelectionKind.Mapping      => "Mapping",
            SelectionKind.Scene        => "Scene",
            SelectionKind.Surface      => "Surface",
            _                          => "Item",
        };

        // Inspector タイトル：ドック選択があればその名前、無ければ従来の FX/Program 表示。
        void RefreshInspectorTitle()
        {
            if (_inspectorTitle == null) return;
            var sel = _selection.Current;
            if (!sel.IsNone && sel.Kind != SelectionKind.Track && !string.IsNullOrEmpty(sel.Id))
            {
                _inspectorTitle.text = sel.Id;
                return;
            }
            var fx = _hub != null ? _hub.GetEffect(_hub.SelectedEffect) : null;
            _inspectorTitle.text = fx != null ? fx.Name : "Inspector";
        }

        void OnDisable()
        {
            if (_quitHooked) { Application.wantsToQuit -= OnWantsToQuit; _quitHooked = false; }
            _selection.Changed -= OnSelectionChanged;
            if (_appMode != null) _appMode.ModeChanged -= OnModeChanged;
            if (_timeline != null)
            {
                _timeline.PlayStateChanged -= RefreshTransport;
                _timeline.ShortStateChanged -= RefreshShortHeld;
            }
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
            UpdateTimeline();   // playhead / 時間表示（ControlHub 非依存）

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
            _smoothedDt = Mathf.Lerp(_smoothedDt, Time.unscaledDeltaTime, 0.1f);
            float fps = _smoothedDt > 0f ? 1f / _smoothedDt : 0f;

            if (_fps != null)
            {
                int shown = Mathf.RoundToInt(fps);
                if (shown != _lastFpsShown) { _fps.text = shown + " fps"; _lastFpsShown = shown; }
                EnableClass(_fps, "rr-fps--warn", fps < 58f);
            }

            // 2 段目「X.X ms · gc N」：フレーム時間（0.1ms 刻み）＋ GC 収集回数（gen0・スパイク監視）。
            // どちらか変わった時だけ整形（毎フレーム alloc を避ける）。
            if (_perf2 != null)
            {
                int ms10 = Mathf.RoundToInt(_smoothedDt * 10000f);
                int gc = System.GC.CollectionCount(0);
                if (ms10 != _lastFrameMsShown || gc != _lastGcShown)
                {
                    _perf2.text = (ms10 / 10f).ToString("0.0") + " ms · gc " + gc;
                    _lastFrameMsShown = ms10;
                    _lastGcShown = gc;
                }
                EnableClass(_perf2, "rr-fps--warn", _smoothedDt > 1f / 58f);
            }
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
            // ドック項目が選択中ならその Inspector を優先（§3・FX/Program より上位）。
            if (DockSelectionActive()) { BuildDockInspector(_selection.Current); return; }

            _inspector.Clear();
            _paramRows.Clear();
            if (_hub == null) { if (_inspectorTitle != null) _inspectorTitle.text = "Inspector"; return; }

            var fx = _hub.GetEffect(_hub.SelectedEffect);
            _inspectorEffect = _hub.SelectedEffect;
            RefreshInspectorTitle();   // ドック選択があればそれを優先（§3）
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
