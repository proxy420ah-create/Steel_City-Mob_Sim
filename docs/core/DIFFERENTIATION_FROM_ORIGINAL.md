# Differentiation from Original — Steel City vs Gangsters: Organized Crime

**Created**: August 14, 2026
**Status**: 🔒 ACTIVE — Living document, updated as features are implemented
**Purpose**: Central reference for how Steel City departs from, polishes, and extends the 1998 original

---

## Overview

Steel City is a **spiritual successor**, not a remake. The core loop (plan → execute → review) and data-driven design are preserved faithfully. But the original had opacity, system isolation, and technical limitations that modern tools and design thinking can address.

This document catalogs every known differentiation — both by design intent and by implementation reality — and includes a dedicated section for combat differentiations.

---

## 1. Rendering & Visual Style

| Feature | Original (1998) | Steel City | Status |
|---------|-----------------|------------|--------|
| **Rendering** | 2D isometric sprites | 3D voxel raymarch engine (GPU compute) | ✅ Implemented |
| **Characters** | Pre-rendered sprite frames | GPU instanced voxel volumes with compute shader animation | ✅ Implemented |
| **Animation** | Sprite frame swapping | Procedural GPU compute (CSPose kernel) with Catmull-Rom keyframe interpolation | ✅ Implemented |
| **Limb articulation** | None (whole-body sprites) | Voxel group transforms (6 groups: head, torso, L/R arms, L/R legs) | ✅ Implemented |
| **Clothing** | None (fixed sprites) | Per-instance material remapping — each character wears unique outfit in 1 draw call | ✅ Implemented |
| **City view** | Flat 2D tile map | 3D voxel city with raymarched buildings, shadows, day/night lighting | ✅ Implemented |
| **Buildings** | 2D tile sprites | GPU instanced voxel sectors (1 draw call per sector vs 100+ chunks) | ✅ Implemented |
| **Lighting** | None (pre-baked sprites) | Hybrid normals, self-shadowing, dynamic day/night cycle | ✅ Implemented |
| **Vehicles** | 2D sprites | GPU instanced voxel vehicles with turning wheels, faction colors | ✅ Implemented |

**Design doc reference**: `DESIGN_PHILOSOPHY.md` §5 originally said "No 3D graphics (isometric 2D)" — this was superseded by the Unity pivot early in development.

---

## 2. City Generation

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **City layout** | Static — same map every game | Procedural generation via 3-phase pipeline (Macro → Granular → Buildings) | ✅ Implemented |
| **Zones** | Not modeled | Hub-and-spoke zoning with weighted influence (EC, Industrial, Commercial, Residential) | ✅ Implemented |
| **Alleys** | Not modeled | 25% spawn rate per commercial/core block, 3-lane debris\|path\|debris | ✅ Implemented |
| **Rail line** | Not modeled | Elevated rail at seed-determined column, train animation | ✅ Implemented |
| **Trolley** | Not modeled | Trolley tracks as future transit layer | 📝 Planned |
| **Block density** | Fixed | 3×3 business sub-grid per block (target: 81 businesses from ~13) | 📝 Planned |

**Design doc reference**: `SOURCE_GAME_ANALYSIS.md` — "Static city → Replace with procedural generation"

---

## 3. Simulation Transparency

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Why owner refused** | Black box — no feedback | Show fear vs hostility roll result | 📝 Planned |
| **Police investigation progress** | Invisible | Visible investigation leads, decay timers | 📝 Planned |
| **NPC state** | Hidden numbers | Fear/Hostility/Squeal visible on inspection | 📝 Planned |
| **Combat results** | Text summary | Combat log with tactical decisions, cover usage, hit/miss per round | 📝 Designed |
| **Notifications** | Spam — every sighting reported | Aggregated and tiered by importance | 📝 Planned |

**Design doc reference**: `DESIGN_PHILOSOPHY.md` §5 — "Transparency: Make simulation state visible"

---

## 4. System Interconnection

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Crime cascading** | Each crime isolated | Raid uncovers tax evasion, bombing displaces residents, arson raises insurance | 📝 Planned |
| **Hood relationships** | None — interchangeable stat blocks | Trust, rivalry, mentorship web | 📝 Planned |
| **Economic interconnection** | Businesses independent | Supply/demand links, goods flow, bombing disrupts supply chains | 📝 Designed |
| **Business interactions** | None | Business fronts mask illegal ops, market share decay, suspicion matrix | 📝 Designed |

**Design doc reference**: `DESIGN_PHILOSOPHY.md` §5 — "Interconnection: Make systems talk to each other"

---

## 5. Intelligence & Fog of War

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Map visibility** | See everything | Territory-based fog of war — only see what you own/scout | 📝 Planned |
| **Squealer identification** | Instant | Lawyer-gated — requires legal infrastructure to identify | 📝 Designed |
| **Business radar** | All businesses visible | Refined — only discovered through scouting | 📝 Designed |
| **Friendly NPCs** | Not modeled | Informants — citizens who feed intel based on relationship | 📝 Planned |

**Design doc reference**: `INTELLIGENCE_TERRITORY.md`, `PLAYTESTING_INSIGHTS.md`

---

## 6. Police & Law Enforcement

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Police structure** | Monolithic "the police" faction | Individual beat officers with geographic jurisdiction | 📝 Planned |
| **Corruption** | Global | Per-beat — bribe specific officers, they suppress heat in their area | 📝 Designed |
| **Internal Affairs** | Not modeled | Natural ceiling on corruption — too many bribes triggers IA | 📝 Designed |
| **Rival corruption** | Not modeled | Rival gangs compete for same cops | 📝 Designed |
| **Escalation** | Not documented | Noise event system → police dispatch → 3-way combat → roadblocks | 📝 Designed |

**Design doc reference**: `CORRUPTION_POLICE.md`, `COMBAT_VEHICLE_DESIGN.md` §9

---

## 7. Economy as Weapon (Mafia Tycoon Principle)

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Economic interaction** | Some business management | Crime-driven only — player disrupts, never manages | ✅ Design locked |
| **Economic information** | Visible | Gated behind gang activity (spying, casing, scouting) | 📝 Planned |
| **Individual citizen finances** | Not modeled | Bank accounts as AI motivation engine (desperation → recruitment, crime) | 📝 Designed |
| **Supply/demand** | Basic | Simulated, discovered through scouting, manipulated through crime | 📝 Designed |

**Design doc reference**: `MAFIA_TYCOON_DESIGN_PRINCIPLE.md`

---

## 8. Character System

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Character models** | 2D portrait + sprite | Procedural voxel characters with per-instance clothing | ✅ Implemented |
| **Animation states** | Sprite frames | 9 GPU compute states (Idle, Walking, Looking, Checking, Aiming, Crouching, Flinching, Falling, Down) | ✅ Implemented (Crouching needs tuning) |
| **Per-instance appearance** | None | Unique outfits per instance via material remap buffer — 1 draw call | ✅ Implemented |
| **Character spawning** | Single "Vinny" debug character | Consolidated hierarchy: Civilians/Civilian_01 + Civilian_02 | ✅ Implemented |
| **Debug control** | None | Debug HUD with character selector (hotkey routing) + clothing instance selector | ✅ Implemented |
| **Portrait generation** | 5-layer compositor, seed-based | Preserved from original analysis | 📝 Planned |

**Design doc reference**: `CHARACTER_SYSTEM.md`, `COMBAT_VEHICLE_DESIGN.md` §5

---

## 9. Combat Differentiations

### 9.1 Firearm Combat Resolution — DIVERGED

The original resolved all combat as a **single instant dice roll**: plug skills + range into the formula, get hit/miss, lookup damage, done. No rounds, no cover, no movement, no environment, no tactical decisions. It was effectively a spreadsheet calculation.

Steel City **diverges** by wrapping the original formula inside a **round-based tactical simulation with physical projectiles**:

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Hit formula** | `((Atk+1)/(Def+1)) × RangeFactor` | **Preserved** as base skill check — but modified by cone of fire, cover geometry, movement | ✅ Data extracted |
| **Damage resolution** | `Range(locations) × 8 + rand(0-7)` → lookup | **Preserved** — same damage table, 4 wound states | ✅ Data extracted |
| **Resolution method** | Single instant math roll | Round-based simulation with physical projectiles, cover, movement, morale | 📝 Designed |
| **Cone of fire** | Not modeled (binary hit/miss) | Weapon-specific cone of fire — bullet direction = aim + perturbation. Skill tightens cone, movement widens it. Missed shots continue as stray projectiles | 📝 Designed |
| **Era-appropriate inaccuracy** | Not modeled | 1920s firearms are inherently inaccurate — revolvers wild past 15yd, Tommy gun = area saturation not marksmanship, shotgun = spread pattern | 📝 Designed |
| **Player role** | Assign orders, see results | **Preserved** — manager, not fighter. Before/after, not during | ✅ Design locked |

**Key divergence**: The original *calculated* combat. Steel City *simulates* it. The formula is the same but the resolution method is fundamentally different — projectiles travel, cover blocks physically, stray bullets create collateral mayhem.

**Design doc reference**: `COMBAT_AUTOBATTLE.md`, `GANGSTERS_GAME_DATA.md` (Hit Table, Damage Table, Cart)

### 9.1b Melee / CQB Combat Resolution — UNDER REVIEW

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Hit formula** | Same formula as firearms (skill comparison + range factor) | **Likely preserved** — same skill-vs-skill comparison | 🔍 Under review |
| **Resolution method** | Single instant math roll | **Hybrid approach proposed** — formula preserved + environment/INT modifiers, but still instant per exchange (no projectiles) | 🔍 Under review |
| **Tactical layer** | None | INT governs closing distance, flanking, multiple-attacker dynamics | 🔍 Under review |
| **Environment** | Not modeled | Tight alley favors shorter-reach fighter, multiple attackers create flanking penalties | 🔍 Under review |
| **Physical simulation** | N/A | No projectile equivalent — spatial hash instance-to-instance distance check for melee range, then formula resolution | 🔍 Under review |

**Design rationale**: Melee is inherently simpler — shorter range, fewer variables, faster resolution. A bar fight being a quick dice roll has charm. But environment matters enormously (phone booth vs open park). Proposed hybrid: preserve the formula, add INT/environment tactical layer, but keep instant resolution per exchange (no travel time, no stray punches hitting bystanders).

**Design doc reference**: `COMBAT_AUTOBATTLE.md`, `GANGSTERS_GAME_DATA.md` (Cart — melee attack types)

### 9.2 Cover as Physical Blocker (NEW — Not in Original)

Cover in Steel City is **not a stat modifier** — it is a **physical ray intersection** against `VoxelCollisionWorld`. A projectile's trajectory is checked against solid voxels along its path. If cover geometry intersects the bullet's ray, the bullet hits the cover, not the target.

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Cover mechanic** | Not modeled | Physical ray intersection — bullet path vs voxel geometry | 📝 Designed |
| **Full wall** | N/A | Bullet stopped completely — target unreachable from that angle | 📝 Designed |
| **Half wall** | N/A | Bullet stopped if trajectory intersects — crouching hood has smaller exposed profile | 📝 Designed |
| **Penetration** | N/A | Material-dependent — brick stops all, wood may let some through, car door stops pistol but not rifle | 📝 Designed |
| **Coverage ratio** | N/A | Percentage of hood's bounding volume occluded by cover from attacker's angle. High-INT hoods actively maximize this | 📝 Designed |

**Key principle**: Cover effectiveness is **emergent from geometry**, not a dice roll. A hood behind a full wall is simply unreachable. A hood behind a half-wall is reachable from certain angles. The spatial hash + projectile system already has `VoxelCollisionWorld.ProbeGround` — cover extends this to check the full ray path, not just the endpoint.

### 9.2b Environment Modifiers — DEFERRED

| Factor | Effect | Status |
|--------|--------|--------|
| **Time of day** | Night reduces visibility → lower hit chances, higher stealth | ⏸️ Deferred |
| **Civilian presence** | Collateral damage risk → increases squeal if civilians hurt | ⏸️ Deferred (collateral still happens via stray projectiles, just no squeal modifier yet) |
| **Open street** | No cover → raw skill matters more | ⏸️ Deferred (emergent from cover geometry — no modifier needed) |
| **Indoors** | Close range → shotguns devastating, rifles cramped | ⏸️ Deferred |

**Note**: Cover (geometry-based) stays in scope since it's a physical projectile interaction, not a stat modifier. These stat-based environment modifiers are deferred to a future pass.

**Design doc reference**: `COMBAT_AUTOBATTLE.md`, `COMBAT_VEHICLE_DESIGN.md` §3 — Cover System

### 9.3 Intelligence as Adaptive Cover AI (NEW — Not in Original)

The original had Intelligence (8-bit, 0-255) but used it only for order execution quality. Steel City makes INT govern **in-combat tactical decisions**, with graduated behavior:

| INT Level | Cover Behavior | Tactical Behavior |
|-----------|---------------|-------------------|
| High (180+) | Seeks cover proactively, crouches for better coverage ratio, aligns body to minimize exposed surface area, repositions when flanked | Prioritizes biggest threat, knows when to retreat |
| Medium (90-180) | Takes cover if obvious nearby | Fires at nearest target, may retreat if badly hurt |
| Low (<90) | Stands in open, maybe finds cover after getting hit | Fires wildly, doesn't recognize when outmatched |
| Very Low (<45) | No cover-seeking behavior | May panic, freeze, or flee immediately |

**Adaptive cover details**:
- **Coverage ratio**: Percentage of hood's bounding volume occluded by cover geometry from attacker's angle. High-INT hoods actively maximize this by adjusting position and stance.
- **Crouch timing**: High-INT hoods crouch sooner when under fire, presenting smaller target profile.
- **Alignment**: High-INT hoods align their body axis with cover to minimize exposed surface area from known threat angles.
- **Repositioning**: High-INT hoods recognize when their cover is flanked and move to new cover.

**Key design principle**: A smart hood with mediocre gun skills can beat a dumb hood with great gun skills — because the smart one took cover and maximized their coverage ratio.

**Design doc reference**: `COMBAT_AUTOBATTLE.md` — "Intelligence as Tactical AI"

### 9.4 Nerve as Morale Stat (NEW — Not in Original)

The original game has **Nerve** as a hood stat but did not use it for combat morale. Steel City repurposes Nerve as the **morale/will-to-fight** stat in combat:

| Trigger | Effect | Status |
|---------|--------|--------|
| Ally goes down | Nerve check (modified by Loyalty) | 📝 Designed |
| Low Nerve | Panic, freeze, or flee | 📝 Designed |
| High Nerve | Cool under fire, small accuracy bonus, fights harder | 📝 Designed |
| Outnumbered 3:1 | Nerve penalty | 📝 Designed |
| Outgunned (pistols vs tommy guns) | Nerve penalty | 📝 Designed |
| Low Nerve + high Loyalty | May still hold position for the boss — loyalty overrides self-preservation | 📝 Designed |
| Low Nerve + low Loyalty | Breaks immediately when pressured | 📝 Designed |

**Stat reuse**: Nerve already exists in the original hood data. No new stat invented — existing stat gets a new combat role, same as Intelligence getting tactical AI.

**Design doc reference**: `COMBAT_AUTOBATTLE.md` — Morale section

### 9.5 Physical Projectiles & Cone of Fire (NEW — Not in Original)

The original used pure math resolution — no projectile entity, no travel time, no stray bullets. Steel City adds **physical projectiles** via spatial hash, with a **cone of fire** model that reflects era-appropriate weapon inaccuracy:

#### Cone of Fire Model

Instead of binary hit/miss, each shot spawns a projectile with direction = aim direction + random perturbation:

```
Projectile direction = aim_direction + RandomWithinCone(half_angle)
half_angle = weapon_base_inaccuracy - (skill_factor × skill_reduction) + (movement_penalty)
```

- **Skill tightens the cone** — a skilled shooter's bullets go closer to where they aimed
- **Movement widens the cone** — firing while running is wild
- **Weapon base inaccuracy** — era-appropriate values below

The bullet goes *somewhere* — just maybe not where aimed. Missed shots naturally become stray bullets that hit civilians, buildings, other hoods. **No separate "miss" calculation needed** — the projectile simply misses the target and continues until it hits *something*.

#### Era-Appropriate Weapon Inaccuracy (1920s)

| Weapon | Base Inaccuracy | Notes |
|--------|----------------|-------|
| Revolver | High | Short barrel, heavy trigger, no real sights → wild past 15 yards |
| Colt 1911 | Medium | Better design but still not precision by modern standards |
| Tommy Gun | High (full auto) | High fire rate + heavy recoil = area saturation, not marksmanship. Cone widens with sustained fire |
| Shotgun | Spread pattern | Devastating close range, spread naturally creates cone, useless at distance |
| Rifle | Low | Most accurate of the era — but slow rate of fire |

**Design driver**: 1920s firearms were genuinely terrible for accuracy. This makes firefights inherently messy and collateral-heavy — exactly the gangster-induced mayhem we want. A skilled shooter with a 1911 at 30 yards still can't guarantee a hit — the cone is just tighter.

#### Projectile Features

| Feature | Original | Steel City |
|---------|----------|------------|
| **Bullet travel** | Instant (math) | Physical projectile with position, velocity, TTL |
| **Stray bullets** | Impossible | Cone of fire → bullets miss target, continue until they hit something (civilian, building, car) |
| **Crossfire** | Impossible | Peds scatter, cars flee, emergent chaos from stray rounds |
| **Suppression** | Not possible | Travel time allows suppression, dodging, leading shots |
| **Drive-by sprays** | Abstract | Actually spray a street — multiple projectiles in a cone, bullets hit whoever's in the path |
| **Building hits** | Not modeled | Bullet holes on walls, atmosphere |
| **Cover interaction** | Not modeled | Ray intersection against VoxelCollisionWorld — cover physically blocks projectiles |
| **Implementation** | N/A | Pure CPU spatial hash — no Unity physics, scales to 500+ instances |

**Design doc reference**: `COMBAT_VEHICLE_DESIGN.md` §13 — Physical Projectiles & Spatial Hash

### 9.6 Cover System (NEW — Not in Original)

| Approach | Description | Status |
|----------|-------------|--------|
| **Approach A: Cover Points** | Scan collision world for wall-adjacent positions. Cache at load (invariant). Zero rendering impact. | 📝 Designed (recommended first) |
| **Approach B: Dynamic Cover Objects** | Physical props (cars, barrels, crates) as Tier 2 instanced, destructible | 📝 Designed (Phase 2) |

Cover points are derived from existing `VoxelCollisionWorld` flat array — no new rendering objects needed. This is an invariant computation (building geometry doesn't change during gameplay).

**Design doc reference**: `COMBAT_VEHICLE_DESIGN.md` §3 — Cover System

### 9.7 Combat AI State Machine (NEW — Not in Original)

The original had combat cases in SIM_TICK (0xB approach, 0xC engagement with 8 substates) but no visible tactical behavior. Steel City adds:

```
IDLE → ALERT (enemy detected) → MOVING_TO_COVER → IN_COVER (peek + fire)
  → RELOCATING (cover destroyed/flanked) → IN_COVER
  → FLINCH (hit) → IN_COVER (if alive)
  → FALLING → DOWN (dead)
  → IDLE (no enemies)
```

**Per-instance combat data** (backward-compatible extension to instance buffer):
- `animState` (enum: includes combat states)
- `faction` (for targeting)
- `health` (0-1, for hit reactions)
- Total: 32 bytes per instance (was 16)

**Design doc reference**: `COMBAT_VEHICLE_DESIGN.md` §4 — Combat AI States

### 9.8 Vehicle Combat & Car Chases (NEW — Not in Original)

The original had `COMBAT_3` (drive-by shooting) limited to ordered combat. Steel City adds **emergent car chases**:

| Feature | Original | Steel City |
|---------|----------|------------|
| **Vehicle combat** | Ordered only (COMBAT_3) | Emergent — rival vehicles trigger combat via proximity + fear/hostility check |
| **Car chases** | Not modeled | Full chase state machine (pursuit, flee, intercept, resolution) |
| **Vehicle physics** | Abstract (tick count) | Tier A (sphere) for traffic, Tier B (raycast suspension) for combat vehicles |
| **Crashes** | Not modeled | PhysX collision, damage states, flipping, vehicle disable |
| **Bail out** | Not modeled | Disabled vehicle → hoods exit on foot → combat transitions to ranged |
| **Bystander casualties** | Not modeled | Stray bullets + car impacts → police escalation, witness events |
| **Police join chases** | Not modeled | Noise events → police spawn → 3-way chases |

**Design doc reference**: `COMBAT_VEHICLE_DESIGN.md` §4A, §6, §7, `ENGINE_INTEGRATION_PLAN.md` §4A

### 9.9 Police Escalation (NEW — Not in Original)

| Feature | Original | Steel City |
|---------|----------|------------|
| **Gunfire noise** | Not modeled | NoiseEvent with radius — any AI within range reacts |
| **Escalation ladder** | Not documented | Gunfight → police dispatch → 3-way combat → roadblocks → city-wide panic |
| **Police AI states** | Not documented | Patrol → Respond → Engage → Search → Patrol |
| **Roadblocks** | Not modeled | Static objects placed on road, mutates collision world, causes crashes |

**Design doc reference**: `COMBAT_VEHICLE_DESIGN.md` §9 — Police Escalation System

### 9.10 Combat Animation (NEW — Not in Original)

| Animation | Technique | Status |
|-----------|-----------|--------|
| **Firing recoil** | Procedural shader (pulse on fireTime) | 📝 Designed |
| **Hit flinch** | Procedural shader (exp decay from hitTime) | 📝 Designed |
| **Falling/death** | Voxel swap (2-3 poses) or procedural rotation | 📝 Designed |
| **Cover crouch** | Morph target blend (standing ↔ crouching) | 📝 Designed |
| **Aiming arms** | Voxel group transforms (arm raises toward target) | 📝 Designed |
| **Head tracking** | Procedural yaw offset on head group | ✅ Implemented (Looking state) |

**Design doc reference**: `COMBAT_VEHICLE_DESIGN.md` §5 — NPC Animation Without Skeletons

---

## 10. Technical Architecture Differentiations

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Engine** | Custom C++ (Win32) | Unity 6 (C#) | ✅ Implemented |
| **Rendering** | DirectDraw sprites | Custom GPU raymarch pipeline (ComputeBuffer + DrawMeshInstanced) | ✅ Implemented |
| **Animation** | Sprite frame lookup | GPU compute shader (CharacterPoseCompute.compute) | ✅ Implemented |
| **Collision** | Tile-based | Flat byte array (VoxelCollisionWorld) — O(1) lookups | ✅ Implemented |
| **Terrain** | Per-tile rendering | Sector baking — 100 chunks → 1 sector (1 draw call) | ✅ Implemented |
| **Instancing** | None | GPU instancing with per-instance data (position, yaw, anim, clothing) | ✅ Implemented |
| **Data format** | .xtx (XOR-encoded text) | JSON / ScriptableObjects | ✅ Implemented |
| **Time system** | 12000-tick weekly budget | Preserved — same tick budget concept | 📝 Planned |
| **SIM_TICK** | 16,980-byte monolithic function | Split into per-state classes (SimulationManager) | 📝 Planned |
| **Pathfinding** | Waypoint graph with countdown timer | A* on WaypointGraph (Pathfinder.cs) | ✅ Implemented |

---

## 11. Tools & Pipeline Differentiations

| Feature | Original | Steel City | Status |
|---------|----------|------------|--------|
| **Asset creation** | Internal tools (lost) | VoxelAssetStudio — voxel editor, character pipeline, animator, city editor | ✅ Implemented |
| **Building generation** | Manual | Python procedural generation scripts → .stasset files | ✅ Implemented |
| **City layout** | Hardcoded | city_editor.html — 3-phase visual pipeline (Macro → Granular → Buildings) | ✅ Implemented |
| **Animation authoring** | Internal | character_animator.html — visual keyframe editor, exports .anim.json | ✅ Implemented |
| **Inspection** | None | sc_inspector.py — 10-check voxel model validation | ✅ Implemented |
| **RE tooling** | N/A | Ghidra scripts (23 custom scripts) for binary analysis | ✅ Complete |

---

## 12. What We Explicitly Preserve

These are **not** differentiations — they are the core of the original that Steel City keeps intact:

- Two-phase game loop (Gang Organizer → Working Week → Results)
- Data-driven game balance (all values in external files)
- Fear/Hostility/Squeal citizen metric system
- Dual NPC/gang system (citizens use F/H/S, gang members use skills/loyalty)
- Crime escalation ladder (extort → intimidate → assault → torch → bomb)
- 18 weighted character archetypes
- Hit/damage formula and weapon tables
- Market share diminishing returns
- Business suspicion matrix
- 12000-tick weekly budget
- Order types (Goto, Guard, Extort, Collect, Kill, Bomb, etc.)
- Vehicle walk/drive decision logic (distance threshold + 25% random for combat)
- FBI suspicion system ($5000 threshold)
- Bribe economy
- Recruitment system

---

## 13. What We Explicitly Don't Do

- **No real-time tactical combat** (no XCOM-style encounters)
- **No scripted narrative** (no story missions, no cutscenes, no dialogue trees)
- **No player-controlled combat** (manager, not action hero)
- **No microtransactions / live service / multiplayer** (initially)
- **No business management simulation** (mafia tycoon, not city builder)

---

## 14. Combat Implementation Priority

Based on existing design docs and current codebase state:

| Phase | Feature | Effort | Dependencies |
|-------|---------|--------|--------------|
| **1** | Finish combat animations (Flinch, Falling, firing recoil) | ~6 hrs | Existing CSPose kernel |
| **2** | Combat data model (weapons, damage, per-instance combat state) | ~8 hrs | Data from GANGSTERS_GAME_DATA.md |
| **3** | Cover point scanner (collision world query) | ~4 hrs | VoxelCollisionWorld |
| **4** | Combat AI state machine + round-based resolution | ~12 hrs | Phases 1-3 |
| **5** | Physical projectiles + spatial hash | ~13 hrs | Phase 4 |
| **6** | Visual feedback (tracers, muzzle flash, hit reactions) | ~6 hrs | Phases 4-5 |
| **7** | Vehicle physics (Tier A + Tier B) | ~32 hrs | Independent of 1-6 |
| **8** | Vehicle combat + car chases | ~25 hrs | Phases 5, 7 |
| **9** | Police escalation + noise events | ~24 hrs | Phases 5, 8 |
| **10** | Polish (cover props, vehicle fire glow, head tracking) | ~4 days | After all above |

**Design doc reference**: `COMBAT_VEHICLE_DESIGN.md` §12 — Implementation Priority & Phasing

---

## Related Documents

- `docs/core/DESIGN_PHILOSOPHY.md` — Foundational principles (preserve/polish/don't-do)
- `docs/core/SOURCE_GAME_ANALYSIS.md` — Original game analysis with preserve-vs-polish table
- `docs/core/GANGSTERS_GAME_DATA.md` — All decoded combat data (Hit Table, Damage Table, Cart, Crime)
- `docs/core/ENGINE_INTEGRATION_PLAN.md` — RE-to-implementation mapping, combat state machine design
- `docs/core/REVERSE_ENGINEERING_FINDINGS.md` — Binary analysis (SIM_TICK combat cases, 4 combat variants)
- `docs/systems/COMBAT_AUTOBATTLE.md` — Auto-resolved combat design (INT as tactical AI, environment, morale)
- `docs/systems/COMBAT_VEHICLE_DESIGN.md` — Full combat + vehicle design (cover, projectiles, chases, police)
- `docs/core/MAFIA_TYCOON_DESIGN_PRINCIPLE.md` — Economy-as-weapon guardrail
