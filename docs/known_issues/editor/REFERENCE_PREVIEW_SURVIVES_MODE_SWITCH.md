# Reference Preview Survives Asset-Type Switch

**Date**: 2026-10-05
**Status**: 🟢 FIXED
**Severity**: 🟢 MEDIUM (visual leak — stale mesh, no data corruption)

## Symptoms
- Load a reference preview (Reference Preview panel → character JSON) while in
  Building mode → renders beside the editable model as intended.
- Switch the asset-type dropdown to Character → the unsaved-changes guard +
  default-model import prompt run, but the building's reference model stays
  rendered over the fresh empty scene.
- Expected: a mode switch produces a clean environment; the reference should
  only exist when explicitly loaded for the current session.

## Root Cause
The `asset-type-select` change handler resets `voxelMap`/`groupMap`/tags,
dims, camera, and history — but never touches the reference-preview state
(`charData`, `charMesh`, `charEdgesMesh`, `attachMesh`, `charPlaying`,
`charArmed`). `rebuildCharMesh()` is only invoked by char-panel events, so the
old `InstancedMesh` simply stayed in the scene.

## Resolution
The handler now performs a full reference teardown on switch:
- `charData = null`, `charPlaying = false`, `charArmed = false`,
  `charOffsetX/Z = 0`
- `detachItem()` — removes an editable-model attachment welded to the
  reference's hand (`attachMesh` would leak independently)
- `rebuildCharMesh()` — disposes `charMesh` + `charEdgesMesh`
  (early-returns on null `charData`)
- `char-info` text reset; `char-play-btn` label/active state and
  `char-disarm-btn` active state reset so the panel doesn't show stale UI.

File-import paths (`loadProject`, `importStasset`, `loadWIP`) intentionally
keep the reference — they replace the working model, and the reference overlay
remains a valid scale comparison across model swaps. Only the explicit
asset-type dropdown (the "fresh environment" path) clears it.

## Related Files
- `VoxelAssetStudio/voxel_editor.html` — asset-type change handler (~line 4720),
  `rebuildCharMesh` (~line 6250), `detachItem` (~line 6349)
