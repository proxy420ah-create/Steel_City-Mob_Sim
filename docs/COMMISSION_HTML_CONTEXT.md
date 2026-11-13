# The Commission HTML — Context & Design Handoff

**Read this first.** Written 2026-11-04 to bring a new agent up to speed to work *as a designer* on the Commission page.
The boss (user) directs; you propose, implement, and verify. Don't redesign what's decided — extend it.

## 1. What it is

`docs/thecommission.html` — a single-file noir voxel "deliberation & verdict machine" for **Steel City: Mob Sim**
(Unity 6, a spiritual successor to Gangsters: Organized Crime 1998). A three.js scene renders the Commission's
back-room office; a file bus (`deliberation_feed.js`) drives seats, lamps, speech bubbles and the proposal board.
It is a *design canvas*, not a game build — the boss is styling the room and the meeting that happens in it.

Served by `docs/commission_server.py` on `http://127.0.0.1:8723/`:
- **Page**: `http://127.0.0.1:8723/thecommission.html` (renamed from DELIBERATION_MACHINE.html 2026-11-04)
- Studio: `/studio/voxel_editor.html` · voxel items: `/voxel/<name>` · building bake: `/bake/<name>`
- If the port is dead, restart: `python docs/commission_server.py` (spawn detached — session children die with the session)

## 2. The aesthetic (the decision that governs all others)

**1920s Pittsburgh ward-boss social club.** Not a modern ops center — a smoky back room where the Commission
meets: brick, dark oak, green felt, warm lamps. The boss rejected clean/sci-fi looks; "the nature of our business"
means it should read slightly illicit, warm, worn. Current skin = **S1 Back-room brick** (chosen from 3 previews,
still at `/design_previews/index.html`).

Chibi/miniature scale: the room renders as a dollhouse diorama. Vinny is .62m; the table is 1.4m dia.
**Two camera modes, one build:**
- **Guest view** (default): peeled stage set — floor, back wall, table set, Vinny seated. No ceiling/side walls.
- **Possess** (G): sealed interior — full building envelope appears (walls, ceilings, coffered grid), Vinny walks with WASD + walk-grid collision, E sits at chairs.

## 3. The room as built (all verified live)

- **Building**: `office_complex` (user-authored in Voxel Studio, 1.115M voxels, 600×69×600 @ .03m). Three 150-cell
  rooms + a hall along the **INFORMANT (west) side** — the boss flipped it there deliberately. Original furnished
  office module sits at anchor (244,0,0) and must stay functionally untouched.
- **Bake pipeline**: `docs/bake_building.py` → `office_complex.bake.json` (stage/env quad split, walk grid, room
  lights). Server auto-rebakes when source metadata changes (mtime-proof). Old stamp KIT is the fallback (`?shell=old`, `?bld=<name>`).
- **Skin** (`docs/building_skin.py` + `docs/reskin_building.py`, materials only — geometry never touched):
  7-tone face brick, light tuck-pointed mortar, soot to ceiling, clinkers, over oak **raised-panel wainscot**
  (colour-only panels → wall mounts stay flat). **One global oak-strip floor** across rooms AND hall — walnut
  border + maple inlay by distance-to-wall, per-room seeds, no threshold seams. Ceilings untouched (3×3 walnut
  coffer + plaster, center bay sits 1 voxel off dead-center over the table).
- **Felt**: green discs removed everywhere **except under the meeting table** — that one disc is the business.
  `keepFelt` rects in `office_complex.skin.json`.
- **Pendant**: `commission_pendant` (authored `docs/_make_pendant.py`): green-enamel dome, brass stem/collar/canopy,
  milk-glass lining, centered bulb — hung over the table (canopy at the 1.89m coffer bay). SpotLight pools warm
  light on the felt + faint green spill in the shade. **Always on — it lights the table in both camera modes.**
  **2026-11-12: all 6 KIT entries PARKED** (commented at `thecommission.html` ~1833-38, positions + light specs
  preserved inline) for the ceiling-lowering pass — the spine row's scene-level rigs (spot+glow loop ~1770)
  still burn with no fixture, and the run needs a re-centering pass when it returns.
- **Vinny**: spawns **seated** at chair #2 (BOSS). G = possess, E = sit/stand/open (nearest prop wins the prompt),
  WASD stands+walks, Esc releases.
- **Set dressing**: voxel monitor (live stats), pegboard (TABLED→POLL→VERDICT→GO→BUILT→CLOSED with a moving card),
  shelf + tray cards, seat lamps/rings (floor-hold = amber underglow), chair speech bubbles, file cabinet,
  law desk + Book of Laws, office door to the hall.
- **Interactables (E-proximity, possess mode)**: file cabinet (case files/archive/inductions — `__cab`, FILES ▸),
  the Book of Laws on the law desk (standing orders + seat contracts + law-kind citations — `__laws`, LAWS ▸),
  and the office door (`__door` — swings on its file-declared hinge, gates the doorway while closed).
  The standing law moved OFF the cabinet into the tome (DVM-LAW1 stage 2); both overlays share the
  oak+brass+manila theme and the same browse contract (unlock cursor, possess held, Esc/E closes).
  **Ballot pin dots were REMOVED** (clutter) — vote tally still accumulates in `DS.vetoSeats`.

## 4. Design sensibilities observed (respect these)

- **Clean over busy.** Pins were clutter; repeating textures were "bad seams". Period-correct means worn, varied,
  hand-laid — never a repeating tile.
- **Functional honesty.** A fixture mounts to the real ceiling bay; the felt is where the business happens;
  seams at module joins are unacceptable — patterns are keyed on GLOBAL coordinates.
- **One hero per view.** Table + pendant + felt is the money shot. Don't compete with it.
- **Small = chibi.** Real-world sizes (lamps, tiles) are scaled to the miniature, not literal.

## 5. Open design threads (boss-flagged, unpicked)

1. **Voting UX redesign** — "the whole voting process needs to be done over to match the upgraded office." Raw
   material: proposal card, seat lamps/rings, chair bubbles, `DS.vetoSeats` tally, `DECK.pin` hook.
2. **Hall lighting** — corridor has no lamp of its own; far ends read dark.
3. **Unfurnished side rooms** — west room + south room are empty shells; `placements[]` in the building JSON lands
   in-scene (or place props via Room builder).
4. Alt skins on file: S2 executive (plaster/parquet/marble-checker), S3 ward (green/lino/terrazzo) — `undo` then
   `reskin --style S2`.

## 6. Working rules (the contract)

- **Verify before claiming.** Headless Edge CDP shots (pattern: `docs/_shot2.js`; method: PERFORMANCE_PATTERNS
  #18 — SwiftShader proves looks, not framerate). Walk Vinny with dispatched events to check interiors.
- **Inspect voxel assets** with `docs/voxel_inspect.py` (see `docs/core/VOXEL_INSPECTION_PROTOCOL.md` — and obey
  its flood-fill rule; an unbounded BFS ate 29GB RAM once).
- **Building edits**: scripted tools only (`building_edit.py`, `reskin_building.py`) — they back up to `.prev.json`,
  one `undo` restores. Never hand-edit the 56MB source. Tell the boss to reload Studio before re-exporting or it
  overwrites scripted changes.
- **Git**: never commit `office_complex.json` / `.prev.json` / `*.bake.stamp` (gitignored) or `deliberation_feed.js`
  (live bus). Committed artifacts: bake + skin config + tools + docs. Update `RECENT_CHANGES.md` before commits.
- **Commission relay** (`/.devin/rules/commission-relay.md` if working the seat daemons): the feed is truth,
  `files[]` rows must follow file renames (done for this one), one relay ear per seat.
- **Scale law (new furniture/props)**: the item JSON's `voxelSize` IS the render pitch — author it for the chibi
  room and load it verbatim, never a second `vs:` in the loader. Legacy KIT entries carry per-item `vs:` because
  they predate the rule (table .0093→.007); new assets must not add to that list — the override is where the
  lawdesk oversize bug lived. Cross-world: editor previews and Unity importers read the same field
  (`ParseVoxelSize`), so file-vs-everywhere keeps all three in lockstep. Exception ledger: Unity SectorBaker
  forces buildings onto the terrain grid at bake time — documented, buildings only.
- **Place mode** (X key): free-cam KIT placement — `__placeOn` folds into the frame loop's envelope/lights
  rule (sealed office while active), orbit clamps lift, pointer lock drops, cam spawns in the office at
  waist level, `mark()` ticks every frame for a live feed. **WASD+QE fly** (Shift sprint; capture-killed so
  fly keys never reach walk/sit). **Left-drag = FPS look** (`enableRotate=false`; the target swings around
  the planted camera so wheel-zoom and the selection lock still work); right-drag pans. Click (<6px)
  raycasts a `__placed` part → magenta box + orbit-target lock that tracks nudges. Arrows/PgUp/PgDn nudge (.005, Shift ×10), Enter drop-to-surface, ,/. rotY, Y copy
  line, Ctrl+Z/Y undo-redo (burst-coalesced stack, 80 deep), **Enter** accept/deselect, **B** drop-to-surface.
  First edit spawns a mint wireframe ghost at `pos0` (returns to origin → ghost + dirty both drop). Exit
  with unsaved edits → prompt: **S** POSTs `/place` (server patches KIT pos/rotY atomically, `pos0`
  disambiguates same-file entries), **C** copies all lines, Esc leaves unsaved. HUD: `#placehud` top-center.
- **Item-carried lights**: KIT entries may carry `lights:[{type:'spot'|'point', c,i,d,pos,tgt?,ang?,pen?,dec?}]`
  — parented to the part so place mode moves light with fixture. `pos`/`tgt` are part-local metres, derived
  from the glow material's voxel centroid (`(v − dims/2 + .5) × vs`), not eyeballed. Live on env pendants,
  west-cap floorlamps + the law-desk lamps; the spine-row pendants still ride room key light.
- **Warm-up / light law**: the page runs a loading phase — `WARMING THE ROOM…` cover, then `window.__warmUp()`
  (debounced, re-entrant) compiles the whole scene with envG forced visible + renders once with culling off, so
  no first-look shader/buffer hitches. RULE: never add/remove lights or reveal a lights-carrying group after
  init (light count is baked into every lit program — a change recompiles all of them); register at init, dim
  to 0. Anything creating materials/lights late must call `window.__warmUp()`. Bug doc:
  known_issues/rendering/FIRST_LOOK_SHADER_COMPILE_HITCH.md; full write-up in BUILDING_PIPELINE.md.
- **Desk lamps**: `commission_desklamp` (authored `docs/_make_desklamp.py`) — B&H/emeralite banker's profile:
  cast-iron octagonal foot, baluster column, neck knuckle + under-shade strap mount (rear underside through
  the mouth edge — never over the crown), level green half-capsule shade, milk interior, chain at the end.
  Two at `x=-.045±.45, y=.30, z=9.40`, `rotY:π/2` (cantilever −z), flanking the Book of Laws — **individual
  copies of one authored file, no placement flags**. The file declares **`truthView:'unity'`** (its authored
  truth = the left-handed Unity render — the chain sits where it was sculpted in UNITY VIEW); this
  right-handed scene honors the declaration via `tvz(m,v)` — mirror z in file space, then placement
  rotations — on EVERY raw prop render path (KIT `flipZ`, building `placements`, chairs, stamper,
  `voxSwap`, law desk). `mirZ` on a KIT entry XORs on top for true mirrored-variant fixtures — never a
  convention fix. Convention law in `docs/core/VOXEL_AUTHORING_STANDARD.md` §truthView.
- **Glass**: `window.voxEmit(m, fillM)` sits beside `tvz` — all raw prop paths emit through it. It partitions
  voxels whose material *name* contains `glass` into a second `InstancedMesh` (`transparent:true, opacity:.55,
  depthWrite:false`, per-instance tint from the file's hex). Unity's raymarch glass ported as the Three.js
  form; any `*glass*` material anywhere is see-through automatically.
- **Office door**: `commission_door` (authored `docs/_make_door.py`) — the door unit (leaf + jamb frame +
  glazed transom, 93 of 200 file-cells wide) nested **centrally** in the west-wing doorway, and the
  ~.40m margins each side are now **real masonry**: `building_edit.py fill-box` laid 5,040 voxels into
  `office_complex.json` (x94..107 + x130..143, y3..62, slab z147..149 — file coords; the doorway gap was
  x94..143) painted by re-running `reskin_building.py`'s own wall formula (`building_skin` `wall_brick`,
  seed off the slab's z-start 147, u = global x) with the file's *existing* skin ids — verified 0 mismatches
  against adjacent wall cells before writing, so courses/wainscot continue with no seams (+66 header-course
  cells at y62 over the reveal after the live pass — the unit sat .086 off the floor and left a 3cm slot at
  the head; pos.y is now .003, floor top is world 0). The **barber-verified
  leaf .60×.80** hinges west (char fills 120% — `VOXEL_AUTHORING_STANDARD.md` §Animated Assemblies); a glazed
  transom rides to the head. **2026-11-12: transom glazing STRIPPED** (glass, mid rail, floating stile —
  41,224 vox now) to clear sightlines for the by-eye ceiling move; transom bar + head frame remain, removed
  `box()` calls kept as `STRIPPED:` comments in `_make_door.py` for the re-fit. The hinge lives in the file: `door:{pivot:[60,0,5], arc, front:'z+'}` +
  `itemParts` tags leaf voxels; the KIT loader parents leaf meshes to a pivot group, `E — OFFICE DOOR`
  (x −6.0, leaf center) eases the swing ~100° into the office, a `__solids` circle run over the leaf span
  gates while closed; the masonry margins block via the real occ grid. Generator lessons: build part-tagged
  voxels in their own map, shell passes need 26-neighbour tests (6-conn floats reliefs), pane glass embeds
  into the sash, and wall infill belongs in the building — painted with the skin's own global-coord formula,
  never freehand.
- Docs-first: `docs/core/DOCUMENTATION_INDEX.md` is the map. `BUILDING_PIPELINE.md`, `COMMISSION_PROTOCOL.md` are
  the deep dives.

## 7. State at handoff

Committed: `f4989c7` (building pipeline + reskin). Uncommitted: rename to thecommission.html, pendant + seated
spawn + deep-link fix, voxel_inspect tool + protocol doc. Server runs detached on :8723. Feed `files[]` rows are
current. Everything above is verified in-scene, not claimed.

**2026-11-12 — chibi reskin + ceiling-lowering in flight (uncommitted):**
- Wall re-grain live on `office_complex`: wainscot rail `.93→.51m`, `2+1` brick courses (.09m) × `5+1` bricks
  (.18m) repeating, recessed-panel wainscot geometry; repainted 349,626 wall cells, rebaked. See
  `BUILDING_PIPELINE.md` §Chibi wall re-grain.
- **Ceiling lowering**: slab dragged to y62..67 in the editor WIP (`~/Downloads/voxel_complex01.json`,
  reskinned — 66,223 cells fixed by the per-cell repaint; `.prev.json` beside it). `office_complex.json`
  itself is NOT yet sliced — `office_complex.snap-pre-ceiling.json` is the pre-move snapshot.
- **Cleared for the move**: 6 pendants parked (KIT comments ~1833-38), door transom glazing stripped.
  Pendants return AFTER the masonry drop lands — they need a re-centering pass (they'd drifted off-centre).
- `voxel_editor.html`: paste-preview teleport fix for >150k-voxel selections (known_issues/editor/
  PASTE_PREVIEW_TELEPORTS_TO_ORIGIN.md).
- New tool `docs/_reskin_walls.py` — repaints skin cells with the live recipe; per-cell face classification.
