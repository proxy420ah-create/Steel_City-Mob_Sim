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

        [Header("Flying voxel debris")]
        [Tooltip("Removed cells re-spawn as individual flying voxels, batched through PathDebugRenderer.")]
        public bool shardBurstEnabled = true;
        public float shardSpeed = 2.5f;      // along the shot direction
        public float shardSpread = 1.6f;     // lateral scatter
        public float shardGravity = 9f;
        public float shardLife = 0.7f;
        public float shardGroundY = 0f;    // ground plane for bounce/rest (roads ~y0)
        public int maxFlyingCells = 8192;   // pool cap — blasts stride-sample to fit; boundary escapes share the pool
        [Tooltip("Debris sweeps against the voxel world (floors, roofs, walls, earlier rubble) instead of only the flat ground plane.")]
        public bool shardVoxelCollision = true;
        [Tooltip("Cells that land in a volume become persistent rubble (material written back) instead of fading.")]
        public bool rubbleDeposit = true;
        [Tooltip("Blast flyers get a radial kick away from the blast center — off = debris falls with pure gravity (A/B test the spray).")]
        public bool flyerRadialVelocity = false;

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
            public readonly HashSet<int> rubble = new();      // cells that landed - the ONLY cells Slump may move
            public readonly List<int> slumpQueue = new();     // candidate cells for the angle-of-repose pass
            public readonly List<int> uploadDirty = new();    // writes pending a GPU upload — flushed once per volume per frame
            public readonly Dictionary<int, ShardPhysics.FallCell> falling = new();  // in-grid ballistic cells — the voxel IS the particle
            public readonly List<int> fallQueue = new();     // StepFalling iteration slots (-1 = tombstone)
        }
        private readonly Dictionary<string, DamageState> cow = new();
        private readonly List<int> dirtyScratch = new();

        // Flying-voxel debris — the world-space phase of a doomed cell between grids.
        // No rotation, no carried mass, no unpack phase: the cell IS the particle. It
        // moves by axis-accumulator strides sampled through point queries (never a
        // swept ray) and hands off into any volume's falling set, flipping a pristine
        // neighbor into damaged state on first arrival. flyingDraw is rendered by
        // PathDebugRenderer batched by quantized color — no per-entity string keys.
        private struct FlyVoxel { public Vector3 pos, vel, acc; public uint vox; public Color col; public float size, tEnd; public string noEnterVol; public float noEnterUntil; }
        private readonly List<FlyVoxel> flying = new();
        /// <summary>Live draw list — cleared+refilled by UpdateFlyVoxels each frame.</summary>
        public readonly List<PathDebugRenderer.VoxelParticle> flyingDraw = new();
        private ShardPhysics.FlyQuery flyQuery;      // world.SamplePoint adapter, bound lazily
        private const float FlyStepLen = 0.2f;       // meters per point-query stride — sub-cell for all current volumes
        private const int FlyMaxSteps = 12;          // per-frame stride cap → 2.4 m/frame ceiling per cell
        private const float FlyNoEnterSec = 0.35f;   // re-entry cooldown on the volume a flyer just escaped
        private const int SlumpCellsPerFrame = 4096;   // angle-of-repose moves per frame - piles relax visibly over a few frames

        [Header("Cellular collapse (in-grid debris)")]
        /// <summary>Impulse seeded into freshly-detached island cells — radial from the blast,
        /// falling off to zero at 2x the blast radius, so rim cells are thrown and far islands
        /// just drop. Velocity lives in cells/sec inside the grid.</summary>
        public float cellImpulse = 7f;
        [Tooltip("Doomed cells sharing a 2x2 tile AND material class are pre-registered as ONE 2-cell entity — a domino that steps/lands as a unit (a whole brick) and fractures only on asymmetric contact (a broken brick).")]
        public bool pairCells = true;
        public float pairChance = 1.0f;         // fraction of 2x2 tiles that pair — 1.0 pairs every eligible tile
        public float cellImpulseJitter = 2.2f;  // m/s per-axis random — breaks laminar sheets into a spray
        public float cellFallDrag = 1.6f;       // 1/s velocity decay — thrown debris slows, then rains
        public float cellLandSpeed = 0.6f;      // |vel| (cells/sec) under which a grounded cell becomes rubble
        private const int FallMovesPerFrame = 24576;  // cell steps per frame across all volumes — collapse pace governor

        [Header("Damage tint (voxel bits 16-19)")]
        [Tooltip("Bake a BFS gradient from the blast cavity's edge into survivors — darkest at the wound, fading over 'char depth' cells. Lives in the voxel itself: rain, flyers and rubble all carry the same tint.")]
        public bool damageTint = true;
        [Range(0, 8)] public int charReach = 4;          // cells past the blast radius the scorch can still reach
        [Range(0f, 1f)] public float charStrength = 0.65f;  // max darken at the wound edge
        [Range(0f, 1f)] public float charRadial = 0.9f;     // S-curve distance falloff strength — 0 = pure absorption shadowing, 1 = char dies at the reach edge
        [Range(0.2f, 0.95f)] public float charAbsorb = 0.62f; // char transmitted per intervening solid cell — the blast "shadow"
        [Range(0f, 1f)] public float glintStrength = 0.85f; // max lighten for glint cells (future crack webbing)
        [Range(0f, 1f)] public float debrisChar = 0.6f;     // fraction of ejected flyers that fly charred — hash-dithered, pair-uniform
        [Tooltip("Glass punctures write the crack halo as a bright damage nibble (frosted webbing) instead of swapping to cracked material 134 — one material slot saved; A/B against the classic halo.")]
        public bool glassGlint = false;
        [Tooltip("Non-glass projectile hits char the impact cell — a bullet-hole decal instead of removal (glass still punctures normally).")]
        public bool bulletImpactTint = true;
        [Range(0, 4)] public int bulletScorchDepth = 2;        // halo rings around the impact cell
        [Range(0f, 1f)] public float bulletScorchKeep = 0.75f; // halo dither — ragged spall vs smooth ring
        private readonly List<int> tintSeedsScratch = new();
        private readonly List<ShardPhysics.FallEscape> escapedScratch = new();
        private readonly HashSet<int> doomSet = new();          // pairing partner lookups (SeedFalling / EmitFlyersShot)
        private readonly Dictionary<int, int> emitCls = new();  // pairing partner class lookups (EmitFlyersRadial)
        private int flyEmitted;                      // per-blast airborne count for the debris log
        /// <summary>Last blast: crater cells removed per resistance class + detached-island count (MatRes tab readout).</summary>
        public readonly int[] lastBlastRemoved = new int[(int)VoxelDamageMath.ResClass.Count];
        public int lastBlastDetached;
        public int lastBlastPairs;                 // domino ENTITIES registered in-grid (one FallCell, two cells)
        public int lastBlastPairCells;             // cells launched sharing a velocity partner — dominoes + glued flyers
        public int lastBlastProfile;               // ResProfile the last blast resolved — MatRes readout
        public int LastFlyEmitted => flyEmitted;

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
            UnityEngine.Profiling.Profiler.BeginSample("VDS.UpdateFlyers");
            UpdateFlyVoxels();
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("VDS.PumpFalling");
            PumpFallingCells();
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("VDS.PumpRubbleSlump");
            PumpRubbleSlump();
            UnityEngine.Profiling.Profiler.EndSample();
            FlushDamageUploads();
            Shader.SetGlobalFloat("_DamageChar", damageTint ? charStrength : 0f);
            Shader.SetGlobalFloat("_DamageGlint", damageTint ? glintStrength : 0f);
        }

        /// <summary>One UploadVoxelEdits per dirty volume per frame — shard pours,
        /// embedded rescues and slump moves all append into DamageState.uploadDirty
        /// instead of uploading individually (previously N shard uploads/frame,
        /// often into the same volume).</summary>
        private void FlushDamageUploads()
        {
            if (chunkManager == null) return;
            foreach (var kv in cow)
            {
                var st = kv.Value;
                if (st.uploadDirty.Count == 0) continue;
                chunkManager.UploadVoxelEdits(kv.Key, st.data, st.uploadDirty);
                st.uploadDirty.Clear();
            }
        }

        private void OnShotResolved(ProjectileSystem.ShotReport r)
        {
            if (!glassDamageEnabled || !r.hit) return;
            if (string.IsNullOrEmpty(r.target) || r.target == "terrain" || r.target == "none") return;
            if (!ProjectileSystem.IsGlassMaterial(r.material))   // non-glass: the voxel stays — char it as an impact decal
            {
                if (damageTint && bulletImpactTint) ApplyImpactTint(r);
                return;
            }

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
                r.seed, ring1RemoveFraction, crackedMaterial, dirtyScratch, out int cracked, glassGlint);
            if (dirtyScratch.Count == 0) return;

            LastRemoved = removed;
            LastCracked = cracked;
            chunkManager.UploadVoxelEdits(r.target, st.data, dirtyScratch);
            EmitFlyersShot(st, r);
            if (glassAudioEnabled && removed > 0)
                AudioSource.PlayClipAtPoint(GlassClip(removed), r.point,
                    glassAudioVolume * Mathf.Clamp01(0.4f + removed / 20f));
            Debug.Log($"[VoxelDamage] 🔨 {r.target} cell {r.cell} mat {r.material}: -{removed} glass cells, {cracked} cracked — hole persists");
        }

        /// <summary>
        /// Bullet-hole decal: the hit cell survives and keeps a max-strength char
        /// (the dark "pit" of the mark), then the wound-adjacency BFS rings a
        /// faint dithered halo around it — spall pattern, not a stencil dot.
        /// No cells are removed; the tint lives in the voxel's damage nibble.
        /// </summary>
        private void ApplyImpactTint(ProjectileSystem.ShotReport r)
        {
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (world == null || chunkManager == null) return;
            if (!chunkManager.HasWritableVolume(r.target)) return;   // decals are cosmetic — silent skip
            var st = EnsureDamageState(r.target);
            if (st == null) return;
            if (r.cell.x < 0 || r.cell.x >= st.w || r.cell.y < 0 || r.cell.y >= st.h || r.cell.z < 0 || r.cell.z >= st.d) return;
            int idx = r.cell.x + r.cell.y * st.w + r.cell.z * st.w * st.h;
            if ((st.data[idx] & VoxelDda.MaterialMask) == 0) return;   // shot into an existing hole — nothing to mark

            dirtyScratch.Clear();
            uint core = 6u + ((r.seed >> 8) & 1u);                     // 6-7, hash-varied — deepest mark at the impact
            if (core > VoxelDamageMath.DamageOf(st.data[idx]))
            {
                st.data[idx] = VoxelDamageMath.WithDamage(st.data[idx], core);
                dirtyScratch.Add(idx);
            }
            tintSeedsScratch.Clear(); tintSeedsScratch.Add(idx);
            VoxelDamageMath.ApplyDamageGradient(st.data, st.w, st.h, st.d,
                tintSeedsScratch, bulletScorchDepth, glint: false, bulletScorchKeep,
                r.seed, dirtyScratch, visitedScratch);
            if (dirtyScratch.Count > 0)
                chunkManager.UploadVoxelEdits(r.target, st.data, dirtyScratch);
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
            lastBlastProfile = VoxelDamageMath.ResProfileOf(volumeId);
            int removed = VoxelDamageMath.Blast(st.data, st.w, st.h, st.d,
                cell.x, cell.y, cell.z, radiusMeters / st.vs, power,
                0xB1A57000u + (uint)blastCounter++, crackedMaterial,
                dirtyScratch, blastIdx, blastMats, out int cracked, occlusionAware, lastBlastProfile);
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
                    detIdx, detMats, detEnds, visitedScratch, ci => st.falling.ContainsKey(ci));
            }
            // char exposure — every solid cell in reach evaluates its line of sight
            // to the blast: S-curve distance falloff × per-cell absorption shadow.
            // Rides the same dirty batch (tinted cells are material-preserving edits).
            if (damageTint)
                VoxelDamageMath.ApplyDamageExposure(st.data, st.w, st.h, st.d,
                    cell.x, cell.y, cell.z, radiusMeters / st.vs, charReach,
                    charRadial, charAbsorb, dirtyScratch);

            LastRemoved = removed + detached;
            LastCracked = cracked;
            flyEmitted = 0;
            System.Array.Clear(lastBlastRemoved, 0, lastBlastRemoved.Length);
            lastBlastPairs = 0; lastBlastPairCells = 0;
            for (int i = 0; i < blastMats.Count; i++)
                lastBlastRemoved[(int)VoxelDamageMath.ClassOf(blastMats[i])]++;
            lastBlastDetached = detached;
            chunkManager.UploadVoxelEdits(volumeId, st.data, dirtyScratch);
            EmitFlyersRadial(st, worldPoint, blastIdx, blastMats, null, 0, blastIdx.Count, radiusMeters / st.vs);
            if (detached > 0) SeedFalling(st, worldPoint, radiusMeters * 2f, detIdx, 0, detached);
            Debug.Log($"[VoxelDamage] 🧨 debris {volumeId}: {flyEmitted} voxel debris airborne{(flying.Count >= maxFlyingCells ? " (pool capped)" : "")} — cells settle where they land");
            if (detached > 0)
                Debug.Log($"[VoxelDamage] 🌧 {volumeId}: {detached:N0} detached cells stay in-grid — raining debris, not shards");
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
            lastBlastProfile = VoxelDamageMath.ResProfileOf(volumeId);
            var plan = VoxelDamageMath.SimulateBlast(scratch, st.w, st.h, st.d,
                cell.x, cell.y, cell.z, radiusMeters / st.vs, power,
                0xB1A57000u + (uint)blastCounter++, Mathf.Max(1, passes), crackedMaterial,
                occlusionAware, dropUnsupported, visitedScratch, lastBlastProfile);
            if (plan.writeIdx.Count == 0)
            {
                Debug.Log($"[VoxelDamage] 💥 {volumeId}: sim removed nothing (radius {radiusMeters:F1}m, power {power:F2})");
                return false;
            }
            int crackedTotal = 0;
            foreach (var v in plan.writeVal) if (v != 0u) crackedTotal++;
            LastRemoved = plan.totalRemoved;
            LastCracked = crackedTotal;
            flyEmitted = 0;
            System.Array.Clear(lastBlastRemoved, 0, lastBlastRemoved.Length);
            lastBlastPairs = 0; lastBlastPairCells = 0;
            lastBlastDetached = plan.writeIdx.Count - plan.detachedStart;
            for (int i = 0; i < plan.detachedStart; i++)
                if (plan.writeVal[i] == 0u)
                    lastBlastRemoved[(int)VoxelDamageMath.ClassOf(plan.writeMat[i])]++;
            StartCoroutine(PlayPlan(volumeId, st, plan, worldPoint, Mathf.Max(0.01f, durationSec), radiusMeters * 2f));
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
            Vector3 center, float duration, float impulseReach)
        {
            ApplyPlanSpan(volumeId, st, plan, 0, plan.detachedStart, center, impulseReach * 0.5f / st.vs);   // the flash
            if (glassAudioEnabled)
                AudioSource.PlayClipAtPoint(GlassClip(plan.totalRemoved), center,
                    glassAudioVolume * Mathf.Clamp01(0.3f + plan.totalRemoved / 200f));
            float step = duration / plan.passes;
            int start = 0;
            for (int i = 0; i < plan.passes; i++)                              // the front — particles only
            {
                int end = plan.passEnds[i];
                EmitFlyersRadial(st, center, plan.writeIdx, plan.writeMat, plan.writeVal, start, end, impulseReach * 0.5f / st.vs);
                start = end;
                if (i < plan.passes - 1) yield return new WaitForSeconds(step);
            }
            if (plan.detachedStart < plan.writeIdx.Count)                      // the settle — islands
            {                                                                   // rain in place, in-grid
                yield return new WaitForSeconds(step);
                SeedFalling(st, center, impulseReach, plan.writeIdx, plan.detachedStart, plan.writeIdx.Count);
            }
            Debug.Log($"[VoxelDamage] 🧨 debris {volumeId}: {flyEmitted} voxel debris airborne{(flying.Count >= maxFlyingCells ? " (pool capped)" : "")} — cells settle where they land");
        }

        /// <summary>Apply a span of plan writes → one dirty-span upload. No shards/audio — PlayPlan owns staging.</summary>
        private void ApplyPlanSpan(string volumeId, DamageState st, VoxelDamageMath.BlastPlan plan,
            int start, int end, Vector3 center, float radiusCells)
        {
            dirtyScratch.Clear();
            for (int i = start; i < end; i++)
            {
                st.data[plan.writeIdx[i]] = plan.writeVal[i];
                dirtyScratch.Add(plan.writeIdx[i]);
            }
            if (damageTint)   // exposure-field charring — same as the direct path; detached cells tint too (they carry it into rubble)
            {
                Vector3 cc = WorldToCell(st, center);
                VoxelDamageMath.ApplyDamageExposure(st.data, st.w, st.h, st.d,
                    cc.x, cc.y, cc.z, radiusCells, charReach,
                    charRadial, charAbsorb, dirtyScratch);
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

        private readonly List<int> blastIdx = new();
        private readonly List<uint> blastMats = new();
        private int blastCounter;

        /// <summary>After a blast, flood-fill from the volume's base: solids the anchor
        /// can't reach drop as debris islands. Blast-only — bullet punctures never detach.</summary>
        public bool dropUnsupported = true;
        /// <summary>Walls attenuate the blast along the bomb→cell path (DDA march).
        /// Toggle off to A/B against the raw delete-sphere.</summary>
        public bool occlusionAware = true;
        /// <summary>DetonateEvent: simulate K expanding fronts up front, play back over frontSec.</summary>
        public bool shockFront = true;
        public int frontPasses = 6;
        public float frontSec = 0.5f;
        private readonly List<int> detIdx = new();
        private readonly List<uint> detMats = new();
        private readonly List<int> detEnds = new();
        private byte[] visitedScratch;


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
        /// World-space phase of a doomed cell: gravity+drag integrate, the meter
        /// accumulator spends whole FlyStepLen strides sampled through
        /// world.SamplePoint — a point query per stride, exact at this step size, so a
        /// swarm of thousands never pays for swept rays (the physics-sweep mitigation).
        /// Free space moves; solid material (volume cell or terrain) absorbs that
        /// axis's momentum; an open cell inside a volume hands the voxel to that
        /// volume's falling set with velocity intact — a pristine neighboring business
        /// flips into damaged state on first arrival. Terrain isn't writable: a cell
        /// that reaches it simply stops existing (shardGroundY stays the safety floor).
        /// </summary>
        private void UpdateFlyVoxels()
        {
            flyingDraw.Clear();
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null) pdr.SetVoxelParticleSource(flyingDraw);
            if (flying.Count == 0) return;
            float dt = Time.deltaTime;
            bool collide = shardVoxelCollision && world != null && world.HasRaycastGeometry;
            if (collide && flyQuery == null) flyQuery = SampleForFly;
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var fv = flying[i];
                bool dead = Time.time >= fv.tEnd;
                if (!dead)
                {
                    if (collide)
                    {
                        var outcome = ShardPhysics.StepFlyer(ref fv.pos, ref fv.vel, ref fv.acc,
                            dt, shardGravity, cellFallDrag, FlyStepLen, FlyMaxSteps, flyQuery, out var entered);
                        if (outcome == ShardPhysics.FlyOutcome.EnteredOpen &&
                            (entered.volumeId != fv.noEnterVol || Time.time >= fv.noEnterUntil) &&
                            TryEnterVolume(entered, fv))
                        {
                            flying[i] = flying[flying.Count - 1];
                            flying.RemoveAt(flying.Count - 1);
                            continue;
                        }
                    }
                    else { fv.vel.y -= shardGravity * dt; fv.pos += fv.vel * dt; }
                    if (fv.pos.y <= shardGroundY) dead = true;   // below everything — terrain isn't writable
                }
                if (dead)
                {
                    flying[i] = flying[flying.Count - 1];
                    flying.RemoveAt(flying.Count - 1);
                    continue;
                }
                flyingDraw.Add(new PathDebugRenderer.VoxelParticle { pos = fv.pos, size = fv.size, col = fv.col });
                flying[i] = fv;
            }
        }

        /// <summary>world.SamplePoint → the delegate StepFlyer queries: terrain and solid
        /// volume cells are both Solid (a flyer can't move into either); only a volume's
        /// AIR cell is Open (a handoff target).</summary>
        private ShardPhysics.FlySample SampleForFly(Vector3 pos)
        {
            var s = world.SamplePoint(pos);
            return new ShardPhysics.FlySample
            {
                kind = s.kind == VoxelCollisionWorld.PointKind.VolumeOpen ? ShardPhysics.FlySampleKind.Open
                     : s.kind == VoxelCollisionWorld.PointKind.Free ? ShardPhysics.FlySampleKind.Free
                     : ShardPhysics.FlySampleKind.Solid,
                volumeId = s.volumeId,
                cellIdx = s.cellIdx,
            };
        }

        /// <summary>
        /// A flying voxel enters `entered.volumeId` at `entered.cellIdx`: the volume flips
        /// into damaged state on first arrival (copy-on-write — a pristine neighboring
        /// business becomes damageable by debris), the arriving cell keeps its own
        /// material, and it resumes as a falling cell with its remaining velocity remapped
        /// to the target's cell axes. From here StepFalling owns it — same rain,
        /// different grid. data is written immediately (falling cells render at their
        /// current index) so the voxel is visible the tick it arrives.
        /// </summary>
        private bool TryEnterVolume(in ShardPhysics.FlySample entered, in FlyVoxel fv)
        {
            var st = EnsureDamageState(entered.volumeId);
            if (st == null) return false;
            int idx = entered.cellIdx;
            if ((st.data[idx] & VoxelDda.MaterialMask) != 0u || st.falling.ContainsKey(idx)) return false;
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            st.data[idx] = fv.vox;
            st.uploadDirty.Add(idx);
            st.falling[idx] = new ShardPhysics.FallCell
            {
                vel = new Vector3(fv.vel.x * c - fv.vel.z * s, fv.vel.y, fv.vel.x * s + fv.vel.z * c) / st.vs,
                acc = new Vector3(fv.acc.x * c - fv.acc.z * s, fv.acc.y, fv.acc.x * s + fv.acc.z * c) / st.vs,
                vox = fv.vox,
            };
            st.fallQueue.Add(idx);
            return true;
        }

        /// <summary>Pool admission — the cap bounds per-frame integrate+query cost;
        /// excess cells were vaporized either way (flight is presentation, not state).</summary>
        private void PushFlyer(FlyVoxel fv)
        {
            if (flying.Count >= maxFlyingCells + 4096) return;
            flying.Add(fv);
            flyEmitted++;
        }

        /// <summary>Cell index → world position (volume placement transform).</summary>
        private Vector3 CellWorld(DamageState st, int ci)
        {
            float vs = st.vs;
            Vector3 half = new Vector3(st.w, st.h, st.d) * (vs * 0.5f);
            Vector3 center = st.corner + half;
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            int z = ci / (st.w * st.h), rem = ci - z * st.w * st.h, y = rem / st.w, x = rem - y * st.w;
            Vector3 lp = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * vs - half;
            return center + new Vector3(lp.x * c + lp.z * s, lp.y, -lp.x * s + lp.z * c);
        }

        /// <summary>Shared fly-voxel constructor: material color folded with the voxel's own
        /// damage tint, cell-scale cube, hash-jittered life. The nibble lives in `vox` — the
        /// airborne cube renders identically to the grid cell it was and the rubble it becomes.</summary>
        private void AddFlyer(DamageState st, Vector3 wpos, uint vox, Vector3 vel, uint hsh, string noEnterVol = null)
        {
            var c = StAssetReader.GetMaterialColor((ushort)(vox & VoxelDda.MaterialMask));
            uint dmg = VoxelDamageMath.DamageOf(vox);
            if (dmg < 8u) { float k = 1f - (dmg / 7f) * charStrength; c = new Color(c.r * k, c.g * k, c.b * k, c.a); }
            else { float t = ((dmg - 7f) / 8f) * glintStrength; c = Color.Lerp(c, Color.white, t); }
            PushFlyer(new FlyVoxel
            {
                pos = wpos, vel = vel, acc = Vector3.zero, vox = vox, col = c,
                size = st.vs * 0.85f,
                tEnd = Time.time + shardLife * 2.2f * (0.7f + 0.6f * ((hsh >> 24) / 255f)),
                noEnterVol = noEnterVol,
                noEnterUntil = noEnterVol != null ? Time.time + FlyNoEnterSec : 0f,
            });
        }

        /// <summary>
        /// Every AIR-removed cell re-spawns as an individual flying voxel at its own world
        /// position — no buckets, no carried mass; the debris swarm IS the blast cavity's
        /// cells. Radial velocity + deterministic per-cell hash jitter. When the cavity
        /// exceeds the remaining pool budget, emission stride-samples by cell hash so
        /// coverage stays uniform across the whole cavity instead of truncating a list.
        /// vals (optional) filters to AIR writes so cracked cells never fly.
        /// </summary>
        private void EmitFlyersRadial(DamageState st, Vector3 blastCenter,
            List<int> idx, List<uint> mats, List<uint> vals, int start, int end, float cellRadius = 0f)
        {
            if (!shardBurstEnabled || world == null) return;
            Vector3 ccell = cellRadius > 0f ? WorldToCell(st, blastCenter) : Vector3.zero;
            int avail = Mathf.Max(0, maxFlyingCells - flying.Count);
            if (avail == 0) return;
            int count = 0;
            for (int i = start; i < end; i++) if (vals == null || vals[i] == 0u) count++;
            if (count == 0) return;
            int stride = Mathf.Max(1, Mathf.CeilToInt(count / (float)avail));
            emitCls.Clear();
            if (pairCells)
                for (int i = start; i < end; i++)
                    if (vals == null || vals[i] == 0u)
                        emitCls[idx[i]] = (int)VoxelDamageMath.ClassOf(mats[i]);
            for (int i = start; i < end; i++)
            {
                if (vals != null && vals[i] != 0u) continue;
                int ci = idx[i];
                // glued flyers — a paired cell hashes its launch AND stride decision
                // from the pair anchor, so both members emit together with identical
                // velocity (the flyer phase has no entities — velocity IS the glue)
                int partner = pairCells
                    ? ShardPhysics.PairPartner(ci, st.w, st.h, st.d, pairChance,
                        emitCls.ContainsKey, j => emitCls[j])
                    : -1;
                int src = partner >= 0 ? (partner < ci ? partner : ci) : ci;
                uint hsh = (uint)src * 2654435761u;
                if ((hsh & 0xFFFFu) % (uint)stride != 0u) continue;
                if (src != ci) lastBlastPairCells += 2;   // the member detects the pair — both emit or neither does
                Vector3 wpos = CellWorld(st, ci);                          // spawn at own cell
                Vector3 vref = src == ci ? wpos : CellWorld(st, src);      // velocity aims from the anchor
                Vector3 dir = vref - blastCenter;
                float dm = dir.magnitude;
                dir = dm > 1e-5f ? dir / dm : new Vector3(((hsh >> 4) & 255u) / 255f - 0.5f,
                    ((hsh >> 10) & 255u) / 255f - 0.5f, ((hsh >> 16) & 255u) / 255f - 0.5f).normalized;
                Vector3 vel = flyerRadialVelocity
                    ? dir * (shardSpeed * 2.5f * (0.7f + 0.6f * ((hsh & 255u) / 255f)))
                    : Vector3.zero;
                vel += new Vector3(((hsh >> 3) & 255u) / 255f - 0.5f, ((hsh >> 11) & 255u) / 255f - 0.5f,
                    ((hsh >> 19) & 255u) / 255f - 0.5f) * shardSpread;
                float scale = 1f;
                if (cellRadius > 0f && charRadial > 0f)   // same S-curve fade as the grid char — near-epicenter debris flies darkest
                {
                    int z = ci / (st.w * st.h), rem = ci - z * st.w * st.h, y = rem / st.w, x = rem - y * st.w;
                    float dx = x + 0.5f - ccell.x, dy = y + 0.5f - ccell.y, dz = z + 0.5f - ccell.z;
                    float tN = Mathf.Min(Mathf.Sqrt(dx * dx + dy * dy + dz * dz) / cellRadius, 1f);
                    scale = 1f - charRadial * tN * tN * (3f - 2f * tN);
                }
                AddFlyer(st, wpos, mats[i] | DebrisDamage(hsh, glint: false, scale), vel, hsh);
            }
        }

        /// <summary>Shot puncture: removed glass cells fly along the ray direction +
        /// a deterministic scatter cone — same idea as the old shard burst, per cell.</summary>
        private void EmitFlyersShot(DamageState st, ProjectileSystem.ShotReport r)
        {
            if (!shardBurstEnabled || world == null) return;
            doomSet.Clear();
            if (pairCells)
                foreach (int idx in dirtyScratch)
                    if (st.data[idx] == 0) doomSet.Add(idx);
            foreach (int idx in dirtyScratch)
            {
                if (st.data[idx] != 0) continue;   // cracked cells stay in place — only AIR flies
                // pairing: all removed cells are glass, so the class check is trivially true
                int partner = pairCells
                    ? ShardPhysics.PairPartner(idx, st.w, st.h, st.d, pairChance, doomSet.Contains, _ => 0)
                    : -1;
                int src = partner >= 0 ? (partner < idx ? partner : idx) : idx;
                if (src != idx) lastBlastPairCells += 2;
                uint hsh = (uint)src * 2654435761u;
                Vector3 vel = r.shotDir * shardSpeed * (0.7f + 0.6f * ((hsh & 255u) / 255f))
                    + new Vector3(((hsh >> 3) & 255u) / 255f - 0.5f, ((hsh >> 11) & 255u) / 255f - 0.5f + 0.3f,
                        ((hsh >> 19) & 255u) / 255f - 0.5f) * shardSpread;
                AddFlyer(st, CellWorld(st, idx), (uint)(r.material & 0x1FFu) | DebrisDamage(hsh, glint: true), vel, hsh);
            }
        }

        /// <summary>Hash-dithered damage nibble for ejected debris — a `debrisChar` fraction
        /// flies charred (levels 2-5) or glinted (10-13). Uses the pair-anchored hash so
        /// domino debris scorches uniformly instead of half-charring a shared brick.</summary>
        private uint DebrisDamage(uint hsh, bool glint, float scale = 1f)
        {
            if (!damageTint || debrisChar <= 0f) return 0u;
            if (((hsh >> 6) & 255u) / 255f >= debrisChar) return 0u;
            // radial scale → near-center debris chars 5-7, rim debris 1-3 (char);
            // glint stays mid-bright 9-13 so glass shards read as sparkle, not soot
            int lvl = Mathf.Clamp(Mathf.RoundToInt((glint ? 3f : 5f) * scale) + 1 + (int)((hsh >> 14) & 1u), 1, 7);
            return ((uint)(glint ? 8 + lvl : lvl)) << VoxelDamageMath.DamageShift;
        }

        /// <summary>A falling cell that stepped out of the volume bounds becomes a flying
        /// voxel — the transport phase of the same cell between grids (a cell flung out a
        /// window keeps flying; if it reaches a neighboring business it joins THAT grid's
        /// rain instead). A short re-entry cooldown on the source volume stops it
        /// re-landing through the doorway it just left.</summary>
        private void EscapeToFlyer(string volumeId, DamageState st, ShardPhysics.FallEscape esc)
        {
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector3 lw = esc.vel * st.vs;
            Vector3 wvel = new Vector3(lw.x * c + lw.z * s, lw.y, -lw.x * s + lw.z * c);
            AddFlyer(st, CellWorld(st, esc.idx), esc.vox, wvel, (uint)esc.idx * 2654435761u, volumeId);
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
                if (moves > 0) { st.uploadDirty.AddRange(dirtyScratch); budget -= moves; }
                if (budget <= 0) break;
            }
        }

        /// <summary>
        /// Islands don't get removed or packaged — every detached cell is marked `falling` where it
        /// stands and keeps rendering as itself (the voxel IS the particle; there is nothing to
        /// unpack on landing). The blast seeds a radial impulse that falls off to zero at
        /// `impulseReach` meters — rim cells are thrown, far islands just drop — plus per-cell
        /// jitter so sheets break up into a spray instead of sliding down laminar. Velocity is
        /// stored in CELL units (cells/sec per volume-local axis).
        /// </summary>
        private void SeedFalling(DamageState st, Vector3 blastCenter, float impulseReach,
            List<int> idx, int start, int end)
        {
            float vs = st.vs;
            Vector3 half = new Vector3(st.w, st.h, st.d) * (vs * 0.5f);
            Vector3 center = st.corner + half;
            float rad = st.yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            doomSet.Clear();
            if (pairCells) for (int i = start; i < end; i++) doomSet.Add(idx[i]);
            for (int i = start; i < end; i++)
            {
                int ci = idx[i];
                if (st.falling.ContainsKey(ci)) continue;
                // ── domino preselection: PairPartner picks ONE 2-cell entity per pair ──
                // The anchor (smaller flat index) registers a single shared FallCell under
                // BOTH indices; the member's own iteration skips. One vel/acc for the piece —
                // it steps, jams, lands, or fractures as a unit (see StepFalling's domino path).
                int partner = -1;
                if (pairCells)
                {
                    partner = ShardPhysics.PairPartner(ci, st.w, st.h, st.d, pairChance,
                        doomSet.Contains,
                        j => (int)VoxelDamageMath.ClassOf(st.data[j] & VoxelDamageMath.MaterialMask));
                    if (partner >= 0)
                    {
                        if (st.falling.ContainsKey(partner)) partner = -1;  // partner is already a live entity → single
                        else if (partner < ci) continue;                     // member — the anchor builds the pair
                    }
                }
                int z = ci / (st.w * st.h), rem = ci - z * st.w * st.h, y = rem / st.w, x = rem - y * st.w;
                Vector3 lp = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * vs - half;
                Vector3 wpos = center + new Vector3(lp.x * c + lp.z * s, lp.y, -lp.x * s + lp.z * c);
                Vector3 dir = wpos - blastCenter;
                float dist = dir.magnitude;
                dir = dist > 1e-5f ? dir / dist : Vector3.down;
                uint hsh = (uint)ci * 2654435761u;
                float falloff = Mathf.Clamp01(1f - dist / Mathf.Max(0.01f, impulseReach));
                Vector3 jit = new Vector3(((hsh >> 3) & 255u) / 255f - 0.5f,
                                          ((hsh >> 11) & 255u) / 255f - 0.5f,
                                          ((hsh >> 19) & 255u) / 255f - 0.5f) * cellImpulseJitter;
                Vector3 wv = dir * (cellImpulse * falloff * (0.6f + 0.6f * ((hsh & 255u) / 255f))) + jit;
                Vector3 lv = new Vector3(wv.x * c - wv.z * s, wv.y, wv.x * s + wv.z * c) / vs;   // world → cell axes
                if (partner >= 0)
                {
                    var fc = new ShardPhysics.FallCell
                    {
                        vel = lv, acc = Vector3.zero,
                        vox = st.data[ci], vox2 = st.data[partner],
                        partner = partner, headIdx = ci
                    };
                    st.falling[ci] = fc; st.falling[partner] = fc;   // one entity, two indices
                    st.fallQueue.Add(ci);                            // ONE slot — the head steps both cells
                    lastBlastPairs++; lastBlastPairCells += 2;
                }
                else
                {
                    st.falling[ci] = new ShardPhysics.FallCell { vel = lv, acc = Vector3.zero, vox = st.data[ci] };
                    st.fallQueue.Add(ci);
                }
            }
        }

        /// <summary>
        /// In-grid debris step: detached cells were never removed — they fly inside the volume's
        /// own grid carrying momentum. All moves batch into uploadDirty (the batched upload IS the
        /// debris animation). Cells that step out of the volume bounds convert to real shards.
        /// </summary>
        private void PumpFallingCells()
        {
            if (chunkManager == null) return;
            float dt = Time.deltaTime;
            int budget = FallMovesPerFrame;
            foreach (var kv in cow)
            {
                var st = kv.Value;
                if (st.falling.Count == 0) continue;
                dirtyScratch.Clear();
                escapedScratch.Clear();
                int rubBefore = st.rubble.Count;
                budget -= ShardPhysics.StepFalling(st.data, st.w, st.h, st.d,
                    st.falling, st.fallQueue, st.rubble, st.slumpQueue, dirtyScratch, escapedScratch,
                    dt, shardGravity / st.vs, cellFallDrag, cellLandSpeed, budget, rubbleDeposit);
                RubbleCells += st.rubble.Count - rubBefore;
                for (int i = 0; i < escapedScratch.Count; i++) EscapeToFlyer(kv.Key, st, escapedScratch[i]);   // StepFalling already removed it from falling + cleared its slot
                if (dirtyScratch.Count > 0) st.uploadDirty.AddRange(dirtyScratch);
                if (budget <= 0) break;
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
            LastRemoved = LastCracked = RubbleCells = 0;
            flying.Clear(); flyingDraw.Clear();
            System.Array.Clear(lastBlastRemoved, 0, lastBlastRemoved.Length);
            lastBlastDetached = 0;
            lastBlastPairs = lastBlastPairCells = 0;
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
