# City Layout Pipeline — Design Tool to Unity Implementation

**Created**: 2026-08-12
**Status**: ✅ Design Tool COMPLETE (Unity Integration Pending)
**Updated**: 2026-08-13
**Related**: `TERRAIN_GENERATION_DESIGN.md`, `3D_CITY_RENDERING.md`, `CityMap3D.cs`, `VoxelAssetStudio/city_editor.html`, `ZONING_DESIGN.md`, `VoxelAssetStudio/zoning_sandbox.html`

---

## Overview

The City Layout Pipeline is a three-phase workflow in `city_editor.html` for designing and implementing real city layouts in Steel City Mob Sim:

### Phase 1 — Macro (Gangsters Map)
Loads the original Gangsters: Organized Crime 32×32 macro map (embedded `replica1_data.js`). Paint terrain types: water, land, OOB, bridges, main streets. Macro tile painter with undo support.

### Phase 2 — Granular (Zones + Alleys + Block Layout)
Converts macro tiles → seam/block layout. Computes distance-field zones (core, commercial, industrial, residential) via `computeZonesFromReplica()`. Generates alleys (25% per commercial/core block, excludes full-block buildings). Renders block-level infrastructure: sidewalks, alleys (continuous 3-lane strips: debris | path | debris), elevated rail line at seed-determined `railCol` (avoids EC), plot subdivision lines. Live color picker for all 41 materials.

### Phase 3 — Buildings (Civic + Municipal + Footprints)
Purely cosmetic building placement layer. Renders 3×3 building footprints per block (buildings + courtyards, skips alley plots). Places emoji overlays for all special buildings: civic, industrial, warehouses, media, churches/schools, gang HQs, railroad terminal, docks. Train animation: enters from north, stops at terminal station, exits south off-map, loops.

### Unity Runtime (Pending)
Import the exported JSON (physical layout + zones + civic placements), build voxel terrain with per-seam materials, generate waypoint graphs from material IDs, spawn buildings, and generate dynamic obstacles/cover.

See `ZONING_DESIGN.md` → "City Generation Pipeline" for zoning algorithm details.

The design tool is **complete and functional**. The Unity runtime integration is **pending**.

---

## Design Tool: `city_editor.html`

**Location**: `VoxelAssetStudio/city_editor.html`
**Tech**: Three.js (orthographic top-down), HTML5 controls
**Nav**: Available from the VoxelAssetStudio editor nav bar

### Features (Implemented)

- **Top-down CAD view** with pan, zoom, zoom-to-fit
- **Real game proportions** sourced from `CityMap3D.cs` defaults
- **32×32 grid** from embedded Gangsters macro map (replica1_data.js)
- **5 seam types** paintable between blocks:
  - **Road** — standard asphalt, 1.6m wide
  - **Alley** — cobblestone path between combined sidewalks, 1.8m wide, through-passage
  - **Main Street** — wide dark asphalt with dual trolley tracks, 4.5m wide
  - **Dead-End Alley** — dark red cobblestone path, 1.8m wide, terminates at one side (no through-link)
  - **Bridge** — wooden crossing over water, with rails, road-width
- **Block terrain types**: `land`, `water` (river), `oob` (out of bounds), `mainstreet`, `bridge`
- **Zone types** (phase 2): `core`, `commercial`, `industrial`, `residential`, `civic` — distance-field computed from `CityGen1.js` parameters
- **Alley system** (phase 2, block-layer infrastructure):
  - 25% chance per commercial/core block (seeded RNG)
  - Full row (h) or column (v) of empty land through 3×3 grid center
  - Three-lane layout: debris (dark, cover) | path (zone color, walkable) | debris
  - Center plot marked as purchasable empty land (courtyard color)
  - Excludes blocks with municipal/civic/church/school/warehouse/media/terminal/docks buildings
  - Rendered as continuous strip at block level (not per-plot)
- **Elevated rail line** (phase 2):
  - Seed-determined N-S column (`railCol`), avoids EC (at least `ecR + 1` from center)
  - Track bed, dual rails, ties, support pillars
  - Train animation: enters from north off-map → stops at terminal station → continues south off-map → loops
  - Falls back to east edge when no zoning data
- **Building footprints** (phase 3): 3×3 grid per block — buildings, courtyards, skips alley plots
- **Emoji overlays** (phase 3): civic, industrial, warehouses, media, churches/schools, gang HQs, terminal, docks
- **Live color picker**: 41 materials with `var` declarations for `window[matName]` access — all colors editable in real-time
- **LOD system**: 3 levels (full detail < 144 blocks, no buildings < 576, minimal ≥ 576)
- **Performance**: `computeCityData()` / `renderCity()` split, shared geometry cache, `rebuildDisplay()` for display-only toggles
- **Parameter sliders**: voxelSize, buildingVoxelWidth, buildingsPerBlockRow, sidewalkWidth, roadWidth, alleyWidth, mainStreetWidth
- **Waypoint visualization**: color-coded nodes and links per seam type
- **JSON export/import**: version 3 format with seamTypes, blockTypes, zone types, rail line info, and all parameters
- **Macro tile painter** with undo support (50-step undo stack)

### Auto-Generated Layout (`generateLayeredLayout`)

The default layout on page load follows 1920s urban planning principles:

| Seam Position | Direction | Type | Rationale |
|---|---|---|---|
| Center column | N-S | Main Street | Trolley line through city center |
| East column | N-S | Alley | Through alley for service access |
| Center row | E-W | Main Street | Trolley cross-street, forms intersection |
| South row | E-W | Dead-End | Tactical chokepoint / ambush location |
| All others | — | Road | Standard residential roads |

This creates a **trolley cross intersection** at the city center with service alleys and tactical dead-ends.

### JSON Export Format (v3)

```json
{
  "version": 3,
  "parameters": {
    "voxelSize": 0.05,
    "buildingVoxelWidth": 64,
    "buildingsPerBlockRow": 3,
    "sidewalkWidth": 1.0,
    "roadWidth": 1.6,
    "alleyWidth": 1.8,
    "mainStreetWidth": 4.5,
    "gridSize": 32
  },
  "groundTileSize": 11.6,
  "spacing": 13.2,
  "gridSize": 32,
  "seamTypes": ["road", "alley", "mainstreet", "deadend", "bridge"],
  "blockTypes": ["land", "water", "mainstreet", "bridge", "oob"],
  "hSeams": [["road", "bridge", "road", ...], ...],
  "vSeams": [["mainstreet", "road", ...], ...],
  "railLine": {
    "enabled": true,
    "direction": "N-S",
    "position": "col-23",
    "railCol": 23,
    "trainRunsPerWeek": 3
  },
  "blocks": [
    { "row": 0, "col": 0, "block_id": "r0c0", "terrain": "land" },
    { "row": 15, "col": 0, "block_id": "r15c0", "terrain": "water" },
    ...
  ]
}
```

**Rail line data**: `railCol` is the seed-determined column index (avoids EC). `position` is a human-readable string. `trainRunsPerWeek` is a gameplay parameter.

**Seam array indexing**:
- `hSeams[row][col]` — horizontal seam between block[row][col] and block[row+1][col]
- `vSeams[row][col]` — vertical seam between block[row][col] and block[row][col+1]

**Block terrain**:
- `terrain: "land"` — normal block with buildings, sidewalk, waypoints
- `terrain: "water"` — water block (river), no buildings, no waypoints, impassable except via bridge seams

---

## Seam Type Specifications

### Road
- **Width**: 1.6m (configurable)
- **Visual**: Standard dark asphalt (`MAT_ASPHALT=104`)
- **Waypoints**: Standard sidewalk corner + mid-edge nodes, through-links across seam
- **Tactical**: Normal exposure, standard pathing speed
- **Material ID**: `MAT_ASPHALT (104)`

### Alley (Through)
- **Width**: 1.8m (configurable)
- **Visual**: Combined sidewalk (same as block sidewalk) with narrow cobblestone path down center
- **Waypoints**: Alley-specific node on cobblestone path center, linked to both adjacent blocks
- **Tactical**: Concealed, slower movement, cover opportunities
- **Material ID**: `MAT_ALLEY (133)` for path, `MAT_SIDEWALK (102)` for surrounding sidewalk

### Main Street
- **Width**: 4.5m (configurable, 2.0-8.0m range)
- **Visual**: Wide dark asphalt with two trolley track lines (metal-colored) offset from center
- **Waypoints**: Trolley stop nodes (cyan) at each block center along seam, through-links
- **Tactical**: Most exposed, fastest transit, trolley stops as special nodes
- **Material IDs**: `MAT_ASPHALT (104)` base, `MAT_TROLLEY_TRACK (135, proposed)` for rails
- **Historical basis**: 1920s main streets were 66-100ft wide to accommodate trolleys + traffic

### Dead-End Alley
- **Width**: 1.8m (same as alley)
- **Visual**: Dark red cobblestone path (distinct from through-alley)
- **Waypoints**: Terminal node (red) connected from one side only — **no through-link**
- **Tactical**: Natural chokepoint, ambush risk, trap location for fleeing characters
- **Material ID**: `MAT_ALLEY (133)` with dead-end flag, or `MAT_OBSTACLE (134)` at terminus
- **Historical basis**: Common in 1920s cities for service access (garbage, loading bays)

### Bridge
- **Width**: Same as road (1.6m configurable)
- **Visual**: Wooden brown surface with thin rail lines on each side
- **Waypoints**: Through-links only — bridges are pure transit, no stop nodes
- **Tactical**: Critical chokepoint — only crossing over water, controls access between land masses
- **Material IDs**: `MAT_WOOD (10)` for bridge surface, `MAT_DURASTEEL (5)` for rails (proposed)
- **RE basis**: Gangsters uses 2-4 bridges across the E-W river. Bridges are the only N-S crossing points when river is present.

### Water Block (River)
- **Block type**: `water` (replaces `land`)
- **Visual**: Blue surface with edge lines for definition
- **Buildings**: None — water blocks are non-buildable
- **Waypoints**: None — water is impassable for hoods/NPCs
- **Tactical**: Natural barrier dividing the city. Only crossable via bridge seams.
- **Material ID**: `MAT_WATER (proposed 137)` — voxel water material with depth
- **RE basis**: River runs E-W through center ~85% of games, divides N from S. ~15% no river variant. Docks only present when river exists.

---

## Unity Integration — Pending Tasks

### 1. CityLayout: Accept Variable Seam Types/Widths

**Current state**: `CityMap3D.cs` uses a single `roadWidth` for all seams. The city layout JSON is loaded but seam types are not consumed.

**Required changes**:
- Parse `seamTypes` array from exported JSON
- Store per-seam type and width in the `CityLayout` data structure
- Replace uniform `roadWidth` with per-seam width lookup
- Expose seam type to `VoxelTerrainBuilder` for material selection

**Key files**:
- `Assets/Scripts/UI/CityMap3D.cs` — main city builder
- `Assets/StreamingAssets/city_layout.json` — runtime layout file
- `Assets/Scripts/Sim/City.cs` — city state

**Effort**: Medium (1 session)

### 2. VoxelTerrainBuilder: Paint Seam-Specific Materials

**Current state**: All seams painted as `MAT_ASPHALT (104)`. No alley or trolley track materials.

**Required changes**:
- For `alley` seams: paint center path with `MAT_ALLEY (133)`, surrounding with `MAT_SIDEWALK (102)`
- For `mainstreet` seams: paint base with `MAT_ASPHALT (104)`, overlay trolley tracks with `MAT_TROLLEY_TRACK (135)`
- For `deadend` seams: paint path with `MAT_ALLEY (133)`, add `MAT_OBSTACLE (134)` wall at terminus
- Seam width must match the exported `mainStreetWidth` / `alleyWidth` parameters

**Proposed new material IDs**:
- `MAT_TROLLEY_TRACK = 135` — metal rail surface for trolley lines
- `MAT_DEADEND_MARKER = 136` — visual marker for dead-end alley terminus (optional)
- `MAT_WATER = 137` — water surface for river/canal blocks
- `MAT_BRIDGE = 138` — wooden bridge surface (or reuse `MAT_WOOD = 10`)

**Key files**:
- `Assets/Scripts/UI/VoxelTerrainBuilder.cs` (or equivalent terrain painting code)
- `Assets/Scripts/UI/CityMap3D.cs` — seam iteration

**Effort**: Medium (1 session)

### 3. VoxelWaypointScanner: Replace Math-Based WaypointGraph

**Current state**: `WaypointGraph.cs` generates waypoints mathematically — uniform grid of sidewalk corners and crosswalk links. No awareness of seam types, alleys, or dead-ends.

**Required changes**:
- New `VoxelWaypointScanner` that reads material IDs from the voxel terrain
- Scan terrain voxels: `MAT_SIDEWALK (102)` → sidewalk waypoint, `MAT_ALLEY (133)` → alley waypoint, `MAT_CROSSWALK (130)` → crosswalk link, `MAT_TROLLEY_TRACK (135)` → trolley stop node
- Dead-end detection: if alley path terminates (no adjacent `MAT_ALLEY` voxel), create terminal waypoint with no outgoing link
- Generate links based on material adjacency rather than mathematical grid positions
- Cover objects (`MAT_COVER_CRATE=131`, `MAT_COVER_CAR=132`) generate cover waypoints

**Waypoint types**:
| Type | Material Source | Color (editor) | Links |
|---|---|---|---|
| Sidewalk | MAT_SIDEWALK (102) | Gold | Perimeter + cross-seam |
| Crosswalk | MAT_CROSSWALK (130) | — | Links across roads |
| Alley | MAT_ALLEY (133) | Orange | Through-passage |
| Trolley Stop | MAT_TROLLEY_TRACK (135) | Cyan | Through-passage, fast transit |
| Dead-End | MAT_ALLEY + no adjacent | Red | One-sided, terminal |
| Cover | MAT_COVER_CRATE/CAR (131/132) | — | Adjacent sidewalk links |

**Key files**:
- `Assets/Scripts/Sim/WaypointGraph.cs` — current math-based graph
- New: `Assets/Scripts/Sim/VoxelWaypointScanner.cs`
- `Assets/Scripts/Sim/Pathfinder.cs` — consumer of waypoint graph

**Effort**: Large (2-3 sessions)

### 4. Dynamic Obstacle + Cover Systems

**Current state**: No cover or obstacle system. Combat uses `COMBAT_AUTOBATTLE.md` rules but environment factors are not data-driven.

**Required changes**:
- Cover objects placed in city editor (future feature) or spawned at runtime
- Material IDs flag cover: `MAT_COVER_CRATE (131)`, `MAT_COVER_CAR (132)`
- Cover provides damage reduction in auto-battle resolution
- Obstacles (`MAT_OBSTACLE=134`) block movement and line-of-sight
- Dead-end alley terminus acts as natural obstacle + cover

**Key files**:
- `Assets/Scripts/Sim/CombatResolver.cs` (or equivalent)
- `Assets/Scripts/Sim/Pathfinder.cs` — obstacle avoidance
- `VoxelAssetStudio/city_editor.html` — future cover placement tool

**Effort**: Medium-Large (2 sessions)

---

## Implementation Priority

| Priority | Task | Effort | Unblocks |
|---|---|---|---|
| 1 | CityLayout: variable seam types/widths | Medium | Tasks 2, 3, 4 |
| 2 | VoxelTerrainBuilder: seam materials | Medium | Task 3 (scanner needs materials to scan) |
| 3 | VoxelWaypointScanner | Large | Pathfinding, AI navigation, combat |
| 4 | Dynamic obstacle + cover | Medium-Large | Combat tactics, emergent gameplay |

Tasks 1 and 2 can be done in parallel. Task 3 depends on both. Task 4 depends on 3.

---

## Research Basis (1920s Urban Planning)

Design decisions for seam types are grounded in historical research:

- **Main streets** were significantly wider (66-100ft) to accommodate trolley lines + automobile traffic. Trolley companies fought for main artery placement, not side streets.
- **Through alleys** connected two main streets, prioritized pedestrian movement and commercial activity (Melbourne Chinatown model).
- **Dead-end alleys** were service-only, used for loading bays and back gates. Low pedestrian traffic, often occupied by garbage bins. Created natural chokepoints.
- **Street hierarchy** (primary → secondary → tertiary) is confirmed by both urban planning literature and procedural city generation research (Parish & Müller, Citygen).
- **Trolley lines** ran continuously along main streets, not broken into segments. Multiple car lines could converge on a single main street at transfer points.

---

## File Reference

| File | Role |
|---|---|
| `VoxelAssetStudio/city_editor.html` | Design tool — 3-phase pipeline (complete, ~3600 lines) |
| `VoxelAssetStudio/CityGenResources/CityGen1.js` | Embedded zoning parameters (seed, EC radius, zone widths) |
| `VoxelAssetStudio/replica1_data.js` | Embedded Gangsters 32×32 macro map data |
| `VoxelAssetStudio/zoning_sandbox.html` | Standalone zoning prototype (reference) |
| `docs/alley_seam_preview.html` | Static visualization (reference) |
| `Assets/Scripts/UI/CityMap3D.cs` | Unity city builder (needs seam type support) |
| `Assets/Scripts/Sim/WaypointGraph.cs` | Current waypoint system (to be replaced) |
| `Assets/Scripts/Sim/Pathfinder.cs` | A* pathfinding (consumer) |
| `Assets/StreamingAssets/city_layout.json` | Runtime layout data |
| `docs/core/TERRAIN_GENERATION_DESIGN.md` | Terrain design (trolley, roads — to be updated) |
| `docs/core/ZONING_DESIGN.md` | Zoning algorithm design (distance-field, EC, rail line) |
| `docs/core/UI_DEVELOPMENT_PITFALLS.md` | UI pitfalls including file:// CORS warnings |
