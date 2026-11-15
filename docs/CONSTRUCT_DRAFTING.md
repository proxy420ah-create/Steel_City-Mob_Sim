# Construct drafting room — Primary Table + building miniatures (2026-11-16)

The west annex's exhibit layer: square low tables hosting architectural miniatures
of the game's real `.stasset` buildings. `MANIFEST.tables` is the expansion seam —
multiple drafting tables are list entries, not new code.

## The table

```js
{ id: 'primary', x: -4.6, z: -2.5, size: 1.2, top: .26, scale: 1 / 64, mv: 1,  // front-of-centre on the door axis
  models: [{ name: 'tenement_block_0', fill: .75 }],      // mounted exhibit — one model at a time
  catalog: [{ name: 'tenement_block_0', fill: .75 },      // the swappable universe — the pad cycles it
            { name: 'barber_shop', fill: .75 }] },
{ id: 'block', x: -6.4, z: -1.3, size: 1.2, top: .26, scale: 1 / 64, mv: 2,   // the Block Table — rear-left, .6m gap
  models: [{ name: 'block_26_tenement', fill: .75, slots: { 0: 'tenement_block_0' } }],
  catalog: [{ name: 'block_26_tenement', fill: .75, slots: { 0: 'tenement_block_0' } },
            { name: 'block_41_barber',  fill: .75, slots: { 2: 'barber_shop' } }] },
{ id: 'studio', x: -6.4, z: -3.7, size: 1.2, top: .26, scale: 1 / 64, mv: 1,  // rear-right — mirrors the block across the axis
  models: [{ name: 'tenement_block_0', fill: .75 }],      // opens identical to the primary — role TBD
  catalog: [singles + block specs] }                       // the union catalog — hosts either presentation
```

The **annex grew to fit the fan**: x[-8,-3] × z[-6,1] — +1m west (the rear
tables no longer clip the wall) and +1m each z side, symmetric so the door
axis stays the room's centreline. At that reach the annex spans the main
room's full z extent, so the two "west edge above/below the annex" wall
segments are absorbed — the shared wall is now only the two door stubs.
The rear pair sit flanking the axis with a 1.2 m slot between them.

- `emitTable(spec)` builds it in the Construct's own language: dark furniture slab
  (`0x14161c`, same stock as consoles/kiosk) on four **piston legs** — a fixed
  barrel (`SLEEVE_H` tall) + collar gland + telescoping machined rod (`0x2e3442`,
  unit-height so `scale.y` is the reach) — plus the **floor-wire seam
  lifted to the tabletop rim** — the same trick the doorway uses, `coreMat` hue-riding.
- **The pistons lift.** `tableLiftSet(t, lift)` / `tableLift(t, dy)` raise the
  whole assembly in `t.lift` (`[-.22, .4]`): slab, rim, rods re-pose, `mini` groups
  ride their pedestal, and every `tops` entry shifts by the delta — the measured
  surface follows the lift exactly. `liftRiders` then applies the same delta to
  any rec standing above the old top inside the footprint (`inB` +
  `y0 >= oldTop`), so stacked items ride the column while floor items beneath
  the table and wall-mounted pads stay put. `tableTop(t) = spec.top + lift` is
  the law everywhere a top is derived (mount, sync, seat). Persisted as
  `dvm_lift:<tableId>`; restored at emit before `mountModel`. At the bottom
  notch (`-.22`) the slab underside reaches `y=0` — the table sits on the
  floor; once the slab drops below the collar tops each leg group sinks below
  the floor line too, so the pistons read as fully retracting into the void.
- `spec.mv` is the manifest revision: `dvm_place` overrides apply only when the
  save's `mv` matches the item's — bumping it (an authored reposition like the
  r16 row layout) makes the manifest spot win over stale saves.
- Collision is a `WALL_BLOCKS` entry generated from the footprint (+.15 capsule) —
  exact square edge-slide, identical feel to the walls. The block is stored on
  `rec._block` and re-synced in `placeApply` (collision follows the drag, same law
  as solids); it is spliced on `del` and on the `dvm_del` tombstone reload path.
- The table is a `surf`-role `PLACED` rec (`table:<id>`) with a flat `tops` map at
  `top` — so every existing surface mechanic works on it natively: items E-place
  onto the felt through the same measured-column law as the desk.

## The miniatures

`emitBldg(table, model)` fetches `/Assets/StreamingAssets/voxel_buildings/<name>.stasset`
(raw bytes — the `/Assets/` static whitelist carries `.stasset` as octet-stream; the
game file is the single source, edits to the building show up on reload).

- **STAS parse in JS**: 16-byte header (magic, u16 dims W×H×D) + dense u16 grid,
  X-major — flat index `x + W*(y + H*z)`. Optional `SKEL` block after the grid
  carries building metadata/attachment points (parsed available, currently unused).
- **Surface-only instancing**: a voxel emits only when ≥1 of its 6 faces borders
  air — tenement 426,012 of 783,098 cells; barber 20,577 of 21,879.
- **Scale law**: `BLDG_VS = .1` is the in-game building voxel (VoxelChunkManager:
  buildings=0.1). Default model voxel = `BLDG_VS × table.scale` — **all models on one
  table share the table's `scale`**, buildings stay comparable. A model may instead
  declare **`fill`**: `vs = fill × table.size / max(W,D)` — the model's long edge spans
  exactly `fill` of the tabletop with a uniform margin (tenement `fill:.75` → .9m on
  the 1.2m table, .15m margin all sides, ~1:21). For a single-model easel `fill` is the
  honest contract — every model presents at its best size regardless of native dims.
- **Unity truth — X-mirror**: `.stasset` grids are authored under the left-handed
  Unity render (`VoxelBuildingMeshifier` maps grid→world unmirrored; the editor's
  UNITY VIEW is `scene.scale.x=-1`). A right-handed render must mirror **x** to
  match what the game shows — `emitBldg` maps `x → W-1-x` in both the instance
  layout and the tops write (same face stays toward the viewer, text un-reverses;
  a z-mirror would spin the facade around). Opt out per model with `flip:false`.
- **Palette**: `BLDG_MAT` mirrors `VoxelAssetStudio/mob_materials.py` (MUST match
  StAssetReader.cs); glass ids `BLDG_GLASS` {112,113,114,134,147} split into the
  `__glassMat` mesh — the same transparency contract as `voxEmit`.
- **Placement**: model group parents to the table group at `(m.x, top, m.z)`
  table-local — axis-aligned; models ride any table drag automatically.

## mountModel — the easel's swap verb

`mountModel(table, spec)`: unmount the current mini (unparent, dispose
instances + their cloned materials, **its tops columns revert to felt** via
the recorded `mini.written`), set `models=[spec]`, emit the new one.
`models[]` therefore always reads as *what's on the table now*.

`catalog` on the spec is the swappable universe; the **debug pad's MODEL
page** is its control surface (the original fixed wall panel was retired once
the pad proved the portable panel). Press → cycle the catalog →
`localStorage['dvm_model:<id>']` persists the choice — an F5 remounts whatever
was on the easel.

## Block composites — a whole city block on the table

The **Block Table** (`id:'block'`, rear-left of the primary — the slabs stand
in a fan: primary front-centre, block and studio flanking behind with a
walkable .6 m gap between the rows) mounts
**block specs** — catalog entries that carry `slots: {i: asset}` instead
of a lone `name`. `emitBlock` builds them: the spec's nine lots are a 3×3 grid
of 64-voxel tiles (`slot i → (i%3, i/3)` — the same `c = i % cols, r = i / cols`
law `CityMap3D` uses), stamped into ONE composite grid that mounts as an
ordinary mini. Spin/slice/tops/lift/persistence need nothing new.

**The composite IS a game terrain chunk** — `emitBlock` ports
`VoxelTerrainBuilder.GeneratePerBlockTerrain` 1:1 at voxel truth, so the
miniature carries its streets, not just its buildings:

- **322×322 grid** = 192 interior + a **65 v margin per side**: 45 v road-half
  + 20 v sidewalk ring — each block owns half its surrounding roads (the
  game's shared-half law; two adjacent chunks' cobble stripes read as the
  dual lane lines at the road's quarter points).
- **Road-half ring**: asphalt (104) with a 1 v cobblestone (105) stripe
  centered in each half — horizontal bands painted first, vertical strips own
  the corners, the builder's own order. `m.seams {n,e,s,w}` overrides an edge
  to `'mainstreet'` (plain asphalt boulevard, no stripe).
- **Ground tile**: sidewalk ring (102) + stone plot (101), flat material
  bands at grade, **2 v thick**; `m.lanes` (bits N=1 E=2 S=4 W=8) cuts asphalt
  curb mouths at the center-32 v of flagged edges and paints the interior
  lane cross-section (sidewalk 16 | asphalt 32 | sidewalk 16) — the alley
  catalog machinery, dormant until a lane spec exists.
- **Anchor-top law**: lots stamp at y+2, on the terrain slab — exactly the
  game's `terrainTopY` anchor.
- **Explicit slots** stamp their asset at `(i%3)*64, (i/3|0)*64` inside the
  interior; a multi-slot asset (the tenement's 192 span at slot 0) marks
  every lot it covers.
- **Vacant lots take `empty_land`** — the game's own 64×8×64 vacant-lot tile
  (cobble ground, rubble, perimeter fence posts), one per empty slot, so the
  block reads exactly like `city_layout`'s `block_41` in Unity.
- **Consistent footprint law**: `fill:.75` keeps the .9 m tabletop span —
  streets now share it, so block-table buildings render at 1:17.9 vs the
  easel's 1:10.67 (the easel's `BLDG_VS` is the live .05 voxel — the old .1
  claim was stale). The street ring is a measured surface: items seat on the
  road/sidewalk at .2656 (felt +2 v).
- `stassetFetch` caches parsed volumes per name — a block's 9 slots share
  fetches; `emitVox` is the shared instance/tops/mini emitter for both single
  mounts and composites.
- **Miniature voxels never raycast — but aim lands on their proxies.** Every
  mini `InstancedMesh` is flagged `userData.miniVox` — a mounted tenement is
  426K instances and three.js matrix-tests *each* one per ray, so the un-pruned
  center-aim raycast turned RMB-hold + walk into a slideshow (~1.64M instance
  tests/frame). `rayPruned` collects ray targets with instanced subtrees
  skipped: `allInst` (the per-frame `aimStep` — aim wants surfaces) or
  `miniVox`-only (`placePick`/`placeSpot` clicks — ordinary instanced props
  stay selectable). Skipped objects keep an **aim surface** via `hitProxy` —
  an invisible 12-tri AABB child (props, minis, rigs) collected only in
  `allInst` mode, so the aim dot/IK land ON the thing you're pointing at.
  The collector also skips `!visible` objects and `isLine`/`isPoints` helpers —
  a `GridHelper` answers every ray near its lines at distance ≈0 and once
  pinned `aimWorld` to the floor. `boundsG` is **not** excluded: its invisible
  wall slabs (`userData.wallSlab`) are real physical surfaces — they block the
  capsule and host the pad — so the aim stops at them instead of sailing into
  the void. The decorative children (floor seams, portal ∩ wires) carry
  `userData.noAim` and stay invisible to the ray — the flag, not the group,
  owns the exclusion so `rebuildBounds` re-marks them on every rebuild.

## The debug pad — a pickable touchscreen

`emitPad()` builds `debug_pad` as a procedural `PLACED` rec (no voxel asset — the
law is rec-driven): a `.18×.012×.12` slab, `EdgesGeometry` trim in `padEdgeMat`
(hue-rides via `boundHue`), and a `CanvasTexture` screen on the +y face. It
spawns on the Primary Table's north felt margin (`-4.6, -1.975`, `source:
'manifest'` → position persists through `dvm_place` like any manifest item),
`pickable` — E-takes, E-places, and all held-item rotation keys work on it.

**Regions are data.** `PAD_REGIONS` are `{rect(px), label, glyph, fn}` entries
hit-tested through raycast `uv` on the screen plane — the face is software, so
future tools are draw code, never new geometry. One region table feeds two
input modes:

- **Held → RMB tap frees the cursor** (`padFocus`: `exitPointerLock`,
  `mousemove` raycasts through `mouseNDC` → `padCursorHit` → hover highlight,
  LMB presses). RMB tap again (or Esc, or entering place mode) re-locks the
  look. WASD still moves you; E still places the pad. The click-to-lock canvas
  handler is gated `!padFocus` so presses don't re-engage.
- **Placed → center-aim press** (`padProbe`, same law as `kioskProbe`):
  raycasts the screen within 1.8 m → `uv` → region → `{isBtn, panel:'pad'}` rides
  the `promptIt` chain, so aim + E (or LMB) presses it like any other face.
  Skipped while the pad is held — you can't aim at your own hand.

Probes are unconditional (`kb || db`) so canvas hover state can't freeze;
ghost wires gate per-panel (`btn.panel`). MODEL page drives `mountModel`.

**The pad is a remote for its nearest table.** `padTable()` resolves the pad's
position (held → your hand; placed → where it sits/hangs) to the closest table's
rec — every MODEL/ROT/SLICE/LIFT verb drives THAT table. Each page title reads
its target (`TURNTABLE — BLOCK`). Rest the pad on the block table's felt and it
drives the block; carry it back to the primary and it drives the primary —
the same pad is both tables' panel.

## Pad pages — the registry, tabs, and the ≡ menu

The face is paged, and **a page is data**: `PAD_PAGES` is an ordered registry
`{id: {tab, draw(g, ctx)}}`. The tab strip **generates itself** from the
registry — `MODEL | ROT | SLICE | LIFT | ≡` — and the trailing `≡` opens the
APPS menu, a launch tile per page so the strip never crowds as tools grow.
`padPage` state + `padUvRegion`/`padDraw` scope regions to `r.tab || r.page ===
padPage` — a page switch costs a `padDraw` and nothing else. New tools are a
`PAD_PAGES` entry + optional `{page: id}` regions; the chrome never changes.

- **LIFT — the piston page.** Two speeds each way (`▼▼ ▼ ▲ ▲▲` = ∓.08/∓.02)
  drive `tableLift(padTable(), dy)`; `RESET` homes `lift` to 0. The readout
  shows `TOP …m · LIFT +…` live — raise the table past eye level (.55) to look
  up at the exhibit. The pad itself rides the lift when it's standing on the
  table — the remote travels with the machine it drives.
- **ROT — the easel turntable.** Four speeds-as-buttons (`◀◀ ◀ ▶ ▶▶`, ±.9/±.25
  rad/s): `miniSpinSet` toggles — press a rate to spin, press the lit rate to
  stop, press another to change. `RESET` (`miniRotReset`) homes it: spin 0,
  `rotation.y` 0, exact tops. The render loop integrates `rotation.y` and
  re-projects tops every ~.02 rad (centre columns hold through the spin); an
  exact `miniSyncTops` runs on stop/reset.
- **SLICE — the editor's xyz window, same contract.** `mn.slice` holds per-axis
  index ranges; `miniSyncSlice` writes `uVoxClipMin/Max` into the model's
  cloned materials (the editor's own shader patch — instance origins vs a
  local-space box, mirrored-x handled through `fx`). `miniSliceStep` nudges the
  active bound in `dim/16` steps, clamped against its twin; `RESET` restores.
  Slicing is truth, not just draw: `miniSyncTops` re-derives each column's top
  as the tallest voxel inside the window (per-column `ys` lists captured at
  emit) — cut the roof off and measured seating drops to the exposed slab.
- `padMini()` reads the mounted model's `userData.mini` — buttons draw `on`
  state (active tab / active axis / live spin rate) so the screen always shows
  what's true. Slice + spin are session-scoped, per model instance — a fresh
  mount resets both.

## The L-key inspection torch

Same rig as the Commission's: `__torch` = a `SpotLight` (warm `0xfff0d0`, cone
.45, penumbra .55, distance 10) + a faint `PointLight` fill, parked at
intensity 0 until L toggles `set(on)` → spot 7 / fill .9. `aim(cam)` repoints
it before every render — it rides the camera object, so the main view, the X
fly-cam, and the follower POV (`__eyes.pip`/`shot`) all light where they look.
`__snap` honors `__snapTorch` (`_shot.mjs --torch`): flips it on for the
capture, restores after — screenshot illumination without a session flag.

## The pad sticks to walls — the movable panel

A held pad detects vertical faces: `dropTarget` asks `padWallTarget`, which
runs the same pruned aim-family raycast (`rayPruned`, proxies live) — **the
wall must own the ray**: the FIRST surface hit must be a `wallSlab` within
2.4 m. A nearer felt/mini/floor wins the drop instead — aiming at the annex
tables can't hijack the pad onto the wall slab behind them. When the slab
does own it, `{wall:true, n:[outward normal]}` returns
(the hit face's world normal is flipped toward the camera side, `y` forced to
0 — vertical faces only). The ghost ring sticks to the face
(`quaternion.setFromUnitVectors(+Y, n)`) so you see the mount before E.

`placeIt` gains a mount branch: `t.wall` → `p.mount = {x,y,z,n}` and
`padMountQ` sets a full basis quaternion — the pad's local `+y` (its screen
normal) maps to the wall's outward normal, `+z` maps to world down, so the
screen reads upright off the face. `p.y0` = mount height; `x/z` = the wall
point. Unstick paths: `takeIt` (pickup), `placeApply` (drag/arrows/rotate —
any move), `B` re-seat (drops to the floor surface at the wall's foot,
rotation reset). PgUp/PgDn keep `mount.y` synced — sliding the panel up/down
the wall is an emergent feature, not a bug. Boot override restores `mount`
(`padMountQ`, skipped by the re-seat fixpoint — nothing pulls a stuck pad
down), `placeSave` writes `mount` into `dvm_place`, and undo restores
`prev.mount` (stick→stick→undo returns to the first wall). A mounted pad
still aim-presses — it's literally a wall panel you carry in and hang.

## Models join the surface truth

During the grid scan `emitBldg` tracks each (x,z) column's `ymax`, then writes
`top + ymax*vs` into the **host table's** `tops` map (5mm columns, max-kept on
collision). Consequences, all measured — never authored:

- the building's roof, parapets, and low sidewalk strips read as *different*
  drop heights (probe-verified: core .4625, front parapet .44375);
- the tenement's open rear yard writes nothing → items seat on the felt .30
  *through* the yard's air — the tabletop IS the yard's floor;
- you can E-place an item on a building miniature's top surface today.

## Current status / next

- One table, swappable exhibit — `emitTable`/`emitBldg`/`mountModel` + the wall
  panel are the whole contract — more tables = more `MANIFEST.tables` entries
  (each can declare its own `catalog`); more buildings = more catalog entries.
- `tenement_glass.stasset` (solid-glass blast testbed) is loadable through the
  same path — just append a catalog entry.
- Not yet: model rotation in spec (tops write is axis-aligned), the barber's
  `itemParts`/attachment-point display (door leaves, barber pole), any actual
  authoring/editing tool — the table is the surface those tools will sit on.
