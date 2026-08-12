# 📚 Steel City: Mob Sim — Documentation Index

**Purpose**: Central hub for all Mob Sim project documentation
**Last Updated**: August 12, 2026
**Project**: Steel City — Mob Sim (Unity)

---

## 🎯 Quick Navigation

| Category | Documents | Status |
|----------|-----------|--------|
| **Game Design** | 1 doc | ✅ Complete |
| **UI System** | 3 docs | ✅ Complete |
| **Voxel Buildings** | 3 docs | ✅ Complete |
| **Voxel Editor** | 1 doc | ✅ Complete |
| **Voxel Rendering** | 4 docs | ✅ Complete |
| **Rendering Systems** | 4 docs | ✅ Complete |
| **Character Animation** | 1 doc | 🔄 Phase 1 Complete |
| **Lighting Debug** | 1 doc | ✅ Complete |
| **Scale Standard** | 1 doc | ⚠️ SEE MASTER DOC |
| **Inspection Toolchain** | 1 doc | ✅ Complete |
| **Asset Pipeline** | 1 doc | ✅ Complete |
| **Porting Notes** | 1 doc | ✅ Complete |

---

## 📁 Documentation Categories

### 1. Game Design ✅ COMPLETE

- **`GAME_DESIGN_SKELETON.md`** — Core game design document
  - Game mechanics, phases, and systems overview
  - Gang organization, hoods, blocks, and economy
  - Police, investigations, and event systems

**Keywords**: game design, mechanics, gangs, economy, police

---

### 2. UI System ✅ COMPLETE

- **`UI_SETUP_GUIDE.md`** — Unity UI setup instructions
  - Canvas configuration
  - Camera viewport setup
  - Component wiring

- **`UI_TABBED_LAYOUT.md`** — Tabbed info panel system
  - Tab bar architecture
  - Page switching logic
  - Layout groups and anchoring

- **`UI_LAYOUT_GOTCHAS.md`** — Common UI pitfalls and fixes
  - Layout issues and solutions
  - Canvas scaler settings
  - Viewport overlap problems

**Keywords**: UI, canvas, tabs, layout, viewport, camera

---

### 3. Voxel Buildings ✅ COMPLETE

- **`BUILDING_PROTRUSION_SYSTEM.md`** — Exterior feature protrusion for voxel buildings
  - Voxel grid padding technique (2-voxel air gap at front)
  - Protruding awnings, barber poles, columns
  - The `+1` bug history and fix
  - Step-by-step guide for adding protrusion to new buildings
  - Buildings: Barbershop, Bakery, Police Station

- **`VOXEL_BUILDING_METHODOLOGY.md`** — Voxel building generation methodology
  - Building generation pipeline
  - Material assignments
  - Mesh conversion process

- **`VOXEL_ORDERING_FIX.md`** — Voxel data ordering fix
  - Fortran vs C ordering
  - X-major voxel reading
  - StAssetReader parsing fix

**Keywords**: voxel, building, protrusion, awning, mesh, ordering, stasset

---

### 3b. Voxel Editor ✅ COMPLETE

- **`VOXEL_EDITOR_AND_FIRE_ESCAPE.md`** — Browser-based voxel editor and fire escape design workflow
  - Voxel editor HTML system (Three.js + InstancedMesh + raycasting)
  - Tool set: paint, erase, box, line, select, extrude, ruler, camera
  - Enhanced selection: flood-fill, Shift+click single, Ctrl+click box
  - Escape key universal reset
  - Volume expansion (dynamic grid resizing)
  - Y-slice layer controls
  - Fire escape historical context (1860 NYC ordinance, 1901 Tenement House Act)
  - Design-to-bolt-on workflow: hand-design → alignment correction → buffer expansion → bolt-on
  - Game map constraints (96×96 footprint, BuildingVoxelWidth=32)
  - Scripts reference: shift_fe.py, fix_fe_spacing.py, bolt_fe_v2.py, analyze_fe.py

**Keywords**: voxel editor, HTML, Three.js, fire escape, tenement, buffer, alignment, drop ladder, roof ladder

---

### 4. Voxel Rendering ✅ COMPLETE

- **`VOXEL_LIGHTING_AND_SHADOWS.md`** — Voxel raymarch lighting pipeline
  - Shader uniforms and lighting model
  - Smooth normals (gradient-based) to eliminate face-normal jitter
  - **Hybrid normals (Option B)**: DDA face normal for top/bottom surfaces (uniform flat ground), SmoothNormal blend for side faces (soft wall shading)
  - Half-Lambert wrap lighting for soft transitions
  - Soft shadow penumbra (perpendicular distance-based)
  - Self-shadowing fix (normal-offset origin + skip steps)
  - Shadow ambient composition
  - **Debug controls**: shadow enable toggle, normal nudge, light nudge, skip steps, max steps
  - **Lighting component toggles**: sun light, ambient, fill light, camera light (independently toggleable)

- **`VOXEL_BLEED_THROUGH_FIX.md`** — Multi-chunk depth buffer fix
  - Root cause: `tMax = sideDist` (volume-relative) instead of `tStart + sideDist` (camera-relative)
  - Why buildings looked correct but terrain didn't
  - One-line fix and verification steps

- **`VOXEL_SUN_DAY_NIGHT.md`** — Dynamic sun and day/night cycle
  - VoxelSun component architecture
  - Sun position calculation from timeOfDay
  - Lighting presets (dawn/noon/dusk/night) and blending
  - Critical design decisions (transform, Start vs Awake, timeOfDay default)

- **`VOXEL_CAMERA_SYSTEM.md`** — Mouse camera controls
  - LMB focus, MMB rotate, RMB pan, wheel zoom
  - Smooth rotator (LerpAngle interpolation)
  - Orbit position calculation from yaw/pitch
  - Voxel size hidden from user UI

**Keywords**: lighting, shadow, sun, day/night, bleed-through, depth, camera, mouse, orbit

---

### 4b. Rendering Systems ✅ COMPLETE

- **`docs/systems/INSTANCING_AND_BUFFERING.md`** — GPU instancing and ComputeBuffer deep dive
  - How instanced character/vehicle rendering works (step by step)
  - ComputeBuffer lifecycle: creation, upload, release
  - MaterialPropertyBlock: why per-draw isolation is critical
  - The shared-material overwrite bug (root cause analysis of invisible Vinny)
  - Sector baking vs instanced characters: same pattern, different scale
  - **Terrain sector baking** (Aug 9) — 100 chunks → 1 sector, same RegisterSector API
  - **Collision world flat-array optimization** (Aug 9) — Dictionary→byte[], 211x faster
  - **Measured performance** — 78s→371ms terrain load, 12 total draw calls, 13.3MB collision memory
  - Shader-side instancing: how vertex/fragment shaders read per-instance data
  - Performance characteristics, debugging guide, glossary
  - **Key lesson**: any time multiple draw calls share a material but need different buffer bindings, use MaterialPropertyBlock

- **`docs/systems/INSTANCING_AND_BUFFERING_VISUAL.html`** — Visual companion guide (HTML)
  - Interactive visual diagrams for draw calls, buffers, instancing pipeline
  - The MaterialPropertyBlock bug visualized (broken vs fixed)
  - Rendering tiers diagram (Bake / Instance / Individual)
  - Full flow: city generation → terrain baking → building baking → instancing → screen
  - **Measured performance table** (Aug 9) — before/after comparison with improvement factors

- **`docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`** — Sector baking architecture and GPU-driven indirect rendering proposal
  - Current sector baking implementation (Tier 1 static buildings)
  - Known gaps: no LOD, no depth sort, 1023-instance cap
  - Proposed GPU-driven indirect rendering evolution

- **`docs/systems/GPU_DRIVEN_RENDERING_PLAN.md`** — Iterative 6-phase plan for GPU-driven rendering (Aug 9)
  - Phase 1: Static sector TRS cache (stop rebuilding 984 matrices/frame)
  - Phase 2: Buffer pooling + ComputeBufferMode.SubUpdates (zero GC allocs)
  - Phase 3: DrawMeshInstancedIndirect (remove 1023 instance cap)
  - Phase 4: Compute shader frustum culling (GPU-side, no CPU readback)
  - Phase 5: GPU-side LOD (compute shader assigns LOD tier per instance)
  - Phase 6: Scale test with 500-1000 block cities
  - Each phase independently testable with rollback instructions

- **`docs/systems/INVARIANT_COMPUTATION_PRINCIPLE.md`** — "Do Once, Not Repeat Constantly" optimization principle (Aug 9)
  - Case studies: TRS matrix cache, collision world flat array
  - 6 pending candidates identified during audit (sector bounds, propblock clear, group size, chunk AABB, corners array, instanced arrays)
  - Audit checklist for code review + anti-patterns to watch for
  - Profiling tips using HUD frame time metrics

- **`docs/systems/OPTIMIZATION_VISUAL.html`** — Visual guide to TRS cache + collision world optimizations (Aug 9)
  - Interactive HTML with diagrams, flowcharts, before/after comparisons
  - Covers TRS matrix structure, dictionary vs flat array, grid expansion bug
  - Frame time vs FPS measurement guide

- **`docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`** — Rendering strategy classification
  - Three tiers: bake (static), instance (batched dynamic), individual (unique mutation)
  - Decision checklist for new dynamic objects
  - Worked examples and anti-patterns

- **`docs/systems/PATH_DEBUG_RENDERING.md`** — Instanced box-beam debug path rendering
  - CommandBuffer-based instanced rendering for path visualization
  - Per-type batching with MaterialPropertyBlock color isolation
  - Composited into voxel render texture

- **`docs/systems/COMBAT_VEHICLE_DESIGN.md`** — Street combat, vehicle chase, physics & NPC animation design (Aug 9)
  - Cover system: Approach A (collision world query) vs Approach B (dynamic cover props)
  - Vehicle physics: 3 tiers (sphere, raycast suspension, WheelCollider) — recommends sphere for civilians, raycast for combat
  - NPC animation without skeletons: procedural shader, voxel swap, voxel groups, morph targets
  - Vehicle combat: Tier 2→3→1 promotion, firing from vehicles, crash/flip/disable
  - Traffic swarm: bumper system, flee vectors, formation following, stuck detection
  - Police escalation: noise/threat system, escalation ladder, roadblocks
  - 7-phase implementation plan with dependencies (~150 hrs total)

- **`docs/systems/COMBAT_VISUAL.html`** — Visual guide to combat & vehicle design concepts (Aug 9)
  - Animated CSS diagrams: hood AI state machine, vehicle chase flow, police escalation ladder
  - NPC animation demos (walking bob, recoil, flinch, falling) with CSS keyframe animations
  - Vehicle physics tier comparison (sphere vs raycast vs WheelCollider)
  - Tier 2→3→1 promotion diagram for vehicles
  - 7-phase implementation timeline with dependency graph
  - Companion to `COMBAT_VEHICLE_DESIGN.md`

- **`docs/systems/VOXEL_GROUP_ANIMATION.md`** — Articulated limb animation without skeletons (Aug 9)
  - Voxel group system: tag voxels with groupID (head, torso, arms, legs) for per-limb transforms
  - Gangsters-inspired design: walking with limb swing, random head look-around, hood coast-clear checks
  - Current architecture analysis: `VoxelProxyRaymarch.shader`, `VoxelChunkManager.cs`, instance buffer format
  - 6-step implementation plan (~11-20 hrs): tag voxels → expand buffer → upload groupIDs → shader transforms → C# driver → AI behavior
  - Animation parameters: walk cycle, head look-around, aim pose, crouch
  - Testing plan (6 phases), risks & mitigations, phase integration with combat design
  - Companion to `COMBAT_VEHICLE_DESIGN.md` (§5) and `COMBAT_VISUAL.html` (Technique 3)

- **`docs/systems/CHARACTER_ANIMATION_PIPELINE.md`** — Unified animation & physics pipeline (Aug 10, updated Aug 12)
  - **Phase 1 COMPLETE**: GPU compute forward-transform pipeline operational
  - Architecture: `CharacterPoseCompute.compute` with CSClear + CSPose kernels → forward-transform rest voxels into posed space
  - `VoxelProxyRaymarch.shader` samples posed buffer directly (no inverse-transform)
  - `VoxelChunkManager.cs` dispatches compute per frame, manages per-instance posed voxel buffers
  - Data flow: animator export JSON → Unity compute shader buffers (pivots, keyframes, axis/sign, leg twist, body bob)
  - Keyframe interpolation: Catmull-Rom spline in HLSL via `GetWalkPoseValue()`, structured buffers
  - Full FK chain: group rotation → parent rotation → pivot offset → body bob/weight shift
  - Verified states: T-Pose, Idle, Walking, Looking, Aiming ✅ | Crouching 🐛 needs tuning
  - Ragdoll design: invisible proxy bones with ConfigurableJoints, per-group capsule colliders, VoxelCollisionWorld probing
  - Steel Tide port mapping: VoxelActor2Joints (direct), VoxelActor2LimbDrive (direct), VoxelActor2Ground (adapt)
  - Flinch system: one-shot keyframe sequences by hit location/force, blend to ragdoll on high force
  - 5 implementation phases: ✅ shader port → 🔲 angular limits → 🔲 flinch → 🔲 ragdoll → 🔲 integration
  - Performance: normal NPCs = zero physics (GPU compute only), combat participants = ~10 Rigidbodies each

**Keywords**: instancing, ComputeBuffer, MaterialPropertyBlock, DrawMeshInstanced, DrawMeshInstancedIndirect, proxy cube, raymarch, buffer binding, sector baking, rendering tiers, GPU-driven, compute shader, frustum culling, LOD, terrain baking, collision world, combat, cover system, vehicle physics, raycast suspension, NPC animation, procedural animation, voxel groups, police escalation, traffic swarm, tier promotion, articulated limbs, groupID, pivot points, walk cycle, head tracking, Gangsters, keyframe, Catmull-Rom, spline, ragdoll, flinch, ConfigurableJoint, proxy bones, physics

---

### 5. Scale Standard ⚠️ SEE MASTER DOC

- **`MODEL_DESIGN_STANDARD.md`** — 🔒 MASTER REFERENCE — source of truth for scale, doors, orientation, proportions
  - NPC ("Vinny") as the scale root, door-to-NPC-height ratio test (1.25×+)
  - Corrected door standard (supersedes the table below)
  - Orientation convention per model type (buildings=Z0 front, vehicles=+Z front, characters=low-Z front)
  - Proportion reference table, per-model audit (which buildings are certified vs need rework)
- **`MOB_SIM_SCALE_STANDARD.md`** — Mob Sim universe scale system (⚠️ door table outdated, see master doc above)
  - Core scale constants (building voxel = 0.1m, char voxel = 0.02m, vehicle voxel = 0.05m)
  - Scale ratio: 3.75× (real world → mob sim)
  - Reference object library (NPC, door, trash can, bench, street light, car, dumpster, tree)
  - Building height reference table
  - Relationship to Steel Tide FPS scale (independent systems)

**Keywords**: scale, proportions, doors, reference, NPC size, voxel size

---

### 5b. Inspection Toolchain ✅ COMPLETE

- **`VOXEL_INSPECTION_TOOLCHAIN.md`** — Operational guide for all voxel model inspection and diagnostic tools
  - Unified inspector (`sc_inspector.py`): 10 quality checks in one pass (dimensions, materials, scale, door height, orientation, symmetry, proportions, wall closure, internal holes, ASCII views)
  - Legacy CLI tools: `hexdump_stasset.py`, `diagnose_stasset.py`, `debug_dimensions.py`, `inspect_city_materials.py`, `toolbox/stasset_inspector.py`
  - Voxel Asset Studio GUI editor usage
  - Model audit workflow (6-step process: generate → inspect → cross-section → cross-check → Unity test → update audit)
  - Common issues and how to spot them (open doors, short doors, asymmetry, missing walls, wrong orientation)
  - `.stasset` binary format reference
  - Baseline results for all certified models (Aug 8, 2026)
  - Material symbol legend for ASCII views

**Keywords**: inspection, diagnostic, audit, symmetry, door height, wall closure, sc_inspector, stasset, hexdump, material histogram

---

### 6. Voxel Asset Pipeline ✅ COMPLETE

- **`VOXEL_ASSET_PIPELINE.md`** — End-to-end workflow for creating, reviewing, and deploying voxel models
  - 5-step pipeline: Create → Review (VS) → Approve → Deploy → Verify
  - Scale standards (buildings 0.1f, characters 0.02f, vehicles 0.05f)
  - Door size standard (8v tall, 4v wide)
  - VoxelAssetStudio review workflow with checklist
  - Analysis tool usage (`analyze_building.py`)
  - Scale reference generation (`gen_scale_reference.py`)
  - Common workflows: door size changes, new buildings, hand-editing

**Keywords**: pipeline, workflow, review, approve, deploy, VS, VoxelAssetStudio, scale reference, door standard

---

### 7. Porting Notes ✅ COMPLETE

- **`PORTING_NOTES.md`** — Porting notes and migration history
  - Codebase migration steps
  - Breaking changes
  - Compatibility notes

**Keywords**: porting, migration, compatibility

---

## 🔍 Search by Topic

### Game Mechanics
- Design overview → `GAME_DESIGN_SKELETON.md`

### UI & Canvas
- Setup → `UI_SETUP_GUIDE.md`
- Tabbed layout → `UI_TABBED_LAYOUT.md`
- Common issues → `UI_LAYOUT_GOTCHAS.md`

### Voxel Buildings
- Protrusion system → `BUILDING_PROTRUSION_SYSTEM.md`
- Generation methodology → `VOXEL_BUILDING_METHODOLOGY.md`
- Voxel ordering fix → `VOXEL_ORDERING_FIX.md`

### Voxel Editor & Fire Escape
- Editor system & tools → `VOXEL_EDITOR_AND_FIRE_ESCAPE.md` (Section 1)
- Fire escape workflow → `VOXEL_EDITOR_AND_FIRE_ESCAPE.md` (Section 2)
- Game map constraints → `VOXEL_EDITOR_AND_FIRE_ESCAPE.md` (Section 3)

### Voxel Rendering & Lighting
- Lighting pipeline → `VOXEL_LIGHTING_AND_SHADOWS.md`
- Bleed-through fix → `VOXEL_BLEED_THROUGH_FIX.md`
- Sun/day-night cycle → `VOXEL_SUN_DAY_NIGHT.md`
- Camera controls → `VOXEL_CAMERA_SYSTEM.md`

### Scale & Proportions
- Master design standard → `MODEL_DESIGN_STANDARD.md`
- Scale standard (legacy) → `MOB_SIM_SCALE_STANDARD.md`
- Door sizes → `MODEL_DESIGN_STANDARD.md` (Section 3, Door Standard)
- Reference objects → `MOB_SIM_SCALE_STANDARD.md` (Reference Object Library section)

### Inspection & Diagnostics
- Unified inspector → `VOXEL_INSPECTION_TOOLCHAIN.md` (Section 1)
- Model audit workflow → `VOXEL_INSPECTION_TOOLCHAIN.md` (Section 4)
- Common issues → `VOXEL_INSPECTION_TOOLCHAIN.md` (Section 5)
- File format → `VOXEL_INSPECTION_TOOLCHAIN.md` (Section 6)

### Porting
- Migration notes → `PORTING_NOTES.md`

---

## 📊 Project Status Dashboard

### UI System ✅ COMPLETE
- [x] Canvas setup
- [x] Tabbed info panel
- [x] Camera viewport separation
- [x] Compass rose for orientation

### Voxel Buildings ✅ COMPLETE
- [x] Building generation pipeline
- [x] Voxel ordering fix
- [x] Protrusion system (awnings, poles, columns, fire escapes)
- [x] Storefront outward-facing rotation
- [x] Road network generation
- [x] Road labels (flat, readable, all streets)
- [x] Standardized door sizes (4v/5v/6v)

### Scale System ✅ COMPLETE
- [x] Mob Sim scale standard defined (3.75× ratio)
- [x] Reference object library (11 models)
- [x] Door size standardization
- [x] Building height reference table

### City Generation ✅ COMPLETE
- [x] Road-first pipeline (Geography → Roads → Sidewalks → Blocks)
- [x] Per-building outward rotation from block center
- [x] Compass rose for scene orientation

### Voxel Rendering ✅ COMPLETE
- [x] GPU raymarch compute shader pipeline
- [x] Dynamic lighting via VoxelSun (day/night cycle)
- [x] Smooth normals (gradient-based) for consistent shading
- [x] **Hybrid normals (Option B)** — DDA face normal for top/bottom, SmoothNormal blend for sides
- [x] Half-Lambert wrap lighting
- [x] Soft shadow penumbra
- [x] Self-shadowing fix (checkerboard on flat surfaces)
- [x] Multi-chunk depth buffer fix (bleed-through)
- [x] Mouse camera controls (LMB/MMB/RMB/wheel)
- [x] Smooth camera rotator
- [x] **Shadow debug controls** — enable toggle, normal nudge, light nudge, skip/max steps
- [x] **Lighting component toggles** — sun, ambient, fill, camera light independently toggleable
- [x] **Raymarch-only rendering** — all mesh-based rendering removed, raymarch always active
- [x] **Rubble decorations** — scattered stone clusters on empty land plots

### Character Animation 🔄 PHASE 1 COMPLETE
- [x] **GPU compute forward-transform pipeline** — `CharacterPoseCompute.compute` (CSClear + CSPose kernels)
- [x] **Full FK chain** — group rotation → parent rotation → pivot offset → body bob/weight shift
- [x] **Walk keyframes** — Catmull-Rom interpolation in HLSL via `GetWalkPoseValue()`
- [x] **Per-instance posed voxel buffers** — allocated per InstancedGroup, sampled directly by raymarch shader
- [x] **VoxelChunkManager compute dispatch** — CSClear + CSPose per frame, anim data upload
- [x] **VoxelProxyRaymarch posed buffer sampling** — no inverse-transform, per-instance buffer offsets via `instMeta.x`
- [x] **CharacterAnimation.cs** — pushes animState/animTime/animSpeed to InstancedCharacter
- [x] **CharacterRig.cs** — character controller with hotkeys (T/I/W/L/A/C), consolidated onto single GameObject
- [x] Verified: T-Pose, Idle, Walking, Looking, Aiming render correctly on GPU
- [ ] Crouching — needs animator-side tuning
- [ ] Angular limits per joint (Phase 2)
- [ ] Flinch keyframes (Phase 3)
- [ ] Ragdoll proxy bones (Phase 4)
- [ ] Hit event → flinch/ragdoll integration (Phase 5)

---

## 🎯 Common Tasks — Quick Links

### "I need to add protrusion to a building"
`BUILDING_PROTRUSION_SYSTEM.md` (Adding Protrusion section)

### "I need to check door scale"
`MODEL_DESIGN_STANDARD.md` (Section 3, Door Standard) — supersedes `MOB_SIM_SCALE_STANDARD.md`'s door table

### "I need to fix a UI layout issue"
`UI_LAYOUT_GOTCHAS.md`

### "I need to understand voxel building generation"
`VOXEL_BUILDING_METHODOLOGY.md`

### "I need to understand the game design"
`GAME_DESIGN_SKELETON.md`

### "I need to fix voxel data ordering"
`VOXEL_ORDERING_FIX.md`

### "I need to fix voxel bleed-through"
`VOXEL_BLEED_THROUGH_FIX.md`

### "I need to understand voxel lighting"
`VOXEL_LIGHTING_AND_SHADOWS.md`

### "I need to set up the sun/day-night cycle"
`VOXEL_SUN_DAY_NIGHT.md`

### "I need camera controls"
`VOXEL_CAMERA_SYSTEM.md`

### "I need to understand instancing and GPU buffers"
`docs/systems/INSTANCING_AND_BUFFERING.md`

### "I need to understand rendering tiers (when to bake vs instance)"
`docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`

### "I need to understand sector baking"
`docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`

### "I need the GPU-driven rendering optimization plan"
`docs/systems/GPU_DRIVEN_RENDERING_PLAN.md`

### "I need the visual guide to instancing and buffering"
`docs/systems/INSTANCING_AND_BUFFERING_VISUAL.html`

### "I need the optimization principle document (do-once, invariant computation)"
`docs/systems/INVARIANT_COMPUTATION_PRINCIPLE.md`

### "I need the visual guide to TRS cache + collision world optimizations"
`docs/systems/OPTIMIZATION_VISUAL.html`

### "I need to understand path debug rendering"
`docs/systems/PATH_DEBUG_RENDERING.md`

### "I need the combat/vehicle/physics design document"
`docs/systems/COMBAT_VEHICLE_DESIGN.md`

### "I need the visual guide to combat & vehicle concepts"
`docs/systems/COMBAT_VISUAL.html`

### "I need the voxel group animation design document"
`docs/systems/VOXEL_GROUP_ANIMATION.md`

### "I need the character animation & physics pipeline design"
`docs/systems/CHARACTER_ANIMATION_PIPELINE.md`

### "I need to understand the GPU compute animation pipeline"
`docs/systems/CHARACTER_ANIMATION_PIPELINE.md` (Section 1 — GPU Compute Forward-Transform Pipeline, Section 7 — Phase 1)

### "I need to work on character animations in the HTML animator"
`VoxelAssetStudio/character_animator.html` — see `CHARACTER_ANIMATION_PIPELINE.md` Section 2 for data flow

---

## 🔧 For Coding Agents

### Git Repository
```
Repo root:     SteelCityMobSim/ (this directory — has its own .git)
Remote:        https://github.com/proxy420ah-create/Steel_City-Mob_Sim.git
Branch:        master
NOTE:          The parent workspace (Cursor Workshop) is a SEPARATE git repo.
               Always run git commands from inside SteelCityMobSim/, not from the parent.
```

### Key File Locations
```
Unity scripts:  Assets/Scripts/
  UI:           Assets/Scripts/UI/CityMap3D.cs, GameUIController.cs, VoxelChunkManager.cs, VoxelSun.cs
  Sim:          Assets/Scripts/Sim/StAssetReader.cs, VoxelBuildingMeshifier.cs, GameEngine.cs
                CharacterAnimation.cs, VoxelCharacterAnimator.cs, CharacterRig.cs
  Bootstrap:    Assets/Scripts/GameBootstrap.cs
Compute shaders: Assets/Resources/Shaders/MobSimVoxelRaymarch.compute
                 Assets/Resources/Shaders/CharacterPoseCompute.compute
Raymarch shader: Assets/Resources/Shaders/VoxelProxyRaymarch.shader
Voxel studio:   VoxelAssetStudio/
  Buildings:    procedural_mob_buildings.py
  Characters:   procedural_mob_characters.py
  Vehicles:     procedural_mob_vehicles.py
  Materials:    mob_materials.py
  I/O:          stasset_io.py
  Editor:       voxel_editor_html.py
  Animator:     character_animator.html
  Inspector:    sc_inspector.py
  City gen:     generate_city_assets.py
  FE scripts:   shift_fe.py, fix_fe_spacing.py, bolt_fe_v2.py, analyze_fe.py
City layout:    Assets/StreamingAssets/city_layout.json
Voxel assets:   Assets/StreamingAssets/voxel_buildings/
Character assets: Assets/StreamingAssets/voxel_characters/
```

### Key Values
- `voxelSize = 0.1` — world units per voxel (buildings)
- `voxelSize = 0.02` — world units per voxel (characters)
- `roadWidth = 1.6` — world units
- `sidewalkWidth = 1.0` — world units
- `BLOCK_W = 32`, `BLOCK_D = 32` — voxel grid dimensions
- `WALL_T = 2` — wall thickness in voxels
- `PROTRUDE = 2` — protrusion depth in voxels

---

## 📝 Document Maintenance

### When to Update This Index
- New document created → Add to relevant category
- Document renamed → Update all references
- Major feature complete → Update status dashboard
- New category needed → Add new section

### ⚠️ When to Update the Roadmap
**`docs/STEEL_CITY_ROADMAP.html`** must be updated alongside this index whenever:
- A **new design document** is created (add a feature block to the relevant phase)
- A **new system is brainstormed** or discussed (add to timeline or create a phase)
- A **phase changes status** (PLANNED → IN PROGRESS → COMPLETE)
- **Progress percentage** shifts (update the overall bar)
- **New findings** invalidate or change existing roadmap items

The roadmap is the living visual summary of the project. If it's stale, new contributors (and coding agents) get a wrong picture of priorities and progress. **Always update both this index AND the roadmap when creating or significantly updating documentation.**

### Status Indicators
- ✅ COMPLETE — Fully documented, tested, working
- 🔄 IN PROGRESS — Being worked on
- ⚠️ PENDING — Planned but not started
- 🐛 NEEDS FIX — Has known issues

---

**Last Updated**: August 12, 2026
**Version**: 1.6.0
**Maintainer**: Development Team
