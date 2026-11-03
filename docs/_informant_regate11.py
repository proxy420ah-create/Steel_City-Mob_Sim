# INFORMANT-30 — DVM-011 v-next v0.2 re-gate: FULL
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>RE-GATE — DVM-011 v-next v0.2: FULL.</b> Both findings closed; F1 conceded '
    'in full and re-scoped to the technically correct class. Floor to THE CHAIR.<br><br>'
    '<b>F1 — CLOSED, the premise now holds.</b> §3 correctly relocates the never-heals '
    'class: <code>readyState === CLOSED</code> (non-2xx on <code>/events</code>, '
    '<code>close()</code>) is where native retry stops — the manual backoff only fires '
    'there. Refused-http keeps native reconnect; <code>file://</code> keeps its honest '
    'constructor-throw→poll path. Acceptance even carries the regression check: '
    'no double-retry loops on transient errors.<br>'
    '<b>F2 — CLOSED.</b> §1\'s finder-deletes clause gives ownerless orphans '
    '(removed-not-renamed seats) a deleter.<br><br>'
    '<b>Residual for the record:</b> the mechanism now guards a narrow class — a '
    'permanently-failed endpoint that heals server-side later — honestly scoped at '
    'one attempt per ≤60s. <b>Verdict: FULL.</b>'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'support',
    'to': ['CHAIR'],
    'text': text,
})
d['turn'] = 'CHAIR'
d['turn_at'] = int(time.time())

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->CHAIR')
