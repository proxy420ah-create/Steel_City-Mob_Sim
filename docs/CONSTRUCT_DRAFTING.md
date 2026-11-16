# Construct drafting room — Primary Table + building miniatures (2026-11-16)

The west annex's exhibit layer: square low tables hosting architectural miniatures
of the game's real `.stasset` buildings. `MANIFEST.tables` is the expansion seam —
multiple drafting tables are list entries, not new code.

## The table

```js
{ id: 'primary', x: -4.1, z: -1.1, size: 1.2, top: .26, scale: 1 / 64, mv: 2,  // south flank of the door approach
  models: [{ name: 'tenement_block_0', fill: .75 }],      // mounted exhibit — one model at a time
  catalog: [{ name: 'tenement_block_0', fill: .75 },      // the swappable universe — the pad cycles it
            { name: 'barber_shop', fill: .75 }] },
{ id: 'block', x: -4.1, z: -3.9, size: 1.2, top: .26, scale: 1 / 64, mv: 3,   // the Block Table — north flank of the approach
  models: [{ name: 'block_26_tenement', fill: .75, slots: { 0: 'tenement_block_0' } }],
  catalog: [{ name: 'block_26_tenement', fill: .75, slots: { 0: 'tenement_block_0' } },
            { name: 'block_41_barber',  fill: .75, slots: { 2: 'barber_shop' } }] },
{ id: 'studio', x: -6.5, z: -2.5, size: 2.4, top: .26, scale: 1 / 64, mv: 2,  // dead-centre — the ZONE tier centerpiece, 2× the others
  models: [{ name: 'battlefield', fill: .75, zone: {...} }],                 // the Battlefield 3×3
  catalog: [zone + singles + block specs] }                                  // the union catalog — hosts either presentation
```

The **annex grew to fit the centerpiece**: x[-10,-3] × z[-6,1] — +2m west so
the **2.4m zone table** can sit dead-centre `(-6.5,-2.5)` with ~2.3m of
inspection aisle on every side. The two 1.2m tables flank the door approach
at x=-4.1 (primary south at z=-1.1, block north at z=-3.9) — a 1.3m channel
on the door axis between them, so you enter *between* the satellites facing
the big table dead ahead. The zone's `fill:.75` exhibit now spans 1.8m — the
966-cell composite draws at ≈**1:27** (was ~1:54) — proper architectural
model scale; same voxel count, only the cell size doubled.

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
  the 1.2m table, .15m margin all sides, ~1:21; the 2.4m studio table doubles it —
  zone `.75` → 1.8m ≈1:27). For a single-model easel `fill` is the
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

The **Block Table** (`id:'block'`, north flank of the door approach — the
slabs stand in a satellite arrangement: studio/zone dead-centre at 2.4m,
primary + block flanking the door channel at 1.2m) mounts
**block specs** — catalog entries that carry `slots: {i: asset}` instead
of a lone `name`. On a `ring` table (the block table), the exhibit is TWO
minis: **`emitRing`** paints the street diorama once — the BLKF ground
(roads, curbs, sidewalks, the empty interior pad) with no lots — and
**`emitCore`** stamps only the model's 3×3 lots into the 192-cell interior
that seats inside the ring (same center; `_vs` pins the ring's voxel size
so cells align 1:1). `mn.static` keeps the ring out of `mountModel`'s
unmount sweep — **swaps replace only the core; roads and parked cars are
table props, never re-emitted.** The ring carries `_road = {}` so every
curb/lane/slot law runs the deterministic band constants — no per-model
road scan, no model-dependent parking variance. (`emitBlock` — the
single-composite path — survives for ring-less hosts: the studio catalog
can still mount a block composite with its own streets.)

**The composite IS a game terrain chunk** — `emitBlock`/`emitRing` ports
`VoxelTerrainBuilder.GeneratePerBlockTerrain` 1:1 at voxel truth, so the
miniature carries its streets, not just its buildings:

- **The seam law: each tile owns HALF its surrounding roads** (45 v band +
  20 v sidewalk = 65 v margin → tile 322). Two adjacent chunks' stripes read
  as the dual lane lines at the road's quarter points — that's how Unity's
  streets get TWO lines and why `emitZone`'s interior seams already look right.
- **…but a lone block exhibit has no neighbor — `emitBlock` paints the far
  half itself** (`BLKF`: band 90 v, margin 110 v → **412×412**): far half +
  seam + our half + far curb at the rim (no far sidewalk — that's the
  neighbor's). `fillBlockGround`'s `B` param carries the constants; stripes
  are `z===22 || z===ROAD−23` — each half-center. `emitZone` keeps strict seam
  law (perimeter = the city cut). `mn._blkM` records the margin so
  `roadProf`/`blockTrf`/`mountParked` read either geometry; `roadProf` scans
  INWARD from the rim for the first raised run (the far half is flat — an
  outward scan would never reach the curb). On the ring table the scan is
  dormant — `_road = {}` is pinned and every consumer runs the band law; it
  still serves a lone `emitBlock` composite on a ring-less host.
- **Road band**: asphalt (104) with 1 v cobblestone (105) stripes at each
  half's center — horizontal bands painted first, vertical strips own
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
- **`parked` — the street scene is the TABLE's, not the model's.** Roads,
  sidewalks and parked cars are the block table's street furniture — swapping
  the lots (tenement ↔ barber) must not change the streets. So `parked` lives
  on the table spec (`t.spec.parked`) and mounts **on the static ring once,
  forever** — a swap can't remount, orphan, or re-seat them; the cruise even
  laps through mid-cruise swaps (cars never leave `loop`). Each `{edge,along,name?}` mounts `vehicle_490_touring` (the
  game's touring car) as a **full-fidelity overlay** — `emitAsset` at
  `vs × voxelSize/BLDG_VS`, so every authored 1 cm voxel renders, just drawn
  at the composite's scale — never downsampled into the grid. Position law
  (`ParkingMap.Build` in composite cells): `parkOffset = roadWidth/2 − carW/2`
  off the lane center **plus the curb push — Unity's `extraParkOffset = 0.25f`**
  (`PARK_PUSH`, 5 cells — `s.pos = station + side·(parkOffset + extraOffset)`)
  lands the car center `50v − carW/2 + 5v` off the block boundary: curb-side
  wheels **overhang the curb face onto the sidewalk edge**, the game's parked
  stance — dead-flush reads as mid-lane (the road recipe is flat to the
  property edge; `roadProf` scans
  each edge's property-line strip out of `localCols` tops and would park
  against a real curb's lane-side edge — an empty scan keeps the bare-band
  law). The real footprint comes from the vehicle manifest (`dims ×
  voxelSize / BLDG_VS`), so any named vehicle parks by its own size. Cars face
  the **rendered-RHT heading** — `PARK_EDGE`/`blockTrf`'s law is authored as
  the mirror-inverse of the raw cell legal set (the `fx` x-mirror flips
  chirality: a mirrored right-hand law renders left-handed — the cell-space
  headings are what *looks* legal on screen, not what reads legal in data) —
  and clamp inside the `KeepClear` 40v
  no-park margin off the corner intersections. The seat is `2·vs + 1.5·vvs` —
  plate top plus **a car-voxel and a half**: flush reads as *sunk* (the road
  silhouette clips the lowest wheel row), and even half a voxel still merges —
  at grazing angles the black tire ring dissolves into the road's dark top
  face. A voxel and a half of air lets the entire ring draw against the road
  surface, which reads as planted (and the hairline under the tires doubles as
  a contact shadow). Cars parent to
  `miniG` → they ride spin/lift/model-swap for free (unmount splices their
  `PLACED` recs, a mid-fetch swap abandons the pending mount); meshes are
  `miniVox`-tagged and the `hitProxy` rides the car's own group so the aim
  surface drives with it. Each is a real PLACED host — `surfaceAt`
  reads its own `tops`, so items seat on roofs/fenders (roof ≈ .3058 on the
  .2656 road). **But `_parked` recs are scenery, never place citizens** —
  their `x/z` are miniG-LOCAL: `placeSave`, override apply, tombstones and
  the agent move/del name-resolution all skip them (a parked car's local
  coords once wrote a name-shared `dvm_place` override that stamped onto
  the spinner — `PARKED_REC_NAME_OVERRIDE_DISPLACEMENT.md`). The block
  table carries two — south street `along` 160, west street `along` 140.
- **`CARS` (MODEL page) — the curbside cruise.** `blockTrf` laps the table's
  parked cars around the block: pull out of the slot, circulate the
  right-of-travel lanes, pull back in and restore the exact `_slot` pose.
  The circuit is the MEASURED street — `roadProf`'s per-edge band then the
  lane at **half-road center** (`roadWidth/4` ≈ 22.5 v off the street
  centerline — each tile paints its half's cobble stripe there, so the stripe
  IS the lane line; fallback band − 18 when no curb is stamped) plus
  quarter-arc corners; the game's road files carry no lane or direction data,
  so the lanes are the part we derive — on the ring table, off the band
  constants (the ring's `_road = {}` → `margin − SIDE − HALF` lands on the
  painted stripe); a lone composite scans its own tops via `roadProf`. Cruise
  direction is **rendered right-hand traffic** — cell space circulates s→e→n→w
  counterclockwise with `sgn:−1` arcs so the `fx` mirror draws it clockwise
  with right turns (the naive RHT cell law rendered left-handed: cars faced
  east/south on their curbs — the mirror flips the *chirality* of the rule,
  not just its directions). Cruise
  speed is the real `VehicleTestSpawner.driveSpeed` = **3.0 m/s** (60 cells/s,
  scale-independent). Heading law: the touring's authored front is **+z**
  (Unity `VehicleAgent` drives `Quaternion.LookRotation(dir)` with no axis
  offset) — `rotY = atan2(worldHx, worldHz)`; the old −z law ran every car
  tail-first, parked and cruising alike.
  **The maneuvers are Unity's diagonal law** (`RequestDepart`/`TrySpliceParking`,
  `lead = 1.2 m`): depart lerps slot → `lanePos + dir·lead` (a ~1.4 m diagonal
  AHEAD of the slot); park splices at `lanePos − dir·lead` then diagonals into
  the curb. Yaw chases the current leg's own direction at `Slerp ~6/s` — the
  car visibly angles out/in, then `snap` squares it to the slot heading (same
  as `FinishPark`). Maneuvers run at drive speed (`vc`, not a creep), and
  **every car departs on the same tick — no stagger** (one keypress, same as
  F10). The old hook (merge a car-length BEHIND the slot, slot heading held,
  1.6 s serial delays) read as a slow sideways crab-walk. A gap clamp
  holds .08 m spacing between cars on the loop.
  **Parking is Unity's park-anywhere law** (`ParkingMap`/`NearestFreeAhead`,
  not a return-to-origin): `slotInv` builds a fixed slot lattice per edge
  curb — `carLength + .5` pitch (≈ 2.0 m) inside the keep-clear margins, curb
  position off the same `curbC` law the mounts use — measured strip edge
  when the model carries a raised sidewalk, else the band's inner boundary
  (flat-ground composites like `block_41_barber` emit no raised cells for
  `roadProf` to find — `st` missing is a model property, not an error, and
  the lattice still exists). Toggling CARS
  off doesn't send cars home — each cruising car CLAIMS the free lattice
  slot whose pull-in approach is nearest ahead on its path (`claim` —
  forward wrap distance; a claimed-by flag serializes same-tick picks, and
  `held` counts physically-occupied slots: a mid pull-in rec holds its
  destination, a departed slot is already free). On arrival the car splices
  the diagonal into that slot and `snap` ADOPTS it — `_slot`/`_slotC` are
  rewritten to the claimed pose, so a later re-arm departs from wherever it
  actually parked, and scenery slots are just lattice members that happen
  to be occupied. An all-full curb keeps the car cruising (it keeps seeking,
  same as Unity); an empty/failed lattice falls back to the car's own origin
  slot so a looping car ALWAYS has a legal target. **The CARS press is
  global** — `toggle` finds whichever table's current mini actually parents
  `_parked` recs and ignores the caller's table guess (`padTable()` nearest
  can resolve a car-less table while you stand between them; a wrong-table
  call used to rebuild the circuit on the wrong model and orphan the cars to
  `gone`). Cars stay miniG-parented —
  spin/lift/swap ride free; a swapped model drops stale recs to `gone`.
  Toggling off mid-lap doesn't freeze cars — they finish the lap and park on
  arrival. Probe: `__cars.toggle(i,on)`, `.state()` (per-car `{st,s,x,z,ry,tgt}` —
  `tgt` is the claimed slot `edge@along` while seeking),
  `.seek()` (jump a loop car to just before its claimed pull-in splice — park
  on the next tick), `.lanes()`, `.inv()` (the lattice).
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

## Zone composites — the Battlefield on the studio table

The third tier is the **zone**: a 3×3 *block* grid — the authored
**Battlefield** region of `city_layout.json` (`r3–5 × c0–2`), mounted
verbatim on the studio table. Catalog entries carry `zone:{r0,r1,c0,c1}`
instead of `slots`/`name`; `mountModel` dispatches to `emitZone`.

- **966×122×966 grid** = nine 322 v block tiles (the same
  `fillBlockGround`/`stampSlots` recipe as `emitBlock`, shared verbatim —
  one law for road halves, sidewalk rings, lot stamping, and lanes). Seams
  read the layout's `hSeamRows`/`vSeamRows` cells per block edge, so each
  tile's road stripes and corner ownership match the live map.
- **Slots stamp the live layout's assets** — `stasset` fields are full
  paths (`voxel_buildings/empty_land.stasset`); the loader takes the
  basename. `block_41` barber@2, `block_42` barber@0 + lane, `block_51`
  tenement, the rest `empty_land`.
- **`alley_lane` is a semantic marker, not voxels** — the stasset is empty
  (64×8×64, 0 cells). `emitZone` reads its slot positions per block row
  and derives `lanes` bits (row 1 → E|W) so the lane renders as the
  builder's curb-mouth + cross-section cut, matching `emitBlock`'s
  `m.lanes` machinery.
- **`plate` ground mode**: a zone/block whose ground is full-coverage
  (every column has the 2 v slab) emits ONE box plate under the composite
  instead of ~620 K slab voxels, and `miniSyncTops` fills top-truth from
  the rotated-rectangle outline (TOPK nearest hits per felt cell) instead
  of per-column bookkeeping — the zone still seats pads/props at felt+2 v.
- **Interior-bottom cull**: `emitVox` drops y=0 voxels that are roofed and
  walled (invisible under the plate/lots) — ~100 K instances per block
  tile, ~1 M across the zone.
- **Proxies stay coarse**: `noProxy` on the volume itself + one proxy per
  stamped slot + ground → 74 aim surfaces, not 1.36 M instance tests.
- **Async emit**: the 114 M-cell grid scan and the instance write pass
  `await` a setTimeout yield every ~2 M cells so boot stays interactive
  (~7 s mount headless); `t._mountP` keeps the fixpoint behind it.
- **Spin throttles on huge models** — `miniSpinSet` drops the per-frame
  top-sync once `dims` exceed the adaptive threshold; rotation still works
  but tops refresh at a coarser cadence.
- Measured result: `battlefield` — `1,356,393` visible surface voxels at
  scale `0.01863` (≈1:54), `312,958` tracked columns, `74` proxies.

## The debug pad — a pickable touchscreen

`emitPad()` builds `debug_pad` as a procedural `PLACED` rec (no voxel asset — the
law is rec-driven): a `.18×.012×.12` slab, `EdgesGeometry` trim in `padEdgeMat`
(hue-rides via `boundHue`), and a `CanvasTexture` screen on the +y face. It
spawns on the Primary Table's felt margin (`-4.1, -.575`, `source:
'manifest'` → position persists through `dvm_place` like any manifest item),
`pickable` — E-takes, E-places, and all held-item rotation keys work on it.

**Regions are data.** `PAD_REGIONS` are `{rect(px), label, glyph, fn}` entries
hit-tested through raycast `uv` on the screen plane — the face is software, so
future tools are draw code, never new geometry. One region table feeds two
input modes:

- **Held → RMB tap frees the cursor** (`padFocus`: `exitPointerLock`,
  `mousemove` raycasts through `mouseNDC` → `padScreenHit` → hover highlight,
  LMB presses). Exits to mouse look: **RMB tap** (toggle — the tap window
  stamps inside the focus branch, else a stale `rmbT0` can never fire),
  **LMB off the screen** (`padScreenHit` miss → release; dead space on the
  face keeps focus), or Esc/place mode. Gesture exits re-lock instantly;
  Esc hits Chrome's pointer-lock cooldown. WASD still moves you; E still
  places the pad. The click-to-lock canvas handler is gated `!padFocus`
  so presses don't re-engage.
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
  exact `miniSyncTops` runs on stop/reset, then **`miniSettle` re-seats every
  rider above the slab** — mid-spin drops seat on lagged tops, so on stop
  anything inside the footprint re-derives under the true map (embedded items
  pop out, floaters drop). The same settle runs after every slice op and at
  mount completion.
- **SLICE — the editor's xyz window, same contract.** `mn.slice` holds per-axis
  index ranges; `miniSyncSlice` writes `uVoxClipMin/Max` into the model's
  cloned materials (the editor's own shader patch — instance origins vs a
  local-space box, mirrored-x handled through `fx`). `miniSliceStep` nudges the
  active bound in `dim/16` steps, clamped against its twin; `RESET` restores.
  Slicing is truth, not just draw: `miniSyncTops` re-derives each column's top
  as the tallest voxel inside the window (per-column `ys` lists captured at
  emit) — cut the roof off and measured seating drops to the exposed slab.
  **`spec.noSlice` tables (`block`, `studio`) are locked:** the page shows
  "LOCKED — STREET MODEL", its regions hide from draw + hit-test, and
  `miniSliceStep`/`miniSliceReset`/`padAxisSet` guard on the flag — street
  composites keep whole roads because `roadProf`'s measured lanes are a
  whole-model law (a mid-session cut would orphan the cars' park/cruise
  geometry with nothing re-deriving it).
- **MODEL — the swap page.** `◀ MODEL ▶` cycles `catalog` (`padSwap` —
  persists `dvm_model:<id>`); `CARS` toggles `blockTrf` for tables whose
  mounted model carries a street scene (the block composites — parked recs
  with `_slotC`); it lights off harmlessly on tables with no parked cars.
- `padMini()` reads the mounted model's `userData.mini` — buttons draw `on`
  state (active tab / active axis / live spin rate) so the screen always shows
  what's true. Slice + spin are session-scoped, per model instance — a fresh
  mount resets both.

## The L-key inspection torch

Same rig as the Commission's: `__torch` = a `SpotLight` + a faint `PointLight`
fill parked ON the camera eye — a .3 m lead let it light the far side of
clipped walls before the camera arrived. Both sit at intensity 0 until L or
the pad's `⏻` toggles them on. `aim(cam)`
repoints it before every render — it rides the camera object, so the main view,
the X fly-cam, and the follower POV (`__eyes.pip`/`shot`) all light where they
look. L also fires **inside place mode** (the `placeOn` branch has its own `l`
arm — the `E/F/T/R` swallow stays, the torch is deliberately not in it) so the
fly-cam keeps its inspection light while you edit. `__snap` honors `__snapTorch` (`_shot.mjs --torch`): flips it on for the
capture, restores after — screenshot illumination without a session flag.

The tune lives in `TC` (`dvm_torch`, persists): `i` spot power 0–12, `f` fill
halo 0–3, `a` beam index → `TORCH_BEAMS` [.28 tight / .45 / .65 wide], `c` swatch
index → `TORCH_COLS` [warm/day/cool/red/green] — both lamps tint together. Stock
is `PWR 5 / FILL .55 / MID / warm` — softened from 7/.9 because the old tune blew
out close-range voxel detail. The pad's **LAMP** page drives it all: PWR/FILL
steppers, 5 swatches with live color chips + `lit` active state (a generic
region predicate), BEAM cycler, ⏻ toggle, RESET → `TORCH_DEF`.

**The rig is deliberately dumb.** `aim(cam)` only repoints it before each
render — intensity, cone and color are the tune verbatim, at every range and
angle. There is **no per-frame raycast and no adaptive layer**: what you dial
is what the light does. Natural lamp physics still apply (decay 1.2 spot /
1.4 fill), so up close it IS brighter — that's the honest baseline.

The auto-exposure stack (distance scaling ∝ `d^decay`, incidence normalization,
proximity fade, close-range cone widening, eased transitions, the `miniRay` DDA
surface refinement, the `lamp2` tuning page, EXP AUTO/RAW, and the SOFT custom
tone map) grew over several passes chasing close-range whiteout — and was
removed in one strip: every layer re-measured the world on ONE center ray each
frame, and the DDA refinement changed that measurement voxel-to-voxel, so
moving the camera pulsed the spot's shape and brightness instead of holding a
stable pool. Git history (`ba74444` and ancestors) has the full law — if any
piece is ever re-added, add ONE layer at a time and judge it on the wall, not
in the numbers.

## Exhibit lights — the wall switch and the table pendants

`construct/17_lamps.js` gives the room its museum mode: **one wall switch
dims the house and lights a pendant over every table.**

**The pendants.** Each `TABLES` entry gets a scenery group at table-local
`(0, .85, 0)` parented into `t.g` — the `commission_pendant` voxel fixture
(the green-dome banker lamp, ~.30m tall, origin at dome bottom; canopy tops
~1.15 under the `WALL_H=1.55` cornice) plus a light rig: a warm
`SpotLight 0xffe2b0` (i=16, dist 4, angle .85, penumbra .65) aimed straight
down — the widened cone pools across each whole slab — and a small
`PointLight 0xcfffb0` (i=.5) inside the dome for its own glass-glow.
Scenery, not `PLACED` — no bbox, so nothing pollutes the drop scanner, and
aim rays pass over the room. Because the group lives inside `t.g`, **the
lamp follows its table everywhere** — ⇧+LMB drags, lifts, even table
deletion carry the fixture and its lights. Pendant fetch is async per
table; lights exist immediately, the fixture mesh lands when
`/voxel/commission_pendant` resolves. A fourth fixture hangs over the
main-room **rotator** — parented to `spinG` (`carG` is what spins; the
lamp must not ride it), ceiling-mounted at `y=1.2` so its canopy kisses
the cornice line over the tall touring, aim dropped to deck level — the
car turns inside a fixed museum pool like the tables' exhibits.

**The switch.** A dark plate + proud rocker nub + tell-pip scenery group on
the annex face of the interior wall at `(-3.04, .5, -1.6)`, just north of
the door jamb — you flip it walking in. The `.03`-thick plate stands 1.5cm
off the wall face (a coplanar back face z-fought and washed out against the
cream); the nub protrudes ~15mm and rocks ±.4; the pip swaps
`0x7fd4ff ↔ 0x3a3a4a`. Registered as an `INTERACT` entry `{isBtn:true, r:.8}`
→ aim-**E** and LMB both fire it; the prompt reads `E — LIGHTS UP` /
`E — EXHIBIT LIGHTS`.

**The dim law — it dims the VOID, not just the lights.** The room's piercing
wash was never the lamps: `scene.background`, `scene.fog`, and the shared
void-colored `floorMat`/`wallMat` are all unlit `VOID 0xf4f1e8`. `lampsApply`
drives the whole environment: background/fog/floor-walls drop to
`VOID_DIM 0x13141c` (distant surfaces now fade to dark), hemi `1.2 → .22`,
key `2.4 → .5` — navigable dusk. The pendant pools carry the exhibits.
Lamps-off restores the full `VOID` flat.

**The taller room.** `WALL_H` went `1.05 → 1.55` when the pendants arrived —
the arch lintel keeps 1.05 (doors stay door-height below the cornice). The
same `seam` wire that traces the floor now also runs at `WALL_H` — a cornice
outline on every wall, so the (invisible) slab height reads as the room's
ceiling in the wireframe style.

State persists in `dvm_lights` (default **off** — the construct boots in
its full bright drafting-room state; the switch dims on demand). Probe hook `__lamps`
(`on()`, `set(b)`). The L-key torch is untouched — it still layers its own
aimed pool on top.

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
