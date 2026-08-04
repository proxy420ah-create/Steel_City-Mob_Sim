# Voxel Lighting & Shadow Pipeline

**Created**: August 3, 2026
**Status**: ✅ Complete
**Shader**: `Assets/Resources/Shaders/MobSimVoxelRaymarch.compute`

---

## Overview

The voxel raymarcher uses a custom lighting model driven by shader uniforms
populated from the `VoxelSun` C# component. This document captures the full
pipeline, the problems we solved, and the techniques applied.

---

## 1. Shader Uniforms

Declared at the top of `MobSimVoxelRaymarch.compute`:

```hlsl
float3 _LightDirection;     // Normalized direction TO the sun
float  _LightIntensity;     // Main light strength (0–2)
float  _AmbientIntensity;   // Ambient fill (0–1)
float  _FillIntensity;      // Bounced fill light (0–1)
float3 _LightColor;         // RGB tint of the sun
```

These are set per-frame by `VoxelChunkManager.RenderChunks()` which calls
`SetVector`/`SetFloat` on the compute shader before each dispatch.

---

## 2. Smooth Normals (Gradient-Based)

### Problem
Hard axis-aligned face normals (`(1,0,0)`, `(0,1,0)`, etc.) cause per-pixel
lighting jumps. Two adjacent pixels hitting the same surface but different
voxel faces get wildly different dot-product results, producing a flickering
checkerboard.

### Solution: `SmoothNormal()`
Samples the 6-neighbourhood of the hit voxel and computes a density gradient:

```hlsl
float3 SmoothNormal(int3 v)
{
    float cx = 0, cy = 0, cz = 0;
    // Sample ±X, ±Y, ±Z neighbours
    // Solid (mat != 0) = +1, Air = 0
    // Gradient points from solid toward air = outward normal
    float3 n = normalize(float3(-cx, -cy, -cz));
    if (length(n) < 0.001) n = float3(0, 1, 0); // fallback
    return n;
}
```

**Cost**: 6 extra voxel reads per hit pixel (only on surface hits, not during
traversal). Negligible performance impact.

---

## 3. Half-Lambert Wrap Lighting

### Problem
Standard Lambert (`max(0, dot(N, L))`) produces hard black-to-white transitions.
Surfaces facing away from the sun go completely black, creating harsh edges.

### Solution
Half-Lambert wraps the dot product from `[-1,1]` to `[0,1]`:

```hlsl
float sky = dot(normal, _LightDirection) * 0.5 + 0.5;
sky = sky * sky; // Square for softer falloff
```

This technique was popularized by Valve in Half-Life 2 for exactly this
purpose — soft, consistent shading on low-poly/blocky surfaces. Surfaces
facing away from the sun still receive partial light instead of going black.

Applied to both the main light and the fill light.

---

## 4. Lighting Composition

```hlsl
float3 GetLighting(float3 normal, uint mat)
{
    // Emissive materials (windows, neon) — no shading
    if (mat == 121u || 123u || 124u || 125u || 154u) return float3(1,1,1);

    // Half-Lambert main light
    float sky = dot(normal, _LightDirection) * 0.5 + 0.5;
    sky *= sky;

    // Fill light from opposite side of sun
    float3 fillDir = normalize(float3(-L.x*0.5, L.y*0.3, -L.z*0.5));
    float fill = dot(normal, fillDir) * 0.5 + 0.5;
    fill *= fill;

    // Camera-facing fill for isometric view
    float3 camLight = normalize(float3(0.6, 0.5, 0.6));
    float cam = dot(normal, camLight) * 0.5 + 0.5;
    cam *= 0.20;

    // Composite
    float3 ambient = float3(A, A, A * 1.03);
    float3 mainLight = _LightColor * sky * _LightIntensity;
    float3 fillLight = fill * _FillIntensity;
    return ambient + mainLight + fillLight + cam;
}
```

---

## 5. Soft Shadow Penumbra

### Problem
Binary shadows (`shadowFactor = 0.0` on first occluder hit) create hard pixel
boundaries at shadow edges. Adjacent pixels flip between fully lit and fully
shadowed.

### Solution
Instead of binary occlusion, compute perpendicular distance from the shadow
ray to the occluder voxel center:

```hlsl
float3 voxelCenter = _VolumeOffset + (float3(shadowVoxel) + 0.5) * _VoxelSize;
float3 toHit = shadowOrigin - voxelCenter;
float perpDist = length(toHit - shadowDir * dot(toHit, shadowDir));
float softness = saturate(perpDist / (_VoxelSize * 2.0));
shadowFactor = min(shadowFactor, softness);
```

Close hits = sharp shadow, glancing hits = soft partial shadow. This creates
smooth shadow edges instead of hard pixel boundaries.

---

## 6. Self-Shadowing Fix (Checkerboard on Flat Surfaces)

### Problem
On flat surfaces (roads, sidewalks) at grazing sun angles, the shadow ray
from a surface voxel immediately grazes adjacent voxels of the **same**
surface, creating a per-voxel checkerboard shadow pattern. Building fronts
looked fine because the shadow ray traveled through open air.

### Solution (Two-Layer)

**Layer 1 — Normal-offset origin:**
```hlsl
float3 shadowOrigin = hitWorldPos
    + smoothN * (_VoxelSize * 1.5)    // Lift off surface into open air
    + shadowDir * (_VoxelSize * 1.0); // Move along ray
```
The old nudge of `0.5 * voxelSize` was insufficient for flat surfaces.
`1.5 * voxelSize` along the smooth normal lifts the origin well clear.

**Layer 2 — Skip first 2 DDA steps:**
```hlsl
int skipSteps = 2;
// First 2 steps advance DDA without testing occlusion
```
Belt-and-suspenders: even after the origin nudge, the first 2 shadow ray
steps advance without testing occlusion to ensure the ray has fully cleared
the surface.

---

## 7. Shadow Ambient

Shadowed areas receive a minimum ambient so they're not pure black:

```hlsl
float3 shadowAmbient = float3(_AmbientIntensity * 0.5,
                               _AmbientIntensity * 0.5,
                               _AmbientIntensity * 0.52);
float3 shadowedLighting = lighting * shadowFactor + shadowAmbient * (1.0 - shadowFactor);
```

---

## File Locations

| Component | File |
|-----------|------|
| Compute shader | `Assets/Resources/Shaders/MobSimVoxelRaymarch.compute` |
| Chunk manager (uniforms) | `Assets/Scripts/UI/VoxelChunkManager.cs` |
| Sun component | `Assets/Scripts/UI/VoxelSun.cs` |
| City map (camera) | `Assets/Scripts/UI/CityMap3D.cs` |

---

## Debugging Tips

- Check `[VoxelSun] Lighting pushed:` console log for current values
- If scene is dark: verify `timeOfDay` is not 12 (noon = straight up = no wall light)
- If checkerboard on flat surfaces: verify `skipSteps` and normal offset are present
- If shadows missing: verify `_LightDirection` is not `(0,0,0)`
