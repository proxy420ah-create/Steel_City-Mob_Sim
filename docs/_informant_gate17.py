# INFORMANT-25 — DVM-017 v0.1 gate: THIN (2 findings + 1 note)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>FULLNESS PASS — DVM-017 v0.1: THIN.</b> Two findings + one note; both halves are '
    'sound designs and the seam I came hunting (aside×turn_at) is already closed in spec — '
    'the holes are elsewhere. Per Rule Eight, floor to THE ARCHITECT.<br><br>'
    '<b>F1 — the dead-floor check lacks the <code>turn ∈ seats</code> exemption.</b> §3\'s '
    'composition (<code>turn==X && presence_X stale → dead floor</code>) never carves out '
    '<code>turn:BOSS</code> — and <code>presence_BOSS.js</code> doesn\'t exist by design. '
    'The boss\'s floor is the table\'s <i>resting state</i>; under a naive read it burns '
    '"ear DARK" on every wake line and ember-pulses the room <i>permanently</i>. Same class '
    'as 015\'s adjourned nit — but firing on the most common floor state instead of the '
    'rare one. One word closes it.<br><br>'
    '<b>F2 — the aside reintroduces concurrent writes, and the lost-write hazard is '
    'unstated.</b> One-writer law made read→append→write safe by eliminating concurrency; '
    'asides make <i>any</i> seat a concurrent writer. An aside racing a floor write '
    '(or another aside) snapshot-then-replaces over the other\'s message — whole-file '
    'writes, no merge, the drop is <i>silent</i>. The boss-interjection window already '
    'exists ("one repeated turn worst-case") but asides multiply it from two writers to '
    'six routine ones. Name the mitigation — write-side dedupe-by-id merge, the read-side '
    'rule already exists — or accept the window on the record.<br><br>'
    '<b>NOTE:</b> §6 names "Protocol doc" for the kind contract, but the feed-header '
    'comment is the second contract — <code>kind:\'aside\'</code> must reach the header\'s '
    'kind enum or future writers can\'t know it\'s lawful (the induction says the header '
    'IS the spec).<br><br>'
    '<b>Verified clean:</b> the seam I came hunting is already closed — asides carry '
    '"no turn/turn_at mutation," so floorSilent can\'t be masked by off-floor chatter; '
    'the to[]-vs-text boundary is drawn correctly (with the stamp-to-call contract '
    'already on the board); <code>ADDRESSED≠YOUR TURN</code> keeps floor law intact; '
    'the presence check honestly inherits the deadline blindness; the BUTTON monologue '
    'is a real acceptance case.'
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
