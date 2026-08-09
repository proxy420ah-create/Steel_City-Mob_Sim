# Voxel Asset Pipeline

**Purpose**: Defines the workflow for creating, reviewing, and deploying voxel models
**Last Updated**: August 8, 2026

---

## Pipeline Overview

```
1. CREATE    →  Procedural Python generators produce voxel grids
2. REVIEW    →  Inspect in VoxelAssetStudio (VS) for visual approval
3. APPROVE   →  User confirms model looks correct
4. DEPLOY    →  Regenerate .stasset files into StreamingAssets
5. VERIFY    →  Load in-game and confirm rendering
```

---

## Step 1: CREATE — Procedural Generation

**Location**: `VoxelAssetStudio/`

**Key Files**:
- `procedural_mob_buildings.py` — Building generators (tenement, barbershop, etc.)
- `procedural_mob_characters.py` — Character generators (hoodlum, overcoat, etc.)
- `procedural_mob_vehicles.py` — Vehicle generators
- `mob_materials.py` — 1920s material palette (IDs 100-129)
- `stasset_io.py` — .stasset file format read/write (v2 with metadata)

**Process**:
- Generators produce numpy arrays of `uint16` material IDs
- Each generator returns `(grid, metadata)` tuple
- Metadata includes door face, dimensions, orientation info
- Run `python generate_city_assets.py` to batch-generate all buildings

**Scale Standards**:
| Asset Type | Voxel Size | Grid Size | World Height |
|------------|-----------|-----------|-------------|
| Buildings  | 0.1f      | 96×44×96  | 4.4m        |
| Characters | 0.02f     | 16×32×10  | 0.64m       |
| Vehicles   | 0.05f     | varies    | varies      |

**Door Standard**:
- Height: 8v (0.8m at building scale)
- Width: 4v (0.4m) — slimmed from 6v for character proportion
- Tenement grand entrance: 8v wide (double door)

---

## Step 2: REVIEW — VoxelAssetStudio

**Launch**: Double-click `run_voxel_studio.bat` in VoxelAssetStudio folder

**What VS Does**:
- Opens .stasset files and renders voxel grids in 3D viewport
- Uses `material_library.py` for colors (merges both sci-fi 0-21 and mob 100-129)
- Supports hand-editing: paint, fill, shape tools, transform

**Review Checklist**:
- [ ] Materials render with correct colors (not white = unknown material)
- [ ] Proportions look correct at intended voxel size
- [ ] Door sizes are consistent across buildings
- [ ] Decorations (fire escapes, canopies, balconies) protrude correctly
- [ ] Building fits within city block (96×96 for full-block buildings)
- [ ] Metadata is correct (door_face, door_height, door_width)

**Scale Reference Files**:
- `scale_reference_vinny_door.stasset` — Vinny next to standard door at true world scale
- Use `gen_scale_reference.py` to regenerate after door size changes
- Characters are downscaled 5x (0.02f → 0.1f) to match building voxel size

**Analysis Tool**:
- `python analyze_building.py <building_type>` — scans for protrusions, buffer needs, block fit
- `python analyze_building.py --all` — analyze all building types

---

## Step 3: APPROVE — User Confirmation

**Gate**: No model moves to deployment without explicit user approval.

**What to Verify**:
- Visual appearance in VS matches expectations
- Scale references confirm proportions
- Analysis tool reports no oversized dimensions or missing buffers
- User says "looks good" or similar approval

---

## Step 4: DEPLOY — Regenerate StreamingAssets

**Command**:
```
cd VoxelAssetStudio
python generate_city_assets.py
```

**Output**: `.stasset` files written to:
```
Assets/StreamingAssets/voxel_buildings/
```

**What Happens**:
- All building generators run with fixed seeds
- Each model saved as .stasset v2 (voxel data + metadata)
- Metadata embedded for Unity to read door orientation, dimensions
- C# `StAssetReader` reads voxel data, ignores trailing metadata bytes

---

## Step 5: VERIFY — In-Game Check

**Process**:
- Compile Unity project
- Enter play mode
- Navigate to building locations in city
- Confirm:
  - [ ] Buildings render with correct materials
  - [ ] Doors face the street
  - [ ] Fire escapes/balconies visible on correct faces
  - [ ] No overlapping or oversized models
  - [ ] Characters move through doors correctly

---

## File Locations Summary

| Purpose | Location |
|---------|----------|
| Generators | `VoxelAssetStudio/procedural_mob_*.py` |
| Materials | `VoxelAssetStudio/mob_materials.py` |
| File I/O | `VoxelAssetStudio/stasset_io.py` |
| Batch gen | `VoxelAssetStudio/generate_city_assets.py` |
| Analysis | `VoxelAssetStudio/analyze_building.py` |
| Scale ref | `VoxelAssetStudio/gen_scale_reference.py` |
| VS launcher | `VoxelAssetStudio/run_voxel_studio.bat` |
| Game assets | `Assets/StreamingAssets/voxel_buildings/*.stasset` |
| Unity reader | `Assets/Scripts/Sim/StAssetReader.cs` |

---

## Common Workflows

### Adjusting Door Size
1. Edit `door_w` in generator functions (`procedural_mob_buildings.py`)
2. Run `python gen_scale_reference.py` to preview Vinny + door in VS
3. Review in VS — confirm proportions
4. If approved: `python generate_city_assets.py` to deploy
5. Verify in-game

### Adding a New Building Type
1. Write `generate_new_building()` in `procedural_mob_buildings.py`
2. Add to `BUILDING_GENERATORS` dict
3. Return `(grid, meta)` tuple with door metadata
4. Run `python analyze_building.py new_type` to check dimensions
5. Review in VS
6. If approved: `python generate_city_assets.py`
7. Verify in-game

### Hand-Editing a Model
1. Launch VS via `run_voxel_studio.bat`
2. Open .stasset from StreamingAssets
3. Edit with paint/fill/shape tools
4. Save back to .stasset
5. Verify in-game
6. Note: hand-edits are overwritten by next `generate_city_assets.py` run — update the procedural generator to make changes permanent
