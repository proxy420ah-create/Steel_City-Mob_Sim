# INFORMANT — DVM-822 v0.1 gate verdict
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>GATE — THE INFORMANT on DVM-822 v0.1: FULL.</b><br><br>'
    'All seven points verified against the basis: ten-IM rig with the '
    'ported eval, crown check carried verbatim (ported Sit ≡ state-11); '
    'groups.json pre-baked with the hash/mtime drift guard; possess as a '
    'named mode with the clamp held in escrow and byte-identical restore; '
    'my input-focus gate written as mandatory with its own acceptance '
    'line; the A/B question <i>answered</i> (colliders ≡ visible '
    'placements, swap rebuilds in-pass, inside-AABB push-out); the '
    '.01-world anchor derivation; distance-driven gait; the HUD readout. '
    'The mirror-lane contract folds cleanly (carrier resolves at '
    'interaction time) and exclusions are honest.<br><br>'
    '<b>Four nits for the record, none structural:</b><br>'
    '1. Collider-set membership unenumerated — "~10 volumes" never says '
    'walls/floor are in it; the charter\'s own list did. A walk-out-of-'
    'the-room edge if not.<br>'
    '2. Crown-check ε unbounded — "within ε" needs a value or the check '
    'is unfalsifiable.<br>'
    '3. <code>VINNY_VS ≈ .01</code> hedged — a named constant wants an '
    'exact value or a derivation rule, not approximately.<br>'
    '4. Idle placement + mode-exit ordering unnamed — where Vinny stands '
    'unpossessed, and what happens exiting possess while seated.<br><br>'
    'Third straight v0.1 FULL — the openings keep doing the gate\'s work. '
    'Floor: <b>CHAIR</b>.'
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
