# GPU-Driven Rendering Plan — Iterative Path to Indirect Rendering

**Created**: Aug 9, 2026
**Status**: 📋 PLANNED — 6-phase iterative plan
**Relates to**: `docs/systems/INSTANCING_AND_BUFFERING.md`, `docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`, `Assets/Scripts/UI/VoxelChunkManager.cs`, `Assets/Scripts/UI/SectorBaker.cs`

---

## Overview

This document outlines an iterative, 6-phase plan to evolve Steel City's rendering from CPU-driven `DrawMeshInstanced` to GPU-driven `DrawMeshInstancedIndirect` with compute shader culling and GPU-side LOD. Each phase is independently testable — if any phase breaks something, stop and the previous phase still works.

### Current State (Aug 9, 2026)

| Pillar | Maturity | Notes |
|--------|----------|-------|
| **Buffering** | 90% | Sector baking, MaterialPropertyBlock, shared buffers |
| **Instancing** | 90% | Two instancing paths (characters + buildings), 12 draw calls for 984 objects |
| **Caching** | 70% | Voxel cache + AABB cache + parallel preload, missing buffer pooling |
| **Batching** | 30% | Sector baking IS batching, but no GPU-driven indirect rendering |

### Why Now?

At current scale (100 blocks, 984 buildings, 12 draw calls), per-frame CPU cost is ~0.5ms — not a bottleneck. However:

- **500-1000 block cities** will need ~5000-10000 buildings — exceeding the 1023 instance cap
- **GPU frustum culling** becomes critical at 50+ sectors (CPU AABB checks add up)
- **GPU-side LOD** saves significant GPU time on wide camera angles
- **Async scene streaming** requires no CPU readback for visible counts

This plan future-proofs the rendering architecture for scale expansion.

---

## Phase 1: Static Sector TRS Cache

**Effort**: ~30 min | **Risk**: Near zero | **Benefit now**: ~0.1ms saved | **Benefit at scale**: ~5-10ms saved

### What

Cache `Matrix4x4[]` for sector buildings — they never move, so stop rebuilding 984 matrices every frame.

### Changes

- `BakedSector` class gets a `Matrix4x4[] cachedMatrices` field
- Build matrices once at `RegisterSector` time (when `buildingPositions` is already available)
- `RenderBakedSectors` uses `cachedMatrices` instead of rebuilding per-frame

### Files Touched

- `VoxelChunkManager.cs` — `BakedSector` class, `RegisterSector`, `RenderBakedSectors`

### Test Plan

1. Run city — verify identical rendering (no visual change expected)
2. Check debug HUD — CPU draw time should drop slightly
3. Walk camera around — sectors should still appear/disappear correctly (frustum cull still works)
4. **Pass criteria**: Zero visual difference, no errors in console

### Rollback

Revert the `cachedMatrices` field and restore the per-frame matrix build loop.

---

## Phase 2: Buffer Pooling + SubUpdates

**Effort**: ~45 min | **Risk**: Low | **Benefit now**: Zero GC allocs for buffer updates | **Benefit at scale**: Same

### What

Stop recreating `instanceOffsetBuffer` every frame for characters/vehicles. Use `ComputeBufferMode.SubUpdates` + `BeginWrite`/`EndWrite` instead of `SetData`.

### Changes

- `InstancedGroup.instanceOffsetBuffer` created with `ComputeBufferMode.SubUpdates` instead of default mode
- `RenderInstancedGroup` uses `BeginWrite`/`EndWrite` instead of `SetData`:
  ```csharp
  // Before:
  group.instanceOffsetBuffer.SetData(offsets, 0, 0, visibleCount);
  // After:
  var nativeArr = group.instanceOffsetBuffer.BeginWrite(0, visibleCount);
  // ... copy offsets into nativeArr ...
  group.instanceOffsetBuffer.EndWrite(visibleCount);
  ```
- Pool reusable `ComputeBuffer`s — don't release/recreate on count change, just resize the underlying buffer with headroom

### Files Touched

- `VoxelChunkManager.cs` — `InstancedGroup` class, `RenderInstancedGroup`, buffer creation/release

### Test Plan

1. Spawn character + vehicle — verify both render correctly
2. Open Profiler — check GC allocs per frame for buffer updates should be zero
3. Move character around — verify instance offset updates correctly (character moves)
4. Spawn/despawn characters — verify buffer resizes without errors
5. **Pass criteria**: Both entities render, zero GC allocs, no errors

### Rollback

Revert `ComputeBufferMode.SubUpdates` back to default, restore `SetData` calls.

### Gotchas

- `BeginWrite` returns a `NativeArray` — write only, do NOT read from it
- Must call `EndWrite` after writing or the buffer is in an invalid state
- Only works with `ComputeBufferMode.SubUpdates` — throws on other modes

---

## Phase 3: DrawMeshInstancedIndirect for Sectors

**Effort**: ~2-3 hours | **Risk**: Medium | **Benefit now**: Enables Phases 4-5 | **Benefit at scale**: Removes 1023 instance cap

### What

Replace `DrawMeshInstanced` with `DrawMeshInstancedIndirect` for sector rendering. Instance count comes from a GPU buffer instead of a CPU variable. Initially, CPU still writes the instance count (no GPU culling yet — just proving the indirect path works).

### Changes

- Create `GraphicsBuffer` with `Target.IndirectArguments` per sector
- Write `IndirectDrawIndexedArgs` struct to args buffer:
  ```
  indexCountPerInstance = 36  (proxy cube: 6 faces × 2 tris × 3 verts)
  instanceCount = buildingCount (from CPU for now)
  startIndex = 0
  baseVertexIndex = 0
  startInstance = 0
  ```
- Replace:
  ```csharp
  cmd.DrawMeshInstanced(proxyCubeMesh, 0, proxyMaterial, 0, matrices, count, block);
  ```
  With:
  ```csharp
  Graphics.DrawMeshInstancedIndirect(proxyCubeMesh, 0, proxyMaterial, sectorBounds, argsBuffer, 0, block);
  ```
- The `MaterialPropertyBlock` still carries `_VoxelData`, `_BuildingMeta`, `_BuildingPositions` — same as before
- TRS matrices still needed (from Phase 1 cache) — `DrawMeshInstancedIndirect` reads them from a `GraphicsBuffer` set via the property block

### Files Touched

- `VoxelChunkManager.cs` — `BakedSector` class (add `argsBuffer`), `RegisterSector`, `RenderBakedSectors`
- May need a `GraphicsBuffer` for TRS matrices per sector (instead of passing `Matrix4x4[]` to `DrawMeshInstanced`)

### Test Plan

1. Run city — verify all 984 buildings + terrain render identically
2. Open Frame Debugger — should still show 10 sector draws
3. Walk camera around — sectors should still appear/disappear (CPU frustum cull still active)
4. Check instance counts in Frame Debugger — match expected per-sector counts
5. **Pass criteria**: Identical rendering, same draw call count, no errors

### Rollback

Revert to `DrawMeshInstanced` — the `MaterialPropertyBlock` and buffer setup are unchanged, so the old path still works.

### Gotchas

- `IndirectDrawIndexedArgs` must be exactly right or nothing renders / GPU crashes
- The proxy cube mesh uses 36 indices (not 24 vertices — it's indexed)
- `Graphics.DrawMeshInstancedIndirect` is a static method, not a `CommandBuffer` method — may need to use `CommandBuffer.DrawMeshInstancedIndirect` instead for the custom render target
- Bounds parameter is used for frustum culling — set to sector's full AABB

---

## Phase 4: Compute Shader Frustum Culling

**Effort**: ~2-3 hours | **Risk**: Medium | **Benefit now**: Negligible | **Benefit at scale**: Major CPU savings

### What

GPU compute shader culls invisible building instances per sector. Writes visible instance indices to a compacted buffer + visible count to the indirect args buffer. No CPU readback.

### Changes

- New compute shader `SectorCull.compute`:
  ```hlsl
  // Input:
  //   _BuildingPositions[] — world pos + voxelSize per instance
  //   _FrustumPlanes[6]   — camera frustum planes (Vector4 each)
  //   _InstanceCount      — total instances in this sector
  // Output:
  //   _VisibleIndices[]   — compacted list of visible instance IDs
  //   _IndirectArgs       — IndirectDrawIndexedArgs with visibleCount in instanceCount
  //   _VisibleCount       — atomic counter (InterlockedAdd)
  
  #pragma kernel CullInstances
  
  [numthreads(64,1,1)]
  void CullInstances(uint3 id : SV_DispatchThreadID)
  {
      if (id.x >= _InstanceCount) return;
      
      float4 pos = _BuildingPositions[id.x];
      float3 worldPos = pos.xyz;
      float radius = max(pos.w * _MaxDim, 1.0); // approximate AABB radius
      
      // Test against 6 frustum planes
      bool visible = true;
      for (int i = 0; i < 6; i++) {
          float dist = dot(_FrustumPlanes[i], float4(worldPos, 1.0));
          if (dist < -radius) { visible = false; break; }
      }
      
      if (visible) {
          uint writeIdx;
          InterlockedAdd(_VisibleCount, 1, writeIdx);
          _VisibleIndices[writeIdx] = id.x;
      }
  }
  ```
- CPU uploads frustum planes once per frame (6 `Vector4`s = 96 bytes)
- `RenderBakedSectors` dispatches cull compute, then issues indirect draw
- The indirect draw reads `_VisibleIndices[]` to index into `_BuildingPositions[]` and `_BuildingMeta[]`
- Shader needs modification: instead of `unity_InstanceID` indexing directly, it reads `_VisibleIndices[unity_InstanceID]` to get the actual building index

### Files Touched

- New: `Assets/Resources/Shaders/SectorCull.compute`
- `VoxelChunkManager.cs` — dispatch compute, pass frustum planes, modify render path
- `VoxelProxyRaymarch.shader` — building instancing path reads `_VisibleIndices[unity_InstanceID]` instead of `unity_InstanceID` directly

### Test Plan

1. Run city — verify all visible buildings render
2. Walk camera to city edge — only visible sectors should draw (check Frame Debugger)
3. Rotate camera 360° — buildings behind camera should stop drawing
4. **Critical**: Stand at city center looking north — all 10 sectors should draw. Turn 180° — same sectors still draw (they surround you)
5. **Critical**: Move to far edge — only nearby sectors draw
6. **Pass criteria**: No false culling (buildings visible when they should be), no missed culling (buildings behind camera don't draw)

### Rollback

Disable compute dispatch, write full instance count to args buffer from CPU (reverts to Phase 3 behavior).

### Gotchas

- Frustum planes must match the camera's actual frustum — extract from `Camera.CalculateFrustumPlanes()`
- Sphere-vs-plane test is an approximation — use building AABB radius, not voxel size
- `InterlockedAdd` requires a `RWStructuredBuffer<uint>` with `UNITY_COUNTER` or manual atomic
- Dispatch size: `ceil(instanceCount / 64)` thread groups
- If a building is on the frustum boundary, it should be visible (conservative test — err on the side of drawing)

---

## Phase 5: GPU-Side LOD

**Effort**: ~3-4 hours | **Risk**: Higher | **Benefit now**: GPU savings on wide shots | **Benefit at scale**: Critical

### What

Compute shader assigns LOD tier per instance based on screen-space size. Distant buildings get fewer raymarch steps (`_MaxSteps` reduced), saving GPU fragment work.

### Changes

- `SectorCull.compute` extended to also write `_InstanceLOD[]`:
  ```hlsl
  // LOD tiers:
  // 0 = Near  (full maxSteps, e.g. 264)
  // 1 = Mid   (half maxSteps, e.g. 132)
  // 2 = Far   (quarter maxSteps, e.g. 66)
  // 3 = Cull  (don't draw at all)
  
  // LOD based on projected screen size:
  float distance = length(worldPos - _CameraPos);
  float screenSize = radius / distance; // approximate
  int lod = screenSize > _LODNearThreshold ? 0 :
            screenSize > _LODMidThreshold  ? 1 :
            screenSize > _LODFarThreshold  ? 2 : 3;
  ```
- Shader reads `_InstanceLOD[unity_InstanceID]` and scales `maxSteps`:
  ```hlsl
  int lod = _InstanceLOD[visibleIdx];
  int effectiveMaxSteps = maxSteps >> lod; // 264, 132, 66
  ```
- LOD hysteresis: store previous LOD per instance, only switch if new LOD is different by 2+ tiers (prevents flickering at boundaries)
- LOD 3 (cull) instances are not written to `_VisibleIndices[]` — same as frustum cull

### Files Touched

- `SectorCull.compute` — add LOD logic
- `VoxelProxyRaymarch.shader` — read `_InstanceLOD`, scale `maxSteps`
- `VoxelChunkManager.cs` — pass LOD thresholds, upload LOD buffer

### Test Plan

1. Run city — check that distant buildings render with fewer steps (slightly chunkier but recognizable)
2. Slowly zoom camera toward a far building — it should gradually increase in detail
3. **Critical**: No flickering at LOD boundaries (hysteresis must work)
4. Check GPU time in Profiler — should drop for wide camera angles where many distant buildings are visible
5. **Pass criteria**: No flickering, no buildings disappearing at LOD 2, GPU time reduced on wide shots

### Rollback

Set all LOD thresholds to 0 (everything is LOD 0 = full detail). Effectively disables LOD while keeping the infrastructure.

### Gotchas

- Hysteresis is essential — without it, buildings flicker between LOD tiers every frame at the boundary
- LOD 2 (quarter steps) may look too chunky — tune the threshold or add a LOD 2.5 that uses cheap shading
- `maxSteps >> lod` is a right-shift — works for powers of 2. If maxSteps isn't a power of 2, use explicit values
- The `_InstanceLOD` buffer needs to persist across frames for hysteresis — don't clear it each frame

---

## Phase 6: Remove 1023 Cap + Scale Test

**Effort**: ~1 hour | **Risk**: Low (if Phases 3-4 are solid) | **Benefit now**: Validation | **Benefit at scale**: 500-1000 blocks feasible

### What

With indirect rendering, there's no instance cap. Optionally merge all 9 building sectors into fewer, larger buffers.

### Changes

- Option A: Keep geographic sectors but increase max buildings per sector beyond 1023
- Option B: Merge all building sectors into 1 mega-sector (984 instances in one indirect draw)
  - Requires one merged voxel buffer for ALL buildings (not just per-sector)
  - Larger upload but fewer draw calls
- Test with 500-block city layout (if available) or scale up current layout

### Files Touched

- `SectorBaker.cs` — adjust sector size limits
- `VoxelChunkManager.cs` — adjust dispatch sizes for larger instance counts
- `CityMap3D.cs` — adjust sector划分 logic

### Test Plan

1. Load 100-block city — verify all buildings render (regression test)
2. If 500-block layout available: load it, verify all buildings render
3. Check draw call count — should be ~3 (1 terrain + 1 buildings + 1 instanced) if Option B
4. Check frame time at 500 blocks vs 100 blocks — should scale sub-linearly (GPU culling helps)
5. **Pass criteria**: All buildings render at scale, no crashes, frame time acceptable

### Rollback

Revert sector merge — keep 9 geographic sectors with 1023 cap per sector.

---

## Summary

| Phase | Effort | Benefit Now | Benefit at Scale | Risk | Status |
|-------|--------|------------|-----------------|------|--------|
| 1. TRS Cache | 30 min | ~0.1ms saved | ~5-10ms saved | Near zero | 🔲 Not started |
| 2. Buffer Pooling | 45 min | Zero GC allocs | Same | Low | 🔲 Not started |
| 3. Indirect Draw | 2-3 hr | Enables 4-5 | Removes 1023 cap | Medium | 🔲 Not started |
| 4. GPU Culling | 2-3 hr | Negligible | Major CPU savings | Medium | 🔲 Not started |
| 5. GPU LOD | 3-4 hr | GPU savings on wide shots | Critical | Higher | 🔲 Not started |
| 6. Scale Test | 1 hr | Validation | 500-1000 blocks feasible | Low | 🔲 Not started |

**Total estimated effort**: ~10-12 hours

### Execution Order

Phases MUST be done in order — each builds on the previous:
- Phase 1 is prerequisite for Phase 3 (cached matrices needed for indirect draw)
- Phase 2 is independent but should be done before Phase 4 (buffer patterns reused)
- Phase 3 is prerequisite for Phase 4 (indirect draw needed for GPU-written args)
- Phase 4 is prerequisite for Phase 5 (compute dispatch extended for LOD)
- Phase 6 validates everything at scale

### Dependencies on Existing Code

| Component | Phase | What's Needed |
|-----------|-------|--------------|
| `BakedSector` class | 1, 3 | Add `cachedMatrices`, `argsBuffer` fields |
| `RenderBakedSectors` | 1, 3, 4 | Modify render path progressively |
| `RenderInstancedGroup` | 2 | Switch to `BeginWrite`/`EndWrite` |
| `VoxelProxyRaymarch.shader` | 4, 5 | Add `_VisibleIndices` lookup, `_InstanceLOD` scaling |
| New: `SectorCull.compute` | 4, 5 | New compute shader for GPU culling + LOD |
| `SectorBaker.cs` | 6 | Adjust sector size limits |

---

## Related Documents

- **`docs/systems/INSTANCING_AND_BUFFERING.md`** — Current instancing/buffering architecture (the starting point)
- **`docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`** — Original sector baking proposal (some ideas now realized)
- **`docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`** — Rendering tier classification
- **`docs/systems/INSTANCING_AND_BUFFERING_VISUAL.html`** — Visual guide companion
