# Zoning Design Document — Hub & Spoke with Weighted Influence

**Created**: 2026-08-12
**Updated**: 2026-08-13
**Status**: 📐 Design — Awaiting procedural generation implementation
**Refines**: `TERRAIN_GENERATION_DESIGN.md` Section 6 ("No Rigid Zones")
**Related**: `CITY_LAYOUT_PIPELINE.md`, `TERRAIN_GENERATION_DESIGN.md`, `city_editor.html`, `zoning_sandbox.html`

---

## Overview

Steel City uses a **hub-and-spoke zoning pattern** with weighted influence zones that guide procedural generation without rigid boundaries. Zones establish centers of gravity — the Economic Core at center, Industrial on a seed-determined side — and block generation uses probability weights that blend organically at zone edges.

**Core principle**: Zones influence, they do not dictate. A block on the boundary between industrial and residential might be either. This preserves the organic city feel from the original Gangsters while providing strategic structure for gameplay.

---

## Zone Topology

```
                    N O R T H
  ┌─────────────────────────────────────┐
  │  Res   │  Res   │  Res   │  Inds   │
  │        │        │        │  (spill) │
  │────────┼────────┼────────┤         │
  │  NW    │  EC    │  NE    │  Inds   │
  │  Quad  │ (Core) │  Quad  │  (core) │
  │────────┼────────┼────────┤         │
  │ ~~~~~~~│~Bridge~│~~~~~~~~│  Inds   │
  │  Res   │  Res   │  Res   │  (spill) │
  │        │        │        │         │
  └─────────────────────────────────────┘
                    S O U T H
  (Industrial shown on East side — seed determines E or W)
```

### Zones

| Zone | Position | Description | Export Type |
|------|----------|-------------|-------------|
| **Economic Core (EC)** | Center of city, straddling river | High-density commercial — banks, department stores, main street throughfare. **Also home to civic and municipal buildings** (city hall, courthouse, police HQ, etc.) spawned procedurally. | `commercial` (high land_value) |
| **Commercial (Outer Ring)** | Ring surrounding EC, within the commercial zone band | Sparse commercial — smaller businesses, shops, offices. Lower density than EC. No civic buildings. | `commercial` (moderate land_value) |
| **Industrial** | Seed-determined East OR West side, spanning both N and S quadrants across the river | Warehouses, factories, **Docks** (export point + industrial business) | `industrial` |
| **Residential** | Outer edges of all four quadrants | Tenement blocks, apartments, neighborhood shops | `residential` |

### EC vs Commercial Distinction

The **Economic Core** and **Commercial outer ring** are both `commercial` zone type when exported. The distinction is design-level only:

- **EC (inner)**: Dense commercial with **civic/municipal building spawning rights**. High land value. Unique to the commercial zone band — only EC tiles can spawn civic buildings (city hall, courthouse, police HQ, etc.). The specific civic buildings to spawn may be specified later or derived from RE data.
- **Commercial (outer)**: Sparse commercial with standard business spawning only. Moderate land value. No civic buildings.

Both use the same `commercial` export type — the difference is encoded in `land_value` and a `is_ec` flag that gates civic building placement.

> **Mixed zones have been eliminated.** Every block is definitively one zone type (industrial, core, commercial, or residential). Dithering creates organic transitions at boundaries, but each tile has exactly one assignment.

### Gang HQ Placement

Gang HQs are placed **on residential tiles** within the four quadrants. Thanks to zone dithering, residential tiles can appear within any zone (industrial, commercial, EC) at boundary areas. The specific block within each quadrant is seed-determined. One HQ per gang, four gangs total.

---

## Weighted Influence Model

Zones are **not hard assignments**. Each block gets a probability weight for each zone type based on its distance from zone centers. Weights are used **raw** (no normalization) — if all weights are below an `ecFloor` threshold, the tile defaults to residential. This prevents the "vacuum effect" where pulling one zone back causes another to rush in.

### Dual EC Rings

The commercial zone uses **two concentric rings**:

```
weight_core(block)       = base_commercial * falloff(dist_from_ec_center, ecRadius, sharpness_com)
weight_commercial(block) = ecOuterWeight * falloff(dist_from_ec_center - ecRadius, ecOuterRadius - ecRadius, sharpness_com)
```

- **Inner ring (EC)**: High weight, small radius, high sharpness → dense core
- **Outer ring (Commercial)**: Low weight, larger radius, lower sharpness → sparse splash

### Industrial Zone (Inside-Out)

Industrial spawns **from the EC center outward** toward the map edge (not from the edge inward):

```
if dist_from_ec_sideways >= indInner:
    weight_industrial(block) = base_industrial * falloff(dist_from_ec_sideways - indInner, indDepth, sharpness_ind)
```

- **Industrial Inner Offset** (`indInner`): Gap between EC and where industrial starts
- **Industrial Depth** (`indDepth`): How far industrial extends outward
- **N/S Stretch**: Controls how tall the industrial rectangle is
- **Zone Dithering** (`indDither`): Bidirectional blend — industrial bleeds inward into EC territory, EC bleeds outward into industrial zone

### Weight Functions (Full)

```
weight_industrial(block)  = base_industrial * falloff(dist_from_ec_sideways - indInner, indDepth, sharpness_ind)
weight_core(block)        = base_commercial * falloff(dist_from_ec_center, ecRadius, sharpness_com)
weight_commercial(block)  = ecOuterWeight * falloff(dist_from_ec_center - ecRadius, ecOuterRadius - ecRadius, sharpness_com)
weight_residential(block) = base_residential * falloff(dist_from_edge, resEdge + 1, sharpness_res)
```

Where `falloff` is a configurable function (linear, gaussian, or exponential) from the zone's center of influence.

### Example

A block at the edge of the industrial zone:
- 45% industrial (warehouse/factory)
- 35% residential (tenement with ground-floor shop)
- 20% commercial (small business)

A block in the deep residential outer edge:
- 80% residential
- 15% commercial
- 5% industrial (rare outlier — a small workshop)

A block in the Economic Core:
- 70% commercial (high-value)
- 20% residential (upscale apartments)
- 10% industrial (unlikely but possible)

### Seed Determinism

- **Industrial side**: Seed picks East or West (50/50)
- **Gang HQ blocks**: Seed picks one block per quadrant
- **Rail line position**: Seed determines N-S column position (can be on either side, cuts through any zones in its path)
- **Railroad Terminal**: Seed picks a block **on the rail line** — classified as municipal but placement is rail-dependent
- **Docks**: Placed in industrial zone (industrial business, also serves as export point)

---

## Industrial Zone Details

- Spans **both north and south quadrants** on its chosen side (E or W)
- **Crosses the river** — industrial blocks on both sides of the water
- Contains the **Docks** — an industrial business that also serves as an export point and recruitment site
- The **Railroad Terminal** is a municipal building but is placed **on the rail line**, which may or may not pass through the industrial zone
- Industrial density is highest near the inner offset (close to EC), thinning outward toward the map edge (inside-out falloff)
- **Zone Dithering** (`indDither`): Controls bidirectional blend at the EC/industrial boundary — industrial bleeds inward, EC bleeds outward. At 0 = hard edge, at 1 = full mix.

---

## Rail Line & Railroad Terminal Integration

The elevated rail line and Railroad Terminal are **connected** — the terminal sits on the rail line. The rail line is cosmetic but its position determines where the terminal (a functional export point) is placed.

### Rail Line
- **Direction**: Always N-S
- **Position**: Seed-determined column — not locked to industrial side. The rail line cuts through whatever blocks are in its path
- **Runs the full height** of the city map
- **Train**: Cosmetic only — 3 evenly-spaced passes per working week, no gameplay effect
- **Blocks under rail line**: Always **residential** (tenement blocks). The original game forces residential zoning under the rail line to keep things simple — the rail line needs clear plots underneath, and tenement blocks provide the simplest building model for this. Main street rail crossings and regular road crossings are handled separately.

### Railroad Terminal
- **Classification**: Municipal building (Group 6 in original game data — same category as City Hall, Courthouse, Hospital, Police HQ)
- **Placement**: On the rail line, at a seed-determined position along its N-S run. **Must spawn outside the Economic Core** — the EC is reserved for other civic buildings (City Hall, Courthouse, Hospital, etc.)
- **Function**: Export destination point — trucks deliver goods here for export outside the city
- **Economic properties**: No profit, no stock, no produce — purely a destination (confirmed from `Economics.xtx`)
- **Cannot be bought or owned** by gangs (municipal property)

### Docks (Second Export Point)
- **Classification**: Industrial business (Group 1 in original game data)
- **Placement**: In the industrial zone
- **Function**: Export destination point + industrial business + recruitment site (triple function)
- **Economic properties**: Profit Group 9 (very high), set-up cost $25,000, union workers (Service & Dockers)
- **Can be bought and owned** by gangs

### Source Data
- Railroad Terminal: `Economics.xtx` line 260 — Municipal, PG=0, CC=0 (random), no contents
- Docks: `Economics.xtx` line 179 — Industrial, PG=9, CC=64, 5 present, Contents=Cars
- Both confirmed as export points in game manual: "Exporting goods involves trucks moving the goods from the warehouse to either the Docks or the Railroad Terminal"

---

## Relationship to TERRAIN_GENERATION_DESIGN.md Section 6

**Section 6 original text**: "No pre-set district zones. Residential and commercial mix naturally."

**This document refines that**: Zones still aren't rigid. The hub-and-spoke pattern adds **weighted influence centers** that guide procedural generation. The "no rigid zones" principle is preserved — boundaries spill, blocks on edges can be either type. What changes is that generation is no longer uniform-random across the whole city; it's spatially weighted by zone proximity.

**The existing `addZoningToLayout()` in `city_editor.html`** currently does hard assignment (`blockTypes[r][c] = 'industrial'`). This is acceptable for the editor's visual preview but the procedural generation pipeline should use the weighted model described here.

---

## Implementation Notes

### City Editor (`city_editor.html`)

- Zone visualization uses hard assignment for clarity (visual preview)
- Rail line position is seed-determined (N-S column), not tied to industrial zone
- **Blocks under rail line are forced to residential** (tenement blocks) — matches original game
- Railroad Terminal marker should appear on the rail line, **outside the EC**
- Docks marker should appear in industrial zone
- Export JSON should include zone metadata for Unity consumption

### Procedural Generation (Unity side)

- Phase 1 blueprint: Determine industrial side from seed, place EC at center
- Phase 2 block fill: Use weighted probability per block based on zone proximity
- Gang HQ: Place one per quadrant, seed-determined block
- Rail line: N-S at seed-determined column (cuts through any zones in path)
- **Rail column forces residential** zoning on all blocks it passes through
- Railroad Terminal: Place on rail line at seed-determined position, **must be outside EC**
- Docks: Place in industrial zone (industrial business + export point)

### Export Format Addition

```json
{
  "zoning": {
    "pattern": "hub-and-spoke",
    "economicCore": { "centerRow": 15, "centerCol": 15, "radius": 3, "civicBuildings": true },
    "commercialRing": { "outerRadius": 6, "outerWeight": 0.20 },
    "industrial": { "side": "east", "innerOffset": 3, "depth": 4, "spansRiver": true },
    "railLine": { "direction": "N-S", "col": 22, "trainRunsPerWeek": 3 },
    "railroadTerminal": { "type": "municipal", "onRailLine": true, "row": 8, "col": 22 },
    "docks": { "type": "industrial", "inIndustrialZone": true, "row": 10, "col": 26 },
    "gangHQ": [
      { "gang": 0, "quadrant": "NW", "row": 5, "col": 3 },
      { "gang": 1, "quadrant": "NE", "row": 5, "col": 27 },
      { "gang": 2, "quadrant": "SW", "row": 27, "col": 3 },
      { "gang": 3, "quadrant": "SE", "row": 27, "col": 27 }
    ]
  },
  "blocks": [
    { "id": "blk_0_0", "row": 0, "col": 0, "zone": "residential", "landValue": 2, "isEC": false },
    { "id": "blk_15_15", "row": 15, "col": 15, "zone": "commercial", "landValue": 8, "isEC": true }
  ]
}
```

**Zone export types**: `industrial`, `commercial` (includes both EC and outer ring), `residential`

**`isEC` flag**: Marks tiles within the Economic Core inner ring. Only `isEC: true` tiles are eligible for civic/municipal building spawning.

**Civic/Municipal buildings** (from `Economics.xtx` Group 6 — 14 types): City Hall, Courthouse, Employment Exchange, FBI Headquarters, Fire Department, Hospital, Museum, Police Headquarters, Power Plant, Public Baths, Railroad Terminal*, School, US Post, Water Plant. (*Railroad Terminal is municipal but placed on the rail line, not in EC.)

**`landValue`**: Encodes the EC vs Commercial distinction — EC tiles get high land_value (7-10), commercial outer ring gets moderate (3-6). The game engine uses `land_value` to drive business income (`economy.py: lv_modifier = 1.0 + land_value * 0.1`).

---

## City Generation Pipeline

The city is built in **three sequential phases**. Both tools use the same 32×32 grid (1024 tiles, ~800 buildable after removing roads/river/OOB) — no extrapolation or upscaling is needed. The grids map 1:1.

### Phase 1: Macro Structure (32×32)

**Tool**: `city_editor.html` — replica map + seam editor
**Output**: 32×32 tile grid with physical infrastructure

- Tile types: `block`, `mainst`, `river`, `bridge`, `oob`
- Seam types between blocks: `road`, `alley`, `mainstreet`, `deadend`, `bridge`
- River runs E-W through center (~row 15) with S-bend variants
- Main streets: N-S cols [0, 5, 11, 16, 21, 27, 31], E-W rows [0, 8, 16, 24, 31]
- Bridges at cols [5, 11, 16, 21, 27]
- Each block = 3×3 building plots with center courtyard
- **No buildings, no zones, no civic placement yet** — pure physical skeleton

### Phase 2: Zoning + Civic Placement (zoning_sandbox.html)

**Tool**: `zoning_sandbox.html` — weighted influence zones + civic building seeding
**Input**: Phase 1 macro tiles (block tiles only) — 32×32 grid maps 1:1
**Output**: Zone type + civic/charity/warehouse placements for every buildable block

- Hub-and-spoke pattern: EC at center (~98 blocks, 7×14 area), industrial on seed-determined side
- Zone types: `industrial`, `commercial` (EC inner + outer ring), `residential`
- Rail line: seed-determined N-S column, forces residential on blocks it crosses
- Railroad Terminal: placed on rail line, outside EC
- Docks: placed in industrial zone
- Gang HQs: one per quadrant
- **Civic/municipal buildings** placed directly in sandbox using fixed schema:
  - EC blocks (~98): City Hall, Courthouse, FBI HQ, Police HQ, Museum, Employment Exchange, US Post
  - Industrial blocks: Power Plant, Water Plant
  - Residential blocks: Schools (×3), Fire Depts (×2), Public Baths, US Post (×2)
  - Rail line: Railroad Terminal (already placed, outside EC)
- **Charity buildings**: Churches (×4), Orphanage (×1) in residential areas
- **Warehouses** (12 fixed): Spread across industrial/commercial edges
- All civic/charity/warehouse placements exported as part of the zoning JSON

### Phase 3: Building Seeding (Unity or export pipeline)

**Input**: Phase 2 zoning JSON (zones + civic placements) + `Economics.txt` game data (CC, NP, group)
**Output**: Every block populated with actual businesses

Now that exact block counts per zone are known from the 32×32 grid, the 171 business types from `Economics.txt` are distributed using:

- **CC (City Capacity)**: Max number of that business type across the city
- **NP (Number Present)**: Starting count (0 = random placement)
- **Group**: Business category (0=Commercial, 1=Industrial, 3=Residential, 4=Warehouse, 5=Charity, 6=Municipal, 7=Interactive Residential)

**Seeding order** (civic/charity/warehouses already placed in Phase 2, then zone businesses):

1. ~~Municipal buildings~~ → **Already placed in Phase 2** (zoning sandbox)
2. ~~Charity buildings~~ → **Already placed in Phase 2** (zoning sandbox)
3. ~~Warehouses~~ → **Already placed in Phase 2** (zoning sandbox)
4. **Industrial businesses** (Group 1, 33 types): Distributed across industrial zone blocks using CC as weights
5. **Commercial businesses** (Group 0, 117 types): Distributed across commercial + EC blocks (minus civic-occupied) using CC as weights
6. **Residential buildings** (Group 3): Tenement blocks on all residential blocks (including rail line blocks)
7. **Interactive residential** (Group 7): Special residential types (e.g., brothels, gambling dens) seeded into residential blocks

**Why civic placement happens in Phase 2**: Since both grids are 32×32 (1:1 mapping, no extrapolation needed), civic/municipal/charity/warehouse placement can happen directly in the zoning sandbox alongside zone assignment. This keeps all spatial planning in one tool and exports a single comprehensive JSON. Phase 3 only needs to fill in the remaining business types per zone.

**EC target**: ~98 blocks (7×14 area) form the Economic Core. This is where civic buildings (City Hall, Courthouse, FBI HQ, Police HQ, Museum, etc.) are placed. With 14 municipal types and ~98 EC blocks, most EC blocks will be commercial with civic buildings seeded at key positions.

---

## Open Questions

1. ~~**Falloff function**: Linear vs gaussian for zone influence~~ → **Resolved**: All three supported (linear, gaussian, exponential) with per-zone sharpness tuning in sandbox
2. ~~**EC size**: Should it scale with grid size?~~ → **Resolved**: Tunable via `ecRadius` slider in sandbox
3. ~~**Industrial width**: How many columns deep?~~ → **Resolved**: Tunable via `indInner` (offset from EC) + `indDepth` (outward extent) sliders
4. **Gang HQ minimum distance**: Should gang HQs have a minimum distance from each other and from the EC?
5. ~~**Civic building list**: What specific civic/municipal buildings spawn in EC tiles?~~ → **Resolved**: 14 municipal buildings from `Economics.xtx` Group 6 (see above). Railroad Terminal is municipal but placed on rail line, not in EC.
6. **Commercial outer ring tuning**: What `ecOuterWeight` and `ecOuterRadius` values produce the best "sparse splash" effect for the target city aesthetic?
