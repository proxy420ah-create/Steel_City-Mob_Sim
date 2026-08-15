# 📚 Steel City: Mob Sim — Documentation Index

**Purpose**: Central hub for all project documentation — helps coding agents find information fast and efficiently.

**Last Updated**: August 14, 2026
**Project**: Steel City: Mob Sim — Organized Crime Simulation
**Status**: 🔄 ALPHA — Vertical Slice Playable (Unity 6)

---

## 🎯 Quick Navigation

| Category | Documents | Status |
|----------|-----------|--------|
| **Core Design** | 6 docs | ✅ Complete |
| **Systems Design** | 11 docs | ✅ Complete |
| **Data Reference** | 2 docs | ✅ Complete |
| **Source Analysis** | 2 docs | ✅ Complete |
| **Unity Project** | 12 docs | ✅ Complete |
| **VoxelAssetStudio** | 20+ docs | ✅ Complete |
| **Vertical Slice** | 1 doc | ✅ Complete |

---

## 📁 Documentation Categories

### 1. Core Design

**Design Philosophy:**
- **`docs/core/DESIGN_PHILOSOPHY.md`** — Foundational design principles
  - Simple mechanics, complex interactions
  - Data-driven architecture
  - Simulation produces narrative
  - Auto-resolved everything
  - Faithful modernization
  - **Mafia Tycoon, not City Builder** — economy as weapon, not management interface

**Mafia Tycoon Design Principle:**
- **`docs/core/MAFIA_TYCOON_DESIGN_PRINCIPLE.md`** — Guardrail against economic over-engineering
  - The line between mafia tycoon and city builder
  - Information rule: economic data gated behind gang activity
  - Action rule: economic change through crime, not management
  - Quick test for evaluating economic features

**Feature-Driven Architecture Pipeline:**
- **`docs/core/FEATURE_DRIVEN_ARCHITECTURE.md`** — Megaplan methodology for technical foundation
  - Per-feature pipeline: Differentiator → Sim/Data/AI → Render/Perf → Lock-in
  - Running feature tracker (core, nice-to-have, deferred)
  - Performance budget tracker (16ms frame budget)
  - Baseline entity counts from RE + gameplay knowledge

**City State Preservation:**
- **`docs/core/CITY_STATE_PRESERVATION.md`** — Week-to-week city baking & incremental update architecture
  - State categories: financial (week-end batch), physical (immediate), static (frozen)
  - Packed voxel file cache (eliminates redundant .stasset reads)
  - ComputeBuffer persistence across week transitions (no full rebuild)
  - Building swap API for mid-week events (bomb, fire)
  - Event queue design for batch processing at week end
  - Performance projections: 92s → ~20-30s load, 0ms week transitions with no changes

**City Layout Pipeline:**
- **`docs/core/CITY_LAYOUT_PIPELINE.md`** — Design tool to Unity implementation pipeline
  - `city_editor.html` design tool (COMPLETE): 3-phase pipeline — Macro → Granular (zones+alleys) → Buildings
  - Phase 1: Gangsters 32×32 macro map (embedded replica1_data.js, macro tile painter with undo)
  - Phase 2: Distance-field zones, alley system (25% per commercial/core, 3-lane debris|path|debris), elevated rail line at seed-determined railCol (avoids EC), live color picker (41 materials), LOD system, compute/render split for performance
  - Phase 3: Building footprints (3×3 per block), emoji overlays (civic, municipal, gang HQs, terminal, docks), train animation (north → station → south loop)
  - JSON v3 export with railLine data (railCol, direction, trainRunsPerWeek)
  - Unity pending tasks: CityLayout variable seams, VoxelTerrainBuilder materials, VoxelWaypointScanner, dynamic cover
  - 1920s urban planning research basis (street hierarchy, trolley lines, alley systems)
  - Material IDs: MAT_ALLEY(133), MAT_TROLLEY_TRACK(135, proposed), MAT_COVER_CRATE(131), MAT_COVER_CAR(132)

**Source Game Analysis:**
- **`docs/core/SOURCE_GAME_ANALYSIS.md`** — Analysis of Gangsters: Organized Crime
  - .xtx file encoding (4-byte XOR key)
  - Decoded data tables summary
  - Original game architecture observations
  - What to preserve, what to polish

**Extracted Game Data:**
- **`docs/core/GANGSTERS_GAME_DATA.md`** — Comprehensive data extraction from 28 decrypted .xtx files
  - Constants: city population (2000 civilians, 400 police, 100 FBI), fear/hostility/squeal tables, bribe prices, FBI suspicion formula, loyalty, recruit tests
  - Economics: All 171 legal business types across 8 groups with full economic data
  - Illegal Economics: All 14 illegal business types with goods flow data
  - Export Ratio: Diminishing returns pricing (Counterfeit $1000, Stolen $200, Liquor $100 per case)
  - Crime: All order types with time cost, suspicion, sentence, investigation, risk values
  - Damage Table: 8 weapons × 7 ranges × 4 damage states
  - Hit Table: 9 weapons × 8 range bands with hit probability formula
  - Hoods: 40 predefined hoods with full stat blocks
  - Character Generation: Hood type generation with skill weightings (includes known skill order bug)
  - Cart: 16 combat attack types with animation result data
  - Business Suspicion: Front suitability for all 15 illegal business types (2=ideal front, 0=poor front)
  - Design implications for Steel City

**Economy & Goods Design:**
- **`docs/core/ECONOMY_DESIGN.md`** — Economy design informed by decrypted game data
  - Three goods types (Counterfeit, Stolen, Liquor) with production/supply chains
  - Warehouse mechanics (12 fixed, never bankrupt, 300+15 case capacity, FBI raid target)
  - Export system (two export points: Docks + Railroad Terminal, diminishing returns)
  - Counterfeit laundering cycle (press → legal businesses → warehouse → export)
  - Business front suitability system (2=ideal cover, 0=poor cover)
  - Illegal business economy (14 types, diminishing returns, profit groups)
  - FBI suspicion system (income threshold $5000, accountant skill reducer)
  - Bribe economy (power-scaled costs)
  - Steel City adaptations: what to preserve, improve, simplify

**Reverse Engineering Findings:**
- **`docs/core/REVERSE_ENGINEERING_FINDINGS.md`** — Ghidra binary analysis of gangsters.exe (18 sections, 2400+ lines)
  - Game timing system (12000-tick weekly budget, 500 ticks/hour)
  - Order type system (dual enum: player orders vs. AI goals)
  - Order setup dispatcher (`FUN_005b3440` — 30+ order types mapped)
  - NotEnough error system (time, cars, bombs, guns, money)
  - **Vehicle-vs-walk decision SOLVED** (bit 15 flag: walk=12000 ticks, drive=32 ticks)
  - Movement state machine (4 states: Init → Pathfinding → Walking → Arrived)
  - Per-tick entity simulation (`FUN_005d2740` — street crossing, wandering)
  - Gang order dispatch with priority calculation (`FUN_0049a530`)
  - Waypoint pathfinding system with countdown timer
  - Block size: 0x60 (96) pixels
  - Engine core deep dive: time budget command queue, pathfinding, street crossing
  - Thunk caller trace analysis (walk/drive dispatch architecture)
  - Vehicle state machine (`0x38000000` field: 3-bit lifecycle)
  - Portrait generation system (5-layer compositor, seed-based)
  - **Section 18: Combat & Pathfinding Deep Dive** — SIM_TICK orchestrator internals (5 queues, 13 cases), 4 combat variants (ranged/melee/vehicle/arrest), waypoint following (3-state), street crossing with traffic lights, AI state machine (8 states with probabilities), entity structure field map, arrest/kidnap message system

**Vehicle RE Reference:**
- **`docs/core/VEHICLE_RE_REFERENCE.md`** — Consolidated vehicle reverse-engineering data (single-stop reference)
  - All 9 Ghidra output files catalogued (walk/drive decision, state setters, flags, traffic, ped interaction)
  - Entity types (tram/train/truck/car with subtypes), key functions table (24 functions)
  - Vehicle flags & bitfields (0x8000, 0x80000, 0x38000000 with setter mapping)
  - Walk vs drive decision logic (distance threshold 0x40, 375× speed differential)
  - SIM_TICK driving cases (4/8/10) with 5-substate advanced driving machine
  - Traffic system (static road flags, street crossing, blocked crossing, vehicle reroute)
  - Pedestrian-vehicle interaction (entity awareness, area scan, post-tick processing)
  - Global state structure offsets, entity structure field map (30+ fields)
  - Time budget system, vehicle strings, vtable architecture, animation system
  - Implementation notes (preserve/modernize/add), re-running Ghidra scripts guide

**Engine Integration Plan:**
- **`docs/core/ENGINE_INTEGRATION_PLAN.md`** — Maps reverse-engineered systems to Steel City implementation
  - 4 core reusable systems: SIM_TICK orchestrator, pathfinding/waypoints, vehicle system, NPC collision/traffic
  - **Vehicle system deep dive**: SIM_TICK driving cases (4/8/10), 5-substate driving AI, vehicle variety table, visual model plans (turning wheels, faction colors)
  - **Section 4A: Dynamic Car Chases & Vehicle Combat Vision** — emergent road encounters, fear/hostility/intelligence trigger system, car chase state machine, 6 emergent gameplay scenarios, code reuse analysis (11 existing systems → 4 new components)
  - C# class designs for each system (SimulationManager, VehiclePhysicsSystem, CarChaseSystem, RoadEncounterDetector, etc.)
  - 5-phase implementation plan with momentum milestones
  - Entity component mapping (original offsets → Unity components)
  - Global state mapping (DAT_007c0024 → SimulationContext)
  - Architecture diagram
  - 8 key design principles from the binary

**Ghidra Scripting Guide:**
- **`docs/core/GHIDRA_SCRIPTING_GUIDE.md`** — Standardized methodology for writing Ghidra Java scripts
  - Script skeleton + key GhidraScript variables
  - Decompilation patterns (timeout guidelines, thunk handling)
  - Caller tracing (including vtable zero-reference workaround)
  - String search (case sensitivity, reference tracing)
  - Binary pattern scanning (x86 instruction patterns for indirect calls, CMP, AND/TEST)
  - Vtable table resolution (scanning .rdata for function pointer arrays)
  - Global state access (DAT_007c0024 offset map)
  - Output file conventions + existing script inventory (23 scripts)
  - Key addresses quick reference (functions, globals, entity types, memory layout)
  - Common pitfalls (signed bytes, thunk resolution, SIB bytes, little-endian)
  - Workflow for adding new scripts

**Zoning Design:**
- **`docs/core/ZONING_DESIGN.md`** — Hub-and-spoke zoning with weighted influence zones
  - Economic Core (EC) at center, Industrial on seed-determined side
  - Rail line: seed-determined N-S column, cuts through any zones (not locked to industrial)
  - Railroad Terminal: municipal building placed ON rail line (export destination)
  - Docks: industrial business in industrial zone (export point + income + recruitment)
  - 14 civic/municipal buildings from Economics.xtx Group 6
  - Weighted influence model with configurable falloff (linear, gaussian, exponential)
  - Export JSON format with zone metadata for Unity consumption

**Keywords**: design, philosophy, principles, source, analysis, xtx, decoding, ghidra, reverse engineering, binary, decompilation, orders, vehicles, timing, combat, pathfinding, sim_tick, traffic, integration, engine, architecture, scripting, vtable, pattern scan, city layout, seam types, alley, main street, trolley, dead-end, waypoint scanner, material ids, city editor, game data, constants, economics, export, counterfeit, liquor, stolen goods, warehouse, docks, railroad terminal, business fronts, suspicion, FBI, bribes, fear, hostility, squeal, zoning, rail line, industrial, commercial, residential

---

### 2. Systems Design

**Systems Overview:**
- **`docs/systems/SYSTEMS_OVERVIEW.md`** — All systems at a glance
  - System interaction map
  - Core gameplay loop
  - System priority for prototyping

**Character System:**
- **`docs/systems/CHARACTER_SYSTEM.md`** — Hood generation and progression
  - Weighted archetypes (from original's 18 types)
  - Skills (10, 6-bit range 0-63)
  - Intelligence (8-bit, 0-255) as tactical AI stat
  - NPC personality: Fear, Hostility, Squeal (citizen metrics)
  - Gang member personality: Loyalty, relationships

**Extortion & Territory:**
- **`docs/systems/EXTORTION_TERRITORY.md`** — Core gameplay loop
  - Extort → Refusal → Escalation → Consequence chain
  - Territory strength (0-100 per block)
  - Block information tiers (Blind → Aware → Informed → Connected → Networked)
  - Market share diminishing returns

**Intelligence System:**
- **`docs/systems/INTELLIGENCE_TERRITORY.md`** — Territory-based fog of war
  - What you own is what you know
  - Squealer identification pipeline
  - Business radar (refined from original)
  - Friendly NPCs as informants

**Corruption & Police:**
- **`docs/systems/CORRUPTION_POLICE.md`** — Beat cops and bribery
  - Geographic corruption (per-beat, not global)
  - Simple bribe mechanic: pay weekly, they suppress heat
  - Internal Affairs as natural ceiling
  - Rival gang corruption competition

**Combat System:**
- **`docs/systems/COMBAT_AUTOBATTLE.md`** — Auto-resolved encounters
  - Intelligence as tactical decision quality
  - Skills determine effectiveness
  - Environment factors (cover, crowds, time of day)
  - No player micromanagement — hoods use their stats

**Crime & Squeal:**
- **`docs/systems/CRIME_SQUEAL.md`** — Crime escalation and consequences
  - Crime table with suspicion/sentence/investigation values
  - NPC squeal rolls trigger investigations
  - Escalation ladder (extort → intimidate → assault → torch → bomb)
  - Investigation visibility and leads decay
  - **Fear Trap**: High fear increases squealing (terrified people talk more)
  - **Information tiers**: Lawyer-gated squealer identification, conditional reports
  - **Legal system chain**: Post-arrest pipeline (Lawyer, Judge/DA bribes, witness/juror intimidation)

**Playtesting Insights:**
- **`docs/systems/PLAYTESTING_INSIGHTS.md`** — Insights from original game manual study + live playtesting
  - Fear/Hostility/Squeal three-axis model with counterintuitive fear-squeal relationship
  - Extortion mechanics (intimidation skill only, office proximity, manpower, service contract model)
  - Information asymmetry design (Lawyer-gated, indirect detection, deduction game)
  - Territory strategy ("baby and scare" your territory, attack rival, donate in neutral)
  - Legal system, illegal business front-matching, diplomacy levels, snitches
  - Open questions for further playtesting

**3D City Rendering:**
- **`docs/systems/3D_CITY_RENDERING.md`** — Working Week visualization
  - Two-phase visual split (2D planning + 3D execution)
  - Simulation as playback layer (decoupled from rendering)
  - Entity budget (~3000-4000, bounded)
  - Event stream format
  - Camera system (free orbit, follow mode)
  - Performance budget and comparison to SteelTide

**Path Debug Rendering:**
- **`docs/systems/PATH_DEBUG_RENDERING.md`** — Instanced box-beam debug path rendering via CommandBuffer
  - Replaces LineRenderer (invisible under voxel RawImage overlay)
  - `CommandBuffer.DrawMeshInstanced` into voxel render texture
  - Per-type batching (Pedestrian/Car/Trolley) with single color per draw call
  - Camera hookup gotcha: bridge must pass camera explicitly (Camera.main fails in URP)
  - Fallback behavior when no VoxelRenderBridge present
  - Diagnostic logging pipeline for troubleshooting

**Voxel Engine Gotchas:**
- **`docs/systems/VOXEL_ENGINE_GOTCHAS.md`** — Non-obvious bugs and traps in the voxel rendering/collision/terrain system
  - ProbeGround flat-array index aliasing (vy out of bounds → false ground hit at Y=66)
  - DebrisScatter log spam (900+ lines per build from per-item logging in batch loops)
  - Empty plot detection (partial vs fully vacant block — per-building vs block-level predicate)
  - Shader animation "Approach A" (output-only offset invisible — must use inverse-transform sampling in DDA)
  - Missing CharacterAnimation component on spawned hood (animState stays 0, pipeline fed zeros)
  - Key voxel engine constants table (terrain thickness, voxel sizes, index formula, animation groups)

**Refactoring Roadmap:**
- **`docs/systems/REFACTORING_ROADMAP.md`** — Future code extraction plan to keep files manageable
  - Current file size assessment (VoxelChunkManager ~2K, CityMap3D ~1.6K, GameUIController ~1.4K)
  - Extraction plan: CityMapCamera, BuildingLoader, SectorBakery, ChunkBufferManager, OrderPanelController
  - Trigger conditions for when to extract (not premature)
  - Anti-patterns to avoid (line count alone, during creative sessions, deep dependency chains)

**Instanced Rendering Pitfalls:**
- **`docs/systems/INSTANCED_RENDERING_PITFALLS.md`** — Non-obvious behaviors of the custom raymarch instancing pipeline
  - Why toggling GameObjects in Inspector doesn't hide them (CommandBuffer bypasses Unity culling)
  - activeInHierarchy check fix in RenderInstancedGroup
  - No frustum culling on instanced characters
  - Unregistering vs destroying patterns
  - Baked sectors have same issue (sector.active flag, not GameObject hierarchy)
  - Architecture diagram: standard Unity pipeline vs Steel City voxel pipeline
  - Debugging tips for invisible/stuck/missing characters

**Keywords**: systems, character, extortion, territory, intelligence, corruption, police, combat, crime, squeal, 3d, rendering, visualization, camera, playtesting, insights, fear, hostility, legal, diplomacy, path, debug, beams, commandbuffer, instanced, voxel, gotchas, pitfalls, collision, probe, terrain, flat-array, aliasing, animation, shader, raymarch, dda, inverse-transform, groupid, stance, characteranimation, refactoring, extraction, modular, file size, citymap, chunkmanager, buildingloader, sectorbakery, activeinhierarchy, drawmeshinstanced, computebuffer, unregister, frustum

---

### 3. Data Reference

- **`docs/data/GAME_DATA_REFERENCE.md`** — Extracted data from original game (legacy)
  - Constants (population, fear, squeal, bribes, FBI, elections)
  - Character generation archetypes
  - Crime table (all 30+ crimes)
  - Hit/damage tables (9 weapons × 8 ranges)
  - Economics (171 legal businesses, 14 illegal)
  - Suspicion matrix
  - Scenarios (10)
  - Market share decay curve
  - Profit/running cost tables

- **`docs/core/GANGSTERS_GAME_DATA.md`** — Comprehensive decrypted game data (28 .xtx files)
  - See Core Design section above for full description
  - Supersedes GAME_DATA_REFERENCE.md with more complete data from decrypted files

**Keywords**: data, reference, constants, crime, weapons, economics, businesses

---

## 📋 Document Conventions

- All design docs use Markdown
- System docs follow template: Overview → Original Implementation → Modernization → Data Schema → Interactions
- Data references include source file name from original game
- Status indicators: ✅ Complete | 📐 In Progress | 📝 Planned | ❌ Blocked

---

## 🔗 External References

- **Gangsters Data Codex**: `gangsters_decoded/index.html` — Visual HTML viewer of all decoded game data
- **Decode Scripts**: `scripts/crack_xtx.py`, `scripts/decode_all_xtx.py` — Tools used to decode original game files
- **Codex Generator**: `scripts/generate_codex.py` — Generates the HTML codex from decoded data

---

## 🎮 Unity Project Documentation

All Unity-side documentation lives in `Assets/docs/`. See `Assets/docs/DOCUMENTATION_INDEX.md` for the full index.

### Key Unity Docs
- **`Assets/docs/GAME_DESIGN_SKELETON.md`** — Core game design (mechanics, phases, systems, data schema, dev sequence)
- **`Assets/docs/VOXEL_LIGHTING_AND_SHADOWS.md`** — Raymarch lighting pipeline (hybrid normals, shadow debug, lighting toggles)
- **`Assets/docs/VOXEL_BUILDING_METHODOLOGY.md`** — Voxel building generation pipeline
- **`Assets/docs/MOB_SIM_SCALE_STANDARD.md`** — Scale system (voxel sizes, door sizes, reference objects)
- **`Assets/docs/COORDINATE_SYSTEM.md`** — Positioning & coordinate spaces (MapRoot offset, block grid, character placement, corner vs center)
- **`Assets/docs/PORTING_NOTES.md`** — Python → C# porting gotchas and verification

### Simulation Architecture (Assets/Scripts/Sim/)
- **`FollowCamera.cs`** — Follow camera with spherical coordinate positioning, free-look mode, OnGUI debug HUD, hotkey controls, UI auto-hide, VoxelRenderBridge integration
- **`SimulationManager.cs`** — Pure logic simulation manager (produces SimEvents, no Unity-specific references)
- **`EventPlayer.cs`** — Consumes SimEvents from SimulationManager, drives visual updates
- **`SimEventStream.cs`** — Event stream with SimEvent factory methods
- **`VoxelCharacter.cs`** — Voxel character rendering with WorldCenter property for camera aiming
- **`ClothingSystem.cs`** — Per-instance clothing/outfit system. Uses VoxelChunkManager per-instance remap API (SetInstanceOutfit) to apply unique material remapping per character without breaking GPU instancing. Auto-adds to VoxelCharacter when useInstancing=true
- **`CharacterRig.cs`** — Character animation controller with hotkeys (T/I/W/L/A/C). Controllable flag + ActiveRig static for multi-character hotkey routing. Uses GPU instanced rendering path (VoxelCharacter + CharacterAnimation)
- **`DebugHUDManager.cs`** — In-game debug panel with tabs (Camera, Render, Clothing, Path). Clothing tab has dual selectors: green buttons for character hotkey routing, blue buttons for outfit instance selection
- **`Pathfinder.cs`** — A* pathfinding on WaypointGraph
- **`WaypointGraph.cs`** — Waypoint graph with sidewalk/crosswalk/jaywalk links

### Per-Instance Clothing System
- **`ClothingSystem.cs`** — Per-instance outfit remapping via VoxelChunkManager API
  - Region-based remapping: outfits applied by remapping material IDs within voxel regions (Torso, Arms, Legs, Feet, etc.)
  - Uses `SetInstanceOutfit()` / `GetInstanceOutfit()` — no shared buffer modification
  - Auto-adds to VoxelCharacter when `useInstancing=true` and asset has regionDefs
  - OutfitPresetType enum for quick preset selection
- **`CharacterPoseCompute.compute`** — CSPose kernel applies per-instance material remap via `instanceMaterialRemapBuffer`
- **`VoxelChunkManager.cs`** — `instanceMaterialRemapBuffer` per InstancedGroup, built per-frame from `InstancedCharacter.materialRemap` arrays
- **`CityMap3D.cs`** — Consolidated spawning: `Characters/Civilians/Civilian_01 + Civilian_02` with `ApplyCivilianOutfits()` coroutine
- **`docs/PROCEDURAL_CLOTHING_CONCEPT.html`** — Visual concept document for procedural clothing system

### VoxelAssetStudio Roadmaps
- **`VoxelAssetStudio/IMPROVEMENT_ROADMAP.md`** — V2.0 improvement plan (undo/redo, layers, selection tools)
- **`VoxelAssetStudio/PHASE2_IMPLEMENTATION_PLAN.md`** — Skeleton rigging system plan
- **`VoxelAssetStudio/INTERACTIVE_JOINTS_ROADMAP.md`** — Interactive joint manipulation
- **`VoxelAssetStudio/city_editor.html`** — City Layout Editor (3-phase pipeline: Macro → Granular → Buildings, see `docs/core/CITY_LAYOUT_PIPELINE.md`)

### Vertical Slice
- **`docs/VERTICAL_SLICE_DESIGN.md`** — End-to-end test design (9 blocks, 2 factions, 5-10 weeks)
