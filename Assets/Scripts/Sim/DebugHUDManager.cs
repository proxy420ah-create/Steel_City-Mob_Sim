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
    /// Tabs: [Camera] [Render] [Clothing] [Path]
    /// Toggle: Backquote (`) key or O key
    ///
    /// Auto-finds FollowCamera, VoxelChunkManager, CityMap3D, and ClothingSystem
    /// in the scene. Missing components just skip their tab.
    /// </summary>
    public class DebugHUDManager : MonoBehaviour
    {
        public enum Tab { Camera, Render, Clothing, Path }
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Config")]
        [SerializeField] private Tab defaultTab = Tab.Camera;
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
        private ClothingSystem clothingSystem; // currently selected
        private PathDebugRenderer pathDebug;

        // Clothing system multi-instance tracking
        private List<ClothingSystem> allClothingSystems = new();
        private int selectedClothingIndex = 0;
        private float clothingListRefreshTimer = 0f;

        // Character rig multi-instance tracking (for hotkey control routing)
        private List<CharacterRig> allCharacterRigs = new();
        private int selectedRigIndex = 0;

        // Cached styles
        private GUIStyle labelStyle;
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
            if (pathDebug == null) pathDebug = FindFirstObjectByType<PathDebugRenderer>();
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
        }

        System.Collections.Generic.List<Tab> GetAvailableTabs()
        {
            var tabs = new System.Collections.Generic.List<Tab>();
            if (followCamera != null) tabs.Add(Tab.Camera);
            if (chunkManager != null) tabs.Add(Tab.Render);
            if (allClothingSystems.Count > 0) tabs.Add(Tab.Clothing);
            if (pathDebug != null) tabs.Add(Tab.Path);
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

            // Periodically refresh clothing system list (every 1 second)
            clothingListRefreshTimer += Time.deltaTime;
            if (clothingListRefreshTimer >= 1f)
            {
                clothingListRefreshTimer = 0f;
                RefreshClothingSystems();
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
            RefreshReferences();

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
                Tab[] tabs = { Tab.Camera, Tab.Render, Tab.Clothing, Tab.Path };
                string[] tabNames = { "Camera", "Render", "Clothing", "Path" };

                for (int i = 0; i < tabs.Length; i++)
                {
                    if (tabs[i] == Tab.Camera && followCamera == null) continue;
                    if (tabs[i] == Tab.Render && chunkManager == null) continue;
                    if (tabs[i] == Tab.Clothing && allClothingSystems.Count == 0) continue;
                    if (tabs[i] == Tab.Path && pathDebug == null) continue;

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
                case Tab.Camera: DrawCameraTab(); break;
                case Tab.Render: DrawRenderTab(); break;
                case Tab.Clothing: DrawClothingTab(); break;
                case Tab.Path: DrawPathTab(); break;
            }

            GUILayout.EndScrollView();

            // Footer — current tab + next tab + controls
            GUILayout.Space(4);

            var availableTabs = new System.Collections.Generic.List<Tab>();
            var availableNames = new System.Collections.Generic.List<string>();
            if (followCamera != null)   { availableTabs.Add(Tab.Camera);   availableNames.Add("Camera"); }
            if (chunkManager != null)    { availableTabs.Add(Tab.Render);   availableNames.Add("Render"); }
            if (allClothingSystems.Count > 0) { availableTabs.Add(Tab.Clothing); availableNames.Add("Clothing"); }
            if (pathDebug != null)      { availableTabs.Add(Tab.Path);     availableNames.Add("Path"); }

            int curIdx = availableTabs.IndexOf(activeTab);
            if (curIdx < 0) curIdx = 0;
            string curName = curIdx < availableNames.Count ? availableNames[curIdx] : "?";
            int nextIdx = (curIdx + 1) % availableTabs.Count;
            string nextName = nextIdx < availableNames.Count ? availableNames[nextIdx] : "?";

            string cornerName = usingCustomPosition ? "Dragged" : currentCorner.ToString();
            GUILayout.Label($"<b>Current:</b> {curName}    <b>Next [Tab]:</b> {nextName}    <b>Pos [Y]:</b> {cornerName}", labelStyle);

            GUILayout.Space(6);
            GUILayout.Label("<b>All Hotkeys</b>", boldStyle);
            GUILayout.Label("[`] / [O] Toggle panel  [Tab] Next tab  [Y] Cycle corner  [M] Minimize", labelStyle);
            GUILayout.Label("[T] Chase/Orbit  [Shift] Free-look  [Z] Reset camera", labelStyle);
            GUILayout.Label("[Q/E] Distance  [R/F] Height  [+/-] FOV  [Arrows] Orbit/Look", labelStyle);
            GUILayout.Label("[C] Capture  [P] Perf snapshot  [H] Toggle old camera HUD", labelStyle);
            GUILayout.Label("[Space] Play/Pause anim  [+/-] Anim speed  [1-6] Debug states", labelStyle);
            GUILayout.Label("[F8] Stress test spawn  [F10] Toggle vehicle driving", labelStyle);
            GUILayout.Label("[Clothing Test] Use ClothingTestSpawner inspector to spawn/dress", labelStyle);
            GUILayout.Label("<i>Drag title bar to move. Click _ to minimize.</i>", labelStyle);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void DrawCameraTab()
        {
            if (followCamera == null)
            {
                GUILayout.Label("No FollowCamera in scene.", labelStyle);
                return;
            }

            GUILayout.Label($"<b>Mode</b>: {(followCamera.ChaseMode ? "CHASE" : "ORBIT")}", labelStyle);
            GUILayout.Label($"<b>Position</b>: {followCamera.transform.position}", labelStyle);
            GUILayout.Label($"<b>FOV</b>: {followCamera.FieldOfView:F1}", labelStyle);
            GUILayout.Label($"<b>Distance</b>: {followCamera.Distance:F2}", labelStyle);
            GUILayout.Label($"<b>Height</b>: {followCamera.Height:F2}", labelStyle);
            GUILayout.Label($"<b>Yaw</b>: {followCamera.CurrentYaw:F1} deg", labelStyle);
            GUILayout.Label($"<b>Pitch</b>: {followCamera.CurrentPitch:F1} deg", labelStyle);
            GUILayout.Label($"<b>Follow Speed</b>: {followCamera.FollowSpeed:F1}", labelStyle);
            GUILayout.Label($"<b>Look Speed</b>: {followCamera.LookSpeed:F1}", labelStyle);

            GUILayout.Space(8);
            GUILayout.Label("<b>Controls</b>", boldStyle);
            GUILayout.Label("[T] Chase/Orbit toggle", labelStyle);
            GUILayout.Label("[Shift] Free-look", labelStyle);
            GUILayout.Label("[Z] Reset camera", labelStyle);
            GUILayout.Label("[Q/E] Distance  [R/F] Height", labelStyle);
            GUILayout.Label("[C] Capture  [P] Perf log", labelStyle);
        }

        void DrawRenderTab()
        {
            if (chunkManager == null)
            {
                GUILayout.Label("No VoxelChunkManager in scene.", labelStyle);
                return;
            }

            GUILayout.Label($"<b>FPS</b>: {fps:F0}  |  Frame: {frameTimeMs:F2}ms", labelStyle);
            GUILayout.Label($"  min:{frameTimeMin:F2}  max:{frameTimeMax:F2}  avg:{frameTimeAvg:F2}", labelStyle);
            GUILayout.Label($"<b>Resolution</b>: {chunkManager.RenderWidth}x{chunkManager.RenderHeight}", labelStyle);
            GUILayout.Label($"<b>Ortho</b>: {chunkManager.IsOrtho}  Size: {chunkManager.CameraOrthoSize:F1}", labelStyle);

            GUILayout.Space(6);
            GUILayout.Label("<b>Draw Calls</b>", boldStyle);
            GUILayout.Label($"  Total: {chunkManager.PerfTotalDrawCalls}", labelStyle);
            GUILayout.Label($"  Sectors: {chunkManager.PerfSectorsDrawn}  Instanced chars: {chunkManager.InstancedCharacterCount}", labelStyle);
            GUILayout.Label($"  Chunks: {chunkManager.PerfTotalChunks} total / {chunkManager.PerfActiveChunks} active / {chunkManager.PerfDrawnChunks} drawn", labelStyle);
            GUILayout.Label($"  Baked: {chunkManager.BakedSectorCount} sectors / {chunkManager.BakedSectorBuildingCount} buildings", labelStyle);

            GUILayout.Space(6);
            GUILayout.Label("<b>LOD Tiers</b>", boldStyle);
            GUILayout.Label($"  Near: {chunkManager.PerfLodNear}  Mid: {chunkManager.PerfLodMid}  Far: {chunkManager.PerfLodFar}", labelStyle);
            GUILayout.Label($"  Ultra: {chunkManager.PerfLodUltra}  Culled: {chunkManager.PerfLodCulled}", labelStyle);
            GUILayout.Label($"  ScreenRatio: {chunkManager.PerfMinScreenRatio:F4} - {chunkManager.PerfMaxScreenRatio:F4}", labelStyle);

            GUILayout.Space(6);
            GUILayout.Label("<b>CPU Timing</b>", boldStyle);
            GUILayout.Label($"  Cull: {chunkManager.CpuCullMs:F2}ms  Draw: {chunkManager.CpuDrawMs:F2}ms  Total: {chunkManager.CpuTotalMs:F2}ms", labelStyle);
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

        void DrawPathTab()
        {
            if (pathDebug == null)
            {
                GUILayout.Label("No PathDebugRenderer in scene.", labelStyle);
                return;
            }

            GUILayout.Label($"<b>Active Paths</b>: {pathDebug.ActivePathCount}", labelStyle);
            GUILayout.Label($"<b>Debug Enabled</b>: {pathDebug.DebugEnabled}", labelStyle);

            GUILayout.Space(8);
            GUILayout.Label("Path debug beams render into the", labelStyle);
            GUILayout.Label("voxel raymarch overlay (right panel).", labelStyle);
            GUILayout.Label("Paths are drawn during execution phase", labelStyle);
            GUILayout.Label("when characters/vehicles are moving.", labelStyle);
        }
    }
}
