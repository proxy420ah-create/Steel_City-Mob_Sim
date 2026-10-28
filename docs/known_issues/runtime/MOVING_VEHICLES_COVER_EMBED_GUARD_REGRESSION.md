# Moving Vehicles Used As Cover; Bodies Embedded In Cars; Guard Reset Regression

**Date**: 2026-10-28
**Status**: 🟡 FIX APPLIED (pending playtest)
**Severity**: 🔴 CRITICAL

## Symptoms
- F10 test cars (driving) qualified as cover; Vinny picked a stand beside a moving car for a reload, ended up inside the car volume and never got out (`rchk=1 leg=1`, own cell `walk=0`, flood 1 cell) for 40+ s.
- A diagnosed leg that should have aborted in 1.8 s never did.

## Root Cause
1. `SolidVolumeSet.QueryCoverWalls` emitted walls for every volume including moving vehicles; zones/stands on a car are stale within a second.
2. Collision is one-way: bodies cannot enter a car volume, but a re-registered (moving) car volume can end up over a standing body - every neighbour is then blocked, so the actor flood serves ~1 cell and no leg can start.
3. The map had no notion of where a moving car is GOING - the 0.4 s field rebuild only sees the car where it is now.
4. (My regression) resetting the stuck guard on a stale-stamp re-route: the field rebuilds every 0.4 s, so the guard never reached its 0.6 s window and a wedged leg never aborted.

## Resolution
- Guard resets only on a new leg (`StepRouted` `!built`).
- `TickUnembed`: after 0.4 s embedded, slip to the nearest free square (`TryNearestFreeSquare`), log `UNEMBED`.
- Moving vehicles excluded from cover (`ExcludeFromCover` hook at scan, `StandReachable` at pick).
- `CoverField.dynBlock`: swept footprints of moving cars painted every scan, priced 12x in the flood, rejected as stands/steps.
- Open: cars never yield to characters (traffic brake on a character in the swept path).

## Related Files
- `Assets/Scripts/Sim/VoxelVehicle.cs` - motion tracking, IsVolumeMoving, PaintMovingFootprints
- `Assets/Scripts/Sim/SolidVolumeSet.cs` - ExcludeFromCover
- `Assets/Scripts/Sim/CoverField.cs` - dynBlock + BuildDistMap pricing
- `Assets/Scripts/Sim/CoverSensor.cs` - paint after Build
- `Assets/Scripts/Sim/CombatController.cs` - StandReachable, TickUnembed, StepRouted
- `docs/known_issues/runtime/STUCK_GUARD_STATE_POISONS_NEXT_LEG.md` - the original guard fix this regressed

## CORRECTION (2026-10-28, same day)
The "moving car re-registered over a standing body" explanation above was an assumption and does NOT match the incident (user: Vinny took cover on a parked car and clipped inside). Measured from `vehicle_490_touring.stasset` (0.7x0.75x1.55 m at 0.01 vox): the cabin/tub floor is 10-15 cm high - inside `stepUpMax` 0.15 - so `TryBodySquare` legally steps a body into the open tub (body-column samples hit air), while the map's ground probe (deck+1.6 m) reads the canopy (`walk=0`, flood 1 cell): a map/mover disagreement, and `SquareFree` is TRUE there so the embed check could not fire.
**Fix**: vehicle volumes are solid for BODIES across the whole yawed volume (`VoxelCollisionWorld.ColumnFreeDetail` -> `BodySolid`: `VolumeOpen` + `veh_` id), so tub/floor/running-board occupancy and step-up are impossible and cabin stands are unwalkable; `TickUnembed` also escapes SEALED POCKETS (flood < 12 cells, 1.0 s) via a breadcrumb. Bullets/LOS unchanged. The moving-car cover exclusion and swept-footprint mask remain valid for the F10 traffic case.
