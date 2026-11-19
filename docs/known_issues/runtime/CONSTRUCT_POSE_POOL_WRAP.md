# CONSTRUCT — Pose scratch-pool wrap (silent transform aliasing)

**Status:** FIXED (pools 96/64 -> 128/128)
**Filed:** 2026-11-19
**Symptom:** Console warn `POSE POOL OVERFLOW — _pi 75` from `05_pose_engine.js:43`, recurring every frame once it trips (called via `selfStep` in `10_self_solids.js` -> `loop`).

## Root cause
`evalPose` draws every rotation-matrix (`_m9`) and vector (`_v3`) scratch slot from a shared monotonic counter `_pi` that resets per call. `nx3()`/`nx9()` wrap silently (`_pi++ % len`). `_v3` was sized 64; the aim+IK path (ikSolve -> basisDelta -> scaledRot, plus head tracking) legitimately consumes ~75 slots in one call, so `_v3` wrapped — a 65th allocation overwrote slot 0 while slot 0 could still be referenced in a live transform chain. Wraps are silent; the DESIGNER-added `_poolCheck` is the only tripwire (warns once per rig).

## Impact
Wrong-posed frames (limb/head math reading a recycled vector) — visual glitch, not a crash. Only the aim/IK-heavy path (self body while aiming) exceeded 64; plain gait stays under.

## Fix
`_m9` and `_v3` both sized to 128 (~70% headroom over observed 75). `_poolCheck` threshold automatically follows via `Math.min(_m9.length, _v3.length)` — still trips once if consumption ever exceeds 128.

## Verification
- `node --check docs/construct/05_pose_engine.js` clean
- `_pose_check.mjs` 7/7: idle finite, gait animates, head tracks gaze 54.43 -> 42.49
- Console should no longer emit POSE POOL OVERFLOW during aim+walk.
