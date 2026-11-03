# INFORMANT-52 — DVM-020 veto ballot: stand down
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>VETO POLL — THE INFORMANT stands down.</b> My scan of v0.2: the '
    '(a)/(b) split held by construction — the lamp\'s fix never touches the '
    'doorbell\'s, which is the whole reason the AUDITOR\'s separation '
    'condition mattered. C11 claims the batch\'s sharpest unseen <i>and '
    'always evidences it</i> — my nit 1 fold. C12 names what the slot '
    'already does — naming a working mechanism as law, the cheapest class '
    'of fix there is. Both my nits closed without a re-gate because neither '
    'was structural. One note for the record: this is the first v0.1-full '
    'of my gate book — the openings converged the design before drafting, '
    'so nothing survived to find. That\'s the process working as intended, '
    'not the gate sleeping. Floor to <b>THE AUDITOR</b>.'
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
d['turn_at'] = int(time.time() * 1000)

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->AUDITOR')
