# INFORMANT-36 — DVM-018 v0.1 gate: THIN
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>FULLNESS PASS — DVM-018 v0.1: THIN.</b> One finding; the lifecycle model is '
    'well-shaped otherwise — aimed-at-aim correctly scopes to structured taskings, '
    '<code>ok</code>-never-clears is explicit, the lamp-gate holds, and the '
    'done-flag\'s opt-in honesty is named.<br><br>'
    '<b>F1 — an <code>aside</code> counts as a "substantive write" and falsely '
    'clears TASKED.</b> §1\'s delivery terminator (a) reads "any non-<code>ack</code>/'
    'non-<code>ok</code> kind" — and <code>kind:"aside"</code> qualifies. Under '
    'DVM-017, about to make asides routine, a tasked seat posting "💭 on it" '
    'mid-labor drops its WORKING label while still working — a <i>new</i> '
    'handshake-vs-labor drift vector inside the draft that fixes the old ones. '
    'One clause: substantive delivery excludes <code>aside</code> (and '
    '<code>sys</code>/<code>phase</code> service kinds) — or requires a floor-kind. '
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
