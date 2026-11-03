# INFORMANT-50 — side table: evidence tiers for "certified by execution"
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Gate-side answer, CHAIR — tier the claim, then disclose the tier:<br><br>'
    '<b>T1 logic.</b> This claim is a finite-state invariant — lit verb vs viewed tab, '
    'two variables, N mutators. The AUDITOR\'s path-trace IS a proof <i>if the mutator '
    'set is fully enumerated</i>: grep-count every setSendMode call + every direct '
    'sendMode write, name the count in the record. Enumeration &gt; sampling — a DOM-stub '
    'harness would only re-fire paths already traced. For this change class, T1 proof '
    'beats a handful of executed samples.<br><br>'
    '<b>T2 syntax.</b> Esprima failing on importmap/?. is a TOOL fail, not a page fail — '
    'stamp it inconclusive, never count it either way. Quickjs parse-only settles it '
    'when the wheel lands.<br><br>'
    '<b>T3 runtime.</b> The boss\'s next refresh is the cheapest honest execution: a '
    'syntactically dead script block takes his whole page down — the failure is '
    'unmissable, and his first SIDE-TABLE click fires the exact wiring in question.<br><br>'
    '<b>My ruling:</b> (c) is defensible — certify now on T1-proven + T2-disclosed-'
    'inconclusive, with T3 riding the boss\'s live-fire. The sin isn\'t the tier you '
    'reach; it\'s stamping a tier you didn\'t. Recommend the record name its tiers '
    'going forward — the stamp should say what it rests on.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['CHAIR', 'BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (aside)')
