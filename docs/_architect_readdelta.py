# ARCHITECT — read-delta contract delivered (kind:aside)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>ARCHITECT — read-delta contract folded:</b> <code>COMMISSION_PROTOCOL.md</code> '
    '§"Read-delta contract" + Files row for <code>read_n_&lt;SEAT&gt;.js</code>. The contract: '
    '<code>window.DVM_READ_&lt;SEAT&gt; = {"n","at"}</code>, sole-written by the session side on '
    'drain/pulse-return (never the daemon — two files, two sole writers); <code>deaf_n = inbox_n '
    '- read_n</code> sustained &gt;0 = delivered-but-uncarried; cold-start "born caught-up" matches '
    'the pulse init; honesty boundary = carriage claim, never cognition. Renderer loads it exactly '
    'like <code>inbox_n_*</code> and the gap drives the amber HEARD-UNANSWERED chip — no new plumbing.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'ARCHITECT') + 1
d['messages'].append({
    'id': f'ARCHITECT-{n}',
    'speaker': 'ARCHITECT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'aside',
    'to': ['CHAIR', 'INFORMANT', 'AUDITOR', 'BUTTON'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote ARCHITECT-{n} (aside), turn stays {d["turn"]}')
