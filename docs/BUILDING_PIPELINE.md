# Building pipeline - Voxel Studio room -> Commission scene (2026-11-04)

**Source (editable truth)**: `Assets/StreamingAssets/voxel_items/office_complex.json` - a Room-type volume
(600x69x600 @ 0.03m, ~1.05M voxels, architecture only) authored in Voxel Studio. The ORIGINAL office module
sat at cell offset (62,0,0); after the 2026-11-04 hallway flip it is at (335,0,0), byte-identical to `commission_office` (240,648/240,648 voxels verified).
**Do not commit the raw file** (56 MB; GitHub warns at 50 MB). Commit the bake.

**Bake**: `python docs/bake_building.py office_complex` (4 s) -> `office_complex.bake.json` (5 MB, 82 KB gzip'd).
The server's `GET /bake/<name>` rebuilds it automatically when the source is newer, serves it gzip'd, and
serves a committed bake even when the source is absent. Contents:
- exposed-face quads, greedy-merged by colour: 548,401 faces -> 21,568 quads (~43k tris vs 12.6M instanced);
  quad area equals the independent exposed-face count exactly (verified).
- STAGE = original module floor + back wall (the only part group view shows); ENV = everything else.
- walk-clearance grid (floor present, no wall within body height, 3.5-cell radius clear),
  bit-packed occupancy (boom/camera DDA), room centres (felt discs), `placements[]` passthrough.

**Scene (`thecommission.html` (was DELIBERATION_MACHINE.html))**: `loadBuilding()` replaces the old shell kit (floor, floor2, walls,
ceilings, stamps); furniture KIT items (table, cabinet, chairs) are unchanged. Group view = STAGE only;
possess = STAGE + ENV + per-room bulbs (scene-level lights, intensity 0 outside possess - toggling light count
would recompile materials). Vinny walks on the grid (slides along walls); the possess boom is a grid DDA over
the occupancy volume (walls, ceiling, floor). Table/chair/cabinet circles still handled by SOLIDS.
**Fallback**: any bake failure loads the old shell kit. Dev switches: `?shell=old` (force old kit, A/B),
`?bld=<name>` (load another baked building).

**Mapping**: world x = (u - ax)*vs, z = (u - az)*vs, y = u*vs - 0.09. ax = module centre; az = centre + 3 so the
back wall's front face is world z = -2.25 (where sign/board/shelf/tray/pegboard mount - they vanished
into the wall when az was the plain centre).

**Known differences vs the old shell** (properties of the merged `commission_office` asset): side/back walls lie
inside the 4.5m footprint (interior 9 cm narrower per side), ceiling slab sits 9 cm higher with beams flush to
the wall tops (old beams hung 9 cm below). Vinny's wall margin is ~0.1 m from the new inner faces.

**Not done / next**: furniture is still one cube per voxel (table 137k voxels ~ 1.65M tris - bake it too);
no lights over the hall; one unreachable room (87,084 of 95,638 walkable cells reachable from spawn);
other rooms have no furnishings yet (place them in the Room builder -> `placements[]` renders in possess mode).

**Scripted edits**: `python docs/building_edit.py office_complex components | delete-box X0 X1 Y0 Y1 Z0 Z1 | reflect-x --module X0 X1 Z0 Z1 | undo`. Each mutation backs up to `<name>.prev.json`; the server rebakes on the next page load.

## Period skins (2026-11-04)
`building_skin.py` holds the palettes + pattern functions (small fixed palettes, keyed on GLOBAL coordinates so modules
never show phase seams); `reskin_building.py` applies a style to the source JSON - **material ids only, geometry is never
touched** (mounts, collision, walk grid, bake anchor unchanged). Preview all styles: `http://127.0.0.1:8723/design_previews/index.html`
(regenerate with `python docs/_skin_preview.py`).

Applied to `office_complex` (boss pick, whole building, back wall follows the style): **S1 Back-room brick** - 7-shade running-bond
face brick with light tuck-pointed mortar, soot darkening toward the ceiling and clinker bricks over a dark-oak raised-panel
wainscot (panels are colour only, so the wall face stays flat for the sign/board/monitor/shelf mounts); oak-strip floors with
staggered board lengths, walnut border + maple inlay and a unique seed per room. The hall runs the SAME oak strip - the pattern
is generated over the full building grid (border=0) so boards continue rooms->hall with no threshold seams, and the walnut
border + maple inlay are painted by chebyshev distance-to-wall (B=9, same depth as the rooms), so halls get the picture-frame
along walls and doorways. Ceilings are untouched. Felt survives ONLY inside `keepFelt` rectangles - in `office_complex.skin.json`
that is `[[285,352,41,108]]`, the meeting-table disc; the other five discs (3 hall + 2 side-room) were re-laid as oak.
Rooms are auto-detected (150x150 modules from their back walls) into `<name>.skin.json` - edit `rooms` / `keepFelt` to override.

    python docs/reskin_building.py office_complex [--style S1|S2|S3] [--dry]     # backs up to <name>.prev.json
    python docs/building_edit.py   office_complex undo                           # un-skin; then reskin with another --style
Before: floor = one 12x5 tile repeated exactly, 2 plank tones, near-black seam grid ~30% of the surface; wall = 16x8 brick
repeat, 2 tones, near-black mortar, 45 cm solid grey slab at the base.

## Chibi wall re-grain + repaints (2026-11-12)

`reskin_building.py` only sees raw WALL_MATS names — it **cannot re-run on an already-skinned file**.
`docs/_reskin_walls.py` closes that gap: it repaints EXISTING skin-material wall cells with the live
`wall_brick()` recipe, preserving plane identity. Floors, ceilings, felt, and every non-skin cell untouched;
repaint never adds cells (writes back over the original cell set).

    python docs/_reskin_walls.py office_complex [--dry]          # auto .prev.json backup
    python docs/_reskin_walls.py C:/path/to/any_file.json        # any file path; backup beside it

Current chibi wall recipe (the `wall_brick` defaults — new buildings get it too):
- baseboard .18m (3 rows) → raised-panel wainscot to the rail at y20–21 (.51–.54m, chibi sternum);
  recessed panels 9×5 cells (.27×.15m portrait), .24m stile rhythm; wainLo shadow on top/left inner
  edges, wainHi light on bottom/right, highlight line under the rail lip
- brick field above: **2+1 courses (.09m) × 5+1 bricks (.18m), a pure repeat** — by design any future
  wall-height slice lands on a course boundary with no seam (the ceiling-drop cut depends on this)

**Plane classification is per-CELL, by the face the cell presents.** The earlier column-level classifier
(winner-take-all run-length vote per (x,z)) breaks wherever a perpendicular wall crosses behind a slab:
the cross-run wins the vote, the face paints with a constant `u`, and the wall shows flat vertical seam
bands. Current rule: wall neighbours on BOTH ±x sides → x-running wall → `u=x` (and symmetric for z);
exposed faces decide outright; run lengths only arbitrate corners and buried interior. Seed keys are
per-FACE coordinate so crossings keep coherent tone sets.

**Repaint after ANY slab drag.** Moved cells keep their previous plane's pattern — a voxel-editor drag
is the seam generator. After moving wall/ceiling geometry in the Studio, re-run `_reskin_walls.py` on the
file before judging seams by eye. A `--dry` report of "0 cells changed" on a visibly seamed wall means
the seam is a classification/geometry problem, not stale paint — that diagnostic is the tell.

Ceiling lowering is in flight on the editor WIP (`voxel_complex01.json`): slab dragged to y62..67,
interior clear ~1.77m. `office_complex.snap-pre-ceiling.json` is the pre-move snapshot beside the source.


## Agent vision toolkit (2026-11-12)
- `docs/_plan.py` renders bake occupancy as top-down plans and sections (`plan --z0/--z1 --ylo/--yhi --mark=wx,wz,label`; `section x|z N`). Uses the runtime layout `(x*H+y)*D+z`, MSB-first. See known_issues/rendering/BAKE_OCC_GRID_DECODE_LAYOUT.md before writing any other decoder.
- `docs/_shot.mjs <name> --cam=x,y,z --look=x,y,z` drives headless Chrome (CDP, absolute `--user-data-dir`; Edge headless is dead here) and writes `docs/probe_<name>.png` through the server's `/shot` route (restart `commission_server.py` after edits). The page hooks are `__cam`, `__snap`, `__envShot`, `__uiClean`; `?probe=<name>&cam=..&look=..` also works.
- Workflow: measure at exact cell resolution, place, then probe along the beam axis. The user's own view overrides any probe or scan.
- Ceiling: beam underside wy 1.08 (y39), coffer face wy 1.17 (y42). Beams hang .09 below coffers.
