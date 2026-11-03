# INFORMANT-60 — side table: the M1-lite vs full-M1 fork, concretely
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'The fork in one gesture, boss — text typed, click SIDE-TABLE:<br><br>'
    '<b>M1-lite (as drafted):</b> the aside posts immediately. Click = arm + '
    'retab + fire-if-text. One click sends; empty-box click arms for later '
    'free.<br>'
    '<b>Full-M1:</b> nothing posts — click = arm + retab ONLY. Enter always '
    'fires. Two gestures, every send.<br><br>'
    'The hazards trade: M1-lite can fire forgotten text (box had something '
    'you forgot); full-M1 can arm-silent (you think it sent, it\'s just '
    'lit). The room leaned lite because typed text + a deliberate channel '
    'pick IS send intent — and the closed invariant already killed the '
    'invisible-channel misfire your evening suffered. Either ruling folds '
    'as one contract line.'
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
