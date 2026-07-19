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
        VisualElement _root;         // UIDocument.rootVisualElement（.rr-theme の外側＝カスタムプロパティ未継承）
        VisualElement _themeRoot;    // "rr-root"（.rr-theme が付く要素）。popover の reparent 先はこちら
        Image _preview;
        Label _fps;
        Label _perf2;           // 上部バー右・2 段目「X.X ms · gc N」（フレーム時間＋GC 収集回数）
        int _lastGcShown = -1;
        int _lastFrameMsShown = -1;

        // 汎用セレクションモデル（§3・#15）
        readonly SelectionModel _selection = new SelectionModel();
        readonly List<(VisualElement item, SelectionKind kind, string id, string meta)> _dockItems
            = new List<(VisualElement, SelectionKind, string, string)>();

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

        // 上バー OUTPUT 状態チップ（Full/Syphon/NDI）。Edit 中は即トグル、Live 中は誤配信防止の
        // 確認 popover を経由する（§2・U5）。
        Button _chipFs, _chipSyphon, _chipNdi;
        VisualElement _routeConfirmPopover;
        int _routeConfirmRoute = -1;   // 確認 popover が対象にしている route（-1=非表示）
        bool _routeConfirmHooked;      // 外側クリックで閉じるグローバルハンドラの二重登録防止

        // WARP 編集オーバーレイ（#21・メッシュ制御点ドラッグ）
        WarpCanvas _warpCanvas;
        Button _warpReset, _warpEditModeBtn;
        Button _warpGridBtn, _warpTestBtn;   // GRID=格子オーバーレイ / TEST・CALIB=テストパターン校正（#34/#35）
        VisualElement _warpMeshGroup;        // Grid X/Y 解像度ステッパー（§7b-A Mesh Warping）
        Label _warpXVal, _warpYVal;
        Button _warpBezierBtn;               // Bezier ⇄ Linear 補間トグル（§7b-A）
        bool _warpEditing;
        bool _warpOutputMode;   // true=OUTPUT(出力変形) を編集 / false=EMBED(埋め込み)
        bool _warpContentMode;  // true=CONTENT(枠内映像 pan) / false=SHAPE(窓の形)
        bool _warpShowGrid;     // 細分化格子オーバーレイの表示状態
        readonly List<VisualElement> _cornerPins = new List<VisualElement>();  // 装飾ピン（編集中は隠す）
        IWarpTarget _warpTarget;   // 現在の warp 編集対象（選択 surface or Compositor）

        // WARP エディタ 2 ペイン化（U6・MAPPING 中央）: EMBED=Input/Output 分割・OUTPUT=単一ペイン
        // （既存の rr-viewport＋_warpCanvas をそのまま流用）。
        enum WarpView { Input, Split, Output }
        WarpView _warpView = WarpView.Split;
        VisualElement _mapToolbar, _centerSplit, _mapPaneIn, _mapPaneOut;
        Image _mapPreviewIn, _mapPreviewOut;
        Button _warpTargetEmbedBtn, _warpTargetOutputBtn;
        VisualElement _warpViewsGroup;
        Button _warpViewInputBtn, _warpViewSplitBtn, _warpViewOutputBtn;
        Label _warpWysiwygLabel;
        WarpCanvas _warpCanvasIn, _warpCanvasOut;   // EMBED 分割ペイン用（_warpCanvas は OUTPUT 単一ペイン用）

        // タイムライン sequence/short/song タブ（07b §3.5.2 / §7c・#27 足場＋#29 U11＝Sequence/Song 3種）
        VisualElement _tlTablist, _tlSeq, _tlShort, _tlSongList, _shortClip, _tlAddMenu;
        Button _tlAddButton;        // タブバーの + （追加メニューのアンカー）

        // Sequence track 行（U3・動的生成・旧称 Song）
        VisualElement _tlTracklist;
        VisualElement _addTrackWrap, _addTrackMenu, _addTrackDivider;
        Button _addTrackBtn;
        readonly List<(VisualElement head, int index)> _trackHeads = new List<(VisualElement, int)>();
        static readonly (string file, string kind, string dur)[] AddTrackFileLib =
        {
            ("reality_base.mov", "video", "03:20"),
            ("loop_grid.mp4",    "video", "00:40"),
            ("overlay_pack.mov", "video", "01:12"),
            ("master_mix.wav",   "audio", "03:20"),
            ("sfx_hits.wav",     "audio", "00:08"),
        };
        // TrackInspector の表示専用パラメータ（バックエンド API 無し・当面値保持のみ・source-camera と同じ扱い）
        float _trackVolume = 0.82f;
        bool _trackFade = true;
        ShowTimeline.TabKind _viewKind = ShowTimeline.TabKind.Sequence;   // いま中央に表示中のタブ種別（§7c・U11）
        Button _shortPadBtn;              // KEY 行の割当ボタン（⌨ アイコン＋キー文字＋⌄・マトリクスのトグル）
        Label _shortPadGlyph;             // 割当ボタン内のキー文字ラベル（毎フレーム再構築しない・U7）
        VisualElement _padMatrix;         // 4×4 パッド割当マトリクス（§7・#13）
        readonly Button[] _padCells = new Button[16];
        Toggle _holdLoopToggle;           // per-Short の Hold-Loop
        // Short 側 +Track（U7・表示レーン追加のみ＝バックエンド無し）＋ ruler/playhead
        VisualElement _shortAddTrackWrap, _shortAddTrackMenu, _shortLanes, _shortBody, _shortPlayhead;
        Button _shortAddTrackBtn;

        // Song（Sequence セットリスト）＝MPC 流 横ストリップ：集計ヘッダー＋左固定 Add Sequence レール＋
        // 横スクロールのステップカード列（§7c・07-18・#29 U12。旧2ペインを全面刷新）。
        VisualElement _songStrip, _songRailList;
        Label _songHeadSummary;
        Button _songHeadJump;
        int _songSelStep;   // 選択中のステップ（ヘッダーのジャンプ対象・カード選択枠）
        readonly List<VisualElement> _songCards = new List<VisualElement>();   // index=step・再生中ハイライト用（#27b）
        int _lastPlayingSongStep = -2;   // 直前フレームの CurrentSongStep（-2=未初期化・変化検知用）

        // PERFORM 左ドック Banks（保存済み Sequence/Short/Song 一覧・U10）
        VisualElement _banksList;

        // タイムライン再生（Song トランスポート・playhead・時間表示）
        Button _tlPrev, _tlPlay, _tlLoop;
        Label _tlCur, _tlTotal, _remain;
        VisualElement _playhead;
        RrIcon _tlPlayIcon;   // 再生/一時停止アイコン切替
        double _lastTimeValue = -1.0;   // 時刻が変わった時だけ整形（停止中は毎フレーム alloc しない）
        // 再生ヘッドをクリップ・レーンに合わせる際の左右オフセット（USS .rr-track-head / .rr-track-tail 幅）。
        const float TimelineLaneLeft = 96f;
        const float TimelineLaneRight = 74f;

        // Surface 一覧（左ドック・#22）＋モード切替。プロパティ本体は右 Inspector（BuildSurfaceInspector・U4）。
        VisualElement _surfaceList;
        Label _surfaceEmpty;
        Button _surfaceAdd;
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
            public RrIcon eyeIcon, lockIcon;   // trailing eye/lock ボタン（U4・罠2＝クリックは選択に伝播させない）
            // 毎フレームの文字列生成を避けるための直近値キャッシュ（変化時のみ .text/アイコン更新）
            public int lastId = int.MinValue, lastCols = -1, lastRows = -1; public string lastName;
            public bool lastEnabled = true, lastLocked = false;
            public string idStr;   // s.Id.ToString() キャッシュ（選択比較の毎フレーム alloc を避ける・罠7）
        }

        // FX 行・パラメータ行のバインド保持（再構築判定/毎フレーム同期用）
        readonly List<FxRow> _fxRows = new List<FxRow>();
        readonly List<ParamRow> _paramRows = new List<ParamRow>();
        int _builtEffectCount = -1;
        int _inspectorEffect = -1;
        float _smoothedDt;
        int _lastFpsShown = -1;

        // per-kind Inspector（U2）: ソース参照はキャッシュ（FindFirstObjectByType の毎回呼び出しを避ける）
        SourceVideo _sourceVideo;
        SourceCamera _sourceCamera;
        AudioAnalyzer _audioAnalyzer;
        // カメラ/オーディオの表示専用パラメータ（バックエンド API 無し・当面値保持のみ）
        float _camExposure = 0.6f, _camZoom = 1f; bool _camEmbed = true;
        float _audioSensitivity = 0.72f;
        // 毎フレーム更新するライブ表示（値変化時のみ更新・罠7）
        VisualElement _meterFillRms, _meterFillLow, _meterFillMid, _meterFillHigh;
        Label _rmsValueLabel; int _lastRmsCenti = int.MinValue;
        Label _srcVideoTimeLabel; double _lastSrcVideoTime = -1d;

        sealed class FxRow { public VisualElement root; public Toggle toggle; public Label name; }
        sealed class ParamRow { public VisualElement root; public Slider slider; public Label value; public EffectParameter param; public int paramIndex; public int lastCenti = int.MinValue; }

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

            // popover の reparent 先は _root（UIDocument.rootVisualElement）ではなく _themeRoot
            // （.rr-theme が付いた "rr-root"）にする。--rr-* はカスタムプロパティで .rr-theme の
            // 子孫にしか継承されないため、_root 直下へ逃がすと var(--rr-*) が解決できず
            // 背景/枠線が消え、下の要素と文字が透けて重なって見える（実機で発覚・修正済み）。
            _themeRoot = _root.Q<VisualElement>("rr-root") ?? _root;

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
            RebuildInspector();   // 初期表示（無選択＝Master/Program・U1）。以後は選択変化/FX クリックで駆動。
            ApplyVisibility();
        }

        // -------------------------------------------------- output routes (top bar)
        // ルート番号: 0=Fullscreen, 1=Syphon, 2=NDI
        static readonly string[] RouteLabel = { "Full", "Syphon", "NDI" };

        void BuildOutputControls()
        {
            _chipFs = _root.Q<Button>("rr-out-chip-fs");
            _chipSyphon = _root.Q<Button>("rr-out-chip-syphon");
            _chipNdi = _root.Q<Button>("rr-out-chip-ndi");
            _routeConfirmPopover = _root.Q<VisualElement>("rr-route-confirm");

            if (_chipFs != null)     _chipFs.clicked     += () => OnRouteChipClicked(0, _chipFs);
            if (_chipSyphon != null) _chipSyphon.clicked += () => OnRouteChipClicked(1, _chipSyphon);
            if (_chipNdi != null)    _chipNdi.clicked    += () => OnRouteChipClicked(2, _chipNdi);

            if (!_routeConfirmHooked && _root != null)
            {
                // 外側クリックで閉じる（capture フェーズ＝チップ自身のクリックより先に判定できる）。
                _root.RegisterCallback<PointerDownEvent>(OnRootPointerDownForRouteConfirm, TrickleDown.TrickleDown);
                _routeConfirmHooked = true;
            }

            RefreshOutputStatus();
        }

        bool RouteAvailable(int route) => _output != null &&
            (route == 0 ? _output.HasFullscreen : route == 1 ? _output.HasSyphon : _output.HasNdi);

        bool RouteEnabled(int route) => _output != null &&
            (route == 0 ? _output.FullscreenEnabled : route == 1 ? _output.SyphonEnabled : _output.NdiEnabled);

        // 準備 Edit 中は即トグル（従来どおり）。本番 Live 中は誤配信防止のため確認 popover を経由する（§2・U5）。
        void OnRouteChipClicked(int route, Button anchor)
        {
            if (!RouteAvailable(route)) return;
            bool live = _appMode != null && _appMode.IsLive;
            if (!live) { SetRoute(route, !RouteEnabled(route)); return; }
            ShowRouteConfirm(route, anchor);
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
            ApplyRoute(_chipFs, 0);
            ApplyRoute(_chipSyphon, 1);
            ApplyRoute(_chipNdi, 2);
        }

        void ApplyRoute(Button chip, int route)
        {
            if (chip == null) return;
            bool available = RouteAvailable(route);
            bool on = available && RouteEnabled(route);
            chip.SetEnabled(available);
            EnableClass(chip, "rr-out-chip--on", on);
            EnableClass(chip, "rr-out-chip--off", !on);
        }

        // 確認 popover（_root 直下へ reparent＋WorldToLocal 配置＝罠3/4）。ON にする側の確定は
        // primary、OFF にする側は secondary（MakeButton の既存 variant を流用）。modal ではないので
        // 外側クリックで閉じる（OnRootPointerDownForRouteConfirm）。
        void ShowRouteConfirm(int route, Button anchor)
        {
            if (_routeConfirmPopover == null) { SetRoute(route, !RouteEnabled(route)); return; }   // popover 未配線なら従来通り即時
            _routeConfirmRoute = route;
            bool turningOn = !RouteEnabled(route);

            _routeConfirmPopover.Clear();
            var row = new VisualElement(); row.AddToClassList("rr-route-confirm-row");
            var dot = new VisualElement(); dot.AddToClassList("rr-route-confirm-dot");
            dot.AddToClassList(turningOn ? "rr-route-confirm-dot--on" : "rr-route-confirm-dot--off");
            var label = new Label(RouteLabel[route] + " → " + (turningOn ? "ON" : "OFF"));
            label.AddToClassList("rr-route-confirm-text"); label.AddToClassList("rr-mono");
            row.Add(dot); row.Add(label);
            _routeConfirmPopover.Add(row);

            var btnRow = new VisualElement(); btnRow.AddToClassList("rr-btn-row");
            btnRow.Add(MakeButton("Cancel", "ghost", HideRouteConfirm));
            btnRow.Add(MakeButton(turningOn ? "Turn On" : "Turn Off", turningOn ? "primary" : "secondary", () =>
            {
                SetRoute(route, turningOn);
                HideRouteConfirm();
            }));
            _routeConfirmPopover.Add(btnRow);

            if (_themeRoot != null && _routeConfirmPopover.parent != _themeRoot) _themeRoot.Add(_routeConfirmPopover);
            if (anchor != null && _themeRoot != null)
            {
                var b = anchor.worldBound;
                var topLeft = _themeRoot.WorldToLocal(new Vector2(b.xMin, b.yMax + 4f));
                _routeConfirmPopover.style.position = Position.Absolute;
                _routeConfirmPopover.style.left = topLeft.x;
                _routeConfirmPopover.style.top = topLeft.y;
            }
            _routeConfirmPopover.style.display = DisplayStyle.Flex;
            _routeConfirmPopover.BringToFront();
        }

        void HideRouteConfirm()
        {
            if (_routeConfirmPopover != null) _routeConfirmPopover.style.display = DisplayStyle.None;
            _routeConfirmRoute = -1;
        }

        void OnRootPointerDownForRouteConfirm(PointerDownEvent evt)
        {
            if (_routeConfirmRoute < 0 || _routeConfirmPopover == null) return;
            if (_routeConfirmPopover.style.display == DisplayStyle.None) return;
            if (IsDescendantOf(evt.target as VisualElement, _routeConfirmPopover)) return;   // ポップオーバー内クリックは無視
            HideRouteConfirm();
        }

        static bool IsDescendantOf(VisualElement el, VisualElement ancestor)
        {
            while (el != null)
            {
                if (el == ancestor) return true;
                el = el.parent;
            }
            return false;
        }

        // -------------------------------------------------- warp editor (#21)
        // ビューポート上に WarpCanvas を重ね、WARP トグルで編集モードに入る（既定はプレビュー）。
        void BuildWarpEditor()
        {
            var viewport = _root.Q<VisualElement>("rr-viewport");
            _warpReset = _root.Q<Button>("rr-warp-reset");
            _warpEditModeBtn = _root.Q<Button>("rr-warp-editmode");
            _warpGridBtn = _root.Q<Button>("rr-warp-grid");
            _warpTestBtn = _root.Q<Button>("rr-warp-test");
            _mapToolbar = _root.Q<VisualElement>("rr-map-toolbar");
            _centerSplit = _root.Q<VisualElement>("rr-center-split");
            _mapPaneIn = _root.Q<VisualElement>("rr-map-pane-in");
            _mapPaneOut = _root.Q<VisualElement>("rr-map-pane-out");
            _mapPreviewIn = _root.Q<Image>("rr-map-preview-in");
            _mapPreviewOut = _root.Q<Image>("rr-map-preview-out");
            _warpTargetEmbedBtn = _root.Q<Button>("rr-warp-target-embed");
            _warpTargetOutputBtn = _root.Q<Button>("rr-warp-target-output");
            _warpViewsGroup = _root.Q<VisualElement>("rr-warp-views");
            _warpViewInputBtn = _root.Q<Button>("rr-warp-view-input");
            _warpViewSplitBtn = _root.Q<Button>("rr-warp-view-split");
            _warpViewOutputBtn = _root.Q<Button>("rr-warp-view-output");
            _warpWysiwygLabel = _root.Q<Label>("rr-warp-wysiwyg");
            if (viewport == null) return;

            if (_warpCanvas == null)
            {
                _warpCanvas = new WarpCanvas { name = "rr-warp-canvas" };
                viewport.Add(_warpCanvas);            // 最前面（ピン等より上）に重ねる（OUTPUT 単一ペイン用）
            }
            if (_warpCanvasIn == null && _mapPaneIn != null)
            {
                _warpCanvasIn = new WarpCanvas { name = "rr-warp-canvas-in" };
                _mapPaneIn.Add(_warpCanvasIn);
            }
            if (_warpCanvasOut == null && _mapPaneOut != null)
            {
                _warpCanvasOut = new WarpCanvas { name = "rr-warp-canvas-out" };
                _mapPaneOut.Add(_warpCanvasOut);
            }
            // EMBED の Input/Output ペインは同じ IWarpTarget を共有する。片方をドラッグしたらもう片方も
            // 再描画する（WarpCanvas.Changed・U6）。
            if (_warpCanvas != null) _warpCanvas.Changed = () => { _warpCanvasIn?.MarkDirtyRepaint(); _warpCanvasOut?.MarkDirtyRepaint(); };
            if (_warpCanvasIn != null) _warpCanvasIn.Changed = () => { _warpCanvas?.MarkDirtyRepaint(); _warpCanvasOut?.MarkDirtyRepaint(); };
            if (_warpCanvasOut != null) _warpCanvasOut.Changed = () => { _warpCanvas?.MarkDirtyRepaint(); _warpCanvasIn?.MarkDirtyRepaint(); };

            // ペインクリック = 選択 surface の切替（pin ヒット時はドラッグ優先・選択させない）。
            WirePaneSelect(viewport, _warpCanvas);
            WirePaneSelect(_mapPaneIn, _warpCanvasIn);
            WirePaneSelect(_mapPaneOut, _warpCanvasOut);

            // 装飾のコーナーピン（静的・非ドラッグ）は編集中に隠す。実ハンドルと紛らわしいため。
            _cornerPins.Clear();
            viewport.Query<VisualElement>(className: "rr-corner-pin").ForEach(e => _cornerPins.Add(e));
            // CONTENT モードのドラッグ → 選択 surface の content pan（3 ペイン共通）。
            System.Action<Vector2> contentPan = OnContentPan;
            if (_warpCanvas != null) _warpCanvas.ContentPan = contentPan;
            if (_warpCanvasIn != null) _warpCanvasIn.ContentPan = contentPan;
            if (_warpCanvasOut != null) _warpCanvasOut.ContentPan = contentPan;

            SetWarpTarget(ResolveWarpTarget());       // 選択 surface or Compositor
            _warpEditing = false;

            if (_warpReset != null)  _warpReset.clicked  += () => { _warpTarget?.ResetWarp(); RepaintWarpCanvases(); };
            BuildWarpTargetSeg();
            if (_warpTargetEmbedBtn != null) _warpTargetEmbedBtn.clicked += () => SetWarpMode(false);
            if (_warpTargetOutputBtn != null) _warpTargetOutputBtn.clicked += () => SetWarpMode(true);
            if (_warpViewInputBtn != null) _warpViewInputBtn.clicked += () => SetWarpView(WarpView.Input);
            if (_warpViewSplitBtn != null) _warpViewSplitBtn.clicked += () => SetWarpView(WarpView.Split);
            if (_warpViewOutputBtn != null) _warpViewOutputBtn.clicked += () => SetWarpView(WarpView.Output);
            if (_warpEditModeBtn != null) _warpEditModeBtn.clicked += ToggleWarpEditMode;
            if (_warpGridBtn != null) _warpGridBtn.clicked += ToggleWarpGrid;
            if (_warpTestBtn != null) _warpTestBtn.clicked += ToggleWarpTest;

            ApplyMappingLayout();

            // Grid 解像度ステッパー（§7b-A・Mesh Warping）: 2..8 で全 warp ターゲット共通に再生成。
            _warpMeshGroup = _root.Q<VisualElement>("rr-warp-mesh");
            _warpXVal = _root.Q<Label>("rr-warp-x-val");
            _warpYVal = _root.Q<Label>("rr-warp-y-val");
            WireStep("rr-warp-x-dec", -1, true);
            WireStep("rr-warp-x-inc", +1, true);
            WireStep("rr-warp-y-dec", -1, false);
            WireStep("rr-warp-y-inc", +1, false);
            _warpBezierBtn = _root.Q<Button>("rr-warp-bezier");
            if (_warpBezierBtn == null)
                Debug.LogWarning("[OperatorUI] BEZIER ボタンが見つかりません。OperatorShell.uxml が古いままです" +
                                 "（Assets/UI を右クリック → Reimport してください）。");
            if (_warpBezierBtn != null) _warpBezierBtn.clicked += () =>
            {
                if (_warpTarget == null) return;
                _warpTarget.BezierInterp = !_warpTarget.BezierInterp;
                RepaintWarpCanvases();
                RefreshWarpMesh();
            };

            RefreshWarpTargetSeg();
            RefreshWarpViewSeg();
            RefreshWarpEditModeBtn();
            RefreshWarpGridBtn();
            RefreshWarpTestBtn();
            RefreshWarpMesh();
        }

        void RepaintWarpCanvases()
        {
            _warpCanvas?.MarkDirtyRepaint();
            _warpCanvasIn?.MarkDirtyRepaint();
            _warpCanvasOut?.MarkDirtyRepaint();
        }

        // ペインクリック = Surface 選択（SelectionModel 経由）。pin ヒット時はドラッグを優先し選択させない。
        void WirePaneSelect(VisualElement pane, WarpCanvas canvas)
        {
            if (pane == null) return;
            pane.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (canvas != null && canvas.HitsHandle(evt.localPosition)) return;
                if (_warpTarget is Surface s) _selection.Select(SelectionKind.Surface, s.Id.ToString());
            }, TrickleDown.TrickleDown);
        }

        // EMBED/OUTPUT セグメントボタンの中身（色ドット＋ラベル）を一度だけ組み立てる（毎フレームではない）。
        VisualElement _warpTargetEmbedDot, _warpTargetOutputDot;
        void BuildWarpTargetSeg()
        {
            if (_warpTargetEmbedBtn != null)
            {
                _warpTargetEmbedBtn.Clear();
                _warpTargetEmbedDot = new VisualElement(); _warpTargetEmbedDot.AddToClassList("rr-list-dot"); _warpTargetEmbedDot.AddToClassList("rr-seg-dot");
                var lbl = new Label("EMBED"); lbl.AddToClassList("rr-mono");
                _warpTargetEmbedBtn.Add(_warpTargetEmbedDot); _warpTargetEmbedBtn.Add(lbl);
            }
            if (_warpTargetOutputBtn != null)
            {
                _warpTargetOutputBtn.Clear();
                _warpTargetOutputDot = new VisualElement(); _warpTargetOutputDot.AddToClassList("rr-list-dot"); _warpTargetOutputDot.AddToClassList("rr-seg-dot");
                var lbl = new Label("OUTPUT"); lbl.AddToClassList("rr-mono");
                _warpTargetOutputBtn.Add(_warpTargetOutputDot); _warpTargetOutputBtn.Add(lbl);
            }
        }

        void WireStep(string name, int delta, bool isX)
        {
            var b = _root.Q<Button>(name);
            if (b != null) b.clicked += () => ChangeGridResolution(delta, isX);
        }

        // Grid X/Y 解像度を 2..8 で変更（§7b-A）。全 warp ターゲット共通（IWarpTarget.SetGridResolution）。
        void ChangeGridResolution(int delta, bool isX)
        {
            if (_warpTarget == null) return;
            _warpTarget.EnsureWarpPoints();
            int cols = _warpTarget.WarpCols, rows = _warpTarget.WarpRows;
            if (isX) cols = Mathf.Clamp(cols + delta, 2, 8);
            else     rows = Mathf.Clamp(rows + delta, 2, 8);
            _warpTarget.SetGridResolution(cols, rows);
            RepaintWarpCanvases();
            RefreshWarpMesh();
        }

        void RefreshWarpMesh()
        {
            if (_warpTarget == null) return;
            _warpTarget.EnsureWarpPoints();
            if (_warpXVal != null) _warpXVal.text = _warpTarget.WarpCols.ToString();
            if (_warpYVal != null) _warpYVal.text = _warpTarget.WarpRows.ToString();
            if (_warpBezierBtn != null)
                EnableClass(_warpBezierBtn, "rr-warp-step__bezier--on", _warpTarget.BezierInterp);
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
            var mode = _warpContentMode ? WarpCanvas.EditMode.Content : WarpCanvas.EditMode.Shape;
            _warpCanvas?.SetEditMode(mode);
            _warpCanvasIn?.SetEditMode(mode);
            _warpCanvasOut?.SetEditMode(mode);
            RefreshWarpEditModeBtn();
        }

        void RefreshWarpEditModeBtn()
        {
            if (_warpEditModeBtn == null) return;
            _warpEditModeBtn.text = _warpContentMode ? "CONTENT" : "SHAPE";
            EnableClass(_warpEditModeBtn, "rr-warp-toggle--content", _warpContentMode);
        }

        /// <summary>EMBED（埋め込み）⇄ OUTPUT（出力変形）を切替。OUTPUT では OutputWarp を有効化する。</summary>
        /// <summary>EMBED（埋め込み）⇄ OUTPUT（出力変形）を切替。OUTPUT では OutputWarp を有効化する。
        /// U6 で単一トグルから EMBED/OUTPUT 2 ボタンのセグメントへ（呼び出し側で対象を明示）。</summary>
        void SetWarpMode(bool output)
        {
            if (_warpOutputMode == output) return;
            if (output && _outputWarp == null) return;
            _warpOutputMode = output;
            if (_warpOutputMode)
            {
                _outputWarp.SetEnabled(true);   // 見たまま反映されるよう有効化
                if (!_warpShowGrid) ToggleWarpGrid();   // OUTPUT 編集は格子オーバーレイ常時表示（#35）
            }
            SetWarpTarget(ResolveWarpTarget());
            RefreshWarpTargetSeg();
            RefreshWarpTestBtn();   // TEST⇄CALIB のラベル/状態はモード依存
            ApplyMappingLayout();   // rr-viewport（OUTPUT 単一）⇄ rr-center-split（EMBED 分割）の出し分け
        }

        // Views（INPUT|SPLIT|OUTPUT・EMBED 編集時のみ）。片側最大化は flex-grow 切替（display は消さない＝
        // WarpCanvas の状態を保ったまま・§7b Views）。
        void SetWarpView(WarpView v)
        {
            _warpView = v;
            RefreshWarpViewSeg();
        }

        /// <summary>細分化格子オーバーレイの表示を切替（#34/#35）。3 ペインとも同じ切替に追従させる。</summary>
        void ToggleWarpGrid()
        {
            _warpShowGrid = !_warpShowGrid;
            _warpCanvas?.SetLattice(_warpShowGrid);
            _warpCanvasIn?.SetLattice(_warpShowGrid);
            _warpCanvasOut?.SetLattice(_warpShowGrid);
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
                RebuildInspector();   // Surface 選択中なら Test Pattern トグル表示を追従（U4）
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

        // EMBED/OUTPUT セグメント（ドット色＋アクティブ状態）＋ Views セグメントの表示可否・WYSIWYG ラベル。
        void RefreshWarpTargetSeg()
        {
            bool available = _outputWarp != null;
            if (_warpTargetEmbedBtn != null) EnableClass(_warpTargetEmbedBtn, "rr-seg__btn--active", !_warpOutputMode);
            if (_warpTargetOutputBtn != null)
            {
                _warpTargetOutputBtn.SetEnabled(available);
                EnableClass(_warpTargetOutputBtn, "rr-seg__btn--active", _warpOutputMode);
            }
            if (_warpTargetEmbedDot != null)
            {
                EnableClass(_warpTargetEmbedDot, "rr-list-dot--tracking", !_warpOutputMode);
                EnableClass(_warpTargetEmbedDot, "rr-seg-dot--off", _warpOutputMode);
            }
            if (_warpTargetOutputDot != null)
            {
                EnableClass(_warpTargetOutputDot, "rr-list-dot--output", _warpOutputMode);
                EnableClass(_warpTargetOutputDot, "rr-seg-dot--off", !_warpOutputMode);
            }
            if (_warpViewsGroup != null) _warpViewsGroup.style.display = _warpOutputMode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_warpWysiwygLabel != null) _warpWysiwygLabel.style.display = _warpOutputMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Views の active 状態＋各ペインの flex-grow（片側最大化・§7b Views）。
        void RefreshWarpViewSeg()
        {
            if (_warpViewInputBtn != null)  EnableClass(_warpViewInputBtn, "rr-seg__btn--active", _warpView == WarpView.Input);
            if (_warpViewSplitBtn != null)  EnableClass(_warpViewSplitBtn, "rr-seg__btn--active", _warpView == WarpView.Split);
            if (_warpViewOutputBtn != null) EnableClass(_warpViewOutputBtn, "rr-seg__btn--active", _warpView == WarpView.Output);

            SetPaneGrow(_mapPaneIn, _warpView != WarpView.Output);
            SetPaneGrow(_mapPaneOut, _warpView != WarpView.Input);
        }

        // 片側最大化（Views）。既定の flex-shrink:0 のせいで flex-grow だけでは中身（rr-preview の
        // width:100%/height:100% な Image）が縮まないため、flex-basis を常に 0 にして
        // 「grow:1+basis:0＝伸びて埋める／grow:0+basis:0＝確実に畳む」の定番形にする。
        static void SetPaneGrow(VisualElement pane, bool visible)
        {
            if (pane == null) return;
            pane.style.flexGrow = visible ? 1 : 0;
            pane.style.flexBasis = 0f;
            pane.style.overflow = visible ? Overflow.Visible : Overflow.Hidden;
        }

        /// <summary>現在の warp 編集対象。OUTPUT モード=OutputWarp、そうでなければ選択 surface or 単一 Compositor。</summary>
        IWarpTarget ResolveWarpTarget()
        {
            if (_warpOutputMode && _outputWarp != null) return _outputWarp;
            if (_surfaces != null && _surfaces.Count > 0 && _surfaces.Active != null)
                return _surfaces.Active;
            return _compositor;
        }

        // EMBED の Input/Output 2 ペインは同じ IWarpTarget を共有する（同じ点をドラッグで動かす・§6）。
        void SetWarpTarget(IWarpTarget t)
        {
            if (ReferenceEquals(_warpTarget, t)) return;
            _warpTarget = t;
            _warpCanvas?.Bind(t);
            _warpCanvasIn?.Bind(t);
            _warpCanvasOut?.Bind(t);
            RefreshWarpMesh();   // 新ターゲットの Grid X/Y を表示に反映（§7b-A）
        }

        // MAPPING ページの中央レイアウトを反映（旧 ApplyWarpEditing）。_warpEditing は「MAPPING ページが
        // アクティブか」＝SelectPage が唯一の書き込み元（手動 WARP トグルボタンは廃止・MAPPING=常時
        // warp 編集・U6）。OUTPUT モードは既存 rr-viewport＋_warpCanvas の単一ペインをそのまま流用、
        // EMBED モードは新設の rr-center-split（Input/Output 2 ペイン）に切替える。
        void ApplyMappingLayout()
        {
            bool embedSplit = _warpEditing && !_warpOutputMode;
            bool outputSingle = _warpEditing && _warpOutputMode;

            var viewport = _root?.Q<VisualElement>("rr-viewport");
            if (viewport != null) viewport.style.display = (!_warpEditing || outputSingle) ? DisplayStyle.Flex : DisplayStyle.None;
            if (_centerSplit != null) _centerSplit.style.display = embedSplit ? DisplayStyle.Flex : DisplayStyle.None;
            if (_mapToolbar != null) _mapToolbar.style.display = _warpEditing ? DisplayStyle.Flex : DisplayStyle.None;

            if (_warpCanvas != null)
            {
                _warpCanvas.style.display = outputSingle ? DisplayStyle.Flex : DisplayStyle.None;
                _warpCanvas.pickingMode = outputSingle ? PickingMode.Position : PickingMode.Ignore;
            }
            if (_warpCanvasIn != null)
            {
                _warpCanvasIn.style.display = embedSplit ? DisplayStyle.Flex : DisplayStyle.None;
                _warpCanvasIn.pickingMode = embedSplit ? PickingMode.Position : PickingMode.Ignore;
            }
            if (_warpCanvasOut != null)
            {
                _warpCanvasOut.style.display = embedSplit ? DisplayStyle.Flex : DisplayStyle.None;
                _warpCanvasOut.pickingMode = embedSplit ? PickingMode.Position : PickingMode.Ignore;
            }
            if (_warpEditing) RefreshWarpMesh();

            if (!_warpEditing && _warpContentMode)   // 編集終了時は SHAPE に戻す
            {
                _warpContentMode = false;
                _warpCanvas?.SetEditMode(WarpCanvas.EditMode.Shape);
                _warpCanvasIn?.SetEditMode(WarpCanvas.EditMode.Shape);
                _warpCanvasOut?.SetEditMode(WarpCanvas.EditMode.Shape);
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
                    RebuildInspector();   // Surface 選択中なら Test Pattern トグル表示を追従（U4）
                }
                RefreshWarpTestBtn();
            }

            // 装飾コーナーピンは編集中のみ隠す（プレビュー時は Claude Design 通り残す）
            for (int i = 0; i < _cornerPins.Count; i++)
                _cornerPins[i].style.display = _warpEditing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // -------------------------------------------------- surface panel (#22・一覧＝左ドック／プロパティ＝右 Inspector・U4)
        void BuildSurfacePanel()
        {
            _surfaceList = _root.Q<VisualElement>("rr-surface-list");
            _surfaceEmpty = _root.Q<Label>("rr-surface-empty");
            _surfaceAdd = _root.Q<Button>("rr-surface-add");

            if (_surfaceAdd != null) _surfaceAdd.clicked += () =>
            {
                if (_surfaces == null) return;
                var s = _surfaces.Add("Surface");
                if (s != null) { _builtSurfaceCount = -1; _selection.Select(SelectionKind.Surface, s.Id.ToString()); }
            };
        }

        /// <summary>現在の warp 対象（選択 surface / Compositor）の制御点を重心中心に拡大縮小（窓自体のスケール）。
        /// SurfaceInspector の Scale ステッパー（Mask セクション）から使う。</summary>
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

        // Grid X/Y 解像度の増減（SurfaceInspector の Mesh Warping セクション）。ラベル更新は
        // 呼び出し側が RebuildInspector() で行う（ここでは値の変更と WarpCanvas 再バインドのみ）。
        void NudgeGrid(int dCols, int dRows)
        {
            var s = _surfaces?.Active;
            if (s == null) return;
            s.SetGridResolution(s.WarpCols + dCols, s.WarpRows + dRows);
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
                SetWarpTarget(ResolveWarpTarget());   // 選択が変われば WARP 対象も切替
                RefreshWarpTestBtn();                 // TEST の ON 表示は選択 surface の content 依存
            }
            SyncSurfaceRows();
        }

        // Surface 一覧行（左ドック）。trailing の eye/lock は選択に伝播させず単独でトグルする（罠2）。
        // 行クリックは SelectionModel へ統合（Goal2・U4）＝ドック項目/track と排他になる。
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

                    var row = new VisualElement(); row.AddToClassList("rr-list-item");
                    var dot = new VisualElement(); dot.AddToClassList("rr-list-dot"); dot.AddToClassList("rr-list-dot--tracking");
                    var name = new Label(); name.AddToClassList("rr-list-label");
                    var meta = new Label(); meta.AddToClassList("rr-list-meta"); meta.AddToClassList("rr-mono");
                    row.Add(dot); row.Add(name); row.Add(meta);

                    string id = s.Id.ToString();
                    row.RegisterCallback<MouseDownEvent>(_ => _selection.Select(SelectionKind.Surface, id));

                    var eyeIcon = new RrIcon { Icon = s.Enabled ? RrIcon.Kind.Eye : RrIcon.Kind.EyeOff };
                    eyeIcon.AddToClassList("rr-icon");
                    EnableClass(eyeIcon, "rr-surf-icon--on", s.Enabled);
                    EnableClass(eyeIcon, "rr-surf-icon--dim", !s.Enabled);
                    var eyeBtn = new Button(); eyeBtn.AddToClassList("rr-surf-icon-btn"); eyeBtn.Add(eyeIcon);
                    eyeBtn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                    eyeBtn.clicked += () => { s.Enabled = !s.Enabled; };

                    var lockIcon = new RrIcon { Icon = s.Locked ? RrIcon.Kind.Lock : RrIcon.Kind.LockOpen };
                    lockIcon.AddToClassList("rr-icon");
                    EnableClass(lockIcon, "rr-surf-icon--warn", s.Locked);
                    EnableClass(lockIcon, "rr-surf-icon--dim", !s.Locked);
                    var lockBtn = new Button(); lockBtn.AddToClassList("rr-surf-icon-btn"); lockBtn.Add(lockIcon);
                    lockBtn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                    lockBtn.clicked += () => { s.Locked = !s.Locked; _warpCanvas?.MarkDirtyRepaint(); };

                    row.Add(eyeBtn); row.Add(lockBtn);

                    _surfaceList.Add(row);
                    _surfRows.Add(new SurfRow
                    {
                        root = row, dot = dot, name = name, meta = meta, id = s.Id, idStr = id,
                        eyeIcon = eyeIcon, lockIcon = lockIcon, lastEnabled = s.Enabled, lastLocked = s.Locked,
                    });
                }
            }
            _builtSurfaceCount = count;
            _selectedSurfaceId = int.MinValue; // 次の同期で WARP 対象を作り直す
        }

        void SyncSurfaceRows()
        {
            if (_surfaces == null) return;
            var list = _surfaces.Surfaces;
            for (int i = 0; i < _surfRows.Count && i < list.Count; i++)
            {
                var s = list[i];
                if (s == null) continue;
                var r = _surfRows[i];
                if (r.lastId != s.Id || !ReferenceEquals(r.lastName, s.Name))
                {
                    r.name.text = $"{s.Id + 1}. {s.Name}";
                    r.lastId = s.Id; r.lastName = s.Name; r.idStr = s.Id.ToString();
                }
                if (r.lastCols != s.WarpCols || r.lastRows != s.WarpRows)
                {
                    r.meta.text = $"{s.WarpCols}×{s.WarpRows}";
                    r.lastCols = s.WarpCols; r.lastRows = s.WarpRows;
                }
                bool active = _selection.Current.SameItem(SelectionKind.Surface, r.idStr);
                EnableClass(r.root, "rr-list-item--active", active);
                EnableClass(r.name, "rr-list-label--off", !s.Enabled && !active);

                if (r.lastEnabled != s.Enabled)
                {
                    r.lastEnabled = s.Enabled;
                    r.eyeIcon.Icon = s.Enabled ? RrIcon.Kind.Eye : RrIcon.Kind.EyeOff;
                    EnableClass(r.eyeIcon, "rr-surf-icon--on", s.Enabled);
                    EnableClass(r.eyeIcon, "rr-surf-icon--dim", !s.Enabled);
                }
                if (r.lastLocked != s.Locked)
                {
                    r.lastLocked = s.Locked;
                    r.lockIcon.Icon = s.Locked ? RrIcon.Kind.Lock : RrIcon.Kind.LockOpen;
                    EnableClass(r.lockIcon, "rr-surf-icon--warn", s.Locked);
                    EnableClass(r.lockIcon, "rr-surf-icon--dim", !s.Locked);
                }
            }
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
            // 構成変更（追加/削除）は準備 Edit のみ許可（Remove は SurfaceInspector 側・都度ビルド時に反映）
            if (_surfaceAdd != null) _surfaceAdd.SetEnabled(edit);
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

            // MAPPING ページ自体が warp 編集を制御する（独立 ◇ WARP トグルは U6 で廃止・1機能1箇所）。
            SelectPage(0);
        }

        // PERFORM=ライブプレビュー / MAPPING=WARP エディタ（#34 の多pin メッシュ編集・EMBED⇄OUTPUT）。
        // ページ切替でワープ編集の ON/OFF を駆動する（§1/§6）。左ドックのライブラリ⇄Surfaces 差替は将来分。
        void SelectPage(int page)
        {
            _page = page;
            // ページ切替でドック選択はクリア（track 選択は保持・§3）。FX 選択（SelectionKind.Fx）も
            // 同じ経路で Master へ戻る（U2 で FX 選択を _selection に統合したため特別扱い不要）。
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
                ApplyMappingLayout();
            }
        }

        // -------------------------------------------------- timeline sequence/short/song tabs (07b §3.5.2 / §7 / §7c・#13・#29 U11)
        // タブ切替（sequence=リニア通し / short=ホールド発火 / song=Sequence セットリスト）＋
        // Short の 4×4 パッド割当マトリクス／Hold-Loop。
        void BuildTimelineTabs()
        {
            _tlTablist  = _root.Q<VisualElement>("rr-tl-tablist");
            _tlSeq      = _root.Q<VisualElement>("rr-tl-seq");
            _tlShort    = _root.Q<VisualElement>("rr-tl-short");
            _tlSongList = _root.Q<VisualElement>("rr-tl-songlist");
            _shortClip  = _root.Q<VisualElement>("rr-short-clip");
            _shortPadBtn = _root.Q<Button>("rr-short-pad");
            _padMatrix   = _root.Q<VisualElement>("rr-pad-matrix");
            _holdLoopToggle = _root.Q<Toggle>("rr-short-holdloop");
            _shortAddTrackWrap = _root.Q<VisualElement>("rr-short-addtrack-wrap");
            _shortAddTrackMenu = _root.Q<VisualElement>("rr-short-addtrack-menu");
            _shortAddTrackBtn  = _root.Q<Button>("rr-short-add-track");
            _shortLanes = _root.Q<VisualElement>("rr-short-lanes");
            _shortBody = _root.Q<VisualElement>("rr-short-body");
            _shortPlayhead = _root.Q<VisualElement>("rr-short-playhead");

            // Song（セットリスト）＝横ストリップ：集計ヘッダー＋左固定 Add Sequence レール＋カード列（§7c・U12）。
            _songHeadSummary = _root.Q<Label>("rr-song-head-summary");
            _songHeadJump    = _root.Q<Button>("rr-song-head-jump");
            _songRailList    = _root.Q<VisualElement>("rr-song-rail-list");
            _songStrip       = _root.Q<VisualElement>("rr-song-strip");
            if (_songHeadJump != null) _songHeadJump.clicked += JumpToSongStepSequence;

            // 動的タブバー（Sequence/Short/Song 追加・削除・切替・07-10 App.jsx／§7c で3種へ）
            _tlAddMenu = _root.Q<VisualElement>("rr-tl-addmenu");
            _tlAddButton = _root.Q<Button>("rr-tl-add");
            if (_tlAddButton != null) _tlAddButton.clicked += ToggleAddMenu;
            var addSeq = _root.Q<Button>("rr-tl-add-seq");
            var addShort = _root.Q<Button>("rr-tl-add-short");
            var addSong = _root.Q<Button>("rr-tl-add-song");
            if (addSeq != null) addSeq.clicked += () =>
            {
                HideAddMenu();
                if (_timeline == null) return;
                SelectTab(ShowTimeline.TabKind.Sequence, _timeline.AddSequence());
            };
            if (addShort != null) addShort.clicked += () =>
            {
                HideAddMenu();
                if (_timeline == null) return;
                SelectTab(ShowTimeline.TabKind.Short, _timeline.AddShort());
            };
            if (addSong != null) addSong.clicked += () =>
            {
                HideAddMenu();
                if (_timeline == null) return;
                SelectTab(ShowTimeline.TabKind.Song, _timeline.AddSong());
            };
            if (_tlTablist == null || _tlAddMenu == null)
                Debug.LogWarning($"[OperatorUI] タイムラインのタブ要素が見つかりません（tablist={_tlTablist != null} addmenu={_tlAddMenu != null}）。" +
                                 "OperatorShell.uxml が最新にリインポートされているか確認してください。");
            RebuildTimelineTabs();

            // Sequence track 行（U3・旧称 Song）。+Track ボタンで video/audio のプレースホルダファイルから追加。
            _tlTracklist   = _root.Q<VisualElement>("rr-tl-tracklist");
            _addTrackWrap  = _root.Q<VisualElement>("rr-addtrack-wrap");
            _addTrackMenu  = _root.Q<VisualElement>("rr-addtrack-menu");
            _addTrackBtn   = _root.Q<Button>("rr-add-track");
            _addTrackDivider = _root.Q<VisualElement>("rr-addtrack-divider");
            if (_tlTracklist == null)
                Debug.LogWarning("[OperatorUI] rr-tl-tracklist が見つかりません。OperatorShell.uxml を Reimport してください。");
            BuildAddTrackMenuInto(_addTrackMenu, (kind, file) => { _timeline?.AddTrack(kind, file); RebuildSequenceTracks(); HideMenu(_addTrackMenu); });
            if (_addTrackBtn != null) _addTrackBtn.clicked += () => ToggleMenuAt(_addTrackMenu, _addTrackBtn);
            RebuildSequenceTracks();
            RebuildSongSteps();

            // Short 側 +Track（U7）: sequence と同じファイルライブラリ popover。選択で表示レーンを末尾に追加。
            BuildAddTrackMenuInto(_shortAddTrackMenu, (kind, file) => { AddShortLane(kind, file); HideMenu(_shortAddTrackMenu); });
            if (_shortAddTrackBtn != null)
                _shortAddTrackBtn.clicked += () => ToggleMenuAt(_shortAddTrackMenu, _shortAddTrackBtn);

            BuildPadMatrix();

            // KEY 行の割当ボタン＝⌨ アイコン＋キー文字＋⌄（マトリクスの開閉トグル・U7）。
            if (_shortPadBtn != null)
            {
                _shortPadBtn.Clear();
                var kbIcon = new RrIcon { Icon = RrIcon.Kind.Keyboard };
                kbIcon.AddToClassList("rr-icon"); kbIcon.AddToClassList("rr-short-pad__icon");
                _shortPadGlyph = new Label("—"); _shortPadGlyph.AddToClassList("rr-short-pad__glyph"); _shortPadGlyph.AddToClassList("rr-mono");
                var caret = new Label("⌄"); caret.AddToClassList("rr-short-pad__caret");
                _shortPadBtn.Add(kbIcon); _shortPadBtn.Add(_shortPadGlyph); _shortPadBtn.Add(caret);
                _shortPadBtn.clicked += TogglePadMatrix;
            }

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
                _timeline.StructureChanged -= OnTimelineStructureChanged;
                _timeline.StructureChanged += OnTimelineStructureChanged;
            }
            RefreshShortHeld();
            RefreshShortAssignment();

            // PERFORM 左ドック Banks（保存済み Sequence/Short/Song 一覧・U10）。
            _banksList = _root.Q<VisualElement>("rr-banks-list");
            RebuildBanksList();
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
            // absolute だが子順が rr-short-body より前のため、そのままだと ruler/レーンの裏に潜る。
            // 開くたび最前面へ（U7・レイヤー修正）。
            if (show) { _padMatrix.BringToFront(); RefreshShortAssignment(); }
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

            // 割当ボタン＝⌨ アイコン＋割当キー（グリフ）＋開閉手がかり ⌄。未割当は UNASSIGNED（減光・U7）。
            if (_shortPadGlyph != null)
                _shortPadGlyph.text = mine >= 0 ? ShowTimeline.PadGlyph(mine) : "UNASSIGNED";
            if (_shortPadBtn != null)
                EnableClass(_shortPadBtn, "rr-short-pad--unassigned", mine < 0);

            if (_holdLoopToggle != null && sh != null) _holdLoopToggle.SetValueWithoutNotify(sh.holdLoop);
        }

        // ---- 動的タブバー（Sequence/Short/Song・07-10 App.jsx／§7c で3種へ改名・U11）----
        // ShowTimeline の sequences+shorts+songs からタブを生成。クリックで切替、× で閉じる、+ で New。
        void RebuildTimelineTabs()
        {
            if (_tlTablist == null) return;
            _tlTablist.Clear();
            if (_timeline == null) return;
            _timeline.EnsureSeeded();   // Awake 未実行でもタブが出るよう明示シード

            for (int i = 0; i < _timeline.SequenceCount; i++)
                _tlTablist.Add(BuildTab(ShowTimeline.TabKind.Sequence, i));
            for (int i = 0; i < _timeline.ShortCount; i++)
                _tlTablist.Add(BuildTab(ShowTimeline.TabKind.Short, i));
            for (int i = 0; i < _timeline.SongCount; i++)
                _tlTablist.Add(BuildTab(ShowTimeline.TabKind.Song, i));

            ApplyTimelineView();
            RebuildBanksList();
        }

        static string TabCssKind(ShowTimeline.TabKind kind) => kind switch
        {
            ShowTimeline.TabKind.Sequence => "rr-tl-tab--seq",
            ShowTimeline.TabKind.Song     => "rr-tl-tab--songlist",
            _                             => "rr-tl-tab--short",
        };

        VisualElement BuildTab(ShowTimeline.TabKind kind, int index)
        {
            bool active = kind switch
            {
                ShowTimeline.TabKind.Sequence => _viewKind == ShowTimeline.TabKind.Sequence && _timeline.ActiveSequenceIndex == index,
                ShowTimeline.TabKind.Song     => _viewKind == ShowTimeline.TabKind.Song && _timeline.ActiveSongIndex == index,
                _                             => _viewKind == ShowTimeline.TabKind.Short && _timeline.ActiveShortIndex == index,
            };
            string tabName = kind switch
            {
                ShowTimeline.TabKind.Sequence => _timeline.GetSequence(index).name,
                ShowTimeline.TabKind.Song     => _timeline.GetSong(index).name,
                _                             => _timeline.GetShort(index).name,
            };

            var tab = new VisualElement();
            tab.AddToClassList("rr-tl-tab");
            tab.AddToClassList(TabCssKind(kind));
            EnableClass(tab, "rr-tl-tab--active", active);

            var iconKind = kind switch
            {
                ShowTimeline.TabKind.Sequence => RrIcon.Kind.AudioLines,
                ShowTimeline.TabKind.Song     => RrIcon.Kind.ListMusic,
                _                             => RrIcon.Kind.Zap,
            };
            var icon = new RrIcon { Icon = iconKind };
            icon.AddToClassList("rr-tl-tab__icon");
            tab.Add(icon);

            var name = new Label(tabName);
            name.AddToClassList("rr-tl-tab__name");
            tab.Add(name);

            // SONG/SHORT バッジは廃止（2026-07-12・kind アイコンで種別は分かるため、
            // タブ幅を Key 割当（keycap）に譲る＝視認性優先）。
            if (kind == ShowTimeline.TabKind.Short)
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
                        // 表示中の種別が空になったら、残っている種別へ切り替える（優先順位＝Sequence→Short→Song）。
                        if (_viewKind == kind)
                        {
                            if (_timeline.SequenceCount > 0) _viewKind = ShowTimeline.TabKind.Sequence;
                            else if (_timeline.ShortCount > 0) _viewKind = ShowTimeline.TabKind.Short;
                            else _viewKind = ShowTimeline.TabKind.Song;
                        }
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
            switch (kind)
            {
                case ShowTimeline.TabKind.Sequence: _timeline.SelectSequence(index); break;
                case ShowTimeline.TabKind.Song:     _timeline.SelectSong(index); _songSelStep = 0; break;
                default:                            _timeline.SelectShort(index); break;
            }
            _viewKind = kind;
            if (_shortLanes != null) _shortLanes.Clear();   // Short 切替で追加表示レーンは破棄（揮発・U7）
            RebuildTimelineTabs();
            RefreshShortAssignment();
            RebuildSequenceTracks();   // Sequence 切替で track 行を差し替え（U3）
            RebuildSongSteps();        // Song 切替でステップ列/プレビューを差し替え（U11）
            if (_tlTotal != null) _tlTotal.text = "/ " + ShowTimeline.FormatTime(_timeline.Length);
        }

        // -------------------------------------------------- Sequence track 行（U3・動的生成・旧称 Song）
        // ShowTimeline.ActiveSequence.tracks から行を生成する。行の増減は Sequence 切替／+Track の時だけ
        // （enabled/muted のトグルや選択ハイライトは行を作り直さず個別に更新・GC 回避）。
        void RebuildSequenceTracks()
        {
            if (_tlTracklist == null) return;
            _tlTracklist.Clear();
            _trackHeads.Clear();
            var seq = _timeline?.ActiveSequence;
            if (seq == null) return;

            for (int i = 0; i < seq.tracks.Count; i++)
                _tlTracklist.Add(BuildTrackRow(i, seq.tracks[i]));
        }

        VisualElement BuildTrackRow(int index, ShowTimeline.Track track)
        {
            bool isAudio = track.kind == ShowTimeline.TrackKind.Audio;

            var row = new VisualElement(); row.AddToClassList("rr-track");

            // ---- head（96px・選択トグル＋名前・クリックで track 選択）----
            var head = new VisualElement(); head.AddToClassList("rr-track-head");
            var enabledToggle = new Toggle(); enabledToggle.AddToClassList("rr-fx-toggle");
            enabledToggle.SetValueWithoutNotify(track.enabled);
            // Toggle 自体の Clickable が PointerDown を消費するため通常は伝播しないが、
            // 選択クリックへ確実に伝えないよう明示的に止める（罠2）。
            enabledToggle.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            enabledToggle.RegisterValueChangedCallback(evt => track.enabled = evt.newValue);
            var nameLabel = new Label(track.name); nameLabel.AddToClassList("rr-track-name");
            nameLabel.AddToClassList("rr-mono");
            EnableClass(nameLabel, "rr-track-name--off", !track.enabled);
            enabledToggle.RegisterValueChangedCallback(evt => EnableClass(nameLabel, "rr-track-name--off", !evt.newValue));
            head.Add(enabledToggle); head.Add(nameLabel);
            EnableClass(head, "rr-track-head--selected", IsTrackSelected(index));
            head.RegisterCallback<MouseDownEvent>(evt =>
                SelectTrack(index, evt.commandKey || evt.ctrlKey || evt.shiftKey));
            row.Add(head);
            _trackHeads.Add((head, index));

            // ---- lane（クリップ・start/duration を Song 尺に対する % に変換）----
            var lane = new VisualElement(); lane.AddToClassList("rr-track-lane");
            double length = _timeline != null ? _timeline.Length : 1.0;
            for (int c = 0; c < track.clips.Count; c++)
            {
                var clip = track.clips[c];
                var clipEl = new VisualElement(); clipEl.AddToClassList("rr-clip");
                clipEl.AddToClassList(isAudio ? "rr-clip--audio" : "rr-clip--source");
                clipEl.style.left = Length.Percent((float)(clip.start / length * 100.0));
                clipEl.style.width = Length.Percent((float)(clip.duration / length * 100.0));
                var clipLabel = new Label(clip.name.ToUpperInvariant()); clipLabel.AddToClassList("rr-clip__label");
                clipEl.Add(clipLabel);
                lane.Add(clipEl);
            }
            row.Add(lane);

            // ---- tail（74px・VID=Opacity 実値／AUD=FADE＋mute アイコン）----
            var tail = new VisualElement(); tail.AddToClassList("rr-track-tail");
            if (isAudio)
            {
                var fade = new Label("FADE"); fade.AddToClassList("rr-track-tail__label"); fade.AddToClassList("rr-mono");
                tail.Add(fade);
                var muteIcon = new RrIcon { Icon = track.muted ? RrIcon.Kind.SpeakerMute : RrIcon.Kind.SpeakerOn };
                muteIcon.AddToClassList("rr-icon"); muteIcon.AddToClassList("rr-track-icon");
                var muteBtn = new Button();
                muteBtn.AddToClassList("rr-track-mute-btn");
                muteBtn.Add(muteIcon);
                muteBtn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                muteBtn.clicked += () =>
                {
                    track.muted = !track.muted;
                    muteIcon.Icon = track.muted ? RrIcon.Kind.SpeakerMute : RrIcon.Kind.SpeakerOn;
                };
                tail.Add(muteBtn);
            }
            else
            {
                var opacityLabel = new Label(track.opacity.ToString("F2"));
                opacityLabel.AddToClassList("rr-track-tail__label"); opacityLabel.AddToClassList("rr-mono");
                tail.Add(opacityLabel);
            }
            row.Add(tail);

            return row;
        }

        bool IsTrackSelected(int index)
        {
            var sel = _selection.Current;
            if (sel.Kind != SelectionKind.Track) return false;
            for (int i = 0; i < sel.Tracks.Count; i++)
                if (sel.Tracks[i].Index == index) return true;
            return false;
        }

        // track ヘッダクリック（§3）: 単一クリック=そのトラックだけ選択（既に単独選択中なら解除）、
        // ⌘/Ctrl/Shift+クリック=トグル追加。
        void SelectTrack(int index, bool additive)
        {
            var current = _selection.Current;
            var next = new List<TrackId>();
            if (current.Kind == SelectionKind.Track) next.AddRange(current.Tracks);

            var id = new TrackId(index);
            if (additive)
            {
                int existing = next.FindIndex(t => t.Index == index);
                if (existing >= 0) next.RemoveAt(existing); else next.Add(id);
            }
            else
            {
                bool onlyThis = next.Count == 1 && next[0].Index == index;
                next.Clear();
                if (!onlyThis) next.Add(id);
            }
            _selection.SelectTracks(next);
        }

        // track 選択ハイライトのみ更新（行は作り直さない）。OnSelectionChanged から呼ぶ。
        void RefreshTrackHighlight()
        {
            for (int i = 0; i < _trackHeads.Count; i++)
            {
                var (head, index) = _trackHeads[i];
                EnableClass(head, "rr-track-head--selected", IsTrackSelected(index));
            }
        }

        // sequence/short/song の中央ビューを _viewKind に合わせる（Time 表示は共通・GATE 表記は廃止＝07-10・
        // §7c で3種へ拡張＝U11）。共通ヘッダの +Track は Sequence 用。short は Key 行左端に自前の +Track を
        // 持つ（U7）ので、ヘッダ側は short/song 表示中に隠す（Timeline.jsx の !isShort && !isSong ガードに合わせる）。
        void ApplyTimelineView()
        {
            bool isSeq = _viewKind == ShowTimeline.TabKind.Sequence;
            bool isShort = _viewKind == ShowTimeline.TabKind.Short;
            bool isSong = _viewKind == ShowTimeline.TabKind.Song;

            if (_tlSeq != null)      _tlSeq.style.display      = isSeq ? DisplayStyle.Flex : DisplayStyle.None;
            if (_tlShort != null)    _tlShort.style.display    = isShort ? DisplayStyle.Flex : DisplayStyle.None;
            if (_tlSongList != null) _tlSongList.style.display = isSong ? DisplayStyle.Flex : DisplayStyle.None;
            if (!isSeq) HideMenu(_addTrackMenu);
            if (!isShort) HideMenu(_shortAddTrackMenu);
            if (_addTrackWrap != null) _addTrackWrap.style.display = isSeq ? DisplayStyle.Flex : DisplayStyle.None;
            if (_addTrackDivider != null) _addTrackDivider.style.display = isSeq ? DisplayStyle.Flex : DisplayStyle.None;
            // Short は per-Short の Hold-Loop トグルが発火挙動を担うため、共通トランスポートの Loop は隠す
            // （役割重複の解消・U9）。Sequence/Song は通常のループ再生として Loop を維持。
            if (_tlLoop != null) _tlLoop.style.display = isShort ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // -------------------------------------------------- Song（Sequence セットリスト）＝MPC 流 横ストリップ（§7c・07-18・U12）
        // 左固定の Add Sequence レール（全 Sequence 常時一覧・1クリックで末尾追加）＋横スクロールのステップカード列
        // （番号／名前／Edit ペン→該当 Sequence タブ／×削除／×N ステッパー／‹›並べ替え）。カード間に → で再生の流れ。
        // 旧2ペイン（縦ステップリスト＋読み取り専用プレビュー・上方向ポップオーバー）は廃止。
        void RebuildSongSteps()
        {
            var song = _timeline?.ActiveSong;
            RebuildSongRail();

            if (_songStrip == null) return;
            _songStrip.Clear();
            _songCards.Clear();

            if (song == null) { UpdateSongSummary(null); return; }

            int count = song.steps.Count;
            if (_songSelStep >= count) _songSelStep = Mathf.Max(0, count - 1);

            if (count == 0)
            {
                var empty = new Label("左から順に再生されます — Add Sequence でステップを追加");
                empty.AddToClassList("rr-song-strip__empty");
                _songStrip.Add(empty);
            }
            for (int i = 0; i < count; i++)
            {
                var card = BuildSongCard(i, song.steps[i]);
                _songCards.Add(card);
                _songStrip.Add(card);
                var arrow = new Label("→"); arrow.AddToClassList("rr-song-arrow"); arrow.AddToClassList("rr-mono");
                _songStrip.Add(arrow);   // 最後のカードの後にも付く＝再生フローが続く表現（モック準拠）
            }

            UpdateSongSummary(song);

            // 構造を作り直したので再生中ハイライトも入れ直す（変化検知キャッシュは無視して強制反映）。
            _lastPlayingSongStep = -2;
            RefreshSongPlayingHighlight(_timeline != null ? _timeline.CurrentSongStep : -1);
        }

        // Song 再生中のカードを強調（rr-song-card--playing）。カードは構造変更時のみ作り直すので、
        // ここでは既存 _songCards の class トグルのみ（毎フレーム呼ばれても GC なし）。
        void RefreshSongPlayingHighlight(int stepIndex)
        {
            for (int i = 0; i < _songCards.Count; i++)
                EnableClass(_songCards[i], "rr-song-card--playing", i == stepIndex);
        }

        // 集計ヘッダー：N steps · M plays（M=Σ×N）＋選択ステップの Sequence へジャンプ。
        void UpdateSongSummary(ShowTimeline.Song song)
        {
            int steps = song != null ? song.steps.Count : 0;
            int plays = 0;
            if (song != null) for (int i = 0; i < song.steps.Count; i++) plays += Mathf.Max(1, song.steps[i].repeat);
            if (_songHeadSummary != null) _songHeadSummary.text = $"{steps} steps · {plays} plays";

            if (_songHeadJump != null)
            {
                bool has = song != null && _songSelStep >= 0 && _songSelStep < song.steps.Count;
                _songHeadJump.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                if (has) _songHeadJump.text = "Edit " + song.steps[_songSelStep].sequenceName + " →";
            }
        }

        VisualElement BuildSongCard(int index, ShowTimeline.SongStep step)
        {
            var card = new VisualElement();
            card.AddToClassList("rr-song-card");
            EnableClass(card, "rr-song-card--active", index == _songSelStep);
            card.RegisterCallback<MouseDownEvent>(_ => { _songSelStep = index; RebuildSongSteps(); });

            // 上段：番号 + Sequence 名 + Edit ペン + × 削除
            var top = new VisualElement(); top.AddToClassList("rr-song-card__top");
            var idx = new Label((index + 1).ToString("00")); idx.AddToClassList("rr-song-card__idx"); idx.AddToClassList("rr-mono");
            top.Add(idx);
            var name = new Label(step.sequenceName); name.AddToClassList("rr-song-card__name");
            top.Add(name);

            var pen = new Button(); pen.AddToClassList("rr-song-card__iconbtn"); pen.tooltip = "Edit " + step.sequenceName;
            var penIcon = new RrIcon { Icon = RrIcon.Kind.SquarePen }; penIcon.AddToClassList("rr-song-card__pen");
            pen.Add(penIcon);
            pen.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            pen.clicked += () => JumpToSequenceByName(step.sequenceName);
            top.Add(pen);

            var del = new Button { text = "×" }; del.AddToClassList("rr-song-card__iconbtn"); del.AddToClassList("rr-song-card__del"); del.tooltip = "Remove";
            del.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            del.clicked += () => { _timeline.RemoveSongStep(index); if (_songSelStep >= index && _songSelStep > 0) _songSelStep--; RebuildSongSteps(); };
            top.Add(del);
            card.Add(top);

            // 下段：[−] ×N [+] | [‹] [›]
            var bottom = new VisualElement(); bottom.AddToClassList("rr-song-card__bottom");
            Button StepBtn(string text, System.Action onClick, bool disabled = false)
            {
                var b = new Button { text = text };
                b.AddToClassList("rr-song-card__stepbtn");
                if (disabled) { b.AddToClassList("rr-song-card__stepbtn--disabled"); b.SetEnabled(false); }
                b.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                b.clicked += onClick;
                return b;
            }

            bottom.Add(StepBtn("−", () => { _timeline.SetSongStepRepeat(index, step.repeat - 1); RebuildSongSteps(); }));
            var repeat = new Label("×" + step.repeat); repeat.AddToClassList("rr-song-card__repeat"); repeat.AddToClassList("rr-mono");
            bottom.Add(repeat);
            bottom.Add(StepBtn("+", () => { _timeline.SetSongStepRepeat(index, step.repeat + 1); RebuildSongSteps(); }));

            var divider = new VisualElement(); divider.AddToClassList("rr-song-card__divider");
            bottom.Add(divider);

            int last = (_timeline?.ActiveSong?.steps.Count ?? 1) - 1;
            bottom.Add(StepBtn("‹", () => { _timeline.MoveSongStep(index, -1); _songSelStep = Mathf.Max(0, index - 1); RebuildSongSteps(); }, disabled: index == 0));
            bottom.Add(StepBtn("›", () => { _timeline.MoveSongStep(index, 1); _songSelStep = index + 1; RebuildSongSteps(); }, disabled: index >= last));
            card.Add(bottom);

            return card;
        }

        // Add Sequence レール：全 Sequence を常時一覧表示（縦スクロール）。クリックで末尾にステップ追加＋選択。
        void RebuildSongRail()
        {
            if (_songRailList == null) return;
            _songRailList.Clear();
            if (_timeline == null) return;

            int n = _timeline.SequenceCount;
            if (n == 0)
            {
                var empty = new Label("Sequence タブがありません。まず Sequence を作成してください。");
                empty.AddToClassList("rr-song-rail__empty");
                _songRailList.Add(empty);
                return;
            }
            for (int i = 0; i < n; i++)
            {
                var seq = _timeline.GetSequence(i);
                var item = new VisualElement(); item.AddToClassList("rr-song-rail-item"); item.tooltip = "Add " + seq.name;
                var dot = new VisualElement(); dot.AddToClassList("rr-song-rail-item__dot"); item.Add(dot);
                var lbl = new Label(seq.name); lbl.AddToClassList("rr-song-rail-item__name"); item.Add(lbl);
                var plus = new RrIcon { Icon = RrIcon.Kind.Plus }; plus.AddToClassList("rr-song-rail-item__plus"); item.Add(plus);
                string seqName = seq.name;
                item.RegisterCallback<MouseDownEvent>(_ =>
                {
                    _timeline.AddSongStep(seqName);
                    _songSelStep = Mathf.Max(0, _timeline.ActiveSong.steps.Count - 1);
                    RebuildSongSteps();
                });
                _songRailList.Add(item);
            }
        }

        // ステップの参照 Sequence タブへジャンプ（カードの Edit ペン／ヘッダーの Edit → から）。
        void JumpToSequenceByName(string name)
        {
            if (string.IsNullOrEmpty(name) || _timeline == null) return;
            for (int i = 0; i < _timeline.SequenceCount; i++)
                if (_timeline.GetSequence(i).name == name) { SelectTab(ShowTimeline.TabKind.Sequence, i); return; }
        }

        // ヘッダー「Edit <Seq> →」＝選択ステップの参照 Sequence タブへジャンプ（§7c）。
        void JumpToSongStepSequence()
        {
            var song = _timeline?.ActiveSong;
            if (song == null || _songSelStep < 0 || _songSelStep >= song.steps.Count) return;
            JumpToSequenceByName(song.steps[_songSelStep].sequenceName);
        }

        // -------------------------------------------------- PERFORM 左ドック Banks（U10・§7b 07-14）
        // 保存済み Sequence/Short/Song を一覧表示。クリックでタイムラインの該当タブを開く
        // （Inspector 選択にはしない＝タブ＝開いている文書／Banks＝保存済み一覧、というブラウザモデル）。
        void RebuildBanksList()
        {
            if (_banksList == null) return;
            _banksList.Clear();
            if (_timeline == null) return;

            void Row(string label, string meta, string dotClass, System.Action onClick)
            {
                var item = new VisualElement(); item.AddToClassList("rr-list-item");
                var dot = new VisualElement(); dot.AddToClassList("rr-list-dot"); dot.AddToClassList(dotClass);
                item.Add(dot);
                var name = new Label(label); name.AddToClassList("rr-list-label");
                item.Add(name);
                var metaLabel = new Label(meta); metaLabel.AddToClassList("rr-list-meta"); metaLabel.AddToClassList("rr-mono");
                item.Add(metaLabel);
                item.RegisterCallback<MouseDownEvent>(_ => onClick());
                _banksList.Add(item);
            }

            for (int i = 0; i < _timeline.SequenceCount; i++)
            {
                int idx = i;
                Row(_timeline.GetSequence(i).name, "SEQ", "rr-list-dot--seq", () => SelectTab(ShowTimeline.TabKind.Sequence, idx));
            }
            for (int i = 0; i < _timeline.ShortCount; i++)
            {
                int idx = i;
                Row(_timeline.GetShort(i).name, "SHORT", "rr-list-dot--short", () => SelectTab(ShowTimeline.TabKind.Short, idx));
            }
            for (int i = 0; i < _timeline.SongCount; i++)
            {
                int idx = i;
                Row(_timeline.GetSong(i).name, "SONG", "rr-list-dot--songlist", () => SelectTab(ShowTimeline.TabKind.Song, idx));
            }
        }

        // 追加メニューは _themeRoot 直下のオーバーレイに出す（タイムライン本体の裏に隠れる/クリップされる
        // のを防ぐ）。reparent 先は _root（UIDocument.rootVisualElement）ではなく _themeRoot（"rr-root"）＝
        // .rr-theme の内側にする。--rr-* カスタムプロパティは .rr-theme の子孫にしか継承されないため、
        // _root 直下へ逃がすと var(--rr-*) が解決できず背景/枠線が消え、裏の要素と文字が透けて重なる
        // （実機で発覚・修正済み・U5）。
        void ToggleAddMenu()
        {
            if (_tlAddMenu == null) return;
            bool show = _tlAddMenu.style.display == DisplayStyle.None;
            if (!show) { HideAddMenu(); return; }

            if (_themeRoot != null && _tlAddMenu.parent != _themeRoot) _themeRoot.Add(_tlAddMenu);
            if (_tlAddButton != null && _themeRoot != null)
            {
                // worldBound はパネル空間の座標。_themeRoot の局所座標系へ変換してから配置する
                // （そのまま left/top へ入れると _themeRoot にオフセットがある場合にズレて画面外/
                // 他パネルの裏に出て「見えない」原因になる）。
                var b = _tlAddButton.worldBound;
                var topLeft = _themeRoot.WorldToLocal(new Vector2(b.xMin, b.yMax + 2f));
                _tlAddMenu.style.position = Position.Absolute;
                _tlAddMenu.style.left = topLeft.x;
                _tlAddMenu.style.top = topLeft.y;
            }
            _tlAddMenu.style.display = DisplayStyle.Flex;
            _tlAddMenu.BringToFront();
        }
        void HideAddMenu() { if (_tlAddMenu != null) _tlAddMenu.style.display = DisplayStyle.None; }

        // -------------------------------------------------- + Track popover（U3・Timeline.jsx FILE_LIB 相当）
        // VIDEO/AUDIO のプレースホルダファイル一覧を一度だけ組み立てる（クリックの度に作り直さない）。
        // U7 で song/short 共用に一般化（onPick で行き先を切替）。実ファイルダイアログは対象外＝将来タスク。
        void BuildAddTrackMenuInto(VisualElement menu, System.Action<ShowTimeline.TrackKind, string> onPick)
        {
            if (menu == null) return;
            menu.Clear();
            AddTrackGroupInto(menu, "VIDEO", "rr-list-dot--source", ShowTimeline.TrackKind.Video, onPick);
            AddTrackGroupInto(menu, "AUDIO", "rr-list-dot--audio", ShowTimeline.TrackKind.Audio, onPick);
        }

        void AddTrackGroupInto(VisualElement menu, string label, string dotClass, ShowTimeline.TrackKind kind,
                               System.Action<ShowTimeline.TrackKind, string> onPick)
        {
            string kindTag = kind == ShowTimeline.TrackKind.Video ? "video" : "audio";

            var group = new VisualElement(); group.AddToClassList("rr-addtrack-group");
            var dot = new VisualElement(); dot.AddToClassList("rr-list-dot"); dot.AddToClassList(dotClass);
            var glabel = new Label(label); glabel.AddToClassList("rr-addtrack-group__label");
            group.Add(dot); group.Add(glabel);
            menu.Add(group);

            for (int i = 0; i < AddTrackFileLib.Length; i++)
            {
                var entry = AddTrackFileLib[i];
                if (entry.kind != kindTag) continue;

                var row = new Button(); row.AddToClassList("rr-addtrack-file");
                var name = new Label(entry.file); name.AddToClassList("rr-addtrack-file__name");
                var dur = new Label(entry.dur); dur.AddToClassList("rr-addtrack-file__dur"); dur.AddToClassList("rr-mono");
                row.Add(name); row.Add(dur);

                string file = entry.file;
                row.clicked += () => onPick(kind, file);
                menu.Add(row);
            }
        }

        // 追加メニュー共通の罠3/4 対策（_themeRoot 直下へ reparent・WorldToLocal で配置・BringToFront）。
        void ToggleMenuAt(VisualElement menu, VisualElement anchor)
        {
            if (menu == null) return;
            bool show = menu.style.display == DisplayStyle.None;
            if (!show) { HideMenu(menu); return; }

            if (_themeRoot != null && menu.parent != _themeRoot) _themeRoot.Add(menu);
            if (anchor != null && _themeRoot != null)
            {
                var b = anchor.worldBound;
                var topLeft = _themeRoot.WorldToLocal(new Vector2(b.xMin, b.yMax + 2f));
                menu.style.position = Position.Absolute;
                menu.style.left = topLeft.x;
                menu.style.top = topLeft.y;
            }
            menu.style.display = DisplayStyle.Flex;
            menu.BringToFront();
        }
        static void HideMenu(VisualElement menu) { if (menu != null) menu.style.display = DisplayStyle.None; }

        // Short に表示レーンを追加（U7・■ゴール4）。バックエンドの track モデルは持たず見た目のみ。
        // Short を切り替えると破棄する（Timeline.jsx の per-short `added` state と同じ揮発挙動）。
        void AddShortLane(ShowTimeline.TrackKind kind, string file)
        {
            if (_shortLanes == null) return;
            bool isAudio = kind == ShowTimeline.TrackKind.Audio;
            int n = _shortLanes.childCount + 2;   // 基底レーン(VID 1)の次から採番

            var row = new VisualElement(); row.AddToClassList("rr-track"); row.AddToClassList("rr-short-lane");
            var head = new VisualElement(); head.AddToClassList("rr-track-head");
            var name = new Label((isAudio ? "AUD " : "VID ") + n); name.AddToClassList("rr-track-name"); name.AddToClassList("rr-mono");
            head.Add(name);
            var lane = new VisualElement(); lane.AddToClassList("rr-track-lane");
            var clip = new VisualElement(); clip.AddToClassList("rr-clip");
            clip.AddToClassList(isAudio ? "rr-clip--audio" : "rr-clip--source");
            clip.style.left = Length.Percent(0f); clip.style.width = Length.Percent(isAudio ? 60f : 30f);
            var clabel = new Label(file.ToUpperInvariant()); clabel.AddToClassList("rr-clip__label");
            clip.Add(clabel); lane.Add(clip);
            row.Add(head); row.Add(lane);
            _shortLanes.Add(row);
        }

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

            // Song 再生中カードの強調（time 不変の early-return より前＝一時停止中のタブ切替でも反映される）。
            if (_viewKind == ShowTimeline.TabKind.Song)
            {
                int cur = _timeline.CurrentSongStep;
                if (cur != _lastPlayingSongStep)
                {
                    _lastPlayingSongStep = cur;
                    RefreshSongPlayingHighlight(cur);
                }
            }

            double t = _timeline.Time;
            if (t == _lastTimeValue) return;
            _lastTimeValue = t;

            // 再生ヘッドはクリップ・レーンの座標系で置く（本体全幅 % だとトラックヘッダ分ずれ、
            // t=0 が 0:00 目盛りに揃わない）。left = ヘッダ幅 + 正規化位置 × レーン実幅。
            // 非表示側は resolvedStyle.width=0 → usable<=0 でスキップされる（Sequence/Short 両方を更新して可）。
            float nt = _timeline.NormalizedTime;
            if (_playhead != null && _tlSeq != null)
            {
                float usable = _tlSeq.resolvedStyle.width - TimelineLaneLeft - TimelineLaneRight;
                if (usable > 0f) _playhead.style.left = TimelineLaneLeft + nt * usable;
            }
            // Short は右端 tail 無し（レーン head 96px のみ）。表示のみ＝同じ sequence クロックで近似（U7）。
            if (_shortPlayhead != null && _shortBody != null)
            {
                float usable = _shortBody.resolvedStyle.width - TimelineLaneLeft;
                if (usable > 0f) _shortPlayhead.style.left = TimelineLaneLeft + nt * usable;
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
        // 静的プレースホルダ行の kind は行の marker クラス（rr-item-*）で判別する
        // （foldout 単位ではなく行単位＝Audio 内に audio-input と mapping が混在するため・U2）。
        // 実データ連動（左ドックの実体化）は #3/#11。
        static readonly (string cls, SelectionKind kind)[] DockItemKindMarkers =
        {
            ("rr-item-source-video",  SelectionKind.SourceVideo),
            ("rr-item-source-camera", SelectionKind.SourceCamera),
            ("rr-item-audio-input",   SelectionKind.AudioInput),
            ("rr-item-mapping",       SelectionKind.Mapping),
            ("rr-item-scene",         SelectionKind.Scene),
        };

        void BuildDockSelection()
        {
            _dockItems.Clear();
            var perform = _root.Q<VisualElement>("rr-dock-perform");
            if (perform != null) WireDockItems(perform);

            _selection.Changed -= OnSelectionChanged;
            _selection.Changed += OnSelectionChanged;
        }

        void WireDockItems(VisualElement container)
        {
            container.Query<VisualElement>(className: "rr-list-item").ForEach(item =>
            {
                SelectionKind kind = SelectionKind.None;
                foreach (var (cls, k) in DockItemKindMarkers)
                    if (item.ClassListContains(cls)) { kind = k; break; }
                if (kind == SelectionKind.None) return;   // 未分類行は選択対象にしない（安全側）

                var lbl = item.Q<Label>(className: "rr-list-label");
                var meta = item.Q<Label>(className: "rr-list-meta");
                string id = lbl != null ? lbl.text : "";
                _dockItems.Add((item, kind, id, meta != null ? meta.text : null));
                item.RegisterCallback<MouseDownEvent>(_ => _selection.Select(kind, id));
            });
        }

        // _dockItems から一致する行のメタ文字列（尺・解像度・アマウント等の右寄せ表示）を探す。
        string FindDockMeta(SelectionRef sel)
        {
            for (int i = 0; i < _dockItems.Count; i++)
            {
                var d = _dockItems[i];
                if (d.kind == sel.Kind && d.id == sel.Id) return d.meta;
            }
            return null;
        }

        void OnSelectionChanged(SelectionRef sel)
        {
            for (int i = 0; i < _dockItems.Count; i++)
            {
                var d = _dockItems[i];
                EnableClass(d.item, "rr-list-item--active", sel.SameItem(d.kind, d.id));
            }
            RefreshTrackHighlight();   // track 行のハイライトのみ更新（行は作り直さない・U3）
            if (sel.Kind == SelectionKind.Surface) SyncActiveSurfaceFromSelection(sel);   // WARP 対象を選択へ追従（U4）
            RebuildInspector();   // 選択に応じて Inspector を出し分け（無選択＝Master/Program）
        }

        // Surface 選択（左ドック行 or 将来の他経路）を _surfaces.ActiveIndex へ反映する。
        // WARP 編集対象の解決（ResolveWarpTarget）は今まで通り ActiveIndex を見るため、
        // 選択の一本化後もここだけ橋渡しすればよい（Goal2・U4）。
        void SyncActiveSurfaceFromSelection(SelectionRef sel)
        {
            if (_surfaces == null || !int.TryParse(sel.Id, out int wantId)) return;
            var list = _surfaces.Surfaces;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Id == wantId) { _surfaces.ActiveIndex = i; return; }
        }

        // ドック項目（track 以外の単一選択）が Inspector を占有するか。
        bool DockSelectionActive()
        {
            var k = _selection.Current.Kind;
            return k != SelectionKind.None && k != SelectionKind.Track;
        }

        // ドック選択の Inspector を kind ごとにディスパッチ（§4b・U2）。
        // Surface/SourceExt（MAPPING 側）は未対応（U4 で実装）＝簡易プレースホルダのまま。
        void BuildDockInspector(SelectionRef sel)
        {
            if (_inspector == null) return;
            _inspector.Clear();
            _paramRows.Clear();

            switch (sel.Kind)
            {
                case SelectionKind.Fx:           BuildFxInspector(); return;
                case SelectionKind.SourceVideo:  BuildSourceVideoInspector(sel); return;
                case SelectionKind.SourceCamera: BuildSourceCameraInspector(sel); return;
                case SelectionKind.AudioInput:   BuildAudioInputInspector(sel); return;
                case SelectionKind.Mapping:      BuildMappingInspector(sel); return;
                case SelectionKind.Scene:        BuildSceneInspector(sel); return;
                case SelectionKind.Surface:      BuildSurfaceInspector(sel); return;
                default:                         BuildGenericDockInspector(sel); return;
            }
        }

        // Surface/SourceExt 等・per-kind 未実装分の簡易表示（U4 で置き換え）。
        void BuildGenericDockInspector(SelectionRef sel)
        {
            // _inspectorEffect は更新しない（LateUpdate の FX ポーリングはこのビュー中は走らない）。
            if (_inspectorTitle != null) _inspectorTitle.text = sel.Id;
            var kindLabel = new Label(KindLabel(sel.Kind));
            kindLabel.AddToClassList("rr-hint");
            _inspector.Add(kindLabel);
            var todo = new Label("Inspector controls: 後続スライスで実装（U4）");
            todo.AddToClassList("rr-hint");
            _inspector.Add(todo);
        }

        // -------------------------------------------------- fx（U2・既存 FX パラメータ表示の後継）
        // FX Chain 一覧（常設の rr-fx-list）の行クリックがここへ来る。Amount/Audio Gain/Mix は
        // 実際の EffectParameter にバインド（ParamRow として登録し SyncParamRows で毎フレーム追従・
        // ドラッグ中は上書きしない）。Enabled トグル・Scope・OSC アドレス表示も実データ。
        void BuildFxInspector()
        {
            if (_hub == null) { if (_inspectorTitle != null) _inspectorTitle.text = "FX"; return; }
            var fx = _hub.GetEffect(_hub.SelectedEffect);
            _inspectorEffect = _hub.SelectedEffect;
            if (_inspectorTitle != null) _inspectorTitle.text = fx != null ? fx.Name : "FX";
            if (fx == null) return;

            bool live = _appMode != null && _appMode.IsLive;

            AddSectionLabel(fx.Name, StagePill("EFFECTS", "effects"));
            AddToggleRow("Enabled", fx.enabled, v => fx.enabled = v);

            TryFindParam(fx, "Amount", out var amountP, out int amountIdx);
            if (amountP == null && fx.Parameters.Count > 1) { amountIdx = 1; amountP = fx.Parameters[1]; }
            AddFxParamRow("Amount", amountP, amountIdx, live);

            TryFindParam(fx, "Audio Gain", out var gainP, out int gainIdx);
            AddFxParamRow("Audio Gain", gainP, gainIdx, live);

            TryFindParam(fx, "Mix", out var mixP, out int mixIdx);
            if (mixP == null) { mixIdx = 0; mixP = fx.Parameters[0]; }   // Mix は基底で必ず [0] に存在
            AddFxParamRow("Mix", mixP, mixIdx, false);

            AddSectionLabel("Scope");
            string scopeText = fx.scope == EffectScope.Surface ? "SURFACE " + fx.targetSurfaceId : "GLOBAL";
            AddInfoRow("Target", scopeText);

            string slug = Slugify(fx.Name);
            float oscVal = amountP != null ? amountP.Value : 0f;
            AddCodeRow("OSC", "/rr/fx/" + slug + "/amount " + oscVal.ToString("F2"));

            AddDeselectRow();
        }

        static string Slugify(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }

        static void TryFindParam(EffectBase fx, string name, out EffectParameter param, out int index)
        {
            var ps = fx.Parameters;
            for (int i = 0; i < ps.Count; i++)
                if (ps[i].Name == name) { param = ps[i]; index = i; return; }
            param = null; index = -1;
        }

        // -------------------------------------------------- source-video（U2）
        void BuildSourceVideoInspector(SelectionRef sel)
        {
            if (_inspectorTitle != null) _inspectorTitle.text = sel.Id;
            AddSectionLabel(sel.Id, StagePill("SOURCE", "source"));

            if (_sourceVideo == null) _sourceVideo = FindFirstObjectByType<SourceVideo>();
            var video = _sourceVideo;
            bool live = _appMode != null && _appMode.IsLive;

            AddSliderRow("Speed", video != null ? video.Speed : 1f, 0.1f, 4f, "x", live,
                v => { if (video != null) video.Speed = v; });
            AddToggleRow("Loop", video != null && video.Loop, v => { if (video != null) video.Loop = v; });

            _srcVideoTimeLabel = AddInfoRow("Time", video != null ? ShowTimeline.FormatTime(video.Time) : "00:00.00");
            _lastSrcVideoTime = video != null ? video.Time : -1d;

            string dur = video != null ? ShowTimeline.FormatTime(video.Duration) : FindDockMeta(sel);
            AddInfoRow("Duration", dur ?? "—");

            AddDeselectRow();
        }

        // -------------------------------------------------- source-camera（U2・当面表示のみ）
        void BuildSourceCameraInspector(SelectionRef sel)
        {
            if (_inspectorTitle != null) _inspectorTitle.text = sel.Id;
            AddSectionLabel(sel.Id, StagePill("SOURCE", "source"));

            if (_sourceCamera == null) _sourceCamera = FindFirstObjectByType<SourceCamera>();
            string res = null;
            if (_sourceCamera != null && _sourceCamera.Texture != null)
                res = _sourceCamera.Texture.width + "×" + _sourceCamera.Texture.height;
            AddInfoRow("Resolution", res ?? FindDockMeta(sel) ?? "—");

            // Exposure/Zoom/Embed: SourceCamera に該当 API 無し。当面 UI 側で値保持のみ。
            AddSliderRow("Exposure", _camExposure, 0f, 1f, "", false, v => _camExposure = v);
            AddSliderRow("Zoom", _camZoom, 0.5f, 3f, "x", false, v => _camZoom = v);
            AddToggleRow("Embed", _camEmbed, v => _camEmbed = v);

            AddDeselectRow();
        }

        // -------------------------------------------------- audio-input（U2・メーターは毎フレーム更新）
        void BuildAudioInputInspector(SelectionRef sel)
        {
            if (_inspectorTitle != null) _inspectorTitle.text = sel.Id;
            AddSectionLabel(sel.Id, StagePill("AUDIO", "audio"));

            if (_audioAnalyzer == null) _audioAnalyzer = FindFirstObjectByType<AudioAnalyzer>();
            BuildAudioMeterRow();

            AddSliderRow("Sensitivity", _audioSensitivity, 0f, 1f, "", false, v => _audioSensitivity = v);
            float rms = _audioAnalyzer != null ? _audioAnalyzer.Features.Rms : 0f;
            _rmsValueLabel = AddInfoRow("RMS", rms.ToString("F2"));
            _lastRmsCenti = Mathf.RoundToInt(rms * 100f);
            AddInfoRow("Source", FindDockMeta(sel) ?? "—");

            AddDeselectRow();
        }

        void BuildAudioMeterRow()
        {
            var row = new VisualElement(); row.AddToClassList("rr-meter-row");
            _meterFillRms  = AddMeterTo(row);
            _meterFillLow  = AddMeterTo(row);
            _meterFillMid  = AddMeterTo(row);
            _meterFillHigh = AddMeterTo(row);
            _inspector.Add(row);
        }

        static VisualElement AddMeterTo(VisualElement row)
        {
            var meter = new VisualElement(); meter.AddToClassList("rr-meter");
            var fill = new VisualElement(); fill.AddToClassList("rr-meter__fill");
            meter.Add(fill);
            row.Add(meter);
            return fill;
        }

        // -------------------------------------------------- mapping（U2・実データ無し＝表示のみ）
        // ラベル文字列「Low → Feedback」から band/target を、meta から amount を読む
        // （左ドックの実データ連動＝#3/#11 が入るまでのプレースホルダ解析）。
        void BuildMappingInspector(SelectionRef sel)
        {
            if (_inspectorTitle != null) _inspectorTitle.text = sel.Id;
            AddSectionLabel(sel.Id, StagePill("AUDIO", "audio"));

            string band = "—", target = "—";
            int arrow = sel.Id.IndexOf('→');
            if (arrow >= 0)
            {
                band = sel.Id.Substring(0, arrow).Trim().ToUpperInvariant();
                target = sel.Id.Substring(arrow + 1).Trim().ToUpperInvariant();
            }
            AddInfoRow("Band", band);
            AddInfoRow("Target", target);

            // TryParse は失敗時も out を 0 で埋めるため、既定値は成功時のみ上書きする。
            float amt = 0.5f;
            if (float.TryParse(FindDockMeta(sel), out var parsedAmt)) amt = parsedAmt;
            AddSliderRow("Amount", amt, 0f, 1f, "", false, v => { });
            AddSliderRow("Smoothing", 0.2f, 0f, 1f, "", false, v => { });
            AddInfoRow("Curve", "EXP");

            AddDeselectRow();
        }

        // -------------------------------------------------- scene（U2・実データ無し＝表示のみ）
        void BuildSceneInspector(SelectionRef sel)
        {
            if (_inspectorTitle != null) _inspectorTitle.text = sel.Id;
            AddSectionLabel(sel.Id, StagePill("SCENE", "scene"));

            AddSliderRow("Fade In", 0.5f, 0f, 5f, "s", false, v => { });
            AddSliderRow("Fade Out", 1.2f, 0f, 5f, "s", false, v => { });

            AddSectionLabel("Trigger");
            AddInfoRow("Key", "PAD 4");
            AddToggleRow("Hold", true, v => { });

            AddButtonRow(
                MakeButton("Fire", "primary", null, enabled: false),
                MakeButton("Save", "secondary", null, enabled: false),
                MakeButton("Deselect", "ghost", () => _selection.Deselect()));
        }

        // -------------------------------------------------- surface（U4・MAPPING 左ドックから移設）
        // Input Surface のみ対応（Output Surface は SurfaceManager が管理する一覧に無く、
        // WARP エディタの EMBED⇄OUTPUT トグル経由で編集する既存方式のまま・#27 で扱う）。
        void BuildSurfaceInspector(SelectionRef sel)
        {
            var s = FindSurfaceById(sel.Id);
            if (s == null) { BuildGenericDockInspector(sel); return; }

            bool live = _appMode != null && _appMode.IsLive;

            if (_inspectorTitle != null) _inspectorTitle.text = s.Name;
            AddSectionLabel("Surface", Badge(s.Name, "rr-badge--selection"));
            AddInfoRow("Grid", $"{s.WarpCols}×{s.WarpRows}");
            AddSliderRow("Opacity", s.Opacity, 0f, 1f, "", live, v => s.Opacity = v);

            AddFitModeRow(s);

            if (s.Fit == Surface.FitMode.Mask)
            {
                AddSectionLabel("Shape");
                AddStepRow("Scale", () => ScaleWarpTarget(1f / 1.1f), () => ScaleWarpTarget(1.1f));
                AddSectionLabel("Content");
                AddSliderRow("Zoom", s.ContentZoom, 0.2f, 5f, "x", live, v => s.ContentZoom = v);
                AddInfoRow("Pan", "DRAG");
                var hint = new Label("Window cutout — content stays undistorted.");
                hint.AddToClassList("rr-surf-hint");
                _inspector.Add(hint);
            }
            else
            {
                AddSectionLabel("Mesh Warping");
                AddToggleRow("Enabled", true, v => { });   // Grid Fit＝常時ワープ有効（表示のみ・■ゴール3）
                AddGridStepRow(s);
                AddToggleRow("Bezier", s.BezierInterp, v => { s.BezierInterp = v; _warpCanvas?.MarkDirtyRepaint(); });
                AddToggleRow("Test Pattern", s.Content == Surface.ContentKind.Pattern, v =>
                {
                    s.Content = v ? Surface.ContentKind.Pattern : Surface.ContentKind.Camera;   // §5 校正
                    RefreshWarpTestBtn();
                });
                AddButtonRow(MakeButton("Reset Warp", "secondary", () =>
                {
                    s.ResetWarp();
                    _warpCanvas?.MarkDirtyRepaint();
                }));
            }

            bool edit = _appMode == null || _appMode.IsEdit;   // 構成変更（削除）は準備 Edit のみ許可
            AddButtonRow(
                MakeButton("Remove", "ghost", () =>
                {
                    if (_surfaces != null && _surfaces.Remove(s)) { _builtSurfaceCount = -1; _selection.Deselect(); }
                }, enabled: edit),
                MakeButton("Deselect", "ghost", () => _selection.Deselect()));
        }

        Surface FindSurfaceById(string id)
        {
            if (_surfaces == null || !int.TryParse(id, out int wantId)) return null;
            return _surfaces.Get(wantId);
        }

        // Fit Mode セグメント（MASK|GRID・UNITY-HANDOFF §5）。切替はセクション出し分けが伴うため
        // Inspector 全体を作り直す（クリック起点のみ・毎フレームではないので GC 上問題なし）。
        void AddFitModeRow(Surface s)
        {
            var row = new VisualElement(); row.AddToClassList("rr-param-row");
            var lbl = new Label("Fit Mode"); lbl.AddToClassList("rr-param-label");
            var spacer = new VisualElement(); spacer.style.flexGrow = 1f;
            var seg = new VisualElement(); seg.AddToClassList("rr-seg");
            var maskBtn = new Button { text = "MASK" }; maskBtn.AddToClassList("rr-seg__btn"); maskBtn.AddToClassList("rr-mono");
            var gridBtn = new Button { text = "GRID" }; gridBtn.AddToClassList("rr-seg__btn"); gridBtn.AddToClassList("rr-mono");
            EnableClass(maskBtn, "rr-seg__btn--active", s.Fit == Surface.FitMode.Mask);
            EnableClass(gridBtn, "rr-seg__btn--active", s.Fit == Surface.FitMode.Grid);
            maskBtn.clicked += () => { s.Fit = Surface.FitMode.Mask; RebuildInspector(); };
            gridBtn.clicked += () => { s.Fit = Surface.FitMode.Grid; RebuildInspector(); };
            seg.Add(maskBtn); seg.Add(gridBtn);
            row.Add(lbl); row.Add(spacer); row.Add(seg);
            _inspector.Add(row);
        }

        // ラベル＋−/+ ステッパー（値表示なし＝ Scale は重心基準の相対操作でバックエンドに永続値が無い）。
        void AddStepRow(string label, System.Action onDec, System.Action onInc)
        {
            var row = new VisualElement(); row.AddToClassList("rr-param-row");
            var lbl = new Label(label); lbl.AddToClassList("rr-param-label");
            var spacer = new VisualElement(); spacer.style.flexGrow = 1f;
            var dec = new Button { text = "−" }; dec.AddToClassList("rr-surf-step"); dec.clicked += onDec;
            var inc = new Button { text = "+" }; inc.AddToClassList("rr-surf-step"); inc.clicked += onInc;
            row.Add(lbl); row.Add(spacer); row.Add(dec); row.Add(inc);
            _inspector.Add(row);
        }

        // Grid X×Y ステッパー（NudgeGrid・変更後は RebuildInspector で値表示を作り直す）。
        void AddGridStepRow(Surface s)
        {
            var row = new VisualElement(); row.AddToClassList("rr-param-row");
            var lbl = new Label("Grid"); lbl.AddToClassList("rr-param-label");
            var spacer = new VisualElement(); spacer.style.flexGrow = 1f;
            row.Add(lbl); row.Add(spacer);

            var colsDec = new Button { text = "−" }; colsDec.AddToClassList("rr-surf-step");
            var colsVal = new Label(s.WarpCols.ToString()); colsVal.AddToClassList("rr-param-value"); colsVal.AddToClassList("rr-mono");
            var colsInc = new Button { text = "+" }; colsInc.AddToClassList("rr-surf-step");
            var x = new Label("×"); x.AddToClassList("rr-param-label"); x.AddToClassList("rr-mono");
            var rowsDec = new Button { text = "−" }; rowsDec.AddToClassList("rr-surf-step");
            var rowsVal = new Label(s.WarpRows.ToString()); rowsVal.AddToClassList("rr-param-value"); rowsVal.AddToClassList("rr-mono");
            var rowsInc = new Button { text = "+" }; rowsInc.AddToClassList("rr-surf-step");

            colsDec.clicked += () => { NudgeGrid(-1, 0); RebuildInspector(); };
            colsInc.clicked += () => { NudgeGrid(+1, 0); RebuildInspector(); };
            rowsDec.clicked += () => { NudgeGrid(0, -1); RebuildInspector(); };
            rowsInc.clicked += () => { NudgeGrid(0, +1); RebuildInspector(); };

            row.Add(colsDec); row.Add(colsVal); row.Add(colsInc);
            row.Add(x);
            row.Add(rowsDec); row.Add(rowsVal); row.Add(rowsInc);
            _inspector.Add(row);
        }

        // -------------------------------------------------- track（U3・Sequence track 選択）
        // 単一選択=video(Role/Opacity 実値/Blend/Track FX 一覧)・audio(Role/Volume/Fade/メーター)。
        // 複数選択=一覧＋Group（Opacity は mixed 表示・Mute All は実際に全選択トラックへ適用）。
        void BuildTrackInspector(SelectionRef sel)
        {
            if (_inspector == null) return;
            _inspector.Clear();
            _paramRows.Clear();

            var seq = _timeline?.ActiveSequence;
            if (seq == null || sel.Tracks.Count == 0) { BuildMasterInspector(); return; }

            if (sel.Tracks.Count > 1) { BuildMultiTrackInspector(seq, sel.Tracks); return; }

            int idx = sel.Tracks[0].Index;
            if (idx < 0 || idx >= seq.tracks.Count) { BuildMasterInspector(); return; }
            var track = seq.tracks[idx];
            bool isAudio = track.kind == ShowTimeline.TrackKind.Audio;

            if (_inspectorTitle != null) _inspectorTitle.text = track.name;
            AddSectionLabel("Track", Badge(track.name, "rr-badge--selection"));
            // Role: Track に該当フィールド無し（バックエンド API 未定義）。当面 "—" 表示のみ。
            AddInfoRow("Role", "—");

            if (isAudio)
            {
                AddSliderRow("Volume", _trackVolume, 0f, 1f, "", false, v => _trackVolume = v);
                AddToggleRow("Fade", _trackFade, v => _trackFade = v);
                if (_audioAnalyzer == null) _audioAnalyzer = FindFirstObjectByType<AudioAnalyzer>();
                BuildAudioMeterRow();

                AddSectionLabel("Audio Mappings", StagePill("AUDIO", "audio"));
                var hint = new Label("No mappings assigned"); hint.AddToClassList("rr-hint");
                _inspector.Add(hint);
                AddButtonRow(
                    MakeButton("+ Mapping", "secondary", null, enabled: false),
                    MakeButton("Deselect", "ghost", () => _selection.Deselect()));
            }
            else
            {
                int capturedIdx = idx;
                AddSliderRow("Opacity", track.opacity, 0f, 1f, "", _appMode != null && _appMode.IsLive,
                    v => { track.opacity = v; RefreshTrackOpacityLabel(capturedIdx, v); });
                AddInfoRow("Blend", "NORMAL");

                AddSectionLabel("Track FX", StagePill("EFFECTS", "effects"));
                var hint = new Label("No FX assigned"); hint.AddToClassList("rr-hint");
                _inspector.Add(hint);
                AddButtonRow(
                    MakeButton("+ Effect", "secondary", null, enabled: false),
                    MakeButton("Deselect", "ghost", () => _selection.Deselect()));
            }
        }

        // Opacity スライダ操作を track 行右端の実値ラベルへも反映（行を作り直さない）。
        void RefreshTrackOpacityLabel(int index, float value)
        {
            for (int i = 0; i < _trackHeads.Count; i++)
            {
                if (_trackHeads[i].index != index) continue;
                var tail = _trackHeads[i].head.parent?.Q<Label>(className: "rr-track-tail__label");
                if (tail != null) tail.text = value.ToString("F2");
                return;
            }
        }

        void BuildMultiTrackInspector(ShowTimeline.Sequence seq, IReadOnlyList<TrackId> tracks)
        {
            if (_inspectorTitle != null) _inspectorTitle.text = tracks.Count + " Tracks";
            AddSectionLabel("Selection", Badge(tracks.Count + " TRACKS", "rr-badge--selection"));

            for (int i = 0; i < tracks.Count; i++)
            {
                int idx = tracks[i].Index;
                if (idx < 0 || idx >= seq.tracks.Count) continue;
                var t = seq.tracks[idx];
                bool isAudio = t.kind == ShowTimeline.TrackKind.Audio;

                var row = new VisualElement(); row.AddToClassList("rr-list-item");
                var dot = new VisualElement(); dot.AddToClassList("rr-list-dot");
                dot.AddToClassList(isAudio ? "rr-list-dot--audio" : "rr-list-dot--source");
                var name = new Label(t.name); name.AddToClassList("rr-list-label");
                row.Add(dot); row.Add(name);
                _inspector.Add(row);
            }

            AddSectionLabel("Group");
            AddInfoRow("Opacity", "mixed");
            AddToggleRow("Mute All", AllSelectedMuted(seq, tracks), v =>
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    int idx = tracks[i].Index;
                    if (idx >= 0 && idx < seq.tracks.Count) seq.tracks[idx].muted = v;
                }
                RebuildSequenceTracks();
            });

            AddDeselectRow();
        }

        static bool AllSelectedMuted(ShowTimeline.Sequence seq, IReadOnlyList<TrackId> tracks)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                int idx = tracks[i].Index;
                if (idx < 0 || idx >= seq.tracks.Count) continue;
                if (!seq.tracks[idx].muted) return false;
            }
            return true;
        }

        // -------------------------------------------------- Master/Program（無選択時の既定・§4a・U1）
        // FX Chain の一覧自体は常設の rr-fx-list（右ドック上段）が担うため、ここでは重複させない
        // （FX 行クリックは RebuildFxList 側の配線で従来の FX パラメータ表示へ切り替える）。
        void BuildMasterInspector()
        {
            if (_inspector == null) return;
            _inspector.Clear();
            _paramRows.Clear();
            // _inspectorEffect は更新しない（LateUpdate の FX ポーリングはこのビュー中は走らない）。
            if (_inspectorTitle != null) _inspectorTitle.text = "Master";

            bool live = _appMode != null && _appMode.IsLive;

            AddSectionLabel("Master", Badge("PROGRAM", "rr-badge--live"));

            AddSliderRow("Master", _hub != null ? _hub.Master : 1f, 0f, 1f, "", live,
                v => { if (_hub != null) _hub.Master = v; });
            AddSliderRow("Fade to Black", _hub != null ? _hub.FadeToBlack : 0f, 0f, 1f, "", false,
                v => { if (_hub != null) _hub.FadeToBlack = v; });
            AddSliderRow("Speed", _hub != null ? _hub.MasterSpeed : 1f, 0f, 4f, "x", live,
                v => { if (_hub != null) _hub.MasterSpeed = v; });

            string res = "1920×1080";
            if (_chain != null && _chain.FinalTexture != null)
                res = _chain.FinalTexture.width + "×" + _chain.FinalTexture.height;
            AddInfoRow("Output", res);

            AddBpmRow(_hub != null ? _hub.Bpm : 128f, v => { if (_hub != null) _hub.Bpm = v; });
        }

        // 小見出し（右に任意で Badge/StagePill 等）。既存 UXML の SURFACES 見出しと同じクラスを使う。
        void AddSectionLabel(string text, VisualElement right = null)
        {
            var row = new VisualElement(); row.AddToClassList("rr-section-label");
            var lbl = new Label(text); lbl.AddToClassList("rr-section-label__text");
            row.Add(lbl);
            if (right != null) row.Add(right);
            _inspector.Add(row);
        }

        // Badge（Master の PROGRAM 等・角丸縁取り）。
        static Label Badge(string text, string toneClass = null)
        {
            var badge = new Label(text); badge.AddToClassList("rr-badge");
            if (!string.IsNullOrEmpty(toneClass)) badge.AddToClassList(toneClass);
            return badge;
        }

        // StagePill（項目種別の色付きピル・見出し右）。suffix は rr-stage-pill--{suffix} に対応。
        static Label StagePill(string text, string suffix)
        {
            var pill = new Label(text); pill.AddToClassList("rr-stage-pill");
            pill.AddToClassList("rr-stage-pill--" + suffix);
            return pill;
        }

        // スライダ行（Master/Fade to Black/Speed 用）。既存 FX パラメータ行と同じ見た目・テンプレ規約。
        void AddSliderRow(string label, float value, float min, float max, string unit, bool armed,
                          System.Action<float> onChange)
        {
            VisualElement row = null; Label lbl = null; Slider slider = null; Label val = null;
            if (_paramRowTemplate != null)
            {
                row = _paramRowTemplate.Instantiate().Q("param-row");
                lbl = row?.Q<Label>("param-label");
                slider = row?.Q<Slider>("param-slider");
                val = row?.Q<Label>("param-value");
            }
            if (row == null || lbl == null || slider == null || val == null)
            {
                row = new VisualElement(); row.AddToClassList("rr-param-row");
                lbl = new Label(); lbl.AddToClassList("rr-param-label");
                slider = new Slider(); slider.AddToClassList("rr-param-slider");
                val = new Label(); val.AddToClassList("rr-param-value"); val.AddToClassList("rr-mono");
                row.Add(lbl); row.Add(slider); row.Add(val);
            }

            lbl.text = label;
            slider.lowValue = min;
            slider.highValue = max;
            slider.SetValueWithoutNotify(value);
            string Fmt(float v) => v.ToString("F2") + unit;
            val.text = Fmt(value);
            EnableClass(val, "rr-param-value--armed", armed);
            slider.RegisterValueChangedCallback(evt =>
            {
                onChange(evt.newValue);
                val.text = Fmt(evt.newValue);
            });
            _inspector.Add(row);
        }

        // トグル行。ラベル＋右寄せ Toggle。
        void AddToggleRow(string label, bool value, System.Action<bool> onChange)
        {
            var row = new VisualElement(); row.AddToClassList("rr-param-row");
            var lbl = new Label(label); lbl.AddToClassList("rr-param-label");
            var spacer = new VisualElement(); spacer.style.flexGrow = 1f;
            var toggle = new Toggle(); toggle.AddToClassList("rr-fx-toggle");
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            row.Add(lbl); row.Add(spacer); row.Add(toggle);
            _inspector.Add(row);
        }

        // 情報行（編集不可・Output 解像度用）。ラベル＋右寄せ値。呼び出し側が値 Label を保持すれば
        // 毎フレーム更新（Time/RMS 等）に使える。
        Label AddInfoRow(string label, string valueText)
        {
            var row = new VisualElement(); row.AddToClassList("rr-param-row");
            var lbl = new Label(label); lbl.AddToClassList("rr-param-label");
            var spacer = new VisualElement(); spacer.style.flexGrow = 1f;
            var val = new Label(valueText); val.AddToClassList("rr-param-value"); val.AddToClassList("rr-mono");
            row.Add(lbl); row.Add(spacer); row.Add(val);
            _inspector.Add(row);
            return val;
        }

        // FX パラメータ行（Amount/Audio Gain/Mix 用）。実際の EffectParameter にバインドし、
        // _paramRows に登録して SyncParamRows で毎フレーム追従（ドラッグ中は上書きしない）。
        // param が見つからない効果（例: Feedback に Audio Gain 無し）は "—" の情報行にフォールバック。
        void AddFxParamRow(string label, EffectParameter p, int paramIndex, bool armed)
        {
            if (p == null) { AddInfoRow(label, "—"); return; }

            VisualElement row = null; Label lbl = null; Slider slider = null; Label val = null;
            if (_paramRowTemplate != null)
            {
                row = _paramRowTemplate.Instantiate().Q("param-row");
                lbl = row?.Q<Label>("param-label");
                slider = row?.Q<Slider>("param-slider");
                val = row?.Q<Label>("param-value");
            }
            if (row == null || lbl == null || slider == null || val == null)
            {
                row = new VisualElement(); row.AddToClassList("rr-param-row");
                lbl = new Label(); lbl.AddToClassList("rr-param-label");
                slider = new Slider(); slider.AddToClassList("rr-param-slider");
                val = new Label(); val.AddToClassList("rr-param-value"); val.AddToClassList("rr-mono");
                row.Add(lbl); row.Add(slider); row.Add(val);
            }

            lbl.text = label;
            slider.lowValue = p.Min;
            slider.highValue = p.Max;
            slider.SetValueWithoutNotify(p.Value);
            val.text = p.Value.ToString("F2");
            EnableClass(val, "rr-param-value--armed", armed);

            int effectIndex = _inspectorEffect;
            slider.RegisterValueChangedCallback(evt =>
            {
                p.Value = evt.newValue;
                _hub.SelectEffect(effectIndex);
                _hub.SelectParam(paramIndex);
            });
            slider.RegisterCallback<MouseDownEvent>(_ =>
            {
                _hub.SelectEffect(effectIndex);
                _hub.SelectParam(paramIndex);
            });

            _inspector.Add(row);
            _paramRows.Add(new ParamRow { root = row, slider = slider, value = val, param = p, paramIndex = paramIndex });
        }

        // ボタン（primary/secondary/ghost・既存 .rr-btn 系トークンを使用）。
        static Button MakeButton(string text, string variant, System.Action onClick, bool enabled = true)
        {
            var btn = new Button { text = text };
            if (onClick != null) btn.clicked += onClick;
            btn.AddToClassList("rr-btn");
            btn.AddToClassList("rr-btn--" + variant);
            btn.SetEnabled(enabled);
            return btn;
        }

        void AddButtonRow(params VisualElement[] buttons)
        {
            var row = new VisualElement(); row.AddToClassList("rr-btn-row");
            foreach (var b in buttons) row.Add(b);
            _inspector.Add(row);
        }

        void AddDeselectRow() => AddButtonRow(MakeButton("Deselect", "ghost", () => _selection.Deselect()));

        // OSC アドレス表示（fx 用・CodeSurface 相当の簡易表現）。
        void AddCodeRow(string label, string code)
        {
            var wrap = new VisualElement(); wrap.AddToClassList("rr-code-row");
            var lbl = new Label(label); lbl.AddToClassList("rr-code-label");
            var box = new VisualElement(); box.AddToClassList("rr-code-box");
            var txt = new Label(code); txt.AddToClassList("rr-code-text"); txt.AddToClassList("rr-mono");
            box.Add(txt);
            wrap.Add(lbl); wrap.Add(box);
            _inspector.Add(wrap);
        }

        // BPM 行（数値入力）。将来のビート同期まで値を保持するだけ（ControlHub.Bpm）。
        void AddBpmRow(float value, System.Action<float> onChange)
        {
            var row = new VisualElement(); row.AddToClassList("rr-param-row");
            var lbl = new Label("BPM"); lbl.AddToClassList("rr-param-label");
            var spacer = new VisualElement(); spacer.style.flexGrow = 1f;
            var field = new FloatField { value = value };
            field.AddToClassList("rr-mono");
            field.style.width = 72;
            field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            row.Add(lbl); row.Add(spacer); row.Add(field);
            _inspector.Add(row);
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

        void OnDisable()
        {
            if (_quitHooked) { Application.wantsToQuit -= OnWantsToQuit; _quitHooked = false; }
            _selection.Changed -= OnSelectionChanged;
            if (_appMode != null) _appMode.ModeChanged -= OnModeChanged;
            if (_timeline != null)
            {
                _timeline.PlayStateChanged -= RefreshTransport;
                _timeline.ShortStateChanged -= RefreshShortHeld;
                _timeline.StructureChanged -= OnTimelineStructureChanged;
            }
        }

        /// <summary>ShowTimeline.StructureChanged 購読先（#36・LoadShow 成功時）。
        /// タブ/トラック行/ステップ列/Banks 一覧を全面再構築して読み込んだバンクへ追随する。</summary>
        void OnTimelineStructureChanged()
        {
            if (_timeline == null) return;
            RebuildTimelineTabs();
            RebuildSequenceTracks();
            RebuildSongSteps();
            RebuildBanksList();
            RefreshShortHeld();
            RefreshShortAssignment();
            if (_tlTotal != null) _tlTotal.text = "/ " + ShowTimeline.FormatTime(_timeline.Length);
            if (_tlLoop != null) EnableClass(_tlLoop, "rr-transport-btn--active", _timeline.Loop);
            RefreshTransport();
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
            // inspector は「fx 選択中」かつキーボード/IMGUI 側で SelectedEffect が変わった時だけ
            // 再構築（Master/他ドックビューは _inspectorEffect を更新しないため、ここを無条件に
            // すると常に不一致になり毎フレーム作り直してしまう＝GC スパイク）。
            if (_selection.Current.Kind == SelectionKind.Fx && _hub.SelectedEffect != _inspectorEffect)
                RebuildInspector();

            // per-kind ビューのライブ表示（audio-input のメーター・source-video の Time）。
            var vk = _selection.Current.Kind;
            if (vk == SelectionKind.AudioInput) SyncAudioMeters();
            else if (vk == SelectionKind.SourceVideo) SyncSourceVideoTime();
            else if (vk == SelectionKind.Track) SyncTrackAudioMeter();

            SyncFxRows();
            SyncParamRows();
        }

        // -------------------------------------------------- preview / fps
        void UpdatePreview()
        {
            if (_preview != null)
            {
                // OUTPUT warp 編集中は、見たまま調整できるよう変形後 RT を表示（無ければ Final RT）。
                Texture rt = null;
                if (_warpOutputMode && _outputWarp != null && _outputWarp.Active) rt = _outputWarp.Output;
                if (rt == null && _chain != null) rt = _chain.FinalTexture;
                if (rt != null && _preview.image != rt) _preview.image = rt;
            }

            // EMBED 分割ビュー（U6）: Input ペイン=camera UV・Output ペイン=composite（rr-preview と同じ Final RT）。
            if (_mapPreviewIn != null)
            {
                if (_sourceCamera == null) _sourceCamera = FindFirstObjectByType<SourceCamera>();
                if (_sourceCamera != null && _sourceCamera.Texture != null && _mapPreviewIn.image != _sourceCamera.Texture)
                    _mapPreviewIn.image = _sourceCamera.Texture;
            }
            if (_mapPreviewOut != null && _chain != null && _chain.FinalTexture != null && _mapPreviewOut.image != _chain.FinalTexture)
                _mapPreviewOut.image = _chain.FinalTexture;
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
                name.RegisterCallback<MouseDownEvent>(_ =>
                {
                    // FX 行クリック＝ドック項目と同じ SelectionModel 経由（§3・U2）。
                    // 同じ行の再クリックで解除＝Master へ戻る（SelectionModel.Select のトグル仕様）。
                    _hub.SelectEffect(index);
                    _selection.Select(SelectionKind.Fx, "fx" + index);
                });

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
        // ドック項目選択（Fx 含む・§3）があればそれを、無選択なら Master/Program を表示。
        void RebuildInspector()
        {
            if (_selection.Current.Kind == SelectionKind.Track) { BuildTrackInspector(_selection.Current); return; }
            if (DockSelectionActive()) { BuildDockInspector(_selection.Current); return; }
            BuildMasterInspector();
        }

        // FX パラメータ行（Amount/Audio Gain/Mix）の毎フレーム同期。fx 以外のビュー表示中は
        // _paramRows が空なのでループ 0 回＝実質ノーコスト。
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
                EnableClass(r.root, "rr-param-row--selected", r.paramIndex == _hub.SelectedParam);
            }
        }

        // Track 単一選択＝audio kind の間だけメーターを追従（LateUpdate で kind ガード済み・
        // BuildTrackInspector の audio 分岐が BuildAudioMeterRow で同じ _meterFill* を張るため使い回せる）。
        void SyncTrackAudioMeter()
        {
            var sel = _selection.Current;
            if (sel.Tracks.Count != 1) return;
            var seq = _timeline?.ActiveSequence;
            if (seq == null) return;
            int idx = sel.Tracks[0].Index;
            if (idx < 0 || idx >= seq.tracks.Count) return;
            if (seq.tracks[idx].kind != ShowTimeline.TrackKind.Audio) return;
            SyncAudioMeters();
        }

        // audio-input 表示中のみ呼ばれる（LateUpdate で kind ガード済み）。値変化時だけ高さ/文字を更新。
        void SyncAudioMeters()
        {
            if (_audioAnalyzer == null) return;
            var f = _audioAnalyzer.Features;
            SetMeterFill(_meterFillRms, f.Rms);
            SetMeterFill(_meterFillLow, f.Low);
            SetMeterFill(_meterFillMid, f.Mid);
            SetMeterFill(_meterFillHigh, f.High);

            if (_rmsValueLabel != null)
            {
                int centi = Mathf.RoundToInt(f.Rms * 100f);
                if (centi != _lastRmsCenti) { _rmsValueLabel.text = f.Rms.ToString("F2"); _lastRmsCenti = centi; }
            }
        }

        static void SetMeterFill(VisualElement fill, float v)
        {
            if (fill == null) return;
            fill.style.height = Length.Percent(Mathf.Clamp01(v) * 100f);
        }

        // source-video 表示中のみ呼ばれる。~30ms 未満の変化は無視（毎フレームの ToString を避ける）。
        void SyncSourceVideoTime()
        {
            if (_srcVideoTimeLabel == null || _sourceVideo == null) return;
            double t = _sourceVideo.Time;
            if (System.Math.Abs(t - _lastSrcVideoTime) < 0.03) return;
            _lastSrcVideoTime = t;
            _srcVideoTimeLabel.text = ShowTimeline.FormatTime(t);
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
