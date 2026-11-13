# KIT Loader Z-Anchor Slipped to Back-Edge — All Props Drifted Half-Depth

**Date**: 2026-11-12
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL

## Symptoms
- Multiple commission KIT props visibly displaced in z — different items by
  different amounts (table off the rug, file cabinet wall gap, door seated
  .045 back in its frame)
- User report: "several items have drifted from their correct positions"

## Root Cause
`thecommission.html` KIT loader (loadItems, ~line 1962) had its shared
z-center constant changed from the project convention:

```js
const cz = m.dims[2] - .5;      // BUG — anchors pos to the file's BACK EDGE
// vs every other loader:
const cz = m.dims[2] / 2 - .5;  // correct — anchors pos to the file centre
```

Since `pos:` is world-space but the local offset is `(vz - cz) * vs`, each
prop slid in −z by `dims[2] / 2 × vs` — proportional to its own file depth.
The slip rode in with the door leaf-parenting work (same edit block) and was
never exercised against non-door props.

## Why it went unnoticed
The door itself rendered plausibly — under back-edge anchoring its slab
happened to fill the wall depth, masking the regression until the scene was
viewed at room scale.

## Fix
- Restored `cz = m.dims[2] / 2 - .5` (matches the three other emit sites)
- Re-seated the door: `pos z 2.16 → 2.115` so the .075 leaf slab centres in
  the wall slab (world z 2.07–2.16). Pivot lands at the same world z as
  before — swing axis unchanged.

## Related Files
- `docs/thecommission.html` — loadItems z-center constant
- `Assets/StreamingAssets/voxel_items/commission_door.json` — leaf-only asset
