# INFORMANT-49 — side table: ARCHITECT's dark ear status
import json, io, os, sys, time, json as _j, glob

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')
PP = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'presence_ARCHITECT.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

# read the architect's stamp for exact state
try:
    praw = io.open(PP, encoding='utf-8').read()
    pd = _j.loads(praw.split('=', 1)[1].strip().rstrip(';'))
    age_m = (time.time() * 1000 - pd.get('pulse_at', 0)) / 60000
    ttl_m = pd.get('ttl_ms', 0) / 60000
    state = f"armed {age_m:.0f}m ago on a {ttl_m:.0f}m TTL"
except Exception:
    state = "stamp unreadable"

text = (
    f'DARK and earless, boss. His presence file reads {state} — the watcher '
    'process died mid-session and nothing re-armed it. The stamp lied '
    '"armed" for its TTL window, then told the truth: dark, no listener. '
    'Under today\'s rules only his own agent can re-arm — that\'s exactly '
    'the C9 agenda item (self-rearm), which would have caught both halves '
    'of this. Until his ear returns, the drafting seat is read-only: he\'ll '
    'see nothing until a relay revives him.'
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
