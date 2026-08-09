# Voxel Sun & Day/Night Cycle

**Created**: August 3, 2026
**Status**: ✅ Complete
**Component**: `Assets/Scripts/UI/VoxelSun.cs`

---

## Overview

The `VoxelSun` component simulates a day-night cycle for the voxel raymarch
scene. It acts as a visual wireframe "sun" GameObject in the scene and
computes lighting parameters (direction, intensity, ambient, fill, color
tint) based on its position. These parameters are pushed to
`VoxelChunkManager.SetLighting()` every frame, which updates the compute
shader uniforms.

This system is **completely separate** from Unity's built-in scene lights.
Voxel raymarch lighting is handled entirely in the compute shader.

---

## Architecture

```
VoxelSun (MonoBehaviour)
  ├── timeOfDay (0–24 hours)
  ├── cityCenter (world-space orbit focus)
  ├── orbitRadius (distance from center)
  ├── DayLightPreset[] (dawn, noon, dusk, night)
  └── Update() → UpdateSunPosition() → UpdateLighting()
                                            │
                                            ▼
                               VoxelChunkManager.SetLighting()
                                            │
                                            ▼
                               MobSimVoxelRaymarch.compute
                               (_LightDirection, _LightIntensity, etc.)
```

---

## Sun Position Calculation

The sun orbits the `cityCenter` at `orbitRadius` distance. Its position is
derived from `timeOfDay` (0–24):

- **0/24 = midnight** (sun directly below)
- **6 = dawn** (sun at eastern horizon)
- **12 = noon** (sun directly overhead)
- **18 = dusk** (sun at western horizon)

```csharp
float angle = (timeOfDay - 6f) / 24f * 360f;  // 0° at dawn
float sunHorizontal = Mathf.Cos(angle * Mathf.Deg2Rad);
float sunHeight = Mathf.Sin(angle * Mathf.Deg2Rad);
```

Axial tilt is applied for seasonal variation.

---

## Lighting Presets

| Preset | Intensity | Ambient | Fill | Tint |
|--------|-----------|---------|------|------|
| Dawn | 0.6 | 0.45 | 0.20 | Warm orange |
| Noon | 1.0 | 0.65 | 0.35 | Warm white |
| Dusk | 0.5 | 0.40 | 0.18 | Deep orange |
| Night | 0.05 | 0.15 | 0.05 | Cool blue |

Presets are blended based on the sun's normalized height (0 = horizon,
1 = zenith). This produces smooth transitions throughout the day.

---

## Critical Design Decisions

### 1. VoxelSun Does NOT Move Its Own Transform
`VoxelSun` is added as a component on the `CityMap3D` GameObject. Early
implementations used `transform.position = sunPos` which moved the entire
city. The fix: store the sun position in a `sunWorldPos` field and only
move the visual wireframe sphere.

### 2. chunkManager Lookup in Start(), Not Awake()
`CityMap3D.Awake()` creates the `VoxelChunkManager` component. If
`VoxelSun.Awake()` runs first, `GetComponent<VoxelChunkManager>()` returns
null. Moving the lookup to `Start()` ensures all `Awake()` calls have
completed.

### 3. Default timeOfDay = 10 (Not 12)
At exactly noon (12), the sun is directly overhead, producing a light
direction of `(0, 1, 0)`. This illuminates rooftops but **not walls** —
the dot product with vertical normals is 0. Setting the default to 10 AM
ensures the sun hits at an angle, lighting walls adequately.

### 4. Deprecated API Fix
Replaced `FindObjectOfType<CityMap3D>()` with `FindFirstObjectByType<CityMap3D>()`
to resolve Unity 6 deprecation warning CS0618.

---

## C# API

```csharp
// VoxelChunkManager public API
public void SetLighting(Vector3 dir, float intensity, float ambient, float fill, Color tint);

// VoxelSun inspector fields
[SerializeField] private float timeOfDay = 10f;
[SerializeField] private float timeSpeed = 0f;  // 0 = static, >0 = auto-advance
[SerializeField] private Vector3 cityCenter = new Vector3(0f, 0f, -100f);
[SerializeField] private float orbitRadius = 60f;
[SerializeField] private bool showWireframe = true;
```

---

## Debugging

Console logs on startup:
```
[VoxelSun] Start: chunkManager=FOUND, cityCenter=(0.00, 0.00, -100.00), timeOfDay=10
[VoxelSun] Lighting pushed: dir=(0.87, 0.50, -0.00), intensity=1.0, ambient=0.65, ...
```

If `chunkManager=NULL`: verify `VoxelChunkManager` component exists on the
same GameObject as `CityMap3D`, or that a `CityMap3D` exists in the scene.

If scene is dark: check `timeOfDay` is not 12 (noon = straight up = no wall
light). Try 10 or 14 for angled sun.
