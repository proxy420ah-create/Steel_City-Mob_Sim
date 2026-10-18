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

## Gotcha #6: DDA Step Budget Sized in Distance, Not Cell Crossings

**Date**: October 8, 2026
**Severity**: CRITICAL — buildings cull geometry at grazing camera angles
**Files**: `VoxelChunkManager.cs` (LOD step tiers), `VoxelProxyRaymarch.shader` (`_MaxSteps` loop bound)

### Symptom

Flat volumes (shooting_range, 192×120×192 container with content only y=0–36) dropped their floor at shallow camera angles; tall features (tenement water tower) caused holes in *neighboring* pixels when viewed from above. Wall/tower survived, mid-field/empty regions vanished.

### Root Cause

Two coupled problems:

1. The DDA `RayAABB`+march was bounded by the **full container** while content filled only its bottom ~30% — every pixel traversed ~80+ empty container cells before reaching content.
2. `_MaxSteps` was a **fixed LOD constant** (264/48/24/12) blind to volume size. A ray needing >264 crossings exited mid-volume → discard → hole. Tall content made it worse by inflating the marched box for all neighbors.

### The Trap (why a first fix still left ~5%)

The obvious floor "ray can cross at most the box diagonal" is **wrong**: the Euclidean diagonal `√(sx²+sy²+sz²)` ≈ 274 for the range, but the DDA counts **plane crossings** — worst case `sx+sy+sz` = 421. A near-axis-parallel ray at the lowest camera angle crosses ~385 cells; the diagonal budget still starved it.

### Fix

- CPU passes `_TightBoundsMin/Max` (voxel units, Max exclusive); shader marches the tight content AABB — indexing still uses container dims (stride unchanged).
- Shader floors the loop bound itself: `stepCap = max(_MaxSteps, marchSX+marchSY+marchSZ+4)` derived from the *marched* bounds — every draw path (chunk/instanced/sector) self-protects.

### Rule

**Bound a DDA loop in cell crossings (`sx+sy+sz`), never in distance or diagonal.** And prefer deriving it from the marched bounds in-shader over CPU constants — it can't drift when the marched region changes.

---

## Gotcha #7: Corner-vs-Center Placement — Non-WorldPosition Branches Offset by Half-Volume

**Date**: October 8, 2026
**Severity**: HIGH — every placement off by ~0.72m diagonal when `useWorldPosition=false` (the default path for `CharacterRig`/`HoodSpawner`/`StressTestSpawner` characters, incl. spawned Vinny)
**Files**: `EventPlayer.cs` (`PlaceCharacter`), `TickSimulation.cs` (waypoint placement), `VoxelCharacter.cs` (`PlaceAtCenter`)

### Symptom

Vinny arrives at `block_46` and stands ~0.7m off the painted `firing_position` centroid; on the way there he hugs one side of every sidewalk instead of walking the waypoint centers.

### Root Cause

`transform.position`/origin = the volume's **-X/-Z corner**; `PlaceAtCenter` exists to subtract half the volume so the *center* lands on the target. But the `useWorldPosition=false` branches skipped that:

```csharp
// EventPlayer.PlaceCharacter (before):
character.transform.localPosition = worldCenter - mapRoot.position;
// → CORNER lands on the point → body offset +0.5·WorldSize in +X/+Z
```

Every waypoint and every arrival was off by half a body-width diagonally — visible on sidewalks as off-center walking, at the range as missing the stand spot.

### Fix

Both `useWorldPosition=false` branches now subtract `new Vector3(half.x, 0, half.z)` from the target before assigning `localPosition` — XZ only, Y preserved (mirrors `PlaceAtCenter`).

### Related (separate, smaller)

`CityMap3D.TryGetBuildingPointLocal` now applies `+0.5` per axis — painted centroids are voxel-*index* means; cell centers sit half a voxel in (same index→cell bridge as `WeaponMount`).

### Rule

**Any code that positions a voxel volume must agree on corner-vs-center semantics.** `transform.position` is always the corner; "place at P" means `P - halfSizeXZ`. Check every `useWorldPosition` branch pair — they must produce the same world result.

---

## Gotcha #8: Coordinate-Space Mismatch — Region Centroid +0.5 Voxels Off

**Date**: March 2026
**Severity**: HIGH — eye anchor rendered one voxel off; three "fixes" verified correct math in the wrong space before it was found
**Files**: `VoxelAssetStudio/character_animator.html` (`rebuildRegionIndex`), `Assets/Scripts/Sim/CharacterAssets.cs` (`TryGetRegionCentroid`)
**Spec**: `docs/systems/VOXEL_COORDINATE_SPACES.md` — read this before writing any region/pivot-derived position

### Symptom

Eye-anchor orb sat centered on a single voxel when the painted Eyes span (`x44–51`, 8 wide) should have anchored on the seam between the two middle voxels. Successive fixes (mean→bbox midpoint, depth-test, proud offset) each addressed real issues but the marker never visibly moved — the +0.5 offset survived all of them.

### Root Cause

The animator renders voxel `i` centered at `(i - dims/2)` — **index space** ("index = center"). `rebuildRegionIndex` computed the centroid in **cell-center space** (`(min+max+1)/2`), systematically +0.5 voxels off on every axis. The Unity side (`(mn+mx)*0.5f`, index space) was correct all along — the bug was only in the JS convention.

### Why It Took So Long

- Each fix was *real* (occlusion, depth test, mean-vs-bbox), masking the residual offset
- The status-line diagnostic reported `.0 = seam` in the same wrong space — confirming the wrong answer
- The FK/gizmo transform chain was verified line-by-line, but never the *input coordinate space* it consumed

### Fix

`(min + max) / 2` in index space — even-width spans land `.5` (seam), odd-width `.0` (middle voxel). Status-line legend corrected to `.5 = seam · .0 = voxel` with span bounds printed.

### Defense Going Forward

**Half a voxel off is a space mismatch until proven otherwise.** Before tuning centroid math or paint theories: (1) name the space the renderer consumes, (2) name the space the producer emits, (3) check parity — even span must give `.5`. Full convention spec + new-code checklist in `docs/systems/VOXEL_COORDINATE_SPACES.md`.

---

## Gotcha #9: Stripped `handedness` Marker — Gun Backwards at Idle, Fine Under IK

**Date**: March 2026
**Severity**: HIGH — silent 180° weapon flip; masked by the IK solver so it only appeared in un-solved poses
**Files**: `character_animator.html` export, `WeaponMount.cs:234`, `CharacterAssets.cs:187`
**Spec**: `docs/systems/WEAPON_ATTACHMENT_SYSTEM.md` — "Handedness Marker & Mount Compensation"

### Symptom

Model-10 sat **backwards in Vinny's palm** at idle — but aimed correctly the moment IK engaged. Any hand-tuned rotation fix would have flipped the working case.

### Root Cause

The animator's export only wrote fields it owned — `handedness:"unity"` (the promote-script marker) wasn't one of them → silently stripped. `CharacterAssets.UnityHanded` fell to `false` → `WeaponMount`'s +180° unity-hand roll compensation (`(hand==Left) != mirroredRig → e.x += 180`) was skipped → the authored `attachRotation` landed 180° off in the static bind frame.

Under IK the solver re-derives the *entire* hand rotation per frame (bore→target, sights→up), so the bad static roll was invisible while aiming — the bug only had a window in poses nobody solves.

### Fix

Marker restored into `Civilian1.json`; animator now carries `handedness`/`attachRotation`/`bgColor`/`assetType`/item fields verbatim via `carriedFields` (both load paths). See also the round-trip entry above.

### Defense Going Forward

- **"Correct under IK, wrong at rest" = check the marker/compensation path, not the authored rotation.** Solver-corrected systems hide bind-frame errors — always diagnose orientation on the un-solved pose.
- **A metadata field can change world geometry.** `handedness` is one string that costs nothing to keep and 180° to lose — invisible-file, visible-world. Every loader→exporter pair must carry fields it doesn't own.

---

## Gotcha #10: UI Tabs Constructing Sim Components — Behavior Gated Behind Tab Opens

**Date**: March 2026
**Severity**: MEDIUM — features silently absent until the right debug tab was opened; one latent NullRef
**Files**: `DebugHUDManager.cs` (draw methods), `CityMap3D.WaitForCharacterSpawn`, `CombatController.EnterCombat`

### Symptom

The vision FOV wedge and cover scan didn't exist at spawn — they appeared only **after opening the VISION/COVER tabs**, because the tab draw methods lazily `AddComponent`ed the sensors on first draw.

### Root Cause

The HUD tabs owned component *lifecycle* instead of just *inspection*: `if (sensor == null) sensor = AddComponent<Sensor>()`. Until a human opened the tab, the sim component — and its per-frame Update, visuals, and scan state — simply didn't exist.

An audit for the same pattern surfaced a worse variant: `CombatController.EnterCombat` did `solver = GetComponent<ArmAimSolver>()` (Get-**only**), then called `solver.Arm(threatPos)` unguarded — and the only attach paths for `ArmAimSolver` were the weapons tab's lazy draw and `PossessController.TryPossess`. **ENTER COMBAT before either → NullReferenceException on cover arrival.**

A second, subtler companion bug: an early fix attached `VisionSensor` to `HoodSpawner`'s `Hood_Debug` — but every tab and `PossessController` inspect `cityMap.SpawnedCharacter`, a *different* character. The sensor was on; the inspected character still had none.

### Fix

- All four HUD-inspectable sim components (`VisionSensor`, `CoverSensor`, `ArmAimSolver`, `CombatController`) attach at `SpawnedCharacter` assignment — all idle cleanly (`Blend<=0`, `State==Idle`, `Threat==null` early-outs).
- `CombatController` solver lookup upgraded to Get-or-**Add** — self-sufficient on any spawn path (test rigs, future NPCs), matching how it already handled `CoverSensor`.
- Tab lazy `AddComponent`s retained as harmless fallbacks for non-`CityMap3D` spawns.

### Defense Going Forward

- **Tabs inspect, they don't construct.** A `Draw*Tab` method that mutates the scene graph is a smell — if the component's absence changes world behavior, attach it at spawn/assignment and let the tab only `GetComponent`.
- **Get-only + unguarded use = latent ordering crash.** Any dependency a sim component needs must be Get-or-Add'd or fail loudly with a named error — never assume some UI ran first.
- **Attach to the canonical reference.** With multiple characters in scene, verify *which* object the consumers actually inspect (`cityMap.SpawnedCharacter` here) — a correctly-attached component on the wrong character is indistinguishable from missing.

---

## Gotcha #11: `out var` in `&&` Conditions — Definite-Assignment Trap (CS0165)

**Date**: March 2026
**Severity**: LOW — compile-time catch, but recurring and easy to write
**Files**: `DebugHUDManager.cs` (`DrawCombatTab` pit detection)

### Symptom

`error CS0165: Use of unassigned local variable 'pitBLocal'` — from:

```csharp
bool pit = cityMap.TryGetBuildingPointLocal(id, "cover_stand_a", out var pitALocal)
        && cityMap.TryGetBuildingPointLocal(id, "cover_stand_b", out var pitBLocal);
if (pit) { Use(pitBLocal); }   // CS0165
```

### Root Cause

`&&` short-circuits: when the left `TryGet` returns false, the right never runs and `pitBLocal` is never assigned. C# definite-assignment analysis knows `pitBLocal` is assigned *when the && expression is true* — but assigning that result to `pit` and later testing `if (pit)` breaks the correlation: the compiler can't prove `pit == true` implies the right operand executed.

### Fix

Split the calls so out-vars are unconditionally assigned:

```csharp
bool pitA = cityMap.TryGetBuildingPointLocal(id, "cover_stand_a", out var pitALocal);
bool pitB = cityMap.TryGetBuildingPointLocal(id, "cover_stand_b", out var pitBLocal);
bool pit = pitA && pitB;
```

### Defense Going Forward

- **`TryGet out var` inside `&&`/`||` is safe only when the out-var is used inside the same expression's true-path.** The moment the result is stored in a bool and branched on later, split the calls.
- Same trap family as Gotcha #10's ordering bugs: *conditional execution creating invisible dependencies* — here the dependency is on evaluation order the compiler won't track for you.

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
