# 📚 Steel City: Mob Sim — Documentation Index

**Purpose**: Central hub for all Mob Sim project documentation
**Last Updated**: August 4, 2026
**Project**: Steel City — Mob Sim (Unity)

---

## 🎯 Quick Navigation

| Category | Documents | Status |
|----------|-----------|--------|
| **Game Design** | 1 doc | ✅ Complete |
| **UI System** | 3 docs | ✅ Complete |
| **Voxel Buildings** | 3 docs | ✅ Complete |
| **Voxel Rendering** | 4 docs | ✅ Complete |
| **Lighting Debug** | 1 doc | ✅ Complete |
| **Scale Standard** | 1 doc | ✅ Complete |
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

### 5. Scale Standard ✅ COMPLETE

- **`MOB_SIM_SCALE_STANDARD.md`** — Mob Sim universe scale system
  - Core scale constants (building voxel = 0.1m, char voxel = 0.015m)
  - Scale ratio: 3.75× (real world → mob sim)
  - Standard door sizes (4v standard, 5v civic, 6v vehicle bay)
  - Reference object library (NPC, door, trash can, bench, street light, car, dumpster, tree)
  - Building height reference table
  - Relationship to Steel Tide FPS scale (independent systems)

**Keywords**: scale, proportions, doors, reference, NPC size, voxel size

---

### 6. Porting Notes ✅ COMPLETE

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

### Voxel Rendering & Lighting
- Lighting pipeline → `VOXEL_LIGHTING_AND_SHADOWS.md`
- Bleed-through fix → `VOXEL_BLEED_THROUGH_FIX.md`
- Sun/day-night cycle → `VOXEL_SUN_DAY_NIGHT.md`
- Camera controls → `VOXEL_CAMERA_SYSTEM.md`

### Scale & Proportions
- Scale standard → `MOB_SIM_SCALE_STANDARD.md`
- Door sizes → `MOB_SIM_SCALE_STANDARD.md` (Standard Door Sizes section)
- Reference objects → `MOB_SIM_SCALE_STANDARD.md` (Reference Object Library section)

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

---

## 🎯 Common Tasks — Quick Links

### "I need to add protrusion to a building"
`BUILDING_PROTRUSION_SYSTEM.md` (Adding Protrusion section)

### "I need to check door scale"
`MOB_SIM_SCALE_STANDARD.md` (Standard Door Sizes section)

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

---

## 🔧 For Coding Agents

### Key File Locations
```
Unity scripts:  Assets/Scripts/
  UI:           Assets/Scripts/UI/CityMap3D.cs, GameUIController.cs, VoxelChunkManager.cs, VoxelSun.cs
  Sim:          Assets/Scripts/Sim/StAssetReader.cs, VoxelBuildingMeshifier.cs, GameEngine.cs
  Bootstrap:    Assets/Scripts/GameBootstrap.cs
Compute shader: Assets/Resources/Shaders/MobSimVoxelRaymarch.compute
Voxel studio:   VoxelAssetStudio/
  Buildings:    procedural_mob_buildings.py
  Characters:   procedural_mob_characters.py
  Materials:    mob_materials.py
  I/O:          stasset_io.py
  City gen:     generate_city_assets.py
City layout:    Assets/StreamingAssets/city_layout.json
Voxel assets:   Assets/StreamingAssets/voxel_buildings/
```

### Key Values
- `voxelSize = 0.1` — world units per voxel (buildings)
- `voxelSize = 0.015` — world units per voxel (characters)
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

### Status Indicators
- ✅ COMPLETE — Fully documented, tested, working
- 🔄 IN PROGRESS — Being worked on
- ⚠️ PENDING — Planned but not started
- 🐛 NEEDS FIX — Has known issues

---

**Last Updated**: August 4, 2026
**Version**: 1.1.0
**Maintainer**: Development Team
