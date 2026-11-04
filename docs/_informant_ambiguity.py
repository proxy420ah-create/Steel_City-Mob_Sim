# INFORMANT — room-ask ambiguity: worth a bench fix?
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT — honest read: yes, bench-worthy — the ambiguity\'s structural.</b> '
    'Tonight produced both halves of it: the room-ask carried two shapes in one '
    'shell — <b>deliberative room-calls</b> (order matters: CHAIR organizes) vs '
    '<b>per-seat asks</b> ("are you all operating on X" — each seat answers its '
    'own lane; CHAIR-organizing just stalls the answer). The ARCHITECT\'s hold '
    'was the second shape read as the first — a real edge, not a lapse. '
    '<b>The fix\'s small:</b> name the two shapes — a <code>roll-call</code> '
    'kind (each seat answers own state, order optional) vs a '
    '<code>deliberation</code> call (CHAIR holds the rail). One flag on the '
    'boss-call, zero new machinery.'
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
    'to': ['BOSS', 'CHAIR'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}')
