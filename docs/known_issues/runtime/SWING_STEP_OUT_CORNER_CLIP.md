# Swing Step-Out Bypassed the Map Corridor — Corner Clip Grind

**Date**: 2026-10-22
**Status**: 🟡 FIX APPLIED (awaiting playtest — second pass 2026-10-23)
**Severity**: 🟡 HIGH

## Symptoms
- Mid-fight, Vinny parked at the valley wall's end face "trying to go around
  this corner but not able to correctly path around the wall" (playtest
  screenshots: body against the wall end, green route arrow, red stand
  marker).
- Console: repeated `swing-p0` FaceTrack yaw entries + `fire gate: stowed
  (vis 0 · cone off … Swing/InCover)` flips and `peek — zero contact,
  stepping back` — a step-out that never reaches the corner, dwells
  nowhere, and returns.
- `return stalled Xm off centroid` readout when the nudge budget burned.

## Root Cause
A gap in the unified-field collapse: only *routed* legs (`StepRoute` via
`BuildRoute` → `ChainToSeed` → string-pull) consumed the goal-seeded
`DistMap`. Three micro-moves still ran **bare straight lines**:

| Move | Old path |
|---|---|
| Swing step-out (`swing-p0`) | `StepToward(swingDest)` — dest cell checked `Walkable ∧ meMap.Reaches`, but the *path* to it was never walked |
| Swing return (`swing-p2`) | `StepToward(CoverPoint)` — straight line |
| Duck off-spot return (`return`) | `StepToward(CoverPoint)` — straight line |

Reachability was verified as a verdict, not consumed as a route: the map
said the step-out was reachable *via a path around the wall end*, and the
swing walked straight at the point — clipping the wall's end column.
`MoveWithCollision` axis-slid along the face, the stuck-nudge budget
(±0.28m perpendicular hops ×3) spent itself against a wall it could never
round, and the "leg done" report left him mid-wall dwelling a peek from
nowhere.

## Fix — `StepRouted(dest, who, ref built)`
All point moves now materialize through the same `BuildRoute` + `StepRoute`
the seek legs use — one truth, one materializer:

- `StepRoute(who, finalDest)` parameterized (was hardwired to `CoverPoint`).
- `StepRouted` builds the corridor once per leg (`*Built` latches reset
  where each leg's step-guard resets) and steps `legRoute`; on open ground
  the string-pull yields the same straight line as before — it only
  deviates when a corner genuinely blocks, which is exactly when deviation
  is needed.
- Rewired: `swing-p0` (latch reset in `TryStartSwing`), `swing-p2` (latch
  at the dwell→phase-2 transition), `return` (latch at `returnWalked`).
- Bare-line fallback kept for the cases the map legitimately can't serve
  (routeLegs off, out-of-field dests) — logged once as
  `no corridor to step-dest (routeFail) — bare line` instead of silently
  grinding.

## Second pass — 2026-10-23 (recurrence: convex-vertex shuttle)
The routed fix worked for the *path*, but two deeper defects kept the same
symptom alive at convex corners:

1. **`CorridorFree` sampled at 0.12m** — the string-pull certified legs by
   sweeping the skinned square every 12cm. A convex corner *vertex* bites
   into the corridor inside a narrower window than the spacing → a
   certified leg the mover's per-frame (~3cm) steps still clipped → wedge
   mid-vertex. Now samples at **0.03m** (mover-step resolution): a
   certified leg is literally step-by-step executable.
2. **Nudge budget reset on ANY body motion** — `swingStuckPos`/
   `stuckNudges` reset whenever the body moved >0.14m. A ±perp nudge at
   the vertex *moves* him sideways → full reset → re-wedge → the
   indefinite `swing-p0` yaw-flap + shot-in-back loop. Stuck is now
   measured by **goal progress** (`wpDist < stuckBest − 5cm`); a sideways
   shuttle burns the 3-nudge budget → honest `leg aborted` verdict.

Plus the destination half: all four stand-commit sites now snap through
`CertifiedStand` → `NearestWalkable` (ring-2) — committing to flood-
certified ground instead of painted zone centroids that averaged
uncertified cells (the `cover unreachable (0.20m out)` verdicts).

## Verification
Duel playtest: step-outs at a wall END face should route around through
floor cells (the HUD `legRoute` polyline will show the micro-corridor);
`swing-p0` yaw spam and `return stalled` readouts should go silent; peeks
should land AT the corner and produce real contact/zero-contact verdicts.
A leg that still can't execute aborts in ~2.4s with an explicit
`leg aborted` line instead of an indefinite shuttle.

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` — `StepRouted`, `StepRoute(who, dest)`, swing/Duck call sites
- `Assets/Scripts/Sim/CoverField.cs` — `DistMap`, `ChainToSeed` (consumed, unchanged)
- `docs/known_issues/runtime/ROUTE_CROSSING_CENTER_WALL_MIDFIGHT.md` — the pick-side sibling (unreachable *destinations*; this one is unrouted *paths*)
