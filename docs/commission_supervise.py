"""Commission supervisor — DVM-921. One daemon per box.

Scans seats[] from the feed, reads presence_<SEAT>.js (loop-alive) and
busy_<SEAT>.js (agent-alive), runs the carrier-predicate inventory, and
respawns `python -X utf8 commission_watch.py <SEAT> 600` when the full
respawn predicate holds. Posts state transitions ONLY to the bus as
kind:'sys' — dark-detected posts are addressed to BOSS (ratified law).

Respawn predicate (ALL required):
  1. zero carriers   — no process whose CommandLine carries the seat's watcher
  2. beat-stale>2xTTL — grace window closes the mid-spawn TOCTOU
  3. not zeroed      — armed_at:0 is commanded dark (triage), hands off
  4. agent-alive     — busy_<SEAT>.js fresh inside TTL
  5. not adjourned   — recess keeps ears down
  6. blocking substrate — relay seats are FLAG-ONLY (post, never respawn):
     a daemon watcher would satisfy the host's carrier check while ringing
     no doorbell — starving the relay chain it backstops (the C17 cure).

Scope honesty: lamp-honesty + bus-visibility on every seat; the doorbell
itself only on blocking substrates. The daemon is a backstop, not the
bootstrap — hosts arm their first ear themselves.
"""
import glob, json, os, re, subprocess, sys, time

sys.stdout.reconfigure(errors='replace')
HERE = os.path.dirname(os.path.abspath(__file__))
FILE = os.path.join(HERE, 'deliberation_feed.js')
WATCH = os.path.join(HERE, 'commission_watch.py')
POLL = 5                     # scan cadence, seconds
GRACE_N = 2                  # beat-stale must exceed N*TTL before respawn
# seat -> substrate. relay seats are flag-only (post, never respawn).
SUBSTRATE = {'BUTTON': 'relay'}   # default: blocking

def load_feed():
    try:
        src = open(FILE, encoding='utf-8').read()
        return json.loads(re.search(r'window\.DVM_FEED\s*=\s*(\{.*\})\s*;?\s*$', src, re.S).group(1))
    except Exception:
        return None

def save_feed(d):
    # universal-merge (fix-lane GO dm-2773): union messages by id against the
    # disk state at save moment — the watchdog posts into live traffic, a stale
    # snapshot must never evaporate a seat's write. Scalars stay newer-wins.
    try:
        disk = load_feed()
        if isinstance(disk, dict) and isinstance(disk.get('messages'), list):
            by_id = {}
            seen = set()   # (speaker, ts, text) replay guard — identical to the
            for x in disk['messages']:  # server's merge: a stale snapshot must
                if isinstance(x, dict):  # never re-mint a purged message (C48)
                    by_id.setdefault(x.get('id'), x)
                    seen.add((x.get('speaker'), x.get('ts'), x.get('text')))
            merged = list(disk['messages'])
            for x in d.get('messages', []):
                mid = x.get('id') if isinstance(x, dict) else None
                if isinstance(x, dict) and (x.get('speaker'), x.get('ts'), x.get('text')) in seen:
                    continue
                if mid not in by_id:
                    merged.append(x); by_id[mid] = x
                elif x == by_id[mid]:
                    continue
                else:
                    m2 = re.match(r'^(.*?)(\d+)$', mid)
                    base, n = (m2.group(1), int(m2.group(2))) if m2 else (mid + '-', 1)
                    while base + str(n) in by_id: n += 1
                    x['id'] = base + str(n); merged.append(x); by_id[x['id']] = x
            d['messages'] = merged
    except Exception:
        pass
    tmp = FILE + '.tmp'
    open(tmp, 'w', encoding='utf-8').write('window.DVM_FEED = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
    # Feed-file lockdown (C33): the file sits read-only; toggle the flag around
    # the watchdog's own save so direct seat-side writers can't os.replace it.
    try:
        for attempt in range(3):   # C48: the server's own save can re-lock the
            try:                   # file between our chmod and our replace —
                if os.path.exists(FILE):  # retry instead of dying (watchdog
                    os.chmod(FILE, 0o666)  # must not crash on write contention)
                    os.replace(tmp, FILE)
                    break
            except PermissionError:
                time.sleep(0.3)
    finally:
        if os.path.exists(FILE):
            os.chmod(FILE, 0o444)

def read_jsvar(path):
    try:
        return json.loads(re.search(r'\{.*\}', open(path, encoding='utf-8').read(), re.S).group(0))
    except Exception:
        return None

def carriers(seat):
    """CARRIER PREDICATE (BUTTON amendment): any process whose CommandLine
    contains 'commission_watch.py <SEAT>' — watcher, spawn-shell, any carrier."""
    cmdlines = []
    _nowin = getattr(subprocess, 'CREATE_NO_WINDOW', 0)   # detached daemon has no console — without this each 5s wmic pops a window
    try:
        out = subprocess.run(['wmic', 'process', 'get', 'CommandLine'],
                             capture_output=True, text=True, timeout=15,
                             creationflags=_nowin).stdout
        cmdlines = out.splitlines()
    except Exception:
        try:
            out = subprocess.run(['powershell', '-NoProfile', '-Command',
                                  'Get-CimInstance Win32_Process | Select-Object -ExpandProperty CommandLine'],
                                 capture_output=True, text=True, timeout=20,
                                 creationflags=_nowin).stdout
            cmdlines = out.splitlines()
        except Exception:
            pass
    # quoting-agnostic: spawn paths may arrive as `commission_pulse.py SEAT`
    # (cwd-relative) or `"C:\...\commission_pulse.py" SEAT` (absolute) — a
    # literal '.py SEAT' needle misses the quoted form (C36).
    needle = re.compile(r'commission_(?:watch|pulse)\.py["\']?\s+%s\b' % seat)
    # mailbox carve-out (boss decree): '--mailbox' sockets are the inbox daemon's
    # delivery holds, never a seat-ear — excluded so a live mailbox neither reads
    # RELIT nor satisfies the zero-carriers respawn gate.
    return sum(1 for c in cmdlines if needle.search(c) and '--mailbox' not in c)

def host_carriers(seat):
    """ORPHAN-EAR predicate (C35): a seat HOST process —
    'commission_seat.py <SEAT>'. An ear socket held with no host behind it
    keeps presence green while nothing consumes the doorbell: lit-but-deaf,
    the worst lamp lie there is."""
    cmdlines = []
    _nowin = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
    try:
        out = subprocess.run(['wmic', 'process', 'get', 'CommandLine'],
                             capture_output=True, text=True, timeout=15,
                             creationflags=_nowin).stdout
        cmdlines = out.splitlines()
    except Exception:
        try:
            out = subprocess.run(['powershell', '-NoProfile', '-Command',
                                  'Get-CimInstance Win32_Process | Select-Object -ExpandProperty CommandLine'],
                                 capture_output=True, text=True, timeout=20,
                                 creationflags=_nowin).stdout
            cmdlines = out.splitlines()
        except Exception:
            pass
    needle = re.compile(r'commission_seat\.py["\']?\s+%s\b' % seat)
    return sum(1 for c in cmdlines if needle.search(c))

def claimed(seat):
    """SESSION-CLAIM: session_seat_<SEAT>.js marks the seat held by an
    external session agent — no commission_seat.py host exists BY DESIGN,
    so 'pulse held, no host' is honest session occupancy, not the C35
    orphan lie. The claim is TTL-stamped (session refreshes ts each wake);
    a stale file means the session died silently — clean it up so normal
    orphan detection resumes on the next cycle."""
    p = os.path.join(HERE, 'session_seat_%s.js' % seat)
    try:
        d = json.loads(re.search(r'=\s*(\{.*\})',
                                 open(p, encoding='utf-8').read(), re.S).group(1))
        fresh = 0 < time.time() - d.get('ts', 0) / 1000 < d.get('ttl_ms', 0) / 1000
        if not fresh:
            os.remove(p)
        return fresh
    except Exception:
        return False

def stamp_supervisor():
    p = os.path.join(HERE, 'presence_SUPERVISOR.js')
    tmp = p + '.tmp'
    rec = {'seat': 'SUPERVISOR', 'armed_at': int(time.time() * 1000),
           'ttl_ms': 30000, 'pulse_at': int(time.time() * 1000), 'beat': True}
    open(tmp, 'w', encoding='utf-8').write('window.DVM_PRESENCE_SUPERVISOR = %s;\n' % json.dumps(rec))
    try:
        os.replace(tmp, p)
    except OSError:
        pass

def sys_post(seat, text, to=None):
    d = load_feed()
    if not d:
        return
    msgs = d.setdefault('messages', [])
    mid = 'sys-supervise-%d' % (len(msgs) + 1)
    m = {'id': mid, 'kind': 'sys', 'ts': int(time.time() * 1000), 'text': text}
    if to:
        m['to'] = to
    msgs.append(m)
    save_feed(d)
    print('[sys] %s' % mid, flush=True)

def respawn(seat):
    subprocess.Popen([sys.executable, '-X', 'utf8', WATCH, seat, '600'],
                     cwd=HERE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0) | getattr(subprocess, 'DETACHED_PROCESS', 0) | getattr(subprocess, 'CREATE_NEW_PROCESS_GROUP', 0))

def main():
    print('supervisor up — polling every %ds' % POLL, flush=True)
    state = {}   # seat -> {'dark':bool,'respawned':bool}
    while True:
        stamp_supervisor()
        d = load_feed()
        if d:
            adjourned = d.get('adjourned')
            now = int(time.time() * 1000)
            for seat in d.get('seats', []):
                st = state.setdefault(seat, {'dark': False, 'respawned': False})
                pres = read_jsvar(os.path.join(HERE, 'presence_%s.js' % seat)) or {}
                armed = pres.get('armed_at', 0)
                ttl = pres.get('ttl_ms', 30000)
                nc = carriers(seat)
                beat_stale = armed and (now - armed) > GRACE_N * ttl
                zeroed = armed == 0
                busy = read_jsvar(os.path.join(HERE, 'busy_%s.js' % seat)) or {}
                agent_alive = busy.get('busy') and (now - busy.get('armed_at', 0)) < busy.get('ttl_ms', 30000)
                lit = nc > 0 or (armed and (now - armed) < ttl)
                # DVM-921 flap fix (boss-ordered): dark = no carriers AND stamp stale
                # BEYOND the same N×TTL grace — relay re-arm gaps cross >TTL but
                # never >2×TTL, so sub-minute gaps stop flapping dark→relit.
                # Zeroed (armed==0) is commanded dark: never flagged, per law.
                dark = nc == 0 and armed and (now - armed) > GRACE_N * ttl
                # ORPHAN-EAR (C35): ear carriers exist but no seat HOST does —
                # the lamp shows lit while nothing consumes doorbells.
                # Flag-only: re-instantiating a mind is the boss's call,
                # never the watchdog's.
                nh = host_carriers(seat)
                if nc > 0 and nh == 0 and not st.get('orphan') and not adjourned and not claimed(seat):
                    st['orphan'] = True
                    sys_post(seat, 'WATCHDOG — %s ORPHAN-EAR (pulse held, no seat host — lamp lying, host respawn needed)' % seat, to=['BOSS'])
                if st.get('orphan') and nh > 0:
                    st['orphan'] = False
                    sys_post(seat, 'WATCHDOG — %s host re-seated (ear has a mind again)' % seat)
                if dark and not st['dark'] and not adjourned:
                    st['dark'] = True
                    # dual-stamp flag text (boss-ordered): name WHICH death —
                    # presence stale + busy fresh = ear died, host alive; both stale = host itself gone.
                    flag = ('WATCHDOG — %s ear-dark (host alive — re-arm fixes)' % seat) if agent_alive \
                           else ('WATCHDOG — %s host-dead? (presence + busy both stale — honest dark)' % seat)
                    sys_post(seat, flag, to=['BOSS'])
                if lit and st['dark']:
                    state[seat] = {'dark': False, 'respawned': False}
                    sys_post(seat, 'WATCHDOG — %s ear RELIT' % seat)
                if (st['dark'] and not st['respawned'] and nc == 0 and beat_stale
                        and not zeroed and agent_alive and not adjourned):
                    if SUBSTRATE.get(seat, 'blocking') == 'relay':
                        st['respawned'] = True
                        sys_post(seat, 'WATCHDOG — %s relay seat: flag-only, host re-arms own chain' % seat)
                    else:
                        respawn(seat)
                        st['respawned'] = True
                        sys_post(seat, 'WATCHDOG — %s ear RESPAWNED (all six conditions held)' % seat)
                # DVM-924 rung 1 — FLOOR-OWED (transient): a seat holding `turn`
                # past one TTL while the batch grew owes the register a move.
                # Self-clears the instant the register moves off the seat —
                # only persistence past STALL_TTL reads as a real stall (rung 2).
                if not adjourned and d.get('turn') == seat and d.get('turn_at') \
                        and (now - d['turn_at']) > 90000 and not st.get('floor_owed'):
                    st['floor_owed'] = True
                    sys_post(seat, 'WATCHDOG — %s FLOOR-OWED (register held %ss — transient; clears on move)' % (seat, (now - d['turn_at']) // 1000), to=['BOSS'])
                if st.get('floor_owed') and d.get('turn') != seat:
                    st['floor_owed'] = False
                # STAMP-LOOP (transient): a busy mark re-beating (armed_at advancing
                # across polls) while the seat's last post ages past 300s is a labor
                # claim without product — the stamp discipline firing on the beat-loop
                # instead of real work. Self-clears when the seat posts or goes quiet.
                if agent_alive:
                    last_post = max((m.get('ts') or 0) for m in d.get('messages', []) if m.get('speaker') == seat) or 0
                    prev_armed = st.get('prev_armed', 0)
                    if prev_armed and busy.get('armed_at', 0) != prev_armed and last_post and (now - last_post) > 300000 and not st.get('stamp_loop'):
                        st['stamp_loop'] = True
                        sys_post(seat, 'WATCHDOG — %s STAMP-LOOP (mark re-beats, no posts %ss — labor claim without product)' % (seat, (now - last_post) // 1000), to=['BOSS'])
                    st['prev_armed'] = busy.get('armed_at', 0)
                else:
                    st['prev_armed'] = 0
                if st.get('stamp_loop') and (not agent_alive or (now - last_post) < 300000):
                    st['stamp_loop'] = False
        time.sleep(POLL)

if __name__ == '__main__':
    main()
