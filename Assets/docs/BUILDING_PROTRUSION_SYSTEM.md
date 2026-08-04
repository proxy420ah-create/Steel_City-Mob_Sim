# Building Protrusion System

## Overview

Protrusion detailing adds exterior architectural features (awnings, barber poles, columns) that stick out from the building's front wall, creating visual depth and 1920s street character.

## How It Works

### Voxel Grid Padding

Buildings normally generate at `32×H×32` (width × height × depth). The front wall occupies `z=0:WALL_T` (z=0,1). To add protrusion, the grid is padded with `PROTRUDE=2` air voxels at the front:

```
Before padding:  [wall][interior...][wall]   z=0..31
After padding:   [AIR][AIR][wall][interior...][wall]   z=0..33
                  ^^^^^^^^^^
                  protrusion zone (z=0,1)
```

Features placed in the protrusion zone are surrounded by air on all sides, so the meshifier generates full faces for them — they appear as solid 3D objects sticking out from the building.

### Code Pattern

```python
# 1. Generate base building normally
grid = np.zeros((w, h, d), dtype=np.uint16)
# ... walls, interior, storefront, windows, roof ...

# 2. Pad front with air
PROTRUDE = 2
padded = np.zeros((w, h, d + PROTRUDE), dtype=np.uint16)
padded[:, :, PROTRUDE:] = grid  # shift building back

# 3. Place protruding features in the air zone
padded[WALL_T:w-WALL_T, 6:8, :PROTRUDE] = AWNING_STRIPED  # awning sticks out
```

### Auto-Centering

`VoxelBuildingMeshifier` centers the mesh using actual grid dimensions (`-d * voxelSize * 0.5f`), so the slightly deeper grid stays centered correctly. No manual offset needed.

## Buildings With Protrusion

| Building | Feature | Material | Voxel Position | World Protrusion |
|---|---|---|---|---|
| Barbershop | Striped awning | `AWNING_STRIPED` | z=0,1, y=6-7, full width | 0.2 units |
| Barbershop | Barber pole | `BARBER_POLE`/`NEON_RED` | z=0,1, y=2-11, left of door | 0.2 units |
| Bakery | Green awning | `AWNING_GREEN` | z=0,1, y=6-7, full width | 0.2 units |
| Police Station | Entrance columns | `TRIM_WHITE` | z=0,1, y=1-11, flanking door | 0.2 units |
| Apartments | Fire escape railings | `FIRE_ESCAPE` | z=0,1, y=floor levels, full width | 0.2 units |
| Apartments | Fire escape stairs | `FIRE_ESCAPE` | z=0,1, y=floor levels, left side | 0.2 units |

## Key Values

- `PROTRUDE = 2` — number of air voxels padded at front
- `voxelSize = 0.1` — world units per voxel
- Protrusion depth = `PROTRUDE × voxelSize` = 0.2 world units
- `WALL_T = 2` — wall thickness (front wall at z=0,1 in base grid)

## Important Notes

- Protrusion features must be placed in `padded[...,:PROTRUDE]` (the air zone), NOT in `grid[...,:WALL_T+1]` (which extends into interior)
- The `+1` bug (`:WALL_T+1`) placed features at z=2, which is interior air after hollowing — this caused features to appear inside the building
- After padding, the building's front wall is at z=PROTRUDE..PROTRUDE+WALL_T (z=2,3), connecting seamlessly to protruding features at z=0,1
- The meshifier's face culling handles the junction automatically — no internal faces are generated where protrusion meets wall

## File References

- **Generator**: `VoxelAssetStudio/procedural_mob_buildings.py`
- **Meshifier**: `Steel_City-Mob_Sim/Assets/Scripts/Sim/VoxelBuildingMeshifier.cs`
- **City Layout**: `Steel_City-Mob_Sim/Assets/StreamingAssets/city_layout.json`

## Adding Protrusion to New Buildings

1. Generate the base building normally at `32×H×32`
2. Pad: `padded = np.zeros((w, h, d + 2), dtype=np.uint16); padded[:, :, 2:] = grid`
3. Place features: `padded[x_range, y_range, :2] = MATERIAL`
4. Return `padded` (now `32×H×34`)
5. Regenerate `.stasset` and copy to `StreamingAssets/voxel_buildings/`
