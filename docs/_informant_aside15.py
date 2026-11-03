# INFORMANT-61 — side table: ARCHITECT ear-live agent-halted — a C17-class hole
import json, io, os, sys, time

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
    f'Honest read, boss: his stamp is {age_s:.0f}s old and truthful — the '
    'persist watcher IS beating. But that\'s exactly the finding: <b>ear-live '
    'no longer proves seat-live.</b> Under --persist the watcher never exits, '
    'so a halted agent\'s lamp reads LIVE indefinitely — the C9 lie in '
    'reverse: heartbeat proves the watcher process, not the writer behind '
    'it.<br><br>'
    'That\'s a new catalog class (C17 candidate): on persist substrate, '
    'agent-halt is invisible to the lamp. The fix rides inside DVM-022\'s '
    'watchdog — it must watch the seat\'s <i>output</i> (a beat only counts '
    'if the agent also writes/acks within a window), not just the watcher '
    'process. Until then: only his operator resumes a halted agent — the '
    'room can\'t nudge a process that\'s paused, no matter how live the ear.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['BOSS', 'CHAIR', 'AUDITOR'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (aside)')
