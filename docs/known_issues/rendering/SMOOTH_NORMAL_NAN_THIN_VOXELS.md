# SmoothNormal NaN on Thin Voxels - One Face Renders Near-Black

**Date**: 2026-10-12
**Status**: 🟢 FIXED - confirmed by user playtest 2026-10-12 (single dark faces gone)
**Severity**: 🟢 MEDIUM

## Symptoms
- Single faces of surviving voxels render flat dark brown while the other faces of the SAME voxel are lit normally. Most visible after blasts (thin remnants at crater rims, facade pockmarks); also present on authored thin features.
- Voxel data is untouched: Voxel Inspector on `block_26_building` cell (76,52,165) -> `mat 106 Dark Wood`, raw `0x6A`, no flag bits. No damage code writes anything except AIR or cracked glass (134) - verified offline on the production tenement.
- Only SIDE faces are affected (`|normal.y| <= 0.5`); top/bottom faces bypass the smooth-normal blend and stay lit.
- Repro: Range tab -> PICK BLDG on the tenement -> DETONATE (r6 / p2 / 6 passes) -> look at 1-voxel-wide remnants -> PICK VOXEL on a dark face.

## Root Cause (hypothesis, strongly supported)
`SmoothNormal` (`Assets/Resources/Shaders/VoxelProxyRaymarch.shader` ~246; identical code in `MobSimVoxelRaymarch.compute` ~123) builds `n = normalize(-(sum of +neighbour occupancy - -neighbour occupancy))`. When every axis has matching neighbours (both occupied or both empty - a 1-voxel strand or sheet) the sum is (0,0,0) and `normalize(0)` = `0 * rsqrt(0)` = NaN. The intended degenerate fallback `if (length(n) < 0.001) n = (0,1,0)` runs AFTER the normalize, and `NaN < 0.001` is false, so it never fires. NaN propagates through `blendedN` -> `GetLighting` -> `shadowedLighting`; the final `max(shadowedLighting, ambientFloor)` returns the non-NaN operand, so the face is lit at the ambient floor only (flat dark, albedo-tinted).

## Investigation Notes
- Editor.log `VOXEL INSPECT` for the dark voxel: `nb: -x:air +x:air -y:air +y:air -z:106 +z:106`, hit `n(1,0,0)` -> neighbour sum is exactly (0,0,0).
- Offline census mirroring the shader (side-exposed voxels whose neighbour sum == 0):
  - pristine tenement_block_0: 6,400 of 116,186 (5.5%)
  - after r6m / p2 / 6-pass blast at center: 17,018 (+10,618); roof: 14,249 (+7,849); south face: 7,042 (+642)
  - Stochastic removal leaves many 1-wide remnants, which is why blasts make it noticeable.
- NOT verified: actual GPU result of `normalize(0)` / `max(NaN, x)` on the user's driver. Decisive test = apply the fix and re-inspect the same voxel.

## Proposed Fix
Compute `length` before normalizing; if the neighbourhood is degenerate, skip the 30% smooth blend and use the face normal (neutral). Apply to both `VoxelProxyRaymarch.shader` and `MobSimVoxelRaymarch.compute`.

## Related Files
- `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` - `SmoothNormal` (~246), blend site (~1195-1204), `GetLighting` (~782), shadow apply + ambient floor (~1326-1333)
- `Assets/Resources/Shaders/MobSimVoxelRaymarch.compute` - same `SmoothNormal` (~123)
- `Assets/Scripts/Sim/DebugHUDManager.cs` - `InspectVoxel` (PICK VOXEL)

## Resolution
Applied and confirmed 2026-10-12 (user playtest: the dark faces are gone):
- `SmoothNormal` (proxy shader + legacy compute) returns zero when the gradient sum is zero (`g2 < 0.0001`) instead of `normalize(0)`; the side-face blend is skipped when `dot(smoothN, smoothN) <= 0.5`, i.e. the plain face normal is used.
- Verified so far: both functions (real text extracted from the files) compile under `d3dcompiler_47` (cs_5_0). Disassembly of the OLD code shows `dp3 -> rsq` (normalize) followed by `dp3 -> sqrt -> lt 0.001` (the guard measures the already-normalized, possibly-NaN vector); the NEW code selects the constant face normal with `movc` when the gradient is zero, so no NaN reaches the lighting.
- Unity imported and compiled the edited shaders, and the on-screen result was confirmed in the user's playtest.
- Verification recipe (passed in user playtest 2026-10-12): Range tab -> PICK BLDG -> DETONATE -> PICK VOXEL on a 1-voxel-wide strand that used to show a dark face. Expected: the face is lit like its neighbours, and `Logs/voxel_inspect.log` shows `neighbour gradient sum (0, 0, 0) -> DEGENERATE ... blend skipped` plus a `sun:` verdict. If the face is still dark, the `sun:` block says whether it is genuinely SHADOWED / facing away from the sun, or neither (then the cause is something else and the log holds the inputs needed to find it).
- Expected side effect: every degenerate side face everywhere (fire escapes, rails, frames, other authored thin features - 5.5% of the tenement's side-exposed voxels even when pristine) now lights like its neighbours.
