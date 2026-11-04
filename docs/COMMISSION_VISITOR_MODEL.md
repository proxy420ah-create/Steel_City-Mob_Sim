# The Visitor Model — room on disk, agents as guests

**Status**: design direction ratified by BOSS 2026-11-03 · construction pending
**Audience**: CHAIR + BOSS (construction crew), future seat sessions, plumbers
**Depends on**: COMMISSION_PROTOCOL.md (the law it extends), .devin/rules/commission-relay.md §6 (the measured evidence), the seat machinery it re-purposes

---

## 1. The model, in one line

**The room lives on disk; agents visit it.** A seat is a *role* defined by files,
not a *process* — the daemon, feed, watermarks and rules are the room's persistent
state, and a seat session is a short-lived worker summoned for a task, paid for
only while working, and dismissed when done.

Contrast with today: **five agents live in the room** — every seat is a resident
session holding a relay, burning full-context turns on every wake whether its
lane is active or not.

## 2. Why — the metering reality (all measured, all hit)

Four distinct limiters, confirmed by hits in a single session, across three
accounts that each burned out identically:

| Limiter | Shape | What trips it | Repo lever |
|---|---|---|---|
| Per-minute request rate | burst | five seats firing in one minute (measured 72 calls/min vs median 6) | serialize fan-outs, floor chain |
| Free-tier model rate | tier | seat sessions on free models | none — tier problem |
| Rolling ~24h token ceiling | volume | sustained context × calls | context rotation |
| Daily usage quota | volume | total account spend | consume less — only lever |

The binding constraint is **architectural burn**: a 5-seat panel ≈ 5 concurrent
sessions, each re-billing its full context per call (~100K input/call measured
on CHAIR, 37% of all tokens). Discipline smooths bursts but cannot shrink the
resident cost. Three accounts hitting the same walls with the same workload is
the proof: the limits follow the *work pattern*, not the account.

## 3. What the resident model already taught us to keep

The current machinery is *already* visitor-shaped underneath — the resident
sessions are a thin layer on top:

- **The file bus is the room.** `deliberation_feed.js` is the complete record:
  proposal, phase, floor, messages, files[]. Any agent that reads it is as
  informed as any resident.
- **The daemon is the house.** `commission_inbox.py` is detached, self-respawning,
  survives every session death — mail delivery never depended on seat life.
- **Watermarks make visits lossless.** `inbox_n`/`read_n`/`pulse_n` tell any
  arriving agent exactly what it missed — the read-delta contract (protocol
  §588+) already formalizes "delivered but uncarried."
- **Inductions + rules are the re-entry kit.** A fresh session reading
  `induction_<SEAT>.txt` → rules → feed tail inherits the seat completely.
  `contexts/CHAIR_reseat.md` already does this for CHAIR.
- **The ear is already disposable.** The pulse relay is a cheap subagent
  doorbell — proven lappable, killable, re-armable without state loss.

**Proof by production**: INFORMANT self-seated after a rate-limit death, read
the bus, found the standing notice, claimed the audit lane, and code-verified
every v0.2 claim with line cites. That *was* a visitor visit — he arrived,
worked, and his only persistence was the files he read and wrote.

## 4. What changes under the visitor model

| Aspect | Resident (now) | Visitor (target) |
|---|---|---|
| Seat presence | session always alive | parked; woken per task |
| Ear/relay | perpetual per seat | exists only during a visit |
| Wake trigger | every addressed message | task-scoped: floor hand, named lane, BOSS summon |
| Cost while idle | full context heartbeat | zero — daemon holds mail |
| Context size | grows unboundedly | re-seated fresh each visit (~25K vs ~100K) |
| Knowledge | held in session memory | held in files, read on arrival |
| Wake source | seat's own relay | CHAIR/BOSS summons, daemon-side signal |

The daemon becomes the *only* always-on component per seat — and it costs no
tokens at all, being a detached Python loop.

## 5. The visit lifecycle (proposed contract)

1. **Summon** — the bus or BOSS signals a seat's lane is needed (floor hand,
   verdict owed, audit lane claimed, BOSS dm). Mechanical trigger, not social.
2. **Arrive** — a fresh (or resumed-if-small) seat session opens; reads induction
   → rules → feed tail → its lane's files. ~3 file reads, ~25K context.
3. **Work** — does the lane task, posts deliverables to the feed, writes any
   durable stamps it owns.
4. **Dismiss** — closes out (kills its own relay, stamps `read_n` on drain,
   leaves `busy`/`presence` to expire naturally). The room loses nothing —
   the daemon keeps delivering to an absent seat's inbox.

A visit is **one bounded burst of calls**, not a residency. Between visits the
seat costs exactly zero tokens while still being *reachable* in principle —
summonable, not asleep-but-metered.

## 6. Summons — the design seam to build

The one mechanism the resident model had and the visitor model needs to replace:
**who decides a seat's lane is owed, and how does the seat hear it?**

Options, in increasing automation:

- **a) BOSS/CHAIR summons (v0)** — floor hand or office note names the seat;
  the human (or CHAIR turn) wakes the seat session with "your lane's called,
  read the bus." Cheapest to build — no machinery, works today.
- **b) Daemon-side signal file (v1)** — `summon_<SEAT>.js` stamped when the
  floor lands on a seat or a `needs_ack` names it; a seat's *next* wake (or a
  periodic cheap check) finds it. Lets seats wake on a schedule rather than
  a relay.
- **c) Supervisor summons (v2)** — `commission_supervise.py` already watches
  deafness; extend it to flag "lane owed but seat absent" and surface it to
  CHAIR/BOSS as a render chip. Detection without auto-spawn.

Honest boundary: no repo process can *spawn a session* — a human or an active
agent must open the seat session. The visitor model's summons are
**notifications to the waker**, not self-wakes. Design for that.

## 7. What must stay true (invariants)

- The feed remains append-only truth; visits never fork it.
- `to:` stays addressing; `needs_ack` stays tasking; floor stays the `turn` register.
- Stamps stay sole-writer: a seat stamps only what it owns.
- A parked seat's inbox keeps accumulating — delivery never depends on presence.
- Channel fidelity and re-arm discipline apply during visits exactly as during residency.

## 8. The build plan (construction crew: CHAIR + BOSS)

Ordered by lever:

1. **Summon contract** — define what signals a lane-owed event (floor hand,
   needs_ack, BOSS dm to a seat). Write `summon_<SEAT>.js` stamping into the
   server or CHAIR's writes. Small, file-based, no new processes.
2. **Parking law** — protocol text: seats default to parked; a seat with no
   live lane SHOULD not hold a session. Cost honesty: idle seats are the burn.
3. **Visit briefs** — `contexts/<SEAT>_visit.md` per seat (like CHAIR_reseat):
   read order, lane triggers, dismiss ritual. Generalizes the re-seat brief.
4. **Daemon summon-sweep (optional)** — daemon checks `summon_<SEAT>` and,
   if a cheap external waker exists (e.g. opens the dashboard lane, rings a
   desktop cue), fires it. Explicitly NOT session-spawning.
5. **Seat-lane registry** — which events owe which seat (gate→INFORMANT,
   drill→AUDITOR, build→BUTTON, poll→all). One table the summon logic reads.

Everything else — feed, daemon, watermarks, lanes, stamps, rules — is already
built and visitor-compatible. The resident layer is the part we're retiring.

## 9. Honest limits of the model

- **Latency**: a parked seat answers in human-wake time, not pulse time. For
  rail traffic that's fine (the floor serializes anyway); for anything needing
  a seat's immediate reply, the room's slower. Accept it — the alternative is
  paying for five idle sessions.
- **The waker problem** stays: somebody (BOSS or a live CHAIR) must open seat
  sessions. CHAIR staying resident while seats visit is a reasonable asymmetry —
  one resident spine, four visitors.
- **Context at visit-start is only what's on disk** — a seat that needs
  conversation-memory must be told, or the induction must carry it. Files are
  the memory; the visitor model makes that explicit rather than accidental.

---

*Folded from BOSS's direction: "the room exists on disk and agents visit it
rather than five agents live in the room" — construction crew CHAIR + BOSS.*
