# Combat & Vehicle Design — Street Combat, Vehicle Chase, Physics, NPC Animation

**Created**: Aug 9, 2026
**Status**: 📐 DESIGN DRAFT — brainstorming captured, not yet implemented
**Relates to**: `docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`, `docs/systems/GPU_DRIVEN_RENDERING_PLAN.md`, `docs/systems/INVARIANT_COMPUTATION_PRINCIPLE.md`, `Assets/Scripts/UI/VoxelChunkManager.cs`, `Assets/Scripts/Sim/VoxelVehicle.cs`

---

## Contents

1. [Design Goals](#1-design-goals)
2. [Street Combat System](#2-street-combat-system)
3. [Cover System](#3-cover-system)
4. [Combat AI States](#4-combat-ai-states)
5. [NPC Animation Without Skeletons](#5-npc-animation-without-skeletons)
6. [Vehicle Physics](#6-vehicle-physics)
7. [Vehicle Combat](#7-vehicle-combat)
8. [Traffic & Swarm Behavior](#8-traffic--swarm-behavior)
9. [Police Escalation System](#9-police-escalation-system)
10. [Rendering Impact](#10-rendering-impact)
11. [Impact on Existing Architecture](#11-impact-on-existing-architecture)
12. [Implementation Priority & Phasing](#12-implementation-priority--phasing)
13. [Physical Projectiles & Spatial Hash](#13-physical-projectiles--spatial-hash)

---

## 1. Design Goals

### Core Vision

Steel City combat should feel like a living urban battlefield — not a shooting gallery. Hoods use cover, vehicles chase and crash, police escalate, civilians panic. Emergent scenarios arise from AI reactions to noise, threat, and environment.

### Design Principles

- **Emergent over scripted**: Combat scenarios develop from AI state machines reacting to events, not pre-planned set pieces
- **Rendering-agnostic where possible**: Most combat logic is gameplay/AI, not rendering changes
- **Tier-aware**: Every new entity type gets classified into the existing Tier 1/2/3 rendering system before implementation
- **Physics only when needed**: Not all vehicles need physics. Match physics complexity to gameplay role
- **Procedural animation first**: NPC animation via shader math and voxel swaps, no skeletal rigs

### What This Document Is

A design brainstorm capturing feasibility analysis, architectural impact, and implementation approaches for:
- Street-level combat between hoods (on foot)
- Vehicle combat (drive-by, chase, crash)
- Police escalation and response
- NPC animation without skeletons
- Vehicle physics implementation options
- Traffic swarm and avoidance behavior

### What This Document Is Not

- A step-by-step implementation plan (that comes after design approval)
- A commitment to all features (phasing section identifies priorities)
- A rendering architecture doc (references existing docs for that)

---

## 2. Street Combat System

### Overview

Two hoods from rival factions encounter each other on foot in the city. Instead of standing and firing blindly, they:
1. Detect the threat (enemy within engagement range)
2. Scan for nearest cover
3. Navigate to cover (using existing Pathfinder + WaypointGraph)
4. Engage from cover, peeking out to fire
5. React to being hit (flinch, retreat, or go down)

### Rendering Impact: Low

Street combat is primarily an **AI and gameplay** problem, not a rendering problem:
- Hoods are already Tier 2 instanced characters — no new rendering path needed
- Cover is derived from existing collision world geometry — no new rendering objects
- Firing is a gameplay effect (hitscan + visual tracer), not a rendering change
- NPC animation (flinch, fall) is procedural shader work — see §5

### Key Subsystems

| Subsystem | Type | New Code? | Rendering Change? |
|---|---|---|---|
| Cover point scanner | Gameplay query on collision world | Yes | No |
| Combat AI state machine | AI logic | Yes | No |
| Firing mechanics | Hitscan + tracer VFX | Yes | Minor (tracer rendering) |
| NPC animation | Shader procedural | Yes | Shader modification |
| Hit/death state | Gameplay state flag | Yes | No (animation handles visual) |

---

## 3. Cover System

### Two Approaches

#### Approach A: Cover Points (No Rendering Change)

Cover is a **gameplay query** on the existing collision world. Buildings are already in `VoxelCollisionWorld` as a flat `byte[]` grid. Cover points are positions adjacent to solid voxel walls where a hood can crouch for protection.

**How it works**:
1. At combat start (or at load time — cover points are invariant), scan the collision world around the engagement area
2. For each solid voxel column, check if there's an adjacent empty voxel at character height
3. Mark that position as a cover point with a direction (which way the wall faces)
4. Hoods query nearest cover points and path to them

**Cover point data structure**:
```
struct CoverPoint {
    Vector3 position;       // world position to stand at
    Vector3 coverDirection;  // direction the wall faces (hood faces away from this)
    float quality;           // 0-1, how much cover (full wall = 1.0, partial = 0.5)
}
```

**Invariant computation note**: Cover points are derived from building geometry which doesn't change during gameplay. This is a "do once" computation — cache cover points at load time or at first combat event per sector. See `INVARIANT_COMPUTATION_PRINCIPLE.md`.

**Pros**: Zero rendering impact, uses existing collision world, fast to implement
**Cons**: No physical cover objects in the world — cover is implicit (walls, corners)

**Recommended for**: First implementation. Covers 90% of urban combat scenarios.

#### Approach B: Dynamic Cover Objects (Rendering Change)

Physical cover props — parked cars, barrels, crates, sandbags — that exist in the world and can be used as cover.

**Tier classification**:
- **Tier 2 (instanced)** when idle — many similar objects, position is the main per-instance data
- **Tier 3 (individual)** when destroyed — unique damage state, brief individual rendering
- Destruction = remove from instance batch, spawn debris particle effect, done

**Instance buffer per cover prop**:
```
Vector4(position.x, position.y, position.z, healthOrState)
```

**Pros**: Visual richness, destructible cover adds tactical depth
**Cons**: New asset pipeline (cover prop voxel models), more complex than Approach A

**Recommended for**: Phase 2 — after Approach A is working and the game needs more visual variety.

### Cover Quality Assessment

When a hood evaluates cover, the quality of the cover matters:

| Cover Type | Quality | Example |
|---|---|---|
| Full wall | 1.0 | Behind a building wall |
| Half wall | 0.5 | Behind a low fence or car |
| Corner | 0.8 | Building corner — partial exposure on one side |
| Vehicle | 0.6 | Behind a parked car (can be destroyed) |

Hoods prefer higher-quality cover. If all high-quality cover is occupied by allies, they take what's available.

---

## 4. Combat AI States

### Hood State Machine

```
                    ┌─────────┐
                    │  IDLE   │ ← default state (walking, loitering)
                    └────┬────┘
                         │ enemy detected
                         ▼
                    ┌─────────┐
                    │ ALERT   │ ← scan for cover, evaluate threat
                    └────┬────┘
                         │ cover found
                         ▼
              ┌──────────────┐
              │ MOVING_TO_COVER│ ← pathfind to cover point
              └──────┬───────┘
                     │ reached cover
                     ▼
              ┌──────────────┐
              │ IN_COVER     │ ← peek + fire, reload, assess
              └──────┬───────┘
                     │ cover destroyed / flanked
                     ▼
              ┌──────────────┐
              │ RELOCATING   │ ← find new cover, move
              └──────┬───────┘
                     │
                     ├──── hit → FLINCH → IN_COVER (if alive)
                     ├──── health = 0 → FALLING → DOWN (dead)
                     └──── no enemies → IDLE
```

### State Data Per Hood

Current instance buffer: `Vector4(x, y, z, yaw)`

Expanded for combat:
```
struct HoodInstanceData {
    Vector3 position;      // 12 bytes
    float yaw;             // 4 bytes
    int animState;         // 4 bytes — enum (IDLE, WALKING, ALERT, IN_COVER, FIRING, FLINCH, FALLING, DOWN)
    float animTime;        // 4 bytes — time since state change (for procedural animation)
    int faction;           // 4 bytes — for faction-based targeting
    float health;          // 4 bytes — 0-1, for hit reactions
    // Total: 32 bytes per instance (was 16)
}
```

This is a **backward-compatible change** — non-combat characters set `animState = IDLE` and the extra fields are unused.

### Firing Mechanics

**Two approaches** — hitscan (simple) and physical projectiles (emergent). See §13 for full design.

**Hitscan (baseline)**:
1. Hood fires — raycast from hood position toward target
2. Check collision world for wall hits (did the shot hit cover?)
3. If no wall hit, check if target is within firing arc
4. Apply damage based on accuracy roll (modified by distance, cover quality, movement)

**Physical projectiles (emergent — recommended for mob sim)**:
1. Hood fires — spawn lightweight projectile entity with position, velocity, TTL
2. Each frame, projectile travels along velocity vector
3. Check spatial hash for instance hits (civilians, hoods, cops — anyone in the path)
4. Check collision world for wall/building hits
5. On hit: resolve damage via CrimeSystem, trigger hit reaction animation
6. Stray bullets can hit civilians → police escalation, witness generation

**Visual effects** (same for both):
- Tracer: brief line renderer or instanced beam from muzzle to impact point
- Muzzle flash: small particle burst at shooter position
- Impact: particle burst at hit location

These are **particle/line effects**, not voxel-rendered objects. Minimal rendering impact.

### Firing Arcs

The user has detailed design documents on firing arcs for hoods. Key considerations:
- **Standing fire**: 360° arc, lower accuracy
- **Cover fire**: limited arc (only exposed side), higher accuracy
- **Vehicle fire**: firing arc limited by window opening, can "pop out" for wider arc at risk

---

## 5. NPC Animation Without Skeletons

### The Problem

Our voxel characters are raymarched voxel volumes, not polygonal meshes with skeletal rigs. Traditional Unity animation (Animator,骨骼) doesn't apply. We need animation that works with the voxel raymarching shader.

### Four Techniques

#### Technique 1: Voxel Swap (Stop-Motion)

Author multiple voxel models for key poses. Each instance gets a `poseIndex`. The shader samples from a different region of a larger voxel atlas buffer.

```
Voxel Atlas Buffer:
┌──────────────┬──────────────┬──────────────┐
│  Pose 0      │  Pose 1      │  Pose 2      │
│  (standing)  │  (walking)   │  (falling)   │
└──────────────┴──────────────┴──────────────┘
```

- **Memory**: 10 poses × 5000 voxels = 50K voxels per character type
- **Effort**: Low — artist authors poses, shader adds atlas offset
- **Best for**: Death/fall (2-3 poses), cover crouch (2 poses)
- **Limitation**: Not smooth between poses — stop-motion feel

#### Technique 2: Procedural Shader Animation (Recommended First)

Math-driven offsets applied in the shader. No pre-authored poses. The shader modifies voxel positions based on time + state.

```hlsl
// Walking bob
if (animState == WALKING) {
    voxelOffset.y = sin(time * 8.0 + instanceID) * 0.05;
    voxelOffset.x = sin(time * 4.0 + instanceID) * 0.02;
}

// Falling
if (animState == FALLING) {
    float fallProgress = saturate((time - fallStartTime) / 0.5);
    rotationAngle = fallProgress * PI * 0.5;  // rotate 90° over 0.5s
}

// Firing recoil
if (animState == FIRING) {
    recoilOffset = -forward * pulse(time - fireTime, 0.1) * 0.03;
}

// Hit flinch
if (animState == FLINCH) {
    flinchOffset = -forward * exp(-(time - hitTime) * 10) * 0.05;
}
```

- **Memory**: Zero extra
- **Effort**: 1-3 hours per animation type
- **Best for**: Walking bob, recoil, flinch, falling rotation, idle breathing
- **Limitation**: Only simple transforms (offset, rotation, scale). No limb articulation.

#### Technique 3: Voxel Group Transforms (Best Quality)

Define named groups of voxels within a character model. Each voxel stores a `groupID`. The shader applies per-group transforms.

```
Character Voxel Model:
├── Group "body"     (voxels 0-2000)     → no transform
├── Group "head"     (voxels 2001-2200)  → yaw + pitch offset
├── Group "leftArm"  (voxels 2201-2600)  → swing rotation
├── Group "rightArm" (voxels 2601-3000)  → swing + aim raise
└── Group "legs"     (voxels 3001-5000)  → walk cycle rotation
```

- **Memory**: +1 byte per voxel (groupID), +per-group transform in instance buffer
- **Effort**: 1-2 days — shader work + voxel model authoring with group tags
- **Best for**: Combat animations — aiming arms, crouching legs, head tracking
- **Limitation**: Requires re-authoring voxel models with group tags

#### Technique 4: Morph Target Blending

Author 2-4 extreme poses. Shader blends between them based on a weight parameter.

```hlsl
finalPosition = lerp(poseA[i].position, poseB[i].position, blendWeight);
```

- **Memory**: poses × voxel count (must keep topology identical)
- **Effort**: Medium — artist + shader work
- **Best for**: Cover crouch (standing ↔ crouching blend)
- **Limitation**: Topology must match between poses

### Recommended Implementation

| Animation Need | Technique | Effort | Phase |
|---|---|---|---|
| Walking bob | Procedural | 1 hr | Phase 1 |
| Idle breathing | Procedural | 30 min | Phase 1 |
| Firing recoil | Procedural | 1 hr | Phase 2 (combat) |
| Hit flinch | Procedural | 30 min | Phase 2 (combat) |
| Falling/knocked down | Voxel swap (2 poses) | 2 hrs | Phase 2 (combat) |
| Cover crouch | Morph or voxel swap | 3 hrs | Phase 3 (cover) |
| Aiming arms | Voxel groups | 1-2 days | Phase 4 (polish) |
| Head tracking | Procedural (yaw offset) | 30 min | Phase 4 (polish) |

### Design Reference: Gangsters-Inspired Behavior

**Source**: The original *Gangsters* game (1998) had all pedestrians walking around and randomly stopping to look around by turning their heads. This made everyone look suspicious — you couldn't tell if someone was a civilian or a hood checking if the coast was clear. This blended perfectly into the criminal gameplay.

**Three animation states that sell the whole vibe**:

1. **Walking with limb swing** — ambient NPCs stroll through the city with natural arm/leg counter-swing. Not robotic, not RimWorld-style whole-body bob. Limbs move independently. Looks alive.
2. **Random head look-around** — pedestrians stop, turn head left, pause, turn right, then resume walking. Just like Gangsters. Makes everyone look slightly suspicious even when innocent.
3. **Hood "coast clear" check** — same head-turn animation but triggered by crime AI: hood pauses at a corner, looks both ways (head tracks left → right), then either proceeds with the crime or backs off if a cop is visible.

**The "suspicious blend" is emergent** — innocent pedestrians and criminal hoods use the *same* head-turn animation, so you can't tell them apart until the hood actually does something. That's exactly the Gangsters feel.

**Animation state mapping** (all use the same voxel group system — same shader path, same instance buffer, different `animState` values):

```
WALKING     → legs swing, arms counter-swing, slight body bob
LOOKING     → body stops, head rotates (procedural yaw offset on head group)
CHECKING    → same as LOOKING but triggered by crime AI, with longer pause
COMMITTING  → transitions to walking (faster pace) or combat stance
```

### Step-by-Step: Making Stiff Vinny Walk

**Yes, this is procedural animation** — but not the RimWorld whole-body-only kind. The limb swing is *procedural* (sin-wave driven, computed per-frame in the shader from `animTime`), but it's applied *per voxel group* (arms, legs, head, torso independently). No pre-baked animation clips, no keyframes. The shader math generates the motion.

**Current state**: Vinny is a single rigid voxel volume. Instance buffer = `float4(x, y, z, yaw)` = 16 bytes. The shader raymarches the entire volume with one yaw rotation. No limb separation, no per-group transforms. He slides across the ground like a statue on roller skates.

**Target state**: Vinny walks with swinging arms and legs, can stop and turn his head to look around.

#### Step 1: Tag Vinny's Voxels with Group IDs (Asset side)

Author (or modify) the Vinny `.stasset` so each voxel stores a `groupID` byte alongside its material index. Groups:

| Group ID | Name | Voxels | Transform |
|---|---|---|---|
| 0 | Body/Torso | torso + pelvis voxels | identity (no per-group transform) |
| 1 | Head | head + neck voxels | yaw + pitch offset (head tracking) |
| 2 | Left Arm | left shoulder → hand | swing rotation (walk) / raise (aim) |
| 3 | Right Arm | right shoulder → hand | swing rotation (walk) / raise (aim) |
| 4 | Left Leg | left hip → foot | stride rotation (walk) |
| 5 | Right Leg | right hip → foot | stride rotation (walk) |

**How**: In the voxel authoring tool (MagicaVoxel → export), tag regions with a group number. Store as a parallel `byte[]` array alongside the existing `uint[]` voxel data, OR pack the groupID into the high bits of the existing uint (currently only low bits used for material index).

**Effort**: 2-4 hours (depends on how Vinny was authored — if regions are already separate objects in MagicaVoxel, just export with group metadata)

#### Step 2: Expand the Instance Buffer (CPU side)

In `VoxelChunkManager.RenderInstancedGroup()`, expand the per-instance data from `float4` to include animation state:

```csharp
// Current: Vector4(x, y, z, yaw) = 16 bytes
// New:     Vector4(x, y, z, yaw) + Vector4(animState, animTime, 0, 0) = 32 bytes
```

Add `animState` (int as float) and `animTime` (float, seconds since animation started) to `InstancedCharacter`. Update the offsets array and buffer stride.

**Effort**: 1-2 hours

#### Step 3: Upload Group ID Buffer (CPU side)

Add a second `ComputeBuffer` per `InstancedGroup` that stores the `groupID` per voxel (same size as the voxel buffer, 1 byte per voxel → use `sizeof(uint)` for alignment). Bind it as `_GroupIDs` in the MaterialPropertyBlock.

**Effort**: 1 hour

#### Step 4: Add Group Transforms to the Shader (GPU side)

In `VoxelProxyRaymarch.shader`, after the ray transforms into volume-local space (line ~307), add per-group transform logic:

```hlsl
// New bindings
StructuredBuffer<uint> _GroupIDs;  // groupID per voxel
// Per-instance anim data (already in _InstanceOffsets, now 2x float4)
// Second float4: (animState, animTime, unused, unused)

// In the DDA loop, when a voxel is hit:
uint groupID = _GroupIDs[bufferOffset + VoxelIndex(voxel, dims)];

// Compute per-group transform based on animState and animTime
float3x3 groupRot = identity;
if (groupID == 1) { // Head
    float headYaw = sin(animTime * 2.0) * 0.5; // look-around
    groupRot = rotationY(headYaw);
} else if (groupID == 2) { // Left arm
    float swing = sin(animTime * 6.0) * 0.3; // walk swing
    groupRot = rotationX(swing);
} else if (groupID == 3) { // Right arm
    float swing = sin(animTime * 6.0 + PI) * 0.3; // counter-swing
    groupRot = rotationX(swing);
} else if (groupID == 4) { // Left leg
    float stride = sin(animTime * 6.0 + PI) * 0.4;
    groupRot = rotationX(stride);
} else if (groupID == 5) { // Right leg
    float stride = sin(animTime * 6.0) * 0.4;
    groupRot = rotationX(stride);
}

// Apply group transform to the voxel's local position
// (pivot point = group origin, e.g. shoulder for arms, hip for legs)
```

**Key insight**: The group transform is applied *inside the DDA loop* when a voxel is hit. Each voxel's position is offset by its group's rotation around the group's pivot point. This means the raymarch still works — it just hits voxels at slightly different positions because the group rotation moves them.

**Effort**: 4-8 hours (shader math + pivot point calibration)

#### Step 5: Drive Animation State from C# (CPU side)

In `VoxelCharacter.cs` (or a new `CharacterAnimation` component), update `animTime` each frame and set `animState` based on behavior:

```csharp
public enum AnimState { Idle = 0, Walking = 1, Looking = 2, Checking = 3 }

void Update() {
    instancedHandle.animState = (float)currentState;
    instancedHandle.animTime += Time.deltaTime;
}
```

The shader reads these from the instance buffer and generates the appropriate motion.

**Effort**: 1-2 hours

#### Step 6: Add "Look Around" Behavior (AI side)

For ambient NPCs: random timer (5-15 seconds) → stop walking → set `animState = Looking` → head turns for 2-3 seconds → resume walking.

For hoods: when near a crime target → set `animState = Checking` → head turns → check for cops in view → proceed or abort.

**Effort**: 2-3 hours

#### Summary

| Step | What | Effort | Files |
|---|---|---|---|
| 1 | Tag voxels with group IDs | 2-4 hrs | `.stasset` (MagicaVoxel export) |
| 2 | Expand instance buffer | 1-2 hrs | `VoxelChunkManager.cs` |
| 3 | Upload group ID buffer | 1 hr | `VoxelChunkManager.cs` |
| 4 | Group transforms in shader | 4-8 hrs | `VoxelProxyRaymarch.shader` |
| 5 | Drive animation from C# | 1-2 hrs | `VoxelCharacter.cs` |
| 6 | Look-around AI behavior | 2-3 hrs | NPC AI script |
| **Total** | | **~11-20 hrs** | |

**This is procedural animation** — the motion is generated by shader math (sin waves, rotation matrices) driven by `animTime`, not pre-baked keyframe clips. But unlike the RimWorld-style procedural technique (Technique 1), the motion is applied *per limb* via voxel groups, giving real articulation. The arms swing because the shader rotates the arm voxels around the shoulder pivot, not because the whole body bobs.

### Instance Buffer Evolution

```
Phase 0 (current):  Vector4(x, y, z, yaw)                    — 16 bytes
Phase 1 (anim):     + int animState + float animTime          — 24 bytes
Phase 2 (combat):   + int faction + float health              — 32 bytes
Phase 3 (groups):   + float4 groupTransforms[4]               — 96 bytes (only for grouped models)
```

All changes are **backward-compatible** — unused fields are zeroed for entities that don't need them.

---


## 6. Vehicle Physics

### Three Tiers of Vehicle Physics

#### Tier A: Rolling Sphere (Arcade — Simplest)

The car is a single Rigidbody sphere with the car mesh parented to it. Physics handles rolling. Steering rotates the mesh, acceleration applies force in the mesh's forward direction.

```
Sphere (Rigidbody + SphereCollider)
└── CarMesh (parented, position follows sphere, rotation from steering input)
```

- **Pros**: Trivial to implement, near-zero tuning, handles collisions naturally
- **Cons**: No suspension, no flipping, no wheel-level detail
- **Our fit**: Civilian traffic — fleeing cars, ambient traffic. They need to move and crash believably, not feel like driving.
- **Entity budget**: 20-50 vehicles

#### Tier B: Raycast Suspension (Arcade-GTA — Recommended for Combat)

No WheelColliders. 4 raycasts downward from the chassis detect ground contact. Each ray applies a spring-damper force (Hooke's law). Steering applies torque to the Rigidbody. Lateral friction cancels sideways velocity.

```
Chassis (Rigidbody + BoxCollider)
├── Raycast FL → spring force + steering
├── Raycast FR → spring force + steering
├── Raycast RL → spring force + motor torque
├── Raycast RR → spring force + motor torque
└── CenterOfMass (low, for stability)
```

- **Pros**: Full control over feel, can flip, suspension visible, no WheelCollider bugs, scales well
- **Cons**: More code than sphere, requires tuning spring/damper values
- **Our fit**: Combat vehicles — chasing cars, police cruisers. Need responsive handling, flipping, dramatic crashes.
- **Entity budget**: 4-14 vehicles (combat participants only)

#### Tier C: WheelCollider (Sim-lite — Not Recommended)

PhysX WheelColliders handle suspension, friction, slip curves automatically.

- **Pros**: Built-in, realistic
- **Cons**: Notoriously finicky, solver iterations need tuning, unstable at high speeds, highest per-vehicle performance cost
- **Our fit**: Skip. We don't need realistic tire slip for a mob sim. Tier B gives everything we need with more control and better performance.

### Recommended Physics Assignment

| Vehicle Type | Physics Tier | Rigidbody? | Can Flip? | Can Crash? | Entity Count |
|---|---|---|---|---|---|
| Civilian ambient traffic | A (sphere) | Yes, simple | Maybe | Yes, basic | 20-50 |
| Civilian fleeing | A (sphere) | Yes, simple | Maybe | Yes, basic | 10-30 |
| Hood combat vehicles | B (raycast) | Yes, tuned | Yes! | Yes, dramatic | 2-8 |
| Police cruisers | B (raycast) | Yes, tuned | Yes! | Yes, dramatic | 2-6 |

**Only 4-14 vehicles have active raycast physics at any time** (combat participants). The rest are simple spheres. Very manageable for PhysX.

### Key Physics Techniques

#### Velocity-Time Curve (Designer-Friendly Acceleration)

Instead of tuning engine parameters, define an AnimationCurve mapping time → target velocity. The physics system applies whatever force is needed to match the curve.

```
Velocity
  30 ─────────╮
  20 ──────╮  │
  10 ───╮  │  │
   0 ──╯  │  │
      0  2  4  6  Time (seconds)
```

Each vehicle type (sedan, sports car, police cruiser) gets its own curve. No physics PhD required.

#### Anti-Roll Bar (Stability + Flipping)

Apply a stabilizing force when the car tilts. If the car leans left, apply upward force on the left side. This prevents rolling during normal driving but **allows flipping during extreme maneuvers** (sharp turns at high speed, collisions). This is the key to "flipping is possible but not accidental."

#### Lateral Friction (Drifting)

Cancel sideways velocity with a configurable "stickiness" factor:
- `stickiness = 1.0` → car grips perfectly, no sliding
- `stickiness = 0.7` → car slides during sharp turns (drift)
- `stickiness = 0.0` → car is on ice

For combat vehicles, lower stickiness = more dramatic chases with drift around corners.

#### Downforce (Groundedness)

Apply downward force proportional to speed. At high speed, the car is pressed into the ground — more grip, harder to flip. At low speed, the car is light — easier to flip from a collision. This creates dynamic where **high-speed crashes are more dramatic** (more energy) but **low-speed crashes can still flip** (less downforce).

---

## 7. Vehicle Combat

### Overview

Two cars full of rival hoods come into contact and erupt in a gunfight between vehicles. A lead car is chased, a pursuing car chases. Hoods exchange gunfire from windows. Civilian cars around the combat react and flee.

### Vehicle AI States

```
                    ┌──────────┐
                    │ CRUISING │ ← normal traffic, waypoint following
                    └─────┬────┘
                          │ combat event (enemy vehicle detected)
                          ▼
                    ┌──────────┐
                    │ CHASING  │ ← pathfind to predicted enemy position
                    └─────┬────┘
                          │ close enough to engage
                          ▼
                    ┌──────────┐
                    │ ENGAGING │ ← hoods fire from windows, vehicle maintains pursuit
                    └─────┬────┘
                          │ collision / disabled
                          ▼
                    ┌──────────┐
                    │ DISABLED │ ← vehicle wrecked, hoods exit on foot
                    └──────────┘

Fleeing variant:
                    ┌──────────┐
                    │ FLEEING  │ ← drive away from threat, avoid obstacles
                    └─────┬────┘
                          │ escaped or cornered
                          ▼
                    ┌──────────┐
                    │ STOPPED  │ ← surrender, hoods exit, or counterattack
                    └──────────┘
```

### Firing From Vehicles

Hoods firing from cars are still Tier 2 instanced characters. A hood "popping out of a window" is a **position offset + animation state**, not a geometry change.

**Firing arc considerations**:
- **Window firing**: limited arc, can fire to the side
- **Popped out (over roof)**: wider arc, but exposed to incoming fire
- **Windshield**: very limited arc, mostly forward
- The hood's instance offset includes "in vehicle" + "popped out" state

**Visual**: The hood model shifts position within the vehicle bounding box. Procedural animation handles the "pop up" motion. No new rendering path.

### Vehicle Damage & Destruction

#### Damage States

| State | Visual | Physics | Rendering Tier |
|---|---|---|---|
| Intact | Normal | Active (Tier A or B) | Tier 2 (instanced) |
| Light damage | Smoke particles | Still driving | Tier 2 + particle effect |
| Heavy damage | Fire + smoke | Slowing, erratic | Tier 2 + particle effect |
| Disabled | Fire, not moving | Physics frozen | Tier 3 (individual) |
| Wrecked (settled) | Burned out shell | Static | Tier 1 (bake as debris) |

#### Tier 2 → Tier 3 Promotion

When a vehicle transitions from "driving with damage" to "disabled/wrecked", it promotes from Tier 2 to Tier 3:

1. **Unregister** from the instanced vehicle batch (remove from instance buffer)
2. **Create individual render** via the non-baked chunk path (`LoadChunk`)
3. **Add Rigidbody** if not already present (for crash physics)
4. **Add particle effects** (fire, smoke) as child objects

When the vehicle fully settles (burned out, no more animation):
5. **Bake as static debris** into nearest sector (Tier 1)
6. **Remove individual render** — now part of the baked sector

This is a **new tier transition pattern**: Tier 2 → Tier 3 → Tier 1. The existing tier doc covers Tier 1↔3 for buildings, but not Tier 2→3→1 for vehicles.

#### Collision with Other Vehicles

When two physics-enabled vehicles collide:
1. PhysX handles the impact (Rigidbody collision)
2. Damage calculated based on relative velocity and mass
3. Both vehicles may promote to Tier 3 if disabled
4. Hoods inside disabled vehicles exit on foot (spawn as Tier 2 characters)
5. The vehicle is removed from the player's inventory pool

#### Flipping

Flipping is achievable with Tier B (raycast) physics:
- Center of mass slightly above the wheels (not too low, or car never flips)
- Anti-roll bar allows tipping at extreme angles
- High-speed collision can apply enough torque to flip
- Once flipped, vehicle is "disabled" — hoods must exit on foot

### Vehicle-to-Pedestrian Impact

If a fleeing car hits a hood on foot:
1. Gameplay state change: hood is injured/knocked down
2. Brief procedural animation: rotation to "fallen" pose (see §5)
3. The vehicle may also take damage from the impact
4. No physics ragdoll — our voxel characters don't need skeletal physics

---

## 8. Traffic & Swarm Behavior

### Current State

Vehicles currently use `WaypointGraph` + `Pathfinder` for point-to-point movement. They're Tier 2 instanced, following pre-planned routes. No physics, no avoidance, no reactions.

### Swarm Behavior Additions

#### 1. Bumper System (Rear-End Avoidance)

Each vehicle has a forward raycast "bumper." If something is ahead within N meters, decelerate. If within M meters, stop.

```
[Car A] →→→ bumper ray → [Car B] →→→ bumper ray → [Car C]
  Car A slows because B is close
  Car B slows because C is close
  Natural accordion effect — looks like real traffic
```

This gives natural traffic jams and pile-ups without pathfinding recalculation. Simple, fast, emergent.

#### 2. Flee Vector (Panic Behavior)

When combat noise reaches a civilian vehicle:
1. Switch from waypoint following to **flee mode**
2. Pick a direction vector away from the noise source
3. Add simple obstacle avoidance (left/right raycast)
4. Drive at increased speed
5. Return to waypoint following when distance from noise > threshold

This creates emergent chaos — cars scattering in all directions during a gunfight. No complex AI needed.

#### 3. Formation Following (Chase Behavior)

For pursuing vehicles, the chase car doesn't pathfind to the target's current position — it pathfinds to the target's **predicted future position** based on current velocity:

```
Target current pos ────→ Target predicted pos (current + velocity * lookahead)
                                    ↑
Chaser pathfinds here (interception, not trailing)
```

This prevents the "follow the leader" train effect and creates realistic interception behavior.

#### 4. Stuck Detection

If a vehicle hasn't moved more than X meters in Y seconds:
1. Check if blocked by another vehicle (bumper ray)
2. If blocked, attempt to reverse and go around
3. If still stuck, enter "abandoned" state — hoods exit on foot
4. Vehicle becomes static (Tier 1 candidate for baking)

### Traffic Density Considerations

| Scenario | Active Vehicles | Physics-Enabled | Performance Concern |
|---|---|---|---|
| Normal traffic | 20-50 | 0 | None — all Tier 2 instanced |
| Combat encounter | 20-50 + 4-14 combat | 4-14 | Moderate — raycast physics |
| Major escalation | 30-60 + 10-20 combat | 10-20 | Watch PhysX cost |
| City-wide panic | 50-100 fleeing | 0 (sphere physics) | Low — spheres are cheap |

---

## 9. Police Escalation System

### Noise/Threat System

Gunfire emits a **noise event** with a radius. Any AI entity within the radius reacts:

```
NoiseEvent {
    Vector3 source;      // where the gunfire happened
    float intensity;     // loudness (caliber-based? or flat)
    float radius;        // how far it carries
    int faction;         // who caused it (for blame)
}
```

### Escalation Ladder

```
Hood vs Hood gunfight
    ↓ noise event (gunfire attracts attention)
Police dispatch (1-2 cruisers)
    ↓ arrival on scene
3-way combat (Hoods vs Hoods vs Police)
    ↓ more gunfire noise → more noise events
More police, roadblocks, civilian panic
    ↓ escalation tier increases
SWAT / heavy response (if game supports it)
```

### Police AI States

```
                    ┌──────────┐
                    │ PATROL   │ ← cruising, low awareness
                    └─────┬────┘
                          │ noise event detected
                          ▼
                    ┌──────────┐
                    │ RESPOND  │ ← drive to noise source, sirens
                    └─────┬────┘
                          │ arrive at scene
                          ▼
                    ┌──────────┐
                    │ ENGAGE   │ ← take cover, return fire
                    └─────┬────┘
                          │ targets down / escaped
                          ▼
                    ┌──────────┐
                    │ SEARCH   │ ← sweep area, look for suspects
                    └─────┬────┘
                          │ clear / lost target
                          ▼
                    ┌──────────┐
                    │ PATROL   │ ← return to cruising
                    └──────────┘
```

### Rendering Impact: None (Beyond Entity Count)

From the renderer's perspective:
- A cop is just another Tier 2 instanced character — same `RegisterInstancedCharacter` path
- A police cruiser is just another Tier 2 vehicle
- The renderer doesn't care about faction — it just draws instances

The concern is **entity count during peak combat**:
- 20 hoods + 10 cops + 15 civilian vehicles fleeing = 45 Tier 2 instances + 2-3 Tier 3 vehicles
- Well within budget for current architecture
- At 100+ entities, GPU-driven plan Phase 2 (buffer pooling) becomes critical

### Roadblocks

Police setting up a roadblock = spawning static objects (barriers, spike strips) on the road:
- **Tier 1** objects — bake into collision world as new solid voxels
- Vehicles that hit them crash (physics collision)
- This is a **collision world mutation** — the first time we'd modify the flat array after load
- Would need a "dirty sector" flag to trigger rebake of affected sector



## 10. Rendering Impact

### Tier Classification Summary

All new entities from combat/vehicle systems, classified into the existing rendering tier model:

| Entity | Default Tier | Combat Tier | Settled Tier | Notes |
|---|---|---|---|---|
| Hoods (on foot) | Tier 2 (instanced) | Tier 2 (instanced) | N/A | Already implemented, just add combat state |
| Police (on foot) | Tier 2 (instanced) | Tier 2 (instanced) | N/A | Same path as hoods, different faction |
| Civilian vehicles | Tier 2 (instanced) | Tier 2 (instanced) | Tier 1 (bake) | Sphere physics, flee behavior |
| Combat vehicles | Tier 2 (instanced) | Tier 3 (individual) | Tier 1 (bake) | Raycast physics when active |
| Cover props (Approach B) | Tier 2 (instanced) | Tier 2 (instanced) | N/A | Destructible — brief Tier 3 on death |
| Vehicle debris | N/A | Tier 3 (individual) | Despawn | Short-lived particle/voxel effect |
| Roadblocks | N/A | Tier 1 (baked) | N/A | Static once placed, mutates collision world |
| Tracers/muzzle flash | N/A | Particle effect | Despawn | Not voxel-rendered, standard Unity particles |

### Instance Buffer Format Changes

The current instance buffer is `Vector4(x, y, z, yaw)` — 16 bytes per entity.

**Phase 1 (Animation)**: Add `animState` + `animTime` → 24 bytes
**Phase 2 (Combat)**: Add `faction` + `health` → 32 bytes
**Phase 3 (Full rotation for vehicles)**: Replace `yaw` with `Quaternion` → 40 bytes

For vehicles that can flip, we need full rotation — not just yaw. This means either:
- A `Quaternion` (16 bytes for rotation alone) replacing the `float yaw` (4 bytes)
- Or Euler `Vector3` (pitch, yaw, roll) — 12 bytes

This is a **breaking change to the instance buffer layout**, but it's a one-time migration that benefits all instanced entities (characters could eventually lean/dodge too).

**Mitigation**: Use a separate buffer for "full rotation" entities (vehicles) vs "yaw-only" entities (characters). This avoids changing the character buffer until needed.

### New Tier Transition Pattern: Tier 2 → Tier 3 → Tier 1

The existing `DYNAMIC_OBJECT_RENDERING_TIERS.md` covers:
- Tier 1 ↔ Tier 3 (buildings: unbake → mutate → rebake)
- Static Tier 2 (characters: always instanced)

Combat vehicles need a **new pattern**: Tier 2 → Tier 3 → Tier 1

```
Tier 2 (instanced, cruising)
    ↓ combat event / crash
Tier 3 (individual, physics active, unique damage)
    ↓ vehicle settles (wrecked, burned out)
Tier 1 (baked as static debris into nearest sector)
```

This should be documented in `DYNAMIC_OBJECT_RENDERING_TIERS.md` when implementation begins.

### Shader Changes

| Change | Trigger | Effort |
|---|---|---|
| Procedural animation offsets | NPC animation (§5) | Medium — shader math per animState |
| Full rotation for vehicles | Vehicle flipping | Medium — replace yaw with quaternion in shader |
| Vehicle damage visual | Disabled vehicles | Low — color tint + smoke particles (not shader) |
| Fire glow from burning vehicles | Disabled vehicles | Medium — per-instance point light in shader |

---

## 11. Impact on Existing Architecture

### GPU-Driven Rendering Plan

| Plan Element | Impact | Notes |
|---|---|---|
| Phase 1 (TRS Cache) | ✅ No impact | Buildings still don't move |
| Phase 2 (Buffer Pooling) | ⚠️ More important | Vehicle tier promotions mean frequent buffer updates. 50+ entities in combat = frequent instance buffer rebuilds. Zero-GC buffer updates become critical. |
| Phase 3 (Indirect Draw) | ✅ Still valid for buildings | Vehicles may stay on DrawMeshInstanced (simpler tier promotion logic) |
| Phase 4 (GPU Culling) | ✅ Still valid for buildings | Vehicles culled separately (smaller batch) |
| Phase 5 (GPU LOD) | ✅ Still valid for buildings | Vehicles don't need LOD (always close to camera in combat) |
| Phase 6 (Scale Test) | ⚠️ Needs revision | Vehicle physics adds CPU cost not in original plan. Scale test must include combat scenarios. |

### Invariant Computation Principle

| Computation | Invariant? | Notes |
|---|---|---|
| Cover points | ✅ Yes | Derived from static building geometry. Cache at load time. |
| Vehicle physics | ❌ No | Dynamic by definition — Rigidbody position changes every FixedUpdate |
| Traffic waypoints | ✅ Yes | Graph is static. Route planning is per-vehicle but graph is shared. |
| Firing arcs | ✅ Yes | Per hood type, not per instance. Cache at load. |
| Police dispatch rules | ✅ Yes | Escalation thresholds are constants. |
| Vehicle damage models | ✅ Yes | Damage state thresholds are constants. Per-vehicle damage is dynamic. |

New invariant candidates to add to `INVARIANT_COMPUTATION_PRINCIPLE.md`:
1. **Cover point cache** — scan collision world once, cache per sector
2. **Firing arc definitions** — per weapon/type, not per instance
3. **Vehicle physics tuning curves** — Velocity-Time AnimationCurves are static data

### Collision World

| Change | Impact |
|---|---|
| Cover point queries | Read-only — no mutation. Safe. |
| Roadblock placement | **Write** — adds solid voxels to flat array. Needs dirty flag + sector rebake. |
| Vehicle collision | Uses PhysX colliders, not voxel collision world. No change to flat array. |
| Vehicle debris | Short-lived, despawns. No collision world change. |

The roadblock feature is the first time we'd **mutate the collision world after load**. This needs careful design:
- Mark affected sectors as "dirty"
- Rebuild cover points for affected sectors
- Rebake affected sectors if roadblock is permanent

### Dynamic Object Rendering Tiers

Update needed: add Tier 2 → Tier 3 → Tier 1 transition pattern for vehicles.

Current doc covers:
- Tier 1 ↔ Tier 3 (buildings)
- Static Tier 2 (characters, doors)

New pattern:
- Tier 2 → Tier 3 (vehicle enters combat/crash)
- Tier 3 → Tier 1 (vehicle settles as wreck)
- Tier 2 → Tier 3 → Tier 2 (cover prop destroyed, debris despawns, new prop spawns)

---

## 12. Implementation Priority & Phasing

### Phase 1: Procedural Animation Foundation (No Combat Yet)

**Goal**: Get NPC animation working with the existing character system.

| Task | Effort | Rendering Change? |
|---|---|---|
| Expand instance buffer: add animState + animTime | 2 hrs | Yes — shader + buffer |
| Implement walking bob in shader | 1 hr | Yes — shader math |
| Implement idle breathing in shader | 30 min | Yes — shader math |
| Test with existing NPC population | 1 hr | No |

**Deliverable**: NPCs visibly bob while walking and breathe while idle. No gameplay change.

### Phase 2: Street Combat (On Foot)

**Goal**: Two hoods can find cover, fire at each other, take damage, fall.

| Task | Effort | Rendering Change? |
|---|---|---|
| Cover point scanner (collision world query) | 4 hrs | No |
| Combat AI state machine | 8 hrs | No |
| Firing mechanics (hitscan + physical projectiles) | 6 hrs | Minor (tracer rendering) |
| Spatial hash + projectile manager | 5 hrs | No — pure CPU |
| Firing recoil + hit flinch animation | 2 hrs | Yes — shader math |
| Falling/death animation (voxel swap) | 3 hrs | Yes — atlas buffer |
| Faction system for targeting | 2 hrs | No |

**Deliverable**: Two hoods encounter each other, take cover, exchange fire, one goes down.

### Phase 3: Vehicle Physics Foundation

**Goal**: Vehicles have physics, can crash and flip.

| Task | Effort | Rendering Change? |
|---|---|---|
| Tier A sphere physics for civilian traffic | 4 hrs | No — position from Rigidbody |
| Tier B raycast suspension for combat vehicles | 8 hrs | No — physics layer |
| Velocity-Time curve system | 2 hrs | No |
| Anti-roll + lateral friction + downforce | 4 hrs | No |
| Vehicle collision damage model | 4 hrs | No |
| Tier 2 → Tier 3 promotion system | 6 hrs | Yes — unregister/re-register |
| Full rotation in instance buffer (quaternion) | 4 hrs | Yes — shader + buffer |

**Deliverable**: Cars drive with physics, can crash into each other, flip, and become disabled.

### Phase 4: Vehicle Combat

**Goal**: Cars chase, hoods fire from windows, vehicles crash and hoods exit on foot.

| Task | Effort | Rendering Change? |
|---|---|---|
| Vehicle AI states (chase, engage, flee) | 8 hrs | No |
| Formation following (predicted interception) | 4 hrs | No |
| Firing from vehicle (hood position offset) | 4 hrs | Minor — instance offset |
| Vehicle disable → hoods exit on foot | 4 hrs | Yes — spawn new Tier 2 |
| Vehicle debris particles | 2 hrs | Minor — particle system |
| Fire/smoke on disabled vehicles | 2 hrs | Minor — particle system |

**Deliverable**: Two cars chase, hoods fire from windows, one crashes and flips, hoods bail out on foot.

### Phase 5: Traffic Swarm & Avoidance

**Goal**: Civilian traffic reacts to combat, flees, creates emergent chaos.

| Task | Effort | Rendering Change? |
|---|---|---|
| Bumper system (rear-end avoidance) | 3 hrs | No |
| Flee vector (panic behavior) | 3 hrs | No |
| Stuck detection + abandon vehicle | 2 hrs | No |
| Vehicle → pedestrian impact | 2 hrs | No |

**Deliverable**: Gunfire causes civilian cars to scatter, traffic jams form, cars get stuck and abandoned.

### Phase 6: Police Escalation

**Goal**: Police respond to combat, escalate, set up roadblocks.

| Task | Effort | Rendering Change? |
|---|---|---|
| Noise/threat event system | 4 hrs | No |
| Police AI states (patrol, respond, engage, search) | 8 hrs | No |
| Police dispatch logic (escalation tiers) | 4 hrs | No |
| Roadblock placement (collision world mutation) | 6 hrs | Yes — sector rebake |
| Cover point cache invalidation for roadblocks | 2 hrs | No |

**Deliverable**: Gunfight triggers police response, cops arrive and engage, roadblocks cause crashes.

### Phase 7: Polish

**Goal**: Voxel group transforms for limb animation, cover props, visual effects.

| Task | Effort | Rendering Change? |
|---|---|---|
| Voxel group transform system (shader) | 2 days | Yes — major shader work |
| Re-author character models with group tags | 2 days | No — asset pipeline |
| Cover props (Approach B) | 4 hrs | Yes — new Tier 2 batch |
| Vehicle fire glow (per-instance light) | 4 hrs | Yes — shader extension |
| Head tracking (procedural yaw) | 1 hr | Yes — shader math |

**Deliverable**: Hoods aim arms at targets, crouch behind cover props, burning vehicles cast glow.

### Total Estimated Effort

| Phase | Effort | Cumulative |
|---|---|---|
| 1. Animation Foundation | ~5 hrs | 5 hrs |
| 2. Street Combat (incl. projectiles + spatial hash) | ~28 hrs | 33 hrs |
| 3. Vehicle Physics | ~32 hrs | 65 hrs |
| 4. Vehicle Combat (incl. vehicle-vs-instance) | ~25 hrs | 90 hrs |
| 5. Traffic Swarm | ~10 hrs | 100 hrs |
| 6. Police Escalation | ~24 hrs | 124 hrs |
| 7. Polish | ~4 days | ~156 hrs |

**Note**: These are rough estimates for design/planning. Actual implementation will vary. Phases can be parallelized (e.g., Phase 5 and 6 are independent).

### Dependencies

```
Phase 1 (Animation) ──→ Phase 2 (Street Combat) ──→ Phase 4 (Vehicle Combat)
                              │                           ↑
                              │                    Phase 3 (Vehicle Physics)
                              │
                              ├──→ Phase 5 (Traffic Swarm)
                              ├──→ Phase 6 (Police Escalation)
                              └──→ Phase 7 (Polish) — after all above
```

Phase 1 is the foundation — everything else depends on having animation state in the instance buffer.
Phase 3 must come before Phase 4 (need physics before vehicle combat).
Phases 5 and 6 can proceed in parallel after Phase 2.

---

## 13. Physical Projectiles & Spatial Hash

### Motivation

The original design used hitscan — instant raycast, target hit or missed, done. But a mob sim where combat is rare but consequential benefits enormously from **physical projectiles**:

- **Stray bullets hit civilians** → police investigation escalates, witnesses generated
- **Crossfire creates mayhem** → peds scatter, cars flee, emergent chaos
- **Travel time** → suppression is possible, targets can dodge, shots can be led
- **Drive-by sprays** actually spray a street → atmosphere and consequence
- **Missed shots hit buildings** → visual bullet holes, atmosphere

In a mob sim, the *stories* come from things going wrong. A hit goes sideways because a pedestrian walked into the crossfire. A drive-by catches a rival who happened to be walking past. Physical projectiles create these moments.

### Design: No Physics Engine Required

This system uses **zero Unity physics infrastructure** — no Rigidbody, no Collider, no PhysX. It's pure CPU math:

1. **Spatial hash** — a `Dictionary<Vector3Int, List<int>>` mapping world grid cells to instance IDs
2. **Projectile entities** — lightweight structs with position, velocity, TTL, source faction
3. **Per-frame queries** — projectile checks its current grid cell + neighbors for instance hits

### Spatial Hash

```csharp
public class InstanceSpatialHash
{
    private Dictionary<Vector3Int, List<int>> grid = new();
    private float cellSize;  // e.g., 2.0 world units

    // Rebuild each frame from instance positions (O(N))
    public void Rebuild(Vector3[] positions, int count)
    {
        grid.Clear();
        for (int i = 0; i < count; i++)
        {
            var key = CellKey(positions[i]);
            if (!grid.TryGetValue(key, out var list))
            {
                list = new List<int>();
                grid[key] = list;
            }
            list.Add(i);
        }
    }

    // Query: return all instance IDs in the same cell + 8 neighbors
    public List<int> QueryNearby(Vector3 pos)
    {
        var key = CellKey(pos);
        var results = new List<int>();
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var k = key + new Vector3Int(dx, dy, dz);
            if (grid.TryGetValue(k, out var list))
                results.AddRange(list);
        }
        return results;
    }

    private Vector3Int CellKey(Vector3 pos) => new(
        Mathf.FloorToInt(pos.x / cellSize),
        Mathf.FloorToInt(pos.y / cellSize),
        Mathf.FloorToInt(pos.z / cellSize));
}
```

**Cell size**: 2.0 world units (roughly one sidewalk segment). At 100 instances, average ~1-3 instances per cell. At 500 instances, ~3-8 per cell. Queries are O(k) where k = instances per cell.

**Rebuild cost**: O(N) per frame — just bucket each instance into its cell. For 500 instances, this is ~500 dictionary operations — trivial.

### Projectile Entity

```csharp
public struct Projectile
{
    public Vector3 position;      // current world position
    public Vector3 velocity;      // direction * speed (world units/sec)
    public float ttl;             // time until despawn (seconds)
    public int sourceFaction;     // who fired (for blame/escalation)
    public int sourceHoodId;      // specific hood (for crime resolution)
    public float damage;          // damage on hit
    public bool active;           // false = despawn next frame
}
```

**Per-frame update** (all active projectiles):
1. `position += velocity * deltaTime`
2. `ttl -= deltaTime` — if <= 0, despawn
3. Query spatial hash at `position` → check each nearby instance
4. If instance hit (distance < instance radius) → resolve damage, despawn projectile
5. If no instance hit, check `VoxelCollisionWorld.ProbeGround` for wall hit → despawn, spawn impact effect

### Cost Analysis

| Scenario | Instances | Projectiles | Checks/Frame | Cost |
|---|---|---|---|---|
| Ambient (no combat) | 100 | 0 | 0 (skip hash rebuild) | Free |
| Single gunfight | 100 | 20 | ~60 distance checks | Trivial |
| Major combat | 200 | 80 | ~240 distance checks | Trivial |
| City-wide mayhem | 500 | 200 | ~600 distance checks | Still trivial |

For comparison, a single Unity Physics.Raycast against a scene with 100 colliders costs more than all of these combined.

### Crossfire Hitting Civilians

This is the core gameplay value. When a projectile hits an instance:

1. Look up instance data → is it a hood, cop, or civilian ped?
2. **Hood hit**: apply damage, trigger flinch/fall animation, faction retaliation
3. **Cop hit**: apply damage, immediate police escalation (cop killer = max threat)
4. **Civilian hit**: apply damage (likely instant down — they're unarmed), generate witness event, police investigation

**Witness generation**: If a civilian is hit but not killed (or nearby civilians see the hit), generate a `WitnessEvent` with:
- `sourceHoodId` — who fired
- `victimId` — who was hit
- `location` — where it happened
- `reportedToPolice` — false until civilian reaches a phone/police station

This feeds into the existing `CrimeSystem` and police escalation ladder (§9).

### Vehicle-vs-Instance (Same Spatial Hash)

The spatial hash also handles vehicle mayhem. A moving vehicle sweeps through grid cells:

1. Each frame, query the vehicle's current cell + next cell along velocity vector
2. Check if any instance is within the vehicle's bounding volume
3. If hit → knockdown/kill the instance, apply damage to vehicle
4. No physics solver needed — just AABB overlap check

**This unifies all "thing hits thing" queries** under one cheap spatial hash:
- Projectile → instance (bullets hitting people)
- Vehicle → instance (cars hitting people)
- Instance → instance (melee, if ever added)

### Integration with Existing Systems

| System | Integration |
|---|---|
| `VoxelCharacter` | Provides position for spatial hash. Takes damage from projectile hits. |
| `VoxelCollisionWorld` | Wall/building hit detection for projectiles (existing `ProbeGround` method). |
| `CrimeSystem` | Damage resolution, witness generation, police escalation triggers. |
| `CharacterAnimation` | Hit reaction — set `animState = Flinch` or `Falling` on projectile hit. |
| `HoodAgent` (future) | Combat AI triggers projectile spawn on fire action. Carries `sourceHoodId`. |
| Instance buffer | Positions already available for spatial hash rebuild — no extra data needed. |

### Rendering Impact: Minimal

Projectiles are **not voxel-rendered objects**. They're visual effects:
- **Tracer**: brief line renderer or instanced beam (same as hitscan visual)
- **Impact**: particle burst at hit location
- **Bullet hole on wall**: decal or small particle effect (optional, Phase 2+)

No new rendering tier. No instance buffer changes. The spatial hash is pure CPU and doesn't touch the GPU pipeline at all.

### Why Not Unity Physics?

| Approach | Cost | Scales To | Physics Engine? |
|---|---|---|---|
| Unity Rigidbody + Collider | Broadphase + solver + contact pairs | ~50 entities before lag | Yes — full PhysX |
| Spatial hash + distance checks | O(N) rebuild + O(P×k) queries | 500+ instances, 200+ projectiles | No — pure math |

Unity physics is designed for general-purpose collision with complex shapes, resting contacts, stacking, joints, etc. We need none of that. We need "does this point overlap this sphere" — a distance check. The spatial hash is the right tool.

### Implementation Effort

| Component | Effort | Files |
|---|---|---|
| `InstanceSpatialHash` class | 2 hrs | New: `InstanceSpatialHash.cs` |
| `Projectile` struct + manager | 3 hrs | New: `ProjectileManager.cs` |
| Projectile spawn from firing AI | 1 hr | Modify: combat AI (Phase 2) |
| Damage resolution on hit | 2 hrs | Modify: `CrimeSystem.cs`, `VoxelCharacter.cs` |
| Witness/event generation | 2 hrs | Modify: `CrimeSystem.cs` |
| Tracer/impact VFX | 2 hrs | New: particle effects |
| Vehicle-vs-instance query | 1 hr | Modify: `VoxelVehicle.cs` or `HoodAgent.cs` |
| **Total** | **~13 hrs** | |

### Phasing Within Combat Doc

- **Phase 2 (Street Combat)**: Add spatial hash + projectile system alongside hitscan. Hitscan remains as a fallback/option. Physical projectiles are the default for all firearm combat.
- **Phase 4 (Vehicle Combat)**: Extend spatial hash for vehicle-vs-instance queries. Drive-by sprays use the same projectile system.
- **Phase 5 (Traffic Swarm)**: Vehicle mayhem (cars hitting peds) uses the spatial hash.

---

## Related Documents

- **`docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`** — Tier 1/2/3 classification philosophy (needs update for Tier 2→3→1 vehicle pattern)
- **`docs/systems/GPU_DRIVEN_RENDERING_PLAN.md`** — 6-phase rendering optimization plan (Phase 2 becomes more critical with combat)
- **`docs/systems/INVARIANT_COMPUTATION_PRINCIPLE.md`** — "Do Once" principle (cover points, firing arcs are new invariant candidates)
- **`docs/systems/INSTANCING_AND_BUFFERING.md`** — Current instancing architecture (instance buffer format will evolve)
- **`docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`** — Sector baking details (roadblocks need sector rebake)
- **`Assets/Scripts/Sim/VoxelVehicle.cs`** — Current vehicle implementation (Tier 2 instanced, no physics)
- **`Assets/Scripts/Sim/VehicleTestSpawner.cs`** — Test harness for vehicle spawning
- **`Assets/Scripts/UI/VoxelChunkManager.cs`** — Rendering manager (RegisterInstancedCharacter, LoadChunk for Tier 3)
- **`docs/systems/GANG_SIMULATION_ARCHITECTURE.md`** — Multi-hood simulation design (HoodAgent will integrate with spatial hash + projectile system)
