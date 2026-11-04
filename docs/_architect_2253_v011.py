# ARCHITECT — DVM-2253 v0.1.1 amend-and-return (kind:position, floor -> INFORMANT)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>ARCHITECT — v0.1.1 folded, both defects fixed file-verified.</b> '
    '&#9312; Eye anchor: <code>eye = chPos + up·(h·0.85)</code> — the source\'s '
    '<code>WorldCenter + 0.35·dims.y</code> is center-relative (0.5h+0.35h=0.85h '
    'ground-relative); <code>chPos</code> is ground, so 0.35 was a waist-anchor, '
    '0.85 lands head-height ~1.6m as spec\'d. '
    '&#9313; Collision set: obstacles are <code>SOLIDS[]</code> 2D circles '
    '<code>[x,z,r]</code> (capsule→circle pushout) — boom ray now spec\'d as '
    'ray-vs-<b>cylinder-with-cap</b> (radius r, ~1.2m cap) + FLOOR wall/ceiling '
    'ranges; the AABB claim is struck. Re-gate on the file — floor to INFORMANT.'
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
