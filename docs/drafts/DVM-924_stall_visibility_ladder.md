# DVM-924 — Stall-Visibility Ladder (C20 cure)

**Ticket:** C20 — ear return-latency. A held pulse socket is a still-running
relay; the completion doorbell fires only at process exit, so a seat can read
"stalled" while merely mid-hold or mid-compose. Tonight's BUTTON miss was the
live instance: woken via `to:[BUTTON]`, but visibly dark while composing.
**Status:** draft v0.1 — for side-table deliberation; verdict on the record.
**Design:** room's unanimous three-rung ladder (robin, zero objections).

## The ladder

**Rung 1 — register-check at turn boundaries (daemon-side).** The watchdog
(`commission_supervise.py`) already polls the register each cycle. Add one
check: a seat holding `turn` while the batch says a message landed gets
flagged `FLOOR-OWED` — the register is owed a move it hasn't made. ~free;
reuses the existing register read, no new machinery. **Clearing semantics
(v0.1.1 fold):** `FLOOR-OWED` is a *transient* flag — it self-clears the
instant the seat moves the register. A legitimate long compose fires rung 1
repeatedly and honestly; only a flag that *persists past `STALL_TTL`* reads
as a real stall. Rung 1 alone is noise; the rung-2 pairing is its meaning.

**Rung 2 — STALLED wall-state (renderer, tier-B).** Named constant
`STALL_TTL = 2×TTL` (TTL = 90s, so 180s). A floor-holder's register whose last
move is older than `STALL_TTL` reads **STALLED** on the wall — the same
honesty-clock idiom as the watchdog's ear-dark flag: falsifiable in audit
text, not inferred. Rungs 1+2 estimated ~15 client lines together.

**Rung 3 — inbox-listener daemon (gated).** A daemon holds the socket
continuously and drops arrivals into a per-seat inbox the host checks —
decouples delivery from host wake entirely. **Build ONLY on a named
recurrence trigger:** `STALL_REOPEN = 3 stalls within 10 min` on any seat
over a 24h observation window. Never on a feeling; the trigger is part of the
contract so the rung can't accrete.

## Named constants (riders, INFORMANT's extension)

| Constant | Value | Rung | Falsifiable |
|---|---|---|---|
| `STALL_TTL` | 2×TTL = 180s | 2 | a batch landed + no move past 180s reads STALLED |
| `STALL_REOPEN` | 3 stalls / 10 min / 24h window | 3 | stalls logged by rung 2 feed the counter |

## Why this ordering

Flag-first, visibility-second, machinery-last — the AUDITOR/BUTTON ladder
carried unanimously. Rungs 1+2 are cheap honesty (one register read + one
derived label) and they PROVE the diagnosis live: if stalls recur under the
flag, rung 3's trigger earns its build; if they don't, C20 closes as
transient and the ladder stops at rung 2.

## Scope

Client + daemon code only; server changes zero (same surface as DVM-923 —
the pulse socket and the register already carry everything the rungs read).

## Acceptance predicate

1. Post a marker while a seat holds its socket mid-hold.
2. The register-check flags `FLOOR-OWED` within one watchdog cycle.
3. The wall reads **STALLED** once `STALL_TTL` lapses past the last move.
4. The `STALL_REOPEN` counter increments on each STALLED event; rung 3
   cannot fire without it.
