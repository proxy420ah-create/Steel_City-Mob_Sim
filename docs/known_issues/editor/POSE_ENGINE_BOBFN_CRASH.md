# Walk Preview Crash: `bobFn is not a function` + Drifted Bob/Shift Formulas

**Date**: 2026-10-02 (user-reported console error)
**Status**: 🟢 FIXED
**Severity**: 🔴 CRITICAL — hard crash on any walking animation preview in the editor

## Symptoms

```
Uncaught TypeError: bobFn is not a function
    getWalkPose voxel_editor.html:919
    poseVoxels voxel_editor.html:1276
    rebuildCharMesh voxel_editor.html:5441
```

Triggered by pressing ▶ Play (or selecting a walk state) in the voxel editor's
reference preview whenever `bodyBob.amplitude > 0` in the loaded model's
animParams.

## Root Cause

Two problems in the editor's **inlined** copy of `getWalkPose` (and identically
in `character_pose_engine.js`):

```js
// BUG 1: Math.sin(...) is EVALUATED — assigns a number, not a function
const bobFn = bobAmp > 0 ? Math.sin(cyclePhase * 2 * Math.PI) : () => 0;
const bodyBobY = bobFn() * bobAmp;   // number() → TypeError

// BUG 2: formulas drifted from canonical versions
//   bob:   sin(phase·2π)  → one bob per cycle (wrong; should be two footfalls)
//   shift: cos(phase·2π)  → 90° phase off
```

Canonical versions in `character_animator.html` and Unity's
`VoxelCharacterAnimator.cs:214-217`:

```js
const bodyBobY    = -Math.cos(cyclePhase * 4 * Math.PI) * bobAmp;    // 2 bobs/cycle
const weightShiftX = Math.sin(cyclePhase * 2 * Math.PI) * shiftAmp;
```

The drift is a symptom of the known tech debt: `voxel_editor.html` maintains an
inline copy of the pose engine rather than importing `character_pose_engine.js`.

## Resolution

Replaced the `bobFn`/`shiftFn` pattern with direct canonical expressions in
both `voxel_editor.html` (inlined copy) and `character_pose_engine.js` —
matching `character_animator.html` and Unity.

## Related Files

- `VoxelAssetStudio/voxel_editor.html:917-924` — inlined getWalkPose
- `VoxelAssetStudio/character_pose_engine.js:133-139` — shared module copy
- `VoxelAssetStudio/character_animator.html:1275-1281` — canonical reference
- `Assets/Scripts/Sim/VoxelCharacterAnimator.cs:213-217` — Unity reference

## Lesson

The inline-vs-shared pose engine duplication is now confirmed harmful: two bugs
(crash + formula drift) existed only in the duplicated copies. Consider
importing `character_pose_engine.js` in the editor, or deleting the module if
the inline copy is the maintained one.
