# Single-Keyword `multi_compile` Has No "Off" Variant — Instanced Characters/Vehicles Invisible

**Date**: 2026-10-11
**Status**: 🟡 FIX APPLIED (pending Play test + standalone build test)
**Severity**: 🔴 CRITICAL — all instanced characters and vehicles invisible

## Symptoms
- Instanced characters (Vinny, civilians) and vehicles invisible. Buildings, terrain, chunk volumes and glass render normally.
- Reproduced as a clean A/B on 2026-10-11: user-verified working tree (glass cull fix passed) → only the three `BUILDING_INSTANCING` pragmas changed from `shader_feature_local` to `multi_compile_local` → cars and characters vanish.

## Root Cause
`#pragma multi_compile_local BUILDING_INSTANCING` declares a **one-keyword set**. Per the Unity manual ("Make shader behavior conditional on keywords"), a single-keyword `shader_feature` automatically gets a "keyword disabled" variant; `multi_compile` does **not** — the "all disabled" variant only exists if the set includes `_` (`#pragma multi_compile _ FEATURE_ON` → 2 variants).

So each pass compiled exactly ONE program with `BUILDING_INSTANCING` always defined, regardless of `Material.EnableKeyword`. Inside `#ifdef UNITY_INSTANCING_ENABLED` the vertex/fragment code therefore always took the sector branch (`_BuildingMeta[unity_InstanceID]`, `_BuildingPositions[unity_InstanceID]`). Group draws (`groupMaterial`: cars, characters) never fill those slots (they bind dummy buffers) → collapsed / displaced / invisible proxies.

Unaffected, matching the observation: sector draws (the intended consumer of that branch) and non-instanced chunk draws (`UNITY_INSTANCING_ENABLED` is off, so the keyword never matters).

## Investigation Notes
- The stashed "tangled" shader (`stash@{0}`) carries the identical single-keyword pragma on all three passes (shader lines 1638 / 1659 / 1684). `git log -S` finds no commit that ever contained it — it only lived in the working tree.
- Probable true cause of the earlier invisible-instancing hunt (`SHARED_MATERIAL_INSTANCED_DRAWS.md`, "second layer"): symptoms match (sectors and chunks perfect; groups drawn "in-world but displaced along the street", which is what reading sector `_BuildingPositions` would produce). Unproven retroactively.
- First standalone build (cars invisible, buildings fine, `_BuildingMeta` SRV error on non-sector draws) is consistent with the same mechanism. Whether that build carried the single-keyword pragma is not recorded.
- No `.mat` / scene / prefab asset references the shader GUID — every material is created at runtime from code, so `shader_feature_local`'s build-time usage scan has no material to learn the keyword state from. Build stripping itself is still unconfirmed.
- An earlier assistant claim that swapping `shader_feature_local` → `multi_compile_local` was "identical in the Editor" was wrong for the single-keyword form.

## Related Files
- `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` — three pass pragmas; `#ifdef UNITY_INSTANCING_ENABLED` / `#if defined(BUILDING_INSTANCING)` in `vert` and `fragCore`
- `Assets/Scripts/UI/VoxelChunkManager.cs` — `RegisterSector` (`sectorMaterial.EnableKeyword`), `groupMaterial`
- `docs/known_issues/rendering/SHARED_MATERIAL_INSTANCED_DRAWS.md` — earlier hunt with probably the same cause
- `docs/known_issues/rendering/MID_RENDER_BUFFER_ALLOC_SRV_DROP.md` — standalone-build SRV issue, possibly the same cause

## Resolution
`#pragma multi_compile_local _ BUILDING_INSTANCING` on all three passes: both variants are always compiled and shipped; materials without the keyword (`proxyMaterial`, `groupMaterial`) run the group path, `sectorMaterial` (keyword enabled in code) runs the sector path.

🧪 Pending: Editor Play test (cars/characters visible, buildings/terrain/glass unchanged) and a standalone build test (the pragma exists for builds).

## Lesson
`multi_compile` with a single keyword always needs the `_` placeholder. `shader_feature` adds it implicitly, so swapping one directive for the other is not a drop-in change.

The `VOXEL_DEBUG_VIEW` heatmap keyword (added later the same day) uses the correct `#pragma multi_compile _ VOXEL_DEBUG_VIEW` form.
