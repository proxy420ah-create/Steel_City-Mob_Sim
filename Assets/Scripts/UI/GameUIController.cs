using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SteelCity.UI;

namespace SteelCity.Sim
{
    /// <summary>
    /// Controller for the Gang Organizer UI.
    /// The city map is a 3D scene (CityMap3D) rendered by its own camera.
    /// The left-side info panel, top bar, and bottom bar are built by
    /// GameUIAutoBuilder (Editor menu: Steel City -> Build Game UI) or by hand
    /// in the Canvas. This script only needs the resulting references wired
    /// up in the Inspector (the auto-builder does this for you).
    /// </summary>
    public class GameUIController : MonoBehaviour
    {
        [Header("=== 3D CITY MAP ===")]
        [SerializeField] private CityMap3D cityMap;

        [Header("=== TOP BAR ===")]
        [SerializeField] private TMP_Text weekText;
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private TMP_Text treasuryText;
        [SerializeField] private TMP_Text characterStatusText;

        [Header("=== INFO PANEL CONTAINERS ===")]
        [SerializeField] private Transform hoodList;
        [SerializeField] private Transform blockInfoContent;
        [SerializeField] private Transform financeContent;
        [SerializeField] private Transform policeContent;
        [SerializeField] private Transform investigationContent;

        [Header("=== INFO PANEL TABS ===")]
        [SerializeField] private GameObject hoodsPage;
        [SerializeField] private GameObject blockInfoPage;
        [SerializeField] private GameObject ordersPage;
        [SerializeField] private GameObject financePage;
        [SerializeField] private GameObject policePage;
        [SerializeField] private GameObject investigationPage;
        [SerializeField] private GameObject eventLogPage;
        [SerializeField] private Button hoodsTabButton;
        [SerializeField] private Button blockInfoTabButton;
        [SerializeField] private Button ordersTabButton;
        [SerializeField] private Button financeTabButton;
        [SerializeField] private Button policeTabButton;
        [SerializeField] private Button investigationTabButton;
        [SerializeField] private Button eventLogTabButton;
        [SerializeField] private Color tabActiveColor = new(0.886f, 0.690f, 0.290f);
        [SerializeField] private Color tabInactiveColor = new(0.094f, 0.094f, 0.157f);

        [Header("=== ORDER BUTTONS ===")]
        [SerializeField] private Button extortButton;
        [SerializeField] private Button collectButton;
        [SerializeField] private Button patrolButton;
        [SerializeField] private Button intimidateButton;
        [SerializeField] private Button lieLowButton;

        [Header("=== BOTTOM BAR ===")]
        [SerializeField] private Transform eventLogContent;
        [SerializeField] private Button runWeekButton;

        [Header("=== PLANNING UI ROOTS (for execution phase hide/show) ===")]
        [SerializeField] private GameObject topBarRoot;
        [SerializeField] private GameObject infoPanelRoot;
        [SerializeField] private GameObject bottomBarRoot;

        [Header("=== FPS COUNTER ===")]
        private float fpsAccumTime;
        private int fpsAccumFrames;
        private float fpsUpdateInterval = 0.5f;

        [Header("=== PREFABS (optional) ===")]
        [Tooltip("Prefab for a hood card. Needs Button+Image + child TMP_Text 'HoodName', 'HoodSkills', optional 'HoodOrder'.")]
        [SerializeField] private GameObject hoodCardPrefab;

        [Header("=== CONFIG ===")]
        [SerializeField] private int randomSeed = -1;

        [Header("=== COLORS ===")]
        [SerializeField] private Color playerColor = new(0.29f, 0.62f, 1.0f);
        [SerializeField] private Color rivalColor = new(1.0f, 0.29f, 0.29f);
        [SerializeField] private Color unownedColor = new(0.23f, 0.23f, 0.31f);
        [SerializeField] private Color policeColor = new(0.29f, 1.0f, 0.62f);
        [SerializeField] private Color goldColor = new(0.89f, 0.69f, 0.29f);
        [SerializeField] private Color greenColor = new(0.29f, 0.86f, 0.46f);
        [SerializeField] private Color yellowColor = new(0.98f, 0.82f, 0.29f);
        [SerializeField] private Color redColor = new(0.92f, 0.29f, 0.29f);
        [SerializeField] private Color mutedColor = new(0.53f, 0.53f, 0.62f);
        [SerializeField] private Color textBright = new(0.87f, 0.87f, 0.91f);
        [SerializeField] private Color cardBgColor = new(0.09f, 0.09f, 0.16f);
        [SerializeField] private Color selectedColor = new(0.89f, 0.69f, 0.29f, 0.5f);

        private GameEngine engine;
        private GamePhase phase = GamePhase.Planning;
        private string selectedHoodId;
        private string selectedBlockId;
        private bool cityEditorBuilt;

        // --- Simulation (decoupled: SimulationManager produces events, EventPlayer renders) ---
        private WaypointGraph waypointGraph;
        private SimulationManager simManager;
        private EventPlayer eventPlayer;
        private TickHUD tickHUD;
        private WeekTransition weekTransition;
        private PathDebugRenderer pathDebugRenderer;

        // --- Execution phase status badge ---
        private GameObject executionStatusBadge;
        private TMP_Text executionStatusText;

        private readonly Dictionary<string, GameObject> hoodCards = new();
        private readonly List<(string text, Color color)> eventLogBuffer = new();
        private Dictionary<string, Button> orderButtons;
        private GameObject[] tabPages;
        private Button[] tabButtons;
        private int activeTabIndex;

        private enum GamePhase { Planning, Execution }

        void Start()
        {
            ResolvePlanningUIRootsFallback();

            string dataDir = Application.streamingAssetsPath;
            var gameData = DataLoader.LoadAll(dataDir);

            if (randomSeed >= 0)
            {
                CharacterGen.SetSeed(randomSeed);
                CityGen.SetSeed(randomSeed);
                CrimeSystem.SetSeed(randomSeed);
                EconomySystem.SetSeed(randomSeed);
                RivalAI.SetSeed(randomSeed);
            }

            engine = new GameEngine(gameData);
            engine.Setup();

            orderButtons = new Dictionary<string, Button>
            {
                ["extort"] = extortButton,
                ["collect_protection"] = collectButton,
                ["patrol"] = patrolButton,
                ["intimidate"] = intimidateButton,
                ["lie_low"] = lieLowButton
            };

            foreach (var (orderType, btn) in orderButtons)
            {
                if (btn == null) continue;
                var captured = orderType;
                btn.onClick.AddListener(() => OnOrderClicked(captured));
                btn.interactable = false;
            }

            if (runWeekButton != null)
                runWeekButton.onClick.AddListener(OnRunWeek);

            SetupTabs();

            if (cityMap == null)
            {
                Debug.LogError("[GameUIController] CityMap3D reference is not assigned!");
            }
            else
            {
                cityMap.OnBlockClicked += OnBlockClicked;
                cityMap.BuildMap(engine.blocks);

                // Build waypoint graph and show labels for planning phase debugging
                if (waypointGraph == null)
                {
                    waypointGraph = new WaypointGraph();
                    var layout = cityMap.CachedLayout;
                    if (layout != null)
                    {
                        waypointGraph.GenerateFromLayout(
                            layout,
                            cityMap.Spacing,
                            cityMap.GroundTile,
                            cityMap.SidewalkW,
                            cityMap.MapRoot.position);
                    }
                }
                // Wire graph to PathDebugRenderer for F7 beam display
                var pdr = PathDebugRenderer.Instance;
                if (pdr == null)
                {
                    var pdrObj = new GameObject("PathDebugRenderer");
                    pdr = pdrObj.AddComponent<PathDebugRenderer>();
                }
                pdr.SetMapRoot(cityMap.MapRoot);
                pdr.SetDebugGraph(waypointGraph);
            }

            RefreshAll();

            // --- CHARACTER STATUS ---
            if (characterStatusText != null)
                characterStatusText.text = "Civilian_01 [ ON STREET ]";

            // --- PRE-FLIGHT CHECK: verify all critical UI references are wired ---
            RunPreflightCheck();

            Debug.Log("[GameUIController] Game initialized. Planning phase active.");
        }

        void Update()
        {
            fpsAccumTime += Time.unscaledDeltaTime;
            fpsAccumFrames++;
            if (fpsAccumTime >= fpsUpdateInterval)
            {
                float fps = fpsAccumFrames / fpsAccumTime;
                Color fpsColor = fps >= 50 ? greenColor : fps >= 30 ? yellowColor : redColor;
                string fpsStr = $"{fps:F0} FPS";

                // Planning phase: append to phaseText
                if (phase == GamePhase.Planning && phaseText != null)
                {
                    phaseText.text = $"PLANNING  [{fpsStr}]";
                    phaseText.color = yellowColor;
                }
                // Working phase: update TickHUD
                else if (tickHUD != null)
                {
                    tickHUD.UpdateFPS(fpsStr, fpsColor);
                }

                fpsAccumTime = 0f;
                fpsAccumFrames = 0;
            }

        }

        void OnDestroy()
        {
            if (cityMap != null) cityMap.OnBlockClicked -= OnBlockClicked;
        }

        #region --- PRE-FLIGHT CHECK ---

        private void RunPreflightCheck()
        {
            var failures = new List<string>();

            // 3D Map
            if (cityMap == null) failures.Add("cityMap (CityMap3D)");

            // Top bar texts
            if (weekText == null) failures.Add("weekText");
            if (phaseText == null) failures.Add("phaseText");
            if (treasuryText == null) failures.Add("treasuryText");
            if (characterStatusText == null) failures.Add("characterStatusText");

            // Info panel content containers
            if (hoodList == null) failures.Add("hoodList");
            if (blockInfoContent == null) failures.Add("blockInfoContent");
            if (financeContent == null) failures.Add("financeContent");
            if (policeContent == null) failures.Add("policeContent");
            if (investigationContent == null) failures.Add("investigationContent");
            if (eventLogContent == null) failures.Add("eventLogContent");

            // Tab pages
            if (hoodsPage == null) failures.Add("hoodsPage");
            if (blockInfoPage == null) failures.Add("blockInfoPage");
            if (ordersPage == null) failures.Add("ordersPage");
            if (financePage == null) failures.Add("financePage");
            if (policePage == null) failures.Add("policePage");
            if (investigationPage == null) failures.Add("investigationPage");
            if (eventLogPage == null) failures.Add("eventLogPage");

            // Tab buttons
            if (hoodsTabButton == null) failures.Add("hoodsTabButton");
            if (blockInfoTabButton == null) failures.Add("blockInfoTabButton");
            if (ordersTabButton == null) failures.Add("ordersTabButton");
            if (financeTabButton == null) failures.Add("financeTabButton");
            if (policeTabButton == null) failures.Add("policeTabButton");
            if (investigationTabButton == null) failures.Add("investigationTabButton");
            if (eventLogTabButton == null) failures.Add("eventLogTabButton");

            // Order buttons
            if (extortButton == null) failures.Add("extortButton");
            if (collectButton == null) failures.Add("collectButton");
            if (patrolButton == null) failures.Add("patrolButton");
            if (intimidateButton == null) failures.Add("intimidateButton");
            if (lieLowButton == null) failures.Add("lieLowButton");

            // Bottom bar
            if (runWeekButton == null) failures.Add("runWeekButton");

            // --- Report ---
            if (failures.Count == 0)
            {
                Debug.Log("[GameUIController] PRE-FLIGHT: All 29 UI references OK. Ready to play.");
                AddEventLogEntry("[SYSTEM] Pre-flight check PASSED — all UI references wired.", greenColor);
                AddEventLogEntry("[SYSTEM] Game initialized. Awaiting orders...", goldColor);
            }
            else
            {
                Debug.LogError($"[GameUIController] PRE-FLIGHT FAILED — {failures.Count} missing reference(s):\n  - {string.Join("\n  - ", failures)}");
                AddEventLogEntry($"[ERROR] Pre-flight FAILED — {failures.Count} missing ref(s).", redColor);
                foreach (var f in failures)
                    AddEventLogEntry($"  [MISSING] {f}", redColor);
                AddEventLogEntry("[ERROR] Rebuild UI: Steel City -> Build Game UI", redColor);
            }
        }

        #endregion

        #region --- TABS ---

        private void SetupTabs()
        {
            tabPages = new[] { hoodsPage, blockInfoPage, ordersPage, financePage, policePage, investigationPage, eventLogPage };
            tabButtons = new[] { hoodsTabButton, blockInfoTabButton, ordersTabButton, financeTabButton, policeTabButton, investigationTabButton, eventLogTabButton };

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                int captured = i;
                tabButtons[i].onClick.AddListener(() => ShowTab(captured));
            }

            ShowTab(0);
        }

        private void ShowTab(int index)
        {
            activeTabIndex = index;
            for (int i = 0; i < tabPages.Length; i++)
            {
                if (tabPages[i] != null) tabPages[i].SetActive(i == index);

                if (tabButtons[i] != null)
                {
                    var img = tabButtons[i].GetComponent<Image>();
                    if (img != null) img.color = i == index ? tabActiveColor : tabInactiveColor;
                }
            }

            // Build City Editor panel the first time Editor tab is shown
            if (index == 2 && !cityEditorBuilt && ordersPage != null && cityMap != null)
            {
                BuildCityEditorPanel(ordersPage.transform);
                cityEditorBuilt = true;
            }

            // Force layout rebuild on the activated page
            if (index >= 0 && index < tabPages.Length && tabPages[index] != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(tabPages[index].GetComponent<RectTransform>());
        }

        #endregion

        #region --- CITY EDITOR ---

        private void BuildCityEditorPanel(Transform parent)
        {
            // Scrollable container
            var scrollObj = new GameObject("CityEditorScroll");
            scrollObj.transform.SetParent(parent, false);
            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            var scrollRT = scrollObj.GetComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;
            var scrollLE = scrollObj.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1;

            // Content container
            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(scrollObj.transform, false);
            var contentRT = contentObj.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = Vector2.one;
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            var vlg = contentObj.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            contentObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = contentRT;

            // Accordion group
            var groupObj = contentObj;
            var accordionGroup = groupObj.AddComponent<AccordionGroup>();

            // --- Section 1: Road & Camera (expanded by default) ---
            var roadSection = CreateAccordionSection(groupObj.transform, "ROAD & CAMERA", goldColor, startExpanded: true);
            accordionGroup.AddSection(roadSection);
            var roadContent = roadSection.Content;
            AddEditorSlider(roadContent, "Road Width", cityMap.GetRoadWidth(), 0.1f, 6f,
                cityMap.SetRoadWidth);
            AddEditorSlider(roadContent, "Sidewalk Width", cityMap.GetSidewalkWidth(), 0.1f, 4f,
                cityMap.SetSidewalkWidth);
            AddEditorSlider(roadContent, "Camera Zoom", cityMap.GetCameraOrthoSize(), 3f, 40f,
                cityMap.SetCameraOrthoSize);
            AddEditorButton(roadContent, "RESET CAMERA", () => cityMap.ResetCamera(), goldColor);
            AddEditorStepper(roadContent, "Buildings/Block Row", cityMap.GetBuildingsPerBlockRow(), 1, 5,
                cityMap.SetBuildingsPerBlockRow);
            AddEditorToggle(roadContent, "Show Road Names", cityMap.GetShowRoadNames(),
                cityMap.SetShowRoadNames);
            AddEditorToggle(roadContent, "Show Block Labels", cityMap.GetShowBlockLabels(),
                cityMap.SetShowBlockLabels);
            AddEditorToggle(roadContent, "Split Terrain", cityMap.GetUseSplitTerrain(),
                cityMap.SetUseSplitTerrain);
            AddEditorToggle(roadContent, "Proxy Render", cityMap.GetUseProxyRender(),
                cityMap.SetUseProxyRender);
            AddEditorButton(roadContent, "REBUILD CITY", () => cityMap.RebuildCity(), goldColor);

            // --- Section 2: Material Brightness ---
            var matSection = CreateAccordionSection(groupObj.transform, "MATERIAL BRIGHTNESS", goldColor, startExpanded: false);
            accordionGroup.AddSection(matSection);
            var matContent = matSection.Content;
            AddEditorSlider(matContent, "Tar (118)", cityMap.GetMaterialBrightness(118), 0.05f, 1.0f,
                (v) => cityMap.SetMaterialBrightness(118, v));
            AddEditorSlider(matContent, "Dark Wood (106)", cityMap.GetMaterialBrightness(106), 0.05f, 1.0f,
                (v) => cityMap.SetMaterialBrightness(106, v));
            AddEditorSlider(matContent, "Cobblestone (105)", cityMap.GetMaterialBrightness(105), 0.05f, 1.0f,
                (v) => cityMap.SetMaterialBrightness(105, v));
            AddEditorSlider(matContent, "Stone (101)", cityMap.GetMaterialBrightness(101), 0.05f, 1.0f,
                (v) => cityMap.SetMaterialBrightness(101, v));
            AddEditorSlider(matContent, "Red Brick (100)", cityMap.GetMaterialBrightness(100), 0.05f, 1.0f,
                (v) => cityMap.SetMaterialBrightness(100, v));
            AddEditorSlider(matContent, "Asphalt (104)", cityMap.GetMaterialBrightness(104), 0.05f, 1.0f,
                (v) => cityMap.SetMaterialBrightness(104, v));

            // --- Section 3: Shadow Debug ---
            var shadowSection = CreateAccordionSection(groupObj.transform, "SHADOW DEBUG", goldColor, startExpanded: false);
            accordionGroup.AddSection(shadowSection);
            var shadowContent = shadowSection.Content;
            AddEditorToggle(shadowContent, "Shadows Enabled", cityMap.GetShadowEnabled(),
                cityMap.SetShadowEnabled);
            AddEditorSlider(shadowContent, "Normal Nudge", cityMap.GetShadowNormalNudge(), 0.0f, 10.0f,
                cityMap.SetShadowNormalNudge);
            AddEditorSlider(shadowContent, "Light Nudge", cityMap.GetShadowLightNudge(), 0.0f, 10.0f,
                cityMap.SetShadowLightNudge);
            AddEditorStepper(shadowContent, "Skip Steps", cityMap.GetShadowSkipSteps(), 0, 16,
                cityMap.SetShadowSkipSteps);
            AddEditorStepper(shadowContent, "Max Steps", cityMap.GetShadowMaxSteps(), 1, 64,
                cityMap.SetShadowMaxSteps);

            // --- Section 4: Lighting Debug ---
            var lightSection = CreateAccordionSection(groupObj.transform, "LIGHTING DEBUG", goldColor, startExpanded: false);
            accordionGroup.AddSection(lightSection);
            var lightContent = lightSection.Content;
            AddEditorToggle(lightContent, "Sun Light (Half-Lambert)", cityMap.GetSunLightEnabled(),
                cityMap.SetSunLightEnabled);
            AddEditorToggle(lightContent, "Ambient", cityMap.GetAmbientEnabled(),
                cityMap.SetAmbientEnabled);
            AddEditorToggle(lightContent, "Fill Light", cityMap.GetFillEnabled(),
                cityMap.SetFillEnabled);
            AddEditorToggle(lightContent, "Camera Light", cityMap.GetCamLightEnabled(),
                cityMap.SetCamLightEnabled);

            Debug.Log("[GameUIController] City Editor panel built with 4 accordion sections.");
        }

        /// <summary>Creates an accordion section with a clickable header and collapsible content area.</summary>
        private AccordionSection CreateAccordionSection(Transform parent, string title, Color headerColor, bool startExpanded)
        {
            // Section root
            var sectionObj = new GameObject($"Accordion_{title.Replace(" ", "_")}");
            sectionObj.transform.SetParent(parent, false);
            var sectionLE = sectionObj.AddComponent<LayoutElement>();
            sectionLE.flexibleWidth = 1;

            var sectionVLG = sectionObj.AddComponent<VerticalLayoutGroup>();
            sectionVLG.spacing = 0;
            sectionVLG.childControlWidth = true;
            sectionVLG.childForceExpandWidth = true;
            sectionVLG.childForceExpandHeight = false;

            // Header button
            var headerObj = new GameObject("Header");
            headerObj.transform.SetParent(sectionObj.transform, false);
            var headerImg = headerObj.AddComponent<Image>();
            headerImg.color = new Color(0.15f, 0.15f, 0.22f, 1f);
            var headerBtn = headerObj.AddComponent<Button>();
            headerBtn.targetGraphic = headerImg;
            var headerLE = headerObj.AddComponent<LayoutElement>();
            headerLE.preferredHeight = 28;
            headerLE.flexibleWidth = 1;

            var headerHLG = headerObj.AddComponent<HorizontalLayoutGroup>();
            headerHLG.spacing = 6;
            headerHLG.padding = new RectOffset(8, 8, 0, 0);
            headerHLG.childControlWidth = false;
            headerHLG.childForceExpandWidth = false;
            headerHLG.childForceExpandHeight = true;
            headerHLG.childAlignment = TextAnchor.MiddleLeft;

            // Arrow icon
            var arrowObj = new GameObject("Arrow");
            arrowObj.transform.SetParent(headerObj.transform, false);
            var arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
            arrowText.text = startExpanded ? "▼" : "▶";
            arrowText.fontSize = 12;
            arrowText.color = headerColor;
            arrowText.raycastTarget = false;
            var arrowLE = arrowObj.AddComponent<LayoutElement>();
            arrowLE.preferredWidth = 16;
            arrowLE.preferredHeight = 20;

            // Header label
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(headerObj.transform, false);
            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.text = title;
            labelText.fontSize = 13;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = headerColor;
            labelText.alignment = TextAlignmentOptions.Left;
            labelText.raycastTarget = false;
            var labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.flexibleWidth = 1;
            labelLE.preferredHeight = 20;

            // Content container (collapsible)
            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(sectionObj.transform, false);
            var contentRT = contentObj.AddComponent<RectTransform>();
            var contentVLG = contentObj.AddComponent<VerticalLayoutGroup>();
            contentVLG.spacing = 4;
            contentVLG.padding = new RectOffset(8, 8, 4, 4);
            contentVLG.childControlWidth = true;
            contentVLG.childForceExpandWidth = true;
            contentVLG.childForceExpandHeight = false;
            var contentLE = contentObj.AddComponent<LayoutElement>();
            contentLE.flexibleWidth = 1;
            contentLE.preferredHeight = startExpanded ? -1 : 0;

            // Setup AccordionSection component
            var section = sectionObj.AddComponent<AccordionSection>();
            // Use reflection-free approach: set fields via serialized property or public methods
            // Since we can't set [SerializeField] fields directly, we'll use a setup method
            section.SetupAccordion(headerBtn, labelText, arrowText, contentRT, contentLE, startExpanded);

            return section;
        }

        private void AddEditorHeader(Transform parent, string text, Color color)
        {
            var obj = new GameObject("Header");
            obj.transform.SetParent(parent, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 16;
            tmp.color = color;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            obj.AddComponent<LayoutElement>().preferredHeight = 28;
        }

        private void AddEditorText(Transform parent, string text, Color color)
        {
            var obj = new GameObject("InfoText");
            obj.transform.SetParent(parent, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 11;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            obj.AddComponent<LayoutElement>().preferredHeight = 18;
        }

        private void AddEditorSlider(Transform parent, string label, float currentVal, float minVal, float maxVal,
            System.Action<float> onChanged)
        {
            var row = new GameObject($"Slider_{label}");
            row.transform.SetParent(parent, false);
            var rowVLG = row.AddComponent<VerticalLayoutGroup>();
            rowVLG.spacing = 2;
            rowVLG.childControlWidth = true;
            rowVLG.childForceExpandWidth = true;
            rowVLG.childForceExpandHeight = false;
            row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Label + value row
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(row.transform, false);
            var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            labelTmp.text = $"{label}: {currentVal:F2}";
            labelTmp.fontSize = 12;
            labelTmp.color = textBright;
            labelTmp.raycastTarget = false;
            labelObj.AddComponent<LayoutElement>().preferredHeight = 18;

            // Slider
            var sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(row.transform, false);
            var sliderRT = sliderObj.AddComponent<RectTransform>();
            sliderRT.sizeDelta = new Vector2(0, 20);
            var slider = sliderObj.AddComponent<Slider>();
            slider.minValue = minVal;
            slider.maxValue = maxVal;
            slider.value = currentVal;
            slider.onValueChanged.AddListener((v) =>
            {
                labelTmp.text = $"{label}: {v:F2}";
                onChanged(v);
            });

            // Slider background
            var bgObj = new GameObject("BG");
            bgObj.transform.SetParent(sliderObj.transform, false);
            var bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.25f);
            bgRT.anchorMax = new Vector2(1, 0.75f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.14f);
            slider.targetGraphic = bgImg;

            // Fill area
            var fillAreaObj = new GameObject("FillArea");
            fillAreaObj.transform.SetParent(sliderObj.transform, false);
            var fillAreaRT = fillAreaObj.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0, 0.25f);
            fillAreaRT.anchorMax = Vector2.one;
            fillAreaRT.offsetMin = new Vector2(4, 0);
            fillAreaRT.offsetMax = new Vector2(-4, 0);
            var fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillAreaObj.transform, false);
            var fillRT = fillObj.AddComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fillImg = fillObj.AddComponent<Image>();
            fillImg.color = goldColor;
            slider.fillRect = fillRT;

            // Handle area
            var handleAreaObj = new GameObject("HandleArea");
            handleAreaObj.transform.SetParent(sliderObj.transform, false);
            var handleAreaRT = handleAreaObj.AddComponent<RectTransform>();
            handleAreaRT.anchorMin = new Vector2(0, 0f);
            handleAreaRT.anchorMax = new Vector2(1, 1f);
            handleAreaRT.offsetMin = new Vector2(4, 0);
            handleAreaRT.offsetMax = new Vector2(-4, 0);
            var handleObj = new GameObject("Handle");
            handleObj.transform.SetParent(handleAreaObj.transform, false);
            var handleRT = handleObj.AddComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(12, 12);
            var handleImg = handleObj.AddComponent<Image>();
            handleImg.color = textBright;
            slider.handleRect = handleRT;

            row.AddComponent<LayoutElement>().preferredHeight = 44;
        }

        private void AddEditorStepper(Transform parent, string label, int currentVal, int minVal, int maxVal,
            System.Action<int> onChanged)
        {
            var row = new GameObject($"Stepper_{label}");
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            row.AddComponent<LayoutElement>().preferredHeight = 24;

            // Label
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(row.transform, false);
            var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            labelTmp.text = $"{label}: {currentVal}";
            labelTmp.fontSize = 12;
            labelTmp.color = textBright;
            labelTmp.raycastTarget = false;
            labelObj.AddComponent<LayoutElement>().preferredWidth = 140;

            // Minus button
            var minusBtn = MakeEditorButtonObj(row.transform, "-", 24);
            minusBtn.onClick.AddListener(() =>
            {
                int v = Mathf.Max(minVal, int.Parse(labelTmp.text.Split(':')[1].Trim()) - 1);
                labelTmp.text = $"{label}: {v}";
                onChanged(v);
            });

            // Plus button
            var plusBtn = MakeEditorButtonObj(row.transform, "+", 24);
            plusBtn.onClick.AddListener(() =>
            {
                int v = Mathf.Min(maxVal, int.Parse(labelTmp.text.Split(':')[1].Trim()) + 1);
                labelTmp.text = $"{label}: {v}";
                onChanged(v);
            });
        }

        private void AddEditorToggle(Transform parent, string label, bool currentVal,
            System.Action<bool> onChanged)
        {
            var row = new GameObject($"Toggle_{label}");
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            row.AddComponent<LayoutElement>().preferredHeight = 24;

            // Label
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(row.transform, false);
            var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            labelTmp.text = label;
            labelTmp.fontSize = 12;
            labelTmp.color = textBright;
            labelTmp.raycastTarget = false;
            labelObj.AddComponent<LayoutElement>().preferredWidth = 140;

            // Toggle — fixed 24x24, not controlled by parent layout
            var toggleObj = new GameObject("Toggle");
            toggleObj.transform.SetParent(row.transform, false);
            var toggleRT = toggleObj.AddComponent<RectTransform>();
            toggleRT.sizeDelta = new Vector2(24, 24);
            var toggleLE = toggleObj.AddComponent<LayoutElement>();
            toggleLE.preferredWidth = 24;
            toggleLE.preferredHeight = 24;
            toggleLE.flexibleWidth = 0;
            toggleLE.flexibleHeight = 0;
            var toggle = toggleObj.AddComponent<Toggle>();
            toggle.isOn = currentVal;
            toggle.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(onChanged));

            // Background — must be raycastTarget for click detection
            var bgObj = new GameObject("BG");
            bgObj.transform.SetParent(toggleObj.transform, false);
            var bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.14f);
            bgImg.raycastTarget = true;
            toggle.targetGraphic = bgImg;

            // Checkmark
            var checkObj = new GameObject("Check");
            checkObj.transform.SetParent(toggleObj.transform, false);
            var checkRT = checkObj.AddComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.2f, 0.2f);
            checkRT.anchorMax = new Vector2(0.8f, 0.8f);
            checkRT.offsetMin = Vector2.zero;
            checkRT.offsetMax = Vector2.zero;
            var checkImg = checkObj.AddComponent<Image>();
            checkImg.color = greenColor;
            checkImg.raycastTarget = false;
            toggle.graphic = checkImg;
        }

        private void AddEditorButton(Transform parent, string label, System.Action onClick, Color color)
        {
            var btn = MakeEditorButtonObj(parent.transform, label, 0);
            var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.color = color;
            btn.onClick.AddListener(() => onClick());
        }

        private Button MakeEditorButtonObj(Transform parent, string label, float fixedWidth)
        {
            var obj = new GameObject($"Btn_{label}");
            obj.transform.SetParent(parent, false);
            var img = obj.AddComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.20f);
            var btn = obj.AddComponent<Button>();
            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 24;
            if (fixedWidth > 0) le.preferredWidth = fixedWidth;

            var txtObj = new GameObject("Text");
            txtObj.transform.SetParent(obj.transform, false);
            var txt = txtObj.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 12;
            txt.color = textBright;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;
            var txtRT = txtObj.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;
            return btn;
        }

        #endregion

        #region --- SELECTION ---

        private void OnBlockClicked(string blockId)
        {
            if (phase != GamePhase.Planning) return;

            selectedBlockId = blockId;
            RefreshBlockInfo();
            RefreshMapHighlights();
            TryEnableOrderButtons();
        }

        private void OnHoodClicked(string hoodId)
        {
            if (phase != GamePhase.Planning) return;
            selectedHoodId = hoodId;
            RefreshHoods();
            TryEnableOrderButtons();
            FocusCameraOnHood(hoodId);
        }

        /// <summary>Pans the planning-phase map camera to center on the player character on the street.</summary>
        private void FocusCameraOnHood(string hoodId)
        {
            if (cityMap == null) return;

            // Focus on the player character's actual position, not the hood's assigned block
            var character = cityMap.SpawnedCharacter;
            if (character != null)
            {
                Vector3 center = character.WorldCenter;
                cityMap.SetCameraFocus(center);
                Debug.Log($"[GameUIController] 📷 Camera focused on character at {center}");
            }
            else
            {
                // Fallback: focus on HQ block if character not spawned yet
                FocusCameraOnHq();
            }

            // Still auto-select the hood's block for info panel + highlights
            Hood hood = null;
            foreach (var gang in engine.gangs.Values)
            {
                foreach (var h in gang.hoods)
                {
                    if (h.id == hoodId) { hood = h; break; }
                }
                if (hood != null) break;
            }
            if (hood != null && !string.IsNullOrEmpty(hood.currentBlockId) && engine.blocks.TryGetValue(hood.currentBlockId, out var block))
            {
                selectedBlockId = block.id;
                RefreshBlockInfo();
                RefreshMapHighlights();
            }
        }

        private void TryEnableOrderButtons()
        {
            bool canAssign = !string.IsNullOrEmpty(selectedHoodId) && !string.IsNullOrEmpty(selectedBlockId);
            foreach (var (_, btn) in orderButtons)
                if (btn != null) btn.interactable = canAssign;
        }

        #endregion

        #region --- ORDER ASSIGNMENT ---

        private void OnOrderClicked(string orderType)
        {
            if (phase != GamePhase.Planning) return;
            if (string.IsNullOrEmpty(selectedHoodId) || string.IsNullOrEmpty(selectedBlockId)) return;

            bool success = engine.AssignOrder(selectedHoodId, selectedBlockId, orderType);
            if (success)
            {
                var hood = engine.FindHood(selectedHoodId);
                var block = engine.blocks[selectedBlockId];
                AddEventLogEntry($"[ORDER] {hood.name} assigned to {orderType} on {block.name}", textBright);
            }
            else
            {
                AddEventLogEntry("[FAIL] Failed to assign order.", redColor);
            }

            selectedHoodId = null;
            selectedBlockId = null;
            RefreshHoods();
            RefreshBlockInfo();
            RefreshMapHighlights();
            TryEnableOrderButtons();
        }

        #endregion

        #region --- EXECUTION PHASE UI ---

        /// <summary>
        /// Resolve topBarRoot/infoPanelRoot/bottomBarRoot if not wired in the Inspector.
        /// Handles scenes built before these fields existed (auto-builder not re-run).
        /// Walks up from known child references (weekText, hoodList, runWeekButton) to find
        /// their panel roots, falling back to GameObject.Find by name.
        /// </summary>
        private void ResolvePlanningUIRootsFallback()
        {
            if (topBarRoot == null && weekText != null)
                topBarRoot = weekText.transform.parent != null ? weekText.transform.parent.gameObject : null;
            if (topBarRoot == null)
                topBarRoot = GameObject.Find("TopBar");

            if (infoPanelRoot == null && hoodList != null)
            {
                // hoodList -> HoodsPage -> ContentArea -> InfoPanel
                var t = hoodList.parent;
                while (t != null && t.name != "InfoPanel") t = t.parent;
                if (t != null) infoPanelRoot = t.gameObject;
            }
            if (infoPanelRoot == null)
                infoPanelRoot = GameObject.Find("InfoPanel");

            if (bottomBarRoot == null && runWeekButton != null)
                bottomBarRoot = runWeekButton.transform.parent != null ? runWeekButton.transform.parent.gameObject : null;
            if (bottomBarRoot == null)
                bottomBarRoot = GameObject.Find("BottomBar");

            Debug.Log($"[GameUIController] Planning UI roots resolved: TopBar={(topBarRoot != null)} InfoPanel={(infoPanelRoot != null)} BottomBar={(bottomBarRoot != null)}");
        }

        /// <summary>Hide all planning-phase UI elements for fullscreen execution.</summary>
        private void SetPlanningUIVisible(bool visible)
        {
            if (topBarRoot != null) topBarRoot.SetActive(visible);
            if (infoPanelRoot != null) infoPanelRoot.SetActive(visible);
            if (bottomBarRoot != null) bottomBarRoot.SetActive(visible);
        }

        /// <summary>Create a small status badge top-right during execution.</summary>
        private void ShowExecutionStatusBadge()
        {
            if (executionStatusBadge != null)
            {
                executionStatusBadge.SetActive(true);
                return;
            }

            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            executionStatusBadge = new GameObject("ExecutionStatusBadge");
            executionStatusBadge.transform.SetParent(canvas.transform, false);
            var rt = executionStatusBadge.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-12, -12);
            rt.sizeDelta = new Vector2(220, 56);

            var bg = executionStatusBadge.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.75f);

            var vlg = executionStatusBadge.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2;
            vlg.padding = new RectOffset(10, 10, 6, 6);
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var weekLine = new GameObject("StatusWeek");
            weekLine.transform.SetParent(executionStatusBadge.transform, false);
            var weekTmp = weekLine.AddComponent<TextMeshProUGUI>();
            weekTmp.text = $"WEEK {engine.week}";
            weekTmp.fontSize = 16;
            weekTmp.color = goldColor;
            weekTmp.fontStyle = FontStyles.Bold;
            weekTmp.alignment = TextAlignmentOptions.Right;
            weekTmp.raycastTarget = false;
            var weekLE = weekLine.AddComponent<LayoutElement>();
            weekLE.preferredHeight = 22;

            var phaseLine = new GameObject("StatusPhase");
            phaseLine.transform.SetParent(executionStatusBadge.transform, false);
            executionStatusText = phaseLine.AddComponent<TextMeshProUGUI>();
            executionStatusText.text = "EXECUTION";
            executionStatusText.fontSize = 13;
            executionStatusText.color = redColor;
            executionStatusText.fontStyle = FontStyles.Bold;
            executionStatusText.alignment = TextAlignmentOptions.Right;
            executionStatusText.raycastTarget = false;
            var phaseLE = phaseLine.AddComponent<LayoutElement>();
            phaseLE.preferredHeight = 18;

            Debug.Log("[GameUIController] Execution status badge created");
        }

        private void HideExecutionStatusBadge()
        {
            if (executionStatusBadge != null)
                executionStatusBadge.SetActive(false);
        }

        public void UpdateExecutionStatus(string phaseName, int tickElapsed, int tickRemaining)
        {
            if (executionStatusText != null)
                executionStatusText.text = $"{phaseName} | Tick {tickElapsed}";
        }

        #endregion

        #region --- RUN WEEK ---

        private void OnRunWeek()
        {
            if (phase != GamePhase.Planning) return;

            if (engine.pendingOrders.Count == 0)
            {
                var hood = engine.gangs["player"].hoods[0];
                string autoBlockId = null;
                foreach (var (bid, blk) in engine.blocks)
                {
                    if (!blk.isPlayerHq) { autoBlockId = bid; break; }
                }
                if (autoBlockId != null)
                {
                    engine.AssignOrder(hood.id, autoBlockId, "stand", "player");
                    AddEventLogEntry($"[AUTO] Auto-assigned {hood.name} to STAND at {engine.blocks[autoBlockId].name}", yellowColor);
                }
                else
                {
                    AddEventLogEntry("[ERROR] No target block found for auto-assign!", redColor);
                    return;
                }
            }

            Debug.Log($"[GameUIController] === PLANNING -> WORKING WEEK transition ===");
            Debug.Log($"[GameUIController] Pending orders: {engine.pendingOrders.Count}");
            Debug.Log($"[GameUIController] Player hoods: {engine.gangs["player"].hoods.Count} (name={engine.gangs["player"].hoods[0].name})");

            phase = GamePhase.Execution;
            if (phaseText != null) { phaseText.text = "EXECUTION"; phaseText.color = redColor; }
            if (characterStatusText != null) characterStatusText.text = "Civilian_01 [ WORKING ]";
            if (runWeekButton != null) runWeekButton.interactable = false;

            // Hide planning UI for fullscreen execution
            SetPlanningUIVisible(false);
            ShowExecutionStatusBadge();

            AddEventLogEntry($"=== WEEK {engine.week} BEGIN ===", goldColor);

            // Start fade-to-black transition, then set up sim during loading phase
            EnsureWeekTransition();
            StartCoroutine(weekTransition.RunTransition(engine.week, () =>
            {
                Debug.Log("[GameUIController] Transition onReady — setting up sim and camera");
                StartTickSimulation();
                // Pause EventPlayer so no movement happens until scene is fully loaded + hold
                if (eventPlayer != null)
                {
                    eventPlayer.isPaused = true;
                    Debug.Log("[GameUIController] EventPlayer paused — waiting for post-fade hold");
                }
            }, () =>
            {
                // onStart: fired after fade-in + 5s hold
                if (eventPlayer != null)
                {
                    eventPlayer.isPaused = false;
                    Debug.Log("[GameUIController] EventPlayer unpaused — simulation starting");
                }
            }));
        }

        private void EnsureWeekTransition()
        {
            if (weekTransition != null) return;
            var obj = new GameObject("WeekTransition");
            weekTransition = obj.AddComponent<WeekTransition>();
            Debug.Log("[GameUIController] WeekTransition created");
        }

        private void StartTickSimulation()
        {
            // Build waypoint graph from city layout
            if (waypointGraph == null)
            {
                waypointGraph = new WaypointGraph();
                var layout = cityMap.CachedLayout;
                if (layout == null)
                {
                    AddEventLogEntry("[ERROR] No city layout loaded!", redColor);
                    EndExecution();
                    return;
                }
                waypointGraph.GenerateFromLayout(
                    layout,
                    cityMap.Spacing,
                    cityMap.GroundTile,
                    cityMap.SidewalkW,
                    cityMap.MapRoot.position);
                // Wire graph to PathDebugRenderer for F7 beam display
                var pdr = PathDebugRenderer.Instance;
                if (pdr != null) pdr.SetDebugGraph(waypointGraph);
            }

            // Get the first pending order (PoC: single hood)
            var order = engine.pendingOrders[0];
            var hood = engine.FindHood(order.hoodId);
            var targetBlock = engine.blocks[order.blockId];

            // Get character position
            var character = cityMap.SpawnedCharacter;
            if (character == null)
            {
                AddEventLogEntry("[ERROR] No spawned character found!", redColor);
                EndExecution();
                return;
            }

            // Compute start and target local positions from LIVE character transform
            Vector3 startLocalPos = character.transform.position - cityMap.MapRoot.position;
            Vector3 targetLocalPos = ComputeBlockCenterLocal(targetBlock);

            // Find nearest block to character's actual current position (not fixed HQ)
            string startBlockId = FindNearestBlockId(startLocalPos);
            if (string.IsNullOrEmpty(startBlockId))
            {
                AddEventLogEntry("[ERROR] Could not determine start block from character position!", redColor);
                EndExecution();
                return;
            }

            Debug.Log($"[GameUIController] Character start pos (live): {startLocalPos} startBlock={startBlockId}");
            Debug.Log($"[GameUIController] Target block pos: {targetLocalPos} (block={targetBlock.id})");

            // Create SimulationManager (pure logic, no rendering)
            simManager = new SimulationManager(waypointGraph, engine);
            Debug.Log("[GameUIController] SimulationManager created");

            // Focus camera on HQ block BEFORE creating EventPlayer,
            // so EventPlayer.Initialize() captures HQ as the initial camera target.
            FocusCameraOnHq();
            cityMap.SetCameraFullscreen();
            cityMap.IsExecutionMode = true;
            cityMap.SetGranularLodMode(true);
            Debug.Log("[GameUIController] Camera focused on HQ + fullscreen — working mode started");

            // Create EventPlayer (MonoBehaviour, drives VoxelCharacter)
            if (eventPlayer != null)
                Destroy(eventPlayer);
            eventPlayer = gameObject.AddComponent<EventPlayer>();
            eventPlayer.Initialize(simManager, character, cityMap.MapRoot);
            Debug.Log("[GameUIController] EventPlayer created and initialized");

            // Create HUD
            if (tickHUD == null)
            {
                var hudObj = new GameObject("TickHUD");
                tickHUD = hudObj.AddComponent<TickHUD>();
                tickHUD.Initialize();
            }
            tickHUD.ClearLog();
            tickHUD.Show();
            tickHUD.UpdateOrder(order.orderType, targetBlock.name);
            tickHUD.UpdatePhase("Walking to target", 0, SimulationManager.TickBudget);

            // Wire up EventPlayer callbacks
            eventPlayer.OnLog = (msg) =>
            {
                Color c = textBright;
                if (msg.Contains("SUCCESS")) c = greenColor;
                else if (msg.Contains("FAILED") || msg.Contains("ERROR")) c = redColor;
                else if (msg.Contains("Wander") || msg.Contains("Traffic")) c = mutedColor;
                tickHUD.AddLogEntry(msg, c);
            };
            eventPlayer.OnStateChanged = (s, elapsed, remaining) =>
            {
                tickHUD.UpdatePhase(s.ToString(), elapsed, remaining);
                UpdateExecutionStatus(s.ToString(), elapsed, remaining);
            };
            eventPlayer.OnComplete = () =>
            {
                OnTickSimulationComplete();
            };

            // Start simulation!
            simManager.StartSimulation(order, startBlockId, startLocalPos, order.blockId, targetLocalPos);

            // Show debug path overlay via PathDebugRenderer
            if (simManager.CurrentPath != null && simManager.CurrentPath.Count > 0)
            {
                if (pathDebugRenderer == null)
                {
                    pathDebugRenderer = FindFirstObjectByType<PathDebugRenderer>();
                    if (pathDebugRenderer == null)
                    {
                        var pdrObj = new GameObject("PathDebugRenderer");
                        pathDebugRenderer = pdrObj.AddComponent<PathDebugRenderer>();
                    }
                    pathDebugRenderer.SetMapRoot(cityMap.MapRoot);
                    pathDebugRenderer.SetDebugGraph(waypointGraph);
                }
                pathDebugRenderer.ClearAllPaths();
                pathDebugRenderer.RegisterPath(
                    character.transform,
                    character.WorldSize,
                    () => simManager.CurrentPath,
                    (nodeId) => simManager.Graph.Nodes.TryGetValue(nodeId, out var n) ? n.localPos : new Vector3(float.NaN, 0, 0),
                    PathDebugType.Pedestrian,
                    () => eventPlayer.VisualPathIndex);
            }

            AddEventLogEntry($"[SIM] {hood.name} begins mission: {order.orderType} on {targetBlock.name}", goldColor);
        }

        private Vector3 ComputeBlockCenterLocal(Block block)
        {
            var layout = cityMap.CachedLayout;
            if (layout == null) return Vector3.zero;

            int minRow = int.MaxValue, maxRow = int.MinValue, minCol = int.MaxValue, maxCol = int.MinValue;
            foreach (var b in layout.blocks)
            {
                if (b.row < minRow) minRow = b.row;
                if (b.row > maxRow) maxRow = b.row;
                if (b.col < minCol) minCol = b.col;
                if (b.col > maxCol) maxCol = b.col;
            }
            float centerRow = (minRow + maxRow) * 0.5f;
            float centerCol = (minCol + maxCol) * 0.5f;
            float spacing = cityMap.Spacing;

            return new Vector3(
                (block.col - centerCol) * spacing,
                0f,
                -(block.row - centerRow) * spacing);
        }

        private string FindNearestBlockId(Vector3 localPos)
        {
            if (engine?.blocks == null || engine.blocks.Count == 0)
                return null;

            float spacing = cityMap.Spacing;
            if (spacing <= 0.0001f)
                return null;

            var layout = cityMap.CachedLayout;
            if (layout == null || layout.blocks == null || layout.blocks.Length == 0)
                return null;

            int minRow = int.MaxValue, maxRow = int.MinValue, minCol = int.MaxValue, maxCol = int.MinValue;
            foreach (var b in layout.blocks)
            {
                if (b.row < minRow) minRow = b.row;
                if (b.row > maxRow) maxRow = b.row;
                if (b.col < minCol) minCol = b.col;
                if (b.col > maxCol) maxCol = b.col;
            }

            float centerRow = (minRow + maxRow) * 0.5f;
            float centerCol = (minCol + maxCol) * 0.5f;

            int nearestCol = Mathf.RoundToInt(localPos.x / spacing + centerCol);
            int nearestRow = Mathf.RoundToInt(-localPos.z / spacing + centerRow);

            string bestId = null;
            float bestDist = float.MaxValue;
            foreach (var (bid, blk) in engine.blocks)
            {
                if (blk.row == nearestRow && blk.col == nearestCol)
                {
                    return bid;
                }

                float dRow = blk.row - nearestRow;
                float dCol = blk.col - nearestCol;
                float dist = dRow * dRow + dCol * dCol;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestId = bid;
                }
            }

            return bestId;
        }

        private void OnTickSimulationComplete()
        {
            Debug.Log("[GameUIController] === WORKING WEEK -> PLANNING transition ===");

            // Restore camera to character (planning mode — animation work)
            RestoreCameraFromHq();
            cityMap.RestoreCameraViewport();
            cityMap.IsExecutionMode = false;
            cityMap.SetGranularLodMode(false);

            // Hide execution HUD and status badge
            if (tickHUD != null)
                tickHUD.Shutdown();
            HideExecutionStatusBadge();

            // Shut down event player
            if (eventPlayer != null)
            {
                eventPlayer.Shutdown();
                Debug.Log("[GameUIController] EventPlayer shutdown complete");
            }

            // Clear path debug overlays
            if (pathDebugRenderer != null)
            {
                pathDebugRenderer.ClearAllPaths();
                Debug.Log("[GameUIController] Path debug overlays cleared");
            }

            // Keep HUD visible but mark complete
            if (tickHUD != null && simManager != null)
                tickHUD.UpdatePhase("Complete", simManager.TicksElapsed, 0);

            AddEventLogEntry($"=== WEEK {engine.week} COMPLETE ===", goldColor);
            Debug.Log($"[GameUIController] Week {engine.week} complete, ticks elapsed: {(simManager != null ? simManager.TicksElapsed : 0)}");

            // Process remaining engine systems (squeal, investigations, economy)
            // but skip order resolution since sim handled it
            engine.week++;

            EndExecution();
        }

        private void EndExecution()
        {
            phase = GamePhase.Planning;
            if (phaseText != null) { phaseText.text = "PLANNING"; phaseText.color = yellowColor; }
            if (characterStatusText != null) characterStatusText.text = "Civilian_01 [ ON STREET ]";
            if (runWeekButton != null) runWeekButton.interactable = true;

            // Restore planning UI
            SetPlanningUIVisible(true);

            selectedHoodId = null;
            selectedBlockId = null;

            // Clear pending orders
            engine.pendingOrders.Clear();
            foreach (var gang in engine.gangs.Values)
            {
                foreach (var hood in gang.hoods)
                {
                    if (hood.status == HoodStatus.Assigned)
                    {
                        hood.status = HoodStatus.Available;
                        hood.assignedOrder = null;
                    }
                }
            }

            RefreshAll();
            ShowTab(6);
        }

        #endregion

        #region --- REFRESH ---

        private void RefreshAll()
        {
            RefreshTopBar();
            RefreshHoods();
            RefreshBlockInfo();
            RefreshMap();
            RefreshFinances();
            RefreshPolice();
            RefreshInvestigations();
            RefreshEventLog();
            TryEnableOrderButtons();
        }

        private void RefreshTopBar()
        {
            if (weekText != null) weekText.text = $"Week {engine.week}";
            var player = engine.gangs["player"];
            if (treasuryText != null)
            {
                treasuryText.text = $"${player.money}";
                treasuryText.color = player.money >= 0 ? greenColor : redColor;
            }
        }

        private void RefreshHoods()
        {
            if (hoodList == null) return;

            foreach (var card in hoodCards.Values)
                if (card != null) Destroy(card);
            hoodCards.Clear();

            var player = engine.gangs["player"];
            foreach (var hood in player.hoods)
            {
                GameObject card;
                TMP_Text nameText = null, skillsText = null, orderText = null;

                if (hoodCardPrefab != null)
                {
                    card = Instantiate(hoodCardPrefab, hoodList);
                    card.name = $"Hood_{hood.id}";
                    nameText = card.transform.Find("HoodName")?.GetComponent<TMP_Text>();
                    skillsText = card.transform.Find("HoodSkills")?.GetComponent<TMP_Text>();
                    orderText = card.transform.Find("HoodOrder")?.GetComponent<TMP_Text>();

                    if (nameText == null || skillsText == null)
                    {
                        var tmps = card.GetComponentsInChildren<TMP_Text>();
                        if (tmps.Length >= 1 && nameText == null) nameText = tmps[0];
                        if (tmps.Length >= 2 && skillsText == null) skillsText = tmps[1];
                        if (tmps.Length >= 3 && orderText == null) orderText = tmps[2];
                    }

                    var img = card.GetComponent<Image>();
                    if (img != null) img.color = hood.id == selectedHoodId ? selectedColor : cardBgColor;

                    var btn = card.GetComponent<Button>();
                    if (btn != null)
                    {
                        var capturedId = hood.id;
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() => OnHoodClicked(capturedId));
                    }
                }
                else
                {
                    card = BuildFallbackHoodCard(hood, out nameText, out skillsText, out orderText);
                }

                string statusIcon = hood.status == HoodStatus.Assigned ? "[ASSIGNED]" :
                                    hood.status == HoodStatus.Arrested ? "[ARRESTED]" :
                                    hood.status == HoodStatus.Dead ? "[DEAD]" : "[READY]";
                string locationIcon = hood.isInsideBuilding ? " [INSIDE]" : "";

                if (nameText != null) nameText.text = $"{statusIcon} {hood.name}{locationIcon}";
                if (skillsText != null) skillsText.text = hood.SkillSummary;

                if (orderText != null)
                {
                    if (hood.assignedOrder != null && engine.blocks.TryGetValue(hood.assignedOrder.blockId, out var block))
                    {
                        orderText.text = $"> {hood.assignedOrder.orderType} @ {block.name}";
                        orderText.gameObject.SetActive(true);
                    }
                    else orderText.gameObject.SetActive(false);
                }

                hoodCards[hood.id] = card;
            }
        }

        private GameObject BuildFallbackHoodCard(Hood hood, out TMP_Text nameText, out TMP_Text skillsText, out TMP_Text orderText)
        {
            var card = new GameObject($"Hood_{hood.id}");
            card.transform.SetParent(hoodList, false);
            var img = card.AddComponent<Image>();
            img.color = hood.id == selectedHoodId ? selectedColor : cardBgColor;
            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2;
            vlg.padding = new RectOffset(6, 6, 4, 4);
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var btn = card.AddComponent<Button>();
            btn.targetGraphic = img;
            var capturedId = hood.id;
            btn.onClick.AddListener(() => OnHoodClicked(capturedId));

            nameText = MakeText(card.transform, "", textBright, true);
            skillsText = MakeText(card.transform, "", mutedColor, false);
            orderText = MakeText(card.transform, "", goldColor, false);
            return card;
        }

        private TMP_Text MakeText(Transform parent, string text, Color color, bool bold)
        {
            var obj = new GameObject("Text");
            obj.transform.SetParent(parent, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 12;
            tmp.color = color;
            tmp.raycastTarget = false;
            if (bold) tmp.fontStyle = FontStyles.Bold;
            obj.AddComponent<LayoutElement>().preferredHeight = 18;
            return tmp;
        }

        private void RefreshBlockInfo()
        {
            if (blockInfoContent == null) return;
            ClearChildren(blockInfoContent);

            if (string.IsNullOrEmpty(selectedBlockId) || !engine.blocks.TryGetValue(selectedBlockId, out var block))
            {
                AddTextToParent(blockInfoContent, "Click a block on the map to view details.", mutedColor);
                return;
            }

            AddTextToParent(blockInfoContent, block.name, textBright, true);
            AddTextToParent(blockInfoContent, $"Owner: {GetOwnerLabel(block)}", GetOwnerColor(block));
            AddTextToParent(blockInfoContent, $"Strength: {block.extortionStrength}/100 ({block.InfoTier})", mutedColor);
            AddTextToParent(blockInfoContent, $"Population: {block.population} | NPCs: {block.npcs.Count}", mutedColor);
            AddTextToParent(blockInfoContent, $"Land Value: {block.landValue}", mutedColor);

            foreach (var bizId in block.businesses)
            {
                if (engine.businesses.TryGetValue(bizId, out var biz))
                {
                    var bizColor = biz.ownerGang == "player" ? playerColor : biz.ownerGang == "rival" ? rivalColor : mutedColor;
                    AddTextToParent(blockInfoContent, $"  - {biz.name} ({biz.type}){(biz.isIllegal ? " [ILLEGAL]" : "")}", bizColor);
                }
            }
        }

        private void RefreshMap()
        {
            if (cityMap == null) return;
            foreach (var (blockId, block) in engine.blocks)
            {
                string hqLabel = block.isPlayerHq ? "\n[YOUR HQ]" : block.isRivalHq ? "\n[RIVAL HQ]" : block.isPoliceStation ? "\n[POLICE]" : "";
                string label = $"{block.name}\n{GetOwnerLabel(block)}\nSTR: {block.extortionStrength}{hqLabel}";
                cityMap.UpdateBlock(blockId, GetBlockColor(block), label, blockId == selectedBlockId);
            }
        }

        private void RefreshMapHighlights()
        {
            if (cityMap == null) return;
            foreach (var (blockId, block) in engine.blocks)
            {
                string hqLabel = block.isPlayerHq ? "\n[YOUR HQ]" : block.isRivalHq ? "\n[RIVAL HQ]" : block.isPoliceStation ? "\n[POLICE]" : "";
                string label = $"{block.name}\n{GetOwnerLabel(block)}\nSTR: {block.extortionStrength}{hqLabel}";
                cityMap.UpdateBlock(blockId, GetBlockColor(block), label, blockId == selectedBlockId);
            }
        }

        private void RefreshFinances()
        {
            if (financeContent == null) return;
            ClearChildren(financeContent);

            var player = engine.gangs["player"];
            int playerBlocks = engine.blocks.Values.Count(b => b.ownerGang == "player");
            int rivalBlocks = engine.blocks.Values.Count(b => b.ownerGang == "rival");
            int unowned = engine.blocks.Values.Count(b => b.ownerGang == null);

            AddTextToParent(financeContent, $"Treasury: ${player.money}", player.money >= 0 ? greenColor : redColor);
            AddTextToParent(financeContent, $"Territory: {playerBlocks} blocks (Rival: {rivalBlocks}, Unowned: {unowned})", textBright);
            AddTextToParent(financeContent, $"Hoods: {player.hoods.Count(h => h.status != HoodStatus.Dead)} active", mutedColor);
        }

        private void RefreshPolice()
        {
            if (policeContent == null) return;
            ClearChildren(policeContent);

            foreach (var officer in engine.police)
            {
                string status = officer.onPayroll ? "[BRIBED]" : "[CLEAN]";
                Color statusColor = officer.onPayroll ? greenColor : mutedColor;
                AddTextToParent(policeContent, $"{officer.name} {status}", statusColor);

                if (!officer.onPayroll)
                {
                    var btnObj = new GameObject($"Bribe_{officer.id}");
                    btnObj.transform.SetParent(policeContent, false);
                    btnObj.AddComponent<RectTransform>();
                    var btnImg = btnObj.AddComponent<Image>();
                    btnImg.color = new Color(0.08f, 0.08f, 0.14f);
                    var btn = btnObj.AddComponent<Button>();
                    btnObj.AddComponent<LayoutElement>().preferredHeight = 22;

                    var btnText = MakeText(btnObj.transform, $"Bribe ${officer.bribeCost}", goldColor, false);
                    btnText.alignment = TextAlignmentOptions.Center;
                    var tRect = btnText.GetComponent<RectTransform>();
                    tRect.anchorMin = Vector2.zero;
                    tRect.anchorMax = Vector2.one;
                    tRect.offsetMin = Vector2.zero;
                    tRect.offsetMax = Vector2.zero;

                    var capturedId = officer.id;
                    btn.onClick.AddListener(() => OnBribeOfficer(capturedId));
                }
            }
        }

        private void OnBribeOfficer(string officerId)
        {
            if (phase != GamePhase.Planning) return;
            bool success = engine.BribeOfficer(officerId);
            if (success)
            {
                var officer = engine.police.First(o => o.id == officerId);
                AddEventLogEntry($"[BRIBE] Bribed {officer.name} for ${officer.bribeCost}", goldColor);
                RefreshAll();
            }
            else
            {
                AddEventLogEntry("[FAIL] Not enough money to bribe.", redColor);
            }
        }

        private void RefreshInvestigations()
        {
            if (investigationContent == null) return;
            ClearChildren(investigationContent);

            int active = 0;
            foreach (var inv in engine.investigations.Values)
            {
                if (inv.status != "active") continue;
                active++;
                if (engine.blocks.TryGetValue(inv.blockId, out var block))
                {
                    float pct = (float)inv.leads / inv.leadsThreshold;
                    Color leadColor = pct > 0.7f ? redColor : pct > 0.3f ? yellowColor : mutedColor;
                    AddTextToParent(investigationContent, $"  {block.name}: Leads {inv.leads}/{inv.leadsThreshold}", leadColor);
                }
            }

            if (active == 0)
                AddTextToParent(investigationContent, "No active investigations.", mutedColor);
        }

        #endregion

        #region --- EVENT LOG ---

        private void AddEventLogEntry(string text, Color color)
        {
            eventLogBuffer.Add((text, color));
            while (eventLogBuffer.Count > 100)
                eventLogBuffer.RemoveAt(0);
            RefreshEventLog();
        }

        private void RefreshEventLog()
        {
            if (eventLogContent == null) return;
            ClearChildren(eventLogContent);
            foreach (var (text, color) in eventLogBuffer)
                AddTextToParent(eventLogContent, text, color);

            // Auto-scroll to bottom so latest entries are visible.
            var scrollRect = eventLogContent.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        #endregion

        #region --- HELPERS ---

        private float savedOrthoSize;
        private bool hasSavedCameraState = false;

        private void FocusCameraOnHq()
        {
            if (engine == null || cityMap == null) return;

            string hqBlockId = null;
            foreach (var kvp in engine.blocks)
            {
                if (kvp.Value.isPlayerHq) { hqBlockId = kvp.Key; break; }
            }

            if (hqBlockId == null)
            {
                Debug.LogWarning("[GameUIController] No player HQ block found — camera stays on character");
                return;
            }

            var hqBlock = engine.blocks[hqBlockId];
            Vector3 localPos = ComputeBlockCenterLocal(hqBlock);
            Vector3 worldPos = cityMap.MapRoot.position + localPos;

            savedOrthoSize = cityMap.GetCameraOrthoSize();
            hasSavedCameraState = true;

            cityMap.SetCameraPerspective(true);
            cityMap.SetCameraFocus(worldPos);
            cityMap.SetCameraOrthoSize(3f);
            cityMap.SetCameraYaw(45f);
            cityMap.SetCameraPitch(35.264f);

            Debug.Log($"[GameUIController] 📷 Camera focused on HQ '{hqBlock.name}' at {worldPos}, perspective street view");
        }

        private void RestoreCameraFromHq()
        {
            if (cityMap == null) return;

            cityMap.SetCameraPerspective(false);

            var character = cityMap?.SpawnedCharacter;
            if (character != null)
            {
                Vector3 center = character.WorldCenter;
                cityMap.SetCameraFocus(center);
                cityMap.SetCameraOrthoSize(4f);
                Debug.Log($"[GameUIController] 📷 Camera restored to character at {center}");
            }
            else if (hasSavedCameraState)
            {
                cityMap.SetCameraOrthoSize(savedOrthoSize);
                hasSavedCameraState = false;
                Debug.Log("[GameUIController] 📷 Camera ortho size restored");
            }
        }

        private Button CreateButtonInParent(Transform parent, string label, Color color)
        {
            var obj = new GameObject("ActionButton");
            obj.transform.SetParent(parent, false);
            var img = obj.AddComponent<Image>();
            img.color = new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 0.9f);
            var btn = obj.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(color.r * 0.3f, color.g * 0.3f, color.b * 0.3f, 0.9f);
            colors.highlightedColor = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 1f);
            colors.pressedColor = new Color(color.r * 0.15f, color.g * 0.15f, color.b * 0.15f, 1f);
            btn.colors = colors;

            var textObj = new GameObject("Label");
            textObj.transform.SetParent(obj.transform, false);
            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            var rect = textObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 28;
            le.flexibleWidth = 1;
            return btn;
        }

        private void AddTextToParent(Transform parent, string text, Color color, bool bold = false)
        {
            var obj = new GameObject("TextLine");
            obj.transform.SetParent(parent, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 12;
            tmp.color = color;
            tmp.raycastTarget = false;
            if (bold) tmp.fontStyle = FontStyles.Bold;
            obj.AddComponent<LayoutElement>().preferredHeight = 20;
        }

        private void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        private string FormatEvent(GameEvent ev)
        {
            var d = ev.data;
            return ev.type switch
            {
                "order_result" => $"{d["hood_name"]} -> {d["order_type"]} @ {d["block_name"]}: {d["result"]} -- {(d.TryGetValue("details", out var det) ? det.ToString() : "")}",
                "squeal" => $"[SQUEAL] {d["npc_name"]} talked about {d["block_name"]}",
                "investigation" => $"[INVEST] {d["block_name"]} -- Leads {d["leads"]}/{d["threshold"]}",
                "arrest" => $"[ARREST] {d["hood_name"]} arrested!",
                "rival_action" => $"[RIVAL] {d["hood_name"]} -> {d["order_type"]} @ {d["block_name"]}: {d["result"]}",
                "economy" => $"[ECON] Income ${d["income"]} | Expenses ${d["expenses"]} | Net ${d["net"]} | Balance ${d["balance"]}",
                "territory_change" => $"[TERR] {d["block_name"]} -> {d["gang_id"]} (STR {d["strength"]})",
                "notification" => $"  {d["message"]}",
                _ => $"[{ev.type}]"
            };
        }

        private Color GetEventColor(string type, Dictionary<string, object> data)
        {
            return type switch
            {
                "order_result" => data.TryGetValue("result", out var r) && r.ToString() == "success" ? greenColor : yellowColor,
                "squeal" => yellowColor,
                "investigation" => yellowColor,
                "arrest" => redColor,
                "rival_action" => rivalColor,
                "economy" => greenColor,
                "territory_change" => data.TryGetValue("gang_id", out var g) && g.ToString() == "player" ? playerColor : rivalColor,
                "notification" => data.TryGetValue("tier", out var t) && t.ToString() == "red" ? redColor : yellowColor,
                _ => textBright
            };
        }

        private Color GetBlockColor(Block block)
        {
            if (block.isPoliceStation) return policeColor * 0.5f + cardBgColor * 0.5f;
            if (block.ownerGang == "player") return playerColor * 0.5f + cardBgColor * 0.5f;
            if (block.ownerGang == "rival") return rivalColor * 0.5f + cardBgColor * 0.5f;
            return unownedColor * 0.6f + cardBgColor * 0.4f;
        }

        private string GetOwnerLabel(Block block)
        {
            if (block.isPoliceStation) return "Police Station";
            if (block.ownerGang == "player") return "Your Territory";
            if (block.ownerGang == "rival") return "Rival Territory";
            return "Unowned";
        }

        private Color GetOwnerColor(Block block)
        {
            if (block.isPoliceStation) return policeColor;
            if (block.ownerGang == "player") return playerColor;
            if (block.ownerGang == "rival") return rivalColor;
            return mutedColor;
        }

        #endregion
    }
}
