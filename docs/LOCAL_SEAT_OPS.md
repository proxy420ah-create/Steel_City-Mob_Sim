# Local Seat Ops — Commission Seats on LM Studio

**Created**: 2026-11-10 · **Status**: live system — six seats on `qwen3.5-9b`

The Commission's chairs are held by `commission_seat.py` hosts driving **one**
LM Studio model (OpenAI `/v1` at `localhost:1234`) on the serial GPU. The bus,
lanes, register, and stamps are unchanged from the session-seat era — only the
mind behind each chair moved. This doc is the operator reference: the tools,
the daily workflow, and how to read what you see.

---

## The tool family

| Tool | Kind | Purpose |
|---|---|---|
| **`seat_panel.py`** | Persistent window (Tk, stdlib) | **THE control surface — one file, whole system.** Probes every 3s: LM/server lamps, daemon row (server/supervisor/inboxes) with per-service START/STOP, per-seat host/ear/stamp/log + KILL/SEAT, master SYSTEM UP / SYSTEM DOWN |
| `seat_panel.bat` | **The one file to click** | `docs\seat_panel.bat` — opens the cockpit |
| `seats_up.bat` | One-shot | Wait for `/v1/models` (≤60s), then spawn only missing hosts (dedup) |
| `seats_down.bat` | One-shot | Kill all six hosts **+ ears** (spares `--mailbox` pulses — those are the inbox daemon's) |
| `seat_down.bat <SEAT>` | One-shot | Kill one seat (host+ear) |

Panel and .bats do the same operations on two surfaces. **Panel = daily driver;
.bats = scriptable fallback** (Task Scheduler, SSH, chained scripts, panel dead).

### Panel controls

- **SYSTEM UP** — infrastructure first (server, supervisor, missing inboxes),
  then seats through the brain-wait path. One click, whole machine.
- **SYSTEM DOWN** — the whole machine cold: seats+ears, supervisor, inboxes +
  their `--mailbox` pulses, server. (The panel itself stays — it's the switch,
  not the machine.)
- **Daemon row toggles** — START/STOP per service: server, supervisor,
  inboxes (counted n/6; STOP kills daemons *and* their mailbox pulses —
  the same orphan class as seat ears).
- **SEAT** (per row) — spawn that host *instantly*, no brain check. Deliberate:
  the surgical tool — "seat it while the brain's off, watch it exit-2" is a
  valid demo.
- **SEAT ALL** — waits for `/v1/models` like the bat (status shows
  `waiting for LM Studio…`), then spawns only missing hosts.
- **KILL** (per row) / **KILL ALL** — host + ear together; on job-bound hosts
  the ear dies anyway, on old hosts the explicit kill prevents orphans (C35).

The panel never touches **LM Studio itself** — the brain is an external app;
the lamp is display-only truth. Everything Python-side, the panel owns.

## The daily workflow

### Cold start — bring the table up

```
1. LM Studio → start the local server (model: qwen3.5-9b)
2. seat_panel.bat            → watch the top lamp go green
3. SEAT ALL (or seats_up.bat) → six rows fill in ~12s
```

`seats_up.bat` forgives running early — it blocks on `/v1/models` before
spawning. A fresh host exits `code 2` at the resolve gate if the brain is
unreachable, so **brain first, chairs second** is the clean order.

### Refresh hosts onto new code

Running processes freeze at their launch code — a `.bat` or data file is always
current; a running host is not.

```
KILL ALL (or seats_down.bat) → all rows dark
SEAT ALL (or seats_up.bat)   → all six spawn on current code
```

### One seat

```
KILL on its row (or seat_down.bat SEAT) → row goes dark
SEAT on its row                          → host respawns (instant)
```

## Reading the state ladder

| Panel row | Meaning | Action |
|---|---|---|
| `● <pid>` host, `●` ear, lamp `<n>s` | Working | — |
| `●` host, `○` ear, lamp fresh | Ear just cycled / between re-arms | watch one poll |
| `○` host, `●` ear | **ORPHAN-EAR** — lamp lying (C35); supervisor flags BOSS | kill the ear PID |
| `○` `○` `dark` | Vacant — honest | SEAT when brain's up |
| top lamp red | LM Studio off — no host can think | start model server |
| `Commission :8723 DOWN` | The bus itself is down — nothing works | restart `commission_server.py` |

**Log column** = last line of `seat_host_<SEAT>.log` — the ground truth under
every lamp: `LM call failed`, `ear stale 300s — re-arming`, `posted X`, holds,
refusals, vacate reasons all land here.

## The honesty mechanics (why the lamp can be trusted)

- **Job-bound ears** (C35): each host binds its pulse ear to a Windows Job
  Object, `KILL_ON_JOB_CLOSE` — host death reaps the ear automatically; a lamp
  can't stay lit on a dead chair.
- **ORPHAN-EAR flag** (C35/C36): the supervisor compares ear carriers vs
  `commission_seat.py <SEAT>` hosts; ear-without-host → `WATCHDOG … ORPHAN-EAR`
  to BOSS; clears on re-seat.
- **Brain-death exit** (C40): on a failed model call the host probes
  `/v1/models`. Probe *answers* → transient, ride it. Probe *dead* → strike;
  two consecutive strikes (~10 min, immune to reload flaps) → `sys.exit(3)` →
  ear reaped → honestly dark. Asleep-at-the-desk no longer exists: the ladder
  is binary — working or vacant.
- **Exit codes**: `2` = no model at startup gate · `3` = brain died mid-life.
- **Supervisor** (`commission_supervise.py`, one daemon): dark/relit/orphan/
  floor-owed/stamp-loop flags post `kind:'sys'` to BOSS; watchdogs, never a
  re-animator — re-seating a mind is the boss's call.

## Per-seat file map (`docs\`)

| File | Role |
|---|---|
| `induction_<SEAT>.local.txt` | The seat's nature — durable role/contract |
| `seat_state_<SEAT>.md` | Now — stance, debts, notes, `## record` hot window (last 30 diary lines) |
| `seat_diary_<SEAT>.md` | Autobiography — append-only; `NEED: seat_diary_<SEAT>.md#<date>` reaches the archive |
| `seat_host_<SEAT>.log` | Every wake/post/failure — the diagnosis trail |
| `presence_<SEAT>.js` | The lamp — server-stamped while an ear socket is held |
| `busy_<SEAT>.js` | Labor mark — THINKING/kind stamps during wakes |
| `pulse_n_<SEAT>.js` | Doorbell cursor (a hint, not a guard — C32) |
| `seat_drain_<SEAT>.js` | Inbox consumption cursor |

Bugs cataloged this cycle: `COMMISSION_BUGS.md` **C34–C40** (lane drift,
orphan ear, quoted-needle, silent memory, NEED-plane defects, 409 crash,
asleep-at-the-desk). Ledger arc: `COMMISSION_LEDGER.md` → *The local-seat arc*.
