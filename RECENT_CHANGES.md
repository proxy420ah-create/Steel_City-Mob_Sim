# Recent Changes — Steel City: Mob Sim

**Last Updated**: August 6, 2026

---

## August 6, 2026 — Camera Controls, Perspective Rendering Fix, Sim Simplification

### Created
- `Assets/Scripts/Sim/FollowCamera.cs` — Follow camera with full debug controls
  - Spherical coordinate camera positioning (yaw, pitch, distance, height)
  - OnGUI debug HUD showing live camera metrics (distance, height, yaw, pitch, FOV, aim point, cam position)
  - Hotkeys: Arrow keys (orbit yaw/pitch), Q/E (distance), R/F (height), +/- (FOV)
  - Free-look mode (hold Left Shift): arrows rotate camera view in-place, offsets persist on release
  - Z key resets look offsets to zero
  - C key captures all camera metrics to console log (copy-pasteable)
  - H key toggles HUD visibility
  - Automatically hides all game UI panels on init, restores on shutdown (raymarch overlay preserved)
  - VoxelRenderBridge integration: swaps VoxelChunkManager render camera, attaches own bridge
- `Assets/Scripts/Sim/SimulationManager.cs` — Pure logic simulation manager (replaces TickSimulation)
  - Decoupled from rendering, produces SimEvents consumed by EventPlayer
  - "stand" order type: sets state to Idle (sim stays active for camera debugging)
- `Assets/Scripts/Sim/EventPlayer.cs` — Consumes SimEvents, drives visual updates
- `Assets/Scripts/Sim/SimEventStream.cs` — Event stream with SimEvent factory methods
- `Assets/Scripts/Sim/Pathfinder.cs` — A* pathfinding on WaypointGraph
- `Assets/Scripts/Sim/WaypointGraph.cs` — Waypoint graph with sidewalk/crosswalk/jaywalk links
- `Assets/Scripts/Sim/VoxelCharacter.cs` — Voxel character with WorldCenter property for camera aiming
- `Assets/Scripts/Sim/TickHUD.cs` — HUD overlay during working week
- `Assets/Scripts/Sim/VoxelCollisionWorld.cs` — Voxel-based collision world
- `Assets/Scripts/Sim/BuildingOrientation.cs` — Building orientation helper

### Changed
- **Perspective rendering fix**: `VoxelChunkManager.cs` now uses `renderCamera.cameraToWorldMatrix` instead of `Matrix4x4.TRS()` for the camera-to-world matrix. The previous TRS approach used Unity's transform convention (+Z forward) but the inverse projection matrix produces view-space coordinates (-Z forward), causing perspective rays to fire backward — only the massive terrain volume got accidentally hit.
- **Compute shader fix**: `MobSimVoxelRaymarch.compute` — ortho ray direction changed from +Z to -Z (view space convention), perspective clip space Z range corrected from 0..1 to -1..+1 (Unity NDC convention). Buildings, characters, and decorations now render correctly in both ortho and perspective modes.
- `GameEngine.cs` — Reduced player to 1 hood (Vinny Moretti) and rival to 1 hood for simplified testing
- `GameUIController.cs` — Auto-assigns "stand" order on Run Week (no clicking required), passes VoxelCharacter to FollowCamera.Initialize for center-based aiming, detailed camera transition logging
- `CityMap3D.cs` — Updated accessor comments for SimulationManager/EventPlayer architecture
- `SimEventStream.cs` — Moved static factory methods to SimEvent class

### Camera Control Hotkeys
| Key | Action |
|-----|--------|
| Arrow Left/Right | Orbit yaw (rotate around target) |
| Arrow Up/Down | Orbit pitch (angle above target) |
| Q / E | Zoom in/out (distance) |
| R / F | Raise/lower height |
| + / - | Narrow/widen FOV |
| Left Shift (hold) | Free-look mode (arrows = look around in-place) |
| Z | Reset look offsets to zero |
| C | Capture all metrics to console log |
| H | Toggle debug HUD |

---

## August 6, 2026 — Playtesting Insights from Manual Study

### Created
- `docs/systems/PLAYTESTING_INSIGHTS.md` — Comprehensive insights from original game manual study + live playtesting
  - Fear/Hostility/Squeal three-axis model (fear increases squealing at high levels)
  - Extortion mechanics (intimidation skill only, distance from nearest office, manpower, protection as service contract)
  - Information tiers for squealer identification (Lawler-gated, conditional reports, indirect detection)
  - Territory strategy ("baby and scare" your territory, attack rival territory, donate in neutral territory)
  - Legal system chain (post-arrest: Lawyer defends, bribe Judge/DA, intimidate witnesses/jurors)
  - Illegal business front-matching (similarity rule, confirmed business types)
  - Diplomacy system (five levels, snitches as limited resource)
  - Open questions for further playtesting

### Updated
- `docs/systems/CRIME_SQUEAL.md` — Added: Fear Trap (high fear increases squealing), Information Tiers for Squealer ID, Legal System Chain, conditional reports pattern, indirect detection methods, fear diminishing returns design note
- `docs/systems/EXTORTION_TERRITORY.md` — Added: Key Extortion Factors table (intimidation only, not intelligence), Office Proximity as territorial strategy, Protection as Service Contract (not permanent), Territory Strategy (baby/scare/attack/donate), Fear Trap cross-reference
- `docs/systems/INTELLIGENCE_TERRITORY.md` — Added: Information Infrastructure Requirements (Lawyer-gated squealer ID), conditional reports pattern, indirect detection without Lawyer, information asymmetry as intentional design

### Context
- Studied the official Gangsters: Organized Crime manual (`manual_text.txt`, 4085 lines) page by page
- Cross-referenced manual findings with binary analysis and existing Steel City design docs
- Confirmed most Steel City design decisions are correct; refined with new mechanical details
- Key new insight: Fear has a negative return on squeal suppression at high levels — over-intimidating is as dangerous as under-intimidating

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
