using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Runtime synthesizer for .stsound recipes authored in VoxelAssetStudio/audio_studio.html.
    /// Faithful port of the editor's render() — layer synth (attack/partials/noiseTail/sweep),
    /// exp-or-ADSR envelope, pitch/note scaling with decay coupling, RBJ biquad EQ,
    /// tanh drive, mulberry32 seeded noise (bit-exact with the editor's RNG).
    /// See docs/systems/PROCEDURAL_AUDIO.md — schema v4 (v4 adds sweep curve/vibrato;
    /// absent fields default to "lin"/off — v3 recipes render identically).
    /// </summary>
    public static class ProceduralAudio
    {
        [Serializable] public class SynthPartial { public float hz; public float amp; }

        [Serializable] public class RecipeLayer
        {
            public string type;                       // attack | partials | noiseTail | sweep
            public float amp = 1f;
            public float decay = 20f;
            public float lowpass = 20000f;            // noiseTail only
            public float tone, toneQ;                 // attack only — resonant bandpass on the click (tone Hz, 0 = white; toneQ = resonance width)
            public float cutoff;                      // attack only — 1-pole lowpass on the click, applied after tone (Hz, 0 = off)
            public string src;                        // attack only — exciter: null/"noise" = white burst | "impulse" = 1-sample spike | "chirp" = pitch-drop sine (startHz→endHz)
            public float crack;                       // attack only — stochastic envelope breakup (0..1) in the first ~35ms: fracture texture vs smooth drum-machine decay
            public float startHz, endHz;              // sweep glide; also attack-chirp drop range (src=="chirp")
            public string curve;                      // sweep only: null/"lin" = Hz ramp · "exp" = equal semitones/sec
            public float vibRate, vibDepth;           // sweep only: vibrato LFO — rate Hz, depth cents (0 = off)
            public int harmN;                         // sweep only: extra harmonics riding the glide (0 = pure sine)
            public float harmAmp = 0.5f;              // sweep only: rolloff per harmonic (h_n = harmAmp^(n-1))
            public float amRate, amDepth;             // sweep only: loudness LFO — rate Hz, depth 0-1 (0 = off)
            public SynthPartial[] partials;           // partials only
        }

        [Serializable] public class Envelope
        {
            public string mode = "exp";               // exp | adsr
            public float attack = 0.005f, decay = 0.1f, sustain = 0.4f, release = 0.2f;
        }

        [Serializable] public class EqBand
        {
            public string type = "peaking";           // lowshelf | peaking | highshelf
            public float hz = 1000f, gainDb, q = 1f;
            public bool on;
        }

        [Serializable] public class EchoFx
        {
            public bool on;
            public float time = 0.09f;      // delay between repeats (s)
            public float feedback = 0.35f;  // repeat decay per pass
            public float mix = 0.25f;       // wet blend
        }

        [Serializable] public class SoundRecipe
        {
            public string name = "sound";
            public int version = 3;
            public int sampleRate = 44100;
            public float duration = 0.3f;
            public int seed;
            public Envelope envelope = new Envelope();
            public float gain = 0.8f;
            public float drive = 1f;
            public float phaseStep = 1.7f;
            public int rootNote = 60;
            public float pitch;
            public bool coupleDecay = true;
            public float tailFade = 0.04f;   // raised-cosine click guard on buffer end (s)
            public float masterAttack;       // global fade-in (s, 0 = instant strike)
            public float masterRelease;      // global fade-out over the buffer's last N seconds (0 = off)
            public EqBand[] eq;
            public EchoFx echo;
            public RecipeLayer[] layers;
        }

        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> cache = new();

        /// <summary>Hot-reload: re-synthesize clips when their .stsound file changes on disk.
        /// On in editor/dev builds — release players keep a deterministic bake-once clip.</summary>
        public static bool liveReload = Application.isEditor || Debug.isDebugBuild;

        class FileEntry { public AudioClip clip; public DateTime stamp; }
        static readonly Dictionary<string, FileEntry> fileClips = new();

        /// <summary>The clip for a .stsound under StreamingAssets (e.g. "sounds/gunshot.stsound").
        /// Stat-checks the file's write time each call — free when unchanged, re-synthesizes
        /// (once) when the studio saves a new version. This is the live-iteration path:
        /// save in audio_studio → next trigger pull plays the new recipe, same Play session.</summary>
        public static AudioClip GetClip(string streamingAssetsRelPath)
        {
            string path = Path.Combine(Application.streamingAssetsPath, streamingAssetsRelPath);
            DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (fileClips.TryGetValue(streamingAssetsRelPath, out var e)
                && (!liveReload || e.stamp == stamp))
                return e.clip;

            var r = LoadRecipe(streamingAssetsRelPath);
            var clip = r != null ? SynthFromRecipe(r, bypassCache: true) : null;
            if (e?.clip != null) UnityEngine.Object.Destroy(e.clip);
            fileClips[streamingAssetsRelPath] = new FileEntry { clip = clip, stamp = stamp };
            if (e != null && clip != null)
                Debug.Log($"[ProceduralAudio] 🔊 hot-reloaded {streamingAssetsRelPath}");
            return clip;
        }

        /// <summary>Positional one-shot with explicit reach — the controllable version of
        /// PlayClipAtPoint: full volume inside refDist, LINEAR fade to silence at maxDist
        /// (maxDist = the sound's audible radius — also the hearing edge for AI).
        /// Spawns a temp AudioSource destroyed after the clip ends.</summary>
        public static AudioSource PlayAt(AudioClip clip, Vector3 pos, float vol, float refDist, float maxDist)
        {
            if (clip == null) return null;
            var go = new GameObject("sfx_" + clip.name);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = vol;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = Mathf.Max(0.01f, refDist);
            src.maxDistance = Mathf.Max(src.minDistance + 0.01f, maxDist);
            src.Play();
            UnityEngine.Object.Destroy(go, clip.length + 0.05f);
            return src;
        }

        /// <summary>Load a .stsound JSON from StreamingAssets (e.g. "sounds/gunshot.stsound").
        /// Returns null with a warning on any failure.</summary>
        public static SoundRecipe LoadRecipe(string streamingAssetsRelPath)
        {
            string path = Path.Combine(Application.streamingAssetsPath, streamingAssetsRelPath);
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[ProceduralAudio] 🔊 recipe not found: {path}");
                    return null;
                }
                var r = JsonUtility.FromJson<SoundRecipe>(File.ReadAllText(path));
                if (r == null || r.layers == null)
                {
                    Debug.LogWarning($"[ProceduralAudio] 🔊 recipe parse failed: {path}");
                    return null;
                }
                return r;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProceduralAudio] 🔊 recipe load failed {path}: {e.Message}");
                return null;
            }
        }

        /// <summary>Synthesize a recipe into an AudioClip. note: MIDI note to render at
        /// (defaults to recipe.rootNote); vel scales master gain (0..1). Cached by key.</summary>
        public static AudioClip SynthFromRecipe(SoundRecipe r, float? note = null, float vel = 1f, bool bypassCache = false)
        {
            if (r == null || r.layers == null) return null;
            float n_ = note ?? r.rootNote;
            string key = $"{r.name}|{n_:F2}|{vel:F3}";
            if (!bypassCache && cache.TryGetValue(key, out var hit)) return hit;

            int n = Mathf.Max(1, Mathf.RoundToInt(Rate * r.duration));
            var smp = new float[n];
            float semi = (n_ - r.rootNote) + r.pitch;
            float fs = Mathf.Pow(2f, semi / 12f);                        // Hz-domain scale
            float ds = r.coupleDecay ? Mathf.Max(fs, 0.05f) : 1f;        // decay scale
            var rng = new Mulberry32((uint)r.seed);
            var phases = new float[r.layers.Length];                     // sweep phase integrators
            var lpState = new float[r.layers.Length];                    // noiseTail 1-pole state
            var atkCoef = new float[r.layers.Length, 5];                 // attack bandpass b0,b1,b2,a1,a2
            var atkSt = new float[r.layers.Length, 4];                   // attack bandpass x1,x2,y1,y2
            var cutSt = new float[r.layers.Length];                      // attack cutoff 1-pole state
            var crackN = new int[r.layers.Length];                       // attack crackle block counter
            var crackG = new float[r.layers.Length];                     // attack crackle held gain
            for (int li = 0; li < r.layers.Length; li++)
            {
                var al0 = r.layers[li];
                if (al0.type == "attack" && al0.tone > 0f)               // RBJ bandpass, constant 0dB peak
                {
                    float w = 2f * Mathf.PI * Mathf.Min(al0.tone * fs, Rate * 0.45f) / Rate;
                    float al = Mathf.Sin(w) / (2f * Mathf.Max(0.05f, al0.toneQ <= 0f ? 1f : al0.toneQ));
                    float a0 = 1f + al;
                    atkCoef[li, 0] = al / a0; atkCoef[li, 1] = 0f; atkCoef[li, 2] = -al / a0;
                    atkCoef[li, 3] = -2f * Mathf.Cos(w) / a0; atkCoef[li, 4] = (1f - al) / a0;
                }
            }

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float v = 0f;
                for (int li = 0; li < r.layers.Length; li++)
                {
                    var L = r.layers[li];
                    float env = EnvOf(r, L, t, ds);
                    switch (L.type)
                    {
                        case "attack":
                            {
                                float x;
                                if (L.src == "impulse") x = i == 0 ? 1f : 0f;          // 1-sample spike — clean resonator ping
                                else if (L.src == "chirp")                             // pitch-drop sine — thump/punch exciter
                                {
                                    float cs = L.startHz > 0f ? L.startHz : 900f;
                                    float ce = Mathf.Max(20f, L.endHz > 0f ? L.endHz : 90f);
                                    float f = cs * Mathf.Exp(-Mathf.Log(cs / ce) * L.decay * ds * t / 6.9078f) * fs;
                                    phases[li] += 2f * Mathf.PI * f / Rate;
                                    x = Mathf.Sin(phases[li]);
                                }
                                else x = rng.Next() * 2f - 1f;                         // noise (default)
                                if (L.tone > 0f)
                                {
                                    float y = atkCoef[li,0]*x + atkCoef[li,1]*atkSt[li,0] + atkCoef[li,2]*atkSt[li,1]
                                            - atkCoef[li,3]*atkSt[li,2] - atkCoef[li,4]*atkSt[li,3];
                                    atkSt[li,1] = atkSt[li,0]; atkSt[li,0] = x;
                                    atkSt[li,3] = atkSt[li,2]; atkSt[li,2] = y;
                                    x = y;
                                }
                                if (L.cutoff > 0f)                      // 1-pole LP — click brightness, after tone
                                {
                                    float fc = Mathf.Min(L.cutoff * fs, Rate * 0.49f);
                                    float a = fc >= Rate * 0.49f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * fc / Rate);
                                    cutSt[li] = a * x + (1f - a) * cutSt[li];
                                    x = cutSt[li];
                                }
                                if (L.crack > 0f && t < Mathf.Min(0.035f, 3.5f / Mathf.Max(1f, L.decay * ds)))
                                {                                       // fracture window — block-rate random
                                    if (--crackN[li] <= 0)              // gain every ~1.1ms: gritty crackle,
                                    {                                   // not white hash — the tom killer
                                        crackN[li] = 48;
                                        crackG[li] = 1f + L.crack * (rng.Next() * 2f - 1f) * 1.2f;
                                    }
                                    x *= Mathf.Max(0.12f, crackG[li]);
                                }
                                v += x * L.amp * Mathf.Exp(-L.decay * ds * t);
                            }
                            break;
                        case "partials":
                            if (L.partials != null)
                                for (int p = 0; p < L.partials.Length; p++)
                                    v += Mathf.Sin(2f * Mathf.PI * L.partials[p].hz * fs * t + p * r.phaseStep)
                                         * L.partials[p].amp * L.amp * env;
                            break;
                        case "noiseTail":
                            {
                                float x = rng.Next() * 2f - 1f;
                                float fc = Mathf.Min(L.lowpass * fs, Rate * 0.49f);
                                float a = fc >= Rate * 0.49f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * fc / Rate);
                                lpState[li] = a * x + (1f - a) * lpState[li];
                                v += lpState[li] * L.amp * env;
                            }
                            break;
                        case "sweep":
                            {
                                float k = t / r.duration;
                                float s = Mathf.Max(1f, L.startHz), e = Mathf.Max(0.01f * s, L.endHz);
                                // curve: "exp" = equal semitones/sec — a true pitch
                                // dive (or rise when end>start); else the linear Hz ramp.
                                float f = L.curve == "exp" ? s * Mathf.Pow(e / s, k)
                                                         : s + (e - s) * k;
                                f *= fs;
                                // Vibrato — the tumbling-bullet warble: pitch wobble in
                                // cents around the glide (0/off = the clean sweep).
                                if (L.vibDepth > 0f && L.vibRate > 0f)
                                    f *= Mathf.Pow(2f, (L.vibDepth / 1200f) * Mathf.Sin(2f * Mathf.PI * L.vibRate * t));
                                phases[li] += 2f * Mathf.PI * f / Rate;
                                // harmonic glide — overtones at 2f,3f,4f riding the same
                                // track: a lone sine = test tone, harmonics = resonant howl
                                float x2 = Mathf.Sin(phases[li]);
                                int hn = Mathf.Min(4, L.harmN);
                                for (int h = 2; h <= 1 + hn; h++)
                                    x2 += Mathf.Pow(L.harmAmp, h - 1) * Mathf.Sin(phases[li] * h);
                                if (L.amDepth > 0f && L.amRate > 0f)      // AM — loudness pulse
                                    x2 *= 1f + L.amDepth * Mathf.Sin(2f * Mathf.PI * L.amRate * t);
                                v += x2 * L.amp * env;
                            }
                            break;
                    }
                }
                smp[i] = v;
            }

            if (r.eq != null)
                foreach (var b in r.eq)
                    if (b.on && b.gainDb != 0f)
                        ApplyBiquad(smp, BiquadCoeffs(b.type, b.hz, b.gainDb, b.q));

            if (r.echo != null && r.echo.on)
            {
                int d = Mathf.Max(1, Mathf.FloorToInt(Rate * r.echo.time));
                float fb = r.echo.feedback, mix = r.echo.mix;
                var dry = (float[])smp.Clone();
                var wet = new float[n];
                for (int i = d; i < n; i++)
                    wet[i] = dry[i] + wet[i - d] * fb;
                for (int i = 0; i < n; i++)
                    smp[i] = dry[i] * (1f - mix) + wet[i] * mix;
            }

            float g = r.gain * vel;
            for (int i = 0; i < n; i++)
                smp[i] = Mathf.Clamp((float)Math.Tanh(smp[i] * r.drive), -1f, 1f) * g;

            // Master trims — global fades on the whole mix (0 = off). tailFade
            // still runs after as the click guard; the fades compose.
            int man = Mathf.Min(n, Mathf.FloorToInt(Rate * Mathf.Max(0f, r.masterAttack)));
            for (int i = 0; i < man; i++)
                smp[i] *= 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / man);
            int rn = Mathf.FloorToInt(Rate * Mathf.Max(0f, r.masterRelease));
            if (rn > 0)
                for (int i = Mathf.Max(0, n - rn); i < n; i++)
                {
                    float k = (float)(n - i) / rn;
                    smp[i] *= 0.5f + 0.5f * Mathf.Cos(Mathf.PI * (1f - k));
                }

            int tfn = Mathf.Min(n, Mathf.Max(1, Mathf.FloorToInt(Rate * Mathf.Max(0.005f, r.tailFade))));
            for (int i = n - tfn; i < n; i++)
            {
                float k = (float)(n - i) / tfn;
                smp[i] *= 0.5f + 0.5f * Mathf.Cos(Mathf.PI * (1f - k));
            }

            var clip = AudioClip.Create($"sts_{r.name}", n, 1, Rate, false);
            clip.SetData(smp, 0);
            if (!bypassCache) cache[key] = clip;
            return clip;
        }

        static float EnvOf(SoundRecipe r, RecipeLayer L, float t, float ds)
        {
            var e = r.envelope;
            if (e != null && e.mode == "adsr")
            {
                float T = r.duration;
                if (t < e.attack) return t / Mathf.Max(e.attack, 1e-5f);
                if (t < e.attack + e.decay) return 1f - (1f - e.sustain) * ((t - e.attack) / Mathf.Max(e.decay, 1e-5f));
                if (t > T - e.release) return e.sustain * Mathf.Max(0f, (T - t) / Mathf.Max(e.release, 1e-5f));
                return e.sustain;
            }
            return Mathf.Exp(-L.decay * ds * t);
        }

        // --- RBJ Audio EQ Cookbook biquads (Direct Form I) — mirrors audio_studio.html
        struct Biquad { public float b0, b1, b2, a1, a2; }

        static Biquad BiquadCoeffs(string type, float fc, float gainDb, float Q)
        {
            float A = Mathf.Pow(10f, gainDb / 40f);
            float w = 2f * Mathf.PI * Mathf.Min(fc, Rate * 0.49f) / Rate;
            float cw = Mathf.Cos(w), sw = Mathf.Sin(w);
            float alpha = sw / (2f * Q), S = 2f * Mathf.Sqrt(A) * alpha;
            float b0, b1, b2, a0, a1, a2;
            if (type == "peaking")
            {
                b0 = 1 + alpha * A; b1 = -2 * cw; b2 = 1 - alpha * A;
                a0 = 1 + alpha / A; a1 = -2 * cw; a2 = 1 - alpha / A;
            }
            else if (type == "lowshelf")
            {
                b0 = A * ((A + 1) - (A - 1) * cw + S); b1 = 2 * A * ((A - 1) - (A + 1) * cw); b2 = A * ((A + 1) - (A - 1) * cw - S);
                a0 = (A + 1) + (A - 1) * cw + S; a1 = -2 * ((A - 1) + (A + 1) * cw); a2 = (A + 1) + (A - 1) * cw - S;
            }
            else // highshelf
            {
                b0 = A * ((A + 1) + (A - 1) * cw + S); b1 = -2 * A * ((A - 1) + (A + 1) * cw); b2 = A * ((A + 1) + (A - 1) * cw - S);
                a0 = (A + 1) - (A - 1) * cw + S; a1 = 2 * ((A - 1) - (A + 1) * cw); a2 = (A + 1) - (A - 1) * cw - S;
            }
            return new Biquad { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
        }

        static void ApplyBiquad(float[] d, Biquad c)
        {
            float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float x0 = d[i];
                d[i] = c.b0 * x0 + c.b1 * x1 + c.b2 * x2 - c.a1 * y1 - c.a2 * y2;
                x2 = x1; x1 = x0; y2 = y1; y1 = d[i];
            }
        }

        /// <summary>Editor-identical RNG (mulberry32) — the noise waveform in Unity is
        /// bit-exact with the audio_studio preview.</summary>
        struct Mulberry32
        {
            uint a;
            public Mulberry32(uint seed) { a = seed; }
            public float Next()
            {
                a += 0x6D2B79F5u;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t = (t + ((t ^ (t >> 7)) * (t | 61u))) ^ t;
                return (t ^ (t >> 14)) / 4294967296f;
            }
        }
    }
}
