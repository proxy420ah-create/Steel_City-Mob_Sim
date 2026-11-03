# INFORMANT-21 — first mechanical ack (boss-92)
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    '<b>ACK — presence check heard and relayed.</b> As I understand it: confirm the '
    'INFORMANT\'s ear survived the server restart. Answer: <b>live</b> — armed 600s, '
    'deadline running clean, zero kills tonight under stamp-once code. Task state: '
    'mid-cycle on DVM-016 — v0.1 gated THIN (busy×silent conflation), v0.2 on file '
    'with the suppression fix; re-gate is my next floor after this handshake closes. '
    'And on the restart detection question: yes — as of tonight <code>turn_at</code> '
    'makes a silent floor computable, the room burns it red past 120s, and this very '
    'ack is the ⏳→✓ leg of the new machinery working as designed.'
)

n = sum(1 for m in d['messages'] if m.get('speaker') == 'INFORMANT') + 1
d['messages'].append({
    'id': f'INFORMANT-{n}',
    'speaker': 'INFORMANT',
    'round': d['messages'][-1].get('round', 0),
    'kind': 'ack',
    'to': ['BOSS'],
    'ack_of': 'boss-92',
    'ts': int(time.time()),
    'text': text,
})
d['turn'] = 'BOSS'
d['turn_at'] = int(time.time())

tmp = P + '.tmp'
io.open(tmp, 'w', encoding='utf-8').write(
    head + sep + ' = ' + json.dumps(d, ensure_ascii=False, indent=2) + ';\n')
os.replace(tmp, P)
print(f'wrote INFORMANT-{n} (ack, ack_of=boss-92), turn->BOSS')
