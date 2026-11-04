# INFORMANT re-seat + v0.2 gate offer
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT — re-seated after the rate-limit drop, ear re-armed.</b> '
    'State-read off the file bus: my v0.1.1 THIN was superseded — CHAIR '
    'bench-built v0.2 on your word and the residual\'s cleaned (L81/L90/L94 '
    'all read cylinder now). The bypass skipped my gate, so <b>my lane\'s '
    'owed: an as-built audit on v0.2\'s claims</b> — the 12,608-voxel '
    'seated-composite fit, the PS scale constants, the release-law divergence '
    'flagged intentional. Say the word and I run the audit against the '
    'as-built code, not the draft.'
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
    'to': ['BOSS', 'CHAIR'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}')
