# Recent Changes — Steel City: Mob Sim

**Last Updated**: August 9, 2026 (Terrain Sector Baking + Collision World Optimization — 211x load time improvement)

---

## August 9, 2026 — Terrain Sector Baking + Collision World Flat Array Optimization

### Impact
- **Terrain load: 78,466ms → 371ms** (211x faster)
- **Total BuildMap: ~79s → ~0.5s** (estimated 158x faster)
- **Draw calls: 100 terrain chunks → 1 sector (1 draw call)**
- **ComputeBuffers: 100 → 1** for terrain
- **GameObjects: 100 → 0** for terrain (no transform hierarchy overhead)
- **Memory: ~500MB dictionary overhead → 13.3MB flat byte array** for collision world

### Changes

#### 1. Terrain Sector Baking (`CityMap3D.cs:868-935`)
Replaced 100 sequential `LoadChunkFromData` calls (each creating a `ComputeBuffer`, `SetData`, `ComputeTightAABB`, and `GameObject`) with a single sector bake:
- All 100 terrain chunks concatenated into one flat `uint[]` buffer (13.9M voxels)
- Per-chunk metadata `(bufferOffset, dims, worldOffset)` stored in `Vector4[]` arrays
- Registered via `RegisterSector("terrain_sector", ...)` — 1 `ComputeBuffer`, 1 `SetData`, 1 draw call
- No `ComputeTightAABB` needed (terrain AABB is full bounds — flat 2-voxel slab)
- No `GameObject` creation (raymarch shader doesn't need transform hierarchy)

#### 2. Collision World Flat Array (`VoxelCollisionWorld.cs`)
**Root cause of 78s bottleneck**: `Dictionary<Vector3Int, byte>` for 13.9M voxel inserts.
- Each insert: `Vector3Int` hash computation + bucket probe + collision chain + dictionary resizing (~24 resizes to grow to 13.9M entries)
- Replaced with flat `byte[]` array indexed by `x + y*gridW + z*gridW*gridH`
- Each write is now `array[idx] = value` — O(1), no hashing, no resizing
- Grid grows dynamically in all directions (handles negative offsets by shifting origin)
- Lookups (`ProbeGround`, `HasGroundAt`) also O(1) array index with bounds check
- Memory: 13.9M bytes (13.3MB) vs ~500MB+ dictionary entry overhead

### What This Enables Next
- **Larger cities**: 500-1000 block cities now feasible (terrain was the bottleneck, not buildings)
- **Faster iteration**: Sub-second reload enables rapid testing of layout/visual changes
- **More GPU headroom**: 99 fewer draw calls and 99 fewer ComputeBuffers frees GPU for characters/vehicles
- **Potential for async terrain**: With collision registration no longer blocking, terrain generation could be moved to a background thread entirely

---

## ⚠️ REMINDER: City Scale Testing — TWO Files Must Change Together

When testing different city sizes (25/100/500/1000 blocks), you MUST copy **both** files from `StreamingAssets/`:

| File | Used By | Controls |
|------|---------|----------|
| `city_template_NN.json` → `city_template.json` | `DataLoader` → `GameEngine.Setup()` | Game logic: blocks, businesses, NPCs, police, gangs |
| `city_layout_NN.json` → `city_layout.json` | `CityMap3D.LoadCityLayout()` | Visuals: .stasset building placement, voxel rendering |

**If only one is updated**, the engine block count won't match the visual layout — e.g., 500 layout with 100 template produces a 10x10 city despite the log saying "500 blocks loaded".

```powershell
# Example: switch to 500 blocks
Copy-Item "SteelCityMobSim\Assets\StreamingAssets\city_template_500.json" "SteelCityMobSim\Assets\StreamingAssets\city_template.json" -Force
Copy-Item "SteelCityMobSim\Assets\StreamingAssets\city_layout_500.json" "SteelCityMobSim\Assets\StreamingAssets\city_layout.json" -Force
```

Available tiers: `city_template_25`, `city_template_100`, `city_template_500`, `city_template_1000` (and matching `city_layout_*`).

---

## August 8, 2026 — Tenement Block 0 Final Redesign (Dual FE + Roof)

### Deployed
- `Assets/StreamingAssets/voxel_buildings/tenement_block_0.stasset` — **Replaced** with final version (96×60×96, 95,702 non-air voxels)
- `Assets/StreamingAssets/voxel_buildings/tenement_block_0_original_backup.stasset` — Backup of original

### Created
- `VoxelAssetStudio/extend_landings.py` — Extend FE landings to 2-window coverage per landing
- `VoxelAssetStudio/mirror_fe.py` — Mirror FE across X axis (back-left → back-right)
- `VoxelAssetStudio/add_side_fe.py` — Duplicate + rotate FE 90° onto adjacent wall (front-left)
- `VoxelAssetStudio/add_roof_deco.py` — Add water tower + parapet wall to roof
- `VoxelAssetStudio/analyze_fe_landings.py` — Detailed landing/window analysis
- `VoxelAssetStudio/load_firework.py` — Load user-edited firework JSON with roof buffer

### Changed
- `VoxelAssetStudio/voxel_editor_html.py` — Added 3-axis slice controls (X/Y/Z min/max sliders)
- `VoxelAssetStudio/procedural_mob_buildings.py` — Added `roof_buf=8` parameter to `generate_apartment_block()`
- `VoxelAssetStudio/json_to_stasset.py` — Fixed `save_stasset()` call (removed unsupported `scale` kwarg)
- `Assets/docs/VOXEL_EDITOR_AND_FIRE_ESCAPE.md` — Added sections: 3-axis slicer, grep gotcha, landing extension, mirroring, 90° rotation, roof buffer, updated alignment table and script/file listings

### Tenement Final Specs
- **Dimensions**: 96×60×96 (was 96×44×96 — +16 roof buffer)
- **Core**: 80×80 with 8-voxel side buffer
- **Fire escapes**: 2 (back-right + front-left, mirrored + rotated)
- **Landing Y levels**: 10, 18, 26, 34 (2 below window sills at 12, 20, 28, 36)
- **Landing coverage**: 2 windows per landing (X=62-75 back wall, Z=10-36 left wall)
- **Roof**: Water tower (8×8 wood tank on iron legs, Y=44-51) + parapet wall (2v brick, Y=44-45)
- **Total voxels**: 95,702 non-air

---

## August 8, 2026 — Voxel Editor Enhancements + Fire Escape Redesign

### Created
- `Assets/docs/VOXEL_EDITOR_AND_FIRE_ESCAPE.md` — Full documentation of voxel editor HTML system and fire escape workflow
- `VoxelAssetStudio/shift_fe.py` — Shift fire escape JSON voxels by Y offset
- `VoxelAssetStudio/fix_fe_spacing.py` — Full pipeline: fix spacing + regenerate tenement + bolt on fire escape
- `VoxelAssetStudio/bolt_fe_v2.py` — Earlier bolt-on version (80×80 core, 8v buffer)
- `VoxelAssetStudio/analyze_fe.py` — Analyze fire escape JSON (bounding box, Y distribution, landings)
- `VoxelAssetStudio/load_fe_test.py` — Load fire escape JSON into editor for inspection

### Changed
- `VoxelAssetStudio/voxel_editor_html.py` — Enhanced with:
  - **Escape key**: Universal reset — clears all tool states, switches to camera mode
  - **Camera tool**: New tool mode (gray highlight) with early returns in `performTool` and `updateHighlight`
  - **Volume expansion**: Dynamic grid resizing via modal (W/H/D changed from `const` to `let`, `gridHelper` to `let`)
  - **Enhanced selection**: Shift+click for single voxel, Ctrl+click for box select, flood-fill default
  - **Ruler tool**: White highlight with voxel count in status bar
  - Updated `TOOL_COLORS` and `TOOL_DESC` dictionaries
- `Assets/docs/DOCUMENTATION_INDEX.md` — Added Voxel Editor category (Section 3b), updated key file locations, version 1.4.0

### Tenement Buffer Change
- **Core**: 88×88 → 80×80 voxels (regenerated with `generate_apartment_block(w=80, d=80)`)
- **Buffer**: 4 → 8 voxels each side (enables 7-voxel-deep fire escape + future decorations)
- **Total footprint**: 96×96 voxels (unchanged — fits game map exactly)
- **BuildingVoxelWidth=32** in `CityMap3D.cs` constrains total to 96×96

### Fire Escape Alignment
- Landings at Y=10, 18, 26, 34 (2 below window sills, 8-voxel spacing)
- Drop ladder with guide rails (street to first landing)
- Roof ladder (top landing to roof, per 1860 NYC ordinance)
- Support posts: vertical iron posts from ground to first landing
- Materials: DARK_IRON (109) structure, PAINTED_METAL (111) railings

### Output
- `tenement_block_0_new_fe.stasset` — 96×52×96, script-generated intermediate version

---

## August 8, 2026 — Instanced Box-Beam Path Debug Rendering + Camera Fix

### Created
- `Assets/Shaders/InstancedColor.shader` — Unlit transparent instanced shader with `_Color` property for per-batch coloring
- `docs/systems/PATH_DEBUG_RENDERING.md` — Documents the CommandBuffer-based instanced beam rendering pipeline, camera hookup gotcha, batching strategy, and troubleshooting

### Changed
- `Assets/Scripts/Sim/PathDebugRenderer.cs` — Complete rewrite from LineRenderer to instanced box beams
  - Uses `CommandBuffer.DrawMeshInstanced` to composite beams into the voxel render texture
  - Per-type batching (Pedestrian/Car/Trolley) with single color per draw call (3 draw calls max)
  - Sorts `activePaths` by type for contiguous batch ranges
  - Reusable `batchBuffer` with `Array.Copy` (no per-frame GC allocation)
  - `RenderBeamsIntoCamera(Camera externalCam = null)` accepts camera from bridge
  - Fallback path in `Update()` when no `VoxelRenderBridge` present
  - Comprehensive diagnostic logging (every 60 frames): path state, batch counts, draw call counts
- `Assets/Scripts/UI/VoxelRenderBridge.cs` — Passes `_camera` to `RenderBeamsIntoCamera()` instead of relying on `Camera.main`
  - Added diagnostic logging for PDR instance status
- `Assets/Scripts/UI/VoxelChunkManager.cs` — Removed unused perf tracking fields (`perfLastActiveChunks`, etc.)

### Bug Fixed
- **Vehicle path beams not emitting**: `PathDebugRenderer` used `Camera.main` to find the render camera, but the voxel render camera (owned by `VoxelRenderBridge`) isn't tagged "MainCamera" in URP. Fix: `VoxelRenderBridge` passes its camera reference directly to `RenderBeamsIntoCamera(_camera)`.
- **Color bleeding across path types**: Setting `_Color` directly on `beamMaterial` caused all `CommandBuffer.DrawMeshInstanced` calls to use the last color set (deferred execution). Fix: Use `MaterialPropertyBlock` per draw call — same pattern as the instanced character MaterialPropertyBlock bug.
- **Pedestrian paths not shown by default**: `StressTestSpawner` started with beams off (level 0). Fix: Default to ALL level, auto-register after spawn, and periodically register agents that acquire paths async.

### Testing
- Press **F10** to toggle vehicle driving. Purple beams should appear showing the planned route.
- Console shows `[PathDebug]` diagnostic logs every 60 frames confirming active paths, batch counts, and draw calls.
- Beams composite on top of the voxel raymarch render (visible through the RawImage overlay).

---

## August 7, 2026 — Vehicle System: Generalized Instancing + RoadGraph + 1920s Touring Car Model

### Created
- `Assets/Scripts/Sim/RoadGraph.cs` — Vehicle pathfinding graph (street intersections as nodes, links between neighbors)
  - `GenerateFromLayout` builds a lattice grid of intersections aligned with city blocks
  - `RandomNodeId` / `RandomNeighbor` for basic random-walk navigation
- `Assets/Scripts/Sim/VoxelVehicle.cs` — Voxel vehicle component (analogous to VoxelCharacter)
  - Loads .stasset, registers with VoxelChunkManager's per-asset InstancedGroup
  - Uses `transform.localPosition` for mapRoot coordinate space consistency
  - `PlaceAtCenter` for external movement control
- `Assets/Scripts/Sim/VehicleTestSpawner.cs` — Test harness + VehicleAgent
  - F9 spawns N vehicles that randomly drive between RoadGraph intersections
  - VehicleAgent does endless random walk (pick random neighbor, drive there, repeat)
  - No AI/destination logic — pure navigation + rendering test
- `VoxelAssetStudio/procedural_mob_vehicles.py` — 1920s vehicle voxel generator
  - `generate_touring_car`: Ford Model T style touring car (20x16x30 voxels)
  - Open-top 4/5 seater: 2 bench seats (driver + 1 front, 2 rear passengers)
  - Artillery wheels (wooden spokes + iron tires), brass headlights, radiator grille
  - Running boards, fenders, spare tire, folding top supports
  - Interior sized to fit 4 characters at 0.05m/voxel scale
- `Assets/StreamingAssets/voxel_buildings/vehicle_civilian_car_0.stasset` — Exported voxel model (2,485 solid voxels, 19KB)
- `docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md` — Three-tier rendering philosophy document

### Changed
- `Assets/Scripts/UI/VoxelChunkManager.cs` — Generalized instanced character/vehicle rendering
  - Replaced singular shared buffer with `Dictionary<string, InstancedGroup>` keyed by asset filename
  - Each asset type gets its own shared voxel buffer + instance offset buffer + draw call
  - `RegisterInstancedCharacter` / `UnregisterInstancedCharacter` / `RenderInstancedCharacters` updated
  - `ReleaseAllInstancedGroups` for cleanup

### Testing
- Press **F9** in Play mode to spawn test vehicles on the RoadGraph
- Vehicles should render via the generalized InstancedGroup system and drive randomly between intersections
- Vehicle model: 20x16x30 at 0.05m/voxel = 1.0m x 0.8m x 1.5m mob sim scale
- **Auto-spawn**: VehicleTestSpawner auto-spawns on Start (parked, not moving). Vehicle appears at the road intersection nearest to player HQ (Vinny's office), visible during planning phase. CityMap3D.SpawnSceneCharacters adds the spawner to the scene if not present.
- **F10 = toggle driving** (F9 conflicts with StressTestDiagnostics stop key). Press F10 once to start driving, press again to park.

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
