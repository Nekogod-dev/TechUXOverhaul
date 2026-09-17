using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using crecheng.DSPModSave;

namespace DSPTechTreeUX
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(DSPModSavePlugin.MODGUID)]
    public sealed class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string PluginGuid = "lee.dsp.techtree.ux";
        public const string PluginName = "DSP Tech Tree UX Overhaul";
        public const string PluginVersion = "1.0.0";

        internal static BepInEx.Configuration.ConfigEntry<float> LineThickness;
        internal static BepInEx.Configuration.ConfigEntry<float> MainLineThickness;

        private const int SaveDataVersion = 2;

        internal static TechTreeSavedViewState SavedViewState =
            TechTreeSavedViewState.CreateDefault();

        public void Export(BinaryWriter w)
        {
            TechTreeView activeView = TechTreeView.ActiveInstance;
            if (activeView != null)
                SavedViewState = activeView.CaptureSavedViewState();

            w.Write(SaveDataVersion);
            w.Write(SavedViewState.Page);
            w.Write(SavedViewState.MatrixFilter);
            w.Write(SavedViewState.CombatFilter);
            w.Write(SavedViewState.InfiniteOnly);

            for (int i = 0; i < 2; i++)
            {
                w.Write(SavedViewState.Zoom[i]);
                w.Write(SavedViewState.Pan[i].x);
                w.Write(SavedViewState.Pan[i].y);
            }
        }

        public void Import(BinaryReader r)
        {
            int version = r.ReadInt32();

            TechTreeSavedViewState state = TechTreeSavedViewState.CreateDefault();

            if (version >= 1)
            {
                state.Page = Mathf.Clamp(r.ReadInt32(), 0, 1);
                state.MatrixFilter = Mathf.Clamp(r.ReadInt32(), -1, 5);
                state.CombatFilter = Mathf.Clamp(r.ReadInt32(), 0, 2);

                if (version >= 2)
                    state.InfiniteOnly = r.ReadBoolean();

                for (int i = 0; i < 2; i++)
                {
                    state.Zoom[i] = Mathf.Clamp(r.ReadSingle(), 0.45f, 1.333333333f);
                    state.Pan[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                }
            }

            SavedViewState = state;

            TechTreeView activeView = TechTreeView.ActiveInstance;
            if (activeView != null)
                activeView.MarkSavedViewStatePending();
        }

        public void IntoOtherSave()
        {
            SavedViewState = TechTreeSavedViewState.CreateDefault();

            TechTreeView activeView = TechTreeView.ActiveInstance;
            if (activeView != null)
                activeView.MarkSavedViewStatePending();
        }

        private void Awake()
        {
            LineThickness = Config.Bind(
                "Appearance",
                "LineThickness",
                5.0f,
                "Thickness of ordinary technology dependency lines.");

            MainLineThickness = Config.Bind(
                "Appearance",
                "MainLineThickness",
                9.0f,
                "Thickness of the main research line from Electromagnetism to Mission Completed.");

            new Harmony(PluginGuid).PatchAll();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }
    }

    internal sealed class TechTreeSavedViewState
    {
        internal int Page;
        internal int MatrixFilter;
        internal int CombatFilter;
        internal bool InfiniteOnly;
        internal readonly float[] Zoom = new float[2];
        internal readonly Vector2[] Pan = new Vector2[2];

        internal static TechTreeSavedViewState CreateDefault()
        {
            TechTreeSavedViewState state = new TechTreeSavedViewState();
            state.Page = 0;
            state.MatrixFilter = -1;
            state.CombatFilter = 0;
            state.InfiniteOnly = false;
            state.Zoom[0] = 0.72f;
            state.Zoom[1] = 0.72f;
            state.Pan[0] = Vector2.zero;
            state.Pan[1] = Vector2.zero;
            return state;
        }
    }

    [HarmonyPatch(typeof(UITechTree), "_OnOpen")]
    internal static class UITechTree_OnOpen_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(UITechTree __instance)
        {
            TechTreeView view = __instance.GetComponent<TechTreeView>();
            if (view == null)
            {
                view = __instance.gameObject.AddComponent<TechTreeView>();
                view.Initialize(__instance);
            }

            view.Show();
        }
    }

    [HarmonyPatch(typeof(UITechTree), "_OnClose")]
    internal static class UITechTree_OnClose_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(UITechTree __instance)
        {
            TechTreeView view = __instance.GetComponent<TechTreeView>();
            if (view != null)
                view.Hide();
        }
    }

    [HarmonyPatch(typeof(UITechTree), nameof(UITechTree.OnPageChanged))]
    internal static class UITechTree_OnPageChanged_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(UITechTree __instance)
        {
            TechTreeView view = __instance.GetComponent<TechTreeView>();
            if (view != null && view.IsVisible)
                view.RefreshPage();
        }
    }

    internal sealed class TechTreeInputCatcher : MonoBehaviour, IScrollHandler, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerClickHandler
    {
        internal TechTreeView Owner;

        private bool _dragged;

        public void OnScroll(PointerEventData eventData)
        {
            if (Owner == null || eventData == null)
                return;

            Owner.HandleScrollDelta(eventData.scrollDelta.y);
            eventData.Use();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Every new pointer gesture starts as a potential click. Unity only
            // promotes it to BeginDrag after the normal EventSystem drag threshold.
            _dragged = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragged = true;

            if (eventData != null)
                eventData.Use();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Owner == null || eventData == null)
                return;

            _dragged = true;
            Owner.HandlePanDelta(eventData.delta);
            eventData.Use();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner == null || eventData == null)
                return;

            // Some Unity UI event paths can still deliver PointerClick after a drag.
            // Panning the graph should never clear the current selected technology.
            if (_dragged)
            {
                eventData.Use();
                return;
            }

            // This catcher only receives clicks that did not land on a node/panel
            // control, so a left click here is genuinely empty graph space.
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Owner.ClearSelection();
                eventData.Use();
            }
        }
    }

    internal sealed class TechTreeResearchWingGraphic : UnityEngine.UI.Graphic
    {
        internal Color BorderColor = Color.white;
        internal Color FillColor = Color.black;
        internal Color ArrowColor = new Color(0.25f, 0.75f, 1f, 1f);

        internal void SetColors(Color border, Color fill)
        {
            if (BorderColor == border && FillColor == fill)
                return;

            BorderColor = border;
            FillColor = fill;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();

            Rect r = rectTransform.rect;
            float w = r.width;
            float h = r.height;
            if (w <= 0f || h <= 0f)
                return;

            Vector2[] outer =
            {
                new Vector2(0f, 0f),
                new Vector2(w * 0.42f, 0f),
                new Vector2(w, h * 0.23f),
                new Vector2(w, h * 0.77f),
                new Vector2(w * 0.42f, h),
                new Vector2(0f, h)
            };

            float b = 4f;
            Vector2[] inner =
            {
                new Vector2(b, b),
                new Vector2(w * 0.42f - 1f, b),
                new Vector2(w - b, h * 0.23f + 3f),
                new Vector2(w - b, h * 0.77f - 3f),
                new Vector2(w * 0.42f - 1f, h - b),
                new Vector2(b, h - b)
            };

            AddConvex(vh, outer, BorderColor);
            AddConvex(vh, inner, FillColor);

        }

        private static void AddConvex(UnityEngine.UI.VertexHelper vh, Vector2[] points, Color color)
        {
            if (points == null || points.Length < 3)
                return;

            int start = vh.currentVertCount;
            for (int i = 0; i < points.Length; i++)
            {
                UIVertex v = UIVertex.simpleVert;
                v.position = points[i];
                v.color = color;
                vh.AddVert(v);
            }

            for (int i = 1; i < points.Length - 1; i++)
                vh.AddTriangle(start, start + i, start + i + 1);
        }

        private static void AddLine(
            UnityEngine.UI.VertexHelper vh,
            Vector2 a,
            Vector2 b,
            float thickness,
            Color color)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f)
                return;

            d.Normalize();
            Vector2 n = new Vector2(-d.y, d.x) * (thickness * 0.5f);

            int start = vh.currentVertCount;
            AddVert(vh, a - n, color);
            AddVert(vh, a + n, color);
            AddVert(vh, b + n, color);
            AddVert(vh, b - n, color);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private static void AddVert(UnityEngine.UI.VertexHelper vh, Vector2 position, Color color)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = position;
            v.color = color;
            vh.AddVert(v);
        }
    }

    internal sealed class TechTreeView : MonoBehaviour
    {
        private const float NodeSize = 104f;
        private const float GraphMargin = 100f;
        private const float DetailWidth = 500f;
        private const float DetailHeight = 540f;
        private const float DetailCollapsedWidth = 250f;
        private const float DetailCollapsedHeight = 46f;
        private const float SearchFilterWidth = 420f;
        private const float SearchFilterHeight = 142f;
        private const float SearchFilterTabWidth = 150f;
        private const float SearchFilterTabHeight = 24f;
        private const float MinZoom = 0.45f;
        private const float MaxZoom = 1.333333333f;

        // Extra pan room beyond the raw graph bounds. Vanilla's surrounding panels sit
        // over the graph viewport, so edge nodes need to be movable clear of them.
        private const float PanSafeLeft = 420f;
        private const float PanSafeRight = 760f;
        private const float PanSafeTop = 190f;
        private const float PanSafeBottom = 120f;

        private static int[] MatrixIds
        {
            get { return TechProto.matrixIds; }
        }

        // Language-independent vanilla main research spine:
        // Electromagnetism -> Electromagnetic Matrix -> Solar Collection ->
        // Photon Frequency Conversion -> Solar Sail Orbit System -> Ray Receiver ->
        // Planetary Ionosphere Utilization -> Dirac Inversion Mechanism ->
        // Universe Matrix -> Mission Completed.
        private static readonly int[] MainResearchLineTechIds =
        {
            1001, 1002, 1501, 1502, 1503,
            1504, 1505, 1506, 1507, 1508
        };

        private static readonly Color LockedColor = new Color(1.00f, 0.62f, 0.10f, 1f);
        private static readonly Color ReadyColor = new Color(0.20f, 0.82f, 0.36f, 1f);
        private static readonly Color CompleteColor = new Color(0.25f, 0.58f, 1.00f, 1f);
        private static readonly Color SelectedColor = new Color(1.00f, 0.95f, 0.55f, 1f);
        private static readonly Color NodeInnerColor = new Color(0.055f, 0.070f, 0.090f, 1.00f);
        private static readonly Color PanelColor = new Color(0.020f, 0.027f, 0.038f, 0.985f);
        private static readonly Color LineColor = new Color(0.52f, 0.58f, 0.66f, 0.52f);
        private static readonly Color ImplicitLineColor = new Color(0.74f, 0.56f, 0.86f, 0.42f);
        private static readonly Color PathLineColor = new Color(1.00f, 0.78f, 0.18f, 0.95f);

        private static readonly Color[] MatrixColors =
        {
            new Color(0.20f, 0.55f, 1.00f, 1f),
            new Color(0.95f, 0.25f, 0.25f, 1f),
            new Color(0.98f, 0.82f, 0.18f, 1f),
            new Color(0.68f, 0.34f, 0.90f, 1f),
            new Color(0.25f, 0.90f, 0.48f, 1f),
            new Color(0.92f, 0.94f, 1.00f, 1f)
        };

        private UITechTree _tree;
        private RectTransform _root;
        private RectTransform _viewport;
        private RectTransform _graphCanvas;
        private readonly RectTransform[] _pageGraphCanvas = new RectTransform[2];
        private TechTreeEdgeGraphic _edgeGraphic;
        private readonly TechTreeEdgeGraphic[] _pageEdgeGraphics = new TechTreeEdgeGraphic[2];
        private RectTransform _detailPanel;
        private UnityEngine.UI.Text _detailEmptyPrompt;
        private UnityEngine.UI.Image _detailIcon;
        private UnityEngine.UI.Text _detailTitle;
        private UnityEngine.UI.Text _detailLevel;
        private UnityEngine.UI.Text _detailStatus;
        private UnityEngine.UI.Image _detailAccent;
        private UnityEngine.UI.Text _detailBody;
        private GameObject _detailProgressGroup;
        private UnityEngine.UI.Image _detailProgressFill;
        private UnityEngine.UI.Text _detailProgressText;
        private UnityEngine.UI.Text _detailProgressSpeedText;
        private RectTransform _unlockRow;
        private RectTransform _researchCostRow;
        private UnityEngine.UI.Text _unlockHeading;
        private UnityEngine.UI.Text _researchCostHeading;
        private UnityEngine.UI.Button _researchButton;
        private UnityEngine.UI.Text _researchButtonText;
        private UnityEngine.UI.Text _legend;
        private RectTransform _searchFilterContent;
        private RectTransform _searchFilterTab;
        private UnityEngine.UI.Text _searchFilterTabText;
        private bool _searchFilterExpanded;
        private UnityEngine.UI.InputField _searchInput;
        private UnityEngine.UI.Button _searchClearButton;
        private UnityEngine.UI.Text _searchStatus;
        private string _searchText = string.Empty;

        private UnityEngine.UI.Button _matrixFilterButton;
        private UnityEngine.UI.Text _matrixFilterText;
        private RectTransform _matrixFilterMenu;
        private UnityEngine.UI.Button _combatFilterButton;
        private UnityEngine.UI.Text _combatFilterText;
        private RectTransform _combatFilterMenu;
        private UnityEngine.UI.Button _infiniteOnlyButton;
        private UnityEngine.UI.Text _infiniteOnlyText;

        // -1 = All, 0..4 = cumulative up to that matrix tier, 5 = White-required only.
        private int _matrixFilter = -1;

        // 0 = All, 1 = Utility combat tech only, 2 = Hide combat tech.
        private int _combatFilter = 0;
        private bool _infiniteOnly;

        private readonly HashSet<int> _utilityCombatTechIds = new HashSet<int>();
        private bool _utilityCombatTechIdsBuilt;

        private UnityEngine.Font _font;
        private Dictionary<int, NodeView> _nodes = new Dictionary<int, NodeView>();
        private List<EdgeView> _edges = new List<EdgeView>();
        private readonly Dictionary<int, NodeView>[] _pageNodes =
        {
            new Dictionary<int, NodeView>(),
            new Dictionary<int, NodeView>()
        };
        private readonly List<EdgeView>[] _pageEdges =
        {
            new List<EdgeView>(),
            new List<EdgeView>()
        };
        private readonly Dictionary<int, List<int>>[] _pageInferredParents =
        {
            new Dictionary<int, List<int>>(),
            new Dictionary<int, List<int>>()
        };
        private Dictionary<int, List<int>> _inferredParents = new Dictionary<int, List<int>>();
        private readonly bool[] _pageBuilt = new bool[2];
        private readonly Vector2[] _pageGraphSize =
        {
            new Vector2(1100f, 720f),
            new Vector2(1100f, 720f)
        };

        // Cached bounds of the currently visible nodes in graph-local coordinates.
        // x = left, y = right, z = top, w = bottom.
        private readonly Vector4[] _pageVisibleBounds =
        {
            new Vector4(0f, 1100f, 0f, -720f),
            new Vector4(0f, 1100f, 0f, -720f)
        };
        private readonly bool[] _pageVisibleBoundsValid = { false, false };
        private readonly Vector2[] _pagePan =
        {
            Vector2.zero,
            Vector2.zero
        };
        private readonly float[] _pageZoom = { 0.72f, 0.72f };
        private readonly bool[] _pageViewInitialized = new bool[2];
        private readonly float[] _pageLastStateRefresh = { -999f, -999f };
        private readonly HashSet<int> _selectedPath = new HashSet<int>();
        private readonly HashSet<long> _selectedEdges = new HashSet<long>();
        private readonly HashSet<long> _backboneEdges = new HashSet<long>();

        private int _builtPage = -1;
        private int _selectedTechId;
        private bool _visible;
        private float _zoom = 0.72f;
        private Vector2 _baseGraphSize = new Vector2(1100f, 720f);
        private float _nextStateRefreshTime;
        private bool _savedViewStatePending = true;

        internal static TechTreeView ActiveInstance { get; private set; }

        internal bool IsVisible => _visible;

        internal void Initialize(UITechTree tree)
        {
            ActiveInstance = this;
            _tree = tree;
            _font = tree.tabButtonText0 != null ? tree.tabButtonText0.font : Resources.GetBuiltinResource<UnityEngine.Font>("Arial.ttf");
            CreateRoot();
            Hide();
        }

        internal TechTreeSavedViewState CaptureSavedViewState()
        {
            TechTreeSavedViewState state = TechTreeSavedViewState.CreateDefault();

            state.Page = _tree != null
                ? Mathf.Clamp(_tree.page, 0, 1)
                : Mathf.Clamp(_builtPage, 0, 1);
            state.MatrixFilter = _matrixFilter;
            state.CombatFilter = _combatFilter;
            state.InfiniteOnly = _infiniteOnly;

            for (int i = 0; i < 2; i++)
            {
                state.Zoom[i] = _pageZoom[i];
                state.Pan[i] = _pagePan[i];
            }

            return state;
        }

        internal void MarkSavedViewStatePending()
        {
            _savedViewStatePending = true;
        }

        private void ApplySavedViewState()
        {
            if (!_savedViewStatePending)
                return;

            TechTreeSavedViewState state = Plugin.SavedViewState;
            if (state == null)
                state = TechTreeSavedViewState.CreateDefault();

            _matrixFilter = Mathf.Clamp(state.MatrixFilter, -1, 5);
            _combatFilter = Mathf.Clamp(state.CombatFilter, 0, 2);
            _infiniteOnly = state.InfiniteOnly;

            for (int i = 0; i < 2; i++)
            {
                _pageZoom[i] = Mathf.Clamp(state.Zoom[i], MinZoom, MaxZoom);
                _pagePan[i] = state.Pan[i];
            }

            if (_tree != null)
                _tree.page = Mathf.Clamp(state.Page, 0, 1);

            _savedViewStatePending = false;
        }

        internal void Show()
        {
            if (_root == null)
                CreateRoot();

            ApplySavedViewState();

            _visible = true;
            _root.gameObject.SetActive(true);
            RefreshFilterLabels();
            RefreshPage();

            HideVanillaBottomGlow();
        }

        private void HideVanillaBottomGlow()
        {
            if (_tree == null)
                return;

            Transform bottomBar = _tree.transform.Find("bottom-bar");
            if (bottomBar == null)
                return;

            UnityEngine.UI.Image image = bottomBar.GetComponent<UnityEngine.UI.Image>();
            if (image != null)
                image.enabled = false;
        }

        internal void Hide()
        {
            _visible = false;
            SetSearchFilterExpanded(false);

            if (_root != null)
                _root.gameObject.SetActive(false);

            RestoreVanillaGraphs();
        }

        private bool PointerOverQueuedTech()
        {
            if (UIRoot.instance == null ||
                UIRoot.instance.uiGame == null ||
                UIRoot.instance.uiGame.techTree == null)
                return false;

            UITechTree tree = UIRoot.instance.uiGame.techTree;
            UIResearchQueue queue = tree.resQueueUI;
            if (queue == null || queue.nodes == null)
                return false;

            for (int i = 0; i < queue.nodes.Length; i++)
            {
                UIResearchQueueNode node = queue.nodes[i];
                if (node == null || node.trans == null || !node.gameObject.activeInHierarchy)
                    continue;

                if (PointerInside(node.trans))
                    return true;
            }

            return false;
        }

        private void LateUpdate()
        {
            if (!_visible || _tree == null)
                return;

            // Mouse-only close path. Check the embedded research queue before
            // closing because Unity's UIButton right-click callback is processed
            // after this component's LateUpdate. If the pointer is over an active
            // queued-tech node, leave the click alone so vanilla can cancel it.
            if (Input.GetMouseButtonDown(1))
            {
                if (PointerOverQueuedTech())
                    return;

                if (UIRoot.instance != null && UIRoot.instance.uiGame != null)
                    UIRoot.instance.uiGame.ShutTechTree();
                return;
            }

            // UITechTree.page toggles these vanilla graph groups itself.
            // Keep them hidden while the replacement is visible.
            if (_tree.graphGroup0 != null && _tree.graphGroup0.gameObject.activeSelf)
                _tree.graphGroup0.gameObject.SetActive(false);
            if (_tree.graphGroup1 != null && _tree.graphGroup1.gameObject.activeSelf)
                _tree.graphGroup1.gameObject.SetActive(false);

            if (_builtPage != _tree.page)
                RefreshPage();

            UpdateNodeResearchWingHoverStates();
            UpdateDetailPanelPosition();
            UpdateSearchFilterFlyout();

            if (_selectedTechId != 0)
                RefreshDetailProgress();

            // Refresh state periodically; explicit actions still update immediately.
            if (Time.unscaledTime >= _nextStateRefreshTime)
            {
                _nextStateRefreshTime = Time.unscaledTime + 1.5f;
                RefreshNodeStates();
                if (_selectedTechId != 0)
                    RefreshResearchButton();
            }
        }

        internal void RefreshPage()
        {
            if (!_visible || _tree == null || GameMain.history == null)
                return;

            HideVanillaGraphs();

            int newPage = Mathf.Clamp(_tree.page, 0, 1);
            _builtPage = newPage;
            RefreshFilterLabels();
            _selectedTechId = 0;
            _selectedPath.Clear();
            _selectedEdges.Clear();

            ActivatePageCache(newPage);

            // Build each page once, then retain its hierarchy and only toggle the active root.
            if (!_pageBuilt[newPage])
            {
                BuildGraph(newPage);
                _pageBuilt[newPage] = true;
                _pageGraphSize[newPage] = _baseGraphSize;
                InitializePageView(newPage);
            }
            else
            {
                _baseGraphSize = _pageGraphSize[newPage];
                _zoom = _pageZoom[newPage];
                ApplyPageTransform(newPage);
            }

            // Rapid tab switching should not repeatedly walk every node when nothing
            // could have changed. Refresh a page if its state snapshot is old.
            if (Time.unscaledTime - _pageLastStateRefresh[newPage] >= 1.0f)
                RefreshNodeStates();

            ApplyFilters();
            RefreshEdges();
            RefreshSearchStatus();
            UpdateDetailPanelPosition();
            _nextStateRefreshTime = Time.unscaledTime + 1.5f;
            ClearDetails();
        }

        private void ActivatePageCache(int page)
        {
            for (int i = 0; i < _pageGraphCanvas.Length; i++)
            {
                if (_pageGraphCanvas[i] != null)
                    _pageGraphCanvas[i].gameObject.SetActive(i == page);
            }

            _graphCanvas = _pageGraphCanvas[page];
            _edgeGraphic = _pageEdgeGraphics[page];
            _nodes = _pageNodes[page];
            _edges = _pageEdges[page];
            _inferredParents = _pageInferredParents[page];
            _baseGraphSize = _pageGraphSize[page];
        }

        private void CreateRoot()
        {
            Transform host = _tree.graphGroup0 != null && _tree.graphGroup0.parent != null
                ? _tree.graphGroup0.parent
                : _tree.transform;

            GameObject rootObj = CreateUIObject("TechTreeUX", host);
            _root = rootObj.GetComponent<RectTransform>();

            // graphGroup0/1 are the movable vanilla graph CONTENT, not the visible
            // viewport.  Stretch our replacement across their parent instead and put
            // it at the same sibling depth as the vanilla graph.  This deliberately
            // leaves DSP's header, left research/goal panel, metadata and current-
            // upgrade panels above us, exactly as with the vanilla graph.
            Stretch(_root);
            _root.localScale = Vector3.one;
            if (_tree.graphGroup0 != null && _tree.graphGroup0.parent == host)
            {
                int sibling = _tree.graphGroup0.GetSiblingIndex();
                _root.SetSiblingIndex(sibling);
            }

            // Transparent interaction layer. Do not paint over the vanilla header / side data UI.
            UnityEngine.UI.Image rootHit = rootObj.AddComponent<UnityEngine.UI.Image>();
            rootHit.color = new Color(0f, 0f, 0f, 0.001f);
            rootHit.raycastTarget = true;

            GameObject viewportObj = CreateUIObject("Viewport", _root);
            _viewport = viewportObj.GetComponent<RectTransform>();
            Stretch(_viewport);

            UnityEngine.UI.Image viewportBg = viewportObj.AddComponent<UnityEngine.UI.Image>();
            viewportBg.color = new Color(0.012f, 0.017f, 0.025f, 0.82f);
            viewportBg.raycastTarget = true;

            // No RectMask2D here. The graph is panned/scaled directly like vanilla and
            // the surrounding DSP UI sits above this replacement root in sibling order.
            // Masking hundreds of individual line Images caused scale-dependent culling
            // where whole orthogonal segments popped in/out at different zoom levels.
            TechTreeInputCatcher inputCatcher = viewportObj.AddComponent<TechTreeInputCatcher>();
            inputCatcher.Owner = this;

            for (int page = 0; page < 2; page++)
            {
                GameObject canvasObj = CreateUIObject("GraphCanvas_" + page, _viewport);
                RectTransform canvas = canvasObj.GetComponent<RectTransform>();
                canvas.anchorMin = new Vector2(0f, 1f);
                canvas.anchorMax = new Vector2(0f, 1f);
                canvas.pivot = new Vector2(0f, 1f);
                canvas.anchoredPosition = Vector2.zero;
                canvas.gameObject.SetActive(false);
                _pageGraphCanvas[page] = canvas;
            }

            _graphCanvas = _pageGraphCanvas[0];
            _nodes = _pageNodes[0];
            _edges = _pageEdges[0];

            CreateDetailPanel();
            CreateSearchFilterFlyout();
            CreateSearchBox();
            CreateFilterBar();
            CreateLegend();
        }

        private void CreateSearchFilterFlyout()
        {
            GameObject contentObj = CreateUIObject("SearchFilterContent", _root);
            _searchFilterContent = contentObj.GetComponent<RectTransform>();
            _searchFilterContent.anchorMin = _searchFilterContent.anchorMax = new Vector2(0.5f, 1f);
            _searchFilterContent.pivot = new Vector2(0.5f, 1f);
            _searchFilterContent.sizeDelta = new Vector2(SearchFilterWidth, SearchFilterHeight);
            _searchFilterContent.anchoredPosition = new Vector2(220f, -28f);

            UnityEngine.UI.Image contentBg = contentObj.AddComponent<UnityEngine.UI.Image>();
            contentBg.color = new Color(0.015f, 0.022f, 0.032f, 0.94f);
            contentBg.raycastTarget = true;

            GameObject tabObj = CreateUIObject("SearchFilterTab", _root);
            _searchFilterTab = tabObj.GetComponent<RectTransform>();
            _searchFilterTab.anchorMin = _searchFilterTab.anchorMax = new Vector2(0.5f, 1f);
            _searchFilterTab.pivot = new Vector2(0.5f, 1f);
            _searchFilterTab.sizeDelta = new Vector2(SearchFilterTabWidth, SearchFilterTabHeight);
            _searchFilterTab.anchoredPosition = new Vector2(220f, -4f);

            UnityEngine.UI.Image tabBg = tabObj.AddComponent<UnityEngine.UI.Image>();
            tabBg.color = new Color(0.045f, 0.060f, 0.078f, 0.98f);
            tabBg.raycastTarget = true;

            UnityEngine.UI.Button tabButton = tabObj.AddComponent<UnityEngine.UI.Button>();
            tabButton.targetGraphic = tabBg;
            tabButton.onClick.AddListener(ToggleSearchFilterFlyout);

            _searchFilterTabText = CreateText(
                "Text",
                _searchFilterTab,
                12,
                UnityEngine.TextAnchor.MiddleCenter);
            Stretch(_searchFilterTabText.rectTransform);
            _searchFilterTabText.fontStyle = UnityEngine.FontStyle.Bold;
            _searchFilterTabText.raycastTarget = false;

            SetSearchFilterExpanded(false);
        }

        private void ToggleSearchFilterFlyout()
        {
            SetSearchFilterExpanded(!_searchFilterExpanded);
        }

        private void SetSearchFilterExpanded(bool expanded)
        {
            _searchFilterExpanded = expanded;

            if (_searchFilterContent != null)
                _searchFilterContent.gameObject.SetActive(expanded);

            if (!expanded)
            {
                if (_matrixFilterMenu != null)
                    _matrixFilterMenu.gameObject.SetActive(false);
                if (_combatFilterMenu != null)
                    _combatFilterMenu.gameObject.SetActive(false);
            }

            if (_searchFilterTabText != null)
                _searchFilterTabText.text = expanded
                    ? "Search / Filters ▲"
                    : "Search / Filters ▼";
        }

        private bool PointerInside(RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
                return false;

            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                camera = canvas.worldCamera;

            return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
        }

        private void UpdateNodeResearchWingHoverStates()
        {
            if (_nodes == null || _nodes.Count == 0)
                return;

            foreach (NodeView node in _nodes.Values)
            {
                if (node == null || node.Root == null || node.Proto == null)
                    continue;

                bool overNode = PointerInside(node.Root);
                bool overWing = node.ResearchWingOpen &&
                                node.ResearchWingRect != null &&
                                PointerInside(node.ResearchWingRect);
                bool selected = _selectedTechId == node.Proto.ID;

                bool shouldOpen = selected || overNode || overWing;
                bool combinedHover = overNode || overWing;

                if (node.Hovered == combinedHover &&
                    node.ResearchWingOpen == shouldOpen)
                    continue;

                node.Hovered = combinedHover;
                node.ResearchWingOpen = shouldOpen;
                RefreshNodeResearchWing(node);
            }
        }

        private void UpdateSearchFilterFlyout()
        {
            if (_searchFilterTab == null || _searchFilterContent == null)
                return;

            bool searchFocused = _searchInput != null && _searchInput.isFocused;
            bool overTab = PointerInside(_searchFilterTab);
            bool overContent = _searchFilterExpanded && PointerInside(_searchFilterContent);
            bool overMatrixMenu = _matrixFilterMenu != null &&
                                  _matrixFilterMenu.gameObject.activeSelf &&
                                  PointerInside(_matrixFilterMenu);
            bool overCombatMenu = _combatFilterMenu != null &&
                                  _combatFilterMenu.gameObject.activeSelf &&
                                  PointerInside(_combatFilterMenu);

            if (searchFocused || overTab || overContent || overMatrixMenu || overCombatMenu)
            {
                if (!_searchFilterExpanded)
                    SetSearchFilterExpanded(true);
            }
            else if (_searchFilterExpanded)
            {
                SetSearchFilterExpanded(false);
            }
        }

        private void CreateSearchBox()
        {
            GameObject searchObj = CreateUIObject("Search", _searchFilterContent);
            RectTransform searchRect = searchObj.GetComponent<RectTransform>();
            searchRect.anchorMin = searchRect.anchorMax = new Vector2(0.5f, 1f);
            searchRect.pivot = new Vector2(0.5f, 1f);
            searchRect.sizeDelta = new Vector2(390f, 36f);
            searchRect.anchoredPosition = new Vector2(0f, -6f);

            UnityEngine.UI.Image bg = searchObj.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.035f, 0.048f, 0.064f, 0.98f);
            bg.raycastTarget = true;

            _searchInput = searchObj.AddComponent<UnityEngine.UI.InputField>();
            _searchInput.targetGraphic = bg;
            _searchInput.lineType = UnityEngine.UI.InputField.LineType.SingleLine;
            _searchInput.contentType = UnityEngine.UI.InputField.ContentType.Standard;
            _searchInput.characterLimit = 80;

            UnityEngine.UI.Text searchText = CreateText("Text", searchRect, 15, UnityEngine.TextAnchor.MiddleLeft);
            RectTransform textRect = searchText.rectTransform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(12f, 3f);
            textRect.offsetMax = new Vector2(-42f, -3f);
            searchText.supportRichText = false;
            searchText.raycastTarget = false;
            _searchInput.textComponent = searchText;

            UnityEngine.UI.Text placeholder = CreateText("Placeholder", searchRect, 15, UnityEngine.TextAnchor.MiddleLeft);
            RectTransform placeholderRect = placeholder.rectTransform;
            Stretch(placeholderRect);
            placeholderRect.offsetMin = new Vector2(12f, 3f);
            placeholderRect.offsetMax = new Vector2(-42f, -3f);
            placeholder.text = "Search technologies...";
            placeholder.color = new Color(0.58f, 0.66f, 0.74f, 0.78f);
            placeholder.fontStyle = UnityEngine.FontStyle.Italic;
            placeholder.raycastTarget = false;
            _searchInput.placeholder = placeholder;

            GameObject clearObj = CreateUIObject("Clear", searchRect);
            RectTransform clearRect = clearObj.GetComponent<RectTransform>();
            clearRect.anchorMin = clearRect.anchorMax = new Vector2(1f, 0.5f);
            clearRect.pivot = new Vector2(1f, 0.5f);
            clearRect.sizeDelta = new Vector2(32f, 30f);
            clearRect.anchoredPosition = new Vector2(-3f, 0f);

            UnityEngine.UI.Image clearBg = clearObj.AddComponent<UnityEngine.UI.Image>();
            clearBg.color = new Color(0.11f, 0.14f, 0.18f, 0.95f);
            _searchClearButton = clearObj.AddComponent<UnityEngine.UI.Button>();
            _searchClearButton.targetGraphic = clearBg;
            _searchClearButton.onClick.AddListener(ClearSearch);

            UnityEngine.UI.Text clearText = CreateText("X", clearRect, 16, UnityEngine.TextAnchor.MiddleCenter);
            Stretch(clearText.rectTransform);
            clearText.text = "×";
            clearText.raycastTarget = false;
            clearText.color = new Color(0.80f, 0.86f, 0.92f, 1f);

            _searchStatus = CreateText("SearchStatus", _searchFilterContent, 12, UnityEngine.TextAnchor.UpperCenter);
            RectTransform statusRect = _searchStatus.rectTransform;
            statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.sizeDelta = new Vector2(390f, 22f);
            statusRect.anchoredPosition = new Vector2(0f, -114f);
            _searchStatus.color = new Color(0.68f, 0.76f, 0.84f, 0.95f);
            _searchStatus.text = "";

            _searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        private void CreateFilterBar()
        {
            GameObject matrixObj = CreateUIObject("MatrixFilter", _searchFilterContent);
            RectTransform matrixRect = matrixObj.GetComponent<RectTransform>();
            matrixRect.anchorMin = matrixRect.anchorMax = new Vector2(0.5f, 1f);
            matrixRect.pivot = new Vector2(0.5f, 1f);
            matrixRect.sizeDelta = new Vector2(190f, 30f);
            matrixRect.anchoredPosition = new Vector2(-102f, -46f);

            UnityEngine.UI.Image matrixBg = matrixObj.AddComponent<UnityEngine.UI.Image>();
            matrixBg.color = new Color(0.045f, 0.060f, 0.078f, 0.96f);
            _matrixFilterButton = matrixObj.AddComponent<UnityEngine.UI.Button>();
            _matrixFilterButton.targetGraphic = matrixBg;
            _matrixFilterButton.onClick.AddListener(ToggleMatrixFilterMenu);

            _matrixFilterText = CreateText("Text", matrixRect, 13, UnityEngine.TextAnchor.MiddleCenter);
            Stretch(_matrixFilterText.rectTransform);
            _matrixFilterText.fontStyle = UnityEngine.FontStyle.Bold;
            _matrixFilterText.raycastTarget = false;

            GameObject combatObj = CreateUIObject("CombatFilter", _searchFilterContent);
            RectTransform combatRect = combatObj.GetComponent<RectTransform>();
            combatRect.anchorMin = combatRect.anchorMax = new Vector2(0.5f, 1f);
            combatRect.pivot = new Vector2(0.5f, 1f);
            combatRect.sizeDelta = new Vector2(190f, 30f);
            combatRect.anchoredPosition = new Vector2(102f, -46f);

            UnityEngine.UI.Image combatBg = combatObj.AddComponent<UnityEngine.UI.Image>();
            combatBg.color = new Color(0.045f, 0.060f, 0.078f, 0.96f);
            _combatFilterButton = combatObj.AddComponent<UnityEngine.UI.Button>();
            _combatFilterButton.targetGraphic = combatBg;
            _combatFilterButton.onClick.AddListener(ToggleCombatFilterMenu);

            _combatFilterText = CreateText("Text", combatRect, 13, UnityEngine.TextAnchor.MiddleCenter);
            Stretch(_combatFilterText.rectTransform);
            _combatFilterText.fontStyle = UnityEngine.FontStyle.Bold;
            _combatFilterText.raycastTarget = false;

            GameObject infiniteObj = CreateUIObject("InfiniteOnly", _searchFilterContent);
            RectTransform infiniteRect = infiniteObj.GetComponent<RectTransform>();
            infiniteRect.anchorMin = infiniteRect.anchorMax = new Vector2(0.5f, 1f);
            infiniteRect.pivot = new Vector2(0.5f, 1f);
            infiniteRect.sizeDelta = new Vector2(190f, 26f);
            infiniteRect.anchoredPosition = new Vector2(0f, -80f);

            UnityEngine.UI.Image infiniteBg = infiniteObj.AddComponent<UnityEngine.UI.Image>();
            infiniteBg.color = new Color(0.045f, 0.060f, 0.078f, 0.96f);
            _infiniteOnlyButton = infiniteObj.AddComponent<UnityEngine.UI.Button>();
            _infiniteOnlyButton.targetGraphic = infiniteBg;
            _infiniteOnlyButton.onClick.AddListener(ToggleInfiniteOnly);

            _infiniteOnlyText = CreateText(
                "Text",
                infiniteRect,
                13,
                UnityEngine.TextAnchor.MiddleCenter);
            Stretch(_infiniteOnlyText.rectTransform);
            _infiniteOnlyText.fontStyle = UnityEngine.FontStyle.Bold;
            _infiniteOnlyText.raycastTarget = false;

            _matrixFilterMenu = CreateFilterMenu(
                "MatrixFilterMenu",
                -102f,
                new string[]
                {
                    "All",
                    "Blue",
                    "≤ Red",
                    "≤ Yellow",
                    "≤ Purple",
                    "≤ Green",
                    "White"
                },
                true);

            _combatFilterMenu = CreateFilterMenu(
                "CombatFilterMenu",
                102f,
                new string[]
                {
                    "All",
                    "Utility",
                    "Hidden"
                },
                false);

            RefreshFilterLabels();
        }

        private RectTransform CreateFilterMenu(
            string name,
            float x,
            string[] labels,
            bool matrixMenu)
        {
            const float optionHeight = 26f;

            GameObject menuObj = CreateUIObject(name, _searchFilterContent);
            RectTransform menuRect = menuObj.GetComponent<RectTransform>();
            menuRect.anchorMin = menuRect.anchorMax = new Vector2(0.5f, 1f);
            menuRect.pivot = new Vector2(0.5f, 1f);
            menuRect.sizeDelta = new Vector2(190f, labels.Length * optionHeight + 4f);
            menuRect.anchoredPosition = new Vector2(x, -78f);

            UnityEngine.UI.Image menuBg = menuObj.AddComponent<UnityEngine.UI.Image>();
            menuBg.color = new Color(0.018f, 0.026f, 0.038f, 0.995f);
            menuBg.raycastTarget = true;

            for (int i = 0; i < labels.Length; i++)
            {
                int value = matrixMenu ? i - 1 : i;

                GameObject optionObj = CreateUIObject("Option_" + i, menuRect);
                RectTransform optionRect = optionObj.GetComponent<RectTransform>();
                optionRect.anchorMin = optionRect.anchorMax = new Vector2(0.5f, 1f);
                optionRect.pivot = new Vector2(0.5f, 1f);
                optionRect.sizeDelta = new Vector2(184f, optionHeight - 1f);
                optionRect.anchoredPosition = new Vector2(0f, -2f - i * optionHeight);

                UnityEngine.UI.Image optionBg = optionObj.AddComponent<UnityEngine.UI.Image>();
                optionBg.color = new Color(0.050f, 0.066f, 0.086f, 0.99f);

                UnityEngine.UI.Button optionButton = optionObj.AddComponent<UnityEngine.UI.Button>();
                optionButton.targetGraphic = optionBg;

                if (matrixMenu)
                    optionButton.onClick.AddListener(delegate { SetMatrixFilter(value); });
                else
                    optionButton.onClick.AddListener(delegate { SetCombatFilter(value); });

                UnityEngine.UI.Text optionText = CreateText(
                    "Text",
                    optionRect,
                    13,
                    UnityEngine.TextAnchor.MiddleCenter);
                Stretch(optionText.rectTransform);
                optionText.text = labels[i];
                optionText.raycastTarget = false;
            }

            menuRect.SetAsLastSibling();
            menuObj.SetActive(false);
            return menuRect;
        }

        private void ToggleMatrixFilterMenu()
        {
            if (_matrixFilterMenu == null)
                return;

            bool show = !_matrixFilterMenu.gameObject.activeSelf;

            if (_combatFilterMenu != null)
                _combatFilterMenu.gameObject.SetActive(false);

            _matrixFilterMenu.gameObject.SetActive(show);
            if (show)
                _matrixFilterMenu.SetAsLastSibling();
        }

        private void ToggleCombatFilterMenu()
        {
            if (_combatFilterMenu == null)
                return;

            bool show = !_combatFilterMenu.gameObject.activeSelf;

            if (_matrixFilterMenu != null)
                _matrixFilterMenu.gameObject.SetActive(false);

            _combatFilterMenu.gameObject.SetActive(show);
            if (show)
                _combatFilterMenu.SetAsLastSibling();
        }

        private void ToggleInfiniteOnly()
        {
            if (_builtPage != 1)
                return;

            _infiniteOnly = !_infiniteOnly;
            RefreshFilterLabels();
            ApplyFilters();
        }

        private void SetMatrixFilter(int value)
        {
            _matrixFilter = Mathf.Clamp(value, -1, 5);

            if (_matrixFilterMenu != null)
                _matrixFilterMenu.gameObject.SetActive(false);

            RefreshFilterLabels();
            ApplyFilters();
        }

        private void SetCombatFilter(int value)
        {
            _combatFilter = Mathf.Clamp(value, 0, 2);

            if (_combatFilterMenu != null)
                _combatFilterMenu.gameObject.SetActive(false);

            RefreshFilterLabels();
            ApplyFilters();
        }

        private void RefreshFilterLabels()
        {
            if (_matrixFilterText != null)
            {
                switch (_matrixFilter)
                {
                    case 0: _matrixFilterText.text = "Matrix: Blue ▼"; break;
                    case 1: _matrixFilterText.text = "Matrix: ≤ Red ▼"; break;
                    case 2: _matrixFilterText.text = "Matrix: ≤ Yellow ▼"; break;
                    case 3: _matrixFilterText.text = "Matrix: ≤ Purple ▼"; break;
                    case 4: _matrixFilterText.text = "Matrix: ≤ Green ▼"; break;
                    case 5: _matrixFilterText.text = "Matrix: White ▼"; break;
                    default: _matrixFilterText.text = "Matrix: All ▼"; break;
                }
            }

            if (_combatFilterText != null)
            {
                switch (_combatFilter)
                {
                    case 1: _combatFilterText.text = "Combat: Utility ▼"; break;
                    case 2: _combatFilterText.text = "Combat: Hidden ▼"; break;
                    default: _combatFilterText.text = "Combat: All ▼"; break;
                }
            }

            bool onUpgradesPage = _builtPage == 1;

            if (_infiniteOnlyButton != null)
                _infiniteOnlyButton.interactable = onUpgradesPage;

            if (_infiniteOnlyText != null)
            {
                _infiniteOnlyText.text =
                    _infiniteOnly ? "Infinite only: ON" : "Infinite only: OFF";
                _infiniteOnlyText.color = onUpgradesPage
                    ? Color.white
                    : new Color(0.45f, 0.48f, 0.52f, 1f);
            }
        }

        private void ApplyFilters()
        {
            if (_builtPage < 0 || _nodes == null)
                return;

            // If the current selection becomes hidden, clear it rather than retaining
            // an invisible selection/path/detail panel.
            if (_selectedTechId != 0)
            {
                NodeView selectedNode;
                if (!_nodes.TryGetValue(_selectedTechId, out selectedNode) ||
                    selectedNode == null ||
                    !NodePassesFilters(selectedNode.Proto))
                {
                    _selectedTechId = 0;
                    _selectedPath.Clear();
                    _selectedEdges.Clear();
                    ClearDetails();
                }
            }

            // Always restore the canonical vanilla-derived positions first. Filter
            // compaction is temporary and must never accumulate across filter changes.
            foreach (NodeView node in _nodes.Values)
            {
                if (node == null || node.Root == null)
                    continue;

                node.Root.anchoredPosition = node.OriginalPosition;
                node.Root.gameObject.SetActive(NodePassesFilters(node.Proto));
            }

            // The normal graph keeps vanilla-derived positions. Two filtered views
            // deliberately compact:
            //  * combat filtering removes empty rows only;
            //  * White-matrix upgrades become a focused late-game research layout,
            //    preserving one upgrade family per row and stage order left-to-right.
            if (_builtPage == 1 && _matrixFilter == 5)
                CompactWhiteUpgradeFamilies();
            else if (_combatFilter != 0)
                CompactVisibleRows();

            CacheVisibleBounds(_builtPage);

            // Moving nodes means the batched dependency mesh must be regenerated from
            // the new temporary positions. This happens only when a filter/page changes,
            // not every frame.
            RebuildEdgeGeometry();

            // A filter is a view change, so frame the surviving content immediately.
            // This is especially useful for the White-only endgame view.
            if (_matrixFilter >= 0 || _combatFilter != 0 || (_builtPage == 1 && _infiniteOnly))
                CenterViewOnVisibleNodes(_builtPage);

            RefreshNodeStates();
            RefreshEdges();
            RefreshSearchStatus();
        }

        private void CompactWhiteUpgradeFamilies()
        {
            if (_builtPage != 1 || _nodes == null)
                return;

            List<float> visibleRows = new List<float>();
            float minOriginalX = float.MaxValue;

            foreach (NodeView node in _nodes.Values)
            {
                if (node == null || node.Root == null || !node.Root.gameObject.activeSelf)
                    continue;

                AddUniqueRow(visibleRows, node.OriginalPosition.y);
                minOriginalX = Mathf.Min(minOriginalX, node.OriginalPosition.x);
            }

            if (visibleRows.Count == 0)
                return;

            visibleRows.Sort((a, b) => b.CompareTo(a));

            // Leave enough headroom for the level label plus the separate percentage
            // label above it. This is deliberately a little roomier than the normal tree.
            float rowGap = Mathf.Max(
                GetTypicalRowGapForPage(1),
                NodeSize + 132f);

            float stageGap = Mathf.Max(
                GetTypicalColumnGapForPage(1),
                NodeSize + 110f);

            int rowsPerColumn = (visibleRows.Count + 1) / 2;

            // Determine how wide the widest family is, then place the second block far
            // enough away that its stages cannot overlap the first block.
            int maxFamilyCount = 1;
            for (int r = 0; r < visibleRows.Count; r++)
            {
                int count = 0;
                foreach (NodeView node in _nodes.Values)
                {
                    if (node == null || node.Root == null || !node.Root.gameObject.activeSelf)
                        continue;

                    if (Mathf.Abs(node.OriginalPosition.y - visibleRows[r]) < 0.1f)
                        count++;
                }

                if (count > maxFamilyCount)
                    maxFamilyCount = count;
            }

            float familyWidth =
                (maxFamilyCount - 1) * stageGap + NodeSize;

            float blockGap = NodeSize + 180f;
            float secondColumnX = minOriginalX + familyWidth + blockGap;
            float topY = visibleRows[0];

            for (int row = 0; row < visibleRows.Count; row++)
            {
                float originalY = visibleRows[row];
                List<NodeView> family = new List<NodeView>();

                foreach (NodeView node in _nodes.Values)
                {
                    if (node == null || node.Root == null || !node.Root.gameObject.activeSelf)
                        continue;

                    if (Mathf.Abs(node.OriginalPosition.y - originalY) < 0.1f)
                        family.Add(node);
                }

                family.Sort((a, b) =>
                    a.OriginalPosition.x.CompareTo(b.OriginalPosition.x));

                int block = row / rowsPerColumn;
                int rowInBlock = row % rowsPerColumn;

                float startX = block == 0 ? minOriginalX : secondColumnX;
                float y = topY - rowInBlock * rowGap;

                for (int col = 0; col < family.Count; col++)
                {
                    family[col].Root.anchoredPosition =
                        new Vector2(startX + col * stageGap, y);
                }
            }
        }

        private float GetTypicalRowGapForPage(int page)
        {
            if (page < 0 || page >= 2)
                return 0f;

            List<float> rows = new List<float>();

            foreach (NodeView node in _pageNodes[page].Values)
            {
                if (node == null)
                    continue;

                AddUniqueRow(rows, node.OriginalPosition.y);
            }

            if (rows.Count < 2)
                return 0f;

            rows.Sort((a, b) => b.CompareTo(a));
            return GetTypicalRowGap(rows);
        }

        private float GetTypicalColumnGapForPage(int page)
        {
            if (page < 0 || page >= 2)
                return 0f;

            List<float> gaps = new List<float>();
            List<float> rows = new List<float>();

            foreach (NodeView node in _pageNodes[page].Values)
            {
                if (node == null)
                    continue;

                AddUniqueRow(rows, node.OriginalPosition.y);
            }

            for (int r = 0; r < rows.Count; r++)
            {
                List<float> xs = new List<float>();

                foreach (NodeView node in _pageNodes[page].Values)
                {
                    if (node == null)
                        continue;

                    if (Mathf.Abs(node.OriginalPosition.y - rows[r]) < 0.1f)
                        xs.Add(node.OriginalPosition.x);
                }

                if (xs.Count < 2)
                    continue;

                xs.Sort();

                for (int i = 0; i + 1 < xs.Count; i++)
                {
                    float gap = xs[i + 1] - xs[i];
                    if (gap > NodeSize + 1f)
                        gaps.Add(gap);
                }
            }

            if (gaps.Count == 0)
                return 0f;

            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private void CompactVisibleRows()
        {
            List<float> allRows = new List<float>();
            List<float> visibleRows = new List<float>();

            foreach (NodeView node in _nodes.Values)
            {
                if (node == null || node.Root == null)
                    continue;

                AddUniqueRow(allRows, node.OriginalPosition.y);

                if (node.Root.gameObject.activeSelf)
                    AddUniqueRow(visibleRows, node.OriginalPosition.y);
            }

            if (visibleRows.Count < 2 || allRows.Count < 2)
                return;

            // Top-to-bottom: graph-local Y becomes more negative as we move down.
            allRows.Sort((a, b) => b.CompareTo(a));
            visibleRows.Sort((a, b) => b.CompareTo(a));

            float typicalGap = GetTypicalRowGap(allRows);
            if (typicalGap < 1f)
                return;

            Dictionary<float, int> rowIndex = new Dictionary<float, int>();
            for (int i = 0; i < allRows.Count; i++)
                rowIndex[allRows[i]] = i;

            Dictionary<float, float> compactY = new Dictionary<float, float>();
            float previousOriginal = visibleRows[0];
            float previousCompact = previousOriginal;
            compactY[previousOriginal] = previousCompact;

            for (int i = 1; i < visibleRows.Count; i++)
            {
                float currentOriginal = visibleRows[i];

                int previousIndex = FindRowIndex(allRows, previousOriginal);
                int currentIndex = FindRowIndex(allRows, currentOriginal);
                int rowsSkipped = Mathf.Max(0, currentIndex - previousIndex - 1);

                float originalGap = previousOriginal - currentOriginal;
                // Collapse only the vertical space actually occupied by hidden rows.
                // This removes holes such as Reconstruction Marking while preserving any
                // deliberate extra separation between larger upgrade sections.
                float newGap = rowsSkipped > 0
                    ? Mathf.Max(typicalGap, originalGap - rowsSkipped * typicalGap)
                    : originalGap;

                previousCompact -= newGap;
                compactY[currentOriginal] = previousCompact;
                previousOriginal = currentOriginal;
            }

            foreach (NodeView node in _nodes.Values)
            {
                if (node == null || node.Root == null || !node.Root.gameObject.activeSelf)
                    continue;

                float newY;
                if (!TryGetRowValue(compactY, node.OriginalPosition.y, out newY))
                    continue;

                node.Root.anchoredPosition = new Vector2(node.OriginalPosition.x, newY);
            }
        }

        private static void AddUniqueRow(List<float> rows, float y)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (Mathf.Abs(rows[i] - y) < 0.1f)
                    return;
            }

            rows.Add(y);
        }

        private static int FindRowIndex(List<float> rows, float y)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (Mathf.Abs(rows[i] - y) < 0.1f)
                    return i;
            }

            return -1;
        }

        private static bool TryGetRowValue(Dictionary<float, float> values, float key, out float value)
        {
            foreach (KeyValuePair<float, float> pair in values)
            {
                if (Mathf.Abs(pair.Key - key) < 0.1f)
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = key;
            return false;
        }

        private static float GetTypicalRowGap(List<float> rows)
        {
            List<float> gaps = new List<float>();

            for (int i = 0; i + 1 < rows.Count; i++)
            {
                float gap = rows[i] - rows[i + 1];
                if (gap > 1f)
                    gaps.Add(gap);
            }

            if (gaps.Count == 0)
                return 0f;

            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private void CacheVisibleBounds(int page)
        {
            if (page < 0 || page >= 2)
                return;

            bool any = false;
            float left = float.MaxValue;
            float right = float.MinValue;
            float top = float.MinValue;
            float bottom = float.MaxValue;

            foreach (NodeView node in _pageNodes[page].Values)
            {
                if (node == null || node.Root == null || !node.Root.gameObject.activeSelf)
                    continue;

                Vector2 p = node.Root.anchoredPosition;
                left = Mathf.Min(left, p.x);
                right = Mathf.Max(right, p.x + NodeSize);
                top = Mathf.Max(top, p.y);
                bottom = Mathf.Min(bottom, p.y - NodeSize);
                any = true;
            }

            if (!any)
            {
                _pageVisibleBoundsValid[page] = false;
                return;
            }

            _pageVisibleBounds[page] = new Vector4(left, right, top, bottom);
            _pageVisibleBoundsValid[page] = true;
        }

        private void CenterViewOnVisibleNodes(int page)
        {
            if (page < 0 || page >= 2 || _viewport == null || !_pageVisibleBoundsValid[page])
                return;

            Vector4 bounds = _pageVisibleBounds[page];
            Vector2 localCenter = new Vector2(
                (bounds.x + bounds.y) * 0.5f,
                (bounds.z + bounds.w) * 0.5f);

            Vector2 viewportCenter = new Vector2(
                _viewport.rect.width * 0.5f,
                -_viewport.rect.height * 0.5f);

            float zoom = _pageZoom[page];
            _pagePan[page] = viewportCenter - localCenter * zoom;
            ApplyPageTransform(page);
        }

        private void RebuildEdgeGeometry()
        {
            if (_edgeGraphic == null || _edges == null)
                return;

            List<EdgeView> oldEdges = new List<EdgeView>(_edges);
            List<EdgeView> rebuilt = new List<EdgeView>(oldEdges.Count);

            _edgeGraphic.ClearSegments();

            for (int i = 0; i < oldEdges.Count; i++)
            {
                EdgeView oldEdge = oldEdges[i];

                NodeView fromNode;
                NodeView toNode;
                if (!_nodes.TryGetValue(oldEdge.FromId, out fromNode) ||
                    !_nodes.TryGetValue(oldEdge.ToId, out toNode) ||
                    fromNode == null || toNode == null ||
                    fromNode.Root == null || toNode.Root == null)
                    continue;

                EdgeView edge = CreateEdge(
                    oldEdge.FromId,
                    oldEdge.ToId,
                    fromNode.Root.anchoredPosition,
                    toNode.Root.anchoredPosition,
                    oldEdge.Implicit,
                    oldEdge.Inferred);

                rebuilt.Add(edge);
            }

            _edges.Clear();
            _edges.AddRange(rebuilt);
            _edgeGraphic.SetVerticesDirty();
        }

        private bool NodePassesFilters(TechProto proto)
        {
            if (proto == null)
                return false;

            if (!PassesMatrixFilter(proto))
                return false;

            if (_builtPage == 1 &&
                _infiniteOnly &&
                !IsInfiniteUpgrade(proto))
                return false;

            if (_combatFilter == 0 || !IsCombatProto(proto))
                return true;

            if (_combatFilter == 2)
                return false;

            // Utility mode keeps only the combat technologies needed to reach the
            // Battlefield Analysis Base. Non-combat prerequisites (Drive Engine etc.)
            // are never hidden by the combat filter in the first place.
            EnsureUtilityCombatTechIds();
            return _utilityCombatTechIds.Contains(proto.ID);
        }

        private bool PassesMatrixFilter(TechProto proto)
        {
            if (_matrixFilter < 0)
                return true;

            int tier = GetEffectiveMatrixTier(proto.ID, new HashSet<int>());

            // Matrix-less/root technologies belong to every cumulative early-game view.
            if (_matrixFilter <= 4)
                return tier <= _matrixFilter;

            // "White" is deliberately distinct from All: show technologies whose
            // effective prerequisite tier actually reaches Universe Matrix.
            return tier == 5;
        }

        private static bool IsInfiniteUpgrade(TechProto proto)
        {
            if (proto == null || proto.ID <= 1999)
                return false;

            // DSP 0.10.34 uses MaxLevel > 20 as its repeatable/infinite-upgrade
            // special case in ACH_UnlockAllTech.
            return proto.MaxLevel > 20;
        }

        private bool IsCombatProto(TechProto proto)
        {
            if (proto == null)
                return false;

            // Vanilla combat technologies occupy the 18xx block.
            if (proto.ID < 2000)
                return proto.ID >= 1800 && proto.ID < 1900;

            // Combat-only upgrades use functions 61 through 86 in DSP 0.10.34.
            // Be deliberately conservative here: some upgrade records may combine a
            // normal progression effect (functions 1-44) with a combat-side effect.
            // Those mixed-purpose upgrades must remain visible (e.g. Veins Utilization,
            // core/mecha progression), so classify an upgrade as combat only when it
            // contains combat functions and no ordinary functions.
            if (proto.UnlockFunctions == null)
                return false;

            bool hasCombatFunction = false;
            bool hasOrdinaryFunction = false;

            for (int i = 0; i < proto.UnlockFunctions.Length; i++)
            {
                int function = proto.UnlockFunctions[i];

                if (function >= 61 && function <= 86)
                    hasCombatFunction = true;
                else if (function >= 1 && function <= 44)
                    hasOrdinaryFunction = true;
            }

            return hasCombatFunction && !hasOrdinaryFunction;
        }

        private void EnsureUtilityCombatTechIds()
        {
            if (_utilityCombatTechIdsBuilt)
                return;

            _utilityCombatTechIdsBuilt = true;
            _utilityCombatTechIds.Clear();

            TechProto[] all = LDB.techs.dataArray;
            if (all == null)
                return;

            for (int i = 0; i < all.Length; i++)
            {
                TechProto proto = all[i];
                if (proto == null || proto.ID < 1800 || proto.ID >= 1900)
                    continue;

                if (!UnlocksBattleBase(proto))
                    continue;

                CollectCombatAncestors(proto.ID, _utilityCombatTechIds);
            }
        }

        private bool UnlocksBattleBase(TechProto proto)
        {
            if (proto == null || proto.unlockRecipeArray == null)
                return false;

            for (int i = 0; i < proto.unlockRecipeArray.Length; i++)
            {
                RecipeProto recipe = proto.unlockRecipeArray[i];
                if (recipe == null || recipe.Results == null)
                    continue;

                for (int r = 0; r < recipe.Results.Length; r++)
                {
                    ItemProto item = LDB.items.Select(recipe.Results[r]);
                    if (item != null && item.prefabDesc != null && item.prefabDesc.isBattleBase)
                        return true;
                }
            }

            return false;
        }

        private void CollectCombatAncestors(int techId, HashSet<int> result)
        {
            if (!result.Add(techId))
                return;

            TechProto proto = LDB.techs.Select(techId);
            if (proto == null)
                return;

            CollectCombatAncestorArray(proto.PreTechs, result);
            CollectCombatAncestorArray(proto.PreTechsImplicit, result);
        }

        private void CollectCombatAncestorArray(int[] ids, HashSet<int> result)
        {
            if (ids == null)
                return;

            for (int i = 0; i < ids.Length; i++)
            {
                TechProto parent = LDB.techs.Select(ids[i]);
                if (parent == null || !IsCombatProto(parent))
                    continue;

                CollectCombatAncestors(parent.ID, result);
            }
        }

        private void ClearSearch()
        {
            if (_searchInput != null)
                _searchInput.text = "";
            else
            {
                _searchText = "";
                RefreshSearchStatus();
                RefreshNodeStates();
            }
        }

        private void OnSearchChanged(string value)
        {
            _searchText = string.IsNullOrEmpty(value) ? "" : value.Trim();
            RefreshSearchStatus();
            RefreshNodeStates();
        }

        private void RefreshSearchStatus()
        {
            if (_searchStatus == null)
                return;

            if (string.IsNullOrEmpty(_searchText))
            {
                _searchStatus.text = "";
                return;
            }

            int matches = 0;
            foreach (NodeView node in _nodes.Values)
            {
                if (node != null && NodePassesFilters(node.Proto) && NodeMatchesSearch(node))
                    matches++;
            }

            _searchStatus.text = matches == 1 ? "1 match" : (matches + " matches");
        }

        private bool NodeMatchesSearch(NodeView node)
        {
            if (node == null || node.Proto == null)
                return false;
            if (string.IsNullOrEmpty(_searchText))
                return true;

            TechProto proto = node.Proto;

            if (ContainsSearch(proto.name))
                return true;

            // Search unlocked recipe names and, more importantly, the output item names.
            // e.g. "glass" should match Automatic Metallurgy because that tech unlocks Glass.
            if (proto.unlockRecipeArray != null)
            {
                for (int i = 0; i < proto.unlockRecipeArray.Length; i++)
                {
                    RecipeProto recipe = proto.unlockRecipeArray[i];
                    if (recipe == null)
                        continue;

                    if (ContainsSearch(recipe.name))
                        return true;

                    if (recipe.Results == null)
                        continue;

                    for (int r = 0; r < recipe.Results.Length; r++)
                    {
                        ItemProto item = LDB.items.Select(recipe.Results[r]);
                        if (item != null && ContainsSearch(item.name))
                            return true;
                    }
                }
            }

            return false;
        }

        private bool ContainsSearch(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CreateDetailPanel()
        {
            GameObject detailObj = CreateUIObject("Details", _root);
            _detailPanel = detailObj.GetComponent<RectTransform>();
            _detailPanel.anchorMin = new Vector2(1f, 1f);
            _detailPanel.anchorMax = new Vector2(1f, 1f);
            _detailPanel.pivot = new Vector2(1f, 1f);
            _detailPanel.sizeDelta = new Vector2(DetailWidth, DetailHeight);
            UpdateDetailPanelPosition();

            UnityEngine.UI.Image bg = detailObj.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.025f, 0.033f, 0.045f, 0.985f);
            bg.raycastTarget = true;

            _detailAccent = CreateImage("Accent", _detailPanel, new Vector2(0f, 4f));
            RectTransform accentRect = _detailAccent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.offsetMin = new Vector2(0f, -4f);
            accentRect.offsetMax = Vector2.zero;
            _detailAccent.color = SelectedColor;
            _detailAccent.raycastTarget = false;

            // Compact header: enough room for identity/state, but not a quarter of the panel.
            GameObject headerObj = CreateUIObject("Header", _detailPanel);
            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -94f);
            headerRect.offsetMax = new Vector2(0f, -4f);

            UnityEngine.UI.Image headerBg = headerObj.AddComponent<UnityEngine.UI.Image>();
            headerBg.color = new Color(0.045f, 0.058f, 0.075f, 0.96f);
            headerBg.raycastTarget = false;

            _detailIcon = CreateImage("Icon", headerRect, new Vector2(66f, 66f));
            RectTransform iconRect = _detailIcon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.pivot = new Vector2(0f, 1f);
            iconRect.anchoredPosition = new Vector2(18f, -13f);
            _detailIcon.preserveAspect = true;

            _detailTitle = CreateText("Title", headerRect, 20, UnityEngine.TextAnchor.UpperLeft);
            RectTransform titleRect = _detailTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(100f, -43f);
            titleRect.offsetMax = new Vector2(-18f, -12f);
            _detailTitle.fontStyle = UnityEngine.FontStyle.Bold;
            _detailTitle.horizontalOverflow = UnityEngine.HorizontalWrapMode.Wrap;

            _detailLevel = CreateText("Level", headerRect, 13, UnityEngine.TextAnchor.UpperLeft);
            RectTransform levelRect = _detailLevel.rectTransform;
            levelRect.anchorMin = new Vector2(0f, 1f);
            levelRect.anchorMax = new Vector2(1f, 1f);
            levelRect.pivot = new Vector2(0f, 1f);
            levelRect.offsetMin = new Vector2(100f, -73f);
            levelRect.offsetMax = new Vector2(-145f, -49f);
            _detailLevel.color = new Color(0.72f, 0.82f, 0.93f, 1f);

            _detailStatus = CreateText("Status", headerRect, 12, UnityEngine.TextAnchor.MiddleCenter);
            RectTransform statusRect = _detailStatus.rectTransform;
            statusRect.anchorMin = statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(1f, 1f);
            statusRect.sizeDelta = new Vector2(128f, 25f);
            statusRect.anchoredPosition = new Vector2(-16f, -55f);
            _detailStatus.fontStyle = UnityEngine.FontStyle.Bold;

            UnityEngine.UI.Image separator = CreateImage("HeaderSeparator", _detailPanel, new Vector2(0f, 1f));
            RectTransform sepRect = separator.rectTransform;
            sepRect.anchorMin = new Vector2(0f, 1f);
            sepRect.anchorMax = new Vector2(1f, 1f);
            sepRect.pivot = new Vector2(0.5f, 1f);
            sepRect.offsetMin = new Vector2(16f, -96f);
            sepRect.offsetMax = new Vector2(-16f, -95f);
            separator.color = new Color(0.35f, 0.43f, 0.52f, 0.40f);
            separator.raycastTarget = false;

            // Unlocks and research inputs share one compact row. Research usually needs
            // more width, so give it roughly 60% of the panel.
            _unlockHeading = CreateText("UnlockHeading", _detailPanel, 12, UnityEngine.TextAnchor.UpperLeft);
            RectTransform unlockHeadingRect = _unlockHeading.rectTransform;
            unlockHeadingRect.anchorMin = unlockHeadingRect.anchorMax = new Vector2(0f, 1f);
            unlockHeadingRect.pivot = new Vector2(0f, 1f);
            unlockHeadingRect.sizeDelta = new Vector2(185f, 20f);
            unlockHeadingRect.anchoredPosition = new Vector2(18f, -106f);
            _unlockHeading.color = new Color(0.95f, 0.65f, 0.35f, 1f);
            _unlockHeading.fontStyle = UnityEngine.FontStyle.Bold;
            _unlockHeading.text = "UNLOCKS";

            _unlockRow = CreateUIObject("UnlockRow", _detailPanel).GetComponent<RectTransform>();
            _unlockRow.anchorMin = _unlockRow.anchorMax = new Vector2(0f, 1f);
            _unlockRow.pivot = new Vector2(0f, 1f);
            _unlockRow.sizeDelta = new Vector2(185f, 56f);
            _unlockRow.anchoredPosition = new Vector2(18f, -128f);

            _researchCostHeading = CreateText("ResearchCostHeading", _detailPanel, 12, UnityEngine.TextAnchor.UpperLeft);
            RectTransform researchHeadingRect = _researchCostHeading.rectTransform;
            researchHeadingRect.anchorMin = researchHeadingRect.anchorMax = new Vector2(0f, 1f);
            researchHeadingRect.pivot = new Vector2(0f, 1f);
            researchHeadingRect.sizeDelta = new Vector2(294f, 20f);
            researchHeadingRect.anchoredPosition = new Vector2(190f, -106f);
            _researchCostHeading.color = new Color(0.95f, 0.65f, 0.35f, 1f);
            _researchCostHeading.fontStyle = UnityEngine.FontStyle.Bold;
            _researchCostHeading.text = "RESEARCH CONSUMPTION";

            _researchCostRow = CreateUIObject("ResearchCostRow", _detailPanel).GetComponent<RectTransform>();
            _researchCostRow.anchorMin = _researchCostRow.anchorMax = new Vector2(0f, 1f);
            _researchCostRow.pivot = new Vector2(0f, 1f);
            _researchCostRow.sizeDelta = new Vector2(294f, 56f);
            _researchCostRow.anchoredPosition = new Vector2(190f, -128f);

            // Vanilla-style live research progress for the selected technology.
            _detailProgressGroup = CreateUIObject("ResearchProgress", _detailPanel);
            RectTransform progressGroupRect = _detailProgressGroup.GetComponent<RectTransform>();
            progressGroupRect.anchorMin = progressGroupRect.anchorMax = new Vector2(0f, 1f);
            progressGroupRect.pivot = new Vector2(0f, 1f);
            progressGroupRect.sizeDelta = new Vector2(464f, 58f);
            progressGroupRect.anchoredPosition = new Vector2(18f, -192f);

            UnityEngine.UI.Text progressHeading = CreateText(
                "Heading",
                progressGroupRect,
                12,
                UnityEngine.TextAnchor.UpperLeft);
            RectTransform progressHeadingRect = progressHeading.rectTransform;
            progressHeadingRect.anchorMin = progressHeadingRect.anchorMax = new Vector2(0f, 1f);
            progressHeadingRect.pivot = new Vector2(0f, 1f);
            progressHeadingRect.sizeDelta = new Vector2(160f, 18f);
            progressHeadingRect.anchoredPosition = Vector2.zero;
            progressHeading.fontStyle = UnityEngine.FontStyle.Bold;
            progressHeading.color = new Color(0.95f, 0.65f, 0.35f, 1f);
            progressHeading.text = "RESEARCH PROGRESS";

            GameObject progressBarBgObj = CreateUIObject("BarBackground", progressGroupRect);
            RectTransform progressBarBgRect = progressBarBgObj.GetComponent<RectTransform>();
            progressBarBgRect.anchorMin = new Vector2(0f, 1f);
            progressBarBgRect.anchorMax = new Vector2(1f, 1f);
            progressBarBgRect.pivot = new Vector2(0.5f, 1f);
            progressBarBgRect.offsetMin = new Vector2(0f, -31f);
            progressBarBgRect.offsetMax = new Vector2(0f, -22f);

            UnityEngine.UI.Image progressBarBg = progressBarBgObj.AddComponent<UnityEngine.UI.Image>();
            progressBarBg.color = new Color(0.06f, 0.09f, 0.11f, 1f);
            progressBarBg.raycastTarget = false;

            GameObject progressFillObj = CreateUIObject("Fill", progressBarBgRect);
            RectTransform detailProgressFillRect = progressFillObj.GetComponent<RectTransform>();
            detailProgressFillRect.anchorMin = new Vector2(0f, 0f);
            detailProgressFillRect.anchorMax = new Vector2(0f, 1f);
            detailProgressFillRect.pivot = new Vector2(0f, 0.5f);
            detailProgressFillRect.offsetMin = Vector2.zero;
            detailProgressFillRect.offsetMax = Vector2.zero;

            _detailProgressFill = progressFillObj.AddComponent<UnityEngine.UI.Image>();
            _detailProgressFill.color = new Color(1.00f, 0.63f, 0.26f, 1f);
            _detailProgressFill.raycastTarget = false;

            _detailProgressSpeedText = CreateText(
                "Speed",
                progressGroupRect,
                12,
                UnityEngine.TextAnchor.LowerLeft);
            RectTransform speedRect = _detailProgressSpeedText.rectTransform;
            speedRect.anchorMin = speedRect.anchorMax = new Vector2(0f, 0f);
            speedRect.pivot = new Vector2(0f, 0f);
            speedRect.sizeDelta = new Vector2(290f, 20f);
            speedRect.anchoredPosition = Vector2.zero;
            _detailProgressSpeedText.color = new Color(0.86f, 0.91f, 0.96f, 1f);

            _detailProgressText = CreateText(
                "Hashes",
                progressGroupRect,
                12,
                UnityEngine.TextAnchor.LowerRight);
            RectTransform progressTextRect = _detailProgressText.rectTransform;
            progressTextRect.anchorMin = progressTextRect.anchorMax = new Vector2(1f, 0f);
            progressTextRect.pivot = new Vector2(1f, 0f);
            progressTextRect.sizeDelta = new Vector2(180f, 20f);
            progressTextRect.anchoredPosition = Vector2.zero;
            _detailProgressText.color = new Color(1.00f, 0.63f, 0.26f, 1f);

            _detailProgressGroup.SetActive(false);

            // Dense lower information block. No ScrollRect: use a modest best-fit range
            // so unusually verbose technologies can shrink slightly instead of clipping.
            _detailBody = CreateText("Body", _detailPanel, 14, UnityEngine.TextAnchor.UpperLeft);
            RectTransform bodyRect = _detailBody.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(18f, 66f);
            bodyRect.offsetMax = new Vector2(-18f, -258f);
            _detailBody.horizontalOverflow = UnityEngine.HorizontalWrapMode.Wrap;
            _detailBody.verticalOverflow = UnityEngine.VerticalWrapMode.Truncate;
            _detailBody.lineSpacing = 1.0f;
            _detailBody.resizeTextForBestFit = true;
            _detailBody.resizeTextMinSize = 11;
            _detailBody.resizeTextMaxSize = 14;

            GameObject buttonObj = CreateUIObject("ResearchButton", _detailPanel);
            RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.offsetMin = new Vector2(18f, 14f);
            buttonRect.offsetMax = new Vector2(-18f, 54f);

            UnityEngine.UI.Image buttonBg = buttonObj.AddComponent<UnityEngine.UI.Image>();
            buttonBg.color = new Color(0.12f, 0.38f, 0.56f, 1f);
            _researchButton = buttonObj.AddComponent<UnityEngine.UI.Button>();
            _researchButton.targetGraphic = buttonBg;
            _researchButton.onClick.AddListener(OnResearchButtonClick);

            _researchButtonText = CreateText("Text", buttonRect, 15, UnityEngine.TextAnchor.MiddleCenter);
            Stretch(_researchButtonText.rectTransform);
            _researchButtonText.fontStyle = UnityEngine.FontStyle.Bold;
            _researchButtonText.raycastTarget = false;

            _detailEmptyPrompt = CreateText(
                "EmptyPrompt",
                _detailPanel,
                14,
                UnityEngine.TextAnchor.MiddleCenter);
            Stretch(_detailEmptyPrompt.rectTransform);
            _detailEmptyPrompt.rectTransform.offsetMin = new Vector2(10f, 5f);
            _detailEmptyPrompt.rectTransform.offsetMax = new Vector2(-10f, -5f);
            _detailEmptyPrompt.text = "Please select a technology";
            _detailEmptyPrompt.color = new Color(0.82f, 0.87f, 0.92f, 1f);
            _detailEmptyPrompt.raycastTarget = false;
            _detailEmptyPrompt.gameObject.SetActive(false);
        }

        private void CreateLegend()
        {
            _legend = CreateText("Legend", _root, 13, UnityEngine.TextAnchor.UpperLeft);
            RectTransform r = _legend.rectTransform;
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(0f, 0f);
            r.pivot = new Vector2(0f, 0f);
            r.sizeDelta = new Vector2(1160f, 28f);
            r.anchoredPosition = new Vector2(152f, 10f);
            _legend.text = "Blue = complete   Green = researchable   Orange = prerequisite locked   •   Q+ = add this tech to the research queue   •   Search = highlight names   •   Wheel = zoom   •   Drag = pan";
            _legend.color = new Color(0.76f, 0.82f, 0.89f, 1f);
        }

        private void BuildGraph(int page)
        {
            _nodes.Clear();
            _edges.Clear();
            _backboneEdges.Clear();

            if (_edgeGraphic != null)
                _edgeGraphic.ClearSegments();
            _inferredParents.Clear();

            List<TechProto> protos = new List<TechProto>();
            Dictionary<int, Vector2> rawPositions = new Dictionary<int, Vector2>();

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            TechProto[] all = LDB.techs.dataArray;
            for (int i = 0; i < all.Length; i++)
            {
                TechProto proto = all[i];
                if (!IsVisibleProto(proto, page))
                    continue;

                Vector2 p = _tree.GetSqueezedPos(proto.Position);
                protos.Add(proto);
                rawPositions[proto.ID] = p;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            if (protos.Count == 0)
            {
                _baseGraphSize = new Vector2(800f, 600f);
                _pageGraphSize[page] = _baseGraphSize;
                _graphCanvas.sizeDelta = _baseGraphSize;

                if (!_pageViewInitialized[page])
                    InitializePageView(page);
                else
                    ApplyPageTransform(page);

                return;
            }

            Dictionary<int, Vector2> positions = new Dictionary<int, Vector2>();
            for (int i = 0; i < protos.Count; i++)
            {
                TechProto proto = protos[i];
                Vector2 p = rawPositions[proto.ID];
                Vector2 local = new Vector2(
                    p.x - minX + GraphMargin,
                    -(maxY - p.y + GraphMargin));
                positions[proto.ID] = local;
            }

            float width = Mathf.Max(1100f, maxX - minX + GraphMargin * 2f + NodeSize);
            float height = Mathf.Max(720f, maxY - minY + GraphMargin * 2f + NodeSize);
            _baseGraphSize = new Vector2(width, height);
            _graphCanvas.sizeDelta = _baseGraphSize;

            if (page == 0)
                BuildMainResearchBackbone(protos);

            BuildInferredResearchPrerequisites(protos);

            // Create nodes first so edge routing can avoid passing directly through
            // unrelated nodes. Edge segments are then pushed to the first sibling so
            // they still render behind every node.
            for (int i = 0; i < protos.Count; i++)
            {
                TechProto proto = protos[i];
                CreateNode(proto, positions[proto.ID]);
            }

            if (_edgeGraphic == null)
            {
                GameObject edgeLayerObj = CreateUIObject("EdgeLayer", _graphCanvas);
                edgeLayerObj.transform.SetAsFirstSibling();
                RectTransform edgeRect = edgeLayerObj.GetComponent<RectTransform>();
                edgeRect.anchorMin = edgeRect.anchorMax = new Vector2(0f, 1f);
                edgeRect.pivot = new Vector2(0f, 1f);
                edgeRect.anchoredPosition = Vector2.zero;
                edgeRect.sizeDelta = _baseGraphSize;

                _edgeGraphic = edgeLayerObj.AddComponent<TechTreeEdgeGraphic>();
                _edgeGraphic.raycastTarget = false;
                _pageEdgeGraphics[page] = _edgeGraphic;
            }
            else
            {
                _edgeGraphic.rectTransform.sizeDelta = _baseGraphSize;
                _edgeGraphic.transform.SetAsFirstSibling();
            }

            for (int i = 0; i < protos.Count; i++)
            {
                TechProto child = protos[i];
                AddEdges(child, child.PreTechs, false, positions);
                AddEdges(child, child.PreTechsImplicit, true, positions);

                List<int> inferred;
                if (_inferredParents.TryGetValue(child.ID, out inferred) && inferred != null)
                    AddInferredEdges(child, inferred, positions);
            }

            _pageGraphSize[page] = _baseGraphSize;
            if (!_pageViewInitialized[page])
                InitializePageView(page);
            else
                ApplyPageTransform(page);
        }

        private void BuildInferredResearchPrerequisites(List<TechProto> protos)
        {
            _inferredParents.Clear();
            if (protos == null || protos.Count == 0)
                return;

            // Map each unlocked output item to the technology that unlocks its recipe.
            // Research inputs are predominantly matrices, but keeping this generic also
            // makes the search/dependency model behave correctly for modded tech data.
            Dictionary<int, int> unlockTechByItem = new Dictionary<int, int>();
            HashSet<int> pageTechIds = new HashSet<int>();

            for (int i = 0; i < protos.Count; i++)
            {
                if (protos[i] != null)
                    pageTechIds.Add(protos[i].ID);
            }

            for (int i = 0; i < protos.Count; i++)
            {
                TechProto proto = protos[i];
                if (proto == null || proto.unlockRecipeArray == null)
                    continue;

                for (int r = 0; r < proto.unlockRecipeArray.Length; r++)
                {
                    RecipeProto recipe = proto.unlockRecipeArray[r];
                    if (recipe == null || recipe.Results == null)
                        continue;

                    for (int j = 0; j < recipe.Results.Length; j++)
                    {
                        int itemId = recipe.Results[j];
                        if (itemId <= 0 || unlockTechByItem.ContainsKey(itemId))
                            continue;

                        unlockTechByItem[itemId] = proto.ID;
                    }
                }
            }

            for (int i = 0; i < protos.Count; i++)
            {
                TechProto child = protos[i];
                if (child == null || child.itemArray == null)
                    continue;

                HashSet<int> rawAncestors = new HashSet<int>();
                CollectRawAncestors(child.ID, rawAncestors);

                List<int> inferred = null;

                for (int j = 0; j < child.itemArray.Length; j++)
                {
                    ItemProto item = child.itemArray[j];
                    if (item == null)
                        continue;

                    int unlockTechId;
                    if (!unlockTechByItem.TryGetValue(item.ID, out unlockTechId))
                        continue;
                    if (unlockTechId == child.ID)
                        continue;

                    // If the item-unlock technology is already reachable through the
                    // normal prerequisite graph, a second edge would only add clutter.
                    if (rawAncestors.Contains(unlockTechId))
                        continue;

                    // Nodes are created after this pass, so use the page's prototype
                    // ID set rather than probing the node dictionary.
                    if (!pageTechIds.Contains(unlockTechId))
                        continue;

                    if (inferred == null)
                        inferred = new List<int>();
                    if (!inferred.Contains(unlockTechId))
                        inferred.Add(unlockTechId);
                }

                if (inferred != null && inferred.Count > 0)
                    _inferredParents[child.ID] = inferred;
            }
        }

        private void CollectRawAncestors(int techId, HashSet<int> result)
        {
            TechProto proto = LDB.techs.Select(techId);
            if (proto == null)
                return;

            CollectRawAncestorArray(proto.PreTechs, result);
            CollectRawAncestorArray(proto.PreTechsImplicit, result);
        }

        private void CollectRawAncestorArray(int[] parents, HashSet<int> result)
        {
            if (parents == null)
                return;

            for (int i = 0; i < parents.Length; i++)
            {
                int parentId = parents[i];
                if (!result.Add(parentId))
                    continue;

                CollectRawAncestors(parentId, result);
            }
        }

        private void BuildMainResearchBackbone(List<TechProto> protos)
        {
            _backboneEdges.Clear();

            // This is deliberately ID-based rather than inferred from node positions.
            // Tech IDs are stable across localization, while display names are not.
            // Only mark an edge when both endpoint prototypes exist in the current data.
            for (int i = 0; i + 1 < MainResearchLineTechIds.Length; i++)
            {
                int fromId = MainResearchLineTechIds[i];
                int toId = MainResearchLineTechIds[i + 1];

                if (LDB.techs.Select(fromId) == null || LDB.techs.Select(toId) == null)
                    continue;

                _backboneEdges.Add(EdgeKey(fromId, toId));
            }
        }

        private bool IsVisibleProto(TechProto proto, int page)
        {
            if (proto == null || proto.page != page)
                return false;
            if (!proto.Published || proto.IsObsolete || proto.IsHiddenTech)
                return false;
            if (proto.ID == 1)
                return false;
            return true;
        }

        private void AddEdges(TechProto child, int[] parents, bool implicitEdge, Dictionary<int, Vector2> positions)
        {
            if (parents == null || !positions.ContainsKey(child.ID))
                return;

            for (int i = 0; i < parents.Length; i++)
            {
                int parentId = parents[i];
                if (!positions.ContainsKey(parentId))
                    continue;

                EdgeView edge = CreateEdge(parentId, child.ID, positions[parentId], positions[child.ID], implicitEdge);
                _edges.Add(edge);
            }
        }

        private void AddInferredEdges(TechProto child, List<int> parents, Dictionary<int, Vector2> positions)
        {
            if (child == null || parents == null || !positions.ContainsKey(child.ID))
                return;

            for (int i = 0; i < parents.Count; i++)
            {
                int parentId = parents[i];
                if (!positions.ContainsKey(parentId))
                    continue;

                EdgeView edge = CreateEdge(parentId, child.ID, positions[parentId], positions[child.ID], false, true);
                _edges.Add(edge);
            }
        }

        private EdgeView CreateEdge(int fromId, int toId, Vector2 from, Vector2 to, bool implicitEdge)
        {
            return CreateEdge(fromId, toId, from, to, implicitEdge, false);
        }

        private EdgeView CreateEdge(int fromId, int toId, Vector2 from, Vector2 to, bool implicitEdge, bool inferredEdge)
        {
            Vector2 fromCenter = from + new Vector2(NodeSize * 0.5f, -NodeSize * 0.5f);
            Vector2 toCenter = to + new Vector2(NodeSize * 0.5f, -NodeSize * 0.5f);
            List<Vector2> points = BuildOrthogonalRoute(fromId, toId, fromCenter, toCenter);

            bool contextualEdge = implicitEdge || inferredEdge;
            bool backbone = !contextualEdge && _backboneEdges.Contains(EdgeKey(fromId, toId));

            // Keep the line weights configurable without multiplying UI components.
            // Contextual implicit/inferred edges stay slightly lighter than ordinary
            // dependency lines while sharing the same base setting.
            float ordinaryThickness = Plugin.LineThickness != null
                ? Mathf.Clamp(Plugin.LineThickness.Value, 1.0f, 20.0f)
                : 5.0f;

            float mainThickness = Plugin.MainLineThickness != null
                ? Mathf.Clamp(Plugin.MainLineThickness.Value, 1.0f, 24.0f)
                : 9.0f;

            float thickness = backbone
                ? mainThickness
                : (contextualEdge ? ordinaryThickness * 0.75f : ordinaryThickness);
            Color color = backbone
                ? new Color(0.20f, 0.72f, 0.98f, 0.95f)
                : (contextualEdge ? ImplicitLineColor : LineColor);

            int segmentStart = _edgeGraphic != null ? _edgeGraphic.SegmentCount : 0;
            int segmentCount = 0;

            if (_edgeGraphic != null)
            {
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    if ((points[i + 1] - points[i]).sqrMagnitude < 0.01f)
                        continue;

                    _edgeGraphic.AddSegment(
                        points[i],
                        points[i + 1],
                        thickness,
                        color,
                        !contextualEdge,
                        backbone);
                    segmentCount++;
                }
            }

            return new EdgeView
            {
                FromId = fromId,
                ToId = toId,
                Implicit = implicitEdge,
                Inferred = inferredEdge,
                Backbone = backbone,
                SegmentStart = segmentStart,
                SegmentCount = segmentCount
            };
        }

        private List<Vector2> BuildOrthogonalRoute(int fromId, int toId, Vector2 fromCenter, Vector2 toCenter)
        {
            List<Vector2> points = new List<Vector2>();
            float dx = toCenter.x - fromCenter.x;
            float dy = toCenter.y - fromCenter.y;
            int hash = Math.Abs((fromId * 397) ^ toId);
            float laneNudge = ((hash % 5) - 2) * 7f;

            if (Mathf.Abs(dx) >= NodeSize * 1.15f)
            {
                float dir = Mathf.Sign(dx);
                Vector2 start = fromCenter + new Vector2(dir * NodeSize * 0.5f, 0f);
                Vector2 end = toCenter - new Vector2(dir * NodeSize * 0.5f, 0f);
                float midX = (start.x + end.x) * 0.5f + laneNudge;

                // If the obvious middle lane would cut through another node, move the
                // vertical leg to a nearby clear lane between columns.
                midX = FindClearVerticalLane(midX, start.y, end.y, fromId, toId, dir);

                points.Add(start);
                points.Add(new Vector2(midX, start.y));
                points.Add(new Vector2(midX, end.y));
                points.Add(end);
            }
            else
            {
                // Same/near column: route around the side instead of drawing through
                // every node between the two levels.
                float dir = ((hash & 1) == 0) ? 1f : -1f;
                Vector2 start = fromCenter + new Vector2(dir * NodeSize * 0.5f, 0f);
                Vector2 end = toCenter + new Vector2(dir * NodeSize * 0.5f, 0f);
                float laneX = Mathf.Max(fromCenter.x, toCenter.x) + NodeSize * 0.5f + 28f + (hash % 4) * 8f;
                if (dir < 0f)
                    laneX = Mathf.Min(fromCenter.x, toCenter.x) - NodeSize * 0.5f - 28f - (hash % 4) * 8f;

                laneX = FindClearVerticalLane(laneX, start.y, end.y, fromId, toId, dir);

                points.Add(start);
                points.Add(new Vector2(laneX, start.y));
                points.Add(new Vector2(laneX, end.y));
                points.Add(end);
            }

            return points;
        }

        private float FindClearVerticalLane(float candidateX, float y0, float y1, int fromId, int toId, float preferredDirection)
        {
            const float clearance = 8f;
            float minY = Mathf.Min(y0, y1);
            float maxY = Mathf.Max(y0, y1);

            for (int attempt = 0; attempt < 10; attempt++)
            {
                float x = candidateX;
                if (attempt > 0)
                {
                    int step = (attempt + 1) / 2;
                    float side = (attempt % 2 == 1) ? preferredDirection : -preferredDirection;
                    x += side * step * 18f;
                }

                bool blocked = false;
                foreach (KeyValuePair<int, NodeView> pair in _nodes)
                {
                    if (pair.Key == fromId || pair.Key == toId || pair.Value == null || pair.Value.Root == null)
                        continue;

                    Vector2 topLeft = pair.Value.Root.anchoredPosition;
                    float left = topLeft.x - clearance;
                    float right = topLeft.x + NodeSize + clearance;
                    float top = topLeft.y + clearance;
                    float bottom = topLeft.y - NodeSize - clearance;

                    if (x >= left && x <= right && maxY >= bottom && minY <= top)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                    return x;
            }

            return candidateX;
        }

        private void CreateNode(TechProto proto, Vector2 position)
        {
            GameObject buttonObj = CreateUIObject("Tech_" + proto.ID, _graphCanvas);
            RectTransform rect = buttonObj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(NodeSize, NodeSize);

            UnityEngine.CanvasGroup canvasGroup = buttonObj.AddComponent<UnityEngine.CanvasGroup>();

            UnityEngine.UI.Image border = buttonObj.AddComponent<UnityEngine.UI.Image>();
            border.color = LockedColor;
            border.raycastTarget = true;

            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = border;
            int techId = proto.ID;
            button.onClick.AddListener(() => SelectTech(techId));

            GameObject innerObj = CreateUIObject("Inner", rect);
            RectTransform innerRect = innerObj.GetComponent<RectTransform>();
            Stretch(innerRect);
            innerRect.offsetMin = new Vector2(4f, 4f);
            innerRect.offsetMax = new Vector2(-4f, -4f);
            UnityEngine.UI.Image inner = innerObj.AddComponent<UnityEngine.UI.Image>();
            inner.color = NodeInnerColor;
            inner.raycastTarget = false;

            UnityEngine.UI.Image icon = CreateImage("Icon", rect, new Vector2(72f, 72f));
            icon.sprite = proto.iconSprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = Vector2.zero;

            // Some DSP icons show a faint 1-2 pixel line at the bottom edge when enlarged.
            // The screenshot confirms it follows the icon rect exactly, which is consistent
            // with atlas/bilinear edge bleed rather than a dependency line. Cover only the
            // bottom edge locally; do not alter the shared atlas/filter mode.
            UnityEngine.UI.Image iconBleedCover = CreateImage("IconBleedCover", rect, new Vector2(72f, 2.5f));
            iconBleedCover.color = NodeInnerColor;
            iconBleedCover.raycastTarget = false;
            iconBleedCover.rectTransform.anchorMin = iconBleedCover.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            iconBleedCover.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            iconBleedCover.rectTransform.anchoredPosition = new Vector2(0f, -35.5f);

            GameObject levelBadgeObj = CreateUIObject("LevelBadge", rect);
            RectTransform lr = levelBadgeObj.GetComponent<RectTransform>();
            lr.anchorMin = lr.anchorMax = new Vector2(0f, 1f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.sizeDelta = new Vector2(126f, 60f);
            lr.anchoredPosition = new Vector2(-4f, 16f);

            UnityEngine.UI.Image levelBadgeBg = levelBadgeObj.AddComponent<UnityEngine.UI.Image>();
            levelBadgeBg.color = new Color(0.035f, 0.045f, 0.060f, 0.01f);
            levelBadgeBg.raycastTarget = false;

            UnityEngine.UI.Text level = CreateText("Text", lr, 30, UnityEngine.TextAnchor.MiddleLeft);
            Stretch(level.rectTransform);
            level.fontStyle = UnityEngine.FontStyle.Bold;
            level.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
            level.verticalOverflow = UnityEngine.VerticalWrapMode.Overflow;
            level.lineSpacing = 0.92f;
            level.raycastTarget = false;

            RectTransform pipGroup = CreateUIObject("MatrixIcons", rect).GetComponent<RectTransform>();
            pipGroup.anchorMin = pipGroup.anchorMax = new Vector2(0.5f, 0f);
            pipGroup.pivot = new Vector2(0.5f, 1f);
            pipGroup.sizeDelta = new Vector2(126f, 24f);
            // Put the matrix requirement indicators outside the node rather than
            // competing with the technology icon inside it.
            pipGroup.anchoredPosition = new Vector2(0f, -3f);

            int directMask = GetDirectMatrixMask(proto);
            List<UnityEngine.UI.Image> pipImages = new List<UnityEngine.UI.Image>();
            int pipCount = CountBits(directMask);

            const float matrixIconSize = 20f;
            const float matrixIconGap = 3f;
            const float matrixIconPitch = matrixIconSize + matrixIconGap;
            float totalWidth = pipCount > 0
                ? pipCount * matrixIconSize + (pipCount - 1) * matrixIconGap
                : 0f;
            float iconX = -totalWidth * 0.5f + matrixIconSize * 0.5f;

            for (int i = 0; i < MatrixIds.Length; i++)
            {
                if ((directMask & (1 << i)) == 0)
                    continue;

                ItemProto matrixItem = LDB.items.Select(MatrixIds[i]);
                if (matrixItem == null)
                    continue;

                GameObject matrixObj = CreateUIObject("Matrix_" + MatrixIds[i], pipGroup);
                RectTransform matrixRect = matrixObj.GetComponent<RectTransform>();
                matrixRect.anchorMin = matrixRect.anchorMax = new Vector2(0.5f, 1f);
                matrixRect.pivot = new Vector2(0.5f, 1f);
                matrixRect.sizeDelta = new Vector2(matrixIconSize, matrixIconSize);
                matrixRect.anchoredPosition = new Vector2(iconX, 0f);

                UnityEngine.UI.Image matrixIcon = matrixObj.AddComponent<UnityEngine.UI.Image>();
                matrixIcon.sprite = matrixItem.iconSprite;
                matrixIcon.preserveAspect = true;
                matrixIcon.raycastTarget = false;

                pipImages.Add(matrixIcon);
                iconX += matrixIconPitch;
            }

            GameObject researchWingObj = CreateUIObject("ResearchWing", rect);
            RectTransform researchWingRect = researchWingObj.GetComponent<RectTransform>();
            researchWingRect.anchorMin = researchWingRect.anchorMax = new Vector2(1f, 0f);
            researchWingRect.pivot = new Vector2(0f, 0f);
            researchWingRect.sizeDelta = new Vector2(62f, NodeSize);
            researchWingRect.anchoredPosition = new Vector2(-3f, 0f);

            TechTreeResearchWingGraphic researchWingGraphic =
                researchWingObj.AddComponent<TechTreeResearchWingGraphic>();
            researchWingGraphic.raycastTarget = true;
            researchWingGraphic.BorderColor = ReadyColor;
            researchWingGraphic.FillColor = NodeInnerColor;
            researchWingGraphic.ArrowColor = new Color(0.28f, 0.78f, 1f, 1f);

            UnityEngine.UI.Button researchWingButton =
                researchWingObj.AddComponent<UnityEngine.UI.Button>();
            researchWingButton.targetGraphic = researchWingGraphic;
            researchWingButton.transition = UnityEngine.UI.Selectable.Transition.None;
            researchWingButton.onClick.AddListener(delegate { QueueTechFromNode(techId); });

            UnityEngine.CanvasGroup researchWingCanvas =
                researchWingObj.AddComponent<UnityEngine.CanvasGroup>();
            researchWingCanvas.alpha = 0f;
            researchWingCanvas.interactable = false;
            researchWingCanvas.blocksRaycasts = false;

            UnityEngine.UI.Text researchWingText =
                CreateText("QueueLabel", researchWingRect, 36, UnityEngine.TextAnchor.MiddleCenter);
            Stretch(researchWingText.rectTransform);
            researchWingText.text = "Q+";
            researchWingText.fontStyle = UnityEngine.FontStyle.Bold;
            researchWingText.color = new Color(0.32f, 0.80f, 1f, 1f);
            researchWingText.raycastTarget = false;

            UnityEngine.UI.Outline researchWingGlow =
                researchWingText.gameObject.AddComponent<UnityEngine.UI.Outline>();
            researchWingGlow.effectColor = new Color(0.10f, 0.52f, 1f, 0.95f);
            researchWingGlow.effectDistance = new Vector2(1.4f, -1.4f);

            NodeView node = new NodeView
            {
                Proto = proto,
                Root = rect,
                OriginalPosition = position,
                Border = border,
                Inner = inner,
                Icon = icon,
                Level = level,
                LevelBadge = levelBadgeObj,
                CanvasGroup = canvasGroup,
                DirectMatrixMask = directMask,
                Pips = pipImages.ToArray(),
                ResearchWing = researchWingObj,
                ResearchWingRect = researchWingRect,
                ResearchWingButton = researchWingButton,
                ResearchWingGraphic = researchWingGraphic,
                ResearchWingCanvas = researchWingCanvas
            };
            _nodes[proto.ID] = node;
            UpdateNodeState(node);
        }

        private void RefreshNodeResearchWing(NodeView node)
        {
            if (node == null ||
                node.Proto == null ||
                node.ResearchWing == null ||
                node.ResearchWingCanvas == null)
                return;

            if (GameMain.history == null || node.Root == null || !node.Root.gameObject.activeSelf)
            {
                node.ResearchWingCanvas.alpha = 0f;
                node.ResearchWingCanvas.interactable = false;
                node.ResearchWingCanvas.blocksRaycasts = false;
                return;
            }

            TechState state = GameMain.history.TechState(node.Proto.ID);
            bool complete = state.unlocked;
            bool ready = !complete && IsTechReadyByMap(node.Proto.ID);
            bool canQueue = ready && GameMain.history.CanEnqueueTech(node.Proto.ID);

            bool visible = node.ResearchWingOpen && canQueue;

            node.ResearchWingCanvas.alpha = visible ? 1f : 0f;
            node.ResearchWingCanvas.interactable = visible;
            node.ResearchWingCanvas.blocksRaycasts = visible;

            if (node.ResearchWingButton != null)
                node.ResearchWingButton.interactable = visible;

            if (node.ResearchWingGraphic != null)
            {
                Color borderColor = node.Border != null ? node.Border.color : ReadyColor;
                node.ResearchWingGraphic.SetColors(borderColor, NodeInnerColor);
            }
        }

        private void QueueTechFromNode(int techId)
        {
            if (techId == 0 || GameMain.history == null)
                return;

            if (!IsTechReadyByMap(techId))
                return;

            if (!GameMain.history.CanEnqueueTech(techId))
                return;

            GameMain.history.EnqueueTech(techId);
            RefreshNodeStates();

            if (_selectedTechId != 0)
                PopulateDetails(_selectedTechId);
        }

        internal void ClearSelection()
        {
            if (_selectedTechId == 0)
                return;

            _selectedTechId = 0;
            _selectedPath.Clear();
            _selectedEdges.Clear();
            ClearDetails();

            RefreshNodeStates();
            RefreshEdges();
        }

        private void SelectTech(int techId)
        {
            if (_selectedTechId == techId)
            {
                ClearSelection();
                return;
            }

            _selectedTechId = techId;
            RebuildSelectedPath(techId);
            PopulateDetails(techId);

            RefreshNodeStates();
            RefreshEdges();
        }

        private void RebuildSelectedPath(int techId)
        {
            _selectedPath.Clear();
            _selectedEdges.Clear();
            CollectPrerequisites(techId, new HashSet<int>());
        }

        private void CollectPrerequisites(int techId, HashSet<int> visited)
        {
            if (!visited.Add(techId))
                return;

            _selectedPath.Add(techId);

            TechProto proto = LDB.techs.Select(techId);
            if (proto == null)
                return;

            CollectParentArray(techId, proto.PreTechs, proto.PreTechsMax, visited);
            CollectParentArray(techId, proto.PreTechsImplicit, proto.PreTechsMax, visited);

            List<int> inferred;
            if (_inferredParents.TryGetValue(techId, out inferred) && inferred != null)
            {
                for (int i = 0; i < inferred.Count; i++)
                {
                    int parentId = inferred[i];
                    if (!_nodes.ContainsKey(parentId))
                        continue;

                    // Inferred prerequisites represent research-input unlocks rather than
                    // a PreTechsMax relationship. Once that prerequisite technology is
                    // completed, it is no longer useful context for "what am I missing?".
                    if (IsPrerequisiteSatisfied(parentId, false))
                        continue;

                    _selectedEdges.Add(EdgeKey(parentId, techId));
                    CollectPrerequisites(parentId, visited);
                }
            }
        }

        private void CollectParentArray(int childId, int[] parents, bool requireMaxLevel, HashSet<int> visited)
        {
            if (parents == null)
                return;

            for (int i = 0; i < parents.Length; i++)
            {
                int parentId = parents[i];
                if (!_nodes.ContainsKey(parentId))
                    continue;

                // The selected overlay answers "what is still blocking this tech?", not
                // "show me every ancestor I researched hours ago". A satisfied parent
                // terminates this branch completely.
                if (IsPrerequisiteSatisfied(parentId, requireMaxLevel))
                    continue;

                _selectedEdges.Add(EdgeKey(parentId, childId));
                CollectPrerequisites(parentId, visited);
            }
        }

        private bool IsPrerequisiteSatisfied(int techId, bool requireMaxLevel)
        {
            return GameMain.history != null &&
                   GameMain.history.TechUnlocked(techId, requireMaxLevel);
        }

        private bool IsTechReadyByMap(int techId)
        {
            if (GameMain.history == null)
                return false;

            TechState state = GameMain.history.TechState(techId);
            if (state.unlocked)
                return true;

            int queueIndex = FindFirstQueueIndex(techId);

            if (queueIndex >= 0)
            {
                // Once a tech is already queued/researching, CanEnqueueTechIgnoreFull()
                // is no longer an appropriate readiness test: it may return false simply
                // because this tech is already present. Validate its actual queue position
                // instead. DSP checks direct + implicit prerequisites against entries before it.
                if (!GameMain.history.CheckTechAtQueueIndex(techId, queueIndex))
                    return false;

                return InferredPrerequisiteChainSatisfiedBefore(
                    techId,
                    queueIndex,
                    new HashSet<int>());
            }

            // A new tech will be appended to the queue. Vanilla validates direct and
            // implicit prerequisites against everything already queued.
            if (!GameMain.history.CanEnqueueTechIgnoreFull(techId))
                return false;

            return InferredPrerequisiteChainSatisfiedBefore(
                techId,
                GameMain.history.techQueueLength,
                new HashSet<int>());
        }

        private bool InferredPrerequisiteChainSatisfiedBefore(
            int techId,
            int queueLimit,
            HashSet<int> visiting)
        {
            if (GameMain.history == null)
                return false;

            if (!visiting.Add(techId))
                return true;

            List<int> inferred;
            if (!_inferredParents.TryGetValue(techId, out inferred) || inferred == null)
                return true;

            for (int i = 0; i < inferred.Count; i++)
            {
                int parentId = inferred[i];
                TechState parentState = GameMain.history.TechState(parentId);

                if (parentState.unlocked)
                    continue;

                // An inferred prerequisite must already appear before the child in the
                // research queue. Merely existing somewhere later in the queue is not enough.
                int parentQueueIndex = FindFirstQueueIndexBefore(parentId, queueLimit);
                if (parentQueueIndex < 0)
                    return false;

                // Validate the parent's own direct + implicit prerequisites at its actual
                // position, then recurse through its inferred prerequisites using the same
                // "must be earlier" rule.
                if (!GameMain.history.CheckTechAtQueueIndex(parentId, parentQueueIndex))
                    return false;

                if (!InferredPrerequisiteChainSatisfiedBefore(
                    parentId,
                    parentQueueIndex,
                    visiting))
                    return false;
            }

            return true;
        }

        private int FindFirstQueueIndex(int techId)
        {
            if (GameMain.history == null || GameMain.history.techQueue == null || techId == 0)
                return -1;

            int[] queue = GameMain.history.techQueue;
            for (int i = 0; i < queue.Length; i++)
            {
                if (queue[i] == techId)
                    return i;

                // DSP keeps the queue packed. Once we hit zero there are no later entries.
                if (queue[i] == 0)
                    break;
            }

            return -1;
        }

        private int FindFirstQueueIndexBefore(int techId, int limit)
        {
            if (GameMain.history == null || GameMain.history.techQueue == null || techId == 0)
                return -1;

            int[] queue = GameMain.history.techQueue;
            int count = Mathf.Min(limit, queue.Length);

            for (int i = 0; i < count; i++)
            {
                if (queue[i] == techId)
                    return i;

                if (queue[i] == 0)
                    break;
            }

            return -1;
        }

        private bool IsImmediateDescendantOfSelected(int techId)
        {
            if (_selectedTechId == 0 || techId == 0 || GameMain.history == null)
                return false;

            bool isDirectChild = false;
            for (int i = 0; i < _edges.Count; i++)
            {
                EdgeView edge = _edges[i];
                if (edge != null &&
                    edge.FromId == _selectedTechId &&
                    edge.ToId == techId)
                {
                    isDirectChild = true;
                    break;
                }
            }

            if (!isDirectChild)
                return false;

            // If it is already ready, keep the immediate child at full emphasis.
            if (IsTechReadyByMap(techId))
                return true;

            // Otherwise only preserve it if queueing the selected technology would
            // actually make this child researchable.
            return WouldBecomeReadyAfterQueueingSelected(techId);
        }

        private bool WouldBecomeReadyAfterQueueingSelected(int childTechId)
        {
            if (GameMain.history == null || _selectedTechId == 0)
                return false;

            TechProto child = LDB.techs.Select(childTechId);
            if (child == null)
                return false;

            TechState childState = GameMain.history.TechState(childTechId);
            if (childState.unlocked)
                return false;

            // The selected tech must itself be a valid queue entry (unless it is
            // already unlocked/queued).
            TechState selectedState = GameMain.history.TechState(_selectedTechId);
            bool selectedAlreadySatisfied =
                selectedState.unlocked || FindFirstQueueIndex(_selectedTechId) >= 0;

            if (!selectedAlreadySatisfied)
            {
                if (!IsTechReadyByMap(_selectedTechId) ||
                    !GameMain.history.CanEnqueueTech(_selectedTechId))
                    return false;
            }

            if (!HypotheticalParentArraySatisfied(
                    child.PreTechs,
                    child.PreTechsMax,
                    _selectedTechId))
                return false;

            if (!HypotheticalParentArraySatisfied(
                    child.PreTechsImplicit,
                    child.PreTechsMax,
                    _selectedTechId))
                return false;

            // Inferred requirements obey the same "must appear before the child"
            // rule. The selected tech is treated as the newly appended queue entry.
            List<int> inferred;
            if (_inferredParents.TryGetValue(childTechId, out inferred) &&
                inferred != null)
            {
                for (int i = 0; i < inferred.Count; i++)
                {
                    int parentId = inferred[i];
                    TechState parentState = GameMain.history.TechState(parentId);

                    if (parentState.unlocked)
                        continue;

                    if (parentId == _selectedTechId)
                        continue;

                    int parentQueueIndex = FindFirstQueueIndex(parentId);
                    if (parentQueueIndex < 0)
                        return false;

                    if (!GameMain.history.CheckTechAtQueueIndex(
                            parentId,
                            parentQueueIndex))
                        return false;

                    if (!InferredPrerequisiteChainSatisfiedBefore(
                            parentId,
                            parentQueueIndex,
                            new HashSet<int>()))
                        return false;
                }
            }

            return true;
        }

        private bool HypotheticalParentArraySatisfied(
            int[] parents,
            bool requireMaxLevel,
            int hypotheticalQueuedTechId)
        {
            if (parents == null || GameMain.history == null)
                return true;

            for (int i = 0; i < parents.Length; i++)
            {
                int parentId = parents[i];

                if (GameMain.history.TechUnlocked(parentId, requireMaxLevel))
                    continue;

                TechState state = GameMain.history.TechState(parentId);
                int queuedCount = GameMain.history.TechQueuedCount(parentId);

                if (parentId == hypotheticalQueuedTechId)
                    queuedCount++;

                if (requireMaxLevel)
                {
                    if (queuedCount - 1 + state.curLevel < state.maxLevel)
                        return false;
                }
                else
                {
                    if (parentId == hypotheticalQueuedTechId)
                    {
                        if (queuedCount <= 0)
                            return false;
                    }
                    else if (!GameMain.history.TechInQueue(parentId))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static Color ScaleRgb(Color color, float factor)
        {
            return new Color(
                Mathf.Clamp01(color.r * factor),
                Mathf.Clamp01(color.g * factor),
                Mathf.Clamp01(color.b * factor),
                color.a);
        }

        private void SetPipBrightness(NodeView node, float factor)
        {
            if (node == null || node.Pips == null)
                return;

            int index = 0;
            for (int i = 0; i < MatrixIds.Length; i++)
            {
                if ((node.DirectMatrixMask & (1 << i)) == 0)
                    continue;

                if (index < node.Pips.Length && node.Pips[index] != null)
                    node.Pips[index].color = ScaleRgb(MatrixColors[i], factor);

                index++;
            }
        }

        private void RefreshNodeStates()
        {
            foreach (NodeView node in _nodes.Values)
                UpdateNodeState(node);

            if (_builtPage >= 0 && _builtPage < 2)
                _pageLastStateRefresh[_builtPage] = Time.unscaledTime;
        }

        private bool HasTightNodeAbove(NodeView node)
        {
            if (node == null || node.Root == null || _nodes == null)
                return false;

            Vector2 p = node.Root.anchoredPosition;

            foreach (NodeView other in _nodes.Values)
            {
                if (other == null ||
                    other == node ||
                    other.Root == null ||
                    !other.Root.gameObject.activeSelf)
                    continue;

                Vector2 q = other.Root.anchoredPosition;

                // Only nodes substantially sharing the same horizontal lane can
                // collide with the level/progress label above this node.
                if (Mathf.Abs(q.x - p.x) > NodeSize * 0.75f)
                    continue;

                float dy = q.y - p.y;
                if (dy > 0f && dy < NodeSize + 92f)
                    return true;
            }

            return false;
        }

        private void UpdateNodeState(NodeView node)
        {
            if (GameMain.history == null || node == null || node.Proto == null)
                return;

            TechState state = GameMain.history.TechState(node.Proto.ID);
            bool complete = state.unlocked;

            // One source of truth: green means our complete dependency map says this
            // technology can safely be queued now.
            bool ready = !complete && IsTechReadyByMap(node.Proto.ID);

            Color borderColor = complete ? CompleteColor : (ready ? ReadyColor : LockedColor);
            if (_selectedTechId == node.Proto.ID)
                borderColor = SelectedColor;

            node.Border.color = borderColor;

            float researchProgress = 0f;

            // Stored hash progress belongs to the current level and remains if research
            // is paused/cancelled/switched away. Keep showing that percentage whenever
            // partial progress exists.
            bool hasStoredProgress =
                !state.unlocked &&
                state.hashNeeded > 0L &&
                state.hashUploaded > 0L;

            // The progress bar itself is an "actively researching now" indicator.
            bool activelyResearching =
                !state.unlocked &&
                GameMain.history.currentTech == node.Proto.ID &&
                state.hashNeeded > 0L;

            if (hasStoredProgress || activelyResearching)
                researchProgress = Mathf.Clamp01((float)((double)state.hashUploaded / (double)state.hashNeeded));

            string levelText = FormatLevel(node.Proto, state);

            if (node.LevelBadge != null && node.Level != null)
            {
                RectTransform badgeRect =
                    node.LevelBadge.GetComponent<RectTransform>();

                if (hasStoredProgress)
                {
                    int percent = Mathf.Clamp(
                        Mathf.FloorToInt(researchProgress * 100f),
                        0,
                        99);

                    if (HasTightNodeAbove(node))
                    {
                        // On tightly stacked branches, an extra line would collide
                        // with the node above. Temporarily replace the level label
                        // with progress instead.
                        badgeRect.sizeDelta = new Vector2(126f, 60f);
                        badgeRect.anchoredPosition = new Vector2(-4f, 16f);
                        node.Level.alignment = UnityEngine.TextAnchor.MiddleLeft;
                        levelText = percent + "%";
                    }
                    else
                    {
                        // With clear space above, show percentage and level together.
                        // This sits two pixels lower than 0.9.31.
                        badgeRect.sizeDelta = new Vector2(170f, 104f);
                        badgeRect.anchoredPosition = new Vector2(-4f, 36f);
                        node.Level.alignment = UnityEngine.TextAnchor.UpperLeft;
                        levelText = percent + "%\n" + levelText;
                    }
                }
                else
                {
                    badgeRect.sizeDelta = new Vector2(126f, 60f);
                    badgeRect.anchoredPosition = new Vector2(-4f, 16f);
                    node.Level.alignment = UnityEngine.TextAnchor.MiddleLeft;
                }
            }

            node.Level.text = levelText;
            if (node.LevelBadge != null)
                node.LevelBadge.SetActive(
                    ShouldShowNodeLevelBadge(node.Proto, state) ||
                    hasStoredProgress);

            bool selectedContext = _selectedTechId != 0;
            bool inSelectedPath = selectedContext && _selectedPath.Contains(node.Proto.ID);
            bool immediateDescendant =
                selectedContext &&
                !inSelectedPath &&
                IsImmediateDescendantOfSelected(node.Proto.ID);

            bool searchActive = !string.IsNullOrEmpty(_searchText);
            bool searchMatch = NodeMatchesSearch(node);
            bool searchDim = searchActive && !searchMatch;

            node.Inner.color = NodeInnerColor;

            // Never dim the complete node through CanvasGroup alpha. Doing so makes the
            // full-size coloured border image bleed through the semi-transparent inner
            // panel. Dim the visible elements directly instead and leave the inner opaque.
            if (node.CanvasGroup != null)
                node.CanvasGroup.alpha = 1f;

            if (searchDim)
            {
                // Search remains the one case where non-matches deliberately go grey.
                Color greyBorder = new Color(0.30f, 0.32f, 0.35f, 1f);
                Color greyContent = new Color(0.48f, 0.50f, 0.53f, 1f);
                node.Border.color = greyBorder;
                node.Icon.color = greyContent;
                node.Level.color = greyContent;

                if (node.Pips != null)
                {
                    for (int i = 0; i < node.Pips.Length; i++)
                    {
                        if (node.Pips[i] != null)
                            node.Pips[i].color = greyContent;
                    }
                }
            }
            else
            {
                // Selection emphasis:
                //   selected + unsatisfied prerequisite path = 125%
                //   immediate descendants                   = 100%
                //   everything else                         = 75%
                float emphasis = 1f;
                if (selectedContext)
                {
                    if (inSelectedPath)
                        emphasis = 1.5f;
                    else if (immediateDescendant)
                        emphasis = 1f;
                    else
                        emphasis = 0.5f;
                }

                node.Border.color = ScaleRgb(borderColor, emphasis);

                float contentBrightness = Mathf.Min(emphasis, 1f);
                Color contentColor = new Color(
                    contentBrightness,
                    contentBrightness,
                    contentBrightness,
                    1f);

                node.Icon.color = contentColor;
                node.Level.color = contentColor;
                SetPipBrightness(node, contentBrightness);
            }

            RefreshNodeResearchWing(node);
        }

        private void RefreshEdges()
        {
            if (_edgeGraphic == null)
                return;

            for (int i = 0; i < _edges.Count; i++)
            {
                EdgeView edge = _edges[i];
                NodeView fromNode;
                NodeView toNode;
                bool endpointsVisible =
                    _nodes.TryGetValue(edge.FromId, out fromNode) &&
                    _nodes.TryGetValue(edge.ToId, out toNode) &&
                    fromNode != null && toNode != null &&
                    fromNode.Root != null && toNode.Root != null &&
                    fromNode.Root.gameObject.activeSelf &&
                    toNode.Root.gameObject.activeSelf;

                bool selected = endpointsVisible &&
                    _selectedTechId != 0 &&
                    _selectedEdges.Contains(EdgeKey(edge.FromId, edge.ToId));
                bool contextualEdge = edge.Implicit || edge.Inferred;
                bool enabled = endpointsVisible && (selected || !contextualEdge);

                Color color;
                if (selected)
                {
                    color = PathLineColor;
                }
                else
                {
                    color = edge.Backbone
                        ? new Color(0.20f, 0.72f, 0.98f, 0.95f)
                        : (contextualEdge ? ImplicitLineColor : LineColor);

                    if (_selectedTechId != 0)
                        color.a *= edge.Backbone ? 0.35f : 0.16f;
                }

                _edgeGraphic.SetSegmentRange(edge.SegmentStart, edge.SegmentCount, color, enabled, selected);
            }

            _edgeGraphic.SetVerticesDirty();
        }

        internal void HandleScrollDelta(float wheel)
        {
            if (!_visible || _viewport == null || _graphCanvas == null || _builtPage < 0)
                return;
            if (Mathf.Abs(wheel) < 0.001f)
                return;

            float oldZoom = _pageZoom[_builtPage];
            float factor = wheel > 0f ? 1.15f : (1f / 1.15f);
            float newZoom = Mathf.Clamp(oldZoom * factor, MinZoom, MaxZoom);
            if (Mathf.Abs(newZoom - oldZoom) < 0.0001f)
                return;

            // Preserve the point currently under the viewport centre, just as vanilla
            // keeps the graph visually anchored while changing its transform scale.
            Vector2 viewportCentre = new Vector2(_viewport.rect.width * 0.5f, -_viewport.rect.height * 0.5f);
            Vector2 oldPan = _pagePan[_builtPage];
            Vector2 localPoint = (viewportCentre - oldPan) / Mathf.Max(0.0001f, oldZoom);

            _pageZoom[_builtPage] = newZoom;
            _zoom = newZoom;
            _pagePan[_builtPage] = viewportCentre - localPoint * newZoom;

            ApplyPageTransform(_builtPage);
        }

        internal void HandlePanDelta(Vector2 delta)
        {
            if (!_visible || _builtPage < 0 || _builtPage >= 2)
                return;

            _pagePan[_builtPage] += delta;
            ApplyPageTransform(_builtPage);
        }

        private void InitializePageView(int page)
        {
            if (page < 0 || page >= 2 || _pageGraphCanvas[page] == null)
                return;

            _pageViewInitialized[page] = true;

            // If DSPModSave supplied non-default view state, preserve it. For a new
            // save/default state, retain the established vanilla-ish initial framing.
            TechTreeSavedViewState saved = Plugin.SavedViewState;
            bool hasSavedView =
                saved != null &&
                (Mathf.Abs(saved.Zoom[page] - 0.72f) > 0.0001f ||
                 saved.Pan[page].sqrMagnitude > 0.0001f);

            if (hasSavedView)
            {
                _pageZoom[page] = Mathf.Clamp(_pageZoom[page], MinZoom, MaxZoom);
                _pagePan[page] = ClampPagePan(page, _pagePan[page], _pageZoom[page]);
            }
            else
            {
                _pageZoom[page] = 0.72f;

                float viewportW = _viewport != null ? _viewport.rect.width : 0f;
                float viewportH = _viewport != null ? _viewport.rect.height : 0f;
                float scaledW = _pageGraphSize[page].x * _pageZoom[page];
                float scaledH = _pageGraphSize[page].y * _pageZoom[page];

                float overflowX = Mathf.Max(0f, scaledW - viewportW);
                float overflowY = Mathf.Max(0f, scaledH - viewportH);

                // Match the old prototype/vanilla-ish initial framing without requiring
                // ScrollRect normalized-position calculations.
                _pagePan[page] = new Vector2(-overflowX * 0.35f, overflowY * 0.50f);
            }

            if (page == _builtPage)
            {
                _zoom = _pageZoom[page];
                ApplyPageTransform(page);
            }
        }

        private void ApplyPageTransform(int page)
        {
            if (page < 0 || page >= 2)
                return;

            RectTransform canvas = _pageGraphCanvas[page];
            if (canvas == null)
                return;

            float zoom = _pageZoom[page];
            Vector2 pan = ClampPagePan(page, _pagePan[page], zoom);
            _pagePan[page] = pan;

            canvas.localScale = new Vector3(zoom, zoom, 1f);
            canvas.anchoredPosition = pan;

            // Let level labels shrink almost with the graph, but keep them 0.10 larger
            // in screen-space for readability. Cap them at 110% so maximum graph zoom
            // does not make the labels disproportionately large.
            float targetLabelScreenScale = Mathf.Min(zoom + 0.10f, 1.10f);
            float labelCompensation = targetLabelScreenScale / Mathf.Max(0.0001f, zoom);
            foreach (NodeView node in _pageNodes[page].Values)
            {
                if (node == null)
                    continue;

                Vector3 labelScale =
                    new Vector3(labelCompensation, labelCompensation, 1f);

                if (node.LevelBadge != null)
                    node.LevelBadge.transform.localScale = labelScale;
            }

            if (page == _builtPage)
            {
                _graphCanvas = canvas;
                _zoom = zoom;
            }
        }

        private Vector2 ClampPagePan(int page, Vector2 pan, float zoom)
        {
            if (_viewport == null)
                return pan;

            float viewportW = _viewport.rect.width;
            float viewportH = _viewport.rect.height;

            bool filtered = _matrixFilter >= 0 || _combatFilter != 0 || (_builtPage == 1 && _infiniteOnly);
            if (filtered && page >= 0 && page < 2 && _pageVisibleBoundsValid[page])
            {
                Vector4 bounds = _pageVisibleBounds[page];
                float left = bounds.x;
                float right = bounds.y;
                float top = bounds.z;
                float bottom = bounds.w;

                // Filtered views should not retain the enormous overscroll area required
                // by the full vanilla tree. Keep modest breathing room for overlay panels.
                const float FilterSafeLeft = PanSafeLeft;
                const float FilterSafeRight = PanSafeRight;
                const float FilterSafeTop = 110f;
                const float FilterSafeBottom = 90f;

                float minX = viewportW - right * zoom - FilterSafeRight;
                float maxX = -left * zoom + FilterSafeLeft;
                float minY = -FilterSafeTop - top * zoom;
                float maxY = -viewportH + FilterSafeBottom - bottom * zoom;

                // If the surviving content is smaller than the viewport, keep its centre
                // near the viewport centre but still allow a little manual movement.
                float contentW = (right - left) * zoom;
                float contentH = (top - bottom) * zoom;

                if (contentW <= viewportW)
                {
                    float centredX = viewportW * 0.5f - (left + right) * 0.5f * zoom;
                    minX = centredX - FilterSafeRight;
                    maxX = centredX + FilterSafeLeft;
                }

                if (contentH <= viewportH)
                {
                    float centredY = -viewportH * 0.5f - (top + bottom) * 0.5f * zoom;
                    minY = centredY - FilterSafeTop;
                    maxY = centredY + FilterSafeBottom;
                }

                pan.x = Mathf.Clamp(pan.x, minX, maxX);
                pan.y = Mathf.Clamp(pan.y, minY, maxY);
                return pan;
            }

            float graphW = _pageGraphSize[page].x * zoom;
            float graphH = _pageGraphSize[page].y * zoom;

            // Do not clamp exactly to the graph's raw edge. DSP's left/right/top
            // panels overlay the graph viewport, so exact clamping can leave edge nodes
            // permanently trapped underneath UI (Antimatter Capsule was the obvious case).
            float fullMinX = viewportW - graphW - PanSafeRight;
            float fullMaxX = PanSafeLeft;
            float fullMinY = -PanSafeTop;
            float fullMaxY = graphH - viewportH + PanSafeBottom;

            if (graphW <= viewportW)
            {
                float centredX = (viewportW - graphW) * 0.5f;
                fullMinX = centredX - PanSafeRight;
                fullMaxX = centredX + PanSafeLeft;
            }

            if (graphH <= viewportH)
            {
                float centredY = -(viewportH - graphH) * 0.5f;
                fullMinY = centredY - PanSafeTop;
                fullMaxY = centredY + PanSafeBottom;
            }

            pan.x = Mathf.Clamp(pan.x, fullMinX, fullMaxX);
            pan.y = Mathf.Clamp(pan.y, fullMinY, fullMaxY);

            return pan;
        }

        private void UpdateDetailPanelPosition()
        {
            if (_detailPanel == null || _tree == null)
                return;

            // UITechTree itself moves showPropertyRect 350 px left on the Upgrades
            // page to make room for the Current upgrade data panel. Reuse that live
            // offset so our details panel follows the vanilla right-side layout.
            float vanillaShift = 0f;
            if (_tree.showPropertyRect != null)
                vanillaShift = _tree.showPropertyRect.anchoredPosition.x;
            else if (_tree.page == 1)
                vanillaShift = -350f;

            // Sit immediately to the left of the metadata strip. This remains stable
            // whether the metadata contents are expanded or hidden, because the toggle
            // changes the group's visibility/height rather than this horizontal anchor.
            _detailPanel.anchoredPosition = new Vector2(vanillaShift - 175f, -8f);
        }

        private static string ResolveKeyTokens(string text)
        {
            if (string.IsNullOrEmpty(text) || DSPGame.key == null || DSPGame.key.builtinKeys == null)
                return text;

            text = ResolveKeyTokenPrefix(text, "$LKEY_");
            // Defensive fallback for strings that have already lost the '$' marker in
            // some localization/font pipelines.
            text = ResolveKeyTokenPrefix(text, "SLKEY_");
            return text;
        }

        private static string ResolveKeyTokenPrefix(string text, string prefix)
        {
            int searchFrom = 0;
            while (searchFrom < text.Length)
            {
                int start = text.IndexOf(prefix, searchFrom, StringComparison.Ordinal);
                if (start < 0)
                    break;

                int numberStart = start + prefix.Length;
                int end = text.IndexOf(';', numberStart);
                if (end < 0)
                    break;

                int id;
                if (!int.TryParse(text.Substring(numberStart, end - numberStart), out id))
                {
                    searchFrom = end + 1;
                    continue;
                }

                string replacement = GetCurrentKeyText(id);
                if (string.IsNullOrEmpty(replacement))
                {
                    searchFrom = end + 1;
                    continue;
                }

                text = text.Substring(0, start) + replacement + text.Substring(end + 1);
                searchFrom = start + replacement.Length;
            }

            return text;
        }

        private static string GetCurrentKeyText(int id)
        {
            BuiltinKey[] builtin = DSPGame.key.builtinKeys;
            for (int i = 0; i < builtin.Length; i++)
            {
                if (builtin[i].id != id)
                    continue;

                CombineKey key = builtin[i].key;
                if (DSPGame.globalOption.overrideKeys != null &&
                    id >= 0 && id < DSPGame.globalOption.overrideKeys.Length)
                {
                    CombineKey overrideKey = DSPGame.globalOption.overrideKeys[id];
                    if (!overrideKey.IsNull())
                        key = overrideKey;
                }

                return key.ToTokenString(0);
            }

            return null;
        }

        private void RefreshDetailProgress()
        {
            if (_detailProgressGroup == null ||
                _detailProgressFill == null ||
                _detailProgressText == null ||
                _detailProgressSpeedText == null ||
                GameMain.history == null)
                return;

            if (_selectedTechId == 0 ||
                GameMain.history.currentTech != _selectedTechId)
            {
                _detailProgressGroup.SetActive(false);
                return;
            }

            TechState state = GameMain.history.TechState(_selectedTechId);
            if (state.unlocked || state.hashNeeded <= 0L)
            {
                _detailProgressGroup.SetActive(false);
                return;
            }

            _detailProgressGroup.SetActive(true);

            float progress = Mathf.Clamp01(
                (float)((double)state.hashUploaded / (double)state.hashNeeded));

            RectTransform fillRect = _detailProgressFill.rectTransform;
            fillRect.anchorMax = new Vector2(progress, 1f);
            fillRect.offsetMax = Vector2.zero;

            GameStatData statistics = GameMain.statistics;
            int instantaneousHashRate = statistics != null
                ? statistics.techHashedThisFrame * 60
                : 0;

            int hashRate = instantaneousHashRate;
            if (statistics != null &&
                statistics.techHashedRecorded >= 60 &&
                statistics.techHashedHistory != null)
            {
                hashRate = 0;
                int sampleCount = Mathf.Min(60, statistics.techHashedHistory.Length);
                for (int i = 0; i < sampleCount; i++)
                    hashRate += statistics.techHashedHistory[i];
            }

            StringBuilder rateBuilder = new StringBuilder("         ", 12);
            StringBuilderUtility.WriteKMG1000(
                rateBuilder,
                8,
                (long)Mathf.Max(0, hashRate),
                false,
                '\u2009',
                ' ');

            long remainingHashes = Math.Max(0L, state.hashNeeded - state.hashUploaded);

            if (instantaneousHashRate > 0 && hashRate > 0)
            {
                long secondsLong = remainingHashes / (long)hashRate;
                int seconds = secondsLong > int.MaxValue ? int.MaxValue : (int)secondsLong;

                if (seconds < 60)
                {
                    _detailProgressSpeedText.text =
                        "Hashrate " + rateBuilder.ToString().TrimStart() +
                        " Hash/s  ~  " + seconds + " sec";
                }
                else
                {
                    int minutes = seconds / 60;
                    int remainderSeconds = seconds % 60;
                    _detailProgressSpeedText.text =
                        "Hashrate " + rateBuilder.ToString().TrimStart() +
                        " Hash/s  ~  " + minutes + ":" + remainderSeconds.ToString("00");
                }
            }
            else
            {
                _detailProgressSpeedText.text = "";
            }

            StringBuilder uploadedBuilder = new StringBuilder("         ", 12);
            StringBuilder neededBuilder = new StringBuilder("         ", 12);
            StringBuilderUtility.WriteKMG(
                uploadedBuilder,
                8,
                state.hashUploaded,
                false,
                '\u2009',
                ' ');
            StringBuilderUtility.WriteKMG(
                neededBuilder,
                8,
                state.hashNeeded,
                false,
                '\u2009',
                ' ');

            _detailProgressText.text =
                uploadedBuilder.ToString() +
                " / " +
                neededBuilder.ToString().TrimStart() +
                " Hashes";
        }

        private void SetDetailPanelExpanded(bool expanded)
        {
            if (_detailPanel == null)
                return;

            _detailPanel.sizeDelta = expanded
                ? new Vector2(DetailWidth, DetailHeight)
                : new Vector2(DetailCollapsedWidth, DetailCollapsedHeight);

            for (int i = 0; i < _detailPanel.childCount; i++)
            {
                Transform child = _detailPanel.GetChild(i);
                if (child == null)
                    continue;

                bool isPrompt = child.gameObject == (_detailEmptyPrompt != null
                    ? _detailEmptyPrompt.gameObject
                    : null);

                child.gameObject.SetActive(isPrompt ? !expanded : expanded);
            }

            // The live research block is conditionally visible even when the full
            // details panel is expanded, so restore its actual state after the
            // generic child visibility pass.
            if (expanded)
                RefreshDetailProgress();
        }

        private void PopulateDetails(int techId)
        {
            SetDetailPanelExpanded(true);

            TechProto proto = LDB.techs.Select(techId);
            if (proto == null)
            {
                ClearDetails();
                return;
            }

            TechState state = GameMain.history.TechState(techId);
            _detailIcon.sprite = proto.iconSprite;
            _detailIcon.color = Color.white;
            _detailTitle.text = proto.name;
            string levelText = FormatLevel(proto, state);
            _detailLevel.text = string.IsNullOrEmpty(levelText) ? "" : ("Level  " + levelText);

            bool complete = state.unlocked;
            bool ready = !complete && IsTechReadyByMap(techId);
            if (_detailAccent != null)
                _detailAccent.color = complete ? CompleteColor : (ready ? ReadyColor : LockedColor);
            if (_detailStatus != null)
            {
                if (complete)
                {
                    _detailStatus.text = "<color=#72AFFF>COMPLETED</color>";
                }
                else if (ready)
                {
                    _detailStatus.text = "<color=#62D67C>RESEARCHABLE</color>";
                }
                else
                {
                    _detailStatus.text = "<color=#FFAA32>LOCKED</color>";
                }
            }

            PopulateDetailIconRows(proto, state);

            StringBuilder sb = new StringBuilder(2048);

            long hashNeeded = proto.GetHashNeeded(state.curLevel);
            sb.Append("<color=#F2A65A>DATA VOLUME</color>  <color=#FFFFFF>");
            sb.Append(FormatHashCount(hashNeeded));
            sb.AppendLine(" Hashes</color>");

            string effectText = proto.UnlockFunctionText(new StringBuilder("         ", 12));
            if (!string.IsNullOrEmpty(effectText))
            {
                sb.Append("<color=#F2A65A>EFFECT</color>  <color=#FFD17A>");
                sb.Append(effectText);
                sb.AppendLine("</color>");
            }

            if (!string.IsNullOrEmpty(proto.description))
            {
                sb.Append("<color=#F2A65A>DESCRIPTION</color>  <color=#E1E6EB>");
                sb.Append(ResolveKeyTokens(proto.description));
                sb.AppendLine("</color>");
            }

            int effectiveTier = GetEffectiveMatrixTier(proto.ID, new HashSet<int>());
            sb.Append("<color=#F2A65A>TIER</color>  <color=#D7E2ED>");
            sb.Append(effectiveTier < 0 ? "None" : MatrixName(effectiveTier));
            sb.AppendLine("</color>");

            AppendPrerequisiteSection(sb, "PREREQUISITES", proto.PreTechs, false);
            AppendPrerequisiteSection(sb, "IMPLICIT", proto.PreTechsImplicit, true);

            _detailBody.text = sb.ToString();
            RefreshDetailProgress();
            RefreshResearchButton();
        }

        private static void AppendSectionHeading(StringBuilder sb, string heading)
        {
            sb.Append("<color=#F2A65A>");
            sb.Append(heading);
            sb.AppendLine("</color>");
        }

        private void PopulateDetailIconRows(TechProto proto, TechState state)
        {
            ClearDetailIconRows();

            if (_unlockHeading != null)
                _unlockHeading.gameObject.SetActive(false);
            if (_researchCostHeading != null)
                _researchCostHeading.gameObject.SetActive(false);

            if (proto == null)
                return;

            // Vanilla tech cards show the produced item(s) for unlocked recipes rather
            // than only the recipe name. Do the same here, and attach DSP's own item tip.
            List<int> unlockItemIds = new List<int>();
            if (proto.unlockRecipeArray != null)
            {
                for (int i = 0; i < proto.unlockRecipeArray.Length; i++)
                {
                    RecipeProto recipe = proto.unlockRecipeArray[i];
                    if (recipe == null || recipe.Results == null)
                        continue;

                    for (int r = 0; r < recipe.Results.Length; r++)
                    {
                        int itemId = recipe.Results[r];
                        if (itemId > 0 && !unlockItemIds.Contains(itemId))
                            unlockItemIds.Add(itemId);
                    }
                }
            }

            if (unlockItemIds.Count > 0 && _unlockRow != null)
            {
                if (_unlockHeading != null)
                    _unlockHeading.gameObject.SetActive(true);

                float x = 0f;
                for (int i = 0; i < unlockItemIds.Count && i < 8; i++)
                {
                    ItemProto item = LDB.items.Select(unlockItemIds[i]);
                    if (item == null)
                        continue;

                    CreateDetailItemIcon(_unlockRow, item, 0L, x, false);
                    x += 58f;
                }
            }

            if (proto.itemArray != null && proto.itemArray.Length > 0 && _researchCostRow != null)
            {
                if (_researchCostHeading != null)
                    _researchCostHeading.gameObject.SetActive(true);

                long hashNeeded = proto.GetHashNeeded(state.curLevel);
                float x = 0f;
                for (int i = 0; i < proto.itemArray.Length && i < 8; i++)
                {
                    ItemProto item = proto.itemArray[i];
                    if (item == null)
                        continue;

                    long count = hashNeeded * (long)proto.ItemPoints[i] / TechProto.kPointPerItem;
                    CreateDetailItemIcon(_researchCostRow, item, count, x, true);
                    x += 56f;
                }
            }
        }

        private void ClearDetailIconRows()
        {
            ClearChildren(_unlockRow);
            ClearChildren(_researchCostRow);
        }

        private static void ClearChildren(RectTransform parent)
        {
            if (parent == null)
                return;

            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        private void CreateDetailItemIcon(RectTransform parent, ItemProto item, long count, float x, bool showCount)
        {
            if (parent == null || item == null)
                return;

            GameObject iconObj = CreateUIObject("Item_" + item.ID, parent);
            RectTransform rect = iconObj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(50f, 58f);
            rect.anchoredPosition = new Vector2(x, 0f);

            UnityEngine.UI.Image iconBg = iconObj.AddComponent<UnityEngine.UI.Image>();
            iconBg.color = new Color(0.08f, 0.12f, 0.15f, 0.92f);
            iconBg.raycastTarget = true;

            UnityEngine.UI.Image icon = CreateImage("Icon", rect, new Vector2(42f, 42f));
            icon.sprite = item.iconSprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            icon.rectTransform.pivot = new Vector2(0.5f, 1f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, -3f);

            if (showCount)
            {
                UnityEngine.UI.Text countText = CreateText("Count", rect, 11, UnityEngine.TextAnchor.LowerCenter);
                countText.raycastTarget = false;
                countText.fontStyle = UnityEngine.FontStyle.Bold;
                countText.color = Color.white;
                countText.rectTransform.anchorMin = new Vector2(0f, 0f);
                countText.rectTransform.anchorMax = new Vector2(1f, 0f);
                countText.rectTransform.pivot = new Vector2(0.5f, 0f);
                countText.rectTransform.offsetMin = new Vector2(0f, 1f);
                countText.rectTransform.offsetMax = new Vector2(0f, 17f);
                countText.text = FormatItemCount(count);
            }

            // Reuse DSP's own hover system. UIButton will create/update UIItemTip,
            // so these icons get the same item tooltip as vanilla inventory/tech UI.
            UIButton tipButton = iconObj.AddComponent<UIButton>();

            // UIButton instances normally come from DSP prefabs, where Unity's inspector
            // serializes an empty/non-null transitions array. A component created at runtime
            // has transitions == null, but UIButton.LateUpdate()/pointer handlers iterate
            // transitions.Length without a null check. Initialise it exactly enough for a
            // tooltip-only button before Start/LateUpdate run.
            tipButton.transitions = new UIButton.Transition[0];

            tipButton.tips.itemId = item.ID;
            tipButton.tips.itemCount = showCount && count <= int.MaxValue ? (int)count : 0;
            tipButton.tips.itemInc = 0;
            tipButton.tips.type = UIButton.ItemTipType.Item;
            tipButton.tips.corner = 3;
            tipButton.tips.offset = new Vector2(6f, -4f);
            tipButton.tips.delay = 0.10f;
            tipButton.tips.level = 0;
        }

        private static string FormatItemCount(long value)
        {
            return value.ToString();
        }

        private static string FormatHashCount(long value)
        {
            StringBuilder sb = new StringBuilder("         ", 12);
            StringBuilderUtility.WriteKMG(
                sb,
                8,
                value,
                false,
                '\u2009',
                ' ');
            return sb.ToString().TrimStart();
        }

        private void AppendPrerequisiteSection(StringBuilder sb, string heading, int[] ids, bool implicitPrereq)
        {
            if (ids == null || ids.Length == 0)
                return;

            AppendSectionHeading(sb, heading);
            for (int i = 0; i < ids.Length; i++)
            {
                TechProto p = LDB.techs.Select(ids[i]);
                if (p == null)
                    continue;

                bool done = GameMain.history.TechUnlocked(ids[i], false);
                sb.Append(done ? "<color=#72AFFF>✓</color>  " : "<color=#FFAA32>✕</color>  ");
                sb.Append("<color=#E8EDF2>");
                sb.Append(p.name);
                sb.Append("</color>");
                if (implicitPrereq)
                    sb.Append("  <color=#9EA9B5>(implicit)</color>");
                sb.AppendLine();
            }
        }

        private void ClearDetails()
        {
            if (_detailIcon != null)
            {
                _detailIcon.sprite = null;
                _detailIcon.color = new Color(1f, 1f, 1f, 0f);
            }
            if (_detailTitle != null)
                _detailTitle.text = "";
            if (_detailLevel != null)
                _detailLevel.text = "";
            if (_detailStatus != null)
                _detailStatus.text = "";
            if (_detailAccent != null)
                _detailAccent.color = SelectedColor;
            if (_detailProgressGroup != null)
                _detailProgressGroup.SetActive(false);
            ClearDetailIconRows();
            if (_unlockHeading != null)
                _unlockHeading.gameObject.SetActive(false);
            if (_researchCostHeading != null)
                _researchCostHeading.gameObject.SetActive(false);
            if (_detailBody != null)
                _detailBody.text = "";

            RefreshResearchButton();
            SetDetailPanelExpanded(false);
        }

        private int GetDirectMatrixMask(TechProto proto)
        {
            int mask = 0;
            if (proto == null || proto.Items == null)
                return mask;

            for (int i = 0; i < proto.Items.Length; i++)
            {
                for (int m = 0; m < MatrixIds.Length; m++)
                {
                    if (proto.Items[i] == MatrixIds[m])
                    {
                        mask |= 1 << m;
                        break;
                    }
                }
            }
            return mask;
        }

        private int GetEffectiveMatrixTier(int techId, HashSet<int> visited)
        {
            TechProto proto = LDB.techs.Select(techId);
            if (proto == null || proto.unlockNeedItemArray == null)
                return -1;

            int tier = -1;

            for (int i = 0; i < proto.unlockNeedItemArray.Length; i++)
            {
                int itemId = proto.unlockNeedItemArray[i].id;

                for (int m = 0; m < MatrixIds.Length; m++)
                {
                    if (itemId == MatrixIds[m])
                    {
                        tier = Mathf.Max(tier, m);
                        break;
                    }
                }
            }

            return tier;
        }

        private string MatrixName(int tier)
        {
            if (tier < 0 || tier >= MatrixIds.Length)
                return "Unknown";
            ItemProto item = LDB.items.Select(MatrixIds[tier]);
            return item != null ? item.name : ("Matrix " + (tier + 1));
        }

        private bool ShouldShowNodeLevelBadge(TechProto proto, TechState state)
        {
            if (proto == null)
                return false;

            // Technologies are overwhelmingly single-level. Keep their nodes clean and
            // only show a badge for a genuinely multi-level technology such as
            // Dyson Sphere Stress System.
            if (proto.ID <= 1999)
                return state.maxLevel > 1 || proto.Level != proto.MaxLevel;

            // Upgrade tier I is visually obvious from being the first node in its row and
            // gains nothing from an extra badge. Separate tier records from II onward do.
            if (proto.Level > 1)
                return true;

            // Genuine repeatable/infinite upgrades may be represented by one TechProto.
            // Hide the badge while it is still at the first level, then show progress once
            // the player has advanced beyond that.
            if (state.maxLevel > 1 && state.curLevel > 1)
                return true;

            return false;
        }

        private string FormatLevel(TechProto proto, TechState state)
        {
            if (proto == null)
                return "";

            // Many vanilla upgrade tiers are separate TechProto records where
            // Level == MaxLevel (for example Distribution Range II is 2/2 in
            // tech data before it has been researched). Showing 2/2 there
            // falsely looks like completion, so show only the tier number.
            if (proto.Level == proto.MaxLevel && proto.MaxLevel > 1)
                return "Lv " + proto.Level;

            // Genuine multi-level/repeatable records span a level range. DSP's
            // curLevel is the level currently being worked on, not the number already
            // completed. Show completed levels so 0/6 means none completed, 5/6 means
            // the final level is next/in progress, and 6/6 means fully complete.
            if (state.maxLevel > 1 || proto.Level != proto.MaxLevel)
            {
                int completedLevel = state.unlocked
                    ? state.curLevel
                    : Mathf.Max(0, state.curLevel - 1);

                string max = state.maxLevel >= 10000 ? "∞" : state.maxLevel.ToString();
                return completedLevel + "/" + max;
            }

            return state.unlocked ? "✓" : "";
        }

        private void RestorePipColors(NodeView node)
        {
            if (node == null || node.Pips == null)
                return;

            int index = 0;
            for (int i = 0; i < MatrixIds.Length; i++)
            {
                if ((node.DirectMatrixMask & (1 << i)) == 0)
                    continue;
                if (index < node.Pips.Length && node.Pips[index] != null)
                    node.Pips[index].color = MatrixColors[i];
                index++;
            }
        }

        private void OnResearchButtonClick()
        {
            if (_selectedTechId == 0)
                return;

            QueueTechFromNode(_selectedTechId);
        }

        private void RefreshResearchButton()
        {
            if (_researchButton == null || _researchButtonText == null)
                return;

            if (_selectedTechId == 0 || GameMain.history == null)
            {
                _researchButton.interactable = false;
                _researchButtonText.text = "Select a technology";
                return;
            }

            bool complete = GameMain.history.TechUnlocked(_selectedTechId);
            bool mapReady = IsTechReadyByMap(_selectedTechId);
            bool canQueue = mapReady && GameMain.history.CanEnqueueTech(_selectedTechId);
            int queued = GameMain.history.TechQueuedCount(_selectedTechId);

            if (complete)
            {
                _researchButton.interactable = false;
                _researchButtonText.text = "Completed";
            }
            else if (!mapReady)
            {
                _researchButton.interactable = false;
                _researchButtonText.text = "Prerequisite locked";
            }
            else if (queued > 0)
            {
                _researchButton.interactable = canQueue;
                _researchButtonText.text = canQueue ? "Queue another level" : "Queued";
            }
            else if (canQueue)
            {
                _researchButton.interactable = true;
                _researchButtonText.text = "Research";
            }
            else
            {
                // The dependency map says the tech is valid, so the remaining reason
                // vanilla refuses it is normally queue capacity.
                _researchButton.interactable = false;
                _researchButtonText.text = "Research queue full";
            }
        }

        private void HideVanillaGraphs()
        {
            if (_tree.graphGroup0 != null)
                _tree.graphGroup0.gameObject.SetActive(false);
            if (_tree.graphGroup1 != null)
                _tree.graphGroup1.gameObject.SetActive(false);
        }

        private void RestoreVanillaGraphs()
        {
            if (_tree == null)
                return;
            if (_tree.graphGroup0 != null)
                _tree.graphGroup0.gameObject.SetActive(_tree.page == 0);
            if (_tree.graphGroup1 != null)
                _tree.graphGroup1.gameObject.SetActive(_tree.page == 1);
        }

        private void OnDestroy()
        {
            RestoreVanillaGraphs();
        }

        private static long EdgeKey(int fromId, int toId)
        {
            return ((long)fromId << 32) ^ (uint)toId;
        }

        private static int HighestBit(int mask)
        {
            for (int i = 31; i >= 0; i--)
            {
                if ((mask & (1 << i)) != 0)
                    return i;
            }
            return -1;
        }

        private static int CountBits(int mask)
        {
            int count = 0;
            while (mask != 0)
            {
                count += mask & 1;
                mask >>= 1;
            }
            return count;
        }

        private GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.layer = parent.gameObject.layer;
            obj.transform.SetParent(parent, false);
            return obj;
        }

        private UnityEngine.UI.Image CreateImage(string name, Transform parent, Vector2 size)
        {
            GameObject obj = CreateUIObject(name, parent);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            return obj.AddComponent<UnityEngine.UI.Image>();
        }

        private UnityEngine.UI.Text CreateText(string name, Transform parent, int size, UnityEngine.TextAnchor anchor)
        {
            GameObject obj = CreateUIObject(name, parent);
            UnityEngine.UI.Text text = obj.AddComponent<UnityEngine.UI.Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = true;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private sealed class NodeView
        {
            internal TechProto Proto;
            internal RectTransform Root;
            internal Vector2 OriginalPosition;
            internal UnityEngine.UI.Image Border;
            internal UnityEngine.UI.Image Inner;
            internal UnityEngine.UI.Image Icon;
            internal UnityEngine.UI.Text Level;
            internal GameObject LevelBadge;
            internal UnityEngine.CanvasGroup CanvasGroup;
            internal int DirectMatrixMask;
            internal UnityEngine.UI.Image[] Pips;
            internal GameObject ResearchWing;
            internal RectTransform ResearchWingRect;
            internal UnityEngine.UI.Button ResearchWingButton;
            internal TechTreeResearchWingGraphic ResearchWingGraphic;
            internal UnityEngine.CanvasGroup ResearchWingCanvas;
            internal bool ResearchWingOpen;
            internal bool Hovered;
        }

        private sealed class EdgeView
        {
            internal int FromId;
            internal int ToId;
            internal bool Implicit;
            internal bool Inferred;
            internal bool Backbone;
            internal int SegmentStart;
            internal int SegmentCount;
        }
    }

    internal sealed class TechTreeEdgeGraphic : UnityEngine.UI.Graphic
    {
        private struct Segment
        {
            internal Vector2 A;
            internal Vector2 B;
            internal float Thickness;
            internal Color Color;
            internal bool Enabled;
            internal bool Selected;
            internal bool Backbone;
        }

        private readonly List<Segment> _segments = new List<Segment>();

        internal int SegmentCount
        {
            get { return _segments.Count; }
        }

        internal void ClearSegments()
        {
            _segments.Clear();
            SetVerticesDirty();
        }

        internal void AddSegment(
            Vector2 a,
            Vector2 b,
            float thickness,
            Color color,
            bool enabled,
            bool backbone)
        {
            _segments.Add(new Segment
            {
                A = a,
                B = b,
                Thickness = thickness,
                Color = color,
                Enabled = enabled,
                Selected = false,
                Backbone = backbone
            });
        }

        internal void SetSegmentRange(int start, int count, Color color, bool enabled, bool selected)
        {
            int end = Mathf.Min(_segments.Count, start + count);
            for (int i = Mathf.Max(0, start); i < end; i++)
            {
                Segment s = _segments[i];
                s.Color = color;
                s.Enabled = enabled;
                s.Selected = selected;
                _segments[i] = s;
            }
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();

            // Explicit visual hierarchy:
            //   1. ordinary dependencies
            //   2. selected ordinary dependencies
            //   3. main research spine
            //   4. selected main research spine
            //
            // This keeps the mainline visually continuous at crossings while still
            // preserving selected-path emphasis within each class.
            AppendSegments(vh, false, false);
            AppendSegments(vh, true, false);
            AppendSegments(vh, false, true);
            AppendSegments(vh, true, true);
        }

        private void AppendSegments(
            UnityEngine.UI.VertexHelper vh,
            bool selectedPass,
            bool backbonePass)
        {
            const float overlap = 1.25f;

            for (int i = 0; i < _segments.Count; i++)
            {
                Segment s = _segments[i];
                if (!s.Enabled ||
                    s.Selected != selectedPass ||
                    s.Backbone != backbonePass)
                    continue;

                Vector2 a = s.A;
                Vector2 b = s.B;
                Vector2 d = b - a;

                Vector2 min;
                Vector2 max;

                if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
                {
                    float x0 = Mathf.Min(a.x, b.x) - overlap;
                    float x1 = Mathf.Max(a.x, b.x) + overlap;
                    float y = (a.y + b.y) * 0.5f;
                    float half = s.Thickness * 0.5f;
                    min = new Vector2(x0, y - half);
                    max = new Vector2(x1, y + half);
                }
                else
                {
                    float y0 = Mathf.Min(a.y, b.y) - overlap;
                    float y1 = Mathf.Max(a.y, b.y) + overlap;
                    float x = (a.x + b.x) * 0.5f;
                    float half = s.Thickness * 0.5f;
                    min = new Vector2(x - half, y0);
                    max = new Vector2(x + half, y1);
                }

                int baseIndex = vh.currentVertCount;
                Color32 c = s.Color;

                vh.AddVert(new Vector3(min.x, min.y, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(min.x, max.y, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(max.x, max.y, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(max.x, min.y, 0f), c, Vector2.zero);

                vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
                vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
            }
        }
    }
}