## Contents

- Overview
- The Core Question
- The Three Tiers
- Tier 1: Fully Static (Bake It)
- Tier 2: Batched Dynamic (Instance It)
- Tier 3: Individually Dynamic (Break It Away)
- Decision Checklist
- Worked Examples
- Anti-Patterns to Avoid
- How This Relates to Other Rendering Docs

---

# Dynamic Object Rendering Tiers — Design Philosophy

**Created**: Aug 8, 2026
**Status**: 📐 FOUNDATIONAL — reference this before deciding how to render any new dynamic city object
**Relates to**: `Assets/Scripts/UI/VoxelChunkManager.cs`, `Assets/Scripts/UI/SectorBaker.cs`, `docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`, `docs/systems/3D_CITY_RENDERING.md`

---

## Overview

The city renderer has to handle objects with wildly different lifecycles: static buildings that never change, hundreds of citizens walking around constantly, doors opening and closing, and (rarely) a specific building getting destroyed. Treating all of these the same way — either "bake everything" or "make everything individually dynamic" — is wrong in both directions. Baking a door would make it impossible to animate. Individually-rendering every static wall would blow the draw-call budget sector baking exists to solve.

This document captures the mental model for deciding, for any new dynamic element, **which rendering strategy it should use** — before writing any code for it.

---

## The Core Question

For any object (or class of objects) you're about to add to the city, ask:

> **How many of these exist, how much does each instance actually differ from the others, and how often does that difference change?**

The answer places it in one of three tiers. Getting this classification right up front avoids both wasted engineering effort (building a heavy system for something that didn't need it) and performance cliffs (using a cheap system for something that needed more).

---

## The Three Tiers

### Tier 1: Fully Static (Bake It)

**Use when**: the object never changes shape at runtime, for the lifetime of that object (buildings that are never destroyed, roads, terrain, sidewalks).

**Mechanism**: `SectorBaker` merges many objects' voxel data into one shared buffer per sector (`mergedVoxelBuffer`/`buildingMetaBuffer`/`buildingPosBuffer`), drawn with **one `DrawMeshInstanced` call per sector** regardless of how many buildings are inside it. See `@/c/Users/NADECC/ATSTradingDashboard Project/Cursor Workshop/SteelCityMobSim/Assets/Scripts/UI/VoxelChunkManager.cs:1237-1354` (`RenderBakedSectors`).

**Cost profile**: near-zero per-frame cost. The only ongoing cost is the sector-level frustum/distance cull test, which happens once per sector, not once per building.

**Trade-off**: mutating a single object inside a baked sector is expensive — it means touching a buffer shared by every other object in that sector. Don't put anything here that needs to change often.

---

### Tier 2: Batched Dynamic (Instance It)

**Use when**: many objects share the same base shape, but each one has its own small, frequently-changing state — position, rotation, a single animated value (swing angle, bob height, tint).

**Mechanism**: one shared mesh/voxel model + one small per-instance `StructuredBuffer` (typically a `Vector4` per instance) rebuilt each frame, drawn with **one `DrawMeshInstanced` call for the entire batch**, no matter how many instances exist. This already exists in the codebase for citizens/hoods: `RegisterInstancedCharacter`/`RenderInstancedCharacters` (`@/c/Users/NADECC/ATSTradingDashboard Project/Cursor Workshop/SteelCityMobSim/Assets/Scripts/UI/VoxelChunkManager.cs:990-1148`). Each character shares one voxel model (`sharedCharacterVoxelBuffer`) and differs only by `Vector4(worldPos.x, worldPos.y, worldPos.z, yaw)` in `instanceOffsetBuffer`.

**Cost profile**: one draw call for the whole batch. Per-frame cost is rebuilding the small instance-data buffer (cheap — bounded by instance count, not by mesh complexity).

**This is the tier for**: door leaves opening/closing, citizens/hoods walking, any city-wide population of similar objects that move or animate but don't change shape.

**Why this tier matters most**: it's the one most likely to be skipped by instinct. The naive approach to "I need this object to animate" is "give it its own draw call" (Tier 3's mechanism). If there are going to be dozens or hundreds of these objects, that instinct is wrong — batch them like the character system already does, even if today there's only one.

---

### Tier 3: Individually Dynamic (Break It Away)

**Use when**: an object's *shape itself* is uniquely and (semi-)permanently changing — not just its position or a single animated parameter, but its actual voxel geometry (a building collapsing into unique rubble, a wall gaining a unique breach hole).

**Mechanism**: pull the object out of its Tier 1 batch (unregister from the sector, shrink the sector's buffers) and give it its own private `ComputeBuffer` via the existing non-baked chunk path, `LoadChunk` (`@/c/Users/NADECC/ATSTradingDashboard Project/Cursor Workshop/SteelCityMobSim/Assets/Scripts/UI/VoxelChunkManager.cs:660-717`). This is the same path every building already uses when `useSectorBaking = false`. Once the change settles, feed the new state back into `SectorBaker.BakeSector` for that sector and re-merge — back to Tier 1.

**Cost profile**: one extra draw call for the duration of the change, plus the one-time cost of rebaking the affected sector when it settles (not per-frame — a single operation at the start and end of the change).

**Trade-off**: this is expensive relative to the other two tiers, both in draw calls while unbaked and in the rebake cost. It should be reserved for events that are genuinely rare per-object (a building doesn't get destroyed multiple times a second) — not used as the default answer to "I need this to be dynamic."

---

## Decision Checklist

Before implementing a new dynamic city feature, answer these in order:

1. **Does this object's actual geometry change, or just its position/rotation/a single parameter?**
   - Geometry changes → Tier 3.
   - Position/rotation/parameter only → continue to #2.
2. **Are there going to be many of these at once (tens, hundreds, thousands)?**
   - Yes → Tier 2 (batch it, even if only one exists today — the population will grow).
   - No, genuinely one-off → Tier 2 is still fine (it's not more expensive to batch one instance), but a dedicated one-off system is acceptable if it's truly singular (e.g., one special landmark).
3. **How often does the change happen, per object?**
   - Rarely, once per object's lifetime (destruction) → Tier 3, and don't worry about the rebake cost since it's infrequent.
   - Constantly (walking, door swings, ambient animation) → Tier 2, never Tier 3. Rebaking every frame for something this frequent would defeat the purpose of baking entirely.

---

## Worked Examples

| Feature | Geometry change? | Population | Frequency | Tier |
|---|---|---|---|---|
| Building walls/roof | No (static) | Thousands | Never | **1** — sector baked |
| Citizen/hood walking | No (shared shape) | Hundreds-thousands | Constant | **2** — instanced (already built) |
| Door opening/closing | No (hinge angle only) | One per business, city-wide | Frequent | **2** — instanced, same pattern as citizens |
| Citizen "inside" a business | N/A — no interior geometry exists at all | N/A | N/A | **Not a rendering problem** — just a visibility/state flag on the citizen (see `docs/systems/3D_CITY_RENDERING.md`'s "no building interiors" principle) |
| Building torched/destroyed | Yes — unique rubble/burned voxels | One specific building, rare per-building | Once (per building, ever) | **3** — unbake, animate, rebake |
| A block-wide explosion affecting many buildings at once | Yes, for each affected building | Several buildings simultaneously | Rare event, but multiple Tier-3 operations at once | **3**, applied per-building, batched as one rebake pass over the affected sector(s) rather than one rebake per building |

---

## Anti-Patterns to Avoid

- **Using Tier 3 for anything that repeats often.** If you find yourself unbaking/rebaking the same sector multiple times per minute, the feature causing it (doors, ambient animation, etc.) was misclassified — it belongs in Tier 2.
- **Using Tier 1 for anything that needs per-object mutation, "just this once."** Once you bake something into a shared sector buffer, mutating one instance means touching data shared by every neighbor in that sector. If a "static" object turns out to need occasional individual changes, it should have gone through Tier 3's unbake path from the start, not a special-cased poke into Tier 1's buffer.
- **Giving every dynamic object its own draw call "to keep it simple."** This is the instinct Tier 2 exists to override. One draw call per citizen or per door does not scale — it's exactly the 1-draw-per-object problem sector baking was built to solve for buildings, just reintroduced for a different object type.
- **Modeling building interiors to support "going inside."** Per `docs/systems/3D_CITY_RENDERING.md`, buildings are solid shells. "Going inside" a business is a citizen state-machine concept (visible → entering → hidden/waiting → leaving → visible), not a rendering concept. Don't build interior geometry to support this.

---

## How This Relates to Other Rendering Docs

- `docs/systems/3D_CITY_RENDERING.md` — the original high-level vision for the Working Week 3D visualization (entity budgets, camera modes, event stream format). This doc's Tier 1/2 examples (buildings, citizens) are the concrete rendering mechanics behind that vision.
- `docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md` — covers Tier 1 specifically in depth: current sector-baking implementation, known gaps (no LOD on baked sectors, no depth sort), and a proposed future evolution (GPU-driven indirect rendering) that would change *how* Tier 1 draws are submitted without changing this tier model at all. The rebake trigger point discussed there (at `WeekTransition`'s loading phase) is a Tier 1 ↔ Tier 3 transition, same concept as this doc's "settles → rebake" step.

This document is the philosophy; the other two are the current implementation and the planned technical evolution of Tier 1 specifically. When in doubt about a *new* feature, start here to classify it, then consult the relevant implementation doc for how that tier is actually built.
