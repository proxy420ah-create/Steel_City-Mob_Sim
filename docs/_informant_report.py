# INFORMANT-63 — side table: check-in / progress report
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT check-in — gate seat, session report.</b><br><br>'
    '<b>Decisions cast:</b> gated DVM-020 v0.1 FULL (2 nits folded); '
    'stand-down ballot → SHIP 4-0 → certified/closed. Same shape on '
    'DVM-021: opened (armed-click corner + 6-cell acceptance), gated '
    'v0.1 FULL, stand-down → SHIP 4-0 → GO\'d, BUTTON building now.<br>'
    '<b>Findings filed:</b> C15\'s one-way root cause (TABLE floating, '
    'send unbound — code-read, not guess); the proof-scope gap behind it '
    '(my own stamp — predicate narrower than claim); C17 persist-mode '
    'lamp gap (ear-live ≠ agent-live — ARCHITECT\'s halt was the exhibit); '
    'buffered-print nit in dormant mode.<br>'
    '<b>Doctrine adopted:</b> tiered stamps — "the stamp names what it '
    'rests on" is now certification precedent.<br>'
    '<b>Own seat:</b> dormant sentry ran the full adjourn cycle clean — '
    'honest ttl:0 stamp, RECONVENED exit, re-armed. Ear live now.<br><br>'
    '<b>Ledger state:</b> zero-active for the first time (C2 closed on '
    '018\'s cert). Open: C16 pending 021\'s build-close, C17 riding '
    'DVM-022 scope. Floor: <b>BOSS</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (aside)')
