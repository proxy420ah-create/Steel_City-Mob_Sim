#!/usr/bin/env python3
"""commission_seat.py — local-model seat host (LM Studio substrate).

Replaces the Devin-session host for one commission seat. The bus is
unchanged: pulse subprocess = ear + presence lamp (server-stamped),
inbox daemon = delivery, deliberation_feed.js = truth, POST /post =
the only write path. What changes is who composes the speech: a local
model behind an OpenAI-compatible endpoint, woken per-batch instead of
holding a 100K-token session.

Usage:  python commission_seat.py <SEAT> [--dry] [--once] [--base URL] [--model ID] [--tail N]

Memory model: the model is stateless per wake. Continuity lives in
seat_state_<SEAT>.md — the model returns a `memory` object each turn
and this host rewrites the file. Debts (needs_ack aimed at the seat)
are scanned MECHANICALLY from the feed each wake and injected — the
model never owns its own ledger.

Gate (--dry): prints prompt + raw response + parsed action; nothing is
posted, stamped, or written. First-contact mode: run --dry --once and
show the boss before the seat ever speaks.
"""
import json, os, re, subprocess, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
FILE = os.path.join(HERE, 'deliberation_feed.js')
BUSY = os.path.join(HERE, 'commission_busy.py')
PULSE = os.path.join(HERE, 'commission_pulse.py')
LM_BASE_DEFAULT = 'http://localhost:1234'

SEAT_KINDS = {'position', 'objection', 'support', 'veto', 'verdict', 'phase', 'ack', 'ok', 'aside'}
REGISTER_KEYS = {'turn', 'turn_at', 'phase', 'adjourned', 'proposal', 'verdict', 'board'}
TURN_HOLDER = re.compile(r'^[A-Z0-9_]+$')

RESPONSE_SCHEMA = {
    'type': 'json_schema',
    'json_schema': {
        'name': 'seat_action',
        'strict': False,
        'schema': {
            'type': 'object',
            'properties': {
                'action': {'type': 'string', 'enum': ['post', 'hold'],
                           'description': 'post = speak to the bus now; hold = read-only, nothing owed'},
                'kind': {'type': 'string'},
                'to': {'type': 'array', 'items': {'type': 'string'}},
                'text': {'type': 'string'},
                'register': {'type': 'object',
                             'description': 'scalar register moves e.g. {"turn":"ARCHITECT"} — omit if nothing moves'},
                'memory': {'type': 'object',
                           'description': 'stance/debts/notes for the next wake — your only continuity'},
                'reason': {'type': 'string', 'description': 'one line: why this action'}
            },
            'required': ['action', 'reason'],
            'additionalProperties': False
        }
    }
}


def log(seat, *a, **_kw):
    """pythonw swallows stdout — every host line also lands in
    seat_host_<SEAT>.log so detached deaths stay diagnosable."""
    line = ' '.join(str(x) for x in a)
    print(line, flush=True)
    try:
        with open(os.path.join(HERE, 'seat_host_%s.log' % seat), 'a', encoding='utf-8') as f:
            f.write('%s %s\n' % (time.strftime('%H:%M:%S'), line))
    except OSError:
        pass


def load_feed():
    try:
        raw = open(FILE, encoding='utf-8').read().strip()
        m = re.search(r'^window\.DVM_FEED\s*=\s*(\{.*\})\s*;?\s*$', raw, re.S | re.M)
        return json.loads(m.group(1)) if m else None
    except Exception:
        return None


def read_jsvar(path):
    try:
        return json.loads(re.search(r'\{.*\}', open(path, encoding='utf-8').read(), re.S).group(0))
    except Exception:
        return None


def seat_state_path(seat):
    return os.path.join(HERE, 'seat_state_%s.md' % seat)


def seat_diary_path(seat):
    return os.path.join(HERE, 'seat_diary_%s.md' % seat)


def read_state(seat):
    try:
        return open(seat_state_path(seat), encoding='utf-8').read()
    except OSError:
        return '(no prior state — fresh induction)'


# ── Rolling diary ────────────────────────────────────────────────────────────
# The seat's long memory: model writes 1-3 first-person entries per wake in its
# own voice; daemon appends verbatim to seat_diary_<SEAT>.md (unbounded archive,
# fetchable via NEED:). Only the last DIARY_KEEP entries ride in the injected
# state file — hot window bounded, nothing ever lost.
DIARY_KEEP = 30
DIARY_ENTRY_MAX = 400
DIARY_MAX_NEW = 3

def diary_append(seat, memory):
    """Append memory.record entries to the archive; return the hot tail."""
    rec = memory.get('record')
    if isinstance(rec, str):
        rec = [rec]
    entries = [str(e).strip()[:DIARY_ENTRY_MAX] for e in (rec or []) if str(e).strip()]
    entries = entries[:DIARY_MAX_NEW]
    stamp = time.strftime('%Y-%m-%d %H:%M')
    if entries:
        with open(seat_diary_path(seat), 'a', encoding='utf-8') as f:
            f.write('### %s\n%s\n' % (stamp,
                    ''.join('- %s\n' % e for e in entries)))
        log(seat, '[seat] diary +%d' % len(entries), flush=True)
    try:
        lines = [l.rstrip('\n') for l in open(seat_diary_path(seat), encoding='utf-8')
                 if l.startswith('- ')]
    except OSError:
        lines = []
    return lines[-DIARY_KEEP:]


def write_state(seat, memory, reason, record):
    body = ('# seat_state_%s — rewritten by commission_seat.py each wake (%s)\n\n'
            '## stance\n%s\n\n## debts\n%s\n\n## notes\n%s\n\n## record (last %d diary entries — full archive: seat_diary_%s.md)\n%s\n'
            % (seat, time.strftime('%Y-%m-%d %H:%M'),
               memory.get('stance', '(unset)'),
               '\n'.join('- ' + str(d) for d in (memory.get('debts') or [])) or '(none)',
               memory.get('notes', '') + ('\n\n_last action reason: %s_' % reason if reason else ''),
               DIARY_KEEP, seat,
               '\n'.join(record) or '(no entries yet — this is your first memory)'))
    p = seat_state_path(seat)
    open(p + '.tmp', 'w', encoding='utf-8').write(body)
    os.replace(p + '.tmp', p)


def ledger_tail(lines=25):
    try:
        return ''.join(open(os.path.join(HERE, 'COMMISSION_LEDGER.md'),
                            encoding='utf-8').readlines()[-lines:])
    except OSError:
        return '(ledger unreadable)'


# ── NEED: fetch plane ────────────────────────────────────────────────────────
# The model cannot read files; it asks via "NEED: <path>[#heading]" in
# memory.notes. Queue persists in seat_fetch_<SEAT>.json so a need asked this
# wake is answered next wake — one turn of latency, zero model-side plumbing.
FETCH_ROOTS = [os.path.realpath(HERE),
               os.path.realpath(os.path.join(HERE, '..', '.devin', 'rules'))]
FETCH_EXT = {'.md', '.txt', '.py', '.js', '.json', '.html'}
FETCH_MAX_CHARS = 6000
FETCH_MAX_PER_WAKE = 2

def fetch_queue_path(seat):
    return os.path.join(HERE, 'seat_fetch_%s.json' % seat)

def fetch_queue(seat):
    try:
        return json.load(open(fetch_queue_path(seat), encoding='utf-8'))
    except Exception:
        return []

def save_fetch_queue(seat, q):
    tmp = fetch_queue_path(seat) + '.tmp'
    open(tmp, 'w', encoding='utf-8').write(json.dumps(q, indent=1))
    os.replace(tmp, fetch_queue_path(seat))

def collect_needs(seat, memory):
    """Scan memory for NEED: <path>[#heading] lines; queue any not seen before."""
    if not isinstance(memory, dict):
        return
    q = fetch_queue(seat)
    seen = {e.get('need') for e in q}
    text = '\n'.join(str(memory.get(k) or '') for k in ('notes', 'stance', 'debts'))
    # model sometimes wraps the request in stray quotes/brackets ("...law']"),
    # sentence-ending punctuation (path.), or html residue (</code>) — clean
    # all of it so the extension check sees the bare path (C44)
    def _clean_need(s):
        s = re.sub(r'<[^\n]*$', '', s.strip())           # cut trailing markup
        return re.sub(r'[\'"\]\)\.,;:!?]+$', '', s)      # strip sentence punct
    new = [{'need': _clean_need(m.group(1)),
            'at': int(time.time() * 1000), 'status': 'queued'}
           for m in (re.search(r'NEED:\s*([^\s<>"\'\]\)]+)', l) for l in text.splitlines())
           if m and _clean_need(m.group(1)) not in seen]
    if new:
        save_fetch_queue(seat, q + new)
        for e in new:
            log(seat, '[seat] NEED queued: %s' % e['need'], flush=True)


def fetch_pending(seat):
    """Any 'queued' entries waiting to be served = the seat asked for docs but
    the action produced no bus event — nothing will wake it to receive them,
    so the daemon owes it a follow-up wake (retrieval deadlock otherwise)."""
    return any(e.get('status') == 'queued' for e in fetch_queue(seat))

def _slice_section(txt, sect):
    lines = txt.splitlines(keepends=True)
    # markdown headings first, then CAPS-HEADERS: (.txt house style)
    for i, l in enumerate(lines):
        m = re.match(r'^(#{1,6})\s+(.*)', l)
        if m and sect.lower() in m.group(2).lower():
            depth, out = len(m.group(1)), [l]
            for l2 in lines[i + 1:]:
                m2 = re.match(r'^(#{1,6})\s', l2)
                if m2 and len(m2.group(1)) <= depth:
                    break
                out.append(l2)
                if sum(len(x) for x in out) > FETCH_MAX_CHARS:
                    break
            return ''.join(out)
    caps = re.compile(r'^[A-Z][A-Z0-9 &/_\-—()]+\s*:\s*$')
    for i, l in enumerate(lines):
        if caps.match(l.rstrip('\n')) and sect.lower() in l.lower():
            out = [l]
            for l2 in lines[i + 1:]:
                if caps.match(l2.rstrip('\n')):
                    break
                out.append(l2)
                if sum(len(x) for x in out) > FETCH_MAX_CHARS:
                    break
            return ''.join(out)
    return None

def fetch_doc(need):
    path, _, sect = need.partition('#')
    path = path.strip().lstrip('/\\')
    if path.startswith('.devin/'):
        p = os.path.normpath(os.path.join(HERE, '..', path))
    else:
        if path.startswith('docs/'):
            path = path[5:]
        p = os.path.normpath(os.path.join(HERE, path))
    if os.path.splitext(p)[1].lower() not in FETCH_EXT:
        return '(fetch denied: extension not whitelisted)'
    rp = os.path.realpath(p)
    if not os.path.isfile(p) or not any(rp.startswith(r) for r in FETCH_ROOTS):
        return '(fetch denied: missing or outside allowed roots)'
    try:
        txt = open(p, encoding='utf-8', errors='replace').read()
    except Exception as e:
        return '(fetch failed: %s)' % e
    if sect:
        sliced = _slice_section(txt, sect)
        if sliced is None:
            return ('(fetch denied: section %r not found — re-issue NEED with an '
                    'exact heading substring, or drop # for the doc head)' % sect)
        txt = sliced
    return txt[:FETCH_MAX_CHARS] + ('\n... [truncated]' if len(txt) > FETCH_MAX_CHARS else '')

def serve_fetches(seat):
    q = fetch_queue(seat)
    out = []
    for e in q:
        if e.get('status') != 'queued' or len(out) >= FETCH_MAX_PER_WAKE:
            continue
        e['status'] = 'served'
        out.append('### NEED: %s\n%s' % (e['need'], fetch_doc(e['need'])))
    if out:
        save_fetch_queue(seat, q)
    return '\n\n'.join(out)


def pulse_n(seat):
    v = read_jsvar(os.path.join(HERE, 'pulse_n_%s.js' % seat))
    return (v or {}).get('n', 0)


def _bind_kill_on_close(proc):
    """Windows Job Object with KILL_ON_JOB_CLOSE: if the host dies, the ear
    is reaped with it — an orphaned socket can never keep presence green
    on a deaf seat again (C35). Failure degrades to an unbound ear (old
    behavior), logged, never fatal."""
    try:
        import ctypes
        from ctypes import wintypes
        k32 = ctypes.windll.kernel32
        job = k32.CreateJobObjectW(None, None)
        if not job:
            return None

        class _BasicLimit(ctypes.Structure):
            _fields_ = [('PerProcessUserTimeLimit', wintypes.LARGE_INTEGER),
                        ('PerJobUserTimeLimit', wintypes.LARGE_INTEGER),
                        ('LimitFlags', wintypes.DWORD),
                        ('MinimumWorkingSetSize', ctypes.c_size_t),
                        ('MaximumWorkingSetSize', ctypes.c_size_t),
                        ('ActiveProcessLimit', wintypes.DWORD),
                        ('Affinity', ctypes.c_size_t),
                        ('PriorityClass', wintypes.DWORD),
                        ('SchedulingClass', wintypes.DWORD)]

        class _IOCounters(ctypes.Structure):
            _fields_ = [(k, wintypes.ULARGE_INTEGER) for k in
                        ('ReadOperationCount', 'WriteOperationCount',
                         'OtherOperationCount', 'ReadTransferCount',
                         'WriteTransferCount', 'OtherTransferCount')]

        class _ExtLimit(ctypes.Structure):
            _fields_ = [('BasicLimitInformation', _BasicLimit),
                        ('IoInfo', _IOCounters),
                        ('ProcessMemoryLimit', ctypes.c_size_t),
                        ('JobMemoryLimit', ctypes.c_size_t),
                        ('PeakProcessMemoryUsed', ctypes.c_size_t),
                        ('PeakJobMemoryUsed', ctypes.c_size_t)]

        info = _ExtLimit()
        info.BasicLimitInformation.LimitFlags = 0x2000   # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (not k32.SetInformationJobObject(job, 9, ctypes.byref(info),
                                           ctypes.sizeof(info))
                or not k32.AssignProcessToJobObject(job, proc._handle)):
            k32.CloseHandle(job)
            return None
        return job   # caller MUST keep this referenced — GC'd handle = closed job
    except Exception:
        return None


def arm_ear(seat):
    """The ear = one held pulse socket; its exit is the doorbell. Sole carrier
    check: an already-held socket stamps presence, never double-arm. The ear
    is job-bound to this host — host death reaps it (C35)."""
    n = pulse_n(seat)
    p = subprocess.Popen(
        [sys.executable, '-X', 'utf8', PULSE, seat, '--since', str(n)],
        cwd=HERE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        text=True, encoding='utf-8', errors='replace')
    p._job = _bind_kill_on_close(p)
    if p._job is None:
        log(seat, '[seat] job-bind failed — ear unbound (orphan risk, C35 degraded)')
    return p


def drain_inbox(seat, cursor):
    """Inbox daemon archives every delivered batch verbatim; we read lines
    past our own cursor so ambient traffic lands without paying a wake."""
    p = os.path.join(HERE, 'inbox_%s.jsonl' % seat)
    out = []
    try:
        with open(p, encoding='utf-8') as f:
            for i, line in enumerate(f):
                if i >= cursor:
                    out.append(line.rstrip('\n'))
    except OSError:
        pass
    return out, cursor + len(out)


def debts_for(seat, msgs):
    """Mechanical debt ledger: un-acked needs_ack aimed at this seat, newest 40."""
    debts = []
    acked = {m.get('ack_of') for m in msgs if m.get('ack_of')}
    for m in msgs[-400:]:
        to = m.get('to') or []
        if (m.get('needs_ack') and seat in to and m.get('id') not in acked
                and m.get('speaker') != seat):
            debts.append({'id': m.get('id'), 'from': m.get('speaker'),
                          'text': (m.get('text') or '')[:160]})
    return debts[-8:]


def build_prompt(seat, feed, inbox_lines, state, small=False):
    msgs = feed.get('messages', [])
    # small=True = context-shed mode (HTTP 400 retry): the seat still wakes on
    # a borderline window instead of wedging on its own backlog (C41)
    tail = msgs[-8:] if small else msgs[-30:]
    register = {k: feed.get(k) for k in ('turn', 'turn_at', 'phase', 'proposal',
                                         'verdict', 'adjourned', 'seats')}
    induction = None
    for cand in ('induction_%s.local.txt', 'induction_%s.txt'):
        try:
            induction = open(os.path.join(HERE, cand % seat), encoding='utf-8').read()
            break
        except OSError:
            continue
    if induction is None:
        induction = '(no induction_%s.txt on disk — act on protocol alone)' % seat

    system = (induction +
              '\n\n=== LOCAL-HOST RUNTIME CONTRACT ===\n'
              'You run on a stateless local model. You have NO tools and no memory — '
              'the daemon feeds you the world each wake and carries out your action.\n'
              'Reply with ONE JSON object only:\n'
              '  {"action":"post"|"hold", "kind":..., "to":[...], "text":"...",\n'
              '   "register":{...}, "memory":{...}, "reason":"..."}\n'
              '- action:"post" only when the register/lane law entitles you to speak.\n'
              '- kind from: position|objection|support|veto|verdict|phase|ack|ok|aside.\n'
              '- text may carry inline <b>/<i>/<code> html; lead with the operative line.\n'
              '- register only for scalar moves you are owed (e.g. {"turn":"NEXT_SEAT"} '
              'when you pass the floor — naming a speaker MUST stamp turn).\n'
              '- memory: {stance, debts[], notes, record[]} — stance/debts/notes are '
              'rewritten each wake (keep ~120 words); record[] entries are APPENDED '
              'to your rolling diary verbatim — your long memory across wakes. 1-3 '
              'short first-person entries: what happened to you + your read of it.\n'
              '- If nothing actionable: {"action":"hold","reason":"..."} — silence is legal.\n'
              '- LANE FIDELITY: answer in the lane you were addressed. A kind:"dm"\n'
              '   message is the OFFICE channel — reply kind:"dm" (CHAIR only; never\n'
              '   carries register), never answer a dm on the main table.\n')

    user = ('=== REGISTER ===\n' + json.dumps(register, indent=1) +
            '\n\n=== MECHANICAL DEBT LEDGER (needs_ack aimed at you, un-acked) ===\n' +
            json.dumps(debts_for(seat, msgs), indent=1) +
            '\n\n=== YOUR STATE FILE ===\n' + state +
            '\n\n=== LEDGER TAIL (COMMISSION_LEDGER.md, closed work) ===\n' + ledger_tail() +
            '\n=== FEED TAIL (last %d of %d messages, verbatim) ===\n' % (len(tail), len(msgs)))
    for m in tail:
        user += ('\n[%s] %s -> %s: %s' % (m.get('id'), m.get('speaker', 'sys'),
                                         m.get('to'), (m.get('text') or '')[:400 if small else 2000]))
    if inbox_lines:
        user += ('\n\n=== INBOX (daemon-archived batches since last drain, verbatim pulse stdout) ===\n'
                 + '\n'.join(inbox_lines[-2 if small else -6:])[:1500 if small else 4000])
    return system, user


def append_fetches(seat, user):
    served = serve_fetches(seat)
    if served:
        user += '\n\n=== FETCHED DOCS (served against your NEED: requests) ===\n' + served
    return user


def lm_call(base, model, system, user, dry=False):
    body = {
        'model': model,
        'messages': [{'role': 'system', 'content': system},
                     {'role': 'user', 'content': user}],
        'temperature': 0.5,
        'max_tokens': 2048,
        'response_format': RESPONSE_SCHEMA,
        'reasoning_effort': 'none',   # this build's thinking switch — ctk enable_thinking is ignored
        'chat_template_kwargs': {'enable_thinking': False},
    }
    req = urllib.request.Request(
        base.rstrip('/') + '/v1/chat/completions',
        data=json.dumps(body).encode(), headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=300) as r:
        d = json.loads(r.read())
    return d['choices'][0]['message']['content']


def resolve_model(base):
    try:
        with urllib.request.urlopen(base.rstrip('/') + '/v1/models', timeout=10) as r:
            d = json.loads(r.read())
        ids = [m.get('id') for m in d.get('data', []) if m.get('id')]
        return ids[0] if ids else None
    except Exception:
        return None


_LM_DEAD = {'n': 0}   # consecutive confirmed brain-death strikes (C40)

def brain_gone(base, seat):
    """Brain-death vs call-failure: a refused/timed-out lm_call while
    /v1/models still answers is transient (slow gen, model hiccup) — ride
    it. Only a failed probe proves the mind is gone. Two strikes (~10 min
    of ear cycles) = real outage, not a reload flap: vacate the chair —
    job bind reaps the ear, the stamp lapses, the lamp goes honestly dark,
    and seats_up.bat / the panel re-seats us when the brain returns (C40)."""
    if resolve_model(base):
        _LM_DEAD['n'] = 0
        return
    _LM_DEAD['n'] += 1
    if _LM_DEAD['n'] >= 2:
        log(seat, '[seat] brain unreachable x%d — vacating; re-seat via seats_up.bat once LM Studio answers'
            % _LM_DEAD['n'], flush=True)
        sys.exit(3)
    log(seat, '[seat] brain unreachable — strike %d/2, riding one more ear cycle'
          % _LM_DEAD['n'], flush=True)


def busy(seat, on, kind='MODERATING'):
    subprocess.run([sys.executable, '-X', 'utf8', BUSY, seat,
                    'on' if on else 'off', kind],
                   cwd=HERE, capture_output=True)


def sanitize(seat, obj, feed):
    """Coerce model output into a /post-shaped action. The server validates
    kind/seat/replay again — this layer catches shape drift early."""
    if not isinstance(obj, dict):
        return {'err': 'response not a JSON object'}
    if obj.get('action') != 'post':
        return {'hold': True, 'reason': obj.get('reason', ''), 'memory': obj.get('memory')}
    kind = str(obj.get('kind') or '').lower()
    kinds = SEAT_KINDS | ({'dm'} if seat == 'CHAIR' else set())   # OFFICE lane is CHAIR↔BOSS only
    if kind not in kinds:
        # C47: small models confuse action with kind (action:"post", kind:"hold").
        # Dropping the wake loses the work AND the memory. Empty text = a hold;
        # real text = salvage into the floor-free side-table lane (lane/turn law
        # still applies downstream).
        if not str(obj.get('text') or '').strip():
            return {'hold': True, 'reason': obj.get('reason', 'bad kind, no text'),
                    'memory': obj.get('memory')}
        kind = 'aside'
    text = obj.get('text')
    if not isinstance(text, str) or not text.strip():
        return {'err': 'empty text'}
    if len(text) > 6000:
        return {'err': 'text over 6000 chars (%d)' % len(text)}
    to = obj.get('to')
    if to is not None:
        if not isinstance(to, list) or not all(isinstance(t, str) and TURN_HOLDER.match(t) for t in to):
            return {'err': 'to must be an array of seat keys'}
    reg = obj.get('register')
    if kind == 'dm' and reg:
        return {'err': 'dm carries no register — office speech never moves the floor'}
    if reg is not None:
        if not isinstance(reg, dict) or any(k not in REGISTER_KEYS for k in reg):
            return {'err': 'bad register keys: %s' % list((reg or {}).keys())}
        seats = set(feed.get('seats', [])) | {'BOSS'}
        if 'turn' in reg and reg['turn'] not in seats:
            return {'err': 'register.turn outside LIVE: %r' % reg['turn']}
    return {'post': {'speaker': seat, 'kind': kind, 'text': text,
                     **({'to': to} if to else {}),
                     **({'register': reg} if reg else {})},
            'memory': obj.get('memory'), 'reason': obj.get('reason', '')}


def lane_pending(seat, msgs):
    """The lane of the newest unanswered message addressed to this seat:
    'dm' (office), 'aside' (side table), or None. Lane is mechanics, not
    model choice — an addressed message gets its reply in the same lane."""
    last_own = max((m.get('ts') or 0) for m in msgs if m.get('speaker') == seat) or 0
    lane = None
    for m in msgs:
        if (seat in (m.get('to') or []) and (m.get('ts') or 0) > last_own
                and m.get('kind') in ('dm', 'aside')):
            lane = m['kind']
    return lane


FLOOR_CLAIM = re.compile(r'floor\s+(to|open|pass|grant|is yours|recognizes)\b|\bfloor:\s*\w+', re.I)

def floor_stamp_missing(act):
    """Prose-named floor without a register stamp = a lie on the record
    (C1/C8 class — naming grants nothing, the stamp IS the grant)."""
    p = act.get('post')
    return bool(p and FLOOR_CLAIM.search(p['text'])
                and 'turn' not in (p.get('register') or {}))


def post_to_bus(action):
    # kind:'dm' rides the OFFICE endpoint (speaker=who, server-stamped) — /post
    # rejects dm by law; the lane keeps its own door.
    if action.get('kind') == 'dm':
        body = {'who': action['speaker'], 'to': action.get('to') or ['BOSS'],
                'text': action['text']}
        req = urllib.request.Request(
            'http://127.0.0.1:8723/dm', data=json.dumps(body).encode(),
            headers={'Content-Type': 'application/json'})
    else:
        req = urllib.request.Request(
            'http://127.0.0.1:8723/post', data=json.dumps(action).encode(),
            headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=15) as r:
        return json.loads(r.read())


def wake(seat, base, model, dry):
    feed = load_feed()
    if not feed:
        log(seat, '[seat] feed unreadable — holding', flush=True)
        return
    if feed.get('adjourned'):
        log(seat, '[seat] adjourned — standing down', flush=True)
        return
    state = read_state(seat)
    cursor_p = os.path.join(HERE, 'seat_drain_%s.js' % seat)
    cursor = (read_jsvar(cursor_p) or {}).get('n', 0)
    inbox_lines, new_cursor = drain_inbox(seat, cursor)

    system, user = build_prompt(seat, feed, inbox_lines, state)
    user = append_fetches(seat, user)
    busy(seat, True, 'THINKING')
    try:
        raw = lm_call(base, model, system, user, dry)
        _LM_DEAD['n'] = 0
    except urllib.error.HTTPError as e:
        if e.code == 400:
            # context overflow — the prompt outgrew the loaded ctx. Shed the
            # big sections and retry once small; a persistent 400 means the
            # ctx itself is too small and the log says so (C41).
            log(seat, '[seat] 400 context overflow — shedding tail, retrying small', flush=True)
            system, user = build_prompt(seat, feed, inbox_lines, state, small=True)
            user = append_fetches(seat, user)
            try:
                raw = lm_call(base, model, system, user, dry)
            except Exception as e2:
                busy(seat, False)
                log(seat, '[seat] still over ctx (%s) — raise LM Studio context length' % e2, flush=True)
                return
        else:
            busy(seat, False)
            log(seat, '[seat] LM call failed: %s' % e, flush=True)
            brain_gone(base, seat)
            return
    except Exception as e:
        busy(seat, False)
        log(seat, '[seat] LM call failed: %s' % e, flush=True)
        brain_gone(base, seat)
        return
    try:
        obj = json.loads(raw)
    except Exception:
        busy(seat, False)
        log(seat, '[seat] non-JSON response:\n%s' % raw[:1500], flush=True)
        return

    act = sanitize(seat, obj, feed)
    if dry:
        print('=== DRY ===\n--- system (%d chars) ---\n%s\n--- user (%d chars) ---\n%s'
              % (len(system), system[:3000], len(user), user[:6000]))
        print('--- raw model output ---\n%s' % raw)
        print('--- parsed action ---\n%s' % json.dumps(act, indent=1))
        busy(seat, False)
        return

    if act.get('err'):
        busy(seat, False)
        log(seat, '[seat] sanitize rejected: %s' % act['err'], flush=True)
        return
    if floor_stamp_missing(act):
        log(seat, '[seat] floor fence tripped — reprompting', flush=True)
        user += ('\n\nDAEMON FENCE: your last action named a floor in prose but '
                'carried no register.turn stamp — naming grants nothing, the stamp '
                'IS the grant. Either put "turn":"<SEAT>" in register, or remove '
                'the floor claim. Re-emit.')
        try:
            raw = lm_call(base, model, system, user, dry)
            obj = json.loads(raw)
        except Exception as e:
            busy(seat, False)
            log(seat, '[seat] reprompt failed: %s\n%s' % (e, raw[:800] if 'raw' in dir() else ''), flush=True)
            brain_gone(base, seat)
            return
        act = sanitize(seat, obj, feed)
        if act.get('err') or floor_stamp_missing(act):
            busy(seat, False)
            log(seat, '[seat] floor fence: still foul after reprompt — refusing: %s' % act.get('err'), flush=True)
            return
    lane = lane_pending(seat, feed.get('messages', []))
    if (lane and act.get('post') and act['post'].get('kind') != lane
            and feed.get('turn') != seat):   # floor-holder keeps main-table rights
        log(seat, '[seat] lane override: %s wake, model chose kind=%s — routing %s'
              % (lane, act['post'].get('kind'), lane), flush=True)
        act['post']['kind'] = lane
        act['post'].pop('register', None)   # side/office lanes carry no floor
    if act.get('hold'):
        log(seat, '[seat] hold — %s' % act.get('reason'), flush=True)
    else:
        # REPLAY GUARD (C45): a post byte-identical to one this seat already has
        # on record is a discharged-debt replay, not speech. Model compliance
        # proved intermittent — the daemon drops it to a hold. >80 chars: short
        # repeats ("ok", acks) are natural speech, not replays.
        ptext = (act['post'].get('text') or '')
        own = {m.get('text') for m in feed.get('messages', [])
               if m.get('speaker') == seat}
        if len(ptext) > 80 and ptext in own:
            log(seat, '[seat] replay-guard: identical text already on record — post dropped', flush=True)
            if isinstance(act.get('memory'), dict):
                act['memory']['notes'] = ('%s [replay-guard: daemon dropped identical re-post]'
                                          % (act['memory'].get('notes') or ''))
        else:
            try:
                r = post_to_bus(act['post'])
                log(seat, '[seat] posted %s (n=%s): %s'
                      % ((r.get('msg') or {}).get('id'), r.get('n'), act['post']['text'][:120]), flush=True)
            except Exception as e:
                # server law (409 turn-gate etc.) is the last fence — log it and let
                # the seat record the refusal in its memory instead of dying
                log(seat, '[seat] /post refused: %s — action dropped' % e, flush=True)
                if isinstance(act.get('memory'), dict):
                    act['memory']['notes'] = ('%s [server refused last post: %s]'
                                              % (act['memory'].get('notes') or '', e))
    mem = act.get('memory')
    if not isinstance(mem, dict):
        # memory omitted = continuity lost forever; daemon writes a labeled
        # stub so the seat still has a state file next wake
        mem = {'stance': '(unset)', 'debts': [],
               'notes': '(no memory returned this wake — daemon stub; '
                        'last action: %s)' % act.get('reason', 'unrecorded')}
        act['memory'] = mem
    collect_needs(seat, mem)
    record = diary_append(seat, mem)
    write_state(seat, mem, act.get('reason') or '', record)
    open(cursor_p + '.tmp', 'w').write('window.DVM_DRAIN_%s = {"n": %d};\n' % (seat, new_cursor))
    os.replace(cursor_p + '.tmp', cursor_p)
    busy(seat, False)


def main():
    seat = sys.argv[1].upper()
    dry = '--dry' in sys.argv
    once = '--once' in sys.argv
    base = LM_BASE_DEFAULT
    model = None
    tail_args = sys.argv[2:]
    for i, a in enumerate(tail_args):
        if a == '--base' and i + 1 < len(tail_args):
            base = tail_args[i + 1]
        elif a == '--model' and i + 1 < len(tail_args):
            model = tail_args[i + 1]
    model = model or resolve_model(base)
    if not model:
        log(seat, '[seat] LM Studio not answering at %s — start the local server' % base)
        sys.exit(2)
    log(seat, '[seat] %s on %s (model %s)%s' % (seat, base, model, ' — DRY RUN' if dry else ''), flush=True)

    if once:
        wake(seat, base, model, dry)
        return

    # Standing loop: arm ear -> block on doorbell -> wake -> re-arm.
    # The pulse socket stamps presence while held; exit 0 = actionable batch.
    ear = arm_ear(seat)
    while True:
        out = None
        try:
            out, _ = ear.communicate(timeout=300)
        except subprocess.TimeoutExpired:
            ear.kill(); ear.wait()
            log(seat, '[seat] ear stale 300s — re-arming', flush=True)
        lines = out.strip().splitlines() if out else []
        for line in lines[-6:]:
            log(seat, '[ear] %s' % line, flush=True)
        # ACTIONABLE GATE (C42): the induction promises wakes only on YOUR TURN,
        # BOSS-SPOKE, or ADDRESSED — ambient batches used to cost a full ~10K
        # prompt for a legal silence. Honor the promise: parse the ear's report
        # and skip the model call entirely when nothing entitles the seat.
        fl = lines[-1] if lines else ''
        actionable = ('your_turn: True' in fl or 'addressed: True' in fl
                      or fetch_pending(seat)          # a queued NEED is its own
                      # entitlement — the queue rings the bell (C46); without it
                      # a fetch-requesting seat parks behind the C42 gate forever
                      or any(re.match(r'\[\w+\]\s+BOSS:', l) for l in lines))
        if actionable:
            wake(seat, base, model, dry)
            if fetch_pending(seat):
                log(seat, '[seat] fetch pending — daemon follow-up wake to serve NEED:', flush=True)
                wake(seat, base, model, dry)
        elif out:
            log(seat, '[seat] ambient batch — no entitlement; holding without model call', flush=True)
        ear = arm_ear(seat)


if __name__ == '__main__':
    main()
