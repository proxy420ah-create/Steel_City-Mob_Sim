# Steel City: Mob Sim

**Version**: 0.0.1-pre-production
**Created**: August 2, 2026
**Status**: 📐 DESIGN & CONCEPTUALIZATION

---

## Project Overview

**Steel City: Mob Sim** is a spiritual successor to Hothouse Creations' 1998
strategy classic *Gangsters: Organized Crime*. It is a systems-heavy,
procedurally-driven organized crime simulation where the player manages a
criminal empire through a weekly planning and execution cycle.

The player assumes the role of a crime boss in a fictional 1920s American
city. Starting with a handful of hoods and a single business, the boss must
extort territory, run illegal operations, manage law enforcement heat,
compete against rival gangs, and optionally enter politics — all through a
data-driven simulation that produces emergent narratives without scripted
storylines.

### Primary Inspiration

- **Gangsters: Organized Crime** (1998, Hothouse Creations) — the direct
  spiritual predecessor. All game data has been decoded from the original
  `.xtx` files and analyzed as a foundation for this project.

### Design Principles

1. **Simple mechanics, complex interactions** — Each individual system is
   trivial (roll against a number). Depth emerges from system interconnection.
2. **Data-driven architecture** — All game balance lives in external, moddable
   data files. Designers can tweak without recompiling.
3. **Simulation produces narrative** — No scripted stories. The butcher who
   refuses to pay, the cop who gets bribed, the lieutenant who betrays you —
   all emerge from simulation state and player decisions.
4. **Auto-resolved everything** — The player is a manager, not an action hero.
   Combat, investigations, and encounters are resolved by the simulation
   based on stats and circumstances.
5. **Faithful modernization** — Preserve the original's core loop and systems.
   Polish transparency, depth, and interconnection. Don't reinvent.

---

## Core Game Loop

```
[ Gang Organizer (Planning) ]  --> Player assigns orders, manages businesses,
               │                   recruits hoods, bribes cops, reviews intel
               │
[ Working Week (Execution) ]  --> Simulation resolves all orders, runs
               │                   encounters, updates world state
               │
[ Results & Reports ]         --> Player reviews outcomes, adjusts strategy
               │
               └──-> back to Gang Organizer
```

---

## Project Structure

```
SteelCityMobSim/
├── README.md                  — This file
├── DOCUMENTATION_INDEX.md     — Central doc hub (READ FIRST)
├── RECENT_CHANGES.md          — Change log
├── .gitignore
├── docs/
│   ├── core/                  — Design philosophy, source analysis
│   ├── systems/               — System design documents
│   └── data/                  — Data schema and reference docs
├── data/                      — Game data files (JSON/TOML, moddable)
├── src/                       — Game source code
├── scripts/                   — Utility scripts (codegen, converters)
└── assets/                    — Art, audio, UI assets
```

---

## Technology Stack

**TBD** — To be decided during prototyping phase.

Candidates:
- **Python + Pygame/SDL2** — Fast prototyping, data-driven friendly
- **Godot Engine** — Open source, good 2D, data-driven, GDScript
- **Custom engine (C++)** — Maximum control, maximum effort

The original game was built in C++ with isometric 2D graphics. A modern
successor should match the original's visual style (isometric city view)
with cleaner UI and higher resolution, not chase photorealism.

---

## Source Game Analysis

All 30 `.xtx` data files from the original game have been decoded using a
4-byte repeating XOR key (`[0xAF, 0xDE, 0xDE, 0xFA]`). Decoded files are
stored in `gangsters_decoded/` (gitignored — not our IP). Analysis is
documented in `docs/core/SOURCE_GAME_ANALYSIS.md` and a visual codex is
available at `gangsters_decoded/index.html`.

Key data tables decoded:
- Constants (population, fear, hostility, squeal, bribes, FBI, elections)
- Character Generation (18 weighted archetypes, 10 skills, 6-bit range)
- Crime Table (30+ crimes with suspicion, sentence, investigation, risk)
- Hit/Damage Tables (9 weapons × 8 range bands)
- Economics (171 legal businesses, 8 groups)
- Illegal Economics (14 illegal business types)
- Business Suspicion Matrix (legal × illegal cross-reference)
- Predefined Hoods (40 named characters with full stat blocks)
- Scenarios (10 single-player scenarios)
