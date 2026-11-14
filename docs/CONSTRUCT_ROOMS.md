# Construct — Rooms, Bounds Wire & Walk Law

The Construct is a white void. There are no wall meshes: **the glowing wire IS the
wall** — a `coreMat` band drawn at every impassable line, and a `walkOK` rect/block
map that actually stops the player. Rooms expand by declaring three things in
sync: floor tiles, wire segments, walk regions.

## Layout (current)

- **Main room** `x[-3,3] × z[-6,1]` — 6×7m. Rotator dead-centre `(0,-2.5)`,
  door+frame on the north edge `(0,.72)`, desk wall east, mannequin NW.
- **West annex (board room)** `x[-7,-3] × z[-5,0]` — 4×5m, joined through a 1m
  doorway in the shared edge `x=-3` at `z[-3,-2]` — centred on `z=-2.5`, dead on
  the rotator axis.

## The three declarations

| Layer | Data | Purpose |
|-------|------|---------|
| Floor | `MANIFEST.tiles` `[i,j]` grid cells @ `TILE=1.0` | invisible void-material tiles you stand on |
| Wire | `BOUND_FLOOR` `[[ax,az,bx,bz]]` + `BOUND_ARCH` `[{x,z0,z1,h}]` | the glow lines: floor bands along impassable edges, ∩ beams lifted at doorways |
| Walk | `WALK_RECTS` (allowed) − `WALL_BLOCKS` (forbidden) | `walkOK(x,z)` = inside any rect AND not inside any block |

`BOUND_FLOOR` carries two kinds of segment: the **exterior outline** of the union
(T-shape) and **interior wall stubs** on the shared edge `x=-3` — both draw the
same flat band (`BND_W`), because in the void a wire *is* the wall either way.

`BOUND_ARCH` is the "lifted wire" made into a **real portal** — each arch spec
`{x, z0, z1, h, d}` emits 9 meshes:

- **Outline posts ×4** — `BND_W×1.6` wire beams at `x±d/2` on each gap edge, so
  *each room's face gets its own glowing doorway outline* (`d` = jamb depth).
- **Solid jamb reveals ×2** — `jambMat` (opaque `0x14161c` Lambert, the
  kiosk/console dark stock) slabs spanning the depth — *no transparency
  between the two wall faces*.
- **Lintel outlines ×2** — wire beams spanning the gap at `y=h` on each face.
- **Soffit ×1** — dark slab under the lintel so looking up mid-door reads solid.

Wall stubs either side stay single floor bands — the portal stands proud of the
wall line like a set frame, which is exactly how a jamb reads.

## Walk law

`walkOK(x,z)` = `WALK_RECTS.some(inside) && !WALL_BLOCKS.some(inside)`.

- `WALK_RECTS` are the room footprints **inset by the capsule radius (.15)**.
  Adjacent rects meet at their shared inset seam (`x=-2.85`), so crossing is
  seamless wherever a block doesn't forbid it.
- `WALL_BLOCKS` are the interior wall stubs **expanded by capsule radius** in
  thickness (`x[-3.15,-2.85]`) and clamped at the door side so the passage
  measures a true 1m minus margins.
- Both `step()` (player) and `followStep` (followers) move with **axis-slide**:
  try the full move, else each axis alone, else stay. Doors slide you through;
  walls stop you dead.
- `flyStep` (place-mode camera) is clamped to `x[-7.5,3.4] z[-7.5,1.8]` — it may
  pass through walls by design so the annex is fully inspectable.

## Expansion path (adding a room / doorway)

1. Append `MANIFEST.tiles` for the new footprint (invisible floor).
2. Append `BOUND_FLOOR` segments: trace the new **exterior outline** plus any
   interior stubs where an old edge remains impassable. Remove/shorten old
   segments the new room absorbs.
3. Where an old edge opens, add a `BOUND_ARCH` ∩ (posts + lintel, `h` ≈ 1.0).
4. Append a `WALK_RECTS` entry (footprint inset .15) and `WALL_BLOCKS` stubs for
   every interior wall that survives (capsule-expanded thickness).
5. `rebuildBounds()` redraws the wire from the spec on every call.

## Invariants (probe-checked)

- `walkOK(-3,-2.5)` is `true` — the door seam passes.
- `walkOK(-3,-4)` and `walkOK(-3,-1)` are `false` — the stubs hold.
- `walkOK(-3,-3.1)` / `(-3,-2.9)` / `(-3,-2.1)` are `false` — capsule edge
  respects the jamb margins (blocks end where the jamb faces stand).
- `walkOK(-3,-2.8)` / `(-3,-2.2)` are `true` — you can hug the jamb through.
- `boundsG.children.length === 19` — 10 floor bands + 9 portal meshes.
- Followers obey the same `walkOK` — they can be led/`goto`'d through the door.
