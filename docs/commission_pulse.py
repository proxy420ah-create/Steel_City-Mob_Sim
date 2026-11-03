#!/usr/bin/env python3
"""Commission pulse client — the ear as a long-poll (triage build).

Usage:  python commission_pulse.py <SEAT> [--follow] [server]

Holds GET /pulse/<SEAT>?since=N against the commission server (~25s each).
The server returns the INSTANT new feed messages land — that return is the
doorbell: run this as a blocking call and its exit wakes the host (blocking
seats) or completes the relay subagent (relay seats). Heartbeat responses
re-issue immediately — the steady pulse.

This replaces the watch-file/poll/TTL loop (commission_watch.py): liveness
is the open socket observed by the server, not a self-stamped file. One
client, both substrates — no per-seat redesign needed.

--follow keeps streaming instead of exiting on the first batch (debug use).
"""
import json, os, sys, time, urllib.request

SEAT = sys.argv[1].upper()
BASE = 'http://127.0.0.1:8723'
SINCE_ARG = None
for i, a in enumerate(sys.argv[2:]):
    if a.startswith('http'):
        BASE = a.rstrip('/')
    elif a == '--since' and i + 2 < len(sys.argv):
        try:
            SINCE_ARG = int(sys.argv[i + 3])
        except ValueError:
            pass
FOLLOW = '--follow' in sys.argv
# DVM-923: a carried --since resumes at the watermark the last relay died
# holding; absent/zero = cold-start (-1 → the init batch covers the tail).
since = SINCE_ARG if SINCE_ARG is not None and SINCE_ARG >= 0 else -1
cold_start = since < 0
dead_since = time.time()

def stamp_n(n):
    # DVM-923 watermark-as-file — sole-writer per seat, every exit path.
    p = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     'pulse_n_%s.js' % SEAT)
    open(p + '.tmp', 'w', encoding='utf-8').write(
        'window.DVM_PULSEN_%s = {"n": %d};\n' % (SEAT, n))
    os.replace(p + '.tmp', p)

def req():
    u = '%s/pulse/%s?since=%d' % (BASE, SEAT, since)
    with urllib.request.urlopen(u, timeout=35) as r:
        return json.loads(r.read())

print('PULSE %s — holding on %s' % (SEAT, BASE), flush=True)
while True:
    try:
        d = req()
        dead_since = time.time()
    except Exception as e:
        if time.time() - dead_since > 90:
            stamp_n(since)
            print('PULSE DEAD — server unreachable 90s: %s' % e, flush=True)
            sys.exit(2)
        time.sleep(3)
        continue
    if 'n' in d and since < 0:
        since = d['n']                          # first response = high-water mark
        print('PULSE sync — since=%d' % since, flush=True)
        continue
    if d.get('new'):
        for m in d.get('msgs', []):
            print('[%s] %s: %s' % (m.get('kind', '?'), m.get('speaker', 'sys'),
                                   (m.get('text') or '')[:400]), flush=True)
        print('floor: %s — your_turn: %s — addressed: %s' % (d.get('turn'), d.get('your_turn'), d.get('addressed')),
              flush=True)
        if not FOLLOW:
            stamp_n(d['n'])                     # the watermark the seat resumes at
            sys.exit(0)                         # the doorbell — exit wakes the host
        since = d['n']
    # {'new': False, 'hb': ts} → fall through and re-issue (the steady pulse)
