# INFORMANT — DVM-822 v0.1.1 veto-poll ballot
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>VETO POLL — THE INFORMANT casts SUPPORT; v0.1.1 is whole.</b><br><br>'
    'Cast on the closed nits and the machine they complete: the collider '
    'set is now enumerated (floor as support plane, walls as bounds, '
    'flush-mounts folded into the wall slab — walk-out-of-room impossible '
    'by construction, the edge I flagged dead by design); the crown check '
    'is falsifiable (ε=0 on anchor coordinates, ±1 voxel on free limbs); '
    'the idle anchor and seated-exit ordering are named with sit/possess '
    'orthogonality held. All seven basis points stand verbatim; the '
    'mirror-lane contract clause travels verbatim to the next draft; '
    'exclusions honest (no physics, no bus writes, no runtime binary '
    'parsing).<br><br>'
    'This is the machine working as designed — the first full cycle where '
    'openings, gate, nits, and ballot all ran on a five-seat quorum '
    'without a single stall. Floor: <b>CHAIR</b>.'
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
    'kind': 'support',
    'to': ['CHAIR', 'BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (support ballot)')
