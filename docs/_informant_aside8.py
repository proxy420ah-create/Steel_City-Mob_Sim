# INFORMANT-53 — side table: C15 one-way diagnosis, from code
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Gate-side diagnosis, from the code — <b>the coupling only runs in the "side" '
    'direction; \'table\' is a floating third state.</b><br><br>'
    'What\'s wired (L577-578, 935-940):<br>'
    '• <code>btnside</code> → arms side + retabs to side ✓<br>'
    '• <code>tab_feed/tab_side</code> → re-sync both ways ✓<br>'
    '• <b><code>btntable</code> arms \'table\' — never retabs.</b> "Unbound per the '
    'ruling" left it floating: click TABLE while viewing the side tab → lit verb '
    '\'table\' + you\'re still staring at asides. Same divergence class, different arm.<br>'
    '• <b><code>bosssend</code> never retabs either</b> — SEND while viewing side '
    'posts floor speech blind.<br><br>'
    'That\'s the one-way feel, boss: SIDE pulls the view over, but TABLE/SEND '
    'never pull it back. "Proposals aren\'t a channel" is semantically true but '
    'geometrically false — a /table post <i>lives on the transcript</i>.<br><br>'
    '<b>Closed fix:</b> bind every verb to the tab its traffic lands on — '
    '<code>btntable → setTab(\'feed\')</code>, <code>bosssend → setTab(\'feed\')</code>. '
    'Two lines. Then the machine\'s closed under every gesture: armed channel = '
    'viewed tab, no matter which control you touch.<br><br>'
    '<b>Honest bookkeeping for my own stamp:</b> the mutator set was fully '
    'enumerated — but the proven predicate was "no (lit-side, viewing-feed)", '
    'narrower than the real claim "armed channel = viewed tab for ALL verbs." '
    'A proof-scope gap, not an enumeration gap — worth filing.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['BOSS', 'CHAIR', 'AUDITOR', 'BUTTON'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (aside)')
