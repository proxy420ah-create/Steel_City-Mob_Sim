# SFX Sound Catalog — design intent per sound

**Status**: 🔄 LIVING DOCUMENT — every sound in the game gets an entry here
**Sibling docs**: `SFX_PIPELINE.md` (runtime plumbing), `PROCEDURAL_AUDIO.md` (synth math + studio)
**Maintenance rule**: when a sound is added, re-authored, or retuned — its entry
updates here. Recipe *numbers* live in the `.stsound` files; this catalog holds
the *why*: design intent, anatomy, tuning history, and variant-family notes.

---

## Conventions

- **Served** = the file the game actually plays (`sounds/<name>.stsound`).
- **Alternate** = sibling candidates kept for re-audition (usually `LiveTesting/`).
- **Two-stage click** = contact strike + delayed seat strike via layer `delay`
  (the `.stsound` per-layer field — each delayed layer runs its own timeline).
- **Reference target** = a real recording the synth was authored against
  (`sounds/Impacts/reference/ricochet.mp3` — never loaded by the game).

---

## Gunshot — `sounds/gunshot.stsound` ✅ served

| | |
|---|---|
| **Intent** | A 1920s revolver report — sharp broadband crack, short body, urban-tail echo |
| **Anatomy** | `attack` (noise burst, decay ~74) + `partials` (180/320/520 Hz inharmonic ring) + `noiseTail` (lowpass ~3.2 kHz) + echo on (80 ms, fb .35, mix .25) |
| **Propagation** | `refDist` 4 m full-volume → `shotAudibleRange` 50 m — the same edge AI hears (`AmplitudeAt` shared law) |
| **Notes** | `drive` ~2.9 does the loudness work pre-tanh; EQ: lowshelf +16.8 dB @ ~560 Hz (body), peaking −6.9 dB @ ~13 kHz (hiss tame), highshelf −6.2 dB @ ~13.3 kHz. `tailFade` 0.28 s. Decay times exceed duration on purpose — the tail cuts mid-ring under the fade. |

**Tuning history**: authored in `audio_studio.html`; reference-vs-muzzle A/B
calibrated in the Sfx tab (serialized `shotAudioVolume` trim is the shipped mix).

## Reload click — `sounds/reload_click.stsound` ✅ served · 6 alternates in `sounds/LiveTesting/`

| | |
|---|---|
| **Intent** | The revolver cylinder seat — a mechanical two-stage "Cu-Lick": dull contact strike, then a brighter seat strike ~22–45 ms later |
| **Anatomy** | two `attack` layers — strike + **delayed** second strike (`delay` field, the two-stage trick); attack `tone` bandpass click + partials + whisper noise tail |
| **Trigger** | `CombatController.RoundSeated` → `ReloadAudio` — one click per cylinder seat, SimClock-locked (same tick as the support-hand `tapPulse` press) |
| **Propagation** | near-field defaults: refDist 1.2 m, audible ~10 m |

**Tuning history**: v1 "too pickaxe" → light-switch snap (ring cut ~40 ms,
attack ~14 ms, sweep micro-glide for the spring-release flick) → v3 Cu-Lick
family: `_am` (AM lobes), `_deep` (ch-unk), `_crisp` (t-lik), `_chirp`
(pressure exciter), `_mech` (ragged double-action). User's Variant-Lab child
(bright twin strikes 6.4k/7.4k Hz, 22 ms delay, seed 1097609) promoted to served.

## Surface impacts — `sounds/Impacts/*.stsound` pool ✅ live

| | |
|---|---|
| **Intent** | Material-specific landing reports — stone thud, wood knock, metal ping — distinct enough to read surface by ear |
| **Anatomy** | per-file recipes; `impact`/`ricochet` pool split by filename + the physics gate (incidence ≥ 85° and muzzle distance ≤ 40 m → ricochet variant) |
| **Variant system** | `modVariants` bounded children per file via `MutateRecipe` (fixed `VariantSeed(file, slot)` — a child sounds identical in panel audition and in game); culled slots persist to `<file>.cull.json` sidecars |
| **Caching** | rendered clips cached per (file, slot, vel, write-time) — the per-impact uncached synth storm was the crackle bug (Oct 29) |

## Glass — synthesized inline (`VoxelDamageSystem.GlassClip`) ✅ live

| | |
|---|---|
| **Intent** | Window/pane shatter — high tink vs structural crash by cell count |
| **Anatomy** | noise-burst attack + inharmonic partials + noise tail, bucketed tink/crack/crash by voxels removed (the original recipe the engine was built on) |

---

## Authoring workflow (the loop)

1. `audio_studio.html` → bind `sounds/` dir → pick preset or mutate in Variant Lab → audition (Space).
2. Save into `sounds/LiveTesting/` to shadow-test in game (a LiveTesting file with
   the same name **wins** over the served one — in-game A/B without touching shipped audio).
3. In-game: Sfx tab ▸ per-file trims (`gain ×` = baked loudness, `level ×` = fader)
   + variant audition/cull grid; serialize = the shipped mix.
4. Promote: move the file out of LiveTesting over the served name. Catalog entry updates here.
