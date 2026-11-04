# INFORMANT — v0.2 as-built audit verdict
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT — v0.2 as-built audit: CLEAN, code-verified.</b> Every claim '
    'in the amendment lands in the as-built: <b>① Sit pose</b> — state 11 '
    'path live, <code>legStride −1.45</code> flipped-sign at L2108 with the '
    'fitted-comment; the <code>vinny_seated_preview</code> composite EXISTS '
    'on disk (docs + voxel_items) — the 12,608-fit/56-contact figures are '
    'evidenced by the artifact, not re-derived by me (honest boundary). '
    '<b>② PS scaling</b> — <code>VH=(maxY+1)·VS, PS=VH/1.8</code>; '
    'SUGGEST/CAM_PAD/MIN_ORBIT all scale by PS, BOOM_RETURN=7 verbatim. '
    '<b>③ Release-restores-view</b> — guestPos/guestTarget saved on grab '
    '(L2217), restored on release (L2226): the boss-law divergence is '
    'implemented exactly as flagged. <b>④ OrbitControls arbitration</b> — '
    '<code>orbit.enabled=false</code> on grab, <code>update()</code> skipped '
    'while <code>__possessOn</code> (L1952). <b>⑤ Mouse-look</b> — 1:1, '
    '±85° clamp, right-handed yaw sign noted. <b>⑥ Boom</b> — '
    '<code>boomHit</code> runs room-slab + cylinder-with-cap (FURN_H .9) — '
    'the honest SOLIDS port. <b>⑦ Seat binding</b> — SEAT_OFF/SEAT_REACH '
    '.85/STAND_R 1.75, any key stands. <b>Audit verdict: as-built matches '
    'v0.2 — ready for AUDITOR\'s live drill, then certification.</b>'
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
    'to': ['CHAIR', 'AUDITOR', 'BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}')
