## Contents

- Overview
- Why Not LineRenderer?
- Architecture
- The Camera Hookup Gotcha
- Shader: Unlit/InstancedColor
- Per-Type Batching Strategy
- Fallback Behavior (No VoxelRenderBridge)
- Diagnostic Logging
- Troubleshooting
- Related Documents

---

# Path Debug Rendering — Instanced Box Beams via CommandBuffer

**Created**: Aug 8, 2026
**Status**: ✅ COMPLETE — documents the working instanced beam rendering pipeline
**Relates to**: `Assets/Scripts/Sim/PathDebugRenderer.cs`, `Assets/Shaders/InstancedColor.shader`, `Assets/Scripts/UI/VoxelRenderBridge.cs`, `Assets/Scripts/UI/VoxelChunkManager.cs`, `docs/systems/INSTANCING_AND_BUFFERING.md`, `docs/systems/3D_CITY_RENDERING.md`

---

## Overview

Debug pathfinding lines are rendered as **thin oriented box meshes** using `CommandBuffer.DrawMeshInstanced`, composited directly into the voxel raymarch render texture. This replaces the traditional `LineRenderer` approach, which was invisible under the voxel `RawImage` overlay.

Each path segment between two waypoints becomes a unit cube scaled, rotated, and positioned to form a thin beam. Node markers are small vertical boxes at each waypoint. All beams of the same type (Pedestrian, Car, Trolley) are drawn in a single instanced draw call with a shared color.

---

## Why Not LineRenderer?

`LineRenderer` components draw to the camera's normal output buffer. But the voxel rendering pipeline uses a `RawImage` UI overlay that covers the entire screen — anything drawn to the camera output is hidden behind the overlay.

The voxel pipeline renders into a `RenderTexture` via a compute shader (`VoxelChunkManager.RenderChunks()`), then assigns that texture to a `RawImage`. To make debug beams visible, they must be composited **into the same RenderTexture** before it's assigned to the overlay.

**Solution**: Use `CommandBuffer.DrawMeshInstanced` targeting the voxel render texture. The `CommandBuffer` is executed via `Graphics.ExecuteCommandBuffer` during `VoxelRenderBridge.OnEndCameraRendering`, after voxel chunks are rendered but before the RT is assigned to the `RawImage`.

---

## Architecture

### Render Pipeline Order

```
VoxelRenderBridge.OnEndCameraRendering (URP hook)
  ├── chunkManager.RenderChunks()           → compute shader fills colorRT
  ├── PathDebugRenderer.RenderBeamsIntoCamera(camera)
  │     ├── Get VoxelChunkManager.GetColorTexture()  → target RT
  │     ├── RenderBeamsInternal(targetRT, cam)
  │     │     ├── Sort activePaths by type            → contiguous batches
  │     │     ├── For each active path:
  │     │     │     ├── Resolve route nodes → world positions
  │     │     │     ├── Build Matrix4x4.TRS for each segment box
  │     │     │     └── Build Matrix4x4.TRS for each node marker
  │     │     ├── CommandBuffer.SetRenderTarget(targetRT)
  │     │     ├── CommandBuffer.SetViewport(rt dimensions)
  │     │     ├── CommandBuffer.SetViewProjectionMatrices(cam)
  │     │     ├── For each type with segments:
  │     │     │     ├── Set beamMaterial._Color = type color
  │     │     │     ├── Array.Copy → batchBuffer
  │     │     │     └── cmd.DrawMeshInstanced(boxMesh, ..., batchBuffer, count)
  │     │     ├── For each type with markers: (same pattern)
  │     │     └── Graphics.ExecuteCommandBuffer(cmd)
  │     └── cmd.Dispose()
  └── overlayImage.texture = chunkManager.GetColorTexture()  → RawImage shows composited result
```

### Key Components

| Component | File | Role |
|-----------|------|------|
| `PathDebugRenderer` | `Assets/Scripts/Sim/PathDebugRenderer.cs` | Manages active paths, builds matrices, issues CommandBuffer draw calls |
| `InstancedColor.shader` | `Assets/Shaders/InstancedColor.shader` | Unlit transparent shader with `_Color` property, instancing enabled |
| `VoxelRenderBridge` | `Assets/Scripts/UI/VoxelRenderBridge.cs` | URP hook that calls `RenderBeamsIntoCamera` at the right time |
| `VoxelChunkManager` | `Assets/Scripts/UI/VoxelChunkManager.cs` | Provides the target `RenderTexture` via `GetColorTexture()` |

### Per-Type Styling

| Type | Width | Color | Enum |
|------|-------|-------|------|
| Pedestrian | 0.06 | Orange (1, 0.5, 0, 0.85) | `PathDebugType.Pedestrian` |
| Car | 0.16 | Purple (0.6, 0.2, 1, 0.85) | `PathDebugType.Car` |
| Trolley | 0.30 | Green (0, 1, 0.4, 0.85) | `PathDebugType.Trolley` |

---

## The Camera Hookup Gotcha

### Problem

`PathDebugRenderer` needs a `Camera` to set view/projection matrices in the `CommandBuffer`. The original code tried:

```csharp
Camera cam = targetCamera;
if (cam == null) cam = Camera.main;
```

This fails when:
- `targetCamera` is never assigned in the inspector (default state)
- `Camera.main` returns null because the voxel render camera isn't tagged "MainCamera"

The voxel render camera is owned by `VoxelRenderBridge` (via `GetComponent<Camera>()`). It may not have the "MainCamera" tag, so `Camera.main` returns null. Result: `RenderBeamsIntoCamera` silently exits, no beams render, and no error is logged.

### Fix

`VoxelRenderBridge` passes its camera directly:

```csharp
// VoxelRenderBridge.OnEndCameraRendering
pathDebug.RenderBeamsIntoCamera(_camera);
```

```csharp
// PathDebugRenderer.RenderBeamsIntoCamera
public void RenderBeamsIntoCamera(Camera externalCam = null)
{
    Camera cam = externalCam != null ? externalCam
               : (targetCamera != null ? targetCamera : Camera.main);
    ...
}
```

### Lesson

**Never rely on `Camera.main` in rendering pipelines that use custom cameras.** The URP render bridge owns the camera and should pass it explicitly. `Camera.main` depends on a Unity tag that may not be set, and in URP the render camera is often a component on the bridge GameObject, not a standalone tagged camera.

---

## Shader: Unlit/InstancedColor

A minimal unlit transparent shader with GPU instancing support:

```hlsl
Shader "Unlit/InstancedColor"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            // ... standard vert/frag using _Color ...
            ENDCG
        }
    }
}
```

Key properties:
- **`_Color`**: Set per-batch via `MaterialPropertyBlock` (not per-instance). One draw call per type, each with a different color.
- **`ZWrite Off`**: Beams don't write depth — they composite on top of the voxel render.
- **`Cull Off`**: Visible from both sides.
- **`#pragma multi_compile_instancing`**: Required for `DrawMeshInstanced` to provide `unity_InstanceID`.

### Why Not Per-Instance Colors?

`CommandBuffer.DrawMeshInstanced` does not support per-instance properties via `MaterialPropertyBlock` in the same way as `Graphics.RenderMeshInstanced` with `RenderParams`. Passing per-instance colors would require a `ComputeBuffer` with custom shader instancing props.

Instead, we **batch by type** — all segments of the same `PathDebugType` are contiguous in the matrix array (achieved by sorting `activePaths` by type). One draw call per type, with the color set via `MaterialPropertyBlock` before each draw. This gives 3 draw calls max (one per type) instead of per-instance color overhead.

### The MaterialPropertyBlock Color Bug (Fixed)

**Symptom**: When vehicle beams (purple) were enabled alongside pedestrian beams (orange), all beams turned purple — the pedestrian orange color was lost.

**Root cause**: Color was set directly on the shared `beamMaterial`:
```csharp
beamMaterial.SetColor("_Color", col);  // mutates shared material
cmd.DrawMeshInstanced(boxMesh, 0, beamMaterial, 0, batchBuffer, count);
```

Since `CommandBuffer` defers execution until `Graphics.ExecuteCommandBuffer`, the material's state at execution time is what the GPU sees — not what it was at record time. The last color set (purple for Car type) overwrites all prior colors.

**Fix**: Use `MaterialPropertyBlock` per draw call:
```csharp
beamProps.Clear();
beamProps.SetColor("_Color", col);
cmd.DrawMeshInstanced(boxMesh, 0, beamMaterial, 0, batchBuffer, count, beamProps);
```

This is the **same bug pattern** as the instanced character MaterialPropertyBlock issue documented in `INSTANCING_AND_BUFFERING.md`. Any time multiple `CommandBuffer` draw calls share a material but need different properties, use `MaterialPropertyBlock`.

---

## Per-Type Batching Strategy

### Sorting

Before processing, `activePaths` is sorted by type index:

```csharp
activePaths.Sort((a, b) => a.type.CompareTo(b.type));
```

This ensures all Pedestrian segments are contiguous, then all Car segments, then all Trolley — making per-type batch ranges valid.

### Range Tracking

```csharp
var segRanges = new (int start, int count)[MaxTypes];
var markerRanges = new (int start, int count)[MaxTypes];
```

As segments are built, the start index and count for each type is tracked. At render time, `Array.Copy` extracts each type's slice into a reusable `batchBuffer`:

```csharp
System.Array.Copy(segmentMatrices, start, batchBuffer, 0, count);
cmd.DrawMeshInstanced(boxMesh, 0, beamMaterial, 0, batchBuffer, count);
```

### Why Array.Copy?

`CommandBuffer.DrawMeshInstanced` does **not** have a `startInstance` parameter (unlike `Graphics.RenderMeshInstanced`). The only overloads are:

- `DrawMeshInstanced(mesh, submesh, material, pass, matrices)`
- `DrawMeshInstanced(mesh, submesh, material, pass, matrices, count)`
- `DrawMeshInstanced(mesh, submesh, material, pass, matrices, count, properties)`

To draw a subset of a larger array, you must copy the slice into a separate buffer. The `batchBuffer` is pre-allocated at `MaxInstances` size and reused every frame — no GC allocation.

---

## Fallback Behavior (No VoxelRenderBridge)

If no `VoxelRenderBridge` is present in the scene, `PathDebugRenderer.Update()` falls back to drawing beams directly:

```csharp
void Update()
{
    var bridge = FindFirstObjectByType<VoxelRenderBridge>();
    if (bridge == null)
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
            RenderBeamsInternal(null, cam);  // null RT = draws to active target
    }
}
```

When `targetRT` is null, the `CommandBuffer` doesn't call `SetRenderTarget`, so it draws to whatever the current active render target is. This is useful for testing in scenes without the full voxel pipeline.

---

## Diagnostic Logging

The renderer includes periodic diagnostic logs (every 60 frames) that trace the full pipeline:

| Log | When | Purpose |
|-----|------|---------|
| `[PathDebug] RenderBeamsInternal FIRST CALL:` | First entry only | Confirms method is reached, shows RT dims, camera, mapRoot |
| `[PathDebug] RenderBeamsIntoCamera SKIP:` | Every 120 frames if skipping | Identifies which early-exit condition triggered |
| `[PathDebug] RenderBeamsIntoCamera: cam is NULL` | On error | Camera lookup failed |
| `[PathDebug] RenderBeamsIntoCamera EXCEPTION:` | On exception | Silent exception caught |
| `[PathDebug] Path[i] type=... routeCount=...` | Every 60 frames | Per-path state: route, progress, remaining nodes |
| `[PathDebug] Path[i] INVALID` | When removing | Node position returned NaN |
| `[PathDebug] Path[i] resolved N positions` | Every 60 frames | First/last world positions |
| `[PathDebug] Batches: segCount=... markerCount=...` | Every 60 frames | Total instances built |
| `[PathDebug] Type[t] segs=... markers=...` | Every 60 frames | Per-type batch ranges |
| `[PathDebug] CommandBuffer executed. segDraws=X` | Every 60 frames | Confirms draw calls submitted |
| `[VoxelRenderBridge] Calling RenderBeamsIntoCamera` | Every 60 frames | Bridge is calling PDR, path count |
| `[VoxelRenderBridge] PathDebugRenderer.Instance is NULL` | Every 120 frames | PDR doesn't exist yet |

### Registration Logging (VehicleTestSpawner)

When F10 is pressed, the spawner logs:
- Route count, route index, remaining nodes
- Per-node ID and resolved position (or NOT_FOUND)
- PDR instance status, mapRoot, roadGraph node count
- Post-registration active path count

---

## Troubleshooting

### Beams Not Visible

1. **Check console for `[PathDebug]` logs** — if none appear, `RenderBeamsIntoCamera` isn't being called or is early-exiting
2. **`cam is NULL` error** — the camera isn't being passed. Ensure `VoxelRenderBridge` passes `_camera` to `RenderBeamsIntoCamera()`
3. **`SKIP: no active paths`** — no paths are registered. Check that `RegisterPath` was called with valid delegates
4. **`SKIP: mapRoot=False`** — `SetMapRoot()` wasn't called. The spawner should call `pdr.SetMapRoot(cityMap.MapRoot)` before registering paths
5. **`Path[i] INVALID`** — node position resolver returned NaN. Check that the graph contains the node IDs in the route
6. **`CommandBuffer executed. segDraws=0`** — segments were built but all type ranges have count=0. Check the sort and range tracking

### Beams Visible But Wrong Position

- Check `mapRoot.position` in the FIRST CALL log — node positions are local + mapRoot offset
- Check that `resolveNodePos` returns local-space positions (not world-space). The renderer adds `mapRoot.position` internally

### Beams Visible But Wrong Color

- Colors are set per-type-batch on `beamMaterial._Color`. Verify `GetStyle()` returns the expected color for each `PathDebugType`
- The shader uses `Blend SrcAlpha OneMinusSrcAlpha` — alpha values < 1 will blend with the voxel render behind

### Performance

- Max 2048 segment matrices + 2048 marker matrices per frame (pre-allocated)
- `Array.Copy` per type batch is O(count) but trivial (struct copies)
- 3 draw calls max for segments + 3 for markers = 6 total
- No per-frame GC allocation after warmup

---

## Related Documents

- **`docs/systems/INSTANCING_AND_BUFFERING.md`** — GPU instancing patterns for voxel characters/vehicles. The MaterialPropertyBlock isolation pattern applies here too (color set per-batch).
- **`docs/systems/3D_CITY_RENDERING.md`** — High-level 3D rendering architecture, camera system, entity budgets.
- **`docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`** — Three-tier rendering philosophy (bake/instance/individual). Path debug beams are Tier 2 (batched dynamic).
- **`docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`** — Sector baking for static buildings. Uses the same `CommandBuffer` + `DrawMeshInstanced` pattern.
- **`Assets/docs/VOXEL_LIGHTING_AND_SHADOWS.md`** — Voxel raymarch lighting pipeline that produces the RT we composite into.
