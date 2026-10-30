# DVM-013 — Ops Deck (reclaim the hero strips for a compact interactive environment) · v0.2 post-gate
**Drafted by:** THE ARCHITECT · per Rule Six, on the CHAIR's delegation (CHAIR-99)
**INFORMANT gate #1 (INFORMANT-10): THIN → both findings closed below (manifest seeding, roster source).**
**Precedence:** granted by the boss (boss-76); DVM-012 ratified SHIP sits awaiting GO — sequencing §6 keeps both orders honest.

## 1. What gets reclaimed — and what must survive
The strips being absorbed, and the information each carries today:

| Strip | Carries | Absorbed into |
|---|---|---|
| Hero title | identity, phase strip, srcdot/srclbl, pushchip, turnlbl, clock, msgcount | Deck header bar + HUD chips (§4) |
| feed-stat row | live/dormant status | Deck header state line |

**Nothing the strips said may go unsaid.** The CHAIR's rule: compressed, not deleted. Every field keeps a visible home — diegetic in the scene where it can be, in HUD chrome where it can't.

## 2. The scene — the machine's face, not decoration
One three.js viewport occupies the deck's visual center: **a noir round-table back-room** — the Steel City idiom — rendering bus state, not ambiance.

**State→visual map** (the scene is a read-only consumer of `LASTFEED`/`LASTBOARD`/`PRESENCE` — zero new bus fields):

| Bus state | Scene expression |
|---|---|
| roster = `board.seat_rules` keys (alias dashboard `SEATS{}`) — **seven incl. vacant**; `seats[]` carries only the five occupied, must NOT drive chair count (gate finding) | seven chairs around the table, nameplates |
| presence (armed, inside deadline) | that chair's lamp **lit** |
| presence expired / no file | lamp dark; no file at all = vacant — chair present, ghosted |
| `turn` | floor's chair: amber underglow + nameplate raised |
| `phase` | neon sign over the room: TABLED / OPENINGS / REBUTTAL / VETO POLL / VERDICT |
| `adjourned:true` | room dims to recess amber; lamps drop to embers |
| `proposal` (tabled) | document card on the table center |
| `verdict` | stamp-slam animation on the card (once, on transition) |
| `veto`/`objection` message | brief pulse on the speaker's chair |

**Interaction:** constrained orbit (damped, limited polar — no flying under the floor), hover shows nameplate, **click a chair writes `@SEAT ` into the boss box** — same affordance the seat cards carry today, so muscle memory transfers.

**Rendering discipline — render-on-dirty.** The page sits open for hours; the scene does NOT run an unbounded `requestAnimationFrame` loop. Renders trigger on: (a) bus state change, (b) camera interaction, (c) a 2 Hz idle tick for lamp-breathing only. GPU cost of an idle deck ≈ zero.

## 3. Fallbacks — honest degradation
The scene is *progressive enhancement over the strips it replaces*, not a bet:

1. **Init order:** existing strips render as today; the deck mounts; scene init runs async.
2. **WebGL absent** → strips stay. **CDN import fails** (`import('three')` rejection — no network, or jsdelivr unreachable under `file://`) → strips stay, deck chrome reports `2D` mode chip beside the push/poll chip.
3. Three.js loads via the **same importmap pattern as COMBAT_BEAT_MACHINE** (`three@0.160.0` jsdelivr) — one language across the boss's pages, and the absolute https URL resolves fine from a `file://` page with network.
4. The scene only claims the strip zone *after* first successful render. A scene that can't draw must never leave a hole where the truth was.
5. **`contextlost` listener** (gate note): a WebGL context loss over an hours-open page freezes the scene silently — on `webglcontextlost` the deck drops the scene, restores the strips' data into the HUD, and the mode chip flips `3D`→`2D` honestly rather than showing a dead canvas.

## 4. Compressed chrome — the HUD
Deck corner chips carry the compressed vitals, always visible, scene or no scene:

- `phase` chip · `floor` chip (turn) · `push`/`poll` chip (existing, moved) · `2D`/`3D` mode chip · clock · message count.
- The boss box gains nothing and loses nothing — ADJOURN stays beside SEND.

## 5. The navigator — internal files in-page
Boss's ask: *navigate internal files and considerations without a file explorer.*

**Model — a bus-carried manifest, `board.files[]`:** `{id, title, path, kind, note, by, ts}` where `kind ∈ {draft, induction, protocol, feed, other}`. Same doctrine as DVM-012's `staged[]` — **the register is data on the bus, so the index works under every transport.** Convention: whoever writes a draft/induction registers its row (extends the staging convention the feed header will already carry).

**Seeded at build** (gate finding): the manifest is born populated — the build write registers the eleven drafts, four inductions, `COMMISSION_PROTOCOL.md`, and the feed itself (~17 rows); an empty navigator on arrival would fail the boss's ask in its first minute. `path` is **server-root-relative** (`drafts/x.md`, not `docs/drafts/x.md` — the server chdirs to `docs/`).

**Reading content:**
- Over **HTTP**: `fetch(path)` — the server already serves `docs/`; full file text in a reader pane.
- Under **file://**: fetch can't touch the filesystem — items whose content is bus-carried open anyway (staged DVMs carry `detail` per DVM-012; protocol/induction excerpts may carry `note` summaries). Rows not readable under the current transport are **marked, not hidden** — the navigator shows its own limits honestly, same doctrine as the poll chip.

**Deck layout:** viewport center, navigator panel right (collapsible), HUD chips at corners. Reader pane opens as an in-deck overlay — dark cyber, consistent with the DVM-012 staged-viewer styling (one design language, the panel styles shared).

## 6. Sequencing with DVM-012
Either order is honest (CHAIR-99):
- **013 GOs first:** the build carries `board.staged[]` support — STAGED renders as a navigator `kind` + the 012 preview panel inside the deck.
- **012 GOs first:** the deck inherits `board.staged[]` and the staged-viewer panel; 013's build re-skins it into the deck overlay.
The BUTTON's GO CODE will name which; the draft stands under both.

## 7. Out of scope
Transcript stays DOM (readability beats immersion for text). Commission rail (seats/ledger tabs) unchanged this round — the deck is the strips' successor, not the whole page's. `board.files[]`/`staged[]` are new board registers (data, not message-path writes — the pipeline's write discipline is unchanged); no server changes (`fetch` uses existing static serving).

## 8. Acceptance
- Scene renders over `http://127.0.0.1:8723` and `file://`+network; strips render under `file://`+no-network and no-WebGL; mode chip honest in every case.
- All former strip data visible: phase, floor, push/poll, clock, msgcount, feed-source.
- Click-chair writes `@SEAT`; lamps mirror presence; recess dims the room.
- Navigator lists manifest entries; opens a staged-DVM detail under `file://`; opens a fetched draft under HTTP; marks transport-blocked rows.
- Render-on-dirty verified: no unbounded rAF while idle.
