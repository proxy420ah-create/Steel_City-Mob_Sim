"""commission_busy.py — the agent-alive mark (DVM-921 dual-stamp).

A seat's HOST writes busy_<SEAT>.js — sole-writer per file (the C7 law; the
watcher owns presence_<SEAT>.js, the agent owns this one). The mark proves
agent-alive, never loop-alive: a dead host cannot stamp, and the TTL decays
any stale claim in <=30s.

Work-beat convention: stamp BEFORE labor (`on`), re-stamp while working,
clear on done (`off`). The supervisor's respawn predicate reads the mark as
condition 4 — no fresh busy mark, no respawn.

Usage:  python commission_busy.py <SEAT> [on|off] [kind]
        kind (DVM-925): THINKING|READING|COMPOSING|WORKING or a seat-flavor
        (AUDITING|BUILDING|DRAFTING|GATING|MODERATING). Absent kind = COMPOSING
        (back-compat — a no-kind writer reads honest, never errors).
"""
import json, os, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
TTL_MS = 30000
KINDS = {'THINKING', 'READING', 'COMPOSING', 'WORKING',
         'AUDITING', 'BUILDING', 'DRAFTING', 'GATING', 'MODERATING'}


def stamp(seat, on=True, kind='COMPOSING'):
    p = os.path.join(HERE, 'busy_%s.js' % seat)
    k = kind.upper()
    rec = {'seat': seat,
           'busy': bool(on),
           'armed_at': int(time.time() * 1000) if on else 0,
           'ttl_ms': TTL_MS,
           'beat': True,
           # DVM-925: enum kinds + WAITING:<seat> pattern (a self-declared block);
           # unmapped degrades to COMPOSING, never errors.
           'kind': k if (k in KINDS or k.startswith('WAITING:')) else 'COMPOSING'}
    tmp = p + '.tmp'
    with open(tmp, 'w') as f:
        f.write('window.DVM_BUSY_%s = %s;\n' % (seat, json.dumps(rec)))
    try:
        os.replace(tmp, p)   # atomic; a failed replace skips the beat, never kills
    except OSError:
        pass
    return rec


if __name__ == '__main__':
    seat = sys.argv[1].upper()
    on = len(sys.argv) < 3 or sys.argv[2].lower() != 'off'
    kind = sys.argv[3].upper() if len(sys.argv) > 3 else 'COMPOSING'
    r = stamp(seat, on, kind)
    print('busy_%s.js: busy=%s armed_at=%d kind=%s' % (seat, r['busy'], r['armed_at'], r['kind']))
