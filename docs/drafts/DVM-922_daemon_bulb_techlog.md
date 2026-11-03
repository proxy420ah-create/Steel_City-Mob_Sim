# DVM-922 (mini) — Daemon-health wall bulb + technical log panel · v0.1

Commissioned by BOSS aside, ratified CHAIR-854; objections closed clean
(CHAIR-855). Drafted by THE ARCHITECT — mini-cycle, light by charter.

## Changelog

| Ver | Decreed by | Change | Ratification |
|-----|-----------|--------|--------------|
| v0.1 | CHAIR-855 | Mini-draft — two surfaces + the redirect law | at gate |

## Design intent

One item, two surfaces, one law: **machine speech gets a home, and
infrastructure health gets a face.** The watchdog proved its voice the same
hour it learned to talk — three dark→relit cycles on the transcript made the
case for both halves at once.

## ① Daemon-health wall bulb

- A physical instrument mounted on the office wall **with the deck's existing
  set** — beside the pegboard, in the idiom of the CRT BURN monitor and tray.
- Reads `presence_SUPERVISOR.js` — already on the wire via the `presence_*.js`
  SSE glob; no new fetch path.
- **State map, same honesty law as the seat burns:**
  `beat:true && stamp < TTL` → green, lit (heartbeat idiom);
  stale or absent → dark.
  The lamp can only ever say "a loop stamped in the last 30s" — inherited law,
  not re-derived.
- The room gains its first *infrastructure* instrument: health you can see
  while you watch the floor.

## ② TECH LOG emit panel

- A tucked panel in the dashboard (collapsed rail / inspector tab):
  timestamped, scrollable, unread badge.
- **`kind:'sys'` traffic renders there** — watchdog transitions, phase posts,
  machine notices.
- **AUDITOR's law, carried:** *render-level diversion only.* Sys posts stay
  ON the bus — the record stays pure, the view splits. Nothing is dropped,
  suppressed, or rerouted at write time.
- Seat-dark notices (law-addressed to BOSS) get always-visible/badge
  treatment on the panel so a dark lamp can't hide inside the log.
- The transcript returns to being a pure deliberation record.

## ③ The channel marker

`kind:'sys'` posts route to the panel by default; a `channel:'tech'` field on
machine-kind posts makes the routing explicit and gives future technical
emits a self-declaring home — the panel becomes the house for machine speech
generally, not just watchdog lines.

## Acceptance

1. Kill `commission_supervise.py` → the wall bulb darkens ≤30s (TTL law);
   restart → green beats.
2. Any `kind:'sys'` post → renders in the TECH LOG panel, NOT the transcript;
   the bus record is byte-identical to today (diversion is render-level).
3. Seat-dark watchdog posts → visible badge on the panel even while tucked.

## Surfaces

| Surface | Change | Size |
|---------|--------|------|
| `docs/DELIBERATION_MACHINE.html` | wall bulb (scene mount + stamp read), TECH LOG panel (render + badge), sys-kind routing to panel | ~115 lines total (bench from the aside) |

Zero server, zero feed, zero protocol changes — the bus and the record are
untouched by construction.

## Exclusions

- **History/persistence** — the panel reads the live feed window like the
  transcript does; no separate log file.
- **Filtering non-sys kinds** — positions/asides stay in the transcript.
- **Bulb interactivity** — it's an instrument, not a control (no click
  actions in this item).
