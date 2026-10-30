# DVM-014 — Room Takeover (the page becomes the scene) · v0.2 post-gate
**Drafted by:** THE ARCHITECT · per Rule Six, on the CHAIR's delegation (CHAIR-104)
**INFORMANT gate #1 (INFORMANT-13): THIN → both findings closed below (pegboard slot sources §4b, ambient ear countdown §3b).**
**Inheritance rule (binding, CHAIR-104):** a panel may fold; its data may not die. Every datum the strips/rail carry today keeps a named home — §2's home-map is the contract. Transcript stays DOM (013 scope stands).

## 1. Layout — the scene takes the seat the rail vacates
Today: `#deck` band (~160px) on top; below it `.layout` = transcript | rail(SEATS/LEDGER); below that the boss box.
After: **the rail is replaced by the deck column.** Main row becomes `[ transcript | deck column ]`, both at chat-feed height — the boss reads the room and the record side by side at full size, exactly his ask.

- `#deck` moves inside the layout as the right column: `deckhead` stays a thin toolbar (chips + FILES + COLLAPSE), `#deckview` flexes to fill the column's remaining height, `#decknav`/`#deckreader` become **overlay drawers inside the column** — they slide over the scene, they don't steal its width.
- The SEATS rail `div` and its cards retire — the room IS the seat panel (§3 shows where every card datum lands).
- The LEDGER tab retires — its content becomes accordion sections inside the files navigator (§4).
- `#deck2d` (the honest-2D view) promotes with it — a full-height column view, not a lamp strip (§6).
- No new page scrollbars: the column is `overflow:hidden` + internal scrollers, same discipline as the rail today.

## 2. The home-map — where every folded datum lives
| Rail/strip datum today | Home after takeover |
|---|---|
| seat identity + occupant | chair + nameplate (exists) |
| ear `m:ss` countdown | **ambient lamp burn-down (§3b)** + inspector digits + `d2seat` chip text under 2D |
| `rules ▸` seat contract | chair inspector popover — duty + rules, readable vacant (DVM-006 doctrine preserved) |
| click-seat → `@SEAT` | inspector's prominent `[@ SEAT ▸]` button — one extra click, affordance preserved |
| vacant-chair contract preview | same inspector, ghosted chair still clickable |
| `closed (N)` business list | LEDGER accordion section in files nav + `closed N` HUD chip |
| `board.rules` global law | LEDGER accordion section |
| `seat_rules` contract ledger | LEDGER accordion section |
| agenda / open business | LEDGER accordion section |
| staged queue (DVM-011 resident) | STAGED accordion section (already rendered — becomes a peer of FILES/LEDGER) |

**Nothing unsaid.** This table is the completeness contract the gate can audit row by row.

## 3. Speech goes diegetic — chair bubbles
Each seat's latest bus message renders as a bubble above its own chair — the boss watches the room talk.

- **Mechanism:** `THREE.Sprite` + `CanvasTexture` per chair — same proven pattern as the nameplates. Bubble canvas: rounded rect + tail toward the chair, seat-hex border, kind-colored accent (`objection` red / `support` green / `veto` gold / `position` neutral). Text wraps ~34 chars/line, max 4 lines + ellipsis — the bubble is the headline, the transcript is the record.
- **Data:** `DS.lastMsg[k] = {text, kind, ts}` set in `ingest()` where `whoIs(speaker)` resolves to a seat — a two-line hook on existing code.
- **Lifecycle:** fades in on arrival; holds ~45s; fades out (opacity ramp through the `mark()` scheduler — a few frames, then rest). Latest-wins: a new message replaces the old bubble.
- **BOSS/sys messages** aren't seated speech — they land on the **wall monitor** (§4's neon-sign canvas gains a second line / ticker region) so the sign reports `BOSS:` one-liners too. No floating unowned bubbles.
- Boss hover on a bubble could open the full message — **deferred**, noted for the gate: v1 = headline only, transcript carries full text a scroll away.

### 3b. The lamp is a fuse — ambient countdown stays ambient (gate finding)
Today's rail gives a one-glance read of *who goes dark next* — every seat's `m:ss` ambient. Binary lamps would flatten that: an ear 30s from expiry reading identical to a fresh one is a visibility regression inside a "nothing unsaid" doctrine. Fix, diegetic: **lamp intensity burns down with remaining ear life** — `lpt.intensity` and lamp emissive scale map to `remaining / ttl` (1.6 → 0.3), so a seat approaching expiry visibly dims before it dies; at expiry it goes dark as today. The deadline is read off the lamp itself — no click, no digits needed for the ambient tier; the inspector still carries exact `m:ss`. Render cost: the presence poll already diffs a signature — add the rounded remaining-fraction to it so burn-down still renders only on change.

## 4. Process on the walls — the pegboard
The lifecycle becomes physical: a **pegboard rail** mounted on the back wall (a framed plane + slot markers), and the proposal card *travels it*.

- **Slots left→right:** `TABLED → DELIBERATION → VETO POLL → VERDICT → GO CODE → BUILT → CLOSED`.
- **The doc card** (existing mesh — currently static on the table) animates slot to slot on `phase`/status transitions through the `mark()` scheduler: tabled → slides to TABLED slot; phase ticks → advances; ballots pin up during the poll (one small pin/sphere per `veto` message, seat-hex colored — the boss literally watches the votes land); verdict → existing stamp-slam on the card at the VERDICT slot, ruling-colored seal; GO CODE → card slides to GO CODE under a green desk-lamp glow; build report → BUILT; certification → slides off the rail onto a **closed shelf** stacked with prior cards (cap the shelf render at last ~5, count chip carries the rest).
- **Staged tray:** a small tray on the wall's corner holds `board.staged[]` as blank-backed cards — one per staged item — so the queue is visible in-world, not just in the nav.

### 4b. `propStage()` — every slot gets a named source (gate finding)
v0.1 claimed "all derives from existing bus state" — but GO CODE and BUILT exist only as *text* inside boss/BUTTON messages; a literal build either sniffs silently or stalls at VERDICT while the boss watches. The derivation is named slot by slot, and the two text-only transitions are converted to **stamped data** where cheap (the DVM-012 doctrine — make it data, not speech):

| Slot | Source (in precedence order — latest wins) |
|---|---|
| TABLED | `proposal.status === 'tabled'` |
| DELIBERATION | `phase === 1` with proposal live |
| VETO POLL | `phase === 2` |
| VERDICT | `verdict.id === proposal.id` |
| GO CODE | `proposal.status === 'go'` — **new stamp (~3 lines in `commission_server.py`)**: the boss-send path already parses his message; when a `kind:'boss'` message `@BUTTON`-addressed and matching `/\bGO\b/i` lands post-verdict, it stamps `status:'go'` in the same write. Render-side fallback for direct-file GOs: latest boss message matching the same regex, `ts > verdict.ts`. |
| BUILT | `proposal.status === 'built'` — **new stamp, written into the BUTTON's seat contract** (binding amendment, CHAIR-105): *your build-report write stamps `proposal.status='built'` in the same atomic write.* Fallback: latest `speaker:'BUTTON'` message matching `/SHIP/i`, `ts >` the GO. |
| CLOSED | `proposal.id ∈ board.closed[]` — fully machine-readable today |

Fallbacks are *named*, not hidden — the gate's point exactly: sniffing that exists is written down, and the two cheap stamps mean a faithful build never silently stalls. `phase`/`proposal.status`/`verdict`/`staged[]`/`closed[]` carry everything else — no other new fields.

## 5. The chair inspector — the contract's new home
Click a chair → an in-column **inspector popover** (DOM, anchored over the scene — the DVM-012/013 reader styling language, compact):

- Seat name + `THE <NAME>` title, seat-hex accent
- **Occupancy:** presence-derived — `ear 4:32` live countdown / `ear dead` / `vacant` / `in recess` (the rail's chip, diegetic-ized)
- **Duty + rules** — the `seat_rules[k]` contract, readable for vacant chairs exactly as the rail's expander allowed (pre-assignment inspection preserved)
- **`[@ SEAT ▸]` button** — writes `@SEAT ` into the boss box and closes (the address affordance survives, one click deeper)
- Dismiss: ✕, Esc, or clicking elsewhere; raycast already distinguishes chairs — the same `hit.userData.key` drives it.
- A subtle `hover`-only nameplate already exists; keep it.

## 6. Honest degradation — the 2D column
The takeover must not leave 2D poorer than the rail was:

- `render2D()` becomes a **column view**: one row per seat — lamp dot, seat name, ear countdown/dead/vacant text, floor highlight — plus the phase line. It carries *more* than the lamp strip did, matching what the scene says.
- Inspector stays DOM → works identically in 2D (click a `d2seat` row → same popover).
- Pegboard under 2D: the phase chip line gains slot markers (`TABLED ▸ DELIB ▸ POLL ▸ VERDICT ▸ GO ▸ BUILT ▸ CLOSED` with the live one lit) — the board's journey readable without pixels. Bubbles under 2D fold to a `last: "…"` line on each seat row — same data, DOM rendering.
- All existing failure paths stand: no-WebGL / CDN-fail / `contextlost` → 2D column + honest `2D` chip.

## 7. Sequencing & scope
- **Absorbs DVM-012's remaining surface:** the staged-queue section and full-text preview ship inside the files/STAGED accordion + reader overlay — both already coded in 013 and carried forward. Recommendation to the table: 012's GO becomes satisfied-by-014; CHAIR certifies both on landing. (012 stays ratified regardless — the verdict stands, the build path just folds.)
- **Out of scope:** transcript stays DOM; boss box untouched; watcher untouched; no new message-path writes. **One server addition (~3 lines):** the `status:'go'` stamp on the boss-send path (§4b) — the only server change in the draft, named here so "server unchanged" isn't a silent lie again. The BUTTON's `status:'built'` is a one-line convention in a write it already makes.
- **Render-on-dirty doctrine holds:** bubbles, pins, card-slides all run through `mark()`; idle stays ≈zero.

## 8. Acceptance
- Scene column at full chat height beside transcript; 160px band gone; no new page scroll.
- Home-map complete: every row of §2 verifiable — ear timers, contracts (vacant included), @address, closed count, global rules, agenda, staged queue.
- Bubbles: a seat's latest message appears above its chair, kind-colored, fades; boss/sys land on the wall monitor.
- Pegboard: card travels stage slots through a full lifecycle (table→…→closed); ballot pins accumulate in poll; closed shelf grows.
- Inspector opens on chair click in 3D and 2D; `@ SEAT ▸` writes the address.
- 2D column view, blocked rows marked-not-hidden, `contextlost` honest — all prior doctrine holds.
