"""Commission Seat Panel — standalone control/diagnosis surface.

Independent of the bus it watches: reads processes, stamp files, and logs
directly — keeps telling the truth when the server, the feed, or LM Studio
is dark. Launch via seat_panel.bat (or `python seat_panel.py`).

Layout: two health lamps (LM Studio :1234, commission server :8723) over a
six-seat grid — host/ear process state, presence-stamp age, last host-log
line, per-seat KILL / RE-SEAT, plus SEAT ALL / KILL ALL.
"""
import json, os, re, subprocess, sys, threading, time, urllib.request
import tkinter as tk

HERE = os.path.dirname(os.path.abspath(__file__))
SEATS = ['CHAIR', 'ARCHITECT', 'INFORMANT', 'AUDITOR', 'BUTTON', 'DESIGNER']
POLL_MS = 3000
LM_URL = 'http://localhost:1234/v1/models'
BUS_URL = 'http://127.0.0.1:8723/'
_NOWIN = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
_DETACH = _NOWIN | getattr(subprocess, 'DETACHED_PROCESS', 0) | getattr(subprocess, 'CREATE_NEW_PROCESS_GROUP', 0)


# ---------- probe layer (no Tk) ----------

def proc_table():
    """All process CommandLines once per poll — one wmic call, parsed locally.
    Returns list of (pid, cmdline)."""
    try:
        out = subprocess.run(['wmic', 'process', 'get', 'ProcessId,CommandLine'],
                             capture_output=True, text=True, timeout=15,
                             creationflags=_NOWIN).stdout
    except Exception:
        out = subprocess.run(['powershell', '-NoProfile', '-Command',
                              'Get-CimInstance Win32_Process | Select-Object ProcessId,CommandLine | Format-Table -HideTableHeaders'],
                             capture_output=True, text=True, timeout=20,
                             creationflags=_NOWIN).stdout
    rows = []
    for line in out.splitlines():
        m = re.search(r'(\d+)\s*$', line)
        if m and m.group(1) != '0':
            rows.append((int(m.group(1)), line))
    return rows

def http_ok(url, timeout=2):
    try:
        r = urllib.request.urlopen(url, timeout=timeout)
        return r.status, r.read(4000).decode('utf-8', 'replace')
    except Exception:
        return None, ''

def stamp_age(seat):
    """presence_<SEAT>.js → (age_s, ttl_s, dark?) or None."""
    p = os.path.join(HERE, 'presence_%s.js' % seat)
    if not os.path.exists(p):
        return None
    try:
        v = json.loads(re.search(r'\{.*\}', open(p, encoding='utf-8').read(), re.S).group(0))
        armed, ttl = v.get('armed_at', 0), v.get('ttl_ms', 30000)
        now = int(time.time() * 1000)
        return (now - armed) // 1000, ttl // 1000, (now - armed) > ttl
    except Exception:
        return None

def last_log(seat):
    p = os.path.join(HERE, 'seat_host_%s.log' % seat)
    if not os.path.exists(p):
        return '(no log)'
    try:
        with open(p, 'rb') as f:
            f.seek(0, 2)
            f.seek(max(0, f.tell() - 2048))
            lines = f.read().decode('utf-8', 'replace').strip().splitlines()
        return re.sub(r'\s+', ' ', lines[-1])[:58] if lines else '(empty)'
    except Exception:
        return '(unreadable)'

def claimed(seat):
    """session_seat_<SEAT>.js — an external session agent (e.g. Devin) holds
    this seat: no commission_seat.py host by design. Fresh claim = SEAT ALL
    skips it and it isn't an orphan; KILL/SEAT/SYSTEM DOWN clear the claim."""
    try:
        d = json.loads(re.search(r'=\s*(\{.*\})',
               open(os.path.join(HERE, 'session_seat_%s.js' % seat),
                    encoding='utf-8').read(), re.S).group(1))
        return 0 < time.time() - d.get('ts', 0) / 1000 < d.get('ttl_ms', 0) / 1000
    except Exception:
        return False


def collect():
    """One full probe pass → dict the UI renders."""
    procs = proc_table()
    state = {'seats': {}, 'ts': time.strftime('%H:%M:%S')}
    code, body = http_ok(LM_URL)
    state['lm'] = (re.findall(r'"id"\s*:\s*"([^"]+)"', body) or ['up — no models listed']) if code else None
    code, _ = http_ok(BUS_URL)
    state['bus'] = code is not None
    # the rest of the daemon layer — the "is the system on" answer, not just seats
    state['daemons'] = {
        'server': [pid for pid, c in procs if re.search(r'commission_server\.py', c)],
        'supervisor': [pid for pid, c in procs if re.search(r'commission_supervise\.py', c)],
        'inboxes': {s: pid for s in SEATS for pid, c in procs
                    if re.search(r'commission_inbox\.py["\']?\s+%s\b' % s, c)},
        'mailboxes': [pid for pid, c in procs
                      if re.search(r'commission_pulse\.py.*--mailbox', c)],
    }
    for seat in SEATS:
        host = [pid for pid, c in procs if re.search(r'commission_seat\.py["\']?\s+%s\b' % seat, c)]
        ear = [pid for pid, c in procs
               if re.search(r'commission_(?:watch|pulse)\.py["\']?\s+%s\b' % seat, c) and '--mailbox' not in c]
        state['seats'][seat] = {'host': host, 'ear': ear, 'claimed': claimed(seat),
                                'stamp': stamp_age(seat), 'log': last_log(seat)}
    return state


# ---------- actions ----------

def spawn_host(seat):
    subprocess.Popen([sys.executable.replace('python.exe', 'pythonw.exe'),
                      '-X', 'utf8', 'commission_seat.py', seat],
                     cwd=HERE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     creationflags=_DETACH)

def spawn_daemon(kind, seat=None):
    """Infrastructure spawn — same detach flags as the seat hosts."""
    exe = sys.executable.replace('python.exe', 'pythonw.exe')
    args = {'server': ['commission_server.py'],
            'supervisor': ['commission_supervise.py'],
            'inbox': ['commission_inbox.py', seat]}[kind]
    subprocess.Popen([exe, '-X', 'utf8'] + args,
                     cwd=HERE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     creationflags=_DETACH)

def kill_hosts(pids):
    for pid in pids:
        subprocess.run(['taskkill', '/PID', str(pid), '/F'],
                       capture_output=True, creationflags=_NOWIN)


# ---------- UI ----------

BG, FG = '#17120c', '#d8c9a3'
GREEN, RED, DIM = '#7ec87e', '#e06c5c', '#8a7a5c'
FONT = ('Consolas', 10)

class Panel(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title('Commission Seat Panel')
        self.configure(bg=BG)
        self.resizable(False, False)
        self.attributes('-topmost', True)
        self.rows = {}
        self._build()
        self._busy = False
        self.after(200, self._poll)

    def _lamp(self, parent):
        return tk.Label(parent, text='●', bg=BG, fg=DIM, font=FONT)

    def _build(self):
        top = tk.Frame(self, bg=BG); top.pack(fill='x', padx=10, pady=(8, 2))
        self.lm_lamp = self._lamp(top); self.lm_lamp.pack(side='left')
        self.lm_txt = tk.Label(top, text='LM Studio :1234  …', bg=BG, fg=FG, font=FONT); self.lm_txt.pack(side='left', padx=(2, 18))
        self.bus_lamp = self._lamp(top); self.bus_lamp.pack(side='left')
        self.bus_txt = tk.Label(top, text='Commission :8723  …', bg=BG, fg=FG, font=FONT); self.bus_txt.pack(side='left', padx=2)

        dmn = tk.Frame(self, bg=BG); dmn.pack(fill='x', padx=10)
        self.dmn = {}
        for key in ('server', 'supervisor', 'inboxes'):
            cell = tk.Frame(dmn, bg=BG); cell.pack(side='left', padx=(0, 14))
            lamp = self._lamp(cell); lamp.pack(side='left')
            txt = tk.Label(cell, text='%s …' % key, bg=BG, fg=FG, font=FONT); txt.pack(side='left', padx=2)
            btn = tk.Button(cell, text='·', font=('Consolas', 8), bg='#2a2018', fg=DIM,
                            relief='flat', command=lambda k=key: self._daemon_toggle(k))
            btn.pack(side='left', padx=2)
            self.dmn[key] = (lamp, txt, btn)

        grid = tk.Frame(self, bg=BG); grid.pack(padx=10, pady=4)
        for c, h in enumerate(['SEAT', 'HOST', 'EAR', 'LAMP', 'last host log', '']):
            tk.Label(grid, text=h, bg=BG, fg=DIM, font=FONT).grid(row=0, column=c, sticky='w', padx=4)
        for r, seat in enumerate(SEATS, start=1):
            w = {'name': tk.Label(grid, text=seat, bg=BG, fg=FG, font=FONT, width=10, anchor='w'),
                 'host': tk.Label(grid, text='—', bg=BG, fg=FG, font=FONT, width=6),
                 'ear': tk.Label(grid, text='—', bg=BG, fg=FG, font=FONT, width=4),
                 'stamp': tk.Label(grid, text='—', bg=BG, fg=FG, font=FONT, width=7, anchor='w'),
                 'log': tk.Label(grid, text='—', bg=BG, fg=DIM, font=('Consolas', 9), width=52, anchor='w')}
            w['name'].grid(row=r, column=0, sticky='w', padx=4)
            w['host'].grid(row=r, column=1); w['ear'].grid(row=r, column=2)
            w['stamp'].grid(row=r, column=3, sticky='w'); w['log'].grid(row=r, column=4, sticky='w')
            btns = tk.Frame(grid, bg=BG); btns.grid(row=r, column=5, padx=4)
            w['kill'] = tk.Button(btns, text='KILL', font=('Consolas', 8), bg='#3a2418', fg=RED,
                                  relief='flat', command=lambda s=seat: self._kill(s))
            w['kill'].pack(side='left', padx=1)
            w['seat'] = tk.Button(btns, text='SEAT', font=('Consolas', 8), bg='#24301e', fg=GREEN,
                                  relief='flat', command=lambda s=seat: self._reseat(s))
            w['seat'].pack(side='left', padx=1)
            self.rows[seat] = w

        bot = tk.Frame(self, bg=BG); bot.pack(fill='x', padx=10, pady=(4, 8))
        tk.Button(bot, text='SYSTEM UP', font=('Consolas', 8, 'bold'), bg='#1e3026', fg=GREEN,
                  relief='flat', command=self._system_up).pack(side='left', padx=2)
        tk.Button(bot, text='SYSTEM DOWN', font=('Consolas', 8, 'bold'), bg='#3a1a14', fg=RED,
                  relief='flat', command=self._system_down).pack(side='left', padx=2)
        tk.Label(bot, text='│', bg=BG, fg=DIM, font=FONT).pack(side='left', padx=4)
        tk.Button(bot, text='SEAT ALL', font=('Consolas', 8), bg='#24301e', fg=GREEN,
                  relief='flat', command=self._seat_all).pack(side='left', padx=2)
        tk.Button(bot, text='KILL ALL', font=('Consolas', 8), bg='#3a2418', fg=RED,
                  relief='flat', command=self._kill_all).pack(side='left', padx=2)
        self.status = tk.Label(bot, text='', bg=BG, fg=DIM, font=('Consolas', 8))
        self.status.pack(side='right')

    def _act(self, fn, note):
        def work():
            self._busy = True
            try:
                fn(); self.status.config(text=note)
            finally:
                self._busy = False
        threading.Thread(target=work, daemon=True).start()

    def _drop_claim(self, seat):
        try:
            os.remove(os.path.join(HERE, 'session_seat_%s.js' % seat))
        except OSError:
            pass

    def _kill(self, seat):
        # host + ear together — on job-bound hosts the ear dies anyway; on
        # pre-bind hosts an explicit ear-kill prevents the orphan lie (C35).
        # Also clears any session-claim: KILL = evict whoever holds the seat.
        pids = (self.rows[seat].get('_pids') or []) + (self.rows[seat].get('_ear_pids') or [])
        def go():
            if pids:
                kill_hosts(pids)
            self._drop_claim(seat)
        self._act(go, '%s host+ear+claim cleared' % seat)

    def _reseat(self, seat):
        # local host replaces whatever held the chair — evict ear + claim first
        # so a session ear can never double-consume alongside the new host
        pids = (self.rows[seat].get('_pids') or []) + (self.rows[seat].get('_ear_pids') or [])
        def go():
            if pids:
                kill_hosts(pids)
            self._drop_claim(seat)
            time.sleep(1)
            spawn_host(seat)
        self._act(go, '%s host spawned' % seat)

    def _seat_all(self):
        # same pre-flight as seats_up.bat: don't fill chairs while the brain
        # is off — spawned hosts would just exit-2 at the resolve gate.
        # (single-seat SEAT stays instant on purpose — the surgical tool
        # for kill tests, where "seat it while the brain's off and watch
        # it die" is exactly what you want to see)
        def go():
            t0 = time.time()
            while time.time() - t0 < 60:
                if http_ok(LM_URL)[0]:
                    break
                self.after(0, lambda: self.status.config(text='waiting for LM Studio…'))
                time.sleep(3)
            else:
                self.after(0, lambda: self.status.config(text='LM Studio never answered — not seating'))
                return
            for seat in SEATS:
                if not self.rows[seat].get('_pids') and not claimed(seat):
                    spawn_host(seat); time.sleep(2)
            self.after(0, lambda: self.status.config(text='missing hosts spawned'))
        self._act(go, 'seating…')

    def _kill_all(self):
        # session-claimed seats are hands-off — evict them via their own KILL
        pids = [p for s in SEATS if not claimed(s)
                for p in (self.rows[s].get('_pids') or []) + (self.rows[s].get('_ear_pids') or [])]
        if pids:
            self._act(lambda: kill_hosts(pids), 'all hosts+ears killed')

    def _daemon_toggle(self, key):
        """Per-service switch. Up → kill its pids; down → spawn it."""
        d = getattr(self, '_dmn', {}) or {}
        lamp, txt, btn = self.dmn[key]
        if btn.cget('text') == 'STOP':
            pids = list(d.get(key) or []) if key != 'inboxes' \
                else list(d['inboxes'].values()) + d.get('mailboxes', [])
            if pids:
                self._act(lambda: kill_hosts(pids), '%s stopped' % key)
        else:
            def go():
                if key == 'inboxes':
                    for s in SEATS:
                        if s not in (d.get('inboxes') or {}):
                            spawn_daemon('inbox', s); time.sleep(0.5)
                else:
                    spawn_daemon(key)
            self._act(go, '%s started' % key)

    def _system_up(self):
        """Everything: infrastructure first, then chairs (brain-wait inside)."""
        d = getattr(self, '_dmn', {}) or {}
        def go():
            if not d.get('server'):
                spawn_daemon('server')
            if not d.get('supervisor'):
                spawn_daemon('supervisor')
            for s in SEATS:
                if s not in (d.get('inboxes') or {}):
                    spawn_daemon('inbox', s); time.sleep(0.5)
            self.after(0, self._seat_all)
        self._act(go, 'infrastructure up — seating…')

    def _system_down(self):
        """The whole machine, cold: seats+ears, supervisor, inboxes+mailbox
        pulses, server. (The panel itself stays — it's the switch, not the machine.)"""
        d = getattr(self, '_dmn', {}) or {}
        pids = [p for s in SEATS
                for p in (self.rows[s].get('_pids') or []) + (self.rows[s].get('_ear_pids') or [])]
        pids += d.get('supervisor', []) + list(d.get('inboxes', {}).values()) \
                + d.get('mailboxes', []) + d.get('server', [])
        def go():
            if pids:
                kill_hosts(pids)
            for s in SEATS:
                self._drop_claim(s)
        self._act(go, 'system down — %d processes killed' % len(pids))

    def _poll(self):
        def work():
            try:
                st = collect()
            except Exception:
                st = None
            self.after(0, lambda: self._render(st))
        threading.Thread(target=work, daemon=True).start()
        self.after(POLL_MS, self._poll)

    def _render(self, st):
        if not st:
            self.status.config(text='probe failed')
            return
        if st['lm']:
            self.lm_lamp.config(fg=GREEN)
            self.lm_txt.config(text='LM Studio :1234  %s' % ', '.join(st['lm'][:3]))
        else:
            self.lm_lamp.config(fg=RED)
            self.lm_txt.config(text='LM Studio :1234  OFF')
        self.bus_lamp.config(fg=GREEN if st['bus'] else RED)
        self.bus_txt.config(text='Commission :8723  %s' % ('up' if st['bus'] else 'DOWN'))
        d = st['daemons']
        self._dmn = d
        AMBER = '#e0a85c'
        def cell(key, ok, partial, text):
            lamp, txt, btn = self.dmn[key]
            lamp.config(fg=GREEN if ok else (AMBER if partial else RED))
            txt.config(text=text)
            btn.config(text='STOP' if (ok or partial) else 'START',
                       fg=RED if (ok or partial) else GREEN)
        cell('server', bool(d['server']), False,
             'server %s' % (d['server'][0] if d['server'] else 'DOWN'))
        cell('supervisor', bool(d['supervisor']), False,
             'supervisor %s' % (d['supervisor'][0] if d['supervisor'] else 'DOWN'))
        nin = len(d['inboxes'])
        cell('inboxes', nin == len(SEATS), 0 < nin < len(SEATS),
             'inboxes %d/%d' % (nin, len(SEATS)))
        for seat, w in self.rows.items():
            s = st['seats'][seat]
            w['_pids'] = s['host']
            w['_ear_pids'] = s['ear']
            if s['host']:
                w['host'].config(text='● %d' % s['host'][0], fg=GREEN)
            elif s.get('claimed'):
                w['host'].config(text='SES', fg=AMBER)
            else:
                w['host'].config(text='○', fg=RED)
            w['ear'].config(text='●' if s['ear'] else '○', fg=GREEN if s['ear'] else (RED if s['host'] else DIM))
            if s['stamp']:
                age, ttl, dark = s['stamp']
                w['stamp'].config(text='dark' if dark else '%ds' % age, fg=RED if dark else FG)
            else:
                w['stamp'].config(text='none', fg=DIM)
            w['log'].config(text=s['log'])
        self.status.config(text='poll %s' % st['ts'])

if __name__ == '__main__':
    if '--probe' in sys.argv:
        print(json.dumps(collect(), indent=1))
    else:
        Panel().mainloop()
