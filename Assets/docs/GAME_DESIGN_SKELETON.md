# Steel City: Mob Sim — Game Design Skeleton

**Created**: August 2, 2026
**Status**: Active — Foundational Design Document
**Derived From**: Original game constants (`Constants.xtx`), business definitions (`Economics.xtx`, `Illegal Economics.xtx`), and vertical slice implementation

---

## 1. Game Overview

Steel City: Mob Sim is a turn-based organized crime strategy game. The player runs a gang in a 1920s Prohibition-era city, competing against rival gangs for territory, income, and influence while avoiding police investigations and FBI scrutiny.

**Core Fantasy**: Build a criminal empire from a small neighborhood to controlling the entire city.

**Core Tension**: Aggressive expansion brings income but also police attention, squeal risk, and rival retaliation.

---

## 2. Core Loop

```
┌─────────────────────────────────────────────────┐
│  PLANNING PHASE (player input, no time limit)   │
│                                                   │
│  1. Inspect city (pan/zoom camera, click businesses)│
│  2. Assign orders to hoods (per-business)         │
│  3. Bribe police, manage finances                │
│  4. Review investigations, threats               │
│  5. Click Run Week                               │
│                                                   │
└──────────────────────┬──────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────┐
│  EXECUTION PHASE (animated, ~10-20 seconds)      │
│                                                   │
│  1. Extortion round — hoods collect from businesses│
│  2. Police round — investigations, arrests       │
│  3. Rival AI round — rivals expand/attack        │
│  4. Economy round — income tallied, expenses paid │
│  5. Summary — auto-switch to Event Log tab       │
│                                                   │
└──────────────────────┬──────────────────────────┘
                       │
                       ▼
                 Back to Planning
```

---

## 3. City Scale

### 3.1 Original Game Reference

Derived from `constants.json` (originally `Constants.xtx`):

| Metric | Original Game | Evidence |
|---------|---------------|----------|
| Civilians | 2,000 | `constants.city.civilians` |
| Police | 400 | `constants.city.police` |
| FBI | 100 | `constants.city.fbi` |
| Judges | 12 | `constants.city.judges` |
| Min blocks to run for mayor | 100 | `constants.elections.min_blocks_to_enter` |
| Starting hoods | 5 | `constants.gang_start.hoods` |
| Starting money | $6,000 | `constants.gang_start.money` |

**Conclusion**: The original game had **100+ blocks**.

### 3.2 Current Vertical Slice

| Metric | Current | Notes |
|---------|---------|-------|
| Grid | 3×3 | Explicitly "vertical slice" |
| Blocks | 9 | |
| Businesses per block | 1-2 | Template uses `count: 1` or `count: 2` |
| Total businesses | ~13 | Mostly apartments |
| Business types | 11 | 7 legal + 4 illegal |
| Police | 2 | 2 beats covering all blocks |
| NPCs | ~118 | Generated from population per block |

### 3.3 Scale Roadmap

| Phase | Grid | Blocks | Businesses/Block | Total Locations | Notes |
|-------|------|--------|-------------------|-----------------|-------|
| **Phase 0** (current) | 3×3 | 9 | 1-2 | ~13 | Vertical slice — prove the systems |
| **Phase 1** | 3×3 | 9 | 9 | 81 | Fill out blocks to full density |
| **Phase 2** | 5×5 | 25 | 9 | 225 | Multiple neighborhoods |
| **Phase 3** | 10×10 | 100 | 9 | 900 | Full original game scale |

**Phase 1 is the immediate target** — same 9 blocks, but each block has a full 3×3 sub-grid of businesses.

---

## 4. Block → Business Hierarchy

### 4.1 Data Model

```
City
└── Block (neighborhood)
     ├── id, name, row, col
     ├── landValue, population
     ├── ownerGang, extortionStrength
     ├── isPlayerHq, isRivalHq, isPoliceStation
     ├── businesses[] (up to 9)
     │    └── Business
     │         ├── id, type, name
     │         ├── isIllegal
     │         ├── ownerGang
     │         ├── profitGroup, runningCostGroup
     │         ├── capacity, active
     │         └── subRow, subCol (NEW — position within block's 3×3 grid)
     └── npcs[]
```

### 4.2 Business Sub-Grid

Each block contains a 3×3 grid of business lots:

```
Block (row=R, col=C)
  ┌─────┬─────┬─────┐
  │ 0,0 │ 0,1 │ 0,2 │   subRow=0
  ├─────┼─────┼─────┤
  │ 1,0 │ 1,1 │ 1,2 │   subRow=1
  ├─────┼─────┼─────┤
  │ 2,0 │ 2,1 │ 2,2 │   subRow=2
  └─────┴─────┴─────┘
       subCol=0  1   2
```

- Each lot can hold one business or be empty
- Business position = `(block.row * 3 + subRow, block.col * 3 + subCol)` in world space
- Streets run between blocks (gaps in the grid)

### 4.3 Business Types

#### Legal Businesses

| ID | Name | Profit Group | Running Cost | Setup Cost | Capacity |
|----|------|-------------|-------------|------------|----------|
| butcher | Butcher Shop | 5 ($250) | 2 ($100) | $500 | 1 |
| bakery | Bakery | 4 ($200) | 2 ($100) | $400 | 1 |
| barber | Barbershop | 3 ($150) | 1 ($50) | $300 | 1 |
| diner | Diner | 6 ($300) | 3 ($160) | $600 | 2 |
| garage | Garage | 5 ($250) | 2 ($100) | $800 | 1 |
| apartments | Apartment Building | 7 ($400) | 4 ($140) | $1000 | 3 |
| empty_land | Empty Land | 0 ($0) | 0 ($0) | $200 | 0 |

#### Illegal Businesses

| ID | Name | Profit Group | Setup Cost | Capacity | Suspicion | Sentence | Investigation |
|----|------|-------------|------------|----------|-----------|----------|---------------|
| card_game | Card Game | 36 ($500) | $100 | 3 | 20 | 0 | 0 |
| speakeasy | Speakeasy | 39 ($1000) | $650 | 20 | 20 | 5 | 10 |
| casino | Casino | 40 ($1500) | $1100 | 6 | 20 | 10 | 15 |
| loan_shark | Loan Shark | 38 ($750) | $500 | 3 | 20 | 2 | 10 |

#### Future Business Types (Phase 2+)

Restaurant, hotel, warehouse, nightclub, drug den, protection front, smuggling operation, betting parlor, jazz club, tailor, pharmacy, jewelry store, bank, union hall, import/export, trucking company, distillery, brewery, etc.

---

## 5. Procedural Asset Pipeline

### 5.1 Voxel Asset Studio Integration

The existing `VoxelAssetStudio` (PyQt6 + OpenGL) already supports:
- `.stasset` file format (compatible with Unity `StAssetReader`/`StAssetWriter`)
- Procedural building generation (`procedural_buildings.py`)
- Procedural tileset generation (`procedural_tilesets.py`)
- Material library with voxel material IDs
- Paint/erase tools, orbit camera, file I/O

### 5.2 New 1920s Material Palette

Distinct from Steel Tide's sci-fi theme:

| ID | Name | Color (RGB) | Usage |
|----|------|-------------|-------|
| 0 | Air | Transparent | Empty space |
| 1 | Brick | (0.55, 0.27, 0.21) | Building walls |
| 2 | Wood | (0.40, 0.26, 0.13) | Doors, frames, signage |
| 3 | Concrete | (0.68, 0.68, 0.65) | Foundations, sidewalks |
| 4 | GlassAmber | (0.85, 0.65, 0.30) | Lit windows |
| 5 | GlassDark | (0.20, 0.25, 0.35) | Unlit windows |
| 6 | Roof | (0.25, 0.22, 0.20) | Rooftops |
| 7 | Asphalt | (0.12, 0.12, 0.14) | Roads |
| 8 | NeonRed | (0.90, 0.15, 0.15) | Speakeasy signs |
| 9 | NeonGreen | (0.15, 0.80, 0.30) | Gambling signs |
| 10 | Awning | (0.35, 0.45, 0.25) | Storefront awnings |
| 11 | Lamp | (0.95, 0.85, 0.60) | Street lamps |
| 12 | Cobblestone | (0.45, 0.42, 0.38) | Older roads |
| 13 | Sandstone | (0.76, 0.70, 0.55) | Upscale buildings |

### 5.3 Building Generators

Each business type gets a procedural generator function:

```python
# procedural_mob_buildings.py

def generate_restaurant(seed=None):
    """Restaurant: awning, large windows, 'EATS' sign"""
    ...

def generate_speakeasy(seed=None):
    """Speakeasy: unmarked door, barred windows, neon accent"""
    ...

def generate_casino(seed=None):
    """Casino: neon sign, curtained windows, ornate facade"""
    ...

def generate_apartments(seed=None):
    """Apartment building: multi-story, many small windows"""
    ...

def generate_diner(seed=None):
    """Diner: rounded front, large windows, counter visible"""
    ...

# ... one per business type
```

### 5.4 City Layout Pipeline

```
Python: procedural_city_gen.py
  │
  ├── Reads city_config.json (block layout, business assignments)
  ├── For each business:
  │     ├── Calls appropriate building generator
  │     ├── Exports as .stasset file (e.g., biz_restaurant_01.stasset)
  │     └── Records position, type, name in city_layout.json
  ├── Generates road tiles between blocks
  ├── Generates sidewalk tiles
  └── Outputs to Unity/Assets/StreamingAssets/city/
       ├── city_layout.json
       ├── buildings/*.stasset
       └── tiles/*.stasset

Unity: CityMap3D.cs
  │
  ├── Loads city_layout.json at runtime
  ├── For each building:
  │     ├── Loads .stasset via StAssetReader
  │     ├── Builds Mesh from voxel data
  │     ├── Places at computed world position
  │     ├── Adds BoxCollider for click detection
  │     └── Adds world-space TMP label (name + income)
  └── Sets up camera with pan/zoom controls
```

### 5.5 Building Scale

| Parameter | Value | Notes |
|-----------|-------|-------|
| Voxel size | 0.125 units (8 voxels/meter) | Match Steel Tide standard |
| Building footprint | 16×16 voxels (2m × 2m) | Fits in 3×3 sub-grid within block |
| Building height | 24-64 voxels (3-8m) | 1-2 stories |
| Block size | ~20×20 Unity units | 3×3 buildings + gaps |
| Road width | ~4 Unity units | Between blocks |
| City total (Phase 1) | ~70×70 units | 3×3 blocks with roads |

---

## 6. 3D Map Architecture

### 6.1 Hierarchy

```
CityRoot (GameObject)
├── Lighting (directional light, ambient)
├── Roads (merged mesh)
├── Blocks
│   └── Block_R_C
│       ├── Foundation (slab mesh)
│       ├── Businesses
│       │   └── Biz_R_C_SR_SC
│       │       ├── Mesh (from .stasset)
│       │       ├── Collider (BoxCollider)
│       │       ├── Label (TMP text, world space)
│       │       └── Highlight (outline mesh, toggled on selection)
│       └── BlockLabel (TMP text, world space)
└── Camera (isometric, pan/zoom)
```

### 6.2 Click Detection

- Raycast from camera through mouse position
- Hit `Biz_*` collider → select business
- Hit `Block_*` foundation → select block (shows block-level info)
- Click empty space → deselect

### 6.3 Visual Indicators

| Element | Visual |
|---------|--------|
| Owner gang | Roof/facade color tinted by gang color |
| Selected business | Outline highlight (gold) |
| Selected block | Foundation tint (gold, semi-transparent) |
| Active order | Pulsing icon above business |
| Investigation | Magnifying glass icon above block |
| Police presence | Badge icon on block |
| Illegal business | Subtle red glow at night (future) |

---

## 7. Camera System

### 7.1 Planning Mode Camera

- **Projection**: Orthographic (isometric feel)
- **Angle**: 45° yaw, 30° pitch (standard iso)
- **Pan**: Right-click drag or middle-click drag to move camera X/Z
- **Zoom**: Mouse wheel adjusts orthographic size (5-30 units)
- **Focus**: Double-click business → camera lerps to center on it
- **Edge pan**: Move mouse to screen edge to pan (optional)

### 7.2 Execution Mode Camera

- Auto-pans to each event as it resolves
- Lerp between event locations (0.5s transition)
- Player can override with manual pan (but camera returns to next event)
- Zoom stays at planning level unless event needs close-up

### 7.3 Viewport

Camera renders to the right 40% of screen (x: 0.6→1.0, y: 0.09→0.93). UI panels occupy the left 60%. This is already implemented in `CityMap3D.cs`.

---

## 8. UI Architecture

Already implemented and documented in:
- [UI_TABBED_LAYOUT.md](UI_TABBED_LAYOUT.md) — tabbed layout, CreatePage/CreateScrollablePage
- [UI_LAYOUT_GOTCHAS.md](UI_LAYOUT_GOTCHAS.md) — Unity uGUI pitfalls
- [UI_SETUP_GUIDE.md](UI_SETUP_GUIDE.md) — quick-start guide

### 8.1 Current Tabs (7)

| Tab | Content | Refresh Method |
|-----|---------|----------------|
| Hoods | Gang roster | `RefreshHoods()` |
| Block | Selected block info | `RefreshBlockInfo()` |
| Orders | Order buttons + selection | `RefreshBlockInfo()` + `TryEnableOrderButtons()` |
| Finance | Income/expenses/treasury | `RefreshFinances()` |
| Police | Officers + bribe status | `RefreshPolice()` |
| Invest | Active investigations | `RefreshInvestigations()` |
| Log | Event log (scrollable) | `RefreshEventLog()` |

### 8.2 UI Changes Needed for Phase 1

- **Block tab**: Show all 9 businesses in the selected block (list with type, owner, income)
- **Orders tab**: Select business within block before assigning order
- **Hoods tab**: Show current assignment (which business/block)
- **Log tab**: Already working with scroll

---

## 9. Simulation Systems

### 9.1 Economy System (`EconomySystem.cs`)

**Weekly flow**:
1. Each active business generates profit based on `profitGroup` → `profit_groups` lookup
2. Running costs deducted based on `runningCostGroup` → `running_cost_groups` lookup
3. Net income = total profits - total running costs
4. Illegal businesses generate higher profit but raise suspicion

**Profit groups** (from `businesses.json`):

| Group | $/week | Business Types |
|-------|--------|----------------|
| 3 | $150 | Barber |
| 4 | $200 | Bakery |
| 5 | $250 | Butcher, Garage |
| 6 | $300 | Diner |
| 7 | $400 | Apartments |
| 36 | $500 | Card Game |
| 38 | $750 | Loan Shark |
| 39 | $1000 | Speakeasy |
| 40 | $1500 | Casino |

### 9.2 Crime System (`CrimeSystem.cs`)

**Order types**:

| Order | Effect |
|-------|--------|
| Extort | Take over business, collect protection money |
| Collect | Gather income from owned businesses |
| Patrol | Defend territory, reduce rival takeover chance |
| Intimidate | Lower squeal risk on businesses |
| Lie Low | Reduce investigation heat on hoods |

**Squeal mechanic**:
- Each NPC has a squeal value (base from `constants.squeal`)
- Fear reduces squeal (intimidation orders)
- If squeal threshold exceeded → NPC reports to police → investigation starts

### 9.3 Police System

**Investigation flow**:
1. Squeal triggers → investigation opens on block
2. Investigation accumulates "leads" each week
3. When leads reach threshold → arrest attempt on hoods in block
4. Bribed officers reduce investigation speed
5. FBI gets involved if illegal income exceeds threshold (`constants.fbi_suspicion.base_illegal_income_threshold: 5000`)

**Police beats**:
- Each officer has a `beat` (list of block IDs)
- Officers only investigate blocks in their beat
- Bribing an officer removes their investigation power

### 9.4 Rival AI (`RivalAI.cs`)

**Decision loop** (each week):
1. Evaluate territory — which blocks are weakly defended?
2. Evaluate economy — need more income?
3. Choose action: extort unowned business, attack player territory, or consolidate
4. Assign hoods to execute
5. Resolve combat (hood stats vs defender stats)

### 9.5 Character System (`NPC.cs`)

**Hood stats**:
- Strength (combat effectiveness)
- Intelligence (investigation resistance)
- Loyalty (resistance to being turned)
- Health (current/max)
- Status (active, arrested, dead, hiding)

**NPC types**: business_owner, civilian, hood, police, fbi, judge, mayor

---

## 10. Run Week — Animated Execution

### 10.1 Current Implementation

Instant: `engine.RunWorkingWeek()` returns all events as a list → logged to Event Log tab immediately.

### 10.2 Target Implementation

Coroutine-based step-by-step playback:

```
OnRunWeek()
  │
  ├── phase = Execution
  ├── ShowTab(6)  // switch to Log tab
  │
  ├── yield RunExtortionStep()     // ~3-5 seconds
  │     ├── For each pending order:
  │     │     ├── Camera lerps to target business
  │     │     ├── Animate hood icon moving to business
  │     │     ├── Resolve extortion/collect/patrol/etc.
  │     │     ├── Floating text shows result ($+ or $-)
  │     │     ├── AddEventLogEntry for result
  │     │     └── yield WaitForSeconds(0.5)
  │     └── yield WaitForSeconds(1.0)
  │
  ├── yield RunPoliceStep()        // ~2-3 seconds
  │     ├── For each investigation:
  │     │     ├── Camera lerps to block
  │     │     ├── Show investigation progress
  │     │     ├── Resolve arrest attempt
  │     │     ├── If arrested: animate hood removal
  │     │     ├── AddEventLogEntry
  │     │     └── yield WaitForSeconds(0.5)
  │     └── yield WaitForSeconds(1.0)
  │
  ├── yield RunRivalStep()         // ~2-3 seconds
  │     ├── For each rival action:
  │     │     ├── Camera lerps to target
  │     │     ├── Animate rival hood icon
  │     │     ├── Resolve combat/takeover
  │     │     ├── Update territory colors on map
  │     │     ├── AddEventLogEntry
  │     │     └── yield WaitForSeconds(0.5)
  │     └── yield WaitForSeconds(1.0)
  │
  ├── yield RunEconomyStep()       // ~1-2 seconds
  │     ├── Animate treasury counter ($ lerping up/down)
  │     ├── AddEventLogEntry with weekly summary
  │     └── yield WaitForSeconds(1.0)
  │
  ├── AddEventLogEntry("=== WEEK N COMPLETE ===")
  ├── phase = Planning
  ├── RefreshAll()
  └── ShowTab(6)  // ensure Log tab is visible
```

### 10.3 Animation Primitives

| Animation | Implementation |
|-----------|----------------|
| Camera lerp | `Vector3.Lerp` + `Quaternion.Slerp` over 0.5s |
| Hood icon move | Simple sprite/quad moving along path |
| Floating $ text | TMP text rising and fading over 1s |
| Territory color change | Lerp building roof color over 0.3s |
| Treasury counter | `Mathf.Lerp` displayed value to target over 1s |
| Arrest animation | Hood icon fades out, "ARRESTED" text appears |

### 10.4 Speed Control

- **Normal**: 0.5s per event
- **Fast**: 0.2s per event (toggle button)
- **Skip**: Instant (button to skip animation, show results immediately)

---

## 11. Game Phases

### 11.1 Planning Phase

- Player can inspect all blocks and businesses
- Assign orders to hoods (select hood → select business → select order)
- Bribe police officers
- Manage illegal business operations
- No time pressure

### 11.2 Execution Phase

- Player watches the week unfold (animated)
- Cannot issue orders
- Can pan camera but camera auto-follows events
- Event log updates in real-time
- Returns to Planning when complete

### 11.3 Win/Loss Conditions (Future)

| Condition | Type | Threshold |
|-----------|------|-----------|
| Control 100 blocks | Win | `min_blocks_to_enter` |
| Get elected mayor | Win | Control + election mechanics |
| All hoods arrested/dead | Loss | No hoods remaining |
| Treasury below $0 | Loss | Bankruptcy |
| FBI raid on HQ | Loss | High suspicion + FBI investigation |

---

## 12. Data Schema

### 12.1 City Template JSON (Phase 1 target)

```json
{
  "version": "0.1.0",
  "grid": {"rows": 3, "cols": 3},
  "blocks": [
    {
      "id": "block_1",
      "name": "Little Italy",
      "row": 0, "col": 0,
      "land_value": 7,
      "population": 25,
      "player_hq": true,
      "businesses": [
        {"type": "restaurant", "subRow": 0, "subCol": 0},
        {"type": "apartments", "subRow": 0, "subCol": 1},
        {"type": "bakery", "subRow": 0, "subCol": 2},
        {"type": "barber", "subRow": 1, "subCol": 0},
        {"type": "empty_land", "subRow": 1, "subCol": 1},
        {"type": "diner", "subRow": 1, "subCol": 2},
        {"type": "speakeasy", "subRow": 2, "subCol": 0, "illegal": true},
        {"type": "garage", "subRow": 2, "subCol": 1},
        {"type": "apartments", "subRow": 2, "subCol": 2}
      ]
    }
    // ... 8 more blocks
  ],
  "police_beats": [
    {"officer_id": "officer_001", "name": "Patrolman O'Brien", "beat": ["block_1", "block_2", "block_4", "block_5", "block_7"], "bribe_cost": 300},
    {"officer_id": "officer_002", "name": "Patrolman Kelly", "beat": ["block_3", "block_6", "block_8", "block_9", "block_5"], "bribe_cost": 350}
  ]
}
```

### 12.2 City Layout JSON (generated by Python tool)

```json
{
  "version": "0.1.0",
  "voxel_size": 0.125,
  "block_size": 20.0,
  "road_width": 4.0,
  "buildings": [
    {
      "id": "biz_block_1_restaurant_0",
      "block_id": "block_1",
      "type": "restaurant",
      "name": "Luigi's Trattoria",
      "subRow": 0, "subCol": 0,
      "world_pos": [2.0, 0.0, 2.0],
      "mesh_file": "buildings/restaurant_01.stasset",
      "is_illegal": false
    }
    // ... one per business
  ]
}
```

---

## 13. File Structure

### 13.1 Current

```
Steel_City-Mob_Sim/
├── Assets/
│   ├── Data/                      # Source JSON (reference)
│   │   ├── city_template.json
│   │   ├── businesses.json
│   │   ├── constants.json
│   │   ├── archetypes.json
│   │   ├── crimes.json
│   │   └── weapons.json
│   ├── StreamingAssets/           # Runtime JSON (loaded by DataLoader)
│   │   └── (same files as Data/)
│   ├── Scripts/
│   │   ├── Sim/                   # Game logic
│   │   │   ├── GameEngine.cs
│   │   │   ├── City.cs            # Block, Business, CityGen
│   │   │   ├── NPC.cs             # Hood, NPC, CharacterGen
│   │   │   ├── CrimeSystem.cs
│   │   │   ├── EconomySystem.cs
│   │   │   ├── RivalAI.cs
│   │   │   ├── EventStream.cs
│   │   │   ├── DataLoader.cs
│   │   │   ├── DataModels.cs
│   │   │   └── JSONParser.cs
│   │   ├── UI/                    # Runtime UI
│   │   │   ├── GameUIController.cs
│   │   │   └── CityMap3D.cs
│   │   └── Editor/                # Editor tools
│   │       └── GameUIAutoBuilder.cs
│   └── docs/                      # Documentation
│       ├── UI_TABBED_LAYOUT.md
│       ├── UI_LAYOUT_GOTCHAS.md
│       ├── UI_SETUP_GUIDE.md
│       ├── PORTING_NOTES.md
│       └── GAME_DESIGN_SKELETON.md  (this file)
```

### 13.2 Phase 1 Target (additions)

```
Steel_City-Mob_Sim/
├── Assets/
│   ├── StreamingAssets/
│   │   └── city/                   # NEW — generated city assets
│   │       ├── city_layout.json
│   │       ├── buildings/
│   │       │   ├── restaurant_01.stasset
│   │       │   ├── speakeasy_01.stasset
│   │       │   ├── casino_01.stasset
│   │       │   └── ... (per business type)
│   │       └── tiles/
│   │           ├── road_straight.stasset
│   │           ├── road_corner.stasset
│   │           └── sidewalk.stasset
│   ├── Scripts/
│   │   ├── Sim/
│   │   │   └── StAssetReader.cs    # NEW — reads .stasset at runtime
│   │   └── UI/
│   │       └── CityMap3D.cs        # MODIFIED — loads buildings from .stasset
│   └── docs/
│       └── GAME_DESIGN_SKELETON.md

VoxelAssetStudio/
├── procedural_mob_buildings.py     # NEW — 1920s building generators
├── procedural_city_gen.py          # NEW — city layout generator
├── mob_materials.py                # NEW — 1920s material palette
└── (existing files unchanged)
```

---

## 14. Development Sequence

### Step 1: Asset Pipeline
1. Create `mob_materials.py` with 1920s material palette
2. Write `procedural_mob_buildings.py` with generators for each business type
3. Write `procedural_city_gen.py` to generate city layout + export .stasset files
4. Generate test buildings, verify .stasset files load in Unity

### Step 2: City Rendering
1. Write `StAssetReader.cs` to load .stasset files at runtime
2. Modify `CityMap3D.cs` to load and render individual buildings
3. Add per-business click detection (raycast on building colliders)
4. Add visual indicators (owner color, selection highlight)

### Step 3: Camera Controls
1. Add pan (drag) to camera
2. Add zoom (scroll wheel)
3. Add focus (double-click to center on business)
4. Clamp camera to city bounds

### Step 4: UI Polish
1. Update Block tab to show all 9 businesses in selected block
2. Update Orders tab to select business within block
3. Update Hoods tab to show current assignment
4. Add business selection state to GameUIController

### Step 5: Animated Run Week
1. Refactor `OnRunWeek()` into coroutine
2. Implement step-by-step playback (extortion → police → rival → economy)
3. Add camera auto-pan to events
4. Add animation primitives (floating $, hood icons, territory color changes)
5. Add speed controls (normal/fast/skip)

---

## 15. Reference Data Summary

### Original Game Constants

| Category | Key | Value | Source |
|----------|-----|-------|--------|
| Population | civilians | 2,000 | `constants.city.civilians` |
| Population | police | 400 | `constants.city.police` |
| Population | fbi | 100 | `constants.city.fbi` |
| Population | judges | 12 | `constants.city.judges` |
| Population | attorneys | 12 | `constants.city.attorneys` |
| Gang start | hoods | 5 | `constants.gang_start.hoods` |
| Gang start | money | $6,000 | `constants.gang_start.money` |
| Gang start | explosives | 3 | `constants.gang_start.explosives` |
| Gang start | businesses | 1 | `constants.gang_start.businesses` |
| Gang start | vehicles | 1 | `constants.gang_start.vehicles` |
| Elections | min blocks | 100 | `constants.elections.min_blocks_to_enter` |
| FBI | illegal income threshold | $5,000 | `constants.fbi_suspicion.base_illegal_income_threshold` |
| Gang splitting | loyalty threshold | 192 | `constants.gang_splitting.loyalty_threshold` |

### Fear Base Values

| NPC Type | Base Fear | Modifier |
|----------|-----------|----------|
| Business Owner | 100 | -20 |
| Civilian | 100 | -20 |
| Hood | 128 | 0 |
| Police | 128 | 0 |
| FBI | 128 | 0 |
| Judge | 128 | 0 |
| Mayor | 128 | 0 |

### Squeal Values

| NPC Type | Squeal Threshold |
|----------|-----------------|
| Business Owner | 125 |
| Civilian | 100 |
| Hood | 100 |
| Police | 200 |
| Police Chief | 250 |
| FBI | 250 |
| Judge | 200 |
| Mayor | 250 |

### Bribe Prices

| NPC Type | Base Cost | Multiplier |
|----------|-----------|------------|
| Business Owner | $500 | ×500 |
| Snitch | $500 | ×4 |
| Attorney | $5,000 | ×3,000 |
| Judge | $10,000 | ×20,000 |
| Mayor | $10,000 | ×10,000 |
| Police | $2,000 | ×3,000 |
| Police Chief | $10,000 | ×10,000 |
