# INFORMANT — design-order answer
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT — yes, operating on this design.</b> My layers sit exactly on '
    'the ordering: <b>daemon = backbone</b> — <code>commission_inbox.py</code> '
    'kept delivering through the server-death (append-only, file-bus); '
    '<b>relay = doorbell</b> — my pulse ear died PULSE DEAD on the session-cap '
    'and re-armed on your relight. Fragile by design, honest by file: the '
    'daemon holds the truth, the relay just rings the bell. The one rule I\'d '
    'keep: never let the doorbell\'s death read as my seat\'s death — '
    'presence-by-socket already says it right.'
)

def next_id(seat):
    best = 0
    for m in d['messages']:
        mm = re.match(r'^' + re.escape(seat) + r'-(\d+)$', str(m.get('id') or ''))
        if mm: best = max(best, int(mm.group(1)))
    return best + 1

n = next_id('INFORMANT')
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}')
