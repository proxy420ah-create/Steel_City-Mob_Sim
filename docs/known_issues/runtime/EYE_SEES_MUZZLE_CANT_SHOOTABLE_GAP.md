# Eye Sees, Muzzle Can't — Aware != Shootable

**Date**: 2026-10-28
**Status**: 🟡 FIX APPLIED (safety net; structural fix specced in AIM_RIG.md) — pending playtest
**Severity**: 🔴 CRITICAL

## Symptoms
- Auto dice duel: Vinny parked 14+ s with `see=1`, `fire=bore buried`, `e2e=BLOCKED`, `vis=2/80` while every move was refused (see DICE_TURN_BASED_SCAFFOLDING_VS_REALTIME.md).
- Sees-can't-shoot recurred through the authored path too (SEES_CANT_SHOOT_LIVELOCK.md).

## Root Cause
1. `ArmAimSolver.Solve` aims the bore DIRECTION along shoulder -> target; the muzzle position is a consequence of the arm, not constrained to the eye. From `Civilian1.json`: gun-side shoulder is 12.5 cm lateral / 10.5 cm below / 5.5 cm behind the eye; revolver muzzle is ~25 cm ahead of the grip and 6.75 cm up -> muzzle ~4 cm below the eye (modeled by `sightGap`) and ~12.5 cm to the gun side (modeled nowhere).
2. The exposure probe rays from the EYE only; the aim point is the centroid of eye-visible voxels. A thin eye-visible sliver is typically invisible to the muzzle.
3. `SolveRiseAtSpot` certifies stands by eye height (`eye.y - sightGap`) at the IDLE pose eye; lateral occlusion is invisible to it.
4. Moves were chosen as raw geometry points then validated after the roll (separate bug: refusals).

## Resolution (applied)
`CoverSensor` probes every sample from the muzzle too (`HasMuzzleProbe`, `VisibleSet.shootable/shootCentroid`); `CombatController.muzzleShootGate` makes the trigger, aim point and fired point use the shootable set; aware-but-unshootable feeds the sustained verdict. **Structural fix**: `docs/systems/AIM_RIG.md` (focal-point rig puts the muzzle near the eye line).

## Related Files
- `Assets/Scripts/Sim/CoverSensor.cs` - VisibleSet, TryGetVisibleVoxels
- `Assets/Scripts/Sim/CombatController.cs` - TickExposureFire, FireHold, boreBlocked verdict, SolveRiseAtSpot
- `Assets/Scripts/Sim/ArmAimSolver.cs` - Solve (shoulder -> target)
- `docs/systems/AIM_RIG.md` - spec
