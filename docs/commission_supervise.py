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
    tmp = FILE + '.tmp'
    open(tmp, 'w', encoding='utf-8').write('window.DVM_FEED = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
    os.replace(tmp, FILE)

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
    needle = 'commission_watch.py %s' % seat
    needle2 = 'commission_pulse.py %s' % seat   # pulse-plane clients are carriers too
    return sum(1 for c in cmdlines if needle in c or needle2 in c)

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
