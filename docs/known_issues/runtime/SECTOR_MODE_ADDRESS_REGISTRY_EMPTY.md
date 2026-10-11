# Sector Mode Leaves the Building Address Registry Empty — Range Points Missing

**Date**: 2026-10-11
**Status**: 🟢 FIXED (verified in-editor Oct 11, 2026)
**Severity**: 🟡 HIGH

## Symptoms
- With **Use Sector Baking** ON (the scene default since commit `3439191`): the Debug HUD Range tab shows `Points: none` and `firing_position: MISSING / firing_target: MISSING`, so **IK AIM stays disabled**; running Target Practice logs `[ORDER] No firing_position on target block — using block center` and Vinny does not face `firing_target`.
- With sector baking OFF (per-building path) everything works.

## Root Cause
`CityMap3D.RegisterAddress()` was only called from `BuildRaymarchBlock` (the per-building path). The sector branch of `BuildMap` baked the buildings but never registered them, so `addressRegistry` stayed empty. `GetBuildingPointNames` and `TryGetBuildingPointLocal` — the only way building attachment points (`firing_position`, `firing_target`, `backstop`, …) resolve to world positions — read nothing but that registry.

**How it got in**: the bug is latent since sector baking was added; it was hidden because the scene shipped with `useSectorBaking: 0`. It surfaced when the scene default was flipped to ON during the Oct 11 cleanup pass. That flip was verified for *rendering* (cars, glass, sectors) but nobody audited what else depended on per-building registration — the Range workflow was never exercised afterwards.

## Fix
- `SectorBaker` now exposes what it already computes: `SectorData.placements` — one `PlacedBuilding` per building (stasset path, row/col/subIndex, buildings-in-block, world corner, voxel size, dims, and a **reference** to the voxel array the sector region was filled from; no copies).
- `CityMap3D.RegisterSectorPlacements` turns those into address-registry entries (same conventions as the per-building path: block id from row/col, `subIndex = -1` for a block's only building, `{blockId}_building[_i]` ids) **and** collision volumes for projectile raycasts (`PROJECTILE_SYSTEM.md`). On a debris rebake only the volumes are refreshed.
- The per-building path additionally registers its chunks as collision volumes (`BuildingFootprint.data` → `RegisterBuildingVolume`), so both render paths are shootable.

## Verification
- Editor + Player-dev assemblies compile clean.
- ✅ Verified in-editor: registrations logged after map build; Range tab lists `firing_position, firing_target, backstop`; Target Practice aims and FIREs against the real target (projectile impacts confirmed on the target board).

## Lesson
When flipping a default that swaps an implementation path, grep for every consumer of the data the *old* path produced (`rg addressRegistry|Addresses|TryGetBuildingPointLocal`) before calling it safe — "renders the same" is not "behaves the same".

## Related Files
- `Assets/Scripts/UI/CityMap3D.cs` — `RegisterSectorPlacements`, `RegisterBuildingVolume`, `RegisterAddress`, `TryGetBuildingPointLocal`
- `Assets/Scripts/UI/SectorBaker.cs` — `PlacedBuilding`, `SectorData.placements`
- `docs/systems/PROJECTILE_SYSTEM.md` — consumer of the collision volumes
