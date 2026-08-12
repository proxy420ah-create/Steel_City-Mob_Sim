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
        [Tooltip("Filename relative to StreamingAssets/voxel_buildings/")]
        public string assetFileName = "vehicle_civilian_car_0.stasset";

        [Header("Voxel Grid")]
        [Tooltip("World units per voxel.")]
        public float voxelSize = 0.05f;

        [Header("Rendering")]
        [Tooltip("Auto-find VoxelChunkManager in scene if not assigned.")]
        public VoxelChunkManager chunkManager;

        [Header("Positioning")]
        [Tooltip("Local-space (relative to parent, e.g. CityMap3D's mapRoot) center position for the vehicle volume. Set externally before Start(). Matches RoadGraph/WaypointGraph's local-space coordinate convention.")]
        public Vector3 centerPosition = Vector3.zero;

        private int dimX, dimY, dimZ;
        private bool initialized;
        private VoxelChunkManager.InstancedCharacter instancedHandle;

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

        void LoadAssetDims()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "voxel_buildings", assetFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[VoxelVehicle] Asset not found: {path}");
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

            instancedHandle = chunkManager.RegisterInstancedCharacter(gameObject, assetFileName, voxelSize, "voxel_buildings");
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

        void OnDestroy()
        {
            if (instancedHandle != null)
            {
                chunkManager?.UnregisterInstancedCharacter(instancedHandle);
                instancedHandle = null;
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
