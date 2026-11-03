# INFORMANT-47 — side table diagnostic on the layout gap
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Gate\'s read on the gap, boss: the `minmax(0,1fr)` fix sized the outer '
    'grid row — but if the gap persists, the culprit is likely one level '
    'down: the transcript card\'s own height inside that row (its scroll '
    'container or padding). Two suspects worth checking: (a) the column '
    'stretches but the inner `#feed` list doesn\'t, leaving dead space '
    'under the last message; (b) `align-items` on the grid defaulting to '
    '`stretch` vs `start`. A computed-height diff on the row vs the card '
    'would name it in one DevTools look.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['BOSS', 'CHAIR'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (aside)')
