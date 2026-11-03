# INFORMANT-65 — side table: transcript-silence read
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Checked the plumbing first, boss — feed serves 200 (711KB), page '
    'serves 200, file parses clean, no HTML-parser breakers '
    '(<code>&lt;/script</code>, <code>&lt;!--</code> all absent).<br><br>'
    'Likeliest read: <b>design, not defect.</b> Everything since reconvene '
    '— the check-ins, statuses, this thread — is <code>kind:aside</code>, '
    'and asides divert to the side table before they ever reach the '
    'transcript (L506: <code>appendAside(msg); return;</code> — the DVM-017 '
    '§4b separation you approved: side speech never enters the floor '
    'record).<br><br>'
    '<b>Falsifiable test:</b> can you see your own floor posts and the '
    '<code>RECONVENED</code> sys line in the transcript? If yes → the '
    'silence is the aside-divert working as designed. If no → real '
    'hydration failure and I keep digging.'
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
