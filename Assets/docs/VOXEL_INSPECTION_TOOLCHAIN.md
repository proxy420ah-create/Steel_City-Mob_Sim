# Voxel Inspection Toolchain

**Version**: 1.0 | **Date**: August 8, 2026 | **Status**: ✅ COMPLETE

---

## Purpose

This document is the **operational guide** for the voxel model inspection and diagnostic
toolchain in Steel City: Mob Sim. It covers every CLI script and GUI tool available for
auditing `.stasset` model files — what each tool does, when to use it, how to run it,
and how to interpret results.

This doc pairs with `MODEL_DESIGN_STANDARD.md` (the source of truth for scale, proportions,
and orientation). The design standard defines *what* a correct model looks like; this
document defines *how to verify* it.

---

## Tool Inventory

### Unified Inspector (Primary Tool)

| Tool | Location | Type |
|---|---|---|
| **`sc_inspector.py`** | `VoxelAssetStudio/sc_inspector.py` | CLI — all-in-one |

The unified inspector runs all quality checks in a single pass. **This is the tool you
should reach for first** when auditing any model.

### Legacy / Specialized Tools

| Tool | Location | Purpose |
|---|---|---|
| `stasset_inspector.py` | `VoxelAssetStudio/toolbox/` | ASCII cross-sections + symmetry (SteelTide actor rig focus) |
| `hexdump_stasset.py` | `VoxelAssetStudio/` | Raw byte-level inspection (header fields, voxel indices) |
| `diagnose_stasset.py` | `VoxelAssetStudio/` | Structural diagnosis (dims, voxel count, vertical slice) |
| `debug_dimensions.py` | `VoxelAssetStudio/` | Quick dimension check across multiple files |
| `inspect_city_materials.py` | `VoxelAssetStudio/` | Material histogram + color audit for city buildings |
| `dump_joints.py` | `VoxelAssetStudio/` | Joint/bone dumper for skeleton-rigged models |

### GUI Editor

| Tool | Location | Purpose |
|---|---|---|
| **Voxel Asset Studio** | `VoxelAssetStudio/voxel_editor.py` | Full PyQt6+OpenGL voxel editor with paint, fill, shape tools |

---

## 1. Unified Inspector (`sc_inspector.py`)

### Quick Start

```bash
cd VoxelAssetStudio

# Inspect the default model (vehicle)
python sc_inspector.py

# Inspect a specific file
python sc_inspector.py ../Assets/StreamingAssets/voxel_buildings/barber_0.stasset

# Analysis only (no ASCII art)
python sc_inspector.py --compact ../Assets/StreamingAssets/voxel_buildings/barber_0.stasset

# Force model type (skip auto-detection)
python sc_inspector.py --type building ../Assets/StreamingAssets/voxel_buildings/barber_0.stasset

# Run only specific checks
python sc_inspector.py --checks symmetry,materials ../Assets/StreamingAssets/voxel_buildings/barber_0.stasset

# Batch inspect all .stasset files in a directory
python sc_inspector.py --batch ../Assets/StreamingAssets/voxel_buildings/
```

### Checks Performed

The unified inspector runs 10 checks, producing a PASS/WARN/FAIL summary:

| # | Check | What It Tests | Applies To |
|---|---|---|---|
| 1 | **Dimensions** | Voxel grid size + real-world meters + fill % | All |
| 2 | **Materials** | Histogram of all material IDs, flags unknown IDs, very dark materials (< 0.10 brightness), low alpha | All |
| 3 | **Scale Validation** | W/H/D vs expected ranges from `MODEL_DESIGN_STANDARD.md` Section 5 | All |
| 4 | **Door Height** | Front-face door opening height ÷ NPC height (must be ≥ 1.25×). Distinguishes doors (3-12v wide) from storefronts (glass behind) and missing walls (no glass) | Buildings only |
| 5 | **Orientation** | Front-facing features at correct Z end (buildings Z=0, vehicles +Z, characters low Z) | All |
| 6 | **Symmetry** | Left/right X-axis mirror per Y-slice. Handles both even and odd width grids | All |
| 7 | **Proportions** | W/H and D/H ratios vs expected values, ±25% tolerance | All |
| 8 | **Wall Closure** | Exterior wall faces for unexpected AIR gaps above ground level (not windows/doors) | Buildings, vehicles |
| 9 | **Internal Holes** | Interior air voxels surrounded by 5+ solid neighbors (unexpected cavities) | All |
| 10 | **ASCII Views** | Front (X-Y), Side (Z-Y), and Top (X-Z at mid-height) cross-sections with material symbols | All (unless `--compact`) |

### Reading the Output

```
--- Summary ---
Summary: 7 PASS, 1 WARN, 0 FAIL
  Scale validation          PASS
  Material validity         PASS
  Door height ratio         PASS
  Orientation               PASS
  Symmetry                  WARN (12 slices)    ← barber pole (intentional)
  Proportions               PASS
  Wall closure              PASS
  Internal holes            PASS
```

- **PASS**: Check passed cleanly.
- **WARN**: Check found something but it's likely intentional (small asymmetry, low-alpha glass, few wall gaps). Review to confirm.
- **FAIL**: Check found a real problem. Fix before merging.

### Model Type Auto-Detection

The inspector auto-detects model type from dimensions:

| Type | Detection Rule | Voxel Size |
|---|---|---|
| `character` | W ≤ 20, H ≥ 28, D ≤ 12 | 0.02m |
| `vehicle` | W ≤ 24, D ≤ 34, H ≤ 20 | 0.05m |
| `building_l` | W ≥ 80 | 0.1m |
| `building_a` | H ≥ 32 | 0.1m |
| `building_c` | H ≥ 22 | 0.1m |
| `building_s` | (default) | 0.1m |

Override with `--type building_s`, `--type vehicle`, etc.

### Door Height Check Details

The door check scans Z=0 (front face) for vertical AIR columns starting from ground level:

- **3-12 voxels wide**: Classified as a **door**. Height is checked against the 1.25× NPC ratio.
- **>12 voxels wide**: Classified as a **large opening**. The inspector then checks Z=1..3 for
  glass materials (112, 113, 114). If glass is found, it's labeled "storefront (glass behind)" —
  legitimate. If no glass, it's flagged "POSSIBLE MISSING WALL".

### Material Symbol Legend (ASCII Views)

| Symbol | Material | ID |
|---|---|---|
| ` ` | Air | 0 |
| `#` | Red Brick | 100 |
| `S` | Stone | 101 |
| `C` | Concrete | 102 |
| `s` | Stucco | 103 |
| `-` | Asphalt | 104 |
| `c` | Cobblestone | 105 |
| `w` | Dark Wood | 106 |
| `W` | Light Wood | 107 |
| `o` | Weathered Wood | 108 |
| `I` | Dark Iron | 109 |
| `i` | Aged Metal | 110 |
| `M` | Painted Metal | 111 |
| `g` | Window Glass | 112 |
| `L` | Lit Window | 113 |
| `G` | Storefront Glass | 114 |
| `R` | Neon Red | 115 |
| `U` | Neon Blue | 116 |
| `N` | Neon Green | 117 |
| `T` | Tar | 118 |
| `t` | Terracotta | 119 |
| `r` | Painted Red | 120 |
| `n` | Painted Green | 121 |
| `b` | Painted Brown | 122 |
| `$` | Gold/Brass | 123 |
| `*` | Lamp Glow | 124 |
| `F` | Flesh | 125 |
| `K` | Black Fabric | 126 |
| `H` | White Fabric | 127 |
| `h` | Hair | 128 |
| `u` | Painted Blue | 129 |

---

## 2. Legacy CLI Tools

### `hexdump_stasset.py` — Raw Byte Inspector

```bash
python hexdump_stasset.py                              # default: vehicle
python hexdump_stasset.py path/to/model.stasset
```

Reads raw bytes: magic field, version, flags, dimensions, then voxel values at specific
indices with material name lookup. Use when you suspect file format corruption or need
to verify the binary header.

### `diagnose_stasset.py` — Structural Diagnosis

```bash
python diagnose_stasset.py                             # default: barber + apartment_block + vehicle
python diagnose_stasset.py file1.stasset file2.stasset
```

Loads via `stasset_io`, reports dimensions, shape, total/solid voxel counts, and a vertical
slice view showing material at each Y level. Lighter than the full inspector.

### `debug_dimensions.py` — Quick Dimension Check

```bash
python debug_dimensions.py
```

Loads a hardcoded list of files and prints dimensions + solid voxel counts. Quick sanity
check across all model types. No CLI args (edit the file list in-script).

### `inspect_city_materials.py` — Material Histogram

```bash
python inspect_city_materials.py                       # default: key city buildings
python inspect_city_materials.py file1.stasset
```

Full material histogram with per-layer breakdown (roof, parapet, ground). Flags very dark
materials and unknown IDs. Checks color palette validity.

### `toolbox/stasset_inspector.py` — ASCII Inspector (Legacy)

```bash
python toolbox/stasset_inspector.py path/to/model.stasset
python toolbox/stasset_inspector.py path/to/model.stasset --compact
```

The original ASCII inspector. Still useful for skeleton-rigged models (bone width analysis,
joint hierarchy, hole detection in spine/legs). Includes Mob Sim material symbols.

### `dump_joints.py` — Joint/Bone Dumper

```bash
python dump_joints.py path/to/model.stasset
```

Prints all joints (type, axis, angle ranges) and bones (name, role, side, parent/child)
plus the root joint. Only useful for v2 `.stasset` files with skeleton data.

---

## 3. Voxel Asset Studio (GUI Editor)

### Launch

```bash
cd VoxelAssetStudio
python main.py
# Or double-click VoxelStudio.bat in the parent folder
```

### Controls

- **Left-click**: Paint / interact with voxels
- **Middle-click**: Pan camera
- **Right-click**: Orbit camera
- **Mouse wheel**: Zoom
- **Ctrl+O**: Open file
- **Ctrl+S**: Save file
- **Ctrl+Shift+S**: Save as
- **Ctrl+Q**: Quit

### Workflow

```
1. Launch Voxel Asset Studio
2. Open or create a .stasset model
3. Paint/modify voxels (material selector in left panel)
4. Save (Ctrl+S) — file writes to disk
5. Switch to Unity — auto-reloads the asset
6. Press Play → see changes live
```

---

## 4. Model Audit Workflow

Follow this process before merging any new or reworked model:

### Step 1: Generate the Model

Run the procedural generator or edit in Voxel Asset Studio. Save the `.stasset` file to
`Assets/StreamingAssets/voxel_buildings/`.

### Step 2: Run the Unified Inspector

```bash
cd VoxelAssetStudio
python sc_inspector.py --compact ../Assets/StreamingAssets/voxel_buildings/your_model.stasset
```

Review the summary. Any FAIL must be fixed before proceeding.

### Step 3: Visual Cross-Section Check

```bash
python sc_inspector.py ../Assets/StreamingAssets/voxel_buildings/your_model.stasset
```

Review the ASCII front, side, and top views. Look for:
- Recognizable silhouette (does it look like the thing it's supposed to be?)
- Features at the correct end (storefront at Z=0 for buildings, headlights at +Z for vehicles)
- No scattered noise or obvious missing sections

### Step 4: Cross-Check Against Design Standard

Open `MODEL_DESIGN_STANDARD.md` and verify:
- **Section 2**: Voxel size matches the model type
- **Section 3**: Door height passes the 1.25× NPC ratio (buildings)
- **Section 4**: Orientation matches the convention for this model type
- **Section 5**: Dimensions fall within the expected W×H×D ranges

### Step 5: Unity Visual Test

Load the model in Unity, press Play, and verify:
- Model renders at the correct scale relative to Vinny
- Orientation is correct (storefront faces the street, vehicle faces forward)
- Materials look right (no invisible voxels, no wrong colors)
- Door openings are walkable by the NPC

### Step 6: Update Audit Table

If the model is a new type or a rework, update the audit table in `MODEL_DESIGN_STANDARD.md`
Section 6 with the certification status.

---

## 5. Common Issues & How to Spot Them

### Open Doors on Vehicle (the "see-through interior" bug)

**Symptom**: ASCII front view shows interior materials (wood seats, dashboard) through
what should be solid door panels.

**Check**: Wall closure check flags AIR gaps on X=0 or X=w-1 faces above ground level.

**Fix**: Fill door openings with solid material (e.g., `PAINTED_GREEN` for body-colored
doors) in the generator. Add detail (pillar seams, handles) for visual interest.

### Door Too Short for NPC

**Symptom**: Door height ratio < 1.25× in the door height check.

**Cause**: Generator uses 4v or 5v door height instead of the standard 8v (pedestrian)
or 10-12v (civic).

**Fix**: Update the generator to use 8v minimum door height. See `MODEL_DESIGN_STANDARD.md`
Section 3.

### Asymmetry

**Symptom**: Symmetry check reports mismatched voxels on multiple Y-slices.

**Interpretation**:
- 1-5 voxel mismatch on a few slices: Likely intentional (steering wheel, barber pole,
  one-sided sign). Confirm by checking which materials are asymmetric.
- 10+ voxel mismatch on many slices: Real generation bug. Check the generator for
  hardcoded one-sided features that should be symmetric.

### Missing Wall Panels

**Symptom**: Wall closure check flags gaps on exterior faces. Door height check reports
"POSSIBLE MISSING WALL" for large openings with no glass behind.

**Fix**: Fill the gaps in the generator. If the opening is a storefront, add glass
material (114) at Z=1..2 behind the opening.

### Wrong Orientation

**Symptom**: Orientation check reports features at the wrong Z end.

**Fix**: Check the generator's Z-axis placement. Buildings: storefront at Z=0. Vehicles:
headlights at +Z. Characters: face at low Z. Do NOT flip buildings to match vehicles —
they use opposite conventions intentionally (see `MODEL_DESIGN_STANDARD.md` Section 4).

---

## 6. File Format Reference

### `.stasset` Binary Format

```
Header (16 bytes):
├─ Magic: "STAS" (4 bytes)
├─ Version: uint16 (1 = voxels only, 2 = + skeleton)
├─ Flags: uint16
├─ Dimensions: width, height, depth (uint16 × 3)
└─ Reserved: 4 bytes

Voxel Data:
└─ width × height × depth × 2 bytes (uint16, little-endian, X-major / Fortran order)

Optional Skeleton Block (v2 only):
├─ Magic: "SKEL" (4 bytes)
├─ JSON length: uint32 LE
└─ JSON payload: {version, bones, joints, influence_map, attachments}
```

### Grid Indexing

```python
grid = np.zeros((width, height, depth), dtype=np.uint16)
grid[x, y, z] = material_id  # X=width, Y=height (0=ground), Z=depth
```

- **X** = width (left-right)
- **Y** = height (vertical, 0 = ground/lowest point)
- **Z** = depth (front-back)

Python writes X-major (Fortran order). Unity C# must read X fastest. See
`VOXEL_ORDERING_FIX.md` for the critical fix that resolved character scrambling.

---

## 7. Baseline Results (Aug 8, 2026)

Models verified with the unified inspector on Aug 8, 2026:

### Characters

| Model | Dims (W×H×D) | Summary | Notes |
|---|---|---|---|
| `character_hoodlum_0` | 16×32×10 | 8 PASS, 0 WARN, 0 FAIL | Perfect symmetry, all materials valid |
| `character_hoodlum_overcoat_0` | 20×32×14 | 8 PASS, 0 WARN, 0 FAIL | Overcoat variant (wider/deeper), perfect symmetry |
| `character_police_0` | 16×32×10 | 8 PASS, 0 WARN, 0 FAIL | Perfect symmetry, all materials valid |
| `character_civilian_0` | 16×28×10 | 7 PASS, 0 WARN, 1 FAIL | Scale FAIL: H=28 (expected 28-32, at edge). Proportions OK. May need height bump to 32. |

### Vehicles

| Model | Dims (W×H×D) | Summary | Notes |
|---|---|---|---|
| `vehicle_civilian_car_0` | 20×16×30 | 5 PASS, 2 WARN, 0 FAIL | Symmetry WARN = steering wheel (intentional). Wall WARN = door window openings (intentional) |

### Buildings — Certified Good

| Model | Type | Dims (W×H×D) | Summary | Notes |
|---|---|---|---|---|
| `barber_0` | building_s | 32×20×34 | 7 PASS, 1 WARN, 0 FAIL | Symmetry WARN = barber pole. Door 6v×10v, 1.56× ratio. Storefront glass detected. |
| `apartments_0` | building_a | 32×36×34 | 7 PASS, 1 WARN, 0 FAIL | Symmetry WARN = window placement. Storefront glass detected. |
| `apartment_block_0` | building_l | 96×44×98 | 6 PASS, 1 WARN, 1 FAIL | Symmetry WARN = window variation. Wall closure FAIL = 9 front-face gaps (windows). Storefronts detected. |
| `tenement_block_0` | building_l | 96×44×98 | 7 PASS, 1 WARN, 0 FAIL | Same generator as apartment_block. Symmetry WARN = window variation. |
| `butcher_0` | building_s | 32×20×32 | 8 PASS, 0 WARN, 0 FAIL | Perfect symmetry, all checks pass |
| `diner_0` | building_s | 32×16×32 | 8 PASS, 0 WARN, 0 FAIL | Perfect symmetry, all checks pass |
| `speakeasy_0` | building_s | 32×18×32 | 8 PASS, 0 WARN, 0 FAIL | Perfect symmetry, all checks pass |
| `bakery_0` | building_s | 32×18×34 | 8 PASS, 0 WARN, 0 FAIL | Storefront glass detected. Perfect symmetry. |
| `casino_0` | building_c | 32×24×32 | 8 PASS, 0 WARN, 0 FAIL | Perfect symmetry, all checks pass |
| `police_0` | building_c | 32×26×34 | 8 PASS, 0 WARN, 0 FAIL | 3 door openings detected, all pass ratio. Perfect symmetry. |
| `hq_0` | building_c | 32×28×32 | 8 PASS, 0 WARN, 0 FAIL | Perfect symmetry, all checks pass |
| `garage_0` | building_s | 32×14×32 | 7 PASS, 0 WARN, 1 FAIL | Proportion FAIL: W/H=2.286 (expected 1.78, drift=0.51). Garage is intentionally low/squat — may need its own proportion class. |

### Buildings — Non-Standard / Special

| Model | Type | Dims (W×H×D) | Summary | Notes |
|---|---|---|---|---|
| `restaurant_0` | building_l (auto) | 96×24×34 | 6 PASS, 0 WARN, 2 FAIL | Scale FAIL: H=24, D=34 (expected 40-50, 80-100). Proportion FAIL: W/H=4.0. This is a wide, low building — not a standard apartment block. Needs its own type entry. |
| `courtyard_0` | building_s (auto) | 32×4×32 | 5 PASS, 1 WARN, 2 FAIL | Scale FAIL: H=4. Proportion FAIL: W/H=8.0. Courtyard is a flat open-space tile, not a building — needs its own type entry. |

### Buildings Still Needing Door Height Rework

Per `MODEL_DESIGN_STANDARD.md` Section 6, these buildings have doors that fail the 1.25×
NPC height ratio and need rework:

- `apartments` (4v door → needs 8v)
- `diner` (4v door → needs 8v)
- `speakeasy` (4v door → needs 8v)
- `hq` side door (4v → needs 8v; storefront door is already 8v)
- `casino` (5v door → needs 10-12v civic)
- `police_station` (5v door → needs 10-12v civic)

### Type System Gaps Identified

The inspector's auto-detection and proportion tables need new entries for:
- **`restaurant`**: Wide low building (96×24×34) — doesn't fit `building_l` (too short) or `building_s` (too wide)
- **`courtyard`**: Flat tile (32×4×32) — not a building at all, needs exemption or its own class
- **`garage`**: Low squat building (32×14×32) — H=14 is at the edge of `building_s` range, proportions drift

---

## File References

- **Unified inspector**: `VoxelAssetStudio/sc_inspector.py`
- **Legacy inspector**: `VoxelAssetStudio/toolbox/stasset_inspector.py`
- **I/O library**: `VoxelAssetStudio/stasset_io.py`
- **Material definitions**: `VoxelAssetStudio/mob_materials.py`
- **Building generators**: `VoxelAssetStudio/procedural_mob_buildings.py`
- **Character generators**: `VoxelAssetStudio/procedural_mob_characters.py`
- **Vehicle generators**: `VoxelAssetStudio/procedural_mob_vehicles.py`
- **Design standard**: `Assets/docs/MODEL_DESIGN_STANDARD.md`
- **Scale standard (legacy)**: `Assets/docs/MOB_SIM_SCALE_STANDARD.md` (voxel constants still valid, door table superseded)
- **Building methodology**: `Assets/docs/VOXEL_BUILDING_METHODOLOGY.md`
- **Coordinate system**: `Assets/docs/COORDINATE_SYSTEM.md`
- **Voxel ordering fix**: `Assets/docs/VOXEL_ORDERING_FIX.md`
