# Selection Move / Model Shifts Orphaned Tag Layers

**Date**: 2026-10-06
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- Moving a selected limb/section with the Selection Tool relocated the voxels but left body-group, wardrobe-region, and attach-part (attachment points, `pivot_N` joint clusters) tags behind at the old coordinates.
- Export then derived `attachmentPoints`/`pivots` from the stale tags: attachment anchors stayed at the limb's old position and exported joints rotated about the pre-move location.
- Same bug class existed in: single-voxel erase, erase-entire-selection, Delete Selection, clothing-preset `removedVoxels`, volume-resize clamping, Expand Volume index shift, and Center Model — all moved/deleted `voxelMap` (+ `groupMap` in some) while leaving `clothingRegionMap`/`itemPartMap`/`loadedAnimData.pivots` behind.
- `stripToBase` additionally cleared `groupMap` with no restore — stripping clothing wiped all body-group assignments off the base model.

## Root Cause
Per-cell authoring data lives in four coordinate-keyed maps (`voxelMap`, `groupMap`, `clothingRegionMap`, `itemPartMap`) plus normalized authored pivots (`loadedAnimData.pivots`). Every voxel-level transform/delete site updated only a subset of the layers, so tags outlived their cells (dangling refs on empty coords) or separated from their geometry.

## Resolution
`voxel_editor.html`:
- `moveSelection()` — clipboard entries now capture the cell's `gid`/`rid`/`pid` alongside `mid`.
- `confirmPaste()` (move mode) — deletes all layer entries at origins; restores each moved cell's tags at the destination; a moved cell fully replaces the destination cell's layers (un-carried layers cleared). Whole-body-group moves also translate `loadedAnimData.pivots[gid]` by the normalized delta when no painted `pivot_N` cluster survives for that gid (partial moves leave the joint — correct for rigid-body semantics).
- `copySelection`/`pasteSelection` unchanged — a pasted arm must not duplicate a `pivot_N` cluster (two blobs corrupt the joint centroid).
- `setVoxel` erase, selection-erase, `deleteSelection`, preset `removedVoxels`, resize clamp — now clear all tag maps per deleted cell.
- Expand Volume + Center Model — shift all four maps and `loadedAnimData.pivots` together (pivots re-normalized across new dims on expand).
- `stripToBase` — restores `groupMap` from new `baseTemplateGroupMap` (captured at base load) and clears `clothingRegionMap`.
- `snapshot()`/`applySnapshot` — include `loadedAnimData.pivots` so undo restores pivot translations too.

## Test
- Select a tagged arm (S tool) → Move → confirm: group color, wardrobe region, `pivot_3` cluster, and `right_hand`/`left_hand` tags all land at the new position; mirror preview re-poses around the moved joint.
- Export → `attachmentPoints`, `itemParts`, `regions`, `groups`, `pivots` all reflect the move.
- Ctrl+Z restores all layers.
- Erase/delete a tagged cell → no ghost tag remains.
- Expand/Center → all layers stay glued.
