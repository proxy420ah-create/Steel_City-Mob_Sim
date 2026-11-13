# Voxel Authoring Standard — The File Is the World

**Status**: 🔒 LAW — applies to every item, prop, decor, fixture, and furniture asset
**Established**: 2026-11-08, after the `commission_floorlamp` scale round
**Scope**: VoxelAssetStudio authoring → `voxel_items/` JSON → HTML loaders → Unity

---

## The Law

**`voxelSize` × `dims` in the JSON file IS the real world.** A correctly authored
asset needs no scaling and no translation anywhere it is placed.

- Placement records **position and rotation only** (`pos`, `rotY`, `env`).
- Placement `vs` **mirrors the file's declared `voxelSize`** — it is a loader
  hint, never a sizing knob.
- **`sXZ` and similar stretch fields are banned as patches.** If a footprint is
  wrong, re-author the dims in the file — do not widen at placement.
- If an asset reads too big or too small in scene, **the file is wrong**.
  Fix `voxelSize` (re-grain) or `dims`/`voxels` (re-shape) — then verify again.

Why: assets start in the editor and must be **uniform everywhere** — studio
preview, thecommission.html, Unity — fit for use and editing by any consumer
without a transform tax. A scale patched at one consumer is a lie the next
consumer re-learns the hard way (the 1.58m lamp towering over the cast was
authored correctly for a real room and wrongly for *this* world).

## Reference Anchors — author against the cast

| Anchor | File | Dims × voxelSize | Height | Use for |
|--------|------|------------------|--------|---------|
| Person (Unity) | `voxel_characters/Civilian1.json` | 96³ @ .01 | **0.96 m** | Items for the Unity cast |
| Person (Commission chibi) | Vinny / seat cast | 62 vox @ .01 | **0.62 m** | Items for thecommission set |

Items stand next to people. A floor lamp ≈ person height; a desk lamp ≈
half person. **Check the reference preview in `voxel_editor.html` against
Civilian1 (or the seat cast) before calling an asset done** — the
floating-diffuser and giant-lamp bugs were both caught by eyeballing the
render, not by numbers.

## Grain Standards

| Class | voxelSize | Notes |
|-------|-----------|-------|
| Building / shell | 0.1 m | coarser, merged faces |
| World / terrain | 0.05 m | |
| Character | 0.01 m | 96³ standard cast |
| **Item / decor / prop** | **0.005 m** | 2× density vs characters — the detail grain |

(`CHARACTER_SPAWNING_SYSTEM.md` first declared these; this doc is the
enforcement point.) Commission-era shell pieces currently run .025–.03 —
historical, flagged in the drift audit below.

## Authoring Workflow

1. **Pick the grain** for the class (props → .005).
2. **Author dims/voxels against the reference anchor** — the studio's
   Reference Preview panel renders Civilian1 at true relative scale.
3. **Declare `voxelSize` in the file** — it is part of the asset, like the
   geometry. Never leave it to be guessed downstream.
4. **Place with `vs` = file `voxelSize` only.** If the temptation to set a
   different `vs` or `sXZ` arises, stop — the file is wrong, go fix it.
5. **Verify connectedness** (flood-fill, 0 orphans) — floating voxels inside
   shells are the classic procedural-authoring bug (see `voxel_inspect.py`,
   `VOXEL_INSPECTION_PROTOCOL.md`).

## Procedural Generators — `docs/_make_*.py`

Generators author complete `steelcity_voxel` JSON procedurally
(`_make_pendant.py`, `_make_desklamp.py`). Same law, plus two traps the
validators cannot catch:

- **Orientation: `y = 0` is the bottom face; hanging shades open DOWN.** A bell
  or dome is narrow collar at the *top* row (at the socket) and wide mouth at
  the *lowest* row — `dome_r`/`bell_r` must be **largest at the mouth row**.
  `commission_desklamp`'s first draft inverted the profile (wide at the socket,
  pinched at the mouth → a bowl), and the first `commission_floorlamp` shade had
  the same inversion in-editor. Connectivity and extent pass either way — **only
  the render catches an upside-down shade.** Always eyeball the studio preview
  before calling a generator done.
- **Deep links pick the department from the file.** `/studio/voxel_editor.html?
  model=<name>` resolves to `voxel_items/<name>.json`, reads the file's own
  `assetType`, and switches to that preset — a bare link always lands in the
  right department. `&type=<preset>` remains as an explicit override. A link
  that lands in the wrong department (or blank) is a stale cached page —
  hard-refresh.
- **Half-capsule shades (banker's/emeralite profile): build in the capsule's
  own frame.** `u` along the mouth plane, `v` along the shade axis, `q` =
  distance past the cylinder's end; shell = `dist(u,v,q) ∈ (R−t, R]`, cut at
  `v ≥ 0` and rolled a little under the equator so it reads enclosed from the
  side. The mount joins at the shade's rear underside through the mouth edge —
  the period hardware never arcs over the crown. `_make_desklamp.py` is the
  reference implementation.
- **Light anchors come from the file.** A fixture's `lights:` `pos`/`tgt` is
  derived from its glow material's voxel centroid — `(v − dims/2 + .5) × vs`
  in part-local metres — not eyeballed. Re-author the bulb, re-derive the
  anchor.

## `truthView` — declared handedness (which render carries the intent)

A voxel file is convention-free data: right-handed renderers (the editor's
normal view, thecommission) and left-handed Unity each draw the mirror of the
other. For symmetric assets the difference is invisible; for one-sided
details (chains, text, fittings) one side is the authored intent and the
other is the mirror. `truthView` declares which render carries the intent:

- **`truthView: 'unity'`** — authored against UNITY VIEW (the studio's
  `scene.scale.x=-1` preview of Unity's left-handed render — per studio law
  the authoring truth for props/buildings/vehicles). Right-handed consumers
  mirror `z → dims[2]−1−z` at load to honor it.
- **absent / `'editor'`** — authored against the normal editor view; renders
  raw everywhere. All pre-declaration files behave this way, including the
  Book of Laws whose ink text reads correctly unmirrored.

The chain of custody:

- **Export stamps it** — `getEditorData` writes `truthView:'unity'` whenever
  the UNITY VIEW toggle is on at export. The stamp records *the view the
  intent was authored against*, not a transform.
- **Load restores it** — `applyFileConventions` (wrapping `demoteIfUnity` at
  every load site) opens declared files under the mirrored view — per-file,
  without touching the per-type mirror pref.
- **Generators declare it** — `_make_*.py` emit `'truthView': 'unity'` for
  props whose authored truth is the unity render.
- **Consumers honor it** — thecommission's `tvz(m,v)` covers every raw prop
  render path (KIT `flipZ`, building `placements`, chairs, stamper,
  `voxSwap`, law desk): mirror z in file space first, then placement
  rotations. A KIT entry's `mirZ` XORs on top — for a true mirrored-variant
  placement, never to fix convention.
- **Do NOT bake the mirror into voxels.** A z-flipped file renders correctly
  under right-handed consumers but mirrors *wrong* under Unity — the file's
  real home. Declaration + consumer honoring keeps both correct from one
  canonical file.

## Animated Assemblies — the `door` contract (moving parts live in the file)

A prop that articulates declares its own mechanics — the consumer never
hard-codes a pivot. `commission_door` is the reference:

- **`door: {hinge:'x-', pivot:[x,y,z], arc:rad, front:'z+'}`** — pivot is the
  hinge-edge voxel, arc the swing in radians, front the public face. The
  loader parents the moving part's meshes to a group at `pivot` and tweens
  `rotation.y` between 0 and `arc`.
- **`itemParts` + `itemPartDefs`** tag the moving subset — `key:'leaf'` is the
  part that swings; everything untagged is the fixed frame. Paint part voxels
  from a dedicated map in generators (never re-derive membership by coordinate
  lists — a `(10,13)` tuple is {10,13}, not `range(10,13)`).
- **City-wide leaf standard: `.60m × .80m` — the barber leaf, verified.** The
  barber_shop WIP door reads correctly next to the reference character in the
  studio (BOSS-verified): leaf .60×.80 composed kick ~.45 + glass upper ~.25 +
  top rail ~.10 under a storefront transom. The .96m cast fills 120% of it —
  chibi doors are deliberately smaller than people. Author city leaves to it;
  grander openings take per-site surrounds, not a bigger leaf.
- **Envelope scale ≠ contact scale** — architecture (walls, ceilings,
  openings) may run real-scale; anything the character *touches* — door
  leaves, knobs, chairs, desks, lamps — authors at the cast's chibi scale.
  The office_complex doorway is 1.50×1.77 but the cast is .96m: the office
  door nests the barber .60×.80 leaf + jamb frame + glazed transom
  **centrally** in the opening. A prop must never fake the surrounding
  wall — a faked brick/wainscot extension seamed badly against the real
  wall's courses and was cut; closing a masonry gap is the *building
  asset's* job, done: `building_edit.py fill-box` laid the margin voxels
  into `office_complex.json`, then they were painted by re-running
  `reskin_building.py`'s wall formula for that plane (`wall_brick`, seed
  off the slab z-start, u = the global along-wall coord) with the file's
  existing skin ids — the global-coord keying makes infill cell-for-cell
  identical to the wall it continues (verify against adjacent cells before
  writing; do NOT rerun full reskin — it appends duplicate skin materials).
  Don't interpolate proportions from memory: measure the verified
  reference asset and author to it.
- **Pane glass embeds into the sash** — a glass pane must overlap its frame's
  walls by ≥1 voxel on every edge; a pane ringed by visible cutout floats.
- **Shell passes test the 26-neighbourhood** — 6-conn culls slab faces buried
  under applied reliefs (moldings, panels, plates) and floats each relief off
  as a sheet; 26-conn keeps the face under a relief's edge via diagonal air.
- **Swing-blocking is the consumer's job** — thecommission derives a
  `__solids` circle run from the leaf's own voxel x-span and splices it
  in/out on toggle.

## Static Deformation — the Fold/Bend tool (voxel skinning for props)

`voxel_editor.html` has a general-purpose fold tool (🌀 toolbar button →
floating panel, same slot as the Selection/Ruler panels). It ports the
character skin-blend dose law — `w = smoothstep(|d − pivot| / R)` — to any
voxels, and **bakes the result into the file**. Baked deformation is authored
geometry: it renders identically in the studio, thecommission, and Unity with
no runtime skeleton — the same file-is-world contract as everything else.

- **Live preview** — sliders bend the render (float positions) without writing
  voxels; **Fold** commits as a normal undoable edit (tags travel with cells,
  moved cells win landing collisions).
- **🎯 pivot** — click a voxel to place the fold line; a magenta crease line +
  diamond marks it. The dose is zero at the line and ripples to full at `R`.
- **Modes** — `trough` (mirrored dip, open-book pages) · `−/+ side` · `curl`
  (one-direction roll — paper, scrolls) · `rise` (lift peaking at the line —
  `θ` reads as voxels of lift) · `mounds` (two hinges at `pivot ± gap` — the
  real hardcover profile: two page-block shoulders + flat spine).
- **Scope** — all / selection / selected material / + or − side of the line.
- **skip checklist** — auto-populates the model's materials; ticked materials
  are always rigid regardless of scope (cover boards, ribbons, frames).
- **Iterative** — folds stack: bake a `mounds` lift, then a small `trough` for
  page tilt. Whole-piece lift = Select All (Selection panel) → Move → PgUp.

Book recipe: axis `Z`, pivot on the crease, `mounds`, `gap ~6`, `R 8–14`,
`θ 3–5`, cover/ribbon ticked. `gap > R` leaves a true flat spine.

## File Pitch Must Match the Consumer's Forced Pitch

`CityMap3D` force-sets `voxelSize = 0.05f` for all building renders (the
Inspector value is ignored). `SectorBaker` consumes that, not the file's
field — so **a building JSON's declared `voxelSize` is free to lie**, and
only the editor's file-is-world preview exposes it. `barber_shop.json`
carried a stale `.1` from the pre-upscale era (the game halved building
pitch to `.05`); the editor showed a 6.4m shop where the game renders
3.2m, and the reference civilian read half-size. Fixed 2026-11-11:
the file now declares `.05` — sweep rule: **building JSONs must declare
`voxelSize: 0.05`** (the pitch `CityMap3D` forces), or the studio preview
misleads.

## Known Gap — Unity ignores `voxelSize` in JSON

`CharacterJsonLoader` does not read the `voxelSize` field; `VoxelCharacter.cs`
uses its Inspector field (default `0.01f`). Today the file declaration is
authoritative for the **editor preview and the HTML pipeline**; Unity-side
scale is prefab/Inspector state. True end-to-end uniformity requires either
Unity reading the field, or authoring at the consumer's default — tracked as
an open item (see `docs/known_issues/editor/REFERENCE_PREVIEW_SCALE.md`,
"Remaining Gaps").

## Drift Audit — `thecommission.html` KIT, 2026-11-08

| Asset | file `voxelSize` | placed `vs` | `sXZ` | Status |
|-------|------------------|-------------|-------|--------|
| `commission_floorlamp` | .005 | .005 | — | ✅ conformant (re-authored at .005 grain, v3) |
| `commission_pendant` | .012 | .012 | — | ✅ match (grain ≠ .005 std — legacy) |
| `commission_filecabinet` | .0115 | .0115 | — | ✅ match (grain ≠ .005 std — legacy) |
| `commission_table` | .0093 | .007 | **1.4** | ❌ double patch — placement shrinks Y, widens XZ to fake the file's intent; re-author at true size |
| `commission_wall` / `floor` / `floor2` / `ceiling` | .025 / none | .03 | — | ⚠ shell grain drift — envelope prims, low priority |
| `chair` (seat cast) | .01 | .010 | — | ✅ match |

Open items:
- Re-author `commission_table` at its intended in-world size (1.4 m Ø × .38 m)
  so `sXZ` and the `.007` override can be deleted.
- Decide whether legacy grains (.0115/.012 props) get re-grained to .005 or
  the standard admits "any declared grain, honestly declared."
- Shell prims (.025→.03) — cosmetic drift; fix on next shell rebuild.

---

*Companion: `VOXEL_INSPECTION_PROTOCOL.md` (integrity checklist),*
*`docs/known_issues/editor/REFERENCE_PREVIEW_SCALE.md` (preview scale + Unity gap).*
