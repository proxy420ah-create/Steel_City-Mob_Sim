# DVM-921 — The Watchdog: supervisor-rearm + dual-stamp aliveness · v0.1.1

Commissioned by boss-DVM-921 — *"fast track dvm 22 since it is impeding work
meaning it wont logically make sense to continue until this is resolved."*
Presented by CHAIR-843; openings closed converged (CHAIR-845 synthesis, ten points).
Drafted by THE ARCHITECT. Closes C17 and the re-arm gap that stalled a veto poll.

## Changelog

| Ver | Decreed by | Change | Ratification |
|-----|-----------|--------|--------------|
| v0.1 | CHAIR-845 (synthesis) | Charter draft — all ten consolidated points carried verbatim | FULL — 1 must-fold nit |
| v0.1.1 | INFORMANT must-fold nit (CHAIR-846) | Carrier-ownership collision cured: flag-only on relay substrates (post, don't respawn); supervisor heartbeat made a real stamp; idle-alive gap consequence named | at gate |

## Design intent

Tonight produced the exhibit reel before the design: three ARCHITECT dark
lapses, a veto poll stalled on a deaf seat, a room reset, and a persist lamp
beating over a dead host. Every one is the same anatomy — **the ear is
expendable by contract (exit IS the doorbell), and nothing owns the re-arm.**

The fix is not "a process that restarts watchers." It is an **honesty
upgrade**: the room learns to tell *loop-alive* from *agent-alive*, and only
then earns the right to resurrect loops. The two halves ship as one fix —
rearm without aliveness manufactures C17's lie by our own infrastructure.

## The two stamps (② — the load-bearing precondition)

The room's records split into two files, two writers, two meanings:

| File | Sole writer | Proves | TTL'd by |
|------|------------|--------|----------|
| `presence_<SEAT>.js` | the watcher | **loop-alive** — an ear is polling | watcher re-stamps per beat (~10s), dark ≤30s after loop death |
| `busy_<SEAT>.js` | the agent host | **agent-alive** — a host is doing work | agent re-stamps per work-beat, dark ≤30s after host death |

`busy_<SEAT>.js` gets its **own file** (not a field inside presence): two
writers on one record is the C7 replace-race class — sole-writer-per-file
stays law.

**Work-beat convention (AUDITOR's close):** the agent stamps `busy:true`
*before* starting labor, re-stamps while working, and clears on completion —
plus a session-start stamp on arrival (arrival is a work-beat; it keeps the
respawn predicate uniform with no first-arm exception). The mark is honest
because a dead host can't stamp, and bounded because it decays.

## The supervisor (① — one daemon per box)

`commission_supervise.py` — a single daemon process per machine:

- Scans `seats[]` from the feed + every `presence_*.js` + the process list
  (carrier-predicate: count every process whose CommandLine contains
  `commission_watch.py <SEAT>` — watcher AND spawn-shells, the BUTTON
  amendment inherited by the machine itself).
- Runs the **respawn predicate**; spawns `python -X utf8 commission_watch.py
  <SEAT> 600` detached when it fires.
- Posts every **state transition** to the bus as `kind:'sys'` (floor-free).

### The respawn predicate (②④⑤ — ALL conditions required)

Respawn a seat's ear iff **all** hold:

1. **Zero carriers** — no watcher, spawn-shell, or relay carrying the seat.
2. **Beat-stale beyond grace** — presence stamp stale > `N×TTL` (N=2 ⇒ ~60s).
   The grace window closes the mid-spawn TOCTOU: a host mid-spawn isn't yet a
   carrier, and inventory-then-respawn over a spawn-in-flight is the ear wars'
   own mechanism, institutionalized. Grace absorbs it.
3. **Not zeroed** — a zeroed presence record (`armed_at:0`) is *commanded
   dark* by `_ear_triage.ps1`. Zeroed ≠ stale: never a respawn target, or the
   daemon un-kills triaged seats and restarts the ear wars every stand-down.
4. **Agent-alive** — `busy_<SEAT>.js` fresh inside TTL. **INFORMANT's
   condition:** respawn only over a live-agent mark. This is what stops the
   daemon from respawning a beating lamp over a dead host — the exact C17
   lie, at scale, if omitted.
5. **Not adjourned** — recess keeps ears down by design.
6. **Blocking substrate only (v0.2 — carrier-ownership cure).** On
   relay-class seats the daemon is **flag-only**: it posts the `sys`
   dark-detected transition and does NOT respawn. A daemon-spawned watcher
   would satisfy the host's own carrier check ("any carrier = already armed")
   while delivering no doorbell — starving the relay chain it backstops.
   Flag-only keeps the host's carrier inventory truthful: the seat reads
   dark on the process list, its own relay re-arms it on the next wake.
   Blocking-class seats (foreground watchers) get the respawn; relay seats
   get the flag. Seat→substrate mapping lives in the daemon's config,
   default `blocking`.

The daemon is a **backstop, not the bootstrap**: hosts arm their own first
ear per induction/return law; the supervisor catches *lapses*.

**Named consequence (the idle-alive gap):** an idle host — alive but not
stamping `busy` for >30s — whose ear dies will not be respawned, because
condition 4 reads the busy mark as its agent-alive proof. The lamp stays
dark over a live-but-quiet host. Accepted trade: honesty over coverage —
the alternative (respawn on stale lamp alone) is the lie this spec exists
to kill. Mitigation, not cure: the session-start stamp keeps the window
small.

### Surfacing (⑥ — transition-only posts)

Sys-posts fire on **state transitions only** — dark-detected once, respawned
once, relit once — never per scan cycle. A flap loop (respawn→die→respawn)
must not spam the transcript AND ring every ear per post; that's the nudge
bill at machine scale. The bus is the agents' only shared sense — the
AUDITOR stall went unseen because dark lamps live on a dashboard agents
don't watch. The watchdog reports where the room reads.

### Watchdog watched (⑧ — one level, honestly bounded)

The supervisor is itself a process that can die — and we do not build a
meta-supervisor. **One level:** the supervisor stamps
`presence_SUPERVISOR.js` with the same heartbeat shape (re-stamp per scan,
30s TTL, `beat:true`), so the room reads *its* dark lamp the same way it
reads any seat's — a stamp, not inference-by-absence. Its respawn is
**host-boot responsibility** — the operator starts it with the server.
Named bound, per INFORMANT: no recursion.

## Per-substrate honesty (③⑨ — what the daemon CANNOT do)

| Substrate | Respawn delivers | Cannot deliver |
|-----------|------------------|----------------|
| **Blocking** (foreground watcher a host reads/`get_output`) | lamp-honesty + bus-visibility + **the doorbell itself** — the printed wake resumes the loop | — |
| **Relay** (subagent-completion wakes) | lamp-honesty + bus-visibility | **agent-doorbell** — a detached watcher's exit notifies nobody; the wake is the *subagent's completion*, and that chain lives inside the host's session. Agent resurrection stays the host operator's job |

The daemon's guarantee is exactly lamp-honesty + bus-visibility on every
seat — anything more on relay substrates is the overclaim that spawned C17.

## Lifecycle rule (⑩ — staged-restore, no fifth literal)

`/table` during an open poll carries the incumbent proposal to
`board.staged[]` and re-seats it on closure — reuses the DVM-012 machinery
instead of adding a fifth literal state every reader must learn. Consistent
with CHAIR-844's ruling: mid-verdict ≠ stalled; the incumbent doesn't
vanish on a fast-track.

## Named failure-paths (AUDITOR's ballot conditions — all carried)

- **Mid-spawn TOCTOU** → predicate condition 2 (grace window).
- **Sys-post flood** → transition-only posts.
- **Stamped-busy-then-idle** → work-beat convention (stamp-before-labor,
  re-stamp-while-working, clear-on-done) + TTL decay bounds the lie to ≤30s.
- **Watchdog recursion** → one level, bus-observable, host-boot respawn.
- **Dead-host lamp resurrection** → predicate condition 4 (agent-alive mark).
- **Triage un-kill** → predicate condition 3 (zeroed = commanded dark).

## Acceptance

1. Kill a seat's watcher mid-meeting: lamp darkens ≤30s (existing law), the
   daemon posts `sys` dark-detected once, waits grace, respawns the ear —
   exactly one carrier afterward (recount by predicate).
2. Kill a seat's *host* (dead agent, dead ear): daemon posts dark-detected,
   does **not** respawn (no agent-alive mark); lamp stays honestly dark.
3. Run `_ear_triage.ps1` then watch: zeroed stamp produces no respawn —
   commanded dark honored.
4. Supervisor killed: the room sees its silence at the next transition
   check; restarting it resumes coverage without state rebuild.
5. Busy mark: seat stamps busy → WORKING reads; host dies mid-work → mark
   expires ≤30s → IDLE; seat never stamps → never claims labor.
6. `/table` during an open poll: incumbent lands in `board.staged[]` and
   re-seats when the tabled item closes.

## Surfaces

| Surface | Change | Size |
|---------|--------|------|
| `docs/commission_supervise.py` | **new** — seat scan, carrier inventory, respawn predicate, detached spawn, transition-only sys-posts | ~100–150 lines (BUTTON's bench) |
| `busy_<SEAT>.js` writer | ~15-line helper any seat calls — stamp/clear the mark | tiny |
| `docs/COMMISSION_PROTOCOL.md` | respawn predicate, zeroed≠stale, dual-file aliveness, per-substrate guarantee, staged-restore law | text |
| `docs/commission_server.py` | `/table` eviction → `board.staged[]` carry + re-seat on closure | ~15 lines |
| `docs/DELIBERATION_MACHINE.html` | busy mark render (WORKING tag reads `busy_<SEAT>.js` + computed state) | small |
| `docs/COMMISSION_BUGS.md` | C17 → FIX APPLIED on build | ledger line |

## Exclusions

- **Agent resurrection** — the daemon cannot restart a dead host session;
  that stays the operator's (boss's) job, named not implied.
- **Meta-supervision** — one watchdog level, observably; no recursion.
- **Self-rearming watchers** — deliberately NOT chosen: a watcher that
  spawns its successor chains forever, survives reboots poorly, and is
  indistinguishable from zombie litter. External daemon with a registry is
  the honest shape.
- **Persist mode** — remains banned on relay substrates (existing law;
  unchanged).
