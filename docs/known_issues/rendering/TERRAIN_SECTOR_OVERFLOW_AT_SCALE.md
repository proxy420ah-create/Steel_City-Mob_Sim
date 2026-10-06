# Terrain Sector Overflow at 32x32 (half the roads/sidewalks missing)

**Date**: 2026-10-05
**Status**: � FIXED — confirmed in Unity playtest 2026-10-05 (whole map now has roads + sidewalks; build log: `1024 chunks in 9 sector(s)`). Which of the three limits was the actual trigger is still unisolated — all three are now respected. Full write-up: `docs/systems/CITY_SCALE_ARCHITECTURE.md`.
**Severity**: 🟡 HIGH

## Symptoms
- 32x32 city (1024 blocks): roughly half the map has no road or sidewalk terrain; empty lots/buildings render, floating on the background colour.
- Street names still appear on hover (game logic / block anchors are fine — only the terrain *render* is missing).
- No errors or warnings in `Editor.log`; `buildmap_log.txt` reports a clean build (`1024 chunks, 143,210,888 voxels`). Silent failure.

## Root Cause (strong hypothesis — not yet observed on GPU)
`BuildVoxelTerrain` baked ALL terrain chunks into ONE sector (`terrain_sector`), which at 1024 chunks breaks three limits at once:

1. **Instance cap** — `DrawMeshInstanced` supports at most 511 instances (with `worldToObject`) / 1023. 511/1024 ≈ 50%, matching "half the city". Predicted in `docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md` gap #3 ("could silently exceed this and fail to render — indistinguishable from a culling bug").
2. **Float offset precision** — per-chunk buffer offsets travel through `float4 _BuildingMeta.x`; floats are integer-exact only below 2^24 = 16.7M, terrain reached 143M.
3. **Buffer element limit** — D3D11 buffer SRVs top out at 2^27 = 134M elements; terrain was 143M.

Any one of these can produce the symptom; the 10x10 city (100 chunks, 13.9M voxels) was under all three.

## Fix
`CityMap3D.BuildVoxelTerrain` now bands the row-major chunk list into `terrain_sector_N` sectors, each ≤ 256 chunks and ≤ 2^24-1 voxels (~114 chunks at the current tile size → ~9 sectors for 32x32). A 10x10 city still produces exactly one sector. Collision registration is unchanged. Building sectors (≈144 instances, ≈4.7M voxels each) were already inside all limits.

## Verify
- `buildmap_log.txt`: `PHASE 1B ... 1024 chunks in N sector(s)` with N > 1.
- Roads + sidewalks visible across the whole 32x32 map.

## Related
- `Assets/Scripts/UI/CityMap3D.cs` — `BuildVoxelTerrain`, `MaxTerrainChunksPerSector`, `MaxTerrainVoxelsPerSector`
- `docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`, `docs/systems/GPU_DRIVEN_RENDERING_PLAN.md` (Phase 3 indirect draw removes the instance cap entirely)
