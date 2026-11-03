# DVM-015 — Acknowledgement Handshake + The Loud No-Response · v0.3 post-gate
**Drafted by:** THE ARCHITECT · per Rule Six, on the CHAIR's delegation (CHAIR-110)
**INFORMANT gate #1 (INFORMANT-16): THIN → F1/F2/F3 closed (§3b transport, §4 boss-fields, §5 hold-default + §5b binding); NOTE default-flip surfaced for ratification (§5).**
**Expanded per CHAIR-111:** the commission is two halves — the handshake (§1–§6) *and* the alarm it exposes (§6b–§6d): once the protocol expects a response, silence becomes a named event, and a named event can be made loud.
**Precedence:** granted by the boss (boss-83) — "after an acknowledgement, all the sending agent needs to do is thumbs-up the relayed understanding which acts as authorization to proceed."

## 1. The hole it fills
`turn` is a write token: it says *who may speak*, never *that the order was understood*. Every directive today lands on trust — the sender assumes the addressed seat read it, parsed it, and means the same task. The handshake makes the assumption a protocol object: **directive → ACK (relayed understanding) → thumbs-up (authorization)**.

## 2. The shape — two new kinds, one link field
**`kind:'ack'`** — the addressee's relay, in its own words:
```js
{"id":"<SEAT>-<n>","speaker":"<SEAT>","kind":"ack","to":["<sender>"],
 "ack_of":"<directive message id>","text":"<the task as I understand it + what I'll deliver>"}
```
**`kind:'ok'`** — the sender's thumbs-up:
```js
{"id":"<SEAT>-<n>","speaker":"<SEAT>","kind":"ok","to":["<acker>"],
 "ack_of":"<ack message id>","text":"👍"}
```
- `ack_of` links the handshake — new optional field on messages (schema-tolerant: old readers ignore it; the contract doc gains it).
- **`ts` on handshake-bearing messages** — pending-age computation needs a timestamp and messages don't reliably carry one today; `needs_ack` directives, `ack`s and `ok`s all stamp `ts`. Cheap, one field, named here so the clock is never silently assumed.
- `needs_ack:true` on the *directive* marks it handshake-bearing (§4 scope rule).
- `hold:true` on the directive means the addressee waits for the `ok` before acting (§5 — the authorization gate is opt-in, not default).

## 3. Why the floor already carries the ACK — zero write-exemption
`@SEAT` routes the floor to the addressed seat — so the tasked seat's *first write is its turn anyway*. The `ack` is simply what that turn opens with: relay understanding → then the work. No turn-exempt kind, no discipline hole; the handshake rides the existing token.

### 3b. The return leg — the token makes the round trip (gate finding F1)
v0.1/v0.2 had a silent deadlock: a held acker's watcher wakes only on `turn`/BOSS, so a seat-side `ok` would land on a bus nobody reads while `turn` sat elsewhere — authorized but floorless. The rule that closes it: **a `kind:'ok'` routes `turn` to its `to[0]`** — the ok hands the floor to the hand it's releasing. Directive(@X → floor X) → ack(X → floor sender) → ok(sender → floor X, authorized *and* floored to proceed). Boss-side: the server's 👍→`ok` write applies the same rule — `turn` → the acker (boss's @SEAT routing already exists; `👍 @SEAT` routes floor *and* confirms in one gesture). The transport is named, §7's "watcher zero-change" stays true.

## 4. Scope — what needs a handshake (the traffic question)
Not every `@SEAT` deserves two more messages. The rule, machine-checkable:
- **Boss `@SEAT` → `needs_ack` is a *virtual* field, reader-inferred (gate finding F2a — named, not materialized):** `kind:'boss' && to≠∅` means handshake-bearing — no server stamp, no convention to die. His addresses are taskings by nature — this proposal exists because he wants to hear the seats repeat the order back. (A boss `@SEAT` that's clearly casual still costs one ack — acceptable, he's the authority on noise budget.)
- **Boss `hold` — typed marker, server-stamped (gate finding F2b):** the boss can't set JSON fields, so the marker lives in his text: `HOLD` (or `hold:`) in an addressed boss directive → `/boss` stamps `hold:true` in the same write — stamped data, not sniffing, per the DVM-014 doctrine.
- **Seat→seat `@X` → `needs_ack`/`hold` only when the sender sets them** (seats write JSON directly). The CHAIR marking "the ink is yours" gets an ack; a "floor to X" pass doesn't (and floor-passes don't use `to:` anyway — they move `turn`).
- **Broadcasts (no `to:`) → never.** Read-mode context isn't a directive; "everyone acknowledge" is the boss opting to @ the room or say so.
- **Roll calls** are `to:`-free by convention today — exempt by construction.
- Machine check: `pending-ack = {to:[X], needs_ack:true, no message with ack_of==id from X}` — derivable from the bus, no hidden state.

## 5. `hold` — the authorization gate, and the default that needs ratifying
`hold:true` on the directive = **don't act until the `ok` arrives**; absent = ack suffices, work proceeds, thumbs-up is welcome ceremony.

> **DEVIATION FROM BRIEF — for the table's ratification, not inheritance (gate NOTE):** boss-83/CHAIR-110 phrase the thumbs-up as *the* authorization — the literal model is gate-always. v0.1 silently flipped it to opt-in. This draft's ruling, stated for explicit ratification: **boss directives default `hold` ON (his model — he can free a held seat with one word), seat→seat directives default OFF (cheap path — the ack itself closes the loop and the sender can always answer).** If the table reads the brief as gate-always everywhere, one flag flips and nothing else in this draft moves. *(CHAIR-112 ratified boss-hold-ON aloud — this draft's spec stands.)* Named residual: under boss-default-hold the only release is the 👍 — no typed `no hold` off-marker is specced in v1 (binding nit, CHAIR-112; acceptable — one keystroke frees a held seat — and named so the syntax can arrive later without redesign).

## 5b. The 👍 binding rule (gate finding F3)
Multiple pending acks per sender are legal (string taskings), so a bare 👍 needs a named resolution:
- **Bare `👍`** → binds the sender's **most recent** ack awaiting that sender's ok (pending-ok).
- **`👍 @SEAT`** (either order) → binds the most recent pending-ok **from that seat**.
- **No pending-ok** → the ok writes `ack_of:null` — harmless ceremony, named so an orphan ok is a visible event not a silent drop.
- Binding resolves **server-side at write time** (the server reads the feed it writes) — the bus record always carries the explicit `ack_of`; render-side never guesses.

## 6. Timeout & failure — pending stays visible
- The dashboard renders pending-ack state on the directive: **⏳ awaiting** (needs_ack, no ack), **✓ acknowledged** (ack landed), **👍 authorized** (ok landed). Render-side derivation, no new fields.
- **Soft timeout: pending >120s → the ⏳ turns amber-flagged** — the sender sees a stale handshake and re-addresses or escalates to the CHAIR. v1 has no auto-escalation: judgment stays with the sender (the honest line — a dead ear can't ack, and that's *information*, not a bug).
- If an ack reveals a misunderstanding (the relayed task isn't what was sent), the sender replies with correction — the handshake's whole point, exercised.

## 6b. `turn_at` — silence becomes computable (CHAIR's primitive, adopted)
Every write already sets `turn`; the same write now stamps **`turn_at`**. Then *floor held silent > N* = `now − turn_at > N` — computable by **any reader**, no new writer, no new loop. The build write stamps `turn_at` once to seed it; every subsequent floor move maintains it. (An armed ear is not an attentive seat — the CHAIR's own relay died in a gap tonight while its file read alive. Presence proves a file; `turn_at` measures *answer*.)

## 6c. The alarm — two layers, cheapest first
- **Render layer (free, always on):** any reader computes `floorSilent = turn ∈ seats && turn_at && !adjourned && now−turn_at > 120s` — floor on BOSS is exempt (no watcher, no expectation), and a floor resting on a seat *into recess* must not burn (binding nit, CHAIR-112 — recess is a deliberate silence, not a dead one). The dashboard burns it: floor chip flashes red, and diegetically the floor chair's **amber underglow ring pulses red** — the room itself names who's silent. Zero writers; works under every transport; correct even if every relay is dead.
- **Transcript layer (best-effort record):** the **CHAIR is the sentinel of record** — picked and named: deterministic single writer, matches its ceremony seat. On *any* wake (turn, boss message, reconvene) it computes `floorSilent`; if true and no `kind:'sys'` no-answer banner already exists for this `turn_at`, it writes one: *"FLOOR HELD SILENT — <SEAT> has not answered in Xm."* Dedupe rule stated for the stray-wake race: check-for-banner-then-write; first writer wins, second stands down.

## 6d. Boundary honesty
A dead relay cannot ack and cannot be heard by the sentinel — the *visual* alarm is the reliable half precisely because it needs no writer. The banner is the durable record for the ledger, best-effort on the CHAIR's wake. If both relay chains are dead the room still burns the silent floor — the boss sees it, which is who the alarm is *for*.

## 7. Surface changes — small on purpose
- **Bus:** two new kinds (`ack`, `ok`), optional `ack_of`/`needs_ack`/`hold`/`ts` fields + top-level `turn_at` — the feed header contract + `COMMISSION_PROTOCOL.md` gain the clause; strict-JSON write discipline unchanged (writers that already set `turn` set `turn_at` beside it).
- **Dashboard:** kind colors (ack=cyan, ok=green), ⏳/✓/👍 state glyphs on handshake messages, pending flag >120s, floor-silent alarm (floor chip red + underglow ring red-pulse), chair `pulse()` on a seat's ack (the room nods). ~60 lines inside existing render paths.
- **Server (~6 lines in `/boss`, counted honestly — three behaviors):** (a) a boss message that is exactly `👍` (±optional @SEAT) writes `kind:'ok'` with `ack_of` resolved to the bound ack per §5b; (b) `HOLD`/`hold:` marker in an addressed directive stamps `hold:true`; (c) the `ok` write routes `turn` to the acker per §3b. All on the path that already writes his messages — no new endpoints.
- **Watcher:** zero change — `to:`-routing + turn wake already deliver the directive.
- **CHAIR's seat contract gains the sentinel clause:** *on any wake, if the floor has held silent >120s on a seated peer and no no-answer banner exists for that `turn_at`, write it.* (Contract-level, like the BUTTON's built-stamp — law, not convention.)
- **BUTTON's contract** already requires report-routing to CHAIR; the ack formalizes a ceremony it performs — no contract edit needed (noted for the table if the CHAIR wants it written anyway).

## 8. Out of scope
No auto-retry/escalation loops (v1 flags, humans decide); no ack-of-ack chains (an `ok` never requires ack); no retroactive handshakes on historic directives (the flag didn't exist — derivable state treats them as ordinary messages).

## 9. Acceptance
- A `needs_ack` directive renders ⏳; the addressed seat's `ack` flips it ✓ and includes `ack_of` pointing at the directive.
- `hold:true` directive → seat acks and waits; sender's `kind:'ok'` (or boss `👍` via the server mapping) flips 👍 **and routes `turn` to the acker** — the held seat wakes authorized *and* floored.
- Bare `👍` binds the sender's most recent pending-ok; `👍 @SEAT` narrows by seat; unbound ok is a named ceremony, not a silent drop.
- Broadcast/casual mentions produce no handshake traffic; pending >120s flags amber.
- The handshake is visible in-transcript AND in-room (pulse on ack), 2D and 3D alike.
- `turn_at` stamps on every write; a seat holding the floor >120s burns the floor chip + red-pulses its ring with zero writers; the CHAIR's sentinel clause writes the no-answer banner once per `turn_at`.
