# Instanced Rendering Pitfalls — Custom Raymarch Pipeline

**Status**: 📋 REFERENCE — Non-obvious behaviors of the custom instanced rendering system
**Created**: August 9, 2026
**Companion docs**: `DYNAMIC_OBJECT_RENDERING_TIERS.md`, `VOXEL_ENGINE_GOTCHAS.md`

---

## Core Concept

Steel City does NOT use Unity's standard `Renderer` + `MeshRenderer` pipeline for characters, vehicles, or buildings. Instead, all voxel objects are rendered through a **custom raymarching pipeline** in `VoxelChunkManager` that bypasses Unity's culling and visibility system entirely.

This means many Unity behaviors you'd expect "just work" — like toggling a GameObject in the Inspector — **don't** unless we explicitly handle them.

---

## How Standard Unity Rendering Works

```
GameObject (activeInHierarchy)
  └─ MeshRenderer (enabled)
      └─ Unity culls, sorts, draws automatically
      └─ Toggling GameObject.SetActive(false) → renderer disappears
```

Unity's renderer pipeline checks `activeInHierarchy` and `Renderer.enabled` before drawing. The checkbox in the Inspector works because Unity's render loop respects it.

---

## How Steel City's Instanced Rendering Works

```
VoxelCharacter (GameObject)
  └─ RegisterInstancedCharacter() → adds to InstancedGroup.instances list
      └─ VoxelChunkManager.RenderInstancedGroup() every frame:
          └─ Reads position/rotation from GameObject.transform
          └─ Packs into ComputeBuffer (offsets, animation state)
          └─ CommandBuffer.DrawMeshInstanced() — single draw call for ALL instances
          └─ Raymarch shader reads voxel data + per-instance offsets
```

The `GameObject` is just a **data source** — its `transform.position` and `transform.rotation` are read every frame to populate the instance buffer. The actual rendering happens through a `CommandBuffer` that runs outside Unity's normal render loop.

**Key difference**: The `CommandBuffer.DrawMeshInstanced` call doesn't know or care about `activeInHierarchy`. It draws whatever matrices are in the buffer.

---

## Known Pitfalls

### 1. Toggling GameObject in Inspector Doesn't Hide It

**Symptom**: You uncheck a character's GameObject in the Hierarchy, but the NPC model stays visible on screen.

**Cause**: `RenderInstancedGroup()` only checked `ic.gameObject == null` (destroyed). An inactive GameObject is not null — `transform.position` still returns valid data, so the instance kept getting fed into the draw call.

**Fix** (applied Aug 9, 2026): Added `!ic.gameObject.activeInHierarchy` check to the visibility loop in `RenderInstancedGroup()`:

```csharp
if (ic.gameObject == null || !ic.gameObject.activeInHierarchy) { ic.visible = false; continue; }
```

**File**: `VoxelChunkManager.cs` line ~1208

---

### 2. No Frustum Culling on Instanced Characters

**Symptom**: Characters behind the camera or far away still get processed in the instance buffer.

**Cause**: `RenderInstancedGroup()` adds every active instance to the buffer regardless of camera position. The raymarch shader will skip voxels outside the proxy cube, but the CPU still writes their data and the GPU still processes the instance.

**Impact**: Minor at current scale (~2-5 instances). Would matter at 3000+ NPC scale — would need distance-based culling or camera frustum check before adding to buffer.

**Status**: Acceptable for now. Future optimization if needed.

---

### 3. Unregistering Doesn't Remove Immediately

**Symptom**: You call `UnregisterInstancedCharacter()` but the model flashes for one more frame.

**Cause**: The instance is removed from the `List<InstancedCharacter>`, but if the `CommandBuffer` was already recorded for this frame, the GPU may still draw it. Next frame it's gone.

**Impact**: Negligible — one frame flash at most.

---

### 4. Destroying the GameObject Without Unregistering

**Symptom**: Null reference errors in `RenderInstancedGroup()`.

**Cause**: If `Destroy(gameObject)` is called without first calling `UnregisterInstancedCharacter()`, the `ic.gameObject` reference becomes null. The null check handles this (`ic.visible = false; continue;`), but the stale entry stays in the list forever.

**Fix**: Always pair `UnregisterInstancedCharacter()` with `Destroy()`. The null check is a safety net, not the intended cleanup path.

---

### 5. Baked Sectors Have the Same Issue

**Symptom**: Toggling a sector's parent GameObject doesn't hide its buildings.

**Cause**: Baked sectors (`BakedSector` class) are rendered through `CommandBuffer.DrawMeshInstanced` in `RenderBakedSectors()`. The sector's `active` flag controls visibility, not the GameObject hierarchy.

**Fix**: Use `sector.active = false` or `UnregisterSector()` to hide baked sectors. The GameObject hierarchy is for organization/click detection only — it has no effect on rendering.

### 6. A Sector Over Its Limits Renders Nothing — And Logs Nothing

**Symptom**: Part of the city (e.g. half of the roads/sidewalks at 32×32) is simply missing. Hover/click logic still works because it runs off game data, not the render path. `Editor.log` shows a clean build.

**Cause**: Every baked sector (terrain *or* building) has hard per-sector limits that fail silently:
- ≤ **511** instances per `DrawMeshInstanced` (excess not drawn)
- ≤ **2^24 − 1** voxels per sector — buffer offsets travel through a `float4` (`_BuildingMeta.x`), so they are integer-exact only below 2^24
- ≤ **2^27** elements per structured-buffer SRV (D3D11)

The 10×10 city was under all of these, so the bug only appeared at scale.

**Fix**: Keep every sector under all three. Terrain is banded in `CityMap3D.BuildVoxelTerrain` (`MaxTerrainChunksPerSector`, `MaxTerrainVoxelsPerSector`); building sectors are ≤144 instances at `sectorSizeBlocks = 4`. After any layout/size change, check the `[VoxelChunkManager] Registered baked sector '...': <n> buildings, <voxels> voxels` log lines against the limits.

**See**: `CITY_SCALE_ARCHITECTURE.md` (invariants table + backlog item O1, a loud guard) and `../known_issues/rendering/TERRAIN_SECTOR_OVERFLOW_AT_SCALE.md`.

---

## Architecture Diagram

```
Standard Unity Pipeline (NOT used for voxels):
  GameObject → MeshRenderer → Unity culling → Draw

Steel City Voxel Pipeline:
  VoxelCharacter GameObject
    ↓ (on Start)
  RegisterInstancedCharacter(chunkManager, assetFile, voxelSize)
    ↓ (stores in InstancedGroup.instances)
  Every frame: RenderInstancedGroup(cmd, group)
    ↓
  For each active instance:
    ↓ Read transform.position, transform.rotation
    ↓ Read animState, animTime, animSpeed
    ↓ Pack into ComputeBuffer
    ↓
  CommandBuffer.DrawMeshInstanced(proxyCubeMesh, ...)
    ↓
  GPU raymarch shader:
    ↓ For each pixel in proxy cube:
    ↓   March through voxel buffer
    ↓   Apply per-instance offset + rotation
    ↓   Apply animation group offset
    ↓   Output color
```

---

## Key Files

| File | Role |
|---|---|
| `VoxelChunkManager.cs` | Owns `InstancedGroup`, `RenderInstancedGroup()`, `RenderBakedSectors()` |
| `VoxelCharacter.cs` | Calls `RegisterInstancedCharacter()` on Start, stores the returned `InstancedCharacter` handle |
| `VoxelVehicle.cs` | Same registration pattern as VoxelCharacter |
| `VoxelRenderBridge.cs` | Hooks `RenderPipelineManager.endCameraRendering` to execute the CommandBuffer |
| `SectorBaker.cs` | Bakes static buildings into sector buffers, calls `RegisterSector()` |

---

## Debugging Tips

- **Character won't disappear?** Check `activeInHierarchy` (fixed) or call `UnregisterInstancedCharacter()`
- **Character stuck at origin?** Check that `transform.position` is being set before registration
- **All characters disappear?** Check `instancedGroups` dictionary — if the group was released, all instances are gone
- **Character visible but not animating?** Check `animState` and `animTime` are being updated (via `CharacterAnimation` component)

---

## Revision History

| Date | Change |
|---|---|
| Aug 9, 2026 | Created — documented activeInHierarchy pitfall + general instanced rendering architecture |
| Oct 5, 2026 | Added pitfall #6 — silent per-sector limit overflow (found at 32×32 terrain) |
