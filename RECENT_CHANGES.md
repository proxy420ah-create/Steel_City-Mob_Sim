# Recent Changes — Steel City: Mob Sim

**Last Updated**: August 2, 2026

---

## August 2, 2026 — Project Initialization

### Created
- Project directory structure (`SteelCityMobSim/`)
- `.gitignore` — Python, build output, saves, decoded source data
- `README.md` — Project overview, design principles, core loop, structure
- `DOCUMENTATION_INDEX.md` — Central doc hub with navigation
- `docs/core/DESIGN_PHILOSOPHY.md` — 5 founding principles
- `docs/core/SOURCE_GAME_ANALYSIS.md` — Full analysis of decoded .xtx files
- `docs/systems/SYSTEMS_OVERVIEW.md` — System interaction map, core loop, priority
- `docs/systems/CHARACTER_SYSTEM.md` — Hoods (skills, INT, loyalty) + Citizens (fear/hostility/squeal)
- `docs/systems/EXTORTION_TERRITORY.md` — Core loop, refusal chain, territory strength, info tiers
- `docs/systems/INTELLIGENCE_TERRITORY.md` — Territory-based fog of war, squealer pipeline, business radar
- `docs/systems/CORRUPTION_POLICE.md` — Beat cops, simple bribe mechanic, geographic coverage
- `docs/systems/COMBAT_AUTOBATTLE.md` — Auto-resolved combat, INT as tactical AI, combat log
- `docs/systems/CRIME_SQUEAL.md` — Crime table, squeal events, investigation leads, escalation ladder
- `docs/data/GAME_DATA_REFERENCE.md` — All extracted values from decoded original game data

### Updated
- `docs/systems/3D_CITY_RENDERING.md` — Added interactive Working Week design
  - Tactical overrides (flee, reinforce, abort, attack, hold ground, lie low)
  - Pause system (spacebar toggle, real-time + paused modes, speed controls)
  - Time-sliced simulation architecture (bidirectional: sim → render → player input → sim)
  - Radial menu HUD for mid-week hood orders
  - Updated data flow to reflect player input bridge

### Context
- All 30 .xtx files from Gangsters: Organized Crime decoded (4-byte XOR key: 0xAF, 0xDE, 0xDE, 0xFA)
- Visual data codex generated at `gangsters_decoded/index.html`
- Design philosophy established: simple mechanics, complex interactions
- Core systems conceptualized through design discussion
- Ready to begin prototyping
