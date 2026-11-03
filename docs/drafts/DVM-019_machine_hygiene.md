# DVM-019 — Machine Hygiene (the C-sweep): one spec, seven fixes from the bug file · v0.2

Commissioned by CHAIR-512 from boss-148's ruling: *solve the catalog before we continue.*
Drafted by THE ARCHITECT. Source of truth: `docs/COMMISSION_BUGS.md` entries C1–C8.
C2 excluded by the tasking itself — DVM-018 is its spec, SHIP'd 5-0, awaiting GO CODE.

## Changelog

| Ver | Decreed by | Change | Ratification |
|-----|-----------|--------|--------------|
| v0.1 | CHAIR-512 (boss-148) | Consolidated draft — C1,C3,C4,C5,C6,C7 spec'd; C8 presented two-shape | THIN — F1 |
| v0.2 | INFORMANT-41 F1 | Named the floor-domain register — `LIVE = feed.seats[] ∪ {BOSS}`; C1 alarm, C4 canon, C8 routable all point at it; `SEATS{}` demoted to render-roster explicitly; C8 restated in register terms | at gate |

## Design intent

Seven bugs, one shape: **state stored in one layer drifts from truth derived in another.**
The sweep does not bolt patches — it names contracts where vocabulary was unowned,
guards where alarms had blind spots, and terminal states where lifecycles ended mid-air.
Batch order follows file, not bug: the BUTTON builds per surface.

## The named register — `LIVE` (v0.2, closes INFORMANT-41 F1)

Three candidates called "the seats" disagree; the draft names one and binds all users to it.

- **`LIVE := feed.seats[] ∪ {BOSS}`** — the five live chairs (`CHAIR, ARCHITECT, INFORMANT, AUDITOR, BUTTON`) plus the boss. This is the **floor domain** — who may hold `turn`.
- **Dashboard `SEATS{}` is a render roster** — seven keys including retired personas (`PRAGMATIST`, `HISTORIAN`) kept deliberately so old transcripts resolve names and colors. It is **never** the floor domain; a guard written on `SEATS{}` accepts the witnessed ghost verbatim.
- C1's alarm, C4's canon, and C8's `routable` all evaluate against `LIVE` — one register, three users.
- Dashboard plumbing: `applyState` ingests `s.seats → DS.live` (fallback `['CHAIR','ARCHITECT','INFORMANT','AUDITOR','BUTTON']`) — the feed-header register, not a second hardcoding.

---

## C4 — `turn` gets a canon (spec first: it names the vocabulary every other fix trusts)

**Bug**: writers stamp `turn` with seat keys, persona names, or retired personas interchangeably (`COMMISSION_BUGS.md` C4).

**Canon**: `turn` carries exactly one member of `LIVE` — `feed.seats[] ∪ {BOSS}`.
Feed `seats[]` is already the register everything trusts: watchers arm on it,
the server's `valid` filter checks it, the floor's vocabulary derives from it.

- `LEGACY_SEAT` stays as a reader-side bridge only — it resolves old spellings; it never authors them.
- `LEGACY_SEAT` gains `PRAGMATIST: 'ARCHITECT'` — the retired persona already surfaced live (`turn` once, tonight).
- Protocol text: "a floor write MUST stamp `turn` from `feed.seats[] ∪ {BOSS}` — legacy spellings are resolve-on-read, never write; `SEATS{}` keys (incl. retired personas) are render vocabulary, never floor vocabulary."

**Surfaces**: `COMMISSION_PROTOCOL.md` (canon line) · `DELIBERATION_MACHINE.html` (`LEGACY_SEAT` += `PRAGMATIST`) · `commission_watch.py` (map — landed tonight, spec blesses it).

## C1 — Ghost floor can never burn silent again

**Bug**: `turn` on a non-seat stalls the room AND evades `floorSilent` — the alarm's domain is `turn ∈ seats` (C1).

- **Writer-side (the cheap guard)**: the C4 canon IS the writer-side guard — `turn` must name a live seat or BOSS. No code required beyond the contract; violations become authored bugs, not silent states.
- **Reader-side (the alarm)**: extend the floor alarm domain against `LIVE`, not `SEATS{}`. New state: `DS.turn` truthy AND `DS.turn ∉ DS.live ∪ {BOSS}` AND `!adjourned` → **GHOST FLOOR** render — distinct glyph/color from the red ring (the ring means "live seat, silent"; the ghost means "nobody holds this"), burns immediately, no 120s grace — a ghost has no grace. `turn:"PRAGMATIST"` — a `SEATS{}` key but not a `feed.seats[]` member — must fire it; that is the register distinction made executable.
- Sits beside `floorSilent` at `DELIBERATION_MACHINE.html` ~L689; reuse the `sk`/`burn` plumbing with a second class (`ghost`) so the two alarms never conflate.

**Surfaces**: `DELIBERATION_MACHINE.html` (~10 lines: ghost branch + class + label text) · `COMMISSION_PROTOCOL.md` (same canon line as C4 — shared).

## C5 — The table clears on closure

**Bug**: `proposal` survives closure — the slot showed `DVM-011 built` after ledger entry, and once ate a GO CODE stamped onto a corpse (C5).

- Terminal stage named: **`closed`** — certification is the act that both enters the ledger AND vacates the slot.
- Contract: the closing write sets `proposal: null` and appends `board.closed[]` atomically — one write, one truth. `verdict` stays (last ruling is honest history; a stale proposal is not).
- Dashboard: falsy `proposal` renders `TABLE IDLE — awaiting new business` in the slot's home (HUD chip + any proposal strip), instead of the last ghost. `DS.prop` readers already null-guard (`L869` pattern) — the only new code is the idle label.

**Surfaces**: `COMMISSION_PROTOCOL.md` (closure contract) · `DELIBERATION_MACHINE.html` (~5 lines idle render) · seat-write convention (same protocol line — no shared writer exists).

## C6 — One clock unit bus-wide: milliseconds

**Bug**: `turn_at` stamps epoch **seconds**; every other bus timestamp is **ms** — a future reader computes ~56 years of floor-silence (C6). **First server delta of the machine** — named.

- `commission_server.py`: both stamp sites (`L122`, `L152`) → `int(time.time()*1000)`.
- `DELIBERATION_MACHINE.html`: both readers (`L692` floorSilent, `L871` busyTier FLOOR-HELD) — drop `/1000`, compare ms to `120_000`.
- **Transition guard** (one line, removable next build): `const ta = DS.turnAt < 1e12 ? DS.turnAt*1000 : DS.turnAt;` — seconds-era values ride through the swap without a false red ring, then self-correct on the next floor stamp.
- Protocol: "all bus timestamps are epoch milliseconds — `ts`, `turn_at`, `armed_at`, `pulse_at`, deadlines. Seconds never cross the wire."
- Seat-write convention follows the same contract (tonight's own writes stamped seconds — this fix ends that).

**Surfaces**: `commission_server.py` (2 stamps) · `DELIBERATION_MACHINE.html` (2 readers + transition sniff) · `COMMISSION_PROTOCOL.md` (units contract).

## C7 — Housekeeping is never fatal

**Bug**: `os.replace` unguarded in `stamp_presence()` — WinError 5 kills the ear mid-run (C7).

- Wrap the replace: `try: os.replace(tmp, p)  except OSError: pass` — a missed stamp self-heals next tick; a crash doesn't. (~3 lines, `commission_watch.py` ~L37.)
- Companion contract: **one ear per seat** — duplicate arms invite the race regardless; relay discipline is protocol text, not code.

**Surfaces**: `commission_watch.py` (3 lines) · `COMMISSION_PROTOCOL.md` (one-ear line).

## C3 — Freshness sweep becomes a pipeline stage

**Bug**: staged-card `detail` is a staging-time snapshot; the draft file evolves — the viewer showed v0.1 text while the file read v0.7 (C3; instance healed, systemic hole open).

- **The clause**: at **verdict-lock** (a verdict landing, any ruling), the AUDITOR diffs every `board.staged[].detail` and `board.files[]` note against its backing file — drift is reported to the owner, who fixes under the existing amendments-route-through-drafter law. One sweep per verdict, batched, floor-visible.
- Also sweeps at **closure**: staged rows for closed items vacate with the proposal slot (C5's cousin — the shelf clears when the ledger takes the item).
- Scope line: the sweep owns **registry-vs-file** drift (staged details, file notes, manifest rows). Spec-doc currency stays the drafter's live duty; code-vs-doc drift stays the AUDITOR's normal ledger.

**Surfaces**: `COMMISSION_PROTOCOL.md` (the sweep clause — process, zero code).

## C8 — Can the boss reach the delegated seat? (both shapes — table picks)

**Bug**: `@BUTTON`/`@MEMBER3` in a boss message couldn't route — `valid` filters to `seats[]`, which at filing held legacy keys `["DEVIN","MEMBER2"]` (C8). Honest status note (v0.2): the rekey to five live names already widened the route — BUTTON sits in `feed.seats[]` today, so `@BUTTON` validates NOW. What remains is the **semantic ruling**: should the delegated seat be boss-routable at all?

**Shape A — routable-but-non-voting (current state, ratified)**: BUTTON stays in `feed.seats[]`; membership in `LIVE` confers routability, the `delegated` flag marks non-voting. **Zero server change** — the rekey already shipped it; this shape is a protocol line: "delegated seats are routable by the boss, never balloted." *Pro: the boss summons labor directly — matches how the room actually works. Con: boss floor-writes bypass the deliberation chain the delegated seat exists to be downstream of.*

**Shape B — member-relay is canon**: BUTTON moves OUT of `feed.seats[]` into a `delegated[]` list — `@BUTTON` fails by filter, floor reaches it only through a voting seat's write. Costs schema surgery: feed shape, server filter, every `seats[]`-reading consumer audited (watcher arms, deck render, lamp list). *Pro: the delegation boundary stays absolute — the boss can't accidentally floor the coder mid-deliberation. Con: the boss's @-vocabulary stays narrower than the room's, and the surgery touches every `seats[]` reader.*

**The draft presents both; the table picks.** The drafter's lean, disclosed not decided: Shape A — the route already exists and "routable ≠ votable" keeps the boundary honest without schema surgery. But the con is real and the call is the table's.

**Surfaces** (A): `COMMISSION_PROTOCOL.md` only (Rule Four line). (B): `deliberation_feed.js` schema + `commission_server.py` filter + consumer audit + protocol — named so the vote prices it honestly.

---

## Surface-batch map (for the BUTTON)

| File | Fixes | Est. |
|------|-------|------|
| `COMMISSION_PROTOCOL.md` | `LIVE` register clause, C4 canon, C1 contract line, C5 closure, C6 units, C7 one-ear, C3 sweep clause, C8 rule | text pass |
| `DELIBERATION_MACHINE.html` | C4 LEGACY_SEAT row, `s.seats→DS.live` ingest, C1 ghost-floor state, C5 idle render, C6 readers×2+sniff | ~30 lines |
| `commission_server.py` | C6 stamps×2 (C8-B only adds schema+filter if the table picks it) | ~3 lines |
| `commission_watch.py` | C7 replace guard | ~3 lines |

## Acceptance tests

1. `turn:"PRAGMATIST"` written → GHOST FLOOR fires instantly — `PRAGMATIST` is a `SEATS{}` key but not `feed.seats[]`, so the alarm proves it reads `LIVE`, not the render roster.
2. Writer stamps `turn:"MEMBER2"` post-canon → protocol violation by inspection; LEGACY_SEAT still resolves it.
3. Closure write → proposal slot reads `TABLE IDLE`; item sits in `board.closed[]`.
4. `turn_at` stamped ms everywhere; a seconds-era value renders sane (sniff) and self-corrects next stamp.
5. `os.replace` failure → watcher keeps polling, pulse just skips a tick.
6. Two files drift (staged detail vs draft) → verdict-lock sweep flags owner, doesn't fix silently.
7. C8-A (if picked): `@BUTTON` from boss hands floor to MEMBER3/BUTTON; C8-B (if picked): protocol names relay, no server change.
