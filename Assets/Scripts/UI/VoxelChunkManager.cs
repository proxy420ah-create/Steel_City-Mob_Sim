using System.Collections.Generic;
using System.IO;
using UnityEngine;

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

        [Header("Render Settings")]
        [Tooltip("Voxel size in world units (should match CityMap3D.voxelSize).")]
        [SerializeField] private float voxelSize = 0.1f;
        [Tooltip("Max DDA steps per ray (safety cap). 256 is fine for 96-voxel chunks.")]
        [SerializeField] private int maxSteps = 256;
        [Tooltip("Render texture resolution scale relative to screen.")]
        [SerializeField] private float resolutionScale = 0.75f;
        [Tooltip("Background color when rays miss all chunks. Alpha=0 for transparent (shows scene behind).")]
        [SerializeField] private Color backgroundColor = new(0f, 0f, 0f, 0f);

        [Header("Debug")]
        [SerializeField] private bool debugChunkBounds = false;

        // --- Chunk data structure ---
        private class VoxelChunk
        {
            public string name;
            public GameObject hostObject;    // World-space GameObject (like Steel Tide's VoxelObject)
            public ComputeBuffer voxelBuffer;    // uint[] packed voxels
            public ComputeBuffer materialBuffer; // float4[] color lookup (shared)
            public ComputeBuffer tintBuffer;     // float4[] per-material tint (per-chunk)
            public VoxelInt3 dims;               // voxel dimensions
            public Vector3 worldOffset;          // cached world-space position
            public Quaternion rotation;            // cached world-space rotation
            public float voxelSize;              // per-chunk voxel size (buildings=0.1, characters=0.015)
            public bool active;
        }

        private readonly List<VoxelChunk> chunks = new();
        private readonly Dictionary<string, VoxelChunk> chunkLookup = new();

        // --- Render targets ---
        private RenderTexture colorRT;
        private RenderTexture depthRT;
        private int renderWidth;
        private int renderHeight;

        // --- Material color lookup (shared) ---
        private ComputeBuffer sharedMaterialBuffer;
        private static readonly int MaxMaterials = 130; // matches StAssetReader.MaterialCount
        private ComputeBuffer defaultTintBuffer; // all (1,1,1,1) — used when no custom tint set

        // --- Fullscreen quad for displaying the render texture ---
        private GameObject displayQuad;
        private Material displayMaterial;

        // --- Camera reference (set by CityMap3D) ---
        private Camera renderCamera;

        // --- Shader kernel IDs ---
        private int kernelCSRaymarch;

        // --- Shader property IDs ---
        private int propVoxelData, propOutput, propDepthBuffer, propMaterialColors;
        private int propMaterialCount, propVolumeDims, propVoxelSize, propVolumeOffset;
        private int propVolumeRotation, propVolumeInvRotation;
        private int propCameraOrigin, propCameraToWorld, propInvProjection;
        private int propScreenSize, propMaxSteps, propBackgroundColor, propIsOrthographic;
        private int propLightDirection, propLightIntensity, propAmbientIntensity, propFillIntensity, propLightColor;
        private int propChunkTints;
        private int propShadowNormalNudge, propShadowLightNudge, propShadowSkipSteps, propShadowMaxSteps, propShadowEnabled;
        private int propSunLightEnabled, propAmbientEnabled, propFillEnabled, propCamLightEnabled;

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
        private int shadowEnabled = 1;

        // --- Lighting debug toggles ---
        private int sunLightEnabled = 1;
        private int ambientEnabled = 1;
        private int fillEnabled = 1;
        private int camLightEnabled = 1;

        #region --- LIFECYCLE ---

        void Awake()
        {
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
            kernelCSRaymarch = raymarchShader.FindKernel("CSRaymarch");
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
            ReleaseAllChunks();
            ReleaseRenderTargets();
            if (sharedMaterialBuffer != null) { sharedMaterialBuffer.Release(); sharedMaterialBuffer = null; }
            if (defaultTintBuffer != null) { defaultTintBuffer.Release(); defaultTintBuffer = null; }
            if (displayMaterial != null) { Destroy(displayMaterial); displayMaterial = null; }
            if (displayQuad != null) { Destroy(displayQuad); displayQuad = null; }
        }

        #endregion

        #region --- INITIALIZATION ---

        private void CacheShaderIDs()
        {
            propVoxelData = Shader.PropertyToID("_VoxelData");
            propOutput = Shader.PropertyToID("_Output");
            propDepthBuffer = Shader.PropertyToID("_DepthBuffer");
            propMaterialColors = Shader.PropertyToID("_MaterialColors");
            propMaterialCount = Shader.PropertyToID("_MaterialCount");
            propVolumeDims = Shader.PropertyToID("_VolumeDims");
            propVoxelSize = Shader.PropertyToID("_VoxelSize");
            propVolumeOffset = Shader.PropertyToID("_VolumeOffset");
            propVolumeRotation = Shader.PropertyToID("_VolumeRotation");
            propVolumeInvRotation = Shader.PropertyToID("_VolumeInvRotation");
            propCameraOrigin = Shader.PropertyToID("_CameraOrigin");
            propCameraToWorld = Shader.PropertyToID("_CameraToWorld");
            propInvProjection = Shader.PropertyToID("_InvProjection");
            propScreenSize = Shader.PropertyToID("_ScreenSize");
            propMaxSteps = Shader.PropertyToID("_MaxSteps");
            propBackgroundColor = Shader.PropertyToID("_BackgroundColor");
            propIsOrthographic = Shader.PropertyToID("_IsOrthographic");
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

            var voxels = StAssetReader.LoadVoxels(stassetPath);
            if (voxels == null) return;

            int w = voxels.GetLength(0);
            int h = voxels.GetLength(1);
            int d = voxels.GetLength(2);

            // Pack ushort[,,] into uint[] (each voxel is 16-bit, stored as uint for shader)
            int voxelCount = w * h * d;
            var packedData = new uint[voxelCount];
            int idx = 0;
            for (int z = 0; z < d; z++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        packedData[idx++] = (uint)voxels[x, y, z];
                    }
                }
            }

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

            chunks.Add(chunk);
            chunkLookup[name] = chunk;

            Debug.Log($"[VoxelChunkManager] Loaded chunk '{name}': {w}x{h}x{d} voxels at {worldPos} (voxelSize={customVoxelSize})");
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
            var voxels = StAssetReader.LoadVoxels(filepath);
            if (voxels == null) return null;

            int w = voxels.GetLength(0);
            int h = voxels.GetLength(1);
            int d = voxels.GetLength(2);

            // Pack ushort[,,] into uint[] (single pass, no double file read)
            int voxelCount = w * h * d;
            var packedData = new uint[voxelCount];
            int idx = 0;
            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        packedData[idx++] = (uint)voxels[x, y, z];

            // Offset so the CENTER of the voxel volume sits at centerPos
            Vector3 cornerPos = centerPos - new Vector3(w * customVoxelSize * 0.5f, 0f, d * customVoxelSize * 0.5f);

            LoadChunkFromData(name, packedData, w, h, d, cornerPos, customVoxelSize);

            return new BuildingFootprint
            {
                center = centerPos,
                size = new Vector3(w * customVoxelSize, h * customVoxelSize, d * customVoxelSize),
                dims = new VoxelInt3(w, h, d)
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

            chunks.Add(chunk);
            chunkLookup[name] = chunk;

            Debug.Log($"[VoxelChunkManager] Loaded procedural chunk '{name}': {w}x{h}x{d} at {worldPos} (voxelSize={customVoxelSize})");
        }

        /// <summary>
        /// Remove a chunk by name, releasing its GPU buffer and destroying its GameObject.
        /// </summary>
        public void RemoveChunk(string name)
        {
            if (!chunkLookup.TryGetValue(name, out var chunk)) return;
            if (chunk.voxelBuffer != null) chunk.voxelBuffer.Release();
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
                materialBuffer = sharedMaterialBuffer,
                tintBuffer = defaultTintBuffer,
                dims = new VoxelInt3(dimsX, dimsY, dimsZ),
                worldOffset = host.transform.position,
                rotation = host.transform.rotation,
                voxelSize = customVoxelSize,
                active = true
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

        /// <summary>
        /// Clear all chunks.
        /// </summary>
        public void ClearAllChunks()
        {
            foreach (var chunk in chunks)
            {
                if (chunk.voxelBuffer != null) chunk.voxelBuffer.Release();
                if (chunk.hostObject != null) Destroy(chunk.hostObject);
            }
            chunks.Clear();
            chunkLookup.Clear();
        }

        private void ReleaseAllChunks()
        {
            foreach (var chunk in chunks)
            {
                if (chunk.voxelBuffer != null) { chunk.voxelBuffer.Release(); chunk.voxelBuffer = null; }
                if (chunk.tintBuffer != null && chunk.tintBuffer != defaultTintBuffer)
                { chunk.tintBuffer.Release(); chunk.tintBuffer = null; }
                if (chunk.hostObject != null) { Destroy(chunk.hostObject); chunk.hostObject = null; }
            }
            chunks.Clear();
            chunkLookup.Clear();
        }

        #endregion

        #region --- RENDERING ---

        private void EnsureRenderTargets()
        {
            // Size to camera viewport pixels, not full screen
            int viewportW = Mathf.Max(1, (int)(Screen.width * resolutionScale));
            int viewportH = Mathf.Max(1, (int)(Screen.height * resolutionScale));
            if (renderCamera != null)
            {
                var r = renderCamera.rect;
                viewportW = Mathf.Max(1, (int)(Screen.width * r.width * resolutionScale));
                viewportH = Mathf.Max(1, (int)(Screen.height * r.height * resolutionScale));
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
        }

        /// <summary>
        /// Called by CityMap3D.OnRenderImage or manually each frame.
        /// Dispatches the raymarch shader for all active chunks and blits result.
        /// </summary>
        private bool hasLoggedRender = false;

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

        public void RenderChunks()
        {
            if (raymarchShader == null || renderCamera == null || chunks.Count == 0)
                return;

            if (!hasLoggedRender)
            {
                Debug.Log($"[VoxelChunkManager] RenderChunks STARTED: {chunks.Count} chunks, " +
                    $"camera ortho={renderCamera.orthographic}, " +
                    $"renderTarget={renderWidth}x{renderHeight}, " +
                    $"voxelSize={voxelSize}, maxSteps={maxSteps}");
                foreach (var c in chunks)
                    Debug.Log($"[VoxelChunkManager]   Chunk '{c.name}': dims={c.dims.x}x{c.dims.y}x{c.dims.z} " +
                        $"pos={c.hostObject?.transform.position} buffer={c.voxelBuffer?.count ?? 0}");
                hasLoggedRender = true;
            }

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

                // Per-chunk voxel size (buildings=0.1, characters=0.015)
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
            return colorRT;
        }

        /// <summary>
        /// Blit the raymarched result to the given destination render texture.
        /// Call this from CityMap3D.OnRenderImage when in voxel mode.
        /// </summary>
        public void BlitToScreen(RenderTexture dest)
        {
            if (colorRT == null) return;
            Graphics.Blit(colorRT, dest);
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
