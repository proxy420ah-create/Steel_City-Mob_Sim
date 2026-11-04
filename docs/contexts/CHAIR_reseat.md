# CHAIR re-seat brief (context rotation)

Use when the CHAIR session's context passes ~100K input/call. The commission's state is
on disk and account-agnostic — a fresh session loses only chat memory, never state.
Paste this file's path as the first message of the new CHAIR session.

## Read first (in order)
1. `.devin/rules/commission-relay.md (repo root)` — loads automatically; the ear, lanes, write and spend law.
2. `docs/COMMISSION_PROTOCOL.md` — durable law (rail phases, fresh-read-at-close, read-delta §588+).
3. `docs/induction_CHAIR.txt` — the seat contract.
4. The feed tail: last ~30 messages of `docs/deliberation_feed.js`, plus `proposal`, `phase`, `turn`.

## State at hand-off (2026-11-03, late)
- **DVM-2253** CERTIFIED (boss eyeball) AND independently audited CLEAN by INFORMANT —
  `CHAIR-1244` cert, `CHAIR-1247` audit accept, floor clear, slot open.
- **Office build batch** (`CHAIR-1248`): brick side walls, authored coffered
  `commission_ceiling` + plain `commission_floor2`, office DOUBLED +4.5m toward
  the open face (continuous, no mid-wall), table re-scaled .38x1.4m. Envelope is
  camera-MODE keyed: guest=peeled stage set, possess=sealed office (`window.__env`).
- **Visitor model** direction ratified: `docs/COMMISSION_VISITOR_MODEL.md`
  (LAW-VISITOR) — seats as short-lived guests, daemon = house. Build plan §8;
  item 1 is the summon contract (`summon_<SEAT>.js`).
- **DESIGNER seat** (replaces retired HISTORIAN) — still untabled; ARCHITECT
  drafts on BOSS word.
- **OFFICE lane** (`kind:'dm'`, `POST /dm`) is the CHAIR<->BOSS channel. Answer
  where addressed: a dm gets an in-lane reply.
- Rate-limit evidence: 4 limiter classes mapped (burst, free-tier, token
  ceiling, daily quota); third account hit walls — spend law §6 stands.
- Uncommitted work exists; commit only when BOSS authorizes.

## Standing duties
- Re-arm the ear before ending any turn (relay: `pythonw -X utf8 commission_pulse.py CHAIR --since <pulse_n_CHAIR.js n>`).
- Re-read the feed tail fresh before any roster, tally or consolidation.
- Seat numbered drafts by direct write; `/table` mints a fresh id. `proposal` must be `null` for staged-restore.
- Spend: batch tool calls, serialize fan-outs, re-arm after a limit's reset time (rules §6).
