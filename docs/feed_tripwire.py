"""Feed tripwire — logs any message appended with a ts far older than now (stale-replay
mint, C33) together with a snapshot of recently-created processes, so the writer's
identity is captured at the moment it writes. Read-only; detached; ~0 cost."""
import json, os, re, subprocess, time, datetime
HERE = os.path.dirname(os.path.abspath(__file__))
FEED = os.path.join(HERE, 'deliberation_feed.js'); LOG = os.path.join(HERE, 'tripwire.log')
NOWIN = getattr(subprocess, 'CREATE_NO_WINDOW', 0)

def ids():
    try:
        s = open(FEED, encoding='utf-8').read()
        d = json.loads(re.search(r'window\.DVM_FEED\s*=\s*(\{.*\})\s*;?\s*$', s, re.S).group(1))
        return {m.get('id'): m for m in d['messages'] if isinstance(m, dict)}
    except Exception:
        return None

def procs():
    try:
        out = subprocess.run(['wmic', 'process', 'get', 'CreationDate,ProcessId,ParentProcessId,Name,CommandLine', '/format:csv'],
                             capture_output=True, text=True, timeout=20, creationflags=NOWIN).stdout
    except Exception:
        return ['wmic failed']
    now = datetime.datetime.now(); rows = []
    for ln in out.splitlines():
        p = ln.split(',')
        if len(p) < 6 or not p[2][:14].isdigit(): continue
        try: age = (now - datetime.datetime.strptime(p[2][:14], '%Y%m%d%H%M%S')).total_seconds()
        except Exception: continue
        if age < 30 and 'wmic' not in ln: rows.append('%s pid=%s ppid=%s age=%ds | %s' % (p[3], p[5], p[4], age, ','.join(p[1:2])[:260]))
    return rows

seen = ids() or {}
while True:
    time.sleep(0.4)
    cur = ids()
    if cur is None: continue
    new = [m for k, m in cur.items() if k not in seen]
    if new:
        stale = [m for m in new if m.get('ts') and m['ts'] < time.time() * 1000 - 600000]
        if stale:
            with open(LOG, 'a', encoding='utf-8') as f:
                f.write('%s STALE MINT %s\n' % (datetime.datetime.now().isoformat(timespec='seconds'), [m['id'] for m in stale]))
                for r in procs(): f.write('   ' + r + '\n')
        seen = cur
