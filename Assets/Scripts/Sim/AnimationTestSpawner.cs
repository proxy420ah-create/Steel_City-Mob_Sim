using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// CPU forward-transform animation test rig.
    /// Loads .stasset/.json, poses on CPU, uploads per-frame.
    /// NOTE: For GPU instanced clothing tests, use ClothingTestSpawner instead.
    ///
    /// Hotkeys: W=Walk, T=TPose, I=Idle, L=Look, A=Aim, C=Crouch,
    ///          Space=Play/Pause, +/-=Speed, R=Reload
    /// </summary>
    public class AnimationTestSpawner : MonoBehaviour
    {
        [Header("Test Asset")]
        [Tooltip("Base filename in StreamingAssets/voxel_characters/ (without extension).")]
        [SerializeField] private string assetBaseName = "Civilian1";

        [Header("Rendering")]
        [Tooltip("Voxel size in world units. Must match authoring.")]
        [SerializeField] private float voxelSize = 0.01f;
        [Tooltip("VoxelChunkManager for raymarch rendering. Auto-found if not assigned.")]
        [SerializeField] private VoxelChunkManager chunkManager;

        [Header("City Integration")]
        [SerializeField] private CityMap3D cityMap;

        [Header("Ground Probe")]
        [SerializeField] private float probeStartHeight = 50f;
        [SerializeField] private float probeMaxDistance = 100f;
        [SerializeField] private float fallbackY = 0f;

        [Header("Posed Grid")]
        [Tooltip("Padding around rest-pose bounding box for the posed voxel grid.")]
        [SerializeField] private int posedGridPadding = 8;

        // ---- Runtime state ----
        private VoxelCharacterAnimator animator;
        private ushort[,,] restGrid;
        private int[,,] groupGrid;
        private Vector3Int restDims;

        private Vector3Int[] restPositions;
        private int[] groupIDs;
        private Vector3[] posedPositions;

        private Vector3Int posedDims;
        private Vector3Int posedOrigin;
        private uint[] posedPackedData;
        private ComputeBuffer posedVoxelBuffer;

        private string posedVolumeName;

        private float animState = 9f; // Start in T-Pose
        private float animTime = 0f;
        private float animSpeed = 1f;
        private bool isPlaying = true;
        private bool filesLoaded = false;

        private VoxelCollisionWorld collisionWorld;
        private Transform mapRoot;

        private static readonly string[] STATE_NAMES = {
            "Idle", "Walking", "Looking", "AimWalk", "Aiming",
            "Crouching", "???", "???", "Down", "T-Pose"
        };

        void Start()
        {
            if (cityMap == null)
                cityMap = FindFirstObjectByType<CityMap3D>();
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            collisionWorld = FindFirstObjectByType<VoxelCollisionWorld>();
            mapRoot = cityMap != null ? cityMap.MapRoot : null;

            StartCoroutine(DelayedSpawn());
        }

        private System.Collections.IEnumerator DelayedSpawn()
        {
            yield return null;
            yield return null;
            SpawnAndLoad();
        }

        void Update()
        {
            if (!filesLoaded || restGrid == null || posedPackedData == null) return;

            HandleInput();

            if (isPlaying)
                animTime += Time.deltaTime * animSpeed;

            if (animator != null && restPositions != null)
            {
                animator.PoseVoxelsToBuffer(
                    restPositions, groupIDs, restDims, 1.0f,
                    animState, animTime, animSpeed,
                    posedPositions);
            }
            else if (restPositions != null)
            {
                for (int i = 0; i < restPositions.Length; i++)
                {
                    float px = restPositions[i].x - restDims.x * 0.5f;
                    float py = restPositions[i].y;
                    float pz = restPositions[i].z - restDims.z * 0.5f;
                    posedPositions[i] = new Vector3(px, py, pz);
                }
            }

            ScatterAndUpload();
        }

        void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tKey.wasPressedThisFrame) { animState = 9f; Debug.Log("[AnimTest] State -> T-Pose"); }
            if (kb.iKey.wasPressedThisFrame) { animState = 0f; Debug.Log("[AnimTest] State -> Idle"); }
            if (kb.wKey.wasPressedThisFrame) { animState = 1f; Debug.Log("[AnimTest] State -> Walking"); }
            if (kb.lKey.wasPressedThisFrame) { animState = 2f; Debug.Log("[AnimTest] State -> Looking"); }
            if (kb.aKey.wasPressedThisFrame) { animState = 4f; Debug.Log("[AnimTest] State -> Aiming"); }
            if (kb.cKey.wasPressedThisFrame) { animState = 5f; Debug.Log("[AnimTest] State -> Crouching"); }

            if (kb.rKey.wasPressedThisFrame)
            {
                Debug.Log("[AnimTest] Reloading files from disk...");
                LoadFiles();
            }

            if (kb.spaceKey.wasPressedThisFrame)
            {
                isPlaying = !isPlaying;
                Debug.Log($"[AnimTest] {(isPlaying ? "Playing" : "Paused")}");
            }

            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)
            {
                animSpeed = Mathf.Min(animSpeed + 0.25f, 4f);
                Debug.Log($"[AnimTest] Speed = {animSpeed}");
            }
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)
            {
                animSpeed = Mathf.Max(animSpeed - 0.25f, 0.1f);
                Debug.Log($"[AnimTest] Speed = {animSpeed}");
            }

        }

        void SpawnAndLoad()
        {
            if (cityMap == null || cityMap.CachedBlocks == null || cityMap.CachedBlocks.Count == 0)
            {
                Debug.LogError("[AnimTest] No city blocks available — build city first");
                return;
            }

            Block debugBlock = FindEmptyPlotBlock();
            if (debugBlock == null)
            {
                foreach (var b in cityMap.CachedBlocks.Values)
                {
                    if (b.isPlayerHq) { debugBlock = b; break; }
                }
            }
            if (debugBlock == null)
            {
                var e = cityMap.CachedBlocks.Values.GetEnumerator();
                e.MoveNext();
                debugBlock = e.Current;
            }

            float spacing = cityMap.Spacing;
            var layout = cityMap.CachedLayout;
            float centerRow = 0f, centerCol = 0f;
            if (layout != null && layout.blocks != null)
            {
                int minR = int.MaxValue, maxR = int.MinValue, minC = int.MaxValue, maxC = int.MinValue;
                foreach (var lb in layout.blocks)
                {
                    if (lb.row < minR) minR = lb.row;
                    if (lb.row > maxR) maxR = lb.row;
                    if (lb.col < minC) minC = lb.col;
                    if (lb.col > maxC) maxC = lb.col;
                }
                centerRow = (minR + maxR) * 0.5f;
                centerCol = (minC + maxC) * 0.5f;
            }

            float spawnX = (debugBlock.col - centerCol) * spacing;
            float spawnZ = -(debugBlock.row - centerRow) * spacing;
            float groundY = ProbeGroundAt(spawnX, spawnZ);

            transform.localPosition = new Vector3(spawnX, groundY, spawnZ);

            Debug.Log($"[AnimTest] Spawned at block {debugBlock.id} ({debugBlock.name}) " +
                      $"world=({spawnX:F2}, {groundY:F3}, {spawnZ:F2}) asset={assetBaseName}");

            LoadFiles();

            if (cityMap != null && mapRoot != null)
            {
                Vector3 worldPos = new Vector3(spawnX, groundY, spawnZ) + mapRoot.position;
                cityMap.SetCameraFocus(worldPos);
                cityMap.SetCameraOrthoSize(4f);
                Debug.Log($"[AnimTest] Camera focused at {worldPos}");
            }
        }

        void LoadFiles()
        {
            filesLoaded = false;
            string dir = Path.Combine(Application.streamingAssetsPath, "voxel_characters");

            UnregisterFromChunkManager();

            // Check for consolidated .character.json first, fall back to legacy .stasset
            string jsonPath = Path.Combine(dir, assetBaseName + ".json");
            string stassetPath = Path.Combine(dir, assetBaseName + ".stasset");

            if (File.Exists(jsonPath))
            {
                // Consolidated .character.json path
                CharacterJsonLoader.Load(jsonPath, out restGrid, out uint[] groupIDsFlat, out var pivotDict, out string animParamsRaw);
                if (restGrid == null)
                {
                    Debug.LogError("[AnimTest] Failed to parse .character.json");
                    return;
                }

                restDims = new Vector3Int(restGrid.GetLength(0), restGrid.GetLength(1), restGrid.GetLength(2));
                Debug.Log($"[AnimTest] Loaded .character.json: {restDims.x}x{restDims.y}x{restDims.z}");

                // Convert flat groupIDs to 3D grid
                groupGrid = new int[restDims.x, restDims.y, restDims.z];
                if (groupIDsFlat != null)
                {
                    int w = restDims.x, h = restDims.y;
                    for (int z = 0; z < restDims.z; z++)
                        for (int y = 0; y < restDims.y; y++)
                            for (int x = 0; x < restDims.x; x++)
                                groupGrid[x, y, z] = (int)groupIDsFlat[x + y * w + z * w * h];
                }

                BuildPackedArrays();

                // Load anim params from the same JSON
                if (animParamsRaw != null || pivotDict != null)
                {
                    // Build synthetic anim JSON for VoxelCharacterAnimator.LoadFromAnimJson
                    string syntheticAnim = "{";
                    syntheticAnim += "\"format\":\"anim_params\",\"version\":1,";
                    syntheticAnim += "\"pivots\":" + (pivotDict != null ? BuildPivotsJson(pivotDict) : "{}") + ",";
                    syntheticAnim += "\"params\":" + (animParamsRaw ?? "{}");
                    syntheticAnim += "}";
                    animator = VoxelCharacterAnimator.LoadFromAnimJson(syntheticAnim);
                    if (animator != null)
                        Debug.Log($"[AnimTest] Loaded anim from .character.json — pivots: {animator.pivots.Count}, jointOffsets: {animator.jointOffsets.Count}");
                    else
                        Debug.LogWarning("[AnimTest] Failed to parse anim params — T-Pose only.");
                }
                else
                {
                    Debug.Log("[AnimTest] No animParams in .character.json — T-Pose only.");
                    animator = null;
                }
            }
            else
            {
                // Legacy .stasset + .groups + .anim.json path
                if (!File.Exists(stassetPath))
                {
                    Debug.LogError($"[AnimTest] Neither .json nor .stasset found for '{assetBaseName}' in {dir}");
                    return;
                }

                byte[] stassetData = File.ReadAllBytes(stassetPath);
                restGrid = StAssetReader.ParseVoxels(stassetData);
                if (restGrid == null)
                {
                    Debug.LogError("[AnimTest] Failed to parse .stasset");
                    return;
                }

                restDims = new Vector3Int(restGrid.GetLength(0), restGrid.GetLength(1), restGrid.GetLength(2));
                Debug.Log($"[AnimTest] Loaded .stasset: {restDims.x}x{restDims.y}x{restDims.z}");

                string groupsPath = Path.Combine(dir, assetBaseName + ".groups");
                groupGrid = LoadGroupsBinary(groupsPath, restDims);
                if (groupGrid == null)
                {
                    Debug.LogWarning("[AnimTest] No .groups file — all voxels default to group 0 (body).");
                    groupGrid = new int[restDims.x, restDims.y, restDims.z];
                }

                BuildPackedArrays();

                string animPath = Path.Combine(dir, assetBaseName + ".anim.json");
                if (File.Exists(animPath))
                {
                    string jsonText = File.ReadAllText(animPath);
                    animator = VoxelCharacterAnimator.LoadFromAnimJson(jsonText);
                    if (animator != null)
                        Debug.Log($"[AnimTest] Loaded .anim.json — pivots: {animator.pivots.Count}, jointOffsets: {animator.jointOffsets.Count}");
                    else
                        Debug.LogWarning("[AnimTest] Failed to parse .anim.json — T-Pose only.");
                }
                else
                {
                    Debug.Log("[AnimTest] No .anim.json — T-Pose only.");
                    animator = null;
                }
            }

            BuildPosedGrid();
            RegisterWithChunkManager();

            filesLoaded = true;
            Debug.Log($"[AnimTest] Ready. {restPositions.Length} voxels. State: {STATE_NAMES[Mathf.RoundToInt(animState)]}. " +
                      $"Hotkeys: T=TPose I=Idle W=Walk L=Look A=Aim C=Crouch R=Reload Space=Play/Pause +/-=Speed");
        }

        static string BuildPivotsJson(Dictionary<int, Vector3> pivots)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (var kvp in pivots)
            {
                if (!first) sb.Append(",");
                sb.Append($"\"{kvp.Key}\":{{\"x\":{kvp.Value.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"y\":{kvp.Value.y.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"z\":{kvp.Value.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
                first = false;
            }
            sb.Append("}");
            return sb.ToString();
        }

        int[,,] LoadGroupsBinary(string path, Vector3Int dims)
        {
            if (!File.Exists(path)) return null;

            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 16) return null;

            if (data[0] != (byte)'S' || data[1] != (byte)'T' ||
                data[2] != (byte)'A' || data[3] != (byte)'G')
            {
                Debug.LogError("[AnimTest] Invalid .groups magic");
                return null;
            }

            int w = data[6] | (data[7] << 8);
            int h = data[8] | (data[9] << 8);
            int d = data[10] | (data[11] << 8);

            if (w != dims.x || h != dims.y || d != dims.z)
            {
                Debug.LogError($"[AnimTest] .groups dims mismatch: {w}x{h}x{d} vs stasset {dims.x}x{dims.y}x{dims.z}");
                return null;
            }

            var groups = new int[w, h, d];
            int offset = 16;
            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        groups[x, y, z] = data[offset] | (data[offset + 1] << 8);
                        offset += 2;
                    }

            var counts = new Dictionary<int, int>();
            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int g = groups[x, y, z];
                        if (!counts.ContainsKey(g)) counts[g] = 0;
                        counts[g]++;
                    }
            var distStr = string.Join(", ", System.Linq.Enumerable.OrderBy(counts, kv => kv.Key).Select(kv => $"G{kv.Key}={kv.Value}"));
            Debug.Log($"[AnimTest] Loaded .groups: {w}x{h}x{d} — {distStr}");

            return groups;
        }

        void BuildPackedArrays()
        {
            var restList = new List<Vector3Int>();
            var gidList = new List<int>();

            for (int z = 0; z < restDims.z; z++)
                for (int y = 0; y < restDims.y; y++)
                    for (int x = 0; x < restDims.x; x++)
                    {
                        ushort mat = restGrid[x, y, z];
                        if (mat == 0) continue;

                        restList.Add(new Vector3Int(x, y, z));
                        gidList.Add(groupGrid[x, y, z]);
                    }

            int count = restList.Count;
            restPositions = restList.ToArray();
            groupIDs = gidList.ToArray();
            posedPositions = new Vector3[count];

            Debug.Log($"[AnimTest] Packed {count} non-air voxels");
        }

        void BuildPosedGrid()
        {
            posedDims = new Vector3Int(
                restDims.x + posedGridPadding * 2,
                restDims.y + posedGridPadding * 2,
                restDims.z + posedGridPadding * 2);
            posedOrigin = new Vector3Int(posedGridPadding, posedGridPadding, posedGridPadding);

            int totalVoxels = posedDims.x * posedDims.y * posedDims.z;
            posedPackedData = new uint[totalVoxels];

            Debug.Log($"[AnimTest] Posed grid: {posedDims.x}x{posedDims.y}x{posedDims.z} (rest origin at {posedOrigin})");
        }

        void RegisterWithChunkManager()
        {
            posedVolumeName = $"animtest_{assetBaseName}_{GetInstanceID()}";

            int totalVoxels = posedDims.x * posedDims.y * posedDims.z;
            posedVoxelBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
            posedVoxelBuffer.SetData(posedPackedData);

            chunkManager.RegisterVolume(
                posedVolumeName, gameObject, posedVoxelBuffer,
                posedDims.x, posedDims.y, posedDims.z, voxelSize);

            Debug.Log($"[AnimTest] Registered with VoxelChunkManager as '{posedVolumeName}' " +
                      "(pure raymarch, no shader animation)");
        }

        void UnregisterFromChunkManager()
        {
            if (!string.IsNullOrEmpty(posedVolumeName) && chunkManager != null)
            {
                chunkManager.UnregisterVolume(posedVolumeName);
                posedVolumeName = null;
            }
            if (posedVoxelBuffer != null)
            {
                posedVoxelBuffer.Release();
                posedVoxelBuffer = null;
            }
        }

        void ScatterAndUpload()
        {
            Array.Clear(posedPackedData, 0, posedPackedData.Length);

            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;

            for (int i = 0; i < restPositions.Length; i++)
            {
                Vector3 pp = posedPositions[i];
                int px = Mathf.RoundToInt(pp.x + restDims.x * 0.5f) + posedOrigin.x;
                int py = Mathf.RoundToInt(pp.y) + posedOrigin.y;
                int pz = Mathf.RoundToInt(pp.z + restDims.z * 0.5f) + posedOrigin.z;

                if (px < 0 || px >= posedDims.x ||
                    py < 0 || py >= posedDims.y ||
                    pz < 0 || pz >= posedDims.z)
                    continue;

                ushort mat = restGrid[restPositions[i].x, restPositions[i].y, restPositions[i].z];
                int idx = px + py * posedDims.x + pz * posedDims.x * posedDims.y;
                posedPackedData[idx] = (uint)mat;

                if (px < minX) minX = px;
                if (py < minY) minY = py;
                if (pz < minZ) minZ = pz;
                if (px > maxX) maxX = px;
                if (py > maxY) maxY = py;
                if (pz > maxZ) maxZ = pz;
            }

            if (posedVoxelBuffer != null)
                posedVoxelBuffer.SetData(posedPackedData);

            if (chunkManager != null && !string.IsNullOrEmpty(posedVolumeName))
            {
                if (maxX >= minX)
                {
                    chunkManager.UpdateChunkTightAABB(
                        posedVolumeName, minX, minY, minZ, maxX, maxY, maxZ);
                }
            }
        }

        private Block FindEmptyPlotBlock()
        {
            var layout = cityMap.CachedLayout;
            if (layout == null || layout.blocks == null) return null;

            var emptyBlocks = new HashSet<string>();
            foreach (var lb in layout.blocks)
            {
                if (lb.buildings == null || lb.buildings.Length == 0) continue;
                bool allEmpty = true;
                foreach (var b in lb.buildings)
                {
                    bool isEmpty = b.stasset != null && b.stasset.Contains("empty_land") && !b.stasset.Contains("tenement");
                    if (!isEmpty) { allEmpty = false; break; }
                }
                if (allEmpty)
                    emptyBlocks.Add(lb.block_id);
            }

            if (emptyBlocks.Count == 0)
            {
                Debug.LogWarning("[AnimTest] No fully-vacant empty_land blocks found — falling back to HQ block");
                return null;
            }

            foreach (var blockId in emptyBlocks)
            {
                if (cityMap.CachedBlocks.TryGetValue(blockId, out var block))
                {
                    Debug.Log($"[AnimTest] Found empty plot: {blockId} ({block.name}) at r{block.row} c{block.col}");
                    return block;
                }
            }

            return null;
        }

        private float ProbeGroundAt(float x, float z)
        {
            if (collisionWorld == null || !collisionWorld.IsInitialized)
            {
                Debug.LogWarning("[AnimTest] No VoxelCollisionWorld — using fallback Y");
                return fallbackY;
            }

            Vector3 worldOrigin = mapRoot != null ? mapRoot.position : Vector3.zero;
            Vector3 probeOrigin = new Vector3(
                worldOrigin.x + x,
                worldOrigin.y + probeStartHeight,
                worldOrigin.z + z);

            if (collisionWorld.ProbeGround(probeOrigin, probeMaxDistance, out float groundY, out Vector3 normal))
            {
                float localGroundY = groundY - worldOrigin.y;
                Debug.Log($"[AnimTest] Ground probe hit at world Y={groundY:F3} (local Y={localGroundY:F3})");
                return localGroundY;
            }

            Debug.LogWarning($"[AnimTest] Ground probe MISS at ({x:F2}, {z:F2}) — using fallback Y={fallbackY}");
            return fallbackY;
        }

        void OnDestroy()
        {
            UnregisterFromChunkManager();
        }

        void OnDrawGizmos()
        {
            if (posedDims == Vector3Int.zero) return;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            var size = new Vector3(posedDims.x * voxelSize, posedDims.y * voxelSize, posedDims.z * voxelSize);
            Gizmos.DrawWireCube(transform.position + new Vector3(0, size.y * 0.5f, 0), size);
        }
    }
}
