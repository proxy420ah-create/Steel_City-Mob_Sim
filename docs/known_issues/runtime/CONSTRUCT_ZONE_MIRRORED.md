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

1. The zone inherited `emitVox`'s default fx x-mirror — correct for a
   lone authored asset shown in the editor view, wrong for a layout:
   Unity's `VoxelChunkManager.LoadChunkCentered` renders map chunks raw
   (no `scale.x = −1`).
2. Slots keyed by `s.slot`; Unity's `SectorBaker` positions buildings by
   array index — the JSON `slot` field is metadata.
3. Slot rows ran index-0 at the block's z-min edge — Unity puts
   index-row0 on the edge TOWARD row+1.

## Fix
- `emitZone`: `slots` keyed by `buildings[i]` array index; `stampSlots`
  gains a footprint-aware `zFlip` (index-row0 lands on the row+1 side —
  the z-max edge under the `row↑→+z` grid).
- The battlefield spec carries `flip:false` — map composites render raw;
  the fx mirror stays the law for lone authoring-view assets (BLDG/BLOCK).
- Row direction `Z0=(row−r0)·TILE` retained — verified against the
  west-facing in-game compass shot (r5 tenement reads LEFT in both).

## Verification
- `probe_zone_west2.png` (camera east of the table looking west, matching
  the in-game W 271° pose): tenement far-left, barbers adjacent across
  the block seam, alley lane mid-block — matches the in-game frame.
- `_pad_check` 16/16, `_noslice_verify` ALL PASS.

## Related Files
- `docs/construct/03_tables.js` — `emitZone` + battlefield spec
- `Assets/Scripts/UI/CityMap3D.cs` — block anchor convention
- `Assets/Scripts/UI/SectorBaker.cs` — index-based building placement
- `Assets/Scripts/UI/VoxelChunkManager.cs` — raw (unmirrored) chunk loads
