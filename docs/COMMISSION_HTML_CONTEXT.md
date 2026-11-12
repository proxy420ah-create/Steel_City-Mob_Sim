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
- **Vinny**: spawns **seated** at chair #2 (BOSS). G = possess, E = sit/stand/open (nearest prop wins the prompt),
  WASD stands+walks, Esc releases.
- **Set dressing**: voxel monitor (live stats), pegboard (TABLED→POLL→VERDICT→GO→BUILT→CLOSED with a moving card),
  shelf + tray cards, seat lamps/rings (floor-hold = amber underglow), chair speech bubbles, file cabinet,
  law desk + Book of Laws.
- **Interactables (E-proximity, possess mode)**: file cabinet (case files/archive/inductions — `__cab`, FILES ▸)
  and the Book of Laws on the law desk (standing orders + seat contracts + law-kind citations — `__laws`, LAWS ▸).
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
  — parented to the part so place mode moves light with fixture. Live on env pendants + west-cap floorlamps;
  the spine-row pendants still ride room key light (no carried rig yet).
- Docs-first: `docs/core/DOCUMENTATION_INDEX.md` is the map. `BUILDING_PIPELINE.md`, `COMMISSION_PROTOCOL.md` are
  the deep dives.

## 7. State at handoff

Committed: `f4989c7` (building pipeline + reskin). Uncommitted: rename to thecommission.html, pendant + seated
spawn + deep-link fix, voxel_inspect tool + protocol doc. Server runs detached on :8723. Feed `files[]` rows are
current. Everything above is verified in-scene, not claimed.
