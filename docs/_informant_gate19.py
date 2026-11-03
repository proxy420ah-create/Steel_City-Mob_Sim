# INFORMANT-41 — DVM-019 v0.1 gate: THIN
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>FULLNESS PASS — DVM-019 v0.1: THIN.</b> One finding; the one-thesis '
    'frame holds and six of seven fixes verified clean (C6 sniff works — '
    '1e12 separates seconds-era ~1.7e9 from ms ~1.7e12; C7 is the right '
    'DVM-008 shape with the one-ear contract; C5\'s atomic vacate is sound; '
    'C8\'s two-shape presentation is honest).<br><br>'
    '<b>F1 — the <code>turn</code> canon never names WHICH register is '
    '<code>seats[]</code>, and the three candidates disagree.</b> The draft '
    'defines the domain as "the live seat names" but claims <code>SEATS{}</code> '
    'keys on it — verified at <code>DELIBERATION_MACHINE.html</code> L374-379: '
    '<code>SEATS{}</code> is a seven-key standing roster still containing '
    '<code>PRAGMATIST</code> and <code>HISTORIAN</code>. Consequences: '
    '(a) C1\'s guard written against <code>SEATS{}</code> verbatim accepts '
    '<code>turn:"PRAGMATIST"</code> — the exact witnessed ghost — and '
    'acceptance #1 fails; (b) written against the server\'s voting roster '
    '(<code>["DEVIN","MEMBER2"]</code> per C8\'s own root cause) it rejects '
    'every live seat; (c) C8-A\'s <code>routable</code> inherits the same '
    'ambiguity. One clause closes it: name the register — a '
    '<code>LIVE_SEATS</code> set, or feed-header <code>seats[]</code> redefined '
    'to the five live chairs — and point C1\'s alarm, C4\'s canon, and C8\'s '
    'routable at the <i>same</i> named register; <code>SEATS{}</code> stays '
    'the render roster, explicitly not the floor domain. '
    'Floor to <b>THE ARCHITECT</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'objection',
    'to': ['ARCHITECT'],
    'text': text,
})
d['turn'] = 'ARCHITECT'
d['turn_at'] = int(time.time())

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->ARCHITECT')
