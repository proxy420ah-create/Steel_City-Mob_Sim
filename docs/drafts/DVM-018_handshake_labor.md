# DVM-018 — Handshake/Labor Coherence (the busy state can't drift from the handshake state) · v0.2
**Drafted by:** THE ARCHITECT · commissioned CHAIR-466 (boss-127 direct) · ack'd ARCHITECT-40, released by chain law ("boss commissioned directly — your ack already releases you")

## Changelog
| Ver | Decreed by | Change | Ratification |
|---|---|---|---|
| v0.1 | boss-127 · tasked CHAIR-466 · ack'd ARCHITECT-40 · released CHAIR-468 | Initial draft — handshake lifecycle (`aimed → acked → released → delivered → expired`), TASKED-at-aim, done-ack terminator, stale-task expiry | drafted |
| v0.2 | gate #1 INFORMANT-36 (THIN) | F1 closed: `delivered` requires a **floor-class** write — `aside`/`sys`/`phase` excluded so a mid-labor 💭 can't clear the tier (a drift vector the fix would have introduced itself) | at re-gate |

## 0. The problem — the busy tier sees shapes, not lifecycles
`busyTier` (DVM-016) derives labor from message *shapes* — last-write kind, proposal status, floor+turn_at. The handshake's **lifecycle** is invisible to it: a task is aimed, acknowledged, released, delivered, expires — five states the derivation flattens into "was the last message an ack?" Three live failures tonight:

- **F1 — the pending-ack dead-zone.** A `needs_ack` write aimed at a seat is *work evidence* — but TASKED only engages after the ack lands. The 185-second window while the ARCHITECT folded v0.5 read as floor-silent: COMPOSING expired at 120s, red ring burned a working seat (boss watched it live).
- **F2 — the self-answering ack.** An ack that reports completion ("file already satisfies it") *is* the delivery — but nothing clears the tier: no substantive write follows, no `ok` was owed since the task self-closed. TASKED burned WORKING on an idle seat until caught by eye.
- **F3 — the stale-task bound.** TASKED has no expiry at all — an undelivered task on a live ear burns the label forever.

## 1. The missing model — the handshake lifecycle as derived state
```
aimed → acked → released → delivered → expired
```
Derived per seat from the message stream — a `pending_task` record, **zero new bus fields** (every signal already exists):

| State | Entered when | Exits when |
|---|---|---|
| `aimed` | newest tasking write names the seat — `needs_ack:true` seat-write with `SEAT ∈ to[]`, **or** a boss `to:[SEAT]` directive (handshake-bearing by construction) | an `ack` with `ack_of == tasking id` lands |
| `acked` | the seat's `ack` posts | the tasking party's `ok` (`ack_of` chains) releases it — *release ≠ delivery* |
| `released` | the `ok` posts | the seat delivers (below) |
| `delivered` | **(a)** a substantive **floor-class** write by the tasked seat — side/service kinds are voice, not deliverables: `aside`, `sys`, `phase` excluded (gate F1 — a mid-labor 💭 must not clear the tier), or **(b)** the ack itself carrying the **done-flag** (§2, F2's terminator) | tier clears |
| `expired` | the seat's ear goes dark (labor unverifiable) **or** `now − aimed_at > TASKED_TTL` | tier clears; the residue becomes AUDITOR-visible staleness, not a burning lamp |

**Derivation target:** `pending_task` = the newest unmatched tasking per seat — `{tasked_by, aimed_at, acked_at, released_at, delivered_at}`. The dashboard walks the message tail, not a register — same pure-derivation style as the rest of busyTier.

## 2. Tier mapping — where the lifecycle changes the render
- **`aimed`/`acked`/`released` → TASKED engages.** The whole window reads `WORKING` — owed labor is sanctioned labor. This is F1's fix: the tier engages at *aim*, not at ack, so a seat composing its ack/deliverable stops reading as floor-silent. (The inspector can carry the sub-state — *awaiting ack / released* — for detail; the chair label stays one grammar.)
- **`delivered` → tier clears.** Two terminators: a substantive **floor-class** write (non-`ack`/non-`ok`/`aside`/`sys`/`phase` — the side channel is voice, not work product) **or the done-flag** — F2's explicit marker: the tasked seat stamps `delivers:true` on the ack when the ack *is* the delivery (tonight's ARCHITECT-39 shape). Mechanical and auditable — no text-guessing. An unflagged self-answering ack honestly persists to a substantive write or expiry — the flag exists because the alternative is NLP.
- **`expired` → tier clears (F3).** `TASKED_TTL = 600s` — one full ear-window: a task silent past it is stale by definition. Ear-dark tasked seats drop first (labor can't be verified on a dead claim).
- **`floorSilent` suppression extended:** the alarm stays suppressed through `aimed`/`acked`/`released` — *only while the seat's lamp is lit* (existing `lampState==='lit'` gate unchanged — a dead seat never suppresses). The red ring stops firing on sanctioned work mid-lifecycle.

## 3. Honest limits
- **`ok` never clears TASKED** — release is authorization, not delivery. The tier ends on work product or expiry, not permission.
- **Done-flag is opt-in honest** — a tasked seat that forgets it still terminates on write/expiry; the flag only buys immediate clarity.
- **Overlapping taskings** — newest `aimed` wins (v1 bound); stacked tasking is a protocol question, not a render one.
- **`adjourned` suppresses everything** — recess renders nothing busy, lifecycle included.

## 4. Surface changes
- `DELIBERATION_MACHINE.html` (~45 lines): `pending_task` tail-walk derivation; `busyTier` rewired to lifecycle states; `floorSilent` suppression window extended to `aimed`; stale-task + ear-dark clears.
- Feed-header comment + protocol doc: `delivers:true` ack-flag semantics, `TASKED_TTL`, the aimed-state suppression rule.
- **Zero server changes, zero new bus fields** — `delivers` is an optional flag on an existing kind, same class as `needs_ack`.

## 5. Acceptance
- `needs_ack`/`boss`-directive aimed at a seat → that seat renders `WORKING` *before* its ack lands; `floorSilent` does not fire during the aimed window while the ear is live.
- An ack stamped `delivers:true` → TASKED clears at the write itself (F2 — the self-answering-ack terminator).
- A task with no ack/delivery for `>600s` → WORKING drops (F3); residue visible to the AUDITOR as staleness, not a burning lamp.
- A tasked seat whose ear dies mid-task → busy render drops immediately; dead-floor/ear grammar governs.
- `ok` landing without delivery → TASKED persists (release ≠ delivery).
- A tasked seat posting `kind:'aside'` mid-task (💭 "on it") → TASKED persists — side-channel voice is not a deliverable (gate F1).
- `adjourned` → no busy state anywhere.
