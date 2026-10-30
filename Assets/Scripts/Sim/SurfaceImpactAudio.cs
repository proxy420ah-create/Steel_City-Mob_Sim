using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Surface-impact audio — second subscriber in the ShotResolved family, alongside
    /// VoxelDamageSystem's glass path. Glass owns itself (synthesized buckets, severity
    /// known from cells removed); every other solid hit routes through file pools here:
    ///
    ///   grazing angle (incidence vs surface normal ≥ ricMinIncidenceDeg)
    ///   AND close to the muzzle (r.distance ≤ ricMaxDistanceM — still carrying energy)
    ///        → sounds/ricochet/   (bullet skips: whine/zip)
    ///   otherwise
    ///        → sounds/impact/    (bullet buries: thud/smack — pool grows as authored)
    ///
    /// Material routing: a file tagged "&lt;pool&gt;_&lt;tag&gt;_NN.stsound" (e.g.
    /// ricochet_stone_04) is preferred when the struck material maps to that tag;
    /// untagged files are the generic fallback. sounds/&lt;pool&gt;/LiveTesting/*.stsound
    /// is the exclusive pool while populated — GetClip also shadows per-file, so the
    /// override holds either way. Pool pick is seeded by the shot seed → replay-stable.
    /// Emits an SfxWaves wave per shot for the visual/debug layer (AI reacts to
    /// "gunshot" only — "ricochet"/"impact" waves are display-only by design).
    /// </summary>
    public class SurfaceImpactAudio : MonoBehaviour
    {
        public static SurfaceImpactAudio Instance { get; private set; }

        public static SurfaceImpactAudio GetOrCreate()
        {
            if (Instance != null) return Instance;
            var found = FindFirstObjectByType<SurfaceImpactAudio>();
            if (found != null) { Instance = found; return found; }
            var go = new GameObject("SurfaceImpactAudio");
            Instance = go.AddComponent<SurfaceImpactAudio>();
            return Instance;
        }

        [Header("Gate — physics decides WHICH pool (sound always plays if files exist)")]
        public bool impactAudioEnabled = true;
        [Tooltip("Ricochet when shotDir skims the surface: incidence vs normal ≥ this (deg). 90 = parallel graze.")]
        [Range(20f, 89f)] public float ricMinIncidenceDeg = 85f;
        [Tooltip("Ricochet only within this muzzle distance (m) — close impact = bullet still has energy to skip.")]
        [Range(1f, 300f)] public float ricMaxDistanceM = 40f;

        [Header("Mix")]
        [Range(0.1f, 1f)] public float impactAudioVolume = 0.9f;
        [Tooltip("Master sample-level gain × applied at synth (vel → recipe gain) — REAL loudness: AudioSource.volume caps at 1, gain scales the waveform itself (clips >1). Per-file gain multiplies on top (sidecar tuning).")]
        [Range(0.25f, 8f)] public float impactGain = 1f;
        [Range(0.5f, 20f)] public float impactRefDistance = 1.5f;
        [Range(5f, 200f)] public float impactAudibleRange = 25f;

        [Header("Modulation — Variant-Lab children generated per hit (seeded)")]
        [Tooltip("Author ONE .stsound; each hit mutates a child around it (layer/partial/sweep structure).")]
        public bool modEnabled = true;
        [Tooltip("Jitter depth — .12 subtle | .30 medium | .55 wild (same bands as the studio Variant Lab).")]
        [Range(0f, 0.6f)] public float modJitter = 0.30f;
        [Tooltip("Lock duration/master/env/eq/echo — mutate structure only (layers/partials/seed).")]
        public bool modLockMaster = true;
        [Tooltip("Bounded child slots per file — the SFX panel auditions/culls these; runtime picks among kept slots.")]
        [Range(1, 16)] public int modVariants = 8;

        /// <summary>Last routing decision for the SFX panel + logs.</summary>
        public string LastInfo { get; private set; } = "—";

        private ProjectileSystem subscribed;

        /// <summary>Palette id → pool file tag. Only "stone" has authored files today;
        /// other tags route to the generic pool until their files land.</summary>
        static string MatTag(int mat) => mat switch
        {
            100 or 101 or 102 or 103 or 104 or 105 => "stone",   // brick/stone/concrete/stucco/asphalt/cobblestone
            106 or 107 or 108 => "wood",
            109 or 110 or 111 or 123 or 140 or 141 or 142 or 144 or 145 => "metal",
            125 => "flesh",
            _ => null,
        };

        /// <summary>Tags a filename can carry — extended as new material pools appear.</summary>
        static readonly string[] KnownTags = { "stone", "metal", "wood", "flesh" };

        void Awake()
        {
            Instance = this;
            subscribed = ProjectileSystem.Instance;
            if (subscribed != null) subscribed.ShotResolved += OnShotResolved;
        }

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

        void OnShotResolved(ProjectileSystem.ShotReport r)
        {
            if (!impactAudioEnabled || !r.hit) return;
            if (ProjectileSystem.IsGlassMaterial(r.material)) return;   // glass owns its sfx

            float incidence = Vector3.Angle(-r.shotDir, r.normal);      // 0° dead-on → 90° skim
            bool grazing = incidence >= ricMinIncidenceDeg;
            bool close = r.distance <= ricMaxDistanceM;
            string pool = (grazing && close) ? "ricochet" : "impact";

            int pickSeed = unchecked((int)r.seed ^ 0x51AB);
            string file = PickFile(pool, r.material, pickSeed);
            if (file == null)
            {
                LastInfo = $"#{r.id} {pool} pool empty — silent";
                return;
            }

            string rel = $"sounds/{PoolDir}/{file}";
            AudioClip clip;
            int slot = -1;
            if (modEnabled)
            {
                // bounded child slots — shot-seeded pick among the KEPT slots only
                // (the SFX panel culls bad children to <file>.cull.json sidecars)
                var culled = CulledSlots(rel);
                var rng = new System.Random(unchecked(pickSeed * 31));
                for (int t = 0; t < 64 && culled.Count < modVariants; t++)
                {
                    slot = rng.Next(modVariants);
                    if (!culled.Contains(slot)) break;
                    slot = -1;
                }
                if (culled.Count >= modVariants) slot = -1;   // every child culled → authored clip
            }
            // The rendered clip is cached per (file, slot, vel, recipe-write-time) —
            // the old path synthesized UNCACHED every impact: disk read + JSON clone
            // + full PCM render + ~1.5 MB allocs on the main thread → stalls → mixer
            // underrun crackle right after each bang. Bounded set, safe to keep.
            clip = VariantClip(rel, slot, impactGain * FileMix(rel).gain);
            if (clip == null) { LastInfo = $"#{r.id} clip missing: {pool}/{file}"; return; }

            var mix = FileMix(rel);   // per-file sidecar tuning — this SFX's own gain/volume
            ProceduralAudio.PlayAt(clip, r.point, impactAudioVolume * mix.volume, impactRefDistance, impactAudibleRange);
            SfxWaves.GetOrCreate().Emit(r.point, impactAudibleRange, impactRefDistance, pool);
            LastInfo = $"#{r.id} {StAssetReader.GetMaterialName(r.material)} inc {incidence:F0}° dist {r.distance:F1}m → {pool}/{file}{(modEnabled ? $" v{(slot >= 0 ? (slot + 1).ToString("D2") : "base")}" : "")}";
            Debug.Log($"[ImpactAudio] 🔊 {LastInfo}");
        }

        /// <summary>Variant-clip cache — children are a bounded set (pool files ×
        /// modVariants), so render each once and keep it. Key carries the recipe
        /// file's write-time: a studio save mints a new key → hot-reload for free,
        /// while the stale clip stays referenced (never Destroyed mid-play — the
        /// crackle guard). slot &lt; 0 = the authored clip itself.</summary>
        static readonly Dictionary<string, AudioClip> variantCache = new();

        AudioClip VariantClip(string rel, int slot, float vel)
        {
            string path = Path.Combine(Application.streamingAssetsPath,
                ProceduralAudio.ResolveLiveTesting(rel));
            long stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L;
            string key = $"{rel}|{slot}|{vel:F3}|{stamp}";
            if (variantCache.TryGetValue(key, out var hit)) return hit;
            var baseR = ProceduralAudio.LoadRecipe(rel);
            var child = slot >= 0
                ? ProceduralAudio.MutateRecipe(baseR,
                    VariantSeed(Path.GetFileName(rel), slot), modJitter, modLockMaster)
                : baseR;
            var clip = child != null
                ? ProceduralAudio.SynthFromRecipe(child, vel: vel, bypassCache: true)
                : null;
            if (clip != null) variantCache[key] = clip;
            return clip;
        }

        // ── pool pick ─────────────────────────────────────────────────────

        class PoolScan { public float at; public List<string> files; }
        static readonly Dictionary<string, PoolScan> scanCache = new();

        const string PoolDir = "Impacts";   // sounds/Impacts/ — both pools, split by filename

        /// <summary>Files eligible for a pool inside sounds/Impacts/: LiveTesting/
        /// exclusively when populated, else the folder root (GetClip shadows per-file
        /// regardless). "ricochet" pool = names containing "ricochet"; "impact" = the rest.
        /// Rescan ~2 Hz.</summary>
        static List<string> PoolFiles(string pool)
        {
            string abs = Path.Combine(Application.streamingAssetsPath, "sounds", PoolDir);
            if (!scanCache.TryGetValue(abs, out var sc)) scanCache[abs] = sc = new PoolScan();
            if (sc.files != null && Time.unscaledTime < sc.at) return sc.files;
            sc.at = Time.unscaledTime + 0.5f;

            string live = Path.Combine(abs, "LiveTesting");
            var files = Directory.Exists(live)
                ? Directory.GetFiles(live, "*.stsound", SearchOption.TopDirectoryOnly).ToList()
                : null;
            if (files == null || files.Count == 0)
                files = Directory.Exists(abs)
                    ? Directory.GetFiles(abs, "*.stsound", SearchOption.TopDirectoryOnly).ToList()
                    : new List<string>();
            bool ric = pool == "ricochet";
            return sc.files = files.Select(Path.GetFileName).Where(n => n != null)
                .Where(n => n.IndexOf("ricochet", System.StringComparison.OrdinalIgnoreCase) >= 0 == ric)
                .ToList();
        }

        /// <summary>Deterministic mutation seed for (file, slot) — stable across sessions
        /// (FNV-1a + golden-ratio slot mix, not string.GetHashCode which can vary).</summary>
        public static int VariantSeed(string fileName, int slot)
        {
            unchecked
            {
                int h = unchecked((int)2166136261);
                foreach (char c in fileName) { h ^= c; h *= 16777619; }
                return h ^ unchecked(slot * (int)0x9E3779B1);
            }
        }

        // ── per-file tuning — sidecar JSON next to the resolved recipe ──────
        // <file>.cull.json: culled variant slots + this SFX's own gain/volume trims
        // (each SFX gets independent loudness; sidecar travels with the recipe).

        [Serializable] public class FileMixData
        {
            public int[] disabled = new int[0];
            public float gain = 1f;
            public float volume = 1f;
            [NonSerialized] public HashSet<int> disabledSet;
        }
        static readonly Dictionary<string, FileMixData> mixCache = new();

        static string SidecarPath(string rel) => Path.Combine(Application.streamingAssetsPath,
            ProceduralAudio.ResolveLiveTesting(rel)) + ".cull.json";

        /// <summary>Per-file tuning (loaded once, sidecar beside the RESOLVED recipe —
        /// LiveTesting files carry their own). gain/volume default 1.</summary>
        public static FileMixData FileMix(string rel)
        {
            if (mixCache.TryGetValue(rel, out var d) && d != null) return d;
            d = new FileMixData { disabledSet = new HashSet<int>() };
            string path = SidecarPath(rel);
            if (File.Exists(path))
            {
                try
                {
                    var j = JsonUtility.FromJson<FileMixData>(File.ReadAllText(path));
                    if (j != null)
                    {
                        d.gain = j.gain > 0f ? j.gain : 1f;
                        d.volume = j.volume > 0f ? j.volume : 1f;
                        if (j.disabled != null) d.disabledSet.UnionWith(j.disabled);
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[ImpactAudio] tune parse failed {path}: {e.Message}"); }
            }
            return mixCache[rel] = d;
        }

        static void SaveMix(string rel, FileMixData d)
        {
            d.disabled = d.disabledSet.OrderBy(x => x).ToArray();
            File.WriteAllText(SidecarPath(rel), JsonUtility.ToJson(d, true));
        }

        /// <summary>Culled slots for a file — never null.</summary>
        public static HashSet<int> CulledSlots(string rel) => FileMix(rel).disabledSet;

        /// <summary>Cut/keep a child slot — writes the sidecar immediately so the cull
        /// survives the session and ships next to the recipe file.</summary>
        public static void SetSlotCulled(string rel, int slot, bool culled)
        {
            var d = FileMix(rel);
            if (culled) d.disabledSet.Add(slot); else d.disabledSet.Remove(slot);
            SaveMix(rel, d);
        }

        /// <summary>Set this SFX's own gain/volume trims — persists to its sidecar.</summary>
        public static void SetFileMix(string rel, float gain, float volume)
        {
            var d = FileMix(rel);
            d.gain = gain; d.volume = volume;
            SaveMix(rel, d);
        }

        /// <summary>Seeded pick: prefer "&lt;pool&gt;_&lt;tag&gt;_*" when the material has a
        /// tag, else untagged files, else anything. Returns the file name or null.</summary>
        static string PickFile(string pool, int material, int seed)
        {
            var files = PoolFiles(pool);
            if (files.Count == 0) return null;
            string tag = MatTag(material);
            var rng = new System.Random(seed);

            if (tag != null)
            {
                var tagged = files.Where(f => f.Contains($"_{tag}_")).ToList();
                if (tagged.Count > 0) return tagged[rng.Next(tagged.Count)];
            }
            var generic = files.Where(f => !KnownTags.Any(t => f.Contains($"_{t}_"))).ToList();
            if (generic.Count > 0) return generic[rng.Next(generic.Count)];
            return files[rng.Next(files.Count)];
        }
    }
}
