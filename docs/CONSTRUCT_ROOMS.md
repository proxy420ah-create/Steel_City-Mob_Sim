# Construct — Rooms, Bounds Wire & Walk Law

The Construct is a white void with real (if minimal) walls: every impassable
line is a **light opaque slab** `WALL_D=.14` thick × `WALL_H=1.05` tall, capped
by a `coreMat` cornice wire at the top edge — the same hue-riding stock as
everything else. `WALL_H` equals the arch lintel height, so the cap runs as one
unbroken glowing outline around the whole T-footprint, door included. Walls are
`wallMat` `0xdcd8cc` — a step darker than the void (`0xf4f1e8`) so faces read
as surface and shade by angle, but light enough that interiors stay bright
(occlusion needs opacity, not darkness). Rooms expand by declaring three things
in sync: floor tiles, wall segments, walk regions.

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
| Wall | `BOUND_WALL` `[[ax,az,bx,bz]]` centerlines → slab + cap | opaque light slabs with glowing cornice tops |
| Portal | `BOUND_ARCH` `[{x,z0,z1,h,d}]` | doorway ∩: dual face outlines + dark jamb/soffit/sill fill |
| Walk | `WALK_RECTS` (allowed) − `WALL_BLOCKS` (forbidden) | `walkOK(x,z)` = inside any rect AND not inside any block |

`BOUND_WALL` covers both the **exterior outline** of the union (T-shape) and
**interior stubs** on the shared edge `x=-3` — one slab serves both rooms (two
faces, one wall). Slab ends pad `+WALL_D/2` to close corner miters, except at
arch-adjacent ends (`ARCH_ENDS`) where the jamb blocks cover the join.

`BOUND_ARCH` is the doorway — a real portal with depth `d`, per spec
`{x, z0, z1, h, d}`:

- **Outline posts ×4** — `BND_W×1.6` wire beams at `x±d/2` on each gap edge, so
  *each room's face gets its own glowing doorway outline*.
- **Jamb reveals ×2 + soffit + threshold sill** — `jambMat` (opaque `0x14161c`
  Lambert, kiosk/console dark stock) — *no transparency anywhere in the
  opening*; the portal reads as a dark frame set in the light wall.

## Walk law

`walkOK(x,z)` = `WALK_RECTS.some(inside) && !WALL_BLOCKS.some(inside)`.

- `WALK_RECTS` are the room footprints inset **wall-face (.07) + capsule (.15)
  = .22** from each centerline — the capsule kisses the physical slab face.
  Adjacent rects meet at their shared inset seam (`x=-2.78`), so crossing is
  seamless wherever a block doesn't forbid it.
- `WALL_BLOCKS` are the interior wall stubs: slab depth + capsule radius
  (`x[-3.22,-2.78]`), clamped at the door so the capsule edge kisses the jamb
  faces exactly (`z<-2.825` / `z>-2.175`).
- Both `step()` (player) and `followStep` (followers) move with **axis-slide**:
  try the full move, else each axis alone, else stay. Doors slide you through;
  walls stop you dead.
- `flyStep` (place-mode camera) is clamped to `x[-7.5,3.4] z[-7.5,1.8]` — it may
  pass through walls by design so the annex is fully inspectable.

## Expansion path (adding a room / doorway)

1. Append `MANIFEST.tiles` for the new footprint (invisible floor).
2. Append `BOUND_WALL` centerlines: trace the new **exterior outline** plus any
   interior stubs where an old edge remains impassable. Remove/shorten old
   segments the new room absorbs.
3. Where an old edge opens, add a `BOUND_ARCH` portal `{x,z0,z1,h,d}`.
4. Append a `WALK_RECTS` entry (footprint inset .22 = face+capsule) and
   `WALL_BLOCKS` stubs for every interior wall that survives (slab+capsule).
5. `rebuildBounds()` redraws walls + wire + portals from the spec on every call.

## Invariants (probe-checked)

- `walkOK(-3,-2.5)` is `true` — the door seam passes.
- `walkOK(-3,-4)` and `walkOK(-3,-1)` are `false` — the stubs hold.
- `walkOK(-3,-3.1)` / `(-3,-2.9)` / `(-3,-2.1)` are `false` — capsule edge
  respects the jamb margins (blocks end where the jamb faces stand).
- `walkOK(-3,-2.8)` / `(-3,-2.2)` are `true` — you can hug the jamb through.
- `boundsG.children.length === 30` — 20 wall meshes (10 slabs + 10 caps) +
  10 portal meshes (4 posts, 2 jambs, 2 lintels, soffit, sill).
- Followers obey the same `walkOK` — they can be led/`goto`'d through the door.
