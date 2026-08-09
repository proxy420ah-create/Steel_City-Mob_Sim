# Engine Integration Plan — From Gangsters.exe to Steel City

**Created**: August 5, 2026
**Status**: 📐 Planning — Architecture mapping from reverse-engineered binary
**Source**: `REVERSE_ENGINEERING_FINDINGS.md` (Sections 1-18)
**Target**: Steel City: Mob Sim (Unity 6, C#)

---

## Purpose

This document maps the reusable systems discovered in the Gangsters.exe binary
analysis to concrete Steel City implementation plans. It identifies which
original engine systems are worth porting, how they should be modernized, and
what implementation order makes sense for building momentum.

---

## 1. Reusable Systems Overview

The binary analysis revealed **four core systems** that form the backbone of the
simulation. These are the highest-value ports for Steel City:

| System | Original Function | Complexity | Reusability | Priority |
|--------|-------------------|------------|-------------|----------|
| **SIM_TICK Orchestrator** | `FUN_005d2740` (16,980 bytes) | High | ✅ Core architecture | P0 |
| **Pathfinding & Waypoints** | `FUN_005844a0`, `FUN_00564060`, `FUN_00609cf0` | Medium | ✅ Core movement | P0 |
| **Vehicle System** | `FUN_00462f30`, `FUN_005dc080`, `FUN_00660e60` | Medium | ✅ Strategic layer | P1 |
| **NPC Collision & Traffic** | `FUN_005dc8c0`, `FUN_00609cf0`, road network data | Medium | ✅ Emergent behavior | P1 |

Supporting systems that enhance the core:

| System | Original Function | Complexity | Reusability | Priority |
|--------|-------------------|------------|-------------|----------|
| **Combat State Machine** | `thunk_FUN_004cb870` through `thunk_FUN_004cc470` | High | ✅ Combat | P2 |
| **AI State Machine** | `FUN_005e0560` | Medium | ✅ AI behavior | P1 |
| **Time Budget** | `FUN_00565790`, `FUN_00565c30` | Low | ✅ Core constraint | P0 |
| **Animation Lookup** | `FUN_0048a750` | Low | ✅ Visual | P2 |
| **Order Queue** | `FUN_00563150`, `FUN_00563190`, `FUN_005691a0` | Low | ✅ Order processing | P0 |
| **Portrait/Character Gen** | `FUN_0063a550` | Medium | ✅ Character system | P2 |

---

## 2. SIM_TICK: The Master Orchestrator

### What the Original Does

`FUN_005d2740` is a ~4,400-line function that processes every entity every tick
through a massive switch on the AI state byte (`entity + 0x11`). It handles:

- Timer decrements (4 separate timers per entity)
- Animation frame advancement (every 16 ticks)
- Movement (cases 0-3: block-by-block pathfinding)
- Driving (cases 4, 8, 10: speed, lane changes, cruise, deceleration)
- Pathfinding (case 9: 3-substate request/follow/arrive)
- Combat (cases 0xB, 0xC: approach + 8-substate engagement)
- Fleeing (case 0xD: 7-substate zigzag patterns)
- 5 linked-list queues for entity management with node recycling

### Why It's the Core Architecture

SIM_TICK is the **single entry point** for all entity simulation. Everything
flows through it — movement, combat, AI decisions, vehicle updates, animation.
This makes it the natural template for Steel City's `SimulationManager`.

### Steel City Implementation

```
SimulationManager (C#)
├── Tick() — called every simulation step
│   ├── UpdateTimers(entity)          // 4 timer decrements
│   ├── UpdateAnimation(entity)       // frame advancement
│   ├── ProcessAIState(entity)        // the big switch
│   │   ├── case Movement:            // block pathfinding
│   │   ├── case Driving:             // vehicle physics
│   │   ├── case Pathfinding:         // waypoint following
│   │   ├── case CombatApproach:      // timer + target selection
│   │   ├── case CombatEngagement:    // 8-substate combat
│   │   └── case Fleeing:             // zigzag patterns
│   └── PostProcess(entity)           // queue management
├── EntityManager
│   ├── ActivePool (LinkedList<Entity>)
│   ├── UpdateQueue (LinkedList<Entity>)
│   ├── MovementQueue (LinkedList<Entity>)
│   ├── CombatQueue (LinkedList<Entity>)
│   └── SecondaryQueue (LinkedList<Entity>)
└── NodePool (recycled linked list nodes)
```

### Key Modernization Decisions

| Original | Steel City | Rationale |
|----------|-----------|-----------|
| Single 16,980-byte function | Split into per-state classes | Maintainability |
| 5 hardcoded linked lists | `EntityManager` with typed queues | Type safety |
| vtable dispatch (offset-based) | C# interfaces + virtual methods | Native OOP |
| Global `DAT_007c0024` for all state | `SimulationContext` (DI-injected) | Testability |
| Node pool with manual recycling | `ObjectPool<T>` (Unity) | Built-in pooling |
| Bitfield flags (`0x7C00 >> 10`) | Enum + Flags attribute | Readability |

### Implementation Phases

**Phase 1 — Skeleton** (P0):
- `SimulationManager.Tick()` with empty state cases
- `EntityManager` with 5 queues using `LinkedList<Entity>`
- `SimulationContext` replacing global state
- Timer decrement system

**Phase 2 — Movement** (P0):
- Cases 0-3 (block movement) + Case 9 (pathfinding)
- Waypoint following (3-state machine)
- Street access checks

**Phase 3 — Vehicles** (P1):
- Cases 4, 8, 10 (driving states)
- Speed accumulation, lane changes
- Vehicle entity lifecycle

**Phase 4 — Combat** (P2):
- Cases 0xB, 0xC (combat states)
- 4 combat function variants
- Projectile/effect spawning

**Phase 5 — Fleeing & AI** (P1):
- Case 0xD (fleeing with zigzag)
- AI state machine transitions
- Alert/suspicious/combat triggers

---

## 3. Pathfinding & Waypoint System

### What the Original Does

The pathfinding system uses pre-computed waypoint paths with a tick-based
countdown timer controlling traversal speed:

1. **Movement Setup** (`FUN_00564060`): Checks if destination is within 480
   pixels (5 blocks) → direct movement. Beyond that → multi-segment pathfinding.
2. **Waypoint Following** (`FUN_005844a0`): 3-state machine (init → wait →
   advance) with countdown timer decremented by global tick delta.
3. **Street Access** (`FUN_00609cf0`): Checks 4 cardinal directions with RNG
   starting direction. Validates road type, access flags, and traffic signals.
4. **Street Crossing** (`FUN_005dc8c0`): 4-directional checker with 6-cell
   maximum. Checks road direction flags and traffic light state.

### Why It's Reusable

The waypoint system is **renderer-agnostic** — it operates on abstract
coordinates (block + sub-block) and produces a path of waypoints. The traversal
timer creates natural speed variation. The street access/crossing logic
produces emergent behavior (NPCs waiting at traffic lights, choosing alternate
routes).

### Steel City Implementation

```csharp
// Coordinate system (preserved from original)
public struct BlockPosition
{
    public short BlockX;
    public short BlockY;
    public byte SubX;  // 0-95 pixels within block
    public byte SubY;
    
    public int PixelX => BlockX * 96 + SubX;
    public int PixelY => BlockY * 96 + SubY;
}

// Waypoint path
public class WaypointPath
{
    public List<BlockPosition> Waypoints;
    public int CurrentIndex;
    public float Timer;        // Countdown to next waypoint
    public float Speed;        // Ticks per waypoint (from global speed)
}

// Pathfinding states (3-state machine)
public enum PathfindingState
{
    Init,     // Copy first waypoint, check street access
    Waiting,  // Countdown timer, then advance
    Advancing // Move to next waypoint
}

// Street access checker
public class StreetAccessChecker
{
    // Checks 4 cardinal directions with randomized start
    // Returns first accessible direction
    public Direction CheckAccess(BlockPosition pos, RoadNetwork network)
    {
        var startDir = (Direction)(Random.Range(0, 4));
        for (int i = 0; i < 4; i++)
        {
            var dir = (Direction)(((int)startDir + i) % 4);
            if (IsAccessible(pos, dir, network))
                return dir;
        }
        return Direction.None;
    }
}

// Street crossing checker
public class StreetCrossingChecker
{
    public const int MAX_CROSSING_DISTANCE = 6;  // 6-cell limit from original
    
    public bool CanCross(BlockPosition pos, Direction dir, 
                         RoadNetwork network, TrafficLightSystem lights)
    {
        // Iterate up to 6 cells in crossing direction
        // Check passability, road flags, traffic signals
    }
}
```

### Key Modernization Decisions

| Original | Steel City | Rationale |
|----------|-----------|-----------|
| 6-byte position struct | `BlockPosition` struct | Same data, named fields |
| 480px direct movement threshold | Configurable `DirectMovementRange` | Tunable |
| 6-cell crossing maximum | Configurable `MaxCrossingDistance` | Wider roads |
| Global tick delta for speed | `Time.deltaTime` or sim-step delta | Frame independence |
| RNG for direction start | `Random.Range(0, 4)` | Unity RNG |
| Road network at fixed global offset | `RoadNetwork` data asset | Data-driven |
| Traffic lights at `DAT_007c0024 + 0x1220` | `TrafficLightSystem` component | ECS-friendly |

---

## 4. Vehicle System

### What the Original Does

The vehicle system is surprisingly robust with three decision paths:

1. **Distance-based** (`FUN_00462f30`): For standard orders (extort, patrol,
   collect). If distance > 64 units (~2/3 block) → drive; else walk.
2. **Random 25%** (`FUN_00660e60`): For Kill/Tail/unknown orders. `RNG & 3 == 0`
   → assign vehicle. Adds unpredictability to combat orders.
3. **Hijack fallback** (`FUN_005dc080`): Any hood with no vehicle state can
   spontaneously acquire one. Creates "steal a car" emergent moments.

The vehicle lifecycle uses a 3-bit state field (`0x38000000`):
- `0x00` = Walking (no vehicle)
- `0x08000000` = Vehicle assigned (car ready)
- `0x20000000` = Driving (self-acquired/stolen)
- `0x30000000` = Cleanup/destroyed

Driving costs 32 ticks vs 12,000 for walking (375× speedup). Driving also
**skips arrest checks** — making vehicles a safety mechanic, not just speed.

### Why the Vehicle System Is Impressive

The vehicle system is not just a "walk faster" flag. The binary reveals a
**full driving simulation** with speed dynamics, lane changes, and traffic
interaction — three separate SIM_TICK cases dedicated to vehicle behavior:

#### SIM_TICK Case 4: Basic Driving
- **Speed accumulation** — vehicles build speed over ticks, not instant
- **Lane change logic** — vehicles can change lanes during travel
- **Collision checks** — vehicles check for obstacles in path
- Calls `thunk_FUN_00664d50` (map cell lookup) to scan ahead

#### SIM_TICK Case 8: Vehicle Cruise
- **Speed-based position update** — position advances proportional to speed
- **Block transitions** — when sub-block position wraps, vehicle enters new
  block and is transferred to that block's entity list
- **Cruise maintenance** — maintains speed unless blocked

#### SIM_TICK Case 10: Advanced Driving (5-Substate)

| Substate | Name | Behavior |
|----------|------|----------|
| 0 | Init | Set up route, initialize speed |
| 1 | Accelerate | Build speed from standstill |
| 2 | Cruise | Maintain speed, scan ahead for obstacles |
| 3 | Lane change | Evaluate adjacent lane, shift if clear |
| 4 | Decelerate | Slow for destination, traffic, or obstacle |

This is a **proper driving AI** — not just a movement speed multiplier.
Vehicles accelerate, cruise, change lanes, and decelerate. The 5-substate
machine in Case 10 is the most sophisticated driving logic in the binary.

#### Vehicle Entities as Independent Agents

The debug display (`FUN_00719f90`) confirms vehicles have their own
**"Vehicle goal"** — a navigation target separate from the hood's destination.
This means:
- Vehicles are tracked as independent entities in the simulation
- They have their own AI state, position, and movement
- The hood "drives" by issuing commands to the vehicle entity
- The vehicle follows its own pathfinding to the goal

#### Vehicle Variety (from binary data)

| Entity Type | Subtype | Vehicle | Steel City Model |
|------------|---------|---------|-----------------|
| 8 | — | Tram | Trolley (fixed rail, stops at stations) |
| 9 | — | Train | Train (edge of map, scheduled) |
| 0xC | 8 | Truck | Delivery truck (slow, heavy) |
| 0xC | 0x10 | Crate Truck | Box truck (cargo variant) |
| 0xC | 0x11 | Tarpauline Truck | Covered truck |
| 0xC | 0x12 | Van Truck | Panel van |
| 0xD | 0 | Civilian Car | Standard sedan (common) |
| 0xD | 1 | Roadster | Fast car (gangster preferred) |
| 0xD | 2 | Police Car | Police cruiser (lights, siren) |

#### Traffic Interaction

Vehicles interact with the same road network and traffic light system as
NPCs on foot:
- Vehicles check road direction flags before proceeding
- Vehicles stop at red lights (traffic signal check in road network data)
- Vehicles are affected by road blockages and construction
- The 6-cell crossing limit constrains vehicle turning at intersections

### Steel City Implementation

#### Visual Models

Keep vehicle models **basic but characterful** — voxel-style with minimal
animation:
- **Wheels turn** (rotation based on speed)
- **Simple body** (no complex damage models at this stage)
- **Type-distinct silhouettes** (truck vs car vs trolley readable at a glance)
- **Color variants** for faction identification (gang colors, police blue/white)
- **Trolley** follows fixed rail path with station stops
- **Police car** has flashing lights when in pursuit mode

#### Vehicle State & Lifecycle

```csharp
public enum VehicleState
{
    Walking = 0x00,
    Assigned = 0x01,    // Vehicle ready, not yet entered
    Driving = 0x02,     // Actively driving (includes stolen)
    Cleanup = 0x03      // Vehicle returned or destroyed
}

public class VehicleDecisionSystem
{
    // Path 1: Distance-based (standard orders)
    public bool ShouldDriveDistance(Hood hood, Order order)
    {
        float distance = BlockPosition.Distance(hood.Position, order.Target);
        return distance > DriveThreshold;  // Original: 64 units
    }
    
    // Path 2: Random (combat orders)
    public bool ShouldDriveRandom(Hood hood, Order order)
    {
        if (order.Type is OrderType.Kill or OrderType.Tail)
            return Random.Range(0, 4) == 0;  // 25% chance
        return false;
    }
    
    // Path 3: Hijack fallback
    public bool TryHijackVehicle(Hood hood)
    {
        if (hood.VehicleState != VehicleState.Walking) return false;
        // Check game mode, create vehicle entity, set driving state
    }
}
```

#### Driving State Machine (from SIM_TICK Cases 4, 8, 10)

```csharp
public enum DrivingState
{
    Init,           // Case 10, substate 0: set up route
    Accelerating,   // Case 10, substate 1: build speed
    Cruising,       // Case 8 / Case 10, substate 2: maintain speed
    LaneChange,     // Case 10, substate 3: shift lanes
    Decelerating,   // Case 10, substate 4: slow for obstacle/destination
    BasicDrive      // Case 4: simple speed accumulation + collision
}

public class VehicleEntity : Entity
{
    public VehicleType Type;        // Civilian, Roadster, Police, Truck, Trolley
    public bool IsUsed;             // Original: offset 0x179
    public Hood Driver;             // Original: entity[0x30] pointer
    public BlockPosition NavigationTarget;  // "Vehicle goal" from debug
    public DrivingState DriveState;  // Current driving substate
    public float CurrentSpeed;       // Speed in blocks/tick
    public float TargetSpeed;        // Desired speed (cruise or max)
    public float Acceleration;       // Speed change per tick
    public byte CurrentLane;         // Lane index on current road
    public byte TargetLane;          // Desired lane (for lane changes)
    
    // Visual components
    public WheelAnimator Wheels;     // Wheel rotation based on speed
    public VehicleLights Lights;     // Headlights, police flashers
}
```

#### Vehicle Physics (Simplified)

```csharp
public class VehiclePhysicsSystem
{
    public void Tick(VehicleEntity vehicle, RoadNetwork network, 
                     TrafficLightSystem lights, float delta)
    {
        switch (vehicle.DriveState)
        {
            case DrivingState.Init:
                vehicle.CurrentSpeed = 0;
                vehicle.TargetSpeed = GetMaxSpeed(vehicle.Type);
                vehicle.DriveState = DrivingState.Accelerating;
                break;
                
            case DrivingState.Accelerating:
                vehicle.CurrentSpeed += vehicle.Acceleration * delta;
                if (vehicle.CurrentSpeed >= vehicle.TargetSpeed)
                {
                    vehicle.CurrentSpeed = vehicle.TargetSpeed;
                    vehicle.DriveState = DrivingState.Cruising;
                }
                break;
                
            case DrivingState.Cruising:
                // Scan ahead for obstacles, traffic lights, destination
                var obstacle = ScanAhead(vehicle, network, lights);
                if (obstacle == ObstacleType.TrafficLight)
                    vehicle.DriveState = DrivingState.Decelerating;
                else if (obstacle == ObstacleType.Vehicle)
                    TryLaneChange(vehicle, network);
                else if (obstacle == ObstacleType.Destination)
                    vehicle.DriveState = DrivingState.Decelerating;
                else
                    AdvancePosition(vehicle, vehicle.CurrentSpeed * delta);
                break;
                
            case DrivingState.LaneChange:
                if (vehicle.CurrentLane == vehicle.TargetLane)
                    vehicle.DriveState = DrivingState.Cruising;
                break;
                
            case DrivingState.Decelerating:
                vehicle.CurrentSpeed -= vehicle.Acceleration * delta;
                if (vehicle.CurrentSpeed <= 0)
                {
                    vehicle.CurrentSpeed = 0;
                    // Arrived or stopped — check what triggered decel
                }
                break;
        }
        
        // Update wheel animation
        vehicle.Wheels?.UpdateRotation(vehicle.CurrentSpeed, delta);
    }
}
```

### Key Modernization Decisions

| Original | Steel City | Rationale |
|----------|-----------|-----------|
| 375× speed ratio (12000 vs 32 ticks) | 10-20× ratio | Less extreme, still meaningful |
| Binary walk/drive (no partial) | Multi-leg journeys possible | Richer gameplay |
| Vehicle marked "used" (one per week) | Vehicle pool with cooldown | More strategic options |
| Arrest immunity while driving | Reduced arrest chance (not 100%) | More balanced |
| Distance threshold fixed at 64 units | Tunable per hood stat/difficulty | Skill matters |
| 25% random for combat orders | Keep but add hood intelligence factor | Smarter AI |
| Hijack has no consequences | Add police heat / wanted level | Risk/reward |
| 5-substate driving (Case 10) | `DrivingState` enum + `VehiclePhysicsSystem` | Visible driving AI |
| Speed is implicit (tick count) | Explicit `CurrentSpeed` / `TargetSpeed` | Chase dynamics |
| Lane changes are bitfield checks | `LaneChange` state with lane scan | Readable + extendable |
| No visual vehicle detail | Turning wheels, faction colors, police lights | Characterful minimalism |
| Trolley/train are entity types 8/9 | Fixed-path transit with station stops | Public transit layer |

---

## 4A. Dynamic Car Chases & Vehicle Combat Vision

### The Vision

The original Gangsters game has `COMBAT_3` (`thunk_FUN_004cc070`) — a vehicle
combat function with drive-by shooting mechanics. But it's limited to ordered
combat: a hood is assigned to attack, drives to the target, and fires.

**Steel City's vision goes further**: dynamic, emergent car chases and
car-to-car gunfights that erupt spontaneously when rival gangs encounter
each other on the road — just like the gangster movies that inspired the
original game.

### How It Emerges From Existing Code

The beauty is that this system **reuses the exact same architecture** the
binary already gives us — we're not inventing new systems, just connecting
existing ones in a new way:

```
Existing Systems (from binary):
  ├── Fear & Hostility metrics (entity + 0x60 threat state)
  ├── AI State Machine (FUN_005e0560 — combat/fleeing transitions)
  ├── Vehicle driving simulation (SIM_TICK cases 4, 8, 10)
  ├── COMBAT_3: Vehicle combat (drive-by shooting)
  ├── Vehicle entity as independent agent (own navigation target)
  └── Police system (entity type 0xD subtype 2)

New Connection (Steel City addition):
  └── Road Encounter Detector → triggers vehicle combat from proximity
```

### The Trigger System

When two rival gang vehicles come within **contact range** on the same road
segment, the game runs a **fear/hostility/intelligence check** to determine
whether violence erupts:

```csharp
public class RoadEncounterDetector
{
    // Called every tick for vehicles on the same road segment
    public void CheckEncounter(VehicleEntity vehicleA, VehicleEntity vehicleB,
                                SimulationContext ctx)
    {
        // Must be rival gangs
        if (vehicleA.Driver.Gang == vehicleB.Driver.Gang) return;
        
        // Must be within contact range (same or adjacent block)
        float distance = BlockPosition.Distance(
            vehicleA.Position, vehicleB.Position);
        if (distance > ContactRange) return;  // e.g., 2 blocks
        
        // Fear/Hostility/Intelligence check
        var driverA = vehicleA.Driver;
        var driverB = vehicleB.Driver;
        
        // Fear in a car = "do nothing" — afraid hoods don't initiate
        if (driverA.Fear > driverA.Hostility) return;
        if (driverB.Fear > driverB.Hostility) return;
        
        // Hostility must be high enough to trigger attack
        // Intelligence modifies: low-intelligence hoods are more reckless
        bool aAttacks = ShouldInitiateVehicleCombat(driverA, driverB, ctx);
        bool bAttacks = ShouldInitiateVehicleCombat(driverB, driverA, ctx);
        
        if (aAttacks)
            InitiateCarChase(vehicleA, vehicleB, ctx);
        else if (bAttacks)
            InitiateCarChase(vehicleB, vehicleA, ctx);
    }
    
    bool ShouldInitiateVehicleCombat(Hood attacker, Hood target, 
                                      SimulationContext ctx)
    {
        // Base: hostility must exceed fear
        if (attacker.Hostility <= attacker.Fear) return false;
        
        // Intelligence check: low intelligence = more reckless
        // High intelligence hoods calculate odds, may hold fire
        float recklessness = 1.0f - (attacker.Intelligence / 255f);
        
        // Gang tension modifier (ongoing feud escalates)
        float tension = ctx.GetGangTension(attacker.Gang, target.Gang);
        
        // Combined trigger score
        float triggerScore = attacker.Hostility * recklessness * tension;
        float threshold = 50f;  // Tunable
        
        return triggerScore > threshold && Random.Range(0f, 1f) < 0.3f;
    }
}
```

### The Car Chase State Machine

Once triggered, both vehicles enter a **chase state machine** that layers on
top of the existing driving simulation:

```
Car Chase Flow:

  ┌──────────────────┐     ┌──────────────────┐
  │ Aggressor Vehicle │     │ Target Vehicle   │
  │ (initiates fire)  │     │ (flees/returns)  │
  └────────┬─────────┘     └────────┬─────────┘
           │                        │
  ┌────────▼─────────┐     ┌────────▼─────────┐
  │ CHASE_PURSUIT    │     │ CHASE_FLEE       │
  │ - Accelerate     │     │ - Accelerate     │
  │ - Close distance  │     │ - Weave through  │
  │ - Fire at target  │     │   traffic        │
  │ - COMBAT_3 logic  │     │ - Run red lights │
  └────────┬─────────┘     │   (reckless)     │
           │               └────────┬─────────┘
           │                        │
  ┌────────▼────────────────────────▼─────────┐
  │ CHASE_ESCALATION                             │
  │ - Bystander casualties → police heat rises  │
  │ - Police spawn if heat > threshold           │
  │ - Police enter CHASE_INTERCEPT               │
  └────────┬────────────────────────────────────┘
           │
  ┌────────▼────────────────────────────────────┐
  │ CHASE_RESOLUTION                             │
  │ - Target escapes (distance > lose range)     │
  │ - Target vehicle disabled (damage > max)     │
  │ - Police intercept and arrest                │
  │ - Aggressor breaks off (fear > hostility)    │
  └──────────────────────────────────────────────┘
```

```csharp
public enum ChaseState
{
    None,               // No chase active
    ChasePursuit,       // Aggressor pursuing and firing
    ChaseFlee,          // Target fleeing at high speed
    ChaseEvade,         // Target weaving through traffic
    ChaseIntercept,     // Police joining the pursuit
    ChaseResolution     // Chase ending — escape, disable, or arrest
}

public class CarChaseSystem
{
    public void Tick(VehicleEntity aggressor, VehicleEntity target,
                     SimulationContext ctx, float delta)
    {
        switch (aggressor.ChaseState)
        {
            case ChaseState.ChasePursuit:
                // Override normal driving — pursue target
                aggressor.NavigationTarget = target.Position;
                aggressor.TargetSpeed = aggressor.MaxSpeed * 1.2f;
                
                // Fire when in range (COMBAT_3 logic)
                float distance = BlockPosition.Distance(
                    aggressor.Position, target.Position);
                if (distance < WeaponRange)
                {
                    // Use COMBAT_3 vehicle combat: spawn effect type 0x16
                    VehicleCombatHandler.Execute(
                        aggressor.Driver, target, ctx);
                    
                    // Bystander risk — stray bullets
                    CheckBystanderCasualties(aggressor, ctx);
                }
                break;
                
            case ChaseState.ChaseFlee:
                // Target drives recklessly — ignore traffic lights
                target.TargetSpeed = target.MaxSpeed * 1.3f;
                target.IgnoreTrafficLights = true;
                
                // Weave: random lane changes to evade
                if (Random.Range(0, 10) < 3)
                    target.TargetLane = (byte)Random.Range(0, 3);
                
                // Check if lost pursuer
                if (distance > LosePursuitRange)
                    ResolveChase(ChaseOutcome.Escape, ctx);
                break;
                
            case ChaseState.ChaseIntercept:
                // Police join — fastest police car targets aggressor
                // Aggressor now has two threats: target + police
                // If fear > hostility → break off and flee
                if (aggressor.Driver.Fear > aggressor.Driver.Hostility)
                    ResolveChase(ChaseOutcome.AggressorFlees, ctx);
                break;
        }
    }
}
```

### Emergent Gameplay Scenarios

This system produces **unscripted gangster movie moments**:

1. **The Drive-By**: Two rival cars pass on a street. High hostility + low
   intelligence → one opens fire. The other accelerates away. Bystanders
   scatter. Police radio lights up.

2. **The Chase Through Traffic**: Aggressor pursues target through city
   blocks. Target runs red lights, weaves between trucks. Aggressor follows,
   firing when lanes align. Civilians panic.

3. **The Police Join**: Bystander casualties raise heat. A police car spawns
   and enters intercept mode. Now it's a three-way: aggressor flees police
   while still chasing target, or breaks off entirely.

4. **The Trolley Block**: A trolley stops at a station, blocking one lane.
   Chase cars must swerve around it — creating a bottleneck moment where
   shots can land.

5. **The Truck Wreck**: A chase car clips a truck. Vehicle disabled. Occupants
   bail out on foot → combat transitions from vehicle (COMBAT_3) to ranged
   (COMBAT_1) or fleeing on foot (Case 0xD zigzag).

6. **The Cold Encounter**: Two rival cars pass. Hostility is moderate but
   fear is also high. Neither attacks. They pass peacefully. Tension without
   violence — the threat is implied.

### Why This Uses "Essentially Identical Code"

The car chase system doesn't require new engine architecture — it **reuses
existing systems in new combinations**:

| Existing System (from binary) | Car Chase Usage |
|-------------------------------|-----------------|
| Fear & Hostility (entity +0x60) | Trigger check for vehicle combat |
| AI State Machine (FUN_005e0560) | Transition to Combat/Fleeing states |
| Driving simulation (Cases 4/8/10) | Chase movement, acceleration, weaving |
| COMBAT_3 (thunk_FUN_004cc070) | Drive-by shooting mechanics |
| Vehicle as independent agent | Both cars navigate independently |
| Vehicle lifecycle (0x38000000) | Vehicle disabled → occupants exit on foot |
| Fleeing zigzag (Case 0xD) | On-foot escape after vehicle disabled |
| Police entity (type 0xD subtype 2) | Police intercept and pursuit |
| Time budget (thunk_FUN_00565c30) | Chase consumes time from weekly budget |
| Street crossing (FUN_005dc8c0) | Reckless cars ignore traffic light checks |
| Road network (DAT_007c0024+0x1220) | Chase path constrained by road layout |

The **only genuinely new code** is:
1. `RoadEncounterDetector` — proximity check between rival vehicles
2. `CarChaseSystem` — chase state machine layered on driving
3. Fear/hostility/intelligence trigger formula
4. Bystander casualty and police escalation logic

Everything else — the driving, the combat, the fleeing, the police, the road
network, the traffic lights — is already mapped from the binary.

### Visual Treatment

- **Turning wheels** accelerate during chase (visual speed cue)
- **Muzzle flash** from car windows during drive-by (COMBAT_3 effect 0x16)
- **Police lights** flash when in intercept mode
- **Civilians scatter** — NPCs on sidewalk enter fleeing state (Case 0xD)
- **Bullet holes / smoke** on damaged vehicles (simple decal/particle)
- **No complex physics** — vehicles don't roll or deform, just stop when
  disabled (health reaches zero → `VehicleState.Cleanup`)

---

## 5. NPC Collision & Traffic System

### What the Original Does

The street crossing and traffic light system is one of the most impressive
"hidden" features in the binary:

**Street Crossing** (`FUN_005dc8c0`):
- 4-directional checker (N/E/S/W)
- Iterates up to 6 cells in crossing direction
- Checks road passability at each cell
- Checks road direction flags (bitfield at offset 0x2C)
- Checks traffic signal state from road network data
- Returns blocked/accessible per direction

**Traffic Light System**:
- Road network stored as 4-byte array per road segment at `DAT_007c0024 + 0x1220`
- Each byte represents one direction (W/E/N/S) — non-zero = open, zero = blocked
- NPCs check traffic signals before crossing
- Creates emergent "waiting at traffic lights" behavior

**Street Access** (`FUN_00609cf0`):
- Randomized starting direction (RNG % 4)
- Checks all 4 cardinal directions
- Validates: road type, access flag, traffic signal
- Intersection detection (x%5 == 2 && y%5 == 2)
- Redirects to connected road cell at intersections

### Why It's Significant

This system creates **emergent urban behavior** without scripting:
- NPCs wait at red lights
- NPCs choose alternate routes when streets are blocked
- Traffic creates natural delays and timing variation
- The 6-cell crossing limit creates realistic road width constraints
- Intersection logic creates realistic traffic flow patterns

### Steel City Implementation

```csharp
// Road network data (replaces DAT_007c0024 + 0x1220)
[CreateAssetMenu]
public class RoadNetworkAsset : ScriptableObject
{
    // Per road segment: 4 directional flags
    public RoadSegment[] Segments;
}

public struct RoadSegment
{
    public bool WestOpen;
    public bool EastOpen;
    public bool NorthOpen;
    public bool SouthOpen;
    public byte RoadId;        // Index into segment array
    public RoadType Type;      // Street, avenue, highway, etc.
}

// Traffic light system
public class TrafficLightSystem : MonoBehaviour
{
    private Dictionary<int, TrafficLightState> _lights;
    
    public bool IsOpen(int roadId, Direction dir)
    {
        if (!_lights.TryGetValue(roadId, out var state))
            return true;  // No light = always open
        return state.IsOpen(dir);
    }
    
    public void Tick(float deltaTime)
    {
        // Cycle traffic lights on timer
        foreach (var light in _lights.Values)
            light.Update(deltaTime);
    }
}

public struct TrafficLightState
{
    public float Timer;
    public LightPhase Phase;  // NS-Green, EW-Green, All-Red
    
    public bool IsOpen(Direction dir) => Phase switch
    {
        LightPhase.NorthSouthGreen => dir is Direction.North or Direction.South,
        LightPhase.EastWestGreen => dir is Direction.East or Direction.West,
        LightPhase.AllRed => false,
        _ => true
    };
}

// NPC crossing decision
public class NPCCrossingSystem
{
    public bool CanCrossStreet(BlockPosition pos, Direction dir,
                               RoadNetwork network, TrafficLightSystem lights)
    {
        // 1. Get cell at position
        // 2. Check passability
        // 3. Iterate up to MaxCrossingDistance cells in direction
        // 4. At each cell: check road flags + traffic signal
        // 5. Return true if path is clear
    }
    
    public Direction FindAccessibleStreet(BlockPosition pos,
                                          RoadNetwork network,
                                          TrafficLightSystem lights)
    {
        // Randomized starting direction (like original)
        int startDir = Random.Range(0, 4);
        for (int i = 0; i < 4; i++)
        {
            var dir = (Direction)((startDir + i) % 4);
            if (CheckDirection(pos, dir, network, lights))
                return dir;
        }
        return Direction.None;  // All blocked — NPC waits
    }
}
```

### Key Modernization Decisions

| Original | Steel City | Rationale |
|----------|-----------|-----------|
| 4-byte directional flags per segment | `RoadSegment` struct with bools | Readability |
| Fixed traffic light array at global offset | `TrafficLightSystem` MonoBehaviour | Unity-native |
| 6-cell crossing maximum | Configurable per road type | Highways wider |
| Intersection detection (x%5==2, y%5==2) | Explicit intersection markers | Data-driven |
| No traffic light cycling logic found | Implement light phase cycling | Visible feature |
| NPCs always wait if blocked | Add jaywalking for reckless NPCs | Personality |

---

## 6. Combat System

### What the Original Does

Four combat variants sharing a 3-phase architecture (approach → attack →
complete):

| Variant | Type | Key Feature |
|---------|------|-------------|
| COMBAT_1 | Ranged | Projectile spawning, line-of-sight, randomized direction |
| COMBAT_2 | Melee | Effect type 0x18, close-range animation |
| COMBAT_3 | Vehicle | Drive-by, effect type 0x16, timer-based |
| COMBAT_4 | Arrest/Kidnap | 4-state machine, 500-tick cost, all-direction clearance check |

Only the local player's combat spawns visible projectiles — AI combat is
resolved statistically. This is a performance optimization.

**COMBAT_3 (Vehicle Combat)** is the foundation for the car chase system
(see Section 4A). It uses `thunk_FUN_004dd1b0` for vehicle target range
checking, spawns effect type `0x16` (drive-by muzzle flash), and runs on a
timer decremented by the global tick delta. In the original, this only fires
when a hood has an explicit attack order while in a vehicle. Steel City
extends this to **spontaneous vehicle combat** triggered by the road encounter
system.

### Steel City Implementation

```csharp
public abstract class CombatHandler
{
    public abstract CombatPhase Phase { get; }
    public abstract void Execute(Hood attacker, Entity target, SimulationContext ctx);
}

public class RangedCombatHandler : CombatHandler { /* COMBAT_1 */ }
public class MeleeCombatHandler : CombatHandler { /* COMBAT_2 */ }

// COMBAT_3 — extended for both ordered and spontaneous vehicle combat
public class VehicleCombatHandler : CombatHandler
{
    // Original: target range check via thunk_FUN_004dd1b0
    // Original: effect type 0x16 (drive-by flash)
    // Original: timer-based, decremented by tick delta
    
    public override void Execute(Hood attacker, Entity target, SimulationContext ctx)
    {
        var vehicle = attacker.Vehicle;
        if (vehicle == null) return;
        
        // Check target in range (original thunk_FUN_004dd1b0)
        float distance = BlockPosition.Distance(
            vehicle.Position, target.Position);
        if (distance > GetWeaponRange(attacker)) return;
        
        // Fire — spawn effect type 0x16
        ctx.EffectSystem.Spawn(
            vehicle.PixelX, vehicle.PixelY,
            EffectType.DriveByFlash, vehicle.Flags);
        
        // Damage roll (original: thunk_FUN_00481290 when flags & 0xA0)
        if ((attacker.Flags & 0xA0) != 0)
            ApplyDamage(attacker, target, ctx);
        
        // Bystander check (Steel City addition — see Section 4A)
        ctx.BystanderSystem.CheckStrayBullets(vehicle, ctx);
    }
}

public class ArrestCombatHandler : CombatHandler { /* COMBAT_4 */ }

// Combat engagement substates (8 from original)
public enum CombatSubstate
{
    Init, Approach, Attack, Cover, Flank, Retreat, VehicleEntry, Reset
}
```

### Key Modernization Decisions

| Original | Steel City | Rationale |
|----------|-----------|-----------|
| 4 hardcoded combat functions | Strategy pattern (`CombatHandler`) | Extensible |
| Only local player gets projectiles | All combat visualized (budget permitting) | Modern expectations |
| 3-phase state in bitfield | Enum-based phase tracking | Readability |
| Arrest costs 500 ticks (1 hour) | Configurable per crime severity | Tunable |
| Combat type determined by order byte | Explicit order→combat mapping | Clarity |
| COMBAT_3 only fires on ordered attack | Also fires from road encounter trigger | Emergent car chases |
| Vehicle combat is timer-only | Add damage model + vehicle health | Chase resolution |
| No bystander casualty logic | Add stray bullet + panic system | Consequences + police heat |

---

## 7. AI State Machine

### What the Original Does

`FUN_005e0560` manages state transitions at `entity + 0x11`:

- **Idle/Wander** (84%): Default state, RNG-based transitions
- **Alert** (1.5%): `RNG & 0x3F == 0` — rare alertness
- **Suspicious** (15%): `RNG & 0x3F < 10` — moderate suspicion
- **Combat**: Triggered by threat value at `entity + 0x60 == 3`
- **Fleeing**: 87.5% chance from combat state
- **Order Execution**: When `entity + 0x58 == 8`
- **Dying/Dead**: Terminal, locked states

### Steel City Implementation

```csharp
public enum AIState : byte
{
    Idle = 0x02,
    Alert = 0x04,
    Suspicious = 0x05,
    Combat = 0x06,
    Fleeing = 0x07,
    OrderExecution = 0x08,
    Dying = 0x13,
    Dead = 0x14
}

public class AIStateMachine
{
    public AIState UpdateState(Entity entity, SimulationContext ctx)
    {
        if (entity.AIState is AIState.Dead or AIState.Dying)
            return entity.AIState;  // Terminal
        
        // Order execution takes priority
        if (entity.HasActiveOrder)
            return EvaluateOrderState(entity, ctx);
        
        // Threat-based transitions
        if (entity.ThreatLevel == 3)
            return entity.Flags.HasFlag(EntityFlags.CombatReady) 
                ? AIState.Combat 
                : AIState.Idle;
        
        // RNG-based ambient states
        var rng = ctx.Random.Next(0, 64);
        if (rng == 0) return entity.Flags.HasFlag(EntityFlags.Suspicious) 
            ? AIState.Suspicious 
            : AIState.Alert;
        if (rng < 10) return AIState.Suspicious;
        return AIState.Idle;
    }
}
```

---

## 8. Time Budget System

### What the Original Does

- **12,000 ticks per hood per week** — the core strategic constraint
- **Command queue** (not immediate) — time commands enqueued, processed during ticks
- **Walking costs 12,000 ticks** (full budget, regardless of distance)
- **Driving costs 32 ticks** (375× speedup)
- **Street crossing costs 16 ticks** per crossing
- **Arrest costs 500 ticks** (1 hour)
- **Patrol costs 1 tick** per step
- **AI spontaneous orders cost 0-127 ticks** (randomized)

### Steel City Implementation

```csharp
public class TimeBudgetSystem
{
    public const int WeeklyBudget = 12000;
    public const int TicksPerHour = 500;
    
    // Command queue (preserved from original)
    private Queue<TimeCommand> _commandQueue;
    
    public void AllocateTime(Hood hood, int amount, Order order)
    {
        _commandQueue.Enqueue(new TimeCommand
        {
            Type = CommandType.Allocate,
            Amount = amount,
            Order = order
        });
    }
    
    public void ConsumeTime(Hood hood, int amount)
    {
        _commandQueue.Enqueue(new TimeCommand
        {
            Type = CommandType.Consume,
            Amount = amount
        });
    }
    
    public void ProcessQueue(SimulationContext ctx)
    {
        while (_commandQueue.Count > 0)
        {
            var cmd = _commandQueue.Dequeue();
            // Apply to hood's time budget
        }
    }
}
```

### Key Modernization Decisions

| Original | Steel City | Rationale |
|----------|-----------|-----------|
| Flat 12000 for walking | Distance-proportional (with minimum) | Fairer |
| 32 for driving | Distance-proportional × vehicle speed | More nuanced |
| Command queue (deferred) | Keep command queue pattern | Preserves architecture |
| NotEnough errors after the fact | Preview costs before order commit | Better UX |
| 500 ticks for arrest | Configurable per crime type | Tunable difficulty |

---

## 9. Implementation Priority & Momentum

### Recommended Build Order

The following sequence builds momentum by creating a **visible, working
simulation** as early as possible:

```
Phase 1: Core Skeleton (Week 1-2)
├── SimulationManager.Tick() framework
├── EntityManager with 5 queues
├── SimulationContext (replaces global state)
├── BlockPosition coordinate system
├── TimeBudgetSystem (command queue)
└── Order queue (pop/peek/push)
    → RESULT: Entities exist, tick processes, time budgets track

Phase 2: Movement & Pathfinding (Week 3-4)
├── Cases 0-3 (block movement)
├── Case 9 (pathfinding 3-substate)
├── WaypointPath + WaypointFollower
├── StreetAccessChecker (4-directional)
├── RoadNetwork data asset
└── Basic NPC wandering
    → RESULT: NPCs walk around city blocks, follow paths

Phase 3: Traffic & Collision (Week 5-6)
├── StreetCrossingChecker (6-cell, 4-directional)
├── TrafficLightSystem (phase cycling)
├── NPC crossing decisions (wait at red lights)
├── Intersection handling
└── Block transition (linked list transfer)
    → RESULT: NPCs wait at traffic lights, cross streets realistically

Phase 4: Vehicles (Week 7-8)
├── VehicleEntity class
├── VehicleDecisionSystem (3 paths: distance, random, hijack)
├── Cases 4, 8, 10 (driving states)
├── Vehicle lifecycle (assigned → driving → cleanup)
├── Speed calculation (configurable ratio)
└── Vehicle-as-strategic-resource UI
    → RESULT: Hoods drive to distant targets, walk to nearby ones

Phase 5: AI & Combat (Week 9-12)
├── AIStateMachine (8 states with RNG transitions)
├── Cases 0xB, 0xC (combat approach + engagement)
├── 4 CombatHandler variants (ranged, melee, vehicle, arrest)
├── Case 0xD (fleeing with zigzag)
├── Projectile/effect spawning
└── Arrest/kidnap message system
    → RESULT: Full combat, arrests, fleeing, emergent AI behavior
```

### Why This Order

1. **Skeleton first** — the simulation loop must exist before anything can live
   inside it. This is the `SIM_TICK` equivalent.
2. **Movement before vehicles** — walking is the default; vehicles are the
   upgrade. You need pathfinding working before driving matters.
3. **Traffic before vehicles** — NPCs need to cross streets before vehicles
   create traffic. The crossing system is what makes the city feel alive.
4. **Vehicles before combat** — vehicles are a strategic layer that makes
   combat more interesting (drive-bys, escape vehicles, arrest immunity).
5. **Combat last** — it's the most complex system and depends on all prior
   systems (movement, vehicles, AI states, time budget).

### Momentum Milestones

| Milestone | What's Visible | Demo Value |
|-----------|---------------|------------|
| End of Phase 1 | Entities tick, time budgets count down | "The simulation runs" |
| End of Phase 2 | NPCs walk the city following paths | "The city is alive" |
| End of Phase 3 | NPCs wait at traffic lights, cross streets | " emergent urban behavior" |
| End of Phase 4 | Hoods drive to distant targets, walk nearby | "Strategic vehicle decisions" |
| End of Phase 5 | Combat, arrests, fleeing, full AI | "Complete game loop" |

---

## 10. Entity Component Mapping

The original's flat struct (offsets 0x00-0x60+) maps to Unity components:

| Original Offset | Steel City Component | Field |
|----------------|---------------------|-------|
| `+0x00` | `Entity` (base) | vtable → C# virtual methods |
| `+0x04, +0x06` | `TransformComponent` | BlockX, BlockY |
| `+0x09` | `TransformComponent` | SubX (pixel offset) |
| `+0x11` | `AIComponent` | State (AIState enum) |
| `+0x15` | `MovementComponent` | CurrentNode, Path |
| `+0x19` | `EntityComponent` | Flags (Flags enum) |
| `+0x1D` | `EntityComponent` | OwnerId (player) |
| `+0x21` | `EntityComponent` | EntityType (low 5 bits) |
| `+0x30` | `VehicleComponent` | Vehicle reference |
| `+0x45-0x48` | `OrderComponent` | OrderList, Count |
| `+0x54` | `CombatComponent` | CombatFlags |
| `+0x58` | `MovementComponent` | Mode (8 = order) |
| `+0x59` | `AnimationComponent` | CurrentAnimId |
| `+0x60` | `AIComponent` | ThreatLevel |

### Global State Mapping

| Original Global | Steel City System | Notes |
|----------------|-------------------|-------|
| `DAT_007c0024 + 0x24` | `EntityManager.ActivePool` | Entity active list |
| `DAT_007c0024 + 0xE4` | `EntityManager.UpdateQueue` | Per-tick update list |
| `DAT_007c0024 + 0x104` | `EntityManager.MovementQueue` | Movement processing |
| `DAT_007c0024 + 0x124` | `EntityManager.CombatQueue` | Combat/action list |
| `DAT_007c0024 + 0x144` | `EntityManager.SecondaryQueue` | Secondary actions |
| `DAT_007c0024 + 0x210` | `SimulationContext.LocalPlayerId` | Current player |
| `DAT_007c0024 + 0x858` | `AnimationTable` (ScriptableObject) | Animation data |
| `DAT_007c0024 + 0x1220` | `RoadNetworkAsset` | Road direction data |
| `DAT_007c0024 + 0x16D8` | `SimulationContext.TickDelta` | Global speed |
| `DAT_007c0024 + 0x1B18` | `SimulationContext.DefaultSpeed` | Default waypoint speed |

---

## 11. Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    SimulationManager                         │
│  ┌──────────┐  ┌──────────────┐  ┌────────────────────┐    │
│  │ Tick()   │→ │ EntityManager│  │ SimulationContext  │    │
│  │          │  │  5 queues    │  │  (DI container)    │    │
│  └────┬─────┘  └──────────────┘  └────────────────────┘    │
│       │                                                      │
│  ┌────▼────────────────────────────────────────────────┐    │
│  │              Per-Entity Processing                    │    │
│  │  ┌─────────┐ ┌──────────┐ ┌─────────┐ ┌──────────┐  │    │
│  │  │ Timers  │ │ Animation│ │ AI State│ │ Movement │  │    │
│  │  │ Update  │ │ Update   │ │ Switch  │ │ Update   │  │    │
│  │  └─────────┘ └──────────┘ └────┬────┘ └─────┬────┘  │    │
│  └────────────────────────────────┼─────────────┼──────┘    │
│                                   │             │            │
│  ┌────────────────────────────────▼─────────────▼──────┐    │
│  │              State Handlers                          │    │
│  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐       │    │
│  │  │Movement│ │Driving │ │Combat  │ │Fleeing │       │    │
│  │  │Handler │ │Handler │ │Handler │ │Handler │       │    │
│  │  └───┬────┘ └───┬────┘ └───┬────┘ └────────┘       │    │
│  └──────┼──────────┼──────────┼────────────────────────┘    │
│         │          │          │                               │
│  ┌──────▼──────────▼──────────▼────────────────────────┐    │
│  │              Sub-Systems                              │    │
│  │  ┌────────────┐ ┌─────────────┐ ┌────────────────┐  │    │
│  │  │Pathfinding │ │Vehicle      │ │Combat          │  │    │
│  │  │Waypoints   │ │Decision     │ │(4 variants)    │  │    │
│  │  │StreetAccess│ │Lifecycle    │ │Projectiles     │  │    │
│  │  │Crossing    │ │Speed Calc   │ │Arrest/Kidnap   │  │    │
│  │  └────────────┘ └─────────────┘ └────────────────┘  │    │
│  │  ┌────────────┐ ┌─────────────┐ ┌────────────────┐  │    │
│  │  │RoadNetwork │ │TrafficLight │ │TimeBudget      │  │    │
│  │  │(data asset)│ │System       │ │(command queue) │  │    │
│  │  └────────────┘ └─────────────┘ └────────────────┘  │    │
│  └──────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

---

## 12. Key Design Principles from the Binary

These principles emerged from the reverse engineering and should guide Steel
City's architecture:

1. **Single entry point for simulation** — SIM_TICK processes everything. No
   scattered update logic. Steel City: `SimulationManager.Tick()` is the only
   entry point.

2. **Command queue for time** — Time allocation/consumption is deferred, not
   immediate. This allows the simulation to process time commands in order and
   handle "not enough time" gracefully. Steel City: Keep the command queue.

3. **Pre-computed waypoint paths** — Paths are computed once, then traversed
   with a countdown timer. This separates pathfinding (expensive, rare) from
   path following (cheap, every tick). Steel City: Same separation.

4. **Per-block entity tracking** — Entities are tracked in per-block linked
   lists, transferred on block transition. This enables efficient spatial
   queries without a spatial hash. Steel City: Consider for large entity counts.

5. **Node recycling** — Linked list nodes are pooled and recycled (keep 2,
   free excess above 5). Steel City: Use Unity's `ObjectPool<T>`.

6. **RNG for emergent variety** — The original uses RNG everywhere: direction
   selection, state transitions, combat variation, vehicle assignment. This
   creates unpredictable but bounded behavior. Steel City: Keep RNG-driven
   variety but make it seedable for reproducibility.

7. **Local player optimization** — Only the local player gets visual effects
   (projectiles, animations). AI is resolved statistically. Steel City: Use
   this for performance budgeting at scale.

8. **Binary flags for compact state** — The original packs enormous state into
   bitfields. Steel City: Use enums and Flags for readability, but keep the
   pattern of compact state representation for network serialization.

---

## References

- **Full analysis**: `docs/core/REVERSE_ENGINEERING_FINDINGS.md` (Sections 1-18)
- **Source file**: `ghidra_pathfinding_combat.txt` (6,843 lines)
- **Design philosophy**: `docs/core/DESIGN_PHILOSOPHY.md`
- **Systems overview**: `docs/systems/SYSTEMS_OVERVIEW.md`
- **Combat design**: `docs/systems/COMBAT_AUTOBATTLE.md`
- **3D rendering**: `docs/systems/3D_CITY_RENDERING.md`
- **Vertical slice**: `docs/VERTICAL_SLICE_DESIGN.md`
