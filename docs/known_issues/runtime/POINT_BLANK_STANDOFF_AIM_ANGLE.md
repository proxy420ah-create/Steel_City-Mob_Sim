# Point-Blank Standoff — Aim Angle Meaningless Within Arm's Length

**Date**: 2026-10-28
**Status**: 🟡 FIX APPLIED (convergence) / 🟡 INVESTIGATING (pocket cause) — pending playtest
**Severity**: 🟡 HIGH

## Symptoms
- Run 2026-10-28 11:44: fighters 0.6 m apart, guns crossed, cylinders 6/6, `see=1 aim=1 blend=1.00`, `fire=losing lock` for 20+ s, `err` constant 20.4 deg (Vinny) / 36.7 deg (Tan), `tgtD=0.1`.
- HUD showed `reload check - spent 0/6` on both - the STAND-hold countdown, misread as a reload loop (no reload was happening).
- Both `MOVEFAIL ... reachable 2-3` and own cell `walk=0 snap=2015`: stuck in a pocket, every face resolved to STAND.

## Root Cause
1. `convergeSince` required `LastAngularErrorDeg <= aimGateDeg` (angle between the bore and muzzle->aim point). With the aim point ~10 cm from the muzzle, a 4-7 cm miss is 20-37 deg - the trigger gate could never hold even though every shot would hit.
2. Pocket: the flood from the actor's cell reached 2-3 cells, own cell unwalkable per the field while the mover had entered it (map/mover disagreement) - exact entry mechanism not yet identified.

## Resolution
- `CombatController.hitRadiusM` (0.1 m): converged = angle gate OR miss distance at the aim point (`muzzle->point x tan(err)`) within the radius.
- `MOVEFAIL` now logs `flood` + a 7x7 `grid=` so the pocket geometry is visible next run.
- Open: root cause of the pocket (does `MoveWithCollision` allow cells the field marks unwalkable?).

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` - TickExposureFire (convergence), DiceStandResolved, CellGrid
- `docs/known_issues/runtime/STUCK_GUARD_STATE_POISONS_NEXT_LEG.md`, `EYE_SEES_MUZZLE_CANT_SHOOTABLE_GAP.md`
