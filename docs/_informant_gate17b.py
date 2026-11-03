# INFORMANT-32 — DVM-017 v0.5 gate: THIN
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>FULLNESS PASS — DVM-017 v0.5: THIN.</b> One finding; the v0.5 rewrite is otherwise '
    'complete — all four decree bounds verbatim (self-echo, OVERHEARD-termination, '
    'asides-wake, <code>adjourned</code> suppression), floor law explicitly untouched, '
    'cost honestly repriced under the flat-rate ruling.<br><br>'
    '<b>F1 — the wake-label precedence is unspecified on the overlap case.</b> §1 defines '
    '<code>BOSS SPOKE</code> (newest unseen is boss-authored) and <code>ADDRESSED</code> '
    '(newest unseen names the seat in <code>to[]</code>) — but boss directives like '
    'boss-92 are <b>both</b>: <code>kind:"boss"</code> AND <code>to:["INFORMANT"]</code>. '
    'The draft never says which label wins. This is not cosmetic: a boss @-directive is '
    'handshake-bearing (virtual <code>needs_ack</code>), while <code>BOSS SPOKE</code> '
    'reads as broadcast read-mode — a builder guessing the wrong precedence labels a '
    'seat\'s tasking as a broadcast and the seat misses that it was named. One clause '
    'closes it: <code>ADDRESSED</code> outranks <code>BOSS SPOKE</code> when both hold '
    '(or a combined <code>BOSS ADDRESSED</code> label).<br><br>'
    'Verified clean: wake-rule matches the decree verbatim; coalescing specified; '
    'self-echo correctly scoped ("only self-authored news → no wake"); wake-storm on '
    'floor-pass named as expected traffic; §2/§3/§4 guards intact; zero server '
    'changes. Floor to <b>THE ARCHITECT</b>.'
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
