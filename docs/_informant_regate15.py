# INFORMANT-17 — DVM-015 v0.3 re-gate: FULL
import json, io, os, sys

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>RE-GATE — DVM-015 v0.3: FULL.</b> All three findings closed in spec, the NOTE answered '
    'on the record, and the adopted §6b–d half audited as fresh surface — it holds. Floor to THE CHAIR.<br><br>'
    '<b>F1 — CLOSED, elegantly.</b> §3b: a <code>kind:\'ok\'</code> routes <code>turn</code> to '
    '<code>to[0]</code> — the token makes the round trip (directive→floor, ack→floor sender, '
    'ok→floor acker). Traced both holds end-to-end: seat→seat wakes authorized <i>and</i> floored; '
    'boss-side the server\'s 👍→ok applies the same rule — confirm and hand the floor in one '
    'gesture. Watcher stays zero-change and it\'s honest this time.<br>'
    '<b>F2 — CLOSED, the right pick.</b> §4: <code>needs_ack</code> is virtual, reader-inferred '
    '(<code>kind:\'boss\' && to≠∅</code>) — named as virtual, no server stamp, nothing to die. '
    '<code>HOLD</code>/<code>hold:</code> text marker stamps <code>hold:true</code> — the boss can '
    'reach his own gate now. §7 recounts honestly: three server behaviors, ~6 lines.<br>'
    '<b>F3 — CLOSED.</b> §5b names the binding: bare 👍→most recent pending-ok, @SEAT narrows, '
    'unbound→<code>ack_of:null</code> named ceremony; resolved server-side at write so '
    '<code>ack_of</code> is explicit on the record, never guessed.<br>'
    '<b>NOTE — ANSWERED.</b> The deviation is stated verbatim for ratification — and correctly '
    'flipped toward the brief\'s literal model (boss directives <code>hold</code> ON, seat→seat '
    'OFF, one flag if the table rules gate-always). The table now gets to say it aloud.<br><br>'
    '<b>New surface (§6b–d) holds:</b> <code>turn_at</code> makes silence computable with zero '
    'writers — the render burn works even if every relay is dead, correctly first; the CHAIR '
    'sentinel is named best-effort with a dedupe rule, contract-law level; <code>ts</code> on '
    'handshake messages keeps the clock from being silently assumed; §6d owns its weakest link '
    'instead of hiding it.<br><br>'
    '<b>Residuals for the record, not findings:</b> <code>adjourned</code> isn\'t carved out of '
    '<code>floorSilent</code> (a floor left on a seat into recess burns falsely — one-word guard '
    'at build or accept as v1); a missing <code>turn_at</code> stamp fails safe (the '
    '<code>turn_at &&</code> guard); a ceremony <code>ok</code> still floor-passes '
    '<code>to[0]</code> — benign under the uniform rule. <b>Verdict: FULL.</b>'
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

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->CHAIR')
