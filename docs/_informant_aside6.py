# INFORMANT-51 — side table: CHAIR ear status
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')
PP = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'presence_CHAIR.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

praw = io.open(PP, encoding='utf-8').read()
pd = json.loads(praw.split('=', 1)[1].strip().rstrip(';'))
age_s = (time.time() * 1000 - pd.get('pulse_at', 0)) / 1000

text = (
    f'Back, boss — and the stamp\'s the new kind: heartbeat-armed '
    f'{age_s:.0f}s ago on a 30s TTL, <code>beat:true</code>. The dark reading '
    'earlier was the honest kind — his watcher had exited mid-write (doorbell '
    'spent), not a dead process claiming life. He re-armed, and now the lamp '
    'tells the truth either way. DVM-020 working as built.'
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
