# Export Drops Animation Metadata (pivots/animParams/states)

**Date**: October 2, 2026
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL — silent data loss on export

## Symptoms

- Export a character (e.g. Civilian1) from the voxel editor, reload the
  exported file into the Reference Preview or animator → **arms/legs drift
  away from the body** in any non-T pose.
- T-pose looks correct (identity rotations hide the bad pivots).

## Root Cause

`getEditorData()` only serialized the fields the editor itself edits:
`voxels`, `groups`, `regions`, `itemParts`, `materials`, `dims`…

It did **not** carry `pivots`, `animParams`, or `states` — the animation
metadata the animator writes and the pose engine needs. Re-exported files
silently lost them; the preview's FK then fell back to default pivots,
rotating arm/forearm groups around incorrect joint positions → detached,
drifting limbs.

## Fix

- New `loadedAnimData` global captures `pivots` / `animParams` / `states`
  verbatim on every load path (`loadProject`, `importStasset`, `loadWIP`,
  `loadBaseTemplate`, `applyDefaultModelData`).
- `getEditorData()` spreads `loadedAnimData` into exports — round-trips are
  now lossless for animation metadata.
- Both globals are cleared on asset-type switch (fresh volume) so stale
  pivots/names can't leak into a new model.
- `loadedModelName` (same mechanism) preserves the filename stem so exports
  download as `<name>.json` drop-in replacements instead of hardcoded
  `Vinny.character.json`, and `format` is now asset-type-correct
  (`steelcity_character` / `steelcity_item` / `steelcity_stasset`).

## Related Files

- `VoxelAssetStudio/voxel_editor.html` — `getEditorData`, `loadedAnimData`,
  all load paths
- `Assets/StreamingAssets/voxel_characters/Civilian1.json` — the file whose
  pivots were being dropped

## Resolution

Fixed same-day. Caveat: pivots are normalized to dims — if a volume is
resized, preserved pivots may be stale; re-tune pivots in the animator after
any dims change.
