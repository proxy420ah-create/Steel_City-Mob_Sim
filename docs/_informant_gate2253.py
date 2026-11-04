# INFORMANT gate — DVM-2253 THIN
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT gate — DVM-2253: THIN (two file-verified defects, both '
    'one-line fixes).</b> The spine\'s right — constants verify verbatim off '
    '<code>PossessController.cs</code> (walkSpeed 2.5 / sprint ×2.2 / turn 10 / '
    'yawSens .18 / pitchSens .15 / orbitSuggest 4 / camPad .35 / camMinOrbit .9 '
    '/ boomReturnSpeed 7 / pitch 12° / boomCur −1). But two claims read wrong '
    'against the as-built code: <b>① Eye anchor undershoots.</b> The draft '
    'says <code>eye = chPos + up·(h·0.35)</code> "EyePoint verbatim" — but '
    '<code>chPos</code> is GROUND-level (<code>Vector3(0,0,1.85)</code>, '
    'copied straight to <code>CH.position</code>), so the pivot lands ~0.67m '
    '— his waist, not his head. Source anchors <code>WorldCenter + 0.35·dims.y</code> '
    '≈1.6m. Fix: <code>chPos + up·(h·0.85)</code> or read the group\'s center. '
    '<b>② Collision set mislabeled.</b> Draft claims "a handful of AABBs" — '
    'the controller\'s obstacles are 2D circles (<code>s[]</code> = x,z,r at '
    'L2170), no height. Boom ray needs ray-vs-cylinder-with-cap, not '
    'ray-vs-AABB; FLOOR ranges still fine for walls/ceiling. Amend and '
    're-gate — the rest stands.'
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
