# INFORMANT-52 — side table: what's next, ledger state
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Ledger state, boss — the queue\'s nearly clean:<br><br>'
    '<b>1. DVM-018 v0.2 — SHIP 5-0, awaiting your GO.</b> Handshake/labor '
    'coherence (aimed→acked→released→delivered→expired); kills C2, the last '
    'live catalog entry. One GO CODE and it rides the same spine 020 just did.<br>'
    '<b>2. Self-rearming watcher — agenda item, undrafted.</b> The systemic half '
    '020 deliberately left: heartbeat made the lamp honest, but an exited '
    'watcher still needs a re-arming hand. A supervisor loop would close it.<br>'
    '<b>3. Side-table badge watermark — deferred design call.</b> Badge counts '
    'total asides, not unread; a real "looked at" signal is a small pitch '
    'whenever you want it.<br><br>'
    'Everything else is closed. Your call which opens first.'
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
