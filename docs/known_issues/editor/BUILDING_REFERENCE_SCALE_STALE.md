# Building-vs-Character Reference Ratio Halved (Stale Building Preset)

**Date**: 2026-10-30 (user screenshot — Vinny read half-size next to the tenement)
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — skewed every door/window/proportion judgment in the studio

## Symptoms

- Reference preview footer showed `×0.10` for a character next to buildings.
- Characters previewed at **half** their true relative size against buildings —
  Vinny (0.62 m) looked tiny next to the tenement, making authored door/window
  proportions feel wrong in review.

## Root Cause

Same function as the earlier `REFERENCE_PREVIEW_SCALE` fix, different stale
constant. `getCharScaleRatio()` divides by the **asset-type preset's**
`voxelSize` — `ASSET_TYPE_PRESETS.building.voxelSize` was hardcoded `0.1`
(citing the pre-upscale scale doc). But `SectorBaker` forces every building
onto the **0.05 m** world grid (`info.voxelSize = 0.05 × scale`) and never
reads the stasset's declared header — so the true in-game ratio is
`0.01 char / 0.05 building = 0.20`, not 0.10.

The stale constant also fed `itemVs` in the walk-path preview — path walkers
moved at half-speed in building scenes for the same reason.

Additionally, most WIP building JSONs still declared `"voxelSize": 0.1` — pure
metadata (Unity ignores the field) but misleading.

## Resolution

- `ASSET_TYPE_PRESETS.building.voxelSize`: `0.1 → 0.05` — fixes the char
  ratio, ref walk speed, and the declared size written into future exports
  in one place.
- Corrected stale `"voxelSize": 0.1` headers → `0.05` in 8 WIP building JSONs
  (`empty_land`, `mainstreet_ew/ns`, `river_straight`, `road_tile_0`,
  `shooting_range`, `tenement_block_0`, `tenement_glass`).
- `vehicle_civilian_car_0` left at 0.1 — legacy vehicle mislabeled `building`.

## Related Files

- `VoxelAssetStudio/voxel_editor.html` — `ASSET_TYPE_PRESETS.building`, `getCharScaleRatio()`, `itemVs` walk speed
- `Assets/Scripts/UI/SectorBaker.cs` — `BuildingInfo.voxelSize = 0.05 × scale` (the render truth)
- `docs/known_issues/editor/REFERENCE_PREVIEW_SCALE.md` — prior 2× fix on the character side of the same function
