# dummyMatrix Scale Leak — Editable Voxels Render Wrong Size

**Date**: October 3, 2026
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — visually misleading editing, no data loss

## Symptoms

- In Item mode (0.005m) after loading a reference preview, the editable
  model renders with **oversized chunky voxels** — a 1-voxel line-tool
  stroke looks like a 2×2×2 blob; hover highlight appears larger than the
  model's voxel pitch.
- Character and Building modes appear unaffected.

## Root Cause

`rebuildCharMesh()` (and `attachItem()`) call
`dummyMatrix.compose(pos, quat, scaleVec)` which stores the preview scale
`s = previewVoxelSize / editableVoxelSize` on the **shared** `dummyMatrix`
scratch object.

`rebuildMesh()` and every other unit-scale instancing loop used
`dummyMatrix.setPosition(...)` — which overwrites only the translation
component and **preserves the leftover scale**.

- Item mode + character preview: `s = 0.01/0.005 = 2` → editable voxels
  rendered at 2× → "fat" model and apparently-giant tool highlights.
- Character mode: `s = 1` → invisible. Building mode without a preview
  loaded: invisible. Matches the user's observation that only Item mode
  looked wrong.

## Fix

All `dummyMatrix.setPosition(...)` call sites converted to
`dummyMatrix.makeTranslation(...)` (5 sites: highlight mesh, ruler frozen,
selection overlay, editable instanced mesh, editable edges). A pure
translation matrix can never carry stale scale/rotation.

## Related Files

- `VoxelAssetStudio/voxel_editor.html` — `rebuildCharMesh`,
  `getCharScaleRatio`, `rebuildMesh`, `showHighlight`, `showRulerFrozen`

## Resolution

Fixed same-day. Class-of-bug note: any future shared scratch matrix use
must use `makeTranslation`/`compose` — never `setPosition` alone.
