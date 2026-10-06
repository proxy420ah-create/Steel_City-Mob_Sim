# Sector Frustum Culling Drops Visible Terrain (False Positive)

**Date**: October 5, 2026
**Status**: 🟡 MITIGATED (culling disabled by default — root cause open)
**Severity**: 🟡 HIGH (whole-sector terrain voids)

## Symptoms
- Block-scale black rectangular voids in the city view — roads, sidewalks, and
  terrain vanish while building lots and debug beams still render.
- Voids move/change with camera angle; largest at oblique/top-down pans.
- Confirmed reproduced: toggling `disableSectorCulling` ON in the
  `VoxelChunkManager` inspector heals the voids 100%.

## Root Cause (hypothesis)
`RenderBakedSectors` computes `sectorBounds` from the baked sector's voxel-grid
min/max, pads by `voxelSize*8` (~0.4 m), and tests `GeometryUtility.TestPlanesAABB`
against `CalculateFrustumPlanes(cam)`. Terrain sectors span ~57×64 m, so at
oblique camera angles a still-visible sector fails the plane test — likely a
tight-bounds issue (raymarch proxy padding / shader-side offsets exceed the
CPU-side AABB at screen edges) rather than a Unity bug; `TestPlanesAABB` is
supposed to be conservative and never wrongly cull.

## Mitigation
`disableSectorCulling` now defaults `true` in `VoxelChunkManager`. With ~9
sectors, per-sector culling buys ~nothing anyway — front-to-back sorting and
per-sector LOD tiers remain active and unaffected (they're not part of the
cull gate). Distance culling (`maxRenderDistance`) is gated behind the same
flag — it also stops applying while disabled; currently moot since it was
already perspective-only and the map camera is ortho.

## If Re-enabled
- Reproduce: tilt/pan the map camera with `disableSectorCulling=false` on a
  river layout; watch for sector_4 (full-width far band) or sector_0 (main slab)
  dropping at screen edges.
- Check whether `sectorMin/sectorMax` under-represents the drawn volume
  (raymarch `_VoxelPadding`, `worldOffset` shifts, deep-channel y range).
- Consider padding bounds by a fraction of sector size instead of `vs*8`, or
  switching to per-chunk culling granularity.

## Related Files
- `Assets/Scripts/UI/VoxelChunkManager.cs` — sector draw loop, ~line 2016
- `docs/known_issues/rendering/TERRAIN_SECTOR_OVERFLOW_AT_SCALE.md`
- `docs/known_issues/rendering/ANGLE_DEPENDENT_BUILDING_CULLING.md` (related
  culling bug family — worth cross-checking root cause)
