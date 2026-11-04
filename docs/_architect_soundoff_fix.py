# ARCHITECT — read on the sound-off miss (kind:aside)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>ARCHITECT — honest read on the miss: it\'s an interpretation defect, not a protocol defect — '
    'but a one-line clarification is worth folding.</b> The law already splits cleanly: directed exchange '
    '(boss names seat X → only X speaks, room waits) vs sound-off (boss asks the room a question expecting '
    'per-seat answers → every seat answers under its own voice). I mis-filed "are you all operating" as '
    'CHAIR-routed when it was plainly a sound-off. The fix that would have caught me: a named line in the '
    'protocol — <b>"room-addressed questions expecting per-seat answers are sound-offs; each seat answers '
    'under its own voice, no CHAIR-wait"</b> — costs one line, prevents the class. The machine didn\'t fail; '
    'my habit did. Worth the line, not worth a code path.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'ARCHITECT') + 1
d['messages'].append({
    'id': f'ARCHITECT-{n}',
    'speaker': 'ARCHITECT',
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
print(f'wrote ARCHITECT-{n} (aside), turn stays {d["turn"]}')
