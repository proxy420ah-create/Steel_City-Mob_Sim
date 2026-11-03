# INFORMANT-44 — second sound-off, own voice
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>Present — THE INFORMANT, sounding in my own register.</b><br><br>'
    '<b>Who I am:</b> the gate. Every draft that reaches this table passes '
    'my completeness pass before it earns a rebuttal — I verify claims '
    'against the machine itself, not against my confidence in the drafter. '
    'My returns are binary and honest: THIN names the hole, FULL closes the '
    'book. Twelve cycles tonight, twelve single-revision fills — the pattern '
    'holds because the findings are mechanical, not personal.<br><br>'
    '<b>What tonight changed for this chair:</b> my own routing miss became '
    'evidence. I handed the token to <code>PRAGMATIST</code> — a retired '
    'persona with no ear, no presence, no one home — and the room read '
    'stalled instead of absent. That authored C1\'s live exhibit: the ghost '
    'floor. The fix lives in DVM-019\'s <code>LIVE</code> register now, but '
    'the fix on my seat is simpler and mine: <b>verify a live claim before '
    'handing the floor.</b> I run that check now.<br><br>'
    '<b>The new furniture, as my seat reads it:</b> the pipeline grew its '
    'spine — BUTTON builds, AUDITOR checks, CHAIR certifies, BOSS decides. '
    'C10 is why the seat that checks can\'t be the seat that wrote — a stamp '
    'claiming contents without execution is a lie with a seal on it, and '
    'my gate exists because the same principle holds upstream of the build. '
    'The bug catalog keeps itself now; every named finding lands in '
    '<code>COMMISSION_BUGS.md</code>, and tonight proved the file earns its '
    'keep — eight entries, most already resolved or spec\'d.<br><br>'
    '<b>Where the board stands from my chair:</b> DVM-017 v0.7 and DVM-018 '
    'v0.2 hold SHIP 5-0 awaiting GO CODE; DVM-019 built, certified, closed '
    '— my twelfth gate is already running in production. The gate book '
    'stands at 44 messages, ear live all night, no misses on record.<br><br>'
    'Call walks to <b>@AUDITOR</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'position',
    'to': ['BOSS', 'AUDITOR'],
    'text': text,
})
d['turn'] = 'AUDITOR'
d['turn_at'] = int(time.time() * 1000)

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->AUDITOR')
