# Vehicle Reverse Engineering Reference

**Created**: August 7, 2026
**Status**: ✅ Complete — All Ghidra vehicle outputs catalogued and interpreted
**Purpose**: Single-stop reference for all vehicle-related RE data from gangsters.exe, consolidated from Ghidra decompilation outputs and existing analysis docs.

---

## Source Files

### Ghidra Output Files (raw decompilation)

| File | Script | Content |
|------|--------|---------|
| `ghidra_walk_drive_decision.txt` | `TraceWalkDriveDecision.java` | WALK vs DRIVE dispatcher functions, vtable jump entries, 0x8000 flag references (237 refs, 116 functions) |
| `ghidra_walk_drive_callers.txt` | `FindWalkDriveCallers.java` | Caller tracing for WALK/DRIVE dispatchers, vtable data entries |
| `ghidra_vehicle_state_setters.txt` | `FindVehicleStateSetters.java` | Functions setting 0x38000000 vehicle bits at offset 0x24, "NotEnoughCars" strings |
| `ghidra_vehicle_flags.txt` | `FindVehicleFlagSetters.java` | 0x80000 flag setter, 212 functions referencing 0x38000000 |
| `ghidra_vehicle_ped_interaction.txt` | `DecompileVehiclePedInteraction.java` | SIM_TICK (16,980 bytes) driving cases, entity assignment, vehicle state management |
| `ghidra_traffic_signal_writes.txt` | `SearchTrafficSignalWrites.java` | Road access flag write search — confirms static (0 dynamic writes) |
| `ghidra_traffic_interactions.txt` | `DecompileTrafficInteractions.java` | Pedestrian entity awareness, rectangular area scan, blocked crossing handler, vehicle reroute, tram logic |
| `ghidra_road_access_init.txt` | `DecompileRoadAccessInit.java` | FUN_00650ee0 — the ONLY function writing to road access flags (7,290 bytes, map load) |
| `ghidra_movement_setup.txt` | `TraceMovementSetup.java` | Animation lookup + walk/drive mode setter (FUN_0048a750), vtable call pattern search |

### Ghidra Scripts (re-runnable)

All scripts at `C:\Tools\ghidra_scripts\`. Ghidra project at `SteelCityMobSim/ghidra_project/GanstersToSteelCity2`.

### Existing Analysis Documents

| Document | Section | Content |
|----------|---------|---------|
| `docs/core/REVERSE_ENGINEERING_FINDINGS.md` | Section 5+ | Vehicle strings, sprite loading, types, walk/drive decision, state machine, traffic |
| `docs/core/ENGINE_INTEGRATION_PLAN.md` | Sections 4, 4A, 5 | Vehicle system implementation plan, car chase vision, NPC collision/traffic |
| `docs/core/GHIDRA_SCRIPTING_GUIDE.md` | Sections 9-10 | Script inventory, key addresses, entity types, memory layout |

---

## 1. Entity Types

| Type ID | Entity | SIM_TICK Cases | Notes |
|---------|--------|----------------|-------|
| 0x08 | Tram | vtable dispatch | Fixed rail path, station stops |
| 0x09 | Train | vtable dispatch | Rail network |
| 0x0C | Trucks | 4, 8, 10 (driving) | Cargo transport |
| 0x0D | Cars | 4, 8, 10 (driving) | Subtypes: 0=Civilian, 1=Roadster, 2=Police |
| 0x10–0x24 | People | 0-3, 9 (walking/pathfinding) | Hoods, civilians |

### Vehicle Subtypes (from debug display `FUN_00719f90`)

| Entity Type | Subtype | Vehicle Name | Steel City Model |
|-------------|---------|--------------|------------------|
| 0x08 | — | Tram/Trolley | Fixed rail, station stops |
| 0x09 | — | Train | Rail network |
| 0x0C | — | Truck | Cargo transport |
| 0x0D | 0 | Civilian Car | Basic car |
| 0x0D | 1 | Roadster | Fast car (gangster preferred) |
| 0x0D | 2 | Police Car | Police cruiser (lights, siren) |

---

## 2. Key Functions

| Address | Function | Size | Purpose |
|---------|----------|------|---------|
| `0x005d2740` | `FUN_005d2740` (SIM_TICK) | 16,980 | Master per-tick simulation — driving cases 4/8/10 |
| `0x00761e00` | `FUN_00761e00` | — | WALK dispatcher |
| `0x00762080` | `FUN_00762080` | — | DRIVE dispatcher |
| `0x004616d0` | `FUN_004616d0` | 330 | WALK real function (thunked from 0x0040356c) |
| `0x00462db0` | `FUN_00462db0` | 288 | DRIVE real function (thunked from 0x00404df9) |
| `0x004c1140` | `FUN_004c1140` | 572 | In-order vehicle upgrade |
| `0x004cb0c0` | `FUN_004cb0c0` | — | State 1: vehicle decision (walk=12000, drive=32) |
| `0x00462f30` | `FUN_00462f30` | — | Distance-based walk/drive decision (threshold 0x40) |
| `0x0048a750` | `FUN_0048a750` | 90 | Animation lookup + walk/drive mode setter |
| `0x005dc080` | `FUN_005dc080` | 557 | Drive state transition (steal/find vehicle) |
| `0x00660e60` | `FUN_00660e60` | 765 | Vehicle assignment for street orders (25% random) |
| `0x005dc8c0` | `FUN_005dc8c0` | 1,312 | Street crossing (4-directional, 6-cell max) |
| `0x00609cf0` | `FUN_00609cf0` | 571 | Street access check (4 cardinal directions) |
| `0x005dd9d0` | `FUN_005dd9d0` | — | Entity search in rectangular area |
| `0x005dd910` | `FUN_005dd910` | — | Pedestrian reaction to nearby entities |
| `0x005ddb80` | `FUN_005ddb80` | 193 | Blocked crossing handler |
| `0x005d6ef0` | `FUN_005d6ef0` | 261 | Vehicle reroute when blocked |
| `0x00563150` | `FUN_00563150` | 62 | Vehicle stop handler |
| `0x005dddc0` | `FUN_005dddc0` | 1,319 | SIM_TICK post-processing (entity interaction) |
| `0x00565c30` | `FUN_00565c30` | 249 | Time consumption (156 refs, 67 callers) |
| `0x00565790` | `FUN_00565790` | — | Time budget allocation |
| `0x00664d50` | `FUN_00664d50` | — | Get map cell at (x,y) — most-called function |
| `0x00650ee0` | `FUN_00650ee0` | 7,290 | Road access flag initializer (map load only) |
| `0x00414ba0` | `FUN_00414ba0` | 7,273 | Tram type checker (3 CMP against 0x08) |

---

## 3. Vehicle Flags & Bitfields

### 0x8000 — Vehicle Use Flag (Bit 15)

Located at offset `0xC` of the movement/AI struct. When set, entity uses a vehicle for travel. When clear, entity walks.

- **237 references** across **116 functions** in the binary
- Set by `FUN_004cb0c0` (vehicle decision) and `FUN_00462db0` (DRIVE function)
- Checked by `FUN_004367f0`, `FUN_00442c00`, `FUN_004499d0`, `FUN_0044cc60`, etc.

### 0x80000 — Vehicle Active Flag

Set by `FUN_0059cb10` and `FUN_00462db0` (DRIVE function). Indicates entity is currently in a vehicle.

### 0x38000000 — Vehicle State Lifecycle (3-bit field at offset 0x24)

| Value | Bits | Meaning |
|-------|------|---------|
| 0x08000000 | bit 27 | Vehicle assigned / in-use |
| 0x10000000 | bit 28 | Vehicle transitioning |
| 0x20000000 | bit 29 | Drive state active |
| 0x30000000 | bits 28+29 | Advanced driving |

**212 functions** reference `0x38000000` — this is one of the most widely used flag fields in the binary.

### Setters Identified

| Function | Flag Set | Context |
|----------|----------|---------|
| `FUN_00660e60` | 0x08000000 | Vehicle assignment for street orders |
| `FUN_005dc080` | 0x20000000 (×2) | Drive state transition |
| `FUN_0048cc60` | 0x30000000 | Advanced driving state |
| `FUN_0059cb10` | 0x80000 | Vehicle active flag |
| `FUN_00462db0` | 0x80000 + 0x8000 | DRIVE function sets both flags |

### 0x80004001/0x80004002/0x80004005 — Composite Flags

Referenced in `FUN_00469b30` and `FUN_00486820` — likely vehicle type + state composite flags combining the 0x8000 vehicle bit with additional mode bits.

---

## 4. Walk vs Drive Decision

### The Decision Logic

The game uses a **distance-based threshold** to decide walk vs drive:

```
FUN_00462f30: if distance > 0x40 (64 units) → drive, else → walk
FUN_004cb0c0: walk cost = 12000 ticks, drive cost = 32 ticks (375× speedup)
```

### How the Vehicle Flag Gets Set

Two functions assign orders to individual hoods:

1. **`FUN_00660e60`** — Vehicle assignment for street orders
   - 25% random chance to assign a vehicle
   - Checks `NotEnoughCars` error string via `FUN_005bbee0`
   - Sets `0x08000000` bit at offset `0x24`

2. **`FUN_004cb0c0`** — Vehicle decision based on order type
   - Compares walk cost (12000) vs drive cost (32)
   - Sets bit 15 (`0x8000`) of movement flags

### WALK Function (`FUN_004616d0`, 330 bytes)

- Checks if entity is already active (`param_1[0x21] & 0x80`)
- Sets `param_1[0x29]` (target)
- Sets `0x80` flag (active)
- Allocates movement struct if needed (`operator_new(0x70)`)
- Calls `thunk_FUN_00565790(0, 12000, target)` — allocates 12000 ticks
- Calls `thunk_FUN_00564060` — sets up waypoint path

### DRIVE Function (`FUN_00462db0`, 288 bytes)

- Sets `param_1[0x29]` (target)
- Sets `0x80080` flags (active + vehicle)
- Allocates movement struct if needed
- Calls `thunk_FUN_00565790(0, 12000, target)` — same time budget call
- Calls `thunk_FUN_00564060` — sets up waypoint path
- Returns 1 (always succeeds, no "already active" check unlike WALK)

### In-Order Vehicle Upgrade (`FUN_004c1140`, 572 bytes)

- Calls DRIVE function (`thunk_FUN_00462db0`)
- If drive succeeds and entity has a vehicle assigned (`param_1 + 0xAC`):
  - Adds vehicle to global linked list at `DAT_007c0024 + 0x24`
  - Manages free list at `DAT_007c0024 + 0x34`
  - Clears `param_1 + 0xAC` (releases vehicle reference)

---

## 5. SIM_TICK Driving Cases

### Case 4: Basic Driving

- Speed accumulation — vehicles build speed over ticks, not instant
- Lane change logic — vehicles can change lanes during travel
- Collision checks — vehicles check for obstacles in path
- Calls `thunk_FUN_00664d50` (map cell lookup) to scan ahead

### Case 8: Vehicle Cruise

- Speed-based position update — position advances proportional to speed
- Block transitions — when sub-block position wraps, vehicle enters new block
- Cruise maintenance — maintains speed unless blocked

### Case 10: Advanced Driving (5-Substate Machine)

| Substate | Name | Behavior |
|----------|------|----------|
| 0 | Accelerate | Build speed from stop |
| 1 | Cruise | Maintain speed, scan ahead |
| 2 | Lane Change | Shift lateral position |
| 3 | Decelerate | Slow for obstacle/turn |
| 4 | Stop | Full stop, await path clear |

### Driving Skips Arrest Checks

When driving, entities **skip arrest checks** — making vehicles a safety mechanic, not just speed. This is a significant gameplay implication: driving isn't just faster, it's safer.

---

## 6. Vehicle State Machine

### Entity State Field: `entity + 0x11` (byte)

The AI state byte at offset `0x11` drives the SIM_TICK switch:

| State | Mode | Entity Types |
|-------|------|-------------|
| 0-3 | Walking | People (0x10-0x24) |
| 4 | Basic Driving | Trucks (0xC), Cars (0xD) |
| 8 | Vehicle Cruise | Trucks, Cars |
| 9 | Pathfinding | People |
| 10 (0xA) | Advanced Driving | Trucks, Cars |
| 0xB-0xD | Combat/Fleeing | People |

### Vehicle Lifecycle States (from 0x38000000 field)

```
Unassigned → Assigned (0x08000000) → Transitioning (0x10000000)
  → Drive Active (0x20000000) → Advanced Driving (0x30000000)
  → Released (back to free list)
```

### Vehicle Entity as Independent Agent

Vehicles have their own **"Vehicle goal"** (string at `0x007bf5bc`, referenced by `FUN_00719f90`) — a navigation target separate from the hood's destination. This means:

- Vehicle entities exist independently in the simulation
- The hood "drives" by issuing commands to the vehicle entity
- The vehicle follows its own pathfinding to the goal
- Vehicle entities are tracked in linked lists at `DAT_007c0024 + 0x24` (active) and `+ 0x34` (free pool)

---

## 7. Traffic System

### Road Access Flags

| Property | Value |
|----------|-------|
| Base address | `DAT_007c0024 + 0x1220` (`0x007c1244`) |
| Size | ~4 bytes per road segment |
| Write references | **0** (static, set at map load) |
| Read references | 20 (from 4 unique functions) |
| Initializer | `FUN_00650ee0` (7,290 bytes, city constructor) |

**Key Finding**: Road access flags are **completely static**. They are set once during map loading and never modified during gameplay. No dynamic traffic light changes.

### Street Crossing (`FUN_005dc8c0`, 1,312 bytes)

- 4-directional checker with 6-cell maximum scan
- Checks road direction flags at `DAT_007c0024 + 0x1220`
- Uses entity direction byte (`param_1 + 0x1B`) to determine crossing direction
- Direction mapping:
  - Cases 0, 8: North-South crossing
  - Cases 1, 9: East-West crossing
  - Cases 2, 10: Diagonal (vehicle direction)
  - Cases 3, 0xB: Reverse diagonal
  - Cases 4-7, 0xC-0xF: Vehicle-specific crossing (4-way check)

### Blocked Crossing Handler (`FUN_005ddb80`, 193 bytes)

- Only triggers for people (type `0x10`) without vehicle flag (`0x80`)
- 1/128 chance per tick (`thunk_FUN_00712500() & 0x7f == 0`)
- Sets `0x80` flag (waiting state)
- Creates a "wait" command with 50% direction randomization
- Calls `thunk_FUN_00565c30(0, 0x10)` — costs 16 ticks

### Vehicle Reroute (`FUN_005d6ef0`, 261 bytes)

When a vehicle is blocked, it scans for alternate paths:
- Scans a grid from -5 to +5 in one axis, 0 to 30 (0x1E) in the other
- Axis depends on travel direction (`param_3` vs `param_4`)
- If alternate found: calls `thunk_FUN_0060b470(7)` — sets new path
- If no alternate: calls `thunk_FUN_00606760()` — stops vehicle

### Vehicle Stop Handler (`FUN_00563150`, 62 bytes)

- Pops the first command from the vehicle's command queue
- Updates linked list pointers
- Decrements command count
- Returns the stopped command for cleanup

---

## 8. Pedestrian-Vehicle Interaction

### Entity Awareness (`FUN_005dd910`)

Pedestrians react to nearby entities:
- Called when entity search finds something at `entity + 0x68`
- Sets `0x700` flags on the pedestrian's state field
- Triggers avoidance behavior

### Entity Search (`FUN_005dd9d0`)

Rectangular area scan for entities:
- Takes center position and search radius
- Returns entity pointer if found
- Used by pedestrians to detect vehicles

### Post-Tick Entity Interaction (`FUN_005dddc0`, 1,319 bytes)

Runs after SIM_TICK for each entity:
- Only for people (type `0x10`) not in vehicle (`0x80` flag clear)
- 1/8 chance per tick (`thunk_FUN_00712500() & 7`)
- Determines entity behavior based on RNG:
  - Cases 0-3: Walk to nearby block (direction random)
  - Cases 4-6, 0xE-0xF: Location-specific behavior (checks map cell type)
  - Cases 7-0xD: Vehicle-related behavior (sets state 3 = "vehicle interaction")
  - Cases 0x11-0x15: Walk to random nearby location
  - Cases 0x16-0x1A: Walk with direction + speed variation

### Tram-Specific Logic (`FUN_00414ba0`, 7,273 bytes)

- Checks entity type against `0x08` (tram) three times
- Large function — likely handles tram routing, station stops, and passenger management
- Tram right-of-way logic embedded

---

## 9. Global State Structure

### Key Offsets from `DAT_007c0024`

| Offset | Address | Purpose |
|--------|---------|---------|
| `+0x24` | `0x007c0048` | Active vehicle linked list head |
| `+0x28` | `0x007c004C` | Active vehicle linked list tail |
| `+0x30` | `0x007c0054` | Active vehicle count |
| `+0x34` | `0x007c0058` | Free vehicle pool head |
| `+0x38` | `0x007c005C` | Free vehicle pool tail |
| `+0x3C` | `0x007c0060` | Free vehicle count |
| `+0x124` | `0x007c0148` | Active entity list head (all entities) |
| `+0x128` | `0x007c014C` | Active entity list tail |
| `+0x130` | `0x007c0154` | Active entity count |
| `+0x134` | `0x007c0158` | Free entity node pool head |
| `+0x138` | `0x007c015C` | Free entity node pool tail |
| `+0x13C` | `0x007c0160` | Free entity node count |
| `+0x140` | `0x007c0164` | Current entity (iteration cursor) |
| `+0x1220` | `0x007c1244` | Road access flags (4 bytes/segment) |
| `+0x16D8` | `0x007c16FC` | Global speed value (timer countdown rate) |
| `+0x1B18` | `0x007c1B3C` | Default speed (short) |
| `+0x858` | `0x007c087C` | Animation/movement table base |

---

## 10. Entity Structure Field Map

| Offset | Type | Purpose |
|--------|------|---------|
| `+0x04` | short | Position X (sub-block) |
| `+0x06` | short | Position Y (sub-block) |
| `+0x08` | int | Command queue head |
| `+0x0C` | int | Command queue tail |
| `+0x04` (queue) | short | Command count |
| `+0x0E` | byte | Animation frame index |
| `+0x0F` | byte | Animation sub-frame (incremented by global speed) |
| `+0x10` | byte | Entity type (0x08=tram, 0x0D=car, 0x10=person) |
| `+0x11` | byte | AI state (0-3=walk, 4=drive, 8=cruise, 10=adv drive) |
| `+0x15` | int | Movement/AI struct pointer (allocated 0x70 bytes) |
| `+0x16` | byte | Animation set ID |
| `+0x18` | byte | Movement flags (bit 0 = has order, bit 1 = vehicle assigned) |
| `+0x19` | uint | Status flags (bit 7 = waiting, bits 8-10 = entity awareness) |
| `+0x1A` | int | Current target entity pointer |
| `+0x1B` | byte | Direction/road segment ID |
| `+0x1D` | int | Vehicle pointer (if driving) |
| `+0x1E` | int | Vehicle backup pointer |
| `+0x21` | uint | Movement flags (bit 7 = active, bit 15 = vehicle use, bits 0-4 = state) |
| `+0x24` | uint | Vehicle state bits (0x38000000 field) |
| `+0x29` | byte | Target/destination reference |
| `+0x59` | byte | Current animation lookup result |
| `+0x68` | int | Nearby entity pointer (set by entity search) |
| `+0xAC` | int | Assigned vehicle ID (for in-order vehicle) |
| `+0xEC` | int | Secondary target (cleared on walk) |
| `+0x488` | int | Vehicle passenger slot 1 |
| `+0x48C` | int | Vehicle passenger slot 2 |

---

## 11. Time Budget System

### Constants

| Constant | Value | Purpose |
|----------|-------|---------|
| 12000 | 0x2EE0 | Weekly tick budget per hood |
| 500 | — | Ticks per in-game hour |
| 24 | — | Hours per day |
| 32 | 0x20 | Drive cost (ticks) — 375× faster than walk |
| 16 | 0x10 | Crossing cost (ticks) |
| 0x40 | 64 | Distance threshold for walk/drive decision |

### Time Cost Functions

| Function | Cost | Context |
|----------|------|---------|
| `thunk_FUN_00565790(0, 12000, target)` | 12000 | Walk — full weekly budget |
| `thunk_FUN_00565790(0, 32, target)` | 32 | Drive — minimal cost |
| `thunk_FUN_00565c30(0, 0x10)` | 16 | Street crossing |
| `thunk_FUN_00565c30(0, random + 0x10)` | 16-47 | Random wandering |
| `thunk_FUN_00565c30(1, 0)` | Reset | Clear time budget / order complete |

---

## 12. Vehicle Strings Found in Binary

| String | Address | Referenced By | Context |
|--------|---------|--------------|---------|
| `NotEnoughCars` | — | `FUN_005bbee0` | Error when no vehicles available |
| `Buy Vehicles` | `0x007ba4b4` | `FUN_00678900` | Objective name |
| `Vehicle goal` | `0x007bf5bc` | `FUN_00719f90` | Debug display label |

---

## 13. Vtable Architecture

### Vtable Offsets for Entity Dispatch

| Offset | Function | Purpose |
|--------|----------|---------|
| `+0x6C` | WALK | Walk vtable entry (at `0x0078266C`) |
| `+0x80` | Target selector | Target selection vtable (at `0x00782680`) |
| `+0x98` | DRIVE | Drive vtable entry (at `0x00782698`) |
| `+0xA0` | Vehicle/tail AI | Vehicle follow/tail vtable (at `0x007826A0`) |

### Thunk Chain

```
WALK:  vtable[0x6C] → thunk @ 0x00403580 → FUN_00761e00 (dispatcher) → FUN_004616d0 (real)
DRIVE: vtable[0x98] → thunk @ 0x00405a83 → FUN_00762080 (dispatcher) → FUN_00462db0 (real)
IN-ORDER VEHICLE: vtable entry → thunk @ 0x00408e6d → FUN_004c1140 (upgrade)
```

### Vtable Call Pattern

Binary pattern scanning for `call [reg+offset]` found **0 direct vtable calls** for walk/drive. This confirms that vtable-dispatched functions have no direct references — they are called exclusively through indirect vtable dispatch. The `TraceMovementSetup.java` script searched for `call [reg+0x6c]`, `call [reg+0x98]`, `call [reg+0x80]`, and `call [reg+0xa0]` patterns but found none, meaning the dispatch uses a different instruction encoding (possibly `call [reg]` after loading the vtable entry into a register).

---

## 14. Animation System

### Animation Lookup (`FUN_0048a750`, 90 bytes)

```c
uint FUN_0048a750(int param_1, byte param_2, byte param_3) {
    // param_1 = animation table base
    // param_2 = animation set ID
    // param_3 = desired animation
    // Returns: animation index from table at offset (param_2 * 0x66 + param_3 * 2)
    // Falls back to scanning 13 entries (0x0D) if target is 0xFFFF
    // Uses hash: iVar5 = iVar5 * 0x33 + uVar4
}
```

- Animation table stride: `0x66` (102) bytes per animation set
- Each entry: 2 bytes (ushort animation index)
- Fallback: linear scan up to 13 entries with hash-based selection
- Called with `(char)param_1[0x16]` (animation set) and state byte

### Animation Table Location

At `DAT_007c0024 + 0x858` — contains animation/movement data indexed by:
- Animation set ID (`entity + 0x16`)
- Animation frame (`entity + 0x0E`)
- Sub-frame (`entity + 0x0F`, incremented by global speed `+0x16D8`)

---

## 15. Implementation Notes for Steel City

### What to Preserve

- **Walk/drive speed differential** (375×) — makes vehicles strategically valuable
- **Vehicle as safety mechanic** — driving skips arrest checks
- **25% random vehicle assignment** — adds variety to street orders
- **NotEnoughCars error** — vehicle scarcity as gameplay constraint
- **5-substate driving AI** — accelerate, cruise, lane change, decelerate, stop
- **Vehicle entities as independent agents** — vehicles have own goals
- **Static road access flags** — simplifies traffic system (no dynamic lights)
- **Linked-list vehicle pool** — active vs free vehicle management

### What to Modernize

- Bitfield flags → C# enums with `[Flags]` attribute
- Manual linked lists → `ObjectPool<T>` + `List<T>`
- Raw offsets → named struct/class fields
- Static road flags → optional dynamic traffic (future enhancement)
- 2D coordinate system → 3D with Unity navigation

### What to Add (Greenfield)

- Visual vehicle models (voxel-style with turning wheels)
- Faction-colored vehicles
- Police car lights/siren
- Car chase system (see `ENGINE_INTEGRATION_PLAN.md` Section 4A)
- Drive-by shooting (COMBAT_3 from original)
- Vehicle damage states
- Parking/vehicle storage at garages

### Existing Code Touchpoints

- `src/sim/character.py` — `SKILLS` list includes `"driving"`
- `src/sim/city.py` — `Business.type` comment mentions `"garage"`
- `data/businesses.json` — No garage business defined yet
- `src/sim/engine.py` — No vehicle mechanics (greenfield)

---

## 16. Re-running Ghidra Scripts

To extract more vehicle data or re-verify findings:

1. **Open Ghidra** → File → Open Project → `SteelCityMobSim/ghidra_project/GanstersToSteelCity2`
2. **Open** `gangsters.exe` in Code Browser
3. **Script Manager** → Analysis category → select script
4. **Run** — output files written to `SteelCityMobSim/` root

### Available Vehicle Scripts

| Script | Output | Status |
|--------|--------|--------|
| `FindVehicleFlagSetters.java` | `ghidra_vehicle_flags.txt` | ✅ Run |
| `FindVehicleStateSetters.java` | `ghidra_vehicle_state_setters.txt` | ✅ Run |
| `DecompileVehiclePedInteraction.java` | `ghidra_vehicle_ped_interaction.txt` | ✅ Run |
| `SearchTrafficSignalWrites.java` | `ghidra_traffic_signal_writes.txt` | ✅ Run |
| `DecompileRoadAccessInit.java` | `ghidra_road_access_init.txt` | ✅ Run |
| `FindWalkDriveCallers.java` | `ghidra_walk_drive_callers.txt` | ✅ Run |
| `TraceWalkDriveDecision.java` | `ghidra_walk_drive_decision.txt` | ✅ Run |
| `DecompileTrafficInteractions.java` | `ghidra_traffic_interactions.txt` | ✅ Run |
| `TraceMovementSetup.java` | `ghidra_movement_setup.txt` | ✅ Run |

### Potential New Scripts to Write

- **DecompileVehicleSpawner** — Find where vehicle entities are created (sprite loading → entity creation)
- **DecompileTramLogic** — Deep dive into `FUN_00414ba0` (7,273 bytes, tram-specific)
- **FindVehicleCombat** — Decompile `COMBAT_3` (`thunk_FUN_004cc070`) for drive-by mechanics
- **TraceVehicleSpriteLoading** — Map the memory-tier fallback sprite loading system
- **DecompileVehicleAssignment** — Full decompilation of `FUN_00660e60` (25% random assignment logic)

See `docs/core/GHIDRA_SCRIPTING_GUIDE.md` for script writing methodology and `docs/core/GHIDRA_SCRIPT_WORKFLOW.md` for workflow details.
