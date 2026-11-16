# Construct Parked Cars Straddle the Curb on Tenement — Per-Model Road Scan Variance

**Date**: November 16, 2026
**Status**: 🟢 FIXED (by architecture — the static street ring)
**Severity**: 🟡 HIGH (visibly wrong parked stance on the default block model; user-reported)

## Symptoms
- On `block_26_tenement`, parked cars sit half on the raised sidewalk — wheels over the
  curb, reading "parked on the curb" instead of in the street like the game.
- The same build on `block_41_barber` parks correctly — same code, different model.

## Root Cause — measuring model-specific geometry for a table-constant law
`roadProf` scans each mounted composite's `localCols` for a raised sidewalk run and parks
cars at that strip's lane edge (`st.road + carW/2`). Tenement's composite carries a wide
raised strip reaching ~34 cells inside the curb band; `st.road = 288` placed the parked
center at ~307 — straddling the sidewalk. Barber's flat ground produces no strip at all →
the `E.tr` band fallback parks flush at the road's inner boundary → correct. The curb law
was faithful per-model; the BUG was that a table-constant (where the curb is) was being
derived from variable authored content — and re-derived on every swap, so every model could
break differently.

## Resolution — the street ring is a static table prop (`ring: true` spec)
- `emitRing` paints the street diorama ONCE per table: BLKF ground (roads, curbs,
  sidewalks, empty pad), `mn.static`, `_road = {}` — deterministic band law forever.
- `emitCore` stamps only the model's 3×3 lots into the 192-cell interior seating inside
  the ring (`_vs` = ring voxel size, same center → cells align 1:1).
- `mountParked` parents cars to the ring once — swaps can no longer remount/orphan them;
  the cruise keeps lapping through a mid-cruise swap.
- `mountModel` skips `mn.static` in its sweep and re-syncs the ring's tops after the
  core's revert drops overlapping keys; `.find` sites for the swappable mini filter
  `!mn.static`; `blockTrf`'s host lookup reads the parked rec's `g.parent` directly.
- Every block model now parks/drives on identical geometry — the "barber look" is the
  only look.

## Verified
- `_ring_verify.mjs` **17/17**: two minis, cars ring-parented, cruise survives swaps,
  claims land, ring uuid stable.
- `_cars_verify.mjs` 30/30, `_car_measure` seats `.265` both mounts, `_noslice_verify`
  13/13.
- Screenshots `probe_ring_low.png`/`probe_ring_top.png`: tenement core in the ring, cars
  on the road at the curb edge.

## Related Files
- `docs/construct/03_tables.js` — `emitRing`, `emitCore`, `emitVox` (`_vs`/`static`),
  `mountModel` sweep, boot chain, `blockTrf` host lookup
- `docs/_ring_verify.mjs`, `docs/_ring_shot.mjs` — architecture + capture probes

## Lesson
When a law is *table-constant* (the curb line, the lanes, the parked cars), don't derive
it from *model-variable* content — hoist it into the table's static exhibit and let models
vary only inside it.
