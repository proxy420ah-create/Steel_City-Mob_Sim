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

## Hall ceiling grid, P.I. front, glass, view aids (2026-11-13)
- **Beam grid contract.** Office beams are 8v (.24m) on a 48v pitch in BOTH axes (z: 46, 94, 142, 190, 238; x: 44, 92, 140, 188, 236, 290, 338, 386); coffer bays 40v (1.20m). The hall continues this grid across the wall. Beam layer = y39-41 mat 157 (underside wy 1.08); coffer layer = y42-44 mat 156 (wy 1.17). Hall spine = z190-197 (world z 3.48). `docs/_hall_ceiling_shift.py` re-grids the hall (rewrites ONLY the beam layer; snapshot `office_complex.snap-pre-hallshift.json`, git-ignored). Re-run it after any hall ceiling drag. Hall pendants ride the spine at z 3.48, y .888 (flush) - x lines: -6.0 (door axis), -2.985, .03.
- **Axes.** Project map convention: +x EAST (office wing is west, x<0), +z NORTH, +y up. The Commission has a top-centre compass HUD (rose + facing + camera x/z/y) - quote it when reporting positions. Cell->world: wx=(x-319)*.03, wz=(z-78)*.03 (anchor 244,0,0 bake).
- **P.I. front (building, not KIT).** `docs/_office_front_glass.py`: sidelights x102-104 / x133-135 (y3-29) + transom x103-134 (y31-37), Storefront Glass (id 114) 1-cell pane at slab z148, air bored z147/z149. The door KIT stays leaf-only.
- **Glass contract.** Any material whose name contains "glass" renders transparent (.55, depthWrite off) in both paths: KIT/props via `voxEmit`, building via the bake's `glass` index list + `quadMesh` split. See known_issues/rendering/BUILDING_GLASS_OPAQUE_AND_ALPHA_STACK.md.
- **Probe flashlight.** `node docs/_shot.mjs <name> --torch --cam=.. --look=..` lights the shot with the L-key torch aimed from the probe camera (`window.__snapTorch`). Use it for any interior/dark check.

## Portal doorway + renderer warm-up phase (2026-11-13)
- **THE CONSTRUCT (own page, docs/construct.html).** The Matrix-style white-void showroom lives at
  /construct.html — reached via the office portal (E - ENTER THE CONSTRUCT -> LOADING PROGRAM veil ->
  location.href). Standalone: import-map three, white fog/bg (0xf4f1e8), invisible floor (unlit
  MeshBasicMaterial at void color), MANIFEST {tiles,props} procedural-expansion seam, FPS walk
  (pointer-lock + WASD, eye .55), platform-bound clamp + door solids, E - EXIT THE CONSTRUCT ->
  thecommission.html?from=construct (Vinny respawns at the portal front). Own voxEmit copy incl.
  imO.count=no contract; lights at init always-on; warm-up = compile+render behind the veil.
  Probe: node _shot.mjs <name> --page=construct --cam=.. --look=.. (page ships __cam/__snap/__uiClean).
- **The portal.** The doorless north-wall hall bay (slab z250-252, clear opening x108-129 y3-29, ~.66x.81m) holds
  a permanent light-door — opaque ShaderMaterial plane (white-hot base, counter-scrolling cyan/violet/magenta
  fbm clouds, alpha 1, no blending), 240 GPU-driven motes (`aSeed` attr + `uT` uniform in the vertex shader,
  soft dots via `gl_PointCoord` — zero per-frame CPU), two PointLights for spill, `__solids` circles blocking
  the span, `INTERACT` entry `E — STEP THROUGH` -> flash + hop to `__portal.target` (currently a placeholder
  hop north of the wall / back; point it at the next scene when one exists). Lives in `portalG` inside envG;
  animated only while the sealed view is live (idle-deck zero-GPU rule).
- **WHY THE WARM-UP EXISTS.** Three.js is lazy: shader programs compile on a material's FIRST draw, vertex
  buffers upload on a mesh's FIRST draw — and every lit material's program bakes in the live LIGHT COUNT.
  envG starts hidden, and the portal's + pendants' lights live inside it, so the first reveal of the sealed
  view both compiled fresh shaders AND changed the light count, recompiling everything — ~1000 ms of work in
  one frame (the "first-look hitch"; `renderer.compile` alone was a no-op because it skips hidden subtrees).
- **The warm-up phase** (`thecommission.html`, inserted before `loadItems`): a `WARMING THE ROOM…` cover over
  `#deckview` from init; `window.__warmUp()` (350ms-debounced, re-entrant) fires when the KIT chain completes
  and again from the portal block — ANY late system that adds lights/materials must call it. `warmRun`:
  `renderer.compileAsync(scene,camera)` (KHR_parallel_shader_compile where available, sync fallback), then one
  real `render()` with envG forced visible AND `frustumCulled` cleared on all objects (forces buffer uploads),
  restore. Cover fades on completion; 15s timeout safety valve so a stall never strands a black deck.
  `window.__warmDone`/`__warmMs` are probe-readable (`_shot.mjs --eval='window.__warmMs'`).
- **Renderer flags.** `powerPreference:'high-performance'`; `?pr=<n>` URL override for pixel ratio (default
  `min(dpr,2)` — `?pr=1.5` is the cheap-fill-rate knob on hi-DPI). Console logs `DVM — GPU:` + parallel-compile
  support at init.
- **Standing law (bug doc known_issues/rendering/FIRST_LOOK_SHADER_COMPILE_HITCH.md):** never add/remove lights
  or flip a lights-carrying group at runtime — light-count change = every lit material recompiles. Register
  lights at init, dim to intensity 0. First-look hitches here are lazy GPU work — check `__warmMs` first.
