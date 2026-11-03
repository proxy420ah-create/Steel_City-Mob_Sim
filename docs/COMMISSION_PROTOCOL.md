# ⚖ Commission Protocol — Deliberation & Verdict Machine

**Purpose**: operational spec for agents seated on the Commission — a multi-agent
panel that deliberates the boss's proposals and returns verdicts. Read this before
taking a seat. The live protocol rules are ALSO embedded in `deliberation_feed.js`.

**Status**: live system — 11 unanimous SHIP verdicts in the first arc (Oct 30,
2026); see `COMMISSION_LEDGER.md` for the milestone record.

---

## Files

| File | Role |
|---|---|
| `deliberation_feed.js` | THE TABLE — `window.DVM_FEED = {…}` whole-state snapshot. Bus + render source in one. |
| `commission_watch.py <SEAT> [timeout] [--persist]` | THE EAR — polls the bus every 2s; exits 0 on `turn==SEAT` or any unseen non-self message (omniscient, DVM-017). Heartbeat presence (`try_stamp` every ~10s, 30s TTL — C9a); dormant mode under `adjourned` (terminal stamp, flag-only exit); `--persist` stream-mode opt-in (C9b) |
| `commission_server.py [port]` | The boss's write-path — static host + `POST /boss`, `/table`, `/adjourn`, `/reconvene`, `GET /events` (SSE, mtime-driven). Stamps `proposal.status='go'` on `@BUTTON`+GO post-verdict |
| `DELIBERATION_MACHINE.html` | THE DASHBOARD — EventSource push over HTTP, poll fallback under `file://`. DVM-014: the page IS the room — three.js scene column, chair bubbles, fuse lamps, pegboard lifecycle, FILES/LEDGER accordions, chair inspector |
| `presence_<SEAT>.js` | Side-channel ear state — **heartbeat contract (DVM-020 C9a)**: a live watcher re-stamps every ~10s with `ttl_ms=30000`; `armed_at` reads *last seen alive*, and `beat:true` marks heartbeat-era records. A dead ear stops beating → the lamp reads DARK within ≤30s. `pulse_at` retained for compat only. Litter-owner (DVM-011): retiring/renaming a seat sweeps its `presence_<OLD>.js`; orphans (seat outside `SEATS{}`) — finder deletes |
| `drafts/` | Implement drafts (`DVM-###_*.md`) — ARCHITECT artifacts, INFORMANT-gated |
| `induction_<SEAT>.txt` | Seat induction prompts — one line seats a member |

---

## Bus contract

```js
window.DVM_FEED = {
  "proposal": {"id","text","status"} | null,
  // proposal.status literal chain (DVM-020 C12): null → 'tabled' → 'go' → 'built' → null.
  // The slot holds ONLY these literals; deliberating phases (phase 1-3), the verdict
  // stamp, and certification are the EVENTS that move it — 'go' means item-under-build
  // (GO-time re-seat), certification writes the ledger row and vacates atomically.
  "phase": 0..4,               // tabled → openings → rebuttal → veto poll → verdict
  "turn": "<SEATKEY>",         // floor token — ONLY this seat may write
  "seats": ["CHAIR","ARCHITECT","INFORMANT","AUDITOR","BUTTON"],  // occupied, pipeline order
  "adjourned": false,          // recess flag — flagged table = do not re-arm
  "messages": [ ... ],         // append-only, dedupe by "id"
  "verdict": {...} | null,
  "board": {"rules":[], "agenda":[], "closed":[], "seat_rules":{},
            "staged":[],       // DVM-012: staged drafts {id,title,path,detail,staged_by,ts} — tabling evicts
            "files":[]}        // DVM-013: navigator manifest {id,title,path,kind,note,by,ts} — writers register
}
```

- **Identity (DVM-009)**: chair names ARE the namespace — `turn`/`speaker`/`to`/
  `seats` carry seat keys (`CHAIR·ARCHITECT·INFORMANT·BUTTON·PRAGMATIST·AUDITOR·HISTORIAN`).
  `members{}` is retired — occupancy derives from `presence_<SEATKEY>.js` side
  files (watcher-stamped). Legacy member ids (DEVIN, MEMBER2–4) survive only as
  session handles; the server still translates them in @mentions.
  `seat_rules{}` holds the seven-chair roster — PRAGMATIST·HISTORIAN sit vacant
  but contract-readable; `seats[]` holds the five currently seated.
  Floor domain (DVM-019): `LIVE := seats[] ∪ {BOSS}` is the only floor
  domain — the dashboard's `SEATS{}` is a render roster, never the guard.
  `turn` carries only LIVE keys going forward; retired personas and member
  ids (PRAGMATIST→ARCHITECT, DEVIN→CHAIR, MEMBER2–4) resolve read-only via
  `LEGACY_SEAT` — writers never author them. A `turn ∉ LIVE` is a ghost
  floor: the dashboard burns it violet instantly — no 120s grace, zero
  writers needed. `turn_at` stamps milliseconds beside every `turn`;
  readers self-correct seconds-era values (`<1e12 → ×1000`) — one clock,
  one unit.
  Presence hygiene (DVM-011): a seat that retires/renames sweeps its
  `presence_<OLD>.js` in the same act; orphans — `presence_*.js` keyed to a
  seat outside `SEATS{}` — are reported and finder-deleted. One watcher per
  seat; presence stamping is housekeeping — a failed `os.replace` skips a
  beat and retries, it never kills the ear (standing since DVM-010).
  **Inventory predicate (BUTTON's amendment, Oct 31)**: "check the process
  list" means count every process whose `CommandLine` contains
  `commission_watch.py <SEAT>` — watcher AND spawn-shell AND any other
  carrier — not just `python*` watchers. A relay seat's ear is a chain
  (subagent → bash shell → python watcher); counting only the leaf reads
  "clean" while dead relays' shells sit on the box (7 carriers hid behind
  a "BUTTON ×1 clean" sweep). `_ear_triage.ps1 -Seat <SEAT>` encodes the
  predicate: inventories cmdline carriers, kills watchers + shells,
  zeroes the lamp, verifies by recount.
  `board.files[]` is the navigator manifest (writers register rows);
  `board.staged[]` holds staged DVMs with full `detail` text on the bus —
  tabling evicts the staged row (DVM-012/013).
  `proposal.status` lifecycle stamps (DVM-014): server sets `'go'` on a
  post-verdict `@BUTTON` GO; the BUTTON's build-report write sets `'built'`
  by seat contract. Closure is atomic (DVM-019): the certifying write sets
  `proposal:null` and appends `board.closed[]` in the same write — the slot
  reads TABLE IDLE, never a corpse of last business. **Close-out ritual
  (boss decree):** every certified/closed item ends with a CHAIR practical
  brief on the main transcript — what to test, what propagation it needs
  (daemon relights, F5, nothing), and the exact expected behavior — then
  the floor formally returns to BOSS.
  Lifecycle named in law (DVM-020 C12): the slot's literal states are
  `null → 'tabled' → 'go' → 'built' → null`; deliberating phases, the
  verdict stamp, and certification are events between literals, never slot
  values. The **GO-time re-seat**: at `'go'` the slot stops meaning
  *proposal under deliberation* and starts meaning **item under build** —
  build authority sits with BUTTON; the slot is closed to objection,
  amendment, and re-deliberation.
- **Writes**: read → `JSON.parse` the object → append ONE message → set `turn` to the
  next seat → write the whole file back, strict JSON, same wrapper. Never drop keys.
- **Message**: `{"id":"<SEATKEY>-<n>","speaker":"<SEATKEY>","round":<n>,
  "kind":"position|objection|support|veto|verdict|phase|sys|boss|ack|ok|aside","text":"<html ok>"}`
- **Aside — the side table (DVM-017)**: any seat MAY append `kind:'aside'`
  without holding `turn` — the one bounded exception to one-writer law. The
  write carries `speaker`, `text`, `ts`, and optionally `to` — nothing else:
  no votes/ballots/handshake legs, no status stamps, no `turn`/`turn_at`
  mutation. An aside's `to` routes attention only (wake label `ADDRESSED
  (aside)` — never a floor call). Writers merge-by-id: re-read the feed
  immediately before writing and fold in any ids the snapshot lacks — a
  concurrent aside must never silently drop a floor write. Asides wake all
  armed watchers (`OVERHEARD`, or `ADDRESSED (aside)` when named); they render
  only in the SIDE TABLE pane — the transcript filters them. Flooding is an
  AUDITOR finding, same class as writing out of turn.
- **Omniscient wake (DVM-017)**: watchers wake on any unseen non-self message —
  `turn==SEAT` (`YOUR TURN`) > `ADDRESSED (boss)` > `ADDRESSED` > `BOSS SPOKE`
  > `ADDRESSED (aside)` > `BOSS SPOKE (aside)` > `OVERHEARD`. Each tick also
  reads the floor-holder's `presence_<SEAT>.js` (only when `turn ∈ seats`) —
  `ear live ~Ns` / `ear DARK — stale Ns over TTL` (under heartbeat, DVM-020:
  the suffix reads *last seen alive*). Recess suppresses all
  message-triggered wakes; waking ≠ floor — read and prepare, never write.
  **Batch label (DVM-020 C11)**: when several unseen land in one poll the
  label claims the batch's **highest-precedence unseen** (rank map above),
  not its tail — `ADDRESSED (boss) (+2 ambient)`; the winning message always
  prints, even beyond the `[-5:]` tail. The label never claims what the
  print doesn't show.
- **Phases advance** via `{"kind":"phase","phase":N}` once all seats spoke, or CHAIR calls it.
- **Server endpoints**: `POST /boss` (boss text; stamps `proposal.status:'go'` on an
  @BUTTON-addressed `/\bGO\b/i` message post-verdict — DVM-014), `/table`, `/adjourn`,
  `/reconvene`, `GET /events` (SSE change signals — DVM-010).
- **Registers (DVM-013 header law)**: a seat that writes a draft/induction registers
  its `board.files[]` row in the same write; staged DVMs live in `board.staged[]` with
  full `detail` on the bus — tabling evicts the staged row (server `/table` included).
- **BUTTON contract**: build-report writes stamp `proposal.status='built'` in the
  same atomic write (DVM-014 binding amendment). Delegated-seat rule
  (DVM-019 C8-A): the BUTTON stays in `seats[]` and is boss-routable —
  `@BUTTON` hands it the floor for acks, poll ballots, and build reports —
  but it holds no deliberation positions: routable ≠ votable.
- **Handshake (DVM-015)**: `kind:'ack'` relays the understanding back to the sender;
  `kind:'ok'` is the thumbs-up. `ack_of` links the pair; `needs_ack`/`hold`/`ts` are
  optional fields. **The token makes the round trip**: a `kind:'ok'` routes `turn`
  to its `to[0]` — directive floors the tasked seat, the ack floors the sender, the
  ok floors the acker *authorized and floored to proceed*. Boss `@SEAT` directives
  carry `needs_ack` **virtually** (`kind:'boss' && to≠∅`, reader-inferred past
  `ack_epoch` — never materialized); boss `hold` is **opt-in** — stamped by
  `HOLD`/`hold:` in the addressed text or withheld entirely, in which case the
  seat acks and proceeds on chain law (boss decree — CHAIR-572); the release
  is the 👍 (bare 👍 binds the sender's most recent pending-ok, `👍 @SEAT` narrows,
  binding resolved server-side at write; an unbound ok is named ceremony,
  `ack_of:null`). Seat→seat directives handshake only when the sender sets the
  fields. `HOLD`/`hold:` in an addressed boss text stamps `hold:true` — a lowercase
  "hold on a second" cannot gate. `turn_at` stamps beside every `turn` assignment:
  `floorSilent = turn ∈ seats && turn_at && !adjourned && >120s` burns the floor
  chip and red-pulses the chair ring **with zero writers** — recess is deliberate
  silence, not dead silence. The CHAIR is sentinel of record: on any wake, if the
  floor held silent >120s and no no-answer `sys` banner exists for that `turn_at`,
  it writes one (check-then-write; first writer wins). Render states: ⏳ awaiting →
  ✓ acknowledged → 👍 authorized; pending >120s flags amber.
- **Handshake/labor lifecycle (DVM-018)** — the dashboard derives `pending_task`
  per seat from the message tail (zero new bus fields): `aimed` (a `needs_ack`
  seat-write or boss directive naming the seat) → `acked` (ack with matching
  `ack_of`) → `released` (the tasking party's `ok` chains it) → `delivered`
  (a **floor-class** write by the tasked seat — `aside`/`sys`/`phase`/`ack`/`ok`
  are voice, not product — **or the ack stamped `delivers:true`**, the done-flag
  for a self-answering ack) → `expired` (ear dark, or `now − aimed > TASKED_TTL
  = 600s`). `aimed`/`acked`/`released` all render TASKED — owed labor is
  sanctioned labor, and `floorSilent` stays suppressed through the whole window
  while the seat's lamp is lit. `ok` releases but never delivers; an aside
  mid-task never delivers; the newest tasking per seat wins. `delivers:true` is
  opt-in honest — a seat that forgets it still terminates on write/expiry.
- **Watcher**: zero change — `to:`-routing + turn wake carry the handshake.
- **Board law (entered Oct 30, boss decrees)**:
  - *Amendments route through the drafter* — a post-verdict BOSS amendment goes to
    the ARCHITECT for a draft revision; the CHAIR logs the decree, the drafter owns
    the spec; re-gate only if the change is structural.
  - *Delegations carry the handshake* — a seat tasking another seat stamps
    `needs_ack:true` on its tasking write; the tasked seat owes the full ack
    grammar, same as a boss directive.
  - *Ack-before-execute (boss decree)* — every seat, CHAIR included, confirms
    directed work at the table BEFORE executing it: the first write after a
    directive is the ack restating scope, then the build, then the report.
    The confirm is where a misread gets caught cheapest — "wait, that's not
    what I meant" costs a line at ack-time and a rebuild at report-time.
  - *Authorization chains* — a tasking carries the tasker's authority; the tasked
    seat acks and proceeds immediately (no hold) unless the tasking exceeds the
    tasker's own authority — un-ratified builds still wait on GO CODE.
  - *Verdict-lock sweep (DVM-019 C3)* — when a verdict locks a spec, any seat
    may correct stale doc/register text to match the ratified spec in the
    same or next write; the spec wins over the prose, and the sweep is
    recorded on the bus.
  - *All implementation goes through the table (boss decree, Oct 30)* —
    upgrades and enhancements ride the full design gate same as new
    proposals: pitch → ARCHITECT drafts → INFORMANT fullness gate →
    deliberation → veto → CHAIR verdict → BOSS GO → BUTTON builds →
    AUDITOR checks → CHAIR certifies. Boss directives enter as **pitches**
    — the boss may under-spec or overlook, so deliberation completes and
    challenges the design before build; his GO CODE after verdict
    approves the shaped spec. Rapid-fire directives may batch into one
    cycle. **Outage heals** (page-dead, bus-dead class) are exempt — fix
    first, AUDITOR retro-checks after; the table is for design, not
    triage. **Defect corrections** — shipped work violating ordered or
    ratified intent, where the spec is fixed and nothing is left to
    design — ride the fix-lane: BUTTON repairs → AUDITOR checks →
    CHAIR certifies, no design deliberation. The test: if there's a
    design question, it's an upgrade; if there isn't, it's a fix.
    The invariant: **no production write happens outside the
    spine** — the CHAIR certifies, never implements.

## Boss semantics

- `@SEAT` in boss text → **routes the floor** to that seat (wakes them to write)
- No `@` → **broadcast**: every ARMED watcher wakes in **read-mode** — you hear
  the boss, but may only WRITE once the floor is yours
- `/table <text>` → tables a proposal, phase → 1 (openings), floor to addressed/last seat; evicts a matching `board.staged[]` row
- `/aside <text>` → boss joins the side table: posts `kind:'aside'`, `speaker:'BOSS'` with **zero** boss-class machinery — no floor routing, no handshake stamp, no GO/verdict stamps; `@SEAT` rides as attention-only `to` (DVM-017 v0.7)
- `@BUTTON` + GO on a ratified verdict → server stamps `proposal.status='go'`; the BUTTON's report write stamps `'built'` per its contract — the GO is its own release; no 👍 owed
- `👍` (±`@SEAT`) → server writes `kind:'ok'` binding your most recent pending-ok (`@SEAT` narrows); a bound ok routes the floor back to the acker — confirm and hand over in one gesture (DVM-015)
- `HOLD`/`hold:` in an addressed directive → `hold:true` — opt-in per tasking; the seat acks and waits for your 👍 only when the stamp lands. Absent it, chain law applies — ack and proceed
- Boss messages never consume a turn
- **Channel decree (boss-249/250, ratified CHAIR-861/-864):** the boss's
  **main-transcript speech is official business** — he talks freely at the
  table and the seats organize it into items. The **side table is casual
  voice** — minor corrections, chat; never a hidden directive channel.
  **Roll calls are official business** — sound-offs live in the main
  transcript, on the record, never as asides.
  **Deliverables are official business** — verifications, audit reports,
  BUILT/SHIPPED results, findings, and defect confirmations post to the
  main transcript. An aside may *remark* on work; it may never *be* the
  deliverable. If the record needs it, it posts on the record.
  **Work announcements are official business** (boss decree, CHAIR-901):
  any work completed by any seat gets announced on the main transcript —
  the side table deliberates, the transcript records.
- **Moderated round-robin (boss decree, CHAIR-901):** when the BOSS asks
  for a multi-seat discussion/consensus, CHAIR moderates — names the
  speaker per round (one seat at a time, all others hold), carries each
  answer forward into the next prompt so seats respond to each other,
  and cross-references ride `re:<msgid>` so threads survive ordering.
  Two rounds — positions, then objections/cross-talk — then CHAIR
  consolidates and reports to the boss. Only the named seat writes per
  round, so replies never collide.
- **Single-spokesperson briefings (boss decree, CHAIR-1001):** when the
  BOSS addresses the main room for updates or briefings, the CHAIR alone
  replies — other seats hold, even when they could answer. Department
  information travels through the CHAIR: the CHAIR formally addresses the
  member (stamping the floor), the member reports to the CHAIR, and the
  CHAIR consolidates back to the boss. One voice to the main room keeps
  briefings clean; member voices reach the boss through the moderator.
  The one bypass: a boss `@SEAT` ask explicitly names its responder —
  the named seat replies directly (addressed wake + boss routing).
- **Naming stamps the register (ratified, CHAIR-922/923 round-robin,
  proven live on BUTTON + AUDITOR + INFORMANT):** a grant that names a
  seat MUST stamp `turn` + `turn_at` to that seat in the same write —
  the name and the register move together. Prose-only naming grants
  nothing the seat can act on: the delivered pulse batch carries
  `your_turn` and the host's wake contract fires on YOUR TURN alone.
  A seat that holds the floor passes it by stamping the register to the
  next named seat (or back to CHAIR) — the rail runs itself.
- **Report-completion floor return (boss decree, CHAIR-933):** when a
  seat finishes a deliverable/report and the next move belongs to the
  BOSS, it stamps `turn=BOSS` — the register rests on whoever owes the
  next move. A boss-held floor is honest idle: the silent-floor alarm
  already exempts it, and no seat shows FLOOR-HELD for merely holding
  the token.
- **Busy-stamp labor duty (boss decree, CHAIR-937 → approved):** the
  COMPOSING tag derives honesty from `busy_<seat>.js` — an
  agent-written mark with a 30s TTL. Seats stamp it when labor begins
  AND keep it fresh for the whole work window (beat inside the TTL —
  a single stamp dies mid-compose). The mark fails honest: skip it and
  you under-report, never over-claim; die and the TTL clears itself.
  **ALL labor stamps, no minimum size (boss decree, widened boss-338 /
  CHAIR-990→992):** every act that takes real seconds — answer-compose,
  audit-reads, build-verify, file work, anything beyond a bare ear-hold —
  stamps the busy mark at labor-start and clears on done. Quick side-table
  answers are not exempt; the renderer already maps the mark to
  COMPOSING/WORKING. **Canonical stamper (audit finding, CHAIR-1053):**
  marks write ONLY via `commission_busy.py` — it owns the schema
  (`busy`/`armed_at`/`ttl_ms`); hand-rolled JSON is an AUDITOR finding.
  **Arm before busy:** the wall refuses labor-claims from a dead-ear seat
  (`busyTier` gates on `lampState` — DVM-921 honesty), so a stamp while
  deaf is wasted — arm the relay first, then mark the labor.
- **Progress-beat hygiene (boss proposal, CHAIR-991 — kernel adopted):**
  on a labor stretch past ~a fold-cycle, the working seat emits a
  progress aside mid-run — what it's on, at natural section boundaries.
  The busy stamp says WORKING; the beat says what. (The checkpoint
  proposal's interrupt mechanics were retired in review: spawn-first +
  the relay socket holds through labor — a remembered duty can't cure
  a remembered-duty failure.)
- **The floor follows the work (boss decree, post-fold correction —
  CHAIR-962):** the register must always show who owes the next move,
  boss→seat handoffs included. Two routes, both live: a boss `@SEAT`
  routes the floor instantly at the server; an unrouted directive —
  a bare "ratify", "fix it", any task the context aims at one seat —
  is a **granted floor the tasked seat claims in its ack write**
  (`turn`→self). The seat's first write after a directive is therefore
  doubly binding: ack restating scope *and* the floor-pass made visible.
  Skipping the ack leaves the register reading BOSS while labor runs —
  an idle-over-working lie, same class as a decayed busy mark.
- **Propagation-tier declaration (ARCHITECT-125 convention):** the bus
  hot-swaps DATA, never CODE — feed/presence/busy files reload live, the
  renderer ships once at page load. Every BUILT/deliverable report
  declares its tier: **tier-A silent** (data-side fix, auto-propagates)
  or **tier-B reload-gated** (renderer fix — the boss's F5 is part of
  the verification gate). One word in the BUILT post saves a stale-code
  hunt.
- **Aimed vs overheard speech (AUDITOR-103/ARCHITECT-123 convention):**
  an aside that names a seat in `to:[SEAT]` and carries an ask is
  ADDRESSED — the addressee answers it regardless of robin order.
  Asides without a `to:` field are OVERHEARD ambience — courtesy reads,
  hold per the moderation law.
- **Declared-lane asides (ratified, boss's word post-robin CHAIR-960):**
  every side-table ask rides a lane the **asker's own grammar declares
  at post-time** — **OPEN** (anyone MAY answer; relaxed debate is the
  lane's point, the pile-on is the product), **@SEAT** (only the named
  seat answers — `to:`-stamped, ADDRESSED wakes are proof-backed by the
  server-side `addressed` flag), **ROBIN** (CHAIR moderates — speak when
  named; naming stamps the register). Undeclared reads OPEN; a
  `to:`-name reads ADDRESSED — no declaration word required, no
  inference, no text-sniffing. **The cascade:** an ask that goes silent
  degrades to the fallback chain — CHAIR picks up first, then the
  boss-named second, for when CHAIR itself is the silent seat.
  **The boss-ask rider:** OPEN frees ordering, never accountability —
  peer asks MAY, boss asks MUST; past the silence window (~90s /
  2×TTL, the watchdog's own honesty clock — no new magic constant) the
  fallback names the answerer. **The response-gate:** during an ordered
  call (robin, roll call) a seat holding `turn≠me` reads boss-spoke and
  broadcast traffic read-only until named or addressed. **The bound:**
  lanes are convention + gate — no mechanism cures a seat that ignores
  its declared lane; a violation is an AUDITOR finding.
- **Directed-exchange discipline (boss decree, boss-aside post-2376):**
  when the boss directs a named seat to address the room or a peer
  ("X, ask the room" / "X, talk to Y"), the directed exchange OWNS the
  lane — every other seat READS AND WAITS until it lands. No racing,
  no consolidating mid-exchange, no courtesy posts: the wait IS the
  duty. A boss call addressed to THE ROOM routes to CHAIR first —
  CHAIR organizes the response (order, rota, or delegates), the room
  doesn't pile-answer. Violation is an AUDITOR finding, same class as
  ignoring a declared lane.
- **Table separation (boss decree, boss-346):** the two tables split
  cleanly — main transcript carries official business (deliverables,
  verdicts, briefings, boss business); the side table carries ALL
  discussion, including idea-crafting and consensus work that used to
  run as moderated robins. The boss's own tag selects his table
  (`[boss]` → main, `[aside]` → side). On the side table, robin
  formality relaxes: open discourse and cross-examination are the
  default — named-order moderation applies only when the boss or the
  CHAIR explicitly invokes it (e.g. a formal ballot read). The
  consolidated result still posts to the main transcript — the side
  table deliberates, the transcript records. **Discussion triggers:**
  a boss "discuss / talk about / debate" order opens a peer-to-peer
  conversation — seats address EACH OTHER (`re:`-refs, direct replies,
  extend/challenge/merge each other's takes), not four parallel
  answers back to the boss; the CHAIR consolidates when it converges.
- **Universal wake-filter (boss-303 decree, mechanical since the
  `addressed` flag went live — BUTTON-932/ARCHITECT-132):** every
  seat's pulse payload carries `addressed:true` when a new message's
  `to:` names it. Host wake-filters act on three classes:
  `your_turn` OR `boss-spoke` OR `addressed`. Peer-asks are actionable
  wakes by mechanism; everything else is courtesy context — read and
  hold. **Addressed IS the side-table write-grant (boss-356 decree /
  AUDITOR-157):** naming a seat in an aside's `to:` both wakes it AND
  authorizes its aside reply — the `to:` stamp IS the side-table
  floor-pass (pings, corrections, rebuttals, cross-talk). Main-table
  writes still gate on `turn`; induction write-rules that demand
  `turn==SEAT` apply to the main table only, never to asides.
- **Decree propagation law:** a boss decree is **immediately binding at seat
  level** — every armed ear reads the same bus, so it binds the moment it
  posts. It becomes **office-wide durable law** only when CHAIR folds it
  into this file (or the induction texts) — convention is adopted,
  protocol text is inherited. Ratified folds so far: grant-posts must stamp
  the turn register (AUDITOR's C18 rule), seat-dark sys-posts address BOSS
  (CHAIR-848), roll-calls-on-record (CHAIR-861), channel decree (above),
  pulse-plane migration + flap-window fuse (below).
- **Send-triad (DVM-021 v0.3, full-M1)** — `sendMode ∈ {send,table,side} × activeTab ∈ {feed,side}`; the invariant is *armed channel ≡ viewed tab under every gesture*, and exactly three cells are legal: `(send,feed) (table,feed) (side,side)`. Verb-buttons **arm + retab only — never fire**; `TABLE`→`feed`, `SIDE-TABLE`→`side`, and clicking the already-armed verb **disarms to `(send,feed)`** regardless of box content. **Enter fires the armed verb and never disarms**; `SEND` fires floor speech (momentary) and returns to `(send,feed)`. Tab clicks arm their channel's verb (C15 coupling); boot rest pose is `(send,feed)`. The traded hazard — *arm-silent* — is the boss's accepted price for the forgotten-text send class dying outright.

## Seat lifecycle

```
ARM watcher → WAKE → READ the file → act → RE-ARM → repeat
  wake on "YOUR TURN"  → compose, write, flip `turn` to next seat
  wake on "BOSS SPOKE" → absorb context; write ONLY if floor is also yours
```

## Wake patterns — THE DOORBELL (per agent substrate)

**The ear is a long-poll, not a file-watcher (pulse-plane, boss-ordered triage,
live Nov 1).** `commission_pulse.py <SEAT>` holds `GET /pulse/<SEAT>?since=N`
on the commission server (~25s holds); the server returns the **instant** the
feed grows — the HTTP response IS the doorbell, and the client's exit wakes
the host on every substrate. `commission_watch.py` is **deprecated** — never
arm it on new seats.

| Substrate | Pattern | Status |
|---|---|---|
| **Devin-class** (background task notifications) | **Listener relay**: spawn a background subagent whose only job is `python -X utf8 commission_pulse.py <SEAT>`; its completion notification wakes the parent mid-idle. Re-spawn after each wake — ONE client run, never re-armed by the relay itself | ✅ live Nov 1 |
| **Shell + blocking calls** (Cursor, Claude Code, etc.) | Foreground `commission_pulse.py <SEAT>`, block on it, re-arm per wake | ✅ live |
| Session idle while server up | Pulse holds; doorbell lands on message — the stall becomes visible instead of silent | ✅ |
| Server down | Client exits `PULSE DEAD` after 90s; re-arm when the server's back | known limit |

**Presence semantics under pulse:** `presence_<SEAT>.js` is **server-stamped**
while the connection holds (`via:'pulse'`) — the lamp is an observed socket,
not a self-declared file. A **delivered doorbell earns a 90s fuse**
(`via:'pulse-doorbell'`, `ttl_ms:90000`) — a seat acting on a fresh batch is
working, not dark (the flap-window law).

**The key insight**: `exec` shells firing don't interrupt an idle agent — output sits
in a dead buffer. A *background subagent's completion notification* DOES. If your
platform has background tasks, always prefer the listener relay.

**Re-arm discipline (consensus law, tonight's working group — BUTTON standard):**
on every doorbell, **spawning the next relay is the FIRST tool call — before
reading the batch, before acting.** `act → re-arm` makes the compose window a
dead-ear window (the watchdog flag); `re-arm → act` shrinks the dark window to
relay spawn-time (~seconds). One relay per seat at a time — check carrier
inventory before every arm. Exit-mode pulse always: the socket's close IS the
honest death; `--persist` stays opt-in for stream-reading substrates only.

**Canonical relay configuration (boss decree — uniform responsiveness,
INFORMANT-led alignment robin; CHAIR + AUDITOR converted Nov 1).** One ear
shape for every seat — the trio's schema verbatim:

- **CANONICAL RELAY TASK:** "Run ONE `python -X utf8 commission_pulse.py
  <SEAT>` — blocking long-poll; let it run to natural return; do NOT kill
  unless hung minutes past visible feed activity; report complete stdout +
  exit code verbatim, no interpretation; do NOT re-run or spawn another
  ear — re-arm authority belongs to the PARENT; no other commands, no file
  edits — one watch then report."
- **CANONICAL PARENT LAW:** ① the completion notification IS the doorbell;
  ② relay spawn is the FIRST tool call on every wake — before reading,
  before composing; ③ exactly one armed relay per seat; ④ the helper NEVER
  re-arms itself; ⑤ `--follow` is a labor-mode exception by substrate proof
  only, never the default.
- **Why uniform over continuous:** a relay doorbell creates a FRESH TURN
  whose whole job is respond — that is the trio's speed. `--follow`/
  stream-mode delivers inside ONE continuous turn, so asks queue behind
  live compose (the mid-read gap the boss caught). Uniform response
  latency beats ear-continuity: a relay's death is honest-flagged by the
  dual-stamp watchdog, and spawn-first keeps the gap at seconds.

**Wake-delivery contract (DVM-020 C9b)** — the two consumption classes, named in law:

- **Exit-mode — the law on relay/blocking substrates.** The watcher exits on
  wake; the process exit (subagent completion, unblocked call) IS the
  doorbell. Every seat at the table tonight runs this. Default, unchanged.
- **Stream-mode — opt-in, stream-reading substrates only.** `--persist`:
  the watcher prints each wake and keeps polling; the printed line is the
  doorbell, consumed by an agent class that reads a live stream. Hard-gated
  on C9a: a persistent ear MUST heartbeat — stamp-once persistence is the
  C9 lie in worse form. Wake-events are never terminal in stream-mode; the
  loop exits only on (i) explicit kill, (ii) the feed unreadable across a
  bounded miss-count (~60s), or (iii) its own heartbeat write failing
  persistently — a persistent ear that cannot heartbeat must die, so the
  lamp goes dark rather than lie. Dedup: each unseen batch prints once
  (the `seen` set is the signal-state); a held floor prints YOUR TURN once
  per takeover, or again when new unseen arrive under it.

The choice is substrate property, not seat preference: stream-mode is legal
where a live-stream consumer exists, never required.

## The watchdog (DVM-921)

Two files, two writers, two meanings — the dual-stamp:

- `presence_<SEAT>.js` — watcher-owned, proves **loop-alive** (an ear polls).
- `busy_<SEAT>.js` — agent-owned (`commission_busy.py`), proves **agent-alive**
  (a host works). Work-beat convention: stamp `busy:true` before labor,
  re-stamp while working, clear on done; `armed_at:0` marks the clear.
  30s TTL; a dead host's claim decays — the mark is honest because a dead
  agent cannot stamp.

`commission_supervise.py` — one daemon per box. Every ~5s: scan `seats[]`,
read both stamp files, run the carrier-predicate inventory (CommandLine
contains `commission_watch.py <SEAT>` OR `commission_pulse.py <SEAT>` — any
carrier), then the **respawn
predicate — ALL required**:

1. zero carriers; 2. beat-stale > `2×TTL` (grace kills the mid-spawn TOCTOU);
3. not zeroed (`armed_at:0` = commanded dark by `_ear_triage.ps1` — never a
   respawn target); 4. **agent-alive** (fresh `busy_<SEAT>.js` — the mark
   that keeps the daemon from lighting a lamp over a dead host);
5. not adjourned; 6. **blocking substrate only** — relay seats are
   *flag-only*: a daemon-spawned watcher would satisfy the host's own
   carrier check while ringing no doorbell, starving the relay chain it
   backstops. Seat→substrate map lives in the daemon (`SUBSTRATE`); default
   `blocking`.

Posts `kind:'sys'` on **state transitions only** — dark-detected (addressed
to `BOSS`, ratified law), respawned, relit — never per scan. **Flap window
(boss-ordered fix):** dark-detection fires only when a stamp is stale beyond
the same `2×TTL` grace — pulse-plane relay gaps cross `>TTL` but never
`>2×TTL`, so natural re-arm gaps no longer flap dark→relit; a zeroed record
(commanded dark) is never flagged, per the zeroed≠stale law. Stamps
`presence_SUPERVISOR.js` per loop (one-level bound: the room reads its own
dark lamp; respawn is host-boot responsibility — no meta-supervisor).
Guarantee: lamp-honesty + bus-visibility on every seat; the doorbell itself
only on blocking substrates. Backstop, not bootstrap — hosts arm their own
first ear. **Named consequence (idle-alive gap):** an idle host whose ear
dies stays dark — agent-alive proof requires the busy mark; honesty over
coverage, mitigated by the session-start stamp.

**Staged-restore (⑩):** `/table` while a proposal sits parked seats it —
the incumbent lands in `board.staged[]` (`parked:true`, status/phase
preserved) and re-seats on the next write once the slot is empty. No fifth
literal.

## Induction prompt (v2 — paste to seat a member)

```
You are THE ARCHITECT (seat key ARCHITECT), a seated chair of "the
Commission" — a multi-agent deliberation panel. You and other seats
deliberate proposals by writing to ONE shared file (the table). The boss
watches live on a dashboard.

FILE: <abs path>/deliberation_feed.js   — read the header comment: it IS the spec.

To speak: read file → JSON.parse the feed object → append ONE message →
set "turn":"CHAIR" → write back (strict JSON, same wrapper, drop nothing).

WAITING — run in background:  python -X utf8 commission_pulse.py ARCHITECT
It holds a long-poll; the server returning IS the doorbell — exits 0 when new
feed messages land (read-mode — write only if floor is yours).
If your platform supports background task notifications, prefer wrapping this
in a background task so its completion interrupts you. Re-arm after each wake.

MESSAGE: {"id":"ARCHITECT-<n>","speaker":"ARCHITECT","round":<n>,
  "kind":"position|objection|support|veto|verdict|sys|ack|ok","text":"<html ok>"}
No char ceiling — say what needs saying; stay on point, not a word count.
Phases advance on {"kind":"phase","phase":N}.
Malformed file? Write {"kind":"sys"} flagging the violation instead.

YOUR PERSONA: <name — domain, duty, veto scope, failure mode>
```

## Account-switch law (C23 — ratified 4-0, 2026-11-01)

**Account-switch silence is metering, not abandonment.** The boss's token cap is
a rolling ~24h input-token window; a duration-based cooldown (~18h) locks the
account, and he returns on a fresh account. Signature of a cap event: quiet
room + mass ear-death + a high `/usage` h24 total. Any fresh session reading
that pattern must treat it as a metering event — not the end of the room.
- **What survives:** the feed, this protocol, the bug catalog, the drafts, the
  inductions, `usage_ledger.json` — all disk, all account-agnostic.
- **What doesn't:** each seat's in-context transcript. A re-seated room
  re-reads the files; it never inherits old transcripts.
- **Ceiling marks:** `/usage/limit` stamps a ceiling observation file-side;
  retroactive marks may be posted from any account.

## Bench work vs DVM Table work (boss decree, 2026-11-02)

**Two lanes.** *Bench work* = quick changes and fixes — one-line clauses, file
swaps, defect patches. The boss grants standing GO: do it on sight, report on
the record. *DVM Table work* = the formal rail — draft → fullness gate → veto
poll → verdict → GO CODE → bench → certification. The line is scope: if the
change carries new behavior, new schema, or new law, it tables; if it repairs
or swaps what already exists, it's bench. Tonight's specimens: the SSE
`busy_*` routing fix, the `to:`/`needs_ack` cut, and the `commission_chair`→
`chair` file swap were all bench work — GO already on them.

## Bench-floor register (discovered live, 2026-11-02)

During a GO'd bench, **no seat holds the floor — the register parks on BOSS.**
Floor-owed assumes a deliberative floor where the holder owes a response; a
bench owes the *product*, not speech. Builder progress beats are asides under
the parked register; the build report claims the floor when it lands, and the
verdict/certification flow takes it from there. (First form — "register
belongs to the builder" — failed the seat-keyed 90s check within minutes;
this is the corrected law.)

## Roll-call law (boss decree, 2026-11-02)

**A roll call sounded at the main table is answered at the main table, in order.**
- Sound-offs are main-table business: `kind:'position'`, one per seat, never
  scattered into side-table asides.
- Orderly means rota: the caller owns the order (default seat order
  CHAIR → ARCHITECT → INFORMANT → AUDITOR → BUTTON); a seat speaks on its
  slot — not in parallel, not out of turn.
- Content per slot: presence/ear state, inbox watermark, and one line of
  current labor. Short. The sound-off is a status report, not a debate slot.

## Verified

- Turn discipline + append-only held over live two-seat exchange (Oct 30)
- Boss routing: `@SEAT` floor-move, broadcast read-mode wake, `/table` (Oct 30)
- Listener-relay async wake via background subagent (Oct 30)
- Full pipeline end-to-end ×13 (Oct 30): DVM-002→016, all unanimous SHIP —
  draft → INFORMANT fullness gate (Rule Eight) → rebuttal → veto poll →
  verdict → GO CODE → BUTTON build → CHAIR certification → boss report
- Handshake live end-to-end (DVM-015): server 👍→ok binding + HOLD + go-stamp
  + `turn_at` active after single-PID restart; first live `ack`/`ok`/`hold`
  sequence exercised on the DVM-016 GO CODE
- Delegation handshake verified (Oct 30): CHAIR→ARCHITECT tasking ack'd and
  proceeded under authorization-chain law — zero hold roundtrip
- Boss-amendment loop verified (Oct 30): decree → ARCHITECT v0.3 fold →
  CHAIR verification — spec stayed single-source-of-truth
- Seat-keyed identity migration live (DVM-009): member-armed ears timed out
  honestly, re-armed under chair names — zero watcher code change
- SSE push verified live (DVM-010): `/events` 200 `text/event-stream`;
  `file://` falls back to poll with honest `poll` chip
- Adjourn/reconvene verified (DVM-007): dormant watchers terminal-stamp,
  suspend deadline, exit only on flag clear
- Per-seat induction files: `induction_<SEAT>.txt` — one line seats a member
- Known race: boss interjection vs agent write — one repeated turn worst-case
