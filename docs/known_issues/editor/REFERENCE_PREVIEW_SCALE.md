# Reference Preview Renders at 2× True Scale

**Date**: 2026-10-02 (discovered via user screenshots)
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH — broke the core scale-comparison workflow for weapon/prop authoring

## Symptoms

- Loading `Civilian1.json` as both the editable model and the preview model in
  `voxel_editor.html` rendered the preview **2× larger** than the editable copy —
  same file, two different sizes.
- Loading `SW_Model_10.json` into the preview picker produced a giant duplicate
  of the gun beside the editable one.
- Any preview model rendered at 2× real scale in all asset modes.

## Root Cause

`getCharScaleRatio()` hardcoded character voxels as `0.02m` — the pre-Aug-14
standard. After the character upscale (48³ → 96³) `voxelSize` was halved to
`0.01m`, but the preview ratio was never updated:

```js
// Before (stale):
return 0.02 / itemVoxelSize;   // = 2.0 for character & prop modes
```

Additionally, `parseCharacterData()` never read the `voxelSize` field that
`getEditorData()` already writes into exported JSON, so the preview had no way
to know the loaded model's real scale.

## Resolution

- `parseCharacterData()` now returns `voxelSize` — explicit `data.voxelSize`
  first, then inferred from `data.assetType` preset, else `null`.
- `getCharScaleRatio()` = `previewVoxelSize / editableVoxelSize`, where
  `previewVoxelSize` comes from the loaded file (fallback: character preset
  `0.01`).
- Added `"voxelSize": 0.01` to `Assets/StreamingAssets/voxel_characters/Civilian1.json`.
- Panel relabeled "Reference Preview" (accepts any model JSON — valid
  scale-comparison workflow), "Sync Gun" → "Re-center", and the info line now
  shows the resolved voxelSize and ×scale vs the editable model.

Result: preview scale = 1.0 for same-type files, 0.1 for character-vs-building —
true real-world proportions everywhere.

## Related Files

- `VoxelAssetStudio/voxel_editor.html` — `getCharScaleRatio()`, `parseCharacterData()`, preview panel
- `Assets/StreamingAssets/voxel_characters/Civilian1.json` — added `voxelSize`
- `docs/systems/CHARACTER_SPAWNING_SYSTEM.md` — stale 0.02m refs corrected

## Remaining Gaps (not fixed)

- **Unity ignores `voxelSize` in JSON** — `VoxelCharacter.cs` uses the Inspector
  field (default `0.01f`), `CharacterJsonLoader` doesn't read the field. Fine
  while the standard stays 0.01, but the file-level field is currently
  write-only on the editor side.
- **Duplicated pose engine** — `voxel_editor.html` keeps an inline copy of
  `character_pose_engine.js` instead of importing it; two copies to maintain.
