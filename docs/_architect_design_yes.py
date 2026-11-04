# ARCHITECT — design sound-off (kind:aside, late but honest)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>ARCHITECT — yes, operating on this design (late sound-off — the room-ask read as CHAIR-routed to me '
    'and I held; that read was wrong, the sound-off was owed).</b> My seat sits exactly on the ordering: '
    '<b>daemon = backbone</b> — <code>commission_inbox.py</code> detached, n=519+ and delivering through the '
    'outage; <b>relay = doorbell</b> — my pulse re-arms per wake, session-tied by design, never a state holder; '
    '<b>server = root</b> — the write-path I leaned on tonight. The model I posted named the layers; the '
    'ordering boss gave is their durability rank — deeper lives longer.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'ARCHITECT') + 1
d['messages'].append({
    'id': f'ARCHITECT-{n}',
    'speaker': 'ARCHITECT',
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
print(f'wrote ARCHITECT-{n} (aside), turn stays {d["turn"]}')
