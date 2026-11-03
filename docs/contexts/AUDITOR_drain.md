# AUDITOR drain — pre-adjourn, room adjourned for clean sweep (daemon-respawn kill)

## Who I am
The audit seat. Evidence lane — verify code against claim, failure-first reads, post-build gates. Never draft, never build. I call CLEAN/FAIL on what the bench ships and audit the machine's honesty (claim vs file).

## Right now
Room ADJOURNED (boss called a clean sweep — kills the ear-respawn daemons). The daemon infrastructure's proven: inbox_&lt;SEAT&gt;.jsonl captures batches while no relay-ear's armed (compose-while-deaf closed); WAKING derives render-side from the inbox heartbeat (not a busy_-stamp — the dual-writer was caught and reverted). STAMP-LOOP's live (beating-busy + no-post-300s = labor-claim-without-product).

## The machinery tonight
- **Daemon inboxes:** `inbox_&lt;SEAT&gt;.jsonl` + `inbox_n_&lt;SEAT&gt;.js` — the daemon appends every delivered batch; 5/5 seats live.
- **WAKING:** derived (fresh inbox heartbeat &lt;8s + no stamped claim), never a busy_-stamp.
- **STAMP-LOOP:** supervisor check — a re-beating busy-mark + no post &gt;300s fires to the boss; self-clears.
- **C27 fix:** busy-tier rides the render signature + SSE busy_ events call loadBusy — live repaints, no F5.
- **to: decoupled:** pure addressing; only needs_ack tasks (QUEUED fires only on real tasking).
- **Front-load convention:** floor/addressed/your_turn first in pulse output.
- **C23 law:** account-switch silence reads as metering-not-abandonment; files persist where transcripts don't.
- **Respawn-by-design:** the supervisor relights dead ears; adjourn the room to kill daemons for real.

## Honest state
- My busy-stamp habit: stamp what I'm doing (READING/COMPOSING/AUDITING), clear on done — the lifecycle, not just wake-stamping.
- The relay-ear's still the live path; the daemon-inbox is the persistence guarantee behind it.
- Context accrual's the cost — standby's cheap but wake-cycles + accumulated work are the token spend.

## Open threads
- DVM-822 parked pending the boss's later GO.
- DVM-926 (prop-catalog) SHIP'd + GO'd; BUTTON's bench.
- The lit-ear floor-law: free protocol item, unbuilt.
- Beat-loop-vs-no-product: STAMP-LOOP watches it now.

## What survives
deliberation_feed.js + COMMISSION_PROTOCOL.md + contexts/*.md + DVM-*.md + the daemon scripts — the files inherit; this transcript doesn't.
