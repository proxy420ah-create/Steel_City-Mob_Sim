# Voxel Bleed-Through Fix (Depth Buffer)

**Created**: August 3, 2026
**Status**: ✅ Fixed
**Shader**: `Assets/Resources/Shaders/MobSimVoxelRaymarch.compute`
**Reference**: `SteelTide/docs/VOXEL_PROXY_RAYMARCH_DESIGN.md` (§1.2)

---

## Problem

Voxel volumes (chunks) rendered in the wrong depth order — geometry that is
physically **behind** another chunk would render **in front** of it. This
caused buildings to bleed through terrain, roads through sidewalks, and
generally broke the multi-chunk compositing that the raymarcher relies on.

## Root Cause

In the compute shader, per-pixel depth is stored in `_DepthBuffer` and
compared between chunk dispatches to determine which chunk's pixel is closer
to the camera. The bug was in how `currentT` (the depth value) was
calculated.

### The Bug

```hlsl
// DDA setup
float3 tMax = sideDist;        // ← BUG: volume-relative distance
float currentT = tStart;       // ← Starts correct (camera distance)

// First DDA step
currentT = tMax.x;             // ← Now = sideDist.x (volume-relative!)
```

- `tStart` = distance from camera to volume AABB entry point (correct)
- `sideDist` = distance from entry voxel boundary to next voxel boundary
  (volume-relative, NOT camera-relative)
- After the first DDA step, `currentT = tMax.x = sideDist.x` — this is a
  **volume-relative** distance, not a camera distance

### Why It Looked Partially Correct

- **Thin shells (buildings)**: First solid voxel is hit on step 0 where
  `currentT == tStart`, so depth coincidentally equals camera distance.
  Buildings appeared correctly occluded.
- **Thick volumes (terrain)**: First solid voxel is deep inside the AABB,
  so `currentT` diverges from `tStart`. Terrain reported artificially small
  depths, winning depth comparisons against buildings that were actually
  closer.

This asymmetry is exactly the pattern described in the Steel Tide design
doc `VOXEL_PROXY_RAYMARCH_DESIGN.md` §1.2.

## Fix

Offset `tMax` by `tStart` so `currentT` always represents true
camera-to-hit distance:

```hlsl
// BEFORE (broken):
float3 tMax = sideDist;

// AFTER (correct):
float3 tMax = tStart + sideDist;
```

Now when the DDA advances:
```hlsl
currentT = tMax.x;  // = tStart + sideDist.x = true camera distance
```

Depth comparisons between chunks are now correct — closer chunks always win,
no bleed-through.

## Impact

- ✅ Buildings no longer bleed through terrain
- ✅ Roads and sidewalks composite correctly with buildings
- ✅ Multi-chunk depth ordering is correct from any camera angle
- ✅ Shadow rays from correctly-ordered chunks produce consistent lighting

## Files Modified

| File | Change |
|------|--------|
| `MobSimVoxelRaymarch.compute` (Resources) | `tMax = tStart + sideDist` |
| `MobSimVoxelRaymarch.compute` (Assets/Shaders) | Synced copy |

## Verification

1. Enable GPU Raymarch mode
2. Rotate camera 360° around the city
3. Verify buildings are always occluded by terrain when behind it
4. Verify roads/sidewalks don't poke through building walls
5. Verify shadow rays produce consistent lighting (no flickering from
   wrong-depth chunks)
