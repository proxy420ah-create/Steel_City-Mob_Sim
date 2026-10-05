# Angle-Dependent Building Culling (Flat Volumes / Tall Features)

**Date**: October 8, 2026
**Status**: 🟢 FIXED (pending in-engine verification)
**Severity**: 🔴 CRITICAL — building geometry disappears at common camera angles

## Symptoms

- `shooting_range` (192×120×192 container, content only y=0–36) renders its
  backstop wall but the floor and mid-field geometry vanish at certain
  camera angles; rotating/zooming restores it.
- Observing a `tenement_block` from above, the roof near the **water tower**
  drops out — the tower itself stays visible.
- Empty lots (thin flat slab, tight box = ground surface) never drop.

## Root Cause

Two compounding problems in the proxy raymarch path:

1. **The DDA marched the full container**, not the content. `RayAABB`
   bounded the march by `volOffset .. volOffset + dims*voxelSize`
   (192×120×192 for the range). Every pixel over the flat floor had to
   traverse ~80+ voxels of empty container air *before* reaching content —
   and more at grazing angles.
2. **Step budgets were fixed per LOD tier** (Near 264 / Mid 48 / Far 24 /
   Ultra 12) regardless of volume size. Once a ray's in-volume traversal
   exceeded `_MaxSteps`, the loop exited and the fragment discarded —
   producing the "hole."

The mechanism explains both observations:

- **Shooting range**: the backstop wall inflates the tight box to y≈36.
  Rays crossing the floor at shallow angles must travel up to ~190 voxel
  steps through mostly-empty interior space; at LOD ≤264 the budget runs
  out mid-field → floor holes, wall survives (hit early).
- **Water tower**: the tower raises the tenement's tight box ~4 m above
  the local roof, so pixels *beside* the tower enter the box top and burn
  ~40 extra steps of air before the roof → holes *around* tall features.

"Tall objects cause it" because tall content inflates the marched bounds
for every neighboring pixel.

## Fix

`Assets/Scripts/UI/VoxelChunkManager.cs` (per-chunk property block):

- `propTightBoundsMin/Max` — pass the chunk's tight AABB in voxel units
  (Max exclusive) to the shader.
- Step budget floored at the voxel-cell crossing bound
  (`tightSizeX + tightSizeY + tightSizeZ + 4`).

`Assets/Resources/Shaders/VoxelProxyRaymarch.shader`:

- New uniforms `_TightBoundsMin` / `_TightBoundsMax`.
- `volumeMin/Max` now bound the `RayAABB`+DDA to the tight AABB when
  provided (`_TightBoundsMax.x > 0`), else fall back to the full container.
  Voxel indexing still uses container `dims`/`volOffset` — stride layout
  unchanged, so buffer reads stay correct.
- **Authoritative step floor in-shader**: `stepCap = max(_MaxSteps,
  marchSizeX + marchSizeY + marchSizeZ + 4)` derived from the marched
  bounds — every path (chunks, sectors, instanced) is self-protecting.
  NOTE: the correct bound is `sx+sy+sz` (Manhattan cell crossings), NOT
  the Euclidean diagonal — a ray near-parallel to an axis crosses ~385
  cells through a 192×37×192 box, where `sqrt(sx²+sy²+sz²)` ≈ 274 still
  starves it. First-pass fix used the diagonal and left residual culling
  at the lowest camera angle; corrected.
- For the range this removes ~70% of the per-pixel traversal — the fix is
  also a perf win for flat buildings.

Instanced (`DrawMeshInstanced`) and sector-baked paths never set the tight
uniforms → zero → full container bounds, unchanged behavior. Their
volumes are either small (characters/vehicles ≈166 step diagonal < 264)
or pre-culled, so they were never affected.

## Deferred

- **Sector path** could adopt tight bounds + diagonal floor the same way
  if baked sectors show the same symptom (merged volumes have large
  diagonals). Not observed yet.
- **Column-occupancy skip grid** — the "real" long-term optimization:
  skip empty space per-column instead of linear-marching it. Would make
  the diagonal floor unnecessary again.

## Related Files

- `Assets/Scripts/UI/VoxelChunkManager.cs` — chunk property block, LOD
  tiers, `proxyDrawList`
- `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` — `RayAABB`, DDA
  loop, `_MaxSteps`
- `Assets/StreamingAssets/voxel_buildings/shooting_range.stasset` — the
  asset that exposed it
