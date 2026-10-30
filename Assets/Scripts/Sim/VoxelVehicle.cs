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
        private static readonly List<VoxelVehicle> allVehicles = new();

        // --- Motion (cover / map consumers) ---
        private Vector3 lastPos; private bool lastPosValid;
        private float lastMoveTime = -999f;
        private Vector3 velocity;
        private VehicleAgent agent;
        /// <summary>Planar velocity estimate (m/s) from the transform delta.</summary>
        public Vector3 Velocity => velocity;
        /// <summary>Driving (agent says so) or moved within the last 1.5 s. A moving car is never cover and its swept footprint is
        /// priced out of the actor flood; a parked car (scenery or pulled over) stays valid cover.</summary>
        public bool IsMoving
        {
            get
            {
                if (agent == null) agent = GetComponent<VehicleAgent>();
                if (agent != null && agent.IsDriving && !agent.IsParked) return true;
                return Time.time - lastMoveTime < 1.5f;
            }
        }

        /// <summary>How many registered vehicles are moving right now — the cover scan's dynamic-world
        /// signature (moving cars are excluded from cover and priced out of the flood, and `IsMoving`
        /// flips on a timer, so a scan is only reusable while this is 0 and unchanged).</summary>
        public static int MovingCount()
        {
            int n = 0;
            for (int i = 0; i < allVehicles.Count; i++)
                if (allVehicles[i] != null && allVehicles[i].IsMoving) n++;
            return n;
        }

        /// <summary>Moving vehicles whose swept footprint could reach a `radius` window around `center`
        /// (distance minus its 1.5 s sweep and half-diagonal, same horizon PaintMovingFootprints uses).
        /// Traffic across town can't touch a fighter's cover scan, so it must not defeat scan reuse.</summary>
        public static int MovingNear(Vector3 center, float radius)
        {
            int n = 0;
            for (int i = 0; i < allVehicles.Count; i++)
            {
                var v = allVehicles[i];
                if (v == null || !v.IsMoving) continue;
                Vector3 size = v.WorldSize;
                Vector3 c = v.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
                float reach = radius + 0.5f * Mathf.Sqrt(size.x * size.x + size.z * size.z) + v.velocity.magnitude * 1.5f + 1f;
                float dx = c.x - center.x, dz = c.z - center.z;
                if (dx * dx + dz * dz <= reach * reach) n++;
            }
            return n;
        }

        void OnEnable() { if (!allVehicles.Contains(this)) allVehicles.Add(this); }
        void OnDisable() { allVehicles.Remove(this); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookCoverExclusion()
        {
            allVehicles.Clear(); byCollisionId.Clear();
            SolidVolumeSet.ExcludeFromCover = IsVolumeMoving;
        }

        /// <summary>True when the collision volume id belongs to a vehicle that is currently moving.</summary>
        public static bool IsVolumeMoving(string volId)
            => TryGetByCollisionId(volId, out var v) && v.IsMoving;

        /// <summary>Mark every field cell a moving vehicle occupies now or will sweep within horizonSec.
        /// A cell p is hit iff p - v*s lies inside the car's CURRENT volume for some s in [0, horizon] - answered by the collision
        /// world itself (yaw handled by the volume), so the mask can't disagree with what blocks the body.</summary>
        public static void PaintMovingFootprints(CoverField f, VoxelCollisionWorld world, float horizonSec)
        {
            if (f == null || !f.HasData || world == null) return;
            for (int vi = 0; vi < allVehicles.Count; vi++)
            {
                var v = allVehicles[vi];
                if (v == null || v.collisionId == null || !v.IsMoving) continue;
                Vector3 size = v.WorldSize;
                Vector3 c = v.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
                Vector3 vel = v.velocity; vel.y = 0f;
                Vector3 end = c + vel * horizonSec;
                float r = 0.5f * Mathf.Sqrt(size.x * size.x + size.z * size.z) + 0.4f;
                int n = f.N; float cs = f.CellSize;
                if (cs <= 0f) return;
                // swept AABB -> cell rect, clamped to the field; a car whose sweep misses the field entirely costs nothing
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(c.x, end.x) - r - f.Origin.x) / cs));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(c.z, end.z) - r - f.Origin.z) / cs));
                int x1 = Mathf.Min(n - 1, Mathf.FloorToInt((Mathf.Max(c.x, end.x) + r - f.Origin.x) / cs));
                int z1 = Mathf.Min(n - 1, Mathf.FloorToInt((Mathf.Max(c.z, end.z) + r - f.Origin.z) / cs));
                if (x0 > x1 || z0 > z1) continue;
                float y = v.transform.position.y + 0.4f;
                for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    Vector3 p = f.CellCenter(cx, cz);
                    for (int k = 0; k <= 4; k++)
                    {
                        float t = horizonSec * (k / 4f);
                        Vector3 q = p - vel * t;
                        var ps = world.SamplePoint(new Vector3(q.x, y, q.z));
                        if (ps.kind == VoxelCollisionWorld.PointKind.VolumeSolid && ps.volumeId == v.collisionId)
                        { f.MarkDyn(cz * n + cx); break; }
                    }
                }
            }
        }
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
            Vector3 np = transform.position;
            if (lastPosValid && Time.deltaTime > 1e-4f)
            {
                Vector3 raw = (np - lastPos) / Time.deltaTime; raw.y = 0f;
                velocity = Vector3.Lerp(velocity, raw, 0.35f);
                if (raw.sqrMagnitude > 0.0025f) lastMoveTime = Time.time;
            }
            lastPos = np; lastPosValid = true;
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
