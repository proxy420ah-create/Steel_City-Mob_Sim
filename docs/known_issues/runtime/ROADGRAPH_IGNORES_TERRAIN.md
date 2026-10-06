# RoadGraph Generates Links Across Open Water / Non-Drivable Corridors

**Date**: 2026-10-07
**Status**: � FIXED (playtested — river corridors suppressed, cars stay on roads)
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
Fixed 2026-10-07 (step 1 of the traffic spec): `RoadGraph.GenerateFromLayout` takes optional `hSeams`/`vSeams` grids — a corridor link is emitted only when its seam is drivable (anything but `"river"`). E-W links read `hSeams[r-1][c]`, N-S links read `vSeams[r][c-1]`; out-of-range = perimeter road. `CityMap3D.ExtractTerrainAndSeams` is the shared extractor (terrain build + `VehicleTestSpawner.BuildRoadGraph`). Suppressed corridors logged.

**Known consequence**: cars can't cross the river at all yet — bridge decks are through-cell traversals (step 4 spines), not corridors. Verify on Play: debug beam never enters the channel; `[RoadGraph] ... (N river corridors suppressed)` appears.
