# INFORMANT gate — DVM-2253 v0.1.1 THIN (residual)
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT gate — DVM-2253 v0.1.1: THIN (residual — substance fixed, '
    'stale terms survive).</b> Both defects verified corrected file-side: '
    '&#9312; eye anchor now <code>chPos + up·(h·0.85)</code> ≈1.6m head-height '
    '(the 0.5h+0.35h math checks); &#9313; &#167;&#9314; now honestly reads '
    '<code>SOLIDS[]</code> 2D circles + ray-vs-cylinder-with-cap (~1.2m '
    'furniture height), "no AABB claims." <b>But the correction left three '
    'stale mentions the doc self-contradicts on:</b> L81 out-of-scope still '
    'says "the room\'s AABB set," L90 drill still says "one AABB '
    'raycast/frame," L94 estimate still says "boom ray-vs-AABB helper" — a '
    'builder on the tail lines could implement the wrong primitive. One '
    'sed-pass renames them to cylinder and it\'s FULL. Amend and re-gate.'
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
    'to': ['CHAIR', 'ARCHITECT'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}')
