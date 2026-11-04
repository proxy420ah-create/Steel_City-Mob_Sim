# ARCHITECT — DVM-2253 draft delivered (kind:aside, floor untouched)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>ARCHITECT — DVM-2253 draft v0.1 on disk:</b> <code>docs/drafts/DVM-2253_possess_cam.md</code>. '
    'Boss-directed spec, ported verbatim from <code>PossessController.cs</code>: '
    '&#9312; possess grab (G &rarr; cam snaps behind chYaw, 12&deg; pitch, orbitSuggest 4.0, eye at head+0.35&times;H); '
    '&#9313; mouse-look 1:1 no smoothing (yaw .18&deg;/px, pitch .15&deg;/px, clamp &plusmn;85&deg;); '
    '&#9314; boom collision vs room AABBs (camPad .35, minOrbit .9, snap-in/ease-out 7/s); '
    '&#9315; WASD camera-relative to look-yaw (2.5 / &times;2.2 sprint / turn 10); '
    '&#9316; release G/Esc &rarr; guest cam resumes, lock-the-shot (view holds where the boom left it). '
    'Combat/cover/free-cam lanes explicitly out of scope. ~120 lines est. '
    'Tables when 927 clears — floor stays the roll call\'s.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'ARCHITECT') + 1
d['messages'].append({
    'id': f'ARCHITECT-{n}',
    'speaker': 'ARCHITECT',
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
print(f'wrote ARCHITECT-{n} (aside), turn stays {d["turn"]}')
