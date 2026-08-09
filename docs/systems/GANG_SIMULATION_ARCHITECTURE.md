# Gang Simulation Architecture — Multi-Hood System Design

**Created**: Aug 9, 2026
**Status**: 📐 DESIGN — awaiting approval before implementation
**Relates to**: `docs/core/REVERSE_ENGINEERING_FINDINGS.md`, `docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`, `docs/systems/COMBAT_VEHICLE_DESIGN.md`, `docs/systems/VOXEL_GROUP_ANIMATION.md`, `docs/data/GAME_DATA_REFERENCE.md`

---

## Purpose

Replace the single-Vinny prototype path with a scalable multi-hood simulation
that faithfully recreates the original Gangsters: Organized Crime architecture
while leveraging Steel City's existing GPU-instanced rendering and real-time
pathfinding systems.

---

## Problem Statement

### Current State (Deprecated)

The codebase has **three parallel systems** that overlap and conflict:

| System | Files | Purpose | Status |
|--------|-------|---------|--------|
| **Single-Vinny spawn** | `CityMap3D.SpawnCharacter()`, `SpawnedCharacter`, `FollowCamera`, `EventPlayer` | Special-cased player hood with follow cam, tick-based sim | ❌ Deprecated — buggy spawn, falling physics, log spam |
| **Tick simulation** | `TickSimulation.cs`, `SimulationManager.cs` | Game logic: orders, dialog, extortion, tick budgets | ⚠️ Single-hood only — game logic is sound but not scalable |
| **Stress test** | `StressTestSpawner.cs`, `StressTestAgent.cs` | 100-agent A* pathfinding stress test | ✅ Works — proves multi-hood movement but has no game logic |

### What's Wrong

1. **Vinny gets special treatment** — unique spawn path, follow camera, selector UI, teleport button. He's just 1 of N hoods.
2. **Spawn height is wrong** — `groundY + 0.5f` assumes flat terrain; actual terrain varies. Causes falling + log spam.
3. **Physics is rudimentary** — `VoxelCharacter.ApplyGravity()` is a single downward raycast with no horizontal collision. Micro-falling on rising terrain generates 999+ "Landed on ground" logs.
4. **`SimulationManager` is single-hood** — one `activeOrder`, one `currentPath`, one `pathIndex`. Cannot simulate a gang.
5. **`EventPlayer` is a single-character renderer** — consumes `SimulationManager` events for one `VoxelCharacter`. No multi-hood support.
6. **FollowCamera is buggy** — deprecated, to be removed.

### What Works (Keep These)

- **`SimulationManager` game logic** — orders, dialog phases, extortion resolution, tick budgets. This is the faithful recreation of the original game's simulation engine.
- **`StressTestAgent` movement** — real-time A* pathfinding, waypoint following, lerp movement, face-direction rotation. Proven at 100 agents.
- **`VoxelCharacter` rendering** — GPU instanced, 1 draw call for all characters, animation state in instance buffer.
- **`WaypointGraph` + `Pathfinder`** — city-wide pathfinding graph with time-sliced async path requests.
- **`VoxelCollisionWorld`** — ground probing for terrain height at any XZ.

---

## Original Game Architecture (from RE Findings)

Source: `docs/core/REVERSE_ENGINEERING_FINDINGS.md`

### Hood Lifecycle

```
Planning Phase (player assigns orders)
    │
    ├── Player selects hood
    ├── Player selects order type (extort, collect, kill, etc.)
    ├── Order stored in hood's order queue (doubly-linked list)
    ├── No execution — orders are just stored
    │
Working Phase (simulation begins)
    │
    ├── FUN_0049a530 (Gang Order Dispatch)
    │   ├── Iterates ALL hoods in gang's linked list
    │   ├── For each hood:
    │   │   ├── Allocate 12,000 tick time budget
    │   │   ├── Set movement destination from order
    │   │   ├── Calculate priority (distance + RNG + hood level)
    │   │   └── Register entity in active movement list
    │   └── All hoods begin moving simultaneously
    │
    ├── FUN_005d2740 (Per-Tick Simulation — runs every tick for ALL entities)
    │   ├── Process movement state machine (Init → Pathfind → Walk → Arrive)
    │   ├── Decrement timers (action, combat, status, animation)
    │   ├── Handle street crossings (16 ticks each)
    │   ├── Handle random wandering (16-47 ticks)
    │   ├── Advance animation frames (every 16 ticks)
    │   └── Process AI state transitions (idle/alert/suspicious/combat/flee)
    │
    ├── FUN_00583dc0 (Order Execution at Destination)
    │   ├── Compare position to target
    │   ├── Trigger order resolution via vtable
    │   ├── Order type determines action duration:
    │   │   ├── Extort: 166 ticks
    │   │   ├── Intimidate: 333 ticks
    │   │   ├── Kill: 6000 ticks
    │   │   ├── Collect: 166 ticks
    │   │   └── etc.
    │   └── On completion: vehicle returned, arrest check, next order
    │
    └── Weekly Report (FUN_00596662)
        ├── Process all gangs sequentially
        ├── Death events, extortion results, territory changes
        └── Reset time budgets for next week
```

### Key Mechanics to Preserve

| Mechanic | Original Value | Steel City Adaptation |
|----------|---------------|----------------------|
| **Weekly time budget** | 12,000 ticks per hood | Keep 12,000 — core tension constraint |
| **Walking cost** | Full 12,000 ticks (entire budget) | Make proportional to distance (RE recommends improvement) |
| **Driving cost** | 32 ticks (375× faster) | Use 10-20× ratio instead (RE recommends improvement) |
| **Street crossing** | 16 ticks per crossing | Keep — adds up over distance |
| **Order action time** | Per-type (extort=166, kill=6000, etc.) | Keep exact values from `GAME_DATA_REFERENCE.md` |
| **Vehicle decision** | Distance > 64 units → drive; else walk | Keep distance-based threshold |
| **Order priority** | Distance + RNG(0-31) + hood level | Keep — creates unpredictable execution order |
| **Arrest vulnerability** | Walking hoods can be arrested en route | Implement in later phase |
| **Per-hood independence** | Each hood has own queue, budget, state | Core architectural principle |

### Entity Types in Original

| Type | Population | Role |
|------|-----------|------|
| Hoods | 5 per gang (start), grows via recruit | Player-controlled order executors |
| Civilians | 2,000 | Ambient population, crime victims, squealers |
| Police | 400 | Law enforcement, arrest logic |
| FBI | 100 | High-level investigation |
| Vehicles | Variable | Transport for hoods |

---

## Steel City Architecture

### Design Principles

1. **No special-cased hoods** — Vinny is hood #0, not a unique entity. All hoods use the same spawn, movement, and rendering path.
2. **N independent SimulationManagers** — one per hood. Each has its own order queue, time budget, and state machine. The gang dispatcher iterates all of them.
3. **Real-time movement, tick-based game logic** — movement uses `StressTestAgent`'s proven real-time lerp + A* pathfinding. Game logic (order resolution, dialog, tick budgets) uses `SimulationManager`'s tick system. These are decoupled: movement is visual, ticks are logical.
4. **GPU instanced rendering for all hoods** — Tier 2 from `DYNAMIC_OBJECT_RENDERING_TIERS.md`. 1 draw call regardless of hood count. Already implemented.
5. **Ground probe at spawn** — use `VoxelCollisionWorld.ProbeGround()` to find correct Y at spawn XZ. No more `groundY + 0.5f` guesswork.

### System Components

```
┌─────────────────────────────────────────────────────────────────┐
│                        GAME PHASES                              │
│                                                                 │
│  PLANNING PHASE          →    WORKING PHASE        →    REPORT  │
│  (player assigns orders)      (simulation runs)         (weekly)│
└─────────────────────────────────────────────────────────────────┘
```

#### Planning Phase

- Player views city in **overhead 3D camera** (current `CityMap3D` camera)
- Player clicks a block → sees block info (existing UI)
- Player clicks "Assign Order" → selects order type + target block
- Order is stored in the selected hood's `OrderQueue`
- **No simulation runs** — hoods are idle at their home block
- Player can have 1-N hoods (starts with 5 per original game data)

#### Working Phase

- `GangDispatcher` iterates all hoods, calls `hood.BeginNextOrder()`
- Each `HoodAgent` runs independently:
  - Requests A* path via shared `Pathfinder` (time-sliced)
  - Walks path in real-time (lerp between waypoints)
  - On arrival: enters order execution (tick-based countdown)
  - On order complete: queues return path or next order
  - Consumes tick budget per action (extort=166, kill=6000, etc.)
- **Camera modes** (future): overhead 3D, street free-cam, 2D city map
- For now: keep existing overhead camera

#### Report Phase

- Weekly summary: extortion income, territory changes, hood deaths
- Reset all hood time budgets to 12,000
- Process AI gang decisions (future)

### Class Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                      HoodSpawner                              │
│  (replaces StressTestSpawner + CityMap3D.SpawnCharacter)     │
│                                                              │
│  - Spawns N VoxelCharacter + HoodAgent pairs               │
│  - Auto-starts on play (no F8)                              │
│  - Assigns home block (player HQ)                           │
│  - Manages gang roster (add/remove hoods)                   │
└──────────────────┬───────────────────────────────────────────┘
                   │ creates N of
                   ▼
┌──────────────────────────────────────────────────────────────┐
│                      HoodAgent                                │
│  (merges StressTestAgent + SimulationManager)               │
│                                                              │
│  MOVEMENT (real-time, from StressTestAgent):                │
│  - A* pathfinding via shared Pathfinder                     │
│  - Waypoint lerp movement                                   │
│  - Face direction rotation                                  │
│  - Ground Y from VoxelCollisionWorld probe                  │
│                                                              │
│  GAME LOGIC (tick-based, from SimulationManager):           │
│  - Order queue (List<Order>)                                │
│  - Time budget (12,000 ticks/week)                          │
│  - Order state machine:                                     │
│    Idle → PathToTarget → ExecuteOrder → PathHome → Idle    │
│  - Order resolution (extort, collect, kill, etc.)           │
│  - Tick consumption per action                              │
│                                                              │
│  ANIMATION (drives CharacterAnimation):                     │
│  - Walking → AnimState.Walking                              │
│  - At target → AnimState.Checking / Aiming                  │
│  - Idle → AnimState.Idle                                    │
│  - Falling → AnimState.Falling                              │
│                                                              │
│  RENDERING (via VoxelCharacter):                            │
│  - GPU instanced (Tier 2)                                   │
│  - animState/animTime/animSpeed in instance buffer          │
└──────────────────┬───────────────────────────────────────────┘
                   │ owns one
                   ▼
┌──────────────────────────────────────────────────────────────┐
│                   VoxelCharacter                              │
│  (rendering wrapper — unchanged from current)               │
│                                                              │
│  - Loads .stasset voxel data                                │
│  - Registers with VoxelChunkManager for GPU instancing      │
│  - transform.position = volume corner (for raymarcher)      │
│  - PlaceAtCenter(worldCenter) for positioning               │
│  - ApplyGravity() — KEEP but fix (see Physics section)      │
└──────────────────────────────────────────────────────────────┘
```

### Gang Dispatcher

```
┌──────────────────────────────────────────────────────────────┐
│                   GangDispatcher                              │
│  (new — replaces EventPlayer's single-hood role)            │
│                                                              │
│  - Holds reference to all HoodAgents in gang                │
│  - StartWorkingPhase(): iterates all hoods,                 │
│    calls hood.BeginNextOrder()                              │
│  - Per-tick update: advances each hood's tick counter       │
│  - Tracks gang-wide stats (ticks spent, orders completed)   │
│  - EndOfWeek(): reset budgets, generate report              │
└──────────────────────────────────────────────────────────────┘
```

---

## Hood Lifecycle Detail

### Spawn

```
1. HoodSpawner creates GameObject "Hood_{i}"
2. AddComponent<VoxelCharacter>
   - assetFileName = "character_hoodlum_0.stasset"
   - voxelSize = cityMap.CharacterVoxelSize
   - chunkManager = (auto-found)
   - collisionWorld = (auto-found)
   - useInstancing = true
   - centerPosition = (spawnX, groundY, spawnZ)  ← probed from collision world
   - useWorldPosition = false
3. AddComponent<HoodAgent>
   - Initialize(homeBlock, waypointGraph, pathfinder, mapRoot)
4. AddComponent<CharacterAnimation>
   - autoDetectWalking = true
5. VoxelCharacter.Start() loads asset, registers instanced
6. HoodAgent.Start() queues initial idle state at home block
```

### Ground Height Resolution

```
spawnX, spawnZ = block center coordinates
probeOrigin = (spawnX, +50f, spawnZ)  // probe from above
collisionWorld.ProbeGround(probeOrigin, 100f, out groundY, out _)
centerPosition = (spawnX, groundY, spawnZ)
```

This replaces the hardcoded `groundY = voxelSize * 2f` that assumed flat terrain.

### Order Assignment (Planning Phase)

```
1. Player selects a hood (clicks hood or selects from roster UI)
2. Player selects order type (extort, collect, kill, etc.)
3. Player selects target block (clicks block on map)
4. GangDispatcher.AssignOrder(hoodId, order)
   - hood.OrderQueue.Enqueue(order)
   - No execution — just stored
5. UI shows queued orders for that hood
```

### Order Execution (Working Phase)

```
1. GangDispatcher.StartWorkingPhase()
2. For each HoodAgent:
   - hood.BeginNextOrder()
   - State: Idle → PathToTarget
   
3. HoodAgent.Update() (per-frame):
   - If PathToTarget:
     - Request A* path (async, time-sliced)
     - When path received: start walking waypoints
     - Walk: lerp between waypoints, update transform.position
     - Set CharacterAnimation state = Walking
     - On arrival: State → ExecuteOrder
     
   - If ExecuteOrder:
     - Set CharacterAnimation state = Checking/Aiming/etc.
     - Tick countdown: order.actionTicks (extort=166, kill=6000, etc.)
     - Consume ticks from time budget
     - On complete: resolve order (success/fail via GameEngine)
     - State → PathHome (or PathToNextOrder if queue non-empty)
     
   - If PathHome:
     - Request return path
     - Walk home
     - On arrival: State → Idle
     - If time budget remaining and orders queued: begin next order
     
   - If Idle:
     - Set CharacterAnimation state = Idle
     - Wait for next order or week end
```

### Tick Budget Consumption

| Action | Ticks Consumed | Source |
|--------|---------------|--------|
| Walk to target | Proportional to distance (path nodes × link cost) | RE: originally 12,000 flat; we improve |
| Drive to target | Proportional to distance ÷ 15 (vehicle speed multiplier) | RE: originally 32 flat; we improve |
| Street crossing | 16 per crossing | RE: exact |
| Extort action | 166 | `GAME_DATA_REFERENCE.md` |
| Collect action | 166 | `GAME_DATA_REFERENCE.md` |
| Intimidate action | 333 | `GAME_DATA_REFERENCE.md` |
| Kill action | 6,000 | `GAME_DATA_REFERENCE.md` |
| Assault action | 6,000 | `GAME_DATA_REFERENCE.md` |
| Bomb action | 333 | `GAME_DATA_REFERENCE.md` |
| Torch action | 333 | `GAME_DATA_REFERENCE.md` |
| Recruit action | 166 | `GAME_DATA_REFERENCE.md` |
| Random wander | 16-47 (RNG) | RE: exact |
| Patrol step | 1 | RE: exact |

**Steel City improvement**: Walking cost is proportional to path length, not a flat 12,000. This makes short trips cheap and long trips expensive, creating more granular strategic decisions. The RE doc explicitly recommends this: *"Steel City should consider proportional travel time (distance-based, not flat 12000)."*

---

## Physics Profile

### Current (Broken)

`VoxelCharacter.ApplyGravity()`:
- Single downward raycast from character feet
- If ground within `snapDistance` (0.05): snap to ground
- If ground within `groundProbeDistance` (2.0) but beyond snap: fall
- If no ground found: fall forever
- **Bug**: On rising terrain, ground is just past snap distance every frame → micro-fall → log spam

### Fixed (Minimal)

```
Each frame:
1. Probe ground from current XZ + small height offset
2. If ground found:
   - If dist < snapDistance: snap to groundY, onGround = true
   - If dist < groundProbeDistance: 
     - Apply gravity (verticalVelocity -= g * dt)
     - Move toward ground
     - On landing: log ONLY on transition (was airborne → now grounded)
   - If dist > groundProbeDistance: fall (shouldn't happen with correct spawn)
3. If no ground found: maintain current Y (don't fall forever)
```

**Key fix**: Increase `snapDistance` from 0.05 to 0.3 (covers terrain height variation per block). Only log "Landed" on true airborne → grounded transition.

### Future (Character Controller)

For combat (cover, crouch, hit reactions), a proper character controller may be needed. But for the current phase — walking on flat city terrain with ground snapping — the minimal fix is sufficient. The RE findings show the original game's movement was purely waypoint-based with no physics engine; gravity is a Steel City addition for 3D terrain.

---

## Camera Tiers

### Current (Deprecated)
- `FollowCamera` — follows single Vinny, buggy, to be removed

### Planned (Future — not in this implementation)

| Camera Mode | Trigger | Description |
|------------|---------|-------------|
| **City Overview** | Default | Overhead 3D, see whole city, click blocks/hoods |
| **District View** | Click district | Zoom to district, see hood movements |
| **Street Free-Cam** | Click street | WASD + mouse free camera at street level |
| **2D City Map** | Toggle button | Top-down 2D strategic view (separate rendering) |

For this implementation: **keep existing `CityMap3D` overhead camera**. Camera modes are a separate future task.

---

## What Gets Removed

| Component | File | Reason |
|-----------|------|--------|
| `CityMap3D.SpawnCharacter()` | `CityMap3D.cs:1090-1167` | Replaced by `HoodSpawner` |
| `CityMap3D.SpawnedCharacter` | `CityMap3D.cs:328` | No more special single character |
| `FollowCamera` | `FollowCamera.cs` | Deprecated, buggy, replaced by future camera system |
| `EventPlayer` | `EventPlayer.cs` | Single-hood event renderer, replaced by `HoodAgent` |
| `TickSimulation` | `TickSimulation.cs` | Older single-hood sim, superseded by `HoodAgent` |
| Vinny selector UI | `GameUIController.cs:1498-1574` | No more special Vinny teleport |
| Vinny placement mode | `GameUIController.cs` | Removed with selector |
| `VoxelCharacter.centerPosition` field | `VoxelCharacter.cs:37` | Set by `HoodSpawner` directly |

### What Gets Kept (Modified)

| Component | File | Change |
|-----------|------|--------|
| `VoxelCharacter` | `VoxelCharacter.cs` | Keep as render wrapper. Fix `ApplyGravity()` snap distance + log guard. Remove `centerPosition` field (set transform directly). |
| `SimulationManager` | `SimulationManager.cs` | Keep game logic. Extract order resolution, tick budgets, dialog phases into `HoodAgent`. Or keep as embedded component. |
| `StressTestAgent` | `StressTestSpawner.cs` | Movement pattern absorbed into `HoodAgent`. File can remain as stress test tool. |
| `CharacterAnimation` | `CharacterAnimation.cs` | Keep — driven by `HoodAgent` instead of `EventPlayer` |
| `PedestrianLookAround` | `PedestrianLookAround.cs` | Keep — works on any `CharacterAnimation` |
| `WaypointGraph` + `Pathfinder` | — | Keep — shared by all `HoodAgent` instances |
| `VoxelCollisionWorld` | — | Keep — used for ground probing |
| `VoxelChunkManager` | — | Keep — GPU instanced rendering unchanged |

### What Gets Created

| Component | File | Purpose |
|-----------|------|---------|
| `HoodAgent` | `HoodAgent.cs` | Merges `StressTestAgent` movement + `SimulationManager` game logic |
| `HoodSpawner` | `HoodSpawner.cs` | Replaces `StressTestSpawner` + `CityMap3D.SpawnCharacter()` |
| `GangDispatcher` | `GangDispatcher.cs` (or embedded in `HoodSpawner`) | Iterates all hoods, starts working phase, manages gang roster |

---

## Implementation Phases

### Phase 1: Core Hood System (this implementation)

1. Create `HoodAgent.cs` — `StressTestAgent` movement + order queue + tick budget
2. Create `HoodSpawner.cs` — auto-spawn N hoods at player HQ, ground-probe spawn
3. Fix `VoxelCharacter.ApplyGravity()` — increase snap distance, fix log guard
4. Remove `FollowCamera`, `EventPlayer`, `TickSimulation` references from `GameUIController`
5. Remove `CityMap3D.SpawnCharacter()` and `SpawnedCharacter` property
6. Remove Vinny selector/teleport UI from `GameUIController`
7. Wire `HoodSpawner` to auto-start with `characterCount = 1` (configurable in Inspector)
8. Keep existing overhead camera

**Result**: 1 hood spawns at correct ground height, walks to a random target block, "extorts" (waits 166 ticks), walks home. No follow cam, no special Vinny UI. Inspector `characterCount` can be set to 5, 100, etc.

### Phase 2: Order Assignment UI (future)

- Planning phase UI: select hood, assign orders, see queued orders
- Block click → order type selection → target assignment
- Order queue visualization per hood

### Phase 3: Full Gang Simulation (future)

- Multiple gangs (AI + player)
- Gang dispatcher iterates all gangs
- Weekly report generation
- Territory/economy integration

### Phase 4: Vehicle System (future)

- Distance-based walk/drive decision (threshold from RE: 64 units)
- Vehicle entities (Tier 2 instanced rendering)
- Driving costs proportional ticks (10-20× faster than walking)
- Vehicle assignment as strategic resource

### Phase 5: Camera Modes (future)

- City overview → district → street free-cam → 2D map
- Smooth transitions between modes

---

## Testing Plan

### Phase 1 Verification

1. **Spawn test**: 1 hood spawns at player HQ at correct ground height (probe, not hardcoded)
2. **Movement test**: hood walks to random target block via A* pathfinding
3. **Order test**: hood "extorts" at target (waits ~166 ticks), then walks home
4. **Multi-hood test**: set `characterCount = 5`, verify all 5 spawn and move independently
5. **Stress test**: set `characterCount = 100`, verify FPS remains acceptable (1 draw call)
6. **No log spam**: verify no "Landed on ground" spam in console
7. **No D3D12 errors**: verify `_GroupIDs` buffer still bound correctly
8. **Animation**: verify walking animation state triggers during movement, idle when stopped
9. **No follow camera**: verify existing overhead camera works without FollowCamera component

### Critical Test Points

- **Spawn height**: Check console log for `[HoodSpawner] Spawned hood 0 at (X, Y, Z) groundY=Y.YY` — Y should match terrain height at that block, not 0.7
- **Ground snap**: Hoods should not micro-fall on terrain. Watch for absence of "Landed on ground" logs after initial spawn.
- **Instance buffer**: With 100 hoods, verify 1 draw call in stats (Frame Debugger)
- **Path requests**: With 100 hoods, verify time-sliced pathfinding doesn't stall (check `pathfinder.PendingRequests` stays bounded)

---

## Relationship to Existing Docs

- **`REVERSE_ENGINEERING_FINDINGS.md`** — Source of all original game mechanics. This doc translates those findings into Steel City architecture.
- **`DYNAMIC_OBJECT_RENDERING_TIERS.md`** — Hoods are Tier 2 (instanced). This doc doesn't change rendering — it changes the simulation logic that drives the rendering.
- **`COMBAT_VEHICLE_DESIGN.md`** — Phase 4 vehicle system will integrate with `HoodAgent`'s movement state machine. §13 adds physical projectiles + spatial hash — `HoodAgent` positions feed the spatial hash, and `HoodAgent` combat AI spawns projectiles. Stray bullets hitting civilians creates emergent consequences via `CrimeSystem`.
- **`VOXEL_GROUP_ANIMATION.md`** — `CharacterAnimation` (already implemented) is driven by `HoodAgent` instead of `EventPlayer`.
- **`GAME_DATA_REFERENCE.md`** — Order action times, crime table, character archetypes. Direct data source for `HoodAgent` order resolution.
- **`3D_CITY_RENDERING.md`** — Entity budgets and camera modes. This doc's camera tier plan aligns with that vision.

---

## Open Questions

1. **Should `SimulationManager` be embedded in `HoodAgent` or kept separate?**
   - Option A: `HoodAgent` contains a `SimulationManager` instance (composition)
   - Option B: `HoodAgent` absorbs `SimulationManager`'s logic directly (merge)
   - Recommendation: Option B — `SimulationManager` is small enough to merge, and having one class per hood is cleaner

2. **Should `HoodSpawner` replace `StressTestSpawner` or coexist?**
   - Option A: Replace — `HoodSpawner` is the new standard, stress test is deprecated
   - Option B: Coexist — `HoodSpawner` for game, `StressTestSpawner` kept as debug tool
   - Recommendation: Option B — stress test is useful for performance validation

3. **Should tick budget be real-time or simulation-time?**
   - Original game: ticks are simulation time (not real seconds)
   - `StressTestAgent`: uses real-time `Time.deltaTime` for movement
   - Recommendation: Use real-time for movement (proven), simulation ticks for order resolution (countdown timer that decrements at a configurable rate). The `tickInterval` from `SimulationManager` (0.08s) means ~12.5 ticks/sec real-time — slow enough to watch, fast enough to not bore.

4. **How many hoods should the player start with?**
   - Original game: 5 (from `GAME_DATA_REFERENCE.md`)
   - Recommendation: Start with 5, configurable in Inspector for testing
