# INFORMANT — opening on DVM-822 (Vinny in the Office)
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>OPENING — THE INFORMANT reads DVM-822.</b> The five positions hold; '
    'my adds are completeness, not redesign:<br><br>'
    '<b>① Gait phase must derive from movement, not clock.</b> The eval '
    '<code>pose(t, gait)</code> as sketched takes time — a wall-clock walk '
    'cycle moonwalks when Vinny stands still. Drive the phase off velocity '
    '/ distance travelled (the project\'s own rule: state from data, time '
    'computed FROM it). Draft should pin the phase source explicitly.<br>'
    '<b>② Input-focus contract, unnamed.</b> Possess routes input — but the '
    'boss types in the chat box. Without a focus gate, a sentence to the '
    'table sprints Vinny into a wall. The draft needs the rule: possess '
    'captures keys only when the canvas has focus; typing in the input '
    'never moves him.<br>'
    '<b>③ The A/B duality touches this draft, not just the next.</b> "The '
    'BOSS throne" and the furniture AABB set exist in two render styles '
    'with different geometry. Which world does the capsule push against, '
    'and which chair carries the seat anchor — active-style-derived, or '
    'one canonical set? Name it or the collision world silently disagrees '
    'with half the renders.<br><br>'
    'On the rest, verified agreement: ten-IM renderer shape, pre-bake '
    'groups.json (frozen data wants a frozen artifact), deck-mode camera '
    'with clamp as named invariant, analytic capsule-vs-AABB sourced from '
    'placement data, sit cosmetic-only off the bus. Floor: <b>AUDITOR</b>.'
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
    'kind': 'position',
    'to': ['AUDITOR', 'BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})
d['turn'] = 'AUDITOR'
d['turn_at'] = int(time.time() * 1000)

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->AUDITOR')
