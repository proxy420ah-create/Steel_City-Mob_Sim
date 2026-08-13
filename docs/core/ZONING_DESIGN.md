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
| **Industrial** | Seed-determined East OR West side, spanning both N and S quadrants across the river | Warehouses, factories, Railroad Terminal, rail line | `industrial` |
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
- **Railroad Terminal**: Seed picks one industrial block near the rail line
- **Rail line position**: Runs N-S along the outer edge of the industrial zone side

---

## Industrial Zone Details

- Spans **both north and south quadrants** on its chosen side (E or W)
- **Crosses the river** — industrial blocks on both sides of the water
- Contains the **Railroad Terminal** building (export destination, cosmetic anchor)
- The **elevated rail line** runs N-S along the outer edge of the industrial zone
- Industrial density is highest near the inner offset (close to EC), thinning outward toward the map edge (inside-out falloff)
- **Zone Dithering** (`indDither`): Controls bidirectional blend at the EC/industrial boundary — industrial bleeds inward, EC bleeds outward. At 0 = hard edge, at 1 = full mix.

---

## Rail Line Integration

The elevated rail line (cosmetic, 3 runs per working week) is positioned based on the industrial zone:

- **Direction**: Always N-S
- **Position**: Outer edge of the industrial zone (East edge if industrial is E, West edge if industrial is W)
- **Runs the full height** of the city map
- **Train**: Cosmetic only — 3 evenly-spaced passes per working week, no gameplay effect

---

## Relationship to TERRAIN_GENERATION_DESIGN.md Section 6

**Section 6 original text**: "No pre-set district zones. Residential and commercial mix naturally."

**This document refines that**: Zones still aren't rigid. The hub-and-spoke pattern adds **weighted influence centers** that guide procedural generation. The "no rigid zones" principle is preserved — boundaries spill, blocks on edges can be either type. What changes is that generation is no longer uniform-random across the whole city; it's spatially weighted by zone proximity.

**The existing `addZoningToLayout()` in `city_editor.html`** currently does hard assignment (`blockTypes[r][c] = 'industrial'`). This is acceptable for the editor's visual preview but the procedural generation pipeline should use the weighted model described here.

---

## Implementation Notes

### City Editor (`city_editor.html`)

- Zone visualization uses hard assignment for clarity (visual preview)
- Rail line position should be calculated from industrial zone blocks, not raw city edge
- Export JSON should include zone metadata for Unity consumption

### Procedural Generation (Unity side)

- Phase 1 blueprint: Determine industrial side from seed, place EC at center
- Phase 2 block fill: Use weighted probability per block based on zone proximity
- Gang HQ: Place one per quadrant, seed-determined block
- Railroad Terminal: Place in industrial zone near rail line
- Rail line: N-S along industrial zone outer edge

### Export Format Addition

```json
{
  "zoning": {
    "pattern": "hub-and-spoke",
    "economicCore": { "centerRow": 15, "centerCol": 15, "radius": 3, "civicBuildings": true },
    "commercialRing": { "outerRadius": 6, "outerWeight": 0.20 },
    "industrial": { "side": "east", "innerOffset": 3, "depth": 4, "spansRiver": true },
    "railLine": { "direction": "N-S", "position": "east-edge", "trainRunsPerWeek": 3 },
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

**`isEC` flag**: Marks tiles within the Economic Core inner ring. Only `isEC: true` tiles are eligible for civic/municipal building spawning. The specific civic buildings (city hall, courthouse, police HQ, etc.) may be specified later or derived from RE data.

**`landValue`**: Encodes the EC vs Commercial distinction — EC tiles get high land_value (7-10), commercial outer ring gets moderate (3-6). The game engine uses `land_value` to drive business income (`economy.py: lv_modifier = 1.0 + land_value * 0.1`).

---

## Open Questions

1. ~~**Falloff function**: Linear vs gaussian for zone influence~~ → **Resolved**: All three supported (linear, gaussian, exponential) with per-zone sharpness tuning in sandbox
2. ~~**EC size**: Should it scale with grid size?~~ → **Resolved**: Tunable via `ecRadius` slider in sandbox
3. ~~**Industrial width**: How many columns deep?~~ → **Resolved**: Tunable via `indInner` (offset from EC) + `indDepth` (outward extent) sliders
4. **Gang HQ minimum distance**: Should gang HQs have a minimum distance from each other and from the EC?
5. **Civic building list**: What specific civic/municipal buildings spawn in EC tiles? (May be specified later or derived from RE data)
6. **Commercial outer ring tuning**: What `ecOuterWeight` and `ecOuterRadius` values produce the best "sparse splash" effect for the target city aesthetic?
