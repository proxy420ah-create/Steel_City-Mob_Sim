# Static Instanced Asset — Instances ≥1 Read Past Buffer End (Invisible Second Vehicle)

**Date**: October 5, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — every instanced asset without `.groups` data silently drew only its first instance

## Symptoms

- Spawning a second `VoxelVehicle` (same `vehicle_civilian_car_0.stasset`): GameObject exists, parked slot verified correct (`Car 1 slot: i_r2_c5→i_r3_c5 @ (-2.0, 0.0, 35.3)`), `Loaded` log fired — but only car 0 ever rendered.
- Same would hit any future instanced asset without a `.groups` file (props, decor).

## Root Cause

`VoxelProxyRaymarch.shader` vertex stage:

```hlsl
uint posedOffset = (_GroupIDsEnabled == 0) ? (unity_InstanceID * totalVoxels) : 0u;
```

`_GroupIDsEnabled == 0` was overloaded to mean "the bound `_VoxelData` is the per-instance **posed** buffer". Assets with no `.groups` file also get `_GroupIDsEnabled = 0` (in `RenderInstancedGroup`: `hasGroups = groupIDBuffer != null && !useComputePose`), but bind the **single shared rest buffer** — instance 0 reads offset 0 (fine), instance 1 reads `totalVoxels` = one past the end → out-of-bounds read → empty → invisible.

Three buffer modes (posed per-instance / inverse-transform groups / static shared) were encoded in one flag that only distinguished two.

## Resolution

New `_SharedRestBuffer` uniform, declared in the shader and set by `RenderInstancedGroup` as `!useComputePose`:

```hlsl
uint posedOffset = (_GroupIDsEnabled == 0 && _SharedRestBuffer == 0)
    ? (unity_InstanceID * totalVoxels) : 0u;
```

Defaults to 0 = legacy behavior, so the chunk/sector/building property blocks that never set it are unaffected.

## Files

- `Assets/Resources/Shaders/VoxelProxyRaymarch.shader` — `_SharedRestBuffer` decl + `posedOffset` fix
- `Assets/Scripts/UI/VoxelChunkManager.cs` — `propSharedRestBuffer` property ID + `block.SetInt` in `RenderInstancedGroup`

## Related

- `docs/systems/INSTANCED_RENDERING_PITFALLS.md` Pitfall #7 (same bug, symptom-first)
- `docs/systems/VEHICLE_VOXEL_ASSETS.md` §2 — full buffer-mode truth table; V6 notes the world-vs-local position latent issue
