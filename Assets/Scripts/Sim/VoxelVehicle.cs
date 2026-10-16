using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Self-contained voxel vehicle component — same instancing pattern as VoxelCharacter,
    /// but for cars/trucks. Vehicles stay on a fixed ground Y (no gravity/ground-probe needed;
    /// roads are flat in this project's terrain).
    ///
    /// Multiple distinct vehicle shapes (civilian car, roadster, police car, per the
    /// reverse-engineered subtypes in docs/core/VEHICLE_RE_REFERENCE.md) can coexist —
    /// each distinct assetFileName gets its own shared voxel buffer and its own batched
    /// draw call via VoxelChunkManager's per-asset InstancedGroup system.
    ///
    /// The GameObject's transform.position IS the volume origin (corner, not center).
    /// </summary>
    public class VoxelVehicle : MonoBehaviour
    {
        [Header("Asset")]
        [Tooltip("Filename under StreamingAssets/ — voxel_vehicles/ is probed first, voxel_buildings/ is the legacy fallback")]
        public string assetFileName = "vehicle_490_touring.stasset";

        [Header("Voxel Grid")]
        [Tooltip("World units per voxel.")]
        public float voxelSize = 0.01f;

        [Header("Rendering")]
        [Tooltip("Auto-find VoxelChunkManager in scene if not assigned.")]
        public VoxelChunkManager chunkManager;

        [Header("Positioning")]
        [Tooltip("Local-space (relative to parent, e.g. CityMap3D's mapRoot) center position for the vehicle volume. Set externally before Start(). Matches RoadGraph/WaypointGraph's local-space coordinate convention.")]
        public Vector3 centerPosition = Vector3.zero;

        [Header("Collision")]
        [Tooltip("Register a solid yawed volume in VoxelCollisionWorld — the car blocks projectiles and counts as cover. On first damage hit the instance promotes to its own writable buffer (bullet holes / windshield shatter).")]
        public bool solidCollider = true;

        private int dimX, dimY, dimZ;
        private bool initialized;
        private VoxelChunkManager.InstancedCharacter instancedHandle;

        // --- Collision registration ---
        // The flat voxel array is shared per-asset (rest data is read-only until
        // VoxelDamageSystem's copy-on-write clones it on first hit).
        private VoxelCollisionWorld collisionWorld;
        private string collisionId;
        private Vector3 regPos;
        private float regYaw;
        private bool regValid;
        private uint[] flatVoxels;
        private static readonly Dictionary<int, VoxelVehicle> byCollisionId = new();
        private static readonly Dictionary<string, uint[]> voxelCache = new();

        /// <summary>Collision-world volume id ("veh_<instanceID>") — null until first registration.</summary>
        public string CollisionId => collisionId;
        /// <summary>Instanced render handle — damage promotion re-keys it into a private-buffer group.</summary>
        public VoxelChunkManager.InstancedCharacter Handle => instancedHandle;

        /// <summary>Resolve a collision volume id back to its vehicle (used by the damage promote path).</summary>
        public static bool TryGetByCollisionId(string id, out VoxelVehicle veh)
        {
            veh = null;
            return id != null && id.Length > 4 && id.StartsWith("veh_")
                && int.TryParse(id.Substring(4), out int iid)
                && byCollisionId.TryGetValue(iid, out veh) && veh != null;
        }

        /// <summary>True after asset loaded and registered with renderer.</summary>
        public bool IsInitialized => initialized;

        /// <summary>Voxel dimensions (x, y, z).</summary>
        public (int x, int y, int z) Dims => (dimX, dimY, dimZ);

        /// <summary>World-space size of the volume (dims * voxelSize).</summary>
        public Vector3 WorldSize => new Vector3(dimX, dimY, dimZ) * voxelSize;

        void Start()
        {
            LoadAssetDims();
            ApplyCenterPosition();
            RegisterWithManager();
            initialized = true;
        }

        /// <summary>StreamingAssets subfolder this asset resolves to (voxel_vehicles first, voxel_buildings legacy fallback).</summary>
        private string assetFolder = "voxel_buildings";

        void LoadAssetDims()
        {
            string vehiclesPath = Path.Combine(Application.streamingAssetsPath, "voxel_vehicles", assetFileName);
            assetFolder = File.Exists(vehiclesPath) ? "voxel_vehicles" : "voxel_buildings";
            string path = Path.Combine(Application.streamingAssetsPath, assetFolder, assetFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[VoxelVehicle] Asset not found: {vehiclesPath} (or {path})");
                return;
            }

            var voxelData = StAssetReader.LoadVoxels(path);
            if (voxelData == null)
            {
                Debug.LogError($"[VoxelVehicle] Failed to load voxel data from {path}");
                return;
            }

            dimX = voxelData.GetLength(0);
            dimY = voxelData.GetLength(1);
            dimZ = voxelData.GetLength(2);

            // Flat packed copy for the collision world — cached per asset file so
            // every parked car of a model shares one rest array (damage COWs it).
            if (!voxelCache.TryGetValue(path, out flatVoxels))
            {
                flatVoxels = new uint[dimX * dimY * dimZ];
                int fi = 0;
                for (int z = 0; z < dimZ; z++)
                    for (int y = 0; y < dimY; y++)
                        for (int x = 0; x < dimX; x++)
                            flatVoxels[fi++] = voxelData[x, y, z];
                voxelCache[path] = flatVoxels;
            }

            Debug.Log($"[VoxelVehicle] Loaded {assetFileName}: {dimX}x{dimY}x{dimZ} (voxelSize={voxelSize})");
        }

        void ApplyCenterPosition()
        {
            Vector3 cornerOffset = new Vector3(dimX * voxelSize * 0.5f, 0f, dimZ * voxelSize * 0.5f);
            transform.localPosition = centerPosition - cornerOffset;
        }

        void RegisterWithManager()
        {
            if (chunkManager == null)
                chunkManager = FindFirstObjectByType<VoxelChunkManager>();

            if (chunkManager == null)
            {
                Debug.LogWarning("[VoxelVehicle] No VoxelChunkManager found in scene! Vehicle will not render.");
                return;
            }

            instancedHandle = chunkManager.RegisterInstancedCharacter(gameObject, assetFileName, voxelSize, assetFolder);
            if (instancedHandle == null)
                Debug.LogWarning("[VoxelVehicle] Instanced registration failed — vehicle will not render.");
        }

        /// <summary>
        /// Move the vehicle to a local-space position. The position is the CENTER of the volume
        /// (not the corner) — transform.localPosition stays at the corner, which the raymarcher expects.
        /// </summary>
        public void PlaceAtCenter(Vector3 localCenter)
        {
            transform.localPosition = localCenter - new Vector3(dimX * voxelSize * 0.5f, 0f, dimZ * voxelSize * 0.5f);
        }

        /// <summary>
        /// Keep the collision volume in sync with the transform — parked cars register
        /// once and never re-enter; traffic re-registers when it actually moves/turns.
        /// </summary>
        void Update()
        {
            if (!solidCollider || flatVoxels == null) return;
            if (collisionWorld == null)
            {
                collisionWorld = FindFirstObjectByType<VoxelCollisionWorld>();
                if (collisionWorld == null) return;
            }
            float yaw = transform.eulerAngles.y;
            if (regValid && (transform.position - regPos).sqrMagnitude <= 1e-4f
                && Mathf.Abs(Mathf.DeltaAngle(yaw, regYaw)) <= 0.25f) return;
            if (collisionId == null)
            {
                collisionId = $"veh_{GetInstanceID()}";
                byCollisionId[GetInstanceID()] = this;
            }
            // Re-register with the CURRENT volume array — after a promote, that's
            // the damage system's COW'd copy; dropping back to flatVoxels would
            // silently un-damage a moving car.
            uint[] data = flatVoxels;
            if (collisionId != null && collisionWorld.TryGetVolume(collisionId, out var cur,
                    out _, out _, out _, out _, out _, out _))
                data = cur;
            collisionWorld.RegisterVolume(collisionId, data, dimX, dimY, dimZ,
                transform.position, voxelSize, yaw);
            regPos = transform.position;
            regYaw = yaw;
            regValid = true;
        }

        void OnDestroy()
        {
            if (instancedHandle != null)
            {
                chunkManager?.UnregisterInstancedCharacter(instancedHandle);
                instancedHandle = null;
            }
            if (collisionId != null)
            {
                collisionWorld?.UnregisterVolume(collisionId);
                byCollisionId.Remove(GetInstanceID());
                collisionId = null;
            }
        }

        void OnDrawGizmos()
        {
            Vector3 size = new Vector3(
                dimX > 0 ? dimX * voxelSize : 1.5f,
                dimY > 0 ? dimY * voxelSize : 0.8f,
                dimZ > 0 ? dimZ * voxelSize : 3f);
            Vector3 center = transform.position + size * 0.5f;

            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f); // Blue for vehicles
            Gizmos.DrawWireCube(center, size);
        }
    }
}
