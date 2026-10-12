using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Voxel damage — P0 glass puncture (GLASS_SHATTER_MECHANIC.md).
    ///
    /// Subscribes to ProjectileSystem.ShotResolved; when a shot stops on glass inside a
    /// per-building chunk volume it removes the impact cell plus a seeded ~80% of the
    /// ring-1 cells in the plane perpendicular to the shot direction, writing AIR into a
    /// copy-on-write clone of the volume's voxel array. The clone replaces the collision
    /// registration by id (raycasts see the hole immediately) and the dirty cell spans
    /// are pushed to the chunk's GPU ComputeBuffer via partial SetData
    /// (PERFORMANCE_PATTERNS #1-2 — bytes, not a re-upload).
    ///
    /// P0 scope is per-building chunks only: sector-baked placements render from merged
    /// sector buffers this path cannot reach, so hits on them are skipped (logged once).
    /// The cracked-halo material swap (survivors → 134) is P1 dressing pulled forward —
    /// without it an AIR hole in a transparent mold is nearly invisible in playtest.
    /// </summary>
    public class VoxelDamageSystem : MonoBehaviour
    {
        public static VoxelDamageSystem Instance { get; private set; }

        public static VoxelDamageSystem GetOrCreate()
        {
            if (Instance != null) return Instance;
            var found = FindFirstObjectByType<VoxelDamageSystem>();
            if (found != null) { Instance = found; return found; }
            return new GameObject("VoxelDamageSystem").AddComponent<VoxelDamageSystem>();
        }

        [Header("Glass puncture (P0)")]
        [Tooltip("Bullet hits on glass carve the impact cell + seeded ring-1 into the volume's voxel array.")]
        public bool glassDamageEnabled = true;
        [Tooltip("Fraction of ring-1 glass neighbors removed per hit (seeded per cell — same shot seed reproduces the same scar).")]
        [Range(0f, 1f)] public float ring1RemoveFraction = 0.8f;
        [Tooltip("Ring-1 survivors swap to this material so the puncture stays visible inside transparent glass.")]
        public int crackedMaterial = 134;

        /// <summary>Last puncture's removed/cracked counts for the HUD report.</summary>
        public int LastRemoved { get; private set; }
        public int LastCracked { get; private set; }

        // volume id → copy-on-write state. The shared asset array stays read-only, so
        // every other placement of the same asset is unaffected; on first damage the
        // clone is re-registered under the same volume id. `original` keeps the shared
        // array + placement for ResetAllDamage.
        private sealed class DamageState
        {
            public uint[] data;       // the working (damaged) copy
            public uint[] original;   // pristine shared array — restore target
            public int w, h, d;
            public Vector3 corner; public float vs, yaw;
        }
        private readonly Dictionary<string, DamageState> cow = new();
        private readonly List<int> dirtyScratch = new();

        private ProjectileSystem subscribed;
        private VoxelCollisionWorld world;
        private VoxelChunkManager chunkManager;
        private readonly HashSet<string> skippedVolumes = new();

        void Awake() { Instance = this; }

        void OnDestroy()
        {
            if (subscribed != null) subscribed.ShotResolved -= OnShotResolved;
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (subscribed == null && ProjectileSystem.Instance != null)
            {
                subscribed = ProjectileSystem.Instance;
                subscribed.ShotResolved += OnShotResolved;
            }
        }

        private void OnShotResolved(ProjectileSystem.ShotReport r)
        {
            if (!glassDamageEnabled || !r.hit || !ProjectileSystem.IsGlassMaterial(r.material)) return;
            if (string.IsNullOrEmpty(r.target) || r.target == "terrain" || r.target == "none") return;

            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (world == null || chunkManager == null) return;

            // P0 reaches per-building chunk buffers only. Sector-baked placements share
            // the volume id convention but render from merged sector buffers — mutating
            // them would open invisible collision holes, so skip (logged once per volume).
            if (!chunkManager.HasChunk(r.target))
            {
                if (skippedVolumes.Add(r.target))
                    Debug.LogWarning($"[VoxelDamage] {r.target}: no live chunk — volume is sector-baked. P0 puncture needs per-building mode (CityMap3D.useSectorBaking off, or context menu 'Rebuild — sector baking OFF'). Skipped.");
                return;
            }

            if (!cow.TryGetValue(r.target, out var st))
            {
                if (!world.TryGetVolume(r.target, out uint[] src, out int w, out int h, out int d,
                        out Vector3 corner, out float vs, out float yaw)) return;
                st = new DamageState
                {
                    data = (uint[])src.Clone(), original = src,
                    w = w, h = h, d = d, corner = corner, vs = vs, yaw = yaw,
                };
                world.RegisterVolume(r.target, st.data, w, h, d, corner, vs, yaw);   // replaces by id
                cow[r.target] = st;
            }

            dirtyScratch.Clear();
            int removed = VoxelDamageMath.Puncture(st.data, st.w, st.h, st.d,
                r.cell.x, r.cell.y, r.cell.z, r.shotDir.x, r.shotDir.y, r.shotDir.z,
                r.seed, ring1RemoveFraction, crackedMaterial, dirtyScratch, out int cracked);
            if (dirtyScratch.Count == 0) return;

            LastRemoved = removed;
            LastCracked = cracked;
            chunkManager.UploadVoxelEdits(r.target, st.data, dirtyScratch);
            Debug.Log($"[VoxelDamage] 🔨 {r.target} cell {r.cell} mat {r.material}: -{removed} glass cells, {cracked} cracked — hole persists");
        }

        /// <summary>How many volumes currently carry damage (for the HUD reset button).</summary>
        public int DamagedVolumeCount => cow.Count;

        /// <summary>
        /// Restore every damaged volume to its pristine shared array — collision
        /// registration and GPU buffer both reset. Returns volumes restored.
        /// </summary>
        public int ResetAllDamage()
        {
            if (world == null || chunkManager == null) return 0;
            int n = 0;
            foreach (var kv in cow)
            {
                var st = kv.Value;
                world.RegisterVolume(kv.Key, st.original, st.w, st.h, st.d, st.corner, st.vs, st.yaw);
                chunkManager.RefreshChunkVoxels(kv.Key, st.original);
                n++;
            }
            if (n > 0) Debug.Log($"[VoxelDamage] ♻ reset {n} damaged volume(s) to pristine");
            cow.Clear();
            skippedVolumes.Clear();
            LastRemoved = LastCracked = 0;
            return n;
        }
    }
}
