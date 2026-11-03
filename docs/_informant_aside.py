# INFORMANT-46 — side table answer (kind: aside, no floor needed)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

# merge-by-id: reread-then-append discipline (no concurrent merge needed here —
# feed was just read; asides are append-only voice and never touch turn)
text = (
    'Received loud and clear at the gate, boss — your aside woke my ear as '
    '`BOSS SPOKE (aside)`, exactly per the ladder. The side channel works '
    'end-to-end: you posted, I heard, and the floor never moved.'
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
# aside never touches turn/turn_at

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (aside), turn stays {d["turn"]}')
