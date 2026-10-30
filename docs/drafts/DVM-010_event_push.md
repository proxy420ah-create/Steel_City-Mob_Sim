# DVM-010 — Event Push + Ear Tightening (foundational hardening) · v0.2 post-gate
**Drafted by:** THE ARCHITECT · per Rule Six, on the CHAIR's delegation (CHAIR-82)
**INFORMANT gate #1 (INFORMANT-2): THIN → all findings closed below.**
**Boss's ask:** does the page have to poll every 2s, or can it watch for file changes?
**Honest answer:** a browser page cannot watch files — no filesystem API exists browser-side. But the *server* can watch on the page's behalf and push changes over a persistent connection: **SSE (Server-Sent Events)** — one-directional push over plain HTTP, exactly this problem's shape.

## Half 1 — Dashboard push (commission_server.py + DELIBERATION_MACHINE.html)
- New endpoint `GET /events` → SSE stream (`text/event-stream`, keep-alive, `Cache-Control: no-cache`).
- Daemon thread stats mtimes at ~250–500ms: `deliberation_feed.js` + `presence_*.js` (globbed each cycle — new seat files self-register). On change → emit `data: {"file":"deliberation_feed"}` / `{"file":"presence_<SEAT>"}`.
- **SSE carries the signal; the existing script-tag loader still carries the payload.** One serialization path — no second contract, no JSON-in-SSE parsing.
- Heartbeat comment (`:\n\n`) ~15s keeps the conn alive; EventSource auto-reconnects for free.
- Dashboard: `new EventSource('/events')` → `onmessage` → reload the named file through the same script-tag path the poll uses today. **Wrap construction in try/catch** — a `file://` page can throw synchronously on the scheme, not just via `onerror` (gate note).
- **Stated assumption (finding 1):** SSE engages only when the page is served over HTTP by a running `commission_server.py` (`http://127.0.0.1:8723/`). If the boss opens `file://`, the fallback poll engages — the spec says so, it doesn't hide it.
- **Visible mode chip** (finding 1): `PUSH_OK` surfaces as a small chip in the status area — `push`/`poll` legible at a glance, not inferable.
- **Fallback**: no EventSource or stream errors → the current 2s `setInterval` poll engages. `file://` serving lands on the fallback automatically — page keeps working either way.
- Wedge watchdog stays — it guards the script-tag loader regardless of trigger source.

## Half 2 — Agent ears: the per-tick stamp is dead code (the sharper finding)
The CHAIR's brief asks for mtime stat over JSON-parse per cycle — **verified: the watcher already does this** (line 56 `os.stat().st_mtime`, `load()` only on change). The real waste is one layer deeper:

- Under the boss's binary amendment, `earInfo()` reads **only `armed_at + ttl_ms`** — `pulse_at` is *unread*. The watcher re-stamps presence **300 times per arm** for a field nothing consults.
- **Fix: stamp once at arm, then a pure stat loop** — with one explicit carve-out (finding 2): the DVM-007 **dormant terminal stamp** (`watch.py:67`, `ttl_ms:0` on adjourn) is *preserved* — it still writes on dormancy entry to kill the ear honestly. "Stamp once" means per-arm + per-adjourn, never per-tick.
- Arm-time stamp gets **~3-attempt retry with short backoff** (finding 3): stamp-once makes a failed arm visible as *vacant* for the whole TTL — a 4-line retry closes the new exposure cheaply. Beyond retries: skip and keep watching — the watcher never dies on a stamp.
- Side-effect worth the whole ticket: the WinError-5 collision surface (16 kills tonight) shrinks ~300× — one replace per arm instead of one per 2s. DVM-008's retry stays queued for the residual feed-write collisions; the fire dies at the source here.
- **Stat-tighten the dormant loop too** (gate note): `watch.py:68-73` full-parses the feed every 2s while dormant — same mtime-stat check applies; dormant ears stat, not parse.

## Contract changes — none on the bus
No feed-schema change: `turn`, `speaker`, `seats`, `adjourned` all untouched. This is a transport+IO hardening — the bus contract is identical, the plumbing gets honest.

## What it doesn't do
- Bus writes unchanged: agents still read→modify→write the whole feed; the server still owns /boss /adjourn /reconvene.
- Presence stays sole-writer per seat — one stamp per arm, same file shape (pulse_at field retained in the payload for backward compat; nothing new reads it).

## Attack points
- SSE vs WebSocket: one-directional push needs no bidirectional framing — WS is veto-worthy over-engineering here.
- Push-signal vs push-payload: pushing full JSON in SSE would create a second serialization contract; signaling the filename keeps ONE payload path. Signal chosen.
- Stamp-once vs keep-pulsing: under binary semantics the pulse has no reader — deleting it is the honest fix, and it happens to delete the bug's oxygen.
- ThreadingHTTPServer + one SSE client: one thread per connection — the page is one client; fine.
- Coalesced events: rapid writes between stat ticks merge — the page reloads latest content on each event; acceptable.

## Migration
Ships as one BUTTON build: `/events` endpoint + watcher-stat daemon, dashboard EventSource wiring + fallback flag, watcher per-tick stamp deletion. No bus write needed — zero-state migration.
