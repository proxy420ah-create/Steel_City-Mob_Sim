# Shared `proxyMaterial` Between `DrawMesh` and `DrawMeshInstanced` — Displaced/Invisible Instanced Models

**Date**: October 11, 2026
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL — all instanced characters and vehicles rendered displaced far off-position or fully invisible

## Symptoms

- Instanced vehicles and Vinny/characters produced no visible output at their scene-verified positions.
- A rasterization diagnostic (solid-color proxy, march bypassed) painted boxes **displaced as a group** far north along the street axis — in-world-looking positions, but not theirs.
- CPU-side evidence clean: `nanMats=0/N`, valid positions/scales, `DrawMeshInstanced` emitted with `instancing=True`, mesh = clean ±0.5 unit cube.
- Chunks using the *same mesh, same material, same pass* via non-instanced `cmd.DrawMesh` rendered perfectly.
- Sectors (`DrawMeshInstanced` through dedicated `sectorMaterial`) rendered perfectly.

## Root Cause

`proxyMaterial` was shared between hundreds of non-instanced `cmd.DrawMesh` chunk draws **and** the instanced `cmd.DrawMeshInstanced` group/character draws. Interleaving instanced and non-instanced draws on one material lets per-draw instancing constant-buffer state get clobbered between draw calls — the instanced draw reads back matrices belonging to other draws (hence "in-world but wrong" positions: stale slots plausibly held sector/chunk transforms along the street).

Sectors never hit this because `sectorMaterial` is dedicated — instanced draws only. The group path had no equivalent.

## Diagnostic Trail (method notes worth keeping)

1. **Reversed-Z trap**: the first rasterization probe wrote `depth = 0.0` — on D3D12's inverted depth that's the *farthest* value, so every legitimately-rendered city fragment overwrote the probe. "No magenta" meant "buried", not "absent". Diagnostic probes on this pipeline must write `depth = 1.0` (nearest).
2. Probe mode escalation: mode 1 (any-variant rasterize) → boxes displaced = **delivery** problem, not march. Mode 2 (post-AABB green) and mode 3 (volOffset color-dump) bisected delivery-vs-march once depth was fixed.
3. `input.instanceID` (raw `SV_InstanceID`) vs `unity_InstanceID` (`SV_InstanceID + unity_BaseInstanceID`) was audited as a suspect — both proven innocent by the HEAD baseline.

## Resolution

Dedicated `groupMaterial` — `new Material(proxyShader)` + `enableInstancing` — used **only** by group `DrawMeshInstanced` calls; per-frame `CopyPropertiesFromMaterial(proxyMaterial)` inherits the ~25 camera/lighting uniforms. Mirrors the `sectorMaterial` pattern exactly.

## Second-layer Fix — Shader Revert + Glass-Only Graft

The accumulated shader (glass + occupancy-DDA + ray-jitter + iterVis + sitting-pose + CGINCLUDE refactor + probes, all uncommitted) still failed to render instanced models even after the material fix. The accumulated diff was too tangled to isolate, so the shader was reverted to the last-known-good HEAD and features were re-grafted **incrementally**:

- `CGINCLUDE` + multi-pass structure alone → instancing still worked (structure innocent).
- **Glass feature set only** was grafted back: `_PaneIDs`, `_GlassForcedMat0-7`, `_FlaggedGlassAlpha`, `_GlassSpecular`, `_GlassMaxLayers`, `IsGlassVoxel`/`GlassLit`/`AdvanceDDA`, front-to-back glass accumulation + frosted-pane terminal, nearest-surface depth, and the 3-pass split (0 combined / 1 opaque / 2 glass).
- Occupancy brick-skip, ray jitter, iterVis, and the sitting-pose anim changes were **deliberately not restored** (FPS hunt already resolved by sector baking; sitting-pose changes were also the Vinny walk-speed regression).

## Sector Two-Pass Composite (follow-up)

Sectors previously hardcoded pass 0 ("far LOD keeps legacy combined path") — unintentional inconsistency. Now:

- `BakedSector.paneBuffer`: per-building `ComputePaneIDs` flood-fill scattered into a merged array in `mergedVoxelData` index space; per-building ids offset `b<<16` so adjacent buildings can't collide under `lastPane` dedup.
- `_PaneMerged` shader flag: sectors index `_PaneIDs[bufferOffset + VoxelIndex]` (same space as `_VoxelData`); chunks/groups keep asset-local indexing.
- Sectors emit pass `useGlassCompositePass ? 1 : 0`, then pass-2 `DrawMeshInstanced` far→near for sectors whose pane build found glass.
- `useGlassCompositePass` default flipped OFF → **ON**.

## Files

- `Assets/Scripts/UI/VoxelChunkManager.cs` — `groupMaterial` (create/sync/destroy/route), `useGlassCompositePass = true`, sector pane build/bind/release/pass-2 emission, `propPaneMerged`
- `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` — HEAD baseline + glass graft + `_PaneMerged`

## Related

- `docs/known_issues/rendering/MID_RENDER_BUFFER_ALLOC_SRV_DROP.md` — sibling D3D12 instancing/SRV issue from the same hunt
- `docs/known_issues/rendering/STATIC_INSTANCED_OOB.md` — earlier instanced-buffer indexing bug
