# Hover Highlight Rebuilt ~2xN Instances Per Mousemove

**Date**: 2026-10-09
**Status**: 🟢 FIXED (awaiting editor verification)
**Severity**: 🟡 HIGH

## Symptoms
- In `voxel_editor.html`, hovering the select tool over a large region (e.g. the ~27.7k-voxel Vehicle Paint mass on `vehicle_490_touring`) or panning/orbiting with a big active selection caused visible frame hitches and sluggish hover tracking.
- Worst on vehicle/building asset types — large dense bodies produce large flood-fill previews.

## Root Cause
Every `mousemove` fired `updateHighlight()` → `showHighlight()`, which disposed and rebuilt **two** `THREE.InstancedMesh` objects — a translucent fill box **and** a 12-edge wireframe box — sized to the *entire* target region. A 27,681-voxel hover meant ~55k instance matrices rebuilt, uploaded, and rasterized **per mousemove**, plus geometry/material allocation churn on the GC. The persistent selection overlay (`showSelectionOverlay`) and frozen-ruler preview used the same per-voxel instanced pattern.

## Resolution
All three call sites now share `buildShellMeshes()`, which renders the region as a single static `BufferGeometry` "shell" instead of per-voxel instances:

- **Fill**: one `THREE.Mesh` of the region's *exterior* faces only (2 tris per surface face; interior voxels emit nothing).
- **Wireframe**: one `THREE.LineSegments` of *contour* edges only — edges shared by two coplanar faces (interior grid lines) are deduped away, so the outline of the shape is drawn without the dense per-voxel lattice.
- Both layers use `depthTest:false` + `renderOrder` 998/999, so the highlight reads through the model instead of being culled by it.
- `positions.length > 150000` (`HL_SHELL_LIMIT`) falls back to a single bounding-box wireframe + fill (e.g. box-tool previews spanning the whole volume).
- Frozen selections no longer rebuild an identical same-color hover duplicate each mousemove — the persistent overlay already draws them.

**Round 2 — compute cost.** The shell swap fixed the render/allocation side, but `updateHighlight` still *recomputed* the flood fill + shell per mousemove. Added:

- **`positionsMemo`** — preview results are deterministic per (params + start cell) and every member cell of a connected region yields the same set, so the final positions array is cached under every member cell. Sliding across a 27k region costs **one** compute total instead of one per voxel. Cleared on any gesture that could mutate (pointerdown / keydown / click capture + any button-held mousemove) — worst case is a wasted rebuild, never staleness.
- **Identity skip** — `showHighlight` returns early when the positions array + color are unchanged (`_hlShownRef`), so memo hits are ~free.
- **Camera-drag freeze** — mid/right drags (rotate/pan per `mouseButtons`) skip `updateHighlight` entirely; the last highlight stays put while orbiting.
- **Box-drag bounds** — `computeBoxVoxels` for a >150k-cell drag allocated the full cell list per mousemove; the preview now draws the bounding box directly without enumerating.
- Memoized paths: select-tool flood preview (plain + Alt-additive), W wardrobe-region scan, Q body-group scan.

**Round 3 — allocation cost on memo misses.** First hover on each new region still ran `computeFloodFill`/`computeFloodFillAir`/`buildShellMeshes`/`mirrorPositionsPreview` with `Set<string>` visited marks and per-cell `[x,y,z]` array churn — tens of thousands of string allocations per compute. All now share a persistent epoch-marked `Uint32Array` scratch grid (`beginMark()`/`_cellIdx`) + flat numeric stacks — ~3-5× faster per region miss, near-zero GC churn. Evaluated and rejected: Web Worker (voxelMap isn't shareable; SharedArrayBuffer needs COOP/COEP headers unavailable on file://) and time-sliced BFS (kept as fallback if first-hover blips still bug).

## Related Files
- `VoxelAssetStudio/voxel_editor.html` — highlight system `buildShellMeshes`/`buildBoxMeshes` (~line 3284), `updateHighlight` select branch (~line 4871)

## Verification
- 🧪 In the editor: select tool → hover the 27k Vehicle Paint region → smooth hover/orbit, outline wireframe visible through the car. Box-select a large volume → bounding-box wireframe. Frozen ruler + paste preview still show their marks.
