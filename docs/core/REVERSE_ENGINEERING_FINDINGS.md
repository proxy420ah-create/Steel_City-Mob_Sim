# Reverse Engineering Findings — Gangsters.exe Binary Analysis

**Created**: August 5, 2026
**Status**: 🔶 Active — Vehicle state machine + portrait system SOLVED, movement state machine mapped, traffic interactions mapped
**Analyst**: Cascade + USER
**Source Binary**: `gangsters.exe` (GOG release, ~1998 Hothouse Creations)
**Toolchain**: Ghidra 12.1.2, JDK 21 (Eclipse Adoptium), custom Java analysis scripts

---

## Purpose

This document preserves discoveries from static binary analysis of the original
Gangsters: Organized Crime executable. These findings serve as the technical
foundation for Steel City: Mob Sim — a love letter that fans of the original
will instantly recognize, built with proprietary rendering and modern design
decisions that make it our own.

**What we're mining for:**
- Order execution mechanics (how orders dispatch, validate, and resolve)
- ~~Vehicle usage logic (when hoods drive vs. walk, how vehicles are assigned)~~ ✅ SOLVED
- Travel time and distance calculations
- Time budget system (weekly 12000-tick allocation)
- AI decision-making patterns
- ~~Character portrait generation~~ ✅ SOLVED (5-layer compositor, seed-based)
- Any hidden mechanics not visible in the .xtx data files

**Guiding principle**: Rely on disassembly and decompiled code only. No speculation.

---

## Analysis Methodology

### Tools
- **Ghidra 12.1.2** — Import, auto-analyze, decompile
- **Custom Java scripts** — Automated string/constant search and batch decompilation
  - `FindOrderLogic.java` — Searches for order/vehicle strings and key constants, decompiles all referencing functions
  - `DecompileKeyFunctions.java` — Targeted decompilation of 19 high-priority functions
  - `DecompileTimeFunctions.java` — Decompiles functions referencing the 12000 time constant

### Output Files
- `ghidra_analysis_output.txt` — Full string/constant reference map + all decompiled functions (~30K lines)
- `ghidra_key_functions.txt` — 19 key functions fully decompiled (~21K lines)
- `ghidra_time_functions.txt` — Time-constant functions (pending)

### Binary Stats
- **Total functions**: 9,841
- **Largest function**: `FUN_0063c0b2` at 23,807 bytes (main window message handler)
- **2nd largest**: `FUN_00450f80` at 22,216 bytes (game initialization / world setup)

---

## 1. Game Timing System

### Constants

| Constant | Hex | Purpose |
|----------|-----|---------|
| 720,000 | — | Total game time in ticks (stored at struct offset `0x21b9`) |
| 12,000 | `0x2EE0` | Weekly time budget per hood (stored at struct offset `0x21ba`) |
| 500 | — | Ticks per in-game hour (`12000 / 0x18 = 500`) |
| 24 | — | Hours per day (`720000 / (500 × 60) = 24`) |

### Source: `FUN_00450f80` (Game Initialization)

```c
// Line 4756-4759 of ghidra_key_functions.txt
param_1[0x21b9] = 720000;           // Total game time
param_1[0x21ba] = 12000;            // Weekly time budget
DAT_007c4754 = param_1[0x21ba] / 0x18;  // 500 ticks/hour
DAT_007c4750 = param_1[0x21b9] / (DAT_007c4754 * 0x3c);  // 24 hours/day
```

### Interpretation

- Each hood gets **12,000 ticks per week** to spend on orders.
- At 500 ticks per hour, that's **24 in-game hours** of action per hood per week.
- The 12000 constant is referenced by **33 locations** across the binary, meaning
  many systems consume from this time budget.
- Key functions referencing 12000 (pending decompilation):
  `FUN_0049a530`, `FUN_004c8940`, `FUN_004cb0c0`, `FUN_004cbd00`,
  `FUN_00583dc0`, `FUN_005844a0`, `FUN_005871c0`, `FUN_005dd870`

### Steel City Design Implication

Preserve the weekly time budget concept — it's the core constraint that makes
order prioritization meaningful. The 12000-tick budget creates real tension
between "extort nearby" vs. "drive across town for a bigger job." Our version
should make the time cost of travel **visible** before the player commits.

---

## 2. Order Type System

### Order Name Dispatcher: `FUN_005aa9b0`

A switch function mapping internal order IDs to display names. This is the
canonical order enum:

| Internal ID | Order Name | Notes |
|-------------|-----------|-------|
| 0x00 | (case 0) | Buy premises |
| 0x01 | Set up illegal | |
| 0x02 | Guard business | |
| 0x03 | Collect protection | |
| 0x04 | Patrol | |
| 0x05 | Investigate | |
| 0x06 | Wait / Visit | |
| 0x07 | Drive | |
| 0x08 | Rob business | |
| 0x09 | Smash up | |
| 0x0a | Attack building | |
| 0x0b | Recruit | Special-cased in UI (shows "Select type") |
| 0x0c | Go to | Special-cased in UI |
| 0x0d | Bribe | |
| 0x0e | Intimidate | |
| 0x0f | Extort | |
| 0x10 | Attack people | |
| 0x11 | Torch (arson) | |
| 0x12 | Assault | |
| 0x13 | Kill | |
| 0x14 | Bomb | |
| 0x15 | Kidnap | |
| 0x16 | Ambush | |
| 0x17 | Break alliance | |
| 0x18 | Give orders | |
| 0x19 | Take over protection | |
| 0x1a | Visit on location | |
| 0x1b | (special) | |
| 0x1c | (special, with cost) | |
| 0x1d | (special) | |
| 0x1e | (special) | |

### Objective Type Enum: `FUN_00678900`

A separate, broader enum for AI goals (60+ entries):

| ID | Name | ID | Name |
|----|------|----|------|
| 0 | No Type | 0x0f | Extort |
| 1 | Gain Money | 0x10 | Purchase Site |
| 2 | Set Up Legal | 0x11 | Legal Businesses |
| 3 | Buy Legal | 0x12 | Protection |
| 4 | Evade Tax | 0x13 | Dump Assets |
| 5 | Close Business | 0x14 | Sack People |
| 6 | Set Up Illegal | 0x15 | Sell Empty Land |
| 7 | Collect Protection | 0x16 | Make me Strong |
| 8 | Increase Protection | 0x17 | Make them Weak |
| 9 | Export Goods | 0x18 | Consolidate Territory |
| 0xa | Trade Goods | 0x19 | Become Sole Gang Leader |
| 0xb | Gain Favori(tism) | 0x1a | Attack Enemy |
| 0xc | Rob Business | 0x1b | Attack Buildings |
| 0xd | Sell Business | 0x1c | Attack People |
| 0xe | Expand Territory | 0x1d | Obtain Info |
| | | 0x1e | Recruit |
| | | 0x1f | Officials |
| | | 0x20 | FBI |
| | | 0x21 | Police |
| | | 0x22 | Court Cases |
| | | 0x23 | Mayor |
| | | 0x24 | Bribe |
| | | 0x25 | Donate |
| | | 0x26 | Police Chief |
| | | 0x27 | Recruit Lawyer |
| | | 0x2e | Buy Vehicles |
| | | 0x2f | Recruit Known |
| | | 0x36 | Attack Robbery |
| | | 0x37 | Attack Illegal Business |
| | | 0x38 | Attack Protection |
| | | 0x39 | Attack Legal |

### Steel City Design Implication

The dual-enum system (player orders vs. AI goals) is a smart architecture we
should preserve. Player-facing orders are simple verbs. AI goals are richer
strategic objectives. This separation lets the AI reason at a higher level
than the player's tactical orders.

---

## 3. Order Setup Function: `FUN_005b3440`

**Size**: 7,736 bytes | **Address**: `0x005b3440`

This is the **order setup dispatcher** — called when a player assigns an order
to a hood. It takes an order struct (`param_3`) and sets up the UI state.

### Structure

```c
void __thiscall FUN_005b3440(int param_1, int param_2, int *param_3)
```

- `param_1` — Game state/UI struct (very large, ~0x4000 bytes)
- `param_2` — Selected entity ID (or -1 for none)
- `param_3` — Order data struct:
  - `param_3[3]` (byte) — Order type (the switch key)
  - `param_3[4]` (short) — Target X coordinate
  - `param_3[0x12]` (short) — Target Y coordinate
  - `param_3[6]` — Target entity pointer (business/hood)
  - `param_3[7]` — Cost value
  - `param_3[2]` — Flags (bit 1 = stealth, bit 8 = driving, bit 9 = stealth flag)
  - `param_3[8]` — Recruit subtype
  - `param_3[0x11]` — Patrol type
  - `param_3 + 9` — Target area/zone data

### Key Behavior

Each order case:
1. Calls `thunk_FUN_00411f40()` — likely centers camera on target
2. Extracts target coordinates from `param_3`
3. Calls `thunk_FUN_00664d50(x, y)` — gets map cell at target
4. Validates cell type (`bVar4 != 0 && bVar4 < 0xB`)
5. Calls vtable method `*local_34 + 0x70` — stores target in order struct at `param_1 + 0x2df8`
6. Sets internal order type at `param_1 + 0x2dd0`
7. Sets order mode at `param_1 + 0x3170`:
   - **1** = hood-targeted order (extort, intimidate, etc.)
   - **2** = business-targeted order (give orders, take over)
   - **3** = special order (bribe, donate, etc.)
8. Calls `thunk_FUN_005b79b0()` — likely validates order feasibility
9. Calls `thunk_FUN_005b7ad0()` — likely updates UI

### Order Type → Internal ID Mapping

| Order Case (byte) | Internal ID (`0x2dd0`) | Mode | Order Name |
|-------------------|----------------------|------|-----------|
| 0x00 | 0x0C | 1 | Go to (location) |
| 0x01 | 0x08 | 1 | Rob business |
| 0x02 | 0x03 | 1 | Collect protection |
| 0x05 | 0x05 | 1 | Investigate (with level: Zero/Low/Normal/High/Very High) |
| 0x06 | 0x01 | 1 | Set up illegal (with cost) |
| 0x07 | 0x04 | 1 | Patrol (with area selection) |
| 0x08 | 0x07 | 1 | Drive / Guard business |
| 0x09 | 0x02 | 1 | Smash up (with patrol params) |
| 0x0C | 0x0A | 1 | Attack building (with area) |
| 0x0E | 0x09 | 1 | Intimidate (with area) |
| 0x0F | 0x0B | 1 | Recruit (with subtype selection) |
| 0x11 | 0x0D | 1 | Bribe (with cost) |
| 0x12 | 0x0F | 1 | Extort |
| 0x13 | 0x0E | 1 | Assault (with target entity) |
| 0x14 | 0x11 | 1 | Torch (with target entity) |
| 0x15 | 0x10 | 1 | Attack people |
| 0x17 | 0x17 | 1 | Break alliance (with area) |
| 0x18 | 0x13 | 1 | Kill (with target entity) |
| 0x19 | 0x15 | 1 | Kidnap (with stealth flag) |
| 0x1A | 0x16 | 1 | Ambush (with target entity) |
| 0x1B | 0x12 | 1 | Bomb (with stealth flag) |
| 0x1C | 0x14 | 1 | (Stealth variant) |
| 0x1F | 0x06 | 1 | Wait / Visit |
| 0x24 | 0x18 | 2 | Give orders (business-targeted) |
| 0x26 | 0x1A | 3 | (area selection, all cells) |
| 0x27 | 0x1B | 3 | (special) |
| 0x28 | 0x1C | 3 | (special, with cost) |
| 0x29 | 0x19 | 3 | Take over protection |
| 0x2A | 0x1E | 3 | (special) |
| 0x2B | 0x1D | 3 | (special) |

### Order Flags

From the flag field (`param_3[2]`):
- **Bit 1 (0x02)** — Stealth mode flag → stored at `param_1 + 0x2db5`
- **Bit 8 (0x100)** — Unknown flag → stored at `param_1 + 0x2db8`
- **Bit 9 (0x200)** — Stealth indicator → stored at `param_1 + 0x2db7`
- **Bit 2 (0x04)** — Another mode flag → stored at `param_1 + 0x2db6`

### Critical Observation

**No vehicle assignment logic exists in the order setup function.** The order
setup purely configures the order type, target, and flags. Vehicle usage must
be decided **during order execution** (the simulation tick), not during order
assignment. This is a key architectural insight.

---

## 4. NotEnough Error System: `FUN_005bbee0`

**Size**: 184 bytes | **Address**: `0x005bbee0`

Handles "not enough" error messages when an order can't be fulfilled.

### Decompiled Code

```c
void __thiscall FUN_005bbee0(int param_1, undefined4 param_2, char param_3)
{
  char *pcVar1;
  char *pcVar2;
  CHAR local_64[100];

  if (*(int *)(param_1 + 0x33ec) == -1) {
    switch(param_2) {
      // Cases for NotEnoughTime, NotEnoughCars, NotEnoughBombs,
      // NotEnoughGuns, NotEnoughLand, NotEnoughMoney, NotEnoughPeople
    }
  }
}
```

### Error Codes

| Error String | Likely Error Code | Meaning |
|-------------|-------------------|---------|
| NotEnoughTime | 0xFFFFFFFF | Hood's weekly time budget exhausted |
| NotEnoughCars | 0xFFFFFFFB / 0xFFFFFFFD | No vehicles available for order |
| NotEnoughBombs | 0xFFFFFFFA | No bombs available |
| NotEnoughGuns | 0xFFFFFFF4 | No guns available |
| NotEnoughLand | (varies) | No land available for purchase |
| NotEnoughMoney | (varies) | Insufficient funds |
| NotEnoughPeople | (varies) | Not enough hoods available |

### Critical Observation

The check `*(int *)(param_1 + 0x33ec) == -1` gates the error display. Offset
`0x33ec` likely represents the current player's identity — errors only show
for the human player, not AI players. This is a UI-only concern; the simulation
engine itself silently fails orders when resources are insufficient.

### Steel City Design Implication

Preserve the "NotEnough" pattern but **make it visible before order commitment**.
The original only tells you after the fact. Steel City should preview time/vehicle
costs before the player confirms an order.

---

## 5. Vehicle System

### Vehicle-Related Strings Found

| String | Address | Referenced By |
|--------|---------|--------------|
| `Graphics\VehicleAndStreetFurnitureSprites.dat` | 0x0079e1a4 | Data loading |
| `Graphics\VehicleScript.vs` | 0x0079e260 | Data loading |
| `Graphics\MEDMAP_VEHICLES.spr` | 0x0079e338 | Data loading |
| `graphics\VehicleScript.vs` | 0x007a485c | `FUN_005c1d80` |
| `graphics\MEDMAP_VEHICLES.spr` | 0x007a49bc | `FUN_005c20b0` |
| `graphics\VehicleAndStreetFurnitureSprites2.dat` | 0x007a4a64 | `FUN_005c1e80`, `FUN_005c2780` |
| `graphics\VehicleAndStreetFurnitureSprites.dat` | 0x007a4a94 | `FUN_005c1e80`, `FUN_005c2780` |
| `Buy Vehicles` | 0x007ba4b4 | `FUN_00678900` (objective names) |
| `Vehicle goal` | 0x007bf5bc | `FUN_00719f90` (debug display) |

### Vehicle Sprite Loading

The binary loads vehicle sprites with **memory-tier fallback**:

```c
// From FUN_005c1e80 / FUN_005c2780
GlobalMemoryStatus(&local_20);
if (local_20.dwTotalPhys < 0x1800000) {  // < 24MB RAM
    // Load low-res sprites (VehicleAndStreetFurnitureSprites2.dat)
} else {
    // Load high-res sprites (VehicleAndStreetFurnitureSprites.dat)
}
```

This is a 1998 memory optimization — the game had to run on 16MB systems.
Steel City doesn't need this, but the **vehicle variety** (trams, trains,
trucks, civilian cars, roadsters, police cars) is worth preserving.

### Vehicle Types (from debug display `FUN_00719f90`)

| Entity Type | Subtype | Vehicle Name |
|------------|---------|-------------|
| 8 | — | Tram |
| 9 | — | Train |
| 0xC | 8 | Truck |
| 0xC | 0x10 | Crate Truck |
| 0xC | 0x11 | Tarpauline Truck |
| 0xC | 0x12 | Van Truck |
| 0xD | 0 | Civilian Car |
| 0xD | 1 | Roadster |
| 0xD | 2 | Police Car |

### Vehicle-vs-Walk Decision: **SOLVED**

The vehicle assignment logic has been found in the movement state machine
functions. This is one of the most significant discoveries of the analysis.

#### The Vehicle Flag

**Bit 15 (`0x8000`) of the movement flags field** (at offset `0xc` of the
movement/AI struct) is the **vehicle use flag**. When set, the entity uses
a vehicle for travel. When clear, the entity walks.

#### The Speed Difference

From `FUN_004cb0c0` (line 617-625 of `ghidra_time_functions.txt`):

```c
if ((uVar1 & 0x8000) == 0) {
    // WALKING: consume full weekly time budget
    thunk_FUN_00565c30(0, 12000);
} else {
    // DRIVING: mark vehicle as used, consume only 32 ticks!
    if (param_1[0x30] != 0) {
        *(undefined1 *)(param_1[0x30] + 0x179) = 1;  // Mark vehicle used
    }
    thunk_FUN_00565c30(0, 0x20);  // 32 ticks instead of 12000
}
```

**Walking costs 12,000 ticks. Driving costs 32 ticks. That's a 375× speedup.**

This explains the user's original observation: a hood walking 10 blocks
consumed the entire weekly time budget (12,000 ticks), while driving would
have cost only 32 ticks — leaving 11,968 ticks for additional orders.

#### How the Vehicle Flag Gets Set

Two functions assign orders to individual hoods:

| Function | Flag Set | Meaning |
|----------|---------|---------|
| `FUN_004616d0` | `0x80` | **Walk** — standard order, no vehicle |
| `FUN_00462db0` | `0x80080` | **Drive** — sets bit 15 (vehicle) + bit 7 + bit 19 |

From `FUN_00462db0` (line 1418-1420):
```c
param_1[0x21] = uVar1 | 0x80;      // Set standard order flag
param_1[0x21] = uVar1 | 0x80080;   // Set vehicle flag (0x8000) + extra flags
```

From `FUN_004616d0` (line 1347-1348):
```c
param_1[0x21] = uVar1 | 0x80;      // Set standard order flag ONLY
```

The decision of which function to call is made **upstream** — likely in the
order setup based on whether the player has assigned a vehicle to the hood
and whether the order type supports driving.

#### Time Budget Functions

Three critical thunk functions form the time/movement system:

| Function | Signature | Purpose |
|----------|-----------|---------|
| `thunk_FUN_00565790(mode, time_budget, order_data)` | `(0, 12000, order)` | **Allocate time budget** for an order |
| `thunk_FUN_00565c30(mode, time_amount)` | `(0, 12000)` or `(0, 0x20)` | **Consume time** from the budget |
| `thunk_FUN_00564060(entity_pos, target_pos, flag1, flag2)` | `(pos, &target, 0, 0)` | **Set movement destination** |

The `thunk_FUN_00565c30` calls reveal the time cost hierarchy:

| Call | Time Cost | Context |
|------|-----------|---------|
| `thunk_FUN_00565c30(0, 12000)` | 12,000 ticks | Walking (full week budget) |
| `thunk_FUN_00565c30(0, 0x20)` | 32 ticks | Driving (vehicle flag set) |
| `thunk_FUN_00565c30(0, 0x10)` | 16 ticks | Street crossing / turning |
| `thunk_FUN_00565c30(0, random + 0x10)` | 16-47 ticks | Random wandering |
| `thunk_FUN_00565c30(1, 0)` | Reset | Clear time budget / order complete |

#### Vehicle Usage Tracking

When a vehicle is used, the game marks it at offset `0x179` of the vehicle
entity struct:

```c
if (param_1[0x30] != 0) {
    *(undefined1 *)(param_1[0x30] + 0x179) = 1;  // Vehicle is now "in use"
}
```

Offset `0x30` in the hood struct points to the assigned vehicle entity.
If null, the hood has no vehicle and must walk.

#### Steel City Design Implication

This is a **core game mechanic** that must be preserved:

1. **Walking consumes the entire weekly budget** — this is why a single
   distant order prevents any further orders that week
2. **Driving costs almost nothing** (32 ticks) — making vehicle assignment
   the most impactful strategic decision
3. **The 375× ratio is extreme** — Steel City should consider a less
   dramatic ratio (perhaps 10-20×) to keep vehicle choice meaningful
   without making it trivially dominant
4. **Vehicle assignment is a binary flag** — no partial driving, no
   "drive partway then walk." Steel City could improve with multi-leg
   journeys
5. **The vehicle is marked as "used"** — suggesting one vehicle per order
   per week, preventing reuse

---

## 6. Weekly Report System: `FUN_00596662`

**Size**: 6,660 bytes | **Address**: `0x00596662`

Processes end-of-week events for all gangs. Iterates through entity types:

| Type ID | Processing |
|---------|-----------|
| 0x14 | Death events ("The following people lost their lives") |
| 0x13 | Unknown weekly events |
| 6 | Loyalty/relationship events (with flag check `0x80`) |
| 9 | Gang strategy decisions |
| 0x0F | Extortion results |
| 0x10 | Protection collection results |
| 0x11 | Territory expansion results |
| 0x12 | Market share / business results |

### Key Observation

The weekly report is a **sequential pipeline** — each type is processed in
order, with results accumulated. This is not a parallel simulation. Steel City
should consider whether parallel processing or event-driven architecture would
produce more emergent behavior.

---

## 7. Debug Display System: `FUN_00719f90`

**Size**: 14,322 bytes | **Address**: `0x00719f90`

A debug overlay that displays entity information on screen. While not gameplay
code, it reveals the **entity type system**:

| Entity Type | Display Label |
|------------|--------------|
| 8 | Tram |
| 9 | Train (with position) |
| 0xC | Trucks (with subtype variants) |
| 0xD | Cars (civilian/roadster/police) |
| 0x10-0x24 | People (with status: Alive/Dying/Dead/Burning/Deleted) |
| 10 | Vehicle goal (AI navigation target) |

### Entity Status Codes

Extracted from the debug display's bitfield extraction (`(flags & 0x38000000) >> 0x1b`):

| Code | Status |
|------|--------|
| 0 | Alive |
| 1 | Dying |
| 2 | (Unknown — possibly "Injured") |
| 3 | Deleted |
| 4 | Burning |

### Steel City Design Implication

The "Vehicle goal" debug label confirms that vehicles have AI navigation
targets. This means the simulation tracks vehicle destinations separately
from hood destinations — vehicles are independent entities, not just
hood attachments.

---

## 8. Function Map (Key Discoveries)

### Identified Functions

| Function | Address | Size | Role |
|----------|---------|------|------|
| `FUN_00450f80` | 0x00450f80 | 22,216 | Game initialization — loads data, sets timing constants |
| `FUN_005b3440` | 0x005b3440 | 7,736 | **Order setup dispatcher** — maps order types to internal IDs |
| `FUN_005aa9b0` | 0x005aa9b0 | 265 | **Order name lookup** — switch(orderID) → string |
| `FUN_005bbee0` | 0x005bbee0 | 184 | **NotEnough error handler** — displays resource shortage messages |
| `FUN_00678900` | 0x00678900 | 1,004 | **AI objective name lookup** — 60+ AI goal types |
| `FUN_00719f90` | 0x00719f90 | 14,322 | Debug entity display — reveals entity types and vehicle subtypes |
| `FUN_00596662` | 0x00596662 | 6,660 | **Weekly report processor** — end-of-week event pipeline |
| `FUN_005b10e0` | 0x005b10e0 | 1,269 | Recruit UI handler — type selection (Known/Lawyer/Accountant) |
| `FUN_005b85f0` | 0x005b85f0 | 804 | **Order target validation** — checks location validity, updates UI |
| `FUN_005bb570` | 0x005bb570 | 147 | Recruit subtype lookup — returns recruit type name |
| `FUN_005c1d80` | 0x005c1d80 | 168 | Vehicle script loader (`VehicleScript.vs`) |
| `FUN_005c1e80` | 0x005c1e80 | 499 | Vehicle sprite loader (memory-tiered) |
| `FUN_005c20b0` | 0x005c20b0 | 1,428 | Map vehicle sprite loader (`MEDMAP_VEHICLES.spr`) |
| `FUN_005c2780` | 0x005c2780 | 476 | Vehicle furniture sprite loader (memory-tiered) |

### Time/Movement Functions (Decompiled)

These functions reference the 12000 time constant and form the **movement and
order execution engine**:

| Function | Address | Size | Role |
|----------|---------|------|------|
| `FUN_0049a530` | 0x0049a530 | 2,267 | **Gang order dispatch** — iterates hoods, allocates time budget |
| `FUN_004c8940` | 0x004c8940 | 204 | **Order state transition** — clears movement flags, triggers completion |
| `FUN_004cb0c0` | 0x004cb0c0 | 614 | **Vehicle-vs-walk decision** — checks `0x8000` flag, allocates time |
| `FUN_004cbd00` | 0x004cbd00 | 857 | **Hood walking state machine** — pathfinding, arrival detection |
| `FUN_00583dc0` | 0x00583dc0 | 1,652 | **Hood order execution** — registers at target, processes arrival |
| `FUN_005844a0` | 0x005844a0 | 538 | **Path traversal** — waypoint following with countdown timer |
| `FUN_005871c0` | 0x005871c0 | 205 | **Order cleanup** — clears state, triggers time consumption |
| `FUN_005dd870` | 0x005dd870 | 156 | **Order switch** — changes active order, resets time budget |
| `FUN_004616d0` | 0x004616d0 | 330 | **Walk order assignment** — sets `0x80` flag (no vehicle) |
| `FUN_00462db0` | 0x00462db0 | 288 | **Drive order assignment** — sets `0x80080` flag (vehicle) |
| `FUN_005ab500` | 0x005ab500 | 17,368 | **City grid scanner** — 5×5 block iteration for territory/Business |
| `FUN_005d2740` | 0x005d2740 | 16,980 | **Per-tick entity simulation** — movement, street crossing, wandering |

---

## 9. Movement State Machine

### Movement Flags Field (offset `0xc` of movement struct)

The movement/AI struct uses a bitfield at offset `0xc` to track state:

| Bits | Mask | Meaning |
|------|------|---------|
| 0 | `0x0001` | **Movement complete** — set when destination reached |
| 4 | `0x0010` | Pathfinding requested |
| 7 | `0x0080` | Order active flag |
| 10 | `0x0400` | Movement in progress |
| 11 | `0x0800` | Pathfinding complete / waypoint following |
| 12-14 | `0x7000` | Movement mode sub-flags |
| 15 | `0x8000` | **VEHICLE FLAG** — set = driving, clear = walking |
| 10-14 | `0x7C00` | **State machine state** (shifted right 10) |

### State Machine States

Extracted from `(flags & 0x7C00) >> 10`:

| State | Name | Description |
|-------|------|-------------|
| 0 | **Init** | Set destination, request pathfinding |
| 1 | **Pathfinding** | Path found, begin traversal. Check `0x8000` for vehicle vs walk |
| 2 | **Walking** | Following waypoint path, decrementing timer |
| 3 | **Arrived** | At destination, trigger order execution |

### State 0: Init

From `FUN_00583dc0` and `FUN_004cb0c0`:
- Sets destination coordinates from order data
- Calls `thunk_FUN_00609cf0()` to check if path exists
- If no path: sets complete flag (`| 1`)
- If path exists: transitions to state 1 (`| 0x800`)

### State 1: Pathfinding Complete

From `FUN_004cb0c0` (the critical vehicle decision):
```c
if ((uVar1 & 0x8000) == 0) {
    // WALKING
    thunk_FUN_00565c30(0, 12000);  // Consume full week budget
} else {
    // DRIVING
    *(param_1[0x30] + 0x179) = 1;  // Mark vehicle used
    thunk_FUN_00565c30(0, 0x20);   // Consume 32 ticks only
}
```

### State 2: Walking/Waypoint Following

From `FUN_005844a0`:
- Countdown timer at offset `+8` of movement struct
- Timer decremented by global speed value at `DAT_007c0024 + 0x16d8`
- When timer reaches 0, advance to next waypoint:
  - Waypoint array at `puVar5[7]` (8 bytes per waypoint)
  - Waypoint count at `puVar5[6]`
  - Current waypoint index at offset `+0x20`
  - Each waypoint: X at `+0x3e`, Y at `+0x3f`
- When all waypoints exhausted: set complete flag

### State 3: Arrived

From `FUN_00583dc0`:
- Compares current position to target position
- If matched: triggers order execution via vtable `+4`
- If not matched: resets to pathfinding state

---

## 10. Per-Tick Entity Simulation: `FUN_005d2740`

**Size**: 16,980 bytes | **Address**: `0x005d2740`

This is the **main per-tick simulation function** for entities (hoods,
civilians, vehicles). It runs every game tick and processes:

### Timer Decrement

Decrements countdown timers at offsets:
- `+0x17` — Primary action timer
- `+0x5d` — Secondary timer (possibly combat)
- `+0x5e` — Tertiary timer (possibly status effect)
- `+0x5f` — Quaternary timer (possibly animation)

### Movement Processing

Uses the same `(flags & 0x7C00) >> 10` state machine:

| State | Behavior |
|-------|----------|
| 0 | Call vtable `+0x84` (idle/update) |
| 1 | Random movement: `thunk_FUN_00712500()` (RNG). If `& 7 == 0`, do pathfinding. Otherwise move with `thunk_FUN_00568870()` |
| 2 | Random wandering: `thunk_FUN_00565c30(0, random + 0x10)` — costs 16-47 ticks |
| 3 | Order execution at destination: `thunk_FUN_00606dc0()` + vtable `+4` |

### Street Crossing Logic

The function checks street direction data at `DAT_007c0024 + 0x1220` to
determine valid crossing directions. The switch on `param_1[0x1b]` (entity
direction byte) handles:

| Direction Value | Crossing Type |
|----------------|--------------|
| 0, 8 | Horizontal crossing (E/W) |
| 1, 9 | Vertical crossing (N/S) |
| 2, 10 | Diagonal NE/SW |
| 3, 11 | Diagonal NW/SE |
| 4-7, 12-15 | Intersection (all directions) |

`thunk_FUN_005dc8c0(direction, position, target)` processes the actual
street crossing, with `thunk_FUN_00565c30(0, 0x10)` charging 16 ticks per
crossing.

### Animation Frame Advancement

At offset `+0x11`, a counter increments each tick. When `(counter & 0xF) == 0`,
the function calls vtable `+0xA0` and `+0x58` to update animation frames.

### Speed/Frame Data Lookup

The function uses a 2D lookup table at:
```
DAT_007c0024 + 0x858 → table base
  + 8 + (movement_type * 0xC) → row
  + 4 + (frame_index * 0x10) → column
```
This table stores animation frame counts and speeds for each movement type.

---

## 11. Gang Order Dispatch: `FUN_0049a530`

**Size**: 2,267 bytes | **Address**: `0x0049a530`

This function dispatches orders to an entire gang. It processes the gang
leader first, then iterates through all hoods in the gang's linked list.

### Order Priority Calculation

```c
// Base priority from distance and randomness
uVar8 = 0x40;
if (iVar7 - 1 <= local_16) {
    uVar8 = ((local_16 <= iVar7 + 3) - 1) & 0xFFFFFFC0;
}
uVar3 = thunk_FUN_00712500();  // Random number
iVar7 = uVar8 + (uVar3 & 0x1F) + hood_level - 0x10;
```

Priority is influenced by:
- Distance to target (`local_16` vs `iVar7`)
- Random factor (`& 0x1F` = 0-31)
- Hood level/stat at `param_1 + 0x20`
- Base value of 0x40 (64) or -64 depending on proximity

### Priority-Based Dispatch

| Priority Range | Action |
|---------------|--------|
| `< 0x14` (20) | **Immediate** — call vtable `+0x14C` to execute now |
| `0x14 - 0x80` | **Queued** — standard order processing |
| `> 0x80` (128) | **Special** — trigger animation, set order type-specific flags |

### Special Order Types (priority > 128)

| Order Byte | Animation | Behavior |
|-----------|-----------|----------|
| `0x12` | `thunk_FUN_00480e30` + vtable `+0x88(5,2,0x18,0)` | Extort variant |
| `0x14` | `thunk_FUN_00480e30` + vtable `+0x88(5,2,0x18,0)` | Torch variant |
| `0x1C, 0x1F` | `thunk_FUN_004808c0` + vtable `+0x88(5,2,0x19,0)` | Bomb/Kidnap variant |

### Time Budget Allocation

For each hood in the gang:
```c
thunk_FUN_00565790(0, 12000, param_2);  // Allocate 12000 ticks
thunk_FUN_00564060(hood_pos, &target, 0, 0);  // Set destination
```

The `0x60` multiplier appears in coordinate calculations:
```c
iVar7 = (int)*(char *)(param_1 + 9) + *(short *)(param_1 + 6) * 0x60;
```
This suggests **0x60 (96) pixels per block** — the block size in the game's
coordinate system.

---

## 12. Architectural Insights for Steel City

### What the Binary Confirms

1. **Separation of order setup and execution** — Orders are configured in the UI
   layer (`FUN_005b3440`) and executed later in the simulation tick. This is a
   clean architecture worth preserving.

2. **Weekly time budget is per-hood, not per-gang** — The 12000 constant is
   applied individually. Each hood has their own time pool.

3. **Vehicle flag is binary, not proportional** — Bit 15 of movement flags.
   When set, travel costs 32 ticks. When clear, travel costs 12,000 ticks.
   There is no proportional speed based on distance. **Walking always costs
   the full budget regardless of distance.** This is a key design decision.

4. **Dual enum system** — Player orders (simple verbs) vs. AI goals (strategic
   objectives). The AI reasons at a higher abstraction level than the player.

5. **Memory-tiered asset loading** — 1998 constraint, but shows the engine was
   designed for scalability. Modern equivalent: LOD systems.

6. **Sequential weekly processing** — End-of-week events run in a fixed pipeline
   order. This creates deterministic outcomes but limits emergence.

7. **Pathfinding uses waypoints** — The movement system follows a pre-computed
   path of waypoints, with a per-tick countdown timer controlling speed.
   The global speed value at `DAT_007c0024 + 0x16d8` determines how fast the
   timer counts down.

8. **Street crossing is a separate state** — Each street crossing costs 16
   ticks (`0x10`), and the direction system supports 16 directional values
   including diagonals and intersections.

9. **Block size is 0x60 (96) pixels** — Coordinates are multiplied by 0x60
   to convert from block indices to pixel positions.

10. **Order priority includes randomness** — The dispatch function adds a
    random 0-31 factor to the priority calculation, creating unpredictable
    execution order even for identical setups.

### What This Means for the "Love Letter"

The original game's **hidden sophistication** is in:
- The time budget system (creates the core tension)
- The vehicle-vs-walk binary (375× speed difference creates dramatic strategic impact)
- The order type separation (player verbs vs. AI strategy)
- The waypoint pathfinding system (pre-computed paths with tick-based traversal)
- The street crossing micro-cost (16 ticks per crossing adds up over distance)
- The entity type system (vehicles as independent agents, not attachments)

For Steel City, we should:
- **Preserve** the weekly time budget and per-hood allocation
- **Preserve** the dual enum system (player orders vs. AI goals)
- **Preserve** the vehicle-as-strategic-resource design (walking is expensive)
- **Improve** by showing time/vehicle costs *before* order commitment
- **Improve** by making vehicle assignment a player decision, not a hidden sim check
- **Improve** by using proportional travel time (distance-based, not flat 12000)
- **Improve** by parallelizing the weekly pipeline for richer emergence
- **Preserve** vehicle variety (trams, trains, trucks, cars, roadsters, police cars)
- **Preserve** the waypoint pathfinding concept (pre-computed paths)
- **Consider** a less extreme vehicle speed ratio (10-20× instead of 375×)

---

## 13. Engine Core Deep Dive

Decompilation of the three critical thunk functions and 47 supporting functions
reveals the complete time/movement architecture.

### 13.1 Time Budget System: Command Queue, Not Immediate

**Critical discovery**: `FUN_00565790` (time allocation) and `FUN_00565c30`
(time consumption) do **not** immediately modify a time counter. Instead, they
both **enqueue commands** onto a doubly-linked list for later processing.

#### Shared Command Queue Structure

Both functions use the same pattern:
1. Allocate node from pool at `DAT_0079d598`
2. Zero-fill 40 bytes (10 dwords)
3. Call `thunk_FUN_005614d0(type)` to initialize node
4. Set command type at offset `+10`
5. Set time amount at offset `+8`
6. Set order data at offset `+0xC` (allocation only)
7. Append to linked list at `param_1+4` (count), `param_1+8` (head), `param_1+0xC` (tail)

| Function | Node Type | Stores | Purpose |
|----------|-----------|--------|---------|
| `FUN_00565790` | `0xC` | time_amount, order_data | **Allocate** time budget for an order |
| `FUN_00565c30` | `0x01` | time_amount | **Consume** time from the budget |

The queue uses:
- `param_1 + 4` (short) — Queue count
- `param_1 + 8` (ptr) — Head pointer (most recent)
- `param_1 + 0xC` (ptr) — Tail pointer (oldest)
- Node `+0x20` (ptr) — Previous node
- Node `+0x24` (ptr) — Next node

**Implication**: The game processes time commands in order during simulation
ticks, not at order assignment time. This means `NotEnoughTime` would fire
during tick processing, not during order setup — confirming the "deferred
validation" pattern.

### 13.2 Walk vs Drive: Critical Differences

Side-by-side comparison of the two assignment functions reveals key behavioral
differences beyond just the vehicle flag.

#### Walk Function (`FUN_004616d0`)

```c
uVar1 = param_1[0x21];
if ((uVar1 & 0x80) == 0) {           // ONLY if no active order
    param_1[0x29] = param_2;          // Store order data
    param_1[0x21] = uVar1 | 0x80;     // Set order active
    if ((uVar1 & 0x1f) != 0xf) {      // If NOT recruit (type 0xF)
        thunk_FUN_00480e30(param_1);   // → Arrest check animation!
    }
    // ... create movement struct, set destination ...
    thunk_FUN_00565790(0, 12000, param_1[0x29]);
    thunk_FUN_00564060(param_1 + 1, &local_20, 0, 0);
}
```

#### Drive Function (`FUN_00462db0`)

```c
param_1[0x29] = param_2;              // Store order data (NO guard!)
uVar1 = param_1[0x21];
param_1[0x21] = uVar1 | 0x80;         // Set order active
param_1[0x21] = uVar1 | 0x80080;      // Set vehicle flag (0x8000)
// NO arrest check animation!
// ... create movement struct, set destination ...
thunk_FUN_00565790(0, 12000, param_1[0x29]);
thunk_FUN_00564060(param_1 + 1, &local_20, 0, 0);
```

#### Key Differences

| Behavior | Walk (`FUN_004616d0`) | Drive (`FUN_00462db0`) |
|----------|----------------------|----------------------|
| Guard against active order | Yes — checks `& 0x80 == 0` | No guard — can overwrite |
| Arrest check animation | Yes — for non-recruit orders | Skipped entirely |
| Vehicle flag (0x8000) | Not set | Set |
| Time budget allocated | 12,000 | 12,000 (same) |
| Movement destination | Same | Same |

**This answers the extort-vs-recruit question**:

1. **Extort orders call the walk function** — which triggers the arrest check
   animation (`thunk_FUN_00480e30`). Walking hoods are vulnerable to arrest
   en route.

2. **Recruit orders (type 0xF) skip the arrest check** even when walking —
   the `if ((uVar1 & 0x1f) != 0xf)` guard prevents it.

3. **Drive orders skip the arrest check entirely** — driving makes hoods
   immune to the arrest animation. Not just speed, but **safety**. Driving
   hoods can't be arrested en route.

4. **The walk function refuses to assign if an order is already active**
   (`& 0x80` guard). The drive function has no such guard — it can
   **preempt** existing orders.

### 13.3 Pathfinding System

#### Movement Setup (`FUN_00564060`, 605 bytes)

The pathfinding entry point handles two cases:

**Case 1: Same position** (source == destination):
- Calls `thunk_FUN_00565120(param_1, param_2, flags, 0)` — direct movement

**Case 2: Different positions**:
1. Save source and destination as 6-byte position structs
2. Check if source is a valid street via `thunk_FUN_006064c0()`
3. If not valid: try `thunk_FUN_00606450()` (alt check), then `thunk_FUN_00608140()` (position fix)
4. If valid: check territory via `thunk_FUN_005c4610()`
5. Check if destination is within range:
   - `thunk_FUN_0060ac90(param_2)` — X distance
   - `thunk_FUN_0060acc0(param_2)` — Y distance
   - Both must be < `0x1E0` (480 pixels = 5 blocks)
6. If within range: `thunk_FUN_0060c3c0()` validation, then direct movement
7. If out of range: **multi-segment pathfinding**:
   - `thunk_FUN_00609130(param_2)` — check if destination needs pathfinding
   - `thunk_FUN_00565120(&local_28, param_2, flags, 1)` — path to destination segment
   - `thunk_FUN_005642c0(&local_20, &local_28, flags, param_4, 1)` — main pathfinding
   - `thunk_FUN_00565120(param_1, &local_20, flags, 1)` — path from source to first waypoint

**Direct movement range: 480 pixels (5 blocks).** Beyond that, multi-segment
pathfinding kicks in.

#### Street Access Check (`FUN_00609cf0`, 571 bytes)

Checks if a position has accessible streets in any of 4 cardinal directions.
Uses RNG to pick starting direction, then iterates all 4:

```c
start_dir = RNG() % 4;
for (i = 0; i < 4; i++) {
    dir = (start_dir + i) % 4;
    switch(dir) {
        case 0: dx = -1, dy =  0; break;  // West
        case 1: dx =  0, dy =  1; break;  // South
        case 2: dx =  0, dy = -1; break;  // North
        case 3: dx =  1, dy =  0; break;  // East
    }
    // Check adjacent cell: must be road (type 0) with access flag
    // Check road network data at DAT_007c0024 + 0x1220
    road_id = *(byte *)(piVar4 + 0x29);
    if (DAT_007c0024[0x1220 + road_id*4] != 0 ||  // West open
        DAT_007c0024[0x1221 + road_id*4] != 0 ||  // East open
        DAT_007c0024[0x1222 + road_id*4] != 0 ||  // North open
        DAT_007c0024[0x1223 + road_id*4] != 0)    // South open
        return 1;  // Street accessible!
}
```

#### Road Network Data (`DAT_007c0024 + 0x1220`)

The road network is stored as a 4-byte array per road segment:

| Byte | Offset | Direction |
|------|--------|-----------|
| 0 | `+0x1220` | West open |
| 1 | `+0x1221` | East open |
| 2 | `+0x1222` | North open |
| 3 | `+0x1223` | South open |

Each road cell has a `road_id` at offset `0x29` that indexes into this array.
A road value of `0x36` (54) means **no through access** (checked in
`FUN_00609060`).

#### Position System

Positions use a 6-byte struct:

| Offset | Type | Field |
|--------|------|-------|
| +0 | short | Block X |
| +2 | short | Block Y |
| +4 | char | Sub-block X (0-95 pixels) |
| +5 | char | Sub-block Y (0-95 pixels) |

When sub-block values exceed 0x5F (95), they wrap to the next block:
```c
if (sub_x > 0x5F || sub_x < 0) {
    total = (block_x + 200) * 0x60 + sub_x;
    block_x = total / 0x60 - 200;
    sub_x = total % 0x60;
}
```

The `+200` offset prevents negative intermediate values during wrapping.

#### Distance Calculation

Pixel-exact Manhattan distance:
```c
// X distance (FUN_00609b30)
delta_x = (target_block_x - source_block_x) * 0x60 + target_sub_x - source_sub_x;

// Y distance (FUN_00609b70)
delta_y = (target_block_y - source_block_y) * 0x60 + target_sub_y - source_sub_y;
```

Returns: `1` if positive, `-1` if negative, `0` if zero.

### 13.4 Street Crossing (`FUN_005dc8c0`, 1312 bytes)

Handles crossing streets in 4 directions (cases 0-3 = N/S/E/W). Each direction:
1. Gets the current cell and checks if it's a road (type 0)
2. Checks road direction flags at `piVar7[0xB]` (offset 0x2C)
3. Iterates up to **6 cells** in the crossing direction
4. At each cell: checks if we've reached the target position
5. Checks road network data for valid crossing
6. Returns 1 if crossing is possible, 0 if blocked

The 6-cell maximum means crossings are limited to 6-lane roads wide.

### 13.5 Random Number Generator (`FUN_00712500`)

Classic linear congruential generator (Windows MINSTD):
```c
DAT_009008e4 = DAT_009008e4 * 0x343fd + 0x269ec3;
return (DAT_009008e4 & 0x7fff0000) >> 0x10;
```

Produces 15-bit values (0-32767). Used everywhere for:
- Pathfinding direction selection (`% 4`)
- Random wandering distance (`% 10` for weighted distance)
- AI state transitions (`& 0x3F`, `& 0x7F`)
- Street crossing triggers (`& 0x7F == 0` → 1/128 chance)

### 13.6 Map Cell Lookup (`FUN_00664d50`)

The most-called function in the engine. Uses a **5x5 block grid**:

```c
if (x < 0 || y < 0 || x >= width || y >= height) return 0;
block_x = x / 5;
block_y = y / 5;
sub_x = x % 5;
sub_y = y % 5;
return *(int*)(*(int*)(*param_1 + (block_y * stride + block_x) * 4) +
               (sub_y * 5 + sub_x) * 4);
```

The map is organized as **5x5 blocks** containing 25 cells each.

### 13.7 Order Queue System

Three functions manage the order queue:

| Function | Operation | Queue Location |
|----------|-----------|---------------|
| `FUN_00563150` | **Pop front** (oldest) | `param_1 + 8` (head) |
| `FUN_00563190` | **Peek front** (read only) | `param_1 + 8` (head) |
| `FUN_005691a0` | **Pop back** (newest) | `param_1 + 4` (tail) |

The queue is a doubly-linked list:
- Node `+0x20` (ptr) — Previous (toward tail)
- Node `+0x24` (ptr) — Next (toward head)
- Node `+0x68` (ptr) — Previous (in hood list)
- Node `+0x6C` (ptr) — Next (in hood list)

`FUN_00568f20` adds nodes to the queue with duplicate detection.
`FUN_005631b0` clears the entire queue by iterating and freeing each node.

### 13.8 Order Completion Processing (`FUN_004c5d70`)

When a hood completes an order:

1. Clear flag `0x10` at `param_1[0x54]` (order complete)
2. Set flag `0x02` at `param_1[0x2E]` (processing)
3. Iterate through all hoods in the list, setting `0x02` flag at each hood's `+0xB8`
4. Check movement struct at `param_1[0x15]`:
   - If flag `0x20` (has movement) is set:
     - If flag `0x80` (order active) is clear AND order type != 0xF:
       - Call `thunk_FUN_00481860` (arrest check)
     - If flag `0x80` is clear OR flag `0x8000` (vehicle) is set:
       - If `param_1[0x30]` (vehicle pointer) != 0:
         - Call `thunk_FUN_00495770` (return vehicle)
5. Switch on movement type (offset `+0x10`):
   - Cases 1,2,6-9,10,12,14,15,17-28,31: trigger animation `0x14`
   - Other cases: no animation

**Vehicle return logic** (line 2507):
```c
if ((((uVar2 & 0x80) == 0) || ((uVar2 & 0x8000) != 0)) && (param_1[0x30] != 0)) {
    thunk_FUN_00495770(param_1[0x15] + 4, param_1);
}
```

The vehicle is returned after completion if: (walking OR has vehicle flag)
AND has vehicle pointer. The `0x8000` check ensures that even if the order
used a vehicle, the vehicle is returned after completion.

### 13.9 Animation System

#### Animation Lookup (`FUN_0048a750`)

Uses a 2D table at `param_1 + 4`:
- Row stride: `0x66` (102) bytes
- 13 entries per row (`0xD`)
- Each entry: 2 bytes (ushort)
- `0xFFFF` means "use fallback"

The function searches for a valid animation frame using direct lookup, then
falls back to iterating all 13 entries with a hash: `iVar5 * 0x33 + param_3`.

#### Animation Frame Data

Speed/frame data stored at:
```
DAT_007c0024 + 0x858 → table base
  + 8 + (movement_type * 0xC) → row
  + 4 + (frame_index * 0x10) → column
```

The frame counter at entity offset `+0x0E` indexes into this table.
When the frame counter exceeds the table's max frame, it resets to 0.

### 13.10 AI State Machine (`FUN_005e0560`)

Controls entity behavior states at offset `+0x11`:

| State | Name | Trigger |
|-------|------|---------|
| 2 | **Idle/Wander** | Default state, RNG-based transitions |
| 4 | **Alert** | `RNG & 0x3F == 0` and no flags |
| 5 | **Suspicious** | `RNG & 0x3F < 10` or alert trigger |
| 6 | **Combat** | `param_1+0x60 == 0x03` and no bit 0 |
| 7 | **Fleeing** | Combat state with RNG check |
| 8 | **Order Execution** | `param_1+0x58 == 8` |
| 0x13 | **Dying** | Terminal state |
| 0x14 | **Dead** | Terminal state |

State transitions use RNG with varying probabilities:
- 1/64 chance to enter alert state
- ~15% chance to enter suspicious state
- Combat triggers based on `param_1+0x60` value

### 13.11 Entity Search (`FUN_005dd9d0`)

Searches for entities in a rectangular area around a position:
1. Use RNG to pick search direction (X-first or Y-first)
2. Use RNG to pick scan direction (+1 or -1 for each axis)
3. Calculate search rectangle: `position ± param_2` (search radius)
4. Iterate through cells, check for road type, find entity
5. Return first entity found

### 13.12 Arrest and Kidnap Messages

`FUN_00480e30` generates arrest messages:
- Format: `"%s %s %s Location: %s Message: I've been arrested!"`
- Creates message object with type 6

`FUN_004808c0` generates kidnap messages:
- Format: `"%s %s %s Location: %s Message: I've been kidnapped!"`
- Creates message object with type 3

Both check `(param_2[0x21] & 0x1f) > 3` — only hoods with order priority > 3
send messages. This prevents low-level hoods from spamming messages.

---

## 14. Thunk Caller Trace Analysis

Source: `ghidra_thunk_callers.txt` (output of `TraceThunkCallers.java`)

### 14.1 Walk Thunk Callers (thunk_FUN_004616d0 @ 0x0040356c)

**4 total references, 1 unique calling function.**

| Call Site | Function | Notes |
|-----------|----------|-------|
| 0x00761e25 | `FUN_00761e00` (575 bytes) | Primary walk dispatcher |
| 0x0077e53c | UNKNOWN (size 0) | Likely data or unanalyzed |
| 0x0078152c | UNKNOWN (size 0) | Likely data or unanalyzed |
| 0x004c0ee5 | UNKNOWN (size 0) | Likely data or unanalyzed |

**`FUN_00761e00` — Walk Dispatcher:**
- Calls `thunk_FUN_004616d0(param_2)` to initiate walking
- If walk returns 0 (failure), returns 0 immediately
- If walk succeeds, registers an entity from `param_1 + 0xec` into the global
  linked list at `DAT_007c0024 + 0x24`
- Uses the standard linked-list insertion pattern (same as drive dispatcher)
- Clears `param_1 + 0xec` after registration
- Returns 1 on success

### 14.2 Drive Thunk Callers (thunk_FUN_00462db0 @ 0x00404df9)

**5 total references, 2 unique calling functions.**

| Call Site | Function | Notes |
|-----------|----------|-------|
| 0x007620a5 | `FUN_00762080` (571 bytes) | Primary drive dispatcher |
| 0x004c1165 | `FUN_004c1140` (572 bytes) | In-order vehicle assignment |
| 0x0077e568 | UNKNOWN (size 0) | Likely data or unanalyzed |
| 0x00781558 | UNKNOWN (size 0) | Likely data or unanalyzed |
| 0x006f9b8e | UNKNOWN (size 0) | Likely data or unanalyzed |

**`FUN_00762080` — Drive Dispatcher:**
- Structurally identical to `FUN_00761e00` (walk dispatcher)
- Calls `thunk_FUN_00462db0(param_2)` to initiate driving
- Checks return value is `'\x01'` (char 1 = success)
- Same entity registration pattern into `DAT_007c0024 + 0x24` linked list
- Clears `param_1 + 0xec` after registration

**`FUN_004c1140` — In-Order Vehicle Assignment:**
- Also 572 bytes, similar structure to the dispatchers
- Called from within the order processing pipeline to upgrade movement to driving
- This is the function that enables vehicles for orders that support them
- Represents the "mid-order vehicle upgrade" path

### 14.3 Walk vs Drive Decision Architecture

```
                    ┌─────────────────┐
                    │ Order Dispatch  │
                    │   (caller)      │
                    └────┬───────┬────┘
                         │       │
              ┌──────────┘       └──────────┐
              ▼                              ▼
    ┌──────────────────┐          ┌──────────────────┐
    │ FUN_00761e00     │          │ FUN_00762080     │
    │ (Walk Dispatcher)│          │ (Drive Dispatcher)│
    │ Calls walk thunk │          │ Calls drive thunk │
    └────────┬─────────┘          └────────┬─────────┘
             │                             │
             ▼                             ▼
    ┌──────────────────┐          ┌──────────────────┐
    │ FUN_004616d0     │          │ FUN_00462db0     │
    │ Walk Assignment  │          │ Drive Assignment  │
    │ - No vehicle flag│          │ - Sets 0x8000    │
    │ - Arrest check   │          │ - No arrest check│
    │ - 12000 ticks    │          │ - 12000 ticks    │
    └──────────────────┘          └──────────────────┘
                                          ▲
                               ┌──────────┘
                               │
                    ┌──────────────────┐
                    │ FUN_004c1140     │
                    │ (In-Order Drive)  │
                    │ Mid-order upgrade │
                    └──────────────────┘
```

**Key Insight:** The walk and drive dispatchers (`FUN_00761e00` and
`FUN_00762080`) are parallel functions with identical post-processing logic.
The caller of these functions decides which one to invoke based on the order
type. `FUN_004c1140` provides an additional path for upgrading to vehicles
during order execution.

### 14.4 Time Budget Allocation Callers (thunk_FUN_00565790 @ 0x00402db0)

**25 total references, 12 unique calling functions.**

| Function | Size | Time Budget | Context |
|----------|------|-------------|---------|
| `FUN_00462b10` | 211 | 12000 | Movement setup helper |
| `FUN_00462db0` | 288 | 12000 | Drive assignment |
| `FUN_004616d0` | 330 | 12000 | Walk assignment |
| `FUN_0049a530` | 2267 | 12000 | Gang order dispatch (per hood) |
| `FUN_005dd910` | 178 | RNG 0–127 | AI spontaneous order |
| `FUN_005dd870` | 156 | 12000 | Order assignment (via vehicle) |
| `FUN_00583dc0` | 1652 | 12000 | Business/building order |
| `FUN_00746950` | 1733 | 12000 | Alt entity order processing |
| `FUN_004d1200` | 3471 | 0xa6 (166) | Order state machine |
| `FUN_004d45b0` | 6436 | 0x50 (80) | Complex order processing |
| `FUN_00466240` | 757 | 1 | Patrol/wander |
| `FUN_004c56e0` | 287 | — | Movement setup |

**Time budget varies by order type:**
- **12000 ticks**: Standard orders (walk, drive, gang, business)
- **166 ticks**: Specific sub-order in `FUN_004d1200` (likely a quick action)
- **80 ticks**: Action execution in `FUN_004d45b0`
- **1 tick**: Patrol/wander — minimal time per patrol step
- **RNG 0–127**: AI spontaneous orders — randomized short-duration tasks

### 14.5 Time Consumption Callers (thunk_FUN_00565c30 @ 0x0040124e)

**156 total references, 67 unique calling functions.**

This is the most widely called thunk — time consumption happens at nearly every
game tick across all order types. Key caller groups:

- **Order state machines**: `FUN_004d1200`, `FUN_004d45b0`, `FUN_004d2950`,
  `FUN_004d3290`, `FUN_004d3ab0`, `FUN_004d6e40`, `FUN_004d6060`
- **AI behavior**: `FUN_005d2740` (16980 bytes — the largest function),
  `FUN_005df4c0`, `FUN_005de990`
- **Movement**: `FUN_00462f30`, `FUN_004c3dd0`, `FUN_004e2240`
- **Combat**: `FUN_004cb870`, `FUN_004cbd00`, `FUN_004cc070`, `FUN_004cc470`

Most calls use `thunk_FUN_00565c30(1, 0)` — likely a "tick" or "consume zero"
call that advances the state without spending budget. The walk function uses
`thunk_FUN_00565c30(0, 12000)` for walking cost and `thunk_FUN_00565c30(0, 0x20)`
(32 ticks) for driving cost.

### 14.6 Movement Destination Callers (thunk_FUN_00564060 @ 0x00407068)

**74 total references, 26 unique calling functions.**

Key callers include all major order and movement functions:

| Function | Context | Call Pattern |
|----------|---------|--------------|
| `FUN_004616d0` | Walk assignment | `(entity+1, &coords, 0, 0)` |
| `FUN_00462db0` | Drive assignment | `(entity+1, &coords, 0, 0)` |
| `FUN_00462b10` | Movement helper | Two calls with coords |
| `FUN_0049a530` | Gang dispatch | `(entity+1, &coords, 0, 0)` per hood |
| `FUN_005dd910` | AI order | `(entity+1, &coords, (RNG&7)==0, 0)` |
| `FUN_005dd870` | Vehicle order | `(entity+1, &coords, 1, 0)` |
| `FUN_004e2240` | Group movement | `(entity+1, &coords, 1, 0)` per member |
| `FUN_004c56e0` | Pathfinding init | Two calls (to/from waypoints) |
| `FUN_00466240` | Patrol | Two calls (position swap) |
| `FUN_005df4c0` | AI state machine | 15 call sites — many state transitions |
| `FUN_005de990` | AI behavior | 6 call sites |
| `FUN_004d45b0` | Complex orders | 1 call for approach movement |
| `FUN_004d6060` | Order processing | 1 call for movement |

**Third parameter significance:** When `1`, the destination may require
pathfinding (indirect route). When `0`, direct movement is attempted first.
`FUN_005dd910` uses `(RNG & 7) == 0` — randomly choosing direct vs pathfinding.

### 14.7 Pathfinding Thunk Results

All pathfinding thunk addresses searched returned **0 references**:

| Thunk Address | Function | References |
|---------------|----------|------------|
| 0x004064c0 | `thunk_FUN_006064c0` | 0 |
| 0x00406450 | `thunk_FUN_00606450` | 0 |
| 0x004071e0 | `thunk_FUN_00609130` | 0 |
| 0x00409130 | `thunk_FUN_00609170` | 0 |
| 0x00408140 | `thunk_FUN_00608140` | 0 |
| 0x0040ac90 | `thunk_FUN_0060ac90` | 0 |
| 0x0040acc0 | `thunk_FUN_0060acc0` | 0 |
| 0x0040c3c0 | `thunk_FUN_0060c3c0` | 0 |

This indicates these functions are called via **indirect calls** (function
pointers or vtable entries) rather than direct calls. The Ghidra reference
search only finds direct references. These pathfinding functions are likely
invoked through a function pointer table or virtual method dispatch.

### 14.8 Order State Machine Architecture

The thunk caller analysis reveals a consistent state machine pattern across
all order processing functions:

**State field:** `entity[0x15] + 0xc` (flags word, 32-bit)

**State extraction:** `(flags & 0x7c00) >> 10` — 3-bit state (0–7)

| State | Meaning | Flag Pattern |
|-------|---------|-------------|
| 0 | Approach/move to target | Sets `0x400` (moving) |
| 1 | Execute action at target | Sets `0x800` (at target) |
| 2 | Direct action (no movement) | Sets `0xc00` (acting) |
| 3 | Countdown/waiting | Sets `0x1000` (waiting) |
| 4 | Special: animation | Sets `0x1400` |
| 5 | Special: return to base | Sets `0x1800` |
| 6 | Special: at-base action | Sets `0x1c00` |

**Completion flag:** `0x01` in flags word indicates order complete.

**Order type byte:** `*(order_data + 10)` determines sub-behavior within each state:

| Order Type | Behavior |
|------------|----------|
| 0x0f, 0x18, 0x19, 0x23 | Movement orders — approach target coordinates |
| 0x10 | Vehicle assignment — creates vehicle, calls `thunk_FUN_005dd870` |
| 0x1a | Action type A — target stored at `entity + 0xac` |
| 0x1b | Action type B — target stored at `entity + 0xec` |
| 0x1c, 0x1f | Action type C — target stored at `entity + 0x154` |
| 0x1d | Action type D — linked to `entity + 0xbc` |

### 14.9 Vehicle Assignment in Order Processing

`FUN_004c1140` (in-order vehicle assignment, 572 bytes):
- Called from within the order state machine when an order supports vehicles
- Calls `thunk_FUN_00462db0` to initiate driving
- Registers the vehicle entity in the global list at `DAT_007c0024 + 0x24`
- This is the "upgrade to vehicle" path — some orders go through this, others
  go through `FUN_00761e00` (walk) or `FUN_00762080` (drive) directly

**The extortion-vs-recruit question:** The order type byte determines which
path is taken. Order types that call `FUN_004c1140` get vehicles; order types
that only call `FUN_00761e00` are forced to walk. The specific mapping of
game order names (extortion, recruit, etc.) to these byte values requires
tracing the order creation functions — the state machine functions read the
type byte but don't define it.

### 14.10 Entity Registration Pattern

Both walk and drive dispatchers use the same post-movement registration:
1. Check if entity at `param_1 + 0xec` is already in the list at
   `DAT_007c0024 + 0x24`
2. If not found, insert via standard doubly-linked list insertion
3. The list uses:
   - Head pointer at `DAT_007c0024 + 0x24`
   - Tail pointer at `DAT_007c0024 + 0x28`
   - Count at `DAT_007c0024 + 0x30`
   - Free list at `DAT_007c0024 + 0x34`
4. Clear `param_1 + 0xec` after registration

This registration likely adds the entity to the "active movement" list for
per-tick processing by the simulation loop.

---

## 15. Vehicle State Machine: The `0x38000000` Field

**Status**: SOLVED — Complete vehicle state system mapped

### Two Separate Vehicle Flag Systems

The binary contains **two distinct vehicle-related flag fields** on hood entities:

| Field | Offset | Mask | Purpose |
|-------|--------|------|---------|
| Movement flags | `entity[0x21]` (dword at +0x84) | `0x8000` (bit 15) | Movement speed flag — controls 12000 vs 32 tick cost |
| Vehicle state | `entity[9]` (dword at +0x24) | `0x38000000` (bits 27-29) | 3-bit vehicle lifecycle state |

The movement flag (section 5) controls **time cost**. The vehicle state field
controls **vehicle assignment and lifecycle**. Both must be set for a hood to
actually drive.

### Vehicle State Values

| Value | Mask | Meaning | Set By |
|-------|------|---------|--------|
| `0x00000000` | — | Walking (no vehicle) | Default / cleared on completion |
| `0x08000000` | bit 27 | Vehicle assigned (car ready) | `FUN_00660e60` (25% random chance) |
| `0x20000000` | bit 29 | Driving (self-acquired vehicle) | `FUN_005dc080` (steal-a-car path) |
| `0x30000000` | bits 28-29 | Cleanup / destroyed | `FUN_0048cc60` (entity destructor) |

### `FUN_00660e60` — Vehicle Assignment for Street Orders (765 bytes)

**Address**: `0x00660e60` | **Called via**: `thunk_FUN_00660e60`

This function iterates all hoods with Kill (0x1a), unknown (0x1b), or Tail (0x1d)
orders who are in state `0xf` (ready for orders). It checks if each hood is on
the gang's linked list at `param_1 + 0x124`, then rolls the RNG:

```c
uVar7 = thunk_FUN_00712500();     // Linear congruential RNG
if ((uVar7 & 3) == 0) {           // 25% chance
    local_8[9] = local_8[9] & 0xcfffffffU | 0x8000000;  // Set vehicle = 0x08000000
}
```

**The 25% random chance applies ONLY to Kill, Tail, and unknown orders** — not
to extort, patrol, or other standard orders. Extort and most other orders use a
**distance-based decision** in `FUN_00462f30` (see below). Only hoods already on
the gang's linked list get the random vehicle check. Hoods not on the list don't
get vehicles through this path.

The function also processes hoods that already have `0x08000000` set — it
removes them from the gang's linked list and calls `vtable[+8]` (likely "begin
driving"). It then counts remaining hoods with these orders and returns the
count.

### `FUN_00462f30` — Distance-Based Walk/Drive Decision

**Address**: `0x00462f30`

This is the **primary walk/drive decision for standard orders** (extort, patrol,
collect protection, etc.). It checks distance to target and assigns a vehicle
if the target is far enough away:

```c
void __fastcall FUN_00462f30(int *param_1)
{
    // Only if order is active AND no vehicle state assigned yet
    if (((param_1[0x21] & 0x80U) != 0) && ((param_1[9] & 0x38000000U) == 0)) {
        // ... distance calculation ...
        if (cVar1 == '\0') {
            if (0x40 < (int)uVar8) {           // Distance > 64 units (~2/3 block)
                *(undefined1 *)((int)param_1 + 0x11) = 1;   // Set AI state
                uVar3 = thunk_FUN_0048a750((char)param_1[0x16], 1);  // mode=1 → DRIVE
                *(undefined1 *)((int)param_1 + 0x59) = uVar3;
            }
            // ... else: walk (mode=0 is the default, no explicit call needed)
        }
        // ...
        (**(code **)(*param_1 + 0x80))();  // Trigger AI brain tick
    }
}
```

**The decision threshold is 0x40 (64) units** — roughly 2/3 of a block (block
size is 0x60 = 96 pixels). If the target is more than 64 units away, the hood
drives. If closer, they walk.

`thunk_FUN_0048a750` is the **walk/drive mode setter** — it takes a mode
parameter (0 = walk, 1 = drive) and dispatches to the appropriate movement
function. This is the same function identified in the earlier vehicle flag
search as the single caller of the walk/drive dispatchers.

### Two Vehicle Decision Paths (Summary)

| Path | Function | Applies To | Logic |
|------|----------|-----------|-------|
| **Distance-based** | `FUN_00462f30` | Extort, patrol, most standard orders | Distance > 0x40 (64 units) → drive; else walk |
| **Random 25%** | `FUN_00660e60` | Kill (0x1a), Tail (0x1d), unknown (0x1b) only | `RNG & 3 == 0` → assign vehicle |
| **Hijack** | `FUN_005dc080` | Any hood with vehicle_state == 0 | Creates vehicle entity, sets driving state |

The distance-based path confirms the user's gameplay observation: hoods walk
to nearby extort targets and drive to distant ones. The 25% random path is a
special case for "street orders" (kill/tail) where the game adds randomness to
make AI behavior less predictable.

### `FUN_005dc080` — Drive State Transition (557 bytes)

**Address**: `0x005dc080` | **Called via**: `thunk_FUN_005dc080`

This function activates when a hood has **no vehicle state** (`vehicle == 0`).
It's the "steal a car" or "find a vehicle" path:

```c
uVar1 = param_1[9];
if ((uVar1 & 0x38000000) == 0) {          // No vehicle state at all
    if (DAT_00902860 == 7) {              // Special game mode check
        param_1[9] = uVar1 & 0xe7ffffff | 0x20000000;  // Set driving
        (**(code **)(*param_1 + 0x30))();  // vtable call
    } else {
        param_1[9] = uVar1 & 0xe7ffffff | 0x20000000;  // Set driving
        // Create a new vehicle entity if hood doesn't have one
        if (param_1[0x15] == 0) {
            pvVar2 = operator_new(0x70);   // Allocate 112-byte vehicle struct
            iVar3 = thunk_FUN_00568670();  // Initialize vehicle
            param_1[0x15] = iVar3;         // Store vehicle pointer
        }
        // Find a target destination (random or from AI)
        iVar3 = thunk_FUN_00563190();      // Peek at order queue
        if ((iVar3 == 0) || (*(char *)(iVar3 + 5) != '\t')) {
            iVar3 = thunk_FUN_00561740();  // Get alternative target
            // ... random direction calculation using RNG ...
            iVar6 = thunk_FUN_00712500();
            *(char *)(param_1 + 2) = (char)(iVar6 % 0x60);  // Random X
            iVar6 = thunk_FUN_00712500();
            *(char *)((int)param_1 + 9) = (char)(iVar6 % 0x60);  // Random Y
        }
        // Set movement and trigger AI brain
        thunk_FUN_00563080(puVar4);        // Set movement destination
        (**(code **)(*param_1 + 0x80))();  // vtable[0x80] = AI brain tick
    }
}
```

**Key insight**: The `0xe7ffffff` mask clears bits 24-26 (not 27-29), suggesting
there are **additional state bits** at bits 24-26 beyond the 3-bit vehicle field.
The `0x20000000` sets bit 29 (driving state).

### `FUN_0048cc60` — Entity Destructor (789 bytes)

**Address**: `0x0048cc60`

Sets `0x30000000` (bits 28-29) during entity cleanup. This is the "vehicle
destroyed/returned" state. The destructor:
1. Iterates and frees linked lists at `param_1[0x70]`, `param_1[0x68]`,
   `param_1[0x80]`, `param_1[0x96]` (various entity sub-lists)
2. Calls 7 cleanup sub-functions (`FUN_0048cf75` through `FUN_0048cfe1`)
3. Frees allocated memory via `FUN_0076a250`

### `FUN_0059cb10` — Gang Strength Assessor (NOT Vehicle-Related)

**Correction**: `FUN_0059cb10` was initially identified as setting the `0x80000`
vehicle flag. It is actually a **gang strength rating function** that writes to
`entity + 0x4ec` (gang description flags), NOT the vehicle state field.

```c
if (0x31 < iVar2)  uVar9 = 0x40000;   // > 49 → bit 18
if (0x4a < iVar2)  uVar9 |= 0x80000;  // > 74 → bit 19 (NOT vehicle!)
if (99 < iVar2)    uVar9 |= 0x100000; // > 99 → bit 20
```

It formats "MOBSTER'S PRIVATE ARMY" and "%s EMPLOYS %i HOODS TO MAINTAIN CONTROL"
strings — this is a **news/AI assessment function** that rates gang strength
based on territory/hood count.

### `FUN_00712500` — Confirmed as PRNG (42 bytes)

**Address**: `0x00712500`

```c
uint FUN_00712500(void) {
    DAT_009008e4 = DAT_009008e4 * 0x343fd + 0x269ec3;
    return (DAT_009008e4 & 0x7fff0000) >> 0x10;
}
```

This is a **linear congruential generator** (Windows MINSTD) producing 15-bit
values (0-32767). It's the same RNG documented in section 13.5, now confirmed
as the function used for the walk/drive decision. The `& 3 == 0` check means
**25% of hoods with Kill/Tail orders get vehicles**.

### Vehicle Decision Flow (Complete)

```
                    ┌─────────────────────────────┐
                    │  Hood receives an order      │
                    └──────────┬──────────────────┘
                               │
              ┌────────────────┼─────────────────┐
              │                │                 │
     ┌────────▼───────┐  ┌────▼─────────┐  ┌────▼──────────────┐
     │ Standard orders │  │ Kill/Tail/   │  │ Any order with    │
     │ (extort, patrol,│  │ unknown      │  │ vehicle_state==0  │
     │ collect, etc.)  │  │ (0x1a/0x1b/  │  │ and no vehicle    │
     │                 │  │ 0x1d)        │  │                   │
     └────────┬────────┘  └──────┬───────┘  └──────┬────────────┘
              │                  │                 │
     ┌────────▼────────┐  ┌──────▼──────────┐     │
     │ FUN_00462f30    │  │ FUN_00660e60    │     │
     │ Distance check  │  │ Is hood on      │     │
     │ Distance > 0x40?│  │ gang list?      │     │
     └───┬─────────┬───┘  └──┬──────────┬───┘     │
        Yes       No        Yes        No         │
         │         │         │         │          │
    ┌────▼───┐ ┌───▼───┐ ┌───▼──────┐  │          │
    │ DRIVE  │ │ WALK  │ │ RNG&3==0?│  │          │
    │ mode=1 │ │ mode=0│ └─┬────┬───┘  │          │
    └────────┘ └───────┘  Yes    No    │          │
                          │      │     │          │
                   ┌──────▼──┐ ┌─▼──┐  │          │
                   │Set      │ │Walk│  │          │
                   │0x0800M  │ │(no │  │          │
                   │(vehicle │ │veh)│  │          │
                   │assigned)│ └────┘  │          │
                   └─────────┘         │          │
                                       │          │
                              ┌────────▼──────────▼───┐
                              │ FUN_005dc080          │
                              │ "Steal a car" path:   │
                              │ - Set 0x20000000      │
                              │ - Create vehicle      │
                              │ - Random target       │
                              │ - Call AI brain       │
                              └───────────────────────┘
```

### Steel City Design Implication

The original game uses a **two-tier vehicle decision system**:

1. **Distance-based for most orders** (`FUN_00462f30`) — threshold of 64 units
   (~2/3 block). This is the system the user observed: nearby extort = walk,
   distant extort = drive. It's clean, predictable, and makes sense.

2. **Random 25% for Kill/Tail/unknown** (`FUN_00660e60`) — adds unpredictability
   to "street orders" so AI gangs don't always behave identically.

3. **Hijack fallback** (`FUN_005dc080`) — any hood without a vehicle can
   spontaneously acquire one. This creates emergent "steal a car" moments.

For Steel City:

- **Preserve the distance-based system** as the primary decision — it's what
  players expect and observe
- **Keep the random element for combat orders** — adds variety to AI behavior
- **Preserve the hijack mechanic** but add consequences (police heat, wanted level)
- **Make vehicle assignment visible to the player** — show "will drive" or "will
  walk" before order commitment, with the distance threshold clearly indicated
- **Consider a tunable threshold** — the original's 64-unit cutoff is fixed;
  Steel City could let difficulty or hood stats influence it

---

## 16. Portrait Generation System

**Status**: SOLVED — 5-layer compositor with randomization fully mapped

### Overview

The portrait system is a **layered sprite compositor** — each character's
portrait is assembled from 5 independent layers (head, hair, eyes, nose, mouth),
each randomly selected from variant-specific option pools. Additional appearance
attributes (skin tone, features) are packed into a bitfield.

### `FUN_0063a550` — Portrait Generator (671 bytes)

**Address**: `0x0063a550` | **Called via**: `thunk_FUN_0063a550`

```c
void __fastcall FUN_0063a550(int param_1)
{
    // Phase 1: Select portrait layer indices using _rand()
    iVar1 = thunk_FUN_0069e9b0(&local_4);  // Pop from seed stack
    if (iVar1 == 0) {
        thunk_FUN_0069e820(0);              // Push seed value 0

        // 1. Pick head type (0-13)
        iVar2 = _rand();
        thunk_FUN_0069e820(iVar2 % 0xe);   // Push head type

        // 2. Pick head variant (count from head type array)
        iVar1 = _rand();
        iVar1 = iVar1 % *(int*)(*(*(int*)(param_1 + 0x560) + 0x1c)
                              + (iVar2 % 0xe) * 0x10);
        thunk_FUN_0069e820(iVar1);          // Push head variant
        iVar1 = iVar1 * 0x18;              // Index into variant array

        // 3. Pick hair (count from variant entry + 0x10)
        iVar2 = _rand();
        thunk_FUN_0069e820(iVar2 % *(int*)(*(*(int*)(*(int*)(param_1 + 0x560)
                              + 0x24) + 0x10 + iVar1) + 0x10));

        // 4. Pick eyes (count from variant entry + 0x0C)
        iVar2 = _rand();
        thunk_FUN_0069e820(iVar2 % *(int*)(*(*(int*)(*(int*)(param_1 + 0x560)
                              + 0x24) + 0x10 + iVar1) + 0xc));

        // 5. Pick nose (count from variant entry + 0x08)
        iVar2 = _rand();
        thunk_FUN_0069e820(iVar2 % *(int*)(*(*(int*)(*(int*)(param_1 + 0x560)
                              + 0x24) + 0x10 + iVar1) + 8));

        // 6. Pick mouth (count from variant entry + 0x14)
        iVar2 = _rand();
        thunk_FUN_0069e820(iVar2 % *(int*)(*(*(int*)(*(int*)(param_1 + 0x560)
                              + 0x24) + 0x10 + iVar1) + 0x14));
    }

    // Phase 2: Generate appearance bitfield
    thunk_FUN_0069e9b0(&local_4);
    *(byte*)(param_1 + 0x9fc) =
        ((byte)local_4 ^ *(byte*)(param_1 + 0x9fc)) & 0x7f
        ^ *(byte*)(param_1 + 0x9fc);           // 7-bit feature

    thunk_FUN_0069e9b0(&local_4);
    *(undefined1*)(param_1 + 0x9fd) = (undefined1)local_4;  // Byte feature

    // Pack bits into 0xa08 field:
    thunk_FUN_0069e9b0(&local_4);
    *(uint*)(param_1 + 0xa08) =
        (local_4 ^ *(uint*)(param_1 + 0xa08)) & 0x3f        // Bits 0-5: skin tone (64 values)
        ^ *(uint*)(param_1 + 0xa08);

    thunk_FUN_0069e9b0(&local_4);
    *(uint*)(param_1 + 0xa08) =
        (local_4 << 6 ^ *(uint*)(param_1 + 0xa08)) & 0xc0   // Bits 6-7: feature
        ^ *(uint*)(param_1 + 0xa08);

    thunk_FUN_0069e9b0(&local_4);
    *(uint*)(param_1 + 0xa08) =
        (local_4 << 8 ^ *(uint*)(param_1 + 0xa08)) & 0x700  // Bits 8-10: feature
        ^ *(uint*)(param_1 + 0xa08);

    thunk_FUN_0069e9b0(&local_4);
    *(uint*)(param_1 + 0xa08) =
        (local_4 << 0xb ^ *(uint*)(param_1 + 0xa08)) & 0x800 // Bit 11: flag
        ^ *(uint*)(param_1 + 0xa08);

    thunk_FUN_0069e9b0(&local_4);
    *(byte*)(param_1 + 0x9fc) = *(byte*)(param_1 + 0x9fc) | 0x80;  // Set valid flag

    uVar3 = (local_4 << 0xc ^ *(uint*)(param_1 + 0xa08)) & 0x7000 // Bits 12-14: feature
        ^ *(uint*)(param_1 + 0xa08);
    *(uint*)(param_1 + 0xa08) = uVar3;

    // Clear unused fields
    *(undefined1*)(param_1 + 0x9fe) = 0;
    *(undefined1*)(param_1 + 0x9ff) = 0;
    *(undefined1*)(param_1 + 0xa00) = 0;
    *(undefined1*)(param_1 + 0xa01) = 0;

    *(uint*)(param_1 + 0xa08) = uVar3 | 0x8000;  // Set "portrait generated" flag
    *(undefined4*)(param_1 + 0xa04) = 0x1e;      // Default value (30)
    *(undefined4*)(param_1 + 0xa0c) = 0;
}
```

### Portrait Data Structure

```
param_1 + 0x560 → Portrait Definition Table
  +0x1C → Head type array (14 entries, 0x10 bytes each)
    Each entry: [variant_count] at offset +0x0
  +0x24 → Variant array (entries of 0x18 bytes each)
    Each variant entry:
      +0x08: nose_count
      +0x0C: eyes_count
      +0x10: hair_count (or head sub-variant)
      +0x14: mouth_count
```

### Appearance Bitfield (entity + 0xa08)

| Bits | Mask | Field | Values |
|------|------|-------|--------|
| 0-5 | `0x003F` | Skin tone | 64 values |
| 6-7 | `0x00C0` | Feature A | 4 values |
| 8-10 | `0x0700` | Feature B | 8 values |
| 11 | `0x0800` | Flag | boolean |
| 12-14 | `0x7000` | Feature C | 8 values |
| 15 | `0x8000` | Portrait generated | boolean |

Additional bytes:
- `entity + 0x9fc`: 7-bit feature (bits 0-6) + valid flag (bit 7)
- `entity + 0x9fd`: byte feature
- `entity + 0x9fe` - `0xa01`: cleared to 0
- `entity + 0xa04`: default value 30 (0x1e)
- `entity + 0xa0c`: cleared to 0

### Seed-Based Random Sequence

The portrait system uses `thunk_FUN_0069e820` (push) and `thunk_FUN_0069e9b0`
(pop) as a **seed stack** — a deterministic random sequence that ensures
portrait generation is reproducible from the same seed. This means:

1. The same seed always produces the same portrait
2. Seeds can be saved/loaded with save games
3. Multiplayer character generation stays consistent

### `FUN_0063a950` — Portrait Renderer (385 bytes)

**Address**: `0x0063a950`

Renders the portrait by loading two sprite layers:
1. Calls `thunk_FUN_00645ff0` with `(index + param_4 * 4) * 2` → face/outline sprite
2. Calls `thunk_FUN_00645ff0` with `(index + param_4 * 4) * 2 + 1` → overlay sprite (hair/features)

The renderer manages a sprite list at `param_1 + 0x40` (vector array). Old
sprites are freed via `FUN_0076a250` and `InvalidateRect` before loading new
ones. The two sprite handles are stored at `param_1 + 0x9f4` and
`param_1 + 0x9f8`.

### `FUN_0063ac90` — Single-Player Hood Setup (1146 bytes)

**Address**: `0x0063ac90`

Reads `DEFAULT_HOODS` from the registry (`SOFTWARE\Hothouse\Gangsters`). Sets up
game configuration including:
- Game type (normal/scenario/short game)
- Screen resolution (640x480, 800x600, 1024x768)
- Economy strength (weak/normal/strong)
- AI aggression (passive/normal/aggressive/random)
- Money, hoods, businesses counts

### `FUN_0063b120` — Multiplayer Hood Setup (1176 bytes)

**Address**: `0x0063b120`

Reads `DEFAULT_MULTIPLAYER_HOODS` from the registry. Similar structure to
single-player but with multiplayer-specific settings. Both functions use the
`thunk_FUN_0069e820`/`thunk_FUN_0069e9b0` seed stack for deterministic
configuration.

### `FUN_006372c0` — Game Options UI (2226 bytes)

**Address**: `0x006372c0`

The Game Options screen with:
- Game type buttons (Normal, Scenarios, Short Game)
- Resolution buttons (640x480, 800x600, 1024x768)
- Economy buttons (Weak, Normal, Strong)
- Opponent AI buttons (Passive, Normal, Aggressive, Random)
- Scroll bars for Money (30-100%), Hoods (6-10), Businesses (1-10)
- "Restore Defaults" and "Advanced Options" buttons

### Registry Strings

| String | Purpose |
|--------|---------|
| `DEFAULT_HEAD` | Default head sprite index |
| `DEFAULT_HAIR` | Default hair sprite index |
| `DEFAULT_EYES` | Default eyes sprite index |
| `DEFAULT_NOSE` | Default nose sprite index |
| `DEFAULT_MOUTH` | Default mouth sprite index |
| `DEFAULT_HOODS` | Default hood count (single-player) |
| `DEFAULT_MULTIPLAYER_HOODS` | Default hood count (multiplayer) |

All read from `SOFTWARE\Hothouse\Gangsters` registry key.

### Voxel Character Generation Mapping

The portrait system maps cleanly to procedural voxel character generation:

| Portrait Layer | Voxel Equivalent |
|---------------|-------------------|
| Head type (14) | Head mesh shape |
| Head variant | Head proportions/scale |
| Hair | Hair mesh + style |
| Eyes | Eye position/shape/color |
| Nose | Nose mesh |
| Mouth | Mouth mesh |
| Skin tone (6 bits) | Skin material color (64 values) |
| Feature bits | Scars, facial hair, accessories |
| Seed stack | Reproducible generation from seed |

The `thunk_FUN_0069e820`/`thunk_FUN_0069e9b0` seed system is ideal for
reproducible voxel character generation — save the seed, regenerate the same
character anywhere.

### Steel City Design Implication

1. **Preserve the 5-layer system** — head, hair, eyes, nose, mouth as
   independent selectable layers
2. **Use the seed-based generation** — deterministic from seed, perfect for
   save/load and multiplayer consistency
3. **Extend to 3D** — each 2D sprite layer becomes a 3D mesh layer
4. **Preserve the bitfield packing** — compact appearance encoding is efficient
   for save games and network transmission
5. **The 14 head types × N variants × hair × eyes × nose × mouth** creates
   enormous variety from small data — same principle applies to voxel models

---

## 17. Updated Pending Investigation

### Confirmed Answers

| Question | Answer |
|----------|--------|
| How does time budget work? | **Command queue** — commands enqueued, processed during ticks |
| Is walking cost flat? | **Yes** — 12,000 ticks regardless of distance |
| Does driving skip arrest checks? | **Yes** — drive function never calls arrest animation |
| What's the pathfinding range? | **480 pixels (5 blocks)** for direct, multi-segment beyond |
| What's the block size? | **0x60 (96) pixels** |
| What's the map grid? | **5x5 blocks**, each containing 25 cells |
| What RNG does the game use? | **Windows LCG**: `x = x * 0x343fd + 0x269ec3` |
| Where's the road network? | `DAT_007c0024 + 0x1220`, 4 bytes per segment (W/E/N/S) |
| Who calls the walk thunk? | **`FUN_00761e00`** — single walk dispatcher (575 bytes) |
| Who calls the drive thunk? | **`FUN_00762080`** (drive dispatcher) + **`FUN_004c1140`** (in-order vehicle upgrade) |
| How many functions consume time? | **67 unique functions** make 156 calls to time consumption thunk |
| How many functions set destinations? | **26 unique functions** make 74 calls to movement destination thunk |
| Are pathfinding thunks called directly? | **No** — 0 direct references; called via function pointers/vtable |
| What's the order state machine? | **3-bit state** (0–7) in flags word at `entity[0x15]+0xc`, bits 10–12 |
| What are the order type bytes? | 0x0f/0x18/0x19/0x23=movement, 0x10=vehicle, 0x1a–0x1f=actions |
| What is the `0x38000000` vehicle state field? | **3-bit lifecycle** at `entity[9]` (offset 0x24): 0=walking, 0x08000000=vehicle assigned, 0x20000000=driving, 0x30000000=cleanup |
| How is the walk/drive decision made for street orders? | **Two paths**: Distance-based (`FUN_00462f30`, threshold 0x40 units) for most orders; 25% random (`FUN_00660e60`) for Kill/Tail/unknown only |
| Is `FUN_0059cb10` a vehicle flag setter? | **No** — it's a gang strength assessor writing to `entity + 0x4ec`, not the vehicle field |
| What is `FUN_00712500`? | **Linear congruential PRNG** (Windows MINSTD): `x = x * 0x343fd + 0x269ec3`, returns 15-bit value |
| How does the portrait system work? | **5-layer compositor**: head (14 types) → variant → hair → eyes → nose → mouth, each randomly selected. Plus 16-bit appearance bitfield (skin tone, features) |
| Is portrait generation deterministic? | **Yes** — uses seed stack (`thunk_FUN_0069e820` push / `thunk_FUN_0069e9b0` pop) for reproducible results |
| Can portraits map to voxel characters? | **Yes** — each 2D layer maps to a 3D mesh layer; seed system enables reproducible generation |
| What is `entity+0x68` (`entity[0x1a]`)? | **Target entity pointer** — stores entity found by pedestrian search that is blocking movement |
| What are the 0x700 flags in `entity[0x19]`? | **"Aware of nearby entity"** status bits, set after entity search finds something |
| What does the blocked crossing handler do? | `FUN_005ddb80`: 1/128 chance per tick for peds only, sets 0x80 flag, creates wait action (type 14) |
| How do vehicles reroute? | `FUN_005d6ef0`: spiral grid search (-5 to +5 lateral, 30 to 1 forward), validates via `thunk_FUN_00609060` |
| Do trams have right-of-way? | **No RE evidence found** — no tram-specific priority logic in any decompiled function |
| Is pedestrian avoidance type-specific? | **No** — `FUN_005dd9d0` checks only `vtable+0x20` (passability), not entity type |
| Are trams excluded from random walks? | **Yes** — `FUN_005dddc0` re-rolls action type 3 if entity is tram (type 8) |
| Do vehicles move passengers? | **Yes** — `FUN_004cb0c0` iterates `entity[0x45]` passenger list, updates each passenger's position |
| Are all entities billboard sprites? | **Yes** — no collision/hitbox/physics code, all interactions are grid cell occupancy checks |
| What is `FUN_00414ba0`? | **Game state/mode transition handler** (not tram movement) — case 8 = tram simulation phase, calls undecompiled `thunk_FUN_005bba40` and `thunk_FUN_006c7a90` |

### Still Pending

1. **Decompile `thunk_FUN_005614d0`** — node type initializer (what do types
   1, 6, 0xC, 0xE mean?)
2. **Decompile `thunk_FUN_005642c0`** — the main multi-segment pathfinding
   algorithm
3. **Decompile `thunk_FUN_00565120`** — direct movement / waypoint generation
4. **Decompile `thunk_FUN_00495770`** — vehicle return function
5. **Decompile `thunk_FUN_0060c3c0`** — distance limit validation
6. **Trace order creation functions** — find where order type bytes (0x0f,
   0x10, 0x1a, etc.) are assigned to map game order names to type values
7. **Trace `FUN_004c1140` callers** — determine which order types trigger
   the in-order vehicle upgrade path
8. **Find pathfinding function pointer table** — since pathfinding thunks
   have 0 direct references, locate the vtable or function pointer array
   used to invoke them
9. **Dump portrait definition table** — extract actual head/hair/eyes/nose/mouth
    counts from binary data at `param_1 + 0x560`
10. **Find portrait sprite files** — locate the actual .spr/.dat files
    referenced by the portrait renderer `FUN_0063a950`

### Open Questions

- Does the command queue process one command per tick, or drain all?
- What happens when the time budget runs out mid-order?
- How does the game handle pathfinding failures (no valid path)?
- What are the 13 animation entries per movement type?
- Which order type bytes map to which game order names (extortion, recruit,
  bomb, etc.)?
- What determines whether an order uses `FUN_00761e00` (walk) vs
  `FUN_00762080` (drive) vs `FUN_004c1140` (in-order vehicle)?
- Is the 25% random vehicle assignment in `FUN_00660e60` truly random, or
  is `DAT_009008e4` seeded with distance/hood-level data before the call?
- What does `DAT_00902860 == 7` check in `FUN_005dc080` signify? (game mode?
  day of week? mission type?)
- What does `thunk_FUN_005bba40` (called in tram mode case 8) actually do?
  Is it tram route advancement, stop processing, or state update?
- What does `thunk_FUN_006c7a90` (called in tram mode case 8) do?
- Does `vtable+0xF4` (vehicle "can drive" check) use the same passability
  logic as `vtable+0x20`, or is it a separate vehicle-specific check?
- Are there tram-specific movement functions beyond `FUN_00414ba0` case 8?
  The mode handler calls thunks that haven't been decompiled yet.

---

## Appendix A: Script Inventory

| Script | Location | Purpose |
|--------|----------|---------|
| `FindOrderLogic.java` | `C:\Tools\ghidra_scripts\` | String + constant search, batch decompile |
| `DecompileKeyFunctions.java` | `C:\Tools\ghidra_scripts\` | Targeted decompilation of 19 functions |
| `DecompileTimeFunctions.java` | `C:\Tools\ghidra_scripts\` | Time-constant function decompilation |
| `DecompileEngineCore.java` | `C:\Tools\ghidra_scripts\` | Decompiles 47 core engine functions + traces callers |
| `TraceThunkCallers.java` | `C:\Tools\ghidra_scripts\` | Traces callers of walk/drive/time thunk functions |
| `FindVehicleFlagSetters.java` | `C:\Tools\ghidra_scripts\` | Searches for 0x80000/0x38000000 flag setters, walk/drive mode setter callers |
| `FindVehicleStateSetters.java` | `C:\Tools\ghidra_scripts\` | Searches for individual vehicle bit setters (0x08M/0x10M/0x20M), IMUL 0x60 distance calc |
| `FindPortraitSystem.java` | `C:\Tools\ghidra_scripts\` | Searches for portrait/character generation strings, _rand callers, sprite loading |
| `DecompileDecisionAndPortraits.java` | `C:\Tools\ghidra_scripts\` | Decompiles walk/drive decision function, vehicle assignment, portrait generator |
| `DecompileVehiclePedInteraction.java` | `C:\Tools\ghidra_scripts\` | Full SIM_TICK decompile, vtable scan, entity type comparisons, street crossing/access decompile |
| `SearchTrafficSignalWrites.java` | `C:\Tools\ghidra_scripts\` | Searches for any writes to `DAT_007c0024 + 0x1220` (road access flags) — binary scan + Ghidra refs |
| `DecompileRoadAccessInit.java` | `C:\Tools\ghidra_scripts\` | Decompiles `FUN_00650ee0` (city constructor) and map init candidates that write road access flags |
| `DecompileTrafficInteractions.java` | `C:\Tools\ghidra_scripts\` | Comprehensive ped/vehicle/tram interaction: entity awareness, blocked crossing, reroute, post-processing, tram type-8 dispatch, occupancy vtable callers, entity offset analyses |

### Output Files (Additional)

| File | Lines | Content |
|------|-------|---------|
| `ghidra_vehicle_flags.txt` | ~919 | Vehicle flag search results (0x80000/0x38000000) |
| `ghidra_vehicle_state_setters.txt` | ~830 | Individual vehicle bit setters + decompiled functions |
| `ghidra_portrait_system.txt` | ~16380 | Portrait system string search, _rand callers, 119 decompiled candidate functions |
| `ghidra_decision_portraits.txt` | ~1420 | Walk/drive decision function + portrait generator + callers |
| `ghidra_vehicle_ped_interaction.txt` | ~15617 | Full SIM_TICK decompile, vtable resolutions, entity type comparisons, street crossing/access |
| `ghidra_traffic_signal_writes.txt` | 85 | Road access flag write search — 20 reads, 0 dynamic writes, 7 static writes in constructor |
| `ghidra_road_access_init.txt` | ~2965 | City constructor `FUN_00650ee0` decompile — confirms road access flags are static |
| `ghidra_traffic_interactions.txt` | ~16007 | Full traffic interaction analysis: entity awareness, blocked crossing, vehicle reroute, SIM_TICK post-processing, tram dispatch, entity type comparisons, string searches, offset analyses |

## Appendix B: Data File Cross-Reference

| .xtx File | Binary Function | Relationship |
|-----------|----------------|-------------|
| `Constants.xtx` | `FUN_00450f80` | Timing constants loaded at init |
| `Crime.xtx` | `FUN_005b3440` | Order types map to crime table entries |
| `Economics.xtx` | `FUN_00450f80` | Loaded at init (`Data_economics_txt`) |
| `RunningCosts.xtx` | `FUN_00450f80` | Loaded at init (`Data_RunningCosts_txt`) |
| `Income Groups.xtx` | `FUN_00450f80` | Loaded at init (`Data_Income_Groups_txt`) |
| `Empty Land Cost.xtx` | `FUN_00450f80` | Loaded at init (`Data_Empty_Land_Cost_txt`) |
| `Market Share.xtx` | `FUN_00450f80` | Loaded at init (`Data_Market_Share_txt`) |
| `Land Value Reductions.xtx` | `FUN_00450f80` | Loaded at init (`Data_Land_Value_Reductions_txt`) |
| `Illegal Economics.xtx` | `FUN_00450f80` | Loaded at init (`Data_illegal_economics_txt`) |
| `Illegal profit.xtx` | `FUN_00450f80` | Loaded at init (`Data_illegal_profit_txt`) |
| `LastWeekReport.xtx` | `FUN_00450f80` | Loaded at init (`Data_LastWeekReport_txt`) |
| `Character Generation.xtx` | `FUN_0063a550`, `FUN_0063ac90`, `FUN_0063b120` | Portrait/hood generation — 5-layer compositor with randomization |
| `Hit Table.xtx` | (not yet found in binary) | Pending |
| `Damage Table.xtx` | (not yet found in binary) | Pending |
| `Cart.xtx` | (not yet found in binary) | Pending |

---

## Document Conventions

- All addresses are virtual addresses from Ghidra's default image base
- Function names (`FUN_xxxxxx`) are Ghidra auto-generated; no debug symbols exist
- `thunk_FUN_xxxxxx` are Ghidra-generated thunks for cross-segment calls
- Decompiled code is C pseudocode from Ghidra's decompiler
- Findings marked **(Pending)** require further decompilation to confirm

---

## 18. Combat & Pathfinding Deep Dive

**Source**: `ghidra_pathfinding_combat.txt` (6,843 lines, full decompilation)
**Status**: SOLVED — Complete combat system, waypoint following, street crossing, and SIM_TICK internals mapped

### 18.1 SIM_TICK: The Master Orchestrator (`FUN_005d2740`)

**Size**: 16,980 bytes | **Address**: `0x005d2740` | ~4,400 lines of decompiled code

This is the **heart of the simulation**. Every game tick, this function processes
every entity through a massive switch on `entity + 0x11` (AI state byte), with
cases 0 through 0xD. Each case contains nested substate machines, linked list
management, and calls to combat/movement functions.

#### Switch Architecture

```
SIM_TICK(entity)
  ├── Timer decrements (+0x17, +0x5d, +0x5e, +0x5f)
  ├── Animation frame advance (counter & 0xF == 0 → vtable +0xA0, +0x58)
  ├── Switch on entity[0x11] (AI state):
  │   ├── Case 0-3: Movement states (block-by-block pathfinding)
  │   ├── Case 4: Driving (speed accumulation, lane changes, collisions)
  │   ├── Case 8: Vehicle cruise (speed-based position, block transitions)
  │   ├── Case 9: Pathfinding (3-substate: request → follow → arrive)
  │   ├── Case 10: Advanced driving (init → accel → cruise → lane change → decel)
  │   ├── Case 0xB: Combat approach (timer + RNG state selection)
  │   ├── Case 0xC: Combat engagement (8-substate: init/approach/attack/cover/flank/retreat/vehicle/reset)
  │   └── Case 0xD: Fleeing (7-substate with zigzag patterns 0x27-0x2C)
  └── Post-processing (thunk_FUN_005dddc0 if flagged)
```

#### Linked List Management (5 Queues)

SIM_TICK manages 5 doubly-linked lists from the global state at `DAT_007c0024`:

| Offset | Purpose | Structure |
|--------|---------|-----------|
| `+0x24` | Entity active pool | head/tail/count/free-list |
| `+0xE4` | Update queue | head/tail/count/free-list |
| `+0x104` | Movement queue | head/tail/count/free-list |
| `+0x124` | Combat/action queue | head/tail/count/free-list |
| `+0x144` | Secondary action queue | head/tail/count/free-list |

Each queue uses a consistent pattern:
- **Head** at `+0x00`, **tail** at `+0x04`, **count** at `+0x10`
- **Free-list head** at `+0x0C`, **free-count** at `+0x1C`
- Node size: 12 bytes (data ptr, prev, next)
- **Node recycling**: When free count > 5, excess nodes are deallocated (down to 2 retained)
- **Insertion**: New nodes prepended to head, count incremented
- **Removal**: Standard doubly-linked list removal with head/tail/count updates

#### Case 9: Pathfinding Substates

| Substate | Behavior |
|----------|----------|
| 0x01 | **Request path** — remove entity from old block's list, request pathfinding |
| 0x02 | **Follow path** — move along computed waypoints |
| 0x03 | **Arrive** — add entity to new block's list, trigger order execution |

Block transitions involve removing the entity from the source block's linked list
and inserting into the destination block's list — entities are tracked per-block
for spatial queries.

#### Case 0xC: Combat Engagement (8 Substates)

| Substate | Name | Behavior |
|----------|------|----------|
| 0 | Init | Set up combat, select target |
| 1 | Approach | Move toward target, check line-of-sight |
| 2 | Attack | Execute combat function (COMBAT_1-4 based on order type) |
| 3 | Cover | Seek cover, evaluate escape routes |
| 4 | Flank | Attempt lateral movement around target |
| 5 | Retreat | Move away from target |
| 6 | Vehicle entry | Enter vehicle for vehicle-based combat |
| 7 | Reset | Clear combat state, return to AI state machine |

#### Case 0xD: Fleeing (7 Substates with Zigzag)

The fleeing state uses a mod-5 grid system for direction selection:

| Substate | Pattern |
|----------|---------|
| 0x02 | Initial flee — pick direction based on `x%5, y%5` grid position |
| 0x05 | Random wander — RNG picks ±1 adjustments to X/Y |
| 0x27 | Zigzag variant A — directional movement with timer |
| 0x28 | Zigzag variant B — alternate directional movement |
| 0x29 | Zigzag variant C — with countdown timer and random direction changes |
| 0x2A | Zigzag variant D — similar to C with different direction logic |
| 0x2B | Return-to-cover A — timer-based with threshold check |
| 0x2C | Return-to-cover B — mirror of 0x2B with alternate direction |

The zigzag uses `thunk_FUN_00712500() % 3` to randomly switch between
direction modes (0x27, 0x28, or reset), creating unpredictable fleeing patterns.

### 18.2 Combat Functions (4 Variants)

All 4 combat functions share the same 3-phase architecture (approach → attack →
complete), with state stored in `(flags & 0x7C00) >> 10` (values 0/1/2).

#### COMBAT_1: Primary Ranged (`thunk_FUN_004cb870` @ `0x00402ab8`)

- **Phase 0**: Approach target, check line-of-sight via vtable `+0xD0`
  - If `flags & 0x100`: order has destination — set coords, check street access
  - Calls `thunk_FUN_00609130` (pathfinding check) and `thunk_FUN_006064c0` (street validation)
  - On arrival: calls `thunk_FUN_00568870` (micro-move) + vtable `+0xC8` (arrived) + vtable `+0x80` (AI tick)
- **Phase 1**: Ranged attack
  - Checks `thunk_FUN_00609f40` (weapon readiness)
  - If ready: calls `thunk_FUN_004c56e0` (attack setup), `thunk_FUN_004c5810` (positioning)
  - Spawns projectile via `thunk_FUN_006abea0(x, y, type, flags, 0, 0, 0, 0)`
  - Projectile type: `random + 0x04` (randomized direction)
  - Calls `thunk_FUN_00481290` (damage handler) when `flags & 0xA0`
- **Phase 2**: Completion — sets `flags |= 1`

#### COMBAT_2: Melee (`thunk_FUN_004cbd00` @ `0x0040329c`)

- **Phase 1**: Checks `thunk_FUN_00609f40` (weapon readiness)
  - If ready: calls `thunk_FUN_004c56e0` (attack setup)
  - Spawns effect type `0x18` (melee hit effect)
  - Calls vtable `+0x8C(x, y, 0xB, 3, 0)` — animation trigger
  - Sets `flags & 0xFFFF8BFF | 0x800` (at-target state)
- **Phase 0**: If at target position → set `flags |= 1` (complete)
- **Phase 2**: If not at target → reset to pathfinding

#### COMBAT_3: Vehicle Combat (`thunk_FUN_004cc070` @ `0x00404b92`)

- **Phase 1**: Uses `thunk_FUN_004dd1b0` (vehicle target check)
  - If target in range: calls `thunk_FUN_004c56e0`, spawns effect type `0x16`
  - Calls vtable `+0x8C(x, y, 0xB, 3, 0)` — drive-by animation
- **Phase 2**: Timer decrement — `timer -= DAT_007c0024 + 0x16D8` (tick delta)
  - When timer < 1: sets `flags |= 1` (complete)
- **Special**: Sets `entity[0x54] |= 1` (combat flag) on phase 2 entry

#### COMBAT_4: Arrest/Kidnap (`thunk_FUN_004cc470` @ `0x004018d9`)

4-state machine (0/1/2/3):

- **Phase 0**: Standard approach (same as COMBAT_1/2/3)
- **Phase 1**: Arrest attempt
  - Checks `thunk_FUN_00609f40` (readiness)
  - If ready: calls `thunk_FUN_004dd1b0` (target check)
  - Calls `thunk_FUN_004c56e0(10)` — arrest setup with 10-tick cost
  - Calls vtable `+0x8C(x, y, 0xB, 3, 0)` — arrest animation
- **Phase 2**: Vehicle pursuit — `thunk_FUN_00665a90` (vehicle pathfinding)
- **Phase 3**: Arrest execution
  - Gets block at target position via `thunk_FUN_00664d50`
  - Checks all 4 directions via vtable `+0x58` (0, 1, 2, 3) for clear path
  - If all clear: spawns effect type `0xF` (arrest effect)
  - Calls vtable `+0xDC` (arrest success check)
  - If success: `thunk_FUN_00565c30(0, 500)` — costs 500 ticks (1 hour)
  - If failure: `entity[0x54] |= 1` (flag for retry)

#### Combat Caller Analysis

All 4 combat functions are called from the order state machine at low addresses:

| Function | Call Site | Caller Context |
|----------|-----------|---------------|
| COMBAT_1 | `0x00402ab8` | Order execution pipeline |
| COMBAT_2 | `0x0040329c` | Order execution pipeline |
| COMBAT_3 | `0x00404b92` | Order execution pipeline |
| COMBAT_4 | `0x004018d9` | Order execution pipeline |

#### Projectile Spawning (`thunk_FUN_006abea0`)

All combat functions spawn projectiles/effects using the same pattern:

```c
thunk_FUN_006abea0(
    (short)entity[1] * 0x60 + (char)entity[2],      // X in pixels (block * 96 + sub)
    *(short*)(entity + 6) * 0x60 + *(char*)(entity + 9), // Y in pixels
    projectile_type,                                   // Type (0x04, 0x16, 0x18, 0xF)
    entity[10] & 0xFFFFFF01,                           // Flags
    0, 0, 0, 0                                         // Unused
);
```

Only the local player (`DAT_007c0024 + 0x210 == entity[0x1D]`) spawns visible
projectiles — AI combat is resolved statistically without visual effects.

### 18.3 Waypoint Following (`FUN_005844a0`)

3-state machine for traversing pre-computed paths:

| State | Name | Behavior |
|-------|------|----------|
| 0 | **Init** | Copy first waypoint to destination. Call `thunk_FUN_00609cf0` (street access check). If accessible: call `thunk_FUN_005821a0` (path setup). If blocked: set flags `& 0xFFFF8BFF \| 0x800` (wait state) |
| 1 | **Pause/Wait** | Countdown timer at `+8` decremented by `DAT_007c0024 + 0x16D8` (tick delta). When timer expires: check if more waypoints exist. If yes: load next waypoint, advance index at `+0x20`. If no: set complete flag |
| 2 | **Advance** | Same countdown pattern. When timer < 1: check if current index < waypoint count. If yes: load next waypoint. If no: set complete flag |

**Waypoint data structure**:
- `puVar5[6]` (byte) — waypoint count
- `puVar5[7]` (ptr) — waypoint array (8 bytes per waypoint)
- Each waypoint: X at `+0x3E`, Y at `+0x3F`
- Current index at `movement_struct + 0x20`
- Default speed: `DAT_007c0024 + 0x1B18` (stored as short at `+8`)

### 18.4 Street Crossing (`FUN_005dc8c0`) — Detailed

4-directional crossing checker (cases 0=North, 1=East, 2=South, 3=West):

**Per direction**:
1. Get current cell via `thunk_FUN_00664d50(x, y)`
2. Check passability via vtable `+0x20`
3. Check road direction flags at `piVar7[0xB]` (offset 0x2C) — bitfield encoding road directions
4. Iterate up to **6 cells** in the crossing direction
5. At each cell:
   - Check if reached target position (exact match)
   - Check if cell is NULL or impassable → stop
   - Check road network data at `DAT_007c0024 + 0x1220` for **road access flags** (static, not dynamic traffic signals)
   - Check directional flags: `0xF0F00` (N/S), `0xF0F000` (E/W), `0xFF0000` (all)
6. Return 1 if crossing possible, 0 if blocked

**Road access flag checking** (NOT traffic signals — see Section 18.12):
```c
road_id = *(byte*)(cell + 0x29);
// Check 4 bytes at DAT_007c0024 + 0x1220 + road_id * 4
// Byte 0: West open, Byte 1: East open, Byte 2: North open, Byte 3: South open
// 0x00 = open, 0x01 = closed (restricted direction)
if (DAT_007c0024[0x1220 + road_id*4] != 0 ||
    DAT_007c0024[0x1221 + road_id*4] != 0)
    goto blocked;  // Road access flag prevents crossing
```

**6-cell maximum**: Crossings are limited to 6-lane roads wide. This is a
hardcoded limit in the original engine.

### 18.5 Street Access (`FUN_00609cf0`) — Detailed

Checks if a position has accessible streets in 4 cardinal directions.

**Algorithm**:
1. Get cell at position, validate type (0 < type < 11)
2. Check `flags & 0x480 == 0` (no blockage) and vtable `+0xC8` returns 0 (not occupied)
3. If at intersection (`x%5 == 2 && y%5 == 2`): redirect to connected road cell
4. Get road direction data from vtable `+0x1C` → `+0xC` (direction array)
5. Pick random starting direction: `RNG() % 4`
6. Iterate all 4 directions:
   - Direction 0: `dx=-1, dy=0` (West)
   - Direction 1: `dx=0, dy=1` (South)
   - Direction 2: `dx=0, dy=-1` (North)
   - Direction 3: `dx=1, dy=0` (East)
7. For each direction: check adjacent cell
   - Must be road type (vtable `+0x20` returns 0)
   - Must have access flag (`cell[10] & 1`)
   - Must have open road access flag in that direction
8. Return 1 if any direction is accessible, 0 if all blocked

### 18.6 Animation Lookup (`FUN_0048a750`) — Detailed

2D table mapping (entity_type, mode) → animation ID:

```
Table base: param_1 + 4
Row stride: 0x66 (102) bytes
Entries per row: 13 (0xD)
Entry size: 2 bytes (ushort)
Sentinel: 0xFFFF (use fallback)
```

**Fallback algorithm** when sentinel found:
```c
for (i = 0; i < 13; i++) {
    if (table[i * 0x33 + mode] != 0xFFFF) {
        hash = i * 0x33 + mode;
        return table[hash];  // Return first non-sentinel entry
    }
}
```

The `0x33` (51) stride is `0x66 / 2` — the table is indexed as shorts, so each
row of 13 entries spans 26 bytes, and the next mode row starts 51 shorts later.

### 18.7 Order Completion (`FUN_004c5d70`) — Detailed

When a hood completes an order:

1. Clear `entity[0x54] &= ~0x10` (order complete flag)
2. Set `entity[0x2E] |= 2` (processing flag)
3. Copy `entity[0x45]` → `entity[0x47]` (order list head)
4. Set `entity[0x4C] = 1` (processing flag)
5. **Iterate all hoods in order list**: Set `+0xB8 |= 2` on each hood
6. Check movement struct at `entity[0x15]`:
   - If `flags & 0x20` (has movement):
     - If `!(flags & 0x80)` and order type != 0xF: call arrest check
     - If `!(flags & 0x80)` or `flags & 0x8000`: return vehicle
7. **Switch on movement type** (offset `+0x10`):
   - Cases 1,2,6-10,12,14,15,17-28,31: trigger animation `vtable+0x88(5, 3, 0x14, 1)`
   - Other cases: no animation
8. Call `thunk_FUN_005691a0` (pop next order from queue)
9. If new order exists: check for arrest, trigger AI tick
10. Call vtable `+0xC8` (arrived) and vtable `+0x80` (AI tick)

### 18.8 AI State Machine (`FUN_005e0560`) — Detailed

Controls entity behavior at offset `+0x11`:

| State | Name | Trigger | Probability |
|-------|------|---------|-------------|
| 0x13 | Dying | Terminal | — |
| 0x14 | Dead | Terminal | — |
| 0x02 | Idle/Wander | Default, `RNG & 0x3F >= 10` | ~84% |
| 0x04 | Alert | `RNG & 0x3F == 0` and no flags | ~1.5% |
| 0x05 | Suspicious | `RNG & 0x3F < 10` or alert | ~15% |
| 0x06 | Combat | `param+0x60 == 0x03` and `!(flags & 1)` | Conditional |
| 0x07 | Fleeing | Combat + `RNG & 7 != 0` | ~87.5% from combat |
| 0x08 | Order Execution | `param+0x58 == 8` | Conditional |

**Transition logic**:
```c
if (state == 0x14) return;  // Dead — locked
if (state == 0x13) return;  // Dying — locked

if (param+0x58 == 8) {  // Has active order
    if (state == 6 || (RNG & 7 != 0 && state != 7)) {
        state = 7;  // Fleeing
    } else {
        state = 7;  // Force fleeing during order if combat
    }
} else if (param+0x60 == 0) {  // No threat
    if (RNG & 0x3F == 0) {
        state = (flags & 3) ? 5 : 4;  // Suspicious or Alert
    } else if (RNG & 0x3F < 10) {
        state = 5;  // Suspicious
    } else {
        state = 2;  // Idle
    }
} else if (param+0x60 == 3) {  // Combat threat
    if (!(flags & 1)) {
        state = 6;  // Combat
        param+0x58 = 8;  // Force order mode
    } else {
        state = 2;  // Idle
    }
} else {  // Other threat
    if (RNG & 0x3F == 0) state = 5;  // Suspicious
    else state = 2;  // Idle
}

// Update animation for new state
anim_id = ANIM_LOOKUP(param+0x58, state);
param+0x59 = anim_id;
```

### 18.9 Entity Structure (Complete Field Map)

| Offset | Type | Field | Notes |
|--------|------|-------|-------|
| `+0x00` | `int*` | vtable pointer | Polymorphic dispatch |
| `+0x04` | `short` | position X (block) | Block coordinate |
| `+0x06` | `short` | position Y (block) | Block coordinate |
| `+0x09` | `char` | sub-block X | 0-95 pixels within block |
| `+0x0E` | `byte` | block type / frame index | Indexes animation table |
| `+0x0F` | `byte` | progress in block | 0 to max_frame for current anim |
| `+0x11` | `byte` | **AI state** | The SIM_TICK switch key |
| `+0x15` | `int*` | current node pointer | Movement/AI struct |
| `+0x16` | `byte` | entity class/type | For animation lookup |
| `+0x19` | `uint` | flags bitfield | `0x80` = order active, etc. |
| `+0x1A` | `int*` | AI controller | AI brain pointer |
| `+0x1D` | `int` | owner/player ID | Compared to `DAT_007c0024+0x210` |
| `+0x21` | `uint` | entity ID/type flags | Low 5 bits = type (0xF = recruit) |
| `+0x2E` | `uint` | processing flags | `0x02` = processing |
| `+0x30` | `int*` | vehicle pointer | Null = no vehicle |
| `+0x40` | `uint` | status flags | `& 3` checked for alert state |
| `+0x45` | `int*` | order list head | Linked list of orders |
| `+0x47` | `int*` | order list iterator | Current position in list |
| `+0x48` | `int` | order list count | |
| `+0x4C` | `int` | processing flag | 0 = done, 1 = processing |
| `+0x54` | `uint` | combat flags | `& 1` = retry, `& 4` = vehicle mode |
| `+0x58` | `byte` | movement mode | 8 = order execution |
| `+0x59` | `byte` | animation ID | Set by ANIM_LOOKUP |
| `+0x60` | `byte` | threat/alert state | 0 = none, 3 = combat |

### 18.10 Arrest and Kidnap Messages

`FUN_00480e30` (arrest) and `FUN_004808c0` (kidnap) are structurally identical:

1. Check if hood priority > 3 (`entity[0x21] & 0x1F > 3`) — low-level hoods don't send messages
2. Allocate 0x18-byte message node via `operator_new`
3. Initialize node with incrementing global ID at `DAT_0079B64C`
4. Format message string with entity name, location, and message body:
   - Arrest: `"I've been arrested!"`
   - Kidnap: `"I've been kidnapped!"`
5. Copy formatted string to heap-allocated buffer
6. Set message type: 6 (arrest) or 3 (kidnap)
7. Call `thunk_FUN_0047FE00` (message dispatch) and `thunk_FUN_00481C20` (message queue insert)

### 18.11 SIM_TICK Case Summary

| Case | State | Key Functions Called | Reusable? |
|------|-------|---------------------|-----------|
| 0-3 | Movement | `MICRO_MOVE`, `STREET_CROSS`, `STREET_ACCESS` | ✅ Core pathfinding |
| 4 | Driving | Speed accumulation, lane changes, `thunk_FUN_00664D50` | ✅ Vehicle system |
| 8 | Vehicle cruise | Speed-based position, block transition | ✅ Vehicle system |
| 9 | Pathfinding | 3-substate: request/follow/arrive, linked list transfer | ✅ Core pathfinding |
| 10 | Advanced driving | 5-substate: init/accel/cruise/lane/decel | ✅ Vehicle system |
| 0xB | Combat approach | Timer + RNG, `thunk_FUN_00565C30` | ✅ Combat system |
| 0xC | Combat engagement | 8-substate, COMBAT_1-4, `thunk_FUN_006ABEA0` | ✅ Combat system |
| 0xD | Fleeing | 7-substate, zigzag patterns, RNG direction | ✅ AI behavior |

### 18.12 Road Access Flags — Static, NOT Traffic Signals (`FUN_00650ee0`)

**Status**: CONFIRMED via RE + user playtest

**Previous interpretation**: The `+0x1220` array was described as "traffic signals" that coordinate traffic flow.

**Corrected interpretation**: The `+0x1220` array is a **static road access grid** baked into the city map at initialization. There are no traffic lights in Gangsters. Vehicle and pedestrian movement is emergent based on map cell passability checks.

**RE Evidence**:

1. **Write search** (`SearchTrafficSignalWrites.java`):
   - 20 defined Ghidra references to `0x007c1244`–`0x007c1400` — **all reads, zero writes**
   - Binary scan of entire `.text` section: 7 STORE instructions with `0x1220`-range displacement — **all in `FUN_00650ee0`**
   - 1 `REP STOSD` (bulk zero-fill) also in `FUN_00650ee0`
   - **No writes found outside the constructor**

2. **Constructor decompilation** (`DecompileRoadAccessInit.java`):
   - `FUN_00650ee0` is a **city object constructor** (7,290 bytes):
     - Allocates 15+ sub-objects via `operator_new(0xc)`
     - Constructs arrays via `_eh_vector_constructor_iterator_`
     - Loads data files: `Data_Business_Suspicion_txt`, `EventLog_txt`, `Data_crime_txt`
     - Loads sprites: `Graphics_Headquarters_spr`
     - Shows `MessageBoxA` for invalid HWND
   - **Line 959**: `REP STOSD` zeroes the entire road access array (0x5E dwords from offset `0x488`)
   - **Lines 965-1013**: 35 specific bytes set to `1` at offsets `0x1221` through `0x127D`
   - These are the **baked-in road access restrictions** — specific road segments have specific directional restrictions

3. **User playtest confirmation**:
   - Roads do not lose access during a game week
   - No in-game visual or behavioral evidence of traffic lights
   - All vehicle movement appears emergent based on other vehicles and map topology

**Conclusion**: The `+0x1220` array defines which directions entities can cross/move at each road segment. It is part of the city's navigation topology, set once during map initialization, and never modified during gameplay. **There are no traffic signals in Gangsters.**

**Implications for Steel City**:
- Road access restrictions should be a property of the road network topology, not a dynamic system
- No traffic light coordination system is needed to replicate Gangsters behavior
- Vehicle interaction is purely emergent from passability checks and vehicle speed/movement logic

---

## 19. Traffic Interactions Deep Dive

**Source**: `ghidra_traffic_interactions.txt` (output of `DecompileTrafficInteractions.java`)
**Date**: August 6, 2026

### 19.1 Architectural Foundation: Billboard Sprite / Grid Occupancy Model

**Key insight**: All entities in Gangsters (pedestrians, vehicles, trams) are
**billboard sprites** at single grid coordinates. There are no 3D meshes,
hitboxes, or physical extents. "Collision" is purely **grid cell occupancy**.

- Each entity occupies one grid cell
- `vtable+0x20` returns whether that entity's cell blocks movement — a
  **logical flag**, not a physics intersection test
- No collision strings ("crash", "hit", "runover") exist in the binary
- No hitbox or mesh intersection code exists
- Vehicle "rerouting" is grid pathfinding around occupied cells, not
  steering around physical obstacles
- The `entity+0x68` target pointer is "which entity is in my way" — a
  pointer to the sprite at the blocking cell

This means the entire traffic interaction system is a **grid-based occupancy
model**: entities check whether cells are passable, and react accordingly.

### 19.2 Pedestrian Entity Awareness (`FUN_005dd910`, ~90 bytes)

**Trigger**: Called from SIM_TICK when `FUN_005dd9d0` (entity search) finds
a non-passable entity in the pedestrian's rectangular search area.

**Behavior**:
1. Only activates if `entity[0x1a]` (offset `0x68`) == 0 — no existing target
2. Stores found entity pointer: `entity[0x1a] = param_2` (the found entity)
3. Calls `vtable+0x8` on the found entity, passing the pedestrian —
   generic "interaction" callback, not type-specific
4. Gets position/direction via `thunk_FUN_0060bab0`
5. Clears bits 10-14 on path structure flags: `& 0xffff83ff`
6. Allocates time budget via `thunk_FUN_00565790` with RNG-based value
7. Sets up new movement via `thunk_FUN_00564060`
8. Triggers AI re-evaluation via `vtable+0x80`

**Key findings**:
- `entity + 0x68` = `entity[0x1a]` = **target entity pointer** (who/what is
  blocking me)
- The 0x700 flags set in SIM_TICK after this call: `entity[0x19] |= 0x700` =
  "aware of nearby entity" status bits
- The reaction is **generic** — no entity type check. A tram, car, truck,
  or another pedestrian would all trigger the same reaction if their
  `vtable+0x20` returns "not passable"

### 19.3 Entity Search (`FUN_005dd9d0`) — Passability, Not Type

The entity search checks `vtable+0x20` (passability) for each entity in the
rectangular search area. It does **NOT** check entity type. Any non-passable
entity triggers the reaction.

**Detection model**:
- Pedestrians: area scan (rectangle `position ± radius`)
- Vehicles: point check (`vtable+0xF4` — "can drive forward to next cell?")
- Both use passability (vtable calls), but different scan patterns

### 19.4 Blocked Crossing Handler (`FUN_005ddb80`, ~120 bytes)

**Trigger**: Called when a pedestrian's street crossing is blocked.

**Behavior**:
1. **Only applies to pedestrians** — checks `entity+0x10 == 0x10`
2. Only if not already blocked: `entity[0x19] & 0x80 == 0`
3. Only if has path: `entity[0x15] != 0`
4. **Probabilistic**: 1/128 chance per tick (`RNG & 0x7f == 0`)
5. Sets blocked flag: `entity[0x19] |= 0x80`
6. Calls `thunk_FUN_00561740()` — check if can wait
7. If can wait: creates wait action (type 0xE = 14) via `thunk_FUN_005614d0(0xe)`
8. Random direction (0 or 1) via RNG
9. Adds to action queue via `thunk_FUN_00563080`
10. Triggers AI re-evaluation via `vtable+0x80`

**Key findings**:
- The 0x80 flag in `entity[0x19]` = "blocked/waiting" status
- Combined with 0x700 (awareness), `entity[0x19]` tracks pedestrian
  interaction state: 0x700 = aware, 0x80 = blocked
- No collision detection, no vehicle interaction — purely pedestrian
  waiting behavior
- The 1/128 probability means pedestrians don't always react to blocked
  crossings — sometimes they just proceed

### 19.5 Vehicle Reroute (`FUN_005d6ef0`, ~200 bytes)

**Trigger**: Called when a vehicle's path is blocked.

**Behavior**:
1. Calls `thunk_FUN_00606340()` — setup
2. **Spiral/grid search pattern**: lateral offset -5 to +5, forward distance
   30 down to 1 (two modes based on `param_3`: searches Y-axis or X-axis)
3. For each candidate position: calls `thunk_FUN_00609060()` — validate
   position (road passability check)
4. If valid position found: calls `thunk_FUN_0060b470(7)` — set new
   destination with mode 7
5. If no valid position: calls `thunk_FUN_00606760()` — fallback (likely
   stop or abort)
6. Outputs new position via `*param_2 = local_8`

**Key findings**:
- **No entity awareness** — rerouting is purely road-network-based,
  not avoiding other vehicles
- The spiral search covers a 11×30 grid area around the vehicle
- Mode 7 destination suggests "rerouted" vs normal pathfinding modes
- This is grid pathfinding around occupied cells, not physical obstacle
  avoidance

### 19.6 SIM_TICK Post-Processing (`FUN_005dddc0`, ~700 bytes)

**Trigger**: Called after entity movement in SIM_TICK.

**Entry conditions**:
- Entity must have no active state (`entity+0x11 & 0x1f == 0`)
- Must be at grid position
- Not already blocked (`entity[0x19] & 0x80 == 0`)
- Must have path (`entity[0x15] != 0`)
- **1/8 chance per tick** (`RNG & 7 == 0`)

**Behavior**: Uses switch on `RNG & 0x1F` (32 cases) to select an action
type (0-7). Action types set different `entity+0x11` values:
- Type 0 → 0x1C (idle/loiter)
- Type 1 → 0x11 (walk)
- Type 2 → 0x21 (wander)
- Type 3 → Complex position-based direction selection (random walk)
- Type 4 → 0x24 (special action)
- Type 5 → 0x05 (look around)
- Type 6 → 0x04 (another action)
- Type 7 → Position-based with grid cell modulo 5

**CRITICAL TRAM FINDING**:
```c
} while (((char)param_1[0x16] != '\b') && (*(char *)(puVar9 + 2) == '\x03'));
```
- `param_1[0x16]` = entity type field
- `'\b'` = 8 = tram entity type
- `puVar9 + 2` = selected action type
- `'\x03'` = action type 3 (random walk direction)

**This is the first direct RE evidence of tram-specific behavior exclusion**:
If the entity is a tram (type 8) AND action type 3 was selected, the loop
**re-rolls**. Trams are excluded from random walk directions — they must
follow fixed routes.

### 19.7 Game State Transition Handler (`FUN_00414ba0`, 7273 bytes)

**Corrected interpretation**: This is a **game state/mode transition handler**
for the world simulation, NOT a UI/view transition handler. The `param_1`
object has offsets up to `0x4600+` — this is the game world/state object.

The switch on `param_1 + 0x114` (current mode) with `param_2` (new mode)
is a state machine for game phases. Functions like `thunk_FUN_0062a390`
are likely **invalidating display regions** (telling the renderer "this area
changed, redraw it"), not setting up user interfaces.

**Case 8 (Tram simulation phase)**:
- Calls `thunk_FUN_005bba40()` if `param_1 + 0x2dcc == 0` (first entry)
- Calls `thunk_FUN_006c7a90()` — tram system update (route advancement,
  stop processing — NOT yet decompiled)
- Clears `param_1 + 0x390d` flag
- Copies state from working fields to active fields
- Tram orders stored at `entity + 0x3904`
- Tram order processing: if `entity + 0x3904 != 0` and
  `entity + 0x1462 == 0x01` (player in control) and
  `*(entity + 0x3904) + 0xfc != 0` (order has data),
  calls `thunk_FUN_005b3440(0xffffffff, 0)` — process order

**No evidence of tram right-of-way logic** in this function. The tram-specific
logic is about state management and order processing, not traffic priority.
Actual tram movement functions (`thunk_FUN_005bba40`, `thunk_FUN_006c7a90`)
have NOT been decompiled yet.

### 19.8 Tram Spawn/Dispatch (`FUN_0054d850`, 2891 bytes)

A **spawn/dispatch function** that activates for entity state `0x3A` (58):
- Reads `entity+0x70` and `entity+0x71` (grid position)
- Iterates through entity array at `DAT_007c0024[0x278]` (stride 0x84)
- Uses `entity+0x68` as a position/bounds field for range checking
- Excludes entity types 0x66, 0x1F, 0x43 from candidate list
- Does 3×3 grid search, checking `vtable+0x20` (passability) and
  `vtable+0x12C` (type identity)
- If passability returns 3, removes entity from candidates
- Randomly selects from remaining candidates, sorts by property at `+0x40`
- Creates new entities (0x6C bytes) via `thunk_FUN_00626520`
- **Special handling for entity type 8 (tram)**: creates 4 additional
  entities in a directional pattern

### 19.9 Vehicle Driving Decision (`FUN_004cb0c0`, 614 bytes)

**Behavior**:
1. Checks path flags at `entity[0x15]+0xC`
2. Calls `vtable+0xF4` — "can drive forward" check (vehicle-specific
   passability variant)
3. If can't drive: attempts pathfinding via `thunk_FUN_0060be40`,
   then `thunk_FUN_00609060` (validate route)
4. If valid route found: starts driving via `thunk_FUN_00607e80`
5. If invalid: calls `thunk_FUN_00608b00` (stop/abort)
6. **Iterates `entity[0x45]` (passenger list)** — moves all passengers
   when vehicle moves. Each passenger gets `thunk_FUN_00564060`
   (movement setup) and `vtable+0x80` (AI tick)

**Key finding**: Vehicles move passengers with them. The passenger list at
`entity[0x45]` is traversed and each passenger's position is updated.

### 19.10 Vehicle AI Decision (`FUN_004c3dd0`, 5624 bytes)

**Behavior**:
- Reads `entity+0x59` (driver ID), looks up skill in table
- If driver skill too low → resets state to 4 (stopped)
- Checks `entity[9] & 0x38000000` — vehicle movement mode flags
- Manages order processing via `vtable+0x58`
- Periodic update via `vtable+0xA0`
- State machine on `entity[0x54] & 0xC` (2-bit sub-state)

### 19.11 Entity+0x179 (Vehicle In-Use Flag)

Written by `FUN_004c3dd0` and `FUN_004cb0c0`. This flag tracks whether a
vehicle is currently in use (has a driver/passenger). The binary pattern
scan for offset `0x179` found these two functions as the primary writers.

### 19.12 String Search Results — No Traffic Strings

All traffic-related strings have **NO code references**:
- "tram", "train" — exist in data section, never referenced by code
- "trolley", "streetcar", "collision", "crash", "yield", "right-of-way",
  "blocked", "traffic", "pedestrian", "reroute", "detour" — NOT FOUND
- "wait" — 7 instances, all no refs
- "ped" — 10 instances, all no refs

**Conclusion**: The game binary contains no debug/diagnostic strings for
traffic interaction systems. All logic is purely numeric/type-based without
string identifiers.

### 19.13 Caller Analysis — All Thunk-Mediated

All 5 key functions (`FUN_005dd910`, `FUN_005ddb80`, `FUN_005d6ef0`,
`FUN_005dddc0`, `FUN_00414ba0`) show **0 direct call references** — they
are all called via thunks (cross-segment stubs), consistent with the
binary's architecture.

### 19.14 Summary: Confirmed vs Refuted

| Prior Inference | RE Evidence | Verdict |
|----------------|-------------|---------|
| Pedestrians have entity awareness | `FUN_005dd910` stores target at `entity[0x1a]` | ✅ Confirmed |
| `entity+0x68` is important state | It's `entity[0x1a]` = target entity pointer | ✅ Confirmed |
| 0x700 flags = pedestrian awareness | Set after entity search in SIM_TICK | ✅ Confirmed |
| Crossing has 16-tick cost | `thunk_FUN_00565c30(0, 0x10)` — 0x10 = 16 | ✅ Confirmed |
| Blocked crossing triggers wait | `FUN_005ddb80` sets 0x80 flag + creates wait action | ✅ Confirmed |
| Vehicles reroute when blocked | `FUN_005d6ef0` does spiral grid search | ✅ Confirmed |
| Trams have right-of-way over peds | No RE evidence found | ❌ Refuted (no evidence) |
| Trams get special treatment in post-proc | `FUN_005dddc0` re-rolls action type 3 for trams | ✅ Confirmed |
| Pedestrian avoidance is type-specific | Search uses only passability, not entity type | ❌ Refuted — generic |
| Vehicles move passengers with them | `FUN_004cb0c0` iterates `entity[0x45]` list | ✅ Confirmed |
| Tram dispatch is type-specific | `FUN_0054d850` has special case for type 8 | ✅ Confirmed |
| Traffic signals exist | No evidence in any RE pass | ❌ Refuted |
| All entities are billboard sprites | No collision/hitbox/physics code in binary | ✅ Confirmed (architectural) |
| `FUN_00414ba0` is tram movement | It's a game state transition handler | ❌ Refuted — state management, not movement |

### 19.15 Steel City Design Implications

1. **Grid occupancy model**: Traffic interactions should be grid-based
   passability checks, not physics collisions. Entities occupy cells and
   block movement through those cells.

2. **Generic detection, not type-specific**: Pedestrians detect ANY blocking
   entity, not just vehicles. The reaction is the same regardless of what
   is blocking them. Type-specific behavior (like trams being excluded from
   random walks) is handled at the action-selection level, not detection.

3. **Parallel detection systems**: Pedestrians use area scans, vehicles use
   point checks. Steel City should implement both patterns.

4. **Tram route enforcement**: Trams are excluded from random walk actions.
   They must follow fixed routes. This is enforced at the post-processing
   level (re-rolling action type 3), not at the movement level.

5. **No traffic signals**: The game has no traffic light system. All traffic
   flow is emergent from passability checks and grid occupancy.

6. **Billboard sprite architecture**: Since all entities are single-point
   sprites, there's no need for complex collision detection. Steel City
   should use the same grid-occupancy model if replicating original behavior.
