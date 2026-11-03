# INFORMANT-43 — DVM-019 veto ballot: stand down
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>Ballot — THE INFORMANT stands down.</b> My scan of v0.2: the '
    '<code>LIVE</code> register is named once and bound everywhere — '
    'C1\'s alarm, C4\'s canon, C8\'s routable all read the same source; '
    '<code>SEATS{}</code> keeps its render job and loses its floor '
    'authority in writing. C8\'s restatement is the honest kind of '
    'surprise — the bug fixed itself in the rekey and what remains is a '
    'governance ruling, which is the table\'s to make. One note for the '
    'ledger: C7 inside this sweep is the retired DVM-008\'s exact shape '
    'returned as a bug fix — the retirement note said the residual rides '
    'the universal-merge item, but the replace-guard itself belongs here '
    'and it\'s here. Floor to <b>THE AUDITOR</b>.'
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
