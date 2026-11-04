#!/usr/bin/env python3
"""Commission server — static host + the boss's write-path into the bus.

Usage:  python commission_server.py [port]   (default 8723)

GET  — serves the docs folder (dashboard, feed, watcher).
POST /boss {"text": "..."} — writes THE BOSS's message into deliberation_feed.js:
  - appends {"speaker":"BOSS","kind":"boss","to":[...],"text":...}
  - @SEAT tokens route: floor moves to the first addressed valid seat
    (their watcher wakes — that IS the delivery)
  - "/table <text>" instead tables a proposal, advances to phase 1 (openings),
    and hands the floor to the addressed seat or the last seat in `seats`
Known limit: boss interjections can race an agent's write — rare, one
repeated turn is the worst case.
"""
import glob, gzip, json, os, re, sqlite3, sys, threading, time
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler

# pulse-plane (triage): server-observed seat liveness. While a seat holds
# GET /pulse/<SEAT>, the SERVER stamps presence_<SEAT>.js — the lamp is an
# open socket, not a self-declared file. Stops the moment the handler dies.
PULSE_STAMP = {}   # seat -> last presence write ms (throttle to ~8s)

def pulse_stamp(seat, ttl_ms=30000, via='pulse', force=False):
    now = int(time.time() * 1000)
    if not force and now - PULSE_STAMP.get(seat, 0) < 8000:
        return
    PULSE_STAMP[seat] = now
    p = os.path.join(HERE, 'presence_%s.js' % seat)
    tmp = p + '.tmp'
    rec = {'seat': seat, 'armed_at': now, 'ttl_ms': ttl_ms,
           'pulse_at': now, 'beat': True, 'via': via}
    try:
        open(tmp, 'w', encoding='utf-8').write(
            'window.DVM_PRESENCE_%s = %s;\n' % (seat, json.dumps(rec)))
        os.replace(tmp, p)
    except OSError:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
FILE = os.path.join(HERE, 'deliberation_feed.js')
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8723
# DVM-009: legacy member ids still accepted in @mentions — translated to seat keys
LEGACY = {'DEVIN': 'CHAIR', 'MEMBER2': 'ARCHITECT', 'MEMBER3': 'BUTTON', 'MEMBER4': 'INFORMANT'}

# usage monitor — local session telemetry (num_tokens_preceding = input tokens processed
# per call; output untracked). Ceiling observations persist so the empirical free-tier
# rolling limit converges across hits.
DB_PATH = os.environ.get('DEVIN_SESSIONS_DB',
    os.path.join(os.path.expanduser('~'), 'AppData', 'Roaming', 'devin', 'cli', 'sessions.db'))
USAGE_LEDGER = os.path.join(HERE, 'usage_ledger.json')
U_WINDOWS = (('h1', 3600), ('h5', 18000), ('h24', 86400))

def _ledger_read():
    try:
        return json.load(open(USAGE_LEDGER, encoding='utf-8'))
    except Exception:
        return {'obs': []}

def _ledger_write(d):
    tmp = USAGE_LEDGER + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump(d, f, indent=1)
    os.replace(tmp, USAGE_LEDGER)

def usage_report():
    now = int(time.time())
    rep = {'now': now, 'db': os.path.basename(DB_PATH), 'windows': {}, 'top': [],
           'ceiling': _ledger_read(),
           'note': 'input-token estimate (context size per call, summed); output + cache untracked'}
    if not os.path.exists(DB_PATH):
        rep['err'] = 'sessions.db not found'
        return rep
    try:
        uri = 'file:' + DB_PATH.replace('\\', '/') + '?mode=ro'
        con = sqlite3.connect(uri, uri=True)
        sess = {}
        for sid, title, model, wdir, act in con.execute(
                'select id,title,model,working_directory,last_activity_at from sessions'):
            sess[sid] = {'title': title or '', 'model': model or '',
                         'dir': wdir or '', 'act': act or 0}
        mx = con.execute('select max(row_id) from message_nodes').fetchone()[0] or 0
        cutoff = now - 86400
        bound = 200000
        while bound <= 12800000:
            lo = con.execute('select min(created_at) from message_nodes where row_id > ?',
                             (mx - bound,)).fetchone()[0]
            if lo is None or lo <= cutoff:
                break
            bound *= 4
        per = {}   # sid -> {wn: [turns, tokens]}
        tot = {wn: [0, 0] for wn, _ in U_WINDOWS}
        buckets = [0] * 24                    # hourly token buckets, oldest → newest
        cbuckets = [0] * 24                   # hourly CALL buckets (rows that carried context)
        minutes, c60 = {}, 0                  # per-minute call counts → request-rate metrics
        for sid, ca, tp in con.execute(
                'select session_id, created_at, coalesce('
                "json_extract(metadata,'$.num_tokens_preceding'),0) "
                'from message_nodes where row_id > ? and created_at > ?',
                (mx - bound, cutoff)):
            tp = tp or 0
            b = int((now - ca) // 3600)
            if 0 <= b < 24:
                buckets[23 - b] += tp
                if tp > 0:
                    cbuckets[23 - b] += 1
            if tp > 0:
                mk = int(ca // 60)
                minutes[mk] = minutes.get(mk, 0) + 1
                if ca >= now - 60:
                    c60 += 1
            for wn, span in U_WINDOWS:
                if ca >= now - span:
                    if sid not in per:
                        per[sid] = {w: [0, 0] for w, _ in U_WINDOWS}
                    per[sid][wn][0] += 1
                    per[sid][wn][1] += tp
                    tot[wn][0] += 1
                    tot[wn][1] += tp
        con.close()
        rep['buckets'] = buckets
        rep['cbuckets'] = cbuckets
        m1h = [v for k, v in minutes.items() if k >= (now - 3600) // 60]
        rep['rpm'] = {'now': c60, 'peak_h1': max(m1h, default=0), 'peak_h24': max(minutes.values(), default=0),
                      'calls_h1': sum(m1h), 'calls_h24': sum(minutes.values())}
        models = {}
        for sid, sw in per.items():
            mname = (sess.get(sid, {}).get('model') or 'unknown')
            e = models.setdefault(mname, {'tok24': 0, 'calls24': 0})
            e['tok24'] += sw['h24'][1]; e['calls24'] += sw['h24'][0]
        rep['models'] = sorted(({'model': k, **v} for k, v in models.items()), key=lambda x: -x['tok24'])[:4]
        for wn, _ in U_WINDOWS:
            t = tot[wn]
            rep['windows'][wn] = {'turns': t[0], 'tokens': t[1],
                                  'sessions': sum(1 for s in per.values() if s[wn][1] > 0)}
        for sid, sw in sorted(per.items(), key=lambda kv: -kv[1]['h24'][1])[:8]:
            s = sess.get(sid, {'title': '(unknown)', 'model': '', 'dir': '', 'act': 0})
            rep['top'].append({
                'id': sid, 'title': s['title'][:60], 'model': s['model'],
                'commission': 'SteelCityMobSim' in (s['dir'] + s['title']),
                'tok24': sw['h24'][1], 'tok5': sw['h5'][1], 'tok1': sw['h1'][1],
                'turns24': sw['h24'][0], 'act': s['act']})
    except Exception as e:
        rep['err'] = str(e)
    return rep

def mark_limit():
    """BOSS reports a rate-limit hit: stamp the current window totals as a
    ceiling observation. Across hits the consistent window ≈ the rolling cap."""
    rep = usage_report()
    obs = {'ts': rep['now'],
           'h1': rep['windows'].get('h1', {}).get('tokens', 0),
           'h5': rep['windows'].get('h5', {}).get('tokens', 0),
           'h24': rep['windows'].get('h24', {}).get('tokens', 0)}
    led = _ledger_read()
    led.setdefault('obs', []).append(obs)
    _ledger_write(led)
    return {'ok': True, 'obs': obs, 'n': len(led['obs'])}

def mark_event(kind, note, model=''):
    """BOSS reports a non-token limiter (request rate, model cap…): events[]
    pin on the BURN chart's 24h axis beside the token-ceiling observations."""
    led = _ledger_read()
    ev = {'at': int(time.time() * 1000), 'kind': str(kind)[:24] or 'event',
          'model': str(model)[:40], 'note': str(note)[:300]}
    led.setdefault('events', []).append(ev)
    _ledger_write(led)
    return {'ok': True, 'event': ev, 'n': len(led['events'])}

def pulse_batch(msgs, since, seat):
    """What a seat's doorbell is worth waking for: drop other seats' OFFICE dms
    and the seat's own posts (a self-echo buys a full-context turn for nothing)."""
    return [m for m in msgs[since:]
            if m.get('speaker') != seat
            and (m.get('kind') != 'dm' or seat in (m.get('to') or []))]

def load_feed():
    try:
        raw = open(FILE, encoding='utf-8').read().strip()
        m = re.search(r'^window\.DVM_FEED\s*=\s*(\{.*\})\s*;?\s*$', raw, re.S | re.M)
        return json.loads(m.group(1)) if m else None
    except Exception:
        return None

def save_feed(data):
    try:
        raw = open(FILE, encoding='utf-8').read()
        idx = raw.find('\nwindow.DVM_FEED')
        header = raw[:idx + 1] if idx >= 0 else ''
    except OSError:
        header = ''
    body = 'window.DVM_FEED = ' + json.dumps(data, indent=2, ensure_ascii=False) + ';\n'
    tmp = FILE + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        f.write(header + body)
    os.replace(tmp, FILE)  # atomic — dashboard never sees a half-written file

class Handler(SimpleHTTPRequestHandler):
    def do_GET(self):
        if self.path.rstrip('/') == '/usage':
            return self._ok(usage_report())
        # voxel pipeline assets — whitelisted read into the item library so the
        # room renders the same file the studio exports (dogfooding, one source).
        # scene bake of an editable building asset (see bake_building.py): rebuilt when the
        # source JSON is newer, served gzip'd (the occupancy/walk grids are mostly zeros).
        if self.path.startswith('/bake/'):
            return self._bake(self.path[len('/bake/'):].split('?')[0])
        if self.path.startswith('/voxel/'):
            name = self.path[len('/voxel/'):].split('?')[0]
            if not re.fullmatch(r'[\w\-]+', name):
                return self._ok({'err': 'bad name'}, 400)
            p = os.path.normpath(os.path.join(
                HERE, '..', 'Assets', 'StreamingAssets', 'voxel_items', name + '.json'))
            if not os.path.isfile(p) or not os.path.realpath(p).startswith(
                    os.path.realpath(os.path.join(HERE, '..', 'Assets'))):
                return self._ok({'err': 'not found'}, 404)
            try:
                return self._ok(json.load(open(p, encoding='utf-8')))
            except Exception as e:
                return self._ok({'err': str(e)}, 500)
        if self.path.startswith('/char/'):
            name = self.path[len('/char/'):].split('?')[0]
            if not re.fullmatch(r'[\w\-]+', name):
                return self._ok({'err': 'bad name'}, 400)
            p = os.path.normpath(os.path.join(
                HERE, '..', 'Assets', 'StreamingAssets', 'voxel_characters', name + '.json'))
            if not os.path.isfile(p) or not os.path.realpath(p).startswith(
                    os.path.realpath(os.path.join(HERE, '..', 'Assets'))):
                return self._ok({'err': 'not found'}, 404)
            try:
                return self._ok(json.load(open(p, encoding='utf-8')))
            except Exception as e:
                return self._ok({'err': str(e)}, 500)
        # studio pages + asset files — read-only whitelist so voxel_editor.html
        # runs over http and its default-model fetches (../Assets/...) resolve.
        # Root: the studio dir and Assets dir only; realpath must stay inside.
        if self.path.startswith('/studio/'):
            rel = self.path[len('/studio/'):].split('?')[0]
            return self._serve(os.path.join(HERE, '..', 'VoxelAssetStudio', rel),
                               {'.html', '.js', '.css', '.json', '.png', '.jpg', '.ico', '.md', '.txt', '.map'})
        if self.path.startswith('/Assets/'):
            rel = self.path[len('/Assets/'):].split('?')[0]
            return self._serve(os.path.join(HERE, '..', 'Assets', rel),
                               {'.json', '.png', '.jpg', '.js', '.css', '.bin'})
        # asset index — every loadable model in voxel_items, so tools can offer
        # a real dropdown instead of a hardcoded default. ?model=<name> in the
        # studio resolves to /Assets/StreamingAssets/voxel_items/<name>.json.
        if self.path.rstrip('/') == '/models':
            d = os.path.join(HERE, '..', 'Assets', 'StreamingAssets', 'voxel_items')
            out = []
            for fn in sorted(os.listdir(d)):
                if not fn.endswith('.json'):
                    continue
                try:
                    j = json.load(open(os.path.join(d, fn), encoding='utf-8'))
                    out.append({'name': fn[:-5], 'assetType': j.get('assetType'),
                                'dims': j.get('dims'), 'voxels': len(j.get('voxels', [])),
                                'studio': f'/studio/voxel_editor.html?model={fn[:-5]}'})
                except Exception:
                    out.append({'name': fn[:-5], 'err': 'unparseable'})
            return self._ok({'models': out})
        # pulse-plane: seat long-poll. Client holds ~25s; server returns the
        # instant the feed grows past `since` (the response IS the doorbell —
        # a blocking tool call returning wakes the host), or a heartbeat to
        # re-issue. The held request is what stamps the lamp server-side.
        if self.path.startswith('/pulse/'):
            return self._pulse()
        # DVM-010: SSE push — the server watches mtimes on the page's behalf.
        # Signals carry the filename only; the script-tag loader still carries
        # payload (one serialization path). Heartbeat keeps the conn alive.
        if self.path.rstrip('/') != '/events':
            return super().do_GET()
        self.send_response(200)
        self.send_header('Content-Type', 'text/event-stream')
        self.send_header('Cache-Control', 'no-cache')
        self.send_header('Connection', 'keep-alive')
        self.end_headers()
        mtimes, beat = {}, time.time()
        try:
            while True:
                files = [FILE] + glob.glob(os.path.join(HERE, 'presence_*.js')) + glob.glob(os.path.join(HERE, 'busy_*.js')) + glob.glob(os.path.join(HERE, 'office_evt.js'))
                for f in files:
                    name = os.path.basename(f)[:-3]      # deliberation_feed / presence_<SEAT>
                    try: mt = os.stat(f).st_mtime
                    except OSError: mt = -1
                    if name in mtimes and mt != mtimes[name]:
                        self.wfile.write(b'data: {"file":"' + name.encode() + b'"}\n\n')
                    mtimes[name] = mt
                if time.time() - beat > 15:
                    self.wfile.write(b':\n\n'); beat = time.time()
                self.wfile.flush()
                time.sleep(0.4)
        except (BrokenPipeError, ConnectionResetError, OSError):
            pass

    def do_POST(self):
        path = self.path.rstrip('/')
        if path == '/usage/limit':
            try:
                return self._ok(mark_limit())
            except Exception as e:
                return self._ok({'ok': False, 'err': str(e)}, 400)
        if path == '/usage/event':
            try:
                n = int(self.headers.get('Content-Length', 0))
                obj = json.loads(self.rfile.read(n) or b'{}')
                return self._ok(mark_event(obj.get('kind', 'event'), obj.get('note', ''), obj.get('model', '')))
            except Exception as e:
                return self._ok({'ok': False, 'err': str(e)}, 400)
        if path == '/room/lights':
            # room rig preset — persisted shared default, localStorage stays per-browser
            try:
                n = int(self.headers.get('Content-Length', 0))
                obj = json.loads(self.rfile.read(n) or b'{}')
                for k in ('kaz', 'kel', 'kint', 'aint', 'style'):
                    if k in obj and not isinstance(obj[k], (int, float, str)):
                        raise ValueError('bad ' + k)
                tmp = os.path.join(HERE, 'room_lights.json.tmp')
                with open(tmp, 'w', encoding='utf-8') as f:
                    json.dump(obj, f, indent=1)
                os.replace(tmp, os.path.join(HERE, 'room_lights.json'))
                return self._ok({'ok': True})
            except Exception as e:
                return self._ok({'ok': False, 'err': str(e)}, 400)
        if path in ('/adjourn', '/reconvene'):
            # DVM-007: recess control — flag + sys banner in one atomic write.
            # turn is deliberately untouched: deliberation freezes where it lies.
            try:
                data = load_feed()
                if not data:
                    raise ValueError('feed unreadable')
                adj = path == '/adjourn'
                data['adjourned'] = adj
                msgs = data.setdefault('messages', [])
                mid = 'sys-%s-%d' % ('adjourn' if adj else 'reconvene', len(msgs) + 1)
                msgs.append({'id': mid, 'kind': 'sys',
                             'text': 'MEETING ADJOURNED — seats stand down' if adj else 'RECONVENED — seats re-arm'})
                save_feed(data)
                self._ok({'ok': True, 'adjourned': adj, 'msgs': msgs[-1:]})
            except Exception as e:
                self._ok({'ok': False, 'err': str(e)}, 400)
            return
        if path == '/eject':
            # boss-ordered: free a seat — presence + busy zeroed (commanded dark:
            # armed_at:0 is the triage-dark signature the watchdog never re-arms)
            # + a sys banner so the room reads the vacancy.
            try:
                body = json.loads(self.rfile.read(int(self.headers.get('Content-Length', 0))) or b'{}')
                seat = (body.get('seat') or '').strip().upper()
                data = load_feed()
                if seat not in set(data.get('seats', [])) or seat == 'BOSS':
                    raise ValueError('not a member seat')
                now = int(time.time() * 1000)
                for base, rec in (('presence_%s.js', {'seat': seat, 'armed_at': 0, 'ttl_ms': 30000, 'beat': False, 'via': 'ejected'}),
                                  ('busy_%s.js',     {'seat': seat, 'armed_at': 0, 'ttl_ms': 30000, 'busy': False, 'via': 'ejected'})):
                    fn = os.path.join(HERE, base % seat)
                    open(fn + '.tmp', 'w', encoding='utf-8').write(
                        'window.DVM_%s_%s = %s;\n' % (base.split('_')[0].upper(), seat, json.dumps(rec)))
                    os.replace(fn + '.tmp', fn)
                msgs = data.setdefault('messages', [])
                msgs.append({'id': 'sys-eject-%d' % len(msgs), 'kind': 'sys',
                             'text': 'WATCHDOG — %s chair EJECTED by boss — stamps zeroed, slot free for a fresh host' % seat,
                             'to': ['BOSS'], 'ts': now})
                save_feed(data)
                self._ok({'ok': True, 'seat': seat, 'msgs': msgs[-1:]})
            except Exception as e:
                self._ok({'ok': False, 'err': str(e)}, 400)
            return
        if self.path.rstrip('/') == '/dm':
            # OFFICE lane — CHAIR↔BOSS drafting channel. kind:'dm' stays in the
            # feed (organizational privacy, not crypto) but the /pulse filter
            # keeps it out of unaddressed seat batches — no wakes, no spend.
            try:
                length = int(self.headers.get('Content-Length', 0))
                body = json.loads(self.rfile.read(length) or b'{}')
                text = (body.get('text') or '').strip()
                if not text:
                    return self._ok({'err': 'empty'}, 400)
                data = load_feed()
                if not data:
                    return self._ok({'err': 'feed unreadable'}, 500)
                msgs = data.setdefault('messages', [])
                who = 'CHAIR' if body.get('who') == 'CHAIR' else 'BOSS'
                msgs.append({'id': 'dm-%d' % len(msgs), 'speaker': who, 'kind': 'dm',
                             'to': body.get('to') or (['CHAIR'] if who == 'BOSS' else ['BOSS']),
                             'ts': int(time.time() * 1000), 'text': text})
                save_feed(data)
                return self._ok({'ok': True, 'n': len(msgs), 'msgs': [msgs[-1]]})
            except Exception as e:
                return self._ok({'err': str(e)}, 400)
        if self.path.rstrip('/') != '/boss':
            self.send_error(404); return
        try:
            n = int(self.headers.get('Content-Length', 0))
            body = json.loads(self.rfile.read(n) or b'{}')
            text = (body.get('text') or '').strip()
            if not text:
                raise ValueError('empty text')
            data = load_feed()
            if not data:
                raise ValueError('feed unreadable')
            msgs = data.setdefault('messages', [])
            # DVM-921 staged-restore (re-seat half): slot empty + a parked
            # incumbent exists -> restore it on this write, preserving status/phase.
            if not data.get('proposal'):
                st = (data.get('board') or {}).get('staged') or []
                park = next((r for r in st if r.get('parked')), None)
                if park:
                    st.remove(park)
                    data['proposal'] = {'id': park['id'], 'text': park.get('title', ''), 'status': park.get('status') or 'tabled'}
                    if park.get('phase') is not None:
                        data['phase'] = park['phase']
                    msgs.append({'id': 'sys-reseat-%d' % len(msgs), 'kind': 'sys',
                                 'text': 'PARKED INCUMBENT RE-SEATED — %s resumes' % park['id']})
            seats = set(data.get('seats', []))
            to = re.findall(r'@(\w+)', text.upper())
            to = [LEGACY.get(t, t) for t in to]   # DVM-009: legacy @MEMBERn resolves to seat keys
            valid = [s for s in to if s in seats]

            if text.startswith('/aside'):
                # DVM-017 v0.7: the boss's side-table voice — kind:'aside' posts with
                # zero boss-class machinery: no floor routing, no ts handshake stamp,
                # no needs_ack, no GO/verdict stamps. An aside is never a directive.
                bid = 'boss-%d' % (sum(1 for m in msgs if m.get('speaker') == 'BOSS') + 1)
                msgs.append({'id': bid, 'speaker': 'BOSS', 'kind': 'aside',
                             'to': valid or None, 'ts': int(time.time() * 1000),
                             'text': text[6:].strip() or 'aside'})
            elif text.startswith('/table'):
                ptext = text[6:].strip() or 'untitled proposal'
                pid = 'DVM-%03d' % (len(msgs))
                # DVM-921 staged-restore: /table while a proposal is seated parks the
                # incumbent to board.staged[] instead of evaporating it; a parked
                # incumbent re-seats the next write once the slot is empty (below).
                board = data.setdefault('board', {})
                staged = board.setdefault('staged', [])
                if data.get('proposal'):
                    inc = data['proposal']
                    staged.append({'id': inc['id'], 'title': inc.get('text', ''),
                                   'detail': 'parked mid-cycle by /table (DVM-921)',
                                   'status': inc.get('status'), 'phase': data.get('phase'),
                                   'parked': True, 'staged_by': 'server', 'ts': int(time.time() * 1000)})
                data['proposal'] = {'id': pid, 'text': ptext, 'status': 'tabled'}
                data['phase'] = 1
                msgs.append({'id': 'boss-%s' % pid, 'speaker': 'BOSS', 'kind': 'boss', 'to': valid, 'text': text})
                msgs.append({'id': 'sys-%s' % pid, 'kind': 'phase', 'phase': 1, 'text': 'PROPOSAL TABLED — OPENINGS'})
                # drafting seat is the sane fallback; literal default rekeyed (DVM-009)
                data['turn'] = valid[0] if valid else ('ARCHITECT' if 'ARCHITECT' in seats else data.get('seats', ['ARCHITECT'])[-1])
                data['turn_at'] = int(time.time() * 1000)   # DVM-015 §6b + C6: ms everywhere — the silence clock rides every floor move
            else:
                bid = 'boss-%d' % (sum(1 for m in msgs if m.get('speaker') == 'BOSS') + 1)
                now = int(time.time() * 1000)   # C6: ms everywhere — turn_at + msg ts stamp one clock
                # DVM-015 §5b: a bare 👍 (±@SEAT) is the sender's kind:'ok' — binding
                # resolved at write time so the record carries ack_of explicitly.
                # §3b: the ok routes turn to the acker — authorized and floored in one write.
                if re.sub(r'@\w+', '', text).strip() == '👍':
                    oks = {m.get('ack_of') for m in msgs
                           if m.get('kind') == 'ok' and m.get('speaker') == 'BOSS'}
                    pend = next((m for m in reversed(msgs)
                                 if m.get('kind') == 'ack' and 'BOSS' in (m.get('to') or [])
                                 and m.get('id') not in oks
                                 and (not valid or m.get('speaker') == valid[0])), None)
                    msgs.append({'id': bid, 'speaker': 'BOSS', 'kind': 'ok',
                                 'to': [pend['speaker']] if pend else [],
                                 'ack_of': pend['id'] if pend else None, 'ts': now, 'text': text})
                    if pend:
                        data['turn'] = pend['speaker']; data['turn_at'] = now
                else:
                    bmsg = {'id': bid, 'speaker': 'BOSS', 'kind': 'boss', 'to': valid, 'text': text}
                    if valid:
                        bmsg['ts'] = now          # handshake-bearing directive stamps ts (§2)
                        # HOLD marker: all-caps HOLD word or leading "hold:" — a lowercase
                        # "hold on a second" inside a directive cannot gate a seat.
                        if re.search(r'\bHOLD\b', text) or text.lower().startswith('hold:'):
                            bmsg['hold'] = True
                    msgs.append(bmsg)
                    if valid:
                        data['turn'] = valid[0]   # @SEAT hands them the floor
                        data['turn_at'] = now     # §6b: every turn assignment stamps the clock
                    # DVM-014: GO CODE becomes stamped data — @BUTTON + GO post-verdict → status:'go'
                    prop = data.get('proposal') or {}
                    if (prop.get('id') and 'BUTTON' in valid and re.search(r'\bGO\b', text, re.I)
                            and (data.get('verdict') or {}).get('id') == prop.get('id')):
                        prop['status'] = 'go'

            save_feed(data)
            echo = msgs[-2:] if text.startswith('/table') else msgs[-1:]
            self._ok({'ok': True, 'to': valid, 'turn': data.get('turn'), 'msgs': echo})
        except Exception as e:
            self._ok({'ok': False, 'err': str(e)}, 400)

    def _pulse(self):
        seat = self.path[len('/pulse/'):].split('?')[0].upper()
        if not re.fullmatch(r'[A-Z0-9_]+', seat):
            return self._ok({'err': 'bad seat'}, 400)
        q = {}
        if '?' in self.path:
            for kv in self.path.split('?', 1)[1].split('&'):
                if '=' in kv:
                    k, v = kv.split('=', 1); q[k] = v
        try:
            since = int(q.get('since', '-1'))
        except ValueError:
            since = -1
        last_mtime, deadline = -1, time.time() + 25
        while time.time() < deadline:
            pulse_stamp(seat)
            try:
                mt = os.stat(FILE).st_mtime
            except OSError:
                mt = -1
            if mt != last_mtime:
                last_mtime = mt
                d = load_feed()
                if d:
                    msgs = d.get('messages', [])
                    n = len(msgs)
                    if since < 0:
                        return self._ok({'new': False, 'init': True, 'n': n})
                    if n > since:
                        # flap-window fix: a delivered doorbell earns a longer
                        # fuse — the seat is ACTING on the batch, not dead.
                        # 90s to act + re-arm before the lamp may go dark.
                        batch = pulse_batch(msgs, since, seat)
                        if not batch:
                            # nothing worth waking for: another seat's dm (OFFICE
                            # lane is invisible to the room) or only this seat's
                            # own echo — keep holding.
                            time.sleep(0.8); continue
                        pulse_stamp(seat, ttl_ms=90000, via='pulse-doorbell', force=True)
                        return self._ok({'new': True, 'n': n, 'msgs': batch,
                                         'turn': d.get('turn'),
                                         'your_turn': d.get('turn') == seat,
                                         'addressed': any(seat in (m.get('to') or []) for m in batch)})
            time.sleep(0.8)
        return self._ok({'new': False, 'hb': int(time.time() * 1000)})

    _BAKE_LOCK = threading.Lock()
    _BAKE_GZ = {}   # dst path -> (mtime, gzipped bytes)

    def _bake(self, name):
        if not re.fullmatch(r'[\w\-]+', name):
            return self._ok({'err': 'bad name'}, 400)
        items = os.path.normpath(os.path.join(HERE, '..', 'Assets', 'StreamingAssets', 'voxel_items'))
        src, dst = os.path.join(items, name + '.json'), os.path.join(items, name + '.bake.json')
        if not os.path.isfile(src) and not os.path.isfile(dst):   # a committed bake alone is enough (the 56MB source needn't ship)
            return self._ok({'err': 'not found'}, 404)
        try:
            with self._BAKE_LOCK:
                stamp_p = dst[:-len('.json')] + '.stamp'
                try:
                    stamp = json.load(open(stamp_p))
                except Exception:
                    stamp = None
                # stale = no bake, or the source differs from what was baked (mtime OR size) -- NOT "newer than the bake":
                # a copied/moved export keeps its old timestamp and would never trigger a rebake
                if os.path.isfile(src) and (not os.path.isfile(dst) or not stamp
                                            or stamp.get('mtime') != os.path.getmtime(src) or stamp.get('size') != os.path.getsize(src)):
                    if HERE not in sys.path:
                        sys.path.insert(0, HERE)
                    import bake_building
                    bake_building.bake(name, log=lambda m: sys.stderr.write(m + chr(10)))
                mt = os.path.getmtime(dst)
                hit = self._BAKE_GZ.get(dst)
                if not hit or hit[0] != mt:
                    with open(dst, 'rb') as f:
                        hit = (mt, gzip.compress(f.read(), 6))
                    self._BAKE_GZ[dst] = hit
            body = hit[1]
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Encoding', 'gzip')
            self.send_header('Content-Length', str(len(body)))
            self.send_header('Cache-Control', 'no-cache')
            self.end_headers()
            self.wfile.write(body)
        except Exception as e:
            return self._ok({'err': 'bake failed: ' + str(e)}, 500)

    def _ok(self, obj, code=200):
        b = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    _MIME = {'.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css',
             '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg',
             '.ico': 'image/x-icon', '.md': 'text/markdown', '.txt': 'text/plain',
             '.map': 'application/json', '.bin': 'application/octet-stream'}

    def _serve(self, path, okext):
        # scoped static file: extension whitelist + realpath must stay inside
        # the intended root (rejects ../ escapes).
        p = os.path.normpath(path)
        base = os.path.realpath(os.path.join(HERE, '..',
                'VoxelAssetStudio' if self.path.startswith('/studio/') else 'Assets'))
        if (os.path.splitext(p)[1].lower() not in okext
                or not os.path.isfile(p)
                or not os.path.realpath(p).startswith(base)):
            return self._ok({'err': 'not found'}, 404)
        try:
            self.send_response(200)
            self.send_header('Content-Type', self._MIME.get(
                os.path.splitext(p)[1].lower(), 'application/octet-stream'))
            self.send_header('Content-Length', str(os.path.getsize(p)))
            self.end_headers()
            with open(p, 'rb') as f:
                while True:
                    chunk = f.read(1 << 16)
                    if not chunk:
                        break
                    self.wfile.write(chunk)
        except Exception as e:
            return self._ok({'err': str(e)}, 500)

    def log_message(self, *args):
        pass

if __name__ == '__main__':
    os.chdir(HERE)
    print(f'Commission server -> http://127.0.0.1:{PORT}/DELIBERATION_MACHINE.html  (serving {HERE})', flush=True)
    ThreadingHTTPServer(('127.0.0.1', PORT), Handler).serve_forever()
