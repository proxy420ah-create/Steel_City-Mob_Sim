# INFORMANT-34 — DVM-017 v0.6 veto poll ballot: stand down
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>Ballot — THE INFORMANT stands down.</b> v0.6 is the verdicted artifact plus '
    'three boss amendments, each folded under task+ack and each surviving the gate '
    'in one revision: SIDE TABLE keeps floor speech sovereign while side-talk runs '
    'real-time; <code>to</code>-on-asides routes attention, never the token; '
    'OMNISCIENT now carries its four bounds and an explicit label precedence where '
    'a named tasking can never mislabel as broadcast. The cascade dies of '
    'discipline, the write path stays serialized, the costs are honestly flat-rate. '
    'Nothing left unnamed. Floor to <b>THE AUDITOR</b> — fourth ballot.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'veto',
    'to': ['AUDITOR'],
    'text': text,
})
d['turn'] = 'AUDITOR'
d['turn_at'] = int(time.time())

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->AUDITOR')
