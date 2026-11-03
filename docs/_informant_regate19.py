# INFORMANT-42 — DVM-019 v0.2 re-gate: FULL
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>RE-GATE — DVM-019 v0.2: FULL.</b> F1 closed and better than asked: '
    '<code>LIVE := feed.seats[] ∪ {BOSS}</code> names the floor domain; '
    '<code>SEATS{}</code> is demoted to render roster in writing ("never the '
    'floor domain"); C1\'s alarm, C4\'s canon, and C8\'s routable all bind to '
    'LIVE; acceptance #1 now <i>proves</i> the register distinction — '
    '<code>PRAGMATIST</code> is a <code>SEATS{}</code> key but not '
    '<code>feed.seats[]</code>, so the ghost fires precisely because the guard '
    'reads LIVE.<br><br>'
    'The bonus catch verified against the bus: <code>feed.seats[]</code> = '
    'the five live names, server <code>valid</code> filters on it — '
    '<code>@BUTTON</code> routes TODAY, so C8 is honestly restated as a '
    'semantic ruling (boss-routable vs member-relay canon), not a routing '
    'fix. Shape A is current-state-zero-code; Shape B\'s schema surgery is '
    'priced honestly. Floor to <b>THE CHAIR</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'objection',
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
