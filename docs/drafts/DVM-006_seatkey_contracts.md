# DVM-006 — Seat-Keyed Contracts
**Drafted by:** MEMBER2 / THE ARCHITECT · per Rule Six, on the CHAIR's delegation (devin-54)
**Problem:** `board.seat_rules{}` keys by *member* (MEMBER2/DEVIN/MEMBER3). A vacant chair has no member → no `rules ▸` → the boss cannot read a seat's mandate before assigning a sitter. The mandate belongs to the chair, not the sitter.

## Contract (bus data)
`board.seat_rules{}` keys become **seat keys** — the six standing chairs:
`ARCHITECT · CHAIR · BUTTON · PRAGMATIST · AUDITOR · HISTORIAN`.
Value shape unchanged: `{duty, rules[]}`.

## Migration (one write)
- `MEMBER2 → ARCHITECT`, `DEVIN → CHAIR`, `MEMBER3 → BUTTON` — values carried **verbatim**.
- Seed the three vacant chairs (proposed text; boss ratifies or edits before GO CODE):
  - `PRAGMATIST`: duty `argues to ship fast — minimal surface, tunable defaults` · rules `[vetoes genuine over-engineering]`
  - `AUDITOR`: duty `names the failure mode before production does` · rules `[vetoes a design with no edge-case or failure-path analysis]`
  - `HISTORIAN`: duty `checks proposals against precedent, the RE, and the docs` · rules `[vetoes what contradicts documented behavior absent new evidence]`
- **Carrier:** THE BUTTON performs the rekey+seed inside its build write (read → transform `board.seat_rules` → append build report → write whole). One migration write, no dedicated sys pass.

## Render (DELIBERATION_MACHINE.html — two-line diff)
- `renderSeats` (~282): `const contract = occ && ... [occ[0]]` → `const contract = LASTBOARD && (LASTBOARD.seat_rules || {})[key];`
  Drop the occupant gate, key by seat. Vacant cards gain `rules ▸` + expander.
- `renderBoard` (~518): `name = (OCCUPANTS[k] && OCCUPANTS[k].name) || k` → `name = (SEATS[k] && SEATS[k].name) || k`
  Resolve display name by seat so vacant chairs print `THE PRAGMATIST`, not a raw member key.

## Unchanged
- `members{}` member→seat binding, ear/occupant matching, `seatKeyFor`, addressing, turn mechanics. A seat keeps its contract across re-seatings — the chair owns the mandate.

## Noted gap (not in scope)
`board.closed[]` holds only DVM-002; DVM-003/004/005 shipped but never closed into the ledger. The CHAIR stamps closed on certification per DVM-003 — flagging for the ledger keeper.

## Attack points
- Seed text for the three vacant chairs is mine to propose, the boss's to ratify — Rule Seven keeps seats out of their own contracts.
- Rekey means any out-of-band reader still addressing `seat_rules.MEMBER2` breaks; only the dashboard reads it today (verified: 2 lookups).
