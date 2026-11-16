# Construct block-table cars parked/drove left-handed in the mirrored render

**Date**: 2026-02-14
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — wrong-side traffic on the flagship exhibit

## Symptoms
- BOSS: "Cars are parking on the curb but facing East and South while in right
  hand lane traffic — their routes need to be flipped so they drive correctly
  on the right side of road and make right turns. Is this a Unity flip issue
  or just signs we used?"
- S-curb car faced rendered **east** with the block on its LEFT; the cruise
  rendered counterclockwise with LEFT turns at every corner.

## Root Cause
**A Unity-flip issue — `mirX`/`fx` chirality, not bad signs.** `emitVox`
renders every composite x-mirrored to match the voxel editor's `scale.x=−1`
"unity view" convention (`CONSTRUCT_DRAFTING.md` "Unity truth — X-mirror").
Mirroring flips **chirality**: `PARK_EDGE`/`blockTrf.segs` were authored as a
legally-correct right-hand law in *cell space* (curb on right, clockwise,
right turns), but the mirrored render draws that law **left-handed** — the
s-curb car's −x heading renders +x/east, and the circulation reads
counterclockwise with left turns. The heading render path (`hx·mir`, rotY)
was already mirror-consistent; the *law itself* was fed unmirrored.

## Fix
Feed the law in render terms — the cell-space law becomes the mirror-inverse
of the raw legal set so the mirrored view shows right-side parking + right
turns:

- `PARK_EDGE` headings negated: s `+x`, n `−x`, e `−z`, w `+z` (cell space);
  through `hx = mirX ? −E.hX : E.hX` the rendered noses land west/north/south/
  east correctly per curb.
- `blockTrf.build` segs reversed: cell space circulates **s→e→n→w**
  counterclockwise; `arc()` gains `sgn` — corner sweeps run negative so the
  loop is one continuous reversed circuit. `at()`'s arc tangent multiplies
  by `g.sgn`.
- `stn`, `slotInv`, `mountParked`, `claim`, `snap` all read `PARK_EDGE`/`sc.hx/
  hz` — maneuver endpoints, pull-ins, claims, and adoption flip with it, no
  additional edits.

## Verification
- `_cars_verify.mjs` ALL PASS under updated `wantRy` (s:−π/2, n:+π/2, e:π, w:0):
  parked noses on the rendered-RHT direction, angled pull-ins at drive speed,
  claims `e@228`/`s@146`, adoption exact, re-depart works.
- Rendered-frame check: every curb's parked car has the block on its RIGHT;
  circulation renders clockwise, all corner turns right.

## Related Files
- `docs/construct/03_tables.js` — `PARK_EDGE`, `blockTrf.build` segs, `arc` sgn,
  `at()` tangent sign
- `docs/_cars_verify.mjs` — `wantRy` updated to the rendered-RHT expectations
- `docs/CONSTRUCT_DRAFTING.md` — parked + CARS sections document the
  rendered-RHT law
- Convention root: `docs/CONSTRUCT_DRAFTING.md` "Unity truth — X-mirror";
  `Assets/Scripts/Sim/CharacterAssets.cs` (`handedness:"unity"` promotion —
  authored data is editor-convention, Unity gets flipped data)
