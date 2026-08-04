# 📚 Steel City: Mob Sim — Documentation Index

**Purpose**: Central hub for all project documentation — helps coding agents find information fast and efficiently.

**Last Updated**: August 4, 2026
**Project**: Steel City: Mob Sim — Organized Crime Simulation
**Status**: 🔄 ALPHA — Vertical Slice Playable (Unity 6)

---

## 🎯 Quick Navigation

| Category | Documents | Status |
|----------|-----------|--------|
| **Core Design** | 2 docs | ✅ Complete |
| **Systems Design** | 8 docs | ✅ Complete |
| **Data Reference** | 1 doc | ✅ Complete |
| **Source Analysis** | 1 doc | ✅ Complete |
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

**Source Game Analysis:**
- **`docs/core/SOURCE_GAME_ANALYSIS.md`** — Analysis of Gangsters: Organized Crime
  - .xtx file encoding (4-byte XOR key)
  - Decoded data tables summary
  - Original game architecture observations
  - What to preserve, what to polish

**Keywords**: design, philosophy, principles, source, analysis, xtx, decoding

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

**3D City Rendering:**
- **`docs/systems/3D_CITY_RENDERING.md`** — Working Week visualization
  - Two-phase visual split (2D planning + 3D execution)
  - Simulation as playback layer (decoupled from rendering)
  - Entity budget (~3000-4000, bounded)
  - Event stream format
  - Camera system (free orbit, follow mode)
  - Performance budget and comparison to SteelTide

**Keywords**: systems, character, extortion, territory, intelligence, corruption, police, combat, crime, squeal, 3d, rendering, visualization, camera

---

### 3. Data Reference

- **`docs/data/GAME_DATA_REFERENCE.md`** — Extracted data from original game
  - Constants (population, fear, squeal, bribes, FBI, elections)
  - Character generation archetypes
  - Crime table (all 30+ crimes)
  - Hit/damage tables (9 weapons × 8 ranges)
  - Economics (171 legal businesses, 14 illegal)
  - Suspicion matrix
  - Scenarios (10)
  - Market share decay curve
  - Profit/running cost tables

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
- **`Assets/docs/PORTING_NOTES.md`** — Python → C# porting gotchas and verification

### VoxelAssetStudio Roadmaps
- **`VoxelAssetStudio/IMPROVEMENT_ROADMAP.md`** — V2.0 improvement plan (undo/redo, layers, selection tools)
- **`VoxelAssetStudio/PHASE2_IMPLEMENTATION_PLAN.md`** — Skeleton rigging system plan
- **`VoxelAssetStudio/INTERACTIVE_JOINTS_ROADMAP.md`** — Interactive joint manipulation

### Vertical Slice
- **`docs/VERTICAL_SLICE_DESIGN.md`** — End-to-end test design (9 blocks, 2 factions, 5-10 weeks)
