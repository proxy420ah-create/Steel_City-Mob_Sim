# CONSTRUCT — Follower PiP strobes (20Hz-gated draw on a non-preserved buffer)

**Status:** FIXED (pip every frame)
**Filed:** 2026-11-18
**Symptom:** FOLLOWER POV inset visibly flickers — alternates between the follower's POV and the main view behind it. Most visible when the two views disagree (bright content in one, dark in the other).

## Root cause
`14_loop.js` gated the scissored `__eyes.pip()` pass at 20 Hz (`_pipT = .05`) with the comment "a second full render is a luxury" — the intent was to throttle POV *updates*, but it throttled the *draw*.

The renderer is a stock `WebGLRenderer` (`preserveDrawingBuffer:false`, `autoClear:true`). Every rAF frame the main pass clears the WHOLE drawing buffer (scissor test off) and paints the main camera across the inset rect too. With no preserved buffer there is no "stale POV" to hold between draws — on the ~2/3 of frames where `pip()` didn't run, the inset showed the main view; on pip frames it showed the follower POV. Result: a ~20 Hz strobe between two images, by construction.

## Impact
Visual only — the PiP was never wrong content-wise, just absent most frames. `__eyes.shot()`/`snap` (agent POV captures) unaffected — those render on demand.

## Fix
`construct/14_loop.js:85` — the gate is gone: `if (povOn) window.__eyes.pip()` runs every frame. `_pipT` removed. The per-frame cost is the scissored second scene render — the "luxury" accepted in exchange for a stable inset (the alternative if it ever hurts: 20 Hz render into a `WebGLRenderTarget` + every-frame textured-quad blit, i.e. throttle the content not the presentation).

## Verification
- `node --check construct/14_loop.js` clean
- Headless CDP (`_shot.mjs --page=construct`): wrapped `__eyes.pip` + `requestAnimationFrame` with counters over a live 2 s window → **92 pips / 92 rafs = 1:1** (was ~1:3 at the 20 Hz gate). `probe_pip60_full.png` shows the inset composited with real POV content.
