#!/usr/bin/env python3
"""Commission bus watcher — block until it's your seat's turn.

Usage:  python commission_watch.py <SEATKEY> [timeout_seconds] [--persist]

Polls deliberation_feed.js (same folder) every ~2s. When the file's
`turn` field equals SEATKEY, prints the newest messages and exits 0.
On timeout prints TIMEOUT and exits 1 — re-arm to keep waiting.

`--persist` (DVM-020 C9b stream-mode, opt-in): the watcher prints each
wake and keeps polling instead of exiting — for agent classes that read
a live stream. Exit-mode remains the law on relay/blocking substrates,
where the process exit IS the doorbell.

Any agent that can run this in a background shell and block on the
call can sit at the table.
"""
import sys, os, re, json, time
sys.stdout.reconfigure(errors='replace')  # bus text carries →/— etc; cp1252 consoles must not crash

HERE = os.path.dirname(os.path.abspath(__file__))
FILE = os.path.join(HERE, 'deliberation_feed.js')

# bus writers use both legacy and current seat keys for `turn` — resolve like
# the dashboard's LEGACY_SEAT so an ear armed on a current name still wakes.
LEGACY_SEAT = {'DEVIN': 'CHAIR', 'MEMBER2': 'ARCHITECT', 'MEMBER3': 'BUTTON', 'MEMBER4': 'INFORMANT',
               'PRAGMATIST': 'ARCHITECT'}  # retired MEMBER2 persona — still surfaces in turn (CHAIR-488)

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
    # DVM-020: `beat` marks a heartbeat-era record — `armed_at` reads as
    # "last seen alive", not "armed since" (C9a).
    p = os.path.join(HERE, 'presence_%s.js' % seat)
    body = 'window.DVM_PRESENCE_%s = %s;\n' % (seat, json.dumps({
        'seat': seat, 'armed_at': armed_ms, 'ttl_ms': ttl_ms,
        'pulse_at': int(time.time() * 1000), 'beat': True}))
    tmp = p + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        f.write(body)
    os.replace(tmp, p)

def try_stamp(seat, armed_ms, ttl_ms, tries=3):
    # DVM-010: retry softens the WinError-5 replace race; beyond retries we
    # skip and keep watching. Returns True on success — a persistent ear
    # that cannot heartbeat must die (DVM-020 C9b terminal condition).
    for _ in range(tries):
        try:
            stamp_presence(seat, armed_ms, ttl_ms)
            return True
        except OSError:
            time.sleep(0.4)
    return False

# DVM-017 §1: wake-label taxonomy — the newest unseen non-self message names
# the wake. Precedence lives in ordering law, not a table: named > speech,
# boss > ambient, side-table named > boss-gossip but < boss-speech.
def wake_label(m, seat):
    to = [LEGACY_SEAT.get(t, t) for t in (m.get('to') or [])]
    named = seat in to
    if m.get('kind') == 'aside':
        if named:
            return 'ADDRESSED (aside)'          # side-channel ping — never a floor call
        return 'BOSS SPOKE (aside)' if m.get('speaker') == 'BOSS' else 'OVERHEARD'
    if named:
        return 'ADDRESSED (boss)' if m.get('speaker') == 'BOSS' else 'ADDRESSED'
    if m.get('speaker') == 'BOSS':
        return 'BOSS SPOKE'
    return 'OVERHEARD'

# DVM-020 C11: the batch claims its highest-precedence unseen message, not
# its tail — rank map mirrors the §ladder law (named > speech, boss >
# ambient, side-table named > boss-gossip but < boss-speech).
LABEL_RANK = {'ADDRESSED (boss)': 6, 'ADDRESSED': 5, 'BOSS SPOKE': 4,
              'ADDRESSED (aside)': 3, 'BOSS SPOKE (aside)': 2, 'OVERHEARD': 1}

def batch_label(trigger, seat):
    # argmax over the whole unseen non-self batch; returns (label, winning
    # message) so the print can always evidence the claim (INFORMANT nit 1).
    win = max(trigger, key=lambda m: LABEL_RANK[wake_label(m, seat)])
    lab = wake_label(win, seat)
    if len(trigger) > 1:
        lab += ' (+%d ambient)' % (len(trigger) - 1)
    return lab, win

HB_TTL = 30000          # C9a: a presence stamp lives 30s, not the whole arm window
PERSIST_MISS_MAX = 30   # C9b terminal: feed unreadable ~60s — die rather than lie

def ear_suffix(turn, seats):
    # §2: read the floor-holder's claim-window each tick — only when turn ∈
    # seats (BOSS floor is the resting state; presence_BOSS.js doesn't exist).
    if turn not in seats:
        return ''
    p = os.path.join(HERE, 'presence_%s.js' % turn)
    if not os.path.exists(p):
        return ' (ear missing)'
    try:
        raw = open(p, encoding='utf-8').read()
        m = re.search(r'=\s*(\{.*\})\s*;?\s*$', raw, re.S)
        d = json.loads(m.group(1)) if m else {}
    except Exception:
        return ' (ear ?)'
    rem = d.get('armed_at', 0) + d.get('ttl_ms', 0) - int(time.time() * 1000)
    if rem > 0:
        s = rem // 1000
        return ' (ear live, ~%s)' % ('%dm' % (s // 60) if s >= 120 else '%ds' % s)
    return ' (ear DARK — stale %ds over TTL)' % (-rem // 1000)

def main():
    seat = sys.argv[1]
    rest = [a for a in sys.argv[2:] if a != '--persist']
    persist = len(rest) != len(sys.argv[2:])   # C9b stream-mode opt-in — exit-mode is the relay law
    timeout = float(rest[0]) if rest else 240.0
    deadline = time.time() + timeout
    armed_ms = int(time.time() * 1000)
    try_stamp(seat, armed_ms, HB_TTL)   # the arm stamp is the first beat — heartbeat-era from birth
    last_mtime = 0  # first poll always evaluates current state — handles "already my turn"
    beats = 0; misses = 0; hb_fails = 0; turn_held = False
    # DVM-017 OMNISCIENT: everything already on the bus at arm-time is seen —
    # only messages arriving after this arm can wake this ear.
    d0 = load() or {}
    seen = {m.get('id') for m in d0.get('messages', [])}

    while persist or time.time() < deadline:
        time.sleep(2)
        beats += 1
        if beats % 5 == 0:
            # C9a heartbeat: re-stamp every ~10s with a 30s TTL — a dead
            # process stops beating and the lamp darkens within one TTL.
            if try_stamp(seat, int(time.time() * 1000), HB_TTL):
                hb_fails = 0
            else:
                hb_fails += 1
                if persist and hb_fails >= 3:
                    print('HEARTBEAT DEAD — %d stamps failed; dying so the lamp cannot lie' % hb_fails)
                    sys.exit(2)
        if not os.path.exists(FILE):
            misses += 1
            if persist and misses > PERSIST_MISS_MAX:
                print('FEED LOST — feed unreadable %d polls; dying rather than lying' % misses)
                sys.exit(1)
            continue
        mtime = os.stat(FILE).st_mtime
        if mtime == last_mtime:
            continue
        last_mtime = mtime
        data = load()
        if not data:
            misses += 1
            if persist and misses > PERSIST_MISS_MAX:
                print('FEED LOST — feed unreadable %d polls; dying rather than lying' % misses)
                sys.exit(1)
            continue
        misses = 0
        if data.get('adjourned'):
            # DVM-007 dormant mode: terminal stamp kills the ear honestly —
            # under heartbeat the dark stamp also means "stood down" (C9a).
            print('MEETING ADJOURNED — seats stand down; dormant until the flag clears')
            try_stamp(seat, armed_ms, 0)
            while True:
                time.sleep(2)
                if not os.path.exists(FILE):
                    misses += 1
                    if persist and misses > PERSIST_MISS_MAX:
                        print('FEED LOST — feed unreadable %d polls; dying rather than lying' % misses)
                        sys.exit(1)
                    continue
                mtime = os.stat(FILE).st_mtime
                if mtime == last_mtime:
                    continue
                last_mtime = mtime
                d = load()
                if not d:
                    misses += 1
                    if persist and misses > PERSIST_MISS_MAX:
                        print('FEED LOST — feed unreadable %d polls; dying rather than lying' % misses)
                        sys.exit(1)
                    continue
                misses = 0
                if not d.get('adjourned'):
                    print('RECONVENED — flag cleared (re-arm to relight)')
                    if persist:
                        break
                    sys.exit(0)
            continue
        msgs = data.get('messages', [])
        unseen = [m for m in msgs if m.get('id') not in seen]
        for m in unseen:
            seen.add(m.get('id'))
        # omniscient trigger: any unseen message not authored by self —
        # the same parse, a wider condition; coalesces free per poll.
        trigger = [m for m in unseen if m.get('speaker') != seat]

        turn_raw = data.get('turn')
        turn = LEGACY_SEAT.get(turn_raw, turn_raw)
        floor = 'floor: %s%s' % (turn_raw, ear_suffix(turn, data.get('seats') or []))
        if turn == seat:
            if not persist or not turn_held or unseen:
                print(f'YOUR TURN ({seat}) — phase {data.get("phase")}, {len(msgs)} messages. {floor}. Newest:')
                for m in msgs[-5:]:
                    who = m.get('speaker', 'SYS')
                    print(f'  [{m.get("kind","?")}] {who}: {m.get("text","")}')   # full text — a truncated preview impersonates a cut message (boss-154 lesson)
                if data.get('proposal'):
                    print(f'PROPOSAL {data["proposal"].get("id","")}: {data["proposal"].get("text","")}')
            turn_held = True
            if not persist:
                sys.exit(0)
            continue
        turn_held = False

        if trigger:
            lab, win = batch_label(trigger, seat)
            print(f'{lab} ({seat}) — {floor} — read-mode; write only when floor is yours:')
            if win not in trigger[-5:]:
                # evidenced claim: the labeled message prints even beyond the tail
                print(f'  [{win.get("kind","?")}] {win.get("speaker","SYS")}: {win.get("text","")}')
            for m in trigger[-5:]:
                who = m.get('speaker', 'SYS')
                print(f'  [{m.get("kind","?")}] {who}: {m.get("text","")}')
            if not persist:
                sys.exit(0)

    print(f'TIMEOUT — still not {seat}\'s turn (re-arm to keep waiting)')
    sys.exit(1)

if __name__ == '__main__':
    main()
