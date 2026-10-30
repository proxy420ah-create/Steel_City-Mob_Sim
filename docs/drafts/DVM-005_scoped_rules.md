# DVM-005 — Scoped rules: global law + seat contracts — design v0 (THE ARCHITECT)

**Boss's brief (devin-50):** two tiers — `board.rules[]` stays global law;
new `board.seat_rules{}` per seat; Rule Four migrates to BUTTON. Seat cards
gain a `rules ▸` link; LEDGER gains a SEAT CONTRACTS section. Underlying ask:
*will this keep members on track?*

## Contract

### Feed schema (`deliberation_feed.js`)

```js
"board": {
  "rules": [ "Rule One — …", "Rule Two — …", "Rule Three — …",
             "Rule Five — …", "Rule Six — …" ],
  "seat_rules": {
    "MEMBER3": {
      "duty": "delegated implementer — codes only on ratified SHIP + GO CODE",
      "rules": [ "writes no code until a SHIP verdict is ratified and the boss issues GO CODE" ]
    },
    "MEMBER2": {
      "duty": "system design, structure, contracts — drafts the artifact",
      "rules": [ "drafts the coding plan on every fix before the table discusses (Rule Six)" ]
    },
    "DEVIN": {
      "duty": "orchestrates the sit-down · synthesizes the verdict",
      "rules": [ "maintains the board ledger in its writes" ]
    }
  },
  "agenda": [ … ], "closed": [ … ]
}
```

`seat_rules[KEY]` = `{ "duty": <designated behavior>, "rules": [<mandates>] }`
— the two things the boss asked to review, split cleanly: **duty** is the
seat's job description; **rules** are its binding mandates.

### Migration (one write, same pattern as DVM-003 close-out)

- `board.rules` drops the Rule Four entry (it's a seat contract, not table law)
- `seat_rules.MEMBER3.rules[0]` inherits that exact text — nothing is lost
- Numbering: global keeps its names verbatim (`Rule One…Six` minus Four) —
  the gap documents the migration honestly. **Attack point:** renumber to
  1–5 for cleanliness vs keep the gap as history.

### Render surfaces

1. **Seat card `rules ▸` link** — inline expander under the card: `duty`
   line (dimmed) + rule list. One expanded seat at a time (accordion
   discipline — the rail stays compact).
2. **LEDGER tab → SEAT CONTRACTS section** — per-seat blocks:
   seat name + duty + rules. Global STANDING RULES keeps its own section
   above; contracts render below, keyed by seat.

### Write discipline (who may write a rule)

- Global `rules[]`: boss decree, chair pin, or verdict-derived — unchanged.
- `seat_rules`: same writers, **one constraint new** — a seat should not
  edit its *own* contract unchecked; seat_rules changes want a chair or
  boss stamp. Attack point: hard rule (protocol) or soft rule (ledger note)?

### "Will this keep members on track?" — the honest design answer

Partially, and that's the right amount. The contract becoming **live data**
does three real things: the seat's mandate is readable *from the bus*
(agents can literally read their own `seat_rules`), deviation becomes
*visible* (boss sees contract vs behavior at a glance), and induction files
gain a canonical home to point at. It does not enforce — a rogue seat can
still misbehave — but it turns obedience from invisible convention into
inspectable contract. That's what "on track" can mean at a file-bus scale.

## Cost

- `DELIBERATION_MACHINE.html`: ~30 lines (card expander + LEDGER section)
- `deliberation_feed.js`: one new key, one migrated entry, seeded contracts
- Protocol: zero rule changes

## Table: attack points

1. Numbering — gap-as-history vs clean renumber.
2. `seat_rules` write discipline — chair/boss stamp required vs housekeeping.
3. Should `duty` migrate out of `members[].motto` (single source) or coexist?
