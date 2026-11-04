# Room Builder — Aim Pick + Ghost Silhouette Lag After Stamping

**Date**: 2026-11-03
**Status**: 🟢 FIXED (boss-confirmed: "night and day difference")
**Severity**: 🟡 HIGH

## Symptoms
- `voxel_editor.html` Room tab: after baking ONE office stamp into the volume, aiming the next
  stamp/placement (mouse-bound ghost) dropped to a crawl. Blank canvas was fine.

## Root Cause
Three per-mousemove costs that scale with baked/prefab voxel count (measured headless, one
62,340-voxel `commission_office_center` stamped):
1. **Aim pick** — `raycastVoxelMesh` -> `intersectObjects([instancedMesh])` tests every instance:
   **15–69 ms/call**; the editor's own `updateHighlight` raycast ran on top of it.
2. **Ghost rewrite** — one instance per prefab voxel (38,860 even after interior-culling, since
   the walls are 3 voxels thick), matrix + color + neighbor lookups per voxel: **31–38 ms** per
   aim-cell change, then rendered translucent over the model every frame.
3. **Anti-pattern repeat** — the same "per-voxel InstancedMesh rebuilt every mousemove" described
   in `HIGHLIGHT_INSTANCED_REBUILD_LAG.md`.

Earlier attempts (signature gate, 30 Hz throttle, interior-cull) were guesses and did not fix it.

## Resolution
- `raycastGrid()` — grid DDA over `voxelMap` (~0.01 ms; identical hits to the mesh pick on 118/118
  mirrored + 114/114 unmirrored samples). Aim handler gate: 16 ms.
- Ghost = block-merged AABB boxes (4x4x4 buckets, cached per model): 38,860 -> 1,888 instances,
  **1.5 ms** per rewrite. Contact colors per block.
- `updateHighlight` skipped while aim mode is armed (the ghost is the highlight).
- Related fixes the same session: Esc now hides the ghost + resets the signature; scaled-prefab
  ghost positions use `pos + idx*s`.
- Patterns written up as `docs/core/PERFORMANCE_PATTERNS.md` #15–#18.

## Known limits
- `raycastGrid` ignores slice clip / hidden materials (placement aim only).
- Stamp commit itself: 240–480 ms one-time (`rebuildMesh` dominates).
- Measured on SwiftShader — GPU frame rate / overdraw with 120k+ voxels not measured.

## Related Files
- `VoxelAssetStudio/voxel_editor.html` — `raycastGrid`, `refreshGhost`, aim-mode block (Room tab)
- `docs/core/PERFORMANCE_PATTERNS.md` — #15 Grid DDA pick, #16 block-merged preview, #17 change-gate, #18 headless measuring
- `docs/known_issues/editor/HIGHLIGHT_INSTANCED_REBUILD_LAG.md` — the earlier instance of the same anti-pattern
