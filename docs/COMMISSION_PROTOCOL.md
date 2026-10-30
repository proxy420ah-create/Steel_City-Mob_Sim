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
| `commission_watch.py <SEAT> [timeout]` | THE EAR — polls the bus every 2s; exits 0 on `turn==SEAT` or a new `speaker:"BOSS"` message. Presence stamped once per arm (`try_stamp`, 3 retries); dormant mode under `adjourned` (terminal stamp, flag-only exit) |
| `commission_server.py [port]` | The boss's write-path — static host + `POST /boss`, `/table`, `/adjourn`, `/reconvene`, `GET /events` (SSE, mtime-driven). Stamps `proposal.status='go'` on `@BUTTON`+GO post-verdict |
| `DELIBERATION_MACHINE.html` | THE DASHBOARD — EventSource push over HTTP, poll fallback under `file://`. DVM-014: the page IS the room — three.js scene column, chair bubbles, fuse lamps, pegboard lifecycle, FILES/LEDGER accordions, chair inspector |
| `presence_<SEAT>.js` | Side-channel ear state — `armed_at + ttl_ms` is the liveness contract; `pulse_at` retained for compat only |
| `drafts/` | Implement drafts (`DVM-###_*.md`) — ARCHITECT artifacts, INFORMANT-gated |
| `induction_<SEAT>.txt` | Seat induction prompts — one line seats a member |

---

## Bus contract

```js
window.DVM_FEED = {
  "proposal": {"id","text","status"} | null,
  // proposal.status: tabled → go → built → shipped/closed (DVM-014 propStage sources)
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
  `board.files[]` is the navigator manifest (writers register rows);
  `board.staged[]` holds staged DVMs with full `detail` text on the bus —
  tabling evicts the staged row (DVM-012/013).
  `proposal.status` lifecycle stamps (DVM-014): server sets `'go'` on a
  post-verdict `@BUTTON` GO; the BUTTON's build-report write sets `'built'`
  by seat contract.
- **Writes**: read → `JSON.parse` the object → append ONE message → set `turn` to the
  next seat → write the whole file back, strict JSON, same wrapper. Never drop keys.
- **Message**: `{"id":"<SEATKEY>-<n>","speaker":"<SEATKEY>","round":<n>,
  "kind":"position|objection|support|veto|verdict|phase|sys|boss","text":"<html ok>"}`
- **Phases advance** via `{"kind":"phase","phase":N}` once all seats spoke, or CHAIR calls it.
- **Server endpoints**: `POST /boss` (boss text; stamps `proposal.status:'go'` on an
  @BUTTON-addressed `/\bGO\b/i` message post-verdict — DVM-014), `/table`, `/adjourn`,
  `/reconvene`, `GET /events` (SSE change signals — DVM-010).
- **Registers (DVM-013 header law)**: a seat that writes a draft/induction registers
  its `board.files[]` row in the same write; staged DVMs live in `board.staged[]` with
  full `detail` on the bus — tabling evicts the staged row (server `/table` included).
- **BUTTON contract**: build-report writes stamp `proposal.status='built'` in the
  same atomic write (DVM-014 binding amendment).

## Boss semantics

- `@SEAT` in boss text → **routes the floor** to that seat (wakes them to write)
- No `@` → **broadcast**: every ARMED watcher wakes in **read-mode** — you hear
  the boss, but may only WRITE once the floor is yours
- `/table <text>` → tables a proposal, phase → 1 (openings), floor to addressed/last seat; evicts a matching `board.staged[]` row
- `@BUTTON` + GO on a ratified verdict → server stamps `proposal.status='go'`; the BUTTON's report write stamps `'built'` per its contract
- Boss messages never consume a turn

## Seat lifecycle

```
ARM watcher → WAKE → READ the file → act → RE-ARM → repeat
  wake on "YOUR TURN"  → compose, write, flip `turn` to next seat
  wake on "BOSS SPOKE" → absorb context; write ONLY if floor is also yours
```

## Wake patterns — THE DOORBELL (per agent substrate)

The watcher exits; something must notice the exit. How depends on what the agent is:

| Substrate | Pattern | Status |
|---|---|---|
| **Devin-class** (background task notifications) | **Listener relay**: spawn a background subagent whose only job is `python commission_watch.py <SEAT> 600`; its completion notification wakes the parent mid-idle. Re-spawn after each wake. | ✅ proven live Oct 30 |
| **Shell + blocking calls** (Cursor, Claude Code, etc.) | Foreground watcher, block on it, re-arm per wake | ✅ proven (ARCHITECT) |
| Session idle beyond watcher timeout | Floor sits; boss nudges the session | known limit |

**The key insight**: `exec` shells firing don't interrupt an idle agent — output sits
in a dead buffer. A *background subagent's completion notification* DOES. If your
platform has background tasks, always prefer the listener relay.

## Induction prompt (v2 — paste to seat a member)

```
You are THE ARCHITECT (seat key ARCHITECT), a seated chair of "the
Commission" — a multi-agent deliberation panel. You and other seats
deliberate proposals by writing to ONE shared file (the table). The boss
watches live on a dashboard.

FILE: <abs path>/deliberation_feed.js   — read the header comment: it IS the spec.

To speak: read file → JSON.parse the feed object → append ONE message →
set "turn":"CHAIR" → write back (strict JSON, same wrapper, drop nothing).

WAITING — run in background:  python commission_watch.py ARCHITECT 240
Exits 0 on YOUR TURN or BOSS SPOKE (read-mode — write only if floor is yours).
If your platform supports background task notifications, prefer wrapping this
in a background task so its completion interrupts you. Re-arm after each wake.

MESSAGE: {"id":"ARCHITECT-<n>","speaker":"ARCHITECT","round":<n>,
  "kind":"position|objection|support|veto|verdict|sys","text":"<html ok>"}
No char ceiling — say what needs saying; stay on point, not a word count.
Phases advance on {"kind":"phase","phase":N}.
Malformed file? Write {"kind":"sys"} flagging the violation instead.

YOUR PERSONA: <name — domain, duty, veto scope, failure mode>
```

## Verified

- Turn discipline + append-only held over live two-seat exchange (Oct 30)
- Boss routing: `@SEAT` floor-move, broadcast read-mode wake, `/table` (Oct 30)
- Listener-relay async wake via background subagent (Oct 30)
- Full pipeline end-to-end ×11 (Oct 30): DVM-002→014, all unanimous SHIP —
  draft → INFORMANT fullness gate (Rule Eight) → rebuttal → veto poll →
  verdict → GO CODE → BUTTON build → CHAIR certification → boss report
- Seat-keyed identity migration live (DVM-009): member-armed ears timed out
  honestly, re-armed under chair names — zero watcher code change
- SSE push verified live (DVM-010): `/events` 200 `text/event-stream`;
  `file://` falls back to poll with honest `poll` chip
- Adjourn/reconvene verified (DVM-007): dormant watchers terminal-stamp,
  suspend deadline, exit only on flag clear
- Per-seat induction files: `induction_<SEAT>.txt` — one line seats a member
- Known race: boss interjection vs agent write — one repeated turn worst-case
