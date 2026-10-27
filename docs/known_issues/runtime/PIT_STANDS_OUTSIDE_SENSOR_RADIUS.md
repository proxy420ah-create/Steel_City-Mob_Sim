# PIT_STANDS_OUTSIDE_SENSOR_RADIUS — START DUEL never sought cover

**Date**: 2026-08-19 (reported) → fixed same day
**Status**: 🟢 FIXED (two stacked causes, both landed)
**Severity**: 🔴 CRITICAL — silently disabled the duel's core flow

## Symptoms
- SETUP PIT → START DUEL (Fight): both fighters stand at their plates and
  fire at each other — nobody moves to cover.
- No seek leg, no crouch, no cover beats; they look like static turrets.

## Root Cause
`CoverSensor.radius` was shrunk `8f → 4f` in the measured-silhouette rework
(commit a431425). The arena's authored spawn plates (`cover_stand_a` z=2.45m,
`cover_stand_b` z=16.65m) sit ~6.4m from the nearest cover rows (z=8.9 /
z=10.3). At radius 4 the `EnterCombat` scan sees ZERO walls → `PickBest`
fails → `TryPickCover` false → the `EnterCombat` fallback path:
`HoldPosition` + `targetDuck=1` + armed → standing open firing.

The bare duel had therefore NEVER sought cover — every earlier successful
seek rode `TryPickCoverNear(anchor)` via the deep/mid/crouch/wall pick row
(anchor-pinned, radius-independent) or scripted slots. `LastResult` said
"no cover within 4m — holding position" the whole time.

## Fix — TWO stacked causes

1. `CoverSensor.radius` 4f → 8f (the last value at which the duel actually
   sought — authored stand↔cover gap ≈ 6.4m fits inside ±8m).
2. **`BuildRoute` flooded unwalkable open ground** (found via the new
   DUEL-status row: verdict `no corridor`, `dst` 0): `CoverField.groundY`
   is only probed under stand-BAND cells — every other cell keeps
   `Array.Clear`'s 0 (street level). `CellWalkable`/`CorridorFree` then
   sampled the body column ~0.06m above STREET on the 0.2m-raised deck —
   inside the slab → VolumeSolid → every unpainted cell read solid. The
   flood was confined to ~180 painted cells and could never reach across
   6.4m of open deck. `CellGroundPoint` now lazily probes unprobed cells
   (down-raycast, same 1.6/3.2 contract as the field's own ProbeFloor),
   caches the hit back into `groundY`, and records misses in
   `legNoGround` (ungrounded cells = unwalkable, never corridor). Both
   sets clear per BuildRoute.

## Verification
- Duel start: `🥊 picked X` logs + both fighters walk to painted stands.
- If `no corridor → leg ruled unreachable` appears instead, that's the
  flood — separate verdict, now reachable for the first time.

## Related Files
- `Assets/Scripts/Sim/CoverSensor.cs` — `radius` field (~L28)
- `Assets/Scripts/Sim/CombatController.cs` — EnterCombat pick (~L740),
  HoldPosition fallback (~L758)
- `Assets/StreamingAssets/voxel_buildings/cover_arena.stasset` — anchors
