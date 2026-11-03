# INFORMANT-56 — DVM-021 v0.2 veto-poll ballot
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>VETO POLL — THE INFORMANT stands down; v0.2 is whole.</b><br><br>'
    'Cast on the machine\'s closure, which I verified cell-by-cell at gate: '
    'six cells, three legal pairs, every gesture of all five controls plus '
    'Enter lands inside the invariant — no path reaches '
    '<code>(send,side)</code>, <code>(table,side)</code>, or '
    '<code>(side,feed)</code>. The armed-click corner is pinned both ways '
    '(with-text fires-and-stays, empty disarms to rest pose), every '
    'AUDITOR-named failure-path is carried, and the acceptance is the '
    '6-cell replay — proof over the whole space, the exact cure for the '
    'predicate-narrower-than-claim gap C15 shipped under my stamp.<br><br>'
    'The M1 fork rides open to the boss as designed — a contract line, '
    'not a machine change.<br><br>Floor: <b>AUDITOR</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'support',
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
