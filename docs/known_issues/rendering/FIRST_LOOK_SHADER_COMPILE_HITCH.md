# First-Look Shader Compile Hitch — Portal / Sealed View

**Date**: 2026-11-13
**Status**: 🟢 FIXED (live-confirmed 2026-11-13)
**Severity**: 🟡 HIGH

## Symptoms

- First camera pan toward the portal doorway (and first entry into the sealed/possess
  view generally) produced a ~1 s page hitch. Measured: `window.__warmMs ≈ 1011 ms`
  of shader compile + buffer upload work, previously paid mid-frame.
- A naive `renderer.compile(portalG, camera)` at init did **not** fix it.

## Root Cause

Three lazy-GPU mechanisms stacked:

1. **`envG` starts `visible = false` and `renderer.compile` skips hidden subtrees** —
   the "prewarm" was a no-op for everything inside the envelope (portal, pendants,
   env quads).
2. **Envelope lights live in the hidden group.** Three.js shader programs bake in
   the live *light count*. The portal's two PointLights (and the pendant spots)
   only join the scene graph when `envG` becomes visible — so the compile pass ran
   against the wrong light count and every lit material recompiled once the sealed
   view revealed them.
3. **Frustum culling + lazy buffer upload** — even compiled programs still pay
   first-draw vertex-buffer/texture uploads when the mesh first enters view.
   An off-frustum warm render does nothing unless `frustumCulled` is defeated.

## Fix — the warm-up phase (`thecommission.html`, before `loadItems`)

- A `WARMING THE ROOM…` cover sits over `#deckview` from init.
- `window.__warmUp()` (debounced, re-entrant) fires after the KIT chain completes
  **and** again from the portal block — any system that adds lights/programs late
  calls it so its programs join the warm.
- `warmRun`: `renderer.compileAsync(scene, camera)` (parallel shader compile where
  `KHR_parallel_shader_compile` exists; falls back to sync `compile`), then one
  real `render()` with `envG` forced visible **and** `frustumCulled` cleared on all
  objects — programs *and* vertex buffers all upload before the player can look.
- Cover fades out on completion (15 s safety `setTimeout` so a stall can never
  strand the deck black).
- `window.__warmDone` / `window.__warmMs` are probe-readable; `_shot.mjs` gained
  `--eval=<expr>` to read them.

## Renderer flags added

- `powerPreference: 'high-performance'` on the `WebGLRenderer` (dual-GPU hint).
- `?pr=<n>` URL override for `setPixelRatio` — `?pr=1.5` halves fragment cost on
  hi-DPI displays; default stays `min(devicePixelRatio, 2)`.
- Console logs `DVM — GPU: <unmasked renderer string>` + parallel-compile support
  at init (Firefox users check `about:support` → WebGL for driver confirmation).

## Rules Going Forward

- **Never add/remove lights or toggle a group containing lights at runtime** —
  the light-count change recompiles every lit material. Scene-level lights stay
  registered and are dimmed to `intensity 0` instead (existing contract at
  line ~1982 comment; portal block now obeys it via `__warmUp`).
- **Any system that creates materials/lights after init must call
  `window.__warmUp()`** so its programs join the compile pass.
- **`window.__env` is the cross-script handle** — `envG` is `initDeck3D`-scoped;
  the Vinny async block must not reference it bare (was a silent page-killer).
- First-look hitches in this engine are almost always *lazy GPU work*, not CPU
  work — check `__warmMs` before profiling elsewhere.

## Related Files

- `docs/thecommission.html` — warm-up block before `loadItems`; portal block calls `__warmUp()`
- `docs/_shot.mjs` — `--eval` flag
- `docs/known_issues/rendering/IMGUI_HUD_FRAME_TAX.md` — related: per-frame tax vs first-use tax
