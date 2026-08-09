# Voxel Building Generation Methodology

**Created**: August 2, 2026
**Status**: Active
**Scope**: Documents the proprietary method used to generate 1920s-themed voxel buildings for Steel City: Mob Sim using Voxel Asset Studio's `.stasset` format and procedural Python generators.

---

## 1. Overview

The voxel buildings in Steel City were generated using a **procedural Python pipeline** that produces `.stasset` binary voxel files, which are then loaded at runtime by Unity C# scripts and converted to meshes. No external 3D modeling tools were used — all building geometry is defined algorithmically.

### Pipeline Flow

```
mob_materials.py          Material palette (56 materials, IDs 0/100-155)
        │
        ▼
procedural_mob_buildings.py   13 building generators (numpy uint16 arrays)
        │
        ▼
generate_city_assets.py       Reads city_template.json → batch exports .stasset files
        │                         + writes city_layout.json manifest
        ▼
*.stasset (18 files)      Binary voxel data (16-byte header + uint16 grid)
        │
        ▼
StAssetReader.cs           Unity runtime: parses .stasset → ushort[,,] voxel grid
        │
        ▼
VoxelBuildingMeshifier.cs  Face-culled mesh generation with per-vertex colors
        │
        ▼
VoxelVertexColor.shader    URP shader: renders vertex colors with directional lighting
        │
        ▼
CityMap3D.cs               Places buildings per block, handles click detection,
                           toggle between voxel/cube modes (V key)
```

---

## 2. Material Palette Design

**File**: `VoxelAssetStudio/mob_materials.py`

### 2.1 ID Allocation Strategy

Material IDs start at **100** to avoid conflicts with VoxelAssetStudio's existing sci-fi material library (`material_library.py`, IDs 0-21). ID 0 is reserved for Air (transparent).

| Range | Category | Count | Examples |
|-------|----------|-------|----------|
| 0 | Air | 1 | Air (transparent) |
| 100-109 | Masonry & Brick | 10 | Red Brick, Dark Brick, Tan Brick, Concrete, Stone Foundation, Stucco White/Cream, Asphalt, Sidewalk, Cobblestone |
| 110-117 | Wood & Trim | 8 | Dark Wood, Light Wood, Window Frame, Door Brown/Green/Red, Trim White/Dark |
| 120-125 | Glass & Neon | 6 | Window Glass, Lit Window, Storefront Glass, Neon Red/Blue/Green |
| 130-134 | Roofing | 5 | Tar Roof, Tile Roof, Metal Roof, Shingle Roof, Chimney Brick |
| 140-155 | Special/Business | 16 | Awning Red/Green/Striped, Sign Gold/Dark, Barber Pole, Police Blue, Casino Carpet, Speakeasy Dark, Garage Metal, Apartment Beige, Fire Escape, Water Tower, Street Lamp, Lamp Glow, HQ Accent |

### 2.2 Color Selection Method

Colors were chosen based on:
- **Historical 1920s urban palette**: muted earth tones, brick reds, cream stuccos, dark woods
- **Period-appropriate neon**: early neon signage was red and blue (first neon signs in US ~1923)
- **Functional coding**: police = blue accent, casino = red carpet, HQ = gold trim, speakeasy = dark/nondescript
- **Alpha channel**: glass materials use alpha < 1.0 for semi-transparency (0.5-0.6)

Each material is a 4-tuple `(R, G, B, A)` in 0.0-1.0 range, mirrored exactly in `StAssetReader.cs` as `Color` values.

### 2.3 Python ↔ Unity Sync

The material color table exists in **two places** that must stay in sync:
1. `mob_materials.py` — `MOB_MATERIALS` dictionary (Python source of truth)
2. `StAssetReader.cs` — `MobColors[256]` array (Unity runtime copy)

When adding or changing materials, update **both files**.

---

## 3. Procedural Building Generation

**File**: `VoxelAssetStudio/procedural_mob_buildings.py`

### 3.1 Voxel Grid Format

Each building is a **3D numpy array** of `uint16` values with shape `(width, height, depth)`:
- **X axis**: width (left-right)
- **Y axis**: height (vertical, 0 = ground)
- **Z axis**: depth (front-back, 0 = front facade facing camera)
- Each cell contains a material ID from `mob_materials.py`
- ID 0 = Air (empty space)

Default block tile size: **32×N×32** (width and depth fixed at 32, height varies per building type).

### 3.2 Construction Method

Buildings are constructed using a **layered approach**:

```
1. Initialize: grid = np.zeros((w, h, d), dtype=np.uint16)  → all air
2. Fill walls: grid[:, :, :] = WALL_MATERIAL                 → solid block
3. Foundation: _add_basement_foundation()                    → stone at bottom
4. Hollow:    _hollow_interior()                             → remove interior, keep walls
5. Storefront: _add_storefront()                             → glass + awning + door on front
6. Windows:   _add_windows_all_sides()                       → punched openings on all facades
7. Doorway:   _add_doorway()                                 → centered front door
8. Roof:      _add_flat_roof()                               → tar/concrete + parapet
9. Details:   chimney, water tower, fire escape, signs       → business-specific accents
```

### 3.3 Helper Functions

| Function | Purpose |
|----------|---------|
| `_hollow_interior(grid, w, h, d, wt, ft)` | Removes interior voxels, leaving walls of thickness `wt` and floor slab of thickness `ft` |
| `_add_windows_all_sides(grid, w, h, d, wt, y_start, y_end, spacing, win_size, material)` | Punches window openings on all 4 facades at regular intervals |
| `_add_doorway(grid, w, d, wt, door_h, door_w, material)` | Creates a centered doorway on the front wall (z=0) with frame |
| `_add_flat_roof(grid, w, h, d, wt, material)` | Adds a flat roof slab with a brick parapet edge |
| `_add_storefront(grid, w, d, wt, awning_mat)` | Large storefront windows + awning + door on ground floor front |
| `_add_basement_foundation(grid, w, d, wt)` | Stone foundation layer at the bottom |
| `_add_chimney(grid, w, h, d, cx, cz)` | 2×2 brick chimney extending above roof |

### 3.4 Building Types & Parameters

| Generator | Business Types | Dimensions (W×H×D) | Key Features |
|-----------|---------------|---------------------|--------------|
| `generate_butcher_shop` | butcher | 32×20×32 | Red brick, red awning, upper office windows, chimney, gold sign |
| `generate_bakery` | bakery | 32×18×32 | Cream stucco, green awning, gold sign |
| `generate_barbershop` | barber | 32×16×32 | White stucco, striped awning, barber pole, small profile |
| `generate_diner` | diner | 32×16×32 | Concrete + metal, full-width glass front, neon signs (red+blue), red door |
| `generate_garage` | garage | 32×14×32 | Corrugated metal walls, large vehicle door, small office window, metal roof |
| `generate_apartments` | apartments | 32×36×32 | 4-story red brick, fire escape, water tower, chimney, interior floor slabs |
| `generate_empty_land` | empty_land | 32×4×32 | Cobblestone ground, rubble piles, wood fence posts around perimeter |
| `generate_casino` | casino | 32×24×32 | Dark brick, large storefront, 3-band neon (red/blue/red), red carpet interior, gold parapet |
| `generate_speakeasy` | speakeasy, card_game, loan_shark | 32×18×32 | Nondescript tan brick, small window, green door, dark interior, lit upper windows |
| `generate_police_station` | police_station | 32×26×32 | Stone facade, columned entrance, blue accent band, "POLICE" sign, concrete roof |
| `generate_hq` | hq (player + rival) | 32×28×32 | Red brick, gold-trim awning, gold window frames, gold parapet accents, chimney |
| `generate_road_tile` | (utility) | 32×2×32 | Asphalt center with sidewalk borders |

### 3.5 Seeded Randomness

All generators accept a `seed` parameter. When provided, `np.random.seed(seed)` is called at the start. This ensures:
- **Reproducibility**: same seed → identical building
- **Deterministic exports**: `generate_city_assets.py` uses `hash(f"{block_id}_{biz_type}_{i}") % 10000` as seed
- **Variation**: different blocks get different seeds, producing visually distinct buildings of the same type

### 3.6 Building Registry

```python
BUILDING_GENERATORS = {
    "butcher":      generate_butcher_shop,
    "bakery":       generate_bakery,
    "barber":       generate_barbershop,
    "diner":        generate_diner,
    "garage":       generate_garage,
    "apartments":   generate_apartments,
    "empty_land":   generate_empty_land,
    "casino":       generate_casino,
    "speakeasy":    generate_speakeasy,
    "card_game":    generate_speakeasy,
    "loan_shark":   generate_speakeasy,
    "police_station": generate_police_station,
    "hq":           generate_hq,
}
```

Illegal businesses (card_game, loan_shark) reuse the speakeasy generator — they're front operations that share the same nondescript exterior.

---

## 4. Batch Export

**File**: `VoxelAssetStudio/generate_city_assets.py`

### 4.1 Process

1. Reads `city_template.json` from `Assets/StreamingAssets/`
2. For each block:
   - Detects special blocks (player_hq, rival_hq, police_station) → generates special building
   - For each business entry: looks up generator, generates voxel grid, saves as `.stasset`
   - Skips `empty_land` if block already has a special building
   - If block has no buildings at all, generates an `empty_land` fallback
3. Writes `city_layout.json` manifest with block→building mappings
4. Output directory: `Assets/StreamingAssets/voxel_buildings/`

### 4.2 Naming Convention

- Special buildings: `{type}_{block_id}.stasset` (e.g., `hq_block_3.stasset`)
- Regular buildings: `{business_type}_{index}.stasset` (e.g., `apartments_0.stasset`)
- Indices are global counters per business type, ensuring unique filenames

### 4.3 city_layout.json Structure

```json
{
  "blocks": [
    {
      "block_id": "block_1",
      "block_name": "NW Block",
      "row": 0,
      "col": 0,
      "buildings": [
        { "type": "apartments", "stasset": "voxel_buildings/apartments_0.stasset", "slot": 0 },
        { "type": "apartments", "stasset": "voxel_buildings/apartments_1.stasset", "slot": 1 }
      ]
    }
  ],
  "building_types": { "apartments": [32, 36, 32], ... }
}
```

---

## 5. .stasset Binary Format

**File**: `VoxelAssetStudio/stasset_io.py` (writer), `Assets/Scripts/Sim/StAssetReader.cs` (reader)

### 5.1 Header (16 bytes)

| Offset | Size | Field | Value |
|--------|------|-------|-------|
| 0 | 4 | Magic | `STAS` (0x53 0x54 0x41 0x53) |
| 4 | 1 | Version | 1 |
| 5 | 1 | Flags | 0 (reserved) |
| 6 | 2 | Width | uint16 LE |
| 8 | 2 | Height | uint16 LE |
| 10 | 2 | Depth | uint16 LE |
| 12 | 4 | Reserved | 0x00000000 |

### 5.2 Voxel Data

- Starts at byte offset 16
- Each voxel is a **uint16** (2 bytes, little-endian)
- Total size: `width * height * depth * 2` bytes
- Ordering: **X-major (Fortran order)** — iterate X first, then Y, then Z
- Value = material ID (0 = air, 100+ = mob materials)

### 5.3 Optional Skeleton Block

Not used for Steel City buildings. The `stasset_io.py` writer supports an optional JSON skeleton appended after voxel data, but `generate_city_assets.py` does not use it.

---

## 6. Unity Runtime Pipeline

### 6.1 StAssetReader.cs

**Location**: `Assets/Scripts/Sim/StAssetReader.cs`

- `LoadVoxels(filepath)` → reads binary file, parses header, returns `ushort[,,]`
- `LoadAsMesh(filepath, voxelSize)` → loads voxels then calls `VoxelBuildingMeshifier.BuildMesh()`
- Static constructor initializes `MobColors[256]` array matching `mob_materials.py`
- `GetMaterialColor(ushort id)` → returns `Color` for a material ID

### 6.2 VoxelBuildingMeshifier.cs

**Location**: `Assets/Scripts/Sim/VoxelBuildingMeshifier.cs`

**Algorithm**: Face-culled voxel mesh generation

For each non-air voxel at `(x, y, z)`:
1. Check all 6 face directions
2. If the neighbor in that direction is air or out-of-bounds, generate a quad
3. Each quad = 4 vertices + 2 triangles, with color and normal from the material/face direction
4. Mesh is centered at origin (X and Z centered, Y starts at 0 = ground level)

**Complexity**: O(n) where n = total voxels. Only visible faces are generated (typically 10-20% of total voxel count).

**Output**: `Mesh` with `vertices`, `triangles`, `colors`, `normals` arrays. Uses `IndexFormat.UInt32` for large buildings.

### 6.3 VoxelVertexColor.shader

**Location**: `Assets/Shaders/VoxelVertexColor.shader`

- URP-compatible HLSL shader with non-URP CG fallback
- Renders per-vertex colors (from voxel materials) with simple directional lighting
- Lighting: `NdotL * 0.6 + 0.4 ambient` with fixed light direction `(0.5, 0.7, -0.3)`
- Supports fog integration

### 6.4 CityMap3D.cs Integration

**Location**: `Assets/Scripts/UI/CityMap3D.cs`

- **Dual rendering modes**: voxel (default) and cube (original)
- **Runtime toggle**: press **V** key to switch modes
- **Voxel mode**: loads `city_layout.json`, places `.stasset` buildings per block
  - Single building per block: centered
  - Multiple buildings per block: arranged in sub-grid (sqrt layout)
  - Ground tile under each block for click detection
  - Labels positioned above tallest building
- **Cube mode**: unchanged from original (colored cubes)
- **Fallback**: if `city_layout.json` missing, auto-falls back to cube mode
- **Caching**: voxel files loaded once, height cached per building path

---

## 7. File Inventory

### Python (VoxelAssetStudio/)

| File | Lines | Purpose |
|------|-------|---------|
| `mob_materials.py` | ~120 | Material palette (56 materials) + convenience constants |
| `procedural_mob_buildings.py` | ~300 | 13 building generators + helper functions + registry |
| `generate_city_assets.py` | ~170 | Batch export: city_template.json → .stasset files + city_layout.json |

### Unity (Steel_City-Mob_Sim/Assets/)

| File | Lines | Purpose |
|------|-------|---------|
| `Scripts/Sim/StAssetReader.cs` | ~150 | .stasset binary parser + material color table |
| `Scripts/Sim/VoxelBuildingMeshifier.cs` | ~130 | Voxel grid → Mesh converter (face culling) |
| `Scripts/UI/CityMap3D.cs` | ~425 | Dual-mode city renderer (voxel/cube toggle) |
| `Shaders/VoxelVertexColor.shader` | ~100 | URP vertex color shader |

### Generated Assets (StreamingAssets/)

| File | Count | Purpose |
|------|-------|---------|
| `voxel_buildings/*.stasset` | 18 | Binary voxel buildings |
| `city_layout.json` | 1 | Block → building manifest |

---

## 8. How to Regenerate Buildings

```bash
cd VoxelAssetStudio
python generate_city_assets.py
```

This reads `city_template.json`, regenerates all `.stasset` files and `city_layout.json`. Unity will pick up the new files on next Play (no Unity rebuild needed — files are in StreamingAssets).

To test individual generators:

```bash
cd VoxelAssetStudio
python procedural_mob_buildings.py
```

Prints voxel counts and dimensions for each building type.

---

## 9. How to Add a New Building Type

1. **Add materials** (if needed) to `mob_materials.py` and mirror in `StAssetReader.cs`
2. **Write generator** in `procedural_mob_buildings.py`:
   ```python
   def generate_new_shop(w=BLOCK_W, h=20, d=BLOCK_D, seed=None):
       if seed is not None: np.random.seed(seed)
       grid = np.zeros((w, h, d), dtype=np.uint16)
       grid[:, :, :] = RED_BRICK
       _add_basement_foundation(grid, w, d, WALL_T)
       _hollow_interior(grid, w, h, d, WALL_T, FLOOR_T)
       _add_storefront(grid, w, d, WALL_T)
       _add_windows_all_sides(grid, w, h, d, WALL_T, 10, 16)
       _add_flat_roof(grid, w, h, d, WALL_T)
       return grid
   ```
3. **Register** in `BUILDING_GENERATORS` dict
4. **Add to city_template.json** if it should appear in the city
5. **Run** `python generate_city_assets.py`
6. Unity picks it up automatically on next Play

---

## 10. Design Decisions & Rationale

### Why procedural over hand-modeled?
- **Scalability**: 100+ blocks needed for full city — procedural generation scales instantly
- **Consistency**: all buildings share the same material palette and construction logic
- **Reproducibility**: seeded generation means deterministic outputs
- **Iteration speed**: change a generator function → re-run → all buildings of that type update

### Why face-culled meshing instead of greedy meshing?
- **Simplicity**: face culling is O(n) and trivially correct
- **Visual fidelity**: each voxel face retains its own color (greedy meshing would merge same-color faces, which is fine but more complex)
- **Performance**: for 32×36×32 buildings (~37K voxels), face culling produces manageable vertex counts
- **Future upgrade path**: `VoxelBuildingMeshifier.BuildMesh()` can be replaced with a greedy meshing implementation without changing any other code

### Why .stasset binary instead of JSON?
- **File size**: binary is 2 bytes/voxel vs ~10+ bytes/voxel for JSON
- **Load speed**: binary read + memcpy is faster than JSON parsing
- **Existing format**: Voxel Asset Studio already used `.stasset` — we leveraged the existing infrastructure

### Why vertex colors instead of textures?
- **No texture authoring needed**: colors come directly from material IDs
- **Infinite variation**: each voxel can have a unique color without texture atlas management
- **Simple shader**: one shader, no UV mapping, no texture sampling
- **Future upgrade path**: could add a texture atlas lookup in the shader based on material ID
