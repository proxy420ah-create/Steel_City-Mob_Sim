# City State Preservation & Incremental Updates

**Created**: August 7, 2026
**Status**: 📐 Design Document — Foundation for Week-to-Week Architecture

---

## Overview

The city should be "baked" once at game start and preserved across week transitions. Only buildings affected by weekly activity get updated, rather than rebuilding the entire city every week. This mirrors how Gangsters: Organized Crime handled city state — the city is static during the working week, with changes batched at week end.

---

## State Categories

| State Type | When It Changes | How | Example |
|---|---|---|---|
| **Financial / Ownership** | Week end batch only | Queued events, processed at transition | Extortion income, block ownership, business revenue |
| **Physical / Visual** | Immediately during week | Swap chunk voxel data on the fly | Bomb → rubble model, fire → burned model |
| **Static (everything else)** | Frozen — no updates | No action needed | Building positions, road layout, terrain |

### Key Distinction

- **Visual changes from dramatic events** (fire, bombing) happen **immediately** mid-week — the player sees the building change
- **Economic consequences** of those changes are **deferred** to the week-end batch calculation — the building earns $0 next week, repair costs calculated, owner may abandon

---

## Architecture

### Current Flow (Rebuild Every Time)

```
Play button → BuildMap() → 92 seconds for 500 blocks
  ├── Read 500+ .stasset files from disk (redundant — only ~45 unique files)
  ├── Pack ushort[,,] → uint[] for each (17.6M+ voxels)
  ├── Create 1,000+ ComputeBuffers (GPU sync points)
  └── Generate WaypointGraph (4,000 nodes, 13,730 links)

Week transition → ClearAllChunks() → BuildMap() → 92 seconds again
```

### Target Flow (Bake Once, Incremental Updates)

```
Game start (one-time bake):
  ├── Load city → create all ComputeBuffers → position all chunks
  ├── Snapshot state: ownership, condition, businesses
  └── ~20-30s with .stasset file caching (down from 92s)

During Working Week:
  ├── City is FROZEN — no structural rebuilds
  ├── Dynamic objects move (hoods, vehicles)
  ├── Dramatic events swap individual building chunks immediately:
  │     ComputeBuffer.SetData() on single chunk (~32K uints = milliseconds)
  └── Events queue up: "building X extorted", "building Y bombed"

Week End (batch update):
  ├── Process queued events
  ├── Only update chunks that changed:
  │     - Bombed building → swap .stasset to rubble variant (SetData on 1 buffer)
  │     - New owner → update tint color on that chunk
  │     - Business opened/closed → update material buffer entry
  ├── Recalculate economy (income, expenses, ownership changes)
  ├── Re-snapshot state for next week
  └── NO full city rebuild — ComputeBuffers persist on GPU
```

---

## Technical Foundation

### 1. Packed Voxel File Cache (In Progress)

Static cache keyed by file path. First load reads disk + packs `ushort[,,]` → `uint[]`. Subsequent loads return cloned copy. With ~45 unique building files loaded 500+ times, this cuts load time by 60-80%.

```csharp
// VoxelChunkManager.cs
private static readonly Dictionary<string, (uint[] data, int w, int h, int d)> packedVoxelCache;

private static (uint[] data, int w, int h, int d) GetPackedVoxels(string filepath)
{
    if (packedVoxelCache.TryGetValue(filepath, out var cached))
        return ((uint[])cached.data.Clone(), cached.w, cached.h, cached.d); // cache hit

    // Cache miss: read disk + pack
    var voxels = StAssetReader.LoadVoxels(filepath);
    // ... pack into uint[] ...
    packedVoxelCache[filepath] = (packedData, w, h, d);
    return ((uint[])packedData.Clone(), w, h, d);
}
```

**Impact**: 92s → ~20-30s load time for 500 blocks.

### 2. ComputeBuffer Persistence Across Weeks

ComputeBuffers live in GPU memory and persist between frames. The key change: **stop calling `ClearAllChunks()` and `BuildMap()` on week transitions**. Instead:

- Keep all chunk ComputeBuffers alive
- Only call `SetData()` on buffers for changed buildings
- WaypointGraph persists (only regenerate if roads/blocks structurally change)

### 3. Building Swap API (Future)

```csharp
// Swap a single building's voxel data without rebuilding the city
public void SwapBuildingChunk(string chunkName, string newStassetPath)
{
    if (!chunkLookup.TryGetValue(chunkName, out var chunk)) return;

    var (newData, w, h, d) = GetPackedVoxels(newStassetPath);
    if (newData == null) return;

    // Resize buffer if dimensions changed (rare — most swaps keep same footprint)
    if (w * h * d != chunk.voxelBuffer.count)
    {
        chunk.voxelBuffer.Release();
        chunk.voxelBuffer = new ComputeBuffer(w * h * d, sizeof(uint));
        chunk.dims = new VoxelInt3(w, h, d);
    }

    chunk.voxelBuffer.SetData(newData); // milliseconds, not seconds
}
```

### 4. Tint Buffer Swap for Ownership Changes (Future)

```csharp
// Update ownership color without touching voxel geometry
public void UpdateChunkTint(string chunkName, Color[] tints)
{
    if (!chunkLookup.TryGetValue(chunkName, out var chunk)) return;

    // Create new tint buffer with ownership colors
    var newTint = new ComputeBuffer(MaxMaterials, sizeof(float) * 4);
    newTint.SetData(tints);

    if (chunk.tintBuffer != null && chunk.tintBuffer != defaultTintBuffer)
        chunk.tintBuffer.Release();
    chunk.tintBuffer = newTint;
}
```

---

## Event Queue Design (Future)

```csharp
// Queued during working week, processed at week end
public struct CityChangeEvent
{
    public string chunkName;      // e.g. "block_42_building_0"
    public ChangeType type;       // Bombed, Burned, OwnershipChange, BusinessClosed
    public string newStassetPath; // for visual swaps (e.g. rubble variant)
    public string newOwnerGang;   // for ownership changes
    // ... economic fields processed by GameEngine
}
```

---

## Performance Projections

| Operation | Current (rebuild) | Target (incremental) |
|---|---|---|
| Game start load (500 blocks) | 92.7s | ~20-30s (with file cache) |
| Week transition (no changes) | 92.7s | **0ms** (buffers persist) |
| Week transition (5 buildings changed) | 92.7s | **~5ms** (5 × SetData) |
| Single building swap (bomb) | N/A | **~1ms** (1 × SetData) |
| Ownership tint update | N/A | **~0.1ms** (1 × tint buffer) |

---

## Implementation Phases

### Phase 1: Packed Voxel File Cache (Current)
- Static `Dictionary<string, (uint[], w, h, d)>` cache in `VoxelChunkManager`
- `GetPackedVoxels()` method replaces direct `StAssetReader.LoadVoxels()` calls
- `ClearPackedVoxelCache()` for explicit cleanup on scene unload
- **Status**: In progress

### Phase 2: Week Transition Preservation
- Stop calling `ClearAllChunks()` / `BuildMap()` on week transitions
- Keep ComputeBuffers and WaypointGraph alive across weeks
- Add `SwapBuildingChunk()` API for individual building changes
- **Status**: Future — requires understanding current week transition flow in `GameUIController`

### Phase 3: Event Queue & Batch Processing
- `ConcurrentQueue<CityChangeEvent>` populated during working week
- Processed at week end: swap chunks, update tints, feed economic changes to `GameEngine`
- Visual swaps (bomb/fire) happen immediately via direct `SwapBuildingChunk()` call
- Economic effects deferred to batch
- **Status**: Future — requires `GameEngine` economy integration

### Phase 4: Ruined/Burned Building Variants
- Create `.stasset` files for damaged building states
- Map each building type to its ruined/burned variant
- `SwapBuildingChunk()` uses this mapping when processing damage events
- **Status**: Future — requires voxel art assets for damaged states

---

## Relationship to Existing Systems

| System | Impact | Notes |
|---|---|---|
| `VoxelChunkManager` | Core change — file cache + buffer persistence | Phase 1 & 2 |
| `CityMap3D.BuildMap()` | Called once per session, not per week | Phase 2 |
| `GameUIController` | Week transition flow modified | Phase 2 |
| `GameEngine` | Economy batch processing at week end | Phase 3 |
| `SimulationManager` | Event queue integration | Phase 3 |
| `WaypointGraph` | Persists across weeks unless roads change | Phase 2 |

---

## Gangsters: Organized Crime Reference

In the original game:
- City state is preserved during the working week
- Visual changes (fire, bombing) appear immediately
- Economic calculations happen at week end
- Ownership changes resolve at week transition
- The city "bakes" at week start with all changes applied

This document aims to replicate that behavior with modern GPU buffer management.
