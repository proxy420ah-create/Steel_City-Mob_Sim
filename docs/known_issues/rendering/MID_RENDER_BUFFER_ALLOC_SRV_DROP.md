# Mid-Render ComputeBuffer Alloc Drops Sector SRV Binding (`_BuildingMeta`)

**Date**: 2026-10-11
**Status**: 🟢 FIXED (pending playtest)
**Severity**: � HIGH

## Symptoms
```
d3d12: Vertex Shader "SteelCity/VoxelProxyRaymarch" requires a buffer (SRV)
"_BuildingMeta" at index 1, but none provided. Skipping draw calls to avoid crashing.
```
- Sporadic in the editor — once per event, never a repeating spam.
- **Standalone D3D12 build (observed)**: instanced cars and characters spawn
  (registration logs, buffers initialized, `RenderInstancedGroup` called) but
  never render — skipped draws persist while sectors/buildings keep drawing.
- Editor severity originally estimated as "at most a single-frame terrain
  flicker" — build behavior shows the drop can be persistent per frame.

## Root Cause
`_BuildingMeta` is a **vertex-stage SRV** required only by the
`BUILDING_INSTANCING` variant of `VoxelProxyRaymarch` — used exclusively by
`sectorMaterial` in `RenderBakedSectors` (draw at `VoxelChunkManager.cs:2505`),
bound via `sector.cachedPropBlock` (`SetBuffer(propBuildingMeta, ...)` at
line ~2428). Code-side binding was verified complete: every sector in
`bakedSectors` always has a valid `buildingMetaBuffer`, and all buffer-release
paths also remove the sector from the draw list.

Log correlation (Editor.log ×1, Editor-prev.log ×4 — 5/5 occurrences):
the warning fires **only on frames where `RenderInstancedGroup` creates a
ComputeBuffer inside `endCameraRendering`** — i.e. inside URP's active render
pass:
- `Posed voxel buffer allocated: 1769472 voxels for 2 instances` (4×)
- `VoxelVehicle` `stasset` loads → first-render `instanceOffsetBuffer` alloc (1×)

Five buffers were lazily allocated inside `RenderInstancedGroup` (inside
`endCameraRendering` via `VoxelRenderBridge`):
- `instanceOffsetBuffer` (line ~1939)
- `posedVoxelBuffer` (~1953)
- `instanceAnimDataBuffer` (~1961)
- `instanceIKDataBuffer` (~1969)
- `instanceMaterialRemapBuffer` (~2010)

Mechanism (inferred, Unity-side): creating GPU resources inside the render
pass correlates with D3D12 dropping the recorded SRV binding for
subsequently-recorded draws in the same command buffer. D3D12-only
validation; matches known Unity issue-tracker behavior for unbound SRVs.
Not a code-binding bug — all paths provably bind the buffer.

> **Update (2026-10-11 evening)**: the persistent invisible cars/characters in
> the standalone build are also consistent with instanced groups running the
> sector variant — an unbound `_BuildingMeta` SRV on a non-sector draw is
> exactly what that produces (`MULTI_COMPILE_SINGLE_KEYWORD_ALWAYS_ON.md`).
> Whether that build carried the single-keyword pragma is not recorded. Treat
> the mid-render-allocation correlation as plausible hardening, not a proven
> sole cause; the standalone build test after the pragma correction will tell.

## Resolution
Three complementary fixes landed 2026-10-11:

1. **`VoxelChunkManager.EnsureGroupBuffers()`** — pre-sizes all five buffers
   at `instances.Count` capacity (upper bound of `visibleCount`), called from
   **`RegisterInstancedCharacter`** at each `instances.Add` (primary — runs
   where instances are added, outside render) plus a `LateUpdate` straggler
   net. Editor verification showed `LateUpdate` never fires on this component
   (rendering still works because `VoxelRenderBridge` calls `RenderChunks`
   directly — component is disabled/inactive; worth checking the inspector
   checkbox), which is why registration-time sizing is the real fix. The
   in-render lazy-create branches remain as cold fallbacks.
2. **Dummy SRV completeness** — `_BuildingMeta` / `_BuildingPositions` are
   now bound on every draw path: `sharedMaterialBuffer` dummies in the chunk
   and instanced-group prop blocks, `sector.buildingMetaBuffer ??
   sharedMaterialBuffer` fallbacks in the sector path. D3D12 validates every
   declared SRV per draw; unbound = skipped draw.
3. **`#pragma multi_compile_local _ BUILDING_INSTANCING`** (all three passes) —
   insurance against build-time stripping of the runtime-enabled
   `BUILDING_INSTANCING` variant (`sectorMaterial.EnableKeyword` is invisible
   to build-time variant scans, and `shader_feature_local` permits stripping
   unused ones). Precautionary: stripping was never confirmed — sectors
   rendered in this same build. **The `_` is mandatory**: the original
   single-keyword form (`multi_compile_local BUILDING_INSTANCING`) has no
   "disabled" variant, so every instanced draw ran the sector branch and
   cars/characters vanished — see `MULTI_COMPILE_SINGLE_KEYWORD_ALWAYS_ON.md`.

## Related Files
- `Assets/Scripts/UI/VoxelChunkManager.cs` — `EnsureInstancedBuffers`,
  `RenderInstancedGroup`, `RenderBakedSectors`
- `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` — `_BuildingMeta`
  vertex-stage SRV (`BUILDING_INSTANCING` variant)
- `Assets/Scripts/UI/VoxelRenderBridge.cs` — invokes `RenderChunks` inside
  `endCameraRendering`

## Lesson
Don't create `ComputeBuffer`s (or other GPU resources) inside render-pipeline
callbacks. Pre-size at an upper bound in `Update`/`LateUpdate`; keep lazy
creation only as a fallback. Same class of residual risk remains for
`EnsureProxyRT`/`EnsureRenderTargets` (RenderTexture creation inside the
render callback on resolution changes — rare, not yet observed failing).
