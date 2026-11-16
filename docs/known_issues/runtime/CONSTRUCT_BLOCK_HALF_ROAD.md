# Construct Block Table Showed Half-Streets — One Stripe Where Unity Shows Two

**Date**: November 16, 2026
**Status**: 🟢 FIXED (design-scope correction — the half-road was seam-correct, the exhibit look wasn't)
**Severity**: 🟡 HIGH (every block/street exhibit read unlike the game; user-reported)

## Symptoms
- The block table's roads show ONE cobble stripe per street; in-game streets show TWO lane
  lines with cars driving on them.
- User hypothesis (correct): "is it only half the road? … how do road seams work?"

## Root Cause — seam semantics, not a paint bug
`BLK.ROAD = 45` cells = 2.25 m = exactly half of the 4.5 m street. The game's law: each
terrain chunk paints **its own half** of the bordering road (`FillCorridors`), cobble stripe
at the half's center; two adjacent chunks' halves meet at the seam → stripes at ±roadWidth/4
= the dual lane lines. Our single-block composite faithfully painted only the block's half →
one stripe at band center. `emitZone`'s interior seams already formed full streets correctly
(two tiles, two halves) — only the standalone block exhibit looked unlike the game.

## Resolution — the block table is a diorama: block + its surrounding full streets
- New `BLKF` constants for `emitBlock` only: band `2×ROAD` = 90 v, margin `M` = 110 v,
  tile = 412 cells. Each 'road' edge paints far half [0,45) + seam at 45 + our half
  [45,90) + our curb — stripes at `z===22 || z===ROAD−23` (each half's center). The rim is
  the far curb; no far sidewalk (it belongs to the absent neighbor). `fillBlockGround` and
  `stampSlots` take a `B` constants param — `emitZone` keeps `BLK` defaults (strict seams).
- `mn._blkM` records the composite's margin so `roadProf`/`blockTrf`/`mountParked` read
  either geometry (zone 65 / block 110).
- `roadProf` scan **reversed to rim→inward**: the far half is flat, so the old outward scan
  from `D−50` would never reach the curb — the first raised run off the rim IS our sidewalk
  strip. `road` = our-half curb-side boundary; `dir` = rim direction (unchanged consumers).
- `blockTrf` bare-band fallback and `mountParked`'s `E.tr`/along-clamp now derive from
  `_blkM` (`ib = _blkM − SIDE` = the road band's inner boundary) instead of `BLK.M`/`TILE`.

## Verification
- `_cars_verify.mjs` ALL PASS — both cars on the correct half's lane, noses on tangent,
  maneuvers unchanged.
- `_car_measure.mjs` — cars at `.265` (proportionally identical seat; `vs` shrank with the
  412-cell composite), consistent pre/post model swap.
- `_noslice_verify.mjs` 13/13 — flags, locks, settle, roof extras intact.
- Shots: `probe_shot.png` — both lane lines visible on the south and east roads; cars
  parked against our curb.

## Related Files
- `docs/construct/03_tables.js` — `BLK`/`BLKF`, `fillBlockGround`, `stampSlots`, `roadProf`,
  `blockTrf.build`, `mountParked`, `emitBlock`
- `docs/CONSTRUCT_DRAFTING.md` — composite recipe section documents the seam law + diorama

## Postscript (2026-11-16) — the diorama now lives on a static ring
The BLKF full-street ground is emitted by `emitRing` once per block table (`spec.ring`,
`mn.static`) — the same recipe, hoisted out of the swappable composite. `emitBlock` keeps
the single-composite path for ring-less hosts. See `CONSTRUCT_TENEMENT_CURB_STRADDLE.md`.
