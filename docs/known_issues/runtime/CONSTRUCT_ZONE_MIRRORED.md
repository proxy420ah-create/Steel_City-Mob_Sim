# Construct Zone — Row Flip + X Mirror Mismatch vs Unity Map

**Date**: 2026-02-14
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- The studio-table Battlefield (city_layout r3–5 × c0–2) did not match the
  in-game Unity map — the user flagged "the alley orientation or tenement
  block position is off."
- The composite was a 180° misplacement: x-mirrored AND row-flipped, so
  the sparse symmetric content still looked plausible until compared
  side by side.

## Root Cause
`emitZone` carried the EDITOR's single-asset conventions into a map
composite:

1. `Z0 = (b.row - z.r0) * TILE` placed increasing layout rows toward +z.
   Unity's `CityMap3D.cs` anchors blocks at
   `(col−centerCol)·spacing → +x`, `−(row−centerRow)·spacing → −z` —
   rows march NORTH (−z). Ours marched south.
2. The whole zone inherited `emitVox`'s default fx x-mirror — correct for
   a lone authored asset shown in the editor view, wrong for a layout:
   Unity's `VoxelChunkManager.LoadChunkCentered` renders map chunks raw
   (no `scale.x = −1`).
3. Slots keyed by `s.slot`; Unity's `SectorBaker` positions buildings by
   array index — the JSON `slot` field is metadata.
4. Seam edges were assigned to the pre-flip z frame — `n`/`s` had to swap
   (`n` = the seam shared with row+1 now faces the z-min edge).

## Fix
- `emitZone`: `Z0 = (z.r1 − b.row)·TILE` (row↑ → −z); seams `n`/`s` swapped
  to follow; `slots` keyed by `buildings[i]` array index.
- The battlefield spec carries `flip:false` — map composites render raw;
  the fx mirror stays the law for lone authoring-view assets (BLDG/BLOCK).

## Verification
- `_shot.mjs` aerial: tenement at the composite's NW corner (r5,c0 →
  local −x,−z), barbers adjacent across the block seam, alley lane E-W
  through block_42's middle index-row.
- `_pad_check` 15/15, `_noslice_verify` ALL PASS, no page errors.

## Related Files
- `docs/construct/03_tables.js` — `emitZone` + battlefield spec
- `Assets/Scripts/UI/CityMap3D.cs` — block anchor convention
- `Assets/Scripts/UI/SectorBaker.cs` — index-based building placement
- `Assets/Scripts/UI/VoxelChunkManager.cs` — raw (unmirrored) chunk loads
