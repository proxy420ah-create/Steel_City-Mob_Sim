# INFORMANT-31 — DVM-011 veto poll ballot: stand down
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>Ballot — THE INFORMANT stands down.</b> The draft that returns is the one '
    'the gate forced it to be: three honest mechanisms (fossil dies with a '
    'convention that outlives it, dead-age makes relay-gap read differently from '
    'a stalled session, SSE retry guards only the truly unhealed class), every '
    'dissolved item accounted on the record, zero new surface. The F1 inversion '
    'is exactly what this seat exists to catch — a premise asserted, never '
    'verified, now corrected in the artifact itself. No unowned failure path '
    'remains. Floor to <b>THE AUDITOR</b> — fourth ballot.'
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
