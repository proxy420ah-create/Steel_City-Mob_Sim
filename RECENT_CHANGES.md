# Recent Changes — Steel City: Mob Sim

**Last Updated**: August 4, 2026

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

---

## August 3, 2026 — Unity Port + 3D Voxel Rendering

### Created
- C# simulation engine ported from Python prototype:
  - `GameEngine.cs`, `City.cs`, `NPC.cs`, `CrimeSystem.cs`, `EconomySystem.cs`, `RivalAI.cs`, `EventStream.cs`
  - `DataLoader.cs`, `DataModels.cs`, `JSONParser.cs` (custom JSON parser for dict support)
  - `GameBootstrap.cs` — Unity MonoBehaviour entry point
- 3D voxel rendering pipeline:
  - `MobSimVoxelRaymarch.compute` — GPU raymarching compute shader (DDA traversal, per-voxel material colors)
  - `VoxelChunkManager.cs` — chunk-based compute shader dispatch, frustum culling, depth buffer
  - `VoxelSun.cs` — dynamic sun position, day/night cycle, lighting presets (dawn/noon/dusk/night)
  - `CityMap3D.cs` — camera system (LMB focus, MMB rotate, RMB pan, wheel zoom), UI integration
  - `GameUIController.cs` — tabbed UI (Hoods, Block, Orders, Finance, Police, Invest, Log)
  - `StAssetReader.cs` — runtime .stasset file loading
  - `VoxelBuildingMeshifier.cs` — voxel-to-mesh conversion (legacy, now superseded by raymarch)
- Voxel asset generation:
  - `procedural_mob_buildings.py` — 1920s building generators (apartments, barber, bakery, butcher, diner, garage, casino, speakeasy, HQ, police station, empty land)
  - `generate_city_assets.py` — city layout generator (reads template, exports .stasset files)
  - `mob_materials.py` — 1920s material palette (brick, wood, concrete, glass, neon, cobblestone, etc.)
- Documentation: UI setup guide, tabbed layout, gotchas, building methodology, scale standard, porting notes

### Verified
- 5-week automated simulation test passes in Unity console
- City: 9 blocks, 16 businesses, 118 NPCs, 2 police officers
- All systems functional: extortion, squeal, investigations, rival AI, economy, territory

---

## August 4, 2026 — Raymarch-Only Rendering + Lighting/Shadow Debug + Repo Detangle

### Changed
- **Raymarch-only rendering**: Removed all mesh-based rendering from `CityMap3D.cs` (BuildVoxelBlock, BuildCubeBlock, BuildRoadNetwork, etc.). Raymarch compute shader is now the sole renderer.
- **Hybrid normals (Option B)**: `MobSimVoxelRaymarch.compute` now uses DDA face normal for top/bottom surfaces (uniform flat ground — no edge-vs-center gradient), and blends with SmoothNormal for side faces (soft wall shading). Fixes brightness inconsistency on flat ground.
- **Shadow debug controls**: Added `_ShadowEnabled`, `_ShadowNormalNudge`, `_ShadowLightNudge`, `_ShadowSkipSteps`, `_ShadowMaxSteps` shader uniforms. Exposed as UI toggles/sliders in GameUIController.
- **Lighting component toggles**: Added `_SunLightEnabled`, `_AmbientEnabled`, `_FillEnabled`, `_CamLightEnabled` shader uniforms. Each lighting term can be independently toggled live via UI.
- **Shadow ambient + safety floor**: Shadowed areas retain full ambient + fill; only sun component modulated by shadowFactor. Safety floor prevents pure black.
- **Rubble decorations**: `generate_empty_land` now adds 20 random 2×2×1 stone clusters at Y=1 on empty land plots.
- **Repo detangle**: Moved Unity project + VoxelAssetStudio out of SteelTide repo into SteelCityMobSim repo. Flattened UnityProject/ to repo root. Updated `generate_city_assets.py` path references.
- Updated all documentation to reflect current codebase.

### Files Modified
- `Assets/Resources/Shaders/MobSimVoxelRaymarch.compute` — hybrid normals, shadow/lighting debug uniforms, parameterized shadow ray
- `Assets/Scripts/UI/VoxelChunkManager.cs` — shadow/lighting debug fields, shader property IDs, Set/Get methods
- `Assets/Scripts/UI/CityMap3D.cs` — proxy API for shadow/lighting debug params, raymarch-only rendering
- `Assets/Scripts/UI/GameUIController.cs` — shadow/lighting debug UI toggles and sliders
- `VoxelAssetStudio/procedural_mob_buildings.py` — rubble decorations in generate_empty_land
- `VoxelAssetStudio/generate_city_assets.py` — path references updated for new repo structure
