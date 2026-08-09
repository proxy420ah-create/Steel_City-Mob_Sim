# Voxel Engine Gotchas — Steel City: Mob Sim

**Created**: August 9, 2026
**Status**: 🔒 ACTIVE — Append-only, grow as bugs are found

---

## Purpose

Catalog of non-obvious bugs, traps, and footguns discovered in the voxel rendering/collision/terrain system. Each entry documents the root cause, symptom, and fix so we don't re-introduce the same class of bug.

**Rule**: When you fix a voxel-engine bug that took more than 5 minutes to diagnose, add an entry here.

---

## Gotcha #1: ProbeGround Flat-Array Index Aliasing

**Date**: August 9, 2026
**Severity**: HIGH — caused NPCs to spawn at Y=66 instead of Y=0.1
**Files**: `VoxelCollisionWorld.cs:157`, `VoxelTerrainBuilder.cs:77`

### Symptom

`HoodSpawner` probed ground from Y+50 downward. `ProbeGround` returned a hit at Y≈66 (bogus) instead of the real terrain surface at Y≈0.1. NPC spawned floating in the sky.

### Root Cause

Terrain is only **2 voxels thick** (`int h = 2` in `VoxelTerrainBuilder.GeneratePerBlockTerrain`). The voxel grid is stored in a **flat `byte[]` array** indexed by:

```
idx = vx + vy * gridW + vz * gridW * gridH
```

`ProbeGround` converted the probe Y (50 world units above ground) to grid coordinates: `local.y = 50 / 0.05 = 1000`. But `vy=1000` is far outside the valid range `[0, gridH=2)`. The code only checked `vy >= 0` in the while-loop condition — it never validated `vy < gridH` before computing the array index.

With `vy=1000`, the index math `vx + 1000 * gridW + vz * gridW * gridH` **aliases into a completely different (x, y, z) cell** elsewhere in the flat array. Since the ground-fill layer (`y=0`) is solid almost everywhere, this alias immediately "hit" a false-positive solid voxel at a bogus height.

Additionally, `vx` and `vz` were never bounds-checked — probing outside the grid's XZ extent would also alias into unrelated data.

### Fix

Three guards added to `ProbeGround`:

1. **`vx`/`vz` bounds check**: Return `false` immediately if outside `[0, gridW)` or `[0, gridD)`.
2. **`vy` clamp**: If `vy >= gridH`, clamp to `gridH - 1` before scanning. This is the critical fix — probing "from above" is a normal pattern (HoodSpawner, GameUIController) and the grid is only 2 voxels tall.
3. The existing `idx` bounds check (`idx >= 0 && idx < voxelGrid.Length`) was retained as a final safety net but should never trigger now.

### Defense Going Forward

- **Any flat-array voxel lookup MUST bounds-check all three axes** before computing the index. The `idx` range check catches some cases but does NOT prevent aliasing — a valid index at the wrong (x,y,z) is worse than an out-of-range index because it returns a false positive silently.
- **Probe-from-above is a standard pattern**: Characters, spawners, and camera focus logic all probe downward from arbitrary heights. The grid height (2 voxels = 0.1 world units) is much smaller than any reasonable probe start height. Always clamp `vy` to valid range.

---

## Gotcha #2: DebrisScatter Log Spam (900+ lines per build)

**Date**: August 9, 2026
**Severity**: LOW (noise) — but masked the real bug in console output
**Files**: `ProceduralDebrisScatterer.cs:66, 191`

### Symptom

Console flooded with ~900 `[DebrisScatter]` log lines during city build, making it impossible to see HoodSpawner logs.

### Root Cause

`ProceduralDebrisScatterer.Scatter()` had two unconditional `Debug.Log` calls that fired for **every** sub-building in every block. With 100 blocks × up to 9 sub-buildings = ~900 calls.

### Fix

Removed both `Debug.Log` lines. Debris scatter is a per-voxel batch operation — it should be silent unless something goes wrong.

### Defense Going Forward

- **Per-item logging in batch loops is forbidden.** If logging is needed for debugging, gate it behind a static bool flag or use `[Conditional("DEBRIS_DEBUG")]` so it compiles out in release.
- Before adding a `Debug.Log` inside a loop that processes 100+ items, calculate the expected log count. If it's >10, don't add it unconditionally.

---

## Gotcha #3: Empty Plot Detection — Partial vs Fully Vacant

**Date**: August 9, 2026
**Severity**: MEDIUM — NPC spawned in a partially-built block instead of a clean empty plot
**Files**: `HoodSpawner.cs:162-176`, `CityMap3D.cs` (`IsEmptyLand`)

### Symptom

HoodSpawner found a block with one `empty_land` stasset but other building slots had real buildings. NPC spawned in a partially-developed block, not a clean debug plot.

### Root Cause

Original detection logic matched `CityMap3D.IsEmptyLand()` per-building: if **any one** building in a block had an `empty_land` stasset, the whole block was flagged as "empty". But a block can have multiple building slots — some empty, some with real buildings.

### Fix

Changed `FindEmptyPlotBlock()` to require **all** building slots in the block to be `empty_land` for it to qualify as a debug spawn target.

### Defense Going Forward

- **Block-level properties derived from per-building data must specify aggregation logic explicitly.** "Any empty" vs "all empty" vs "majority empty" are different predicates — pick the right one for the use case.
- `IsEmptyLand()` in `CityMap3D` is a per-building check. Block-level vacancy is a different question that requires iterating all buildings.

---

## Gotcha #4: Shader Animation "Approach A" — Output-Only Offset Is Invisible

**Date**: August 9, 2026
**Severity**: HIGH — character animations were completely invisible regardless of state changes
**Files**: `VoxelProxyRaymarch.shader:630-646` (old), `CharacterAnimation.cs`, `VoxelChunkManager.cs:1054-1065`

### Symptom

User cycled through all 9 animation states (Idle, Walking, Looking, Checking, Aiming, Crouching, Flinching, Falling, Down). The character rendered correctly but **none of the states produced any visible movement** — head, arms, and legs stayed in rest position.

### Root Cause

The original `GroupTransformOffset` function in the fragment shader used **"Approach A"** — it computed the group transform offset and applied it to `worldHit` **after** the DDA raymarch found a hit:

```hlsl
// OLD (Approach A — BROKEN):
worldHit = ro + rd * currentT;
worldHit += mul(volInvRot, offset);  // offset applied to OUTPUT only
hit = true;
break;
```

This changed the depth value written to the depth buffer but **did not change the screen position or color** of the rendered pixel. The voxel was still sampled at its rest position during the DDA march — the offset was purely cosmetic on the depth output. Since the proxy cube mesh is axis-aligned and the raymarch determines color/visibility from the DDA hit, the character looked identical in every animation state.

### Fix

Switched to **"Approach B"** — inverse-transform sampling in the DDA loop. Instead of offsetting the output, we inverse-transform each DDA voxel position to find where that voxel would be in **rest space**, then sample the voxel data at that rest position:

```hlsl
// NEW (Approach B — WORKING):
int3 sampleVoxel = voxel;
if (_GroupIDsEnabled != 0) {
    uint gid = _GroupIDs[bufferOffset + VoxelIndex(voxel, dims)];
    if (gid > 0u) {
        float3 voxelLocalPos = (float3(voxel) + 0.5) * voxelSize;
        float3 restOffset = InverseGroupTransformOffset(gid, voxelLocalPos, ...);
        int3 restVoxel = (int3)floor((voxelLocalPos + restOffset) / voxelSize);
        if (InBounds(restVoxel, dims)) sampleVoxel = restVoxel;
    }
}
uint packed = _VoxelData[bufferOffset + VoxelIndex(sampleVoxel, dims)];
```

This makes the ray "see" voxels at their posed positions. When a head rotates, the DDA ray hits the head voxel at a different screen position, producing visible movement.

Also refactored the shader to use a shared `ComputeGroupRotation()` function (returns `bool` — false when state has no transform for that group) to eliminate duplicate code between forward and inverse transforms. Added Aiming, Crouching, Flinching, and Falling states that were missing from the original implementation.

### Defense Going Forward

- **Raymarch shader animation MUST use inverse-transform sampling, not output-only offsets.** The DDA loop determines what the user sees — if voxel data is sampled at rest positions, the character is always in rest pose regardless of what happens to the output position.
- **Forward transform** (`GroupTransformOffset`): restPos → posedPos. Used for reference/debugging.
- **Inverse transform** (`InverseGroupTransformOffset`): posedPos → restPos. Used in DDA to sample voxel data. For rotation matrices, inverse = transpose.
- **The two are NOT interchangeable.** Applying forward transform to the DDA sample position would sample at the wrong location (double-transformed).
- **Test all animation states**, not just Walking. The original code only handled Walking and Looking — Aiming, Crouching, Flinching, and Falling were missing entirely.

---

## Gotcha #5: Missing CharacterAnimation Component on Spawned Hood

**Date**: August 9, 2026
**Severity**: HIGH — without CharacterAnimation, animState/animTime/animSpeed stay at 0 forever
**Files**: `HoodSpawner.cs:139-148`, `CharacterAnimation.cs`, `VoxelChunkManager.cs:1054-1064`

### Symptom

Even after fixing the shader (Gotcha #4), animations still didn't work because the `InstancedCharacter` handle's `animState` field was always 0 (Idle). The `CharacterAnimation` component that pushes animation data to the GPU instance buffer was never added to the spawned character.

### Root Cause

`HoodSpawner.SpawnDebugHood()` created a `VoxelCharacter` but never added a `CharacterAnimation` component. The animation pipeline is:

1. `CharacterAnimation.Update()` reads `currentState` and pushes it to `instancedHandle.animState`/`animTime`/`animSpeed`
2. `VoxelChunkManager.RenderInstancedGroup()` packs those values into the `_InstanceOffsets` StructuredBuffer
3. The vertex shader reads them from the buffer and passes to the fragment shader as TEXCOORD5/6/7
4. The fragment shader's DDA uses them for inverse-transform sampling

Without step 1, the entire pipeline is fed zeros. The character renders (voxel data is independent of animation) but never animates.

### Fix

Added `CharacterAnimation` and `PedestrianLookAround` components to the spawned hood in `HoodSpawner.SpawnDebugHood()`. Also added debug key cycling (1-9 keys) to manually set animation states for testing.

### Defense Going Forward

- **`VoxelCharacter` renders voxels. `CharacterAnimation` drives animation. They are separate components and both must be present for animated characters.**
- `PedestrianLookAround` will auto-add a `CharacterAnimation` if missing (line 36), but `HoodSpawner` doesn't use `PedestrianLookAround` for state control — it adds `CharacterAnimation` explicitly.
- When spawning any animated character, always add `CharacterAnimation` explicitly. Don't rely on another component's `Start()` to add it — component execution order is not guaranteed.
- The `.groups` file (STAG format) must exist alongside the `.stasset` file for `_GroupIDsEnabled` to be set. Without it, the shader skips all animation transforms. Verified: `character_hoodlum_0.groups` has 1820 non-zero groupIDs across 5 groups (head, left/right arm, left/right leg).

---

## Appendix: Key Voxel Engine Constants

| Constant | Value | Location |
|----------|-------|----------|
| Terrain thickness | 2 voxels | `VoxelTerrainBuilder.cs:77` (`int h = 2`) |
| Terrain voxel size | 0.1 world units | `CityMap3D.cs` (`voxelSize`) |
| Character voxel size | 0.015 world units | `VoxelCharacter.cs:28` (default) |
| Terrain world height | 0.2 units (2 × 0.1) | Derived |
| Grid storage | Flat `byte[]` | `VoxelCollisionWorld.cs:19` |
| Index formula | `x + y*W + z*W*H` | `VoxelCollisionWorld.cs:40,176` |
| Animation groups | 5 (head, L arm, R arm, L leg, R leg) | `.groups` file (STAG format) |
| GroupID 0 | Torso (no transform) | Shader `ComputeGroupRotation` |
| Animation states | 9 (Idle=0 … Down=8) | `CharacterAnimation.AnimState` enum |
| Instance buffer layout | N × float4 pos+yaw, N × float4 anim | `VoxelChunkManager.cs:1217-1234` |

### Critical Implication

The terrain is **0.2 world units thick** but characters probe from **2+ units above**. Any probe-from-above logic MUST clamp the starting Y to the grid height, or the flat-array index will alias into wrong data. This is not a hypothetical edge case — it is the normal operating mode for every ground probe in the game.
