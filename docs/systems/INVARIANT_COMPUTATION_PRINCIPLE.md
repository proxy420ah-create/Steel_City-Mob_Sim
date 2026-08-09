# Invariant Computation Principle — "Do Once, Not Repeat Constantly"

**Created**: Aug 9, 2026
**Status**: ✅ ACTIVE — living document, audit at each milestone
**Relates to**: `GPU_DRIVEN_RENDERING_PLAN.md`, `INSTANCING_AND_BUFFERING.md`, `OPTIMIZATION_VISUAL.html`, `Assets/Scripts/UI/VoxelChunkManager.cs`, `Assets/Scripts/Sim/VoxelCollisionWorld.cs`

---

## Overview

This document codifies the single most powerful optimization pattern in real-time rendering: **if a computation produces the same result every time, do it once — not every frame.**

This sounds obvious. In practice, it's the #1 source of wasted CPU/GPU cycles in game engines. Code that "works" gets written first, correctness is verified, and nobody goes back to ask "wait, does this actually need to run 120 times per second?"

This document serves three purposes:
1. **Teach the principle** with case studies from our own codebase
2. **Provide an audit checklist** for catching invariant computation during code review
3. **Track known candidates** that have been identified but not yet fixed

---

## The Principle

### Definition

> **Invariant computation** is any code that:
> - Runs in a hot path (per-frame, per-tick, per-entity)
> - Produces the same output every time given the same inputs
> - Has inputs that do not change between invocations
>
> **Rule**: If all three conditions are met, the computation should be moved to a cold path (load time, registration time, or first-use) and the result cached.

### Hot Path vs Cold Path

| Path | Frequency | Examples |
|------|-----------|----------|
| **Hot** | 60-120× per second | `Update()`, `OnRenderImage()`, `RenderBakedSectors()`, `RenderInstancedGroup()` |
| **Cold** | Once or rarely | `Start()`, `Awake()`, `RegisterSector()`, `LoadChunk()`, `BuildMap()` |

### The Three Questions

Before writing any code in a hot path, ask:

1. **Does the input change between calls?** If no → cache the result.
2. **Is the output deterministic given the input?** If yes → cache the result.
3. **Am I allocating memory (arrays, objects, structs)?** If yes → can I reuse a cached allocation?

If you answer "no, yes, yes" → you have an invariant computation candidate.

---

## Case Studies (Completed Fixes)

### Case Study 1: TRS Matrix Cache

**File**: `VoxelChunkManager.cs` — `RenderBakedSectors()` / `RegisterSector()`

**What was repeated**: Every frame, for 10 sectors × 984 buildings total:
- Allocate `new Matrix4x4[buildingCount]` (10 array allocations)
- Loop through all buildings, read metadata, compute size/center
- Call `Matrix4x4.TRS()` for each building (984 calls)
- Pass to `DrawMeshInstanced()`

**Why it's invariant**: Buildings are static. They don't move, rotate, or change size. The TRS matrix for building #500 is identical on frame 1 and frame 100,000.

**Fix**: Move computation to `RegisterSector()` (cold path). Store in `sector.cachedMatrices`. Hot path just reads the reference.

**Impact**:
- 984 `Matrix4x4.TRS()` calls per frame → 0
- 10 array allocations per frame → 0
- ~15KB GC pressure per frame → 0
- Scales: at 500 blocks, saves 5-10ms per frame

**Date fixed**: Aug 9, 2026 (Phase 1 GPU-Driven Rendering)

---

### Case Study 2: Collision World Flat Array

**File**: `VoxelCollisionWorld.cs` — `RegisterTerrainChunk()`

**What was repeated**: 13.9 million `Dictionary<Vector3Int, byte>` inserts at load time:
- Hash each `Vector3Int` key (XOR of x/y/z)
- Probe bucket, walk collision chain
- Internal resize ~24 times (each copies ALL existing entries)

**Why it's invariant**: The voxel collision data is written once at load and never modified. The dictionary's overhead (hashing, bucketing, chaining) is pure waste for write-once-read-many data.

**Fix**: Replace `Dictionary<Vector3Int, byte>` with flat `byte[]` array. Index = `x * strideY * strideZ + y * strideZ + z`. Direct array write, no hashing.

**Impact**:
- 78,466ms → 371ms load time (211× faster)
- 500MB+ memory → 13.3MB
- 13.9M hash computations → 0

**Secondary fix**: Grid expansion in "all directions" — when chunks have negative offsets relative to origin, shift origin and `Array.Copy` existing data. ~10 expansions for a 10×10 city.

**Date fixed**: Aug 9, 2026

---

## Known Candidates (Pending Fixes)

These have been identified during the Aug 9 audit but not yet implemented. They are tracked here so they are not forgotten.

### Candidate 1: Sector Bounds Recomputed Every Frame

| Field | Value |
|-------|-------|
| **Status** | 🔴 PENDING |
| **File** | `VoxelChunkManager.cs` |
| **Location** | `RenderBakedSectors()` lines ~1398-1404 |
| **Hot path cost** | 10 × `new Bounds()` + center/size math per frame |
| **Why invariant** | `sectorMin`, `sectorMax`, `voxelSize` set at `RegisterSector()` and never change |
| **Proposed fix** | Add `sector.cachedBounds` field, compute once in `RegisterSector()` |
| **Estimated savings** | ~0.05ms/frame (scales with sector count) |
| **Priority** | HIGH — same code path as TRS cache, same pattern |

### Candidate 2: MaterialPropertyBlock Clear + Re-set Every Frame

| Field | Value |
|-------|-------|
| **Status** | 🔴 PENDING |
| **File** | `VoxelChunkManager.cs` |
| **Location** | `RenderBakedSectors()` lines ~1431-1436, `RenderInstancedGroup()` lines ~1176-1187 |
| **Hot path cost** | 10+ × `Clear()` + 4-8 × `SetBuffer/SetFloat` per frame |
| **Why invariant** | Buffers and voxel size never change after registration. The property block already holds correct values from last frame. |
| **Proposed fix** | Set buffer/float properties once at `RegisterSector()` time. Only call `Clear()` + re-set if a buffer is replaced (add a `dirty` flag). |
| **Estimated savings** | ~0.03ms/frame + reduced managed call overhead |
| **Priority** | HIGH — eliminates redundant API calls in the main render loop |
| **Risk** | LOW — if a buffer is ever replaced, just set the dirty flag and re-set. The `Clear()` is defensive but wasteful. |

### Candidate 3: Instanced Group Size/Pad Recomputed Every Frame

| Field | Value |
|-------|-------|
| **Status** | 🟡 PENDING |
| **File** | `VoxelChunkManager.cs` |
| **Location** | `RenderInstancedGroup()` lines ~1144-1148 |
| **Hot path cost** | 4 × `new Vector3()` + multiply/add per group per frame |
| **Why invariant** | `group.dimX/dimY/dimZ` and `group.voxelSize` set at group creation, never change |
| **Proposed fix** | Add `group.cachedPaddedSize` and `group.cachedPaddedHalf` fields, compute at group creation |
| **Estimated savings** | ~0.01ms/frame (small, but free) |
| **Priority** | MEDIUM — low cost but trivially easy to fix |

### Candidate 4: Chunk Tight AABB Recomputed Twice Per Frame

| Field | Value |
|-------|-------|
| **Status** | 🟡 PENDING |
| **File** | `VoxelChunkManager.cs` |
| **Location** | Culling pass lines ~1741-1749, Draw pass lines ~1900-1909 |
| **Hot path cost** | 2 × (6 `new Vector3()` + multiply) per chunk per frame. Also `boundsRadius` at line ~1929. |
| **Why invariant** | `chunk.tightMinX/maxX/etc` and `chunk.voxelSize` set at chunk load, never change. (Note: `chunkWorldPos` CAN change if `hostObject` moves, so center is not fully invariant — but tight SIZE and boundsRadius are.) |
| **Proposed fix** | Cache `chunk.cachedTightSize` and `chunk.cachedBoundsRadius` at load time. `tightCenter` still needs per-frame computation if `hostObject` exists, but for static chunks (no hostObject), cache that too. |
| **Estimated savings** | ~0.02ms/frame (scales with chunk count) |
| **Priority** | MEDIUM — double computation is wasteful, but chunk count is currently low |

### Candidate 5: Corners Array Allocated Per Chunk Per Frame

| Field | Value |
|-------|-------|
| **Status** | 🟡 PENDING |
| **File** | `VoxelChunkManager.cs` |
| **Location** | Culling pass lines ~1800-1810 |
| **Hot path cost** | 1 × `new Vector3[8]` allocation per chunk per frame |
| **Why invariant** | The array itself is just a scratch buffer — same size every time, contents derived from bounds |
| **Proposed fix** | Use a `static readonly Vector3[8]` scratch array, fill it per-chunk without allocation |
| **Estimated savings** | Eliminates N array allocations per frame (N = visible chunk count) |
| **Priority** | MEDIUM — GC pressure reduction, especially at scale |

### Candidate 6: Instanced Group Arrays Allocated Every Frame

| Field | Value |
|-------|-------|
| **Status** | 🟢 PENDING (scales with character count) |
| **File** | `VoxelChunkManager.cs` |
| **Location** | `RenderInstancedGroup()` lines ~1140-1141 |
| **Hot path cost** | 2 × `new T[visibleCount]` per group per frame |
| **Why NOT fully invariant** | Characters/vehicles move, so matrix CONTENTS change. But the ARRAY SIZE is bounded by `group.instances.Count` which rarely changes. |
| **Proposed fix** | Pool arrays per group: `group.cachedOffsets` and `group.cachedMatrices` resized only when instance count grows. Reuse the same arrays each frame. |
| **Estimated savings** | Eliminates 2 × G array allocations per frame (G = group count). GC pressure scales with character count. |
| **Priority** | LOW now (few characters) → HIGH when stress-testing with 1000+ characters |

---

## Audit Checklist

Use this checklist during code review or before committing new hot-path code:

### Per-Frame Code Audit

- [ ] **Does this code run in `Update()`, `OnRenderImage()`, `LateUpdate()`, or any per-frame render callback?**
- [ ] **Does it allocate `new` arrays, lists, or objects?** → Can these be pooled or cached?
- [ ] **Does it call `MaterialPropertyBlock.Clear()`?** → Are the re-set values actually different from last frame?
- [ ] **Does it compute `Bounds`, `Matrix4x4`, `Vector3` from data that doesn't change?** → Cache at load/registration time
- [ ] **Does it call `GeometryUtility.CalculateFrustumPlanes()`?** → This allocates a `Plane[6]` array. Can it be cached if the camera doesn't move?
- [ ] **Does it compute `Quaternion.Inverse()`?** → If the rotation doesn't change, cache the inverse.
- [ ] **Does it call `new Material(...)` or `new Texture2D(...)`?** → These should NEVER be in a hot path.
- [ ] **Does it use LINQ (`Count()`, `Where()`, `Select()`)?** → LINQ allocates enumerators and lambda closures. Replace with manual loops.

### Load-Time Code Audit

- [ ] **Does it use `Dictionary<K,V>` for write-once data?** → Consider flat array with direct indexing
- [ ] **Does the dictionary grow unpredictably?** → Pre-size with `new Dictionary<K,V>(expectedCount)` to avoid resizes
- [ ] **Does it call `Array.Copy` or `List<T>.Add` in a tight loop?** → Pre-allocate to final size if known

### General Questions

- [ ] **Is the same computation done in both the cull pass and the draw pass?** → Compute once, pass the result
- [ ] **Is a static object being treated as dynamic?** → Buildings don't move. Terrain doesn't change. Cache their transforms.
- [ ] **Does the code scale linearly with city size?** → At 500 blocks, will this still be fast enough?

---

## Anti-Patterns to Watch For

### 1. "Defensive" PropertyBlock Clear

```csharp
// ANTI-PATTERN: Clear + re-set every frame "just in case"
block.Clear();
block.SetBuffer(propVoxelData, sector.mergedVoxelBuffer);  // same buffer as last frame
block.SetFloat(propVoxelSize, sector.voxelSize);           // same value as last frame
```

```csharp
// BETTER: Set once, only re-set if dirty
if (sector.propBlockDirty)
{
    sector.cachedPropBlock.SetBuffer(propVoxelData, sector.mergedVoxelBuffer);
    sector.cachedPropBlock.SetFloat(propVoxelSize, sector.voxelSize);
    sector.propBlockDirty = false;
}
```

### 2. Per-Frame Array Allocation

```csharp
// ANTI-PATTERN: New array every frame
var corners = new Vector3[8] { ... };
```

```csharp
// BETTER: Reuse static scratch array
private static readonly Vector3[] s_corners = new Vector3[8];
// Fill s_corners each frame, no allocation
```

### 3. Dictionary for Static Data

```csharp
// ANTI-PATTERN: Dictionary for write-once-read-many
var dict = new Dictionary<Vector3Int, byte>();
for (int i = 0; i < 13_900_000; i++)
    dict[positions[i]] = values[i];
```

```csharp
// BETTER: Flat array with direct indexing
var array = new byte[gridX * gridY * gridZ];
for (int i = 0; i < 13_900_000; i++)
    array[ComputeIndex(positions[i])] = values[i];
```

### 4. Redundant Computation Across Passes

```csharp
// ANTI-PATTERN: Compute AABB in cull pass, then again in draw pass
// Cull pass:
Vector3 tightCenter = ComputeTightCenter(chunk);
Bounds bounds = new Bounds(tightCenter, ComputeTightSize(chunk));

// Draw pass (same frame, same chunk):
Vector3 tightCenter = ComputeTightCenter(chunk);  // SAME computation again!
```

```csharp
// BETTER: Cache on the chunk object at load time
chunk.cachedTightCenter = ComputeTightCenter(chunk);
chunk.cachedTightSize = ComputeTightSize(chunk);
// Both passes read from chunk.cached*
```

### 5. LINQ in Hot Paths

```csharp
// ANTI-PATTERN: LINQ allocates closures + enumerators
int activeCount = instancedGroups.Count(g => g.instances.Count > 0);
```

```csharp
// BETTER: Manual loop, zero allocation
int activeCount = 0;
foreach (var g in instancedGroups.Values)
    if (g.instances.Count > 0) activeCount++;
```

---

## Audit Cadence

| When | What | Who |
|------|------|-----|
| **Before each GPU-Driven Rendering phase** | Full hot-path audit of `VoxelChunkManager.cs` | Developer + AI assistant |
| **After adding any new per-frame code** | Self-audit using the checklist above | Author of the change |
| **At each city-size milestone** (100, 250, 500 blocks) | Full audit + profiling run | Developer |
| **When frame time exceeds budget** | Emergency audit — find what's eating the budget | Developer + AI assistant |

---

## Profiling Tips

### How to Identify Invariant Computation

1. **Open Unity Profiler** → CPU Usage module → Timeline view
2. **Look for repeated blocks** of the same color/pattern in the timeline
3. **Check for GC.Alloc** markers — these indicate per-frame allocations
4. **Compare frame time with vs without the code** — if commenting it out doesn't change the visual result, it's invariant

### Using the HUD Frame Time Metrics

The HUD (added Aug 9, 2026) shows:
- **Frame (ms)** — total frame time with min/max/avg over 120-frame window
- **CPU thread (ms)** — main thread time via `ProfilerRecorder`
- **GPU (ms)** — GPU frame time via `ProfilerRecorder`
- **Budget: 8.33ms @ 120fps** — your frame budget

**Before fixing**: Note the `avg` frame time.
**After fixing**: Check if `avg` dropped. The difference is the real gain.

Frame time (ms) is the correct metric — not FPS. See `OPTIMIZATION_VISUAL.html` Section 8 for why.

---

## Related Documents

- `GPU_DRIVEN_RENDERING_PLAN.md` — 6-phase plan, Phase 1 (TRS cache) already complete
- `INSTANCING_AND_BUFFERING.md` — Full instancing pipeline documentation
- `OPTIMIZATION_VISUAL.html` — Visual guide to TRS cache + collision world fixes
- `INSTANCING_AND_BUFFERING_VISUAL.html` — Visual guide to GPU instancing concepts
- `VoxelChunkManager.cs` — Main rendering code (hot paths)
- `VoxelCollisionWorld.cs` — Collision grid (cold path, flat array)
- `CityMap3D.cs` — HUD frame time metrics, vSync settings

---

## Changelog

| Date | Change |
|------|--------|
| Aug 9, 2026 | Document created. Captured principle, 2 case studies, 6 pending candidates, audit checklist, anti-patterns |
