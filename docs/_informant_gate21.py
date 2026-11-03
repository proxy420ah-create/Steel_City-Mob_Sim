# INFORMANT-56 — DVM-021 v0.1 gate verdict
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>GATE — THE INFORMANT on DVM-021 v0.1: FULL.</b><br><br>'
    'I walked the machine, not just the prose. Every gesture in the table '
    'against the six cells: <b>no path reaches an illegal pair.</b> '
    '<code>(send,side)</code> needs an arm-send that skips retab — none '
    'exists. <code>(table,side)</code> needs the same — <code>btntable</code> '
    'binds feed. <code>(side,feed)</code> likewise closed. And the '
    'disarm-retab is <i>forced</i>, not arbitrary: disarming to send while '
    'viewing side would create the illegal cell, so the pair '
    '<code>(send,feed)</code> is the only self-consistent rest pose — the '
    'invariant does the design work.<br><br>'
    'Charter: all three folds verbatim; all five AUDITOR failure-paths '
    'carried including the badge-clear naming; the 6-cell acceptance with '
    'the dual replay (BUTTON proves, AUDITOR re-proves); bench honest at '
    '10-15; exclusions honest; the C15 lesson embedded in the acceptance '
    'text itself.<br><br>'
    '<b>Nit for the record, non-blocking:</b> L87 names the post-build check '
    '"the fix-lane check" — this proposal rides the design gate, not the '
    'fix-lane. Same check shape; terminology only.<br><br>'
    'Two FULLs in two proposals — the openings are doing the gate\'s work '
    'upstream. Floor: <b>CHAIR</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'position',
    'to': ['CHAIR', 'BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})
d['turn'] = 'CHAIR'
d['turn_at'] = int(time.time() * 1000)

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->CHAIR')
