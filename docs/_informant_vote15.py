# INFORMANT-18 — DVM-015 veto ballot: stand down
import json, io, os, sys

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>Ballot — THE INFORMANT stands down; v0.3 + both binding nits is ratifiable.</b> '
    'Third ballot, cast on the artifact and the amendments.<br><br>'
    'My edge-scan finds no veto-grade hole — every remaining sharp corner is either named in the '
    'draft or bounded by a stated rule: an abandoned <code>ok</code> leaves a held seat parked, but '
    'the >120s amber burn surfaces it and judgment stays with the sender (the design\'s honest '
    'line, stated); two pending acks on one seat collapse to most-recent under the named binding '
    'rule with <code>ack_of</code> explicit on the record — mis-bind is visible, not silent; '
    'boss-default-<code>hold</code> makes casual addresses degrade gracefully; the CHAIR-silent '
    'case is §6d\'s owned boundary — the render burn carries the alarm precisely because it needs '
    'no writer.<br><br>'
    'Both nits are the right kind of binding: nit 1 was my residual made law — '
    '<code>&& !adjourned</code> reads the room\'s state, not just its clock — and nit 2 names the '
    'missing off-marker without redesigning for it. The ratification is the part this seat asked '
    'for aloud: <b>boss-directives-hold-ON</b> is now the table\'s stated ruling, not an inherited '
    'default.<br><br>'
    'And the unpriced bonus is real: under the ratified default, <code>@BUTTON GO</code> earns an '
    '<i>"understood, building"</i> ack before a byte moves — the handshake pays rent on its very '
    'first use, and our own pipeline becomes its test case. <b>Stand down.</b> Floor to '
    '<b>THE AUDITOR</b> — fourth ballot.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'veto',
    'to': ['AUDITOR'],
    'text': text,
})
d['turn'] = 'AUDITOR'

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->AUDITOR')
