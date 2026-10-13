using System.Collections;
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
        [Range(0f, 1f)] public float ring1RemoveFraction = 0.2f;
        [Tooltip("Ring-1 survivors swap to this material so the puncture stays visible inside transparent glass.")]
        public int crackedMaterial = 134;

        [Header("Shard burst (P1 dressing)")]
        [Tooltip("Removed cells re-spawn as tiny velocity-stretched cube shards drawn via PathDebugRenderer beams.")]
        public bool shardBurstEnabled = true;
        public float shardSpeed = 2.5f;      // along the shot direction
        public float shardSpread = 1.6f;     // lateral scatter
        public float shardGravity = 9f;
        public float shardLife = 0.7f;
        public float shardGroundY = 0f;    // ground plane for bounce/rest (roads ~y0)
        public float shardBounce = 0.35f;  // vertical restitution
        public float shardRestSec = 1.1f;  // how long a settled chunk lingers
        public int maxShards = 192;
        [Tooltip("Debris sweeps against the voxel world (floors, roofs, walls, earlier rubble) instead of only the flat ground plane.")]
        public bool shardVoxelCollision = true;
        [Tooltip("A shard that comes to rest on a damaged volume writes its own material back into the cells it settled on - dust fills one cell, a collapsed chunk fills a block.")]
        public bool rubbleDeposit = true;
        [Tooltip("Edge cap (cells) for one deposited chunk.")]
        public int rubbleMaxCells = 20;

        [Header("Glass audio (P1, synthesized — no assets)")]
        public bool glassAudioEnabled = true;
        [Range(0f, 1f)] public float glassAudioVolume = 0.6f;

        /// <summary>Last puncture's removed/cracked counts for the HUD report.</summary>
        public int LastRemoved { get; private set; }
        public int LastCracked { get; private set; }
        /// <summary>Cells of rubble written back into volumes since the last reset (HUD).</summary>
        public int RubbleCells { get; private set; }

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
            public readonly HashSet<int> rubble = new();      // cells DepositHeap wrote - the ONLY cells Slump may move
            public readonly List<int> slumpQueue = new();     // candidate cells for the angle-of-repose pass
        }
        private readonly Dictionary<string, DamageState> cow = new();
        private readonly List<int> dirtyScratch = new();

        // Shard sim — CPU-integrated, rendered as velocity-stretched PathDebugRenderer
        // beams (an elongated box reads as a flying glass fragment; same pipeline as
        // tracers, so it composites into the map RT for free).
        // mat = material of the cell it came from (rubble writes it back) - mass = cells it carries, spilled as a heap on deposit - vol = volume it last touched - depositWait = at rest, queued to become rubble
        private struct Shard { public Vector3 pos, vel, angVel; public Quaternion rot; public float tEnd; public int beamId; public Color col; public float size, size0; public bool resting; public ushort mat, mat2; public float mix2; public int mass, mass0; public string vol; public bool depositWait; }
        private readonly List<Shard> shards = new();
        private int shardCounter;
        private ShardRaycast shardCast;      // sweep delegate over world.Raycast, built once
        private float planBusyUntil;         // a shock-front playback runs until here - rubble waits so it never lands on an island about to drop
        private const float RubbleWaitSec = 4f;
        private const int RubblePerFrame = 32;   // deposits per frame - the whole waiting batch becomes eligible the frame the playback window closes
        private const int RubbleCellsPerFrame = 65536; // cells written per frame - a mass-conserving batch can't hitch the window-close frame
        private const int SlumpCellsPerFrame = 4096;   // angle-of-repose moves per frame - piles relax visibly over a few frames
        private const float RenderCapCells = 5f;       // a debris shard never renders wider than this (voxels) - the mass it carries is spilled on deposit, not flown
        /// <summary>Rubble pour rate (cells/sec per shard): a resting chunk drains over ~mass/rate seconds instead of popping its mound in one frame - the cube shrinks as it spills.</summary>
        public float rubblePourPerSec = 8000f;
        private const float FrontShardShare = 0.7f;    // of maxShards: blast-front debris budget (islands get the rest)
        private const float IslandShardShare = 0.3f;   // of maxShards: island-drop debris budget
        private int planAirCells = -1;                 // AIR writes across the plan's fronts - sizes each front's share of the shard budget
        private int emitShards, emitMass, emitEvictMass;  // per-blast debris accounting - proves removed cells became carried mass
        private readonly Dictionary<ushort, int> emitMatMass = new();   // carried cells per material - the "is it really asphalt?" answer
        private float rubbleLogT = -10f;     // one log line per deposit burst, not per shard
        private readonly System.Random shardRng = new(0x5EED);

        private readonly Dictionary<int, AudioClip> clipCache = new();

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
            UnityEngine.Profiling.Profiler.BeginSample("VDS.UpdateShards");
            UpdateShards();
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("VDS.PumpRubbleSlump");
            PumpRubbleSlump();
            UnityEngine.Profiling.Profiler.EndSample();
        }

        private void OnShotResolved(ProjectileSystem.ShotReport r)
        {
            if (!glassDamageEnabled || !r.hit || !ProjectileSystem.IsGlassMaterial(r.material)) return;
            if (string.IsNullOrEmpty(r.target) || r.target == "terrain" || r.target == "none") return;

            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (world == null || chunkManager == null) return;

            // P2: damage writes reach both render paths — live per-building chunks
            // and regions inside baked sectors' merged buffers (bufferOffset +
            // localIndex). Only skip volumes with no writable backing at all.
            if (!chunkManager.HasWritableVolume(r.target))
            {
                if (skippedVolumes.Add(r.target))
                    Debug.LogWarning($"[VoxelDamage] {r.target}: no writable volume — not a live chunk or sector placement. Skipped.");
                return;
            }

            var st = EnsureDamageState(r.target);
            if (st == null) return;

            dirtyScratch.Clear();
            int removed = VoxelDamageMath.Puncture(st.data, st.w, st.h, st.d,
                r.cell.x, r.cell.y, r.cell.z, r.shotDir.x, r.shotDir.y, r.shotDir.z,
                r.seed, ring1RemoveFraction, crackedMaterial, dirtyScratch, out int cracked);
            if (dirtyScratch.Count == 0) return;

            LastRemoved = removed;
            LastCracked = cracked;
            chunkManager.UploadVoxelEdits(r.target, st.data, dirtyScratch);
            EmitShards(st, r);
            if (glassAudioEnabled && removed > 0)
                AudioSource.PlayClipAtPoint(GlassClip(removed), r.point,
                    glassAudioVolume * Mathf.Clamp01(0.4f + removed / 20f));
            Debug.Log($"[VoxelDamage] 🔨 {r.target} cell {r.cell} mat {r.material}: -{removed} glass cells, {cracked} cracked — hole persists");
        }

        /// <summary>COW clone on first damage — clone registers into collision by id.</summary>
        private DamageState EnsureDamageState(string volumeId)
        {
            if (cow.TryGetValue(volumeId, out var st)) return st;
            if (!world.TryGetVolume(volumeId, out uint[] src, out int w, out int h, out int d,
                    out Vector3 corner, out float vs, out float yaw)) return null;
            st = new DamageState
            {
                data = (uint[])src.Clone(), original = src,
                w = w, h = h, d = d, corner = corner, vs = vs, yaw = yaw,
            };
            world.RegisterVolume(volumeId, st.data, w, h, d, corner, vs, yaw);   // replaces by id
            cow[volumeId] = st;
            return st;
        }

        /// <summary>
        /// Demolition bench: detonate a sphere inside a volume. Same COW + dirty-span
        /// pipeline as the bullet path — works on chunks and baked sectors alike.
        /// Removed cells fly radially from the blast point, tinted by their own
        /// material so debris reads as building parts.
        /// </summary>
        public bool Detonate(string volumeId, Vector3 worldPoint, float radiusMeters, float power)
        {
            if (!glassDamageEnabled) return false;
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (world == null || chunkManager == null) return false;
            if (string.IsNullOrEmpty(volumeId) || !chunkManager.HasWritableVolume(volumeId))
            {
                Debug.LogWarning($"[VoxelDamage] 💥 blast skipped — '{volumeId ?? "null"}' isn't a writable volume");
                return false;
            }
            var st = EnsureDamageState(volumeId);
            if (st == null) return false;
            Vector3 cell = WorldToCell(st, worldPoint);

            dirtyScratch.Clear();
            blastIdx.Clear(); blastMats.Clear();
            int removed = VoxelDamageMath.Blast(st.data, st.w, st.h, st.d,
                cell.x, cell.y, cell.z, radiusMeters / st.vs, power,
                0xB1A57000u + (uint)blastCounter++, crackedMaterial,
                dirtyScratch, blastIdx, blastMats, out int cracked, occlusionAware);
            if (dirtyScratch.Count == 0)
            {
                Debug.Log($"[VoxelDamage] 💥 {volumeId}: blast removed nothing (radius {radiusMeters:F1}m, power {power:F2})");
                return false;
            }

            // connectivity — islands the ground anchor can no longer reach join the
            // same dirty batch (one upload covers crater + collapse)
            int detached = 0;
            if (dropUnsupported)
            {
                detIdx.Clear(); detMats.Clear(); detEnds.Clear();
                if (visitedScratch == null || visitedScratch.Length < st.data.Length)
                    visitedScratch = new byte[st.data.Length];
                detached = VoxelDamageMath.FindDetached(st.data, st.w, st.h, st.d,
                    detIdx, detMats, detEnds, visitedScratch);
                for (int i = 0; i < detached; i++)
                {
                    st.data[detIdx[i]] = 0u;
                    dirtyScratch.Add(detIdx[i]);
                }
            }

            LastRemoved = removed + detached;
            LastCracked = cracked;
            emitShards = emitMass = emitEvictMass = 0;
            emitMatMass.Clear();
            chunkManager.UploadVoxelEdits(volumeId, st.data, dirtyScratch);
            EmitShards(st, worldPoint, blastIdx, blastMats);
            if (detached > 0) EmitDebris(st, worldPoint, detIdx, detMats, 0);
            Debug.Log($"[VoxelDamage] 🧨 debris {volumeId}: {emitShards} shards carrying {emitMass:N0} cells airborne" +
                $"{EmitMatSummary()}" +
                $"{(emitEvictMass > 0 ? $" · {emitEvictMass:N0} cells evicted" : "")} — rubble spills as they land");
            if (glassAudioEnabled)
                AudioSource.PlayClipAtPoint(GlassClip(removed + detached), worldPoint,
                    glassAudioVolume * Mathf.Clamp01(0.4f + (removed + detached) / 20f));
            Debug.Log($"[VoxelDamage] 💥 {volumeId} @ {worldPoint}: -{removed} blast" +
                $"{(detached > 0 ? $", -{detached} detached in {detEnds.Count} islands" : "")}" +
                $", {cracked} cracked — crater persists");
            return true;
        }

        /// <summary>
        /// Shock-front detonation: the whole explosion is SIMULATED up front on a
        /// scratch clone (K expanding-radius passes + connectivity → BlastPlan),
        /// then played back as scheduled writes — the front visibly chews outward,
        /// shards stream per wavefront, islands drop at the end. The fuse time is
        /// the compute budget; playback frames are tiny dirty-span uploads.
        /// </summary>
        public bool DetonateEvent(string volumeId, Vector3 worldPoint, float radiusMeters,
            float power, int passes, float durationSec)
        {
            if (!glassDamageEnabled) return false;
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (world == null || chunkManager == null) return false;
            if (string.IsNullOrEmpty(volumeId) || !chunkManager.HasWritableVolume(volumeId))
            {
                Debug.LogWarning($"[VoxelDamage] 💥 event skipped — '{volumeId ?? "null"}' isn't a writable volume");
                return false;
            }
            var st = EnsureDamageState(volumeId);
            if (st == null) return false;
            Vector3 cell = WorldToCell(st, worldPoint);

            var scratch = (uint[])st.data.Clone();
            var plan = VoxelDamageMath.SimulateBlast(scratch, st.w, st.h, st.d,
                cell.x, cell.y, cell.z, radiusMeters / st.vs, power,
                0xB1A57000u + (uint)blastCounter++, Mathf.Max(1, passes), crackedMaterial,
                occlusionAware, dropUnsupported, visitedScratch);
            if (plan.writeIdx.Count == 0)
            {
                Debug.Log($"[VoxelDamage] 💥 {volumeId}: sim removed nothing (radius {radiusMeters:F1}m, power {power:F2})");
                return false;
            }
            int crackedTotal = 0;
            foreach (var v in plan.writeVal) if (v != 0u) crackedTotal++;
            LastRemoved = plan.totalRemoved;
            LastCracked = crackedTotal;
            planBusyUntil = Mathf.Max(planBusyUntil, Time.time + Mathf.Max(0.01f, durationSec) + 0.25f);
            planAirCells = -1;
            emitShards = emitMass = emitEvictMass = 0;
            emitMatMass.Clear();
            StartCoroutine(PlayPlan(volumeId, st, plan, worldPoint, Mathf.Max(0.01f, durationSec)));
            Debug.Log($"[VoxelDamage] 💥 SIM {volumeId} @ {worldPoint}: {plan.totalRemoved} doomed cells, " +
                $"{crackedTotal} cracked, {plan.passes} fronts" +
                $"{(plan.islandEnds.Count > 0 ? $" + {plan.islandEnds.Count} islands" : "")} — playing {durationSec:F1}s");
            return true;
        }

        /// <summary>
        /// Playback: the flash — every blast write lands in ONE upload at t=0 so the
        /// building is instantly scarred; the motion lives in the particles, which
        /// stream outward per wavefront over `duration` (expanding debris, and it
        /// amortizes the shard budget). A beat later the unsupported islands drop —
        /// explosion, then collapse.
        /// </summary>
        private IEnumerator PlayPlan(string volumeId, DamageState st, VoxelDamageMath.BlastPlan plan,
            Vector3 center, float duration)
        {
            ApplyPlanSpan(volumeId, st, plan, 0, plan.detachedStart, center);   // the flash
            if (glassAudioEnabled)
                AudioSource.PlayClipAtPoint(GlassClip(plan.totalRemoved), center,
                    glassAudioVolume * Mathf.Clamp01(0.3f + plan.totalRemoved / 200f));
            float step = duration / plan.passes;
            int start = 0;
            for (int i = 0; i < plan.passes; i++)                              // the front — particles only
            {
                int end = plan.passEnds[i];
                EmitPlanShards(st, center, plan, start, end);
                start = end;
                if (i < plan.passes - 1) yield return new WaitForSeconds(step);
            }
            if (plan.detachedStart < plan.writeIdx.Count)                      // the settle
            {
                yield return new WaitForSeconds(step);
                ApplyPlanSpan(volumeId, st, plan, plan.detachedStart, plan.writeIdx.Count, center);
                EmitDebris(st, center, plan.writeIdx, plan.writeMat, plan.detachedStart);
            }
            Debug.Log($"[VoxelDamage] 🧨 debris {volumeId}: {emitShards} shards carrying {emitMass:N0} cells airborne" +
                $"{EmitMatSummary()}" +
                $"{(emitEvictMass > 0 ? $" · {emitEvictMass:N0} cells evicted" : "")} — rubble spills as they land");
        }

        /// <summary>Apply a span of plan writes → one dirty-span upload. No shards/audio — PlayPlan owns staging.</summary>
        private void ApplyPlanSpan(string volumeId, DamageState st, VoxelDamageMath.BlastPlan plan,
            int start, int end, Vector3 center)
        {
            dirtyScratch.Clear();
            for (int i = start; i < end; i++)
            {
                st.data[plan.writeIdx[i]] = plan.writeVal[i];
                dirtyScratch.Add(plan.writeIdx[i]);
            }
            chunkManager.UploadVoxelEdits(volumeId, st.data, dirtyScratch);
        }

        /// <summary>world → cell coords (invert the RotY placement convention)</summary>
        private Vector3 WorldToCell(DamageState st, Vector3 worldPoint)
        {
            Vector3 half = new Vector3(st.w, st.h, st.d) * (st.vs * 0.5f);
            Vector3 lp = worldPoint - (st.corner + half);
            float rad = -st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector3 local = new Vector3(lp.x * c + lp.z * s, lp.y, -lp.x * s + lp.z * c);
            return (local + half) / st.vs;
        }

        /// <summary>
        /// Radial debris for one plan wavefront: the front's AIR writes spatial-hash into debris
        /// buckets — one shard per bucket, carrying the bucket's whole cell count as mass.
        /// </summary>
        private void EmitPlanShards(DamageState st, Vector3 blastCenter, VoxelDamageMath.BlastPlan plan, int start, int end)
        {
            if (!shardBurstEnabled) return;
            if (planAirCells < 0)
            {
                planAirCells = 0;
                for (int i = 0; i < plan.detachedStart; i++) if (plan.writeVal[i] == 0u) planAirCells++;
            }
            int air = 0;
            for (int i = start; i < end; i++) if (plan.writeVal[i] == 0u) air++;
            EmitClusterRange(st, blastCenter, plan.writeIdx, plan.writeMat, plan.writeVal,
                start, end, Mathf.Max(2, Mathf.CeilToInt(debrisClusterM / st.vs)),
                Mathf.Max(4, Mathf.RoundToInt(maxShards * FrontShardShare * air / Mathf.Max(1, planAirCells))), radial: true);
        }
        private readonly List<int> blastIdx = new();
        private readonly List<uint> blastMats = new();
        private int blastCounter;

        /// <summary>After a blast, flood-fill from the volume's base: solids the anchor
        /// can't reach drop as debris islands. Blast-only — bullet punctures never detach.</summary>
        public bool dropUnsupported = true;
        /// <summary>Spatial bucket size for aggregating detached islands into debris shards.</summary>
        public float debrisClusterM = 0.9f;
        /// <summary>Walls attenuate the blast along the bomb→cell path (DDA march).
        /// Toggle off to A/B against the raw delete-sphere.</summary>
        public bool occlusionAware = true;
        /// <summary>DetonateEvent: simulate K expanding fronts up front, play back over frontSec.</summary>
        public bool shockFront = true;
        public int frontPasses = 16;
        public float frontSec = 0.5f;
        private readonly List<int> detIdx = new();
        private readonly List<uint> detMats = new();
        private readonly List<int> detEnds = new();
        private byte[] visitedScratch;
        private readonly Dictionary<int, ShardPhysics.DebrisBucket> clusterBuckets = new();

        private Quaternion RandRot()
            => new Quaternion((float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f,
                (float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f).normalized;
        private Vector3 RandAng(float rate)
            => new Vector3((float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f,
                (float)shardRng.NextDouble() - 0.5f) * rate;

        /// <summary>
        /// World-space center of a registered volume — yaw pivots about the center,
        /// so it's simply corner + half-extent regardless of rotation.
        /// </summary>
        public bool TryGetVolumeCenter(string volumeId, out Vector3 center)
        {
            center = default;
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (world == null || string.IsNullOrEmpty(volumeId)) return false;
            if (!world.TryGetVolume(volumeId, out _, out int w, out int h, out int d,
                    out Vector3 corner, out float vs, out _)) return false;
            center = corner + new Vector3(w, h, d) * (vs * 0.5f);
            return true;
        }

        /// <summary>
        /// Removed cells are the dirty indices whose final value is AIR — each one
        /// re-spawns as a shard at its world position, velocity = shotDir plus a
        /// small scatter cone (design doc: the removed voxels ARE the particles).
        /// </summary>
        private void EmitShards(DamageState st, ProjectileSystem.ShotReport r)
        {
            if (!shardBurstEnabled) return;
            float vs = st.vs;
            Vector3 half = new Vector3(st.w, st.h, st.d) * (vs * 0.5f);
            Vector3 center = st.corner + half;
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Color col = StAssetReader.GetMaterialColor((ushort)r.material);
            foreach (int idx in dirtyScratch)
            {
                if (st.data[idx] != 0) continue;   // cracked cells stay in place — only AIR flies
                int z = idx / (st.w * st.h), rem = idx - z * st.w * st.h, y = rem / st.w, x = rem - y * st.w;
                Vector3 lp = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * vs - half;
                var sh = new Shard
                {
                    pos = center + new Vector3(lp.x * c + lp.z * s, lp.y, -lp.x * s + lp.z * c),
                    vel = r.shotDir * shardSpeed * (0.7f + 0.6f * (float)shardRng.NextDouble())
                        + new Vector3((float)(shardRng.NextDouble() - 0.5), (float)(shardRng.NextDouble() - 0.5) + 0.3f,
                            (float)(shardRng.NextDouble() - 0.5)) * shardSpread,
                    tEnd = Time.time + shardLife * (0.7f + 0.6f * (float)shardRng.NextDouble()),
                    beamId = shardCounter++,
                    col = col,
                    mat = (ushort)(r.material & 0x1FF),
                    mass = 1,
                    size = st.vs * 0.5f,
                    rot = RandRot(), angVel = RandAng(12f),
                };
                PushShard(sh);
            }
        }

        /// <summary>
        /// Radial variant for blasts (non-front path): removed cells cluster into debris
        /// buckets the same way wavefronts do — one shard per bucket, mass-conserving,
        /// tinted by the bucket's majority material.
        /// </summary>
        private void EmitShards(DamageState st, Vector3 blastCenter, List<int> removedIdx, List<uint> removedMats)
        {
            if (!shardBurstEnabled) return;
            EmitClusterRange(st, blastCenter, removedIdx, removedMats, null,
                0, removedIdx.Count, Mathf.Max(2, Mathf.CeilToInt(debrisClusterM / st.vs)),
                Mathf.Max(4, Mathf.RoundToInt(maxShards * FrontShardShare)), radial: true);
        }

        /// <summary>
        /// Detached islands fall as clustered debris: the whole detached span spatial-hashes into
        /// buckets sized to the island share of the shard budget — one falling shard per bucket
        /// carrying the bucket's mass, so a collapsed region lands as mounds of its own cells.
        /// </summary>
        private void EmitDebris(DamageState st, Vector3 blastCenter, List<int> idx, List<uint> mats, int idxBase = 0)
        {
            if (!shardBurstEnabled) return;
            EmitClusterRange(st, blastCenter, idx, mats, null, idxBase, idx.Count,
                Mathf.Max(2, Mathf.CeilToInt(debrisClusterM / st.vs)),
                Mathf.Max(2, Mathf.RoundToInt(maxShards * IslandShardShare)), radial: false);
        }

        /// <summary>Top carried materials as "matId×cells" for the debris log.</summary>
        private string EmitMatSummary()
        {
            var top = new List<KeyValuePair<ushort, int>>(emitMatMass);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < top.Count && i < 3; i++)
                sb.Append(i == 0 ? " · " : ", ").Append($"{top[i].Key}×{top[i].Value:N0}");
            return sb.ToString();
        }

        /// <summary>
        /// One debris shard per spatial bucket over idx[start..end): cells hash into cs-pitch buckets and
        /// each shard CARRIES the bucket's cell count as `mass` — it renders as a plain cube whose edge is
        /// ∛count voxels capped at RenderCapCells (a chunk reads as a chunk, never as a true-shape slab)
        /// tinted by the bucket's majority material. On rest TryDeposit pours the mass back into the
        /// volume as a mound, so rubble ≈ what was removed, not what fit the particle budget. vals
        /// (optional) filters to AIR writes so cracked cells never spawn debris. If the pitch produces
        /// more than `target` buckets it is auto-coarsened until they fit (bucket count scales ~1/cs³) —
        /// every removed cell still lands in some bucket, so emission is mass-conserving at any budget.
        /// radial launches along (rep cell − blastCenter) damped by the chunk's edge (big sections drift
        /// and thud, dust flies far); non-radial is the island drop — mostly down, slight outward.
        /// </summary>
        private void EmitClusterRange(DamageState st, Vector3 blastCenter, List<int> idx, List<uint> mats,
            List<uint> vals, int start, int end, int cs, int target, bool radial)
        {
            float vs = st.vs;
            Vector3 half = new Vector3(st.w, st.h, st.d) * (vs * 0.5f);
            Vector3 center = st.corner + half;
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            ShardPhysics.BuildBuckets(idx, mats, st.w, st.h, start, end, cs, clusterBuckets, vals);
            for (int guard = 0; clusterBuckets.Count > target && guard < 3; guard++)
            {
                cs = Mathf.CeilToInt(cs * Mathf.Pow(clusterBuckets.Count / (float)target, 1f / 3f));
                ShardPhysics.BuildBuckets(idx, mats, st.w, st.h, start, end, cs, clusterBuckets, vals);
            }
            foreach (var b in clusterBuckets.Values)
            {
                emitMatMass[(ushort)(b.mat & 0x1FFu)] = emitMatMass.TryGetValue((ushort)(b.mat & 0x1FFu), out int em) ? em + b.majCells : b.majCells;
                if (b.mat2 != 0u)
                    emitMatMass[(ushort)(b.mat2 & 0x1FFu)] = emitMatMass.TryGetValue((ushort)(b.mat2 & 0x1FFu), out int em2) ? em2 + b.othCells : b.othCells;
                int ri = b.repIdx;                                        // spawn AT a real removed cell - the raw
                int rz = ri / (st.w * st.h), rrem = ri - rz * st.w * st.h, ry = rrem / st.w, rx = rrem - ry * st.w;
                Vector3 lp = new Vector3(rx + 0.5f, ry + 0.5f, rz + 0.5f) * vs - half;   // centroid can sit inside surviving solid
                Vector3 wpos = center + new Vector3(lp.x * c + lp.z * s, lp.y, -lp.x * s + lp.z * c);
                float edge = Mathf.Max(1f, Mathf.Pow(b.count, 1f / 3f));   // cube-edge equivalent of the mass, in cells
                Vector3 vel;
                if (radial)
                {
                    Vector3 dir = wpos - blastCenter;
                    dir = dir.sqrMagnitude < 1e-6f
                        ? new Vector3((float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f).normalized
                        : dir.normalized;
                    vel = (dir * shardSpeed * 1.8f * (0.7f + 0.6f * (float)shardRng.NextDouble())
                        + new Vector3((float)(shardRng.NextDouble() - 0.5), (float)(shardRng.NextDouble() - 0.5) + 0.3f,
                            (float)(shardRng.NextDouble() - 0.5)) * shardSpread) / Mathf.Sqrt(edge);
                }
                else
                {
                    Vector3 outw = wpos - blastCenter; outw.y = 0f;
                    if (outw.sqrMagnitude < 1e-6f) outw = Vector3.right;
                    vel = Vector3.down * shardSpeed * (0.5f + 0.5f * (float)shardRng.NextDouble())
                        + outw.normalized * shardSpeed * 0.3f * (float)shardRng.NextDouble();
                }
                PushShard(new Shard
                {
                    pos = wpos,
                    vel = vel,
                    tEnd = Time.time + shardLife * (radial ? 1.4f : 2.2f) * (0.7f + 0.6f * (float)shardRng.NextDouble()),
                    beamId = shardCounter++,
                    col = StAssetReader.GetMaterialColor((ushort)b.mat),
                    mat = (ushort)(b.mat & 0x1FFu),
                    mat2 = (ushort)(b.mat2 & 0x1FFu),
                    mix2 = b.count > 0 ? b.othCells / (float)b.count : 0f,
                    mass = b.count,
                    size = vs * 0.75f * Mathf.Min(edge, RenderCapCells),
                    rot = RandRot(), angVel = RandAng(9f / Mathf.Sqrt(edge)),
                });
            }
        }

        /// <summary>Enqueue a shard under the budget — the evicted shard's beam must be
        /// cleared or it freezes mid-air forever (expiry clears; eviction forgot).</summary>
        private void PushShard(Shard sh)
        {
            if (shards.Count >= maxShards)
            {
                // prefer evicting debris that can never become rubble (still flying, or resting on
                // terrain / an undamaged volume) so a settled chunk's mass isn't silently dropped
                int evict = 0;
                for (int i = 0; i < shards.Count; i++)
                    if (!shards[i].depositWait) { evict = i; break; }
                var ev = shards[evict];
                if (ev.depositWait && Time.time >= planBusyUntil) TryDeposit(ref ev, int.MaxValue, out _);   // rescue the settled mass before dropping it
                emitEvictMass += ev.mass;
                PathDebugRenderer.Instance?.ClearCustomBeam($"gshard_{ev.beamId}");
                PathDebugRenderer.Instance?.ClearCustomCube($"gshard_{ev.beamId}");
                shards.RemoveAt(evict);
            }
            sh.mass0 = sh.mass; sh.size0 = sh.size;                     // pour render shrinks toward 0 from these
            shards.Add(sh);
            emitShards++; emitMass += sh.mass;
        }

        /// <summary>
        /// Integrate shards, draw each as a tumbling cube, expire dead ones. Airborne shards sweep the voxel world
        /// (ShardPhysics.Step: floors, roofs, walls, earlier rubble) and keep the flat ground plane as the fallback when
        /// collision is off or nothing is under them. A shard that comes to rest on a damaged volume waits to be written
        /// back as rubble (TryDeposit) instead of just fading.
        /// </summary>
        private void UpdateShards()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null || shards.Count == 0) return;
            float dt = Time.deltaTime;
            bool sweep = shardVoxelCollision && world != null && world.HasRaycastGeometry;
            int deposits = 0, depositCells = 0;
            if (sweep && shardCast == null)
                shardCast = (Vector3 o, Vector3 dir, float max, out VoxelRayHit h) => world.Raycast(o, dir, max, out h);
            for (int i = shards.Count - 1; i >= 0; i--)
            {
                var s = shards[i];
                if (!s.resting)
                {
                    s.rot = Quaternion.Euler(s.angVel.x * Mathf.Rad2Deg * dt,
                        s.angVel.y * Mathf.Rad2Deg * dt, s.angVel.z * Mathf.Rad2Deg * dt) * s.rot;
                    if (sweep)
                    {
                        var step = ShardPhysics.Step(ref s.pos, ref s.vel, s.size * 0.5f, dt, shardGravity,
                            shardBounce, 0.6f, shardCast, out var hit);
                        if (step == ShardStep.Embedded)
                        {
                            // wedged into surviving solid - after the collapse window, spill the mass it
                            // carries into the air around it instead of losing it (a chunk that clips a
                            // wall edge leaves its cells in the wall's cavities)
                            if (s.mass > 1 && Time.time >= planBusyUntil && s.vol != null && cow.TryGetValue(s.vol, out var est))
                            {
                                Vector3 cp = WorldToCell(est, s.pos);
                                dirtyScratch.Clear();
                                int written = ShardPhysics.DepositHeap(est.data, est.w, est.h, est.d,
                                    Mathf.FloorToInt(cp.x), Mathf.FloorToInt(cp.y), Mathf.FloorToInt(cp.z),
                                    s.mass0 > 0 ? s.mass0 : s.mass, rubbleMaxCells, s.mat, s.mat2, s.mix2,
                                    s.mass, dirtyScratch);
                                if (written > 0)
                                {
                                    for (int di = 0; di < dirtyScratch.Count; di++) { est.rubble.Add(dirtyScratch[di]); est.slumpQueue.Add(dirtyScratch[di]); }
                                    chunkManager.UploadVoxelEdits(s.vol, est.data, dirtyScratch); RubbleCells += written;
                                }
                            }
                            s.resting = true; s.depositWait = false; s.vel = s.angVel = Vector3.zero;
                            s.tEnd = Mathf.Min(s.tEnd, Time.time + 0.15f);      // swallowed by geometry - fade out fast
                        }
                        else if (step == ShardStep.Contact)
                        {
                            s.vol = hit.volumeId;
                            s.angVel *= 0.55f;
                            if (hit.normal.y > 0.7f && s.vel.sqrMagnitude < 0.16f) SettleShard(ref s);
                        }
                    }
                    else
                    {
                        s.pos += s.vel * dt;
                        s.vel.y -= shardGravity * dt;
                    }
                    float floor = shardGroundY + s.size * 0.5f;     // flat plane: the whole floor with collision off, the safety net under it otherwise
                    if (!s.resting && s.pos.y < floor && s.vel.y < 0f)
                    {
                        s.pos.y = floor;
                        s.vel.y = -s.vel.y * shardBounce;
                        s.vel.x *= 0.6f; s.vel.z *= 0.6f;
                        s.angVel *= 0.55f;
                        s.vol = null;
                        if (s.vel.sqrMagnitude < 0.16f) SettleShard(ref s);
                    }
                }
                bool dead = Time.time >= s.tEnd;
                bool willClear = dead && !(s.depositWait && s.mass > 0);     // a pouring shard outlives its expiry
                if (!willClear && s.depositWait && deposits < RubblePerFrame && depositCells < RubbleCellsPerFrame &&
                    (Time.time >= planBusyUntil || dead))
                {
                    int pour = Mathf.Min(Mathf.Max(1, (int)(rubblePourPerSec * dt)), RubbleCellsPerFrame - depositCells);
                    if (TryDeposit(ref s, pour, out int wrote))
                    { deposits++; depositCells += wrote; dead = s.mass <= 0; willClear = dead; }   // fully poured - the mound remains
                }
                if (willClear)
                {
                    pdr.ClearCustomBeam($"gshard_{s.beamId}");
                    pdr.ClearCustomCube($"gshard_{s.beamId}");
                    shards[i] = shards[shards.Count - 1];
                    shards.RemoveAt(shards.Count - 1);
                    continue;
                }
                float fade = (s.tEnd - Time.time) / shardLife;
                var c = s.col; c.a = Mathf.Lerp(0.15f, 0.9f, fade);
                pdr.SetCustomCube($"gshard_{s.beamId}", s.pos, s.rot, s.size, c);
                shards[i] = s;
            }
        }

        /// <summary>
        /// Angle-of-repose pump: runs every frame (not just while shards live) so deposited rubble
        /// keeps relaxing after the last chunk pours. Each damaged volume's slumpQueue feeds
        /// ShardPhysics.Slump up to a per-frame move budget — a big collapse visibly settles over
        /// several frames instead of snapping to its final cone.
        /// </summary>
        private void PumpRubbleSlump()
        {
            if (chunkManager == null) return;
            int budget = SlumpCellsPerFrame;
            foreach (var kv in cow)
            {
                var st = kv.Value;
                if (st.slumpQueue.Count == 0) continue;
                dirtyScratch.Clear();
                int moves = ShardPhysics.Slump(st.data, st.w, st.h, st.d, st.rubble, st.slumpQueue, budget, dirtyScratch);
                if (moves > 0) { chunkManager.UploadVoxelEdits(kv.Key, st.data, dirtyScratch); budget -= moves; }
                if (budget <= 0) break;
            }
        }

        /// <summary>Come to rest. On a damaged volume the shard waits (up to RubbleWaitSec) to be written back as rubble; anywhere else it lingers shardRestSec and fades.</summary>
        private void SettleShard(ref Shard s)
        {
            s.resting = true; s.vel = Vector3.zero; s.angVel = Vector3.zero;
            s.depositWait = rubbleDeposit && s.mat != 0 && s.vol != null && cow.ContainsKey(s.vol);
            if (s.depositWait) s.tEnd = Time.time + RubbleWaitSec;
            else Linger(ref s);
        }

        /// <summary>Give up on becoming rubble: just sit for shardRestSec and fade.</summary>
        private void Linger(ref Shard s)
        {
            s.depositWait = false;
            s.tEnd = Mathf.Min(s.tEnd, Time.time + shardRestSec);
        }

        /// <summary>
        /// A resting shard POURS: writes up to `budget` cells of its carried mass into the mound under it
        /// each call (DepositHeap's deterministic ring order resumes the pile - earlier writes are solid
        /// now) and shrinks its rendered cube toward nothing as mass drains, so the chunk visibly melts
        /// into rubble instead of popping a mound. The surface under it is re-probed every tick - if an
        /// island dropped out from beneath it the shard wakes and falls with its remaining mass instead
        /// of leaving rubble hanging in mid-air. True = the pour ticked; wrote = cells deposited this call.
        /// </summary>
        private bool TryDeposit(ref Shard s, int budget, out int wrote)
        {
            wrote = 0;
            if (world == null || chunkManager == null) { Linger(ref s); return false; }
            float r = s.size * 0.5f;
            if (!world.Raycast(s.pos, Vector3.down, r + 0.05f, out var sup) || sup.normal.y < 0.7f)
            {
                s.resting = false; s.depositWait = false;
                s.tEnd = Mathf.Max(s.tEnd, Time.time + shardLife);
                return false;
            }
            if (sup.volumeId == null || !cow.TryGetValue(sup.volumeId, out var st)) { Linger(ref s); return false; }
            Vector3 cp = WorldToCell(st, new Vector3(s.pos.x, sup.point.y + r + ShardPhysics.Skin, s.pos.z));
            dirtyScratch.Clear();
            int written = ShardPhysics.DepositHeap(st.data, st.w, st.h, st.d,
                Mathf.FloorToInt(cp.x), Mathf.FloorToInt(cp.y), Mathf.FloorToInt(cp.z),
                s.mass0 > 0 ? s.mass0 : s.mass, rubbleMaxCells, s.mat, s.mat2, s.mix2,
                Mathf.Min(budget, s.mass), dirtyScratch);
            if (written == 0) { Linger(ref s); return false; }
            for (int i = 0; i < dirtyScratch.Count; i++) { st.rubble.Add(dirtyScratch[i]); st.slumpQueue.Add(dirtyScratch[i]); }
            chunkManager.UploadVoxelEdits(sup.volumeId, st.data, dirtyScratch);
            RubbleCells += written;
            s.mass -= written;
            s.size = Mathf.Max(st.vs * 0.4f, s.size0 * Mathf.Pow(s.mass / (float)Mathf.Max(1, s.mass0), 1f / 3f));   // the cube melts into the mound
            s.tEnd = Mathf.Max(s.tEnd, Time.time + 0.5f);                    // keep the pourer alive while it drains
            wrote = written;
            if (Time.time - rubbleLogT > 2f)
                Debug.Log($"[VoxelDamage] 🧱 rubble settling into {sup.volumeId}: shard poured {written} cell(s), {s.mass} left" +
                    $" (mat {s.mat}{(s.mat2 != 0 ? $"/{s.mat2} @{s.mix2:F2}" : "")}) — {RubbleCells} total");
            rubbleLogT = Time.time;
            return true;
        }

        /// <summary>
        /// Synthesized glass sound — no audio assets in the project, so the clip is
        /// generated: a sharp noise attack + inharmonic decaying partials (glass rings
        /// non-harmonically). Removed-count picks the bucket: tink / crack / crash.
        /// </summary>
        private AudioClip GlassClip(int removed)
        {
            int bucket = removed <= 4 ? 0 : removed <= 15 ? 1 : 2;
            if (clipCache.TryGetValue(bucket, out var clip)) return clip;
            const int rate = 44100;
            float dur = bucket == 0 ? 0.14f : bucket == 1 ? 0.28f : 0.55f;
            float noiseAmp = bucket == 0 ? 0.3f : bucket == 1 ? 0.5f : 0.85f;
            float decay = bucket == 0 ? 45f : bucket == 1 ? 24f : 10f;
            float[] partials = bucket == 0
                ? new[] { 5200f, 7400f }
                : bucket == 1 ? new[] { 2800f, 3900f, 5600f } : new[] { 1500f, 2200f, 3100f, 4700f };
            int n = (int)(rate * dur);
            var smp = new float[n];
            var rng = new System.Random(1337 + bucket);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Exp(-decay * t);
                float v = (float)(rng.NextDouble() * 2 - 1) * noiseAmp * Mathf.Exp(-60f * t);   // attack click
                for (int p = 0; p < partials.Length; p++)
                    v += Mathf.Sin(2f * Mathf.PI * partials[p] * t + p * 1.7f) * (0.5f / (p + 1)) * env;
                v += (float)(rng.NextDouble() * 2 - 1) * noiseAmp * 0.35f * env;                // noise tail
                smp[i] = Mathf.Clamp(v, -1f, 1f) * 0.8f;
            }
            clip = AudioClip.Create($"glass_{bucket}", n, 1, rate, false);
            clip.SetData(smp, 0);
            clipCache[bucket] = clip;
            return clip;
        }

        /// <summary>
        /// Preview a synthesized glass clip AT the AudioListener — removes distance
        /// rolloff from the equation so the planning-phase test answers "does audio
        /// reach the mix at all". bucket: 0 = tink, 1 = crack, 2 = crash.
        /// </summary>
        public void PreviewGlassSound(int bucket)
        {
            var listener = FindFirstObjectByType<AudioListener>();
            if (listener == null)
            {
                Debug.LogWarning("[VoxelDamage] 🔊 preview failed — no AudioListener in scene");
                return;
            }
            int removed = bucket == 0 ? 2 : bucket == 1 ? 10 : 30;   // representative counts → clip bucket
            var clip = GlassClip(removed);
            AudioSource.PlayClipAtPoint(clip, listener.transform.position, glassAudioVolume);
            Debug.Log($"[VoxelDamage] 🔊 preview bucket {bucket} — clip {clip.length:F2}s @ listener {listener.name} vol {glassAudioVolume:F2}");
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
            LastRemoved = LastCracked = RubbleCells = 0;
            planBusyUntil = 0f;
            return n;
        }

        /// <summary>Pristine value of a cell in a damaged volume (false = never damaged, so current == original).</summary>
        public bool TryGetOriginalVoxel(string volumeId, int idx, out uint original)
        {
            original = 0;
            if (string.IsNullOrEmpty(volumeId) || !cow.TryGetValue(volumeId, out var st) || idx < 0 || idx >= st.original.Length) return false;
            original = st.original[idx];
            return true;
        }
    }
}
