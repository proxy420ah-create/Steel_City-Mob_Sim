# Design Catalog — THE DESIGNER's canon (DVM-2668)

**Owner**: sole-written by THE DESIGNER. No other seat edits this file.
**Contract**: this catalog is an *index to truth, not the truth* — every
entry carries a `source` + `ts` verified-against marker so staleness is
visible on read, not discoverable on veto. A DESIGNER veto must cite an
entry here; uncataloged elements flag for deliberation only.
**Domain**: sizes, dimensions, voxel sizes, themes, styles, colors,
materials — the established grammar of Steel City: Mob Sim.

## Entry format

```
### <element name>
- domain   : scale | palette | material | style | voxel
- value    : <the canonical detail>
- source   : <file/RE ref the entry was verified against>
- verified : <YYYY-MM-DD hh:mm UTC>
- notes    : <conflicts, exceptions, style-change rulings>
```

## Entries

### Voxel unit — building scale
- domain   : voxel
- value    : 0.03 m per voxel; `office_complex` 600×69×600 vox (18×2.07×18 m)
- source   : `Assets/StreamingAssets/voxel_items/office_complex.bake.json` (`voxelSize`, `dims`); `docs/BUILDING_PIPELINE.md`
- verified : 2026-11-05 01:42 UTC
- notes    : baked from `office_complex.json` (56 MB source — scripted tools only, never hand-edit)

### Chibi scale anchors — commission room
- domain   : scale
- value    : dollhouse diorama, not literal scale — Vinny 0.62 m; meeting table 1.4 m dia (.007 vs ×1.4 sXZ, .38 m tall); chair ring r1.15 m; pendant canopy 1.89 m, shade bottom 1.31 m
- source   : `docs/COMMISSION_HTML_CONTEXT.md` §2–3; `thecommission.html` KIT (l.1461–1476)
- verified : 2026-11-05 01:42 UTC
- notes    : "small = chibi" — real-world sizes scaled to the miniature, never literal (context §4)

### Aesthetic decision — 1920s ward-boss social club
- domain   : style
- value    : smoky back-room: brick, dark oak, green felt, warm lamps; reads slightly illicit/warm/worn; skin = S1 Back-room brick (boss-chosen from 3 previews)
- source   : `docs/COMMISSION_HTML_CONTEXT.md` §2; `docs/design_previews/index.html`
- verified : 2026-11-05 01:42 UTC
- notes    : governs all other decisions — clean/sci-fi looks rejected; S2 executive / S3 ward skins remain on file (`reskin --style S2|S3`)

### S1 material grammar — walls, floor, ceiling
- domain   : material
- value    : 7-tone face brick + light tuck-pointed mortar + soot-to-ceiling + clinkers; oak raised-panel wainscot (colour-only panels); one global oak-strip floor across rooms AND hall — walnut border + maple inlay by distance-to-wall, per-room seeds; ceilings 3×3 walnut coffer + plaster (untouched)
- source   : `docs/COMMISSION_HTML_CONTEXT.md` §3; `docs/building_skin.py`; `office_complex.skin.json` room rects
- verified : 2026-11-05 01:42 UTC
- notes    : patterns keyed on GLOBAL coordinates — seams at module joins are a defect (context §4 functional honesty)

### Felt rule — the business disc
- domain   : style
- value    : green felt exists ONLY under the meeting table; removed everywhere else
- source   : `office_complex.skin.json` — `keepFelt:[[285,352,41,108]]`, `keepHallFelt:false`
- verified : 2026-11-05 01:42 UTC
- notes    : "that one disc is the business" — a new felt surface is a style change needing an explicit record

### Seat palette — commission roster
- domain   : palette
- value    : CHAIR #d29922 amber · ARCHITECT #39c5cf cyan · INFORMANT #f778ba pink · BUTTON #f85149 red · AUDITOR #f0883e orange · DESIGNER #bc8cff purple · BOSS #e6d5b0 cream · PRAGMATIST #3fb950 green (vacant contract chair)
- source   : `thecommission.html` l.1188–1190 (`SEAT_HEX`/`SEAT_CSS`)
- verified : 2026-11-05 01:42 UTC
- notes    : DESIGNER inherits the retired HISTORIAN purple slot (DVM-2668 build)

### Lighting rig — warm-room grammar
- domain   : material
- value    : room bulbs #ffd9a0 (PointLight i26, possess-only outside module 1); pendant SpotLight #ffe2b0 + milk-glass glow #cfffb0 — the pendant is ALWAYS ON in both camera modes; seat lamps burn seat-hex while ear live; supervisor wall bulb green #3fb950 / dark #2a2f36
- source   : `thecommission.html` l.1429–1437, l.1550–1558, l.1878–1888
- verified : 2026-11-05 01:42 UTC
- notes    : "one hero per view — table + pendant + felt is the money shot" (context §4); hall corridor has no lamp of its own — known open thread

### Camera envelope — two modes, one build
- domain   : style
- value    : Guest view = peeled stage set (floor, back wall, table set); Possess (G) = sealed interior — walls, ceilings, coffered grid appear, WASD + walk-grid collision
- source   : `docs/COMMISSION_HTML_CONTEXT.md` §2; `thecommission.html` (`envG` envelope, `__bldg.setPossess`)
- verified : 2026-11-05 01:42 UTC
- notes    : envelope-only pieces (walls/ceilings/placements) are `env:true` — never visible in guest view

### Clean-over-busy rule — surface grammar
- domain   : style
- value    : no repeating tiles, no pin clutter — period-correct means worn, varied, hand-laid; ballot pin dots removed as clutter (tally lives in `DS.vetoSeats`)
- source   : `docs/COMMISSION_HTML_CONTEXT.md` §4
- verified : 2026-11-05 01:42 UTC
- notes    : repeating textures read as "bad seams" — the boss's words govern

_First-pass scope: commission-room as-built. Game-side grammar (city scale, agent scale, vehicle scale, UI palette) not yet cataloged — flagged for next pass._
