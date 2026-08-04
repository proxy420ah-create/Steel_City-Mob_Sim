# Mob Sim Scale Standard

**Version**: 1.0 | **Date**: August 3, 2026 | **Status**: ✅ ACTIVE

---

## Overview

The Mob Sim has its own micro universe where NPC wise guys (0.48m tall) are the "humans." Everything in the city is proportional to this scale, not real-world scale. This document defines the standard for all Mob Sim objects.

## Core Scale Constants

| Constant | Value | Description |
|---|---|---|
| `BUILDING_VOXEL_SIZE` | 0.1m | World units per building voxel |
| `CHAR_VOXEL_SIZE` | 0.015m | World units per character voxel |
| `SCALE_RATIO` | 3.75 | Real-world size ÷ Mob Sim size |
| `NPC_HEIGHT` | 0.48m | Wise guy height (32 char voxels) |
| `NPC_HEIGHT` | 4.8 building voxels | Same height in building voxel grid |

## Conversion Formulas

```
Mob Sim meters = Real meters ÷ 3.75
Building voxels = Mob Sim meters ÷ 0.1
Character voxels = Mob Sim meters ÷ 0.015
```

## Standard Door Sizes

| Door Type | Voxels (H×W) | Mob Sim Size | Real Equivalent | Used By |
|---|---|---|---|---|
| **Standard** | 4×4 | 0.4m × 0.4m | 1.5m × 1.5m | Storefronts, apartments, speakeasy, HQ, diner |
| **Civic** | 5×8-10 | 0.5m × 0.8-1.0m | 1.9m × 3.0-3.75m | Police station, casino |
| **Vehicle bay** | 6×16 | 0.6m × 1.6m | 2.25m × 6.0m | Garage |

## Reference Object Library

| Object | Voxels | Mob Sim Size | Real Equivalent |
|---|---|---|---|
| 🧍 NPC Wise Guy | 3×5×2 (ref) / 16×32×10 (full) | 0.24m × 0.48m | 0.9m × 1.8m |
| 🚪 Standard Door | 4×4×2 | 0.4m × 0.4m | 1.5m × 1.5m |
| 🗑️ Trash Can | 5×3×5 | 0.4m × 0.3m | 0.6m × 1.0m |
| 🪑 Bench | 8×1×3 | 0.8m × 0.1m | 3.0m × 0.5m |
| 💡 Street Light | 3×11×3 | 0.2m × 1.1m | 0.3m × 4.0m |
| 🚗 Car | 12×4×6 | 1.2m × 0.4m | 4.5m × 1.5m |
| 🚮 Dumpster | 6×5×4 | 0.6m × 0.5m | 1.8m × 2.0m |
| 🌳 Tree | 9×16×9 | 0.8m × 1.6m | 1.5m × 6.0m |

## Building Heights

| Building | Voxels | Mob Sim Height | Real Equivalent |
|---|---|---|---|
| Empty lot | 4 | 0.4m | 1.5m |
| Garage | 14 | 1.4m | 5.25m |
| Barber shop | 16 | 1.6m | 6.0m |
| Diner | 16 | 1.6m | 6.0m |
| Bakery | 18 | 1.8m | 6.75m |
| Speakeasy | 18 | 1.8m | 6.75m |
| Butcher shop | 20 | 2.0m | 7.5m |
| Casino | 24 | 2.4m | 9.0m |
| Police station | 26 | 2.6m | 9.75m |
| HQ | 28 | 2.8m | 10.5m |
| Apartments | 36 | 3.6m | 13.5m |

## Relationship to Steel Tide FPS Scale

The VoxelAssetStudio has a separate reference system for the FPS game (8 voxels/meter, 1.8m human). The Mob Sim uses its own system (10 voxels/meter for buildings, 66.7 voxels/meter for characters, 0.48m NPC). These are **independent** — do not mix scales.

## File References

- **Reference models**: `VoxelAssetStudio/mob_sim_reference_models.py`
- **Building generators**: `VoxelAssetStudio/procedural_mob_buildings.py`
- **Character generators**: `VoxelAssetStudio/procedural_mob_characters.py`
- **Materials**: `VoxelAssetStudio/mob_materials.py`
- **Unity voxel size**: `CityMap3D.cs` → `voxelSize = 0.1f`, `characterVoxelSize = 0.015f`
