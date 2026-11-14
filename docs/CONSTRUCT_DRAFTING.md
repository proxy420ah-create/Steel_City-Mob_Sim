# Construct drafting room — Primary Table + building miniatures (2026-11-16)

The west annex's exhibit layer: square low tables hosting architectural miniatures
of the game's real `.stasset` buildings. `MANIFEST.tables` is the expansion seam —
multiple drafting tables are list entries, not new code.

## The table

```js
{ id: 'primary', x: -5, z: -2.5, size: 1.2, top: .26, scale: 1 / 64,
  models: [{ name: 'tenement_block_0' }] }     // one model at a time — a single-model easel
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
  buildings=0.1). Model voxel = `BLDG_VS × table.scale`. **All models on one table
  share the table's `scale`** — buildings stay comparable, like a real architect's
  set (1:64 → tenement .30m footprint, barber .10m — the block vs the slot reads true).
- **Palette**: `BLDG_MAT` mirrors `VoxelAssetStudio/mob_materials.py` (MUST match
  StAssetReader.cs); glass ids `BLDG_GLASS` {112,113,114,134,147} split into the
  `__glassMat` mesh — the same transparency contract as `voxEmit`.
- **Placement**: model group parents to the table group at `(m.x, top, m.z)`
  table-local — axis-aligned; models ride any table drag automatically.

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

- One table, one model (tenement, centered) — the table is a single-model easel
  by design. `emitTable`/`emitBldg` are the
  whole contract — more tables = more `MANIFEST.tables` entries; more buildings =
  more `models[]` entries.
- `tenement_glass.stasset` (solid-glass blast testbed) is loadable through the
  same path but not placed.
- Not yet: model rotation in spec (tops write is axis-aligned), per-model scale
  override, model selection UI, the barber's `itemParts`/attachment-point display
  (door leaves, barber pole), any actual authoring/editing tool — the table is
  the surface those tools will sit on.
