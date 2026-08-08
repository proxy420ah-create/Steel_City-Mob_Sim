## Contents

- Overview
- Current Architecture (as of this document)
- Known Gaps in the Current Sector Baking Path
- Proposed Enhancement: GPU-Driven Indirect Rendering
- Phased Rollout Plan
- Interaction with Working-Week Building Mutation / Rebaking
- Risks and Open Questions
- Recommendation

---

# GPU-Driven Sector Rendering — Design Doc

**Created**: Aug 8, 2026
**Status**: 📐 Proposed — not yet implemented
**Relates to**: `Assets/Scripts/UI/VoxelChunkManager.cs`, `Assets/Scripts/UI/SectorBaker.cs`, `Assets/Scripts/UI/CityMap3D.cs`, `Assets/Resources/Shaders/VoxelProxyRaymarch.shader`

---

## Overview

The city's static buildings are rendered with a raymarched proxy-cube technique: each building (or, after sector baking, each *sector* of buildings) is drawn as a scaled cube mesh, and a fragment shader raymarches the voxel volume inside it. This is cheap per-pixel relative to true voxel geometry, but each draw call still costs CPU-side submission overhead (buffer rebinding, matrix upload, SetPass).

**Sector baking** (already implemented) solves the immediate scaling problem: instead of one `DrawMesh` call per building (~1600 draws for a full city), buildings are merged into per-sector buffers and drawn with one `DrawMeshInstanced` call per sector (~13-49 draws, depending on `sectorSizeBlocks`). This document covers the *next* step: removing per-sector CPU-side culling and draw submission entirely by moving to GPU-driven indirect rendering, while staying on the Built-in Render Pipeline (no URP/HDRP migration required).

This is a follow-up, not a replacement — it assumes sector baking is already working and stable at full city scale.

---

## Current Architecture

### Bake time (`SectorBaker.BakeAllSectors`)
- Groups blocks into `sectorSizeBlocks × sectorSizeBlocks` sectors.
- Concatenates all buildings' voxel data into one flat `uint[]` per sector.
- Builds per-building metadata: `_BuildingMeta[i] = (bufferOffset, dimsX, dimsY, dimsZ)`, `_BuildingPositions[i] = (worldX, worldY, worldZ, voxelSize)`.
- Computes sector-level AABB (`sectorMin`/`sectorMax`).
- Calls `VoxelChunkManager.RegisterSector(...)` to upload buffers and store the sector.

### Render time (`VoxelChunkManager.RenderBakedSectors`)
- One shared `sectorMaterial` (clone of `proxyMaterial` with `BUILDING_INSTANCING` keyword enabled).
- Per sector: CPU frustum test (`GeometryUtility.TestPlanesAABB`) + distance-to-AABB test against `maxRenderDistance`, gated by a `disableSectorCulling` toggle.
- Per sector: builds a `Matrix4x4[]` (one TRS per building) sized to that building's own dims × per-instance `voxelSize`.
- Per sector: binds `_VoxelData`/`_BuildingMeta`/`_BuildingPositions`/`_VoxelSize` into a **per-sector cached `MaterialPropertyBlock`** (fixed recently — previously these were set directly on the shared material, which caused every sector to render with the *last* sector's buffers since `CommandBuffer.DrawMeshInstanced` reads material state at execution time, not record time).
- Issues one `cmd.DrawMeshInstanced(proxyCubeMesh, 0, sectorMaterial, 0, matrices, buildingCount, sectorBlock)` per surviving sector.

### Shader (`VoxelProxyRaymarch.shader`)
- `BUILDING_INSTANCING` variant reads `_BuildingMeta[unity_InstanceID]` / `_BuildingPositions[unity_InstanceID]` in `vert()`, passes per-building `voxelSize` and dims through `Varyings` to `frag()`, and raymarches using those instead of the uniform `_VoxelSize`/`_VolumeDims`.

This is a solid foundation. The remaining cost is **CPU-driven**: one C# loop per frame decides which sectors are visible, builds their matrix arrays, and issues their draw calls.

---

## Known Gaps in the Current Sector Baking Path

These are cheap to fix and should be done **before** attempting the larger GPU-driven rewrite below:

1. **No LOD on baked sectors.** `RenderBakedSectors` hardcodes `maxSteps` / `cheapShading = 0` / `unlitLod = 0` for every sector regardless of distance or screen coverage. `RenderProxyChunks` already computes a screen-ratio-based LOD tier for individual (non-baked) chunks — that logic needs to be applied per-sector (or per-building) here too. Without it, a sector far in the distance still raymarches every building at full step count, which is a GPU fill-rate cost baking does nothing to address.
2. **No depth sort.** `RenderProxyChunks` sorts its draw list nearest-first for early-Z rejection. `bakedSectors` draws in registration order — occluded far sectors don't benefit from near sectors having written depth first.
3. **1023-instance cap risk.** `DrawMeshInstanced` supports at most 511 (with `worldToObject` matrix) or 1023 instances (with `assumeuniformscaling`). A dense sector (large `sectorSizeBlocks` × many small buildings per block) could silently exceed this and fail to render — indistinguishable from a culling bug.

---

## Proposed Enhancement: GPU-Driven Indirect Rendering

### The idea

Replace the per-sector CPU culling + per-sector `DrawMeshInstanced` loop with:

1. **One global set of buffers** for the whole city (not per-sector): a single merged `_VoxelData` buffer, a single `_BuildingMeta` buffer, a single `_BuildingPositions` buffer, each building tagged with a sector/cell index if needed for locality.
2. **A compute shader "cull pass"** that runs once per frame:
   - Reads every building's AABB (derivable from `_BuildingPositions` + `_BuildingMeta` dims).
   - Tests against camera frustum planes + `maxRenderDistance` (same math currently done in C# in `RenderBakedSectors`, just moved to HLSL).
   - Appends surviving building indices to a `StructuredBuffer` (via `AppendStructuredBuffer` or an atomic counter + indexed write).
   - Writes the surviving count into a small `ComputeBuffer` formatted as `GraphicsBuffer.IndirectArgs` (instance count, etc.).
3. **One (or a handful) `CommandBuffer.DrawMeshInstancedIndirect` call** using that args buffer. The vertex shader indexes into the surviving-instance list (instead of `unity_InstanceID` directly into `_BuildingMeta`) to find which building each instance corresponds to.

### Why this is available without URP

`DrawMeshInstancedIndirect` (and the non-command-buffer `Graphics.DrawMeshInstancedIndirect`) is a Built-in Render Pipeline API that predates URP — it does not require the SRP Batcher or BatchRendererGroup, both of which are SRP-only (confirmed: Unity's own render-pipeline feature comparison lists SRP Batcher and BRG as "Not supported" / "No" for Built-in RP). Indirect draws also don't have the 511/1023 instance ceiling of `DrawMeshInstanced`, because the instance count is read from a GPU buffer at draw time rather than baked into a fixed C# array — this incidentally also solves gap #3 above for free.

### What this removes from the CPU

- The per-sector `GeometryUtility.TestPlanesAABB` / `DistanceToAABB` loop (`disableSectorCulling` path) — replaced by the GPU cull pass.
- The per-sector `Matrix4x4[]` construction loop — replaced by the shader reading position/dims directly from the global buffers per surviving instance.
- Per-sector `MaterialPropertyBlock` setup — replaced by one set of global buffer bindings set once per frame.

### Before / after (approximate, depends on `sectorSizeBlocks` and city size)

| | Today (sector baking) | Proposed (GPU-driven indirect) |
|---|---|---|
| Draw calls, full city in view | ~13-49 (one per sector) | 1-2 |
| Visibility decision | CPU, per-sector AABB test | GPU compute, per-building |
| Instance count ceiling | 511/1023 per sector | None (buffer-driven) |
| Engine migration required | No | No |

---

## Phased Rollout Plan

**Phase 0 (already done)**: Sector baking + per-sector `MaterialPropertyBlock` fix.

**Phase 1 (do first, low risk)**:
- Add screen-ratio-based LOD tiers to `RenderBakedSectors` (mirror `RenderProxyChunks`'s formula).
- Sort `bakedSectors` nearest-first before drawing.
- Add an assert/guard for sectors exceeding the instance cap (either split the sector's draw into batches, or clamp `sectorSizeBlocks` at bake time so it can't happen).
- **Stress-test at full city size before moving to Phase 2.** If baking + LOD holds 60fps at full city scale, GPU-driven indirect rendering becomes a "raise the ceiling further" project, not a blocker.

**Phase 2 (bigger lift, do only if Phase 1 isn't enough headroom)**:
- Merge per-sector buffers into one global city-wide buffer set.
- Write the compute-shader cull pass (frustum + distance, matching current CPU logic).
- Add the indirect-args buffer and switch `RenderBakedSectors` (or its successor) to `DrawMeshInstancedIndirect`.
- Update `VoxelProxyRaymarch.shader`'s `BUILDING_INSTANCING` path to resolve building index through the surviving-instance list instead of `unity_InstanceID` directly.
- Sector boundaries become primarily a **bake-time organizational unit** (for partial rebakes, see below) rather than a **render-time draw-call unit**.

---

## Interaction with Working-Week Building Mutation / Rebaking

**Question**: does this complement the plan to update sectors/blocks on building changes during the Working Week and rebake after animations finish?

**Yes — and it clarifies why the two-path architecture already in the codebase is the correct shape, not a stopgap.**

### Why baked sectors are the wrong representation for live mutation

A baked sector's `_VoxelData` is one large concatenated buffer shared by every building in that sector. Damaging or destroying a single building mid-week would mean either:
- Re-uploading the *entire* sector's merged buffer just to change one building's voxels (defeats the point of merging), or
- Maintaining a separate small "dirty" buffer per building anyway (at which point you've reinvented individual per-building buffers with extra bookkeeping).

Neither is a good trade during real-time gameplay.

### Why the existing per-building path is the right one for Working Week

`CityMap3D.BuildMap` already has two paths (`@/c/Users/NADECC/ATSTradingDashboard Project/Cursor Workshop/SteelCityMobSim/Assets/Scripts/UI/CityMap3D.cs:865-912`):
- `useSectorBaking = true` → merged sector buffers (cheap to draw, expensive to mutate).
- `useSectorBaking = false` → one `ComputeBuffer` per building via `BuildRaymarchBlock` (cheap to mutate — `SetData` on one small buffer — more expensive to draw at scale).

The natural split: **Working Week uses the per-building path** (fewer buildings in view at once via `FollowCamera`, needs per-voxel mutability for damage/fire/destruction animations), and **Planning view uses the baked sector path** (full city visible at once via the ortho map camera, needs the draw-call reduction, doesn't need per-frame mutability since nothing is animating).

### Rebake trigger point

`WeekTransition.RunTransition` already has a natural, already-hidden integration point: the `onReady` callback fires during the "Loading..." phase, while the screen is fully black (`@/c/Users/NADECC/ATSTradingDashboard Project/Cursor Workshop/SteelCityMobSim/Assets/Scripts/UI/WeekTransition.cs:41-59`). Re-baking affected sectors here is free from the player's perspective — it's already a loading screen.

### Partial rebake, not full rebake

Because `VoxelChunkManager.RegisterSector`/`UnregisterSector` are keyed by sector name and operate independently, a rebake after the Working Week does **not** need to touch every sector — only the sectors containing blocks that changed (damaged/destroyed/rebuilt buildings). This holds true whether sectors are drawn via today's per-sector `DrawMeshInstanced` or the proposed GPU-driven indirect path — partial rebake is a bake-time concern (which sectors' CPU-side voxel data changed), independent of how the render path consumes the resulting buffers.

If Phase 2 (global merged buffers) is implemented later, "partial rebake" becomes "re-upload the sub-range of the global buffer corresponding to the changed sector's buildings" rather than "swap out one sector's `ComputeBuffer`" — same concept, slightly different plumbing (`ComputeBuffer.SetData` with an offset/count range instead of a full buffer swap).

### Summary of the interaction

The two ideas are complementary and operate on different axes:
- **Sector baking / GPU-driven rendering** — how efficiently the *Planning* view's static city draws.
- **Per-building path + partial rebake at week transitions** — how the *Working Week* mutates individual buildings cheaply, then hands the result back to the baked representation for the next Planning phase.

Neither blocks the other. Implementing GPU-driven indirect rendering later doesn't require changing the rebake trigger, granularity, or the per-building mutation path — only how the *result* of a bake gets drawn.

---

## Risks and Open Questions

- **Compute shader cull pass cost**: needs to run once per frame over every building in the city (not just visible ones) to determine visibility. For a few thousand buildings this should be trivial on GPU, but should be profiled, not assumed.
- **Indirect draw debugging is harder**: bugs in the args buffer (wrong instance count, stale data) tend to manifest as "nothing draws" or "wrong buildings draw" with less obvious error messages than the current CPU path. Budget time for a debug/validation mode (e.g., a CPU-computed shadow count to compare against the GPU-computed one).
- **Sector-vs-global buffer layout for partial rebakes**: if Phase 2 moves to one global buffer, changed-building updates become sub-range `SetData` calls — need to confirm `ComputeBuffer.SetData(data, managedBufferStartIndex, computeBufferStartIndex, count)` overload covers this cleanly, or keep a hybrid (global position/meta buffers, but still-separate per-sector voxel data buffers) if that's simpler.
- **Priority**: Phase 2 should only be pursued if Phase 1 (LOD + sort + instance-cap guard) doesn't provide enough headroom at full city scale. It's a meaningfully larger engineering effort and shouldn't be started speculatively.

---

## Recommendation

Ship Phase 1 (sector LOD, depth sort, instance-cap guard) now — small, low-risk, directly addresses the GPU fill-rate concern already identified. Stress-test at full city size. Only pursue Phase 2 (GPU-driven indirect rendering) if that stress test reveals the current per-sector `DrawMeshInstanced` approach is still the bottleneck. Keep the Working Week / per-building mutation + partial-rebake-at-transition plan as-is — it's orthogonal to and unaffected by which render path baked sectors eventually use.
