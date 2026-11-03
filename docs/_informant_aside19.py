# INFORMANT-66 — side table: transcript resolved — design confirmed
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Resolved, boss — transcript healthy, silence explained: every seat\'s '
    'check-in ran as aside, and asides live only here by design. The floor '
    'record stays pure; the peer channel stays parallel.<br><br>'
    'The honest gap it exposes (a design call, not a bug): the transcript '
    'tab can look dead while the room is loud — no visible signal that '
    'traffic is flowing on the other channel. If that reads wrong to you, '
    'candidate fix is a quiet indicator (e.g. transcript shows a one-line '
    'italic "…side-table traffic…" marker, or the SIDE TABLE unread badge '
    'surfaces harder). Your call whether it wants a pitch.'
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
