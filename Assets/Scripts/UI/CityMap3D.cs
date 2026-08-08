using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.IO;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SteelCity.Sim
{
    /// <summary>
    /// Renders the city map as 3D isometric blocks in world space, rendered by a
    /// dedicated orthographic camera into the right portion of the screen.
    /// Clicking a block raises OnBlockClicked. Call BuildMap() once after the
    /// engine is initialized, then UpdateBlock() per block whenever data changes.
    ///
    /// Supports three rendering modes:
    ///   - Cube mode (original): simple colored cubes per block
    ///   - Voxel mesh mode: loads .stasset voxel buildings as Unity Meshes
    ///   - Voxel raymarch mode: GPU DDA raymarch via compute shader (production target)
    /// Toggle at runtime via the Inspector or City Editor panel.
    /// </summary>
    public class CityMap3D : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Camera mapCamera;
        [Range(0f, 1f)]
        [SerializeField] private float viewportWidth = 0.4f;
        [SerializeField] private bool mapOnRightSide = true;
        [Range(0f, 1f)]
        [SerializeField] private float viewportYMin = 0.09f;
        [Range(0f, 1f)]
        [SerializeField] private float viewportYMax = 0.93f;
        [SerializeField] private Color backgroundColor = new(0.063f, 0.063f, 0.106f);

        [Header("Voxel Raymarch")]
        [Tooltip("Reference to VoxelChunkManager component.")]
        [SerializeField] private VoxelChunkManager chunkManager;
        private VoxelRenderBridge renderBridge;
        [Tooltip("World size of each voxel in the .stasset buildings.")]
        [SerializeField] private float voxelSize = 0.1f;
        [Tooltip("Width of the road between blocks (cars, trolleys).")]
        [SerializeField] private float roadWidth = 1.6f;
        [Tooltip("Width of sidewalk strip around each building (in world units). ~10 building voxels = room for benches, foot traffic, cops on beat.")]
        [SerializeField] private float sidewalkWidth = 1.0f;
        [Tooltip("Number of building slots per block row (3 = 3×3 grid with center courtyard).")]
        [SerializeField] private int buildingsPerBlockRow = 3;
        // Computed: groundTileSize = (buildingVoxelWidth * buildingsPerRow * voxelSize) + sidewalkWidth * 2
        //           voxelBlockSpacing = groundTileSize + roadWidth
        private const int BuildingVoxelWidth = 32;
        private float GroundTileSize => (BuildingVoxelWidth * buildingsPerBlockRow * voxelSize) + sidewalkWidth * 2f;
        private float ComputedSpacing => GroundTileSize + roadWidth;
        private float voxelBlockSpacing => ComputedSpacing;

        [Header("Roads")]
        [Tooltip("Show road name labels on streets.")]
        [SerializeField] private bool showRoadNames = true;

        [Header("Terrain")]
        [Tooltip("If true, generate one small terrain chunk per block instead of one massive chunk. Reduces DDA traversal cost.")]
        [SerializeField] private bool useSplitTerrain = true;

        [Header("Sector Baking")]
        [Tooltip("If true, bake static buildings into sector-level merged buffers (1 draw call per sector instead of 1 per building).")]
        [SerializeField] private bool useSectorBaking = false;
        [Tooltip("Number of blocks per sector side (4 = 4x4 blocks = 16 blocks per sector).")]
        [SerializeField] private int sectorSizeBlocks = 4;

        [Header("Label")]
        [Tooltip("Show floating block name labels above buildings.")]
        [SerializeField] private bool showBlockLabels = false;
        [SerializeField] private int labelFontSize = 3;
        [SerializeField] private Color labelColor = Color.white;

        public event Action<string> OnBlockClicked;

        // === City Editor API (runtime parameter adjustment) ===

        /// <summary>Rebuild the entire city from cached blocks. Call after changing any parameter.</summary>
        public void RebuildCity()
        {
            if (cachedBlocks != null)
                BuildMap(cachedBlocks);
        }

        public void SetRoadWidth(float width)
        {
            roadWidth = Mathf.Max(0.1f, width);
            RebuildCity();
        }

        public void SetSidewalkWidth(float width)
        {
            sidewalkWidth = Mathf.Max(0.1f, width);
            RebuildCity();
        }

        public void SetVoxelSize(float size)
        {
            voxelSize = Mathf.Clamp(size, 0.01f, 0.5f);
            RebuildCity();
        }

        public void SetBuildingsPerBlockRow(int rows)
        {
            buildingsPerBlockRow = Mathf.Clamp(rows, 1, 5);
            RebuildCity();
        }

        public void SetShowRoadNames(bool show)
        {
            showRoadNames = show;
            RebuildCity();
        }

        public void SetShowBlockLabels(bool show)
        {
            showBlockLabels = show;
            RebuildCity();
        }

        public void SetCameraOrthoSize(float size)
        {
            if (mapCamera != null)
                mapCamera.orthographicSize = Mathf.Clamp(size, 1f, 60f);
        }

        // --- Camera rotation (smooth) ---
        private float cameraYaw = 45f;
        private float cameraPitch = 35.264f;
        private float targetYaw = 45f;
        private float targetPitch = 35.264f;
        private const float CameraOrbitDistance = 40f;
        private const float CameraOrbitHeight = 20f;
        private Vector3 cameraFocus = new Vector3(0f, 0f, -100f);
        private bool cameraFollowsCityCenter = true;

        public float GetCameraYaw() => cameraYaw;
        public float GetCameraPitch() => cameraPitch;

        public void SetCameraYaw(float yaw)
        {
            targetYaw = yaw;
        }

        public void SetCameraPitch(float pitch)
        {
            targetPitch = Mathf.Clamp(pitch, 10f, 80f);
        }

        public void ResetCamera()
        {
            targetYaw = 45f;
            targetPitch = 35.264f;
            panOffset = Vector3.zero;
            cameraFollowsCityCenter = true;
            if (mapCamera != null)
                mapCamera.orthographicSize = 18f;
        }

        /// <summary>Set the world-space point the camera orbits around.</summary>
        public void SetCameraFocus(Vector3 worldPos)
        {
            cameraFocus = worldPos;
            cameraFollowsCityCenter = false;
        }

        private void UpdateCameraTransform()
        {
            if (mapCamera == null) return;

            // Update focus point to follow city center
            if (cameraFollowsCityCenter && mapRoot != null)
                cameraFocus = mapRoot.position;

            // Smooth interpolation toward target
            cameraYaw = Mathf.LerpAngle(cameraYaw, targetYaw, Time.deltaTime * 5f);
            cameraPitch = Mathf.Lerp(cameraPitch, targetPitch, Time.deltaTime * 5f);

            // Compute camera position from yaw/pitch orbiting focus point
            float yawRad = cameraYaw * Mathf.Deg2Rad;
            float pitchRad = cameraPitch * Mathf.Deg2Rad;
            float horizDist = CameraOrbitDistance * Mathf.Cos(pitchRad);
            float height = CameraOrbitDistance * Mathf.Sin(pitchRad);

            Vector3 camPos = cameraFocus + new Vector3(
                -horizDist * Mathf.Sin(yawRad),
                height,
                -horizDist * Mathf.Cos(yawRad)
            );

            mapCamera.transform.position = camPos;
            mapCamera.transform.LookAt(cameraFocus);
        }

        public void SetMaterialBrightness(ushort matId, float brightness)
        {
            var def = StAssetReader.GetDefaultMaterialColor(matId);
            float defBrightness = (def.r + def.g + def.b) / 3f;
            if (defBrightness < 0.001f) defBrightness = 0.001f;
            float factor = brightness / defBrightness;
            StAssetReader.SetMaterialColor(matId, new Color(
                Mathf.Clamp01(def.r * factor),
                Mathf.Clamp01(def.g * factor),
                Mathf.Clamp01(def.b * factor),
                def.a));
            if (chunkManager != null)
                chunkManager.RefreshMaterialBuffer();
        }

        public float GetMaterialBrightness(ushort matId)
        {
            var def = StAssetReader.GetDefaultMaterialColor(matId);
            return (def.r + def.g + def.b) / 3f;
        }

        // --- Per-chunk per-material tint API ---

        /// <summary>
        /// Set per-material tint for a specific chunk (building).
        /// Tints are float3 multipliers: finalColor = baseColor * tint.
        /// Default tint (1,1,1) = no change.
        /// </summary>
        public void SetChunkTint(string chunkName, Dictionary<ushort, Vector4> tints)
        {
            if (chunkManager != null)
                chunkManager.SetChunkTint(chunkName, tints);
        }

        /// <summary>
        /// Clear tint for a chunk, resetting to default (no tinting).
        /// </summary>
        public void ClearChunkTint(string chunkName)
        {
            if (chunkManager != null)
                chunkManager.ClearChunkTint(chunkName);
        }

        /// <summary>
        /// Set a single-material tint for a building chunk.
        /// Convenience wrapper for the common case of tinting one material.
        /// </summary>
        public void SetBuildingMaterialTint(string chunkName, ushort materialId, Color tint)
        {
            var tints = new Dictionary<ushort, Vector4>
            {
                { materialId, new Vector4(tint.r, tint.g, tint.b, tint.a) }
            };
            SetChunkTint(chunkName, tints);
        }

        public float GetRoadWidth() => roadWidth;
        public float GetSidewalkWidth() => sidewalkWidth;
        public float GetVoxelSize() => voxelSize;
        public int GetBuildingsPerBlockRow() => buildingsPerBlockRow;
        public bool GetShowRoadNames() => showRoadNames;
        public bool GetShowBlockLabels() => showBlockLabels;
        public bool GetUseSplitTerrain() => useSplitTerrain;
        public void SetUseSplitTerrain(bool v) => useSplitTerrain = v;
        public bool GetUseProxyRender() => chunkManager?.GetUseProxyRender() ?? false;
        public void SetUseProxyRender(bool v) { if (chunkManager != null) chunkManager.SetUseProxyRender(v); }
        public void SetGranularLodMode(bool v) { if (chunkManager != null) chunkManager.SetGranularLodMode(v); }
        public float GetCameraOrthoSize() => mapCamera != null ? mapCamera.orthographicSize : 18f;

        // --- Shadow debug API ---

        public void SetShadowNormalNudge(float v)
        {
            if (chunkManager != null) chunkManager.SetShadowParams(v, chunkManager.GetShadowLightNudge(), chunkManager.GetShadowSkipSteps(), chunkManager.GetShadowMaxSteps(), chunkManager.GetShadowEnabled());
        }
        public void SetShadowLightNudge(float v)
        {
            if (chunkManager != null) chunkManager.SetShadowParams(chunkManager.GetShadowNormalNudge(), v, chunkManager.GetShadowSkipSteps(), chunkManager.GetShadowMaxSteps(), chunkManager.GetShadowEnabled());
        }
        public void SetShadowSkipSteps(int v)
        {
            if (chunkManager != null) chunkManager.SetShadowParams(chunkManager.GetShadowNormalNudge(), chunkManager.GetShadowLightNudge(), v, chunkManager.GetShadowMaxSteps(), chunkManager.GetShadowEnabled());
        }
        public void SetShadowMaxSteps(int v)
        {
            if (chunkManager != null) chunkManager.SetShadowParams(chunkManager.GetShadowNormalNudge(), chunkManager.GetShadowLightNudge(), chunkManager.GetShadowSkipSteps(), v, chunkManager.GetShadowEnabled());
        }
        public void SetShadowEnabled(bool v)
        {
            if (chunkManager != null) chunkManager.SetShadowParams(chunkManager.GetShadowNormalNudge(), chunkManager.GetShadowLightNudge(), chunkManager.GetShadowSkipSteps(), chunkManager.GetShadowMaxSteps(), v ? 1 : 0);
        }

        public float GetShadowNormalNudge() => chunkManager?.GetShadowNormalNudge() ?? 2.5f;
        public float GetShadowLightNudge() => chunkManager?.GetShadowLightNudge() ?? 2.0f;
        public int GetShadowSkipSteps() => chunkManager?.GetShadowSkipSteps() ?? 4;
        public int GetShadowMaxSteps() => chunkManager?.GetShadowMaxSteps() ?? 32;
        public bool GetShadowEnabled() => (chunkManager?.GetShadowEnabled() ?? 1) == 1;

        // --- Lighting debug toggles ---

        public void SetSunLightEnabled(bool v)
        {
            if (chunkManager != null) chunkManager.SetLightingToggles(v, chunkManager.GetAmbientEnabled(), chunkManager.GetFillEnabled(), chunkManager.GetCamLightEnabled());
        }
        public void SetAmbientEnabled(bool v)
        {
            if (chunkManager != null) chunkManager.SetLightingToggles(chunkManager.GetSunLightEnabled(), v, chunkManager.GetFillEnabled(), chunkManager.GetCamLightEnabled());
        }
        public void SetFillEnabled(bool v)
        {
            if (chunkManager != null) chunkManager.SetLightingToggles(chunkManager.GetSunLightEnabled(), chunkManager.GetAmbientEnabled(), v, chunkManager.GetCamLightEnabled());
        }
        public void SetCamLightEnabled(bool v)
        {
            if (chunkManager != null) chunkManager.SetLightingToggles(chunkManager.GetSunLightEnabled(), chunkManager.GetAmbientEnabled(), chunkManager.GetFillEnabled(), v);
        }

        public bool GetSunLightEnabled() => chunkManager?.GetSunLightEnabled() ?? true;
        public bool GetAmbientEnabled() => chunkManager?.GetAmbientEnabled() ?? true;
        public bool GetFillEnabled() => chunkManager?.GetFillEnabled() ?? true;
        public bool GetCamLightEnabled() => chunkManager?.GetCamLightEnabled() ?? true;

        // --- Accessors for SimulationManager / EventPlayer ---
        public Transform MapRoot => mapRoot;
        public CityLayout CachedLayout => cachedLayout;
        public float Spacing => ComputedSpacing;
        public float GroundTile => GroundTileSize;
        public float SidewalkW => sidewalkWidth;
        public Camera MapCamera => mapCamera;
        public VoxelCharacter SpawnedCharacter { get; private set; }
        public Dictionary<string, Block> CachedBlocks => cachedBlocks;
        public float CharacterVoxelSize => characterVoxelSize;

        // --- Debug path overlay ---
        private GameObject debugPathRoot;

        /// <summary>
        /// Draw a debug overlay showing the planned path as colored lines through waypoint nodes.
        /// Reuses compass-style LineRenderer + sphere markers. Green = start, Yellow = end,
        /// Cyan = intermediate nodes. Lines sit slightly above ground to avoid z-fighting.
        /// </summary>
        public void ShowDebugPath(List<string> nodeIds, WaypointGraph graph)
        {
            ClearDebugPath();

            if (nodeIds == null || nodeIds.Count == 0 || graph == null) return;

            debugPathRoot = new GameObject("DebugPath");
            debugPathRoot.transform.SetParent(mapRoot, false);

            float pathY = voxelSize * 2.5f; // slightly above ground surface

            // Build line positions in world space
            var positions = new Vector3[nodeIds.Count];
            for (int i = 0; i < nodeIds.Count; i++)
            {
                if (!graph.Nodes.ContainsKey(nodeIds[i])) return;
                positions[i] = graph.Nodes[nodeIds[i]].localPos + mapRoot.position;
                positions[i].y = pathY;
            }

            // Main path line
            var lineObj = new GameObject("DebugPathLine");
            lineObj.transform.SetParent(debugPathRoot.transform, false);
            var lr = lineObj.AddComponent<LineRenderer>();
            lr.positionCount = positions.Length;
            lr.useWorldSpace = true;
            lr.startWidth = 0.12f;
            lr.endWidth = 0.12f;
            lr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Standard"));
            lr.startColor = new Color(0.2f, 1f, 0.3f, 0.9f);  // green start
            lr.endColor = new Color(1f, 0.9f, 0.2f, 0.9f);    // yellow end
            for (int i = 0; i < positions.Length; i++)
                lr.SetPosition(i, positions[i]);

            // Node markers — small spheres at each waypoint
            for (int i = 0; i < positions.Length; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = $"PathNode_{i}_{nodeIds[i]}";
                marker.transform.SetParent(debugPathRoot.transform, false);
                marker.transform.localScale = new Vector3(0.15f, 0.05f, 0.15f);
                marker.transform.position = positions[i];

                var rend = marker.GetComponent<Renderer>();
                rend.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

                if (i == 0)
                    rend.material.color = new Color(0.2f, 1f, 0.3f, 1f);  // green = start
                else if (i == positions.Length - 1)
                    rend.material.color = new Color(1f, 0.9f, 0.2f, 1f); // yellow = end
                else
                    rend.material.color = new Color(0.2f, 0.8f, 1f, 1f); // cyan = intermediate

                var col = marker.GetComponent<Collider>();
                if (col) Destroy(col);
            }

            Debug.Log($"[CityMap3D] Debug path drawn: {nodeIds.Count} nodes");
        }

        public void ClearDebugPath()
        {
            if (debugPathRoot != null)
            {
                Destroy(debugPathRoot.gameObject);
                debugPathRoot = null;
            }
        }

        // --- Waypoint label overlay ---
        private GameObject waypointLabelRoot;

        /// <summary>
        /// Render floating text labels at every waypoint node in the graph.
        /// Shows node ID, type, and edge index so you can verify corner/mid placement.
        /// Corners = yellow, Mids = cyan, Crosswalk = orange.
        /// </summary>
        public void ShowWaypointLabels(WaypointGraph graph)
        {
            ClearWaypointLabels();

            if (graph == null) return;

            waypointLabelRoot = new GameObject("WaypointLabels");
            waypointLabelRoot.transform.SetParent(mapRoot, false);

            float labelY = voxelSize * 3f; // above ground + path line

            foreach (var (nodeId, node) in graph.Nodes)
            {
                var labelObj = new GameObject($"WP_{nodeId}");
                labelObj.transform.SetParent(waypointLabelRoot.transform, false);

                Vector3 worldPos = node.localPos + mapRoot.position;
                worldPos.y = labelY;
                labelObj.transform.position = worldPos;

                // Rotate to lay flat on ground (same as compass labels)
                labelObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var tmp = labelObj.AddComponent<TextMeshPro>();
                tmp.fontSize = 2f;
                tmp.fontSizeMax = 2f;
                tmp.enableAutoSizing = false;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontStyle = FontStyles.Bold;

                string typeShort = node.type switch
                {
                    WaypointType.SidewalkCorner => "C",
                    WaypointType.SidewalkMid => "M",
                    WaypointType.CrosswalkCorner => "X",
                    _ => "?"
                };

                tmp.text = $"{nodeId}\n{typeShort} e{node.edgeIndex}";

                tmp.color = node.type switch
                {
                    WaypointType.SidewalkCorner => new Color(1f, 0.9f, 0.2f, 0.95f),   // yellow
                    WaypointType.SidewalkMid => new Color(0.2f, 0.8f, 1f, 0.95f),       // cyan
                    WaypointType.CrosswalkCorner => new Color(1f, 0.5f, 0.1f, 0.95f),   // orange
                    _ => Color.white
                };

                // Small sphere marker underneath the text
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = $"Marker_{nodeId}";
                marker.transform.SetParent(labelObj.transform, false);
                marker.transform.localPosition = Vector3.zero;
                marker.transform.localScale = new Vector3(0.08f, 0.03f, 0.08f);
                var rend = marker.GetComponent<Renderer>();
                rend.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                rend.material.color = tmp.color;
                var col = marker.GetComponent<Collider>();
                if (col) Destroy(col);
            }

            Debug.Log($"[CityMap3D] Waypoint labels drawn: {graph.Nodes.Count} nodes");
        }

        public void ClearWaypointLabels()
        {
            if (waypointLabelRoot != null)
            {
                Destroy(waypointLabelRoot.gameObject);
                waypointLabelRoot = null;
            }
        }

        private readonly Dictionary<string, BlockView3D> views = new();
        private Transform mapRoot;
        private Dictionary<string, Block> cachedBlocks;
        private CityLayout cachedLayout;
        private VoxelCollisionWorld collisionWorld;

        // --- Road ticker UI (reports road under mouse) ---
        private TextMeshProUGUI roadTickerText;
        // --- Perf ticker UI (drawn/cov/scale/steps/LOD) ---
        private TextMeshProUGUI perfTickerText;
        private int cachedMinRow, cachedMaxRow, cachedMinCol, cachedMaxCol;
        private float cachedCenterRow, cachedCenterCol, cachedSpacing;
        private bool gridCached = false;

        void Awake()
        {
            mapRoot = new GameObject("MapRoot").transform;
            mapRoot.SetParent(transform, false);
            // Offset the 3D city scene away from origin so the ScreenSpaceOverlay
            // canvas plane (locked at origin by Unity) doesn't overlap in the Scene view
            mapRoot.localPosition = new Vector3(0f, 0f, -100f);

            if (mapCamera == null)
                mapCamera = CreateIsometricCamera();

            // Ensure chunk manager exists and has camera reference
            if (chunkManager == null)
                chunkManager = GetComponent<VoxelChunkManager>();
            if (chunkManager == null)
                chunkManager = gameObject.AddComponent<VoxelChunkManager>();
            if (chunkManager != null)
            {
                chunkManager.SetRenderCamera(mapCamera);
                // Auto-load compute shader if not assigned in Inspector
                chunkManager.TryAutoLoadShader();
            }

            // Add render bridge to the camera GameObject so OnRenderImage fires
            renderBridge = mapCamera.GetComponent<VoxelRenderBridge>();
            if (renderBridge == null)
                renderBridge = mapCamera.gameObject.AddComponent<VoxelRenderBridge>();
            renderBridge.chunkManager = chunkManager;

            // Add VoxelSun for raymarch lighting (auto-creates if not present)
            var sun = GetComponent<VoxelSun>();
            if (sun == null)
                sun = gameObject.AddComponent<VoxelSun>();

            SetupRoadTicker();
        }

        /// <summary>
        /// Create a road ticker UI text element positioned in the upper-left corner
        /// of the map viewport. Reports the road/intersection under the mouse cursor.
        /// </summary>
        void SetupRoadTicker()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[CityMap3D] No Canvas found — road ticker disabled.");
                return;
            }

            var tickerObj = new GameObject("RoadTicker");
            tickerObj.transform.SetParent(canvas.transform, false);
            var vp = mapCamera.rect;
            var rt = tickerObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(vp.xMin, vp.yMax);
            rt.anchorMax = new Vector2(vp.xMin, vp.yMax);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(8f, -8f);
            rt.sizeDelta = new Vector2(220f, 60f);

            // Background on a child (can't share Graphic components on one GameObject)
            var bgObj = new GameObject("RoadTickerBG");
            bgObj.transform.SetParent(tickerObj.transform, false);
            var bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bg = bgObj.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.6f);
            bg.raycastTarget = false;

            // Text on the parent
            roadTickerText = tickerObj.AddComponent<TextMeshProUGUI>();
            roadTickerText.fontSize = 14;
            roadTickerText.fontStyle = FontStyles.Bold;
            roadTickerText.alignment = TextAlignmentOptions.TopLeft;
            roadTickerText.color = new Color(1f, 0.95f, 0.7f, 0.9f);
            roadTickerText.text = "—";
            roadTickerText.raycastTarget = false;

            Debug.Log("[CityMap3D] Road ticker created.");

            // Perf ticker just below the road ticker
            var perfObj = new GameObject("PerfTicker");
            perfObj.transform.SetParent(canvas.transform, false);
            var prt = perfObj.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(vp.xMin, vp.yMax);
            prt.anchorMax = new Vector2(vp.xMin, vp.yMax);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(8f, -72f);
            prt.sizeDelta = new Vector2(360f, 64f);

            perfTickerText = perfObj.AddComponent<TextMeshProUGUI>();
            perfTickerText.fontSize = 12;
            perfTickerText.fontStyle = FontStyles.Bold;
            perfTickerText.alignment = TextAlignmentOptions.TopLeft;
            perfTickerText.color = new Color(0.8f, 1f, 0.9f, 0.95f);
            perfTickerText.text = "";
            perfTickerText.raycastTarget = false;
        }

        // --- Mouse camera state ---
        private bool isRotating;
        private bool isPanning;
        private Vector2 lastMousePos;
        private Vector3 panOffset = Vector3.zero;

        // Set by GameUIController when entering/leaving Working mode
        public bool IsExecutionMode { get; set; } = false;

        void Update()
        {
            if (IsExecutionMode)
            {
                // Working mode: skip planning-only interactions (mouse camera, click, road ticker)
                // FollowCamera handles camera; map camera is disabled
                return;
            }
            HandleMouseCamera();
            HandleClick();
            UpdateCameraTransform();
            UpdateRoadTicker();
            UpdatePerfTicker();
        }

        private void UpdatePerfTicker()
        {
            if (perfTickerText == null || chunkManager == null) return;
            // Respect HUD toggle on chunk manager
            bool show = chunkManager.ShowOrthoHud;
            if (!show)
            {
                if (perfTickerText.enabled) perfTickerText.enabled = false;
                return;
            }
            if (!perfTickerText.enabled) perfTickerText.enabled = true;

            int drawn = chunkManager.PerfDrawnChunks;
            float covPct = chunkManager.ApproxCoveragePct * 100f;
            float scale = chunkManager.CurrentResolutionScale;
            float steps = chunkManager.AvgLodSteps;
            int n = chunkManager.PerfLodNear;
            int m = chunkManager.PerfLodMid;
            int f = chunkManager.PerfLodFar;
            int u = chunkManager.PerfLodUltra;

            perfTickerText.text = $"Drawn {drawn}  |  Cov {covPct:F0}%  |  Scale {scale:F2}  |  Steps {steps:F0}\nLOD N:{n} M:{m} F:{f} U:{u}";
        }

        /// <summary>
        /// Mouse camera controls (only when cursor is over the map viewport):
        ///   LMB click  — focus on clicked block / reset focus to city center
        ///   MMB drag   — rotate camera (yaw + pitch)
        ///   RMB drag   — pan camera focus
        ///   Wheel      — zoom in/out
        /// </summary>
        private void HandleMouseCamera()
        {
            if (Mouse.current == null || mapCamera == null) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            float normX = mousePos.x / Screen.width;
            float normY = mousePos.y / Screen.height;
            var vp = mapCamera.rect;
            bool overMap = normX >= vp.x && normX <= vp.x + vp.width && normY >= vp.y && normY <= vp.y + vp.height;

            // Don't interfere with UI clicks
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                isRotating = false;
                isPanning = false;
                return;
            }

            // --- Wheel zoom (works anywhere over map) ---
            if (overMap)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.1f)
                {
                    float newSize = mapCamera.orthographicSize - scroll * 0.08f;
                    mapCamera.orthographicSize = Mathf.Clamp(newSize, 3f, 40f);
                }
            }

            // --- MMB: Rotate ---
            if (Mouse.current.middleButton.wasPressedThisFrame && overMap)
            {
                isRotating = true;
                lastMousePos = mousePos;
            }
            if (Mouse.current.middleButton.isPressed && isRotating)
            {
                Vector2 delta = mousePos - lastMousePos;
                targetYaw += delta.x * 0.3f;
                targetPitch = Mathf.Clamp(targetPitch - delta.y * 0.2f, 10f, 80f);
                lastMousePos = mousePos;
            }
            if (Mouse.current.middleButton.wasReleasedThisFrame)
                isRotating = false;

            // --- RMB: Pan ---
            if (Mouse.current.rightButton.wasPressedThisFrame && overMap)
            {
                isPanning = true;
                lastMousePos = mousePos;
                cameraFollowsCityCenter = false;
                // Resync panOffset from the current focus point — cameraFocus may have moved
                // externally (e.g. SetCameraFocus from clicking a hood) without panOffset
                // tracking it, which would otherwise cause the drag to jump on the next update.
                panOffset = cameraFocus - mapRoot.position;
            }
            if (Mouse.current.rightButton.isPressed && isPanning)
            {
                Vector2 delta = mousePos - lastMousePos;
                // Pan in screen space, converted to world-space offset
                float panSpeed = mapCamera.orthographicSize * 0.005f;
                Vector3 right = mapCamera.transform.right * (-delta.x * panSpeed);
                Vector3 up = mapCamera.transform.up * (-delta.y * panSpeed);
                panOffset += right + up;
                cameraFocus = mapRoot.position + panOffset;
                lastMousePos = mousePos;
            }
            if (Mouse.current.rightButton.wasReleasedThisFrame)
                isPanning = false;

            // --- LMB: Focus on clicked point ---
            if (Mouse.current.leftButton.wasPressedThisFrame && overMap && !isRotating && !isPanning)
            {
                Ray ray = mapCamera.ScreenPointToRay(mousePos);
                if (Physics.Raycast(ray, out RaycastHit hit, 200f))
                {
                    cameraFocus = hit.point;
                    cameraFollowsCityCenter = false;
                    panOffset = hit.point - mapRoot.position;
                }
            }
        }

        private Camera CreateIsometricCamera()
        {
            var camObj = new GameObject("MapCamera");
            camObj.transform.SetParent(transform, false);
            var cam = camObj.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 18f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            cam.rect = new Rect(mapOnRightSide ? 1f - viewportWidth : 0f, viewportYMin, viewportWidth, viewportYMax - viewportYMin);
            cam.depth = 10;

            camObj.transform.position = new Vector3(-20f, 20f, -120f); // Initial — UpdateCameraTransform will override
            camObj.transform.rotation = Quaternion.Euler(35.264f, 45f, 0f); // Initial — will be overridden

            if (FindFirstObjectByType<Light>() == null)
            {
                var lightObj = new GameObject("MapLight");
                lightObj.transform.SetParent(transform, false);
                var light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            return cam;
        }

        public void BuildMap(Dictionary<string, Block> blocks)
        {
            var buildStopwatch = System.Diagnostics.Stopwatch.StartNew();
            cachedBlocks = blocks;

            foreach (var kv in views) if (kv.Value.root != null) Destroy(kv.Value.root);
            views.Clear();

            // Clear old voxel chunks if in raymarch mode
            if (chunkManager != null)
                chunkManager.ClearAllChunks();
            blockAnchors.Clear();
            addressRegistry.Clear();

            // Destroy old roads if any
            var oldRoads = mapRoot.Find("Roads");
            if (oldRoads != null) Destroy(oldRoads.gameObject);

            // Destroy old characters if any
            var oldChars = mapRoot.Find("Characters");
            if (oldChars != null) Destroy(oldChars.gameObject);

            // Destroy old compass if any
            var oldCompass = mapRoot.Find("Compass");
            if (oldCompass != null) Destroy(oldCompass.gameObject);

            if (blocks.Count == 0) return;

            if (cachedLayout == null)
            {
                cachedLayout = LoadCityLayout();
                if (cachedLayout == null)
                {
                    Debug.LogWarning("[CityMap3D] city_layout.json not found — cannot build city.");
                    return;
                }
            }

            int minRow = int.MaxValue, maxRow = int.MinValue, minCol = int.MaxValue, maxCol = int.MinValue;
            foreach (var block in blocks.Values)
            {
                if (block.row < minRow) minRow = block.row;
                if (block.row > maxRow) maxRow = block.row;
                if (block.col < minCol) minCol = block.col;
                if (block.col > maxCol) maxCol = block.col;
            }
            float centerRow = (minRow + maxRow) * 0.5f;
            float centerCol = (minCol + maxCol) * 0.5f;

            float spacing = ComputedSpacing;

            // Cache grid params for road ticker
            cachedMinRow = minRow; cachedMaxRow = maxRow;
            cachedMinCol = minCol; cachedMaxCol = maxCol;
            cachedCenterRow = centerRow; cachedCenterCol = centerCol;
            cachedSpacing = spacing;
            gridCached = true;

            // --- File logger for debugging freezes ---
            string logPath = Path.Combine(Application.persistentDataPath, "buildmap_log.txt");
            void LogBuild(string msg)
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
                Debug.Log(line);
                try { File.AppendAllText(logPath, line + "\n"); } catch { }
            }
            LogBuild($"=== BuildMap START: {blocks.Count} blocks, grid {minRow}-{maxRow} x {minCol}-{maxCol} ===");
            LogBuild($"Log file: {logPath}");

            // PHASE 1: Terrain (roads + ground) — single voxel chunk for raymarch
            if (chunkManager != null)
            {
                var t1 = Stopwatch.StartNew();
                BuildVoxelTerrain(minRow, maxRow, minCol, maxCol, centerRow, centerCol, spacing);
                t1.Stop();
                LogBuild($"PHASE 1 (terrain): {t1.ElapsedMilliseconds}ms");
            }

            // PHASE 2: City blocks — load building .stasset chunks
            // Pre-load all unique .stasset files in parallel (file I/O + packing + AABB on worker threads)
            if (cachedLayout != null && cachedLayout.blocks != null)
            {
                var uniquePaths = new HashSet<string>();
                foreach (var lb in cachedLayout.blocks)
                {
                    if (lb.buildings == null) continue;
                    foreach (var b in lb.buildings)
                    {
                        string fullPath = Path.Combine(Application.streamingAssetsPath, b.stasset);
                        uniquePaths.Add(fullPath);
                    }
                }
                var t2 = Stopwatch.StartNew();
                VoxelChunkManager.PreloadStassetFiles(new List<string>(uniquePaths));
                t2.Stop();
                LogBuild($"PHASE 2A (preload {uniquePaths.Count} files): {t2.ElapsedMilliseconds}ms");
            }

            if (useSectorBaking && cachedLayout != null && chunkManager != null)
            {
                // Sector baking path: merge buildings into sector buffers
                var t3 = Stopwatch.StartNew();
                var sectors = SectorBaker.BakeAllSectors(
                    chunkManager, cachedLayout, blockAnchors,
                    sectorSizeBlocks, buildingsPerBlockRow, BuildingVoxelWidth,
                    sidewalkWidth, roadWidth, voxelSize,
                    (int)centerRow, (int)centerCol, spacing);
                t3.Stop();
                int totalBuildings = 0;
                foreach (var s in sectors) totalBuildings += s.buildingCount;
                LogBuild($"PHASE 2B (sector bake): {t3.ElapsedMilliseconds}ms for {sectors.Count} sectors ({totalBuildings} buildings)");

                // Still create block root GameObjects for labels and game logic
                foreach (var (blockId, block) in blocks)
                {
                    var root = new GameObject($"Block_{blockId}");
                    root.transform.SetParent(mapRoot, false);
                    root.transform.localPosition = new Vector3(
                        (block.col - centerCol) * spacing,
                        0f,
                        -(block.row - centerRow) * spacing);
                }
            }
            else
            {
                // Per-building path (original): 1 draw call per building
                var t3 = Stopwatch.StartNew();
                int blockNum = 0;
                foreach (var (blockId, block) in blocks)
                {
                    blockNum++;
                    if (blockNum % 50 == 0)
                        LogBuild($"  building block {blockNum}/{blocks.Count}...");

                    var root = new GameObject($"Block_{blockId}");
                    root.transform.SetParent(mapRoot, false);
                    root.transform.localPosition = new Vector3(
                        (block.col - centerCol) * spacing,
                        0f,
                        -(block.row - centerRow) * spacing);

                    BuildRaymarchBlock(root.transform, blockId, block, block.row, block.col, minRow, maxRow, minCol, maxCol);
                }
                t3.Stop();
                LogBuild($"PHASE 2B (buildings): {t3.ElapsedMilliseconds}ms for {blocks.Count} blocks");
            }

            // PHASE 3: Compass (flat on ground, next to city)
            CreateCompass(minRow, maxRow, minCol, maxCol, centerRow, centerCol, spacing);

            // PHASE 4: Characters
            SpawnSceneCharacters(blocks, centerRow, centerCol, spacing);

            buildStopwatch.Stop();
            LogBuild($"=== BuildMap COMPLETE: {blocks.Count} blocks in {buildStopwatch.ElapsedMilliseconds}ms ===");
            LogBuild($"Voxel cache: {VoxelChunkManager.PackedCacheHits} hits / {VoxelChunkManager.PackedCacheMisses} misses ({VoxelChunkManager.PackedCacheFiles} unique files)");
        }

        // ====================================================================
        // Road generation between blocks
        // ====================================================================

        // 1920s street name pool
        private static readonly string[] StreetNamesNS = {
            "5th Ave", "Madison", "Park Ave", "Lexington", "Broadway",
            "Amsterdam", "West End"
        };
        private static readonly string[] StreetNamesEW = {
            "42nd St", "47th St", "52nd St", "57th St", "59th St",
            "34th St", "14th St"
        };

        /// <summary>
        /// Anchor positions for each block — exact world-space centers where buildings should snap.
        /// Key format: "r{row}c{col}". Populated by BuildVoxelTerrain, consumed by BuildRaymarchBlock.
        /// </summary>
        private Dictionary<string, Vector3> blockAnchors = new();

        /// <summary>
        /// Address registry — one entry per building, keyed by unique address string.
        /// Format: "{streetNumber} {streetName}" (e.g., "142 5th Ave").
        /// Used for game intel, click-to-select, and precise placement verification.
        /// </summary>
        public class BuildingAddress
        {
            public string address;         // "142 5th Ave"
            public string blockId;         // "block_1"
            public string chunkName;       // "block_1_building_0"
            public Vector3 worldCenter;    // Center of building base in world space
            public Vector3 size;           // World-space dimensions
            public int row, col;           // Grid position
            public int subIndex;           // Building index within block (-1 for single)
        }
        private readonly List<BuildingAddress> addressRegistry = new();
        public IReadOnlyList<BuildingAddress> Addresses => addressRegistry;

        /// <summary>
        /// Generate all terrain (ground tiles + roads) as a single voxel chunk
        /// for the GPU raymarch pipeline. Replaces mesh-based BuildRoadNetwork
        /// in raymarch mode for unified depth compositing.
        /// Also records anchor positions for precise building placement.
        /// </summary>
        private void BuildVoxelTerrain(
            int minRow, int maxRow, int minCol, int maxCol,
            float centerRow, float centerCol, float spacing)
        {
            float groundTile = GroundTileSize;

            // Ensure collision world exists
            if (collisionWorld == null)
                collisionWorld = gameObject.GetComponent<VoxelCollisionWorld>();
            if (collisionWorld == null)
                collisionWorld = gameObject.AddComponent<VoxelCollisionWorld>();

            if (useSplitTerrain)
            {
                // === SPLIT TERRAIN: one small chunk per block ===
                var tGen = Stopwatch.StartNew();
                var terrainChunks = VoxelTerrainBuilder.GeneratePerBlockTerrain(
                    minRow, maxRow, minCol, maxCol,
                    centerRow, centerCol,
                    spacing, groundTile, roadWidth, voxelSize, sidewalkWidth,
                    mapRoot.position,
                    out blockAnchors);
                tGen.Stop();

                var tUpload = Stopwatch.StartNew();
                int totalVoxels = 0;
                int chunkNum = 0;
                foreach (var tc in terrainChunks)
                {
                    chunkNum++;
                    if (chunkNum % 50 == 0)
                        try { File.AppendAllText(Path.Combine(Application.persistentDataPath, "buildmap_log.txt"),
                            $"[{DateTime.Now:HH:mm:ss.fff}]   terrain upload {chunkNum}/{terrainChunks.Count}...\n"); } catch { }

                    chunkManager.LoadChunkFromData(tc.name, tc.data, tc.w, tc.h, tc.d, tc.worldOrigin);
                    collisionWorld.RegisterTerrainChunk(tc.data, tc.w, tc.h, tc.d, tc.worldOrigin, voxelSize);
                    totalVoxels += tc.w * tc.h * tc.d;
                }
                tUpload.Stop();

                string logPath = Path.Combine(Application.persistentDataPath, "buildmap_log.txt");
                try {
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] PHASE 1A (terrain gen parallel): {tGen.ElapsedMilliseconds}ms for {terrainChunks.Count} chunks\n");
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] PHASE 1B (terrain GPU upload + collision): {tUpload.ElapsedMilliseconds}ms for {terrainChunks.Count} chunks, {totalVoxels:N0} voxels\n");
                } catch { }

                Debug.Log($"[CityMap3D] Split terrain: {terrainChunks.Count} chunks, {totalVoxels:N0} total voxels, " +
                    $"{blockAnchors.Count} block anchors");
            }
            else
            {
                // === ORIGINAL: single large terrain chunk ===
                var terrainData = VoxelTerrainBuilder.GenerateCityTerrain(
                    minRow, maxRow, minCol, maxCol,
                    centerRow, centerCol,
                    spacing, groundTile, roadWidth, voxelSize,
                    mapRoot.position,
                    out int w, out int h, out int d, out Vector3 origin,
                    out blockAnchors);

                chunkManager.LoadChunkFromData("terrain", terrainData, w, h, d, origin);
                collisionWorld.RegisterTerrain(terrainData, w, h, d, origin, voxelSize);

                Debug.Log($"[CityMap3D] Voxel terrain generated: {w}x{h}x{d} at world origin {origin} " +
                    $"(covers {w * voxelSize:F1}m × {d * voxelSize:F1}m, {blockAnchors.Count} block anchors)");
            }

            // Log first few anchors for verification
            int logged = 0;
            foreach (var kv in blockAnchors)
            {
                if (logged++ < 3)
                    Debug.Log($"[CityMap3D]   Anchor {kv.Key} → {kv.Value}");
            }
        }

        /// <summary>
        /// Creates a flat compass rose on the ground next to the city for orientation.
        /// Placed to the right of the grid. Shows N/S/E/W with colored arrows and labels.
        /// </summary>
        private void CreateCompass(
            int minRow, int maxRow, int minCol, int maxCol,
            float centerRow, float centerCol, float spacing)
        {
            var compassParent = new GameObject("Compass");
            compassParent.transform.SetParent(mapRoot, false);

            // Place compass to the right of the grid, centered vertically
            float gridRightEdge = (maxCol - centerCol) * spacing + spacing * 0.5f + roadWidth;
            float compassRadius = spacing * 0.8f;
            float cx = gridRightEdge + compassRadius + spacing * 0.5f;
            float cz = 0f;
            float compassY = 0.02f;

            // Base disc
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "CompassDisc";
            disc.transform.SetParent(compassParent.transform, false);
            disc.transform.localScale = new Vector3(compassRadius, 0.01f, compassRadius);
            disc.transform.localPosition = new Vector3(cx, compassY - 0.01f, cz);
            var discRend = disc.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            discRend.material = new Material(shader);
            discRend.material.color = new Color(0.12f, 0.12f, 0.14f, 1f);
            var discCollider = disc.GetComponent<Collider>();
            if (discCollider != null) Destroy(discCollider);

            // Direction labels and arrow colors
            // Unity world: +Z = north, -Z = south, +X = east, -X = west
            var directions = new (string label, float dx, float dz, Color color)[]
            {
                ("N", 0f, compassRadius * 0.65f, new Color(0.9f, 0.2f, 0.2f, 1f)),   // North = +Z = red
                ("S", 0f, -compassRadius * 0.65f, new Color(0.2f, 0.4f, 0.9f, 1f)),  // South = -Z = blue
                ("E", compassRadius * 0.65f, 0f, new Color(0.2f, 0.8f, 0.3f, 1f)),   // East = +X = green
                ("W", -compassRadius * 0.65f, 0f, new Color(0.9f, 0.8f, 0.2f, 1f)),  // West = -X = yellow
            };

            foreach (var (label, dx, dz, color) in directions)
            {
                // Letter label
                var labelObj = new GameObject($"Compass_{label}");
                labelObj.transform.SetParent(compassParent.transform, false);
                labelObj.transform.localPosition = new Vector3(cx + dx, compassY + 0.02f, cz + dz);
                var tmp = labelObj.AddComponent<TextMeshPro>();
                tmp.fontSize = 4f;
                tmp.fontSizeMax = 4f;
                tmp.enableAutoSizing = false;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = color;
                tmp.text = label;
                tmp.fontStyle = FontStyles.Bold;
                // Lay flat on ground
                labelObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                // Arrow line from center toward direction
                var arrowObj = new GameObject($"Arrow_{label}");
                arrowObj.transform.SetParent(compassParent.transform, false);
                var lr = arrowObj.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = 0.08f;
                lr.endWidth = 0.08f;
                lr.startColor = color;
                lr.endColor = color;
                lr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Standard"));
                lr.SetPosition(0, new Vector3(cx, compassY + 0.01f, cz));
                lr.SetPosition(1, new Vector3(cx + dx * 0.85f, compassY + 0.01f, cz + dz * 0.85f));
                lr.useWorldSpace = false;
            }

            // Center dot
            var centerDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            centerDot.name = "CompassCenter";
            centerDot.transform.SetParent(compassParent.transform, false);
            centerDot.transform.localScale = new Vector3(0.15f, 0.05f, 0.15f);
            centerDot.transform.localPosition = new Vector3(cx, compassY, cz);
            var dotRend = centerDot.GetComponent<Renderer>();
            dotRend.material = new Material(shader);
            dotRend.material.color = Color.white;
            var dotCollider = centerDot.GetComponent<Collider>();
            if (dotCollider != null) Destroy(dotCollider);
        }

        // ====================================================================
        // Character spawning (test)
        // ====================================================================

        [Header("Characters")]
        [Tooltip("Character voxel size (smaller than buildings for proper person scale).")]
        [SerializeField] private float characterVoxelSize = 0.02f;

        private void SpawnSceneCharacters(
            Dictionary<string, Block> blocks,
            float centerRow, float centerCol, float spacing)
        {
            // Find player HQ block — fall back to first block if not found
            float blockRow = 0f;
            float blockCol = 0f;
            Block spawnBlock = null;
            foreach (var b in blocks.Values)
            {
                if (b.isPlayerHq)
                {
                    spawnBlock = b;
                    blockRow = b.row;
                    blockCol = b.col;
                    break;
                }
            }
            if (spawnBlock == null)
            {
                // Fallback: use first block
                var first = blocks.Values.GetEnumerator();
                first.MoveNext();
                spawnBlock = first.Current;
                blockRow = spawnBlock.row;
                blockCol = spawnBlock.col;
            }

            Debug.Log($"[CityMap3D] Spawning character at {spawnBlock.id} (r{blockRow} c{blockCol})");

            float bx = (blockCol - centerCol) * spacing;
            float bz = -(blockRow - centerRow) * spacing;

            // Place character at block center — simple and reliable for any city size
            float px = bx;
            float pz = bz;

            // Y: terrain is 2 voxels thick at voxelSize=0.1 → top surface at Y=0.2
            float groundY = voxelSize * 2f; // 0.2

            // SteelTide VoxelObject approach: create a standalone GameObject with VoxelCharacter component.
            var charParent = mapRoot.Find("Characters");
            if (charParent == null)
            {
                var cp = new GameObject("Characters");
                cp.transform.SetParent(mapRoot, false);
                charParent = cp.transform;
            }

            var charObj = new GameObject("Character_Hoodlum_0");
            charObj.transform.SetParent(charParent, false);
            var vc = charObj.AddComponent<VoxelCharacter>();
            vc.assetFileName = "character_hoodlum_0.stasset";
            vc.voxelSize = characterVoxelSize;
            vc.chunkManager = chunkManager;
            vc.collisionWorld = collisionWorld;
            // Local position relative to mapRoot — start above ground, gravity will pull down
            vc.centerPosition = new Vector3(px, groundY + 0.5f, pz);
            vc.useWorldPosition = false;
            vc.showGroundProbe = true; // debug — turn off after verifying

            SpawnedCharacter = vc;

            Debug.Log($"[CityMap3D] Spawned VoxelCharacter hoodlum at ({px:F2}, {pz:F2}), groundY={groundY}");

            // Spawn vehicle test spawner (auto-spawns a car near HQ on Start)
            if (FindFirstObjectByType<VehicleTestSpawner>() == null)
            {
                var vehObj = new GameObject("VehicleTestSpawner");
                vehObj.transform.SetParent(mapRoot, false);
                var vts = vehObj.AddComponent<VehicleTestSpawner>();
                Debug.Log("[CityMap3D] Added VehicleTestSpawner to scene — will auto-spawn vehicle near HQ.");
            }
        }

        private void BuildRaymarchBlock(Transform root, string blockId, Block block,
            int row, int col, int minRow, int maxRow, int minCol, int maxCol)
        {
            // All-voxel mode: ground tiles and roads are generated as a single
            // voxel terrain chunk by BuildVoxelTerrain(). Here we only load
            // building .stasset chunks, snapping to anchor positions.

            // Look up the anchor position recorded during terrain generation
            string anchorKey = $"r{row}c{col}";
            Vector3 anchorPos = root.position; // fallback to block root position
            if (blockAnchors != null && blockAnchors.TryGetValue(anchorKey, out var anchored))
            {
                anchorPos = anchored;
            }
            else
            {
                Debug.LogWarning($"[CityMap3D] No anchor found for {anchorKey} (block {blockId}), using root.position");
            }

            CityLayoutBlock layoutBlock = null;
            if (cachedLayout != null && cachedLayout.blocks != null)
            {
                foreach (var lb in cachedLayout.blocks)
                {
                    if (lb.block_id == blockId) { layoutBlock = lb; break; }
                }
            }

            // Load buildings as voxel chunks, CENTERED on anchor positions
            float maxBuildingHeight = 0f;
            if (layoutBlock != null && layoutBlock.buildings != null)
            {
                int buildingCount = layoutBlock.buildings.Length;
                if (buildingCount == 1)
                {
                    string stasset = layoutBlock.buildings[0].stasset;
                    string fullPath = Path.Combine(Application.streamingAssetsPath, stasset);
                    float bh = GetBuildingHeight(stasset);
                    if (bh > maxBuildingHeight) maxBuildingHeight = bh;

                    string chunkName = $"{blockId}_building";
                    // LoadChunkCentered offsets so building CENTER aligns with anchorPos
                    var footprint = chunkManager.LoadChunkCentered(chunkName, fullPath, anchorPos);
                    if (footprint != null)
                    {
                        RegisterAddress(blockId, chunkName, anchorPos, footprint.size, row, col, -1);
                        // Suppress per-building centered log (too many at scale)
                    }
                }
                else
                {
                    // Multi-building block: check for full-block-sized assets
                    // In Gangsters, tenement/industrial blocks occupy the entire block.
                    // Small commercial buildings share the block in a sub-grid.
                    bool hasFullBlockBuilding = false;
                    for (int i = 0; i < buildingCount; i++)
                    {
                        string stasset = layoutBlock.buildings[i].stasset;
                        string fullPath = Path.Combine(Application.streamingAssetsPath, stasset);
                        var (vw, vh, vd) = VoxelChunkManager.GetStassetDimensions(fullPath);
                        if (vw >= VoxelChunkManager.FullBlockVoxelThreshold ||
                            vd >= VoxelChunkManager.FullBlockVoxelThreshold)
                        {
                            hasFullBlockBuilding = true;
                            break;
                        }
                    }

                    if (hasFullBlockBuilding)
                    {
                        // Place the first full-block building centered on the anchor.
                        // In Gangsters, a tenement block occupies all slots — no neighbors.
                        for (int i = 0; i < buildingCount; i++)
                        {
                            string stasset = layoutBlock.buildings[i].stasset;
                            string fullPath = Path.Combine(Application.streamingAssetsPath, stasset);
                            var (vw, vh, vd) = VoxelChunkManager.GetStassetDimensions(fullPath);

                            if (vw >= VoxelChunkManager.FullBlockVoxelThreshold ||
                                vd >= VoxelChunkManager.FullBlockVoxelThreshold)
                            {
                                float bh = GetBuildingHeight(stasset);
                                if (bh > maxBuildingHeight) maxBuildingHeight = bh;

                                string chunkName = $"{blockId}_building_{i}";
                                var footprint = chunkManager.LoadChunkCentered(chunkName, fullPath, anchorPos);
                                if (footprint != null)
                                {
                                    RegisterAddress(blockId, chunkName, anchorPos, footprint.size, row, col, i);
                                }
                                break; // Only place one full-block building
                            }
                        }
                    }
                    else
                    {
                        int cols = Mathf.CeilToInt(Mathf.Sqrt(buildingCount));
                        int rows = Mathf.CeilToInt((float)buildingCount / cols);
                        float subSize = GroundTileSize * 0.9f / cols;
                        float subOffset = GroundTileSize * 0.45f - subSize * 0.5f;
                        float buildingMeshWidth = BuildingVoxelWidth * voxelSize;

                        for (int i = 0; i < buildingCount; i++)
                        {
                            int r = i / cols;
                            int c = i % cols;
                            float px = -subOffset + c * subSize;
                            float pz = -subOffset + r * subSize;

                            string stasset = layoutBlock.buildings[i].stasset;
                            string fullPath = Path.Combine(Application.streamingAssetsPath, stasset);
                            float scale = subSize / buildingMeshWidth;
                            float bh = GetBuildingHeight(stasset) * scale;
                            if (bh > maxBuildingHeight) maxBuildingHeight = bh;

                            // Sub-building center = anchor + local offset within block
                            Vector3 buildingCenter = anchorPos + new Vector3(px, 0f, pz);
                            string chunkName = $"{blockId}_building_{i}";
                            var footprint = chunkManager.LoadChunkCentered(chunkName, fullPath, buildingCenter);
                            if (footprint != null)
                            {
                                RegisterAddress(blockId, chunkName, buildingCenter, footprint.size, row, col, i);
                            }
                        }
                    }
                }
            }

            // Block label (same as mesh mode)
            TextMeshPro tmp = null;
            if (showBlockLabels)
            {
                float labelY = Mathf.Max(maxBuildingHeight + 0.3f, 0.5f);
                var labelObj = new GameObject("Label");
                labelObj.transform.SetParent(root, false);
                labelObj.transform.localPosition = new Vector3(0f, labelY, 0f);
                tmp = labelObj.AddComponent<TextMeshPro>();
                tmp.fontSize = labelFontSize;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = labelColor;
                tmp.text = block.name;
                labelObj.transform.rotation = mapCamera.transform.rotation;
            }

            // Create a hidden ground cube for click detection (no renderer — just collider)
            var ground = new GameObject("GroundCollider");
            ground.transform.SetParent(root, false);
            ground.transform.localScale = new Vector3(GroundTileSize, 0.02f, GroundTileSize);
            ground.transform.localPosition = Vector3.zero;
            var groundCollider = ground.AddComponent<BoxCollider>();
            groundCollider.size = Vector3.one;

            views[blockId] = new BlockView3D
            {
                root = root.gameObject,
                groundCollider = ground,
                label = tmp,
                blockId = blockId
            };
        }

        /// <summary>
        /// Register a building address in the address registry.
        /// Generates a 1920s-style street address from the grid position.
        /// </summary>
        private void RegisterAddress(string blockId, string chunkName,
            Vector3 worldCenter, Vector3 size, int row, int col, int subIndex)
        {
            // Generate street number: base 100 + row*20 + col*10 + subIndex
            int streetNumber = 100 + row * 20 + col * 10 + (subIndex < 0 ? 0 : subIndex + 1);

            // Pick street name based on grid orientation
            // Even rows get EW street names, odd rows get NS avenue names
            string streetName;
            if (row % 2 == 0)
                streetName = StreetNamesEW[Mathf.Abs(row) % StreetNamesEW.Length];
            else
                streetName = StreetNamesNS[Mathf.Abs(col) % StreetNamesNS.Length];

            string address = $"{streetNumber} {streetName}";

            var entry = new BuildingAddress
            {
                address = address,
                blockId = blockId,
                chunkName = chunkName,
                worldCenter = worldCenter,
                size = size,
                row = row,
                col = col,
                subIndex = subIndex
            };
            addressRegistry.Add(entry);

            // Suppress per-address registration log (too many at scale)
        }

        private readonly Dictionary<string, float> heightCache = new();

        private float GetBuildingHeight(string relativePath)
        {
            if (heightCache.TryGetValue(relativePath, out float cached))
                return cached;

            string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
            var voxels = StAssetReader.LoadVoxels(fullPath);
            float h = voxels == null ? 0.5f : voxels.GetLength(1) * voxelSize;
            heightCache[relativePath] = h;
            return h;
        }

        private CityLayout LoadCityLayout()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "city_layout.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[CityMap3D] city_layout.json not found at {path}");
                return null;
            }

            string json = File.ReadAllText(path);
            var layout = JsonUtility.FromJson<CityLayout>(json);
            if (layout == null || layout.blocks == null || layout.blocks.Length == 0)
            {
                Debug.LogWarning("[CityMap3D] city_layout.json is empty or invalid.");
                return null;
            }

            Debug.Log($"[CityMap3D] Loaded city_layout.json: {layout.blocks.Length} blocks");
            return layout;
        }

        public void UpdateBlock(string blockId, Color color, string labelText, bool selected)
        {
            if (!views.TryGetValue(blockId, out var view)) return;
            if (view.label != null)
                view.label.text = labelText;
        }

        /// <summary>
        /// Each frame, check what road/intersection/block the mouse is over
        /// and update the road ticker UI text.
        /// </summary>
        void UpdateRoadTicker()
        {
            if (roadTickerText == null) return;

            if (Mouse.current == null)
            {
                roadTickerText.text = "—";
                return;
            }

            Vector2 mousePos = Mouse.current.position.ReadValue();
            float normX = mousePos.x / Screen.width;
            float normY = mousePos.y / Screen.height;

            var vp = mapCamera.rect;
            if (normX < vp.x || normX > vp.x + vp.width || normY < vp.y || normY > vp.y + vp.height)
            {
                roadTickerText.text = "—";
                return;
            }

            if (!gridCached)
            {
                roadTickerText.text = "Loading city...";
                return;
            }

            // Convert screen mouse to world point on the terrain plane (Y=0)
            Ray ray = mapCamera.ScreenPointToRay(mousePos);
            // Intersect with horizontal plane at mapRoot.position.y
            float planeY = mapRoot.position.y;
            if (Mathf.Abs(ray.direction.y) < 0.001f)
            {
                roadTickerText.text = "—";
                return;
            }
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t < 0)
            {
                roadTickerText.text = "—";
                return;
            }
            Vector3 worldHit = ray.origin + ray.direction * t;

            // Convert to city-local (relative to mapRoot)
            Vector3 local = worldHit - mapRoot.position;

            // City coordinate system:
            //   Block (col,row) center: X = (col - centerCol) * spacing, Z = -(row - centerRow) * spacing
            //   Horizontal roads (EW): Z = -(minRow + i - 0.5 - centerRow) * spacing, for i = 0..ewCount
            //   Vertical roads (NS):   X = (minCol + i - 0.5 - centerCol) * spacing, for i = 0..nsCount
            //   Road half-width = roadWidth * 0.5
            //   Block half-size = GroundTileSize * 0.5

            float halfRoad = roadWidth * 0.5f;
            float halfTile = GroundTileSize * 0.5f;
            float spacing = cachedSpacing;

            // Find nearest horizontal road (EW street)
            // Road i is at Z = -(minRow + i - 0.5 - centerRow) * spacing
            // Invert: i = -Z/spacing - minRow + 0.5 + centerRow
            float ewIdx_f = -local.z / spacing - cachedMinRow + 0.5f + cachedCenterRow;
            int ewIdx = Mathf.RoundToInt(ewIdx_f);
            float ewRoadZ = -(cachedMinRow + ewIdx - 0.5f - cachedCenterRow) * spacing;
            bool onEW = Mathf.Abs(local.z - ewRoadZ) <= halfRoad;

            // Find nearest vertical road (NS avenue)
            // Road i is at X = (minCol + i - 0.5 - centerCol) * spacing
            // Invert: i = X/spacing - minCol + 0.5 + centerCol
            float nsIdx_f = local.x / spacing - cachedMinCol + 0.5f + cachedCenterCol;
            int nsIdx = Mathf.RoundToInt(nsIdx_f);
            float nsRoadX = (cachedMinCol + nsIdx - 0.5f - cachedCenterCol) * spacing;
            bool onNS = Mathf.Abs(local.x - nsRoadX) <= halfRoad;

            // Determine what block the cursor is over (if not on a road)
            float blockX_f = local.x / spacing + cachedCenterCol;
            float blockZ_f = -local.z / spacing + cachedCenterRow;
            int blockCol = Mathf.RoundToInt(blockX_f);
            int blockRow = Mathf.RoundToInt(blockZ_f);
            float blockCenterX = (blockCol - cachedCenterCol) * spacing;
            float blockCenterZ = -(blockRow - cachedCenterRow) * spacing;
            bool onBlock = !onEW && !onNS &&
                Mathf.Abs(local.x - blockCenterX) <= halfTile &&
                Mathf.Abs(local.z - blockCenterZ) <= halfTile;

            if (onEW && onNS)
            {
                // Intersection
                string ewName = StreetNamesEW[((ewIdx % StreetNamesEW.Length) + StreetNamesEW.Length) % StreetNamesEW.Length];
                string nsName = StreetNamesNS[((nsIdx % StreetNamesNS.Length) + StreetNamesNS.Length) % StreetNamesNS.Length];
                roadTickerText.text = $"Intersection\n{ewName} & {nsName}";
            }
            else if (onEW)
            {
                string ewName = StreetNamesEW[((ewIdx % StreetNamesEW.Length) + StreetNamesEW.Length) % StreetNamesEW.Length];
                roadTickerText.text = $"On {ewName}";
            }
            else if (onNS)
            {
                string nsName = StreetNamesNS[((nsIdx % StreetNamesNS.Length) + StreetNamesNS.Length) % StreetNamesNS.Length];
                roadTickerText.text = $"On {nsName}";
            }
            else if (onBlock)
            {
                // Look up block by row/col
                string blockName = "Block";
                if (cachedBlocks != null)
                {
                    foreach (var blk in cachedBlocks.Values)
                    {
                        if (blk.row == blockRow && blk.col == blockCol)
                        {
                            blockName = blk.name;
                            break;
                        }
                    }
                }
                roadTickerText.text = $"{blockName}\n(r{blockRow} c{blockCol})";
            }
            else
            {
                roadTickerText.text = "Out of bounds";
            }
        }

        private void HandleClick()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            float normX = mousePos.x / Screen.width;
            float normY = mousePos.y / Screen.height;

            var vp = mapCamera.rect;
            if (normX < vp.x || normX > vp.x + vp.width || normY < vp.y || normY > vp.y + vp.height)
                return;

            Ray ray = mapCamera.ScreenPointToRay(mousePos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                Transform t = hit.collider.transform;
                while (t != null)
                {
                    foreach (var (blockId, view) in views)
                    {
                        if (view.root == t.gameObject ||
                            view.groundCollider == hit.collider.gameObject)
                        {
                            OnBlockClicked?.Invoke(blockId);
                            return;
                        }
                    }
                    t = t.parent;
                }
            }
        }

        // --- Ortho HUD (perf + LOD debug) ---
        private GUIStyle hudLabelStyle;
        private GUIStyle hudBgStyle;
        private Texture2D hudBgTex;

        void OnGUI()
        {
            if (chunkManager == null || !chunkManager.ShowOrthoHud) return;

            if (hudLabelStyle == null)
            {
                hudLabelStyle = new GUIStyle(GUI.skin.label);
                hudLabelStyle.fontSize = 13;
                hudLabelStyle.richText = true;
                hudLabelStyle.normal.textColor = new Color(0.85f, 1f, 0.85f);
            }
            if (hudBgTex == null)
            {
                hudBgTex = new Texture2D(1, 1);
                hudBgTex.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.06f, 0.85f));
                hudBgTex.Apply();
            }
            if (hudBgStyle == null)
            {
                hudBgStyle = new GUIStyle(GUI.skin.box);
                hudBgStyle.normal.background = hudBgTex;
            }

            float fps = 1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f);
            float w = 320f, h = 200f;
            GUILayout.BeginArea(new Rect(10, 10, w, h), hudBgStyle);
            GUILayout.Label("<b>ORTHO RENDER HUD</b>", hudLabelStyle);
            GUILayout.Label($"FPS: {fps:F0}  |  OrthoSize: {chunkManager.CameraOrthoSize:F1}", hudLabelStyle);
            GUILayout.Label($"Res: {chunkManager.PerfTotalChunks} total / {chunkManager.PerfActiveChunks} active / {chunkManager.PerfDrawnChunks} drawn / {chunkManager.InstancedCharacterCount} instanced / {chunkManager.BakedSectorCount} sectors ({chunkManager.BakedSectorBuildingCount} baked)", hudLabelStyle);
            GUILayout.Label("", hudLabelStyle);
            GUILayout.Label("<b>LOD TIERS</b>", hudLabelStyle);
            GUILayout.Label($"  <color=#2ee62e>Near</color>: {chunkManager.PerfLodNear}  <color=#e6e62e>Mid</color>: {chunkManager.PerfLodMid}  <color=#e68a2e>Far</color>: {chunkManager.PerfLodFar}  <color=#e62e2e>Ultra</color>: {chunkManager.PerfLodUltra}  Culled: {chunkManager.PerfLodCulled}", hudLabelStyle);
            GUILayout.Label($"  ScreenRatio  min:{chunkManager.PerfMinScreenRatio:F4}  max:{chunkManager.PerfMaxScreenRatio:F4}  avg:{chunkManager.PerfAvgScreenRatio:F4}", hudLabelStyle);
            GUILayout.Label("", hudLabelStyle);
            GUILayout.Label($"CPU: cull={chunkManager.CpuCullMs:F2}ms  draw={chunkManager.CpuDrawMs:F2}ms  total={chunkManager.CpuTotalMs:F2}ms", hudLabelStyle);
            GUILayout.Label($"Render: {chunkManager.RenderWidth}x{chunkManager.RenderHeight}  Proxy: {chunkManager.GetUseProxyRender()}", hudLabelStyle);
            GUILayout.EndArea();
        }
    }

    [Serializable]
    public class CityLayout
    {
        public CityLayoutBlock[] blocks;
    }

    [Serializable]
    public class CityLayoutBlock
    {
        public string block_id;
        public string block_name;
        public int row;
        public int col;
        public CityLayoutBuilding[] buildings;
    }

    [Serializable]
    public class CityLayoutBuilding
    {
        public string type;
        public string stasset;
        public int slot;
    }

    public class BlockView3D
    {
        public GameObject root;
        public GameObject groundCollider;
        public TextMeshPro label;
        public string blockId;
    }
}
