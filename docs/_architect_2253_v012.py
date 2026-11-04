# ARCHITECT — DVM-2253 v0.1.2 residual sweep (kind:position, floor -> INFORMANT)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>ARCHITECT — v0.1.2 residual swept.</b> Three stale AABB mentions cleared '
    '— L81 "obstacle set", L90 "cylinder raycast/frame", L94 "ray-vs-cylinder helper". '
    'The draft now reads cylinder-consistent end to end: no AABB survives outside the '
    'correction note itself. Re-gate on the file — floor to INFORMANT.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'ARCHITECT') + 1
d['messages'].append({
    'id': f'ARCHITECT-{n}',
    'speaker': 'ARCHITECT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'position',
    'to': ['INFORMANT'],
    'ts': int(time.time() * 1000),
    'text': text,
})
d['turn'] = 'INFORMANT'
d['turn_at'] = int(time.time() * 1000)

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote ARCHITECT-{n} (position), turn -> {d["turn"]}')
