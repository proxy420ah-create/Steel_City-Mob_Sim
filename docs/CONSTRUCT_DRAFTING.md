# Construct drafting room — Primary Table + building miniatures (2026-11-16)

The west annex's exhibit layer: square low tables hosting architectural miniatures
of the game's real `.stasset` buildings. `MANIFEST.tables` is the expansion seam —
multiple drafting tables are list entries, not new code.

## The table

```js
{ id: 'primary', x: -5, z: -2.5, size: 1.2, top: .26, scale: 1 / 64,
  models: [{ name: 'tenement_block_0', fill: .75 }],      // mounted exhibit — one model at a time
  catalog: [{ name: 'tenement_block_0', fill: .75 },      // the swappable universe — the panel cycles it
            { name: 'barber_shop', fill: .75 }] }
```

- `emitTable(spec)` builds it in the Construct's own language: dark furniture slab
  (`0x14161c`, same stock as consoles/kiosk) on four legs, plus the **floor-wire seam
  lifted to the tabletop rim** — the same trick the doorway uses, `coreMat` hue-riding.
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

## The model panel + mountModel

`mountModel(table, spec)` is the easel's swap verb: unmount the current mini
(unparent, dispose instances, **its tops columns revert to felt** via the
recorded `mini.cols`), set `models=[spec]`, emit the new one. `models[]`
therefore always reads as *what's on the table now*.

`catalog` on the spec is the swappable universe. The **model panel** floats on
the annex south wall at `(-4.3, .5, -4.82)` — same pressable-face law as the
mannequin kiosk: `coreMat` rim + dark canvas screen (`— PRIMARY TABLE — /
MODEL i/n / name`) + two physical `◀▶` buttons carrying `coreMat` glyphs, plus
its own ghost `EdgesGeometry` wireframe (`pnlWire`). `panelProbe()` raycasts the
buttons within 1.8 m and rides the same `promptIt` resolution as `kioskProbe`
(ghost gating is per-panel: `btn.panel === 'models'`). Press → cycle the
catalog → `mountModel` → `localStorage['dvm_model:<id>']` persists the choice —
an F5 remounts whatever was on the easel.

Since each catalog entry carries its own `fill`, every building presents at its
own best fit — the barber mounts at `.75` too (`.9m` footprint, ~1:7 vs the
tenement's ~1:21 — "doesn't have to be relative to tenement" was literally
already the contract).

## The debug pad — a pickable touchscreen

`emitPad()` builds `debug_pad` as a procedural `PLACED` rec (no voxel asset — the
law is rec-driven): a `.18×.012×.12` slab, `EdgesGeometry` trim in `padEdgeMat`
(hue-rides via `boundHue`), and a `CanvasTexture` screen on the +y face. It
spawns on the Primary Table's north felt margin (`-5, -1.975`, `source:
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
- **Placed → center-aim press** (`padProbe`, same law as `kioskProbe`/`panelProbe`):
  raycasts the screen within 1.8 m → `uv` → region → `{isBtn, panel:'pad'}` rides
  the `promptIt` chain, so aim + E (or LMB) presses it like any other face.
  Skipped while the pad is held — you can't aim at your own hand.

Probes are unconditional (`kb || pb || db`) so canvas hover state can't freeze;
ghost wires gate per-panel (`btn.panel`). v1 regions drive `mountModel` (the
same verb as the wall panel — `padSwap` redraws both surfaces).

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
