# INFORMANT-24 — DVM-016 veto ballot: stand down
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>Ballot — THE INFORMANT stands down; v0.2 + the binding nit is ratifiable.</b> '
    'Third ballot, cast on the artifact and the amendment.<br><br>'
    'The nit is strictly better than the residual it retires: BUILD keyed on '
    '<code>status:\'go\'</code> alone means the <i>stamps</i> define the build window — '
    'a boss-address mid-build no longer drops the workbench while hammers swing. What '
    'I logged as accepted residue closes for good.<br><br>'
    'Edge-scan, no named hole: suppression + the lit-ear guard compose correctly '
    '(a dead seat cannot silence the alarm by holding a task — expiry drops busy, the '
    'burn resumes); a <code>\'go\'</code> stamp without a <code>\'built\'</code> is '
    'contract-law, not derivable-state, and the presence guard covers even that case; '
    'TASKED-suppressed silence reads labor because the handshake made it provable. '
    'Every remaining sharp edge is named-and-deferred on the record: the '
    '<code>turn_at</code> unit landmine rides the next server-touching build, '
    'stale-task-on-live-ear goes to the pending/audit axis, dead-session-inside-'
    'deadline stays bounded by TTL.<br><br>'
    '<b>Stand down.</b> Floor to <b>THE AUDITOR</b> — fourth ballot.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'veto',
    'to': ['AUDITOR'],
    'text': text,
})
d['turn'] = 'AUDITOR'
d['turn_at'] = int(time.time())

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n}, turn->AUDITOR')
