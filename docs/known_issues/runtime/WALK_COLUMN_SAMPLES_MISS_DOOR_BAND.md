# Walk Column Samples Miss Vehicle Door Band

**Date**: October 21, 2026
**Status**: 🟢 FIX APPLIED — pending playtest
**Severity**: 🟡 HIGH — body can occupy solid geometry; routes/stands can be painted inside vehicles

## Symptoms
- During an otherwise-successful orbit flank, the actor clipped deep into an open-top car — legs inside the cabin cavity, torso above the sill.
- Root mechanism: every "is this spot free" check sampled a **two-point center column** — feet at `+0.06m` and chest/eye at `0.6h` (~1.08m).
- A car's door/sill band spans ~0.2–1.0m — *between* the samples. Feet read the air gap beneath the floor pan (vehicle ground clearance), chest cleared the door top → both returned free while the entire body volume intersected the car.

## Affected checks (all shared the two-point shape)
- `CombatController.MoveWithCollision.Free` — per-step walk gate
- `CombatController.CorridorFree` — route string-pull straightness + `CellWalkable` flood predicate
- `CoverField` stand validity probe (`standPt+0.15` + `eye`) — could paint stands inside cabin cavities

## Root Cause
Point-sampled clearance assumes obstacles occupy either the low or high band. Thin-shell objects with ground clearance (vehicles, tables, kiosks) present a solid mid-band with air above and below — invisible to a two-point column.

## Fix
Added a mid-band sample to all three predicates:
- `Free` / `CorridorFree`: waist at `dims.y * 0.35` (~0.63m — inside every car door band)
- `CoverField` stand check: `standPt + cellTop * 0.5` — scaled to the required pose height so deep-crouch stands under legitimate overhangs aren't over-rejected

Still point-based (cheap); the column is now sampled low/mid/high. Edge clipping at body corners (shoulder brushing a fender) remains unhandled by design — center-column only.

## Related Files
- `Assets/Scripts/Sim/CombatController.cs` — `MoveWithCollision`, `CorridorFree`, `CellWalkable`
- `Assets/Scripts/Sim/CoverField.cs` — stand validity probe (~line 280)
- `Assets/Scripts/Sim/VoxelCollisionWorld.cs` — `SamplePoint`
