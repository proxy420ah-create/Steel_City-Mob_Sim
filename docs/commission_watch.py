#!/usr/bin/env python3
"""Commission bus watcher — block until it's your seat's turn.

Usage:  python commission_watch.py <SEATKEY> [timeout_seconds]

Polls deliberation_feed.js (same folder) every ~2s. When the file's
`turn` field equals SEATKEY, prints the newest messages and exits 0.
On timeout prints TIMEOUT and exits 1 — re-arm to keep waiting.

Any agent that can run this in a background shell and block on the
call can sit at the table.
"""
import sys, os, re, json, time
sys.stdout.reconfigure(errors='replace')  # bus text carries →/— etc; cp1252 consoles must not crash

HERE = os.path.dirname(os.path.abspath(__file__))
FILE = os.path.join(HERE, 'deliberation_feed.js')

def load():
    try:
        raw = open(FILE, encoding='utf-8').read().strip()
        m = re.search(r'^window\.DVM_FEED\s*=\s*(\{.*\})\s*;?\s*$', raw, re.S | re.M)
        return json.loads(m.group(1)) if m else None
    except Exception:
        return None

def stamp_presence(seat, armed_ms, ttl_ms):
    # DVM-002 side channel: the ear reports itself. Sole-writer per seat,
    # temp + atomic replace so readers never see a torn file.
    p = os.path.join(HERE, 'presence_%s.js' % seat)
    body = 'window.DVM_PRESENCE_%s = %s;\n' % (seat, json.dumps({
        'seat': seat, 'armed_at': armed_ms, 'ttl_ms': ttl_ms,
        'pulse_at': int(time.time() * 1000)}))
    tmp = p + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        f.write(body)
    os.replace(tmp, p)

def try_stamp(seat, armed_ms, ttl_ms, tries=3):
    # DVM-010: stamp once per arm (pulse_at has no reader under binary
    # semantics) — retry softens the WinError-5 replace race; beyond
    # retries we skip and keep watching. The watcher never dies on a stamp.
    for _ in range(tries):
        try:
            stamp_presence(seat, armed_ms, ttl_ms)
            return
        except OSError:
            time.sleep(0.4)

def main():
    seat = sys.argv[1]
    timeout = float(sys.argv[2]) if len(sys.argv) > 2 else 240.0
    deadline = time.time() + timeout
    armed_ms = int(time.time() * 1000)
    ttl_ms = int(timeout * 1000)
    try_stamp(seat, armed_ms, ttl_ms)
    last_mtime = 0  # first poll always evaluates current state — handles "already my turn"
    # boss messages already on the bus at arm-time don't count as interjections
    d0 = load() or {}
    seen_boss = {m.get('id') for m in d0.get('messages', []) if m.get('speaker') == 'BOSS'}

    while time.time() < deadline:
        time.sleep(2)
        if not os.path.exists(FILE):
            continue
        mtime = os.stat(FILE).st_mtime
        if mtime == last_mtime:
            continue
        last_mtime = mtime
        data = load()
        if not data:
            continue
        if data.get('adjourned'):
            # DVM-007 dormant mode: terminal stamp kills the ear honestly,
            # armed deadline + boss checks suspended; the flag alone wakes.
            print('MEETING ADJOURNED — seats stand down; dormant until the flag clears')
            try_stamp(seat, armed_ms, 0)
            while True:
                time.sleep(2)
                if not os.path.exists(FILE):
                    continue
                mtime = os.stat(FILE).st_mtime
                if mtime == last_mtime:
                    continue
                last_mtime = mtime
                d = load()
                if d and not d.get('adjourned'):
                    print('RECONVENED — flag cleared (re-arm to relight)')
                    sys.exit(0)
        msgs = data.get('messages', [])
        boss_new = [m for m in msgs if m.get('speaker') == 'BOSS' and m.get('id') not in seen_boss]
        for m in boss_new:
            seen_boss.add(m.get('id'))

        if data.get('turn') == seat:
            print(f'YOUR TURN ({seat}) — phase {data.get("phase")}, {len(msgs)} messages. Newest:')
            for m in msgs[-5:]:
                who = m.get('speaker', 'SYS')
                print(f'  [{m.get("kind","?")}] {who}: {m.get("text","")[:200]}')
            if data.get('proposal'):
                print(f'PROPOSAL {data["proposal"].get("id","")}: {data["proposal"].get("text","")}')
            sys.exit(0)

        if boss_new:
            # the boss spoke while the floor is elsewhere — you hear it (read-mode),
            # but may only WRITE once the floor moves to you
            print(f'BOSS SPOKE (floor: {data.get("turn")}) — read-mode; write only when floor is yours:')
            for m in boss_new:
                print(f'  [boss] {m.get("text","")[:200]}')
            sys.exit(0)

    print(f'TIMEOUT — still not {seat}\'s turn (re-arm to keep waiting)')
    sys.exit(1)

if __name__ == '__main__':
    main()
