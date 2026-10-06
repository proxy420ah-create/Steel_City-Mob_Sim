# Road Lanes & Traffic — Design Spec

**Status**: 📋 DESIGN — nothing here is implemented. `roadWidth` live-resize already works (see below); everything else is planned.
**Date**: 2026-10-07
**Depends on**: `RIVER_AND_BRIDGE_TILES.md` (terrain/seam vocabulary — this spec consumes it), `CITY_SCALE_ARCHITECTURE.md`
**RE reference**: `docs/core/VEHICLE_RE_REFERENCE.md` — original Gangsters driving model (5-substate: accelerate/cruise/lane-change/decelerate/stop, static road flags, blocked-crossing handler) — mine this when Phase B outgrows the slot-car model
**Motivation**: `vehicle_civilian_car_0` measured 20×16×30 voxels @ 0.05 = **1.0 m wide × 0.8 m tall × 1.5 m long** — plausible vs. Civilian1, but the stock 1.6 m road is single-lane. Two-way streets need ~3.0 m, and the graph/agent layers need lanes, directionality, and terrain conditioning before vehicles go live.

---

## What exists today (measured)

| Layer | Reality |
|---|---|
| `roadWidth` | Serialized on `CityMap3D` (1.6 m). `SetRoadWidth` → `RebuildCity()`. UI slider already live: Map Editor → ROAD & CAMERA → 0.1–6 m. Sidewalks/tiles untouched — `spacing = groundTile + roadWidth`, blocks spread evenly. |
| `RoadGraph` | Uniform intersection lattice, one larger than the block grid. Nodes at road centerlines (block corners). **Bidirectional links, no lanes, no directionality, no road class.** |
| `RoadGraph` terrain gap | `GenerateFromLayout` never reads `terrain` or seams → **links exist across the open river channel today**. A car can path over water. Must be fixed before lane work. |
| `VehicleAgent` | Endless random walk: `RandomNeighbor` (no immediate U-turn), `PlanAheadCount=6`. Lerps **node→node on the centerline**; snap turns at intersections; constant speed; no collision detection or occupancy. |
| `VehicleTestSpawner` | Spawns N `VoxelVehicle` parked at the intersection nearest the HQ block; **F10** toggles driving; registers a `PathDebugType.Car` beam with `PathDebugRenderer`. F9 is reserved (StressTestDiagnostics). |
| Main street terrain | `mainstreet` blocks = full 11.6 m asphalt interiors, trolley track pairs (cobble) at **±0.35 m of block centerline**, axis from `mainst` neighbor mask (NS/EW). |
| Bridge terrain | `bridge` blocks = deck at grade across the channel, promenade edges. Drivable surface = the block interior, like mainstreet. |

## Geometry foundation — road width ✅ LOCKED 3.0 m (2026-10-07)

`CityMap3D.roadWidth` is now `private const float = 3.0f` — confirmed on-screen. Two 1.4 m lanes around the 1.0 m car; main street needs no help (whole 11.6 m interior drivable).

**Free divider**: the existing cobble center stripe (±1 voxel ≈ 0.1 m, dead-center of every corridor) sits exactly between the two opposing lanes at ±0.75 m offsets — it doubles as the painted center line for dual traffic. Lane-offset agents hug either side of it; no new geometry needed.

Side effects of 1.6→3.0: spacing 13.2→14.6 m (~+10 % footprint), terrain voxels up per chunk, river geometry unchanged (only riverside streets widen — correct).

## Lane model

Right-hand traffic. All offsets measured from the corridor/block **centerline**.

### Two-lane street (regular corridors, `roadWidth` = 3.0 m)

- One lane per direction, offset **±`roadWidth`/4 = ±0.75 m** — the rendered cobble center stripe is the divider between them.
- Directed links: corridor yields two graph edges, one per direction.
- Parked-car margin lives inside the lane; no dedicated parking lane at this width.

### Four-lane main street (`mainstreet` blocks — through-cell spines)

Main streets are **blocks**, not corridors — the drivable path runs through cell interiors. Model each contiguous run of `mainst` cells as a spine:

- Spine nodes at each mainstreet cell's **edge midpoints** where it meets a drivable corridor (entry/exit), plus links chaining cell-center to cell-center along the avenue.
- Lane layout inside the block, from centerline outward:
  - **Trolley track pair at ±0.35 m** (authored terrain). Trolley lane envelope ≈ ±0.9 m.
  - **Rail buffer** ≈ ±0.6 m either side of the envelope: cars may *cross* it (at intersections and lane changes) but may not stop, park, or cruise in it.
  - **Car lanes**: two per direction flanking the buffer, centers ≈ ±1.8 m and ±4.2 m.
- Trolley has **right of way** on its track segment — it does not deviate; cars yield to the envelope when crossing.

### Bridge deck (`bridge` blocks)

Same through-cell topology as mainstreet: spine across the deck cell. Deck lanes inherit the street's two-lane model; promenade strips are not drivable. Narrow-deck option: single shared lane with an intersection-reservation at each approach (see below).

## Graph extensions

`RoadGraph.GenerateFromLayout(layout, spacing)` → needs the same conditioning inputs `VoxelTerrainBuilder` already receives:

```csharp
GenerateFromLayout(layout, spacing,
    Dictionary<Vector2Int, string> terrainByCell,
    Dictionary<Vector2Int, string> hSeam, vSeam)   // row-wrapped seam grids
```

1. **Link conditioning** — emit a corridor link only where a drivable seam exists: `road`, `mainstreet`, `bridge` seams → yes; `river` → no; land–oob → edge road only if wanted. *Fixes cars driving on water.*
2. **Directed links** — each drivable corridor/segment becomes two `RoadLink`s (one per direction) with `laneOffset` = ±roadWidth/4 applied at agent runtime (`pos + right·offset`), not baked into node position (intersections stay shared).
3. **Link class** — `Street | MainStreet | BridgeDeck`, from seam/terrain type. Carries lane count, speed limit, and trolley-conflict flags; lets agents and the debug beam color differently.
4. **Through-cell nodes** — for each `mainstreet`/`bridge` cell, emit spine nodes and connect to the corridor lattice where its edges meet drivable seams. Corner mainstreet cells (avenue bend) get a curved intra-cell link.

## Agent behaviors

Ordered by what's needed for the 2-car test vs. the full system:

**Phase A — needed for the F10 two-car test:**
- **Lane offset**: agents drive `lerp(from,to,t) + right·laneOffset`. Right-hand side per direction. Opposing traffic naturally separates — no more centerline head-ons.
- **Intersection reservation**: nodes get a single-occupant reservation (timestamp + holder). An agent claims the next node before entering; on conflict, first-come wins, loser waits at the stop line. Prevents intersection collisions without full traffic rules.
- **Car following**: raycast/sphere-check along the lane for a vehicle within `gap ≈ 1.2 m`; hold speed of the leader or stop. Prevents same-lane rear-ending.

**Phase B — after lanes are proven:**
- Turn arcs (bezier/arc through intersection instead of lerped snap-turns).
- Speed per link class (slower over bridge deck, faster on main street).
- Route planning with intent (A→B via Dijkstra over directed graph, replacing the random walk for real actors).
- Lane change links (diagonal edges between same-direction lanes on 4-lane spines).
- Trolley actor: fixed spine route, ROW enforcement, stop schedule.

## F10 test evolution — "two cars, no crashes"

Extend `VehicleTestSpawner`:

- `vehicleCount = 2`, spawn at two *distant* intersections (e.g. HQ-nearest + far side of the river) so they meet organically.
- F10 toggles both into driving; each keeps its own `VehicleAgent` + debug beam (existing per-agent `RegisterPath` already supports multiple).
- **Acceptance criteria**:
  1. Both cars hold the right lane — parallel paths, no centerline overlap on shared streets.
  2. At a shared intersection, one car yields — no interpenetration.
  3. At least one crossing of a **bridge deck** — car stays on the deck lane, does not clip the parapet.
  4. At least one traversal of a **main street** segment — car lanes clear of the rail buffer; if a trolley is present, no conflict on the envelope.
  5. No car ever enters a `river` corridor (regression check for the terrain-conditioning fix).

## Open questions

- **Overtaking** on 2-lane streets: disallow for now (one lane per direction, no oncoming-lane passing in city driving).
- **Parking**: defer — no dedicated lanes at 3.0 m. Parked cars in the test harness remain stationary props, not lane blockers.
- **Do river test bridges support 4-lane?** No — deck stays 2-lane; the replica's 4 `bridge` cells are the same recipe.
- **Emergency/wrong-way driving**: out of scope for the test; the reservation system makes it survivable if ever needed.

## Implementation order

```
0. Road width → 3.0 m                                           ✅ DONE (const, confirmed on-screen)
1. RoadGraph terrain conditioning (links only over drivable seams)   ← correctness fix
2. Directed links + laneOffset at agent                              ← lanes exist
3. Intersection reservation + car following                          ← no crashes
4. Through-cell spines (mainstreet + bridge)                         ← trolley/boulevard
5. F10 two-car scenario + acceptance run
6. (later) turn arcs, speed classes, Dijkstra routing, trolley actor
```
