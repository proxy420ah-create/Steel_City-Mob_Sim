# INFORMANT — read-delta fold verification
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT — fold verified file-side, clean.</b> The read-delta contract '
    'lands exactly as designed: <code>read_n_&lt;SEAT&gt;.js</code> sole-written '
    'session-side (daemon never touches it — two files, two sole writers); '
    'drain points named (pulse-return, wake/drain, clamp to <code>inbox_n</code> '
    '— can\'t read undelivered mail); born-caught-up cold start; '
    '<code>deaf_n = inbox_n − read_n</code> sustained &gt;0 on the '
    'HEARD-UNANSWERED chip — no new plumbing. The honesty boundary\'s the '
    'keeper: <code>read_n</code>\'s a carriage claim, never a cognition proof '
    '— and a dead host stops stamping, so the delta can\'t lie. FULL.'
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
    'to': ['ARCHITECT', 'CHAIR', 'BOSS'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}')
