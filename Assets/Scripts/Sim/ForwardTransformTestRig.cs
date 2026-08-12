using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Forward-transform character test rig that renders through the VoxelChunkManager
    /// raymarch shader pipeline — NO mesh rendering, NO instanced cubes.
    ///
    /// Flow:
    ///   1. Load .stasset (rest voxels) + .groups + .anim.json
    ///   2. Run VoxelCharacterAnimator.PoseVoxels each frame to get posed positions
    ///   3. Scatter posed voxels into a new 3D grid (round to nearest cell)
    ///   4. Upload posed grid to VoxelChunkManager as a ComputeBuffer via RegisterVolume
    ///   5. Shader does pure raymarching — no group IDs, no animation, no inverse transforms
    ///
    /// This tests the C# forward-transform math through the actual production render path.
    ///
    /// Hotkeys:
    ///   T = T-Pose (state 9), I = Idle (0), W = Walking (1),
    ///   L = Looking (2), A = Aiming (4), C = Crouching (5)
    ///   R = Reload files from disk
    ///   Space = Play/Pause animation
    ///   +/- = Speed up / slow down
    /// </summary>
    public class ForwardTransformTestRig : MonoBehaviour
    {
        [Header("Test Asset")]
        [Tooltip("Base filename in StreamingAssets/voxel_characters/ (without extension).")]
        [SerializeField] private string assetBaseName = "character_rig2";

        [Header("Rendering")]
        [Tooltip("Voxel size in world units. Must match authoring.")]
        [SerializeField] private float voxelSize = 0.02f;
        [Tooltip("VoxelChunkManager for raymarch rendering. Auto-found if not assigned.")]
        [SerializeField] private VoxelChunkManager chunkManager;

        [Header("Placement")]
        [Tooltip("Local offset added to this GameObject's transform.position to compute the render origin.")]
        [SerializeField] private Vector3 spawnPosition = Vector3.zero;
        [Tooltip("If true, automatically moves this GameObject next to the first VoxelCharacter found in the scene on Start.")]
        [SerializeField] private bool autoPositionNextToCharacter = true;
        [Tooltip("World-space offset applied when auto-positioning next to a found VoxelCharacter.")]
        [SerializeField] private Vector3 autoPositionOffset = new Vector3(2f, 0f, 0f);

        [Header("Posed Grid")]
        [Tooltip("Padding added around the rest-pose bounding box for the posed voxel grid. " +
                 "Arms/legs may extend beyond rest bounds when posed.")]
        [SerializeField] private int posedGridPadding = 8;

        // ---- Runtime state ----
        private VoxelCharacterAnimator animator;
        private ushort[,,] restGrid;          // [x,y,z] -> materialId (rest pose)
        private int[,,] groupGrid;             // [x,y,z] -> groupId
        private Vector3Int restDims;

        // Packed arrays for posing
        private Vector3Int[] restPositions;
        private int[] groupIDs;
        private Vector3[] posedPositions;

        // Posed grid (scattered each frame)
        private Vector3Int posedDims;
        private Vector3Int posedOrigin;        // offset of rest grid within posed grid
        private uint[] posedPackedData;        // flat array for GPU upload
        private ComputeBuffer posedVoxelBuffer;

        // Chunk registration
        private string posedVolumeName;

        // Animation state (HTML state IDs)
        private float animState = 9f; // Start in T-Pose
        private float animTime = 0f;
        private float animSpeed = 1f;
        private bool isPlaying = true;
        private bool filesLoaded = false;

        private static readonly string[] STATE_NAMES = {
            "Idle", "Walking", "Looking", "AimWalk", "Aiming",
            "Crouching", "???", "???", "Down", "T-Pose"
        };

        void Start()
        {
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            if (chunkManager == null)
            {
                Debug.LogError("[FTTR] No VoxelChunkManager found in scene! Cannot render.");
                return;
            }

            if (autoPositionNextToCharacter)
                StartCoroutine(DelayedAutoPosition());
            else
                LoadFiles();
        }

        private System.Collections.IEnumerator DelayedAutoPosition()
        {
            yield return new WaitForSeconds(0.5f);
            AutoPositionNextToCharacter();
            LoadFiles();
        }

        void AutoPositionNextToCharacter()
        {
            var existing = FindFirstObjectByType<VoxelCharacter>();
            if (existing == null)
            {
                Debug.LogWarning("[FTTR] No VoxelCharacter found — using current Transform position.");
                return;
            }

            Vector3 basePos = existing.transform.position;
            transform.position = basePos + autoPositionOffset;
            Debug.Log($"[FTTR] Auto-positioned next to '{existing.gameObject.name}' at world {basePos} -> this rig at {transform.position}");
        }

        void Update()
        {
            if (!filesLoaded || restGrid == null || posedPackedData == null) return;

            HandleInput();

            if (isPlaying)
                animTime += Time.deltaTime * animSpeed;

            // Pose voxels every frame
            // NOTE: voxelSize=1.0 for the math (pivots in voxel-grid-space, matching HTML animator)
            if (animator != null && restPositions != null)
            {
                animator.PoseVoxelsToBuffer(
                    restPositions, groupIDs, restDims, 1.0f,
                    animState, animTime, animSpeed,
                    posedPositions);
            }
            else if (restPositions != null)
            {
                // No animator — just copy rest positions (T-Pose)
                for (int i = 0; i < restPositions.Length; i++)
                {
                    float px = restPositions[i].x - restDims.x * 0.5f;
                    float py = restPositions[i].y;
                    float pz = restPositions[i].z - restDims.z * 0.5f;
                    posedPositions[i] = new Vector3(px, py, pz);
                }
            }

            // Scatter posed voxels into the posed grid and upload to GPU
            ScatterAndUpload();
        }

        void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tKey.wasPressedThisFrame) { animState = 9f; Debug.Log("[FTTR] State -> T-Pose"); }
            if (kb.iKey.wasPressedThisFrame) { animState = 0f; Debug.Log("[FTTR] State -> Idle"); }
            if (kb.wKey.wasPressedThisFrame) { animState = 1f; Debug.Log("[FTTR] State -> Walking"); }
            if (kb.lKey.wasPressedThisFrame) { animState = 2f; Debug.Log("[FTTR] State -> Looking"); }
            if (kb.aKey.wasPressedThisFrame) { animState = 4f; Debug.Log("[FTTR] State -> Aiming"); }
            if (kb.cKey.wasPressedThisFrame) { animState = 5f; Debug.Log("[FTTR] State -> Crouching"); }

            if (kb.rKey.wasPressedThisFrame)
            {
                Debug.Log("[FTTR] Reloading files from disk...");
                LoadFiles();
            }

            if (kb.spaceKey.wasPressedThisFrame)
            {
                isPlaying = !isPlaying;
                Debug.Log($"[FTTR] {(isPlaying ? "Playing" : "Paused")}");
            }

            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)
            {
                animSpeed = Mathf.Min(animSpeed + 0.25f, 4f);
                Debug.Log($"[FTTR] Speed = {animSpeed}");
            }
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)
            {
                animSpeed = Mathf.Max(animSpeed - 0.25f, 0.1f);
                Debug.Log($"[FTTR] Speed = {animSpeed}");
            }
        }

        void LoadFiles()
        {
            filesLoaded = false;
            string dir = Path.Combine(Application.streamingAssetsPath, "voxel_characters");

            // Unregister previous volume if reloading
            UnregisterFromChunkManager();

            // Load .stasset (voxel data)
            string stassetPath = Path.Combine(dir, assetBaseName + ".stasset");
            if (!File.Exists(stassetPath))
            {
                Debug.LogError($"[FTTR] .stasset not found: {stassetPath}");
                return;
            }

            byte[] stassetData = File.ReadAllBytes(stassetPath);
            restGrid = StAssetReader.ParseVoxels(stassetData);
            if (restGrid == null)
            {
                Debug.LogError("[FTTR] Failed to parse .stasset");
                return;
            }

            restDims = new Vector3Int(restGrid.GetLength(0), restGrid.GetLength(1), restGrid.GetLength(2));
            Debug.Log($"[FTTR] Loaded .stasset: {restDims.x}x{restDims.y}x{restDims.z}");

            // Load .groups
            string groupsPath = Path.Combine(dir, assetBaseName + ".groups");
            groupGrid = LoadGroupsBinary(groupsPath, restDims);
            if (groupGrid == null)
            {
                Debug.LogWarning("[FTTR] No .groups file — all voxels default to group 0 (body).");
                groupGrid = new int[restDims.x, restDims.y, restDims.z];
            }

            // Build packed arrays
            BuildPackedArrays();

            // Load .anim.json
            string animPath = Path.Combine(dir, assetBaseName + ".anim.json");
            if (File.Exists(animPath))
            {
                string jsonText = File.ReadAllText(animPath);
                animator = VoxelCharacterAnimator.LoadFromAnimJson(jsonText);
                if (animator != null)
                    Debug.Log($"[FTTR] Loaded .anim.json — pivots: {animator.pivots.Count}, jointOffsets: {animator.jointOffsets.Count}");
                else
                    Debug.LogWarning("[FTTR] Failed to parse .anim.json — T-Pose only.");
            }
            else
            {
                Debug.Log("[FTTR] No .anim.json — T-Pose only.");
                animator = null;
            }

            // Build posed grid (padded rest grid)
            BuildPosedGrid();

            // Register with VoxelChunkManager
            RegisterWithChunkManager();

            filesLoaded = true;
            Debug.Log($"[FTTR] Ready. {restPositions.Length} voxels. State: {STATE_NAMES[Mathf.RoundToInt(animState)]}. " +
                      $"Hotkeys: T=TPose I=Idle W=Walk L=Look A=Aim C=Crouch R=Reload Space=Play/Pause +/-=Speed");
        }

        int[,,] LoadGroupsBinary(string path, Vector3Int dims)
        {
            if (!File.Exists(path)) return null;

            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 16) return null;

            if (data[0] != (byte)'S' || data[1] != (byte)'T' ||
                data[2] != (byte)'A' || data[3] != (byte)'G')
            {
                Debug.LogError($"[FTTR] Invalid .groups magic");
                return null;
            }

            int w = data[6] | (data[7] << 8);
            int h = data[8] | (data[9] << 8);
            int d = data[10] | (data[11] << 8);

            if (w != dims.x || h != dims.y || d != dims.z)
            {
                Debug.LogError($"[FTTR] .groups dims mismatch: {w}x{h}x{d} vs stasset {dims.x}x{dims.y}x{dims.z}");
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

            Debug.Log($"[FTTR] Loaded .groups: {w}x{h}x{d}");
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

            Debug.Log($"[FTTR] Packed {count} non-air voxels");
        }

        /// <summary>
        /// Build a padded posed grid. The posed grid is larger than the rest grid
        /// to accommodate arms/legs extending beyond rest bounds when posed.
        /// The rest grid is centered within the posed grid.
        /// </summary>
        void BuildPosedGrid()
        {
            posedDims = new Vector3Int(
                restDims.x + posedGridPadding * 2,
                restDims.y + posedGridPadding * 2,
                restDims.z + posedGridPadding * 2);
            posedOrigin = new Vector3Int(posedGridPadding, posedGridPadding, posedGridPadding);

            int totalVoxels = posedDims.x * posedDims.y * posedDims.z;
            posedPackedData = new uint[totalVoxels];

            Debug.Log($"[FTTR] Posed grid: {posedDims.x}x{posedDims.y}x{posedDims.z} (rest origin at {posedOrigin})");
        }

        /// <summary>
        /// Register with VoxelChunkManager using RegisterVolume — this provides
        /// a pre-built ComputeBuffer and bypasses .stasset file loading.
        /// The shader will do pure raymarching (no group IDs, no animation).
        /// </summary>
        void RegisterWithChunkManager()
        {
            posedVolumeName = $"posed_{assetBaseName}_{GetInstanceID()}";

            int totalVoxels = posedDims.x * posedDims.y * posedDims.z;
            posedVoxelBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
            posedVoxelBuffer.SetData(posedPackedData);

            chunkManager.RegisterVolume(
                posedVolumeName, gameObject, posedVoxelBuffer,
                posedDims.x, posedDims.y, posedDims.z, voxelSize);

            Debug.Log($"[FTTR] Registered with VoxelChunkManager as '{posedVolumeName}' " +
                      $"(pure raymarch, no shader animation)");
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

        /// <summary>
        /// Scatter posed voxels into the posed grid and upload to GPU.
        /// posedPositions are in voxel-space relative to rest grid center:
        ///   x: centered (restX - restDims.x/2)
        ///   y: absolute (restY)
        ///   z: centered (restZ - restDims.z/2)
        /// We map them into the padded posed grid.
        /// </summary>
        void ScatterAndUpload()
        {
            // Clear the posed grid
            Array.Clear(posedPackedData, 0, posedPackedData.Length);

            // Track tight AABB for proxy rendering
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;

            // Scatter each posed voxel
            for (int i = 0; i < restPositions.Length; i++)
            {
                Vector3 pp = posedPositions[i];
                // Map posed position back to posed grid coordinates
                int px = Mathf.RoundToInt(pp.x + restDims.x * 0.5f) + posedOrigin.x;
                int py = Mathf.RoundToInt(pp.y) + posedOrigin.y;
                int pz = Mathf.RoundToInt(pp.z + restDims.z * 0.5f) + posedOrigin.z;

                // Bounds check
                if (px < 0 || px >= posedDims.x ||
                    py < 0 || py >= posedDims.y ||
                    pz < 0 || pz >= posedDims.z)
                    continue;

                // Write material ID into posed grid
                ushort mat = restGrid[restPositions[i].x, restPositions[i].y, restPositions[i].z];
                int idx = px + py * posedDims.x + pz * posedDims.x * posedDims.y;
                posedPackedData[idx] = (uint)mat;

                // Track tight AABB
                if (px < minX) minX = px;
                if (py < minY) minY = py;
                if (pz < minZ) minZ = pz;
                if (px > maxX) maxX = px;
                if (py > maxY) maxY = py;
                if (pz > maxZ) maxZ = pz;
            }

            // Upload to GPU
            if (posedVoxelBuffer != null)
                posedVoxelBuffer.SetData(posedPackedData);

            // Update tight AABB so the proxy box matches the posed voxels
            if (chunkManager != null && !string.IsNullOrEmpty(posedVolumeName))
            {
                if (maxX >= minX) // at least one voxel was written
                {
                    chunkManager.UpdateChunkTightAABB(
                        posedVolumeName, minX, minY, minZ, maxX, maxY, maxZ);
                }
            }
        }

        void OnDestroy()
        {
            UnregisterFromChunkManager();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            var size = new Vector3(posedDims.x * voxelSize, posedDims.y * voxelSize, posedDims.z * voxelSize);
            Vector3 origin = transform.position + spawnPosition;
            Gizmos.DrawWireCube(origin + new Vector3(0, size.y * 0.5f, 0), size);
        }
    }
}
