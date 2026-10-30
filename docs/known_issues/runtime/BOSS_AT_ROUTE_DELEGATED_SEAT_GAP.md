# Boss @-Route Cannot Reach Delegated Seat (THE BUTTON)

**Date**: 2026-10-30
**Status**: 🔴 ACTIVE (known limit — workaround in use)
**Severity**: 🟢 MEDIUM

## Symptoms
- Boss typed `@BUTTON` (boss-27) intending to hand the floor to MEMBER3.
- Floor did not move — `turn` stayed `BOSS`; MEMBER3 heard it read-mode only.
- `@MEMBER3` would fail the same way.

## Root Cause
`commission_server.py` filters @-tokens against `data["seats"]`
(`["DEVIN","MEMBER2"]`). Delegated seat MEMBER3 is intentionally not a
voting seat, so no @-token maps to it — the server has no route to hand
it the floor.

## Investigation Notes
- Floor CAN reach MEMBER3 another way: any floor-holder (DEVIN/MEMBER2)
  can set `"turn":"MEMBER3"` in their own write — proven live (devin-29).
- Design question, not a one-line fix: should the boss be able to summon
  the delegated seat directly (add MEMBER3 to a routable-but-non-voting
  list), or is member-relay the intended path? Affects COMMISSION_PROTOCOL
  semantics — table it, don't patch it unilaterally.
- Related: `members.MEMBER3` registration needed for the delegated seat's
  ear chip / rail occupancy (DVM-002 note).

## Related Files
- `docs/commission_server.py` — `valid = [s for s in to if s in seats]` (line ~60)
- `docs/deliberation_feed.js` — bus `seats[]` / `members{}` contract
- `docs/COMMISSION_PROTOCOL.md` — delegated seat spec (Rule Four)

## Resolution
Workaround: seated member relays the floor (devin-29 → MEMBER3 sounded off).
Permanent fix: pending — needs a table ruling on delegated-seat routability.
