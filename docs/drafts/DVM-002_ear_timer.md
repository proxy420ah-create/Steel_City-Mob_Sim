# DVM-002 — Ear Timer — draft v0.1 (THE ARCHITECT)

*Rebuttal amendments ruled in (devin-18): both accepted — see tail.*

**Boss's ask**: see each seat's remaining listener time on the dashboard —
"how long do I have your attention for."

## Ruling: presence is a side channel, not a bus write

CHAIR's opening puts `members.<seat>.listen_until` inside the feed and declares
stamping "housekeeping, not a turn". **Rejected on structure**: any off-turn
writer is a read-modify-write racing the floor holder — the file has been
mangled twice tonight by exactly that pattern. The exemption blesses the race;
it doesn't remove it. And on-turn stamping is stale exactly when it matters
(the question is never "is the seat live while it writes" — it's "is it live
*between* writes").

The ear already knows seat, timeout, and start time. Let it report itself —
into a file it solely owns.

## Contract

### `presence_<SEAT>.js` — one per seat, sole-writer per ear

```js
window.DVM_PRESENCE_MEMBER2 = {
  "seat": "MEMBER2",
  "armed_at": 1730330000000,   // epoch ms — watcher process start
  "ttl_ms": 600000,            // watcher's timeout argument
  "pulse_at": 1730330100000    // epoch ms — last poll tick (~2s)
};
```

- Written by `commission_watch.py` at arm time; `pulse_at` refreshed each poll.
- Write to temp + `os.replace` → readers never see a torn file.
- Per-seat file = one writer = zero contention. No turn token, no protocol
  change, append-only stays absolute.

### Dashboard (`DELIBERATION_MACHINE.html`)

- For each occupied seat, load `presence_<SEAT>.js` by **`<script>`-tag
  injection** — the same mechanism as the feed poll (fetch dies on `file://`;
  script tags don't). Reads `window.DVM_PRESENCE_<SEAT>`, nulls it, removes
  the tag.
- Chip renders `armed_at + ttl_ms - now` as a ticking `ear m:ss`.
- Missing file, or `now - pulse_at > ~6s`, or countdown expired → `ear dead`,
  seat derives **away** — the manual `members.<seat>.status` flag is retired;
  pulse age becomes the single source of truth for lit/away/dead.

## Failure modes

| Case | Behaviour |
|---|---|
| Ear times out, no re-arm | file goes stale → `ear dead` (true signal: no ear = no attention) |
| Re-arm | new `armed_at`, countdown restarts (self-healing) |
| Re-arm with different ttl | new file supersedes — no ambiguity |
| Agent alive but earless | correctly reads dead — attention IS the ear |
| Clock skew agent↔boss | same host, negligible; `pulse_at` is the ground truth |

## Cost

- `commission_watch.py`: ~8 lines (stamp on arm, refresh on poll)
- `DELIBERATION_MACHINE.html`: ~25 lines (per-seat script-tag loader + chip renderer + away derivation)
- Protocol: **zero changes**

---

## Amendments ruled in — rebuttal (devin-18), accepted by the drafter

1. **Load via script-tag injection, not fetch** — verified against the live
   dashboard: the feed itself polls by `<script>` injection because fetch is
   dead on `file://`. Presence files keep the `window.* = {}` wrapper for
   exactly this reason. Accepted — already implicit in the v0 format, now explicit.
2. **Retire `members.<seat>.status`** — lit/away/dead derives solely from
   presence-file age; removes the second, manual source of truth that let a
   dead seat read lit (the boss's devin-9 catch). Accepted.
