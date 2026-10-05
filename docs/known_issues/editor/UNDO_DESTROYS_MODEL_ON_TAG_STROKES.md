# Ctrl+Z After Tag Painting Removes the Whole Model

**Date**: October 4, 2026
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL — work loss, silent tag desync

## Symptoms

- In `voxel_editor.html`: load a character, paint body parts / clothing
  regions / attach points (tools that write `groupMap`, `clothingRegionMap`,
  or `itemPartMap`), then press Ctrl+Z → the entire model disappears from
  the viewport.
- Redo does not reliably restore it; tag data can drift out of sync with
  voxels even when the model survives.

## Root Cause

Two stacked defects in the editor history system:

1. **`snapshot()` captured only `voxelMap`.** Body-group tags, clothing
   regions, and attach-part tags (`groupMap` / `clothingRegionMap` /
   `itemPartMap`) were never part of history — undo could not revert them.
2. **Tag tools never called `pushHistory()`.** Painting body parts left the
   history index pointing at the entry *before* the model was loaded
   (history[0] is the empty init snapshot pushed at startup, or the empty
   post-asset-switch snapshot). A single Ctrl+Z therefore restored the
   empty voxelMap → "removed the character."

## Fix

- `snapshot()` now returns `{vox, grp, parts, regions}` — copies of all four
  per-voxel maps; new `applySnapshot()` restores all four.
- `undo()`/`redo()` call `applySnapshot()` and refresh the group/part/region
  count panels (`updateGroupCounts`, `updateItemPartCounts`,
  `updateClothingRegionCounts`).
- `pushHistory()` added to every tag-mutation branch: `bodypart`,
  `clothingregion`, `itempart` — both single-click (with mirror candidates)
  and selection-assign paths. Tag strokes are click-only (no drag mode), so
  per-click pushes are correct granularity.

## Related Files

- `VoxelAssetStudio/voxel_editor.html` — `snapshot`/`applySnapshot`/`undo`/`redo` (~line 2859), tag tool handlers (~line 3199+)
- History consumers unchanged: `pushHistory` callers for place/erase/fill/line/box/paste/extrude

## Verification

- Load character → paint a body part → Ctrl+Z → tag reverts AND model stays.
- Ctrl+Y → tag re-applies.
- Multi-stroke undo walks back through tag AND sculpt strokes in order.
- `isDirty()`/`savedHistoryIndex` unaffected (index-based, not content-based).
