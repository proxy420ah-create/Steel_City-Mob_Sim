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
        public enum Tab { Camera, Render, Clothing, Path, Range, Keys }
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Config")]
        [SerializeField] private Tab defaultTab = Tab.Keys;
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private float windowW = 380f;
        [SerializeField] private float windowH = 440f;
        [SerializeField] private float margin = 10f;

        private Corner currentCorner = Corner.TopLeft;

        private Tab activeTab;
        private bool visible = true;
        private bool minimized = false;
        private Vector2 scrollPos;

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

        // Range tab state — set by clicking a range block while sim is Dwelling
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
        private float nextTabRefresh;
        private float nextFooterRefresh;
        private float nextRefsRefresh;

        void Awake()
        {
            activeTab = defaultTab;
            visible = showOnStart;
            minimized = false;
            SetupBlockerCanvas();
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

        void RefreshReferences()
        {
            if (followCamera == null) followCamera = FindFirstObjectByType<FollowCamera>();
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (cityMap == null) cityMap = FindFirstObjectByType<CityMap3D>();
            if (gameUI == null) gameUI = FindFirstObjectByType<GameUIController>();
            if (cityMap != null && !blockClickHooked)
            {
                cityMap.OnBlockClicked += HandleBlockClicked;
                blockClickHooked = true;
            }
            if (pathDebug == null) pathDebug = FindFirstObjectByType<PathDebugRenderer>();
            if (vehicleTest == null) vehicleTest = FindFirstObjectByType<VehicleTestSpawner>();
            if (stressTest == null) stressTest = FindFirstObjectByType<StressTestSpawner>();
            if (stressDiag == null) stressDiag = FindFirstObjectByType<StressTestDiagnostics>();
            if (hoodSpawner == null) hoodSpawner = FindFirstObjectByType<HoodSpawner>();
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

        System.Collections.Generic.List<Tab> GetAvailableTabs()
        {
            var tabs = new System.Collections.Generic.List<Tab>();
            if (followCamera != null) tabs.Add(Tab.Camera);
            if (chunkManager != null) tabs.Add(Tab.Render);
            if (allClothingSystems.Count > 0) tabs.Add(Tab.Clothing);
            if (pathDebug != null) tabs.Add(Tab.Path);
            if (cityMap != null) tabs.Add(Tab.Range);
            tabs.Add(Tab.Keys);
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

                // Minimize toggle with M key
                if (kb.mKey.wasPressedThisFrame)
                {
                    minimized = !minimized;
                }
            }

            // Periodically refresh clothing system list (every 1 second)
            clothingListRefreshTimer += Time.deltaTime;
            if (clothingListRefreshTimer >= 1f)
            {
                clothingListRefreshTimer = 0f;
                RefreshClothingSystems();
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
                cityMap.OnBlockClicked -= HandleBlockClicked;
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
            if (Time.unscaledTime >= nextRefsRefresh)
            {
                nextRefsRefresh = Time.unscaledTime + 0.5f;
                RefreshReferences();
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

            GUILayout.BeginArea(windowRect, bgStyle);
            GUILayout.BeginVertical();

            // Header — title + minimize button + tab buttons
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            GUILayout.Label("<b>DEBUG HUD</b>", boldStyle, GUILayout.Width(80));

            if (!minimized)
            {
                Tab[] tabs = { Tab.Camera, Tab.Render, Tab.Clothing, Tab.Path, Tab.Range, Tab.Keys };
                string[] tabNames = { "Camera", "Render", "Clothing", "Path", "Range", "Keys" };

                for (int i = 0; i < tabs.Length; i++)
                {
                    if (tabs[i] == Tab.Camera && followCamera == null) continue;
                    if (tabs[i] == Tab.Render && chunkManager == null) continue;
                    if (tabs[i] == Tab.Clothing && allClothingSystems.Count == 0) continue;
                    if (tabs[i] == Tab.Path && pathDebug == null) continue;
                    if (tabs[i] == Tab.Range && cityMap == null) continue;

                    var style = activeTab == tabs[i] ? tabActiveStyle : tabStyle;
                    if (GUILayout.Button(tabNames[i], style, GUILayout.Height(20)))
                        activeTab = tabs[i];
                }
            }

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

            // Separator
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));

            // Tab content
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));

            switch (activeTab)
            {
                case Tab.Clothing: DrawClothingTab(); break;
                case Tab.Range: DrawRangeTab(); break;
                default:
                    if (activeTab != lastBuiltTab
                            || Time.unscaledTime >= nextTabRefresh)
                        RebuildTabText();
                    GUILayout.Label(tabText, labelStyle);
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
            hudSB.AppendLine("[I/W/L/A/C/T] Character anim  [Space] Pause  [=/-] Speed");
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

        void BuildRenderTab(System.Text.StringBuilder sb)
        {
            if (chunkManager == null)
            {
                sb.AppendLine("No VoxelChunkManager in scene.");
                return;
            }

            sb.AppendLine($"<b>FPS</b>: {fps:F0}  |  Frame: {frameTimeMs:F2}ms");
            sb.AppendLine($"  min:{frameTimeMin:F2}  max:{frameTimeMax:F2}  avg:{frameTimeAvg:F2}");
            sb.AppendLine($"<b>Resolution</b>: {chunkManager.RenderWidth}x{chunkManager.RenderHeight}");
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
                sb.AppendLine(H("Character Anim") + " (green rig, Clothing tab)");
                sb.AppendLine("[I] Idle     [W] Walk     [L] Look");
                sb.AppendLine("[A] Aim      [C] Crouch   [T] T-Pose");
                sb.AppendLine("[Space] Play/pause    [=] / [-] Anim speed");
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
        // Range tab — live firing-range tuning/debug.
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
            OpenRangeTab(blockId);
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

        /// <summary>Open the HUD on the Range tab for the given block.</summary>
        public void OpenRangeTab(string blockId)
        {
            rangeBlockId = blockId;
            activeTab = Tab.Range;
            visible = true;
            minimized = false;
        }

        void DrawRangeTab()
        {
            var sim = gameUI != null ? gameUI.Sim : null;
            string simState = sim != null ? sim.State.ToString() : "no sim";
            bool dwelling = sim != null && sim.State == SimState.Dwelling;

            GUILayout.Label($"<b>RANGE</b>   sim: {simState}", boldStyle);

            // SFX preview — above the dwelling gate so it works in planning phase.
            // Plays at the AudioListener, so distance can't mask a pipeline problem.
            var dmgSys = VoxelDamageSystem.GetOrCreate();
            GUILayout.BeginHorizontal();
            GUILayout.Label("sfx test:", labelStyle, GUILayout.Width(52));
            if (GUILayout.Button("tink", tabStyle, GUILayout.Height(20))) dmgSys.PreviewGlassSound(0);
            if (GUILayout.Button("crack", tabStyle, GUILayout.Height(20))) dmgSys.PreviewGlassSound(1);
            if (GUILayout.Button("crash", tabStyle, GUILayout.Height(20))) dmgSys.PreviewGlassSound(2);
            GUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(rangeBlockId))
            {
                GUILayout.Space(4);
                GUILayout.Label("Click a range block while the sim is", labelStyle);
                GUILayout.Label("dwelling (Vinny standing at the line).", labelStyle);
                return;
            }

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
            bool hasPos = cityMap.TryGetBuildingPointLocal(rangeBlockId, posName, out var posLocal)
                       || cityMap.TryGetBuildingPointLocal(rangeBlockId, "firing_position", out posLocal);
            bool hasTgt = cityMap.TryGetBuildingPointLocal(rangeBlockId, tgtName, out var tgtLocal)
                       || cityMap.TryGetBuildingPointLocal(rangeBlockId, "firing_target", out tgtLocal);
            Vector3 posWorld = cityMap.MapRoot.position + posLocal;
            Vector3 tgtWorld = cityMap.MapRoot.position + tgtLocal;
            GUILayout.Label($"{posName}: {(hasPos ? "OK" : "<color=#ff6666>MISSING</color>")}   {tgtName}: {(hasTgt ? "OK" : "<color=#ff6666>MISSING</color>")}", labelStyle);

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
            if (mount != null && hasPos)
            {
                var d = mount.Dims; float vs = mount.CharVoxelSize;
                Vector3 chCenter = ch.transform.position + new Vector3(d.x * vs * 0.5f, 0f, d.z * vs * 0.5f);
                float offM = Vector3.Distance(new Vector3(chCenter.x, 0, chCenter.z), new Vector3(posWorld.x, 0, posWorld.z));
                GUILayout.Label($"In position: {(offM < 0.35f ? "<color=#66ff66>YES</color>" : $"<color=#ffaa44>off {offM:F2}m</color>")}", labelStyle);
            }
            GUILayout.Label($"Weapon: {(mount != null && mount.IsEquipped ? mount.ItemFileName : "<color=#ff6666>none</color>")}", labelStyle);
            if (mount != null && mount.IsEquipped)
            {
                GUILayout.Label($"  bore axis: {(mount.BoreAxisLocal != Vector3.right ? "measured" : "+X fallback")}  hand gid {mount.HandGid}", labelStyle);
                mount.showMuzzleRay = GUILayout.Toggle(mount.showMuzzleRay, " muzzle ray (red bore axis)", toggleStyle);
                solver.showSightBeam = GUILayout.Toggle(solver.showSightBeam, " sight line (cyan, while aiming)", toggleStyle);
            }

            GUILayout.Space(6);

            // --- IK Aim toggle ---
            bool aiming = solver.IsAiming;
            var btnStyle = aiming ? tabActiveStyle : tabStyle;
            GUI.enabled = dwelling && hasPos && hasTgt && mount != null && mount.IsEquipped;
            string btnLabel = aiming ? "IK AIM — ENGAGED (click to release)" : "IK AIM";
            if (GUILayout.Button(btnLabel, btnStyle, GUILayout.Height(26)))
            {
                if (aiming) solver.Disarm();
                else solver.Arm(tgtWorld);
            }
            GUI.enabled = true;
            if (!dwelling)
                GUILayout.Label("(available while sim is Dwelling)", labelStyle);
            if (!string.IsNullOrEmpty(solver.LastError))
                GUILayout.Label($"<color=#ff6666>solver: {solver.LastError}</color>", labelStyle);

            DrawFireControls(dwelling, mount, tgtWorld, hasTgt);

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
                dmg.glassAudioEnabled = GUILayout.Toggle(dmg.glassAudioEnabled, " glass sfx (synth tink/crash)", toggleStyle);
                if (dmg.glassAudioEnabled)
                    dmg.glassAudioVolume = HSlider("sfx volume", dmg.glassAudioVolume, 0f, 1f, "F2");
            }
            if (dmg.DamagedVolumeCount > 0 && GUILayout.Button($"RESET DAMAGE ({dmg.DamagedVolumeCount} volume{(dmg.DamagedVolumeCount > 1 ? "s" : "")}) — fresh glass", GUILayout.Height(22)))
                dmg.ResetAllDamage();

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
