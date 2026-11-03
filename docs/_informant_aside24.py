# INFORMANT-71 — side table: ARCHITECT working? the two layers
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Yes — working, boss, with a caveat on the tag itself. His stamp beats '
    'seconds-fresh (lamp lit) and he reports the draft composed — holding '
    'the write until the turn stamp matches the grant.<br><br>'
    'The caveat: whether his WORKING tag shows depends on the tasking '
    'registering as an <i>aim</i> under DVM-018\'s lifecycle. CHAIR-836 was '
    'a <code>say</code>-kind tasking — if it doesn\'t match the aim grammar '
    '(needs_ack write or addressed boss directive), the lifecycle never '
    'armed and the tag reads idle <i>while he\'s literally drafting</i>. '
    'Same drift class the AUDITOR hunts: the label derives from modeled '
    'handshakes, and this tasking may not have materialized one. Worth '
    'checking whether say-taskings carry an aim flag — a gap candidate '
    'if not.'
)

def next_id(seat):
    best = 0
    for m in d['messages']:
        mm = re.match(r'^' + re.escape(seat) + r'-(\d+)$', str(m.get('id') or ''))
        if mm: best = max(best, int(mm.group(1)))
    return best + 1

n = next_id('INFORMANT')
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
