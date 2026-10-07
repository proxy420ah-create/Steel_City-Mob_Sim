# Promoted-File Round Trip — Double Flip (wrong aim arm)

**Date**: October 6, 2026
**Status**: 🟢 FIXED (editor auto-convert; pending in-editor confirmation)
**Severity**: 🟡 HIGH

## Symptoms
- Re-exporting Civilian1 and running `Tools → Voxel Import → Character`: the S&W Model 10 sat in the correct (right) hand, but Aim / Aim Sweep raised the LEFT arm.
- Loading `StreamingAssets/voxel_characters/Civilian1.json` into `voxel_editor.html` showed one left-hand voxel inside the right-hand cluster and vice versa (right_hand centroid drifted x 12.5 → 16.875). Repainting them "clean" is what produced the bad export.

## Root Cause
`tools/promote_character_to_unity.py` flips `attachmentPoints` names and the asymmetric `animParams` and stamps `handedness: "unity"`, but never flips the painted `itemParts` tags. Loading a promoted file into the editor therefore:
1. re-tags one voxel per hand from the *flipped* `attachmentPoints` onto the *unflipped* painted clusters — the phantom "contamination" (not an authoring error; the WIP source and the working runtime file both had clean 16/16 clusters), and
2. on export rebuilds `attachmentPoints` from the painted tags (editor side again), carries `animParams` through verbatim (still Unity-flipped — `loadedAnimData` copies pivots/animParams/states), and drops the stamp.

Promotion's two guards (the stamp, and which side `right_hand` sits on) then both see an ordinary editor file and flip again: attachments once (correct), anim params twice (wrong arm). Reproduced bit-for-bit offline against the 22:01 output (`right_hand` 82.5/gid 8, `armSwingR=-1.4`, `torsoTwist=-0.2`).

## Fix
`voxel_editor.html`: `demoteIfUnity(data)` runs at every file / default-model / project / stasset-JSON load site. A file stamped `handedness: "unity"` is converted back to editor convention on load (same flip as the Python — it is its own inverse), the stamp is removed, and the status bar notes the conversion. Exports are always editor convention, so promotion runs exactly once.

Verified offline: JS-vs-Python flip parity in headless Edge (real runtime file, real WIP source, synthetic every-branch and single-key cases); flip∘flip = identity; stamp handling; replay of load → export → promote reproduces the working runtime file exactly (no centroid drift, no repainting).

## Not Covered
- `character_animator.html` has no stamp handling — do not load promoted files there (its export carries no `attachmentPoints`, so promotion aborts loudly instead of flipping silently).
- Vehicles/buildings are unaffected by this bug (nothing to promote), but the editor↔Unity mirror still shows in asymmetric geometry — see `docs/systems/VEHICLE_VOXEL_ASSETS.md` (Handedness).

## Related Files
- `VoxelAssetStudio/voxel_editor.html` — `demoteIfUnity` and the `hd*` flip helpers
- `tools/promote_character_to_unity.py` — the Python flip (single owner of the promotion contract)
- `Assets/Editor/ImportCharacterJSON.cs` — `Tools → Voxel Import → Character` shells out to the script
- `docs/systems/CHARACTER_ASSET_LIFECYCLE.md` — G10 (mirror), G11 (this bug)
