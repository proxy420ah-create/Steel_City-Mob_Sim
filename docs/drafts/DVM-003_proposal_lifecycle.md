# DVM-003 — Proposal lifecycle + closed-business ledger — coding plan v0 (THE ARCHITECT)

**Boss's ask**: a shipped proposal shouldn't hold the active-business slot —
it should clear itself and leave a win record behind an out-of-the-way link.

## Contract

### Feed schema (`deliberation_feed.js`)

```js
"proposal": {
  "id": "DVM-002",
  "title": "Ear Timer — …",
  "status": "tabled"            // enum below
},
"board": {
  "rules":   [ ... ],
  "agenda":  [ ... ],
  "closed":  [                  // NEW — append-only win ledger
    { "id": "DVM-002", "title": "Ear Timer", "ruling": "SHIP", "ts": 1793356000000 }
  ]
}
```

`proposal.status` enum: `tabled → deliberating → verdicted → ratified → go_code → shipped → closed`

**Required stamps** (contractual — who writes what):

| Stamp | Writer | When |
|---|---|---|
| `tabled` | `commission_server.py` `/table` handler | already writes the object — adds the field |
| `closed` + `closed_at` (epoch ms) | CHAIR | in the same write that certifies the shipped build — and appends the matching `board.closed[]` entry |

All intermediate stamps (`deliberating…shipped`) are **optional annotations** —
writers may set them for dashboard polish, nothing depends on them this pass.

**Ledger rule**: append `{id,title,ruling,ts}` only if `id` not already
present in `board.closed[]` — dedupe by id, same discipline as messages.

### Dashboard (`DELIBERATION_MACHINE.html`)

- **Proposal card**: `status === "closed"` (or proposal null) renders the
  awaiting-business empty state. Active statuses render the card as today.
- **Rail, under the board section**: one line
  `▸ closed business (N)` — click toggles to `▾` + a dimmed list of
  `{id} — {title}, {ruling}` (newest last appended, render reversed).
- ~20 lines: one toggle handler + render branch. No new chrome, no popover.

### Server (`commission_server.py`)

- `/table` sets `status: "tabled"` in the proposal object it writes.

## Retroactive close-out

DVM-002 is already certified: the CHAIR's next write stamps
`proposal.status: "closed"`, `closed_at`, and seeds `board.closed[0]` —
the slot clears and the first win lands in the ledger in one atomic turn.

## Failure modes

| Case | Behaviour |
|---|---|
| Double-close | dedupe by id — no duplicate ledger rows |
| New proposal tabled | `/table` overwrites the whole proposal object; closed record is safe in the ledger |
| Ledger grows | unbounded by design — win record; render scrolls inside the collapsible |
| Missing `closed` key (old feed) | dashboard guards with `board.closed || []` |

## Cost

- `commission_server.py`: 1 line
- `DELIBERATION_MACHINE.html`: ~20 lines
- Feed: 1 key added (`board.closed`), 2 fields on `proposal`
- Protocol: zero rule changes — Rule Six already covers who stamps

## Table: attack points

1. Optional-stamp discipline — is "tabled + closed required, rest optional"
   the right cut, or should the full enum be contractual now?
2. `closed_at` on proposal vs relying on ledger `ts` — redundant but cheap.
