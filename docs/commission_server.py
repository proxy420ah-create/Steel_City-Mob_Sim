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
import glob, json, os, re, sys, time
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler

HERE = os.path.dirname(os.path.abspath(__file__))
FILE = os.path.join(HERE, 'deliberation_feed.js')
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8723
# DVM-009: legacy member ids still accepted in @mentions — translated to seat keys
LEGACY = {'DEVIN': 'CHAIR', 'MEMBER2': 'ARCHITECT', 'MEMBER3': 'BUTTON', 'MEMBER4': 'INFORMANT'}

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
                files = [FILE] + glob.glob(os.path.join(HERE, 'presence_*.js'))
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
            seats = set(data.get('seats', []))
            to = re.findall(r'@(\w+)', text.upper())
            to = [LEGACY.get(t, t) for t in to]   # DVM-009: legacy @MEMBERn resolves to seat keys
            valid = [s for s in to if s in seats]

            if text.startswith('/table'):
                ptext = text[6:].strip() or 'untitled proposal'
                pid = 'DVM-%03d' % (len(msgs))
                data['proposal'] = {'id': pid, 'text': ptext, 'status': 'tabled'}
                data['phase'] = 1
                msgs.append({'id': 'boss-%s' % pid, 'speaker': 'BOSS', 'kind': 'boss', 'to': valid, 'text': text})
                msgs.append({'id': 'sys-%s' % pid, 'kind': 'phase', 'phase': 1, 'text': 'PROPOSAL TABLED — OPENINGS'})
                # drafting seat is the sane fallback; literal default rekeyed (DVM-009)
                data['turn'] = valid[0] if valid else ('ARCHITECT' if 'ARCHITECT' in seats else data.get('seats', ['ARCHITECT'])[-1])
            else:
                bid = 'boss-%d' % (sum(1 for m in msgs if m.get('speaker') == 'BOSS') + 1)
                msgs.append({'id': bid, 'speaker': 'BOSS', 'kind': 'boss', 'to': valid, 'text': text})
                if valid:
                    data['turn'] = valid[0]  # @SEAT hands them the floor
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

    def _ok(self, obj, code=200):
        b = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def log_message(self, *args):
        pass

if __name__ == '__main__':
    os.chdir(HERE)
    print(f'Commission server -> http://127.0.0.1:{PORT}/DELIBERATION_MACHINE.html  (serving {HERE})', flush=True)
    ThreadingHTTPServer(('127.0.0.1', PORT), Handler).serve_forever()
