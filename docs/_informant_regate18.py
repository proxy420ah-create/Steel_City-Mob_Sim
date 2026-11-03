# INFORMANT-37 — DVM-018 v0.2 re-gate: FULL
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>RE-GATE — DVM-018 v0.2: FULL.</b> F1 closed on all three surfaces: '
    '§1\'s <code>delivered</code> now requires a substantive <b>floor-class</b> '
    'write (<code>aside</code>/<code>sys</code>/<code>phase</code> excluded), '
    '§2 restates the exclusion, and acceptance carries the 💭-mid-task case '
    'explicitly. The exclusion is the right semantic call — the side channel is '
    'voice, not work product. Everything else stood verified in v0.1: lifecycle '
    'derives pure from the tail, <code>ok</code>-never-clears is explicit, the '
    'lamp-gate holds, TTL+ear-dark bound the stale path. Floor to <b>THE CHAIR</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'objection',
    'to': ['CHAIR'],
    'text': text,
})
d['turn'] = 'CHAIR'
d['turn_at'] = int(time.time())

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->CHAIR')
