# Voxel Camera System

**Created**: August 3, 2026
**Status**: ✅ Complete
**Component**: `Assets/Scripts/UI/CityMap3D.cs`

---

## Overview

The city map camera is an orthographic camera that orbits around a focus
point (defaulting to the city center). It supports smooth mouse-based
controls for debugging and city planning.

---

## Mouse Controls

| Input | Action |
|-------|--------|
| **LMB click** | Focus camera on clicked world point (raycasts to find position) |
| **MMB drag** | Rotate camera — horizontal = yaw (0–360°), vertical = pitch (10–80°) |
| **RMB drag** | Pan — moves camera focus in screen space, scaled by zoom level |
| **Scroll wheel** | Zoom — adjusts orthographic size (3–40 range, 0.08 sensitivity) |
| **RESET CAMERA button** | Returns to default isometric view (45° yaw, 35° pitch, 18 zoom) |

All controls only activate when the cursor is over the map viewport (checked
via `mapCamera.rect`). UI clicks are filtered via
`EventSystem.current.IsPointerOverGameObject()`.

---

## Smooth Rotator

Camera rotation uses `Mathf.LerpAngle` for yaw and `Mathf.Lerp` for pitch
with `Time.deltaTime * 5f` interpolation speed. This produces smooth eased
rotation toward target values — dragging the MMB feels natural, not jerky.

```csharp
cameraYaw = Mathf.LerpAngle(cameraYaw, targetYaw, Time.deltaTime * 5f);
cameraPitch = Mathf.Lerp(cameraPitch, targetPitch, Time.deltaTime * 5f);
```

### Orbit Position Calculation

The camera position is computed from spherical coordinates around the
focus point:

```csharp
float yawRad = cameraYaw * Mathf.Deg2Rad;
float pitchRad = cameraPitch * Mathf.Deg2Rad;
float horizDist = CameraOrbitDistance * Mathf.Cos(pitchRad);
float height = CameraOrbitDistance * Mathf.Sin(pitchRad);

Vector3 camPos = cameraFocus + new Vector3(
    -horizDist * Mathf.Sin(yawRad),
    height,
    -horizDist * Mathf.Cos(yawRad)
);
mapCamera.transform.position = camPos;
mapCamera.transform.LookAt(cameraFocus);
```

- `CameraOrbitDistance = 40` — fixed distance from focus point
- `cameraFocus` defaults to `mapRoot.position` (city center at `(0, 0, -100)`)
- LMB click raycasts to find a world point and sets it as the new focus
- RMB drag pans the focus in screen space relative to camera orientation
- Reset restores focus to city center

---

## Editor Panel

The City Editor panel previously had camera sliders (yaw, pitch, zoom).
These have been **removed** in favor of mouse controls. Only a
**RESET CAMERA** button remains in the panel.

---

## Voxel Size Hidden

The `voxelSize` slider was removed from the City Editor panel. This value
is too critical to the rendering pipeline to be user-facing — changing it
affects all building dimensions, spacing, and raymarch scaling. It remains
accessible in the Unity Inspector for developers.

---

## Design Decisions

### Why Orthographic?
The isometric orthographic view matches the game's art style and makes it
easy to read the grid-based city layout. Perspective would distort building
proportions at edges.

### Why Not Unity Cinemachine?
The custom orbit camera gives precise control over the focus point and
smooth interpolation without Cinemachine's overhead. The raymarch compute
shader also needs the camera matrices directly, and a simple custom camera
makes this wiring straightforward.

### Pan Speed Scales with Zoom
```csharp
float panSpeed = mapCamera.orthographicSize * 0.005f;
```
When zoomed in (small orthographic size), pan is slow and precise. When
zoomed out (large orthographic size), pan is fast — covering more ground
per pixel of mouse movement.
