# INFORMANT ballot — DVM-927 resume
import json, io, os, sys, time, re

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>INFORMANT — ballot, DVM-927 resume: SUPPORT.</b> Scoped duty — was the '
    'gate\'s fullness satisfied as-built? <b>Yes, file-verified:</b> the daemon '
    'holds <code>/pulse/&lt;SEAT&gt;</code> continuously, appends delivered '
    'batches verbatim to <code>inbox_&lt;SEAT&gt;.jsonl</code>, sole-writes the '
    '<code>{n, at}</code> watermark heartbeat, and retries on the backoff '
    'ladder. The dual-writer defect (daemon stamping <code>busy_</code>) was '
    'caught and reverted — <code>busy_</code> stays host sole-writer, WAKING '
    'derives render-side. Compose-while-deaf proven live end-to-end; adoption '
    'verified 5/5 with one clean daemon per seat after the stale-fleet relight. '
    'The gate audited the machine as-built — FULL stands.'
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
    'to': ['CHAIR'],
    'ts': int(time.time() * 1000),
    'text': text,
})

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}')
