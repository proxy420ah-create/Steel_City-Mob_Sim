# Instanced Proxy Yaw Clip (elongated volumes clipped at ±90° yaw)

**Date**: October 6, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- `vehicle_490_touring` (70×75×155 voxels — 1.05×2.33 m footprint) rendered
  truncated: front/rear clipped away on streets running along world X
  (Amsterdam), correct on streets along world Z (57th).
- Read as "the renderer is culling them" + "not aligned on Amsterdam".

## Root Cause
`VoxelChunkManager.RenderInstancedGroup` built the instanced proxy cube
axis-aligned at the **unrotated** volume dims (`Matrix4x4.TRS(centerPos,
identity, paddedSize)`), while the shader yaw-rotates the volume *inside*
the proxy about its center. At yaw ±90° an elongated footprint swaps axes —
the 490's 2.33 m length lay along world X but the proxy was still 1.05 m
wide in X, so ~0.64 m of car hung outside the proxy on both ends. Voxels
outside the proxy are never raymarched → clipped, and the remnant looked
misaligned.

Masked until now because every prior instanced asset had a near-square or
cubic footprint (characters = 96³ cubes; `vehicle_civilian_car_0` = 30×20)
whose yawed AABB barely differs from the unrotated one. Documented as
"Pitfall #8" in VEHICLE_VOXEL_ASSETS.md before the 490 confirmed it.

## Fix
Per-instance yaw-aware proxy bounds in `RenderInstancedGroup`
(~line 1610): the axis-aligned proxy XZ extents become the rotated
footprint —
`(|cosYaw|·sizeX + |sinYaw|·sizeZ, sizeY, |sinYaw|·sizeX + |cosYaw|·sizeZ) + pad`,
centered on the volume's true rotation pivot (`worldOffset + size*0.5`).

Other paths unaffected: baked sectors don't rotate; the single-chunk path
(≈line 2712) rotates the proxy cube itself via `chunkRot` and was already
correct.

## Verification
Unity playtest: parked/driving 490s on Amsterdam St (world-X) must show
full length, parallel to the curb; identical on 57th St. F8-freeze a car
mid-turn at an intersection — the body should stay whole through the yaw
sweep (proxy grows to ~2.5 m diagonal at 45° — slightly more fragment work
is expected and correct).

## Related
- `docs/systems/VEHICLE_VOXEL_ASSETS.md` §7 pitfall #8 → confirmed/fixed
- `docs/known_issues/rendering/SECTOR_FRUSTUM_CULL.md` (separate issue)
