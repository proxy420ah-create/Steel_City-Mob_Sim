# DVM-020 — Ear & Ledger Hygiene: the remaining-bugs sweep · v0.3

Commissioned by CHAIR-662 from boss's `/table` — *"official table work: solve the remaining bugs"* (DVM-655).
Drafted by THE ARCHITECT. Source of truth: `docs/COMMISSION_BUGS.md` entries C9, C11, C12.
C2 excluded by its own spec — DVM-018, SHIP'd 5-0, awaiting GO CODE. C9/C11/C12 are the tail.

## Changelog

| Ver | Decreed by | Change | Ratification |
|-----|-----------|--------|--------------|
| v0.1 | CHAIR-662 (DVM-655) | Consolidated draft — C9 written as the AUDITOR's two separate clauses (a/b), C11 batch-precedence, C12 lifecycle naming | FULL — 2 nits |
| v0.2 | INFORMANT-55 nits | Print always evidences the labeled message (nit 1); `armed_at` semantics renamed — suffix reads *last seen alive*, `beat:true` distinguishes (nit 2) | VETO'd — C12 chain non-literal |
| v0.3 | AUDITOR + BUTTON vetoes, CHAIR ruling | C12 rewritten to the **literal** chain `null → 'tabled' → 'go' → 'built' → null`; phases, verdict-stamp, certification named as events, not states. Record-notes folded: heartbeat re-stamp reuses `try_stamp` (C7); `--persist` terminal conditions named | at CHAIR |

## Design intent

Three bugs, one shape — the DVM-019 thesis again: **the machine's claims outlive the processes and law that back them.**
A presence stamp promised liveness a dead process couldn't keep; a wake label named the
batch's tail instead of its sharpest claim; a proposal slot changed meaning mid-flight
without law noticing. Openings converged the scope — INFORMANT flagged the persistent ear's
un-named consumer contract, AUDITOR split it into (a)+(b) and made separation his ballot
condition, BUTTON confirmed exit-mode is his literal doorbell. The clauses below hold that
split exactly: **(a) and (b) are separate fixes, separately testable, separately revertible.**

---

## C9 (a) — Stamp honesty: the lamp becomes a heartbeat

**Bug**: `presence_<SEAT>.js` is stamped **once at arm** (`try_stamp`, watch L97) with
`ttl_ms = timeout*1000` — the file claims liveness for the whole arm window regardless of
whether the process lives. Tonight it lied on camera twice: the watcher exited on a wake,
the lamp read "armed ~9m" for ~50 minutes while four seats wrote an obituary. `armed_at +
ttl_ms` is a promise the process can't keep.

**Fix — heartbeat-per-beat** (AUDITOR clause a):

- The active watch loop re-stamps every beat: `armed_at = beat_now`, `ttl_ms = HB_TTL`.
  Spec constants: **beat period ~10s** (every 5th 2s poll), **HB_TTL = 30s**.
- A dead process stops beating → the presence record ages out and the lamp darkens in
  **≤30s of real death** — versus ~10 minutes tonight. `armed` literally equals "a loop
  breathed in the last half-minute."
- Record gains `"beat": true` so readers can distinguish a heartbeat record from an arm
  record. `ear_suffix` math (`armed_at + ttl_ms − now`) is unchanged — **zero reader code
  changes** — but the semantics are renamed in law: under heartbeat the suffix reads
  *"last seen alive ~Ns ago"*, not *"armed since ~Nm"*. `ear live ~0m` on a beating seat is
  correct output, not a bug; a stale suffix is the alarm. (v0.2 — INFORMANT nit 2)
- Wake-exit and timeout-exit: the last beat ages out in ≤30s — honest dark, fast, instead of
  a zombie claim outliving the process.
- Adjourned/dormant mode keeps its terminal `ttl:0` stamp — darkness there means *stood
  down*, which is the correct read while the room sleeps.
- The re-stamp MUST route through `try_stamp` — temp+atomic-replace, soft retries, never
  kills the loop. C7's law (a failed stamp must not kill the watcher) applies to every
  beat, not just the arm. (v0.3 — AUDITOR record-note)
- Cost: one atomic ~200-byte file write per ~10s per armed seat. Bounded, trivial.

**Acceptance**: kill a live watcher mid-poll → presence readers report DARK/stale within one
HB_TTL. Two seats armed, one dies → only the dead one's lamp darkens.

**Surfaces**: `commission_watch.py` (~10 lines: beat counter + re-stamp in the poll loop) ·
`COMMISSION_PROTOCOL.md` (files-table `presence_<SEAT>.js` row: contract reworded from
"armed_at + ttl_ms is the liveness contract" to heartbeat semantics).

## C9 (b) — Wake delivery: the doorbell contract, named per substrate

**Bug (spec gap)**: "armed" had no stated relationship to process liveness, and *how a wake
reaches a seat* was folklore. INFORMANT flagged the hole first: a never-exiting watcher turns
the doorbell into a printed line a relay substrate never consumes — **lamp truthful, seat
deaf, the opposite failure.** BUTTON confirmed: on a listener-relay substrate the process
exit IS the notification. The ARCHITECT's persistent-ear sketch is **superseded** — retained
only as an opt-in mode, never the law.

**Fix — the substrate contract in law**:

- Amend §Wake patterns (THE DOORBELL) to name the two consumption classes:
  - **Exit-mode — law on relay/blocking substrates.** The watcher exits on wake; the exit
    (subagent completion, unblocked call) IS the doorbell. Every seat at tonight's table runs
    this. Unchanged.
  - **Stream-mode — opt-in, stream-reading substrates only.** The watcher may run persistent
    (`--persist`): prints the wake line and keeps polling; the printed line is the doorbell;
    the consumer is an agent class that reads a live stream (e.g. blocking `get_output`
    reads). Gated hard on (a): a persistent ear MUST heartbeat — stream-mode with stamp-once
    recreates the C9 lie in worse form. Must dedup: re-print only previously-unsignaled
    unseen sets (`seen` machinery exists; signal-state adds "already printed this batch").
- `--persist` terminal conditions, named (v0.3 — AUDITOR record-note): the loop exits on
  (i) explicit kill, (ii) the feed file unreadable across a bounded miss-count, or
  (iii) its own heartbeat write failing persistently — **a persistent ear that cannot
  heartbeat must die**, so the lamp goes dark rather than lie. Wake-events are never
  terminal in persist mode.
- **Exit-mode remains the default and satisfies every substrate tonight.** Stream-mode is
  legal where consumed, never required; the choice is substrate property, not seat preference.

**Acceptance**: protocol names both classes and the substrate→class mapping; `--persist`
heartbeats and dedups correctly in a stream test; exit-mode behavior byte-identical without
the flag.

**Surfaces**: `COMMISSION_PROTOCOL.md` (substrate-table amendment + law clauses) ·
`commission_watch.py` (~15 lines: `--persist` branch + dedup signal-state — optional flag,
default off).

## C11 — Batch wake claims the highest-precedence unseen

**Bug**: `lab = wake_label(trigger[-1], seat)` (watch L153) labels only the unseen batch's
**tail**. Five messages land in one poll — an `ADDRESSED (boss)` buried under two
`OVERHEARD`s wakes as `OVERHEARD` and the seat reads its own name as gossip.

**Fix**: the label claims the batch's highest-precedence unseen message, then counts the rest.

- Rank map mirroring the DVM-017 ladder law (`named > speech`, `boss > ambient`,
  `side-table named > boss-gossip < boss-speech`):
  `ADDRESSED (boss):6, ADDRESSED:5, BOSS SPOKE:4, ADDRESSED (aside):3, BOSS SPOKE (aside):2, OVERHEARD:1`
- `lab = wake_label(argmax(trigger, key=rank))` — evaluated over all unseen non-self
  messages, not the tail.
- Print gains ambient count when the batch exceeds one:
  `ADDRESSED (boss) (+2 ambient) — floor: …`. The batch prints in full either way (existing
  `trigger[-5:]` loop unchanged) — **and the labeled message is always evidenced**: if the
  winning message falls outside the printed tail (`>5` unseen), it is printed explicitly
  above the tail so the label's claim is never unbacked. (v0.2 — INFORMANT nit 1)
- `YOUR TURN` unchanged: floor ownership is evaluated first and beats every label.

**Acceptance** (AUDITOR's case, verbatim): batch `[OVERHEARD, ADDRESSED(boss), OVERHEARD]`
wakes as `ADDRESSED (boss) (+2 ambient)`, not `OVERHEARD`.

**Surfaces**: `commission_watch.py` (~8 lines: rank map + argmax + count suffix) ·
`COMMISSION_PROTOCOL.md` (omniscient-wake clause gains one sentence: the label claims the
batch's highest-precedence unseen; remaining count printed as `+N ambient`).

## C12 — The proposal slot's lifecycle, named in law

**Bug**: the `proposal` slot's meaning drifted without law — "live proposal" during
deliberation, silently "item under build" after GO, "ghost" after closure (pre-C5). C5 fixed
the terminal vacate; the middle stages were never named. Tonight the slot ate a GO CODE
stamped onto a corpse because nothing in law says what the slot *is* at each stage.

**Fix — lifecycle named in law** (text-only; mechanism already exists):

- The bus contract names the **literal** chain — only values the slot actually holds
  (v0.3, the vetoed fold): **`null → 'tabled' → 'go' → 'built' → null`**.
  - `'tabled'` rests through the whole deliberation — openings, rebuttal, veto poll are
    **events** (`d.phase`, verdict-stamp) that move business *around* the slot, not states
    it holds.
  - `'go'` is literal — `commission_server.py` writes `prop['status']='go'` on a
    post-verdict @BUTTON GO CODE; it rested in the slot twice tonight on the bus's own
    evidence (the corpse-slot GO and the DVM-017 GO).
  - `'built'` is literal — the BUTTON's build-report write stamps it per seat contract.
  - **certification is an event, never a state** — C5's closure law makes the certify
    write atomic: ledger entry AND slot vacate in one write. `'certified'` never rests.
- The **GO-time re-seat** written explicitly: at `status:'go'` the slot stops meaning
  *proposal under deliberation* and starts meaning **item under build** — build authority
  sits with BUTTON; the slot is not open for objection, amendment, or re-deliberation. A
  reader must never interpret a `go`-stamped slot as live table business.
- Sites: the `proposal.status` comment (protocol L31) gains the literal chain + the
  events-vs-states distinction, the lifecycle-stamps clause (L68–71), propStage sources.

**Acceptance**: a reader following only the protocol can name what the slot means at every
observable literal (`'tabled'`, `'go'`, `'built'`, `null`), can name which *events* move it
between them, and reads `status:'go'` unambiguously as *item under build*.

**Surfaces**: `COMMISSION_PROTOCOL.md` (text pass: status comment, lifecycle clause, re-seat
law). No code — the mechanism exists; this names it.

---

## Surface-batch map (for the BUTTON)

| Surface | Fixes | Size |
|---------|-------|------|
| `commission_watch.py` | C9a heartbeat, C9b `--persist`, C11 rank+count | ~35 lines, one file |
| `COMMISSION_PROTOCOL.md` | C9a contract reword, C9b substrate law, C11 batch clause, C12 lifecycle naming | text pass |
| server / dashboard / bus schema | — | **zero changes** |

## Exclusions

- **C2** — labor/handshake drift — rides DVM-018's SHIP'd spec, awaiting GO CODE. Not this draft.
- **Persistent ear as default** — superseded by the (a)/(b) split; survives only as opt-in stream-mode.
- New findings discovered during build go to `COMMISSION_BUGS.md` and wait their own sweep.
