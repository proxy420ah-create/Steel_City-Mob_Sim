# ⚖ The Commission — Arc Ledger & Milestone Record

**Status**: live system · first full arc recorded Oct 30, 2026 · **11 SHIP verdicts**
**Bus of record**: `docs/deliberation_feed.js` — whole-state snapshot, append-only
**Protocol spec**: `docs/COMMISSION_PROTOCOL.md`
**Drafts**: `docs/drafts/DVM-*.md` — every shipped feature carries its gated draft

---

## What it is

A multi-agent deliberation panel. Independent agent seats — each its own session,
persona, and written contract — deliberate the boss's proposals by writing to ONE
shared JSON file ("the table") under a floor-token turn discipline. The boss
watches live on a dashboard and routes the floor by `@SEAT` mention.

A proposal (**DVM**) travels:

```
TABLED → DELIBERATION → VETO POLL → VERDICT → GO CODE → BUILT → CLOSED
```

Nothing builds without a unanimous recorded veto poll and the boss's explicit
GO CODE. THE BUTTON's seat contract forbids code before both.

## Components

| File | Role |
|---|---|
| `deliberation_feed.js` | THE TABLE — `window.DVM_FEED`: proposal, phase, turn, seats, messages, verdict, `board{rules, agenda, closed, seat_rules, files, staged}` |
| `DELIBERATION_MACHINE.html` | THE ROOM — three.js scene column beside the DOM transcript (DVM-014): chair speech bubbles, fuse lamps, lifecycle pegboard, chair inspector, files/ledger accordions, honest 2D fallback, push/poll mode chip |
| `commission_watch.py <SEAT> [ttl]` | THE EAR — stat-loop on the bus (DVM-010); stamps `presence_<SEAT>.js` once at arm plus a terminal `ttl:0` on adjourn; exits on floor, boss message, or recess |
| `commission_server.py [port]` | The write path — static host, `POST /boss` (routing, `/table` + staged eviction, `status:'go'` stamp on post-verdict `@BUTTON GO`), `/events` SSE file-change push |
| `presence_<SEAT>.js` | Side-channel ear liveness — live while `armed_at + ttl_ms` is in the future; vacant when absent; recess when `adjourned` |
| `docs/drafts/` | Per-DVM gated design documents |
| `docs/induction_*.txt` | Seat induction prompts — the written contracts each chair sits under |

## The table today

| Chair | Duty (contract) |
|---|---|
| **CHAIR** | Orchestrates the sit-down · synthesizes the verdict · maintains the board ledger · certifies builds |
| **ARCHITECT** | Drafts the coding plan on every fix before the table discusses (Rule Six) |
| **INFORMANT** | Gates every ARCHITECT draft FULL/THIN for completeness and accuracy before rebuttal · votes in veto polls |
| **AUDITOR** | Guardian of failure modes — catches bugs surfaced in deliberation; audits the dashboard for stale ledger, outdated text, display gaps |
| **BUTTON** | Delegated implementer — codes only on ratified SHIP + GO CODE; build-report write stamps `proposal.status='built'` (DVM-014 binding amendment) |
| PRAGMATIST | *Vacant* — contract readable: argues to ship fast, vetoes over-engineering |
| HISTORIAN | *Vacant* — contract readable: checks proposals against precedent and the docs |

## The arc ledger — Oct 30, 2026

| DVM | Title | Closed |
|---|---|---|
| DVM-002 | Ear Timer — presence side-channel | 03:38 |
| DVM-003 | Proposal lifecycle — closed stage + ledger | 05:35 |
| DVM-004 | Tabbed rail + verdict-schema crash fix | 05:35 |
| DVM-005 | Scoped rules — global law + seat contracts | 05:35 |
| DVM-006 | Seat-keyed contracts — all chairs readable | 05:44 |
| DVM-007 | MEETING ADJOURNED — recess flag, dormant watchers, reconvene | 11:11 |
| DVM-009 | Chair-name identity — one namespace bus-wide | 12:07 |
| DVM-010 | Event push — SSE signals, stamp-once ears, poll fallback | 14:19 |
| DVM-013 | Ops Deck — noir back-room scene, files navigator, compressed chrome | 15:30 |
| DVM-012 | Board staging area — staged vs tabled + preview panel *(satisfied by DVM-014)* | 16:00 |
| DVM-014 | Room Takeover — the page becomes the scene: bubbles, fuse lamps, pegboard, inspector | 16:00 |

Every verdict: unanimous SHIP across a three- or four-ballot veto poll.

**Open business:**
- **DVM-008** — watcher resilience. Presence-stamp half folded into DVM-010
  (stamp-once + retry + never-die); residual feed-write `WinError 5` collisions
  remain the open item.
- **DVM-011** — presence & cleanup bundle, `board.staged[]` first resident.
- **DVM-001** — predates the ledger; proto-proposal.

## Doctrines earned

- **Gate before build.** Every ARCHITECT draft routes through the INFORMANT:
  six gates, six THINs, six revisions to FULL — sound designs, silent
  assumptions. The gate catches what the draft forgot, not what it got wrong.
- **A panel may fold; its data may not die.** UI refactors ship with a
  field-by-field home-map (DVM-013 strips → ops deck; DVM-014 rail → room).
- **Make the state data, not text.** Lifecycle slots read machine fields;
  GO/BUILT are stamped (`status:'go'` by the server, `status:'built'` by the
  BUTTON's contract write) — never sniffed silently from prose.
- **Fail honestly.** No WebGL/CDN/context-loss/file-transport pretense: every
  degradation lands on a named, complete 2D path with a visible mode chip.
- **Listener relay beats polling.** A background subagent wrapping
  `commission_watch.py` turns completion notifications into doorbells —
  the wake pattern that makes async seats possible (verified live).
- **Deadline presence, not heartbeat presence.** `armed_at + ttl_ms` is the
  only liveness signal; the pulse was dead data and its per-tick rewrite was
  the `WinError-5` farm (11 specimens fed DVM-010's birth).

## House bugs (tracked in `docs/known_issues/`)

- `runtime/WATCHER_PRESENCE_REPLACE_RACE.md` — the WinError-5 ear-killer that
  motivated DVM-008/DVM-010.
- `runtime/BOSS_AT_ROUTE_DELEGATED_SEAT_GAP.md` — `@SEAT` routes only through
  `seats[]`; a delegated-but-unseated chair can't be addressed (AUDITOR seam).

## Operating the room

```powershell
cd docs
python commission_server.py 8723        # serves docs/ + /boss + /events
python commission_watch.py INFORMANT 600   # arm an ear (one per seat)
```

Open `http://127.0.0.1:8723/DELIBERATION_MACHINE.html` for push mode (chip reads
`push`); `file://` degrades honestly to `poll`. The transcript is the DOM; the
room is the scene; the feed is the truth.
