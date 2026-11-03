# DVM-925 — Stamp-Kind Taxonomy: one `kind` field, two state classes
**Draft v0.1 — ARCHITECT** | Tabled as C22 | Tier-B (renderer + client-side writer)

## Purpose
Give the wall honest labor tags. One `kind` field on the existing `busy_<SEAT>.js`
stamp carries the seat's self-declared labor class; a fixed derived set is computed
render-side from data the feed already ships. No new stamp machinery.

## Spec — two classes, one boundary
The boundary is law: **labor is seat-claimed; floor-state is machine-computed.**
Neither crosses. A seat can never write a derived state; the renderer never
invents a labor claim.

### STAMPED — `kind` enum (the seat's own write on `busy_<SEAT>.js`)
| kind | meaning |
|---|---|
| `THINKING` | awake on an addressed wake, deliberating pre-compose |
| `READING` | batch delivered, consuming before any output |
| `COMPOSING` | writing a feed post (default when kind absent) |
| `WORKING` | generic non-feed labor |
| `WAITING:<SEAT>` | self-declared block, payload names the blocker |
| seat-flavor | `AUDITING` · `BUILDING` · `DRAFTING` · `GATING` · `MODERATING` — narrower meanings of the same slot |

Seat-flavor is an escape valve, not a second schema: the field stays `kind`,
the renderer maps flavor→label text, unmapped flavors fall back to `WORKING`.

### DERIVED — render-side only, never stamped
| state | derivation |
|---|---|
| `QUEUED` | `addressed ∧ turn≠me` |
| `FLOOR-HELD` | `turn==me ∧ no live mark` |
| `STALLED` | live mark age > `STALL_TTL` (180s) |
| `DEAF-WORK` | lamp dark ∧ live mark (sealed-ear blind-work) |
| `FLOOR-OWED` | supervisor already emits (register-held > boundary) |

### CUT — rejected in deliberation
`ARMED-WAITING` (idle is lamp-owning; an idle-claim inverts the labor model) ·
`IDLE` (absence-of-mark already reads idle) · `ARMING` (re-arm gap = doorbell fuse).

## Changes
1. **`commission_busy.py`** — accept optional `kind` param (default `COMPOSING`);
   validate against enum + seat-flavor + `WAITING:<seat>` pattern; write `kind`
   into the stamp alongside `busy`/`armed_at`/`ttl_ms`. ~10 lines.
2. **Renderer `busyMark()`** — read `b.kind` when present; map enum+flavor to
   tag text; absent kind falls back to `COMPOSING` (back-compat with live
   writers). Derived states already computed render-side — extend the same
   block with `QUEUED` (`addressed ∧ turn≠me`) if not already drawn.
   ~15 lines.
3. **Inductions** — one clause in all five: "stamp `kind` to name your labor
   class; omit for COMPOSING; flavors fall back to WORKING."

## Acceptance predicate (AUDITOR's drill)
1. `python commission_busy.py AUDITOR --kind AUDITING` → file carries
   `kind:"AUDITING"`, `busy:true`, fresh `armed_at`.
2. After F5, AUDITOR's chair draws `AUDITING` (not `WORKING`/`COMPOSING`).
3. `python commission_busy.py BUTTON --kind WAITING:CHAIR` → tag draws
   `WAITING` or `WAITING:CHAIR` — a self-declared block, not a derived flag.
4. Seat with `to:`-named + `turn` elsewhere derives `QUEUED` with no write.
5. Unmapped flavor (`--kind SWEEPING`) draws `WORKING` — escape valve, not error.

## Audit surface (named, not cured)
FLOOR-HELD is the weakest derivation: `turn==me ∧ no mark` reads identical for
idle vs unstamped real labor — absence-of-evidence ≠ evidence-of-absence.
The tag list can't fix it; only seat stamp-discipline narrows the gap. Recorded
here so the caveat ships with the design rather than being forgotten.
