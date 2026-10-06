# River & Bridge Tiles — M2 Design Spec

**Status**: 🟢 PLAYTESTED (straight-river v1) — 10×10 river row verified in-scene: continuous channel, bridges with water beneath, clean console (no unsupported-mask warning), ped `BridgeDeck` routing confirmed (Vinny crossed). Waypoint lanes now voxel-measured (see RECENT_CHANGES). Remaining: authored corner tile → corner recipe → full 32×32 replica run.
**Date**: 2026-10-05
**Milestone**: M2 of `CITY_SCALE_ARCHITECTURE.md` (city is proven at 32×32; this adds the river + bridges)
**Depends on**: `CITY_LAYOUT_PIPELINE.md` (pending tasks 1–2: seam types, water/bridge blocks), `CITY_SCALE_ARCHITECTURE.md` (sector invariants, O4 dedupe interaction)

---

## What the editor already models (source of truth)

`city_editor.html` v3 export emits everything needed:

- **`blocks[].terrain`**: `'land' | 'water' | 'mainstreet' | 'bridge' | 'oob'`
- **`hSeams[r][c]` / `vSeams[r][c]`**: per-corridor type — `'road' | 'mainstreet' | 'bridge' | 'river'`
- `parameters`, `groundTileSize`, `spacing`, `railLine`
- The **Gangsters replica** (`REPLICA1_DATA`, v4 tiles): 642 `block` / 220 `mainst` / **34 `river`** / **4 `bridge`** / 124 `oob` cells on a 32×32 grid.

Two river widths exist in the model: **block-scale** (whole 11.6 m cells = water — the replica's river) and **seam-scale** (a 'river' seam = 1.6 m canal in the road corridor). This spec implements block-scale first; seam-scale river is deferred (it needs variable seam handling anyway).

## The "smart foundation" decision

Terrain is currently a 2-voxel pancake (~0.1 m thick) — the black void under the map edge is visible in screenshots. A *uniform* deep foundation at 0.05 voxel size multiplies 143M terrain voxels by ~11× → ~6.3 GB. Not viable.

**Instead: depth only where the player can see it — the river channel.**

- Street grade stays at y=0 (top of the 2-voxel slab). Foundation extends **downward** — every building anchor, road, and sidewalk is untouched.
- **Water tiles** carve a real channel: stone bed, water fill, quay walls.
- **Land tiles adjacent to water** get a full-depth edge wall on the water-facing side (the quay face) — no other change.
- Since the only below-grade space is the channel and every channel-facing edge gets a wall, **no view angle exposes the thin pancake**. The illusion is airtight.
- `worldOffset.y` is already arbitrary → docks, boat assets, and below-grade municipal buildings need **zero pipeline changes** later.

## Tile ownership (verified in `VoxelTerrainBuilder`)

Each terrain chunk covers **block interior (11.6 m) + half-road on all 4 sides** (13.2 m footprint, 264×264×2 @ 0.05). Consequences:

- A **water–water seam**: each water tile carves its half of the corridor → the channel is continuous automatically.
- A **water–land seam**: stays a road — this is the *riverside street* seen in the editor's map. The water tile must **not** carve that corridor half.
- ⇒ Water tiles are **neighbor-aware**: carve a corridor half only where the neighbor is also water. 4-bit edge mask → the tile recipe reads adjacent block terrain.

## Recipes (terrain builder)

| `terrain` | Recipe |
|---|---|
| `land` | current — ground tile (sidewalk ring + stone) + road seams |
| `water` | carved channel: bed at −1.5 m, `MAT_WATER` fill to ~0.2 m below grade; quay walls on channel-facing edges; corridor halves carved where neighbor is water; riverside sidewalk strip along land-facing edges |
| `bridge` | deck at grade (road material across the channel width) + `MAT_WEATHERED_WOOD`/`MAT_DARK_WOOD` rails; optional support piers to the bed |
| `oob` | plain ground, no sidewalk ring; **no buildings** |
| `mainstreet` | deferred (wider seam + `MAT_TROLLEY_TRACK`) |

## The neighbor mask — census-verified against the replica

Per water/bridge tile, compute `mask = N|E|S|W` (4 bits: neighbor is channel = water|bridge). The actual replica river (34 water + 4 bridge cells) contains **only**:

| Mask | Recipe | Count in replica |
|---|---|---|
| `.E.W` (10) | **straight E-W** | 17 |
| `N.S.` (5) | **straight N-S** | 4 |
| `.ES.` / `..SW` / `NE..` / `N..W` | **corner ×4 rotations** | 9 |
| `.E..` / `...W` | **end** (only vs. `oob`/map edge) | 2 |
| inline river cells | **bridge** (deck over channel) | 4 |

**No caps mid-city, no isolated ponds, no T, no cross, no interior** — the river is a 1-block-wide flow with turns that enters/exits at the map edge. Recipe set: `straight ×2 rot + corner ×4 rot + end + bridge`. Corner lands when the authored corner JSON exists; until then unsupported masks land-fill + warn (never silent).

`mainstreet` (220 tiles — canonical block-wide boulevards: rows 5/12/21/26, cols 6/11/16/26) is its own block recipe, not a wide seam.

## Bridge

A `bridge` cell in the replica is a **block-scale tile** replacing a water cell in the channel line:

- Road deck at grade spanning the tile, continuing the road corridor through it
- Wood rails at the deck edges (2–3 voxels high)
- Optional piers: 2–3 stone columns from bed to deck underside (period look, ~free)
- Water continues beneath — tile carves the channel *except* the deck strip

## Materials

| ID | Name | Notes |
|---|---|---|
| 101 | Stone | bed + quay walls |
| 102 | Concrete | riverside sidewalks |
| 104 | Asphalt | bridge deck / riverside roads |
| 108 | Weathered Wood | bridge rails |
| **137** | **Water** | **NEW** — proposed `MobColors[137] ≈ (0.10, 0.25, 0.45)`; requires `MaterialCount`/`MaxMaterials` bump to ≥138 (color buffer is sized `MaxMaterials`; arrays are already 256, packing is 9-bit). |

## Layout schema extension

`city_layout.json` blocks gain `"terrain": "land" | "water" | "oob" | "bridge" | "mainstreet"`. Layout optionally gains `hSeams`/`vSeams` grids for future seam-type work. `generate_city_layout.py` stamps terrain from the replica footprint (or an explicit river spec).

- Water/oob/bridge blocks get **no building slots** → ~158 blocks × 9 lots ≈ 1,400 fewer instances.
- `VoxelTerrainBuilder.GeneratePerBlockTerrain` reads terrain per block + neighbor mask per edge.
- `VoxelCollisionWorld`: water bed + quay walls register for collision normally; water surface material should be non-walkable (waypoint scanning is a later milestone — do not silently treat water as ground).

## Navigation — terrain-conditioned `WaypointGraph`

### The invariants that make this exact

River/bridge/oob tiles are **not special shapes** — they occupy the same 11.6 m block interior at the same 13.2 m spacing, grade stays at y=0, and walk surfaces land on the same edge lanes as every land block. Consequence: `WaypointGraph.GenerateFromLayout`'s fixed node positions remain geometrically valid on every terrain type — a river block's W/E promenade lane **is** the standard sidewalk lane position (0.5 m inside the tile edge, verified against the authored prototype: lane voxel ≈5 inside the walkable strip x∈[0,7]; ≈110 inside x∈[108,115]). Corner nodes (~0.5 m in from both edges) sit exactly at the promenade lane ends.

So sidewalk waypoint logic stays consistent **by design, not by convention**: voxel data and nav data derive from the same `terrain` field at the same block anchor — there is no authored-path data to drift out of sync. Any seeded/procedural layout generator that stamps `terrain` gets correct navigation for free — orientation is encoded by the neighbor mask, not by per-tile hand-tuning.

### Per-terrain node emission (in `GenerateFromLayout`)

| `terrain` | Nodes emitted | Internal links |
|---|---|---|
| `land` | unchanged: c0–c3 + m0–m3 | perimeter loop (unchanged) |
| `water` (N–S river) | c0–c3 + **m1, m3 only** (E/W promenade lanes; N/S edges are channel → no mid) | c0↔m3↔c3 (W lane N–S), c1↔m1↔c2 (E lane N–S) |
| `water` (E–W river) | c0–c3 + **m0, m2 only** | c0↔m0↔c1, c3↔m2↔c2 (N/S lanes E–W) |
| `bridge` | all 8 | deck lane carries the crossing direction (mid↔mid through the deck); promenade lanes optional under-bridge |
| `oob` | **none** | none — `FindNearestNode` never resolves there → unwalkable free |

River flow axis comes from the neighbor mask (N+S water ⇒ channel N–S ⇒ promenades on W/E; E+W water ⇒ channel E–W ⇒ promenades on N/S).

### Seam-aware inter-block pass

The adjacency pass (mid↔mid + corner↔corner links across each shared edge) checks both blocks' terrain:

- **water↔water along the channel** (perpendicular to flow): promenade continuation — corner links stay (they're already positioned on the lanes); no mid links.
- **water↔water across the channel** (parallel to flow — the channel itself): **no links** — cannot walk across water.
- **land↔water**: no crosswalk links (the seam road is a riverside street; peds cross onto the *land* block's sidewalk side only — land-side links emit as normal since they're within the land block's own graph).
- **bridge edges**: mid↔mid across the water seam get a new `WaypointType.BridgeDeck` — path-cost identical to a crosswalk, but the type marks the crossing for AI/heat-map use (bridges are chokepoints).
- New types: `Promenade` and `BridgeDeck` join the enum; `SidewalkMid`/`SidewalkCorner`/`CrosswalkCorner` semantics unchanged.

### Lane position detail

The standard mid lane sits 0.5 m inside the tile edge (voxel ≈5 on a promenade whose walkable strip is 0–7 → center 3.5). The lane therefore hugs the parapet side slightly off visual center. Options: keep the standard lane (zero special-casing) or offset `m1`/`m3` on water blocks to the promenade centerline. Default: keep standard — corner-to-mid links jog ~1.5 voxels, invisible at gameplay distance; revisit only if it reads badly in the walk preview.

### Worked example — extortion across the river

`Vinny: HQ → target across river` resolves through plain A*: land sidewalk nodes → promenade corner/mid chain along the water blocks → `BridgeDeck` links across a bridge block → far-side promenade → land sidewalks → destination. No river-specific pathfinding logic; water interior emits no nodes so nothing routes through the channel. `pathCache` keys are block pairs — unchanged.

### Non-goals here

Editor hand-drawn waypoints (voxel_editor 🚩 Path) are a *preview aid only* — they validate promenade readability and never serialize into gameplay data. Nav data is generated, always.

## Prototype (authored, this milestone)

`Tools/generate_river_tile.py` → `VoxelAssetStudio/JSON Models In Progress/river_straight.json`:

- dims **116 × 64 × 116**, `voxelSize 0.1` → 11.6 m × 11.6 m block interior, 6.4 m total height
- Grade slab at y 22–23 (2 voxels, matching terrain's 2-voxel surface); y 24–63 is **empty headroom** above grade — authoring room for quay-side decorations (fences, lamps, signage) painted on the sidewalk; tunable via `--headroom`
- Channel x ∈ [10, 105]: stone bed at y 4–5, water fill y 6–19 (**1.4 m deep**), air gap to grade
- Quay walls: x = 10 and x = 105, full height
- Flanking sidewalks: x ∈ [0,9] and x ∈ [106,115] at grade (1.0 m — same as `sidewalkWidth`)
- Ends open — channel runs to the tile boundary so neighbors continue the flow
- Materials array carries the water entry so it previews correctly in `voxel_editor.html`

This is the **visual spec** the procedural recipe must match — and it can serve as a real `.stasset` if we later prefer authored tiles over procedural recipes.

## Perf notes

- River tiles add ~2–5M voxels total (~34 cells × walls/bed/water) — invisible vs. 143M existing.
- Deeper AABBs on ~34 river tiles → slightly more DDA steps locally; negligible at that count.
- **O4 (terrain dedupe)**: the neighbor-mask recipe makes water tiles content-addressable — dedupe them like everything else *after* the seam set is final.
- Sector invariants still apply — water tiles live in the same banded terrain sectors.

## Open questions

1. Water surface: opaque blue voxel (137) vs. a real transparent/animated water shader later? Voxel first — cheapest, composites correctly.
2. Bridge as block-scale tile (replica model) vs. seam-scale crossing (a road seam bridging a 1-block channel)? Replica uses block-scale — start there.
3. Boats/docks: out of scope for M2 but the below-grade convention enables them — flag any choice that would block placing assets under y=0.

## Test plan

1. Load `river_straight.json` in `voxel_editor.html` — inspect channel depth, quay walls, sidewalk flanks.
2. Extend `generate_city_layout.py` to stamp the replica's 34 river + 4 bridge cells as `terrain` (and oob ring).
3. `VoxelTerrainBuilder` water/quay/bridge recipes + neighbor mask; materials bump.
4. Unity run: river renders carved with quay walls; bridges read as decks at grade; roads still riverside streets; no buildings on water/oob; collision registers bed+walls; `buildmap_log.txt` sector counts + voxel totals still under invariants.
