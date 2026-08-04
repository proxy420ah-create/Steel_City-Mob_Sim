using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using System;
using System.Collections.Generic;
using System.IO;

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

        private readonly Dictionary<string, BlockView3D> views = new();
        private Transform mapRoot;
        private Dictionary<string, Block> cachedBlocks;
        private CityLayout cachedLayout;

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
        }

        // --- Mouse camera state ---
        private bool isRotating;
        private bool isPanning;
        private Vector2 lastMousePos;
        private Vector3 panOffset = Vector3.zero;

        void Update()
        {
            HandleMouseCamera();
            HandleClick();
            UpdateCameraTransform();

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

            // PHASE 1: Terrain (roads + ground) — single voxel chunk for raymarch
            if (chunkManager != null)
            {
                BuildVoxelTerrain(minRow, maxRow, minCol, maxCol, centerRow, centerCol, spacing);
            }

            // PHASE 2: City blocks — load building .stasset chunks
            foreach (var (blockId, block) in blocks)
            {
                var root = new GameObject($"Block_{blockId}");
                root.transform.SetParent(mapRoot, false);
                root.transform.localPosition = new Vector3(
                    (block.col - centerCol) * spacing,
                    0f,
                    -(block.row - centerRow) * spacing);

                BuildRaymarchBlock(root.transform, blockId, block, block.row, block.col, minRow, maxRow, minCol, maxCol);
            }

            // PHASE 3: Compass (flat on ground, next to city)
            CreateCompass(minRow, maxRow, minCol, maxCol, centerRow, centerCol, spacing);

            // PHASE 4: Characters
            SpawnSceneCharacters(blocks, centerRow, centerCol, spacing);
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

            var terrainData = VoxelTerrainBuilder.GenerateCityTerrain(
                minRow, maxRow, minCol, maxCol,
                centerRow, centerCol,
                spacing, groundTile, roadWidth, voxelSize,
                mapRoot.position,  // Pass the city offset so terrain aligns with buildings
                out int w, out int h, out int d, out Vector3 origin,
                out blockAnchors);

            chunkManager.LoadChunkFromData("terrain", terrainData, w, h, d, origin);

            Debug.Log($"[CityMap3D] Voxel terrain generated: {w}x{h}x{d} at world origin {origin} " +
                $"(covers {w * voxelSize:F1}m × {d * voxelSize:F1}m, {blockAnchors.Count} block anchors)");

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
        [SerializeField] private float characterVoxelSize = 0.015f;

        private void SpawnSceneCharacters(
            Dictionary<string, Block> blocks,
            float centerRow, float centerCol, float spacing)
        {
            var charParent = new GameObject("Characters");
            charParent.transform.SetParent(mapRoot, false);

            // Barber shop is block_4 at row=1, col=0
            float blockRow = 1f;
            float blockCol = 0f;
            float bx = (blockCol - centerCol) * spacing;
            float bz = -(blockRow - centerRow) * spacing;

            // Barber building is 32v×20v×34v (X×Y×Z) at voxelSize=0.1
            // Building footprint: 3.2 × 3.4 world units
            // Building half-width X = 1.6, half-depth Z = 1.7
            // Ground tile = 11.6, outer edge = 5.8
            // Sidewalk band: from building edge to ground tile edge
            // South sidewalk midpoint Z: building half (1.7) + sidewalk half (0.5) = 2.2 from center
            // East sidewalk midpoint X: building half (1.6) + sidewalk half (0.5) = 2.1 from center
            float bHalfX = 1.6f;
            float bHalfZ = 1.7f;
            float sidewalkHalf = sidewalkWidth * 0.5f;

            // Place characters around the barber shop on sidewalks on 3 sides
            // South side (front), East side, West side — leave north (back) empty
            var spawns = new (string asset, float dx, float dz, float rotY)[]
            {
                // South sidewalk (front of barber) — facing north toward building
                ("character_hoodlum_0.stasset",  0f,                bHalfZ + sidewalkHalf, 0f),
                ("character_hoodlum_0.stasset",  1.2f,              bHalfZ + sidewalkHalf, 0f),
                ("character_civilian_0.stasset", -1.2f,             bHalfZ + sidewalkHalf, 0f),
                // East sidewalk — facing west toward building
                ("character_civilian_0.stasset", bHalfX + sidewalkHalf,  0.5f, 270f),
                // West sidewalk — facing east toward building
                ("character_hoodlum_overcoat_0.stasset", -(bHalfX + sidewalkHalf),  0.5f, 90f),
                // South sidewalk corner — police on patrol
                ("character_police_0.stasset",   bHalfX + sidewalkHalf, bHalfZ + sidewalkHalf, 315f),
            };

            int placed = 0;
            for (int i = 0; i < spawns.Length; i++)
            {
                var (asset, dx, dz, rotY) = spawns[i];
                string charPath = Path.Combine(Application.streamingAssetsPath, "voxel_buildings", asset);
                if (!File.Exists(charPath))
                {
                    Debug.LogWarning($"[CityMap3D] {asset} not found.");
                    continue;
                }

                var mesh = StAssetReader.LoadAsMesh(charPath, characterVoxelSize);
                if (mesh == null || mesh.vertexCount == 0) continue;

                var go = new GameObject($"Char_{i}_{Path.GetFileNameWithoutExtension(asset)}");
                go.transform.SetParent(charParent.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rend = go.AddComponent<MeshRenderer>();
                var shader = Shader.Find("SteelCity/VoxelVertexColor")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
                rend.material = new Material(shader);

                // Position relative to barber block center
                float px = bx + dx;
                float pz = bz + dz;
                go.transform.localPosition = new Vector3(px, 0.02f, pz);
                go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                placed++;
            }

            Debug.Log($"[CityMap3D] Spawned {placed} characters on sidewalks around barber shop at ({bx:F1}, {bz:F1})");
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
                        Debug.Log($"[CityMap3D] Building '{chunkName}' centered at {anchorPos}, footprint {footprint.size}");
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

            Debug.Log($"[CityMap3D] Address registered: {address} → {chunkName} at {worldCenter} ({size.x:F1}×{size.y:F1}×{size.z:F1}m)");
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
