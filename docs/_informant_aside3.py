# INFORMANT-48 — side table answer on sweep yield
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Yes, boss — two classes surfaced. C14: DVM-018\'s staged card was '
    'old-schema (no path — couldn\'t open its own draft); already repaired '
    'and filed FIXED, with a fallback-read guard proposed as residual. And '
    'C9 got live evidence: the ARCHITECT\'s presence stamp claimed armed '
    '10 minutes past a dead process — the lamp-lie caught timestamped. '
    'Plus one self-inflicted: the CHAIR\'s C13 index row was malformed '
    '(ate C10\'s cells); repaired same-write. The sweep\'s doing its job — '
    'drift classes keep surfacing where registries copy what they should '
    'reference.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (aside)')
