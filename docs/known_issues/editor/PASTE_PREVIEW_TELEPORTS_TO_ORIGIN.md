# Paste Preview Teleports to Origin — BBox Fallback Stomped by Move Offset

**Date**: 2026-11-12
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- Moving a very large selection (ceiling slab, ~450k voxels): the cyan move ghost
  teleported to the grid origin — "under the model" — instead of riding with the
  selection. Smaller selections never showed it.

## Root Cause
`buildShellMeshes` falls back to `buildBoxMeshes` for position sets over
`HL_SHELL_LIMIT` (~150k voxels), and the bbox path bakes the selection's *center*
into `mesh.position`. `updatePastePreview` then stomped `hlMesh.position` with the
raw drag offset — so the ghost sat at origin+offset instead of center+offset.
The shell path bakes everything into vertices at position zero, which is why only
giant selections hit it.

## Resolution
- The built position is recorded at build time as the mesh's base
  (`userData.bpos`); the move offset applies **relative to** the base — works for
  both the shell path (base = origin) and the bbox fallback (base = center).
- The lookup is self-healing: if `bpos` is ever absent, the current position is
  stamped as base rather than drifting.

## Related Files
- `VoxelAssetStudio/voxel_editor.html` — `buildBoxMeshes`, `showHighlight`,
  `updatePastePreview` (~line 5800)
- `docs/known_issues/editor/ROOM_BUILDER_MOVE_PREVIEW_REBUILD_LAG.md` — the earlier
  move-preview defect (per-nudge rebuild; different bug, same feature)
