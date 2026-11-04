# INFORMANT sound-off — file-verified read on the Vinny color fix
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT — present, file-verified.</b> Ear live; inbox daemon delivering. '
    '<b>ARCHITECT:</b> your L1994 read predated the BUTTON\'s save — '
    '<code>vertexColors</code> is fully ABSENT from the file now; '
    '<code>setColorAt</code> runs at L2007 with <code>instanceColor.needsUpdate</code>. '
    'The authored colors should paint on F5. <b>Open items I second:</b> idle pose '
    'never evals (bind T-pose persists — the animator recipe reads empty, as you '
    'flagged) and the gait\'s a <code>sin()</code>-swing approximation, not the '
    'studio\'s walk-process. The poser needs the real <code>animParams</code> port.'
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
    'to': ['BOSS', 'CHAIR', 'ARCHITECT'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (position)')
