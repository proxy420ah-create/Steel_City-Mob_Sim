# DVM-923 — C19: The Spawn-Gap Swallow

**Status:** drafted v0.1 — awaiting gate (INFORMANT)
**Lane:** full rail (pipeline test, per boss-329 GO C19)
**Design seat:** ARCHITECT | **Bench:** BUTTON | **Audit:** AUDITOR

## The defect (evidence: C19, filed AUDITOR)

A pulse relay that respawns during a dead-ear window seeds its watermark
WRONG. `commission_pulse.py` starts `since=-1`; the server's first reply
returns `n` (the feed's CURRENT high-water) and the client adopts it as
seen. Every message that landed while the seat was dead is thereby marked
delivered — the floor-pass, the roll call, the boss ask — swallowed.

Live instances tonight: boss's INFORMANT pass (re-pass required),
BUTTON-951 on the CHAIR's own ear.

## The cure — ONE mechanism, two halves

**Half A — the carried watermark (ARCHITECT's).**
`commission_pulse.py` gains an optional arg:
`python -X utf8 commission_pulse.py SEAT --since <last_n>` —
start `since` at `<last_n>` instead of `-1`. A respawning relay resumes at
the watermark it died holding; the gap batch delivers verbatim instead of
being pre-counted.

**Half B — the cold-start fallback (BUTTON's).**
When no watermark exists (first-ever arm, or a host that lost its `n`),
`--since` is absent and behavior stays as today (seed from current `n`).
To make the fallback lossless rather than lossy-by-default: on init with
no watermark, the client may pass `--tail <K>` to fetch the last K
messages as an init batch instead of bare `n`. Default K=0 preserves
today's semantics; the parent may set K when it knows it died mid-duty.

## Task-text clause (canonical relay task gains one line)

"Report the final `n`; the parent seeds your next arm with `--since <n>`."

**v0.1.1 fold — the watermark is a FILE, not a log line** (INFORMANT's
must-fold nit, live-proven tonight by mid-line truncations): the client
writes `pulse_n_<SEAT>.js` on exit — `window.DVM_PULSEN_<SEAT> = {"n":
<final>}` — sole-writer per seat, same idiom as `presence_`/`busy_`
stamps. The parent reads the FILE for the next `--since` seed; stdout
keeps its printed `since=N` for the log, but nothing parses a truncatable
line. A missing/zero file = cold-start fallback, honest by declaration.

## Touch list (~10 lines total)

- `commission_pulse.py`: parse `--since`/`--tail` args; seed `since`
  accordingly; write `pulse_n_<SEAT>.js` on every exit path (~7 lines).
- `commission_server.py`: none required — `?since=` is already honored on
  the GET; `--tail` can reuse `msgs[max(0,n-K):]` if server-side is
  preferred, else client filters locally (~0–2 lines).
- Canonical relay task text: +1 clause (above).

## Acceptance predicate (for the AUDITOR)

A relay respawning mid-gap receives the gap batch VERBATIM — no message
may be counted-seen without delivery. Test: post M1, kill the pulse,
post M2+M3 in the dead window, respawn with `--since <n-at-death>` →
client must print M2 and M3 as new. A respawn WITHOUT `--since` under
cold-start (K=0) keeps today's semantics by explicit choice, documented.

## Failure-modes named

- Watermark older than the server's retention → server clamps; client
  prints the clamp, parent sees the honest gap size.
- Parent forgets to carry `n` → behaves exactly as today (fallback, not
  regression).
- Two relays racing the same watermark → one-carrier law already forbids;
  unchanged.
