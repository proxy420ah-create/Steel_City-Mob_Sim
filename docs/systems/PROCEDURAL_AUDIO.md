# Procedural Audio — Synthesized Sound Effects

**Status**: ✅ Implemented (glass shatter — first synthesis consumer)
**Since**: February 2026
**Implementation**: `VoxelDamageSystem.GlassClip()` + `PreviewGlassSound()`

---

## 1. Why Procedural Audio

The project is all-procedural — voxel assets are generated, materials are palette entries, there are zero binary art assets. Audio was the missing piece:

- **No audio files exist** anywhere in the project. No `.wav`, no `.mp3`, no AudioMixer.
- **Determinism** — the whole sim is seeded. Synthesized sound can be seeded too (same shot → same scar → could be the same tink).
- **Version-control friendly** — a sound recipe is ~15 lines of C# or a JSON blob, not a binary blob.
- **Iteration speed** — change a number, hear a new sound. No DAW, no export, no import pipeline.

A `.wav` file is literally a list of amplitude values. `AudioClip.Create` gives us an in-memory buffer; `clip.SetData` fills it with samples we *calculate* instead of record. Unity treats the result identically to a loaded file.

---

## 2. The Synthesis Recipe (Glass)

A convincing percussive sound is three ingredients layered:

```
sample[i] = noiseBurst(i) + Σ partials(i) + noiseTail(i)
```

| Layer | What it is | Why it works |
|---|---|---|
| **Attack** | White noise × fast exponential decay (~10 ms) | The physical fracture event — a broadband "click" |
| **Partials** | `sin(2π·f·t)` at **inharmonic** frequencies, each decaying | Glass resonates at non-integer ratios — this is what separates "tink" from "thud" |
| **Noise tail** | White noise × slow decay | Fracture energy spreading through material — the "crunch" |

The **envelope** `exp(-decay·t)` is the single most important curve — it turns a raw sine into a plucked/struck sound. Slow decay = long ring; fast decay = click.

### Bucket mapping

Gameplay state picks the recipe — `removed` cells → bucket:

| Bucket | Removed | Dur | Noise amp | Decay | Partials (Hz) |
|---|---|---|---|---|---|
| 0 `tink` | ≤4 | 0.14s | 0.30 | 45 | 5200, 7400 |
| 1 `crack` | ≤15 | 0.28s | 0.50 | 24 | 2800, 3900, 5600 |
| 2 `crash` | >15 | 0.55s | 0.85 | 10 | 1500, 2200, 3100, 4700 |

The progression is intuitive: **more material failing → lower frequencies, more noise, slower decay.**

Clips are generated **once** and cached by bucket (`clipCache`) — synthesis cost is paid on first use only.

### Playback

```csharp
AudioSource.PlayClipAtPoint(clip, r.point, volume);
```

Positional 3D audio — the impact point's world position, log-rolloff against the active `AudioListener`. Volume scales with removal count: `0.4 + removed/20` clamped to 1, times `glassAudioVolume`.

**Critical requirement**: an active `AudioListener`. The MapCamera auto-adds one during planning; FollowCamera takes over (disabling others) when it spawns. Without a listener, `PlayClipAtPoint` is a silent no-op — this was the actual "no sound" bug.

---

## 3. The Sound Design Space

The recipe is general — glass is just one parameterization. Any struck/impulsive sound is *attack + partials + envelope + tail*:

| Sound | Attack | Partials | Envelope | Tail |
|---|---|---|---|---|
| Glass tink | sharp click | high, inharmonic | fast | light |
| Glass crash | heavy click | low-mid, dense | slow | heavy noise |
| **Gunshot** | very sharp, saturated | low-mid body (200–800 Hz) | fast-mid | long noise "boom" |
| **Ricochet** | click | high metallic ring | medium | pitch-bent whine (see §5) |
| **Footstep** | soft click | ~none | very fast | dirt/gravel noise texture |
| **Metal clang** | click | harmonic stack ×1,2.76,5.4 (bar modes) | medium | ringing |
| **UI blip** | none | single tone | fast | none |
| **Engine idle** | none | low harmonic stack | none (loop) | filtered noise rumble |

### Techniques that extend the palette

- **Frequency sweeps** — `sin(2π·f(t)·t)` where `f` descends = ricochet whine, dive-bomb whistle.
- **AM/FM modulation** — `sin(carrier + modDepth·sin(modFreq·t))` = growly engine, electric hum.
- **Filtered noise** — run the noise through a one-pole lowpass (`y = α·x + (1−α)·y_prev`) = muffled thump, cloth, flesh hits.
- **ADSR envelope** — attack/decay/sustain/release instead of pure exponential = tonal instruments, sustained alarms.
- **Additive stacking** — several decays at once = "body + ring" (punch + resonance).
- **Seeded variation** — `rng = new Random(shotSeed)` on the partials' phases/amps → every impact slightly different, deterministic replay.

---

## 4. Parameterizing for an Editor

The current code hardcodes recipes per bucket. The natural next step is to lift the numbers into a **sound recipe** struct — this is what makes the HTML editor viable:

```csharp
public struct SynthRecipe {
    public float duration, noiseAmp, decay;
    public float[] partials;       // Hz
    public float[] partialAmps;    // relative loudness per partial
    public float attackDecay;      // the click's falloff (60f in glass)
    public int seed;               // variation seed
    // future: sweepHz, filterCutoff, adsr[], fmDepth…
}
```

A recipe is **pure data** — JSON-serializable. The Unity side only needs `AudioClip SynthFromRecipe(SynthRecipe r)`. Recipes can live next to `.stasset` files, ride the same tooling philosophy: author in a visual editor → JSON → runtime synth.

---

## 5. HTML Audio Editor — Feasibility

**Verdict: very feasible.** The synthesis is platform-agnostic math (sin, exp, RNG). The browser's **Web Audio API** can reproduce it exactly:

### What the editor would look like

```
┌──────────────────────────────────────────┐
│  Recipe editor           Waveform view    │
│  ┌─────────────┐        ┌──────────────┐ │
│  │ duration    │ 0.28   │  ╲╲_          │ │
│  │ noise amp   │ 0.50   │   ╲───___     │ │
│  │ decay       │ 24     │              │ │
│  │ partials    │ [2800] │              │ │
│  │             │ [3900] │              │ │
│  │             │ [5600] │              │ │
│  │ attack dec. │ 60     │              │ │
│  │ seed        │ 1337   │              │ │
│  └─────────────┘        └──────────────┘ │
│  [▶ PREVIEW]  [EXPORT JSON]  [SAVE RECIPE]│
└──────────────────────────────────────────┘
```

### Implementation sketch (pure Web Audio — no engine)

```js
// Identical math to GlassClip — render into an AudioBuffer
const rate = 44100, n = rate * recipe.duration;
const buf = ctx.createBuffer(1, n, rate);
const ch = buf.getChannelData(0);
const rng = seededRng(recipe.seed);
for (let i = 0; i < n; i++) {
  const t = i / rate, env = Math.exp(-recipe.decay * t);
  let v = (rng()*2-1) * recipe.noiseAmp * Math.exp(-60*t);        // attack
  for (let p = 0; p < recipe.partials.length; p++)
    v += Math.sin(2*Math.PI*recipe.partials[p]*t + p*1.7)
         * (0.5/(p+1)) * env;                                     // partials
  v += (rng()*2-1) * recipe.noiseAmp * 0.35 * env;               // tail
  ch[i] = Math.max(-1, Math.min(1, v)) * 0.8;
}
const src = ctx.createBufferSource(); src.buffer = buf;          // play
src.connect(ctx.destination); src.start();
```

The loop is **identical** to the C# — same rate, same envelopes, same seeded RNG (use a deterministic LCG/Mulberry32 in JS to match Unity's `System.Random` only if cross-platform replay matters; otherwise any seeded RNG works).

### Pipeline options (mirror the voxel tooling)

| Path | How | Verdict |
|---|---|---|
| **JSON recipes at runtime** | Editor exports `glass_crash.stsound` / JSON → Unity deserializes → `SynthFromRecipe` | ✅ **Preferred** — live-editable, no recompile, matches JSON↔stasset workflow |
| **Baked WAV export** | Editor renders the buffer, exports `.wav` → Unity `Resources.Load` | Fine, but reintroduces binary assets |
| **Copy params into C#** | Editor emits a `SynthRecipe` literal | Zero infra, but doesn't scale to a library |

### Round-trip parity caveat

If sounds must be **bit-identical** between the editor preview and Unity playback, port `System.Random` to JS (it's a subtractive RNG — ~20 lines) or pick a cross-platform RNG both sides share. If "sounds right" is enough, any seeded RNG per-side is fine — the recipe shape (partials, envelopes) is what the listener hears, not the exact noise waveform.

### Editor feature roadmap (if built)

1. **P0**: sliders/fields → live Web Audio preview + JSON export
2. **P1**: preset library (tink/crack/crash/gunshot/footstep), waveform + spectrum display (AnalyserNode)
3. **P2**: multi-layer recipes (stack two attacks, a body + a ring), ADSR editor
4. **P3**: randomize-with-seed (play N seeded variants, keep the good one → locks recipe+seed into the sound's identity)

---

## 6. Integration Points

- `VoxelDamageSystem.GlassClip(removed)` — current glass bucket synth + cache
- `VoxelDamageSystem.PreviewGlassSound(bucket)` — planning-phase HUD preview (plays at listener, no distance falloff)
- `ProjectileSystem` — future hook for gunshot synthesis (muzzle report, distant crack)
- `PathDebugRenderer` — the shard/particle system already proved the "gameplay data → presentation" pattern; audio recipes ride the same event (a glass hit already knows count, position, material)

## 7. Open Questions

- Should recipes be **per-material** (glass/wood/metal/flesh get their own struck-sound sets)? Likely yes — a material ID → recipe table is a small dictionary.
- Should `shotSeed` seed the synth so identical shots sound identical? Cheap, deterministic — probably yes.
- Should there be a `.stsound` authoring format alongside `.stasset`? If the HTML editor is built, this is the natural artifact.

## References

- `VoxelDamageSystem.cs` — `GlassClip()`, `PreviewGlassSound()`, `clipCache`
- `GLASS_SHATTER_MECHANIC.md` — the consuming mechanic (removed-count → bucket mapping)
- `VOXEL_GLASS_TRANSPARENCY.md` — material 134 (cracked) palette
