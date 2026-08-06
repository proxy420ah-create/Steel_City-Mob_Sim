# Coordinate System & Positioning Guide

**Version**: 1.0 | **Date**: August 4, 2026 | **Status**: ✅ ACTIVE

---

## Overview

Steel City: Mob Sim uses a **local coordinate system** rooted at `MapRoot`. All buildings, terrain, and characters are positioned relative to this root. Understanding the layering of coordinate spaces is essential for placing objects correctly.

---

## Coordinate Spaces

### 1. MapRoot (City Origin)

```
CityMap3D (GameObject)
  └─ MapRoot (Transform)
       localPosition = (0, 0, -100)   ← world-space offset
       worldPosition = CityMap3D.position + (0, 0, -100)
```

**Why -100 Z?** Unity's ScreenSpaceOverlay canvas plane is locked at the world origin (0,0,0). The -100 Z offset moves the 3D city scene away from the canvas plane so they don't overlap in the Scene view.

**Key rule**: All city content (buildings, terrain, characters, compass) must be **parented to MapRoot** and positioned using **localPosition**.

### 2. Block Grid (Row/Col System)

The city is laid out on a 2D grid of blocks. Each block has a `row` and `col` index.

```
Block localPosition = (
    (block.col - centerCol) * spacing,   ← X (east-west)
    0,                                    ← Y (up)
    -(block.row - centerRow) * spacing    ← Z (north-south, negated)
)
```

- **+X** = east (increasing column)
- **+Z** = south (decreasing row, note the negation)
- **centerRow / centerCol** = midpoint of all blocks, so the city is centered at MapRoot origin

### 3. Spacing Constants

```
BuildingVoxelWidth = 32          ← voxels per building slot
voxelSize = 0.1f                  ← world units per building voxel
buildingsPerBlockRow = 3          ← building slots per block side
sidewalkWidth = 1.0f              ← world units of sidewalk per side
roadWidth = 1.6f                  ← world units of road between blocks

GroundTileSize = (32 * 3 * 0.1) + (1.0 * 2) = 11.6 world units
ComputedSpacing = 11.6 + 1.6 = 13.2 world units
```

| Constant | Value | Description |
|---|---|---|
| `BuildingVoxelWidth` | 32 | Voxel width of one building slot |
| `voxelSize` | 0.1m | World units per building voxel |
| `sidewalkWidth` | 1.0m | Sidewalk strip on each side of a block |
| `roadWidth` | 1.6m | Road width between blocks |
| `GroundTileSize` | 11.6m | Full block tile size (buildings + sidewalks) |
| `ComputedSpacing` | 13.2m | Block center-to-center distance (tile + road) |

---

## Positioning Buildings

### Via Chunk Manager (LoadChunkCentered)

```csharp
// anchorPos is a WORLD-SPACE position from blockAnchors dictionary
// LoadChunkCentered offsets internally so the CENTER of the voxel volume sits at anchorPos
chunkManager.LoadChunkCentered(name, filepath, anchorPos, voxelSize);
```

**Important**: `LoadChunkCentered` sets the chunk's `worldOffset` to the **corner** of the volume:
```
cornerPos = centerPos - (w * voxelSize * 0.5, 0, d * voxelSize * 0.5)
```

The compute shader uses this corner as `_VolumeOffset` — the raymarcher's AABB starts there.

### Block Anchors

During terrain generation, `VoxelTerrainBuilder` records exact world-space center positions for each block:

```
Key: "r{row}c{col}"  (e.g. "r1c0" = row 1, col 0)
Value: Vector3 world-space center of the block's building area
```

These anchors account for `mapRoot.position` (the -100 Z offset). Use them for building placement.

---

## Positioning Characters

### VoxelCharacter Component (SteelTide VoxelObject Approach)

Characters use a self-contained `VoxelCharacter` component on their own GameObject:

```csharp
var charObj = new GameObject("Character_Hoodlum_0");
charObj.transform.SetParent(charParent, false);  // charParent is under MapRoot

var vc = charObj.AddComponent<VoxelCharacter>();
vc.assetFileName = "character_hoodlum_0.stasset";
vc.voxelSize = 0.015f;               // or 0.1f for testing
vc.chunkManager = chunkManager;
vc.centerPosition = new Vector3(px, 0f, pz);  // LOCAL to MapRoot
vc.useWorldPosition = false;                   // false = localPosition
```

### How VoxelCharacter Positions Itself

In `Start()`:
1. Loads `.stasset` → gets `dimX, dimY, dimZ`
2. Computes corner offset: `(dimX * voxelSize * 0.5, 0, dimZ * voxelSize * 0.5)`
3. Sets `transform.localPosition = centerPosition - cornerOffset`
4. Creates `ComputeBuffer` and registers with `VoxelChunkManager`

The `transform.position` (world space) is then used by `VoxelChunkManager.RenderChunks()` as the volume offset for the compute shader dispatch.

### Character Voxel Sizes

| Voxel Size | Character Size | Use Case |
|---|---|---|
| 0.02f | 0.32m × 0.64m × 0.20m | Production (80% of door width, proper NPC scale) |
| 0.1f | 1.6m × 3.2m × 1.0m | Testing (matches building scale, easy to see) |

**Note**: At 0.02f, the character is 80% of door width — comfortable for entering/exiting buildings. At 0.1f, the character is building-sized (testing only).

---

## Placing Characters Relative to Buildings

To place a character on the sidewalk in front of a building:

```csharp
// 1. Get block center (local to MapRoot)
float bx = (blockCol - centerCol) * spacing;
float bz = -(blockRow - centerRow) * spacing;

// 2. Calculate building half-dimensions
//    Barber shop: 32v × 20v × 34v at voxelSize=0.1 → 3.2 × 2.0 × 3.4 world units
float bHalfZ = buildingDepthVoxels * voxelSize * 0.5f;  // e.g. 1.7f
float sidewalkHalf = sidewalkWidth * 0.5f;                // e.g. 0.5f

// 3. Place on south sidewalk (front = -Z)
float px = bx;
float pz = bz - (bHalfZ + sidewalkHalf);
Vector3 charLocalCenter = new Vector3(px, 0f, pz);

// 4. Assign to VoxelCharacter
vc.centerPosition = charLocalCenter;
vc.useWorldPosition = false;  // local to MapRoot
```

### Direction Reference (Compass)

| Direction | Axis | Sign | Compass Color |
|---|---|---|---|
| North | Z | - (more negative) | Red |
| South | Z | + (toward camera) | Blue |
| East | X | + | Green |
| West | X | - | Yellow |

**Note**: Camera looks from the south-east by default. North = -Z (away from camera), South = +Z (toward camera).

---

## Building Orientation Detection

`BuildingOrientation.Analyze()` scans the terrain voxels for road material (asphalt=104, cobblestone=105) adjacent to each of the building's 4 faces. This determines:

- **Which faces are street-facing** (have a road outside)
- **Whether the building is on a corner** (2+ street faces)
- **Door direction** (preferred street-facing face)
- **Corner position** (for corner buildings, the world position of the corner intersection)

### How It Works

1. For each face (N/S/E/W), probe outward from the building face in voxel-sized steps
2. Check terrain voxels at each step for road material IDs (104 or 105)
3. Faces with road = street-facing; faces without = interior-facing
4. Corner buildings (2+ street faces) get corner position calculated

### Usage

```csharp
Vector3 buildingWorldCenter = buildingLocalCenter + mapRoot.position;
var orientation = BuildingOrientation.Analyze(
    collisionWorld, buildingWorldCenter, buildingSize, probeDistance: 3f);

// orientation.streetFaces  → which faces have roads (flags enum)
// orientation.isCorner     → true if 2+ street faces
// orientation.doorDirection → normalized direction the door faces
// orientation.cornerPosition → world position of corner (if corner building)
```

### Material IDs for Road Detection

| ID | Material | Used For |
|---|---|---|
| 104 | Asphalt | Road surface |
| 105 | Cobblestone | Road center stripe |
| 102 | Concrete | Sidewalk (not road) |
| 101 | Stone | Building plot base (not road) |

---

## VoxelChunkManager Rendering Pipeline

### How Chunks Are Rendered

Each frame, `RenderChunks()` loops through all registered chunks:

```
For each chunk:
  1. Read world position: chunk.hostObject.transform.position
  2. Set compute shader params:
     _VolumeOffset = world position (corner of volume)
     _VolumeDims   = (dimX, dimY, dimZ)
     _VoxelSize    = chunk.voxelSize
  3. Dispatch raymarch shader
```

The compute shader's AABB is:
```
volumeMin = _VolumeOffset
volumeMax = _VolumeOffset + (_VolumeDims * _VoxelSize)
```

### Registration Methods

| Method | Buffer Owner | GameObject Owner | Use Case |
|---|---|---|---|
| `LoadChunk()` | VoxelChunkManager | VoxelChunkManager | Buildings from file |
| `LoadChunkCentered()` | VoxelChunkManager | VoxelChunkManager | Buildings centered on point |
| `LoadChunkFromData()` | VoxelChunkManager | VoxelChunkManager | Procedural terrain |
| `RegisterVolume()` | Caller | Caller | External components (VoxelCharacter) |

With `RegisterVolume()`, the caller owns the `ComputeBuffer` and `GameObject`. `VoxelChunkManager` only renders it. `UnregisterVolume()` removes it from rendering without releasing the buffer.

---

## Common Pitfalls

1. **Wrong parent**: If a character is parented to `CityMap3D` instead of `MapRoot`, it will be at the wrong world position (missing the -100 Z offset).
2. **World vs Local**: `LoadChunkCentered` expects WORLD-SPACE positions. `VoxelCharacter.centerPosition` can be either — set `useWorldPosition` accordingly.
3. **Corner vs Center**: Voxel volumes are positioned by their **corner** (0,0,0 voxel), not their center. `LoadChunkCentered` and `VoxelCharacter.PlaceAtCenter` handle this offset internally.
4. **Voxel size mismatch**: Each chunk has its own `voxelSize`. Buildings use 0.1f, characters use 0.015f. The compute shader sets `_VoxelSize` per-chunk dispatch.
5. **Z axis negation**: Block rows map to -Z (north = more negative Z). This is because Unity's camera looks down -Z by default, and we want row 0 at the "top" (far from camera).

---

## File References

- **CityMap3D.cs** — `mapRoot` creation, block positioning, character spawning
- **VoxelChunkManager.cs** — Chunk registration, rendering dispatch, `RegisterVolume()`
- **VoxelCharacter.cs** — Self-contained character component (SteelTide VoxelObject approach)
- **VoxelTerrainBuilder.cs** — Terrain generation, block anchor computation
- **MobSimVoxelRaymarch.compute** — GPU raymarch shader (uses `_VolumeOffset`, `_VoxelSize`)
- **MOB_SIM_SCALE_STANDARD.md** — Voxel sizes, scale ratios, reference object dimensions
