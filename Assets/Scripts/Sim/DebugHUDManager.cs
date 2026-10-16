using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SteelCity.Sim
{
    /// <summary>
    /// Unified debug HUD — single IMGUI window with tabs for all debug overlays.
    /// Replaces the scattered OnGUI() methods in FollowCamera and CityMap3D.
    ///
    /// Tabs: [Camera] [Render] [Clothing] [Path] [Keys]
    /// Toggle: Backquote (`) key or O key
    ///
    /// Auto-finds FollowCamera, VoxelChunkManager, CityMap3D, and ClothingSystem
    /// in the scene. Missing components just skip their tab. The Keys tab is always
    /// available and lists only the hotkeys whose handlers currently exist.
    /// </summary>
    public class DebugHUDManager : MonoBehaviour
    {
        public enum Tab { Camera, Render, Clothing, Path, Combat, Cover, Vision, Demolitions, Weapons, MatRes, BuildLog, Keys, Sfx }
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Config")]
        [SerializeField] private Tab defaultTab = Tab.Keys;
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private float windowW = 560f;
        [SerializeField] private float windowH = 440f;
        [SerializeField] private float margin = 10f;

        private Corner currentCorner = Corner.TopLeft;

        private Tab activeTab;
        private bool visible = true;
        private bool minimized = false;
        private Vector2 scrollPos;
        private Texture2D rtProbeTex;   // BuildLog tab — proxyRT pixel probe

        // Drag state
        private bool isDragging;
        private Vector2 dragOffset;
        private Vector2 customPosition;
        private bool usingCustomPosition;

        // Canvas input blocker — transparent full-screen Image that captures raycasts
        // when the mouse is over the IMGUI panel, so IsPointerOverGameObject() returns true
        // and both 3D world clicks and Canvas UI clicks are blocked behind the panel.
        private GameObject blockerCanvasGO;
        private Image blockerImage;
        private Rect currentWindowRect;

        /// <summary>Static flag: true when mouse is currently over the debug panel.</summary>
        public static bool IsMouseOverPanel { get; private set; }

        // Cached references
        private FollowCamera followCamera;
        private VoxelChunkManager chunkManager;
        private CityMap3D cityMap;
        private GameUIController gameUI;
        private ClothingSystem clothingSystem; // currently selected
        private PathDebugRenderer pathDebug;

        // Combat tab state — set by clicking a range block while sim is Dwelling
        private string rangeBlockId;

        /// <summary>Selected firing lane (1=left, 2=center, 3=right). Read by
        /// GameUIController when resolving per-lane point names for
        /// target_practice orders.</summary>
        public static int RangeLane = 2;
        /// <summary>When true (lane 3 only), the aim point is the glass mold's
        /// metal core ("glass_target") instead of the lane's firing_target.</summary>
        public static bool RangeAimMold = false;
        private ArmAimSolver solver;
        private bool blockClickHooked;

        // Free-fire picks — voxel clicks armed from the Combat/Demolitions tabs. PICK STAND
        // teleports the character (DebugSnapTo) onto the hit surface; PICK AIM
        // overrides the lane/mold aim point so shots can target anything.
        private enum PickSlot { None, Stand, Aim, Blast, Building, Inspect }
        private PickSlot pickArmed = PickSlot.None;
        private Vector3? pickedStand, pickedAim;
        private CoverSensor coverSensor;   // Vinny's always-on cover awareness (COVER tab)
        private VisionSensor visionSensor; // Vinny's vision sense (VISION tab)
        private string inspectHud;                             // last voxel-inspect report (HUD)
        private VoxelCollisionWorld World => world != null ? world : (world = cityMap != null ? cityMap.GetComponent<VoxelCollisionWorld>() : null);
        private VoxelCollisionWorld world;
        // Demolition bench — surface pick + inward depth push puts the blast inside.
        private Vector3? pickedBlast;
        private Vector3 blastNormal;
        private string blastVolume;
        private bool blastAtVolumeCenter;
        private float blastDepth = 0.8f, blastRadius = 6f, blastPower = 4f;

        // Optional test/debug components — used by the Keys tab to show only
        // hotkeys whose handlers actually exist in the scene right now.
        private VehicleTestSpawner vehicleTest;
        private StressTestSpawner stressTest;
        private StressTestDiagnostics stressDiag;
        private HoodSpawner hoodSpawner;
        private bool testRigPresent;

        // Clothing system multi-instance tracking
        private List<ClothingSystem> allClothingSystems = new();
        private int selectedClothingIndex = 0;
        private float clothingListRefreshTimer = 0f;

        // Character rig multi-instance tracking (for hotkey control routing)
        private List<CharacterRig> allCharacterRigs = new();
        private int selectedRigIndex = 0;

        // Cached styles
        private GUIStyle labelStyle;
        private GUIStyle toggleStyle;
        private GUIStyle boldStyle;
        private GUIStyle tabStyle;
        private GUIStyle tabActiveStyle;
        private GUIStyle bgStyle;
        private Texture2D bgTex;
        private Texture2D tabActiveTex;
        private Texture2D tabNormalTex;

        // Cached perf data for Render tab
        private float fps;
        private float frameTimeMs;
        private float frameTimeMin, frameTimeMax, frameTimeAvg;
        private float fpsAccumTime;
        private int fpsAccumFrames;

        // IMGUI cost control — OnGUI fires once per event (Layout + Repaint +
        // every input event), so each widget runs several times per frame and
        // scene-scanning every event is what tanked the frame rate. Text-only
        // tabs rebuild into ONE cached rich-text string at ~4 Hz; interactive
        // tabs (Clothing/Range) keep live widgets.
        private readonly System.Text.StringBuilder hudSB = new();
        private string tabText;
        private string footerText;
        private Tab lastBuiltTab = (Tab)(-1);
        private int matResSub;                                 // MatRes sub-tab: 0 building / 1 vehicle / 2 item
        private int matResSel = -1;                            // MatRes selected class (click a name) — promote-to-authored target
        private float nextTabRefresh;
        private float nextFooterRefresh;
        private float nextRefsRefresh;

        void Awake()
        {
            activeTab = defaultTab;
            visible = showOnStart;
            minimized = false;
            SetupBlockerCanvas();
            BuildDiag.Event("DebugHUDManager.Awake");
        }

        void Start()
        {
            RefreshReferences();
            RefreshClothingSystems();
        }

        /// <summary>
        /// Create a hidden Canvas with a transparent Image sized to match the IMGUI panel.
        /// When enabled, it captures raycasts so IsPointerOverGameObject() returns true
        /// for clicks inside the panel area — blocking both 3D world clicks and Canvas UI.
        /// </summary>
        void SetupBlockerCanvas()
        {
            blockerCanvasGO = new GameObject("DebugHUD_BlockerCanvas");
            blockerCanvasGO.transform.SetParent(transform);

            var canvas = blockerCanvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999; // Above game UI

            blockerCanvasGO.AddComponent<CanvasScaler>();
            blockerCanvasGO.AddComponent<GraphicRaycaster>();

            var imgGO = new GameObject("BlockerImage");
            imgGO.transform.SetParent(blockerCanvasGO.transform, false);
            blockerImage = imgGO.AddComponent<Image>();
            blockerImage.color = new Color(0, 0, 0, 0); // Fully transparent
            blockerImage.raycastTarget = true;

            // Anchor at bottom-left so position matches screen-space (0,0 = bottom-left)
            var rt = imgGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;

            blockerCanvasGO.SetActive(false);
        }

        /// Returns true when every reference resolved — a null ref left behind
        /// means an absent scene object (test rigs etc.) that callers should
        /// retry on a slow cadence rather than rescanning the scene rapidly.
        bool RefreshReferences()
        {
            if (followCamera == null) followCamera = FindFirstObjectByType<FollowCamera>();
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (cityMap == null) cityMap = FindFirstObjectByType<CityMap3D>();
            if (gameUI == null) gameUI = FindFirstObjectByType<GameUIController>();
            if (cityMap != null && !blockClickHooked)
            {
                cityMap.OnBlockClicked += HandleBlockClicked;
                cityMap.OnVoxelPicked += HandleVoxelPicked;
                blockClickHooked = true;
            }
            if (pathDebug == null) pathDebug = FindFirstObjectByType<PathDebugRenderer>();
            if (vehicleTest == null) vehicleTest = FindFirstObjectByType<VehicleTestSpawner>();
            if (stressTest == null) stressTest = FindFirstObjectByType<StressTestSpawner>();
            if (stressDiag == null) stressDiag = FindFirstObjectByType<StressTestDiagnostics>();
            if (hoodSpawner == null) hoodSpawner = FindFirstObjectByType<HoodSpawner>();
            return followCamera != null && chunkManager != null && cityMap != null
                && gameUI != null && pathDebug != null && vehicleTest != null
                && stressTest != null && stressDiag != null && hoodSpawner != null;
        }

        void RefreshClothingSystems()
        {
            allClothingSystems.Clear();
            var found = FindObjectsByType<ClothingSystem>(FindObjectsSortMode.None);
            foreach (var cs in found)
                if (cs != null && cs.gameObject != null)
                    allClothingSystems.Add(cs);

            // Clamp selection
            if (selectedClothingIndex >= allClothingSystems.Count)
                selectedClothingIndex = Mathf.Max(0, allClothingSystems.Count - 1);

            // Set active clothing system
            clothingSystem = (allClothingSystems.Count > 0) ? allClothingSystems[selectedClothingIndex] : null;

            // Also refresh CharacterRigs
            allCharacterRigs.Clear();
            var rigs = FindObjectsByType<CharacterRig>(FindObjectsSortMode.None);
            foreach (var r in rigs)
                if (r != null && r.gameObject != null)
                    allCharacterRigs.Add(r);
            if (selectedRigIndex >= allCharacterRigs.Count)
                selectedRigIndex = Mathf.Max(0, allCharacterRigs.Count - 1);

            // Optional single-key test rigs — any one present means its keys are live
            testRigPresent = FindFirstObjectByType<ForwardTransformTestRig>() != null
                          || FindFirstObjectByType<AnimationTestSpawner>() != null
                          || FindFirstObjectByType<CharacterTestRig>() != null;
        }

        bool TabAvailable(Tab t) => t switch
        {
            Tab.Camera => followCamera != null,
            Tab.Render => chunkManager != null,
            Tab.Clothing => allClothingSystems.Count > 0,
            Tab.Path => pathDebug != null,
            Tab.Combat or Tab.Cover or Tab.Vision or Tab.Demolitions => cityMap != null,
            Tab.Sfx => true,
            _ => true,
        };

        System.Collections.Generic.List<Tab> GetAvailableTabs()
        {
            var tabs = new System.Collections.Generic.List<Tab>();
            foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
                if (TabAvailable(t)) tabs.Add(t);
            return tabs;
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.backquoteKey.wasPressedThisFrame || kb.oKey.wasPressedThisFrame)
            {
                visible = !visible;
            }

            // Remaining HUD keys only apply while the panel is visible
            if (visible)
            {
                // Tab cycling — only cycle through available tabs
                if (kb.tabKey.wasPressedThisFrame)
                {
                    RefreshReferences();
                    RefreshClothingSystems();
                    var avail = GetAvailableTabs();
                    if (avail.Count > 0)
                    {
                        int idx = avail.IndexOf(activeTab);
                        if (idx < 0) idx = 0;
                        idx = (idx + 1) % avail.Count;
                        activeTab = avail[idx];
                    }
                }

                // Corner cycling with Y key (resets custom position)
                if (kb.yKey.wasPressedThisFrame)
                {
                    currentCorner = (Corner)(((int)currentCorner + 1) % 4);
                    usingCustomPosition = false;
                }

                // Cover sensor threat sync — the PICK AIM point is the shared
                // threat context for both combat and cover inspection.
                if (coverSensor != null && coverSensor.Threat != pickedAim)
                {
                    coverSensor.Threat = pickedAim;
                    coverSensor.Refresh(true);
                }
                // Vision testbed: the same pick point is "the thing he's looking at".
                if (visionSensor != null) visionSensor.SetTarget(pickedAim);

                // Minimize toggle with M key
                if (kb.mKey.wasPressedThisFrame)
                {
                    minimized = !minimized;
                }
            }

            // Periodically refresh clothing system list (every 1 second).
            // Gated on visible: the refresh runs 2 FindObjectsByType + 3
            // FindFirstObjectByType scene scans — doing that once per second
            // while the HUD is hidden was a periodic frame spike.
            if (visible)
            {
                clothingListRefreshTimer += Time.deltaTime;
                if (clothingListRefreshTimer >= 1f)
                {
                    clothingListRefreshTimer = 0f;
                    RefreshClothingSystems();
                }
            }

            // Update IsMouseOverPanel flag using current mouse position vs panel rect.
            // currentWindowRect is stored in screen-space (bottom-left origin) in OnGUI.
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && visible)
            {
                Vector2 mp = mouse.position.ReadValue();
                IsMouseOverPanel = currentWindowRect.Contains(mp);

                if (blockerCanvasGO != null)
                {
                    bool shouldBlock = IsMouseOverPanel;
                    if (blockerCanvasGO.activeSelf != shouldBlock)
                        blockerCanvasGO.SetActive(shouldBlock);

                    if (shouldBlock)
                    {
                        // Position the blocker Image to exactly match the panel in screen space
                        var rt = blockerImage.GetComponent<RectTransform>();
                        rt.anchoredPosition = new Vector2(currentWindowRect.x, currentWindowRect.y);
                        rt.sizeDelta = new Vector2(currentWindowRect.width, currentWindowRect.height);
                    }
                }
            }
            else
            {
                IsMouseOverPanel = false;
                if (blockerCanvasGO != null && blockerCanvasGO.activeSelf)
                    blockerCanvasGO.SetActive(false);
            }
        }

        void OnDisable()
        {
            IsMouseOverPanel = false;
            if (blockerCanvasGO != null)
                blockerCanvasGO.SetActive(false);
        }

        void OnDestroy()
        {
            if (cityMap != null && blockClickHooked)
            {
                cityMap.OnBlockClicked -= HandleBlockClicked;
                cityMap.OnVoxelPicked -= HandleVoxelPicked;
            }
            if (blockerCanvasGO != null)
                DestroyImmediate(blockerCanvasGO);
        }

        void LateUpdate()
        {
            // FPS tracking
            fpsAccumTime += Time.unscaledDeltaTime;
            fpsAccumFrames++;
            if (fpsAccumTime >= 0.5f)
            {
                fps = fpsAccumFrames / fpsAccumTime;
                fpsAccumTime = 0f;
                fpsAccumFrames = 0;
            }
            frameTimeMs = Time.unscaledDeltaTime * 1000f;
            frameTimeMin = Mathf.Min(frameTimeMin > 0 ? frameTimeMin : frameTimeMs, frameTimeMs);
            frameTimeMax = Mathf.Max(frameTimeMax, frameTimeMs);
            frameTimeAvg = frameTimeAvg > 0 ? frameTimeAvg * 0.95f + frameTimeMs * 0.05f : frameTimeMs;
        }

        void InitStyles()
        {
            if (labelStyle != null) return;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true,
                normal = { textColor = new Color(0.88f, 0.92f, 1f) }
            };

            boldStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.1f, 1f, 1f) }
            };

            // Toggles need the skin's toggle style — a label style renders the row
            // clickable but with no checkbox glyph.
            toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 12,
                richText = true,
                normal = { textColor = new Color(0.88f, 0.92f, 1f) },
                onNormal = { textColor = new Color(0.88f, 0.92f, 1f) }
            };

            bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.06f, 0.06f, 0.10f, 0.90f));
            bgTex.Apply();

            bgStyle = new GUIStyle(GUI.skin.box) { normal = { background = bgTex } };

            tabActiveTex = new Texture2D(1, 1);
            tabActiveTex.SetPixel(0, 0, new Color(0.15f, 0.35f, 0.65f, 0.95f));
            tabActiveTex.Apply();

            tabNormalTex = new Texture2D(1, 1);
            tabNormalTex.SetPixel(0, 0, new Color(0.12f, 0.12f, 0.18f, 0.80f));
            tabNormalTex.Apply();

            tabStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                normal = { background = tabNormalTex, textColor = new Color(0.6f, 0.7f, 0.8f) },
                hover = { background = tabNormalTex, textColor = new Color(0.8f, 0.9f, 1f) },
                active = { background = tabNormalTex },
                padding = new RectOffset(8, 8, 4, 4)
            };

            tabActiveStyle = new GUIStyle(tabStyle)
            {
                normal = { background = tabActiveTex, textColor = Color.white },
                hover = { background = tabActiveTex, textColor = Color.white }
            };
        }

        void OnGUI()
        {
            if (!visible) return;

            InitStyles();
            // FindFirstObjectByType scans the scene — 8 of them per IMGUI
            // event (~4-6 events/frame) was a measurable stall. Throttled.
            // Missing refs (absent test rigs) retry on a slow cadence — they
            // only matter if such a rig spawns mid-session.
            if (Time.unscaledTime >= nextRefsRefresh)
            {
                bool complete = RefreshReferences();
                nextRefsRefresh = Time.unscaledTime + (complete ? 1f : 3f);
            }

            // Position window — custom drag position or corner snap
            float sw = Screen.width;
            float sh = Screen.height;
            float x, y;
            if (usingCustomPosition)
            {
                x = customPosition.x;
                y = customPosition.y;
            }
            else
            {
                switch (currentCorner)
                {
                    case Corner.TopLeft:     x = margin;       y = margin;       break;
                    case Corner.TopRight:    x = sw - windowW - margin;  y = margin;       break;
                    case Corner.BottomLeft:  x = margin;       y = sh - windowH - margin;  break;
                    case Corner.BottomRight: x = sw - windowW - margin;  y = sh - windowH - margin;  break;
                    default:                 x = margin;       y = margin;       break;
                }
            }

            // Minimized = just the title bar
            float effectiveH = minimized ? 28f : windowH;
            var windowRect = new Rect(x, y, windowW, effectiveH);

            // Convert GUI rect (top-left origin) to screen rect (bottom-left origin)
            // so Mouse.current.position comparisons in Update() are correct.
            currentWindowRect = new Rect(x, Screen.height - y - effectiveH, windowW, effectiveH);

            // Drag handling — track state, but do NOT consume Event.current
            // (the Canvas blocker already stops game/Canvas clicks from behind the panel).
            var headerRect = new Rect(x, y, windowW, 28f);
            var ev = Event.current;
            if (ev != null && headerRect.Contains(ev.mousePosition))
            {
                if (ev.type == EventType.MouseDown && ev.button == 0)
                {
                    isDragging = true;
                    dragOffset = ev.mousePosition - new Vector2(x, y);
                }
            }
            if (isDragging && ev != null && ev.type == EventType.MouseDrag)
            {
                customPosition = ev.mousePosition - dragOffset;
                // Clamp to screen
                customPosition.x = Mathf.Clamp(customPosition.x, 0, sw - windowW);
                customPosition.y = Mathf.Clamp(customPosition.y, 0, sh - effectiveH);
                usingCustomPosition = true;
            }
            if (isDragging && ev != null && ev.type == EventType.MouseUp && ev.button == 0)
            {
                isDragging = false;
            }

            // IMGUI cost control: interactive tabs (Range/Clothing) redraw every
            // widget on every event — in the Editor, cursor movement over the
            // Game view floods MouseMove/MouseDrag events (dozens per frame), so
            // a moving camera pays the full tab rebuild many times per frame.
            // Nothing here consumes MouseMove (hover only), and a drag with no
            // GUI control hot and no panel drag in progress belongs to the
            // scene camera — safe to skip both.
            if (ev != null && (ev.type == EventType.MouseMove
                || (ev.type == EventType.MouseDrag && !isDragging && GUIUtility.hotControl == 0)))
                return;

            GUILayout.BeginArea(windowRect, bgStyle);
            GUILayout.BeginVertical();

            // Header — title + minimize button, then TWO tab rows (9 tabs would
            // clip a 560px window in one row): viewer/systems, then the sim suite
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            GUILayout.Label("<b>DEBUG HUD</b>", boldStyle, GUILayout.Width(80));
            GUILayout.FlexibleSpace();

            // Minimize / expand button
            string minLabel = minimized ? "+" : "_";
            if (GUILayout.Button(minLabel, GUILayout.Width(24), GUILayout.Height(20)))
            {
                minimized = !minimized;
            }

            GUILayout.EndHorizontal();

            if (minimized)
            {
                GUILayout.EndVertical();
                GUILayout.EndArea();
                return;
            }

            DrawTabRow(Tab.Camera, Tab.Render, Tab.Sfx, Tab.Clothing, Tab.Path, Tab.Keys);
            DrawTabRow(Tab.Combat, Tab.Cover, Tab.Vision, Tab.Demolitions, Tab.Weapons, Tab.MatRes, Tab.BuildLog);

            // Separator
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));

            // Tab content
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));

            switch (activeTab)
            {
                case Tab.Clothing: DrawClothingTab(); break;
                case Tab.Combat:
                    UnityEngine.Profiling.Profiler.BeginSample("HUD.DrawCombatTab");
                    try { DrawCombatTab(); } finally { UnityEngine.Profiling.Profiler.EndSample(); }
                    break;
                case Tab.Demolitions:
                    UnityEngine.Profiling.Profiler.BeginSample("HUD.DrawDemolitionsTab");
                    try { DrawDemolitionsTab(); } finally { UnityEngine.Profiling.Profiler.EndSample(); }
                    break;
                case Tab.Cover: DrawCoverTab(); break;
                case Tab.Vision: DrawVisionTab(); break;
                case Tab.Weapons: DrawWeaponsTab(); break;
                case Tab.Sfx: DrawSfxTab(); break;
                case Tab.MatRes:
                    UnityEngine.Profiling.Profiler.BeginSample("HUD.DrawMatResTab");
                    try { DrawMatResTab(); } finally { UnityEngine.Profiling.Profiler.EndSample(); }
                    break;
                default:
                    if (activeTab != lastBuiltTab
                            || Time.unscaledTime >= nextTabRefresh)
                        RebuildTabText();
                    GUILayout.Label(tabText, labelStyle);
                    if (activeTab == Tab.Render) DrawShadowControls();
                    break;
            }

            GUILayout.EndScrollView();

            // Footer — current tab + next tab + controls
            GUILayout.Space(4);

            if (Time.unscaledTime >= nextFooterRefresh)
                RebuildFooterText();
            GUILayout.Label(footerText ?? "", labelStyle);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void DrawTabRow(params Tab[] row)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            foreach (var t in row)
            {
                if (!TabAvailable(t)) continue;
                var style = activeTab == t ? tabActiveStyle : tabStyle;
                if (GUILayout.Button(t.ToString(), style, GUILayout.Height(20)))
                    activeTab = t;
            }
            GUILayout.EndHorizontal();
        }

        static string TabName(Tab t) => t.ToString();

        // boldStyle colour inline — cached text carries no per-line style
        static string H(string s) => $"<color=#19FFFF><b>{s}</b></color>";

        /// <summary>
        /// Rebuild the active text-only tab into one cached rich-text block —
        /// a single GUILayout.Label per repaint instead of ~40 widgets per
        /// IMGUI event. Interactive tabs stay live and skip this path.
        /// </summary>
        void RebuildTabText()
        {
            hudSB.Length = 0;
            switch (activeTab)
            {
                case Tab.Camera: BuildCameraTab(hudSB); break;
                case Tab.Render: BuildRenderTab(hudSB); break;
                case Tab.Path: BuildPathTab(hudSB); break;
                case Tab.BuildLog: BuildBuildLogTab(hudSB); break;
                default: BuildKeysTab(hudSB); break;
            }
            tabText = hudSB.ToString();
            lastBuiltTab = activeTab;
            nextTabRefresh = Time.unscaledTime + 0.25f;
        }

        void RebuildFooterText()
        {
            var avail = GetAvailableTabs();
            int curIdx = avail.IndexOf(activeTab);
            if (curIdx < 0) curIdx = 0;
            int nextIdx = (curIdx + 1) % avail.Count;
            hudSB.Length = 0;
            hudSB.AppendLine($"<b>Current:</b> {TabName(avail[curIdx])}    <b>Next [Tab]:</b> {TabName(avail[nextIdx])}    <b>Pos [Y]:</b> {(usingCustomPosition ? "Dragged" : currentCorner.ToString())}");
            hudSB.Append('\n');
            hudSB.AppendLine(H("Hotkeys") + " — full list in [Keys] tab");
            hudSB.AppendLine("[`] / [O] Hide  [Tab] Next tab  [Y] Corner  [M] Minimize");
            hudSB.AppendLine("[LMB] Select/focus  [MMB] Rotate  [RMB] Pan  [Wheel] Zoom");
            hudSB.AppendLine("[G] Possess Vinny (WASD/mouse/LMB)");
            hudSB.AppendLine("[R] Render scale  [F6] Free cam  [F7] Waypoints  [F8] Freeze  [F10] Vehicles");
            hudSB.AppendLine("<i>Drag title bar to move. Click _ to minimize.</i>");
            footerText = hudSB.ToString();
            nextFooterRefresh = Time.unscaledTime + 0.25f;
        }

        void BuildCameraTab(System.Text.StringBuilder sb)
        {
            if (followCamera == null)
            {
                sb.AppendLine("No FollowCamera in scene.");
                return;
            }

            sb.AppendLine($"<b>Mode</b>: {(followCamera.ChaseMode ? "CHASE" : "ORBIT")}");
            sb.AppendLine($"<b>Position</b>: {followCamera.transform.position}");
            sb.AppendLine($"<b>FOV</b>: {followCamera.FieldOfView:F1}");
            sb.AppendLine($"<b>Distance</b>: {followCamera.Distance:F2}");
            sb.AppendLine($"<b>Height</b>: {followCamera.Height:F2}");
            sb.AppendLine($"<b>Yaw</b>: {followCamera.CurrentYaw:F1} deg");
            sb.AppendLine($"<b>Pitch</b>: {followCamera.CurrentPitch:F1} deg");
            sb.AppendLine($"<b>Follow Speed</b>: {followCamera.FollowSpeed:F1}");
            sb.AppendLine($"<b>Look Speed</b>: {followCamera.LookSpeed:F1}");
            sb.Append('\n');
            sb.AppendLine(H("Controls"));
            sb.AppendLine("[T] Chase/Orbit toggle");
            sb.AppendLine("[Shift] Free-look");
            sb.AppendLine("[Z] Reset camera");
            sb.AppendLine("[Q/E] Distance  [R/F] Height");
            sb.AppendLine("[C] Capture  [P] Perf log");
        }

        /// <summary>
        /// BuildLog — player-build diagnostics: what the build actually resolved
        /// (shaders, kernels, render path, draw counts) plus the init event trail.
        /// Lives on the cached-text path (4Hz rebuild) so it costs ~one label.
        /// </summary>
        void BuildBuildLogTab(System.Text.StringBuilder sb)
        {
            if (BuildDiag.StateKeys.Count == 0 && BuildDiag.Events.Count == 0)
            {
                sb.AppendLine("No diagnostics recorded yet —");
                sb.AppendLine("nothing instrumented has run.");
                return;
            }

            sb.AppendLine(H("Device / init state"));
            foreach (var key in BuildDiag.StateKeys)
            {
                if (key.StartsWith("render.") || key.StartsWith("bridge.")) continue;
                BuildDiag.TryGet(key, out var v);
                sb.AppendLine($"  {key}: {v}");
            }
            sb.Append('\n');

            sb.AppendLine(H("Render (live)"));
            bool any = false;
            foreach (var key in BuildDiag.StateKeys)
            {
                if (!key.StartsWith("render.") && !key.StartsWith("bridge.")) continue;
                BuildDiag.TryGet(key, out var v);
                sb.AppendLine($"  {key}: {v}");
                any = true;
            }
            if (!any) sb.AppendLine("  (no render calls yet)");
            // RT probe — sample a 48x48 center block of the live render texture.
            // Flat (lum range ≈ 0) = draws emit but nothing lands in the RT.
            if (chunkManager != null)
            {
                var rt = chunkManager.GetColorTexture();
                if (rt != null)
                {
                    int N = Mathf.Min(48, rt.width, rt.height);
                    if (rtProbeTex == null || rtProbeTex.width != N)
                        rtProbeTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    rtProbeTex.ReadPixels(new Rect((rt.width - N) / 2, (rt.height - N) / 2, N, N), 0, 0);
                    rtProbeTex.Apply();
                    RenderTexture.active = prev;
                    var px = rtProbeTex.GetPixels();
                    float minL = 1f, maxL = 0f;
                    foreach (var p in px)
                    {
                        float l = p.grayscale;
                        if (l < minL) minL = l;
                        if (l > maxL) maxL = l;
                    }
                    sb.AppendLine($"  rt.probe: {N}px block, center {px[px.Length / 2]} lumRange {minL:F2}–{maxL:F2}" +
                        (maxL - minL < 0.02f ? " <b>FLAT — RT holds only clear color</b>" : " content present"));
                }
            }
            sb.Append('\n');

            sb.AppendLine(H($"Init log ({BuildDiag.Events.Count})"));
            int start = Mathf.Max(0, BuildDiag.Events.Count - 60);
            for (int i = start; i < BuildDiag.Events.Count; i++)
                sb.AppendLine(BuildDiag.Events[i]);
        }

        /// Live shadow controls appended under the Render tab's cached text —
        /// the GameUI "SHADOW DEBUG" accordion is buried in the Orders page.
        /// A handful of widgets per event keeps the text-tab cost profile.
        void DrawShadowControls()
        {
            if (cityMap == null || chunkManager == null) return;
            GUILayout.Space(6);
            bool on = chunkManager.GetShadowEnabled() == 1;
            if (GUILayout.Toggle(on, " shadows (in-volume sun march)", toggleStyle) != on)
                cityMap.SetShadowEnabled(!on);
            int maxSteps = Mathf.RoundToInt(HSlider("shadow max steps",
                chunkManager.GetShadowMaxSteps(), 1f, 64f, "F0"));
            if (maxSteps != chunkManager.GetShadowMaxSteps())
                cityMap.SetShadowMaxSteps(maxSteps);
        }

        void BuildRenderTab(System.Text.StringBuilder sb)
        {
            if (chunkManager == null)
            {
                sb.AppendLine("No VoxelChunkManager in scene.");
                return;
            }

            sb.AppendLine($"<b>FPS</b>: {fps:F0}  |  Frame: {frameTimeMs:F2}ms");
            sb.AppendLine($"  min:{frameTimeMin:F2}  max:{frameTimeMax:F2}  avg:{frameTimeAvg:F2}");
            sb.AppendLine($"<b>Resolution</b>: {chunkManager.RenderWidth}x{chunkManager.RenderHeight}   <b>scale</b> {chunkManager.CurrentResolutionScale:F2}  ([R] cycles 0.5/0.65/0.75/1.0)");
            sb.AppendLine($"<b>Ortho</b>: {chunkManager.IsOrtho}  Size: {chunkManager.CameraOrthoSize:F1}");
            sb.Append('\n');
            sb.AppendLine(H("Draw Calls"));
            sb.AppendLine($"  Total: {chunkManager.PerfTotalDrawCalls}");
            sb.AppendLine($"  Sectors: {chunkManager.PerfSectorsDrawn}  Instanced chars: {chunkManager.InstancedCharacterCount}");
            sb.AppendLine($"  Chunks: {chunkManager.PerfTotalChunks} total / {chunkManager.PerfActiveChunks} active / {chunkManager.PerfDrawnChunks} drawn");
            sb.AppendLine($"  Baked: {chunkManager.BakedSectorCount} sectors / {chunkManager.BakedSectorBuildingCount} buildings");
            sb.Append('\n');
            sb.AppendLine(H("LOD Tiers"));
            sb.AppendLine($"  Near: {chunkManager.PerfLodNear}  Mid: {chunkManager.PerfLodMid}  Far: {chunkManager.PerfLodFar}");
            sb.AppendLine($"  Ultra: {chunkManager.PerfLodUltra}  Culled: {chunkManager.PerfLodCulled}");
            sb.AppendLine($"  ScreenRatio: {chunkManager.PerfMinScreenRatio:F4} - {chunkManager.PerfMaxScreenRatio:F4}");
            sb.Append('\n');
            sb.AppendLine(H("CPU Timing"));
            sb.AppendLine($"  Cull: {chunkManager.CpuCullMs:F2}ms  Draw: {chunkManager.CpuDrawMs:F2}ms  Total: {chunkManager.CpuTotalMs:F2}ms");
        }

        void DrawClothingTab()
        {
            if (allClothingSystems.Count == 0)
            {
                GUILayout.Label("No ClothingSystem in scene.", labelStyle);
                GUILayout.Label("ClothingSystem auto-adds to VoxelCharacter.", labelStyle);
                return;
            }

            // Character selector — controls which rig receives hotkeys (T/I/W/L/A/C etc.)
            if (allCharacterRigs.Count > 0)
            {
                GUILayout.Label("<b>Character Control (hotkeys)</b>", boldStyle);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < allCharacterRigs.Count; i++)
                {
                    var r = allCharacterRigs[i];
                    if (r == null) continue;
                    string label = $"#{i}: {r.gameObject.name}";
                    bool active = (i == selectedRigIndex);
                    var oldBg = GUI.backgroundColor;
                    if (active) GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                    if (GUILayout.Button(label, active ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                    {
                        selectedRigIndex = i;
                        // Deactivate all, activate selected
                        foreach (var rig in allCharacterRigs)
                            if (rig != null) rig.Controllable = false;
                        r.Controllable = true;
                    }
                    GUI.backgroundColor = oldBg;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
            }

            // Clothing instance selector
            GUILayout.Label("<b>Clothing Instance</b>", boldStyle);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < allClothingSystems.Count; i++)
            {
                var cs = allClothingSystems[i];
                if (cs == null) continue;
                string label = $"#{i}: {cs.gameObject.name}";
                bool active = (i == selectedClothingIndex);
                var oldBg = GUI.backgroundColor;
                if (active) GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
                if (GUILayout.Button(label, active ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                {
                    selectedClothingIndex = i;
                    clothingSystem = cs;
                }
                GUI.backgroundColor = oldBg;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Draw the selected instance's clothing controls
            if (clothingSystem != null)
                clothingSystem.DrawClothingTab();
            else
                GUILayout.Label("No instance selected.", labelStyle);
        }

        /// <summary>
        /// Hotkey cheat-sheet. Sections only appear when the component that owns
        /// those keys is actually present in the scene — stale keys are never listed.
        /// </summary>
        void BuildKeysTab(System.Text.StringBuilder sb)
        {
            sb.AppendLine(H("This Panel"));
            sb.AppendLine("[`] / [O]  Toggle panel      [Tab] Next tab");
            sb.AppendLine("[Y]  Cycle corner            [M] Minimize");
            sb.AppendLine("Drag title bar to reposition freely");
            sb.Append('\n');
            sb.AppendLine(H("Map Camera") + " (mouse over map viewport)");
            sb.AppendLine("[LMB] Click — select block / focus camera");
            sb.AppendLine("[MMB] Drag — rotate (yaw + pitch)");
            sb.AppendLine("[RMB] Drag — pan (execution mode needs [F6])");
            sb.AppendLine("[Wheel] — zoom in / out");
            sb.Append('\n');
            sb.AppendLine(H("Scene / Render Debug"));
            sb.AppendLine("[F6] Free camera (also disables debris)");
            sb.AppendLine("[F7] Waypoint graph beams");
            sb.AppendLine("[R] Cycle render scale: 0.5 / 0.65 / 0.75 / 1.0");
            sb.AppendLine("[V] Cycle ray-march heatmap: heat / steps / miss waste / off");

            if (allCharacterRigs.Count > 0)
            {
                sb.Append('\n');
                sb.AppendLine(H("Character Control"));
                sb.AppendLine("[G] Possess: WASD move · mouse look · LMB fire");
                sb.AppendLine("<i>anim letter keys deprecated — animHotkeys on rig</i>");
            }

            if (vehicleTest != null)
            {
                sb.Append('\n');
                sb.AppendLine(H("Vehicles"));
                sb.AppendLine("[F10] Spawn parked cars / toggle driving");
            }

            if (stressTest != null || stressDiag != null)
            {
                sb.Append('\n');
                sb.AppendLine(H("Stress Test"));
                if (stressTest != null)
                {
                    sb.AppendLine("[F8] Spawn agents and run test");
                    sb.AppendLine("[F7] Cycle path-beam count (during test)");
                }
                if (stressDiag != null)
                    sb.AppendLine("[F8] Start diagnostics    [F9] Stop");
            }

            if (hoodSpawner != null || testRigPresent)
            {
                sb.Append('\n');
                sb.AppendLine(H("Test Rigs"));
                if (hoodSpawner != null)
                    sb.AppendLine("[1-9,0] Force animation state");
                if (testRigPresent)
                    sb.AppendLine("[T/I/W/L/A/C] States  [R] Reload  [Space] Pause  [=/-] Speed");
            }
        }

        void BuildPathTab(System.Text.StringBuilder sb)
        {
            if (pathDebug == null)
            {
                sb.AppendLine("No PathDebugRenderer in scene.");
                return;
            }

            sb.AppendLine($"<b>Active Paths</b>: {pathDebug.ActivePathCount}");
            sb.AppendLine($"<b>Debug Enabled</b>: {pathDebug.DebugEnabled}");
            sb.Append('\n');
            sb.AppendLine("Path debug beams render into the");
            sb.AppendLine("voxel raymarch overlay (right panel).");
            sb.AppendLine("Paths are drawn during execution phase");
            sb.AppendLine("when characters/vehicles are moving.");
        }

        // ------------------------------------------------------------------
        // Combat tab — live firing-range tuning/debug.
        // Opens automatically when a block with a firing_position is clicked
        // while the sim is Dwelling (Target Practice hold-open state).
        // ------------------------------------------------------------------

        private void HandleBlockClicked(string blockId)
        {
            var sim = gameUI != null ? gameUI.Sim : null;
            if (sim == null || sim.State != SimState.Dwelling) return;
            if (cityMap == null) return;
            bool hasPos = cityMap.TryGetBuildingPointLocal(blockId, "firing_position", out _);
            Debug.Log($"[DebugHUD] 🔵 Dwell click on {blockId} — firing_position: {(hasPos ? "found" : "absent")}");
            if (!hasPos) return;
            OpenCombatTab(blockId);
        }

        /// <summary>
        /// Debug snap: while dwelling at a range block, move the spawned
        /// character onto lane N's firing_position and face him downrange at
        /// that lane's firing_target. Teleports — no pathing (the dwell state
        /// has no move queue, and lane-switching is a bench tool, not sim).
        /// </summary>
        private void MoveCharacterToLane(string blockId, int lane)
        {
            var sim = gameUI != null ? gameUI.Sim : null;
            if (sim == null || sim.State != SimState.Dwelling) return;
            var ch = cityMap.SpawnedCharacter;
            var mount = ch != null ? ch.GetComponent<WeaponMount>() : null;
            if (mount == null) return;
            if (!cityMap.TryGetBuildingPointLocal(blockId, $"firing_position_{lane}", out var posLocal)) return;
            if (solver != null && solver.IsAiming) solver.Disarm();
            var ep = FindFirstObjectByType<EventPlayer>();
            if (ep == null) return;
            Vector3? faceWorld = cityMap.TryGetBuildingPointLocal(blockId, $"firing_target_{lane}", out var tgtLocal)
                ? cityMap.MapRoot.position + tgtLocal : (Vector3?)null;
            ep.DebugSnapTo(cityMap.MapRoot.position + posLocal, mount.Dims, mount.CharVoxelSize, faceWorld);
        }

        /// <summary>
        /// Voxel pick delivery — fires when CityMap3D's pick mode consumed a map click.
        /// STAND teleports the character onto the hit surface (facing the aim point if
        /// set); AIM stores the hit point as the fire target and re-arms a live solver.
        /// </summary>
        private void HandleVoxelPicked(VoxelRayHit hit)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pickArmed == PickSlot.Stand)
            {
                // Wall-face picks push the stand point off the surface so he lands
                // beside the wall instead of inside it; a down-raycast (volumes +
                // terrain) finds the surface to stand on — roof, floor, or street.
                Vector3 snap = hit.point;
                Vector3 nXZ = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (nXZ.sqrMagnitude > 0.01f) snap += nXZ.normalized * 0.45f;
                float? feetY = null;
                if (World != null && World.Raycast(snap + Vector3.up * 0.05f, Vector3.down, 40f, out var gh))
                    feetY = gh.point.y;
                else if (hit.normal.y > 0.5f)
                    feetY = hit.point.y;
                pickedStand = new Vector3(snap.x, feetY ?? snap.y, snap.z);
                var ch = cityMap.SpawnedCharacter;
                var mount = ch != null ? ch.GetComponent<WeaponMount>() : null;
                var ep = FindFirstObjectByType<EventPlayer>();
                if (mount != null && ep != null)
                {
                    if (solver != null && solver.IsAiming) solver.Disarm();
                    ep.DebugSnapTo(snap, mount.Dims, mount.CharVoxelSize, pickedAim, feetY);
                }
                pdr?.SetCustomBeam("pick_stand", pickedStand.Value, pickedStand.Value + Vector3.up * 1.8f,
                    0.06f, new Color(0.2f, 1f, 0.35f, 0.9f));
                Debug.Log($"[DebugHUD] 🎯 STAND picked @ {pickedStand.Value} ({hit.volumeId ?? "terrain"} mat {hit.material})");
            }
            else if (pickArmed == PickSlot.Aim)
            {
                pickedAim = hit.point;
                pdr?.SetCustomBeam("pick_aim", hit.point + Vector3.left * 0.25f, hit.point + Vector3.right * 0.25f,
                    0.05f, new Color(1f, 0.55f, 0.1f, 0.95f));
                pdr?.SetCustomBeam("pick_aim2", hit.point + Vector3.down * 0.25f, hit.point + Vector3.up * 0.25f,
                    0.05f, new Color(1f, 0.55f, 0.1f, 0.95f));
                if (solver != null && solver.IsAiming) solver.Arm(hit.point);   // re-aim live
                Debug.Log($"[DebugHUD] 🎯 AIM picked @ {hit.point} ({hit.volumeId ?? "terrain"} mat {hit.material})");
            }
            else if (pickArmed == PickSlot.Blast)
            {
                pickedBlast = hit.point;
                blastNormal = hit.normal;
                blastVolume = hit.volumeId;
                var red = new Color(1f, 0.25f, 0.15f, 0.95f);
                pdr?.SetCustomBeam("pick_blast", hit.point + Vector3.left * 0.3f, hit.point + Vector3.right * 0.3f, 0.06f, red);
                pdr?.SetCustomBeam("pick_blast2", hit.point + Vector3.down * 0.3f, hit.point + Vector3.up * 0.3f, 0.06f, red);
                pdr?.SetCustomBeam("pick_blast3", hit.point + Vector3.forward * 0.3f, hit.point + Vector3.back * 0.3f, 0.06f, red);
                Debug.Log($"[DebugHUD] 💥 BLAST picked @ {hit.point} ({hit.volumeId ?? "terrain"} mat {hit.material})");
            }
            else if (pickArmed == PickSlot.Building)
            {
                // Building-center mode: the blast origin snaps to the volume's
                // geometric center — visualize an inside-out explosion.
                blastVolume = hit.volumeId;
                blastAtVolumeCenter = false;
                blastNormal = Vector3.up;
                pickedBlast = hit.point;
                var dmg = VoxelDamageSystem.GetOrCreate();
                if (!string.IsNullOrEmpty(blastVolume) && dmg.TryGetVolumeCenter(blastVolume, out Vector3 vc))
                {
                    pickedBlast = vc;
                    blastAtVolumeCenter = true;
                }
                var orange = new Color(1f, 0.55f, 0.1f, 0.95f);
                Vector3 bp = pickedBlast.Value;
                pdr?.SetCustomBeam("pick_blast", bp + Vector3.left * 0.3f, bp + Vector3.right * 0.3f, 0.06f, orange);
                pdr?.SetCustomBeam("pick_blast2", bp + Vector3.down * 0.3f, bp + Vector3.up * 0.3f, 0.06f, orange);
                pdr?.SetCustomBeam("pick_blast3", bp + Vector3.forward * 0.3f, bp + Vector3.back * 0.3f, 0.06f, orange);
                Debug.Log($"[DebugHUD] 🏢 BLDG picked {(blastAtVolumeCenter ? $"center @ {bp}" : $"surface @ {bp} — no volume")} ({blastVolume ?? "terrain"})");
            }
            else if (pickArmed == PickSlot.Inspect)
            {
                InspectVoxel(hit);
            }
            pickArmed = PickSlot.None;
        }

        private int inspectCount;

        /// <summary>
        /// Voxel inspector — full diagnostic dump for the picked cell: material/raw value, volume placement, damage
        /// delta vs the pristine array (the cell + its 6 neighbours), the shader's smooth-normal inputs (flags
        /// degenerate neighbourhoods) and a CPU replica of its sun + shadow-ray lighting. Marks the cell with a
        /// yellow cube, shows a short HUD summary, logs the report and appends it to Logs/voxel_inspect.log
        /// (survives editor restarts; Editor.log is too large to search).
        /// </summary>
        private void InspectVoxel(VoxelRayHit hit)
        {
            var pdr = PathDebugRenderer.Instance;
            var dmg = VoxelDamageSystem.GetOrCreate();
            int mat = hit.material;
            string matName = StAssetReader.GetMaterialName(mat);
            string vol = hit.volumeId ?? "terrain";
            var sb = new System.Text.StringBuilder(1024);
            sb.AppendLine($"🔍 VOXEL INSPECT #{++inspectCount}  t={Time.realtimeSinceStartup:F1}s frame={Time.frameCount}");
            sb.AppendLine($"  cell     : [{vol}] {hit.cell}  mat {mat} '{matName}'  raw 0x{hit.voxel:X}  flags 0x{hit.voxel & ~0x1FFu:X}");
            var cam = Camera.main;
            sb.AppendLine($"  hit      : world {hit.point:F2}  face n{hit.normal:F2}  dist {hit.distance:F2}m"
                + (cam != null ? $"  | camera {cam.transform.position:F1} fwd {cam.transform.forward:F2}" : ""));

            uint[] vox = null; int w = 0, h = 0, d = 0; Vector3 corner = default; float vs = 0.05f, yaw = 0f;
            bool isVol = !string.IsNullOrEmpty(hit.volumeId) && World != null
                && World.TryGetVolume(hit.volumeId, out vox, out w, out h, out d, out corner, out vs, out yaw);
            if (!isVol)
            {
                vs = World != null ? (World.VoxelGridToWorld(hit.cell + Vector3Int.right) - World.VoxelGridToWorld(hit.cell)).x : 0.05f;
                yaw = 0f;
            }

            int MatAt(Vector3Int cc)
            {
                if (isVol)
                    return cc.x >= 0 && cc.y >= 0 && cc.z >= 0 && cc.x < w && cc.y < h && cc.z < d
                        ? (int)(vox[cc.x + cc.y * w + cc.z * w * h] & 0x1FFu) : -1;
                return World != null ? World.GetVoxelAtGrid(cc) : -1;
            }
            bool damaged = isVol && dmg.TryGetOriginalVoxel(hit.volumeId, 0, out _);
            int OrigMat(Vector3Int cc)
            {
                int cur = MatAt(cc);
                if (!damaged || cur < 0) return cur;
                return dmg.TryGetOriginalVoxel(hit.volumeId, cc.x + cc.y * w + cc.z * w * h, out uint o) ? (int)(o & 0x1FFu) : cur;
            }

            if (isVol)
            {
                var ext = new Vector3(w, h, d) * vs;
                var loc = new Vector3(hit.cell.x + 0.5f, hit.cell.y + 0.5f, hit.cell.z + 0.5f) * vs;
                sb.AppendLine($"  volume   : {w}×{h}×{d} cells  vs {vs}  yaw {yaw}  corner {corner:F2}  extent {ext:F2}m  cell-center (local) {loc:F2}m");
            }
            else sb.AppendLine($"  terrain  : grid cell {hit.cell}  vs {vs:F3}  voxel byte {MatAt(hit.cell)}");

            int cellOrig = OrigMat(hit.cell);
            sb.AppendLine("  damage   : " + (!isVol ? "terrain (no damage tracking)"
                : !damaged ? "volume pristine (never damaged)"
                : $"volume DAMAGED · this cell original {cellOrig} '{StAssetReader.GetMaterialName(cellOrig)}' {(cellOrig == mat ? "(unchanged)" : "→ CHANGED")}"));

            string[] axes = { "-x", "+x", "-y", "+y", "-z", "+z" };
            var dl = new Vector3Int[] { new(-1, 0, 0), new(1, 0, 0), new(0, -1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1) };
            var occ = new int[6];
            var nb = new System.Text.StringBuilder();
            int exposed = 0, changedNb = 0;
            for (int i = 0; i < 6; i++)
            {
                var nc = hit.cell + dl[i];
                int m = MatAt(nc), o = OrigMat(nc);
                occ[i] = m > 0 ? 1 : 0;
                if (m == 0) exposed++;
                string nm = m < 0 ? "oob" : m == 0 ? "air" : $"{m} {StAssetReader.GetMaterialName(m)}";
                if (m >= 0 && o != m) { nm += $" [was {o} {StAssetReader.GetMaterialName(o)} - REMOVED/CHANGED]"; changedNb++; }
                nb.Append($"{axes[i]}:{nm}  ");
            }
            sb.AppendLine($"  neighbors: {nb}({exposed}/6 exposed, {changedNb} changed by damage)");

            // Replica of the shader's shading inputs: hybrid normal = face normal (+ 30% SmoothNormal on side faces),
            // sun term = half-Lambert², shadow = voxel march toward the light (first N steps skipped, soft edge).
            float yr = -yaw * Mathf.Deg2Rad, ycos = Mathf.Cos(yr), ysin = Mathf.Sin(yr);
            Vector3 ToLocal(Vector3 v) => new Vector3(v.x * ycos + v.z * ysin, v.y, -v.x * ysin + v.z * ycos);
            Vector3 ToWorld(Vector3 v) => new Vector3(v.x * ycos - v.z * ysin, v.y, v.x * ysin + v.z * ycos);
            Vector3 nL = ToLocal(hit.normal);
            bool haveN = nL.sqrMagnitude > 0.5f;
            Vector3 g = new Vector3(-(occ[1] - occ[0]), -(occ[3] - occ[2]), -(occ[5] - occ[4]));
            bool sideFace = Mathf.Abs(nL.y) <= 0.5f, degenerate = g.sqrMagnitude < 1e-4f;
            Vector3 blended = haveN && sideFace && !degenerate ? (nL * 0.7f + g.normalized * 0.3f).normalized : nL;
            sb.AppendLine($"  shading  : face n(local) {nL:F2} → {(!haveN ? "unknown (ray started inside solid)" : sideFace ? "side face: smooth-normal blend applies" : "top/bottom face: raw normal, no blend")}");
            sb.AppendLine($"             neighbour gradient sum {g:F0} → {(degenerate ? "DEGENERATE (opposite neighbours cancel: 1-voxel strand/sheet)" + (sideFace ? " — blend skipped, flat face normal used (before the 2026-10-12 shader fix: normalize(0)=NaN → near-black face)" : " — not used for this face") : "ok")}  blended n {blended:F2}");

            string sunLine = "n/a (no lighting info)", sunShort = "n/a";
            Vector3 L = chunkManager != null ? chunkManager.GetLightDirection() : Vector3.zero;
            if (haveN && L.sqrMagnitude > 0.5f)
            {
                Vector3 sl = ToLocal(L);
                float ndl = Vector3.Dot(blended, sl), sunTerm = Mathf.Pow(ndl * 0.5f + 0.5f, 2f);
                int skip = chunkManager.GetShadowSkipSteps(), maxSteps = chunkManager.GetShadowMaxSteps();
                float nn = chunkManager.GetShadowNormalNudge(), ln = chunkManager.GetShadowLightNudge();
                string shadow;
                if (chunkManager.GetShadowEnabled() == 0) shadow = "shadows disabled";
                else
                {
                    float l1 = Mathf.Abs(sl.x) + Mathf.Abs(sl.y) + Mathf.Abs(sl.z);
                    float skipM = skip / l1 * vs, maxM = maxSteps / l1 * vs;
                    Vector3 org = hit.point + ToWorld(blended) * (vs * nn) + L * (vs * ln);
                    shadow = World != null && World.Raycast(org + L * skipM, L, Mathf.Max(0.01f, maxM - skipM), out var sh)
                        ? $"BLOCKED by [{sh.volumeId ?? "terrain"}] cell {sh.cell} mat {sh.material} '{StAssetReader.GetMaterialName(sh.material)}' ~{skipM + sh.distance:F2}m along the ray"
                        : "CLEAR";
                }
                bool blockedS = shadow.StartsWith("BLOCKED");
                sunShort = blockedS ? "SHADOWED" : ndl <= 0f ? "faces away" : "lit";
                sunLine = $"to-light dir {L:F2}  N·L {ndl:F2}  half-Lambert² sun term {sunTerm:F2}\n"
                    + $"             shadow ray (≈ skip {skip} / max {maxSteps} steps, nudges n×{nn} L×{ln} voxels): {shadow}\n"
                    + $"             verdict: {(blockedS ? "SHADOWED — sun term removed, ambient only" : ndl <= 0f ? "faces away from the sun" : "sun-lit")}";
            }
            sb.AppendLine("  sun      : " + sunLine);

            Vector3 markerPos; Quaternion markerRot;
            if (isVol)
            {
                Vector3 half = new Vector3(w, h, d) * (vs * 0.5f);
                Vector3 lc = new Vector3(hit.cell.x + 0.5f, hit.cell.y + 0.5f, hit.cell.z + 0.5f) * vs - half;
                float rad = yaw * Mathf.Deg2Rad, rc = Mathf.Cos(rad), rs = Mathf.Sin(rad);
                markerPos = corner + half + new Vector3(lc.x * rc - lc.z * rs, lc.y, lc.x * rs + lc.z * rc);
                markerRot = Quaternion.Euler(0f, yaw, 0f);
            }
            else { markerPos = World != null ? World.VoxelGridToWorld(hit.cell) : hit.point; markerRot = Quaternion.identity; }
            pdr?.SetCustomCube("inspect_cell", markerPos, markerRot, vs * 1.08f, new Color(1f, 0.9f, 0.1f, 0.95f));

            var resCls = VoxelDamageMath.ClassOf((uint)mat);
            int resProf = VoxelDamageMath.ResProfileOf(vol);
            inspectHud = $"🔍 {hit.cell} · {mat} {matName} · {vol}\n   {exposed}/6 exposed · res {VoxelDamageMath.MaterialResistance((uint)mat, resProf):F2} ({VoxelDamageMath.ResClassNames[(int)resCls]} · {VoxelDamageMath.ResProfileNames[resProf]}) · normal {(degenerate && sideFace ? "degenerate→flat" : "ok")} · sun {sunShort}{(damaged && changedNb > 0 ? $" · {changedNb} nb changed" : "")}";
            string report = sb.ToString().TrimEnd();
            Debug.Log($"[DebugHUD] {report}");
            try
            {
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Logs"));
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "voxel_inspect.log"), $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  {report}\n\n");
            }
            catch (System.Exception e) { Debug.LogWarning($"[DebugHUD] voxel_inspect.log write failed: {e.Message}"); }
        }

        /// <summary>Arm/cancel a voxel pick — the click is consumed by CityMap3D's pick mode.</summary>
        private void ArmPick(PickSlot slot)
        {
            pickArmed = pickArmed == slot ? PickSlot.None : slot;
            cityMap.voxelPickArmed = pickArmed != PickSlot.None;
        }

        /// <summary>Open the HUD on the Combat tab for the given range block.</summary>
        public void OpenCombatTab(string blockId)
        {
            rangeBlockId = blockId;
            activeTab = Tab.Combat;
            visible = true;
            minimized = false;
        }

        /// <summary>Voxel-inspect readout — shared by Combat and Demolitions pick rows.</summary>
        void DrawInspectHud()
        {
            if (inspectHud == null) return;
            GUILayout.Label(inspectHud, labelStyle);
            if (GUILayout.Button("clear inspect", GUILayout.Height(18)))
            {
                inspectHud = null;
                PathDebugRenderer.Instance?.ClearCustomCube("inspect_cell");
            }
        }

        /// <summary>Stand/aim/blast pick status + clear — shared; picks persist across tabs.</summary>
        void DrawPickStatus()
        {
            if (!pickedStand.HasValue && !pickedAim.HasValue && !pickedBlast.HasValue) return;
            GUILayout.Label($"stand: {(pickedStand.HasValue ? pickedStand.Value.ToString("F1") : "—")}   aim: {(pickedAim.HasValue ? pickedAim.Value.ToString("F1") : "—")}   blast: {(pickedBlast.HasValue ? pickedBlast.Value.ToString("F1") : "—")}", labelStyle);
            if (GUILayout.Button("clear picks", GUILayout.Height(18)))
            {
                pickedStand = pickedAim = pickedBlast = null; blastVolume = null; blastAtVolumeCenter = false;
                var pdr = PathDebugRenderer.Instance;
                foreach (var n in new[] { "pick_stand", "pick_aim", "pick_aim2", "pick_blast", "pick_blast2", "pick_blast3", "blast_rx", "blast_ry", "blast_rz" })
                    pdr?.ClearCustomBeam(n);
            }
        }

        void DrawCombatTab()
        {
            var sim = gameUI != null ? gameUI.Sim : null;
            string simState = sim != null ? sim.State.ToString() : "no sim";
            bool dwelling = sim != null && sim.State == SimState.Dwelling;

            GUILayout.Label($"<b>COMBAT</b>   sim: {simState}", boldStyle);

            // SFX preview — above the dwelling gate so it works in planning phase.
            // Plays at the AudioListener, so distance can't mask a pipeline problem.
            var dmgSys = VoxelDamageSystem.GetOrCreate();

            // --- Free-fire picks (combat-facing; blast picks live on Demolitions) ---
            GUILayout.Space(4);
            GUILayout.Label("<b>Free-fire &amp; picks</b>", labelStyle);
            GUILayout.BeginHorizontal();
            bool standArmed = pickArmed == PickSlot.Stand;
            if (GUILayout.Button(standArmed ? "STAND: click map…" : "PICK STAND",
                    standArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Stand);
            bool aimArmed = pickArmed == PickSlot.Aim;
            if (GUILayout.Button(aimArmed ? "AIM: click map…" : "PICK AIM",
                    aimArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Aim);
            bool inspArmed = pickArmed == PickSlot.Inspect;
            if (GUILayout.Button(inspArmed ? "VOXEL: click map…" : "PICK VOXEL",
                    inspArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Inspect);
            GUILayout.EndHorizontal();
            DrawInspectHud();
            DrawPickStatus();

            // --- Target source: a range block's lane points, or free-fire picks ---
            bool freeFire = string.IsNullOrEmpty(rangeBlockId);
            bool hasPos, hasTgt;
            Vector3 posWorld = Vector3.zero, tgtWorld = Vector3.zero;

            if (freeFire)
            {
                // Vinny stands wherever PICK STAND dropped him; PICK AIM sets the target.
                hasPos = true;
                hasTgt = pickedAim.HasValue;
                if (hasTgt) tgtWorld = pickedAim.Value;
                GUILayout.Space(4);
                GUILayout.Label(hasTgt
                    ? $"free-fire target: <b>{tgtWorld.ToString("F1")}</b>"
                    : "PICK AIM a voxel to arm the fire controls.", labelStyle);
            }
            else
            {
            GUILayout.Label($"Block: <b>{rangeBlockId}</b>", labelStyle);
            var pointNames = cityMap.GetBuildingPointNames(rangeBlockId);
            GUILayout.Label($"Points: {(pointNames.Count > 0 ? string.Join(", ", pointNames) : "none")}", labelStyle);

            // --- Lane + aim-point selection ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("lane:", labelStyle, GUILayout.Width(38));
            for (int i = 1; i <= 3; i++)
            {
                if (GUILayout.Toggle(RangeLane == i, $" {i}", RangeLane == i ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                {
                    if (RangeLane != i)
                    {
                        RangeLane = i;
                        MoveCharacterToLane(rangeBlockId, i);
                    }
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("aim:", labelStyle, GUILayout.Width(38));
            if (GUILayout.Toggle(!RangeAimMold, " lane target", !RangeAimMold ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                RangeAimMold = false;
            bool moldAvailable = pointNames.Contains("glass_target");
            GUI.enabled = moldAvailable;
            if (GUILayout.Toggle(RangeAimMold, " glass mold", RangeAimMold ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
            {
                RangeAimMold = true;
                RangeLane = 3;   // the mold lives on lane 3
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!moldAvailable && RangeAimMold) RangeAimMold = false;

            string posName = $"firing_position_{RangeLane}";
            string tgtName = RangeAimMold ? "glass_target" : $"firing_target_{RangeLane}";
            // fall back to the pre-multilane names for older range assets
            hasPos = cityMap.TryGetBuildingPointLocal(rangeBlockId, posName, out var posLocal)
                  || cityMap.TryGetBuildingPointLocal(rangeBlockId, "firing_position", out posLocal);
            hasTgt = cityMap.TryGetBuildingPointLocal(rangeBlockId, tgtName, out var tgtLocal)
                  || cityMap.TryGetBuildingPointLocal(rangeBlockId, "firing_target", out tgtLocal);
            posWorld = cityMap.MapRoot.position + posLocal;
            tgtWorld = cityMap.MapRoot.position + tgtLocal;
            GUILayout.Label($"{posName}: {(hasPos ? "OK" : "<color=#ff6666>MISSING</color>")}   {tgtName}: {(hasTgt ? "OK" : "<color=#ff6666>MISSING</color>")}", labelStyle);

            if (pickedAim.HasValue) { tgtWorld = pickedAim.Value; hasTgt = true; }
            }

            GUILayout.Space(6);

            // --- Character / weapon status ---
            var ch = cityMap.SpawnedCharacter;
            if (ch == null)
            {
                GUILayout.Label("<color=#ff6666>No spawned character.</color>", labelStyle);
                return;
            }
            var mount = ch.GetComponent<WeaponMount>();
            if (solver == null || solver.gameObject != ch.gameObject)
            {
                solver = ch.GetComponent<ArmAimSolver>();
                if (solver == null) solver = ch.gameObject.AddComponent<ArmAimSolver>();
            }

            // Position check — character transform is the volume corner; the
            // firing position target is where the XZ center should sit.
            if (!freeFire && mount != null && hasPos)
            {
                var d = mount.Dims; float vs = mount.CharVoxelSize;
                Vector3 chCenter = ch.transform.position + new Vector3(d.x * vs * 0.5f, 0f, d.z * vs * 0.5f);
                float offM = Vector3.Distance(new Vector3(chCenter.x, 0, chCenter.z), new Vector3(posWorld.x, 0, posWorld.z));
                GUILayout.Label($"In position: {(offM < 0.35f ? "<color=#66ff66>YES</color>" : $"<color=#ffaa44>off {offM:F2}m</color>")}", labelStyle);
            }
            else if (freeFire && pickedStand.HasValue)
                GUILayout.Label($"stand: <b>{pickedStand.Value.ToString("F1")}</b> — wherever PICK STAND left him", labelStyle);
            GUILayout.Label($"Weapon: {(mount != null && mount.IsEquipped ? mount.ItemFileName : "<color=#ff6666>none</color>")}", labelStyle);
            if (mount != null && mount.IsEquipped)
            {
                GUILayout.Label($"  bore axis: {(mount.BoreAxisLocal != Vector3.right ? "measured" : "+X fallback")}  hand gid {mount.HandGid}", labelStyle);
                mount.showMuzzleRay = GUILayout.Toggle(mount.showMuzzleRay, " muzzle ray (red bore axis)", toggleStyle);
                solver.showSightBeam = GUILayout.Toggle(solver.showSightBeam, " sight line (cyan, while aiming)", toggleStyle);
            }

            GUILayout.Space(6);

            // --- Combat mode: autonomous cover-seek (COMBAT_COVER_STATE.md) ---
            // Threat = the PICK AIM point — simulates a shooter standing there.
            var cc = ch.GetComponent<CombatController>();
            if (cc == null) cc = ch.gameObject.AddComponent<CombatController>();
            GUILayout.Label("<b>Combat mode (autonomous)</b>", labelStyle);
            if (cc.State == CombatController.CombatState.Idle)
            {
                bool hasThreat = pickedAim.HasValue;
                GUI.enabled = hasThreat;
                if (GUILayout.Button(hasThreat ? "ENTER COMBAT — seek cover vs PICK AIM"
                                             : "ENTER COMBAT (PICK AIM a threat first)",
                        tabStyle, GUILayout.Height(24)))
                    cc.EnterCombat(pickedAim.Value);
                GUI.enabled = true;
            }
            else
            {
                GUILayout.Label($"state: <b>{cc.State}</b>   cover: <b>{cc.CoverObstacleId ?? "—"}</b>", labelStyle);
                if (!string.IsNullOrEmpty(cc.LastResult))
                    GUILayout.Label($"  {cc.LastResult}", labelStyle);
                if (cc.lastStats.tested > 0)
                {
                    var st = cc.lastStats;
                    var reasons = string.Join("  ", System.Linq.Enumerable.Select(
                        st.reasons, kv => $"{kv.Key}×{kv.Value}"));
                    GUILayout.Label($"  scan: {st.obstacles} obstacles → {st.tested} pts, {st.valid} valid" +
                        (st.reasons.Count > 0 ? $"  ({reasons})" : ""), labelStyle);
                }
                if (GUILayout.Button("EXIT COMBAT", tabStyle, GUILayout.Height(22))) cc.ExitCombat();
            }
            cc.moveSpeed = HSlider("move speed", cc.moveSpeed, 0.5f, 5f);
            GUILayout.Label("cover detection tuning lives on the COVER tab", labelStyle);

            GUILayout.Space(6);

            // --- IK Aim toggle ---
            bool aiming = solver.IsAiming;
            var btnStyle = aiming ? tabActiveStyle : tabStyle;
            GUI.enabled = hasTgt && mount != null && mount.IsEquipped && (freeFire || (dwelling && hasPos));
            string btnLabel = aiming ? "IK AIM — ENGAGED (click to release)" : "IK AIM";
            if (GUILayout.Button(btnLabel, btnStyle, GUILayout.Height(26)))
            {
                if (aiming) solver.Disarm();
                else solver.Arm(tgtWorld);
            }
            GUI.enabled = true;
            if (!freeFire && !dwelling)
                GUILayout.Label("(range mode available while sim is Dwelling)", labelStyle);
            if (!string.IsNullOrEmpty(solver.LastError))
                GUILayout.Label($"<color=#ff6666>solver: {solver.LastError}</color>", labelStyle);

            DrawFireControls(dwelling || freeFire, mount, tgtWorld, hasTgt);

            // --- Telemetry ---
            if (solver.Blend > 0f || aiming)
            {
                GUILayout.Space(4);
                GUILayout.Label($"blend {solver.Blend:F2}   arm {solver.ArmGid} → fore {solver.ForearmGid}", labelStyle);
                GUILayout.Label($"target dist: {solver.LastTargetDistanceM:F2}m   aim err: {solver.LastAngularErrorDeg:F1}°", labelStyle);
                if (mount != null && mount.IsEquipped)
                {
                    GUILayout.Label($"muzzle: {mount.MuzzleWorld}", labelStyle);
                    GUILayout.Label($"aim dir: {mount.AimDirection}", labelStyle);
                }
            }

            GUILayout.Space(6);

            // --- Tuning sliders (live) ---
            GUILayout.Label("<b>Tuning</b>", labelStyle);
            solver.torsoAssist = GUILayout.Toggle(solver.torsoAssist, " torso assist (turret yaw)", toggleStyle);
            if (solver.torsoAssist)
            {
                solver.torsoShare = HSlider("torso share", solver.torsoShare, 0f, 1f);
                solver.maxTorsoYawDeg = HSlider("max torso °", solver.maxTorsoYawDeg, 0f, 120f);
            }
            solver.reachFraction = HSlider("reach fraction", solver.reachFraction, 0.5f, 1f);
            solver.blendInSpeed = HSlider("blend in", solver.blendInSpeed, 0.5f, 8f);
            solver.blendOutSpeed = HSlider("blend out", solver.blendOutSpeed, 0.5f, 8f);
            solver.azimuthOffsetDeg = HSlider("azimuth °", solver.azimuthOffsetDeg, -45f, 45f);
            solver.elevationOffsetDeg = HSlider("elevation °", solver.elevationOffsetDeg, -45f, 45f);
            if (GUILayout.Button("Reset tuning", GUILayout.Height(20)))
            {
                solver.reachFraction = 0.97f;
                solver.blendInSpeed = 2.5f;
                solver.blendOutSpeed = 3.5f;
                solver.azimuthOffsetDeg = 0f;
                solver.elevationOffsetDeg = 0f;
                solver.torsoShare = 0.6f;
                solver.maxTorsoYawDeg = 70f;
            }
        }

        /// <summary>
        /// COVER tab — Vinny's always-on cover-awareness sense, inspected live.
        /// Passive layer: walls (low/crouch/full) inside the scan radius, volume
        /// footprints, the radius ring itself — no threat needed. Set a threat
        /// (PICK THREAT, shared with Combat's PICK AIM) and the active layer
        /// lights up: threat-facing walls, validated stand-spot candidates with
        /// reject reasons, and optionally the threat→eye LOS rays.
        /// </summary>
        void DrawCoverTab()
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            if (ch == null)
            {
                GUILayout.Label("spawn the character first (sim running)", labelStyle);
                return;
            }
            var sensor = ch.GetComponent<CoverSensor>();
            if (sensor == null) sensor = ch.gameObject.AddComponent<CoverSensor>();
            coverSensor = sensor;

            // --- Threat context ---
            GUILayout.Label("<b>Threat</b>", labelStyle);
            GUILayout.BeginHorizontal();
            bool thrArmed = pickArmed == PickSlot.Aim;
            if (GUILayout.Button(thrArmed ? "THREAT: click map…" : "PICK THREAT",
                    thrArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22)))
                ArmPick(PickSlot.Aim);
            GUI.enabled = pickedAim.HasValue;
            if (GUILayout.Button("CLEAR", tabStyle, GUILayout.Width(60), GUILayout.Height(22)))
            {
                pickedAim = null;
                sensor.Threat = null;
                var pdr0 = PathDebugRenderer.Instance;
                pdr0?.ClearCustomBeam("pick_aim");
                pdr0?.ClearCustomBeam("pick_aim2");
                sensor.Refresh(true);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(pickedAim.HasValue
                ? $"threat: <b>{pickedAim.Value.ToString("F1")}</b>"
                : "no threat — passive wall awareness only", labelStyle);

            // --- Detection tuning ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Detection</b>", labelStyle);
            bool dirty = false;
            float v;
            v = HSlider("cover radius", sensor.radius, 4f, 40f);
            if (v != sensor.radius) { sensor.radius = v; dirty = true; }
            v = HSlider("standoff", sensor.standoff, 0.15f, 1.5f);
            if (v != sensor.standoff) { sensor.standoff = v; dirty = true; }
            v = HSlider("crouch eye (fallback)", sensor.crouchEye, 0.3f, 1.4f);
            if (v != sensor.crouchEye) { sensor.crouchEye = v; dirty = true; }
            GUILayout.Label(sensor.EyePosed
                ? $"  eye band: <b>{sensor.EyeOffset:F2}m</b> — read from posed crouch head"
                : $"  eye band: {sensor.EyeOffset:F2}m — slider fallback (no anim data)", labelStyle);
            v = HSlider("full cover", sensor.standEye, 1.0f, 1.8f);
            if (v != sensor.standEye) { sensor.standEye = v; dirty = true; }
            v = HSlider("spot spacing", sensor.spotSpacing, 0.15f, 1.0f);
            if (v != sensor.spotSpacing) { sensor.spotSpacing = v; dirty = true; }
            v = HSlider("facing dot", sensor.facingDot, -0.6f, 0.4f);
            if (v != sensor.facingDot) { sensor.facingDot = v; dirty = true; }
            if (dirty) sensor.Refresh(true);

            // --- Visualization toggles ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Visualize</b>", labelStyle);
            dirty = false;
            void Tog(ref bool f, string label)
            {
                bool nv = GUILayout.Toggle(f, label, toggleStyle);
                if (nv != f) { f = nv; dirty = true; }
            }
            Tog(ref sensor.showRing, " scan radius ring");
            Tog(ref sensor.showFootprints, " obstacle footprints (local frame)");
            Tog(ref sensor.showWalls, " cover walls  (green full / yellow crouch / red low)");
            Tog(ref sensor.tintWallFacing, "   dim walls not facing threat");
            Tog(ref sensor.showCandidates, " candidates  (green ok / red rejected / cyan pick)");
            Tog(ref sensor.showLos, " threat→eye LOS rays");
            if (dirty) sensor.Refresh(true);
            if (GUILayout.Button("RESCAN NOW", tabStyle, GUILayout.Height(20)))
                sensor.Refresh(true);

            // --- Live stats ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Scan</b>", labelStyle);
            var st = sensor.stats;
            GUILayout.Label($"  {st.obstacles} volumes · {st.wallCols} wall cols" +
                $"  (full {st.wallsFull} / crouch {st.wallsCrouch} / low {st.wallsLo})", labelStyle);
            if (sensor.HasThreat)
            {
                string reasons = st.reasons != null && st.reasons.Count > 0
                    ? "   (" + string.Join("  ", System.Linq.Enumerable.Select(
                        st.reasons, kv => $"{kv.Key}×{kv.Value}")) + ")"
                    : "";
                GUILayout.Label($"  {st.facing} threat-facing → {st.tested} candidates, " +
                    $"<b>{st.valid} valid</b>{reasons}", labelStyle);
            }
        }

        /// <summary>
        /// VISION tab — Vinny's vision sense, inspected live. The cone rides the
        /// POSED head joint, so the Looking state's authored head-yaw sweep is the
        /// scan itself. Detection builds while a target sits inside an unblocked
        /// cone and decays outside it — the sweep produces intermittent exposure.
        /// Testbed target = the shared PICK AIM point ("the thing he's looking at").
        /// </summary>
        void DrawVisionTab()
        {
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            if (ch == null)
            {
                GUILayout.Label("spawn the character first (sim running)", labelStyle);
                return;
            }
            var sensor = ch.GetComponent<VisionSensor>();
            if (sensor == null) sensor = ch.gameObject.AddComponent<VisionSensor>();
            visionSensor = sensor;

            // --- Target context ---
            GUILayout.Label("<b>Target</b>", labelStyle);
            GUILayout.BeginHorizontal();
            bool aimArmed = pickArmed == PickSlot.Aim;
            if (GUILayout.Button(aimArmed ? "TARGET: click map…" : "PICK TARGET",
                    aimArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22)))
                ArmPick(PickSlot.Aim);
            GUILayout.EndHorizontal();
            GUILayout.Label(pickedAim.HasValue
                ? $"observing: <b>{pickedAim.Value.ToString("F1")}</b>"
                : "no target — pick a point for Vinny to watch", labelStyle);

            // --- Perception tuning ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Perception</b>", labelStyle);
            sensor.range = HSlider("range", sensor.range, 5f, 60f);
            sensor.halfAngleDeg = HSlider("half angle °", sensor.halfAngleDeg, 15f, 90f);
            sensor.riseTime = HSlider("detect rise (s)", sensor.riseTime, 0.1f, 3f);
            sensor.decayTime = HSlider("forget decay (s)", sensor.decayTime, 0.5f, 5f);
            GUILayout.Label($"  eye: <b>{sensor.EyePos.y:F2}m</b> — {sensor.EyeAnchorDesc}", labelStyle);

            // --- Visualization ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Visualize</b>", labelStyle);
            sensor.showCone = GUILayout.Toggle(sensor.showCone, " FOV cone (sweeps with head)", toggleStyle);
            sensor.showTargetRay = GUILayout.Toggle(sensor.showTargetRay, " eye→target rays", toggleStyle);

            // --- Live status ---
            GUILayout.Space(6);
            GUILayout.Label("<b>Status</b>", labelStyle);
            GUILayout.Label($"  detection: <b>{sensor.MaxDetection * 100f:F0}%</b>" +
                (sensor.AnyDetected ? "  <color=#ff5544>DETECTED</color>"
                 : sensor.AnyVisible ? "  <color=#ffbb44>in cone…</color>" : ""), labelStyle);
            GUILayout.Label("  cone: pale = watching · amber = detecting · red = detected", labelStyle);
        }

        /// <summary>Demolitions: blast/inspect picks, the global voxel-damage &amp; debris
        /// system (char tint sliders, debris physics, rubble, reset — shot debris rides
        /// these too) and the blast bench. Shot-facing nibble toggles (glass glint /
        /// bullet decals) live on Combat's Firearm panel; the master gate is here.</summary>
        void DrawDemolitionsTab()
        {
            var sim = gameUI != null ? gameUI.Sim : null;
            string simState = sim != null ? sim.State.ToString() : "no sim";
            var dmgSys = VoxelDamageSystem.GetOrCreate();

            GUILayout.Label($"<b>DEMOLITIONS</b>   sim: {simState}", boldStyle);

            GUILayout.Space(4);
            GUILayout.Label("<b>Blast &amp; inspect picks</b>", labelStyle);
            GUILayout.BeginHorizontal();
            bool blastArmed = pickArmed == PickSlot.Blast;
            if (GUILayout.Button(blastArmed ? "BLAST: click map…" : "PICK BLAST",
                    blastArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Blast);
            bool bldgArmed = pickArmed == PickSlot.Building;
            if (GUILayout.Button(bldgArmed ? "BLDG: click map…" : "PICK BLDG",
                    bldgArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Building);
            bool inspArmed = pickArmed == PickSlot.Inspect;
            if (GUILayout.Button(inspArmed ? "VOXEL: click map…" : "PICK VOXEL",
                    inspArmed ? tabActiveStyle : tabStyle, GUILayout.Height(22))) ArmPick(PickSlot.Inspect);
            GUILayout.EndHorizontal();
            DrawInspectHud();
            DrawPickStatus();

            // --- Damage & debris — GLOBAL: shots AND blasts share these rules ---
            GUILayout.Space(4);
            GUILayout.Label("<b>Damage &amp; debris</b>", labelStyle);
            dmgSys.damageTint = GUILayout.Toggle(dmgSys.damageTint, " damage tint (char gradient — master)", toggleStyle);
            if (dmgSys.damageTint)
            {
                dmgSys.charReach = Mathf.RoundToInt(HSlider("char reach cells", dmgSys.charReach, 0f, 8f, "F0"));
                dmgSys.charStrength = HSlider("char strength", dmgSys.charStrength, 0f, 1f, "F2");
                dmgSys.charRadial = HSlider("S-curve radial", dmgSys.charRadial, 0f, 1f, "F2");
                dmgSys.charAbsorb = HSlider("char absorb/cell", dmgSys.charAbsorb, 0.2f, 0.95f, "F2");
                dmgSys.debrisChar = HSlider("debris char", dmgSys.debrisChar, 0f, 1f, "F2");
            }
            dmgSys.shardVoxelCollision = GUILayout.Toggle(dmgSys.shardVoxelCollision, " debris collides with voxel world (walls / roofs)", toggleStyle);
            dmgSys.pairCells = GUILayout.Toggle(dmgSys.pairCells, " same-material pairing (brick dominoes)", toggleStyle);
            if (dmgSys.pairCells)
                dmgSys.pairChance = HSlider("pair chance", dmgSys.pairChance, 0f, 1f, "F2");
            dmgSys.rubbleDeposit = GUILayout.Toggle(dmgSys.rubbleDeposit, $" debris settles into volume (rubble · {dmgSys.RubbleCells} cells)", toggleStyle);
            if (dmgSys.DamagedVolumeCount > 0 &&
                GUILayout.Button($"RESET DAMAGE ({dmgSys.DamagedVolumeCount} volume{(dmgSys.DamagedVolumeCount > 1 ? "s" : "")}) — restore voxels", GUILayout.Height(22)))
                dmgSys.ResetAllDamage();

            // --- Blast bench — only with a blast picked ---
            if (!pickedBlast.HasValue)
            {
                GUILayout.Space(4);
                GUILayout.Label("PICK BLAST a building face to arm the bench.", labelStyle);
                return;
            }
            GUILayout.Space(4);
            GUILayout.Label($"<b>Demolition</b>  on {(blastVolume ?? "<color=#ffaa44>terrain — needs a building</color>")}{(blastAtVolumeCenter ? " · <color=#ffaa44>center</color>" : "")}", labelStyle);
            Vector3 blastCenter = blastAtVolumeCenter
                ? pickedBlast.Value
                : pickedBlast.Value - blastNormal.normalized * blastDepth;
            // live extent preview — 3 axis beams spanning the blast radius
            var pdrB = PathDebugRenderer.Instance;
            var ext = new Color(1f, 0.55f, 0.1f, 0.7f);
            pdrB?.SetCustomBeam("blast_rx", blastCenter - Vector3.right * blastRadius, blastCenter + Vector3.right * blastRadius, 0.05f, ext);
            pdrB?.SetCustomBeam("blast_ry", blastCenter - Vector3.up * blastRadius, blastCenter + Vector3.up * blastRadius, 0.05f, ext);
            pdrB?.SetCustomBeam("blast_rz", blastCenter - Vector3.forward * blastRadius, blastCenter + Vector3.forward * blastRadius, 0.05f, ext);
            GUI.enabled = !blastAtVolumeCenter;
            blastDepth = HSlider("depth into wall m", blastDepth, 0f, 4f, "F1");
            GUI.enabled = true;
            blastRadius = HSlider("radius m", blastRadius, 0.3f, 30f, "F1");
            blastPower = HSlider("power", blastPower, 0.1f, 8f, "F2");
            dmgSys.occlusionAware = GUILayout.Toggle(dmgSys.occlusionAware, " occlusion (walls shield)", toggleStyle);
            dmgSys.dropUnsupported = GUILayout.Toggle(dmgSys.dropUnsupported, " drop unsupported (collapse)", toggleStyle);
            if (dmgSys.dropUnsupported)
                dmgSys.cellImpulse = HSlider("cell impulse", dmgSys.cellImpulse, 0f, 20f, "F1");
            dmgSys.shockFront = GUILayout.Toggle(dmgSys.shockFront, " shock front (sim → playback)", toggleStyle);
            if (dmgSys.shockFront)
            {
                dmgSys.frontPasses = Mathf.RoundToInt(HSlider("front passes", dmgSys.frontPasses, 1f, 24f, "F0"));
                dmgSys.frontSec = HSlider("front sec", dmgSys.frontSec, 0.1f, 1.5f, "F2");
            }
            dmgSys.flyerRadialVelocity = GUILayout.Toggle(dmgSys.flyerRadialVelocity, " debris radial launch velocity", toggleStyle);
            bool canDet = !string.IsNullOrEmpty(blastVolume);
            GUI.enabled = canDet;
            if (GUILayout.Button($"💥 DETONATE ({blastRadius:F1}m)", canDet ? tabActiveStyle : tabStyle, GUILayout.Height(26)))
            {
                if (dmgSys.shockFront)
                    dmgSys.DetonateEvent(blastVolume, blastCenter, blastRadius, blastPower, dmgSys.frontPasses, dmgSys.frontSec);
                else
                    dmgSys.Detonate(blastVolume, blastCenter, blastRadius, blastPower);
            }
            GUI.enabled = true;
        }

        /// <summary>Weapons placeholder — live weapon-stat tuning (damage, spread,
        /// muzzle velocity, recoil, ammo) lands here via the live-editor pipeline.</summary>
        void DrawWeaponsTab()
        {
            GUILayout.Label("<b>WEAPONS</b>", boldStyle);
            GUILayout.Space(4);
            GUILayout.Label("Placeholder — live weapon-stat tuning lands here.", labelStyle);
            GUILayout.Label("Planned knobs: damage · spread · muzzle velocity · recoil · ammo —", labelStyle);
            GUILayout.Label("wired through the live-editor pipeline (docs/core/LIVE_EDITOR_PIPELINE.md).", labelStyle);
            GUILayout.Space(4);
            GUILayout.Label("Current combat tuning lives on the Combat tab:", labelStyle);
            GUILayout.Label("skill / cone ° / muzzle speed / spread cone / aim feel.", labelStyle);
        }

        /// <summary>
        /// Firearm panel: shooter skill (1-10, default 10), cone slider, muzzle speed, FIRE and the last-shot
        /// report. FIRE needs the IK aim fully raised (blend ≥ 0.98) so the shot goes where the gun actually
        /// points; the report shows how far the shot line passed from firing_target and the solver's residual
        /// aim error at the trigger pull. Physics + hit resolution live in ProjectileSystem.
        /// </summary>
        private void DrawFireControls(bool dwelling, WeaponMount mount, Vector3 targetWorld, bool hasTarget)
        {
            var proj = ProjectileSystem.GetOrCreate();

            // The cone wireframe follows this mount while the panel is up (stale-stamped, auto-clears).
            proj.aimSource = mount;
            proj.aimTarget = hasTarget ? targetWorld : (Vector3?)null;
            proj.aimSourceStamp = Time.time;

            GUILayout.Space(6);
            GUILayout.Label("<b>Firearm</b>", labelStyle);
            proj.shooterSkill = Mathf.RoundToInt(HSlider("skill (1-10)", proj.shooterSkill, 1f, 10f, "F0"));
            proj.baseConeDeg = HSlider("cone ° @10", proj.baseConeDeg, 0f, 5f);
            proj.muzzleSpeed = HSlider("speed u/s", proj.muzzleSpeed, 5f, 150f, "F0");
            GUILayout.Label($"effective cone ±{proj.EffectiveConeDeg:F2}°   (skill ×{ProjectileSystem.SkillSpreadMultiplier(proj.shooterSkill):F2})", labelStyle);
            proj.showSpreadCone = GUILayout.Toggle(proj.showSpreadCone, " spread cone (wireframe)", toggleStyle);

            bool tr = GUILayout.Toggle(proj.showTracers, " tracers (in-flight streak)", toggleStyle);
            if (tr != proj.showTracers)
            {
                proj.showTracers = tr;
                if (!tr) proj.ClearTracers();
            }
            bool im = GUILayout.Toggle(proj.showImpactMarks, " impact marks (red ticks)", toggleStyle);
            if (im != proj.showImpactMarks)
            {
                proj.showImpactMarks = im;
                if (!im) proj.ClearImpactMarks();
            }
            var dmg = VoxelDamageSystem.GetOrCreate();
            dmg.glassDamageEnabled = GUILayout.Toggle(dmg.glassDamageEnabled, " glass puncture (P0)", toggleStyle);
            if (dmg.glassDamageEnabled)
            {
                dmg.ring1RemoveFraction = HSlider("ring removal", dmg.ring1RemoveFraction, 0f, 1f, "F2");
                GUILayout.Label("  low = small hole + wide crack halo · high = chunk blown out", labelStyle);
                dmg.shardBurstEnabled = GUILayout.Toggle(dmg.shardBurstEnabled, " shard burst (flying cubes)", toggleStyle);
            }

            // Shot-facing damage-nibble features — the master gate (`damage tint`)
            // lives on Demolitions; grayed here while it's off so the dependency shows.
            GUI.enabled = dmg.damageTint;
            dmg.glassGlint = GUILayout.Toggle(dmg.glassGlint, " glass glint (retire cracked material)", toggleStyle);
            if (dmg.glassGlint)
                dmg.glintStrength = HSlider("glint strength", dmg.glintStrength, 0f, 1f, "F2");
            dmg.bulletImpactTint = GUILayout.Toggle(dmg.bulletImpactTint, " bullet impact decals (non-glass)", toggleStyle);
            if (dmg.bulletImpactTint)
                dmg.bulletScorchDepth = Mathf.RoundToInt(HSlider("scorch depth", dmg.bulletScorchDepth, 0f, 4f, "F0"));
            GUI.enabled = true;
            if (!dmg.damageTint)
                GUILayout.Label("<i>  tint inert — master 'damage tint' on Demolitions</i>", labelStyle);

            if (solver != null)
            {
                solver.feelSkill = proj.shooterSkill;   // placeholder for the hood's Firearms stat
                solver.aimFeelEnabled = GUILayout.Toggle(solver.aimFeelEnabled, " aim feel (sway + recoil)", toggleStyle);
                if (solver.aimFeelEnabled)
                {
                    solver.swayDeg = HSlider("sway ° @10", solver.swayDeg, 0f, 3f);
                    solver.kickDeg = HSlider("kick °", solver.kickDeg, 0f, 5f);
                    solver.settleSec = HSlider("settle s @10", solver.settleSec, 0.05f, 0.5f);
                    GUILayout.Label($"aim offset now {solver.AimOffsetDeg:F2}°", labelStyle);
                }
            }

            bool aimed = solver != null && solver.IsAiming && solver.Blend >= 0.98f;
            bool canFire = dwelling && mount != null && mount.IsEquipped && aimed;
            GUI.enabled = canFire;
            if (GUILayout.Button("FIRE", canFire ? tabActiveStyle : tabStyle, GUILayout.Height(30)))
                proj.Fire(mount, solver.LastAngularErrorDeg, hasTarget ? targetWorld : (Vector3?)null);
            GUI.enabled = true;
            if (!canFire)
                GUILayout.Label("(engage IK AIM and wait for blend 1.00)", labelStyle);
            if (proj.InFlight > 0)
                GUILayout.Label($"in flight: {proj.InFlight}", labelStyle);

            if (proj.HasShot)
            {
                var s = proj.LastShot;
                GUILayout.Space(4);
                GUILayout.Label($"<b>SHOT #{s.id}</b>  seed {s.seed:X8}  skill {s.skill}  cone ±{s.coneHalfDeg:F2}° (drew {s.spreadDeg:F2}°)", labelStyle);
                if (s.hit)
                {
                    string glass = ProjectileSystem.IsGlassMaterial(s.material) ? " (glass)" : "";
                    GUILayout.Label($"<color=#66ff66>HIT</color> {s.target}   mat {s.material}{glass}   cell {s.cell.x},{s.cell.y},{s.cell.z}", labelStyle);
                    GUILayout.Label($"dist {s.distance:F2}m   flight {s.flightTime:F2}s", labelStyle);
                    var dmgSys = VoxelDamageSystem.Instance;
                    if (dmgSys != null && ProjectileSystem.IsGlassMaterial(s.material) && dmgSys.LastRemoved + dmgSys.LastCracked > 0)
                        GUILayout.Label($"🔨 puncture: -{dmgSys.LastRemoved} cells, {dmgSys.LastCracked} cracked", labelStyle);
                }
                else
                {
                    GUILayout.Label($"<color=#ffaa44>MISS</color> — expired after {s.distance:F1}m", labelStyle);
                }
                if (s.hasIntended)
                    GUILayout.Label($"lateral miss {s.lateralMiss:F3}m   impact→target {s.impactOffset:F3}m", labelStyle);
                GUILayout.Label($"aim err at trigger {s.aimErrorDeg:F1}°", labelStyle);
            }
        }

        /// <summary>
        /// SFX home — every sound's in-game level is a TRIM on the studio file
        /// (1.00 = exactly what audio_studio authored). A/B calibration buttons
        /// compare the 2D studio reference (at the listener) against the real
        /// 3D in-game path. Tuned trims are serialized — they ARE the shipped mix.
        /// </summary>
        void DrawSfxTab()
        {
            var proj = ProjectileSystem.GetOrCreate();
            var dmg = VoxelDamageSystem.GetOrCreate();
            var ch = cityMap != null ? cityMap.SpawnedCharacter : null;
            var mnt = ch != null ? ch.GetComponent<WeaponMount>() : null;
            var listener = FindFirstObjectByType<AudioListener>();

            GUILayout.Label("<b>SFX</b>   studio file = reference · trim tunes the game level", boldStyle);
            GUILayout.Label("<i>1.00 = exactly what audio_studio plays. Serialized — tuned values ARE the shipped mix.</i>", labelStyle);

            GUILayout.Space(4);
            GUILayout.Label("<b>GUNSHOT</b>   sounds/gunshot.stsound", labelStyle);
            proj.shotAudioVolume = HSlider("level ×", proj.shotAudioVolume, 0.1f, 2f, "F2");
            proj.shotRefDistance = HSlider("full-vol radius m", proj.shotRefDistance, 0.5f, 20f, "F1");
            proj.shotAudibleRange = HSlider("report range m", proj.shotAudibleRange, 5f, 200f, "F0");
            GUILayout.BeginHorizontal();
            GUI.enabled = listener != null;
            if (GUILayout.Button("▶ reference", tabStyle, GUILayout.Height(22)))
            {
                var clip = ProceduralAudio.GetClip("sounds/gunshot.stsound");
                if (clip != null) AudioSource.PlayClipAtPoint(clip, listener.transform.position, 1f);
            }
            GUI.enabled = mnt != null && mnt.IsEquipped;
            if (GUILayout.Button("▶ muzzle", tabStyle, GUILayout.Height(22)))
            {
                var clip = ProceduralAudio.GetClip("sounds/gunshot.stsound");
                if (clip != null)
                {
                    ProceduralAudio.PlayAt(clip, mnt.MuzzleWorld, proj.shotAudioVolume,
                        proj.shotRefDistance, proj.shotAudibleRange);
                    SfxWaves.GetOrCreate().Emit(mnt.MuzzleWorld, proj.shotAudibleRange,
                        proj.shotRefDistance, "gunshot");
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>reference = at the listener (studio loudness) · muzzle = real 3D path + trim, emits the wave</i>", labelStyle);
            if (mnt == null || !mnt.IsEquipped)
                GUILayout.Label("<i>  muzzle test needs Vinny armed — possess (G) or equip via Combat</i>", labelStyle);

            GUILayout.Space(6);
            GUILayout.Label("<b>GLASS</b>   synthesized tink/crack/crash", labelStyle);
            dmg.glassAudioEnabled = GUILayout.Toggle(dmg.glassAudioEnabled, " glass sfx", toggleStyle);
            if (dmg.glassAudioEnabled)
            {
                dmg.glassAudioVolume = HSlider("level ×", dmg.glassAudioVolume, 0.1f, 2f, "F2");
                dmg.glassRefDistance = HSlider("full-vol radius m", dmg.glassRefDistance, 0.5f, 20f, "F1");
                dmg.glassAudibleRange = HSlider("report range m", dmg.glassAudibleRange, 5f, 200f, "F0");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("tink", tabStyle, GUILayout.Height(20))) dmg.PreviewGlassSound(0);
                if (GUILayout.Button("crack", tabStyle, GUILayout.Height(20))) dmg.PreviewGlassSound(1);
                if (GUILayout.Button("crash", tabStyle, GUILayout.Height(20))) dmg.PreviewGlassSound(2);
                GUILayout.EndHorizontal();
                GUILayout.Label("<i>shatters scale with cells removed (0.4+n/20) — trim is the base level</i>", labelStyle);
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>REPORT WAVES</b>   the audible edge visualized", labelStyle);
            var waves = SfxWaves.GetOrCreate();
            waves.showWaves = GUILayout.Toggle(waves.showWaves, " show report waves (TEAL loud → VIOLET fades)", toggleStyle);
            if (waves.showWaves)
            {
                waves.waveSpeed = HSlider("wave speed m/s", waves.waveSpeed, 5f, 343f, "F0");
                GUILayout.Label("<i>rim color = live audibility · inner ring = full-vol radius · edge = hearing radius (real 343 m/s slowed for viewing)</i>", labelStyle);
            }

            GUILayout.Space(6);
            GUILayout.Label($"hot-reload <b>{(ProceduralAudio.liveReload ? "on" : "off")}</b> — save in audio_studio → next trigger pulls the new recipe", labelStyle);
        }

        /// <summary>
        /// Material-resistance tuning + last-blast readout, sub-tabbed per model type.
        /// Edits the live static tables in VoxelDamageMath — detonations and occlusion
        /// marches pick up changes immediately; each blast resolves its table from the
        /// target volume id (veh_*/item_* conventions). Slider labels carry live kill
        /// probability at the current power dial: @center = power·(1−res), @½r = same
        /// with the linear distance falloff halved.
        /// </summary>
        void DrawMatResTab()
        {
            var dmgSys = VoxelDamageSystem.GetOrCreate();
            GUILayout.Label("<b>MATERIAL RESISTANCE</b>   <i>live</i>", boldStyle);

            // sub-tabs — one live table per model type
            GUILayout.BeginHorizontal();
            for (int p = 0; p < (int)VoxelDamageMath.ResProfile.Count; p++)
            {
                bool act = matResSub == p;
                if (GUILayout.Button(VoxelDamageMath.ResProfileNames[p],
                        act ? tabActiveStyle : tabStyle, GUILayout.Height(20)))
                    matResSub = p;
            }
            GUILayout.EndHorizontal();
            int prof = matResSub;
            string profName = VoxelDamageMath.ResProfileNames[prof];
            if (prof != (int)VoxelDamageMath.ResProfile.Building)
                GUILayout.Label($"<i>no damageable {profName} volumes yet — this table goes live for '{profName}_*'/'*_{profName}_*' volume ids when they register</i>", labelStyle);

            GUILayout.Label("kill roll p = power·(1−dist/radius)·(1−res)·transmit — walls spend the same table per cell the occlusion march crosses", labelStyle);
            VoxelDamageMath.GlobalResistScale =
                HSlider("global ×scale (all types)", VoxelDamageMath.GlobalResistScale, 0f, 2f, "F2");

            var tbl = VoxelDamageMath.ResTable[prof];
            var work = VoxelDamageMath.WorkshopRes[prof];
            GUILayout.Label($"{profName} res — kill% @center / @½r at power {blastPower:F1}:", labelStyle);
            GUILayout.Label("<color=#6fb7ff>■ authored</color>  <color=#ffa64d>■ workshop baseline</color>  <color=#5fe07a>■ tuned this session</color>", labelStyle);
            int n = (int)VoxelDamageMath.ResClass.Count;
            for (int c = 0; c < n; c++)
            {
                float eff = Mathf.Clamp01(tbl[c] * VoxelDamageMath.GlobalResistScale);
                float pC = Mathf.Clamp01(blastPower * (1f - eff));
                float pH = Mathf.Clamp01(blastPower * 0.5f * (1f - eff));
                // provenance color: factory → blue, on the workshop baseline → orange, tuned → green
                string col = Mathf.Approximately(tbl[c], VoxelDamageMath.BaseResistance[c]) ? "#6fb7ff"
                           : Mathf.Approximately(tbl[c], work[c]) ? "#ffa64d" : "#5fe07a";
                int sel = c;   // closure
                tbl[c] = ResRow($"<color={col}>{(matResSel == c ? "► " : "  ")}{VoxelDamageMath.ResClassNames[c]}</color>  {pC:P0}/{pH:P0}",
                    tbl[c], () => matResSel = sel);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"reset {profName} → workshop", GUILayout.Height(20)))
                VoxelDamageMath.ResetResistanceTable(prof);
            if (GUILayout.Button("reset ALL + scale", GUILayout.Height(20)))
                VoxelDamageMath.ResetResistanceTable();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"promote live → {profName} workshop", GUILayout.Height(20)))
                VoxelDamageMath.PromoteToWorkshop(prof);
            if (GUILayout.Button("restore AUTHORED", GUILayout.Height(20)))
                VoxelDamageMath.RestoreAuthoredDefaults();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            string selName = matResSel >= 0 && matResSel < n ? VoxelDamageMath.ResClassNames[matResSel] : null;
            GUI.enabled = selName != null;
            if (GUILayout.Button(selName != null ? $"promote '{selName}' → AUTHORED" : "click a name to select", GUILayout.Height(20)))
                PromoteResToAuthored(prof, matResSel);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("workshop = pending baseline (in code, seeds each play); AUTHORED = factory defaults — promote writes the .cs literal + recompiles (exits play mode)", labelStyle);

            GUILayout.Space(4);
            GUILayout.Label("<b>last blast</b>", boldStyle);
            int tot = 0;
            var sb = new System.Text.StringBuilder();
            for (int c = 0; c < n; c++)
                if (dmgSys.lastBlastRemoved[c] > 0)
                {
                    sb.Append($"{VoxelDamageMath.ResClassNames[c]}×{dmgSys.lastBlastRemoved[c]:N0}  ");
                    tot += dmgSys.lastBlastRemoved[c];
                }
            GUILayout.Label(tot > 0 || dmgSys.lastBlastDetached > 0
                ? $"on a <b>{VoxelDamageMath.ResProfileNames[Mathf.Clamp(dmgSys.lastBlastProfile, 0, (int)VoxelDamageMath.ResProfile.Count - 1)]}</b> volume — crater removed {tot:N0}:\n   {sb}\ndetached/collapse {dmgSys.lastBlastDetached:N0} · flyers emitted {dmgSys.LastFlyEmitted:N0}\ndominoes {dmgSys.lastBlastPairs:N0} · paired cells {dmgSys.lastBlastPairCells:N0}"
                : "no blast yet — pick a point and DETONATE", labelStyle);
        }

        /// <summary>HSlider with a wider label column so class name + live kill% fit;
        /// the name is a flat button when onName is supplied (MatRes class selection).</summary>
        private float ResRow(string name, float val, System.Action onName = null)
        {
            GUILayout.BeginHorizontal();
            if (onName != null)
            {
                if (GUILayout.Button(name, labelStyle, GUILayout.Width(150))) onName();
            }
            else GUILayout.Label(name, labelStyle, GUILayout.Width(150));
            float v = GUILayout.HorizontalSlider(val, 0f, 1f, GUILayout.ExpandWidth(true));
            GUILayout.Label(v.ToString("F2"), labelStyle, GUILayout.Width(42));
            GUILayout.EndHorizontal();
            return v;
        }

#if UNITY_EDITOR
        /// <summary>Locks a live resistance into the shipped table: rewrites the
        /// BaseResistance literal in VoxelDamageMath.cs and refreshes — the recompile
        /// + domain reload reseeds every layer (exits play mode).</summary>
        private void PromoteResToAuthored(int prof, int cls)
        {
            var vals = (float[])VoxelDamageMath.BaseResistance.Clone();
            vals[cls] = VoxelDamageMath.ResTable[prof][cls];
            var lit = "{ " + string.Join(", ", System.Array.ConvertAll(vals,
                v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "f")) + " }";
            string path = System.IO.Path.Combine(Application.dataPath, "Scripts/Sim/VoxelDamageMath.cs");
            string src = System.IO.File.ReadAllText(path);
            var rx = new System.Text.RegularExpressions.Regex(@"(BaseResistance\s*=\s*)\{[^}]*\}");
            if (!rx.IsMatch(src)) { Debug.LogError($"[MatRes] ❌ BaseResistance literal not found in {path}"); return; }
            System.IO.File.WriteAllText(path, rx.Replace(src, "$1" + lit, 1));
            Debug.Log($"[MatRes] 🔒 promoted {VoxelDamageMath.ResClassNames[cls]} → AUTHORED {vals[cls]:F2} — recompiling");
            UnityEditor.AssetDatabase.Refresh();
        }
#else
        private void PromoteResToAuthored(int prof, int cls) { }
#endif

        private float HSlider(string name, float val, float min, float max, string format = "F2")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, labelStyle, GUILayout.Width(105));
            float v = GUILayout.HorizontalSlider(val, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(v.ToString(format), labelStyle, GUILayout.Width(42));
            GUILayout.EndHorizontal();
            return v;
        }
    }
}
