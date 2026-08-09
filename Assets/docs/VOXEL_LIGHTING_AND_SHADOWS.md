# Voxel Lighting & Shadow Pipeline

**Created**: August 3, 2026
**Last Updated**: August 4, 2026
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
// Lighting parameters (set from C# via VoxelSun)
float3 _LightDirection;     // Normalized direction TO the sun
float  _LightIntensity;     // Main light strength (0–2)
float  _AmbientIntensity;   // Ambient fill (0–1)
float  _FillIntensity;      // Bounced fill light (0–1)
float3 _LightColor;         // RGB tint of the sun

// Shadow debug parameters (set from C# via VoxelChunkManager)
float  _ShadowNormalNudge;    // Multiplier for normal offset on shadow ray origin
float  _ShadowLightNudge;     // Multiplier for light-dir offset on shadow ray origin
int    _ShadowSkipSteps;      // DDA steps to skip before testing occlusion
int    _ShadowMaxSteps;       // Max DDA steps for shadow ray
int    _ShadowEnabled;        // 1 = shadows on, 0 = shadows off

// Lighting debug toggles (set from C# via VoxelChunkManager)
int    _SunLightEnabled;      // 1 = half-Lambert sun term on
int    _AmbientEnabled;       // 1 = ambient term on
int    _FillEnabled;          // 1 = fill light term on
int    _CamLightEnabled;      // 1 = camera-facing light term on
```

These are set per-frame by `VoxelChunkManager.RenderChunks()` which calls
`SetVector`/`SetFloat`/`SetInt` on the compute shader before each dispatch.
`CityMap3D.cs` proxies all debug parameters through to `VoxelChunkManager`,
and `GameUIController.cs` exposes them as live UI toggles/sliders.

---

## 2. Hybrid Normals (Option B) — DDA + SmoothNormal Blend

### Problem
Hard axis-aligned face normals (`(1,0,0)`, `(0,1,0)`, etc.) cause per-pixel
lighting jumps. Two adjacent pixels hitting the same surface but different
voxel faces get wildly different dot-product results, producing a flickering
checkerboard on flat surfaces.

Pure `SmoothNormal()` (gradient-based) fixes the checkerboard but introduces
brightness variation on flat ground — edges appear brighter than centers because
the gradient changes near voxel boundaries.

### Solution: Hybrid Approach
Uses DDA face normal directly for top/bottom surfaces (uniform flat ground —
no edge-vs-center gradient), and blends with `SmoothNormal` for side faces
(soft wall shading at building edges/corners):

```hlsl
float3 smoothN = SmoothNormal(voxel);
float3 blendedN;
if (abs(normal.y) > 0.5)
{
    // Top or bottom face — use hard DDA normal directly
    blendedN = normal;
}
else
{
    // Side face — blend for softer wall appearance
    blendedN = normalize(normal * 0.7 + smoothN * 0.3);
}
```

`SmoothNormal()` samples the 6-neighbourhood of the hit voxel and computes a
density gradient:

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

**Result**: Flat ground (roofs, roads, sidewalks) is now uniformly lit with no
checkerboard or gradient artifacts. Building walls retain soft shading at edges
and corners.

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

## 4. Lighting Composition (with Debug Toggles)

Each lighting term can be independently toggled via shader uniforms for debugging:

```hlsl
float3 GetLighting(float3 normal, uint mat)
{
    // Emissive materials (windows, neon) — no shading
    if (mat == 113u || mat == 115u || mat == 116u || mat == 117u || mat == 124u)
        return float3(1.0, 1.0, 1.0);
    if (mat == 112u || mat == 114u)
        return float3(0.85, 0.85, 0.85);

    // Half-Lambert main light
    float sky = dot(normal, _LightDirection) * 0.5 + 0.5;
    sky = sky * sky;

    // Fill light from opposite side of sun
    float3 fillDir = normalize(float3(-_LightDirection.x * 0.5, _LightDirection.y * 0.3, -_LightDirection.z * 0.5));
    float fill = dot(normal, fillDir) * 0.5 + 0.5;
    fill = fill * fill;

    // Camera-facing fill for isometric view
    float3 camLight = normalize(float3(0.6, 0.5, 0.6));
    float cam = dot(normal, camLight) * 0.5 + 0.5;
    cam = cam * 0.20;

    // Composite — each term toggleable for debugging
    float3 ambient = float3(_AmbientIntensity, _AmbientIntensity, _AmbientIntensity * 1.03);
    float3 mainLight = _LightColor * sky * _LightIntensity;
    float3 fillLight = fill * _FillIntensity;
    float3 result = float3(0,0,0);
    if (_AmbientEnabled)  result += ambient;
    if (_SunLightEnabled) result += mainLight;
    if (_FillEnabled)     result += fillLight;
    if (_CamLightEnabled) result += cam;
    return result;
}
```

**UI Controls** (in GameUIController City Editor panel):
- **Sun Light (Half-Lambert)** toggle
- **Ambient** toggle
- **Fill Light** toggle
- **Camera Light** toggle
- **Shadows Enabled** toggle
- **Shadow Normal Nudge** slider
- **Shadow Light Nudge** slider
- **Shadow Skip Steps** slider
- **Shadow Max Steps** slider

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

## 6. Self-Shadowing Fix (Parameterized)

### Problem
On flat surfaces (roads, sidewalks) at grazing sun angles, the shadow ray
from a surface voxel immediately grazes adjacent voxels of the **same**
surface, creating a per-voxel checkerboard shadow pattern. Building fronts
looked fine because the shadow ray traveled through open air.

### Solution (Two-Layer, Parameterized)

**Layer 1 — Normal-offset origin (parameterized via `_ShadowNormalNudge` and `_ShadowLightNudge`):**
```hlsl
float3 shadowOrigin = hitWorldPos
    + blendedN * (_VoxelSize * _ShadowNormalNudge)    // Lift off surface
    + shadowDir * (_VoxelSize * _ShadowLightNudge);   // Move along ray
```
Defaults: `normalNudge = 1.5`, `lightNudge = 1.0`. These are now live-tunable
via UI sliders.

**Layer 2 — Skip first N DDA steps (parameterized via `_ShadowSkipSteps`):**
```hlsl
int skipSteps = _ShadowSkipSteps;  // default: 2
// First N steps advance DDA without testing occlusion
```
Belt-and-suspenders: even after the origin nudge, the first N shadow ray
steps advance without testing occlusion to ensure the ray has fully cleared
the surface.

**Max steps** (`_ShadowMaxSteps`, default: 64) limits shadow ray length for
performance.

---

## 7. Shadow Ambient & Safety Floor

Shadowed areas retain full ambient + fill so they're never pure black.
Only the directional sun component is modulated by `shadowFactor`:

```hlsl
float3 sunContribution = _LightColor * skyDot * _LightIntensity;
float3 nonSunLighting = lighting - sunContribution; // ambient + fill + cam
float3 shadowedLighting = nonSunLighting + sunContribution * shadowFactor;

// Safety floor: ensure a minimum ambient so flats never go black
float ambientFloor = max(0.0, _AmbientIntensity * 0.35);
shadowedLighting = max(shadowedLighting, float3(ambientFloor, ambientFloor, ambientFloor * 1.02));
```

---

## File Locations

| Component | File |
|-----------|------|
| Compute shader | `Assets/Resources/Shaders/MobSimVoxelRaymarch.compute` |
| Chunk manager (uniforms + dispatch) | `Assets/Scripts/UI/VoxelChunkManager.cs` |
| Sun component (day/night) | `Assets/Scripts/UI/VoxelSun.cs` |
| City map (proxy API + camera) | `Assets/Scripts/UI/CityMap3D.cs` |
| UI controller (debug toggles/sliders) | `Assets/Scripts/UI/GameUIController.cs` |

---

## Debugging Tips

- Check `[VoxelSun] Lighting pushed:` console log for current values
- If scene is dark: verify `timeOfDay` is not 12 (noon = straight up = no wall light)
- If checkerboard on flat surfaces: verify `_ShadowSkipSteps` and `_ShadowNormalNudge` are set
- If shadows missing: verify `_ShadowEnabled` is 1 and `_LightDirection` is not `(0,0,0)`
- Use the **Lighting Debug** UI toggles to isolate each lighting term:
  - Turn off all but Ambient → verify flat ambient floor
  - Turn on only Sun Light → verify half-Lambert gradient on walls
  - Turn on only Fill → verify fill light from opposite side
  - Turn on only Camera Light → verify subtle camera-facing brightening
- Use **Shadow Debug** sliders to tune shadow quality:
  - Increase `_ShadowNormalNudge` if self-shadowing persists
  - Increase `_ShadowSkipSteps` if surface acne remains
  - Reduce `_ShadowMaxSteps` for performance (shorter shadows)
