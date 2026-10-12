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
        private struct Shard { public Vector3 pos, vel; public float tEnd; public int beamId; }
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
            EmitShards(st, r);
            if (glassAudioEnabled && removed > 0)
                AudioSource.PlayClipAtPoint(GlassClip(removed), r.point,
                    glassAudioVolume * Mathf.Clamp01(0.4f + removed / 20f));
            Debug.Log($"[VoxelDamage] 🔨 {r.target} cell {r.cell} mat {r.material}: -{removed} glass cells, {cracked} cracked — hole persists");
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
                if (shards.Count >= maxShards) shards.RemoveAt(0);
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
                };
                shards.Add(sh);
            }
            shardColor = col;
        }

        private Color shardColor = new Color(0.72f, 0.86f, 0.96f, 0.9f);

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
                var c = shardColor; c.a = Mathf.Lerp(0.15f, 0.9f, fade);
                pdr.SetCustomBeam($"gshard_{s.beamId}", s.pos - s.vel * 0.035f, s.pos, 0.018f, c);
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
