## Contents

- Overview
- Core Concepts
- How GPU Instancing Works in This Project
- ComputeBuffer: The GPU-Side Data Container
- MaterialPropertyBlock: Per-Draw Isolation
- The Instanced Character/Vehicle Pipeline (Step by Step)
- The MaterialPropertyBlock Bug (and How to Spot It)
- Sector Baking vs Instanced Characters: Same Pattern, Different Scale
- Shader-Side Instancing: How the Vertex/Fragment Shader Reads Per-Instance Data
- Buffer Lifecycle: Creation, Upload, Release
- Performance Characteristics
- Debugging Instanced Rendering
- Glossary
- Related Documents

---

# Instancing and Buffering — How Voxel Rendering Works on the GPU

**Created**: Aug 8, 2026
**Status**: ✅ COMPLETE — documents the working instancing + buffering pipeline
**Relates to**: `Assets/Scripts/UI/VoxelChunkManager.cs`, `Assets/Resources/Shaders/VoxelProxyRaymarch.shader`, `Assets/Scripts/Sim/VoxelCharacter.cs`, `Assets/Scripts/Sim/VoxelVehicle.cs`, `docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`, `docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`

---

## Overview

This project renders all voxel objects (buildings, characters, vehicles) using a **proxy-cube raymarch** technique. Instead of generating actual triangle meshes from voxel data, a unit cube mesh is drawn at the object's position and scale, and a fragment shader raymarches through the voxel volume inside that cube. This keeps geometry trivial (6 faces) while the actual visual content comes from GPU buffer reads.

This document covers the two GPU concepts that make this work at scale:
1. **GPU Instancing** — drawing many copies of the same mesh in one draw call
2. **ComputeBuffers** — uploading voxel data to the GPU once and reading it per-pixel

These are deeply related: instancing is what makes buffers efficient (one buffer shared across all instances), and buffers are what make instancing possible (each instance reads the same voxel data but at different world positions).

---

## Core Concepts

### What is a Draw Call?

Every time the CPU tells the GPU "draw this mesh with this material," that's a draw call. Draw calls are expensive — not because the GPU can't handle the triangles, but because the CPU spends time preparing state (binding shaders, setting uniforms, uploading matrices). At ~1600 buildings, the CPU becomes the bottleneck before the GPU even breaks a sweat.

**Instancing** solves this: instead of 1600 draw calls for 1600 buildings, you make 1 draw call that says "draw this mesh 1600 times, here's a buffer telling me where each one goes."

### What is a ComputeBuffer?

A `ComputeBuffer` (also called a StructuredBuffer in shaders) is a block of GPU memory that the CPU fills once and the GPU reads many times. Think of it as an array that lives on the GPU:

```
CPU: Create buffer → Upload data → Forget about it
GPU: Read from buffer every frame in vertex/fragment shader
```

In this project:
- **Voxel data** (the actual 3D grid of material IDs) lives in a `ComputeBuffer`
- **Instance offsets** (world position + yaw for each instance) live in a `ComputeBuffer`
- **Material colors** (the RGB lookup table) live in a `ComputeBuffer`

### What is a MaterialPropertyBlock?

A `MaterialPropertyBlock` is a per-draw-call override for material properties. Without it, if you set `_VoxelSize = 0.02` on a material, every draw call using that material sees 0.02. With it, you can say "this specific draw call uses 0.02, that one uses 0.05" — even though both use the same material.

This is critical when multiple instanced groups (e.g., characters + vehicles) share one material but need different buffer bindings.

---

## How GPU Instancing Works in This Project

### The Three Instancing Modes

The `VoxelProxyRaymarch.shader` has two code paths controlled by preprocessor directives:

1. **Non-instanced** (`UNITY_INSTANCING_ENABLED` not defined):
   - One `DrawMesh` call per object
   - Reads `_VolumeOffset`, `_VolumeDims`, `_VoxelSize` from uniform shader globals
   - Used for: individual non-baked buildings (rare path)

2. **Standard instancing** (`UNITY_INSTANCING_ENABLED`, no `BUILDING_INSTANCING`):
   - One `DrawMeshInstanced` call for all instances of the same asset
   - Reads per-instance data from `_InstanceOffsets[unity_InstanceID]` (world pos + yaw)
   - Reads voxel data from the shared `_VoxelData` buffer
   - Reads dims/voxelSize from uniform `_VolumeDims`/`_VoxelSize` (set per-group via MaterialPropertyBlock)
   - Used for: **characters and vehicles**

3. **Building instancing** (`BUILDING_INSTANCING` keyword enabled):
   - One `DrawMeshInstanced` call per sector of buildings
   - Reads per-building data from `_BuildingMeta[unity_InstanceID]` (buffer offset + dims) and `_BuildingPositions[unity_InstanceID]` (world pos + voxelSize)
   - All buildings in a sector share one merged voxel buffer, each building reads its own slice via `bufferOffset`
   - Used for: **sector-baked static buildings**

### What Happens Per Frame (Standard Instancing Path)

```
1. CPU: For each instanced group (e.g., "character_hoodlum_0.stasset"):
   a. Loop through all instances, read transform.position and rotation from their GameObjects
   b. Pack each into Vector4(x, y, z, yawRadians) → instanceOffsetBuffer
   c. Build Matrix4x4[] for proxy cube positions (one per instance)
   d. Bind group's voxel buffer, dims, voxelSize via MaterialPropertyBlock
   e. Call cmd.DrawMeshInstanced(proxyCubeMesh, ..., matrices, count, propBlock)

2. GPU: For each vertex of the proxy cube, for each instance:
   a. Vertex shader: look up _InstanceOffsets[instanceID] → get world offset + yaw
   b. Vertex shader: compute world position, pass to fragment shader
   c. Fragment shader: raymarch through voxel grid at that world offset
   d. Fragment shader: if a voxel is hit, compute lighting and output color
   e. Fragment shader: if no voxel hit, discard (transparent)
```

---

## ComputeBuffer: The GPU-Side Data Container

### Voxel Data Buffer

Each unique `.stasset` file (e.g., `character_hoodlum_0.stasset`, `vehicle_civilian_car_0.stasset`) gets its own `ComputeBuffer`:

```csharp
// VoxelChunkManager.RegisterInstancedCharacter (line ~1043)
group.sharedVoxelBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
group.sharedVoxelBuffer.SetData(gpuData);
```

- **Element type**: `uint` (4 bytes) — packed voxel material ID (lower 9 bits = material, upper bits = shape/rotation)
- **Size**: `dimX * dimY * dimZ` elements (e.g., a 32×32×8 character = 8192 uints = 32KB)
- **Lifetime**: created once at registration, released on scene cleanup
- **Sharing**: all instances of the same asset share this one buffer — that's the whole point of instancing

### Instance Offset Buffer

Per-group, rebuilt every frame:

```csharp
// VoxelChunkManager.RenderInstancedGroup (line ~1116)
group.instanceOffsetBuffer = new ComputeBuffer(Mathf.Max(visibleCount, 128), sizeof(float) * 4);
group.instanceOffsetBuffer.SetData(offsets, 0, 0, visibleCount);
```

- **Element type**: `Vector4` (16 bytes) — `(worldX, worldY, worldZ, yawRadians)`
- **Size**: one element per visible instance, rounded up to 128 to avoid reallocation when count fluctuates
- **Lifetime**: created lazily, reused across frames, released on scene cleanup
- **Rebuild frequency**: every frame (because instances move)

### Material Colors Buffer

Shared across ALL voxel rendering (buildings, characters, vehicles):

```csharp
sharedMaterialBuffer = new ComputeBuffer(MaxMaterials, sizeof(float) * 4);
```

- **Element type**: `float4` (16 bytes) — RGBA color
- **Size**: `MaxMaterials` (currently 256)
- **Lifetime**: created at startup, updated when materials change, released on cleanup

### Chunk Tints Buffer

Per-chunk color multipliers for visual variety. A default (all-white) buffer is used for instanced characters/vehicles:

```csharp
block.SetBuffer(propChunkTints, defaultTintBuffer);
```

---

## MaterialPropertyBlock: Per-Draw Isolation

### The Problem

`proxyMaterial` is a single shared `Material` instance. When you call:

```csharp
proxyMaterial.SetBuffer(propVoxelData, groupA.sharedVoxelBuffer);
proxyMaterial.SetFloat(propVoxelSize, 0.02f);
```

...you're setting state on the material itself. If you then do:

```csharp
proxyMaterial.SetBuffer(propVoxelData, groupB.sharedVoxelBuffer);
proxyMaterial.SetFloat(propVoxelSize, 0.05f);
```

...the first group's settings are **gone**. Both draw calls will use group B's buffer and voxel size.

This is because `CommandBuffer` records draw commands but executes them all at once at `Graphics.ExecuteCommandBuffer`. The material's state at execution time is what the GPU sees — not what it was at record time.

### The Solution

`MaterialPropertyBlock` snapshots per-draw properties:

```csharp
var block = group.cachedPropBlock;
block.Clear();
block.SetBuffer(propVoxelData, group.sharedVoxelBuffer);
block.SetVector(propVolumeDims, new Vector4(group.dimX, group.dimY, group.dimZ, 0));
block.SetFloat(propVoxelSize, group.voxelSize);
// ...
cmd.DrawMeshInstanced(proxyCubeMesh, 0, proxyMaterial, 0, matrices, visibleCount, block);
```

Each draw call gets its own property block, so group A's draw uses group A's buffer and group B's draw uses group B's buffer. The shared `proxyMaterial` is never mutated.

### What Must Go in the PropertyBlock

Anything that varies per group:
- `_VoxelData` — the voxel buffer (different asset = different voxels)
- `_VolumeDims` — grid dimensions (character is 32×32×8, car is 40×20×80, etc.)
- `_VoxelSize` — world units per voxel (character=0.02, vehicle=0.05)
- `_InstanceOffsets` — the per-instance position/yaw buffer
- `_MaxSteps` — raymarch step count (can vary for LOD)
- `_ChunkTints` — tint buffer (default for characters/vehicles)

### What Stays on the Material

Things that are truly global (same for all draws):
- `_ProxyCamToWorld`, `_ProxyInvProj`, `_ProxyCamOrigin` — camera matrices
- `_ScreenSize` — render target dimensions
- `_LightDirection`, `_LightIntensity`, `_AmbientIntensity` — lighting
- `_MaterialColors` — the shared color lookup table
- `_IsOrthographic` — camera mode flag
- `_ShadowEnabled`, `_ShadowMaxSteps` — shadow settings

---

## The Instanced Character/Vehicle Pipeline (Step by Step)

### Registration (once, at spawn time)

```
VoxelCharacter.Start()
  ├── LoadAsset()           → read .stasset file, get ushort[,,] voxelData
  ├── ApplyCenterPosition() → set transform.localPosition from centerPosition
  ├── RegisterInstancedWithManager()
  │     ├── VoxelChunkManager.RegisterInstancedCharacter(gameObject, assetFileName, voxelSize)
  │     │     ├── If first instance of this asset:
  │     │     │     ├── Load .stasset → get voxel dims
  │     │     │     ├── Flatten ushort[,,] → uint[] (Z-major order)
  │     │     │     ├── Create ComputeBuffer(totalVoxels, sizeof(uint))
  │     │     │     ├── Upload voxel data to GPU
  │     │     │     └── Create InstancedGroup { sharedVoxelBuffer, dims, voxelSize }
  │     │     ├── Create InstancedCharacter { gameObject, worldOffset, yaw, assetKey }
  │     │     └── Add to group.instances list
  │     └── Store returned handle for later unregistration
  └── initialized = true
```

### Rendering (every frame)

```
VoxelChunkManager.RenderProxyChunks()
  ├── ... (cull + draw building chunks) ...
  ├── RenderInstancedCharacters(cmd)
  │     └── For each InstancedGroup in instancedGroups:
  │           ├── RenderInstancedGroup(cmd, group)
  │           │     ├── Skip if no instances
  │           │     ├── For each instance:
  │           │     │     ├── Read gameObject.transform.position → worldOffset
  │           │     │     ├── Read gameObject.transform.rotation.y → yaw (radians)
  │           │     │     └── Mark visible
  │           │     ├── Build Vector4[] offsets (pos + yaw per instance)
  │           │     ├── Build Matrix4x4[] (proxy cube TRS per instance)
  │           │     ├── Upload offsets to instanceOffsetBuffer
  │           │     ├── Set per-group properties on MaterialPropertyBlock:
  │           │     │     ├── _VoxelData → group.sharedVoxelBuffer
  │           │     │     ├── _InstanceOffsets → group.instanceOffsetBuffer
  │           │     │     ├── _VolumeDims → (dimX, dimY, dimZ)
  │           │     │     ├── _VoxelSize → group.voxelSize
  │           │     │     └── _MaxSteps, _CheapShading, etc.
  │           │     └── cmd.DrawMeshInstanced(proxyCubeMesh, 0, proxyMaterial, 0, matrices, count, block)
  │           └── (next group — same process with its own buffers + property block)
  └── ... (draw baked sectors) ...
```

### Unregistration (on destroy)

```
VoxelCharacter.OnDestroy()
  └── chunkManager.UnregisterInstancedCharacter(instancedHandle)
        └── group.instances.Remove(ic)
```

Note: the shared voxel buffer is NOT released when a single instance is removed — it's only released when all instances of that asset are gone (at scene cleanup via `ReleaseAllInstancedGroups`).

---

## The MaterialPropertyBlock Bug (and How to Spot It)

### The Bug (Fixed Aug 8, 2026)

**Symptom**: When two instanced groups exist (e.g., Vinny character + civilian car), only the last-registered group renders. The other's GameObject is at the correct position (visible in scene view), but the raymarch shader produces no visible pixels.

**Root cause**: `RenderInstancedGroup` set per-group properties (`_VoxelData`, `_VolumeDims`, `_VoxelSize`) directly on the shared `proxyMaterial` instead of using a `MaterialPropertyBlock`. Since `CommandBuffer` defers execution, both draw calls saw the last group's properties:

```
Frame timeline:
  1. Record: Set proxyMaterial._VoxelData = characterBuffer
  2. Record: cmd.DrawMeshInstanced(character)  ← will use whatever _VoxelData is at execution time
  3. Record: Set proxyMaterial._VoxelData = carBuffer  ← OVERWRITES
  4. Record: cmd.DrawMeshInstanced(car)
  5. Execute: GPU runs both draws with _VoxelData = carBuffer
     → Character raymarch reads car voxels with car voxelSize (0.05) and car dims
     → Character's actual voxels (at 0.02 spacing) are never hit → discard → invisible
```

**Fix**: Use a per-group `MaterialPropertyBlock` passed to `DrawMeshInstanced`:

```csharp
cmd.DrawMeshInstanced(proxyCubeMesh, 0, proxyMaterial, 0, matrices, visibleCount, block);
```

Each draw call now carries its own property snapshot.

### How to Spot This Class of Bug

- **Some instanced objects render, others don't** — especially when adding a new asset type
- **The invisible object's GameObject is at the correct position** (check scene view gizmos)
- **The debug HUD shows the correct instance count** — the instance is registered, just rendering wrong
- **The problem appears only when 2+ groups exist** — single-group rendering works fine
- **The last-registered group always renders correctly** — it's the one whose properties stick

### Same Bug in Sector Baking

The sector baking path had the identical issue and was fixed earlier — see `GPU_DRIVEN_SECTOR_RENDERING.md` line 45. The fix there was also per-sector `MaterialPropertyBlock`s. This is a recurring pattern: **any time multiple draw calls share a material but need different buffer bindings, use MaterialPropertyBlock**.

---

## Sector Baking vs Instanced Characters: Same Pattern, Different Scale

| Aspect | Instanced Characters/Vehicles | Sector-Baked Buildings |
|--------|-------------------------------|------------------------|
| **Groups** | Per unique asset file | Per sector (geographic region) |
| **Buffer sharing** | All instances of same asset share 1 voxel buffer | All buildings in a sector share 1 merged voxel buffer |
| **Per-instance data** | `Vector4(pos.x, pos.y, pos.z, yaw)` | `float4(bufferOffset, dimsX, dimsY, dimsZ)` + `float4(pos.x, pos.y, pos.z, voxelSize)` |
| **Shader path** | Standard instancing (`_InstanceOffsets[unity_InstanceID]`) | Building instancing (`_BuildingMeta[unity_InstanceID]` + `_BuildingPositions[unity_InstanceID]`) |
| **PropertyBlock** | Per group (per asset file) | Per sector |
| **Voxel size** | Per group (character=0.02, vehicle=0.05) | Per building (stored in `_BuildingPositions[i].w`) |
| **Buffer offset** | Always 0 (each group has its own complete buffer) | Per building (each building reads its slice of the merged buffer) |
| **Draw calls** | 1 per asset file | 1 per sector |

The key difference: sector baking merges multiple different assets into one buffer (with offsets), while character/vehicle instancing keeps each asset in its own buffer. Both use `MaterialPropertyBlock` to isolate per-draw state.

---

## Shader-Side Instancing: How the Vertex/Fragment Shader Reads Per-Instance Data

### Vertex Shader (Standard Instancing Path)

```hlsl
#ifdef UNITY_INSTANCING_ENABLED
    // Non-building instancing (characters, vehicles):
    float4 instData = _InstanceOffsets[unity_InstanceID];
    output.volumeOffset = instData.xyz;    // world position of this instance
    output.yaw = instData.w;               // rotation in radians
    output.instMeta = float4(0.0, _VolumeDims.x, _VolumeDims.y, _VolumeDims.z);
    output.voxelSize = _VoxelSize;         // from MaterialPropertyBlock
#else
    // Non-instanced fallback:
    output.volumeOffset = _VolumeOffset;
    output.yaw = 0.0;
    output.voxelSize = _VoxelSize;
#endif
```

`unity_InstanceID` is a built-in Unity variable that indexes into the instance array. The GPU automatically provides this when `DrawMeshInstanced` is used.

### Fragment Shader (Raymarch)

The fragment shader receives `volumeOffset`, `yaw`, `voxelSize`, and `dims` from the vertex shader via `Varyings`. It then:

1. Reconstructs the camera ray (orthographic or perspective)
2. Rotates the ray into volume-local space using the yaw
3. Computes the volume's AABB in world space: `[volumeOffset, volumeOffset + dims * voxelSize]`
4. Raymarches through the voxel grid using DDA (Digital Differential Analyzer)
5. For each voxel step, reads `_VoxelData[bufferOffset + VoxelIndex(voxel, dims)]`
6. If material != 0 (not air), computes lighting and outputs the pixel color
7. If no voxel is hit, `discard` (transparent — existing pixel stays)

### Why VoxelSize Matters So Much

The raymarch converts world positions to voxel grid coordinates by dividing by `voxelSize`:

```hlsl
float3 localStart = (startPos - volOffset) / voxelSize;
int3 voxel = clamp((int3)floor(localStart), int3(0,0,0), dims - int3(1,1,1));
```

If `voxelSize` is wrong (e.g., 0.05 instead of 0.02), the world-space positions map to the wrong grid cells. A character at 0.02m/voxel has voxels every 2cm, but dividing by 0.05 means the shader thinks voxels are every 5cm — it steps through the grid 2.5× too fast and most cells appear empty. This is exactly why the MaterialPropertyBlock bug made Vinny invisible.

---

## Buffer Lifecycle: Creation, Upload, Release

### Creation

| Buffer | When Created | Size Formula |
|--------|-------------|--------------|
| `sharedVoxelBuffer` | First registration of an asset | `dimX * dimY * dimZ * sizeof(uint)` |
| `instanceOffsetBuffer` | First render frame with instances | `max(instanceCount, 128) * sizeof(float) * 4` |
| `sharedMaterialBuffer` | VoxelChunkManager startup | `MaxMaterials * sizeof(float) * 4` |
| `defaultTintBuffer` | VoxelChunkManager startup | `MaxMaterials * sizeof(float) * 4` |

### Upload

| Buffer | Upload Frequency | Method |
|--------|-----------------|--------|
| `sharedVoxelBuffer` | Once (at registration) | `SetData(gpuData)` |
| `instanceOffsetBuffer` | Every frame | `SetData(offsets, 0, 0, visibleCount)` |
| `sharedMaterialBuffer` | When materials change | `SetData(colorData)` |

### Release

| Buffer | When Released | Method |
|--------|-------------|--------|
| `sharedVoxelBuffer` | Scene cleanup (`ReleaseAllInstancedGroups`) | `buffer.Release()` |
| `instanceOffsetBuffer` | Scene cleanup or count exceeds capacity | `buffer.Release()` |
| `sharedMaterialBuffer` | VoxelChunkManager `OnDestroy` | `buffer.Release()` |

**Important**: Buffers are NOT released when individual instances are unregistered. The shared voxel buffer persists as long as any instance of that asset might be re-registered. This is by design — spawning/despawning characters shouldn't thrash GPU memory.

---

## Performance Characteristics

### Draw Call Count

| Scenario | Draw Calls |
|----------|-----------|
| 1 character, no vehicle | 1 (one instanced group, one instance) |
| 1 character + 1 vehicle | 2 (one per group) |
| 100 characters (same asset) + 1 vehicle | 2 (100 instances in 1 draw + 1 draw) |
| 100 characters (5 different assets) + 1 vehicle | 6 (one per asset group) |
| Full city (sector baked) + 50 characters + 10 vehicles | ~15 sectors + a few instanced groups |

### Per-Frame CPU Cost

For instanced characters/vehicles:
- **Instance count loop**: O(n) where n = instance count (reading transforms)
- **Buffer upload**: O(n) — copying Vector4s to the instance offset buffer
- **Matrix build**: O(n) — building TRS matrices for proxy cubes
- **Draw call**: O(1) — one `DrawMeshInstanced` per group

Total CPU cost is linear in instance count but with very small constants — 1000 instances is still sub-millisecond.

### GPU Cost

Per pixel covered by a proxy cube:
- DDA raymarch: O(maxSteps) buffer reads (typically 64-128 steps)
- Shadow ray: O(shadowMaxSteps) additional reads (if shadows enabled)
- Normal blend: 6 additional buffer reads (if not cheap-shading)

The proxy cube is small on screen for characters (a few dozen pixels), so even with full lighting + shadows, the GPU cost is negligible.

### Memory

| Asset Type | Typical Voxel Dims | Buffer Size |
|-----------|-------------------|-------------|
| Character (hoodlum) | 32×32×8 | 8KB |
| Vehicle (civilian car) | 40×20×80 | 64KB |
| Building (small shop) | 32×32×32 | 128KB |
| Sector (16 blocks, ~50 buildings) | merged | ~6MB |

Voxel buffers are surprisingly small — the entire character + vehicle voxel data fits in under 100KB of GPU memory.

---

## Debugging Instanced Rendering

### Check the HUD

The ortho render HUD (toggled via `ShowOrthoHud` on `VoxelChunkManager`) shows:
- `InstancedCharacterCount` — total instances across all groups
- `BakedSectorCount` / `BakedSectorBuildingCount` — sector baking stats

If instance count is correct but the object is invisible, the issue is in the render path (likely a property block / buffer binding problem).

### Check the Scene View

Vinny's `VoxelCharacter` has `showGizmo = true` by default, which draws a wireframe cube at his position. If the gizmo is visible but the character isn't rendering, the proxy cube is being drawn but the raymarch isn't hitting any voxels — pointing to a buffer/dims/voxelSize mismatch.

### Add Debug Logging

```csharp
// In RenderInstancedGroup, after setting up the property block:
Debug.Log($"[Instanced] Group {group.dimX}x{group.dimY}x{group.dimZ} vs={group.voxelSize} instances={visibleCount} buffer={group.sharedVoxelBuffer.count}");
```

This confirms which buffer and dims each group is actually using.

### Isolation Test

Comment out one group's registration to see if the other renders correctly. If it does, the problem is cross-group contamination (the MaterialPropertyBlock bug).

---

## Glossary

| Term | Definition |
|------|-----------|
| **Draw call** | One CPU→GPU command to render a mesh. Expensive due to state setup, not triangle count. |
| **Instancing** | Drawing the same mesh multiple times in one draw call, with per-instance variation via buffers. |
| **ComputeBuffer** | GPU memory buffer that the CPU writes and the GPU reads. Used for voxel data, instance offsets, material colors. |
| **StructuredBuffer** | HLSL name for a read-only ComputeBuffer. Declared in shader as `StructuredBuffer<uint> _VoxelData`. |
| **MaterialPropertyBlock** | Per-draw-call override for material properties. Prevents shared materials from cross-contaminating between draw calls. |
| **Proxy cube** | A unit cube mesh scaled to the voxel volume's AABB. The fragment shader raymarches inside it. |
| **DDA** | Digital Differential Analyzer — the voxel grid traversal algorithm used in the raymarch. Steps through grid cells one at a time. |
| **unity_InstanceID** | Built-in shader variable indexing the current instance in an instanced draw call. |
| **Sector baking** | Merging multiple buildings' voxel data into one buffer per geographic sector, drawn with one instanced call. |
| **InstancedGroup** | C# class grouping all instances of the same asset file (e.g., all characters sharing `character_hoodlum_0.stasset`). |
| **VoxelSize** | World units per voxel. Characters=0.02m, vehicles=0.05m, buildings=0.1m. Critical for raymarch grid traversal. |

---

## Related Documents

- **`docs/systems/GPU_DRIVEN_SECTOR_RENDERING.md`** — Sector baking implementation details, known gaps, proposed GPU-driven indirect rendering evolution. Covers Tier 1 (static buildings) in depth.
- **`docs/systems/DYNAMIC_OBJECT_RENDERING_TIERS.md`** — Philosophy of when to bake, instance, or individually render objects. The tier model this project uses to classify rendering strategy.
- **`docs/systems/3D_CITY_RENDERING.md`** — High-level vision for the 3D city visualization, entity budgets, camera modes.
- **`Assets/docs/VOXEL_LIGHTING_AND_SHADOWS.md`** — How the raymarch shader computes lighting (Half-Lambert, soft shadows, smooth normals).
- **`Assets/docs/VOXEL_BLEED_THROUGH_FIX.md`** — Depth buffer compositing fix for multi-chunk rendering.
