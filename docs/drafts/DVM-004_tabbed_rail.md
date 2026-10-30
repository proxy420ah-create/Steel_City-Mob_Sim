# DVM-004 — Tabbed roster rail + render-chain hardening — design v0 (THE ARCHITECT)

**Boss's brief (via CHAIR, devin-45):** the rail becomes two tabs —
`SEATS` (default, seat cards + ear chips) and `LEDGER` (rules + open business
+ closed collapsible). Root cause to kill: the no-scroll shell clipped
`#board` — ledger rendered but unreachable.

## Audit finding — a second bug, and the truer root cause

The clip theory explains reach, not absence. Reading the live render chain:

- `applyState` line 374: `if (s.verdict) renderVerdict(s.verdict.result, …)`
- The live verdict is `{id, ruling, summary, unanimous}` — `.result` does
  not exist → `renderVerdict(undefined,…)` → line 355
  `result.toUpperCase()` → **TypeError, every poll**
- The throw kills the tail: `renderBoard` (rules/agenda/closed) and
  `turnlbl` never execute — dead since the verdict landed (devin-20)
- Side damage: the `onload` handler skips `pollTag` cleanup → every poll
  stalls ~8s on the wedge-reset → the feed itself runs degraded
- Consistent with evidence: messages flow (ingest precedes the crash),
  rules "absent for a while" (crash began at verdict), boss-38 saw no
  accordion post-refresh (fresh page, fresh crash)

**Ruling:** the tab build must co-ship the render fix — else the LEDGER
tab opens onto an empty board and the fix reads as failed.

## Contract

### Fix A — render chain (prerequisite)

1. `applyState`: pass the live schema —
   `renderVerdict(s.verdict.ruling, s.verdict.summary, s.verdict.conds)`
2. `renderVerdict`: extend the maps — `ship`/`SHIP` → approved styling +
   `✓ SHIP` word; add the guard
   `(result || 'awaiting').toString().toUpperCase()` — no throw on drift
3. Isolate sections: each `applyState` block gets a `safe(fn)` wrapper
   (try/catch → console.warn) — one bad renderer can never starve the tail

### Feature B — tabbed rail (the brief)

- **Markup**: rail gains a tab strip — `[ SEATS ][ LEDGER ]` — above two
  views. `#view_seats`: seat cards + intro hint. `#view_ledger`:
  `#board` (rules + open business) + `#closedwrap` (existing collapsible).
- **Behaviour**: SEATS active by default every load (boss's stated default);
  click swaps `.active` on tab + view — two lines of JS, no state file.
- **Badge**: `LEDGER (N)` where **N = `board.closed.length`** — the win
  count the boss already tracks; rules and open business are ambient, not
  badge-worthy. Survives contract review on the lean reading.
- **CSS**: `.railtabs` strip + `.tabview{display:none}` / `.active`.
  `#view_ledger` keeps `overflow-y:auto` as the escape valve — the page
  stays no-scroll; a growing ledger scrolls inside its own tab only.

### Files / cost

- `DELIBERATION_MACHINE.html` only: ~15 lines markup shuffle, ~25 CSS, ~25 JS
- Feed, server, watcher, protocol: untouched

## Failure modes

| Case | Behaviour |
|---|---|
| Verdict schema drifts again | guard renders `AWAITING`, board still draws |
| Any renderer throws | `safe()` contains it; rest of state still applies |
| `board.closed` absent | badge shows `(0)`, collapsible empty |
| Ledger outgrows the rail | internal scroll inside LEDGER tab only |

## Table: attack points

1. Co-ship scope — is the crash fix in DVM-004's lane, or split as DVM-005?
   My ruling: same file, same symptom chain — one ticket.
2. Badge semantics — `closed.length` vs `agenda.length` vs both.
3. Escape valve — internal scroll on the ledger tab vs the boss's
   no-scroll doctrine.
