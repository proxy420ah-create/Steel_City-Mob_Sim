# Construct Placement Law — `docs/construct.html`

**Status**: 🔒 LAW | **Updated**: 2026-11-16

The measured-surface contract for every asset placed in the Construct. This doc is
the authority for how things sit on things — if placement code and this doc disagree,
the doc wins and the code gets fixed.

---

## 1. The PLACED record

`emitAsset(name, opts)` is the only way an asset enters the room. Every emit pushes
one record:

```js
{ name, kind, g,                 // asset id, ROUTES key ('item'|'vehicle'|'char'), THREE.Group
  tops, lb,                      // LOCAL column-top map (5mm keys) + local bounds
  x, z, rotY, y0, minB, vs,      // world transform: center xz, yaw, bottom-seat y, voxel bottom, voxelSize
  surf,                          // ROLE — see §2
  bbox,                          // world AABB footprint — footB(lb, x, z, rotY), kept LIVE by placeApply
  pickable, solids,              // E-takeable? / collision boxes
  source }                       // 'manifest' | 'extra'
```

`surfaceAt(host, wx, wz)` transforms a world point into the host's local column map
(through `g.getWorldPosition`/`getWorldQuaternion` — **world transform, never rec x/z**,
so parented assets like the car inside `carG` don't ghost their map at the origin)
and returns the highest column top near the point (±.07m scan radius).

## 2. Surface roles — `surf` is declared, never inferred

> BOSS law: *"tag these for specificity so we always know what's a surface and what rides surfaces."*

```js
surf = o.surf ?? (SURF_TYPES.has(m.assetType) || !o.pickable)
SURF_TYPES = { furniture, vehicle, room, building }   // voxel-editor department tag
```

| Role | Members | Rule |
|------|---------|------|
| `surf:true` | desks, door, doorframe, car, furniture, walls | **Floors only on other `surf` recs** — riders are invisible to its floor query, at emit and at re-seat |
| `surf:false` | gun, book, lamp, stamper, any pickable prop | **Conforms** to the highest top beneath it (surfaces, floor, or stacked riders) |

Rationale: hand-carryable (`pickable`) means rider by definition — a desk is never
E-taken, so `!pickable` marks it furniture even when its JSON `assetType` still says
`'prop'`. `o.surf` is the explicit override when a caller knows better.

## 3. The two enforcement levers

`placeSurf(wx, wz, skipSet, opt)` / `worldSurfaceAt(wx, wz, skip, opt)`:

```js
opt.surfOnly   // true → candidates filtered to rec.surf — furniture can never land on a rider
opt.cap        // tops above cap don't count — settle-only re-seat, nothing ever climbs
```

**Where each applies:**

| Site | Call | Effect |
|------|------|--------|
| `emitAsset` `seatY:'auto'` | conform + role | extras land on top of what's there; furniture ignores riders |
| Boot fixpoint re-seat | `surfOnly: p.surf, cap: p.y0+.01` | every item re-derives height on the final layout — **settle-only, descending order** |
| `B` re-seat | `surfOnly: p.surf` | deliberate re-seat conforms within role |
| `/cmd move` | `surfOnly: p.surf` | agent move seats deliberately |
| `settleOrphans` | `surfOnly: q.surf` | orphan drop also can't land furniture on a rider |
| `placeSpot` (drag ghost) | `surfOnly: sel.surf` | the preview obeys the same law |

**The bug class this kills** (all observed live): a desk emitting onto a floating
passenger's stale top; a desk re-seating onto the grounded passenger's own height;
door↔frame leapfrogging +.85m per commit; any host ratcheting on its own cargo.
A rider top can never host furniture (`surfOnly`), and no top can ever be `≤` an
item's bottom while being inside it (`cap`) — both failure modes are impossible by
declaration, not by tuning.

## 4. Boot restore order

`ready.then`:

1. **Tombstones** (`dvm_del`) — deleted manifest items removed first (a dead host
   must not seat anything).
2. **Overrides** (`dvm_place`) — `placeApply` lands manifest items at saved x/z/rotY.
   *Saves carry no `y` — height is re-derived, never trusted.*
3. **Extras** (`dvm_extra`) — emit with `seatY:'auto'` → conform onto the final layout.
4. **Fixpoint re-seat** (≤4 passes, descending y0) — every item settles:
   `surfOnly: p.surf, cap: p.y0 + .01`. Hosts correct first; passengers see the
   corrected tops. Converges when a pass moves nothing.
5. `refreshSolids()`, warm-up compile, `__warmDone`.

## 5. Persistence keys

| Key | Contents |
|-----|----------|
| `dvm_place` | `{name: {x,z,rotY}}` — manifest overrides (no `y` — heights re-derive) |
| `dvm_extra` | `[{name,kind,pickable,solids,x,z,rotY}]` — pasted/agent-emitted items |
| `dvm_del` | `[name]` — manifest tombstones |
| `dvm_fol` | follower roster (CHAIR survives F5) |
| `dvm_live` | `'1'` → live bridge auto-resumes after reload |

`placeSave()` writes all three after every completed move/drop/paste/delete/undo.

## 6. Held items — carry + orientation

`takeIt(p)` lifts a `pickable` rec; the group rides `.38m` ahead of the camera
each frame. `dropTarget()` marches the aim ray (`.08 → 2.4`, `.06` steps) until
`py ≤ surface+.02` — that march runs `placeSurf` per step, so:

- **Perf law**: `placeSurf` rejects by `p.bbox` before any column scan
  (`.12` pad covers the `.07` disc + `.05` footprint pad). Parented recs
  (`g.parent !== scene`, e.g. the car in `carG`) are exempt — their bbox is
  parent-local. `tops` keys are **numeric** (`TOPK(ix,iz)`) — string keys in a
  per-frame hot loop are GC churn. The pre-reject march ran ~39 steps × every
  host's ~841-cell scan with string concats — that was the carry-walk hitch.
- **Orientation law**: `held.g.quaternion = qYaw(facing) × qSnap × qFree`.
  - Axis snaps: 15° counters per axis — `, .` yaw · `[ ]` pitch · `; '` roll.
    A snap press clears `heldFree` — **axis rotation resets free rotation**, so
    level is always reachable.
  - `0` = LEVEL: all counters + tumble to identity → exactly authored-upright.
  - `G` + mouse = free trackball tumble (world X/Y); camera look is suspended
    while `G` is held.
  - `placeIt` persists `rotY = yaw + snapYaw` (normalized); pitch/roll drop —
    **placed items always seat level** because `tops`/`bbox` are authored-
    upright maps. Tilted persistence is a future contract, not a flag day.

## 7. Invariants — testable law

1. Door and doorframe are `y0 === 0` forever.
2. A `surf` item is never supported by a `surf:false` item.
3. Re-seat never raises an item (emit/place/move may conform up — re-seat may not).
4. A rider on a desk at `y0 = desktop` stays; a rider with no support drops to `0`.
5. Deleting a host drops its passengers (orphan settle, `oldTop`-capped).
6. `p.bbox` always equals `footB(p.lb, p.x, p.z, p.rotY)` — `placeApply` maintains it.
7. A held item's `0`-key pose has exactly zero pitch/roll (identity × facing yaw).
8. `dropTarget()` never column-scans a host whose footprint doesn't contain the
   sample point (parented recs exempt — their map is queried in world space).

Regression harness: `docs/_place_probe.mjs` (stages the exact cascade — floating
manifest passenger over an extra host's footprint — plus role assertions, the
drop-march scan budget, and the held-rotation snap/tumble/level contract).

---

*Related: `RECENT_CHANGES.md` — "Surface-role law" entry; bug family doc:
`known_issues/runtime/CONSTRUCT_SURFACE_CLIMB_FAMILY.md`.*
