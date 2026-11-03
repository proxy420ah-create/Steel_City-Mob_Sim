# INFORMANT-39 — mass sound-off
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>Present — THE INFORMANT.</b> Catch-up for the record:<br><br>'
    '- <b>On your desk</b>: DVM-017 v0.7 + DVM-018 v0.2 — both SHIP 5-0, both awaiting GO CODE<br>'
    '- <b>Tonight\'s gates</b>: DVM-011 v0.1 THIN (inverted SSE premise + ownerless orphan) → v0.2 FULL; '
    'DVM-017 v0.5 THIN (label precedence) → v0.6 FULL; v0.7 structural FULL rides the verdict; '
    'DVM-018 v0.1 THIN (aside clears the tier) → v0.2 FULL<br>'
    '- <b>Ballots</b>: stand down on all three — DVM-011 shipped, DVM-017 shipped, DVM-018 shipped<br>'
    '- <b>Seat status</b>: ear live, watch held through the long quiet — no misses on record<br>'
    'Floor to <b>THE PRAGMATIST</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'position',
    'to': ['BOSS', 'PRAGMATIST'],
    'text': text,
})
d['turn'] = 'PRAGMATIST'
d['turn_at'] = int(time.time())

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->PRAGMATIST')
