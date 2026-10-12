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
        public int maxShards = 192;

        [Header("Glass audio (P1, synthesized — no assets)")]
        public bool glassAudioEnabled = true;
        [Range(0f, 1f)] public float glassAudioVolume = 0.6f;

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

        // Shard sim — CPU-integrated, rendered as velocity-stretched PathDebugRenderer
        // beams (an elongated box reads as a flying glass fragment; same pipeline as
        // tracers, so it composites into the map RT for free).
        private struct Shard { public Vector3 pos, vel; public float tEnd; public int beamId; public Color col; public float width; }
        private readonly List<Shard> shards = new();
        private int shardCounter;
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
            UpdateShards();
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
            chunkManager.UploadVoxelEdits(volumeId, st.data, dirtyScratch);
            EmitShards(st, worldPoint, blastIdx, blastMats);
            if (detached > 0) EmitDebris(st, worldPoint, detIdx, detMats, detEnds);
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
                EmitDebris(st, center, plan.writeIdx, plan.writeMat, plan.islandEnds, plan.detachedStart);
            }
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

        /// <summary>Radial shards for one plan wavefront — AIR writes only, tinted by original material.</summary>
        private void EmitPlanShards(DamageState st, Vector3 blastCenter, VoxelDamageMath.BlastPlan plan, int start, int end)
        {
            if (!shardBurstEnabled) return;
            float vs = st.vs;
            Vector3 half = new Vector3(st.w, st.h, st.d) * (vs * 0.5f);
            Vector3 center = st.corner + half;
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            int air = 0;
            for (int i = start; i < end; i++) if (plan.writeVal[i] == 0u) air++;
            int stride = Mathf.Max(1, air / Mathf.Max(1, maxShards));
            for (int i = start; i < end; i += stride)
            {
                if (plan.writeVal[i] != 0u) continue;
                int idx = plan.writeIdx[i];
                int z = idx / (st.w * st.h), rem = idx - z * st.w * st.h, y = rem / st.w, x = rem - y * st.w;
                Vector3 lp = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * vs - half;
                Vector3 wpos = center + new Vector3(lp.x * c + lp.z * s, lp.y, -lp.x * s + lp.z * c);
                Vector3 dir = wpos - blastCenter;
                dir = dir.sqrMagnitude < 1e-6f
                    ? new Vector3((float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f).normalized
                    : dir.normalized;
                PushShard(new Shard
                {
                    pos = wpos,
                    vel = dir * shardSpeed * 1.8f * (0.7f + 0.6f * (float)shardRng.NextDouble())
                        + new Vector3((float)(shardRng.NextDouble() - 0.5), (float)(shardRng.NextDouble() - 0.5) + 0.3f,
                            (float)(shardRng.NextDouble() - 0.5)) * shardSpread,
                    tEnd = Time.time + shardLife * 1.4f * (0.7f + 0.6f * (float)shardRng.NextDouble()),
                    beamId = shardCounter++,
                    col = StAssetReader.GetMaterialColor((ushort)plan.writeMat[i]),
                    width = 0.018f,
                });
            }
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
        public int frontPasses = 12;
        public float frontSec = 0.5f;
        private readonly List<int> detIdx = new();
        private readonly List<uint> detMats = new();
        private readonly List<int> detEnds = new();
        private byte[] visitedScratch;
        private struct DebrisBucket { public int sx, sy, sz, count, matCount; public uint mat; }
        private readonly Dictionary<int, DebrisBucket> clusterBuckets = new();

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
                    width = 0.018f,
                };
                PushShard(sh);
            }
        }

        /// <summary>
        /// Radial variant for blasts: velocity = (shardPos − blastCenter), faster
        /// than bullet spall; per-cell material colors so debris reads as building
        /// parts (brick flies brick-colored, glass glass-colored).
        /// </summary>
        private void EmitShards(DamageState st, Vector3 blastCenter, List<int> removedIdx, List<uint> removedMats)
        {
            if (!shardBurstEnabled) return;
            float vs = st.vs;
            Vector3 half = new Vector3(st.w, st.h, st.d) * (vs * 0.5f);
            Vector3 center = st.corner + half;
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            int stride = Mathf.Max(1, removedIdx.Count / Mathf.Max(1, maxShards));
            for (int i = 0; i < removedIdx.Count; i += stride)
            {
                int idx = removedIdx[i];
                int z = idx / (st.w * st.h), rem = idx - z * st.w * st.h, y = rem / st.w, x = rem - y * st.w;
                Vector3 lp = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * vs - half;
                Vector3 wpos = center + new Vector3(lp.x * c + lp.z * s, lp.y, -lp.x * s + lp.z * c);
                Vector3 dir = wpos - blastCenter;
                dir = dir.sqrMagnitude < 1e-6f
                    ? new Vector3((float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f, (float)shardRng.NextDouble() - 0.5f).normalized
                    : dir.normalized;
                var sh = new Shard
                {
                    pos = wpos,
                    vel = dir * shardSpeed * 1.8f * (0.7f + 0.6f * (float)shardRng.NextDouble())
                        + new Vector3((float)(shardRng.NextDouble() - 0.5), (float)(shardRng.NextDouble() - 0.5) + 0.3f,
                            (float)(shardRng.NextDouble() - 0.5)) * shardSpread,
                    tEnd = Time.time + shardLife * 1.4f * (0.7f + 0.6f * (float)shardRng.NextDouble()),
                    beamId = shardCounter++,
                    col = StAssetReader.GetMaterialColor((ushort)removedMats[i]),
                    width = 0.018f,
                };
                PushShard(sh);
            }
        }

        /// <summary>
        /// Detached islands fall as clustered debris: cells spatial-hash into
        /// ~debrisClusterM buckets, one falling shard per bucket tinted by the
        /// bucket's majority material (Boyer-Moore vote), beam width scaled by
        /// cell count — a wall section reads as falling chunks, not confetti.
        /// </summary>
        private void EmitDebris(DamageState st, Vector3 blastCenter, List<int> idx, List<uint> mats, List<int> ends, int idxBase = 0)
        {
            if (!shardBurstEnabled) return;
            float vs = st.vs;
            Vector3 half = new Vector3(st.w, st.h, st.d) * (vs * 0.5f);
            Vector3 center = st.corner + half;
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            int cs = Mathf.Max(1, Mathf.CeilToInt(debrisClusterM / vs));
            var buckets = clusterBuckets;
            int start = idxBase;
            foreach (int end in ends)
            {
                buckets.Clear();
                for (int i = start; i < end; i++)
                {
                    int idxx = idx[i];
                    int z = idxx / (st.w * st.h), rem = idxx - z * st.w * st.h, y = rem / st.w, x = rem - y * st.w;
                    int key = ((x / cs) << 20) | ((y / cs) << 10) | (z / cs);
                    if (!buckets.TryGetValue(key, out var b)) b = new DebrisBucket();
                    b.sx += x; b.sy += y; b.sz += z; b.count++;
                    if (b.matCount == 0) { b.mat = mats[i]; b.matCount = 1; }
                    else if (mats[i] == b.mat) b.matCount++; else b.matCount--;
                    buckets[key] = b;
                }
                foreach (var b in buckets.Values)
                {
                    Vector3 lp = new Vector3(b.sx / (float)b.count + 0.5f, b.sy / (float)b.count + 0.5f,
                        b.sz / (float)b.count + 0.5f) * vs - half;
                    Vector3 wpos = center + new Vector3(lp.x * c + lp.z * s, lp.y, -lp.x * s + lp.z * c);
                    Vector3 outw = wpos - blastCenter; outw.y = 0f;
                    if (outw.sqrMagnitude < 1e-6f) outw = Vector3.right;
                    PushShard(new Shard
                    {
                        pos = wpos,
                        vel = Vector3.down * shardSpeed * (0.5f + 0.5f * (float)shardRng.NextDouble())
                            + outw.normalized * shardSpeed * 0.3f * (float)shardRng.NextDouble(),
                        tEnd = Time.time + shardLife * 2.2f,
                        beamId = shardCounter++,
                        col = StAssetReader.GetMaterialColor((ushort)b.mat),
                        width = 0.03f * Mathf.Pow(b.count, 1f / 3f),
                    });
                }
                start = end;
            }
        }

        /// <summary>Enqueue a shard under the budget — the evicted shard's beam must be
        /// cleared or it freezes mid-air forever (expiry clears; eviction forgot).</summary>
        private void PushShard(Shard sh)
        {
            if (shards.Count >= maxShards)
            {
                PathDebugRenderer.Instance?.ClearCustomBeam($"gshard_{shards[0].beamId}");
                shards.RemoveAt(0);
            }
            shards.Add(sh);
        }

        /// <summary>Integrate shard positions, draw each as a velocity-stretched beam, expire dead ones.</summary>
        private void UpdateShards()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null || shards.Count == 0) return;
            float dt = Time.deltaTime;
            for (int i = shards.Count - 1; i >= 0; i--)
            {
                var s = shards[i];
                s.pos += s.vel * dt;
                s.vel.y -= shardGravity * dt;
                bool dead = Time.time >= s.tEnd;
                if (dead)
                {
                    pdr.ClearCustomBeam($"gshard_{s.beamId}");
                    shards[i] = shards[shards.Count - 1];
                    shards.RemoveAt(shards.Count - 1);
                    continue;
                }
                float fade = (s.tEnd - Time.time) / shardLife;
                var c = s.col; c.a = Mathf.Lerp(0.15f, 0.9f, fade);
                pdr.SetCustomBeam($"gshard_{s.beamId}", s.pos - s.vel * 0.035f, s.pos, s.width, c);
                shards[i] = s;
            }
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
            LastRemoved = LastCracked = 0;
            return n;
        }
    }
}
