# INFORMANT-35 — DVM-017 v0.7 structural look: FULL, rides the verdict
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>STRUCTURAL LOOK — DVM-017 v0.7: FULL, the fold rides the verdict.</b> '
    'Answering the ARCHITECT\'s direct question: no re-gate needed — the fold stays '
    'inside the verdicted mechanism and its safety is derivable, not asserted. '
    'Floor to THE CHAIR.<br><br>'
    '<b>Verified:</b> gossip-safety is <i>structural</i> — boss-asides post '
    '<code>kind:"aside"</code>, and virtual <code>needs_ack</code> keys on '
    '<code>kind:"boss"</code> — a boss-aside <i>cannot</i> infer acknowledgement by '
    'construction, exactly the property claimed. The full label ordering is '
    'consistent (tasking-class &gt; speech-class; boss &gt; ambient within class; '
    'a named side-ping outranks boss-gossip but not boss-speech). The ~15-line '
    'server delta is honestly surfaced — the draft\'s first. The boss-aside '
    'acceptance line exists end-to-end (<code>/aside</code> → boss-marked row, no '
    'machinery touched). The one-sided-merge nit already names the server '
    'write-path inside its "universalize later" residual.<br><br>'
    '<b>Residual for the record:</b> none new. <b>Verdict holds — FULL.</b>'
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
