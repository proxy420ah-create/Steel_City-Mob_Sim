using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace SteelCity.Sim
{
    /// <summary>
    /// Simple int3 replacement (avoids Unity.Mathematics dependency).
    /// </summary>
    public struct VoxelInt3
    {
        public int x, y, z;
        public VoxelInt3(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
    }

    /// <summary>
    /// Manages voxel chunks for GPU raymarch rendering.
    /// Each chunk = one .stasset building uploaded to a ComputeBuffer.
    /// Dispatches MobSimVoxelRaymarch.compute per visible chunk.
    /// Composites results into a render texture displayed via a fullscreen quad.
    /// </summary>
    public class VoxelChunkManager : MonoBehaviour
    {
        [Header("Compute Shader")]
        [SerializeField] private ComputeShader raymarchShader;

        [Header("Proxy Render (Fragment Shader Path)")]
        [Tooltip("If true, use proxy-box fragment shader instead of compute dispatches. 5-10x faster for small on-screen volumes.")]
        [SerializeField] private bool useProxyRender = true;
        [SerializeField] private Shader proxyShader;
        private Material proxyMaterial;
        // Dedicated material for instanced group draws (characters/vehicles).
        // Sharing proxyMaterial between non-instanced chunk DrawMesh calls and
        // instanced DrawMeshInstanced calls corrupts the per-draw instancing
        // constant buffer on this pipeline — instance matrices arrive stale or
        // not at all. sectorMaterial already proves the dedicated pattern works.
        private Material groupMaterial;
        private Mesh proxyCubeMesh;
        private RenderTexture proxyRT;

        [Header("GPU Compute Pose (Forward-Transform Animation)")]
        [Tooltip("Compute shader for forward-transforming rest voxels into posed voxels on GPU.")]
        [SerializeField] private ComputeShader poseComputeShader;
        private int kernelCSClear;
        private int kernelCSPose;

        // Default ON — TestPlanesAABB false-culls the huge terrain-sector AABBs at
        // oblique camera angles (block-sized terrain voids confirmed in playtest).
        // Only ~9 sectors exist, so culling buys nothing. Re-enable only with a
        // conservative bounds fix (see docs/known_issues/rendering/SECTOR_FRUSTUM_CULL.md).
        [SerializeField] private bool disableSectorCulling = true;

        // Coverage-aware dynamic resolution tuning
        private const float CoverageHeuristicScale = 0.85f; // heuristic to map sum(r^2) → 0..1 coverage
        private const float LowCoverageThreshold = 0.20f;    // below this, switch to half-res (small-coverage optimization)
        // Drawn-based dynamic resolution (addresses cliff at ~85 drawn)
        private const int DrawnHalfResThreshold = 80;        // at or above this drawn count, prefer half-res
        private const int DrawnHalfResReturn = 74;           // drop back to full-res when drawn falls below this
        // When user manually sets resolution via R key, suspend auto-resolution
        private bool manualResolutionOverride = false;

        [Header("Render Settings")]
        [Tooltip("Voxel size in world units (should match CityMap3D.voxelSize).")]
        [SerializeField] private float voxelSize = 0.05f;
        [Tooltip("Max DDA steps per ray (safety cap). 264 covers terrain chunks at voxelSize=0.05; buildings need ~192.")]
        [SerializeField] private int maxSteps = 264;
        [Tooltip("Render texture resolution scale relative to screen.")]
        [SerializeField] private float resolutionScale = 0.5f;
        [Tooltip("Background color when rays miss all chunks. Alpha=0 for transparent (shows scene behind).")]
        [SerializeField] private Color backgroundColor = new(0f, 0f, 0f, 0f);
        [Tooltip("Max world-space distance to render chunks. 0 = no limit. Set ~40-60 for Working mode perf.")]
        [SerializeField] private float maxRenderDistance = 50f;
        [Header("Screen-Space LOD (Unified — works for ortho + perspective)")]
        [Tooltip("Screen ratio threshold for Near tier (full quality). Chunk bounding sphere / view extent. 0.15 = 15% of screen.")]
        [SerializeField] private float lodNearScreenRatio = 0.15f;
        [Tooltip("Screen ratio threshold for Mid tier (cheap shading). 0.05 = 5% of screen.")]
        [SerializeField] private float lodMidScreenRatio = 0.05f;
        [Tooltip("Screen ratio threshold for Far tier (unlit, reduced steps). 0.02 = 2% of screen.")]
        [SerializeField] private float lodFarScreenRatio = 0.02f;
        [Tooltip("Screen ratio threshold for Ultra tier (minimal steps). 0.005 = 0.5% of screen.")]
        [SerializeField] private float lodUltraScreenRatio = 0.005f;
        [Tooltip("Screen ratio below which chunks are culled entirely. 0.002 = 0.2% of screen (sub-pixel).")]
        [SerializeField] private float lodCullScreenRatio = 0.002f;
        [Tooltip("Ray steps for Mid LOD tier.")]
        [SerializeField] private int lodMidSteps = 48;
        [Tooltip("Ray steps for Far LOD tier.")]
        [SerializeField] private int lodFarSteps = 24;
        [Tooltip("Ray steps for Ultra LOD tier.")]
        [SerializeField] private int lodUltraFarSteps = 12;
        [Tooltip("Enable cheap shading at Mid tier and beyond (skips smooth-normal blend, 6 fewer buffer reads per pixel).")]
        [SerializeField] private bool enableCheapShadingLod = true;
        [Tooltip("Enable unlit fast-path at Far tier and beyond (skips GetLighting + shadow-ray setup — biggest GPU perf win).")]
        [SerializeField] private bool enableUnlitLod = true;
        [Tooltip("Log per-chunk LOD tier once per second when true.")]
        [SerializeField] private bool debugLodTiers = false;
        [Tooltip("DEBUG: force ALL chunks to Ultra LOD regardless of screen size.")]
        [SerializeField] private bool debugForceAllBuildingsUltraLod = false;
        [Tooltip("DEBUG: solid-tint chunks by LOD tier (green=near, yellow=mid, orange=far, red=ultra) so tiers are visually obvious.")]
        [SerializeField] private bool debugColorizeLodTiers = false;
        [Tooltip("Debug: color each glass pixel by accumulated pane layers (cyan=1, green=2, yellow=3, red=4+). Opaque surfaces render black.")]
        public bool debugGlassLayers = false;
        [Tooltip("Show perf + LOD HUD overlay on the ortho/planning camera. Disabled — use DebugHUDManager instead.")]
        [SerializeField] private bool showOrthoHud = false;

        [Header("Glass / Transparency")]
        [Tooltip("Auto Glass (mat 147) tint — picker alpha channel = opacity (0 = invisible, 1 = opaque).")]
        public Color autoGlassColor = new Color(0.59f, 0.67f, 0.73f, 0.6f);
        [Tooltip("Window Glass (mat 112) tint — picker alpha channel = opacity (0 = invisible, 1 = opaque).")]
        public Color windowGlassColor = new Color(0.45f, 0.55f, 0.65f, 0.6f);
        [Tooltip("Storefront Glass (mat 114) tint — picker alpha channel = opacity (0 = invisible, 1 = opaque).")]
        public Color storefrontGlassColor = new Color(0.55f, 0.65f, 0.70f, 0.5f);
        [Tooltip("Sun-glint strength on glass surfaces (specular highlight).")]
        [Range(0f, 2f)] public float glassSpecular = 0.5f;
        [Tooltip("Max glass surfaces a ray blends through before falling back to opaque.")]
        [Range(1, 8)] public int glassMaxLayers = 4;
        [Tooltip("EXPERIMENTAL: two-pass glass composite — an opaque pass renders interiors through glass holes, then a blended glass pass tints over it. Fixes cross-volume tint, see-through into rooms, and objects sandwiched between glass and walls. Toggle off for the legacy single-pass renderer.")]
        public bool useGlassCompositePass = true;
        [Tooltip("Material ids (max 8) forced into the glass path even when their palette alpha is opaque — e.g. an emissive or solid id that should still transmit.")]
        public List<int> forcedGlassMaterialIds = new();
        [Tooltip("Transmission alpha used for forced-glass materials whose palette alpha is opaque (lower = more see-through).")]
        [Range(0f, 1f)] public float flaggedGlassAlpha = 0.6f;

        public enum IterVisMode { Off = 0, IterationHeat = 1, StepChannels = 2, MissWaste = 3 }
        [Header("Raymarch Debug")]
        [Tooltip("V key. IterationHeat: blue<80 green~160 red>160 | StepChannels: R=voxel steps, B=shadow steps | MissWaste: heat on no-hit rays only")]
        public IterVisMode iterVisMode = IterVisMode.Off;

        [Header("Debug")]
        [SerializeField] private bool debugChunkBounds = false;

        // --- Chunk data structure ---
        private class VoxelChunk
        {
            public string name;
            public GameObject hostObject;    // World-space GameObject (like Steel Tide's VoxelObject)
            public ComputeBuffer voxelBuffer;    // uint[] packed voxels
            public ComputeBuffer paneBuffer;     // uint[] per-voxel pane group ids (0 = non-glass / ungrouped)
            public ComputeBuffer occBuffer;      // uint[] macro-cell occupancy bitmask (M=4) for the DDA empty-skip
            public uint[] occData;               // CPU copy for incremental updates
            public int occWords;                 // words in occData
            public ComputeBuffer materialBuffer; // float4[] color lookup (shared)
            public ComputeBuffer tintBuffer;     // float4[] per-material tint (per-chunk)
            public MaterialPropertyBlock cachedPropBlock; // reused across frames to avoid GC
            public Matrix4x4 lastDrawTRS;            // proxy TRS from the opaque draw — reused by the glass composite pass
            public VoxelInt3 dims;               // voxel dimensions
            public Vector3 worldOffset;          // cached world-space position
            public Quaternion rotation;            // cached world-space rotation
            public float voxelSize;              // per-chunk voxel size (buildings=0.1, characters=0.01)
            public bool active;
            // Tight AABB of solid voxels (in voxel coords, inclusive)
            public int tightMinX, tightMinY, tightMinZ;
            public int tightMaxX, tightMaxY, tightMaxZ;
            public bool hasSolid;               // false if chunk is entirely air
            // If true, skip frustum + screen-ratio culling for this chunk (always draw).
            // Used by debug/test volumes (e.g. ForwardTransformTestRig) that need to match
            // the "always visible" behavior of the instanced character render path.
            public bool skipCulling;
        }

        private readonly List<VoxelChunk> chunks = new();
        private readonly Dictionary<string, VoxelChunk> chunkLookup = new();

        // --- Reusable proxy draw list (avoids per-frame allocation) ---
        private readonly List<(VoxelChunk chunk, float dist)> proxyDrawList = new();
        private readonly List<(BakedSector sector, float dist)> sectorDrawList = new();

        // --- Render targets ---
        private RenderTexture colorRT;
        private RenderTexture depthRT;
        private int renderWidth;
        private int renderHeight;
        private float currentResolutionScale;

        // --- Material color lookup (shared) ---
        private ComputeBuffer sharedMaterialBuffer;
        private static readonly int MaxMaterials = StAssetReader.MaterialCount; // keep coupled — buffer must cover every material id
        private ComputeBuffer defaultTintBuffer; // all (1,1,1,1) — used when no custom tint set
        private ComputeBuffer dummyGroupIDBuffer; // single uint(0) — bound when no .groups file exists
        private ComputeBuffer defaultPaneBuffer;  // single uint(0) — bound when a volume has no glass panes
        private ComputeBuffer dummyWalkKeyframeBuffer; // 10 float4s of zeros — bound when walk keyframes disabled
        private ComputeBuffer dummyJointConfigBuffer;   // 7 float4s of zeros — bound when walk keyframes disabled
        private ComputeBuffer dummyPivotBuffer;         // 10 float4s of zeros — bound when authored pivots disabled
        private ComputeBuffer dummyAnimStaticParamsBuffer; // 15 float4s of zeros — bound when anim static params disabled
        private ComputeBuffer dummyRegionIDBuffer;       // single uint(0) — bound when no region data
        private ComputeBuffer dummyMaterialRemapBuffer;  // single uint(0) — bound when no remap
        private ComputeBuffer defaultOccBuffer;          // single uint(0xFFFFFFFF = occupied) — bound when a volume has no occupancy grid
        private ComputeBuffer dummyBuildingOccBuffer;    // single uint(0) — bound when no per-building occ offsets exist

        // --- Packed voxel cache: avoids re-reading + re-packing the same .stasset files ---
        // Includes pre-computed tight AABB so LoadChunkFromData can skip the scan.
        // Data is read-only after caching — no clone needed (SetData and AABB only read).
        private struct CachedVoxelData
        {
            public uint[] data;
            public int w, h, d;
            public int minX, minY, minZ, maxX, maxY, maxZ;
            public bool hasSolid;
        }
        private static readonly Dictionary<string, CachedVoxelData> packedVoxelCache = new();
        private static int packedCacheHits;
        private static int packedCacheMisses;

        // --- Pane group cache: per-voxel pane ids keyed by packed data reference ---
        // Same packed array → same pane map (cloned/procedural arrays recompute).
        private static readonly Dictionary<uint[], uint[]> paneIDCache = new();

        /// <summary>
        /// Pre-load all unique .stasset files in parallel. Populates cache with
        /// packed voxel data + pre-computed AABB. Call before the building loop.
        /// </summary>
        public static void PreloadStassetFiles(List<string> filepaths)
        {
            // Filter to unique files that aren't already cached
            var unique = new HashSet<string>();
            foreach (var path in filepaths)
            {
                if (File.Exists(path) && !packedVoxelCache.ContainsKey(path))
                    unique.Add(path);
            }

            if (unique.Count == 0) return;

            var paths = new List<string>(unique);
            var results = new CachedVoxelData[paths.Count];

            Parallel.For(0, paths.Count, i =>
            {
                string path = paths[i];
                var voxels = StAssetReader.LoadVoxels(path);
                if (voxels == null) return;

                int w = voxels.GetLength(0);
                int h = voxels.GetLength(1);
                int d = voxels.GetLength(2);
                int voxelCount = w * h * d;
                var packedData = new uint[voxelCount];
                int idx = 0;
                for (int z = 0; z < d; z++)
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            packedData[idx++] = (uint)voxels[x, y, z];

                // Compute AABB in parallel too
                ComputeTightAABB(packedData, w, h, d,
                    out int minX, out int minY, out int minZ,
                    out int maxX, out int maxY, out int maxZ, out bool hasSolid);

                results[i] = new CachedVoxelData
                {
                    data = packedData, w = w, h = h, d = d,
                    minX = minX, minY = minY, minZ = minZ,
                    maxX = maxX, maxY = maxY, maxZ = maxZ,
                    hasSolid = hasSolid
                };
            });

            // Add to cache on main thread (Dictionary is not thread-safe)
            for (int i = 0; i < paths.Count; i++)
            {
                if (results[i].data != null)
                {
                    packedVoxelCache[paths[i]] = results[i];
                    packedCacheMisses++;
                }
            }

            int loaded = 0;
            for (int i = 0; i < results.Length; i++)
                if (results[i].data != null) loaded++;

            Debug.Log($"[VoxelChunkManager] Pre-loaded {loaded}/{paths.Count} unique .stasset files in parallel");
        }

        public static (uint[] data, int w, int h, int d) GetPackedVoxels(string filepath)
        {
            if (packedVoxelCache.TryGetValue(filepath, out var cached))
            {
                packedCacheHits++;
                // No clone needed — data is read-only (SetData copies to GPU, AABB only reads)
                return (cached.data, cached.w, cached.h, cached.d);
            }

            packedCacheMisses++;
            var voxels = StAssetReader.LoadVoxels(filepath);
            if (voxels == null) return (null, 0, 0, 0);

            int w = voxels.GetLength(0);
            int h = voxels.GetLength(1);
            int d = voxels.GetLength(2);
            int voxelCount = w * h * d;
            var packedData = new uint[voxelCount];
            int idx = 0;
            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        packedData[idx++] = (uint)voxels[x, y, z];

            ComputeTightAABB(packedData, w, h, d,
                out int minX, out int minY, out int minZ,
                out int maxX, out int maxY, out int maxZ, out bool hasSolid);

            packedVoxelCache[filepath] = new CachedVoxelData
            {
                data = packedData, w = w, h = h, d = d,
                minX = minX, minY = minY, minZ = minZ,
                maxX = maxX, maxY = maxY, maxZ = maxZ,
                hasSolid = hasSolid
            };
            return (packedData, w, h, d);
        }

        // Glass materials that participate in pane grouping — the authored
        // glass ids plus up to 8 forcedGlassMaterialIds flagged in the inspector.
        private static readonly int[] forcedGlassIds = new int[8];
        private static bool IsGlassMaterial(uint mat)
        {
            if (mat == 112u || mat == 113u || mat == 114u || mat == 134u || mat == 147u) return true;
            for (int i = 0; i < forcedGlassIds.Length; i++)
                if (forcedGlassIds[i] != 0 && (int)mat == forcedGlassIds[i]) return true;
            return false;
        }

        /// <summary>
        /// Pane IDs for a packed voxel array — contiguous glass clusters share one
        /// id (0 = non-glass). Cached by data reference so repeated loads of the
        /// same asset don't re-flood; cloned/modified arrays get their own pass.
        /// Returns null when the volume contains no glass.
        /// </summary>
        private static uint[] GetPaneIDs(uint[] packedData, int w, int h, int d)
        {
            if (packedData == null) return null;
            if (!paneIDCache.TryGetValue(packedData, out var pane))
            {
                pane = ComputePaneIDs(packedData, w, h, d);
                paneIDCache[packedData] = pane;
            }
            return pane;
        }

        /// <summary>
        /// 6-connected flood fill over glass voxels (112/114/147): every contiguous
        /// cluster = one pane surface. The shader charges each pane's tint once per
        /// ray instead of per voxel face crossed. Returns null if no glass found.
        /// </summary>
        private static uint[] ComputePaneIDs(uint[] packed, int w, int h, int d)
        {
            uint[] pane = null;
            var stack = new Stack<int>(256);
            uint nextId = 1;
            int wh = w * h;

            for (int i = 0; i < packed.Length; i++)
            {
                if (!IsGlassMaterial(packed[i] & 0x1FFu)) continue;
                if (pane != null && pane[i] != 0) continue;
                if (pane == null) pane = new uint[packed.Length];

                uint id = nextId++;
                if (nextId > 0xFFFFu) nextId = 1u; // wrap: distant panes may merge — tint-only artifact
                pane[i] = id;
                stack.Push(i);

                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    int z = c / wh;
                    int rem = c - z * wh;
                    int y = rem / w;
                    int x = rem - y * w;
                    int n;

                    if (x + 1 < w)  { n = c + 1;  if (pane[n] == 0 && IsGlassMaterial(packed[n] & 0x1FFu)) { pane[n] = id; stack.Push(n); } }
                    if (x > 0)      { n = c - 1;  if (pane[n] == 0 && IsGlassMaterial(packed[n] & 0x1FFu)) { pane[n] = id; stack.Push(n); } }
                    if (y + 1 < h)  { n = c + w;  if (pane[n] == 0 && IsGlassMaterial(packed[n] & 0x1FFu)) { pane[n] = id; stack.Push(n); } }
                    if (y > 0)      { n = c - w;  if (pane[n] == 0 && IsGlassMaterial(packed[n] & 0x1FFu)) { pane[n] = id; stack.Push(n); } }
                    if (z + 1 < d)  { n = c + wh; if (pane[n] == 0 && IsGlassMaterial(packed[n] & 0x1FFu)) { pane[n] = id; stack.Push(n); } }
                    if (z > 0)      { n = c - wh; if (pane[n] == 0 && IsGlassMaterial(packed[n] & 0x1FFu)) { pane[n] = id; stack.Push(n); } }
                }
            }
            return pane;
        }

        private ComputeBuffer CreatePaneBuffer(uint[] paneIDs)
        {
            var buf = new ComputeBuffer(paneIDs.Length, sizeof(uint));
            buf.SetData(paneIDs);
            return buf;
        }

        public static void ClearPackedVoxelCache()
        {
            int count = packedVoxelCache.Count;
            packedVoxelCache.Clear();
            paneIDCache.Clear();
            Debug.Log($"[VoxelChunkManager] Cleared packed voxel cache: {count} files (hits={packedCacheHits} misses={packedCacheMisses})");
            packedCacheHits = 0;
            packedCacheMisses = 0;
        }

        public static int PackedCacheHits => packedCacheHits;
        public static int PackedCacheMisses => packedCacheMisses;
        public static int PackedCacheFiles => packedVoxelCache.Count;

        // --- Dimension cache: read only 16-byte header for fast size lookup ---
        private static readonly Dictionary<string, (int w, int h, int d)> dimCache = new();

        /// <summary>Get .stasset dimensions from header only (no voxel data loaded). Cached.</summary>
        public static (int w, int h, int d) GetStassetDimensions(string filepath)
        {
            if (dimCache.TryGetValue(filepath, out var cached))
                return cached;

            if (!File.Exists(filepath))
                return (0, 0, 0);

            byte[] header = new byte[16];
            using (var fs = File.OpenRead(filepath))
            {
                int read = fs.Read(header, 0, 16);
                if (read < 16 || header[0] != (byte)'S' || header[1] != (byte)'T' ||
                    header[2] != (byte)'A' || header[3] != (byte)'S')
                    return (0, 0, 0);
            }

            int w = header[6] | (header[7] << 8);
            int h = header[8] | (header[9] << 8);
            int d = header[10] | (header[11] << 8);
            var result = (w, h, d);
            dimCache[filepath] = result;
            return result;
        }

        /// <summary>Threshold: buildings wider/deeper than this occupy an entire block.</summary>
        public const int FullBlockVoxelThreshold = 128;

        // --- Fullscreen quad for displaying the render texture ---
        private GameObject displayQuad;
        private Material displayMaterial;

        // --- Camera reference (set by CityMap3D) ---
        private Camera renderCamera;

        // --- Shader kernel IDs ---
        private int kernelCSRaymarch;

        // --- Shader property IDs ---
        private int propVoxelData, propOutput, propDepthBuffer, propMaterialColors;
        private int propOccData, propOccEnabled, propBuildingOcc;
        private int propMaterialCount, propVolumeDims, propVoxelSize, propVolumeOffset;
        private int propTightBoundsMin, propTightBoundsMax;
        private int propVolumeRotation, propVolumeInvRotation;
        private int propCameraOrigin, propCameraToWorld, propInvProjection;
        private int propProxyCamOrigin, propProxyCamToWorld, propProxyInvProj;
        private int propScreenSize, propMaxSteps, propBackgroundColor, propIsOrthographic;
        private int propCheapShading;
        private int propUnlitLod;
        private int propLodDebugEnabled, propLodDebugColor;
        private int propLightDirection, propLightIntensity, propAmbientIntensity, propFillIntensity, propLightColor;
        private int propChunkTints;
        private int propShadowNormalNudge, propShadowLightNudge, propShadowSkipSteps, propShadowMaxSteps, propShadowEnabled;
        private int propSunLightEnabled, propAmbientEnabled, propFillEnabled, propCamLightEnabled;
        private int propInstanceOffsets;
        private int propGroupIDs;
        private int propGroupIDsEnabled;
        private int propPaneIDs;
        private int propPaneMerged;
        private int propIterVis;
        private int propSharedRestBuffer;
        private int propInstanceCount;
        private int propBuildingMeta, propBuildingPositions;
        // Walk keyframe system
        private int propWalkKeyframes, propWalkKeyframesEnabled;
        private int propJointConfig, propJointConfigEnabled;
        private int propWalkConfig;
        private int propPivots, propPivotsEnabled;
        private int propAnimStaticParams, propAnimStaticParamsEnabled;

        // --- Perf tracking (event-driven, not timed) ---
        private bool distanceCullingEnabled = true;
        private float savedMaxRenderDistance = 50f;

        // --- Lighting state (set by VoxelSun) ---
        private Vector3 lightDirection = new Vector3(0.3f, 1f, -0.2f).normalized;
        private float lightIntensity = 0.8f;
        private float ambientIntensity = 0.55f;
        private float fillIntensity = 0.25f;
        private Color lightColor = new Color(1f, 0.98f, 0.95f, 1f);

        // --- Shadow debug state (set from C# via CityMap3D) ---
        private float shadowNormalNudge = 2.5f;
        private float shadowLightNudge = 2.0f;
        private int shadowSkipSteps = 4;
        private int shadowMaxSteps = 32;
        private int shadowEnabled = 0; // disabled by default for performance — re-enable for quality shots

        // --- Lighting debug toggles ---
        private int sunLightEnabled = 1;
        private int ambientEnabled = 1;
        private int fillEnabled = 1;
        private int camLightEnabled = 1;

        #region --- LIFECYCLE ---

        void Awake()
        {
            // Force 2x-upscaled defaults — Inspector may hold stale values from before the voxelSize change
            voxelSize = 0.05f;
            maxSteps = 264;
            // Lock dynres to 1.0 (full resolution)
            resolutionScale = 1.0f;
            shadowEnabled = 0;
            useProxyRender = true;
            if (maxRenderDistance <= 0f) maxRenderDistance = 50f;
            savedMaxRenderDistance = maxRenderDistance;
            // Reset runtime LOD state — never inherit granular LOD from a previous session
            runtimeGranularLod = false;
            distanceCullingEnabled = true;
            currentResolutionScale = 1.0f;
            manualResolutionOverride = true; // lock dynres — prevent auto hysteresis from lowering

            Debug.Log($"[VoxelChunkManager] Awake: maxRenderDistance={maxRenderDistance} resolutionScale={resolutionScale} maxSteps={maxSteps} useProxyRender={useProxyRender} shadows={shadowEnabled}");

            TryAutoLoadShader();
            if (raymarchShader == null)
            {
                Debug.LogError("[VoxelChunkManager] No compute shader assigned! " +
                    "Assign MobSimVoxelRaymarch.compute in the Inspector or place it in a Resources folder.");
                enabled = false;
                return;
            }

            CacheShaderIDs();
            CreateSharedMaterialBuffer();
            ApplyGlassSettings();
            kernelCSRaymarch = raymarchShader.FindKernel("CSRaymarch");
            if (poseComputeShader != null)
            {
                kernelCSClear = poseComputeShader.FindKernel("CSClear");
                kernelCSPose = poseComputeShader.FindKernel("CSPose");
                Debug.Log("[VoxelChunkManager] Pose compute shader kernels cached (CSClear, CSPose)");
            }
            else
            {
                // Will be auto-loaded in InitProxyRender() if available in Resources
                Debug.Log("[VoxelChunkManager] No pose compute shader assigned in inspector — will auto-load from Resources");
            }
            InitProxyRender();
        }

        private void InitProxyRender()
        {
            // Auto-load proxy shader if not assigned
            if (proxyShader == null)
            {
                proxyShader = Resources.Load<Shader>("Shaders/VoxelProxyRaymarch");
                if (proxyShader == null)
                    proxyShader = Shader.Find("SteelCity/VoxelProxyRaymarch");
            }
            // Auto-load pose compute shader if not assigned
            if (poseComputeShader == null)
            {
                poseComputeShader = Resources.Load<ComputeShader>("Shaders/CharacterPoseCompute");
                if (poseComputeShader != null)
                {
                    kernelCSClear = poseComputeShader.FindKernel("CSClear");
                    kernelCSPose = poseComputeShader.FindKernel("CSPose");
                    Debug.Log("[VoxelChunkManager] Pose compute shader auto-loaded from Resources");
                }
            }
            if (proxyShader != null)
            {
                proxyMaterial = new Material(proxyShader);
                proxyMaterial.enableInstancing = true;
                groupMaterial = new Material(proxyShader);
                groupMaterial.enableInstancing = true;
                Debug.Log("[VoxelChunkManager] Proxy shader loaded and material created (instancing enabled).");
            }
            else
            {
                Debug.LogWarning("[VoxelChunkManager] Proxy shader not found! Proxy render will not work.");
            }

            // Create a unit cube mesh for proxy rendering
            proxyCubeMesh = CreateUnitCubeMesh();
        }

        private static Mesh CreateUnitCubeMesh()
        {
            var mesh = new Mesh();
            mesh.vertices = new Vector3[]
            {
                // Front face
                new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
                // Back face
                new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
                // Left face
                new(-0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, -0.5f),
                // Right face
                new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, -0.5f),
                // Top face
                new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
                // Bottom face
                new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f),
            };
            // Outward-facing winding on all 6 faces (Unity front = clockwise
            // from outside) — the glass pass uses Cull Front, which only keeps
            // the far side if every face winds the same way.
            mesh.triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,       // Front
                4, 5, 6, 4, 6, 7,       // Back
                8, 9, 10, 8, 10, 11,    // Left
                12, 14, 13, 12, 15, 14, // Right
                16, 18, 17, 16, 19, 18, // Top
                20, 21, 22, 20, 22, 23, // Bottom
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Try to auto-load the compute shader if not assigned in Inspector.
        /// Looks in Resources/Shaders/ and Resources/ folders.
        /// </summary>
        public void TryAutoLoadShader()
        {
            if (raymarchShader != null) return;

            // Try loading from Resources
            raymarchShader = Resources.Load<ComputeShader>("Shaders/MobSimVoxelRaymarch");
            if (raymarchShader == null)
                raymarchShader = Resources.Load<ComputeShader>("MobSimVoxelRaymarch");

            if (raymarchShader != null)
                Debug.Log("[VoxelChunkManager] Auto-loaded MobSimVoxelRaymarch.compute from Resources.");
        }

        void OnDestroy()
        {
            Shader.DisableKeyword("VOXEL_DEBUG_VIEW");
            ReleaseAllChunks();
            ReleaseRenderTargets();
            if (sharedMaterialBuffer != null) { sharedMaterialBuffer.Release(); sharedMaterialBuffer = null; }
            if (defaultTintBuffer != null) { defaultTintBuffer.Release(); defaultTintBuffer = null; }
            if (dummyGroupIDBuffer != null) { dummyGroupIDBuffer.Release(); dummyGroupIDBuffer = null; }
            if (defaultPaneBuffer != null) { defaultPaneBuffer.Release(); defaultPaneBuffer = null; }
            if (defaultOccBuffer != null) { defaultOccBuffer.Release(); defaultOccBuffer = null; }
            if (dummyBuildingOccBuffer != null) { dummyBuildingOccBuffer.Release(); dummyBuildingOccBuffer = null; }
            if (dummyWalkKeyframeBuffer != null) { dummyWalkKeyframeBuffer.Release(); dummyWalkKeyframeBuffer = null; }
            if (dummyJointConfigBuffer != null) { dummyJointConfigBuffer.Release(); dummyJointConfigBuffer = null; }
            if (dummyPivotBuffer != null) { dummyPivotBuffer.Release(); dummyPivotBuffer = null; }
            if (dummyAnimStaticParamsBuffer != null) { dummyAnimStaticParamsBuffer.Release(); dummyAnimStaticParamsBuffer = null; }
            if (dummyRegionIDBuffer != null) { dummyRegionIDBuffer.Release(); dummyRegionIDBuffer = null; }
            if (dummyMaterialRemapBuffer != null) { dummyMaterialRemapBuffer.Release(); dummyMaterialRemapBuffer = null; }
            if (displayMaterial != null) { Destroy(displayMaterial); displayMaterial = null; }
            if (displayQuad != null) { Destroy(displayQuad); displayQuad = null; }
            if (proxyMaterial != null) { Destroy(proxyMaterial); proxyMaterial = null; }
            if (groupMaterial != null) { Destroy(groupMaterial); groupMaterial = null; }
            if (proxyRT != null) { proxyRT.Release(); proxyRT = null; }
            if (proxyCubeMesh != null) { Destroy(proxyCubeMesh); proxyCubeMesh = null; }
        }

        void LateUpdate()
        {
            EnsureInstancedBuffers();
        }

        /// <summary>
        /// Pre-size instanced-group GPU buffers at instances.Count capacity (the upper
        /// bound of any frame's visibleCount) OUTSIDE the render callback. Creating a
        /// ComputeBuffer inside endCameraRendering drops recorded D3D12 SRV bindings
        /// ("requires a buffer (SRV) '_BuildingMeta' ... Skipping draw calls"), silently
        /// killing sector and group draws. The lazy-create branches inside
        /// RenderInstancedGroup remain as cold fallbacks and should never fire.
        /// </summary>
        private void EnsureInstancedBuffers()
        {
            foreach (var kvp in instancedGroups)
                EnsureGroupBuffers(kvp.Value, kvp.Key);
        }

        private void EnsureGroupBuffers(InstancedGroup group, string key)
        {
            {
                int capacity = group.instances.Count;
                if (capacity == 0) return;
                bool grew = false;

                // 2 float4 per instance: [i] pos+yaw, [i + count] animState/time/speed
                int offsetElems = capacity * 2;
                if (group.instanceOffsetBuffer == null || group.instanceOffsetBuffer.count < offsetElems)
                {
                    if (group.instanceOffsetBuffer != null) group.instanceOffsetBuffer.Release();
                    group.instanceOffsetBuffer = new ComputeBuffer(Mathf.Max(offsetElems, 256), sizeof(float) * 4);
                    grew = true;
                }

                // Compute-pose buffers only exist for groupID-tagged assets. Deliberately
                // ignore poseComputeShader here — over-allocating beats under-allocating.
                if (group.groupIDBuffer != null && group.useComputePose)
                {
                    int totalVoxels = group.dimX * group.dimY * group.dimZ;
                    int posedSize = totalVoxels * capacity;
                    if (group.posedVoxelBuffer == null || group.posedVoxelBuffer.count < posedSize)
                    {
                        if (group.posedVoxelBuffer != null) group.posedVoxelBuffer.Release();
                        group.posedVoxelBuffer = new ComputeBuffer(posedSize, sizeof(uint));
                        grew = true;
                    }
                    if (group.instanceAnimDataBuffer == null || group.instanceAnimDataBuffer.count < capacity * 2)
                    {
                        if (group.instanceAnimDataBuffer != null) group.instanceAnimDataBuffer.Release();
                        group.instanceAnimDataBuffer = new ComputeBuffer(Mathf.Max(capacity * 2, 16), sizeof(float) * 4);
                        grew = true;
                    }
                    if (group.instanceIKDataBuffer == null || group.instanceIKDataBuffer.count < capacity * 4)
                    {
                        if (group.instanceIKDataBuffer != null) group.instanceIKDataBuffer.Release();
                        group.instanceIKDataBuffer = new ComputeBuffer(Mathf.Max(capacity * 4, 16), sizeof(float) * 4);
                        grew = true;
                    }
                    if (group.materialRemapEnabled && group.regionIDBuffer != null)
                    {
                        int remapSize = capacity * group.maxRegions;
                        if (group.instanceMaterialRemapBuffer == null || group.instanceMaterialRemapBuffer.count < remapSize)
                        {
                            if (group.instanceMaterialRemapBuffer != null) group.instanceMaterialRemapBuffer.Release();
                            group.instanceMaterialRemapBuffer = new ComputeBuffer(Mathf.Max(remapSize, 16), sizeof(uint));
                            grew = true;
                        }
                    }
                }
                if (grew)
                    Debug.Log($"[VoxelChunkManager] Pre-sized instanced buffers for {key}: {capacity} instances");
            }
        }

        #endregion

        #region --- INITIALIZATION ---

        private void CacheShaderIDs()
        {
            propVoxelData = Shader.PropertyToID("_VoxelData");
            propOccData = Shader.PropertyToID("_OccData");
            propOccEnabled = Shader.PropertyToID("_OccEnabled");
            propBuildingOcc = Shader.PropertyToID("_BuildingOcc");
            propOutput = Shader.PropertyToID("_Output");
            propDepthBuffer = Shader.PropertyToID("_DepthBuffer");
            propMaterialColors = Shader.PropertyToID("_MaterialColors");
            propMaterialCount = Shader.PropertyToID("_MaterialCount");
            propVolumeDims = Shader.PropertyToID("_VolumeDims");
            propVoxelSize = Shader.PropertyToID("_VoxelSize");
            propVolumeOffset = Shader.PropertyToID("_VolumeOffset");
            propTightBoundsMin = Shader.PropertyToID("_TightBoundsMin");
            propTightBoundsMax = Shader.PropertyToID("_TightBoundsMax");
            propVolumeRotation = Shader.PropertyToID("_VolumeRotation");
            propVolumeInvRotation = Shader.PropertyToID("_VolumeInvRotation");
            propCameraOrigin = Shader.PropertyToID("_CameraOrigin");
            propCameraToWorld = Shader.PropertyToID("_CameraToWorld");
            propInvProjection = Shader.PropertyToID("_InvProjection");
            propProxyCamOrigin = Shader.PropertyToID("_ProxyCamOrigin");
            propProxyCamToWorld = Shader.PropertyToID("_ProxyCamToWorld");
            propProxyInvProj = Shader.PropertyToID("_ProxyInvProj");
            propScreenSize = Shader.PropertyToID("_ScreenSize");
            propMaxSteps = Shader.PropertyToID("_MaxSteps");
            propBackgroundColor = Shader.PropertyToID("_BackgroundColor");
            propIsOrthographic = Shader.PropertyToID("_IsOrthographic");
            propCheapShading = Shader.PropertyToID("_CheapShading");
            propUnlitLod = Shader.PropertyToID("_UnlitLod");
            propLodDebugEnabled = Shader.PropertyToID("_LodDebugEnabled");
            propLodDebugColor = Shader.PropertyToID("_LodDebugColor");
            propLightDirection = Shader.PropertyToID("_LightDirection");
            propLightIntensity = Shader.PropertyToID("_LightIntensity");
            propAmbientIntensity = Shader.PropertyToID("_AmbientIntensity");
            propFillIntensity = Shader.PropertyToID("_FillIntensity");
            propLightColor = Shader.PropertyToID("_LightColor");
            propChunkTints = Shader.PropertyToID("_ChunkTints");
            propShadowNormalNudge = Shader.PropertyToID("_ShadowNormalNudge");
            propShadowLightNudge = Shader.PropertyToID("_ShadowLightNudge");
            propShadowSkipSteps = Shader.PropertyToID("_ShadowSkipSteps");
            propShadowMaxSteps = Shader.PropertyToID("_ShadowMaxSteps");
            propShadowEnabled = Shader.PropertyToID("_ShadowEnabled");
            propSunLightEnabled = Shader.PropertyToID("_SunLightEnabled");
            propAmbientEnabled = Shader.PropertyToID("_AmbientEnabled");
            propFillEnabled = Shader.PropertyToID("_FillEnabled");
            propCamLightEnabled = Shader.PropertyToID("_CamLightEnabled");
            propInstanceOffsets = Shader.PropertyToID("_InstanceOffsets");
            propGroupIDs = Shader.PropertyToID("_GroupIDs");
            propGroupIDsEnabled = Shader.PropertyToID("_GroupIDsEnabled");
            propPaneIDs = Shader.PropertyToID("_PaneIDs");
            propPaneMerged = Shader.PropertyToID("_PaneMerged");
            propIterVis = Shader.PropertyToID("_IterVis");
            propSharedRestBuffer = Shader.PropertyToID("_SharedRestBuffer");
            propInstanceCount = Shader.PropertyToID("_InstanceCount");
            propBuildingMeta = Shader.PropertyToID("_BuildingMeta");
            propBuildingPositions = Shader.PropertyToID("_BuildingPositions");
            // Walk keyframe system
            propWalkKeyframes = Shader.PropertyToID("_WalkKeyframes");
            propWalkKeyframesEnabled = Shader.PropertyToID("_WalkKeyframesEnabled");
            propJointConfig = Shader.PropertyToID("_JointConfig");
            propJointConfigEnabled = Shader.PropertyToID("_JointConfigEnabled");
            propWalkConfig = Shader.PropertyToID("_WalkConfig");
            propPivots = Shader.PropertyToID("_Pivots");
            propPivotsEnabled = Shader.PropertyToID("_PivotsEnabled");
            propAnimStaticParams = Shader.PropertyToID("_AnimStaticParams");
            propAnimStaticParamsEnabled = Shader.PropertyToID("_AnimStaticParamsEnabled");
        }

        private void CreateSharedMaterialBuffer()
        {
            // Build 256-entry color lookup from StAssetReader's palette
            var colors = new Vector4[MaxMaterials];
            for (int i = 0; i < MaxMaterials; i++)
            {
                var c = StAssetReader.GetMaterialColor((ushort)i);
                colors[i] = new Vector4(c.r, c.g, c.b, c.a);
            }
            sharedMaterialBuffer = new ComputeBuffer(MaxMaterials, sizeof(float) * 4);
            sharedMaterialBuffer.SetData(colors);

            // Create default tint buffer (all 1,1,1 = no tint)
            var defaultTints = new Vector4[MaxMaterials];
            for (int i = 0; i < MaxMaterials; i++)
                defaultTints[i] = new Vector4(1f, 1f, 1f, 1f);
            defaultTintBuffer = new ComputeBuffer(MaxMaterials, sizeof(float) * 4);
            defaultTintBuffer.SetData(defaultTints);

            // Dummy group ID buffer: 1 voxel with groupID=0 (torso/no transform)
            // Bound when a group has no .groups file so D3D12 doesn't reject the draw call
            dummyGroupIDBuffer = new ComputeBuffer(1, sizeof(uint));
            dummyGroupIDBuffer.SetData(new uint[] { 0 });

            // Dummy pane buffer: 1 voxel with paneId=0 (ungrouped).
            // Bound when a volume has no glass panes so D3D12 doesn't reject the draw call.
            defaultPaneBuffer = new ComputeBuffer(1, sizeof(uint));
            defaultPaneBuffer.SetData(new uint[] { 0 });

            // Dummy occupancy buffer: one word of all-occupied so a volume with no
            // occ grid marches exactly as before (never skips) — SRV completeness.
            defaultOccBuffer = new ComputeBuffer(1, sizeof(uint));
            defaultOccBuffer.SetData(new uint[] { 0xFFFFFFFFu });
            dummyBuildingOccBuffer = new ComputeBuffer(1, sizeof(uint));
            dummyBuildingOccBuffer.SetData(new uint[] { 0 });

            // Dummy walk keyframe / joint config buffers: bound whenever walk keyframes are
            // disabled so D3D12 doesn't reject the draw call (SRV must be bound at every
            // declared shader slot, regardless of whether the shader logic reads it).
            dummyWalkKeyframeBuffer = new ComputeBuffer(10, sizeof(float) * 4);
            dummyWalkKeyframeBuffer.SetData(new Vector4[10]);
            dummyJointConfigBuffer = new ComputeBuffer(7, sizeof(float) * 4);
            dummyJointConfigBuffer.SetData(new Vector4[7]);
            dummyPivotBuffer = new ComputeBuffer(10, sizeof(float) * 4);
            dummyPivotBuffer.SetData(new Vector4[10]);
            dummyAnimStaticParamsBuffer = new ComputeBuffer(15, sizeof(float) * 4);
            dummyAnimStaticParamsBuffer.SetData(new Vector4[15]);
            dummyRegionIDBuffer = new ComputeBuffer(1, sizeof(uint));
            dummyRegionIDBuffer.SetData(new uint[] { 0 });
            dummyMaterialRemapBuffer = new ComputeBuffer(1, sizeof(uint));
            dummyMaterialRemapBuffer.SetData(new uint[] { 0 });

            // Debug: log key material colors to verify they're updated
            Debug.Log($"[VoxelChunkManager] Material buffer created ({MaxMaterials} entries). " +
                $"Mat118(Tar)=({colors[118].x:F2},{colors[118].y:F2},{colors[118].z:F2}) " +
                $"Mat104(Asphalt)=({colors[104].x:F2},{colors[104].y:F2},{colors[104].z:F2}) " +
                $"Mat100(RedBrick)=({colors[100].x:F2},{colors[100].y:F2},{colors[100].z:F2})");
        }

        public void RefreshMaterialBuffer()
        {
            if (sharedMaterialBuffer == null) return;
            var colors = new Vector4[MaxMaterials];
            for (int i = 0; i < MaxMaterials; i++)
            {
                var c = StAssetReader.GetMaterialColor((ushort)i);
                colors[i] = new Vector4(c.r, c.g, c.b, c.a);
            }
            sharedMaterialBuffer.SetData(colors);
        }

        // Inspector live-tuning: fires on every field change (edit + play mode).
        private void OnValidate() => ApplyGlassSettings();

        /// <summary>
        /// Push glass inspector settings into the material palette + shader globals.
        /// Alpha rides the shared material buffer; specular/maxLayers are globals.
        /// </summary>
        public void ApplyGlassSettings()
        {
            StAssetReader.SetMaterialColor(147, autoGlassColor);
            StAssetReader.SetMaterialColor(112, windowGlassColor);
            StAssetReader.SetMaterialColor(114, storefrontGlassColor);
            Shader.SetGlobalFloat("_GlassSpecular", glassSpecular);
            Shader.SetGlobalInt("_GlassMaxLayers", glassMaxLayers);
            Shader.SetGlobalFloat("_FlaggedGlassAlpha", flaggedGlassAlpha);
            // Forced-glass ids → 8 global slots (0 = empty). Shared by the
            // shader gate and the pane flood-fill below.
            Array.Clear(forcedGlassIds, 0, forcedGlassIds.Length);
            int n = Mathf.Min(forcedGlassMaterialIds.Count, forcedGlassIds.Length);
            for (int i = 0; i < n; i++)
                forcedGlassIds[i] = forcedGlassMaterialIds[i];
            for (int i = 0; i < forcedGlassIds.Length; i++)
                Shader.SetGlobalInt($"_GlassForcedMat{i}", forcedGlassIds[i]);
            if (Application.isPlaying)
                RefreshMaterialBuffer();
        }

        [ContextMenu("Reset glass to authored palette")]
        private void ResetGlassColors()
        {
            autoGlassColor = StAssetReader.GetDefaultMaterialColor(147);
            windowGlassColor = StAssetReader.GetDefaultMaterialColor(112);
            storefrontGlassColor = StAssetReader.GetDefaultMaterialColor(114);
            ApplyGlassSettings();
        }

        /// <summary>
        /// Set per-chunk per-material tint. Creates a dedicated tint buffer for this chunk.
        /// tint[materialID] = (r,g,b,a) multiplier. Default is (1,1,1,1) = no change.
        /// Only materials present in the tint array are applied; others default to (1,1,1,1).
        /// </summary>
        public void SetChunkTint(string chunkName, Dictionary<ushort, Vector4> tints)
        {
            if (!chunkLookup.TryGetValue(chunkName, out var chunk)) return;

            // Build full tint array
            var tintData = new Vector4[MaxMaterials];
            for (int i = 0; i < MaxMaterials; i++)
                tintData[i] = new Vector4(1f, 1f, 1f, 1f); // default: no tint

            if (tints != null)
            {
                foreach (var kv in tints)
                {
                    if (kv.Key < MaxMaterials)
                        tintData[kv.Key] = kv.Value;
                }
            }

            // Release old custom tint buffer if we had one
            if (chunk.tintBuffer != null && chunk.tintBuffer != defaultTintBuffer)
                chunk.tintBuffer.Release();

            // Create new dedicated tint buffer for this chunk
            chunk.tintBuffer = new ComputeBuffer(MaxMaterials, sizeof(float) * 4);
            chunk.tintBuffer.SetData(tintData);
        }

        /// <summary>
        /// Reset a chunk's tint to default (no tinting).
        /// </summary>
        public void ClearChunkTint(string chunkName)
        {
            if (!chunkLookup.TryGetValue(chunkName, out var chunk)) return;
            if (chunk.tintBuffer != null && chunk.tintBuffer != defaultTintBuffer)
            {
                chunk.tintBuffer.Release();
                chunk.tintBuffer = defaultTintBuffer;
            }
        }

        public void SetRenderCamera(Camera cam)
        {
            renderCamera = cam;
        }

        #endregion

        #region --- TIGHT AABB ---

        /// <summary>
        /// Scan voxel data to find the tight bounding box of all solid voxels.
        /// Used to size the proxy cube mesh so only pixels covering solid geometry run the fragment shader.
        /// </summary>
        private static void ComputeTightAABB(uint[] data, int w, int h, int d,
            out int minX, out int minY, out int minZ,
            out int maxX, out int maxY, out int maxZ, out bool hasSolid)
        {
            minX = int.MaxValue; minY = int.MaxValue; minZ = int.MaxValue;
            maxX = int.MinValue; maxY = int.MinValue; maxZ = int.MinValue;
            hasSolid = false;

            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (data[x + y * w + z * w * h] != 0u)
                        {
                            hasSolid = true;
                            if (x < minX) minX = x;
                            if (y < minY) minY = y;
                            if (z < minZ) minZ = z;
                            if (x > maxX) maxX = x;
                            if (y > maxY) maxY = y;
                            if (z > maxZ) maxZ = z;
                        }
                    }

            if (!hasSolid)
            {
                minX = minY = minZ = 0;
                maxX = maxY = maxZ = 0;
            }
        }

        #endregion

        #region --- CHUNK MANAGEMENT ---

        /// <summary>
        /// Load a .stasset file as a voxel chunk at the given world position.
        /// Creates a visible GameObject in the scene hierarchy (like Steel Tide's VoxelObject)
        /// so you can inspect and adjust positions in the Editor.
        /// </summary>
        public void LoadChunk(string name, string stassetPath, Vector3 worldPos)
        {
            LoadChunk(name, stassetPath, worldPos, voxelSize);
        }

        /// <summary>
        /// Load a .stasset file as a voxel chunk with a custom voxel size.
        /// Used for character models which use a smaller voxel size than buildings.
        /// </summary>
        public void LoadChunk(string name, string stassetPath, Vector3 worldPos, float customVoxelSize)
        {
            if (!File.Exists(stassetPath))
            {
                Debug.LogWarning($"[VoxelChunkManager] Chunk file not found: {stassetPath}");
                return;
            }

            // Remove existing chunk with same name
            RemoveChunk(name);

            var (packedData, w, h, d) = GetPackedVoxels(stassetPath);
            if (packedData == null) return;

            // Use pre-computed AABB from cache if available
            var cached = packedVoxelCache.TryGetValue(stassetPath, out var cvd);

            int voxelCount = w * h * d;

            // Create a world-space GameObject for this chunk (visible in Hierarchy)
            var hostObj = new GameObject($"VoxelChunk_{name}");
            hostObj.transform.SetParent(transform, false);
            hostObj.transform.position = worldPos;
            hostObj.AddComponent<VoxelChunkGizmo>().Initialize(w, h, d, customVoxelSize);

            var chunk = new VoxelChunk
            {
                name = name,
                hostObject = hostObj,
                dims = new VoxelInt3(w, h, d),
                worldOffset = worldPos,
                voxelSize = customVoxelSize,
                active = true
            };

            chunk.voxelBuffer = new ComputeBuffer(voxelCount, sizeof(uint));
            chunk.voxelBuffer.SetData(packedData);
            chunk.materialBuffer = sharedMaterialBuffer; // shared
            chunk.tintBuffer = defaultTintBuffer; // default: no tint

            var paneIDs = GetPaneIDs(packedData, w, h, d);
            chunk.paneBuffer = paneIDs != null ? CreatePaneBuffer(paneIDs) : defaultPaneBuffer;

            chunk.occData = BuildOccupancy(packedData, w, h, d, out chunk.occWords);
            chunk.occBuffer = new ComputeBuffer(chunk.occWords, sizeof(uint));
            chunk.occBuffer.SetData(chunk.occData);

            if (cached)
            {
                chunk.tightMinX = cvd.minX; chunk.tightMinY = cvd.minY; chunk.tightMinZ = cvd.minZ;
                chunk.tightMaxX = cvd.maxX; chunk.tightMaxY = cvd.maxY; chunk.tightMaxZ = cvd.maxZ;
                chunk.hasSolid = cvd.hasSolid;
            }
            else
            {
                ComputeTightAABB(packedData, w, h, d,
                    out chunk.tightMinX, out chunk.tightMinY, out chunk.tightMinZ,
                    out chunk.tightMaxX, out chunk.tightMaxY, out chunk.tightMaxZ, out chunk.hasSolid);
            }

            chunks.Add(chunk);
            chunkLookup[name] = chunk;

            // Suppress per-chunk load logs (too many at scale — 300+ chunks for 100 blocks)
        }

        /// <summary>
        /// Load a building chunk centered on the given world position.
        /// Voxel data origin is the corner (0,0,0), so we offset by half the
        /// building's XZ footprint to center it on the anchor point.
        /// Returns the building's world-space footprint (center + size).
        /// </summary>
        public BuildingFootprint LoadChunkCentered(string name, string filepath, Vector3 centerPos)
        {
            return LoadChunkCentered(name, filepath, centerPos, voxelSize);
        }

        /// <summary>
        /// Load a building chunk centered on the given world position with a custom voxel size.
        /// Used for character models which use a smaller voxel size than buildings.
        /// </summary>
        public BuildingFootprint LoadChunkCentered(string name, string filepath, Vector3 centerPos, float customVoxelSize)
        {
            var (packedData, w, h, d) = GetPackedVoxels(filepath);
            if (packedData == null) return null;

            // Use pre-computed AABB from cache if available
            var cached = packedVoxelCache.TryGetValue(filepath, out var cvd);

            // Offset so the CENTER of the voxel volume sits at centerPos
            Vector3 cornerPos = centerPos - new Vector3(w * customVoxelSize * 0.5f, 0f, d * customVoxelSize * 0.5f);

            LoadChunkFromDataWithAABB(name, packedData, w, h, d, cornerPos, customVoxelSize,
                cached, cvd.minX, cvd.minY, cvd.minZ, cvd.maxX, cvd.maxY, cvd.maxZ, cvd.hasSolid);

            return new BuildingFootprint
            {
                center = centerPos,
                size = new Vector3(w * customVoxelSize, h * customVoxelSize, d * customVoxelSize),
                dims = new VoxelInt3(w, h, d),
                data = packedData
            };
        }

        /// <summary>
        /// Load a building chunk centered on the given world position, with a procedural
        /// modification pass applied to a CLONE of the cached voxel data.
        /// Used for empty land plots — each gets unique debris scattered deterministically.
        /// The modifier callback receives (voxels, w, h, d) and modifies the array in-place.
        /// </summary>
        public BuildingFootprint LoadChunkCenteredProcedural(
            string name, string filepath, Vector3 centerPos,
            Action<uint[], int, int, int> modifier)
        {
            return LoadChunkCenteredProcedural(name, filepath, centerPos, voxelSize, modifier);
        }

        public BuildingFootprint LoadChunkCenteredProcedural(
            string name, string filepath, Vector3 centerPos, float customVoxelSize,
            Action<uint[], int, int, int> modifier)
        {
            var (baseData, w, h, d) = GetPackedVoxels(filepath);
            if (baseData == null) return null;

            // Clone the cached data so we don't corrupt the shared cache
            var clonedData = new uint[baseData.Length];
            System.Array.Copy(baseData, clonedData, baseData.Length);

            // Run the procedural modifier
            modifier?.Invoke(clonedData, w, h, d);

            // Recompute AABB after modification
            ComputeTightAABB(clonedData, w, h, d,
                out int minX, out int minY, out int minZ,
                out int maxX, out int maxY, out int maxZ, out bool hasSolid);

            // Offset so the CENTER of the voxel volume sits at centerPos
            Vector3 cornerPos = centerPos - new Vector3(w * customVoxelSize * 0.5f, 0f, d * customVoxelSize * 0.5f);

            LoadChunkFromDataWithAABB(name, clonedData, w, h, d, cornerPos, customVoxelSize,
                true, minX, minY, minZ, maxX, maxY, maxZ, hasSolid);

            return new BuildingFootprint
            {
                center = centerPos,
                size = new Vector3(w * customVoxelSize, h * customVoxelSize, d * customVoxelSize),
                dims = new VoxelInt3(w, h, d),
                data = clonedData
            };
        }

        /// <summary>
        /// Load a chunk centered on centerPos from shared pre-built voxel data.
        /// Unlike LoadChunkCenteredProcedural, the array is NOT cloned — callers
        /// must treat it as read-only (e.g. ProceduralDebrisScatterer variants).
        /// </summary>
        public BuildingFootprint LoadChunkCenteredShared(
            string name, uint[] data, int w, int h, int d, Vector3 centerPos)
        {
            return LoadChunkCenteredShared(name, data, w, h, d, centerPos, voxelSize);
        }

        public BuildingFootprint LoadChunkCenteredShared(
            string name, uint[] data, int w, int h, int d, Vector3 centerPos, float customVoxelSize)
        {
            if (data == null) return null;

            ComputeTightAABB(data, w, h, d,
                out int minX, out int minY, out int minZ,
                out int maxX, out int maxY, out int maxZ, out bool hasSolid);

            // Offset so the CENTER of the voxel volume sits at centerPos
            Vector3 cornerPos = centerPos - new Vector3(w * customVoxelSize * 0.5f, 0f, d * customVoxelSize * 0.5f);

            LoadChunkFromDataWithAABB(name, data, w, h, d, cornerPos, customVoxelSize,
                true, minX, minY, minZ, maxX, maxY, maxZ, hasSolid);

            return new BuildingFootprint
            {
                center = centerPos,
                size = new Vector3(w * customVoxelSize, h * customVoxelSize, d * customVoxelSize),
                dims = new VoxelInt3(w, h, d),
                data = data
            };
        }

        /// <summary>
        /// Building footprint data for address system and precise placement.
        /// </summary>
        public class BuildingFootprint
        {
            public Vector3 center;     // World-space center of the building base
            public Vector3 size;       // World-space dimensions (width, height, depth)
            public VoxelInt3 dims;     // Voxel dimensions
            public uint[] data;        // Voxel array the chunk was loaded from (shared with the cache / debris pool — read-only); used for collision registration
        }

        /// <summary>
        /// Load a chunk from raw uint[] voxel data (procedurally generated, no file I/O).
        /// Used by VoxelTerrainBuilder for ground tiles, roads, and sidewalks.
        /// </summary>
        public void LoadChunkFromData(string name, uint[] packedData, int w, int h, int d, Vector3 worldPos)
        {
            LoadChunkFromData(name, packedData, w, h, d, worldPos, voxelSize);
        }

        /// <summary>
        /// Load a chunk from raw uint[] voxel data with a custom voxel size.
        /// </summary>
        public void LoadChunkFromData(string name, uint[] packedData, int w, int h, int d, Vector3 worldPos, float customVoxelSize)
        {
            RemoveChunk(name);

            int voxelCount = w * h * d;
            if (packedData == null || packedData.Length != voxelCount)
            {
                Debug.LogError($"[VoxelChunkManager] LoadChunkFromData '{name}': data length {packedData?.Length ?? 0} != {voxelCount}");
                return;
            }

            var hostObj = new GameObject($"VoxelChunk_{name}");
            hostObj.transform.SetParent(transform, false);
            hostObj.transform.position = worldPos;
            hostObj.AddComponent<VoxelChunkGizmo>().Initialize(w, h, d, customVoxelSize);

            var chunk = new VoxelChunk
            {
                name = name,
                hostObject = hostObj,
                dims = new VoxelInt3(w, h, d),
                worldOffset = worldPos,
                voxelSize = customVoxelSize,
                active = true
            };

            chunk.voxelBuffer = new ComputeBuffer(voxelCount, sizeof(uint));
            chunk.voxelBuffer.SetData(packedData);
            chunk.materialBuffer = sharedMaterialBuffer;
            chunk.tintBuffer = defaultTintBuffer; // default: no tint

            var paneIDs = GetPaneIDs(packedData, w, h, d);
            chunk.paneBuffer = paneIDs != null ? CreatePaneBuffer(paneIDs) : defaultPaneBuffer;

            chunk.occData = BuildOccupancy(packedData, w, h, d, out chunk.occWords);
            chunk.occBuffer = new ComputeBuffer(chunk.occWords, sizeof(uint));
            chunk.occBuffer.SetData(chunk.occData);

            ComputeTightAABB(packedData, w, h, d,
                out chunk.tightMinX, out chunk.tightMinY, out chunk.tightMinZ,
                out chunk.tightMaxX, out chunk.tightMaxY, out chunk.tightMaxZ, out chunk.hasSolid);

            chunks.Add(chunk);
            chunkLookup[name] = chunk;

            // Suppress per-chunk load logs for procedural terrain (too many at scale)
            // Only log errors (empty chunks, data mismatches) not routine loads
        }

        /// <summary>
        /// Load a chunk from raw uint[] voxel data with pre-computed AABB.
        /// Skips the ComputeTightAABB scan when AABB is already known (from cache).
        /// </summary>
        public void LoadChunkFromDataWithAABB(string name, uint[] packedData, int w, int h, int d,
            Vector3 worldPos, float customVoxelSize,
            bool hasCachedAABB,
            int aabbMinX, int aabbMinY, int aabbMinZ,
            int aabbMaxX, int aabbMaxY, int aabbMaxZ, bool aabbHasSolid)
        {
            RemoveChunk(name);

            int voxelCount = w * h * d;
            if (packedData == null || packedData.Length != voxelCount)
            {
                Debug.LogError($"[VoxelChunkManager] LoadChunkFromDataWithAABB '{name}': data length {packedData?.Length ?? 0} != {voxelCount}");
                return;
            }

            var hostObj = new GameObject($"VoxelChunk_{name}");
            hostObj.transform.SetParent(transform, false);
            hostObj.transform.position = worldPos;
            hostObj.AddComponent<VoxelChunkGizmo>().Initialize(w, h, d, customVoxelSize);

            var chunk = new VoxelChunk
            {
                name = name,
                hostObject = hostObj,
                dims = new VoxelInt3(w, h, d),
                worldOffset = worldPos,
                voxelSize = customVoxelSize,
                active = true
            };

            chunk.voxelBuffer = new ComputeBuffer(voxelCount, sizeof(uint));
            chunk.voxelBuffer.SetData(packedData);
            chunk.materialBuffer = sharedMaterialBuffer;
            chunk.tintBuffer = defaultTintBuffer;

            var paneIDs = GetPaneIDs(packedData, w, h, d);
            chunk.paneBuffer = paneIDs != null ? CreatePaneBuffer(paneIDs) : defaultPaneBuffer;

            chunk.occData = BuildOccupancy(packedData, w, h, d, out chunk.occWords);
            chunk.occBuffer = new ComputeBuffer(chunk.occWords, sizeof(uint));
            chunk.occBuffer.SetData(chunk.occData);

            if (hasCachedAABB)
            {
                chunk.tightMinX = aabbMinX; chunk.tightMinY = aabbMinY; chunk.tightMinZ = aabbMinZ;
                chunk.tightMaxX = aabbMaxX; chunk.tightMaxY = aabbMaxY; chunk.tightMaxZ = aabbMaxZ;
                chunk.hasSolid = aabbHasSolid;
            }
            else
            {
                ComputeTightAABB(packedData, w, h, d,
                    out chunk.tightMinX, out chunk.tightMinY, out chunk.tightMinZ,
                    out chunk.tightMaxX, out chunk.tightMaxY, out chunk.tightMaxZ, out chunk.hasSolid);
            }

            chunks.Add(chunk);
            chunkLookup[name] = chunk;
        }

        /// <summary>
        /// Remove a chunk by name, releasing its GPU buffer and destroying its GameObject.
        /// </summary>
        public void RemoveChunk(string name)
        {
            if (!chunkLookup.TryGetValue(name, out var chunk)) return;
            if (chunk.voxelBuffer != null) chunk.voxelBuffer.Release();
            if (chunk.paneBuffer != null && chunk.paneBuffer != defaultPaneBuffer)
                chunk.paneBuffer.Release();
            if (chunk.occBuffer != null) { chunk.occBuffer.Release(); chunk.occBuffer = null; chunk.occData = null; }
            // Don't release tintBuffer if it's the shared default
            if (chunk.tintBuffer != null && chunk.tintBuffer != defaultTintBuffer)
                chunk.tintBuffer.Release();
            if (chunk.hostObject != null) Destroy(chunk.hostObject);
            chunks.Remove(chunk);
            chunkLookup.Remove(name);
        }

        /// <summary>
        /// Update world position of a chunk (for City Editor live adjustments).
        /// </summary>
        public void UpdateChunkPosition(string name, Vector3 worldPos)
        {
            if (chunkLookup.TryGetValue(name, out var chunk))
            {
                chunk.worldOffset = worldPos;
                if (chunk.hostObject != null)
                    chunk.hostObject.transform.position = worldPos;
            }
        }

        /// <summary>
        /// Update the tight AABB of a registered chunk. Used by ForwardTransformTestRig
        /// to update proxy box bounds as voxels move due to forward-transform posing.
        /// </summary>
        public void UpdateChunkTightAABB(string name,
            int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
            if (chunkLookup.TryGetValue(name, out var chunk))
            {
                chunk.tightMinX = minX;
                chunk.tightMinY = minY;
                chunk.tightMinZ = minZ;
                chunk.tightMaxX = maxX;
                chunk.tightMaxY = maxY;
                chunk.tightMaxZ = maxZ;
            }
        }

        /// <summary>
        /// Register an externally-managed voxel volume for rendering.
        /// The caller owns the ComputeBuffer and GameObject; VoxelChunkManager
        /// just renders it each frame. Similar to SteelTide's VoxelRenderer.RegisterVolume.
        /// </summary>
        public void RegisterVolume(string name, GameObject host, ComputeBuffer buffer,
            int dimsX, int dimsY, int dimsZ, float customVoxelSize)
        {
            RemoveChunk(name);

            // Ensure shared buffers are initialized (in case RegisterVolume is called
            // before Awake completes on this component)
            if (sharedMaterialBuffer == null)
            {
                Debug.LogWarning("[VoxelChunkManager] RegisterVolume called before Awake — initializing shared buffers now");
                CreateSharedMaterialBuffer();
            }

            var chunk = new VoxelChunk
            {
                name = name,
                hostObject = host,
                voxelBuffer = buffer,
                paneBuffer = defaultPaneBuffer,   // external volumes: no pane data
                materialBuffer = sharedMaterialBuffer,
                tintBuffer = defaultTintBuffer,
                dims = new VoxelInt3(dimsX, dimsY, dimsZ),
                worldOffset = host.transform.position,
                rotation = host.transform.rotation,
                voxelSize = customVoxelSize,
                active = true,
                hasSolid = true,
                tightMinX = 0,
                tightMinY = 0,
                tightMinZ = 0,
                tightMaxX = Mathf.Max(0, dimsX - 1),
                tightMaxY = Mathf.Max(0, dimsY - 1),
                tightMaxZ = Mathf.Max(0, dimsZ - 1),
                skipCulling = true
            };

            chunks.Add(chunk);
            chunkLookup[name] = chunk;

            Debug.Log($"[VoxelChunkManager] Registered external volume '{name}': {dimsX}x{dimsY}x{dimsZ} at {host.transform.position} (voxelSize={customVoxelSize})");
        }

        /// <summary>
        /// Unregister an externally-managed volume (does NOT release the buffer or destroy the GameObject).
        /// </summary>
        public void UnregisterVolume(string name)
        {
            if (!chunkLookup.TryGetValue(name, out var chunk)) return;
            chunks.Remove(chunk);
            chunkLookup.Remove(name);
            Debug.Log($"[VoxelChunkManager] Unregistered external volume '{name}'");
        }

        /// <summary>True when a chunk with this name is loaded on the per-chunk render path.</summary>
        public bool HasChunk(string name) => name != null && chunkLookup.ContainsKey(name);

        /// <summary>
        /// True when this volume id renders through a buffer the damage path can write —
        /// either a live per-building chunk or a region inside a baked sector's merged
        /// buffer. This is the gate VoxelDamageSystem uses instead of HasChunk (P2).
        /// </summary>
        public bool HasWritableVolume(string name) =>
            HasChunk(name) || (name != null && sectorVolumeLookup.ContainsKey(name));

        // --- Macro-cell occupancy (raymarch empty-skip) ---
        // One bit per 4x4x4 macro cell: set when ANY voxel inside is solid.
        // The shader's DDA checks the bit before sampling — an empty macro
        // jumps straight to its exit plane instead of stepping 64 voxels.
        private const int OccShift = 2;   // macro edge = 1 << OccShift = 4 cells

        private static uint[] BuildOccupancy(uint[] data, int w, int h, int d, out int words)
        {
            int mdx = (w + 3) >> OccShift, mdy = (h + 3) >> OccShift, mdz = (d + 3) >> OccShift;
            words = (mdx * mdy * mdz + 31) >> 5;
            var occ = new uint[words];
            int wh = w * h;
            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if ((data[x + y * w + z * wh] & 0x1FFu) == 0u) continue;
                        int mi = (x >> OccShift) + mdx * ((y >> OccShift) + mdy * (z >> OccShift));
                        occ[mi >> 5] |= 1u << (mi & 31);
                    }
            return occ;
        }

        // Recompute every macro cell touched by a dirty voxel write.
        // data/dims are volume-local; writes land at occOffset + word.
        private void UpdateOccupancy(uint[] data, int w, int h, int d,
            List<int> dirtyIndices, uint[] occ, int occOff, int occWords,
            ComputeBuffer occBuf, int occBufBase)
        {
            occMacroScratch.Clear();
            int mdx = (w + 3) >> OccShift, mdy = (h + 3) >> OccShift;
            int wh = w * h;
            for (int i = 0; i < dirtyIndices.Count; i++)
            {
                int idx = dirtyIndices[i];
                int z = idx / wh, rem = idx - z * wh, y = rem / w, x = rem - y * w;
                occMacroScratch.Add((x >> OccShift) + mdx * ((y >> OccShift) + mdy * (z >> OccShift)));
            }
            foreach (int mi in occMacroScratch)
            {
                int mz = mi / (mdx * mdy), mrem = mi - mz * mdx * mdy, my = mrem / mdx, mx = mrem - my * mdx;
                bool solid = false;
                int x0 = mx << OccShift, y0 = my << OccShift, z0 = mz << OccShift;
                int x1 = System.Math.Min(x0 + (1 << OccShift), w);
                int y1 = System.Math.Min(y0 + (1 << OccShift), h);
                int z1 = System.Math.Min(z0 + (1 << OccShift), d);
                for (int z = z0; z < z1 && !solid; z++)
                    for (int y = y0; y < y1 && !solid; y++)
                        for (int x = x0; x < x1; x++)
                            if ((data[x + y * w + z * wh] & 0x1FFu) != 0u) { solid = true; break; }
                int wi = occOff + (mi >> 5), bit = mi & 31;
                if (solid) occ[wi] |= 1u << bit; else occ[wi] &= ~(1u << bit);
            }
            occBuf.SetData(occ, occOff, occBufBase, occWords);   // whole-volume occ slice — a few KB
        }
        private readonly HashSet<int> occMacroScratch = new();

        // --- Sector-baked damage writes (P2) ---
        // A sector placement's voxels live at mergedVoxelData[bufferOffset .. +w*h*d].
        // The volume id convention is identical to the per-chunk path, so the same
        // dirty-cell API works — writes just land at bufferOffset + localIndex.
        // Merged pane ids are localId + (buildingIndex << 16), so damage pane ids take
        // a distinct high band (0x8000_0000+) — they can't collide with any building's
        // authored pane slot and still dedup correctly along a ray.
        private sealed class SectorVolumeRef
        {
            public BakedSector sector;
            public int bufferOffset;
            public int occOffset;     // word offset into sector.mergedOcc for this building
            public uint paneIdBase;   // buildingIndex << 16
            public int w, h, d;
        }
        private readonly Dictionary<string, SectorVolumeRef> sectorVolumeLookup = new();
        // Pane ids handed to damage edits — high band so they rarely collide with
        // authored flood-fill ids (a collision only means two panes dedup: tint-only).
        private static uint nextDamagePaneId = 0x8000u;
        private static uint nextSectorDamagePaneId = 0x8000_0000u;
        private static uint[] paneScratch = new uint[0];

        /// <summary>
        /// Record that volume `volumeName` renders from sector `sectorName` at
        /// `bufferOffset` — called once per placement by CityMap3D after sector bake.
        /// </summary>
        public void RegisterSectorVolume(string volumeName, string sectorName,
            int bufferOffset, int buildingIndex, int w, int h, int d)
        {
            if (string.IsNullOrEmpty(volumeName)) return;
            if (!sectorLookup.TryGetValue(sectorName, out var sector)) return;
            sectorVolumeLookup[volumeName] = new SectorVolumeRef
            {
                sector = sector,
                bufferOffset = bufferOffset,
                occOffset = sector.buildingOccOffsets != null && buildingIndex >= 0
                            && buildingIndex < sector.buildingOccOffsets.Length
                            ? sector.buildingOccOffsets[buildingIndex] : 0,
                paneIdBase = (uint)buildingIndex << 16,
                w = w, h = h, d = d
            };
        }

        public bool UploadVoxelEdits(string chunkName, uint[] data, List<int> dirtyIndices)
        {
            if (data == null || dirtyIndices == null || dirtyIndices.Count == 0)
                return true;

            ComputeBuffer voxelBuf, paneBuf;
            int indexBase = 0;                      // sector path: + bufferOffset
            bool chunkPath = chunkLookup.TryGetValue(chunkName, out var chunk) && chunk.voxelBuffer != null;
            if (chunkPath)
            {
                voxelBuf = chunk.voxelBuffer;
                paneBuf = chunk.paneBuffer;
            }
            else if (sectorVolumeLookup.TryGetValue(chunkName, out var sv)
                     && sv.sector.mergedVoxelBuffer != null
                     && sv.bufferOffset + data.Length <= sv.sector.mergedVoxelBuffer.count)
            {
                voxelBuf = sv.sector.mergedVoxelBuffer;
                paneBuf = sv.sector.paneBuffer;
                indexBase = sv.bufferOffset;
            }
            else return false;

            // Occupancy targets: chunk keeps its own occ slice; sector placements
            // write into the sector's merged grid at occOffset. Lazy-build for
            // externally-registered chunks that had no CPU data at registration.
            ComputeBuffer occBuf = null; uint[] occCpu = null;
            int occOff = 0, occWords = 0, occBufBase = 0;
            int ow = 0, oh = 0, od = 0;
            if (chunkPath)
            {
                ow = chunk.dims.x; oh = chunk.dims.y; od = chunk.dims.z;
                if (chunk.occBuffer == null && data.Length == ow * oh * od)
                {
                    chunk.occData = BuildOccupancy(data, ow, oh, od, out chunk.occWords);
                    chunk.occBuffer = new ComputeBuffer(chunk.occWords, sizeof(uint));
                    chunk.occBuffer.SetData(chunk.occData);
                }
                occBuf = chunk.occBuffer; occCpu = chunk.occData; occWords = chunk.occWords;
            }
            else if (sectorVolumeLookup.TryGetValue(chunkName, out var svOcc))
            {
                ow = svOcc.w; oh = svOcc.h; od = svOcc.d;
                occBuf = svOcc.sector.occBuffer; occCpu = svOcc.sector.mergedOcc;
                occOff = svOcc.occOffset;
                int mdx = (ow + 3) >> OccShift, mdy = (oh + 3) >> OccShift, mdz = (od + 3) >> OccShift;
                occWords = (mdx * mdy * mdz + 31) >> 5;
                occBufBase = svOcc.occOffset;
            }

            dirtyIndices.Sort();

            // Pane bookkeeping: a cell whose material changed must leave its old
            // pane — a cracked (still-glass) cell keeping the intact pane's id is
            // deduped into the front surface and never renders. AIR cells → 0;
            // cells that stayed glass → one fresh id shared across this edit, so
            // the scar dedups as its own surface.
            bool hasPane = paneBuf != null && paneBuf != defaultPaneBuffer
                           && (long)indexBase + data.Length <= paneBuf.count;
            // Sector damage ids live at 0x8000_0000+ — above any authored
            // (b<<16 + localId) value — so they need no per-building base.
            uint dmgPane = 0;
            if (hasPane)
            {
                for (int i = 0; i < dirtyIndices.Count; i++)
                    if (IsGlassMaterial(data[dirtyIndices[i]] & 0x1FFu)) { dmgPane = NextDamagePaneId(chunkPath); break; }
            }

            int start = dirtyIndices[0], prev = start;
            for (int i = 1; i <= dirtyIndices.Count; i++)
            {
                if (i < dirtyIndices.Count && dirtyIndices[i] == prev + 1) { prev = dirtyIndices[i]; continue; }
                int len = prev - start + 1;
                voxelBuf.SetData(data, start, indexBase + start, len);
                if (hasPane)
                {
                    if (paneScratch.Length < len) paneScratch = new uint[len];
                    for (int k = 0; k < len; k++)
                        paneScratch[k] = IsGlassMaterial(data[start + k] & 0x1FFu) ? dmgPane : 0u;
                    paneBuf.SetData(paneScratch, 0, indexBase + start, len);
                }
                if (i < dirtyIndices.Count) start = prev = dirtyIndices[i];
            }

            if (occBuf != null && occCpu != null)
                UpdateOccupancy(data, ow, oh, od, dirtyIndices, occCpu, occOff, occWords, occBuf, occBufBase);
            return true;
        }

        private static uint NextDamagePaneId(bool chunkPath)
        {
            if (chunkPath)
            {
                uint id = nextDamagePaneId;
                if (++nextDamagePaneId > 0xFFFEu) nextDamagePaneId = 0x8000u;
                return id;
            }
            uint sid = nextSectorDamagePaneId;
            if (++nextSectorDamagePaneId > 0x8000_FFFEu) nextSectorDamagePaneId = 0x8000_0000u;
            return sid;
        }

        /// <summary>
        /// Full-buffer re-upload of a volume's voxel data — used by VoxelDamageSystem
        /// reset to restore a damaged volume to its pristine array. Works for both
        /// live chunks (whole-buffer write) and sector placements (region write at
        /// bufferOffset, pane ids rebuilt with the building's id base).
        /// </summary>
        public bool RefreshChunkVoxels(string chunkName, uint[] data)
        {
            if (chunkLookup.TryGetValue(chunkName, out var chunk) && chunk.voxelBuffer != null)
            {
                if (data == null || data.Length != chunk.voxelBuffer.count) return false;
                chunk.voxelBuffer.SetData(data);
                if (chunk.occData != null && chunk.occBuffer != null)
                {
                    chunk.occData = BuildOccupancy(data, chunk.dims.x, chunk.dims.y, chunk.dims.z, out chunk.occWords);
                    if (chunk.occBuffer.count != chunk.occWords)
                    { chunk.occBuffer.Release(); chunk.occBuffer = new ComputeBuffer(chunk.occWords, sizeof(uint)); }
                    chunk.occBuffer.SetData(chunk.occData);
                }

                // Restore pane ids too — damage edits overwrote them (AIR cells → 0,
                // cracked cells → fresh ids). The pristine array's pane map is cached
                // by reference from chunk creation, so this is a lookup, not a re-flood.
                if (chunk.paneBuffer != null && chunk.paneBuffer != defaultPaneBuffer
                    && chunk.paneBuffer.count == data.Length)
                {
                    var pane = GetPaneIDs(data, chunk.dims.x, chunk.dims.y, chunk.dims.z);
                    if (pane != null && pane.Length == data.Length)
                        chunk.paneBuffer.SetData(pane);
                }
                return true;
            }

            if (!sectorVolumeLookup.TryGetValue(chunkName, out var sv)
                || sv.sector.mergedVoxelBuffer == null
                || data == null || sv.bufferOffset + data.Length > sv.sector.mergedVoxelBuffer.count)
                return false;

            sv.sector.mergedVoxelBuffer.SetData(data, 0, sv.bufferOffset, data.Length);

            // Rebuild this building's occupancy slice (full-volume writes like
            // RESET DAMAGE don't go through UploadVoxelEdits' dirty spans).
            if (sv.sector.occBuffer != null && sv.sector.mergedOcc != null
                && data.Length == sv.w * sv.h * sv.d)
            {
                var bo = BuildOccupancy(data, sv.w, sv.h, sv.d, out int bw);
                Array.Copy(bo, 0, sv.sector.mergedOcc, sv.occOffset, bw);
                sv.sector.occBuffer.SetData(sv.sector.mergedOcc, sv.occOffset, sv.occOffset, bw);
            }

            // Rebuild this region's pane ids in merged space (localId + buildingIndex<<16).
            if (sv.sector.paneBuffer != null && sv.sector.paneBuffer != defaultPaneBuffer
                && sv.bufferOffset + data.Length <= sv.sector.paneBuffer.count)
            {
                var pane = ComputePaneIDs(data, sv.w, sv.h, sv.d);
                if (pane != null)
                {
                    var slice = new uint[data.Length];
                    for (int i = 0; i < pane.Length; i++)
                        slice[i] = pane[i] != 0 ? pane[i] + sv.paneIdBase : 0u;
                    sv.sector.paneBuffer.SetData(slice, 0, sv.bufferOffset, slice.Length);
                }
            }
            return true;
        }

        /// <summary>
        /// Clear all chunks.
        /// </summary>
        public void ClearAllChunks()
        {
            foreach (var chunk in chunks)
            {
                if (chunk.voxelBuffer != null) chunk.voxelBuffer.Release();
                if (chunk.paneBuffer != null && chunk.paneBuffer != defaultPaneBuffer)
                    chunk.paneBuffer.Release();
                if (chunk.occBuffer != null) { chunk.occBuffer.Release(); chunk.occBuffer = null; chunk.occData = null; }
                if (chunk.hostObject != null) Destroy(chunk.hostObject);
            }
            chunks.Clear();
            chunkLookup.Clear();
            ReleaseAllInstancedGroups();

            // Clear baked sectors
            foreach (var sector in bakedSectors)
            {
                if (sector.mergedVoxelBuffer != null) { sector.mergedVoxelBuffer.Release(); sector.mergedVoxelBuffer = null; }
                if (sector.paneBuffer != null && sector.paneBuffer != defaultPaneBuffer) { sector.paneBuffer.Release(); sector.paneBuffer = null; }
                if (sector.occBuffer != null) { sector.occBuffer.Release(); sector.occBuffer = null; sector.mergedOcc = null; }
                if (sector.buildingOccBuffer != null) { sector.buildingOccBuffer.Release(); sector.buildingOccBuffer = null; }
                if (sector.buildingMetaBuffer != null) { sector.buildingMetaBuffer.Release(); sector.buildingMetaBuffer = null; }
                if (sector.buildingPosBuffer != null) { sector.buildingPosBuffer.Release(); sector.buildingPosBuffer = null; }
            }
            bakedSectors.Clear();
            sectorLookup.Clear();
            sectorVolumeLookup.Clear();
        }

        // --- Instanced character/vehicle rendering ---
        // Each distinct .stasset asset gets its OWN shared voxel buffer + its OWN per-instance
        // offset buffer, drawn with its OWN DrawMeshInstanced call — one draw call PER ASSET TYPE,
        // not per instance. This lets citizens, hoods, and vehicles each use a different shape while
        // every instance of a given shape still batches into a single draw call.
        // Per-instance data (xyz = world position, w = yaw radians) is passed via a StructuredBuffer.
        // See docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md ("Tier 2: Batched Dynamic").

        public class InstancedCharacter
        {
            public GameObject gameObject;
            public Vector3 worldOffset;
            public float yaw;
            public bool visible = true;
            public string assetKey; // which InstancedGroup this instance belongs to
            // Animation state (voxel group system)
            public float animState;   // AnimState enum cast to float (0=Idle, 1=Walking, 2=Looking, 3=AimWalk, 4=Aiming, 5=Crouching, 6=Flinching, 7=Falling, 8=Down, 9=TPose)
            public float animTime;    // seconds since animation started
            public float animSpeed = 1.0f; // walk speed multiplier
            // Per-instance material remap (clothing system): regionId → materialId (0 = no remap)
            // Sized to match the group's maxRegions. Null = no remap for this instance.
            public uint[] materialRemap;
            // IK override channel (solver-driven, e.g. ArmAimSolver). Model-space
            // quaternion overrides for up to two group IDs; mirrors CPU ikOverrides.
            public bool ikEnabled;
            public float ikBlend;
            public int ikGidA = -1, ikGidB = -1;
            public Quaternion ikQuatA = Quaternion.identity, ikQuatB = Quaternion.identity;
            // Torso override (gid 0) — propagates through the body-transform path
            public bool ikTorsoEnabled;
            public Quaternion ikTorsoQuat = Quaternion.identity;
        }

        private class InstancedGroup
        {
            public ComputeBuffer sharedVoxelBuffer;   // rest voxel data (shared, read-only)
            public ComputeBuffer paneBuffer;          // per-voxel glass pane ids (0 = non-glass)
            public ComputeBuffer posedVoxelBuffer;    // posed voxel data (per-instance slices, GPU compute output)
            public ComputeBuffer groupIDBuffer;     // per-voxel groupID (animation groups)
            public ComputeBuffer instanceOffsetBuffer;
            public ComputeBuffer instanceAnimDataBuffer; // per-instance anim state (2 float4s each)
            public ComputeBuffer instanceIKDataBuffer;   // per-instance IK overrides (4 float4s each)
            public int dimX, dimY, dimZ;
            public float voxelSize;
            public readonly List<InstancedCharacter> instances = new();
            public MaterialPropertyBlock cachedPropBlock;
            // Walk keyframe data (per character type, shared by all instances)
            public ComputeBuffer walkKeyframeBuffer;  // 10 float4s (one per pose value, 4 KFs each)
            public ComputeBuffer jointConfigBuffer;   // 7 float4s (axis/sign/rest per joint pair)
            public Vector4 walkConfig;                // (cycleDuration, bodyBobAmp, weightShiftAmp, autoMirror)
            public bool walkKeyframesEnabled = false;
            // Authored per-model pivots (per character type, shared by all instances).
            // 10 float4s, indexed by groupID (0=body,1=head,2/3=arms,4/5=legs,6/7=shins,8/9=forearms).
            // xyz = normalized pivot fraction of dims (0.0-1.0), matching .anim.json "pivots" format.
            public ComputeBuffer pivotBuffer;
            public bool pivotsEnabled = false;
            // Static animation parameters (looking/aiming/crouching/jointOffset)
            public ComputeBuffer animStaticParamsBuffer; // 15 float4s
            public bool animStaticParamsEnabled = false;
            // GPU compute forward-transform
            public bool useComputePose = false;  // true when groupIDBuffer is available
            // Per-instance material remap (clothing system)
            public ComputeBuffer regionIDBuffer;          // [totalVoxels] per-voxel region ID (shared, read-only)
            public ComputeBuffer instanceMaterialRemapBuffer; // [maxInstances * MaxRegions] per-instance remap
            public int maxRegions = 0;                    // number of region IDs (0 = no region data)
            public bool materialRemapEnabled = false;     // true when regionIDBuffer + remap buffer exist
            public bool firstDrawLogged = false;          // one-shot emit diagnostic (D3D12 SRV triage)
        }

        private readonly Dictionary<string, InstancedGroup> instancedGroups = new();

        public InstancedCharacter RegisterInstancedCharacter(GameObject host, string assetFileName, float voxelSize, string subfolder = "voxel_characters")
        {
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
            {
                string path = System.IO.Path.Combine(Application.streamingAssetsPath, subfolder, assetFileName);
                if (!System.IO.File.Exists(path))
                {
                    Debug.LogError($"[VoxelChunkManager] Instanced asset not found: {path}");
                    return null;
                }

                // Detect consolidated .character.json vs legacy .stasset
                bool isJson = assetFileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase);

                ushort[,,] voxelData;
                uint[] groupIDs = null;
                Dictionary<string, int> regionMap = null;
                List<RegionDef> regionDefs = null;

                if (isJson)
                {
                    // Shared asset — the registry's single parse feeds the GPU
                    // buffers too (CHARACTER_ASSET_LIFECYCLE.md gotcha G1).
                    var asset = CharacterAssets.Get(assetFileName);
                    if (asset != null)
                    {
                        voxelData = asset.Voxels;
                        groupIDs = asset.GroupIDs;
                        regionMap = asset.Regions;
                        regionDefs = asset.RegionDefs;
                    }
                    else
                    {
                        voxelData = null;
                    }
                }
                else
                {
                    voxelData = StAssetReader.LoadVoxels(path);
                }

                if (voxelData == null)
                {
                    Debug.LogError($"[VoxelChunkManager] Failed to load instanced asset: {path}");
                    return null;
                }

                int dimX = voxelData.GetLength(0);
                int dimY = voxelData.GetLength(1);
                int dimZ = voxelData.GetLength(2);

                int totalVoxels = dimX * dimY * dimZ;
                var gpuData = new uint[totalVoxels];
                int idx = 0;
                for (int z = 0; z < dimZ; z++)
                    for (int y = 0; y < dimY; y++)
                        for (int x = 0; x < dimX; x++)
                            gpuData[idx++] = (uint)voxelData[x, y, z];

                group = new InstancedGroup { dimX = dimX, dimY = dimY, dimZ = dimZ, voxelSize = voxelSize };
                group.sharedVoxelBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
                group.sharedVoxelBuffer.SetData(gpuData);

                // Pane dedup for glass (vehicles: windshields/windows = 147)
                var instPaneIDs = GetPaneIDs(gpuData, dimX, dimY, dimZ);
                group.paneBuffer = instPaneIDs != null ? CreatePaneBuffer(instPaneIDs) : defaultPaneBuffer;

                // Build per-voxel region ID buffer if regions exist
                if (regionMap != null && regionMap.Count > 0)
                {
                    var regionIDs = new uint[totalVoxels];
                    foreach (var kvp in regionMap)
                    {
                        var parts = kvp.Key.Split(',');
                        if (parts.Length == 3 &&
                            int.TryParse(parts[0], out int rx) &&
                            int.TryParse(parts[1], out int ry) &&
                            int.TryParse(parts[2], out int rz))
                        {
                            int flat = rx + ry * dimX + rz * dimX * dimY;
                            if (flat >= 0 && flat < totalVoxels)
                                regionIDs[flat] = (uint)kvp.Value;
                        }
                    }
                    group.regionIDBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
                    group.regionIDBuffer.SetData(regionIDs);
                    int maxRid = 0;
                    if (regionDefs != null)
                        foreach (var rd in regionDefs)
                            if (rd.id > maxRid) maxRid = rd.id;
                    group.maxRegions = maxRid + 1;
                    group.materialRemapEnabled = true;
                    Debug.Log($"[VoxelChunkManager] Region data loaded: {assetFileName} — {regionMap.Count} region voxels, {group.maxRegions} regions (material remap enabled)");
                }

                // Load group IDs — from JSON directly, or from .groups binary file
                if (groupIDs == null && !isJson)
                {
                    string groupPath = path.Replace(".stasset", ".groups");
                    if (System.IO.File.Exists(groupPath))
                        groupIDs = LoadGroupIDs(groupPath, totalVoxels);
                }

                if (groupIDs != null)
                {
                    group.groupIDBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
                    group.groupIDBuffer.SetData(groupIDs);
                    group.useComputePose = true;
                    Debug.Log($"[VoxelChunkManager] Animation groups loaded: {assetFileName} — {totalVoxels:N0} voxels tagged with groupIDs (compute pose enabled)");
                }
                
                instancedGroups[assetFileName] = group;

                Debug.Log($"[VoxelChunkManager] Shared instanced buffer initialized: {assetFileName} {dimX}x{dimY}x{dimZ} = {totalVoxels:N0} voxels (shared across all instances of this asset)");
            }

            var ic = new InstancedCharacter
            {
                gameObject = host,
                worldOffset = host.transform.position,
                yaw = host.transform.rotation.eulerAngles.y * Mathf.Deg2Rad,
                assetKey = assetFileName
            };
            group.instances.Add(ic);
            EnsureGroupBuffers(group, assetFileName);
            return ic;
        }

        public void UnregisterInstancedCharacter(InstancedCharacter ic)
        {
            if (ic == null) return;
            if (instancedGroups.TryGetValue(ic.assetKey, out var group))
                group.instances.Remove(ic);
        }

        /// <summary>
        /// Set walk keyframe data for an instanced character type.
        /// All instances sharing the same assetFileName will use these keyframes.
        /// Call this after RegisterInstancedCharacter, before the first render frame.
        ///
        /// walkKeyframes: 10 Vector4s, one per pose value.
        ///   Index 0=armSwingL, 1=armSwingR, 2=legStrideL, 3=legStrideR,
        ///   4=elbowBendL, 5=elbowBendR, 6=kneeBendL, 7=kneeBendR,
        ///   8=forearmTwistL, 9=forearmTwistR
        ///   Each Vector4 = (kf0, kf1, kf2, kf3) — the 4 keyframe values for that pose.
        ///   When autoMirror is true, kf2 and kf3 should be pre-mirrored by the caller.
        ///
        /// jointConfig: 7 Vector4s:
        ///   [0] = (armAxisL, armAxisR, armSignL, armSignR)
        ///   [1] = (legAxisL, legAxisR, legSignL, legSignR)
        ///   [2] = (elbowAxisL, elbowAxisR, elbowSignL, elbowSignR)
        ///   [3] = (kneeAxisL, kneeAxisR, kneeSignL, kneeSignR)
        ///   [4] = (legTwistL, legTwistR, 0, 0)
        ///   [5] = (restPoseLArmZ, restPoseRArmZ, elbowRestL, elbowRestR)
        ///   [6] = (kneeRestL, kneeRestR, 0, 0)
        ///
        /// walkConfig = (cycleDuration, bodyBobAmplitude, weightShiftAmplitude, autoMirror(1/0))
        /// </summary>
        /// <summary>
        /// True when the live instanced group already holds pivots, walk/joint config and
        /// static params. Group lifetime != asset-cache lifetime (groups are rebuilt on
        /// scene reload), so uploads must be gated on THIS, never on a flag stored on the
        /// cached asset.
        /// </summary>
        public bool HasAnimUploads(string assetFileName)
        {
            return instancedGroups.TryGetValue(assetFileName, out var g)
                && g.walkKeyframesEnabled && g.walkKeyframeBuffer != null
                && g.jointConfigBuffer != null && g.pivotBuffer != null
                && g.animStaticParamsBuffer != null;
        }

        public void SetWalkKeyframes(string assetFileName, Vector4[] walkKeyframes, Vector4[] jointConfig, Vector4 walkConfig)
        {
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
            {
                Debug.LogWarning($"[VoxelChunkManager] SetWalkKeyframes: group not found for {assetFileName}");
                return;
            }

            if (walkKeyframes == null || walkKeyframes.Length != 10)
            {
                Debug.LogWarning($"[VoxelChunkManager] SetWalkKeyframes: expected 10 walkKeyframe entries, got {(walkKeyframes == null ? "null" : walkKeyframes.Length.ToString())}");
                return;
            }

            if (jointConfig == null || jointConfig.Length != 7)
            {
                Debug.LogWarning($"[VoxelChunkManager] SetWalkKeyframes: expected 7 jointConfig entries, got {(jointConfig == null ? "null" : jointConfig.Length.ToString())}");
                return;
            }

            // Release old buffers if they exist
            if (group.walkKeyframeBuffer != null) group.walkKeyframeBuffer.Release();
            if (group.jointConfigBuffer != null) group.jointConfigBuffer.Release();

            group.walkKeyframeBuffer = new ComputeBuffer(10, sizeof(float) * 4);
            group.walkKeyframeBuffer.SetData(walkKeyframes);

            group.jointConfigBuffer = new ComputeBuffer(7, sizeof(float) * 4);
            group.jointConfigBuffer.SetData(jointConfig);

            group.walkConfig = walkConfig;
            group.walkKeyframesEnabled = true;

            Debug.Log($"[VoxelChunkManager] Walk keyframes set for {assetFileName}: cycleDur={walkConfig.x}s bob={walkConfig.y} shift={walkConfig.z} autoMirror={walkConfig.w}");
        }

        /// <summary>
        /// Set authored per-model joint pivots for an instanced character type, read from the
        /// HTML animator's .anim.json "pivots" dict (auto-detected per-model in the editor).
        /// Overrides the shader's hardcoded fractional pivot approximation, which only works
        /// for proportions similar to the original hoodlum model (16x32x10).
        ///
        /// pivots: 10 Vector4s, indexed by groupID (xyz = normalized fraction of dims, 0.0-1.0):
        ///   0=body, 1=head, 2=left arm, 3=right arm, 4=left leg, 5=right leg,
        ///   6=left shin, 7=right shin, 8=left forearm, 9=right forearm.
        /// Missing groupIDs should be passed as Vector4.zero (shader treats zero pivot as
        /// volume-corner-relative, which callers should avoid by always supplying all 10 entries).
        /// </summary>
        public void SetPivots(string assetFileName, Vector4[] pivots)
        {
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
            {
                Debug.LogWarning($"[VoxelChunkManager] SetPivots: group not found for {assetFileName}");
                return;
            }

            if (pivots == null || pivots.Length != 10)
            {
                Debug.LogWarning($"[VoxelChunkManager] SetPivots: expected 10 pivot entries, got {(pivots == null ? "null" : pivots.Length.ToString())}");
                return;
            }

            if (group.pivotBuffer != null) group.pivotBuffer.Release();

            group.pivotBuffer = new ComputeBuffer(10, sizeof(float) * 4);
            group.pivotBuffer.SetData(pivots);
            group.pivotsEnabled = true;

            Debug.Log($"[VoxelChunkManager] Authored pivots set for {assetFileName} (10 groups)");
        }

        /// <summary>
        /// Set static animation parameters (looking/aiming/crouching/jointOffset) for an
        /// instanced character type. All instances sharing the same assetFileName use these.
        /// 12 Vector4s packed as described in the shader comments.
        /// </summary>
        public void SetAnimStaticParams(string assetFileName, Vector4[] animParams)
        {
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
            {
                Debug.LogWarning($"[VoxelChunkManager] SetAnimStaticParams: group not found for {assetFileName}");
                return;
            }

            if (animParams == null || animParams.Length != 15)
            {
                Debug.LogWarning($"[VoxelChunkManager] SetAnimStaticParams: expected 15 entries, got {(animParams == null ? "null" : animParams.Length.ToString())}");
                return;
            }

            if (group.animStaticParamsBuffer != null) group.animStaticParamsBuffer.Release();
            group.animStaticParamsBuffer = new ComputeBuffer(15, sizeof(float) * 4);
            group.animStaticParamsBuffer.SetData(animParams);
            group.animStaticParamsEnabled = true;

            Debug.Log($"[VoxelChunkManager] Anim static params set for {assetFileName} (13 float4s)");
        }

        /// <summary>
        /// Load a .groups file (STAG format, same layout as .stasset but uint16 groupIDs).
        /// Returns uint[] of groupID per voxel, or null if file invalid.
        /// </summary>
        private uint[] LoadGroupIDs(string path, int expectedVoxels)
        {
            if (!System.IO.File.Exists(path)) return null;

            byte[] data = System.IO.File.ReadAllBytes(path);
            if (data.Length < 16)
            {
                Debug.LogError($"[VoxelChunkManager] Groups file too small: {path}");
                return null;
            }

            // Check magic: STAG (ST Asset Groups)
            if (data[0] != (byte)'S' || data[1] != (byte)'T' ||
                data[2] != (byte)'A' || data[3] != (byte)'G')
            {
                Debug.LogError($"[VoxelChunkManager] Invalid groups magic: {path}");
                return null;
            }

            int gWidth  = data[6]  | (data[7]  << 8);
            int gHeight = data[8]  | (data[9]  << 8);
            int gDepth  = data[10] | (data[11] << 8);
            int gTotal = gWidth * gHeight * gDepth;

            if (gTotal != expectedVoxels)
            {
                Debug.LogError($"[VoxelChunkManager] Groups voxel count mismatch: {gTotal} vs {expectedVoxels} in {path}");
                return null;
            }

            var groupIDs = new uint[gTotal];
            int offset = 16;
            for (int i = 0; i < gTotal; i++)
            {
                groupIDs[i] = (uint)(data[offset] | (data[offset + 1] << 8));
                offset += 2;
            }

            return groupIDs;
        }

        private void RenderInstancedCharacters(CommandBuffer cmd, int shaderPass)
        {
            // Sync per-frame material-level uniforms (camera/lighting/etc.) from
            // proxyMaterial — per-draw values still come through each group's
            // MaterialPropertyBlock.
            if (groupMaterial != null && proxyMaterial != null)
                groupMaterial.CopyPropertiesFromMaterial(proxyMaterial);
            foreach (var group in instancedGroups.Values)
                RenderInstancedGroup(cmd, group, shaderPass);
        }

        private void RenderInstancedGroup(CommandBuffer cmd, InstancedGroup group, int shaderPass)
        {
            if (group.instances.Count == 0) return;
            // Glass pass only emits where a pane exists — skip glass-free groups
            if (shaderPass == 2 && (group.paneBuffer == null || group.paneBuffer == defaultPaneBuffer)) return;

            int visibleCount = 0;
            foreach (var ic in group.instances)
            {
                if (ic.gameObject == null || !ic.gameObject.activeInHierarchy) { ic.visible = false; continue; }
                ic.worldOffset = ic.gameObject.transform.position;
                ic.yaw = ic.gameObject.transform.rotation.eulerAngles.y * Mathf.Deg2Rad;
                ic.visible = true;
                visibleCount++;
            }

            if (visibleCount == 0) return;

            // Allocate buffers: 2x float4 per instance (position+yaw, animState+animTime+speed)
            int bufferElements = visibleCount * 2;
            var offsets = new Vector4[bufferElements];
            var matrices = new Matrix4x4[visibleCount];
            int writeIdx = 0;

            Vector3 size = new Vector3(group.dimX, group.dimY, group.dimZ) * group.voxelSize;
            // Pad proxy by +1 voxel on each axis to ensure full coverage at perspective grazing angles
            Vector3 pad = new Vector3(group.voxelSize, group.voxelSize, group.voxelSize);

            int totalVoxels = group.dimX * group.dimY * group.dimZ;
            bool useComputePose = group.groupIDBuffer != null && poseComputeShader != null && group.useComputePose;

            // Build per-instance offset buffer + anim data buffer
            // For compute pose: also build instanceAnimData (2 float4s per instance)
            var animData = useComputePose ? new Vector4[visibleCount * 2] : null;
            // Per-instance IK override data (4 float4s each: quatA, quatB, quatTorso, meta)
            var ikData = useComputePose ? new Vector4[visibleCount * 4] : null;

            foreach (var ic in group.instances)
            {
                if (!ic.visible) continue;
                offsets[writeIdx] = new Vector4(ic.worldOffset.x, ic.worldOffset.y, ic.worldOffset.z, ic.yaw);
                // Second float4: animation data
                offsets[writeIdx + visibleCount] = new Vector4(ic.animState, ic.animTime, ic.animSpeed, 0);
                // IMPORTANT: Keep proxy cube axis-aligned in world (no rotation). Shader handles volume rotation —
                // so the axis-aligned proxy must bound the YAWED footprint: rotating the XZ extents by the
                // instance yaw gives the exact conservative AABB. Cube-shaped volumes (characters) were
                // rotation-invariant, which is why this never mattered before elongated vehicles.
                float cosY = Mathf.Abs(Mathf.Cos(ic.yaw));
                float sinY = Mathf.Abs(Mathf.Sin(ic.yaw));
                Vector3 instSize = new Vector3(
                    size.x * cosY + size.z * sinY,
                    size.y,
                    size.x * sinY + size.z * cosY) + pad;
                // Rotation pivot is the volume center (volOffset + dims*voxelSize*0.5 in the shader),
                // so the proxy centers on the unpadded volume center.
                Vector3 centerPos = ic.worldOffset + size * 0.5f;
                matrices[writeIdx] = Matrix4x4.TRS(centerPos, Quaternion.identity, instSize);

                if (useComputePose)
                {
                    animData[writeIdx * 2] = new Vector4(ic.animState, ic.animTime, ic.animSpeed, 0);
                    animData[writeIdx * 2 + 1] = new Vector4(writeIdx * totalVoxels, 0, 0, 0); // posed buffer offset
                    ikData[writeIdx * 4] = new Vector4(ic.ikQuatA.x, ic.ikQuatA.y, ic.ikQuatA.z, ic.ikQuatA.w);
                    ikData[writeIdx * 4 + 1] = new Vector4(ic.ikQuatB.x, ic.ikQuatB.y, ic.ikQuatB.z, ic.ikQuatB.w);
                    ikData[writeIdx * 4 + 2] = new Vector4(ic.ikTorsoQuat.x, ic.ikTorsoQuat.y, ic.ikTorsoQuat.z, ic.ikTorsoQuat.w);
                    float ikFlags = (ic.ikEnabled ? 1f : 0f) + (ic.ikTorsoEnabled ? 2f : 0f);
                    ikData[writeIdx * 4 + 3] = new Vector4(ic.ikGidA, ic.ikGidB, ic.ikBlend, ikFlags);
                }
                writeIdx++;
            }

            if (group.instanceOffsetBuffer == null || group.instanceOffsetBuffer.count < bufferElements)
            {
                if (group.instanceOffsetBuffer != null) group.instanceOffsetBuffer.Release();
                group.instanceOffsetBuffer = new ComputeBuffer(Mathf.Max(bufferElements, 256), sizeof(float) * 4);
            }
            group.instanceOffsetBuffer.SetData(offsets, 0, 0, bufferElements);

            // --- GPU Compute Forward-Transform Pose ---
            // Skipped on the glass pass — the opaque call already dispatched it
            // this frame; pass 2 only re-emits the draw.
            if (useComputePose && shaderPass != 2)
            {
                // Ensure posed buffer is large enough
                int posedSize = totalVoxels * visibleCount;
                if (group.posedVoxelBuffer == null || group.posedVoxelBuffer.count < posedSize)
                {
                    if (group.posedVoxelBuffer != null) group.posedVoxelBuffer.Release();
                    group.posedVoxelBuffer = new ComputeBuffer(posedSize, sizeof(uint));
                    Debug.Log($"[VoxelChunkManager] Posed voxel buffer allocated: {posedSize} voxels for {visibleCount} instances");
                }

                // Ensure anim data buffer is large enough
                if (group.instanceAnimDataBuffer == null || group.instanceAnimDataBuffer.count < visibleCount * 2)
                {
                    if (group.instanceAnimDataBuffer != null) group.instanceAnimDataBuffer.Release();
                    group.instanceAnimDataBuffer = new ComputeBuffer(Mathf.Max(visibleCount * 2, 16), sizeof(float) * 4);
                }
                group.instanceAnimDataBuffer.SetData(animData, 0, 0, visibleCount * 2);

                // Ensure IK override buffer exists (always bound — meta.w=0 disables per instance)
                if (group.instanceIKDataBuffer == null || group.instanceIKDataBuffer.count < visibleCount * 4)
                {
                    if (group.instanceIKDataBuffer != null) group.instanceIKDataBuffer.Release();
                    group.instanceIKDataBuffer = new ComputeBuffer(Mathf.Max(visibleCount * 4, 16), sizeof(float) * 4);
                }
                group.instanceIKDataBuffer.SetData(ikData, 0, 0, visibleCount * 4);

                // Set compute shader params for CSPose kernel
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_RestVoxelData", group.sharedVoxelBuffer);
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_GroupIDs", group.groupIDBuffer);
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_InstanceAnimData", group.instanceAnimDataBuffer);
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_PosedVoxelData", group.posedVoxelBuffer);
                cmd.SetComputeIntParam(poseComputeShader, "_RestDimX", group.dimX);
                cmd.SetComputeIntParam(poseComputeShader, "_RestDimY", group.dimY);
                cmd.SetComputeIntParam(poseComputeShader, "_RestDimZ", group.dimZ);
                cmd.SetComputeIntParam(poseComputeShader, "_TotalRestVoxels", totalVoxels);
                cmd.SetComputeIntParam(poseComputeShader, "_InstanceCount", visibleCount);

                // Bind shared animation params
                bool hasPivotsCS = group.pivotsEnabled && group.pivotBuffer != null;
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_Pivots", hasPivotsCS ? group.pivotBuffer : dummyPivotBuffer);
                cmd.SetComputeIntParam(poseComputeShader, "_PivotsEnabled", hasPivotsCS ? 1 : 0);
                bool hasWalkCS = group.walkKeyframesEnabled && group.walkKeyframeBuffer != null;
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_WalkKeyframes", hasWalkCS ? group.walkKeyframeBuffer : dummyWalkKeyframeBuffer);
                cmd.SetComputeIntParam(poseComputeShader, "_WalkKeyframesEnabled", hasWalkCS ? 1 : 0);
                bool hasJointCS = hasWalkCS && group.jointConfigBuffer != null;
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_JointConfig", hasJointCS ? group.jointConfigBuffer : dummyJointConfigBuffer);
                cmd.SetComputeIntParam(poseComputeShader, "_JointConfigEnabled", hasJointCS ? 1 : 0);
                bool hasAnimCS = group.animStaticParamsEnabled && group.animStaticParamsBuffer != null;
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_AnimStaticParams", hasAnimCS ? group.animStaticParamsBuffer : dummyAnimStaticParamsBuffer);
                cmd.SetComputeIntParam(poseComputeShader, "_AnimStaticParamsEnabled", hasAnimCS ? 1 : 0);
                cmd.SetComputeVectorParam(poseComputeShader, "_WalkConfig", hasWalkCS ? group.walkConfig : Vector4.zero);
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_InstanceIKData", group.instanceIKDataBuffer);
                cmd.SetComputeIntParam(poseComputeShader, "_IKDataEnabled", 1);

                // Per-instance material remap (clothing system)
                bool hasRemap = group.materialRemapEnabled && group.regionIDBuffer != null;
                if (hasRemap)
                {
                    // Ensure remap buffer is large enough for current visible instance count
                    int remapSize = visibleCount * group.maxRegions;
                    if (group.instanceMaterialRemapBuffer == null || group.instanceMaterialRemapBuffer.count < remapSize)
                    {
                        if (group.instanceMaterialRemapBuffer != null) group.instanceMaterialRemapBuffer.Release();
                        group.instanceMaterialRemapBuffer = new ComputeBuffer(Mathf.Max(remapSize, 16), sizeof(uint));
                    }
                    // Build remap data array from per-instance materialRemap, in visible instance order
                    var remapData = new uint[Mathf.Max(remapSize, 16)];
                    int remapWriteIdx = 0;
                    foreach (var ic in group.instances)
                    {
                        if (!ic.visible) continue;
                        if (ic.materialRemap != null)
                        {
                            int copyLen = Mathf.Min(ic.materialRemap.Length, group.maxRegions);
                            System.Array.Copy(ic.materialRemap, 0, remapData, remapWriteIdx * group.maxRegions, copyLen);
                        }
                        remapWriteIdx++;
                    }
                    group.instanceMaterialRemapBuffer.SetData(remapData);
                }
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_RegionIDs",
                    hasRemap ? group.regionIDBuffer : dummyRegionIDBuffer);
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSPose, "_InstanceMaterialRemap",
                    hasRemap ? group.instanceMaterialRemapBuffer : dummyMaterialRemapBuffer);
                cmd.SetComputeIntParam(poseComputeShader, "_MaxRegions", hasRemap ? group.maxRegions : 0);
                cmd.SetComputeIntParam(poseComputeShader, "_MaterialRemapEnabled", hasRemap ? 1 : 0);

                // Also need to set params for CSClear kernel
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSClear, "_InstanceAnimData", group.instanceAnimDataBuffer);
                cmd.SetComputeBufferParam(poseComputeShader, kernelCSClear, "_PosedVoxelData", group.posedVoxelBuffer);
                cmd.SetComputeIntParam(poseComputeShader, "_RestDimX", group.dimX);
                cmd.SetComputeIntParam(poseComputeShader, "_RestDimY", group.dimY);
                cmd.SetComputeIntParam(poseComputeShader, "_RestDimZ", group.dimZ);
                cmd.SetComputeIntParam(poseComputeShader, "_InstanceCount", visibleCount);

                // Dispatch clear kernel: zero out posed buffer slices
                int clearGroups = Mathf.CeilToInt((float)totalVoxels / 64f);
                cmd.DispatchCompute(poseComputeShader, kernelCSClear, clearGroups, visibleCount, 1);

                // Dispatch pose kernel: 1 thread per rest voxel per instance
                int poseGroups = Mathf.CeilToInt((float)totalVoxels / 64f);
                cmd.DispatchCompute(poseComputeShader, kernelCSPose, poseGroups, visibleCount, 1);
            }

            // Use a MaterialPropertyBlock per group so each group's voxel buffer, dims,
            // and voxelSize are isolated. Without this, the shared proxyMaterial's
            // properties get overwritten by the last group drawn, causing earlier groups
            // to raymarch with wrong dims/voxelSize/buffer — voxels never get hit.
            if (group.cachedPropBlock == null)
                group.cachedPropBlock = new MaterialPropertyBlock();
            var block = group.cachedPropBlock;
            block.Clear();
            // When compute pose is active, bind posed buffer; otherwise bind rest buffer
            block.SetBuffer(propVoxelData, useComputePose ? group.posedVoxelBuffer : group.sharedVoxelBuffer);
            block.SetBuffer(propPaneIDs, group.paneBuffer ?? defaultPaneBuffer);
            block.SetInt(propOccEnabled, 0);   // character/prop groups: no occupancy grid
            block.SetBuffer(propOccData, defaultOccBuffer);
            block.SetBuffer(propBuildingOcc, dummyBuildingOccBuffer);
            block.SetBuffer(propBuildingMeta, sharedMaterialBuffer);      // unused — D3D12 SRV completeness
            block.SetBuffer(propBuildingPositions, sharedMaterialBuffer); // unused — D3D12 SRV completeness
            block.SetBuffer(propInstanceOffsets, group.instanceOffsetBuffer);
            block.SetBuffer(propMaterialColors, sharedMaterialBuffer);
            block.SetBuffer(propChunkTints, defaultTintBuffer);
            block.SetVector(propVolumeDims, new Vector4(group.dimX, group.dimY, group.dimZ, 0));
            block.SetFloat(propVoxelSize, group.voxelSize);

            block.SetInt(propMaxSteps, maxSteps);
            block.SetInt(propCheapShading, 1);
            block.SetInt(propUnlitLod, 0);
            block.SetInt(propLodDebugEnabled, 0);

            // When using compute pose, disable inverse-transform sampling in the shader.
            // The posed buffer already contains forward-transformed voxels.
            // When NOT using compute pose (no groupIDs), keep original behavior.
            bool hasGroups = group.groupIDBuffer != null && !useComputePose;
            block.SetInt(propGroupIDsEnabled, hasGroups ? 1 : 0);
            // Not compute-posed => the bound buffer is the single shared rest buffer: all instances read offset 0
            block.SetInt(propSharedRestBuffer, useComputePose ? 0 : 1);
            block.SetInt(propInstanceCount, visibleCount);
            block.SetBuffer(propGroupIDs, hasGroups ? group.groupIDBuffer : dummyGroupIDBuffer);

            // Walk keyframe bindings — always bind buffers (D3D12 requires SRV at declared index)
            // When using compute pose, these are still needed for body bob in the shader's DDA loop
            bool hasWalkKeyframes = group.walkKeyframesEnabled && group.walkKeyframeBuffer != null;
            block.SetInt(propWalkKeyframesEnabled, hasWalkKeyframes ? 1 : 0);
            block.SetBuffer(propWalkKeyframes, hasWalkKeyframes ? group.walkKeyframeBuffer : dummyWalkKeyframeBuffer);
            bool hasJointConfig = hasWalkKeyframes && group.jointConfigBuffer != null;
            block.SetInt(propJointConfigEnabled, hasJointConfig ? 1 : 0);
            block.SetBuffer(propJointConfig, hasJointConfig ? group.jointConfigBuffer : dummyJointConfigBuffer);
            block.SetVector(propWalkConfig, hasWalkKeyframes ? group.walkConfig : Vector4.zero);
            bool hasPivots = group.pivotsEnabled && group.pivotBuffer != null;
            block.SetInt(propPivotsEnabled, hasPivots ? 1 : 0);
            block.SetBuffer(propPivots, hasPivots ? group.pivotBuffer : dummyPivotBuffer);

            // Anim static params (looking/aiming/crouching/jointOffset)
            bool hasAnimParams = group.animStaticParamsEnabled && group.animStaticParamsBuffer != null;
            block.SetInt(propAnimStaticParamsEnabled, hasAnimParams ? 1 : 0);
            block.SetBuffer(propAnimStaticParams, hasAnimParams ? group.animStaticParamsBuffer : dummyAnimStaticParamsBuffer);

            if (!group.firstDrawLogged)
            {
                group.firstDrawLogged = true;
                var sb = new System.Text.StringBuilder();
                int nanMats = 0;
                for (int mi = 0; mi < matrices.Length; mi++)
                {
                    var m = matrices[mi];
                    if (float.IsNaN(m.m00) || float.IsNaN(m.m03) || float.IsNaN(m.m13) || float.IsNaN(m.m23)) nanMats++;
                    if (mi < 6) sb.AppendLine($"  [{mi}] pos={m.GetColumn(3)} scale={m.lossyScale}");
                }
                Debug.Log($"[VoxelChunkManager] Emitting instanced draw: {group.instances[0].assetKey} " +
                    $"x{visibleCount} pass={shaderPass} computePose={useComputePose} " +
                    $"voxelBufCount={(useComputePose ? group.posedVoxelBuffer : group.sharedVoxelBuffer)?.count ?? -1} " +
                    $"nanMats={nanMats}/{matrices.Length} instancing={groupMaterial.enableInstancing}\n{sb}");
            }

            cmd.DrawMeshInstanced(proxyCubeMesh, 0, groupMaterial, shaderPass, matrices, visibleCount, block);

            // No LOD settings to restore — using full quality for instanced characters/vehicles
        }

        private void ReleaseAllInstancedGroups()
        {
            foreach (var group in instancedGroups.Values)
            {
                if (group.sharedVoxelBuffer != null) group.sharedVoxelBuffer.Release();
                if (group.paneBuffer != null && group.paneBuffer != defaultPaneBuffer) group.paneBuffer.Release();
                if (group.posedVoxelBuffer != null) group.posedVoxelBuffer.Release();
                if (group.groupIDBuffer != null) group.groupIDBuffer.Release();
                if (group.instanceOffsetBuffer != null) group.instanceOffsetBuffer.Release();
                if (group.instanceAnimDataBuffer != null) group.instanceAnimDataBuffer.Release();
                if (group.instanceIKDataBuffer != null) group.instanceIKDataBuffer.Release();
                if (group.walkKeyframeBuffer != null) group.walkKeyframeBuffer.Release();
                if (group.jointConfigBuffer != null) group.jointConfigBuffer.Release();
                if (group.pivotBuffer != null) group.pivotBuffer.Release();
                if (group.animStaticParamsBuffer != null) group.animStaticParamsBuffer.Release();
                if (group.regionIDBuffer != null) group.regionIDBuffer.Release();
                if (group.instanceMaterialRemapBuffer != null) group.instanceMaterialRemapBuffer.Release();
                group.instances.Clear();
            }
            instancedGroups.Clear();
        }

        private void ReleaseAllChunks()
        {
            foreach (var chunk in chunks)
            {
                if (chunk.voxelBuffer != null) { chunk.voxelBuffer.Release(); chunk.voxelBuffer = null; }
                if (chunk.paneBuffer != null && chunk.paneBuffer != defaultPaneBuffer)
                { chunk.paneBuffer.Release(); chunk.paneBuffer = null; }
                if (chunk.occBuffer != null) { chunk.occBuffer.Release(); chunk.occBuffer = null; chunk.occData = null; }
                if (chunk.tintBuffer != null && chunk.tintBuffer != defaultTintBuffer)
                { chunk.tintBuffer.Release(); chunk.tintBuffer = null; }
                if (chunk.hostObject != null) { Destroy(chunk.hostObject); chunk.hostObject = null; }
            }
            chunks.Clear();
            chunkLookup.Clear();
            ReleaseAllInstancedGroups();

            // Release baked sectors
            foreach (var sector in bakedSectors)
            {
                if (sector.mergedVoxelBuffer != null) { sector.mergedVoxelBuffer.Release(); sector.mergedVoxelBuffer = null; }
                if (sector.paneBuffer != null && sector.paneBuffer != defaultPaneBuffer) { sector.paneBuffer.Release(); sector.paneBuffer = null; }
                if (sector.occBuffer != null) { sector.occBuffer.Release(); sector.occBuffer = null; sector.mergedOcc = null; }
                if (sector.buildingOccBuffer != null) { sector.buildingOccBuffer.Release(); sector.buildingOccBuffer = null; }
                if (sector.buildingMetaBuffer != null) { sector.buildingMetaBuffer.Release(); sector.buildingMetaBuffer = null; }
                if (sector.buildingPosBuffer != null) { sector.buildingPosBuffer.Release(); sector.buildingPosBuffer = null; }
            }
            bakedSectors.Clear();
            sectorLookup.Clear();
            if (sectorMaterial != null) { Destroy(sectorMaterial); sectorMaterial = null; }
        }

        #endregion

        // --- Sector baking (static building instancing) ---
        // Multiple buildings in a sector share one flat voxel buffer.
        // One DrawMeshInstanced call per sector replaces N DrawMesh calls.
        // Shader BUILDING_INSTANCING keyword reads per-building dims + buffer offset.

        public class BakedSector
        {
            public string name;
            public ComputeBuffer mergedVoxelBuffer;   // flat uint[] — all buildings concatenated
            public ComputeBuffer occBuffer;           // macro-cell occupancy bitmask, all buildings concatenated
            public uint[] mergedOcc;                  // CPU copy for incremental updates
            public int[] buildingOccOffsets;          // word offset per building into mergedOcc
            public ComputeBuffer buildingOccBuffer;   // uint[] occOffset per instance (_BuildingOcc)
            public ComputeBuffer paneBuffer;          // flat uint[] — per-voxel pane ids in merged index space
            public ComputeBuffer buildingMetaBuffer;   // float4[] (bufferOffset, dimsX, dimsY, dimsZ)
            public ComputeBuffer buildingPosBuffer;    // float4[] (worldOffsetX, Y, Z, 0)
            public Vector4[] cpuMeta;                  // CPU copy of buildingMeta for TRS computation
            public Vector4[] cpuPositions;             // CPU copy of buildingPositions
            public Matrix4x4[] cachedMatrices;         // pre-built TRS matrices (buildings never move)
            public int buildingCount;
            public float voxelSize;
            public Vector3 sectorMin;   // world-space AABB of sector
            public Vector3 sectorMax;
            public bool active = true;
            public MaterialPropertyBlock cachedPropBlock; // per-sector buffer bindings (CommandBuffer.DrawMeshInstanced does not snapshot Material.SetBuffer state)
        }

        private readonly List<BakedSector> bakedSectors = new();
        private readonly Dictionary<string, BakedSector> sectorLookup = new();
        private Material sectorMaterial; // clone of proxyMaterial with BUILDING_INSTANCING enabled

        public void RegisterSector(string name, uint[] mergedVoxelData,
            Vector4[] buildingMeta, Vector4[] buildingPositions,
            float sectorVoxelSize, Vector3 sectorMin, Vector3 sectorMax)
        {
            UnregisterSector(name);

            if (sectorMaterial == null)
            {
                sectorMaterial = new Material(proxyMaterial);
                sectorMaterial.enableInstancing = true;
                sectorMaterial.EnableKeyword("BUILDING_INSTANCING");
            }

            int buildingCount = buildingMeta.Length;
            var sector = new BakedSector
            {
                name = name,
                buildingCount = buildingCount,
                voxelSize = sectorVoxelSize,
                sectorMin = sectorMin,
                sectorMax = sectorMax,
                cpuMeta = buildingMeta,
                cpuPositions = buildingPositions
            };

            sector.mergedVoxelBuffer = new ComputeBuffer(mergedVoxelData.Length, sizeof(uint));
            sector.mergedVoxelBuffer.SetData(mergedVoxelData);

            // Per-building pane flood-fill into a merged pane array — pane ids
            // live in the same index space as mergedVoxelData so the shader's
            // lookup is bufferOffset + VoxelIndex (identical to _VoxelData).
            // Each building's ids are offset by b<<16 so panes of adjacent
            // buildings can't collide under the per-ray lastPane dedup.
            {
                uint[] mergedPane = null;
                for (int b = 0; b < buildingCount; b++)
                {
                    int bOff = (int)buildingMeta[b].x;
                    int bw = (int)buildingMeta[b].y, bh = (int)buildingMeta[b].z,
                        bd = (int)buildingMeta[b].w;
                    int len = bw * bh * bd;
                    var slice = new uint[len];
                    Array.Copy(mergedVoxelData, bOff, slice, 0, len);
                    var pane = ComputePaneIDs(slice, bw, bh, bd);
                    if (pane == null) continue;
                    if (mergedPane == null) mergedPane = new uint[mergedVoxelData.Length];
                    uint idBase = (uint)b << 16;
                    for (int i = 0; i < len; i++)
                        if (pane[i] != 0) mergedPane[bOff + i] = pane[i] + idBase;
                }
                if (mergedPane != null)
                    sector.paneBuffer = CreatePaneBuffer(mergedPane);
            }

            // Merged occupancy: per-building macro grids concatenated in building
            // order — the shader skips empty 4x4x4 regions during the DDA.
            {
                var occOffs = new int[buildingCount];
                int occTotal = 0;
                for (int b = 0; b < buildingCount; b++)
                {
                    int bw = (int)buildingMeta[b].y, bh = (int)buildingMeta[b].z, bd = (int)buildingMeta[b].w;
                    int mdx = (bw + 3) >> OccShift, mdy = (bh + 3) >> OccShift, mdz = (bd + 3) >> OccShift;
                    occOffs[b] = occTotal;
                    occTotal += (mdx * mdy * mdz + 31) >> 5;
                }
                sector.buildingOccOffsets = occOffs;
                sector.mergedOcc = new uint[occTotal];
                for (int b = 0; b < buildingCount; b++)
                {
                    int bOff = (int)buildingMeta[b].x;
                    int bw = (int)buildingMeta[b].y, bh = (int)buildingMeta[b].z, bd = (int)buildingMeta[b].w;
                    var slice = new uint[bw * bh * bd];
                    Array.Copy(mergedVoxelData, bOff, slice, 0, slice.Length);
                    var bo = BuildOccupancy(slice, bw, bh, bd, out int words);
                    Array.Copy(bo, 0, sector.mergedOcc, occOffs[b], words);
                }
                sector.occBuffer = new ComputeBuffer(occTotal, sizeof(uint));
                sector.occBuffer.SetData(sector.mergedOcc);
                sector.buildingOccBuffer = new ComputeBuffer(buildingCount, sizeof(uint));
                var occF = new uint[buildingCount];
                for (int b = 0; b < buildingCount; b++) occF[b] = (uint)occOffs[b];
                sector.buildingOccBuffer.SetData(occF);
            }

            sector.buildingMetaBuffer = new ComputeBuffer(buildingCount, sizeof(float) * 4);
            sector.buildingMetaBuffer.SetData(buildingMeta);

            sector.buildingPosBuffer = new ComputeBuffer(buildingCount, sizeof(float) * 4);
            sector.buildingPosBuffer.SetData(buildingPositions);

            // Pre-build TRS matrices once — buildings never move so no need to rebuild per frame
            sector.cachedMatrices = BuildSectorMatrices(buildingMeta, buildingPositions, buildingCount, sectorVoxelSize);

            bakedSectors.Add(sector);
            sectorLookup[name] = sector;

            Debug.Log($"[VoxelChunkManager] Registered baked sector '{name}': {buildingCount} buildings, {mergedVoxelData.Length:N0} voxels, bounds {sectorMin}..{sectorMax}");
        }

        public void UnregisterSector(string name)
        {
            if (!sectorLookup.TryGetValue(name, out var sector)) return;
            if (sector.mergedVoxelBuffer != null) { sector.mergedVoxelBuffer.Release(); sector.mergedVoxelBuffer = null; }
            if (sector.paneBuffer != null && sector.paneBuffer != defaultPaneBuffer) { sector.paneBuffer.Release(); sector.paneBuffer = null; }
            if (sector.buildingMetaBuffer != null) { sector.buildingMetaBuffer.Release(); sector.buildingMetaBuffer = null; }
            if (sector.buildingPosBuffer != null) { sector.buildingPosBuffer.Release(); sector.buildingPosBuffer = null; }
            bakedSectors.Remove(sector);
            sectorLookup.Remove(name);
            // Drop damage-write mappings pointing at the released buffers — a rebake
            // re-registers fresh placements via RegisterSectorVolume.
            var stale = new List<string>();
            foreach (var kv in sectorVolumeLookup)
                if (kv.Value.sector == sector) stale.Add(kv.Key);
            foreach (var k in stale) sectorVolumeLookup.Remove(k);
        }

        public int BakedSectorCount => bakedSectors.Count;
        public int BakedSectorBuildingCount
        {
            get
            {
                int total = 0;
                foreach (var s in bakedSectors) total += s.buildingCount;
                return total;
            }
        }

        private static Matrix4x4[] BuildSectorMatrices(Vector4[] meta, Vector4[] positions, int count, float sectorVoxelSize)
        {
            var matrices = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                int dx = (int)meta[i].y;
                int dy = (int)meta[i].z;
                int dz = (int)meta[i].w;

                float vsb = positions[i].w > 0f ? positions[i].w : sectorVoxelSize;
                Vector3 buildingSize = new Vector3(dx, dy, dz) * vsb;
                Vector3 voxelPad = new Vector3(vsb, vsb, vsb);
                Vector3 paddedSize = buildingSize + voxelPad;
                Vector3 paddedHalf = paddedSize * 0.5f;

                Vector3 worldOffset = new Vector3(positions[i].x, positions[i].y, positions[i].z);
                Vector3 centerPos = worldOffset + paddedHalf;
                matrices[i] = Matrix4x4.TRS(centerPos, Quaternion.identity, paddedSize);
            }
            return matrices;
        }

        private void RenderBakedSectors(CommandBuffer cmd, Camera cam, bool isOrtho, float orthoSize,
            float perspHalfHeight)
        {
            if (bakedSectors.Count == 0 || sectorMaterial == null) { perfSectorsDrawn = 0; return; }
            perfSectorsDrawn = 0;

            // Set shared lighting/material properties on sector material once
            sectorMaterial.SetBuffer(propMaterialColors, sharedMaterialBuffer);
            sectorMaterial.SetBuffer(propChunkTints, defaultTintBuffer);
            sectorMaterial.SetInt(propMaxSteps, maxSteps);
            sectorMaterial.SetInt(propCheapShading, 0);
            sectorMaterial.SetInt(propUnlitLod, 0);
            sectorMaterial.SetInt(propLodDebugEnabled, 0);
            sectorMaterial.SetInt(propIsOrthographic, isOrtho ? 1 : 0);
            sectorMaterial.SetVector(propScreenSize, new Vector4(renderWidth, renderHeight, 0, 0));
            sectorMaterial.SetVector(propLightDirection, lightDirection);
            sectorMaterial.SetFloat(propLightIntensity, lightIntensity);
            sectorMaterial.SetFloat(propAmbientIntensity, ambientIntensity);
            sectorMaterial.SetFloat(propFillIntensity, fillIntensity);
            sectorMaterial.SetVector(propLightColor, lightColor);
            sectorMaterial.SetInt(propSunLightEnabled, sunLightEnabled);
            sectorMaterial.SetInt(propAmbientEnabled, ambientEnabled);
            sectorMaterial.SetInt(propFillEnabled, fillEnabled);
            sectorMaterial.SetInt(propCamLightEnabled, camLightEnabled);
            sectorMaterial.SetInt(propShadowEnabled, shadowEnabled);
            sectorMaterial.SetFloat(propShadowNormalNudge, shadowNormalNudge);
            sectorMaterial.SetFloat(propShadowLightNudge, shadowLightNudge);
            sectorMaterial.SetInt(propShadowSkipSteps, shadowSkipSteps);
            sectorMaterial.SetInt(propShadowMaxSteps, shadowMaxSteps);
            sectorMaterial.SetInt(propMaterialCount, MaxMaterials);
            sectorMaterial.SetVector(propBackgroundColor, backgroundColor);
            sectorMaterial.SetMatrix(propProxyCamToWorld, cam.cameraToWorldMatrix);
            sectorMaterial.SetMatrix(propProxyInvProj, cam.projectionMatrix.inverse);
            sectorMaterial.SetVector(propProxyCamOrigin, cam.transform.position);

            // Prepare camera frustum for CPU frustum-culling of sector AABBs (when enabled)
            Plane[] frustumPlanes = null;
            if (!disableSectorCulling)
                frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);

            // Local helper: distance from a point to an AABB (0 if inside)
            static float DistanceToAABB(Vector3 p, Vector3 bmin, Vector3 bmax)
            {
                float dx = Mathf.Max(Mathf.Max(bmin.x - p.x, 0f), p.x - bmax.x);
                float dy = Mathf.Max(Mathf.Max(bmin.y - p.y, 0f), p.y - bmax.y);
                float dz = Mathf.Max(Mathf.Max(bmin.z - p.z, 0f), p.z - bmax.z);
                return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            // Collect visible sectors, then draw nearest-first — matches the
            // per-chunk path and lets early-Z reject occluded sector fragments.
            sectorDrawList.Clear();
            foreach (var sector in bakedSectors)
            {
                if (!sector.active || sector.buildingCount == 0) continue;

                // Sector AABB and culling
                float vs = sector.voxelSize;
                Vector3 center = (sector.sectorMin + sector.sectorMax) * 0.5f;
                Vector3 size = sector.sectorMax - sector.sectorMin;
                var sectorBounds = new Bounds(center, size);
                // Pad bounds slightly to avoid edge pop from numeric error and proxy-padding
                float pad = vs * 8f;
                sectorBounds.Expand(pad * 2f);

                if (!disableSectorCulling)
                {
                    if (!GeometryUtility.TestPlanesAABB(frustumPlanes, sectorBounds))
                    {
                        perfLodCulled++;
                        continue;
                    }

                    // Distance culling is perspective-only — in ortho the camera's
                    // vertical gap would cull sectors directly under the map camera.
                    if (!isOrtho && maxRenderDistance > 0f)
                    {
                        float distToBox = DistanceToAABB(cam.transform.position, sector.sectorMin, sector.sectorMax);
                        if (distToBox > (maxRenderDistance + pad))
                        {
                            perfLodCulled++;
                            continue;
                        }
                    }
                }

                float sortDist = DistanceToAABB(cam.transform.position, sector.sectorMin, sector.sectorMax);
                sectorDrawList.Add((sector, sortDist));
            }

            sectorDrawList.Sort((a, b) => a.dist.CompareTo(b.dist));

            foreach (var (sector, sortDist) in sectorDrawList)
            {
                // Per-building TRS matrices — pre-cached at RegisterSector time (buildings never move)
                var matrices = sector.cachedMatrices;

                // Bind sector-specific buffers via a per-sector MaterialPropertyBlock.
                // IMPORTANT: CommandBuffer.DrawMeshInstanced does NOT snapshot Material.SetBuffer
                // state at record time — it reads whatever is currently set on the material when
                // the command buffer executes. Since sectorMaterial is shared across all sectors,
                // calling sectorMaterial.SetBuffer(...) in this loop would make every sector draw
                // with the LAST sector's buffers. Use a cached property block per sector instead.
                if (sector.cachedPropBlock == null)
                    sector.cachedPropBlock = new MaterialPropertyBlock();
                var sectorBlock = sector.cachedPropBlock;
                sectorBlock.Clear();
                sectorBlock.SetBuffer(propVoxelData, sector.mergedVoxelBuffer);
                sectorBlock.SetBuffer(propPaneIDs, sector.paneBuffer ?? defaultPaneBuffer);
                sectorBlock.SetInt(propPaneMerged, 1); // pane ids live in merged index space (bufferOffset + local)
                sectorBlock.SetInt(propOccEnabled, sector.occBuffer != null ? 1 : 0);
                sectorBlock.SetBuffer(propOccData, sector.occBuffer ?? defaultOccBuffer);
                sectorBlock.SetBuffer(propBuildingOcc, sector.buildingOccBuffer ?? dummyBuildingOccBuffer);
                // D3D12 validates every declared SRV per draw — a null/released
                // buffer here is a skipped draw call, not a warning. Fall back
                // to the always-live shared float4 buffer rather than unbind.
                sectorBlock.SetBuffer(propBuildingMeta, sector.buildingMetaBuffer ?? sharedMaterialBuffer);
                sectorBlock.SetBuffer(propBuildingPositions, sector.buildingPosBuffer ?? sharedMaterialBuffer);
                // Non-instanced path reads _VoxelSize; instanced path uses per-instance voxel size from Varyings
                sectorBlock.SetFloat(propVoxelSize, sector.voxelSize);

                // Animation group bindings — sectors have no groups, but shader requires these SRVs
                sectorBlock.SetInt(propGroupIDsEnabled, 0);
                sectorBlock.SetInt(propInstanceCount, 0);
                sectorBlock.SetBuffer(propGroupIDs, dummyGroupIDBuffer);
                sectorBlock.SetInt(propWalkKeyframesEnabled, 0);
                sectorBlock.SetBuffer(propWalkKeyframes, dummyWalkKeyframeBuffer);
                sectorBlock.SetInt(propJointConfigEnabled, 0);
                sectorBlock.SetBuffer(propJointConfig, dummyJointConfigBuffer);
                sectorBlock.SetInt(propPivotsEnabled, 0);
                sectorBlock.SetBuffer(propPivots, dummyPivotBuffer);
                sectorBlock.SetInt(propAnimStaticParamsEnabled, 0);
                sectorBlock.SetBuffer(propAnimStaticParams, dummyAnimStaticParamsBuffer);

                // --- Per-sector screen-space LOD (same tiers as the chunk path) ---
                // Sectors are big, so screenRatio stays high — a sector spanning
                // half the screen correctly keeps near quality; only distant
                // small-coverage sectors drop tiers. The shader self-floors
                // _MaxSteps at each instance's cell-crossing bound, so the wins
                // here are cheap/unlit shading (skips shadow march + normal
                // blend), not march length.
                float boundsRadius = (sector.sectorMax - sector.sectorMin).magnitude * 0.5f;
                float distToCenter = Vector3.Distance(
                    (sector.sectorMin + sector.sectorMax) * 0.5f, cam.transform.position);
                float screenRatio = isOrtho
                    ? boundsRadius / Mathf.Max(orthoSize, 0.001f)
                    : boundsRadius / (Mathf.Max(distToCenter, 0.001f) * perspHalfHeight);

                bool forceUltra = debugForceAllBuildingsUltraLod;
                int lodSteps, cheapShading = 0, unlitLod = 0, lodDebugEnabled = 0;
                Color lodDebugColor = Color.white;

                if (forceUltra || screenRatio < lodUltraScreenRatio)
                {
                    lodSteps = Mathf.Clamp(lodUltraFarSteps, 8, maxSteps);
                    cheapShading = enableCheapShadingLod ? 1 : 0;
                    unlitLod = enableUnlitLod ? 1 : 0;
                    perfLodUltra++;
                }
                else if (screenRatio < lodFarScreenRatio)
                {
                    lodSteps = Mathf.Clamp(lodFarSteps, 8, maxSteps);
                    cheapShading = enableCheapShadingLod ? 1 : 0;
                    unlitLod = enableUnlitLod ? 1 : 0;
                    perfLodFar++;
                }
                else if (screenRatio < lodMidScreenRatio)
                {
                    lodSteps = Mathf.Clamp(lodMidSteps, 8, maxSteps);
                    cheapShading = enableCheapShadingLod ? 1 : 0;
                    perfLodMid++;
                }
                else
                {
                    lodSteps = maxSteps;
                    perfLodNear++;
                }

                if (debugColorizeLodTiers)
                {
                    lodDebugEnabled = 1;
                    if (forceUltra || screenRatio < lodUltraScreenRatio) lodDebugColor = new Color(1f, 0.15f, 0.15f);      // red
                    else if (screenRatio < lodFarScreenRatio) lodDebugColor = new Color(1f, 0.55f, 0f);                     // orange
                    else if (screenRatio < lodNearScreenRatio) lodDebugColor = new Color(1f, 0.9f, 0.1f);                   // yellow
                    else lodDebugColor = new Color(0.2f, 0.9f, 0.2f);                                                        // green
                }

                sectorBlock.SetInt(propMaxSteps, lodSteps);
                sectorBlock.SetInt(propCheapShading, cheapShading);
                sectorBlock.SetInt(propUnlitLod, unlitLod);
                sectorBlock.SetInt(propLodDebugEnabled, lodDebugEnabled);
                sectorBlock.SetVector(propLodDebugColor, lodDebugColor);

                cmd.DrawMeshInstanced(proxyCubeMesh, 0, sectorMaterial,
                    useGlassCompositePass ? 1 : 0, matrices, sector.buildingCount, sectorBlock);
                perfSectorsDrawn++;
            }
        }

        #region --- RENDERING ---

        private void EnsureRenderTargets()
        {
            // Size to camera viewport pixels, not full screen
            int viewportW = Mathf.Max(1, (int)(Screen.width * currentResolutionScale));
            int viewportH = Mathf.Max(1, (int)(Screen.height * currentResolutionScale));
            if (renderCamera != null)
            {
                var r = renderCamera.rect;
                viewportW = Mathf.Max(1, (int)(Screen.width * r.width * currentResolutionScale));
                viewportH = Mathf.Max(1, (int)(Screen.height * r.height * currentResolutionScale));
            }

            if (colorRT != null && colorRT.width == viewportW && colorRT.height == viewportH)
                return;

            ReleaseRenderTargets();

            // Color render target (ARGB32 for performance, HDR optional)
            colorRT = new RenderTexture(viewportW, viewportH, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear
            };
            colorRT.Create();

            // Depth render target (RFloat for per-pixel depth)
            depthRT = new RenderTexture(viewportW, viewportH, 0, RenderTextureFormat.RFloat)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            depthRT.Create();

            renderWidth = viewportW;
            renderHeight = viewportH;
        }

        private void ReleaseRenderTargets()
        {
            if (colorRT != null) { colorRT.Release(); colorRT = null; }
            if (depthRT != null) { depthRT.Release(); depthRT = null; }
            if (proxyRT != null) { proxyRT.Release(); proxyRT = null; }
        }

        /// <summary>
        /// Called by CityMap3D.OnRenderImage or manually each frame.
        /// Dispatches the raymarch shader for all active chunks and blits result.
        /// </summary>

        public void SetLighting(Vector3 dir, float intensity, float ambient, float fill, Color tint)
        {
            lightDirection = dir.normalized;
            lightIntensity = intensity;
            ambientIntensity = ambient;
            fillIntensity = fill;
            lightColor = tint;
        }

        public void SetShadowParams(float normalNudge, float lightNudge, int skipSteps, int maxSteps, int enabled)
        {
            shadowNormalNudge = normalNudge;
            shadowLightNudge = lightNudge;
            shadowSkipSteps = skipSteps;
            shadowMaxSteps = maxSteps;
            shadowEnabled = enabled;
        }

        public float GetShadowNormalNudge() => shadowNormalNudge;
        public float GetShadowLightNudge() => shadowLightNudge;
        public int GetShadowSkipSteps() => shadowSkipSteps;
        public int GetShadowMaxSteps() => shadowMaxSteps;
        public int GetShadowEnabled() => shadowEnabled;
        public Vector3 GetLightDirection() => lightDirection;

        public void SetLightingToggles(bool sun, bool ambient, bool fill, bool camLight)
        {
            sunLightEnabled = sun ? 1 : 0;
            ambientEnabled = ambient ? 1 : 0;
            fillEnabled = fill ? 1 : 0;
            camLightEnabled = camLight ? 1 : 0;
        }

        public bool GetSunLightEnabled() => sunLightEnabled == 1;
        public bool GetAmbientEnabled() => ambientEnabled == 1;
        public bool GetFillEnabled() => fillEnabled == 1;
        public bool GetCamLightEnabled() => camLightEnabled == 1;

        public bool GetUseProxyRender() => useProxyRender;
        public void SetUseProxyRender(bool v) => useProxyRender = v;

        private bool runtimeGranularLod = false;

        public void SetGranularLodMode(bool enabled)
        {
            runtimeGranularLod = enabled;
            if (enabled)
            {
                distanceCullingEnabled = false;
            }
            else
            {
                distanceCullingEnabled = true;
            }
        }

        // --- CPU timing tracking ---
        private float lastCpuCullMs;
        private float lastCpuDrawMs;
        private float lastCpuTotalMs;
        private int perfActiveChunks;
        private int perfDrawnChunks;
        private int perfSectorsDrawn;
        private int perfLodNear, perfLodMid, perfLodFar, perfLodUltra, perfLodCulled;
        private float perfMinScreenRatio = 1f, perfMaxScreenRatio = 0f, perfAvgScreenRatio = 0f;
        // Coverage and LOD debug metrics
        private float approxCoveragePct = 0f;
        private float avgLodSteps = 0f;

        // Public read-only access for GUI display
        public float CpuCullMs => lastCpuCullMs;
        public float CpuDrawMs => lastCpuDrawMs;
        public float CpuTotalMs => lastCpuTotalMs;
        public int PerfActiveChunks => perfActiveChunks;
        public int PerfDrawnChunks => perfDrawnChunks;
        public int PerfSectorsDrawn => perfSectorsDrawn;
        public int PerfTotalChunks => chunks.Count;
        public int PerfTotalDrawCalls => perfSectorsDrawn + CountActiveInstancedGroups();
        public int PerfLodNear => perfLodNear;
        public int PerfLodMid => perfLodMid;
        public int PerfLodFar => perfLodFar;
        public int PerfLodUltra => perfLodUltra;
        public int PerfLodCulled => perfLodCulled;
        public float PerfMinScreenRatio => perfMinScreenRatio;
        public float PerfMaxScreenRatio => perfMaxScreenRatio;
        public float PerfAvgScreenRatio => perfAvgScreenRatio;
        public float ApproxCoveragePct => approxCoveragePct;
        public float AvgLodSteps => avgLodSteps;
        public bool IsOrtho => renderCamera != null && renderCamera.orthographic;
        public float CameraOrthoSize => renderCamera != null && renderCamera.orthographic ? renderCamera.orthographicSize : 0f;
        public float CameraFov => renderCamera != null && !renderCamera.orthographic ? renderCamera.fieldOfView : 0f;
        public bool ShowOrthoHud => showOrthoHud;
        public int RenderWidth => renderWidth;
        public int RenderHeight => renderHeight;
        public int InstancedCharacterCount
        {
            get
            {
                int total = 0;
                foreach (var group in instancedGroups.Values) total += group.instances.Count;
                return total;
            }
        }
        public float CurrentResolutionScale => currentResolutionScale;

        /// <summary>Get the flat GPU voxel data array for an instanced asset (read-only copy).</summary>
        public uint[] GetSharedVoxelData(string assetFileName)
        {
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
                return null;
            var data = new uint[group.dimX * group.dimY * group.dimZ];
            group.sharedVoxelBuffer.GetData(data);
            return data;
        }

        /// <summary>Write updated voxel data back to the shared GPU buffer (material ID swaps).</summary>
        public void SetSharedVoxelData(string assetFileName, uint[] data)
        {
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
                return;
            group.sharedVoxelBuffer.SetData(data);
        }

        /// <summary>Get voxel dimensions for an instanced asset.</summary>
        public bool TryGetInstancedDims(string assetFileName, out int dimX, out int dimY, out int dimZ)
        {
            dimX = dimY = dimZ = 0;
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
                return false;
            dimX = group.dimX;
            dimY = group.dimY;
            dimZ = group.dimZ;
            return true;
        }

        /// <summary>Get the max region count for an instanced asset (0 if no region data).</summary>
        public int GetMaxRegions(string assetFileName)
        {
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
                return 0;
            return group.maxRegions;
        }

        /// <summary>Look up the InstancedCharacter for a given GameObject + asset key.</summary>
        public InstancedCharacter GetInstancedCharacter(string assetFileName, GameObject host)
        {
            if (!instancedGroups.TryGetValue(assetFileName, out var group))
                return null;
            foreach (var ic in group.instances)
                if (ic.gameObject == host) return ic;
            return null;
        }

        /// <summary>
        /// Set per-instance outfit remap for a specific instance.
        /// regionMaterials: regionId → newMaterialId (0 = keep original material).
        /// The remap is stored on the InstancedCharacter and uploaded to GPU each frame.
        /// </summary>
        public void SetInstanceOutfit(InstancedCharacter ic, Dictionary<int, ushort> regionMaterials)
        {
            if (ic == null) return;
            if (!instancedGroups.TryGetValue(ic.assetKey, out var group)) return;
            if (group.maxRegions <= 0) return;

            if (ic.materialRemap == null || ic.materialRemap.Length < group.maxRegions)
                ic.materialRemap = new uint[group.maxRegions];
            else
                System.Array.Clear(ic.materialRemap, 0, ic.materialRemap.Length);

            if (regionMaterials != null)
            {
                foreach (var kvp in regionMaterials)
                {
                    if (kvp.Key >= 0 && kvp.Key < group.maxRegions)
                        ic.materialRemap[kvp.Key] = (uint)kvp.Value;
                }
            }
        }

        /// <summary>Get the current outfit remap for an instance (or null if none set).</summary>
        public uint[] GetInstanceOutfit(InstancedCharacter ic)
        {
            return ic?.materialRemap;
        }

        /// <summary>Clear outfit remap for an instance (revert to original materials).</summary>
        public void ClearInstanceOutfit(InstancedCharacter ic)
        {
            if (ic == null) return;
            ic.materialRemap = null;
        }

        private int CountActiveInstancedGroups()
        {
            int count = 0;
            foreach (var g in instancedGroups.Values)
                if (g.instances.Count > 0) count++;
            return count;
        }

        // Call to emit a one-shot perf log (e.g. on key press)
        public void LogPerfSnapshot()
        {
            Debug.Log($"[Perf] total={chunks.Count} active={perfActiveChunks} drawn={perfDrawnChunks} sectorsDrawn={perfSectorsDrawn} totalDrawCalls={perfSectorsDrawn + CountActiveInstancedGroups()} LOD(N:{perfLodNear} M:{perfLodMid} F:{perfLodFar} U:{perfLodUltra} C:{perfLodCulled}) screenRatio(min:{perfMinScreenRatio:F4} max:{perfMaxScreenRatio:F4} avg:{perfAvgScreenRatio:F4}) render={renderWidth}x{renderHeight} proxy={useProxyRender} shadows={shadowEnabled} maxSteps={maxSteps} | CPU: cull={lastCpuCullMs:F2}ms draw={lastCpuDrawMs:F2}ms total={lastCpuTotalMs:F2}ms");
        }

        private int debugViewKeywordState = -1; // -1 = not yet synced

        // The heatmap counters compile into a separate shader variant
        // (VOXEL_DEBUG_VIEW), so the production variant carries no debug code.
        private void SyncDebugViewKeyword(bool on)
        {
            int state = on ? 1 : 0;
            if (state == debugViewKeywordState) return;
            debugViewKeywordState = state;
            if (on) Shader.EnableKeyword("VOXEL_DEBUG_VIEW");
            else Shader.DisableKeyword("VOXEL_DEBUG_VIEW");
        }

        public void RenderChunks()
        {
            // Runtime resolution cycling: press R to cycle through 0.5 → 0.65 → 0.75 → 1.0 → 0.5
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                // Debug keys — work in builds where inspector toggles can't be reached.
                // V chosen over F-keys: F6-F10 are all taken (cam/waypoints/freeze/stress/vehicles).
                if (kb.vKey.wasPressedThisFrame)
                {
                    iterVisMode = (IterVisMode)(((int)iterVisMode + 1) % 4);
                    Debug.Log($"[Vis] iterVisMode = {iterVisMode}");
                }
            }
            if (kb != null && kb.rKey.wasPressedThisFrame)
            {
                float[] scales = { 0.5f, 0.65f, 0.75f, 1.0f };
                int idx = 0;
                for (int i = 0; i < scales.Length; i++)
                {
                    if (Mathf.Abs(currentResolutionScale - scales[i]) < 0.02f) { idx = (i + 1) % scales.Length; break; }
                }
                currentResolutionScale = scales[idx];
                manualResolutionOverride = true;
                EnsureProxyRT();
                proxyMaterial.SetVector(propScreenSize, new Vector4(renderWidth, renderHeight, 0, 0));
                Debug.Log($"[DynRes] Manual: scale set to {currentResolutionScale:F2} | render={renderWidth}x{renderHeight}");
            }

            if (renderCamera == null || (chunks.Count == 0 && bakedSectors.Count == 0 && instancedGroups.Count == 0))
                return;

            float totalStart = Time.realtimeSinceStartup;

            if (useProxyRender && proxyMaterial != null && proxyCubeMesh != null)
            {
                RenderProxyChunks();
            }
            else
            {
                RenderComputeChunks();
            }

            lastCpuTotalMs = (Time.realtimeSinceStartup - totalStart) * 1000f;

            // Track perf data for GUI display (no auto-logging)
            perfActiveChunks = 0;
            foreach (var c in chunks) if (c.active) perfActiveChunks++;
            perfDrawnChunks = useProxyRender ? proxyDrawList.Count : perfActiveChunks;
            // perfSectorsDrawn is set inside RenderBakedSectors

            // Per-frame logging (every 30 frames)
            // Perf logging silenced — use P key for on-demand snapshots
            // if (Time.frameCount % 30 == 0)
            // {
            //     float fps = 1f / Time.smoothDeltaTime;
            //     bool isOrtho = renderCamera != null && renderCamera.orthographic;
            //     Debug.Log($"[PerfFrame] f={Time.frameCount} fps={fps:F0} drawn={perfDrawnChunks} LOD(N:{perfLodNear} M:{perfLodMid} F:{perfLodFar} U:{perfLodUltra} C:{perfLodCulled}) cov={(approxCoveragePct*100f):F0}% stepsAvg={avgLodSteps:F0} CPU={lastCpuTotalMs:F2}ms cull={lastCpuCullMs:F2}ms draw={lastCpuDrawMs:F2}ms ortho={isOrtho} res={renderWidth}x{renderHeight} scale={currentResolutionScale:F2}");
            // }

            // Auto perf snapshot on state change — silenced to reduce log noise
            // if (perfActiveChunks != perfLastActiveChunks || renderWidth != perfLastRenderW ||
            //     renderHeight != perfLastRenderH || useProxyRender != perfLastProxy)
            // {
            //     LogPerfSnapshot();
            //     perfLastActiveChunks = perfActiveChunks;
            //     perfLastRenderW = renderWidth;
            //     perfLastRenderH = renderHeight;
            //     perfLastProxy = useProxyRender;
            // }
        }

        /// <summary>
        /// Proxy-box fragment shader render path.
        /// Draws a scaled cube mesh per chunk into a depth-enabled render texture.
        /// Only pixels covered by each cube's screen footprint run the fragment shader.
        /// Off-screen cubes are frustum-culled by Unity's mesh pipeline.
        /// </summary>
        private void RenderProxyChunks()
        {
            EnsureProxyRT();
            // Set per-frame shader constants
            var camTransform = renderCamera.transform;
            var cameraToWorld = renderCamera.cameraToWorldMatrix;
            var invProj = renderCamera.projectionMatrix.inverse;

            proxyMaterial.SetInt(propMaterialCount, MaxMaterials);
            proxyMaterial.SetInt(propMaxSteps, maxSteps);
            proxyMaterial.SetVector(propBackgroundColor, backgroundColor);
            proxyMaterial.SetInt(propIsOrthographic, renderCamera.orthographic ? 1 : 0);
            proxyMaterial.SetInt(propCheapShading, 0);
            proxyMaterial.SetInt(propUnlitLod, 0);
            proxyMaterial.SetInt(propLodDebugEnabled, 0);
            proxyMaterial.SetVector(propLightDirection, lightDirection);
            proxyMaterial.SetFloat(propLightIntensity, lightIntensity);
            proxyMaterial.SetFloat(propAmbientIntensity, ambientIntensity);
            proxyMaterial.SetFloat(propFillIntensity, fillIntensity);
            proxyMaterial.SetVector(propLightColor, lightColor);
            proxyMaterial.SetFloat(propShadowNormalNudge, shadowNormalNudge);
            proxyMaterial.SetFloat(propShadowLightNudge, shadowLightNudge);
            proxyMaterial.SetInt(propShadowSkipSteps, shadowSkipSteps);
            proxyMaterial.SetInt(propShadowMaxSteps, shadowMaxSteps);
            proxyMaterial.SetInt(propShadowEnabled, shadowEnabled);
            proxyMaterial.SetInt(propSunLightEnabled, sunLightEnabled);
            proxyMaterial.SetInt(propAmbientEnabled, ambientEnabled);
            proxyMaterial.SetInt(propFillEnabled, fillEnabled);
            proxyMaterial.SetInt(propCamLightEnabled, camLightEnabled);
            proxyMaterial.SetMatrix(propProxyCamToWorld, cameraToWorld);
            proxyMaterial.SetMatrix(propProxyInvProj, invProj);
            proxyMaterial.SetVector(propScreenSize, new Vector4(renderWidth, renderHeight, 0, 0));
            proxyMaterial.SetVector(propProxyCamOrigin, camTransform.position);

            // Build sorted draw list with unified screen-space LOD
            // Works for both ortho and perspective — screen ratio = bounding sphere / view extent
            float cullStart = Time.realtimeSinceStartup;

            bool isOrtho = renderCamera.orthographic;
            float orthoSize = renderCamera.orthographicSize;
            float perspHalfHeight = isOrtho ? 0f : Mathf.Tan(renderCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(renderCamera);
            proxyDrawList.Clear();
            perfLodNear = perfLodMid = perfLodFar = perfLodUltra = perfLodCulled = 0;
            perfMinScreenRatio = 1f;
            perfMaxScreenRatio = 0f;
            float screenRatioSum = 0f;
            int screenRatioCount = 0;
            // Coverage union accumulator: cov = 1 - Π(1 - area_i)
            float coverageUnion = 0f;

            foreach (var chunk in chunks)
            {
                if (!chunk.active || chunk.voxelBuffer == null || !chunk.hasSolid)
                    continue;

                Vector3 chunkWorldPos = chunk.hostObject != null
                    ? chunk.hostObject.transform.position
                    : chunk.worldOffset;

                float vs = chunk.voxelSize;
                Vector3 tightCenter = chunkWorldPos + new Vector3(
                    (chunk.tightMinX + chunk.tightMaxX + 1) * vs * 0.5f,
                    (chunk.tightMinY + chunk.tightMaxY + 1) * vs * 0.5f,
                    (chunk.tightMinZ + chunk.tightMaxZ + 1) * vs * 0.5f);

                Vector3 tightSize = new Vector3(
                    (chunk.tightMaxX - chunk.tightMinX + 1) * vs,
                    (chunk.tightMaxY - chunk.tightMinY + 1) * vs,
                    (chunk.tightMaxZ - chunk.tightMinZ + 1) * vs);
                Bounds chunkBounds = new Bounds(tightCenter, tightSize);

                // Frustum cull — skipped for debug/test volumes (skipCulling)
                if (!chunk.skipCulling && !GeometryUtility.TestPlanesAABB(frustumPlanes, chunkBounds))
                {
                    perfLodCulled++;
                    continue;
                }

                float dist = Vector3.Distance(tightCenter, camTransform.position);

                // Perspective distance culling (Working mode)
                if (!chunk.skipCulling && !isOrtho && distanceCullingEnabled && maxRenderDistance > 0f && dist > maxRenderDistance)
                {
                    perfLodCulled++;
                    continue;
                }

                // Compute screen-space ratio: bounding sphere radius / view half-extent
                float boundsRadius = chunkBounds.extents.magnitude;
                float screenRatio;
                if (isOrtho)
                {
                    // Ortho: view extent = orthographicSize (half-height in world units)
                    screenRatio = boundsRadius / Mathf.Max(orthoSize, 0.001f);
                }
                else
                {
                    // Perspective: view extent = distance * tan(fov/2)
                    float viewExtent = Mathf.Max(dist, 0.001f) * perspHalfHeight;
                    screenRatio = boundsRadius / viewExtent;
                }

                // Screen-space culling — skip sub-pixel chunks (skipped for debug/test volumes)
                if (!chunk.skipCulling && screenRatio < lodCullScreenRatio)
                {
                    perfLodCulled++;
                    continue;
                }

                // Track stats
                perfMinScreenRatio = Mathf.Min(perfMinScreenRatio, screenRatio);
                perfMaxScreenRatio = Mathf.Max(perfMaxScreenRatio, screenRatio);
                screenRatioSum += screenRatio;
                screenRatioCount++;

                // Approximate per-chunk screen rect in viewport space and accumulate union
                // Build world AABB corners (axis-aligned) from chunkBounds
                Vector3 c = chunkBounds.center;
                Vector3 e = chunkBounds.extents;
                Vector3[] corners = new Vector3[8]
                {
                    new Vector3(c.x - e.x, c.y - e.y, c.z - e.z),
                    new Vector3(c.x + e.x, c.y - e.y, c.z - e.z),
                    new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
                    new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
                    new Vector3(c.x - e.x, c.y - e.y, c.z + e.z),
                    new Vector3(c.x + e.x, c.y - e.y, c.z + e.z),
                    new Vector3(c.x - e.x, c.y + e.y, c.z + e.z),
                    new Vector3(c.x + e.x, c.y + e.y, c.z + e.z)
                };

                // Project to normalized viewport (0..1 in screen space)
                float vxMin = 1f, vyMin = 1f, vxMax = 0f, vyMax = 0f;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 vp = renderCamera.WorldToViewportPoint(corners[i]);
                    vxMin = Mathf.Min(vxMin, vp.x);
                    vxMax = Mathf.Max(vxMax, vp.x);
                    vyMin = Mathf.Min(vyMin, vp.y);
                    vyMax = Mathf.Max(vyMax, vp.y);
                }

                // Intersect with camera rect to get coverage relative to the map viewport
                var r = renderCamera.rect; // in 0..1 screen space
                float ixMin = Mathf.Max(vxMin, r.xMin);
                float ixMax = Mathf.Min(vxMax, r.xMax);
                float iyMin = Mathf.Max(vyMin, r.yMin);
                float iyMax = Mathf.Min(vyMax, r.yMax);
                float iw = Mathf.Max(0f, ixMax - ixMin);
                float ih = Mathf.Max(0f, iyMax - iyMin);
                float rectAreaInScreen = iw * ih; // fraction of full screen
                float viewportAreaInScreen = r.width * r.height;
                float areaFrac = viewportAreaInScreen > 0f ? Mathf.Clamp01(rectAreaInScreen / viewportAreaInScreen) : 0f;

                // Union update: cov' = 1 - (1 - cov) * (1 - area)
                coverageUnion = 1f - (1f - coverageUnion) * (1f - areaFrac);

                proxyDrawList.Add((chunk, dist));
            }

            perfAvgScreenRatio = screenRatioCount > 0 ? screenRatioSum / screenRatioCount : 0f;
            proxyDrawList.Sort((a, b) => a.dist.CompareTo(b.dist)); // nearest first — enables GPU early-Z rejection

            lastCpuCullMs = (Time.realtimeSinceStartup - cullStart) * 1000f;

            // Estimate coverage and adjust dynamic resolution before issuing draws
            approxCoveragePct = Mathf.Clamp01(coverageUnion * CoverageHeuristicScale);
            int drawnCount = proxyDrawList.Count;

            // Skip auto-resolution if user manually set via R key
            if (!manualResolutionOverride)
            {
            // Hysteresis for drawn-based decision
            bool wantHalfByDrawn = drawnCount >= DrawnHalfResThreshold;
            if (!wantHalfByDrawn && currentResolutionScale < 0.75f)
            {
                // Currently half-res: only return to full when comfortably below return threshold
                wantHalfByDrawn = !(drawnCount <= DrawnHalfResReturn);
            }

            // Small-coverage also prefers half-res (cheap win when only tiny area visible)
            bool wantHalfByCoverage = approxCoveragePct < LowCoverageThreshold;

            float targetScale = (wantHalfByDrawn || wantHalfByCoverage) ? 0.5f : 1.0f;
            // Never upscale above the configured base resolutionScale
            targetScale = Mathf.Min(targetScale, Mathf.Clamp(resolutionScale, 0.25f, 1.0f));
            if (Mathf.Abs(targetScale - currentResolutionScale) > 0.001f)
            {
                float old = currentResolutionScale;
                currentResolutionScale = targetScale;
                EnsureProxyRT();
                // Update screen size for new RT
                proxyMaterial.SetVector(propScreenSize, new Vector4(renderWidth, renderHeight, 0, 0));
                Debug.Log($"[DynRes] scale {old:F2} -> {currentResolutionScale:F2} | drawn={drawnCount} cov={(approxCoveragePct*100f):F0}%");
            }
            } // end if (!manualResolutionOverride)

            float drawStart = Time.realtimeSinceStartup;

            // Clear and draw via CommandBuffer
            var cmd = new CommandBuffer { name = "VoxelProxyRaymarch" };
            cmd.SetRenderTarget(proxyRT);
            cmd.ClearRenderTarget(true, true, backgroundColor);
            // Set view/projection matrices so UNITY_MATRIX_VP works in vertex shader
            cmd.SetViewProjectionMatrices(renderCamera.worldToCameraMatrix, renderCamera.projectionMatrix);

            int stepsAccum = 0;
            int opaquePass = useGlassCompositePass ? 1 : 0;

            Shader.SetGlobalFloat(propIterVis, (float)iterVisMode);
            SyncDebugViewKeyword(iterVisMode != IterVisMode.Off);
            Shader.SetGlobalInt("_GlassLayerVis", debugGlassLayers ? 1 : 0);

            foreach (var (chunk, dist) in proxyDrawList)
            {
                Vector3 chunkWorldPos = chunk.hostObject != null
                    ? chunk.hostObject.transform.position
                    : chunk.worldOffset;
                Quaternion chunkRot = chunk.hostObject != null
                    ? chunk.hostObject.transform.rotation
                    : Quaternion.identity;
                float vs = chunk.voxelSize;

                // Tight AABB size in world space
                Vector3 tightSize = new Vector3(
                    (chunk.tightMaxX - chunk.tightMinX + 1) * vs,
                    (chunk.tightMaxY - chunk.tightMinY + 1) * vs,
                    (chunk.tightMaxZ - chunk.tightMinZ + 1) * vs);

                // Tight AABB center in world space
                Vector3 tightCenter = chunkWorldPos + new Vector3(
                    (chunk.tightMinX + chunk.tightMaxX + 1) * vs * 0.5f,
                    (chunk.tightMinY + chunk.tightMaxY + 1) * vs * 0.5f,
                    (chunk.tightMinZ + chunk.tightMaxZ + 1) * vs * 0.5f);

                // TRS matrix: position at tight center, scale to tight size, apply rotation
                Matrix4x4 trs = Matrix4x4.TRS(tightCenter, chunkRot, tightSize);

                // Reuse cached MaterialPropertyBlock — avoids per-chunk per-frame allocation
                if (chunk.cachedPropBlock == null)
                    chunk.cachedPropBlock = new MaterialPropertyBlock();
                var block = chunk.cachedPropBlock;
                block.Clear();
                block.SetBuffer(propVoxelData, chunk.voxelBuffer);
                block.SetBuffer(propPaneIDs, chunk.paneBuffer ?? defaultPaneBuffer);
                block.SetInt(propOccEnabled, chunk.occBuffer != null ? 1 : 0);
                block.SetBuffer(propOccData, chunk.occBuffer ?? defaultOccBuffer);
                block.SetBuffer(propBuildingOcc, dummyBuildingOccBuffer);
                block.SetBuffer(propBuildingMeta, sharedMaterialBuffer);      // unused — D3D12 SRV completeness
                block.SetBuffer(propBuildingPositions, sharedMaterialBuffer); // unused — D3D12 SRV completeness
                block.SetBuffer(propMaterialColors, sharedMaterialBuffer);
                block.SetBuffer(propChunkTints, chunk.tintBuffer ?? defaultTintBuffer);
                // Animation group bindings — non-instanced chunks have no groups, but shader requires these SRVs
                block.SetInt(propGroupIDsEnabled, 0);
                block.SetInt(propInstanceCount, 0);
                block.SetBuffer(propGroupIDs, dummyGroupIDBuffer);
                block.SetInt(propWalkKeyframesEnabled, 0);
                block.SetBuffer(propWalkKeyframes, dummyWalkKeyframeBuffer);
                block.SetInt(propJointConfigEnabled, 0);
                block.SetBuffer(propJointConfig, dummyJointConfigBuffer);
                block.SetInt(propPivotsEnabled, 0);
                block.SetBuffer(propPivots, dummyPivotBuffer);
                block.SetInt(propAnimStaticParamsEnabled, 0);
                block.SetBuffer(propAnimStaticParams, dummyAnimStaticParamsBuffer);
                block.SetVector(propVolumeDims, new Vector4(chunk.dims.x, chunk.dims.y, chunk.dims.z, 0));
                block.SetFloat(propVoxelSize, vs);
                block.SetVector(propVolumeOffset, chunkWorldPos);
                // Tight content AABB (voxel units, Max exclusive) — the DDA marches
                // this region instead of the full container, skipping empty space
                // above flat content. Unset on other paths = full container bounds.
                block.SetVector(propTightBoundsMin, new Vector4(chunk.tightMinX, chunk.tightMinY, chunk.tightMinZ, 0));
                block.SetVector(propTightBoundsMax, new Vector4(chunk.tightMaxX + 1, chunk.tightMaxY + 1, chunk.tightMaxZ + 1, 0));

                // --- Unified screen-space LOD ---
                // Compute screen ratio from the same formula used in culling.
                // This replaces the old distance-based LOD that was disabled in ortho.
                float boundsRadius = new Vector3(
                    (chunk.tightMaxX - chunk.tightMinX + 1) * vs * 0.5f,
                    (chunk.tightMaxY - chunk.tightMinY + 1) * vs * 0.5f,
                    (chunk.tightMaxZ - chunk.tightMinZ + 1) * vs * 0.5f).magnitude;

                float screenRatio;
                if (isOrtho)
                    screenRatio = boundsRadius / Mathf.Max(orthoSize, 0.001f);
                else
                    screenRatio = boundsRadius / (Mathf.Max(dist, 0.001f) * perspHalfHeight);

                bool forceUltra = debugForceAllBuildingsUltraLod;

                int lodSteps;
                int cheapShading = 0;
                int unlitLod = 0;
                int lodDebugEnabled = 0;
                Color lodDebugColor = Color.white;

                // Assign LOD tier based on screen-space ratio
                // Near: full quality | Mid: cheap shading | Far: unlit | Ultra: minimal steps
                if (forceUltra || screenRatio < lodUltraScreenRatio)
                {
                    lodSteps = Mathf.Clamp(lodUltraFarSteps, 8, maxSteps);
                    cheapShading = enableCheapShadingLod ? 1 : 0;
                    unlitLod = enableUnlitLod ? 1 : 0;
                    perfLodUltra++;
                }
                else if (screenRatio < lodFarScreenRatio)
                {
                    lodSteps = Mathf.Clamp(lodFarSteps, 8, maxSteps);
                    cheapShading = enableCheapShadingLod ? 1 : 0;
                    unlitLod = enableUnlitLod ? 1 : 0;
                    perfLodFar++;
                }
                else if (screenRatio < lodMidScreenRatio)
                {
                    lodSteps = Mathf.Clamp(lodMidSteps, 8, maxSteps);
                    cheapShading = enableCheapShadingLod ? 1 : 0;
                    perfLodMid++;
                }
                else
                {
                    lodSteps = maxSteps;
                    perfLodNear++;
                }

                if (debugLodTiers && Time.frameCount % 60 == 0)
                {
                    string tier = forceUltra ? "ultra(forced)" : screenRatio < lodUltraScreenRatio ? "ultra" : screenRatio < lodFarScreenRatio ? "far" : screenRatio < lodNearScreenRatio ? "mid" : "near";
                    Debug.Log($"[LOD] {chunk.name} screenRatio={screenRatio:F4} tier={tier} steps={lodSteps} cheapShading={cheapShading} unlitLod={unlitLod}");
                }

                // Debug: solid-tint by tier so LOD boundaries are visually obvious
                if (debugColorizeLodTiers)
                {
                    lodDebugEnabled = 1;
                    if (forceUltra || screenRatio < lodUltraScreenRatio) lodDebugColor = new Color(1f, 0.15f, 0.15f);      // red
                    else if (screenRatio < lodFarScreenRatio) lodDebugColor = new Color(1f, 0.55f, 0f);                     // orange
                    else if (screenRatio < lodNearScreenRatio) lodDebugColor = new Color(1f, 0.9f, 0.1f);                   // yellow
                    else lodDebugColor = new Color(0.2f, 0.9f, 0.2f);                                                        // green
                }

                // Floor the step budget at the voxel-cell crossing bound — the
                // DDA counts plane crossings, so worst case is sx+sy+sz cells,
                // not the Euclidean diagonal. Shader also self-floors; this keeps
                // the CPU-side intent explicit.
                int cellSteps = (chunk.tightMaxX - chunk.tightMinX + 1)
                              + (chunk.tightMaxY - chunk.tightMinY + 1)
                              + (chunk.tightMaxZ - chunk.tightMinZ + 1) + 4;
                block.SetInt(propMaxSteps, Mathf.Max(lodSteps, cellSteps));
                block.SetInt(propCheapShading, cheapShading);
                block.SetInt(propUnlitLod, unlitLod);
                block.SetInt(propLodDebugEnabled, lodDebugEnabled);
                block.SetVector(propLodDebugColor, lodDebugColor);

                stepsAccum += lodSteps;

                Matrix4x4 localToWorld = Matrix4x4.Rotate(chunkRot);
                Matrix4x4 worldToLocal = Matrix4x4.Rotate(Quaternion.Inverse(chunkRot));
                block.SetMatrix(propVolumeRotation, worldToLocal);
                block.SetMatrix(propVolumeInvRotation, localToWorld);

                chunk.lastDrawTRS = trs;
                cmd.DrawMesh(proxyCubeMesh, trs, proxyMaterial, 0, opaquePass, block);
            }

            // Draw all instanced characters in a single DrawMeshInstanced call
            RenderInstancedCharacters(cmd, opaquePass);

            // Draw all baked sectors (static buildings) — far LOD keeps the
            // legacy combined path even in composite mode
            RenderBakedSectors(cmd, renderCamera, isOrtho, orthoSize, perspHalfHeight);

            if (useGlassCompositePass)
            {
                // Sectors with glass get the same composite treatment — far→near
                // (sectorDrawList is sorted nearest-first) so nearer panes blend
                // over farther ones. Buildings generally sit behind chunks and
                // characters, so they emit first.
                if (sectorMaterial != null)
                {
                    for (int i = sectorDrawList.Count - 1; i >= 0; i--)
                    {
                        var sector = sectorDrawList[i].sector;
                        if (sector.paneBuffer == null || sector.paneBuffer == defaultPaneBuffer)
                            continue;   // no glass — nothing to blend
                        cmd.DrawMeshInstanced(proxyCubeMesh, 0, sectorMaterial, 2,
                            sector.cachedMatrices, sector.buildingCount, sector.cachedPropBlock);
                    }
                }

                // Glass composite pass: re-draw glass-bearing volumes far→near
                // (list is sorted nearest-first) so every pane alpha-blends
                // over the opaque scene — including nearer glass already blended.
                for (int i = proxyDrawList.Count - 1; i >= 0; i--)
                {
                    var chunk = proxyDrawList[i].chunk;
                    if (chunk.paneBuffer == null || chunk.paneBuffer == defaultPaneBuffer)
                        continue;   // no glass — nothing to blend
                    cmd.DrawMesh(proxyCubeMesh, chunk.lastDrawTRS, proxyMaterial, 0, 2, chunk.cachedPropBlock);
                }
                RenderInstancedCharacters(cmd, 2);
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Dispose();

            lastCpuDrawMs = (Time.realtimeSinceStartup - drawStart) * 1000f;
            avgLodSteps = proxyDrawList.Count > 0 ? (float)stepsAccum / proxyDrawList.Count : 0f;
        }

        private void EnsureProxyRT()
        {
            int viewportW = Mathf.Max(1, (int)(Screen.width * currentResolutionScale));
            int viewportH = Mathf.Max(1, (int)(Screen.height * currentResolutionScale));
            if (renderCamera != null)
            {
                var r = renderCamera.rect;
                viewportW = Mathf.Max(1, (int)(Screen.width * r.width * currentResolutionScale));
                viewportH = Mathf.Max(1, (int)(Screen.height * r.height * currentResolutionScale));
            }

            if (proxyRT != null && proxyRT.width == viewportW && proxyRT.height == viewportH)
                return;

            if (proxyRT != null) proxyRT.Release();

            proxyRT = new RenderTexture(viewportW, viewportH, 24, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear
            };
            proxyRT.Create();

            renderWidth = viewportW;
            renderHeight = viewportH;
        }

        /// <summary>
        /// Original compute shader render path — dispatches MobSimVoxelRaymarch.compute per chunk.
        /// </summary>
        private void RenderComputeChunks()
        {
            if (raymarchShader == null || renderCamera == null || chunks.Count == 0)
                return;


            EnsureRenderTargets();

            // Clear color and depth targets
            var prevRT = RenderTexture.active;
            Graphics.SetRenderTarget(colorRT);
            GL.Clear(false, true, backgroundColor);
            Graphics.SetRenderTarget(depthRT);
            GL.Clear(false, true, new Color(float.MaxValue, 0, 0, 0));
            Graphics.SetRenderTarget(null);

            // Set per-dispatch constants
            raymarchShader.SetInt(propMaterialCount, MaxMaterials);
            raymarchShader.SetInt(propMaxSteps, maxSteps);
            raymarchShader.SetVector(propBackgroundColor, backgroundColor);
            raymarchShader.SetInt(propIsOrthographic, renderCamera.orthographic ? 1 : 0);
            raymarchShader.SetVector(propLightDirection, lightDirection);
            raymarchShader.SetFloat(propLightIntensity, lightIntensity);
            raymarchShader.SetFloat(propAmbientIntensity, ambientIntensity);
            raymarchShader.SetFloat(propFillIntensity, fillIntensity);
            raymarchShader.SetVector(propLightColor, lightColor);
            raymarchShader.SetFloat(propShadowNormalNudge, shadowNormalNudge);
            raymarchShader.SetFloat(propShadowLightNudge, shadowLightNudge);
            raymarchShader.SetInt(propShadowSkipSteps, shadowSkipSteps);
            raymarchShader.SetInt(propShadowMaxSteps, shadowMaxSteps);
            raymarchShader.SetInt(propShadowEnabled, shadowEnabled);
            raymarchShader.SetInt(propSunLightEnabled, sunLightEnabled);
            raymarchShader.SetInt(propAmbientEnabled, ambientEnabled);
            raymarchShader.SetInt(propFillEnabled, fillEnabled);
            raymarchShader.SetInt(propCamLightEnabled, camLightEnabled);
            raymarchShader.SetInts(propScreenSize, new int[] { renderWidth, renderHeight });

            // Camera matrices — use cameraToWorldMatrix (view space -Z forward) not TRS (transform +Z forward)
            var camTransform = renderCamera.transform;
            var cameraToWorld = renderCamera.cameraToWorldMatrix;
            var worldToCamera = cameraToWorld.inverse;
            var invProj = renderCamera.projectionMatrix.inverse;

            raymarchShader.SetVector(propCameraOrigin, camTransform.position);
            raymarchShader.SetMatrix(propCameraToWorld, cameraToWorld);
            raymarchShader.SetMatrix(propInvProjection, invProj);

            // Set shared material buffer
            raymarchShader.SetBuffer(kernelCSRaymarch, propMaterialColors, sharedMaterialBuffer);

            // Thread groups
            int threadX = Mathf.CeilToInt(renderWidth / 8f);
            int threadY = Mathf.CeilToInt(renderHeight / 8f);

            // Dispatch per chunk
            foreach (var chunk in chunks)
            {
                if (!chunk.active || chunk.voxelBuffer == null) continue;

                // Read live world position from host GameObject (Editor-adjustable)
                Vector3 chunkWorldPos = chunk.hostObject != null
                    ? chunk.hostObject.transform.position
                    : chunk.worldOffset;

                // Read live rotation from host GameObject
                Quaternion chunkRot = chunk.hostObject != null
                    ? chunk.hostObject.transform.rotation
                    : Quaternion.identity;

                // Per-chunk voxel size (buildings=0.1, characters=0.01)
                float chunkVoxelSize = chunk.voxelSize;

                // Frustum cull: skip chunks behind camera or outside view
                Vector3 chunkCenter = chunkWorldPos + new Vector3(
                    chunk.dims.x * chunkVoxelSize * 0.5f,
                    chunk.dims.y * chunkVoxelSize * 0.5f,
                    chunk.dims.z * chunkVoxelSize * 0.5f);
                Vector3 toCenter = chunkCenter - camTransform.position;
                if (Vector3.Dot(toCenter, camTransform.forward) < -chunk.dims.x * chunkVoxelSize)
                    continue; // Behind camera

                // Compute rotation matrices for shader
                // _VolumeRotation = world-to-local (inverse), _VolumeInvRotation = local-to-world
                Matrix4x4 localToWorld = Matrix4x4.Rotate(chunkRot);
                Matrix4x4 worldToLocal = Matrix4x4.Rotate(Quaternion.Inverse(chunkRot));

                // Set per-chunk parameters
                raymarchShader.SetInts(propVolumeDims, new int[] { chunk.dims.x, chunk.dims.y, chunk.dims.z });
                raymarchShader.SetVector(propVolumeOffset, chunkWorldPos);
                raymarchShader.SetMatrix(propVolumeRotation, worldToLocal);
                raymarchShader.SetMatrix(propVolumeInvRotation, localToWorld);
                raymarchShader.SetFloat(propVoxelSize, chunkVoxelSize);
                raymarchShader.SetBuffer(kernelCSRaymarch, propVoxelData, chunk.voxelBuffer);
                raymarchShader.SetBuffer(kernelCSRaymarch, propChunkTints, chunk.tintBuffer ?? defaultTintBuffer);

                // Set render targets
                raymarchShader.SetTexture(kernelCSRaymarch, propOutput, colorRT);
                raymarchShader.SetTexture(kernelCSRaymarch, propDepthBuffer, depthRT);

                // Dispatch
                raymarchShader.Dispatch(kernelCSRaymarch, threadX, threadY, 1);
            }

            RenderTexture.active = prevRT;
        }

        /// <summary>
        /// Get the composited color render texture for blitting to screen.
        /// </summary>
        public RenderTexture GetColorTexture()
        {
            return useProxyRender ? proxyRT : colorRT;
        }

        /// <summary>
        /// Blit the raymarched result to the given destination render texture.
        /// Call this from CityMap3D.OnRenderImage when in voxel mode.
        /// </summary>
        public void BlitToScreen(RenderTexture dest)
        {
            var rt = useProxyRender ? proxyRT : colorRT;
            if (rt == null) return;
            Graphics.Blit(rt, dest);
        }

        #endregion

        #region --- DEBUG ---

        private void OnDrawGizmos()
        {
            if (!debugChunkBounds) return;
            foreach (var chunk in chunks)
            {
                if (chunk.voxelBuffer == null) continue;
                var size = new Vector3(chunk.dims.x, chunk.dims.y, chunk.dims.z) * chunk.voxelSize;
                var center = chunk.worldOffset + size * 0.5f;
                Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
                Gizmos.DrawWireCube(center, size);
            }
        }

        #endregion
    }
}
