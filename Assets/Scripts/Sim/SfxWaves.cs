using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// SfxWaves — expanding "sound report" shells emitted at 3D sfx origins.
    ///
    /// MODEL: a wave is a sphere growing from the source at <see cref="waveSpeed"/> m/s
    /// (a VISUAL rate — real sound at 343 m/s crosses the map in a blink). The shell's
    /// rim color encodes live audibility at that radius: TEAL at loud (inside refDist),
    /// grading to VIOLET as the linear rolloff approaches the audible edge. A static
    /// teal ring marks refDist (the full-volume zone). The wave dies at `range` —
    /// that outer edge is the *hearing radius*: characters the shell passes over are
    /// inside the report (hook for NPC reactions later; amplitude law is AmplitudeAt).
    ///
    /// Rendering rides PathDebugRenderer.SetCustomBeam — ring segments keyed per wave.
    /// </summary>
    public class SfxWaves : MonoBehaviour
    {
        public static SfxWaves Instance { get; private set; }
        public static SfxWaves GetOrCreate()
            => Instance != null ? Instance : new GameObject("SfxWaves").AddComponent<SfxWaves>();

        [Header("Wave visuals")]
        [Tooltip("Draw expanding report shells for 3D sfx")]
        public bool showWaves = false;   // beam-flood emitter — off by default (perf baseline); SFX tab opts in
        [Tooltip("Visual expansion speed (m/s) — slowed for inspection; real sound is ~343 m/s")]
        [Range(5f, 343f)] public float waveSpeed = 45f;
        [Tooltip("Segments per ring (3 rings per wave)")]
        [Range(8, 64)] public int ringSegments = 28;
        [Tooltip("Beam thickness")]
        [Range(0.005f, 0.2f)] public float beamWidth = 0.03f;

        static readonly Color LoudTeal = new Color(0.10f, 1.00f, 0.75f, 1f);
        static readonly Color FaintViolet = new Color(0.55f, 0.20f, 1.00f, 1f);

        class Wave { public int id; public Vector3 origin; public float range, refDist, age; public string tag; }
        readonly List<Wave> waves = new();
        int nextId = 0;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDisable() { ClearAll(); }

        /// <summary>Audibility (linear rolloff gain) at a distance — 1.0 inside refDist,
        /// falling linearly to 0 at range. Same law the AudioSource applies.</summary>
        public static float AmplitudeAt(float dist, float refDist, float range)
        {
            if (range <= refDist) return dist <= refDist ? 1f : 0f;
            return Mathf.Clamp01(1f - (dist - refDist) / (range - refDist));
        }

        /// <summary>Emit a report shell. tag drives nothing visual — it's the semantic
        /// source id for the future NPC-hearing hook (e.g. "gunshot", "glass").</summary>
        public void Emit(Vector3 origin, float range, float refDist, string tag)
        {
            if (!showWaves || range <= 0f) return;
            waves.Add(new Wave { id = nextId++, origin = origin, range = range,
                                 refDist = Mathf.Min(refDist, range), age = 0f, tag = tag });
        }

        void Update()
        {
            var pdr = PathDebugRenderer.Instance;
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                w.age += Time.deltaTime;
                float r = w.age * waveSpeed;
                if (r >= w.range || pdr == null)
                {
                    ClearWave(w);
                    waves.RemoveAt(i);
                    continue;
                }
                DrawWave(pdr, w, r);
            }
        }

        void DrawWave(PathDebugRenderer pdr, Wave w, float r)
        {
            float amp = AmplitudeAt(r, w.refDist, w.range);
            var rim = Color.Lerp(LoudTeal, FaintViolet, Mathf.Clamp01(r / w.range));
            rim.a = 0.25f + 0.75f * amp;                        // fade as audibility dies
            string pre = $"sw{w.id}_";

            // 3 orthogonal rings (XZ, XY, ZY) — wireframe shell
            for (int plane = 0; plane < 3; plane++)
            {
                Vector3 prev = RingPoint(w.origin, r, plane, 0), first = prev;
                for (int i = 1; i <= ringSegments; i++)
                {
                    Vector3 cur = RingPoint(w.origin, r, plane, i);
                    pdr.SetCustomBeam($"{pre}p{plane}_{i}", prev, cur, beamWidth, rim);
                    prev = cur;
                }
            }

            // static full-volume ring at refDist (XZ plane — the "still loud" boundary)
            float rd = w.refDist;
            if (rd > 0.01f)
            {
                var refCol = LoudTeal; refCol.a = 0.5f;
                Vector3 prev = RingPoint(w.origin, rd, 0, 0);
                for (int i = 1; i <= ringSegments; i++)
                {
                    Vector3 cur = RingPoint(w.origin, rd, 0, i);
                    pdr.SetCustomBeam($"{pre}ref_{i}", prev, cur, beamWidth * 0.7f, refCol);
                    prev = cur;
                }
            }
        }

        Vector3 RingPoint(Vector3 c, float r, int plane, int i)
        {
            float a = i * Mathf.PI * 2f / ringSegments;
            float x = Mathf.Cos(a) * r, y = Mathf.Sin(a) * r;
            return plane switch
            {
                0 => c + new Vector3(x, 0f, y),     // ground ring (the one that reads on the map)
                1 => c + new Vector3(x, y, 0f),
                _ => c + new Vector3(0f, x, y),
            };
        }

        void ClearWave(Wave w)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;
            string pre = $"sw{w.id}_";
            for (int plane = 0; plane < 3; plane++)
                for (int i = 1; i <= ringSegments; i++)
                    pdr.ClearCustomBeam($"{pre}p{plane}_{i}");
            for (int i = 1; i <= ringSegments; i++)
                pdr.ClearCustomBeam($"{pre}ref_{i}");
        }

        void ClearAll()
        {
            for (int i = waves.Count - 1; i >= 0; i--) ClearWave(waves[i]);
            waves.Clear();
        }
    }
}
