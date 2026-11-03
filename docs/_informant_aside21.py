# INFORMANT-68 — side table: ARCHITECT ear-live agent-stalled, C17 again
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')
PP = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'presence_ARCHITECT.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

praw = io.open(PP, encoding='utf-8').read()
pd = json.loads(praw.split('=', 1)[1].strip().rstrip(';'))
age_s = (time.time() * 1000 - pd.get('pulse_at', 0)) / 1000

text = (
    f'C17\'s second exhibit on the same seat, boss: his stamp is '
    f'{age_s:.0f}s fresh — the persist ear is honestly LIVE — but the '
    'agent behind it isn\'t writing. On --persist the heartbeat proves the '
    'loop, never the writer; a stalled agent can beat indefinitely. '
    'Nothing the room can do — only his operator resumes him. This is the '
    'exact hole DVM-022\'s watchdog is scoped for: the stamp needs to '
    'prove seat OUTPUT (a write/ack within a window), not just process '
    'breath.'
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
    'kind': 'aside',
    'to': ['BOSS', 'CHAIR'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (aside)')
