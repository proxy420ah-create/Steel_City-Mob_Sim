# Missing Stasset Silently Skipped — Building Invisible, No Diagnostics

**Date**: 2026-10-30 (barber_shop placed in 2 layout slots, rendered nowhere)
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — any missing/misnamed stasset produces an invisible hole with zero console output

## Symptoms

- `barber_shop.stasset` referenced in `block_42` slot 0 and `block_41` slot 2;
  both lots rendered empty after rebuild — no errors, no warnings.

## Root Cause (two stacked failures)

1. **Converter invoked wrong.** `Tools/json_to_stasset.py` takes the output
   path as a **positional** argument — there is no `-o` flag. Called with
   `-o <dst>`, it silently wrote a file literally named `-o` in the project
   root; the expected `voxel_buildings/barber_shop.stasset` never existed.
2. **Bake path swallows the failure.** `SectorBaker` does
   `GetStassetDimensions(path)` → `if (vw == 0) continue;` at every placement
   site (single-building, full-block detect, sub-grid) — an unreadable stasset
   is a silent `continue`. The layout entry, block, and path are never logged.

## Resolution

- `SectorBaker.BakeAllSectors`: `Debug.LogError` on `vw == 0` naming block id,
  block name, buildings[] index, and the stasset path — at the single-building
  site and in the full-block detection loop (which probes every entry and thus
  covers the sub-grid skip too).
- New editor menu **`Steel City/Voxel Assets/Convert WIP → Stassets`**
  (`Assets/Editor/StassetBuildMenu.cs`): scans `VoxelAssetStudio/JSON Models
  In Progress/`, converts `assetType=="building"` files whose JSON is newer
  than the .stasset (or missing) — with a "force all" variant — and reports
  converted/skipped/failed. Removes the manual CLI step that caused failure #1.
- Moved the stray `-o` file to the correct
  `voxel_buildings/barber_shop.stasset`; verified 64×36×64, 21,879 vox.

## Gotcha (persistent)

- `json_to_stasset.py` has **no `-o` flag** — output path is positional.
  The converter accepts it silently and writes to whatever arg lands second;
  a flag-looking filename in CWD is the tell.

## Related Files

- `Assets/Scripts/UI/SectorBaker.cs` — `BakeAllSectors` placement loops
- `Assets/Editor/StassetBuildMenu.cs` — new
- `Tools/json_to_stasset.py` — positional output arg
- `docs/VOXEL_ENGINE_GOTCHAS.md` — pipeline section updated
