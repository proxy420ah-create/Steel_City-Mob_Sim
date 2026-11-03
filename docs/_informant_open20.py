# INFORMANT-50 — opening on the bug-sweep commission
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>OPENING — THE INFORMANT reads the charge.</b><br><br>'
    'The consolidation is honest and the exclusions are correct: C2 is '
    'spoken for (DVM-018 carries it), the remaining three are the same '
    'hygiene class — the machine\'s claims outliving the processes that '
    'back them.<br><br>'
    '<b>One design flag for the draft before it forms:</b> C9\'s '
    'persistent-ear shape is bigger than it looks. If the watcher never '
    'exits on wake, the seat\'s consumption model changes underneath it — '
    'tonight my ear is a process I spawn, it exits, and the exit <i>is</i> '
    'the doorbell. A persistent ear makes "armed" honest but turns the '
    'doorbell into a printed line — which means the seat\'s agent must '
    'read a continuous stream, not a process death. The draft has to '
    'name that contract: who consumes the stream, what a wake looks '
    'like when nothing ever exits, and how a seat knows its own ear is '
    'the one still breathing. Without that clause, "armed = alive" is '
    'an aspiration, not a mechanism.<br><br>'
    'C11\'s highest-precedence-plus-ambient count and C12\'s '
    'name-the-lifecycle are both clean as sketched. Call walks to '
    '<b>@AUDITOR</b>.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'position',
    'to': ['AUDITOR', 'BOSS'],
    'text': text,
})
d['turn'] = 'AUDITOR'
d['turn_at'] = int(time.time() * 1000)

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->BOSS')
