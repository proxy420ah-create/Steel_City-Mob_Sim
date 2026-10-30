# DVM-009 — Chair-Name Identity (one namespace, bus-wide) · v0.2 post-gate
**Drafted by:** MEMBER2 / THE ARCHITECT · per Rule Six, on the CHAIR's delegation (devin-75)
**INFORMANT gate #1 (MEMBER4-6): THIN → all findings closed below.**
**Boss's brief:** kill the member-number/designation split — the chair name IS the identity; call-ons wear a bold tag. Clicking a seat writes the chair, not the member id.
**CHAIR's frame:** rekey it ALL — turn, speaker, presence, watcher args, server validation, induction text. Member ids survive only off-bus as session handles.

## Canonical namespace
Seat keys — `CHAIR · ARCHITECT · INFORMANT · BUTTON · PRAGMATIST · AUDITOR · HISTORIAN` (the `SEATS{}` keys). Display still renders `THE ARCHITECT` via `SEATS[key].name`. Member ids (DEVIN, MEMBER2–4) retire from the bus; they live on only as session handles in induction files.

## Migration write (one write, rides the BUTTON build)
- `seats`: `[DEVIN, MEMBER2, MEMBER3, MEMBER4]` → `[CHAIR, ARCHITECT, INFORMANT, BUTTON]` — pipeline order (chair leads, architect drafts, informant gates, button builds).
- `turn`: current value → its seat key via the legacy map.
- `members{}`: **retired from the bus** — occupancy derives from presence (`presence_<SEAT>.js` exists+fresh → seat held). Display attrs already live in `SEATS`; the stale `status:"away"` hacks die with it.
- `proposal.by`, any live `to` fields: rekey member id → seat key.
- Historical `messages[].speaker`/`to`: **untouched** — the transcript is history; a render-side legacy map resolves old ids.
- **Feed header comment: RESTORE with chair-name text** (INFORMANT finding 1 — the header is absent today, verified: feed opens on the bare assignment). The restored contract must NOT contain the literal `window.DVM_FEED` string — the string-in-comment mangle broke this bus twice; refer to "the feed assignment" in prose.
- **Self-announce banner inside the migration write** (finding 2): append `kind:"sys"` — `NAMESPACE REKEYED — re-arm watchers as <SEATKEY>` — so any agent's next bus read converts the seam into an instruction. Without it, an agent re-arming verbatim under its member id goes permanently deaf and recreates the presence file the cleanup deleted.
- **Message id convention** (finding 3): `<SEATKEY>-<n>` — `ARCHITECT-22`, `INFORMANT-6`, `CHAIR-74`; `boss-N` unchanged.
- **Live board strings scrubbed** (finding 5): `board.agenda` is live state, not transcript — the `MEMBER2 re-seating` line rewrites to chair names in the migration write. Transcript stays untouched; board does not.
- Same build deletes the orphaned `presence_MEMBER*.js` files.

## Bus write rules going forward
- `speaker` = seat key (`ARCHITECT`); `turn` = seat key; `to` = seat keys. `speaker:"BOSS"` unchanged.
- COMMISSION_PROTOCOL.md + feed header comment rekey in the same pass.

## Watcher (commission_watch.py) — zero code change
`commission_watch.py ARCHITECT 600` — argv is already a string compared to `turn` and stamped into the presence filename. Seat-keyed arg produces `presence_ARCHITECT.js` / `window.DVM_PRESENCE_ARCHITECT` automatically. Agents simply re-arm with their chair name going forward; induction text updates to match.

## Server (commission_server.py)
- `@X` validation already reads `data['seats']` — seat keys validate for free after rekey.
- Add `LEGACY = {DEVIN:CHAIR, MEMBER2:ARCHITECT, MEMBER3:BUTTON, MEMBER4:INFORMANT}` — translate legacy @mentions → seat keys before validation, so boss muscle memory (`@MEMBER3`) keeps working. Stored `to`/`turn` carry seat keys only.
- `/table` fallback: floor routes to `'ARCHITECT'` if seated, else `seats[-1]` — the drafting seat is the sane default. **And fix `server.py:88`** (finding 4): the literal `data.get('seats', ['MEMBER2'])` default carries a member id — rekey the default string itself.

## Dashboard (DELIBERATION_MACHINE.html)
- `LEGACY_SEAT = {DEVIN:'CHAIR', MEMBER2:'ARCHITECT', MEMBER3:'BUTTON', MEMBER4:'INFORMANT'}` — resolves old transcript speakers.
- `whoIs(id)` → `SEATS[id] || SEATS[LEGACY_SEAT[id]]`; `seatKeyFor(id)` → `SEATS[id] ? id : LEGACY_SEAT[id]`.
- `renderSeats`: drop the `OCCUPANTS` name-matching — presence is occupancy: `PRESENCE[key]` → lit / dead→away / none→vacant. Contracts stay `[key]`-keyed.
- Presence poll loop: iterate `Object.keys(SEATS)` → `presence_<key>.js`.
- Click-to-address: seat card AND speaker name both insert `@<SEATKEY>` — uniform.
- **Bold call-on tags**: `appendMsg` wraps `@(CHAIR|ARCHITECT|INFORMANT|BUTTON|PRAGMATIST|AUDITOR|HISTORIAN|BOSS)` **plus legacy ids** `DEVIN|MEMBER2|MEMBER3|MEMBER4` (finding 7 — stored `@MEMBER3` text bolds too, uniform styling) as `<b class="mtag">@$1</b>`.

## Induction files
- `induction_MEMBER*.txt` → rename `induction_<SEAT>.txt`; content rekeys (watcher arg, turn discipline, speaker values, the member-id farewell line).

## Edge cases
- Mid-flight member-keyed arms: a watcher armed as MEMBER2 stamps `presence_MEMBER2.js` which nothing polls → ear reads dead until the next re-arm under the chair name — honest, self-heals.
- `seatKeyFor`/demo speakers: demo already speaks seat keys — no change.
- Occupancy truth table (finding 6, corrected): **no file → vacant · file+unexpired → lit · file+expired → away/dead.** A *crashed* watcher therefore shows **lit** until its armed deadline lapses — dead only after expiry, not on death.

## Attack points
- Dropping `members{}` vs keeping a seat→session binding on the bus: bus keeps zero session handles — cleanest split, occupancy = presence.
- LEGACY alias on the server: kindness, not requirement — kills a class of silent `@`-typos.
- `seats` order becomes meaningful: pipeline order documents itself; the `/table` fallback no longer depends on it.
