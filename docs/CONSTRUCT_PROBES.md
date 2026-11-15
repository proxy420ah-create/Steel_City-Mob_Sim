# CONSTRUCT — Probe & Shot Contract

The headless-Chrome toolchain (`_place_probe.mjs`, `_shot.mjs`, `_fol_probe.mjs`) drives
`construct.html` over CDP. Every rule below was paid for with a real debugging session —
they are contract traps, not code bugs, and they bite again the moment they're forgotten.

## Shot flags — order of operations (`_shot.mjs`)

```
wait __snap → sleep(SETTLE) → --env/--ui/--torch/--pre → __snap (capture) → --eval → --full
```

- **`--eval` runs AFTER the capture.** It is a post-shot diagnostic channel, not a scene
  setup hook. Anything the image must contain goes in **`--pre`** (`--pre='__pad.stick(...)'`).
  Symptom of getting this wrong: "the scene didn't change" across 5+ shots — the evals were
  fine; the captures just never saw them.
- `--torch` flips `__snapTorch` for the capture only — the page's own L/`⏻` state is untouched.
- `--full` adds a composited DOM shot (dialogs/HUD) as `probe_<name>_full.png`; the default
  `__snap` captures the WebGL canvas only.

## Camera law — `__cam` is asynchronous

- `window.__cam(px,py,pz,tx,ty,tz)` **only sets `camLock`**; the rAF loop applies it on the
  next tick. `__cam(null)` releases back to body-follow. Only `__snap` applies a lock
  synchronously (for captures).
- **Headless rAF ticks are sparse.** Between two CDP evals the loop may not have run at all —
  "the camera is where I pinned it" is never true until verified.
- **Pin-then-poll**: after `__cam`, poll `__camera.position` until it matches the pin before
  asserting anything derived from the camera (pattern used in `_place_probe.mjs` §12.98):
  ```js
  for (let i = 0; i < 30 && !(await evl('Math.abs(__camera.position.x + 6.5) < .01')); i++) await sleep(100);
  ```
- **Verify the lane.** A pinned camera is only a known state if you know the first surface
  its ray hits. `z=-2.5` is *correctly* blocked by the primary mini's hit proxy; `z=-4.6`
  reaches the west wall face `x=-7.93` cleanly. An unexpected hit usually means the lane
  was wrong, not the ray.
- **Reload resets every camera/focus binding** — `camLock`, `padFocus`, `held` all die with
  the page. `localStorage` keys (`dvm_*`) persist; module state does not. Re-pin after reload.
- Boot spawn is `(0,.56,0)` with spawn yaw — pre-pin reads measure that view, not your lane.

## Eval-time raycasts — matrixWorld is stale

`position.set()`/`quaternion` writes do not touch `matrixWorld` — it refreshes on the next
render. A raycast in the same eval as a move hits the object's OLD spot (or nothing).
Call `updateWorldMatrix`/`updateMatrixWorld(true)` first — `getWorldPosition` does it for
its own object, which is why `aim()` calls it before `torchDist`.

## Assertion style — assert the law, not the value

Live-light values (`spot.intensity`, `fill.intensity`) are rewritten by `aim()` every frame
(distance-scaled since the auto-exposure pass). Value assertions only hold when the camera
is pinned to a `d≥1` lane — or assert the law: `intensity ≈ cfg.i * scale(dist(camera))`.
The same applies anywhere per-frame code owns a value a probe might read.

## Probe-hook API traps (learned the hard way)

- `__pad.page()` **reads** the page; `__pad.tab(id)` **writes** it. `__pad.page('lamp')`
  silently returns `'model'` — no error, just an `ok:false` shot.
- `__pad.stick(x,y,z,n)` mounts a wall pad; `n` is the outward face normal. The mount target
  must sit on the slab's inner face — annex south wall face is `z≈-5.93` (centerline `-6`,
  slab depth .14), not `-4.93`.
- `__torch` surface: `set/setOn` toggle, `aim(cam)` repoints+rescales, `dist(cam)` = the
  auto-exposure ray, `cfg` = the tune, `tune(k,v)` = `torchCfg`, `rst()` = reset.
- Exclusion lists are law: `held`, `selfBody.CH`, followers' `CH`, `ghost`, `aimDot`, `marks`
  never participate in aim/torch rays — "can't aim at your own hand" (a held pad in your
  face would dim the LAMP page's own preview otherwise).
- Scratch vars are named once: `_zv` is the shared zero `Vector2` (there is no `_zc`).

## Async boot — ordering, not timing

`ready.then` pins the order: pad emit → tombstones → `dvm_place` overrides (`o.y` restores
the saved seat) → `dvm_lift` lifts → **`await` all `t._mountP` mount promises** → `dvm_extra`
emits (`seatY:'auto'` reads tops) → fixpoint passes (`cap: p.y0+.01` reads tops). The mount
await is what keeps a slow stasset parse from capping a saved roof seat down to felt
(`known_issues/runtime/CONSTRUCT_MOUNT_PROMISE_TOPS_RACE.md`).

`waitBoot` gates on `__place && __warmDone`; `__warmDone` fires at the END of that sequence —
but page code between `location.reload()` and `waitBoot` resolving is still mid-flight:
`__pad.drop` against a `padRec` that doesn't exist yet throws inside `takeIt` and the
failure surfaces downstream as a wrong-value assert, not an error. After reload, always
`waitBoot` + settle before driving probe hooks.

## The meta-rule

**Debug the tooling contract before the scene.** When a shot/eval disagrees with the world,
read `_shot.mjs`'s flag order and the hook's signature before touching scene code — three
of this session's longest detours were correct code observed through the wrong window.
