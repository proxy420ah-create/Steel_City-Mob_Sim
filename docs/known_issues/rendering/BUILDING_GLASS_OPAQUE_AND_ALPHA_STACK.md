# Building Glass Baked Opaque, Jambs Hollow Beside Panes, Door Pane Alpha Stacking

**Date**: 2026-11-13
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- Glass placed in the building model (`Storefront Glass`, id 114: P.I. sidelights + transom) rendered as an
  opaque grey-blue wall in the Commission, while the door KIT's glass was see-through (barely).
- After making building glass transparent: jambs, casing and brick beside a pane looked hollow / one voxel
  thin where the model has two (a layer "missing" at the pane edge).
- Door pane read near-opaque next to the building's glass; the two never matched in transparency.

## Root Cause
Three independent causes, same feature:
1. **Opaque bake path.** The `*Glass*` -> transparent split lived only in `window.voxEmit` (KIT/props).
   `bake_building.py` quads went through `quadMesh` as one opaque Lambert mesh.
2. **Glass counted as solid for exposure.** `mesh(env, full > 0)` culled the faces of opaque cells that
   touch glass (glass == solid). Invisible while glass was opaque; once see-through, the culled faces
   exposed the hollow insides of the neighbouring wall/jamb.
3. **Alpha stacking in voxEmit.** The door pane is 2 voxels thick (z5-6); each voxel was its own transparent
   box, so opacity compounded (1 - .45^N) while the building pane is one single surface at .55.

## Resolution
- `bake_building.py`: emits `glass: [palette idx]`; opaque quads exposure-tested against OPAQUE occupancy
  only (`opq`), glass quads against everything (internal glass/solid seams culled). Collision `occ` grid
  unchanged (glass still blocks).
- `thecommission.html` `quadMesh(..., glass)`: returns `[opaqueMesh, glassMesh]`; glass mesh is
  `MeshLambertMaterial({vertexColors, transparent, opacity:.55, depthWrite:false})`, stage + env both split.
- `voxEmit`: props that carry the door contract (`m.door`) collapse each glass (x,y) column to its
  median-z cell so a pane is ONE sheet at the same .55 as the building.

## Rules going forward
- Any new glass material MUST contain "glass" in its name (the single trigger for both render paths).
- Never cull opaque faces against transparent neighbours.
- Panes in building models: 1-cell pane in the slab, air bored either side (see `_office_front_glass.py`).
- Verify glass with `node docs/_shot.mjs <name> --torch ...` (dark interiors hide this class of bug).

## Related Files
- `docs/bake_building.py` - glass list + opaque/glass exposure split
- `docs/thecommission.html` - `quadMesh`, `voxEmit`, `__snapTorch`
- `docs/_office_front_glass.py` - sidelights + transom authoring
