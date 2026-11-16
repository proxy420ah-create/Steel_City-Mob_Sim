# Construct Park-Anywhere — Empty Lattice on Flat-Strip Models → Cars Lap to Origin

**Date**: November 16, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH (park-anywhere silently dead on `block_41_barber`; user-reported)

## Symptoms
- CARS toggles on: cars pull out and lap correctly. Toggle off: cars keep full speed and
  lap indefinitely — or, after the origin fallback shipped, advance to their AUTHORED
  spots and park there instead of the next free curb slot.
- `__cars.diag()` on the user's session: `on:false, len:1091, inv:0, free:0` — the press
  landed, the circuit was the bare-band fallback, and the parking lattice was empty.

## Root Cause — two stacked failures on `block_41_barber`
1. **`roadProf` finds no curb strips.** The scanner detects the sidewalk as a raised run
   (`columnTop > 4`) in `mn.localCols`. Barber's ground emits road/curb/sidewalk cells
   FLAT — its `localCols` lists exactly the 192² interior cells; nothing raised exists in
   the 140-cell rim scan → `road = {}` → `build()` falls back to the bare-band circuit
   (`len:1091`, lane centers still on the painted stripes at `margin − SIDE − HALF`).
2. **`slotInv` skipped unmeasured edges.** `if (!st) continue` → every edge skipped →
   `inv:0` → `claim()` returns null every tick → cars can only lap (or hit the origin
   fallback). The lattice never existed for the user's model.

## Resolution — one shared curb law + a poison guard
- New `curbC(e, mn, carW)`: the measured strip's lane edge when a raised sidewalk exists,
  else the band's inner boundary (`D−ib+carW/2` / `ib−carW/2` — the old `E.tr` fallback).
  `slotInv` and `mountParked` both read it, so slots and parked cars land on the same curb
  line regardless of whether the model carries raised strips.
- `roadProf` no longer caches an empty result — a `mn._road = {}` from an early/partial
  scan would have poisoned the model's lifetime.
- `__cars.diag()` now reports `model` and which `road` edges measured, so the next
  "cars won't park" report names the failing stage in one line.

## Verified
- `_park_barber.mjs` (the user's session): barber mounted → pad CARS on → 6 s cruise →
  pad CARS off → `inv:12, free:10`, claims land `n@146`/`e@146`, both cars `slot` in ~6 s.
- `_cars_verify.mjs` 30/30 on tenement (measured-strip path unchanged).
- `_car_measure.mjs` — seats `.265` on both tenement and barber.

## Related Files
- `docs/construct/03_tables.js` — `roadProf` (no-cache-empty), `curbC`, `slotInv`,
  `mountParked` (unified on `curbC`)
- `docs/construct/04_pad.js` — `__cars.diag()` model/road fields
- `docs/_park_barber.mjs` — the flat-model repro/verify probe
- `docs/_roadprof_scan.mjs` — per-model roadProf/slotInv sanity dump

## Lesson
Any road-profile consumer must treat "no measured strip" as a *valid model property*, not
an error state — flat-ground composites are legal exhibits. Every measured-geometry code
path needs a band-law fallback at the same call site it measures.
