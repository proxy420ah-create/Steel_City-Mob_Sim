# SFX Pipeline — runtime audio architecture (handoff doc)

**Status**: ✅ Live — gunshot + surface impacts + glass all play in game
**Read first**: `docs/systems/PROCEDURAL_AUDIO.md` (synthesis math, `.stsound` schema v4, audio_studio.html)
**Design layer**: `docs/systems/SFX_SOUND_CATALOG.md` — per-sound intent, anatomy, tuning history (living doc; every sound gets an entry)
**Key files**: `Assets/Scripts/Sim/ProceduralAudio.cs`, `SurfaceImpactAudio.cs`, `ProjectileSystem.cs`, `VoxelDamageSystem.cs`, `DebugHUDManager.cs` (Sfx tab), `VoxelAssetStudio/audio_studio.html`

---

## 1. The pipeline in one paragraph

`.stsound` JSON recipes (authored in `audio_studio.html`, saved into `Assets/StreamingAssets/sounds/`) → `ProceduralAudio.LoadRecipe` (JSON → `SoundRecipe`) → `SynthFromRecipe` (sample-level synth → `AudioClip`, mulberry32 bit-exact vs the studio) → `ProceduralAudio.PlayAt` (positional `AudioSource`, linear rolloff `refDist`→`audibleRange`) + `SfxWaves.Report` (semantic `Heard` event → AI hearing) + `SfxWaves.Emit` (expanding wireframe shell for the debug layer — visual only).

Two loudness stages, deliberately separate:
- **gain ×** = sample amplitude at synth (`vel` param multiplies `recipe.gain` — applied AFTER the tanh limiter, so a trim pushing peak > 1.0 is soft-limited by `ProceduralAudio.SoftLimit` (knee 0.95) and warns `peak X > 1.0` in the console. Treat that warning as "trim too hot": over-full-scale audio saturates/crackles instead of getting louder)
- **level ×** = `AudioSource.volume` (caps at 1, then distance-rolled-off — a mixer fader)

## 2. Sound families and who owns them

| Family | Owner | Source | Wave tag | AI reacts? |
|---|---|---|---|---|
| **gunshot** | `ProjectileSystem.PlayShotSound` (at `Fire`) | `sounds/gunshot.stsound` file | `gunshot` | ✅ yes (only this) |
| **impact + ricochet** | `SurfaceImpactAudio` (new, `ShotResolved` subscriber) | `sounds/Impacts/*.stsound` file pools | `impact` / `ricochet` | ❌ debug waves only |
| **glass** | `VoxelDamageSystem` (`ShotResolved` subscriber) | synthesized in C# (`GlassClip`), 3 buckets tink/crack/crash by cells removed | `glass` | ❌ |

`ShotResolved` carries `muzzle, aimDir, shotDir, hit, target, material, point, normal, distance, flightTime, intendedTarget, lateralMiss, impactOffset, seed`. `SurfaceImpactAudio.GetOrCreate()` is spawned from `ProjectileSystem.Fire` before resolution so no shot ever resolves unserved. **Glass is checked first** (`ProjectileSystem.IsGlassMaterial`) and exits early — VoxelDamageSystem owns glass exclusively.

## 3. Surface-impact routing (the physics gate)

`SurfaceImpactAudio.OnShotResolved(ShotReport r)`:

- `incidence = Vector3.Angle(-r.shotDir, r.normal)` — **0° dead-on → 90° skim** (angle from the surface normal, not the surface)
- `grazing = incidence ≥ ricMinIncidenceDeg` (default **85°** — nearly parallel only) `&& close = r.distance ≤ ricMaxDistanceM` (default 40 m — bullet still has energy to skip)
- `grazing && close → "ricochet" pool`, else → `"impact" pool`
- **One folder, split by filename**: `sounds/Impacts/` — names containing "ricochet" (ricochet_*, ricochet_stone_*, Thud_ricochet, ricochet_dl) form the ricochet pool; everything else is the impact pool
- **Material tags**: `MatTag(materialId)` maps palette ids → stone/wood/metal/flesh; files named `<pool>_<tag>_NN` are preferred on matching hits (only `ricochet_stone_*` exists today), untagged = generic fallback
- File pick + child slot pick both derive from `r.seed` → deterministic replay

## 4. LiveTesting — the exclusive test pool

`sounds/Impacts/LiveTesting/*.stsound` — while populated it IS the pool (shipped files invisible to the picker). Also per-file: `ProceduralAudio.ResolveLiveTesting` shadows `sounds/<dir>/<file>` → `sounds/<dir>/LiveTesting/<file>` inside `GetClip`/`LoadRecipe`, so requests for a name resolve to the test twin. Delete/move files out → shipped mix returns. Currently holds `Impact_01.stsound` + `ricochet_01.stsound` (one file per pool — perfect A/B rig).

**Filename is the single source of truth**: studio Save-As syncs `recipe.name` to the chosen filename; `LoadRecipe` overrides `r.name` with the file basename (logs always show the real file played).

## 5. Runtime modulation — Variant-Lab children per hit

`ProceduralAudio.MutateRecipe` = C# port of the studio's `mutateRecipe` (same grammar: layer amp/decay jitter, partial drop/grow/freq-shift, attack tone/crack/chirp, noise lowpass, sweep fields, layer drop/dup; master knobs — duration/gain/env/eq/echo — frozen when `modLockMaster`).

- **`modVariants`** bounded child slots per file (default 8). Child `vN` = `MutateRecipe(base, VariantSeed(file, N))` — a FIXED seed per (file, slot), so `v03` sounds the same in panel audition and in game.
- Per hit: seeded pick → file → seeded pick → kept slot → `MutateRecipe` → `SynthFromRecipe` → play. One authored file = a family of children.
- **Variant clips are cached** (`SurfaceImpactAudio.variantCache`, key = `rel | slot | vel | recipe write-time`). The original path ran `LoadRecipe` + `MutateRecipe` + uncached `SynthFromRecipe` **on the main thread per impact** — disk IO + JSON clone + full PCM render + ~1.5 MB allocs per bullet landing → frame stalls + GC churn → mixer crackle right after each bang. The set is bounded so caching is safe; the write-time in the key keeps studio-save hot-reload, and stale clips are never Destroyed mid-play.
- **Culling**: SFX panel ▸-expand on a file shows `v01…vN` cells (▶ audition + keep-checkbox); unchecked children never play (all culled → authored file, `vbase`).

## 6. Per-file tuning — sidecar JSON

`<file>.stsound.cull.json` sits next to the RESOLVED recipe (LiveTesting files get their own):

```json
{"disabled":[1,4], "gain": 4.0, "volume": 0.9}
```

- `disabled` — culled variant slots
- `gain` — this SFX's sample-level gain × (0.25–8)
- `volume` — this SFX's source volume × (0.05–1)

API: `SurfaceImpactAudio.FileMix(rel)` / `SetFileMix` / `SetSlotCulled` — loaded once, written on change, cached by request path. Old `{disabled:[…]}` sidecars load fine (missing fields → defaults).

## 7. SFX panel (DebugHUD → Sfx tab) — the control surface

Per family a `SecFold`: **GUNSHOT** / **SURFACE IMPACT** / **GLASS**. Each has:
- A **mute toggle** (`shotAudioEnabled`, `impactAudioEnabled`, `glassAudioEnabled`) — checked per event → real-time isolation. ⚠️ Gunshot's wave is what AI hears; muting it also silences AI awareness.
- Mix sliders: `level ×` (src volume), `gain ×` (samples, impact pool only), `full-vol radius m` (refDist), `report range m` (audibleRange).
- SURFACE IMPACT extra: `ricochet min incidence °` (85), `ricochet max muzzle dist m` (40), modulation toggle + `jitter` (.12/.30/.55) + `lock master knobs`, `modVariants` count, and a `last:` decision line (`#N Stone inc 63° dist 12.4m → ricochet/ricochet_01.stsound v03`).
- File rows: `▶` preview at listener (through the SAME gain/mix chain as gameplay), `(LIVE)/(live)/(shipped)` tags, `▸` expander → per-file `gain ×/level ×` sliders + variant audition/cull grid.

## 8. SfxWaves — the debug + hearing layers (now actually two)

Two entry points, deliberately split:

- **`Report(origin, range, refDist, tag, shooter, visualOrigin?)`** — the
  semantic channel. Raises the static `SfxWaves.Heard` event for EVERY real
  report regardless of `showWaves` (the old `Emit` early-out made hearing
  debug-dependent), then calls `Emit` for the shell. `origin` is the intel
  position (`WeaponMount.FeetCenter` — what belief snaps to); `visualOrigin`
  puts the display ring at the acoustic source (`MuzzleWorld`). Same-tick
  synchronous inside the fixed-step fire → deterministic, F8-safe.
- **`Emit(origin, range, refDist, tag)`** — visual only. Expanding 3-ring
  wireframe (PathDebugRenderer), rim color = live audibility
  (`SfxWaves.AmplitudeAt` is the shared law). All tags visualize. Preview/test
  emits use this directly — they never reach AI.

The shipped `Heard` subscriber is `CombatController.OnSfxHeard` — **AI hears
`gunshot` only**, self-filtered (not self, in `AmplitudeAt` range, shooter is
the listener's threat/watch target) → `OnShotHeard` = intel, not a sighting.
The report emit is inside the same toggle-gated path as playback for every
family (`shotAudioEnabled=false` silences AI hearing too — by design).

## 9. Tuning the feel — where the knobs live

| Want | Knob |
|---|---|
| Louder than authored | per-file `gain ×` (or pool `impactGain`) — sample level |
| Reach further | `report range m` (audibleRange) — the rolloff edge |
| Fuller near-field | `full-vol radius m` (refDist) |
| Ricochet rarer/flatter | `ricochet min incidence °` toward 89 |
| Ricochet at longer range | `ricochet max muzzle dist m` |
| Wilder variation | `jitter` up / unlock master knobs / more `modVariants` |
| Kill a bad child | ▸ expand → uncheck its `vNN` (sidecar persists) |
| Isolate a family | uncheck the others' sfx toggles |

## 10. Open work / known gaps (delegate's backlog)

1. **Authored content**: only `Impact_01` + `ricochet_01` are authored for the new pools; `impact_stone_*`, `impact_metal_*`, `impact_wood_*`, `impact_flesh_*` slots already route by tag — just author files named `<pool>_<tag>_NN.stsound` (flesh = body hits, a distinct pool).
2. **Pool per weapon**: all projectiles share the same pools; caliber→pitch (`SynthFromRecipe note`) is wired in the synth but unused by gameplay (open Q §7 of PROCEDURAL_AUDIO).
3. **Angle-differentiated children**: within-pool choice is seeded-random only; a zip-vs-thud split by incidence band is a possible second tier.
4. **Misses are silent** by design (`r.hit=false` → no impact) — a near-miss whiz/crack channel is unimplemented (wave infrastructure already exists).
5. **Gunshot wave couples AI + audio**: muting gunshot for audio isolation also blinds AI hearing (`Report` rides inside the `shotAudioEnabled` gate) — if solo-audio debugging needs AI to keep reacting, split the report emit from the clip path.
6. **`modLockMaster` unlocked** mutates `duration`/`echo` — audible timing drift; intentional but noticeable on wild jitter.
7. **Glass is synth-only** (no files) — could become file-based buckets via the same pool machinery if authored glass recipes appear.
8. **Footsteps/engines/UI blips** — preset recipes exist in the studio; no gameplay emitters yet.

## 11. Conventions to keep (read before changing)

- `ShotResolved` subscribers own families — never put playback in `ProjectileSystem` itself (ballistics stays clean); a ricochet wire-up inside `ProjectileSystem` was previously reverted at user request.
- `SfxWaves.Report` rides inside the same toggle gate as playback for every family — don't decouple silently (and never call `Emit` for a REAL shot — that's a silent shot to AI).
- Sidecars travel with the recipe file — moving a file between main folder and LiveTesting should move its `.cull.json`.
- Determinism: anything a shot "rolls" must derive from `r.seed` (or `VariantSeed` for stable per-file slots), never `Random`/`Time`.
- Headless compile recipe: `docs/core/HEADLESS_UNITY_COMPILE.md`.
