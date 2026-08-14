# Terrain Generation Design Document

**Created**: 2026-08-07  
**Status**: 🔒 LOCKED DECISIONS  
**Reference**: Gangsters: Organized Crime (1998) RE findings

---

## Design Principle: Faithful Evolution

Steel City Mob Sim evolves the Gangsters formula visually while preserving its core gameplay mechanics. Terrain generation must honor the source material's design philosophy — not invent new systems that diverge from what made the original work.

**The rule**: If Gangsters didn't have it, we don't add it unless it enhances an existing mechanic without changing the game's feel.

---

## Locked Decisions

### 1. World Navigation: 2D Plane

**Decision**: The world is a 2D navigable plane, identical to Gangsters.

- No verticality, no elevation, no multi-level terrain
- Hoods, NPCs, and vehicles navigate on a flat surface
- Visual rendering is 3D voxels, but gameplay is top-down 2D
- No hills, ridges, viaducts, or elevated roads

**Rationale**: Gangsters was 2D navigation. Adding verticality fundamentally changes pathfinding, line-of-sight, and territory mechanics. Keep it simple and faithful.

---

### 2. Water and Bridges: Painted Blocks (Gangsters-Style)

**Decision**: Water is a block-level terrain type, not a physics system.

- Water blocks use a distinct voxel material (visual only)
- Water blocks are non-buildable (no buildings placed on them)
- Water blocks are impassable for hoods/NPCs (waypoint graph treats them as walls)
- Bridges are road tiles drawn across water blocks — same `FillRoadStrip` logic
- Bridges function identically to Gangsters: a chokepoint connecting two land masses
- No water physics, no currents, no swimming, no boats

**Strategic purpose**: Water creates natural chokepoints. Bridges become contested strategic assets — controlling a bridge controls access between districts. This is exactly what Gangsters did, just with better visuals.

**Implementation**:
- `city_template.json` blocks get a `"terrain": "water"` field
- `VoxelTerrainBuilder` fills water blocks with water material ID instead of ground
- `WaypointGraph` skips water blocks except where bridge road tiles exist
- Bridge road tiles are placed by the layout generator to connect land masses

---

### 3. Trolley System: Functional Transit (Main Street Seam Type)

**Decision**: Trolley tracks are embedded in **Main Street** seam types — wider roads that serve as city arteries.

- Main Street seams are ~2.8x wider than standard roads (4.5m vs 1.6m)
- Trolley tracks are drawn as dual metal rails on the main street surface
- Main streets function as roads for navigation, with trolley stop waypoints at block centers
- **Future mechanic**: Hoods can use trolley lines for faster city traversal
- Trolley tracks follow fixed routes (painted as Main Street seams in city editor)
- No trolley stations as separate buildings — stops are at road intersections

**Rationale**: Gangsters had trolley tracks visually but they were non-functional. We keep the visual fidelity and add a gameplay purpose — rapid hood deployment across the city. Historical research confirms 1920s trolley lines ran on main arteries (66-100ft wide), not side streets.

**Implementation**:
- City editor exports `"mainstreet"` seam type in hSeams/vSeams arrays
- `VoxelTerrainBuilder` paints `MAT_TROLLEY_TRACK (135)` rails on `MAT_ASPHALT (104)` base for main street seams
- `VoxelWaypointScanner` generates trolley stop nodes (cyan) at block centers along main street seams
- See `CITY_LAYOUT_PIPELINE.md` for full specification

---

### 4. Rail Infrastructure: Excluded

**Decision**: No trains, no rail stations, no rail yards.

- Gangsters did not have functional rail infrastructure
- Trolley tracks are the only rail-like feature (see above)
- No freight trains, no passenger trains, no stations
- Industrial districts use warehouses and factories (buildings), not rail infrastructure

**Rationale**: Adding trains would require pathfinding for vehicles, scheduling, and new AI systems that diverge significantly from the Gangsters formula.

---

### 5. Seam Types: Four-Tier Street Hierarchy

**Decision**: Four seam types replace the single road type, based on 1920s urban planning research.

- **Road** — standard asphalt, 1.6m wide, normal pathing
- **Alley** — cobblestone path between combined sidewalks, 1.8m wide, through-passage, concealed
- **Main Street** — wide asphalt with dual trolley tracks, 4.5m wide, fastest transit
- **Dead-End Alley** — dark red cobblestone, 1.8m wide, terminates at one side (no through-link), tactical chokepoint

**Rationale**: Historical research confirms 1920s cities had distinct street hierarchies (primary/secondary/tertiary). The city editor (`city_editor.html`) supports painting all four types. See `CITY_LAYOUT_PIPELINE.md` for full specifications and material IDs.

**Implementation**:
- City editor exports seam types in JSON v2 format
- `VoxelTerrainBuilder` paints per-seam materials (pending)
- `VoxelWaypointScanner` generates type-specific waypoints (pending)

---

### 6. District Zoning: No Rigid Zones

**Decision**: No pre-set district zones. Residential and commercial mix naturally.

- City layout mixes residential and commercial blocks, just like Gangsters
- Industrial blocks tend to cluster (warehouses near water/edge) but are not forced into zones
- The jigsaw generation approach does NOT use rigid district pieces
- Blocks are placed individually with weighted randomness based on historical patterns

**Rationale**: Gangsters had organic city layouts where a butcher shop could be next to an apartment building next to a casino. Rigid zoning would make the city feel artificial and diverge from the source material.

---

### 7. Procedural Generation: Two-Phase Jigsaw with Edge Constraints

**Decision**: Procedural generation uses a two-phase constraint-based pipeline, not rigid zones or pure WFC.

**Industry context**: Pure Wave Function Collapse (WFC) is notoriously bad at global connectivity guarantees — it makes local decisions that can isolate regions. The recommended fix (per BorisTheBrave's Tessera and Mob City's production pipeline) is to decide strategic structure first, then fill details with constraints. Our approach follows this pattern.

#### Phase 1: Strategic Geography (Blueprint)

The blueprint phase establishes the city's strategic layout on a 2D grid. It produces a lightweight, inspectable data structure (no voxel data, no GPU buffers) that can be verified before instantiation.

1. **Water placement** — Place water blocks to create geographic features (rivers, canals, waterfronts). Water blocks partition the city into land masses.
2. **Bridge placement** — Connect land masses with bridge road tiles across water. Each bridge is a strategic chokepoint.
3. **Cardinal road grid** — Lay down the base N/S/E/W road network connecting all land blocks. This guarantees base connectivity.
4. **Connectivity verification** — Flood-fill from a random land block. If any land block is unreachable, add bridges or roads until all land is connected. No isolated land masses allowed.
5. **Diagonal road insertion** — Insert 45-degree road segments that connect to existing cardinal roads at both endpoints. These create visual variety and organic city flow without breaking connectivity. Each diagonal segment must terminate at a cardinal road intersection.
6. **Trolley route placement** — Lay trolley track routes along selected roads (cardinal and/or diagonal). Trolley routes must form connected loops or lines that span the city.

**Blueprint output**: A 2D grid where each cell contains:
- `terrain`: `land` | `water`
- `road_type`: `none` | `cardinal` | `diagonal` | `bridge`
- `road_direction`: `N` | `S` | `E` | `W` | `NE` | `NW` | `SE` | `SW` (for diagonal)
- `has_trolley`: `true` | `false`
- `building_type`: assigned in Phase 2

#### Phase 2: Block Fill (Instantiation)

With the verified blueprint, fill remaining land blocks with buildings:

1. **Industrial blocks** — Higher probability near water/edges (warehouses, factories)
2. **Residential and commercial blocks** — Mixed weighting in interior (butcher, bakery, apartments, diner, etc.)
3. **Special blocks** — Police stations, gang HQs placed at strategic locations
4. **Voxel generation** — For each block, generate voxel data based on building type and terrain flags
5. **Waypoint graph generation** — Build navigation graph from all road tiles (cardinal + diagonal + bridge). Graph nodes at road intersections, edges along road segments.

**Edge constraint types** (used during Phase 1 placement):
- `road` — standard cardinal road connection (N/S/E/W)
- `diagonal_road` — 45-degree road connection (NE/NW/SE/SW)
- `water` — impassable water edge (no road connection)
- `bridge` — road over water (connects to road on opposite side)
- `trolley` — road with trolley track overlay (can be cardinal or diagonal)

**Key rules**:
- Every land block must have at least one `road`, `diagonal_road`, or `bridge` connection to ensure full navigability
- Diagonal road segments must terminate at cardinal road intersections (no floating diagonals)
- Water blocks cannot have building placements
- Blueprint must pass connectivity verification before Phase 2 begins

#### 45-Degree Road System

**Decision**: Roads can run at 45-degree angles in addition to cardinal directions, creating organic city layouts that differ from Gangsters' pure grid.

**How it works**:
- Diagonal roads are voxel chunks rotated 45° — the voxel data itself doesn't change, only the chunk transform (position + rotation)
- The mega-buffer batching approach supports arbitrary chunk transforms
- Diagonal segments connect to the cardinal road grid at intersection nodes
- The existing `WaypointGraph` system is not grid-based — it generates waypoints from road tiles, so diagonal roads are supported without new pathfinding infrastructure

**Navigation impact**:
- `WaypointGraph` already handles non-grid road placement
- Intersection nodes connect cardinal and diagonal road segments
- A* pathfinding works on the waypoint graph regardless of road angles
- Hoods and NPCs navigate diagonals the same way as cardinal roads

**Visual impact**:
- Diagonal roads break up the grid pattern, making the city feel more organic
- Intersections where diagonal meets cardinal create interesting visual landmarks
- Trolley lines can follow diagonal routes for more varied transit paths

**Constraints**:
- Diagonal segments must be at least 2 blocks long (no single-tile diagonals)
- Diagonal segments must connect to cardinal roads at both endpoints
- No diagonal-to-diagonal-only intersections (must have at least one cardinal road connection for guaranteed grid connectivity)

---

## What We Do NOT Do (Divergence Prevention)

| Feature | Decision | Reason |
|---|---|---|
| Elevation/hills | ❌ No | 2D navigation, faithful to Gangsters |
| Multiple road surfaces | ❌ No (for now) | No gameplay value, cosmetic only |
| Trains/stations | ❌ No | Not in Gangsters, adds complexity |
| Water physics | ❌ No | Gangsters used painted blocks |
| Rigid district zoning | ❌ No | Gangsters had organic mixing |
| Boats/water vehicles | ❌ No | Not in Gangsters |
| Multi-level terrain | ❌ No | 2D navigation only |

---

## What We DO Differently from Gangsters (Enhancements)

| Feature | Gangsters | Steel City | Justification |
|---|---|---|---|
| Visual rendering | 2D sprites | 3D voxel raymarching | Modern presentation |
| Water appearance | Flat blue tiles | Voxel water material with depth | Visual enhancement, same function |
| Bridge appearance | Road tiles on blue | Road tiles on water voxels | Visual enhancement, same function |
| Trolley functionality | Visual only | Functional hood transit | Enhances existing concept |
| Procedural generation | Fixed maps | Two-phase jigsaw with edge constraints | Replayability, same city feel |
| Road layout | Pure cardinal grid | Cardinal + 45-degree diagonals | Organic city flow, modern differentiation |

---

## Implementation Priority

1. **Water blocks + bridges** — terrain type flag, material swap, waypoint graph update
2. **Trolley tracks** — visual overlay on roads, layout flag
3. **Procedural jigsaw generator** — two-phase pipeline: blueprint (strategic geography + connectivity verification) then instantiation (block fill + voxel generation)
4. **45-degree roads** — diagonal road segments, chunk rotation, waypoint graph integration
5. **Terrain batching** — mega-buffer GPU optimization (enables larger procedural cities)

Terrain batching is listed last because it's a performance optimization, not a feature. The jigsaw generator should work correctly first, then batching makes it fast.

---

## 8. City Layout Structure (Manual + Gameplay Verified)

**Created**: 2026-08-07
**Sources**: Gangsters: Organized Crime manual (104 pages, extracted), countless hours of gameplay observation, Ghidra binary analysis

This section supersedes earlier assumptions about "4 equal quadrants." The original game uses a **hub-and-spoke** design, not equal squares.

### 8.1 Overall City Topology

The city is divided into **4 quadrants** (NW, NE, SW, SE) but they are NOT equal squares. The city has a **central core** that straddles the quadrant boundaries, with territories radiating outward.

```
          N
          |
     ┌────┼────┐
     │    │    │
     │ NW │ NE │  ← Gang 1 | Gang 2
     │    │    │
 W───┼════╪════┼─── E
     │    │    │
     │ SW │ SE │  ← Gang 3 | Gang 4
     │    │    │
     └────┼────┘
          |
          S

     River runs E-W through center (when present)
     Central core straddles the river/quadrant boundary
     Industrial zone on E or W side of core
```

### 8.2 River (E-W Divider)

**Observation**: The river always runs **East-West**, dividing North from South. Your direct rival is across the river (N vs S). The gangs East and West of you are also threats since the river doesn't block E-W movement.

**No-river variant**: Some seeds have **no river at all**. When this happens:
- Docks are also removed
- Industrial zone remains
- Creates a more open map with no natural N-S barrier
- All 4 gangs have direct land access to each other
- Estimated frequency: ~10-15% of games (rare but not negligible)

**Implementation**: River presence is a boolean flag determined at city generation time. When present, river runs E-W through the center row of blocks.

### 8.3 Central Core (The Prize)

**What's here**: The highest land value blocks and best commercial buildings (Hotels, Department Stores, Main Bank). All civic/municipal buildings are in the core.

**Civic buildings in core** (confirmed by manual + gameplay):
- Police Department (Police HQ) — **exactly 1**
- F.B.I. Headquarters — **exactly 1** (separate from Police Dept)
- City Hall
- Courthouse
- Bank (Main Bank)
- Hospital
- Museum
- Mail Office
- Fire Department

**Size**: Roughly 15-20% of total blocks, but 40%+ of total land value.

**Strategic significance**: Gangs don't start here — they fight to expand INTO here. Each quadrant borders a slice of the core, giving equal access to compete for high-value territory.

**Land value taper**: Land value is highest in the core and tapers outward. The ratio is approximately **2:1** — the high-value core area vs the tapering outer area. Outer edges have the lowest land value (residential, empty land).

### 8.4 Industrial Zone (The Asymmetry)

**Placement**: The industrial zone flanks the central core on either the **East or West side** (randomly determined per game). It spreads across **both N or both S quadrants** on that side.

**Contents**:
- Warehouses (special industrial — no profit, goods storage only)
- Factories/industrial businesses
- Power Plant — **always in industrial zone**
- Docks — **only when river present**, in industrial sector
- Railroad Terminal (export destination)
- Labour Exchange (recruitment site)

**Edge effects** (gameplay observation):
- A **dusting of warehouses** on the edges of the industrial zone (bleeding into adjacent territory)
- A **dusting of hotels** outside the industrial zone (on the opposite side from industrial)

**Union/Teamsters mechanic**: Industrial workers belong to unions. Union buildings deliver block votes in elections. Teamsters (illegal front) placed behind Union buildings can fix the union vote.

### 8.5 Quadrant Buildings (Per-Quadrant Distribution)

Each quadrant contains:
- **1 Church** — charity building, donate/intimidate/ignore
- **1 Newspaper** — one of four (Herald, Post, Tribune, Times), situated in a different corner
- **1 School** — municipal building
- **1 Radio Station** — municipal building
- **Gang HQ (Office)** — placed anywhere in the quadrant with a buffer from the map edge

**Church mechanics** (manual confirmed):
- **Donate**: Lieutenant takes money to church. Amount based on local squalor (poorer area = less money). Donations make areas easier to take over. Priests praise donors in weekly sermons, affecting Business Owner and Citizen opinions.
- **Intimidate**: Silences priest's opposition (standard intimidate order on a person).
- **Ignore**: Priest may denounce your criminal activity in sermons, reducing popularity.
- **Newspaper synergy**: Priests use newspapers to appeal for funds and issue thanks for donations.

**Newspaper mechanics** (manual confirmed):
- 4 newspapers, one in each corner, each affecting opinions in its immediate area
- Owning a newspaper prevents it from reporting your major purchases to other papers
- Editors influence people's opinions about each Gang Leader

### 8.6 Gang HQ Placement

**Observation**: The starting office can be **anywhere in the quadrant**, with some buffer from the map edge. Not necessarily in the far corner — just somewhere within the quadrant's territory.

**Fairness guarantee**: All 4 HQs are roughly equidistant from the central core, ensuring equal travel time to compete for high-value territory.

### 8.7 Police System (Manual Confirmed)

**Dispatch chain**: Mayor → Police Chief → Police Officers
- The Mayor allocates officers to the Police Chief each week
- The Police Chief deploys them to police the city
- Officers walk assigned beats from the **single Police HQ** in the central core

**Bribery chain**:
- **Bribe Mayor** → Mayor influences Police Chief → fewer police sent to patrol your territory
- **Bribe Police Chief** → directly reduces hostility, fewer police in your patch
- **Bribe individual officers** → case-by-case corruption
- **Employ Police** (Lawyer order) → Lawyer walks an area, finds officers on beats, offers regular weekly wage to turn a blind eye. Officers will ignore assaults/robberies but most won't ignore murder.

**FBI** (separate from police):
- Head of FBI is **incorruptible**
- FBI Agents are **incorruptible**
- FBI detects and raids **illegal businesses** (not street crime)
- Violence against FBI only makes them more determined
- FBI HQ is a valid bombing target (along with Police Dept, City Hall, Courthouse)

### 8.8 Updated Phase 1 Blueprint (Revised)

The Phase 1 blueprint from Section 7 above is revised to incorporate these findings:

1. **Determine river presence** — Random: ~85% chance river, ~15% no river
2. **Place river** (if present) — E-W through center row, dividing N/S
3. **Define central core** — Blocks adjacent to river center (or geometric center if no river). Mark as highest land value.
4. **Determine industrial side** — Random E or W. Place industrial zone adjacent to core on that side, spreading across both N or both S quadrants.
5. **Place civic buildings** — All within central core: Police HQ, FBI HQ, City Hall, Courthouse, Bank, Hospital, Museum, Mail Office, Fire Department
6. **Define 4 quadrants** — NW, NE, SW, SE radiating from core to edges
7. **Place per-quadrant buildings** — 1 church, 1 newspaper, 1 school, 1 radio station per quadrant
8. **Place industrial buildings** — Power Plant (always), Docks (if river), Railroad Terminal, Labour Exchange, warehouses, factories
9. **Place gang HQs** — 4 HQs, one per quadrant, with edge buffer, equidistant from core
10. **Land value assignment** — Core = high (10-15), tapering outward to edges (2-5). Distance-from-core + noise.
11. **Cardinal road grid** — N/S/E/W roads connecting all quadrants to core
12. **Bridge placement** (if river) — 2-4 bridges across river
13. **Connectivity verification** — Flood-fill check
14. **Diagonal roads** — Optional, for visual variety
15. **Trolley routes** — Through core, connecting quadrants

### 8.9 Complete Building Inventory

| Building | Count | Location | Type | Notes |
|----------|-------|----------|------|-------|
| Police Department | 1 | Central core | Municipal | All police dispatch from here weekly |
| FBI Headquarters | 1 | Central core | Municipal | Incorruptible, targets illegal businesses |
| City Hall | 1 | Central core | Municipal | Mayor's office, election target |
| Courthouse | 1 | Central core | Municipal | Trials, judges |
| Main Bank | 1 | Central core | Commercial | Prime site, recruitment for accountants |
| Hospital | 1 | Central core | Municipal | |
| Museum | 1 | Central core | Municipal | |
| Mail Office | 1 | Central core | Municipal | |
| Fire Department | 1 | Central core | Municipal | |
| Church | 4 | 1 per quadrant | Charity | Donate/intimidate/ignore |
| Newspaper | 4 | 1 per corner | Municipal | Herald, Post, Tribune, Times |
| School | 4 | 1 per quadrant | Municipal | |
| Radio Station | 4 | 1 per quadrant | Municipal | |
| Orphanage | 1+ | Quadrant (TBD) | Charity | Donate target |
| Power Plant | 1 | Industrial zone | Industrial | Always present |
| Docks | 0-1 | Industrial zone | Industrial | Only when river present; recruitment + export |
| Railroad Terminal | 1 | Industrial zone | Industrial | Export destination |
| Labour Exchange | 1 | Industrial zone | Industrial | Hood recruitment site |
| Warehouse | Multiple | Industrial zone + edges | Industrial | Goods storage, no profit |
| Factory/Industrial | Multiple | Industrial zone | Industrial | Union workers, block voting |
| Gang HQ (Office) | 4 | 1 per quadrant | Illegal | Starting point for hoods each week |
| Public Baths | 1+ | TBD | Municipal | Referenced in tutorial |
