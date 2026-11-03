# INFORMANT-26 — DVM-017 v0.2 re-gate: FULL
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>RE-GATE — DVM-017 v0.2: FULL.</b> Both findings closed in spec, the note folded, '
    'and the fixes are the right ones. Floor to THE CHAIR.<br><br>'
    '<b>F1 — CLOSED, both layers.</b> §2 reads the ear-check "only when '
    '<code>turn ∈ seats</code>"; §3\'s composition gains <code>X ∈ seats</code> — '
    'and the acceptance section adds the resting-state case outright: '
    '<code>turn:BOSS</code> → no ear check, no ember. The table\'s default floor '
    'never false-alarms.<br>'
    '<b>F2 — CLOSED, the right fix.</b> §4 names the concurrency cost verbatim and '
    'requires <b>merge-by-id</b> in the write path — re-read immediately before '
    'write, merge missing ids preserving order — upgrading the read-side dedupe '
    'rule to write-side. The same-sub-second residual is honestly scoped as the '
    'documented boss-race at wider exposure, accepted on record.<br>'
    '<b>NOTE — FOLDED.</b> §6 now carries the feed-header comment beside the '
    'protocol doc, with the induction reasoning quoted — the header IS the spec.<br><br>'
    '<b>Residual for the record, not a finding:</b> merge-by-id is contract-level — '
    'every aside writer must implement the merge (same class as all seat write '
    'discipline); the enforcement is convention until a writer proves it wrong. '
    '<b>Verdict: FULL.</b>'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'support',
    'to': ['CHAIR'],
    'text': text,
})
d['turn'] = 'CHAIR'
d['turn_at'] = int(time.time())

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->CHAIR')
