# DVM-008 — Watcher Resilience (presence stamp must never kill the ear)
**Drafted by:** MEMBER2 / THE ARCHITECT
**Problem:** `stamp_presence()` runs at arm AND every ~2s loop tick. `os.replace(tmp, p)` throws `PermissionError [WinError 5]` whenever the destination `presence_<SEAT>.js` is momentarily open — the dashboard polls all seats' presence files by script-tag on the same 2s cadence, so collisions are frequent once multiple seats stamp. The exception is uncaught → **watcher exits 1 mid-loop → ear goes dead**. Observed 4 kills in one session (2 back-to-back); orphaned `presence_MEMBER2.js.tmp` files confirm the .tmp write lands and the replace is what fails.

## Contract
A missed pulse must **degrade to staleness, never a dead process**. The stale-pulse detector (6s) already tolerates skipped beats; the only unforgivable failure is the watcher dying.

## Patch (commission_watch.py — stamp_presence only)
```python
def stamp_presence(seat, armed_ms, ttl_ms):
    # DVM-002 side channel: sole-writer per seat, temp + atomic replace
    # so readers never see a torn file. Best-effort: a lock on the
    # destination is transient — retry briefly, then skip the beat.
    p = os.path.join(HERE, 'presence_%s.js' % seat)
    body = 'window.DVM_PRESENCE_%s = %s;\n' % (seat, json.dumps({
        'seat': seat, 'armed_at': armed_ms, 'ttl_ms': ttl_ms,
        'pulse_at': int(time.time() * 1000)}))
    tmp = p + '.tmp'
    for i in range(3):
        try:
            with open(tmp, 'w', encoding='utf-8') as f:
                f.write(body)
            os.replace(tmp, p)
            return
        except OSError:
            time.sleep(0.1 + 0.1 * i)   # 100/200ms; ~300ms worst case
    # all retries failed — skip this beat; stale pulse, live watcher
```

## Why this shape
- **Retry first**: the browser's read lock is a sub-second transient; 2 retries with 100/200ms backoff will win most races (observed re-arms stamp clean ~immediately after a kill).
- **Skip beats, don't die**: worst case adds ≤300ms to one 2s tick — invisible to the pulse cadence.
- **No signature/behavior change**: same atomic temp+replace, same file shape, same sole-writer rule. Callers untouched.
- Orphaned `.tmp` files are harmless (readers only load `presence_*.js`); a failed beat leaves the last good stamp in place.

## Not in scope
- Changing the poll cadence or the stale threshold (6s) — the current values already absorb skipped beats.
- Lock-free writes direct to `p`: violates the never-show-a-torn-file guarantee for dashboard readers.

## Attack points
- 3 retries vs more: beyond ~300ms the pulse is already going stale; more retries don't help.
- Logging a skipped beat: watcher stdout is a notification channel; a per-tick miss log would spam relays — silent-skip is deliberate.
