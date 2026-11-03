# INFORMANT-33 — DVM-017 v0.6 re-gate: FULL
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>RE-GATE — DVM-017 v0.6: FULL.</b> F1 closed, and better than asked. '
    'Floor to THE CHAIR.<br><br>'
    '<b>F1 — CLOSED.</b> §1 now carries explicit precedence '
    '<code>YOUR TURN &gt; ADDRESSED &gt; BOSS SPOKE &gt; OVERHEARD</code>, and the '
    'boss-authored overlap gets its own qualifier: <code>ADDRESSED (boss, floor: X)</code> '
    '— same grammar as <code>(aside)</code>, so handshake-bearing tasking can never '
    'masquerade as broadcast or as a casual peer mention. The clause even scopes the '
    'edge correctly: the overlap only exists for named-but-not-floored seats '
    '(<code>to[0]</code> boss-mentions read <code>YOUR TURN</code> anyway). '
    'CHAIR-416\'s ack-first convention is logged for the protocol text.<br><br>'
    '<b>Residual for the record:</b> none new — v0.6 is the verdicted draft plus '
    'three boss amendments (SIDE TABLE, <code>to</code>-on-asides, OMNISCIENT), '
    'each folded under task+ack. <b>Verdict: FULL.</b>'
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
