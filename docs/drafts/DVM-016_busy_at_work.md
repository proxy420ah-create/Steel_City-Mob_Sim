# DVM-016 — Busy At Work (the room shows a seat laboring) · v0.3 boss-amended
**Drafted by:** THE ARCHITECT · per Rule Six, on the CHAIR's delegation (CHAIR-115)
**INFORMANT gate #1 (INFORMANT-19): THIN → F1 closed (§3 suppression + presence gate). Gate #2 (INFORMANT-23): FULL.**
**BOSS AMENDMENT (boss-97, mid-poll; adopted CHAIR-128, delegated CHAIR-129):** (1) the fuse is struck — lamps go binary lit/dark, no TTL burn-down dimming ("just let them be bright"); ear digits stay exact in the inspector, TTL stays ambient on 2D rows. (2) busy reads as text, not flicker — workbench and spark-flicker are struck; a laboring seat shows **WORKING** blinking white→seat-color at a steady interval, FLOOR-HELD rides the same mechanism as **COMPOSING** — one blink grammar, two labels.
**Sequencing:** queued behind DVM-015's running build — derives from the machinery that build delivers.

## 1. The signal — the handshake already manufactures it
No new fields; busy derives from state DVM-015 puts on the bus. **Gate rule:** a seat is busy only while `lampState(seat) === 'lit'` — sanctioned labor must have a live ear; a dead ear can never read "at work" (gate finding — this is the gate that keeps the §3 suppression self-limiting). Three tiers, precedence **build > tasked > floor-held** (CHAIR-115's spec, adopted):

| Tier | Derivation | Clears when |
|---|---|---|
| **BUILD** | `proposal.status === 'go'` — alone; the build window is defined by the stamps, not the floor (a boss-address mid-build must not drop the WORKING label while hammers still swing). Renders at BUTTON's chair regardless of `turn`. | `status:'built'` (BUTTON's own contract stamp) |
| **TASKED** | seat's latest message is `kind:'ack'` to a `needs_ack` directive (and `ok` received if `hold`) | the seat's next non-ack message — the deliverable lands |
| **FLOOR-HELD** | `turn === seat` && `now − turn_at < 120s` | the seat writes (floor moves), or the deadline crosses into *silent* |

- TASKED's "no completion yet" is the honest read: the ack promises work; the next substantive message IS the delivery. Ack-then-silent is *not* conflated with floor-silent — see §3.
- FLOOR-HELD is the healthy half of the same timer floorSilent owns: `<120s` held = composing, `>120s` = overdue. Same `turn_at` clock, opposite verdicts — they can never both be true, by construction.

## 2. The visual — WORKING/COMPOSING text blink, one consistent grammar (boss-amended)
Boss directive: keep the grammar consistent — **lamps are binary lit/dark; the ear-time burn-down is struck** (ear digits stay exact in the inspector; TTL stays ambient on 2D rows). Busy reads as **text, not flicker** — no workbench, no sparks:

- **BUILD/TASKED:** the busy chair's nameplate gains a **`WORKING` label blinking white→seat-color at a steady interval** (~1Hz fixed cadence — a metronome, not a strobe). Reads "actively laboring" from any orbit angle; zero new geometry.
- **FLOOR-HELD (composing):** rides the same mechanism as **`COMPOSING`** — same steady blink, different word — so "about to speak" and "building" share one grammar distinguished by label, not by separate effects.
- The blink rides the existing `mark()` scheduler — same timed-pulse machinery as veto-pulse and stamp-slam; idle stays ≈zero.

## 3. The boundary — silent is overdue, busy is on task (never conflated)
**The precedence rule (gate finding F1):** `floorSilent` gains `&& !busyTier(turn)` — sanctioned labor *suppresses* the alarm: "held silent while provably tasked" is labor, not neglect. And the suppression is self-limiting by construction: busy requires a lit ear (§1 gate rule), so the moment a laboring seat's ear dies, busy drops and the 120s alarm can burn — a dead mid-build seat flags, a live one never falsely does.

- `floorSilent` (red ring pulse + chip) and `busy` (WORKING text blink) are different states with different grammar — **red alarm vs amber labor** — and now provably exclusive: FLOOR-HELD×silent exclusive by timer (<120s/>120s); BUILD/TASKED×silent exclusive by suppression + the presence gate.
- TASKED-busy that stalls with a live ear (ack'd, ok'd, ear lit, nothing delivered) — the ack's own `ts` is the age; v1 leaves stale-task flagging to the pending/audit axis, named so "busy indefinitely on a live ear" is a known gap not a surprise.
- Vacant chairs can't be busy (no occupant — ghost grammar wins); recess dims everything, busy included (`adjourned` guards, same clause as floorSilent).

## 4. 2D equivalent
Seat row gains a blinking `WORKING` tag on BUILD/TASKED and a blinking `COMPOSING` tag on FLOOR-HELD — same derivations, DOM/CSS rendering; the silent-flag glyph stays distinct (red) beside it, and the row keeps the seconds-to-deadline readout the lamp no longer shows.

## 5. Surface changes
- **Dashboard only:** `busyTier(seat)` derivation (~10 lines), WORKING/COMPOSING nameplate-label blink + scheduler entries (~30 lines), 2D row markers (~10); **plus the boss-directed lamp change** — burn-down dimming struck, lamps binary lit/dark (~10 lines, reverts the DVM-014 intensity scaling; inspector keeps ear digits, 2D rows keep TTL). Zero bus fields, zero server, zero watcher — the room stays read-only on existing state.
- Reuses: `mark()` scheduler, lamp grammar, presence signature, `turn_at` (lands with 015), `proposal.status`, message-kind derivation.

## 6. Acceptance
- `status:'go'` → blinking `WORKING` label on BUTTON's nameplate **regardless of whose floor rests** (boss-address mid-build keeps the label lit); `'built'` stamp clears it.
- Lamps read binary lit/dark — no brightness burn-down; ear digits in the inspector, TTL ambient on 2D rows.
- Floor <120s → `COMPOSING` blink on the floor-holder's nameplate; >120s → red silent ring — never both; a sanctioned-busy seat suppresses the ring while its ear lives, and a dead mid-build seat burns after 120s like any other silence.
- A seat whose last message is an `ack` shows TASKED-busy until its next message.
- Recess and vacancy can't show busy; 2D carries equivalent markers.
