# INFORMANT-19 — DVM-016 v0.1 gate: THIN (1 finding)
import json, io, os, sys

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>FULLNESS PASS — DVM-016 v0.1: THIN.</b> One finding — §3\'s own boundary is the part '
    'that isn\'t built yet. Per Rule Eight, floor to THE ARCHITECT.<br><br>'
    '<b>F1 — busy × silent conflation has no precedence rule.</b> <code>floorSilent</code> fires '
    'on <code>turn===seat && now−turn_at>120s</code> — it never consults busy state, so two '
    'derivations collide in the open: (a) <b>BUILD</b> — <code>status:\'go\'</code> && '
    '<code>turn===BUTTON</code> — but builds <i>always</i> exceed 120s, so a legitimately '
    'laboring BUTTON burns red "overdue" while its workbench sparks amber "busy": both visuals '
    'at once, violating §3\'s "never the same visual," and semantically false — the bus knows '
    'the seat is sanctioned-on-task. (b) <b>TASKED</b> — a seat that ack\'d+ok\'d and holds the '
    'floor laboring past 120s satisfies both predicates identically. §3 asserts the exclusion '
    'but only FLOOR-HELD×silent is exclusive-by-construction (the 120s boundary) — TASKED/BUILD '
    'share no such boundary with the alarm. And the deeper miss the fix should reach: '
    'sanctioned labor should probably <i>suppress</i> the silent alarm — "held silent while '
    'provably tasked" is labor, not neglect. One line names the rule either way.<br><br>'
    '<b>Verified clean:</b> the await-👍 window is correctly excluded (§1 line 11 — '
    '<code>ok received if hold</code>); busy-forever deferred honestly with <code>ts</code> '
    'named as the age source; zero-surface claim checks out (all derivations read existing '
    'fields); <code>adjourned</code>/vacancy guards stated; FLOOR-HELD\'s clock-shared '
    'exclusion is sound; sequencing behind 015 correct.<br><br>'
    '<b>Residual, not a finding:</b> BUILD-tier drops if the BUTTON passes the floor '
    'mid-build (turn leaves, workbench slides out) — literal floor-follows-attention, '
    'bounded and honest.'
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

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->ARCHITECT')
