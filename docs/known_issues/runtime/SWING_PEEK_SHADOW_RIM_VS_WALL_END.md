# Swing Peek Measured the Shadow Rim, Not the Wall End — Wall-Side Bias

**Date**: 2026-10-23
**Status**: 🟡 FIX APPLIED (awaiting playtest)
**Severity**: 🟡 HIGH

## Symptoms
- Side-peek favors the wall side over the open side — the step-out lands
  still behind the wall (peeks AT cover) or overshoots past a corner the
  wall already ended at, while the side the actor could actually see
  over/around was the nearer opening.

## Root Cause
`TryStartSwing` measured the step-out boundary with
`CoverField.TryZoneEdge` — the farthest **Hidden** zone member along
±lateral. That's a *threat-shadow* metric, not geometry:

- If the hidden band wraps the wall's end (corner shadow), the rim sits
  past the physical end → the step overshoots into open ground.
- If the hidden band ends before the wall's end (asymmetric shadow /
  zone clipped by the field window), the rim stops early → `rim+margin`
  lands still behind the wall → `SwingSideScore` reads `Hidden` →
  peek-at-wall.
- On tied scores (`sL == sR`) the committed `PeekSide` roll won — a
  stale coin-flip could favor the wall side indefinitely.

## Fix
The wall's physical ends are already authored in `sensor.walls` — per-face
marks with `facePos` + `outDir`. `TryStartSwing` now:

1. Projects all cover-facing marks on `CoverObstacleId`
   (`outDir·zoneOutDir > 0.7`) onto the lateral axis → `latMin`/`latMax` =
   the wall's real ends.
2. Extends each side's step base to `max(hiddenRim, wallEnd)` — under-
   shooting the wall end is fatal (peeks at wall); overshooting the
   hidden rim is merely conservative (longer covered transit).
3. Scores the FINAL destination on each side, and on tied scores picks
   the **nearer** end — the open side wins ties instead of `PeekSide`.

`PeekSide` remains the last-resort tie-break when both ends are
equidistant.

## Verification
Duel playtest on asymmetric cover (wall with one end adjoining more
geometry): the swing should step to the side where the wall actually ends;
`swing-peek: side X step→(...) verdict L/R` log line should show the
winning side's destination in Exposed/open ground.

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` — `TryStartSwing`, `SwingSideScore`
- `Assets/Scripts/Sim/CoverField.cs` — `TryZoneEdge` (shadow-rim metric, still
  consumed as the safe-transit boundary)
- `docs/known_issues/runtime/SWING_STEP_OUT_CORNER_CLIP.md` — the wedge that
  motivated measuring the corridor itself
