# RoadGraph Generates Links Across Open Water / Non-Drivable Corridors

**Date**: 2026-10-07
**Status**: 🔴 ACTIVE
**Severity**: 🟡 HIGH (correctness; only visible once vehicles drive near river/oob edges)

## Symptoms
- `RoadGraph.GenerateFromLayout` builds a uniform intersection lattice for the whole grid — it never reads `layout.blocks[].terrain` or the seam grids.
- Vehicles can path across `river` corridors (open water channel) and into `oob`/non-drivable areas.
- Reproduce: activate the river layout (`--river-row 6 --bridge-cols 3,7 --activate`), spawn the F10 test car, watch its `PathDebugType.Car` beam — links exist across the channel, not just over the two bridges.

## Root Cause
`RoadGraph` was written before the terrain system existed and takes only `(layout, spacing)`. The terrain-conditioned data (`terrainByCell`, `hSeamRows`/`vSeamRows`) already flows into `VoxelTerrainBuilder` — the road graph just never consumes it.

## Investigation Notes
- `VehicleAgent` random-walks `RandomNeighbor` with only a U-turn guard — it will happily pick a link over water.
- Waypoint graph already solved the same problem (`GenerateFromLayout` is terrain-conditioned) — same pattern applies.
- Design + fix plan captured in `docs/systems/ROAD_LANES_AND_TRAFFIC.md` (graph extensions, step 1 of implementation order).

## Related Files
- `Assets/Scripts/Sim/RoadGraph.cs` — `GenerateFromLayout` lattice
- `Assets/Scripts/Sim/VehicleTestSpawner.cs` — `BuildRoadGraph` call site
- `Assets/Scripts/UI/CityMap3D.cs` — builds `terrainByCell`/seam maps (source data exists)
- `docs/systems/ROAD_LANES_AND_TRAFFIC.md` — design spec

## Resolution
Planned: emit corridor links only where drivable seams exist (`road`/`mainstreet`/`bridge`), classify link type, add through-cell spines for `mainstreet`/`bridge` blocks. Tracked as step 1 of the traffic spec — not yet fixed.
