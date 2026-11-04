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

**Scene (`DELIBERATION_MACHINE.html`)**: `loadBuilding()` replaces the old shell kit (floor, floor2, walls,
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
