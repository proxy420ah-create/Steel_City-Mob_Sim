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
- **Args are `--cam=`/`--look=` equals syntax, not positional.** `_shot.mjs` calls
  `(arg.cam || "0,3.1,4.9").split(...)` — a bare or object-shaped camera value crashes
  with `TypeError: ...split is not a function` before the page even opens.

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
- `__torch` surface: `set/setOn` toggle, `aim(cam)` repoints the rig — that is ALL
  it does; intensity/cone/color are the tune verbatim at every range. `cfg` = the
  tune, `tune(k,v)` = `torchCfg`, `rst()` = reset. The `ease`/`hit`/`inc`/`dist`
  levers died with the auto-exposure stack — old probes calling them return
  undefined (or throw on `.dist()`/`.hit()`).
- `__place.fly(x,y,z)` pins `flyPos`; `__place.look(yaw,pitch)` writes the yaw/pitch
  vars the loop stamps into `camera.rotation` in place mode. **`camLock` wins over
  both** — `__cam(...)` earlier in the suite keeps stomping the camera until
  `__cam(null)` releases it. Free-cam checks fail silently otherwise.
- Exclusion lists are law: `held`, `selfBody.CH`, followers' `CH`, `ghost`, `aimDot`, `marks`
  never participate in aim/torch rays — "can't aim at your own hand" (a held pad in your
  face would dim the LAMP page's own preview otherwise).
- Scratch vars are named once: `_zv` is the shared zero `Vector2` (there is no `_zc`).
- `placed` rows in `_live.json` are `[name, x, z, y0, source]` — **emit coords, not
  world**: for `_parked`/parented recs that's the parent's local frame (a parked
  car's `x,z` reads as composite-cell fractions, meaningless in world space).
  Read world position with `p.g.getWorldPosition(v)` — the same frame `surfaceAt`
  uses. This misread once "proved" a centered car displaced.
- `__scanN` counts host column-scans per drop-march — it legitimately grows when
  async mounts (`_parked` cars) register mid-run; parented recs reject by the
  parent's frame, so their count should hold near zero unless the march is
  actually over a miniature.

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

**Poll budgets scale with mount size.** `models[]` is *empty* until `_mountP` resolves —
the Battlefield zone needs ~7 s to emit (headless `setTimeout` yields throttle further),
so a 20 s poll that sufficed for the block table died on `JSON.parse(undefined)` at the
zone's first snapshot. Poll `models.length` (not a fixed sleep) and budget for the
largest mounted model, not the typical one.

## Surface assertions — field-scan + contrast, not single points

Miniature peaks are authored, not centered: the tenement's 122 v peak lives on a thin
perimeter parapet — a centered 3×3 sample read `.359`, not the spec max. Assert a
**footprint-wide max scan** for "is the tall thing there." Conversely, some features are
*flat by design*: the alley lane is ground-level — its identity is **contrast** (lane
column `.2619` asphalt vs adjacent lot `.2656` plot), not a height. Single-point absolute
asserts on authored content are fragile twice over — they miss off-center peaks and they
invent heights that were never promised.

## Flake discipline — rerun before believing a fail cluster

Headless mouse-event timing produces transient clusters: one run failed five early
drag/paste checks that passed identically on the next run with zero code changes. When a
cluster (a) sits in code the session didn't touch and (b) passed on prior runs, rerun
once before treating it as real. A *persistent* identical failure is a bug; a flaky
cluster is the headless event pump, and chasing it as scene code wastes the session.

## Probe tooling runs on Node's native WebSocket

The CDP tools (`_place_probe.mjs`, `_shot.mjs`) use **Node 24's built-in `WebSocket`** —
no `ws` package is installed. Scratch scripts that `require('ws')` die at import; copy
the native-socket pattern from the existing tools.

## Suite duration — long ≠ stuck, and how to run it

The full suite does ~6 page reloads, and each boot re-emits the ~7 s zone plus the
warm-up's unculled render — all in software GL. A silent stretch mid-run is almost
always a reload in flight or the zone emitting, not a hang: PASS lines resume when
the boot lands. Budget several minutes; treat "stopped waiting" as a reader timeout,
not a probe result.

**Run it to a file + a background watcher** (the same ear pattern the commission
uses for seat relays): `node _place_probe.mjs > _probe_out.txt 2>&1`, then spawn a
background subagent that polls the file every ~20 s for the `ALL PASS`/`FAILURES:`
sentinel and reports the tail + counts. Blocking shell reads cap out well under the
suite's runtime — the file is the record, the wake is the doorbell.

## Heavy scenes — SwiftShader black frames

Once the studio table mounts the ~1.36 M-instance Battlefield zone, headless
`_shot.mjs` interior captures can return `ok:true` with a **black PNG**, and
`--eval` can time out to `undefined` — software GL spends seconds per frame,
so the canvas presents nothing fresh and CDP evaluates stall. The exterior
room shot still rendered (cheap frustum); interior angles went black at every
pre-wait up to 45 s. This is a **tooling limit, not scene truth** — the probe
suite's own eval channel (all numeric `__construct.tables()` assertions)
passed cleanly in the same runs. For content verification under load, render
the data instead of the scene: a top-down material map straight from
`city_layout.json` + the stassets (`docs/zone_topdown.png`) shows the 9-block
grid, roads, barbers, lane band, and the tenement footprint. On real GPU
hardware the in-scene zone renders normally.

## The meta-rule

**Debug the tooling contract before the scene.** When a shot/eval disagrees with the world,
read `_shot.mjs`'s flag order and the hook's signature before touching scene code — three
of this session's longest detours were correct code observed through the wrong window.
