# Room Builder — Move-Mode Preview Rebuilt the Whole Shell Per Nudge

**Date**: 2026-11-03
**Status**: 🟢 FIXED (headless-measured; pending boss feel-test)
**Severity**: 🟡 HIGH

## Symptoms
- After a stamp commits it enters the native Move (paste preview) mode. Holding PgUp/PgDn (or
  the arrow keys) made the editor bog badly — presses queued behind each other.

## Root Cause
`updateHighlight`'s paste branch rebuilt the preview for the entire clipboard on every
`movePaste`: 62,340 fresh `[x,y,z]` arrays -> `showHighlight` -> `buildShellMeshes` (surface
extraction) -> dispose/re-add meshes. Measured **~450 ms per nudge** (profile: buildShellMeshes
43%, GC 34%, showHighlight 16%). A translation does not change the shape — the rebuild was pure waste.

## Resolution
- Shell built once per clipboard shape (key: clipboard identity + `_pasteRev` + origin + length) at
  offset 0; each nudge only sets `hlMesh/hlEdgesMesh.position = pasteOffset`. **450 ms -> 2.6–4.9 ms.**
- `rotatePaste` bumps `_pasteRev`; Mirror Move (`_pasteUniform = false`) keeps the old per-cell path.
- Documented as `docs/core/PERFORMANCE_PATTERNS.md` #19 (+ CDP profiler recipe in #18).

## Same-session audit fixes (Room builder state/UX)
- Aim mode could outlive the Room type and swallow canvas clicks -> disarms on asset-type switch,
  tool change and Room-tab leave (each verified headless).
- Leaving the Room tab mid-move left an invisible active Move -> now cancelled + stamp selection cleared.
- Stamps could silently clip outside the volume (9,180 of 27,000 voxels in one test) -> "Keep inside
  volume" clamp (default on).
- Stamping a prop at a different voxel scale baked it at the wrong size -> blocked with a message
  pointing at Place. Scale for ghosts/placements now derives from asset class (`prefabVS`):
  commission_wall/floor metadata says 0.025 but runs at the room grid's 0.03.
- Arming with an empty model list failed silently; the ghost only appeared after the first mouse move;
  Delete now removes a selected placement.

## Follow-up (same day): preview was tool- and hover-gated
Boss report: after placing a stamp the Move panel opened but arrows did nothing and no ghost showed.
`updateHighlight` (which drew the paste preview) returns early when the active tool is **Camera**
(where Esc leaves you) or when the pointer isn't over a voxel; headless tests had used the default
Place tool, so they missed it. Fix: preview moved into `updatePastePreview()`, called directly by
`moveSelection/pasteSelection/movePaste/rotatePaste` and dispatched first in `updateHighlight`; canvas
`mouseleave` no longer clears it during a move; confirm/cancel clear it. Verified with Camera tool +
pointer off-canvas (hl visible at stamp time, offset follows 4 keys).
Note: the default Room canvas height (69) equals the module height, so PgUp past the top clips the
ceiling's top layer (7,500 voxels in test) - raise H via Canvas... for headroom.

## Known limits
- Two undo steps remove a moved stamp (stamp + move are separate history entries — native behavior).
- Mirror Move preview still rebuilds per nudge; placements are not in undo history.
- Arrow-key axis choice is arbitrary at an exact 45-degree camera angle (still consistent).

## Related Files
- `VoxelAssetStudio/voxel_editor.html` — paste branch in `updateHighlight`, `_pasteRev/_pasteShape/_pasteUniform`, `prefabVS`, Room aim/selection block
- `docs/core/PERFORMANCE_PATTERNS.md` — #19 translate-don't-rebuild, #18 measuring, lessons 10–12
- `docs/known_issues/editor/ROOM_BUILDER_AIM_PICK_AND_GHOST_LAG.md` — the earlier Room perf fix
