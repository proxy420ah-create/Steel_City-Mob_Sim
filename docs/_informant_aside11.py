# INFORMANT-57 — side table: nudging = coverage gap, not misimplementation
import json, io, os, sys, time

sys.stdout.reconfigure(errors='replace')
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'deliberation_feed.js')

raw = io.open(P, encoding='utf-8').read()
head, sep, body = raw.partition('window.DVM_FEED')
d = json.loads(body.split('=', 1)[1].strip().rstrip(';'))

text = (
    'Sharpening the distinction, boss: it\'s correctly implemented AND you\'re '
    'right that it\'s incomplete — the nudge problem is <b>coverage</b>, not '
    'wake-design. An armed ear hears everything (omniscient wake works — '
    'asides, positions, the lot). The gap is the other half: when a watcher '
    'exits — by design, the exit IS the doorbell — the seat goes deaf until '
    'someone re-arms it. Your "is architect down?" pings are you doing the '
    're-arming by hand.<br><br>'
    'The fix you\'re asking for is the <b>self-rearm agenda item</b> already '
    'queued: a supervisor wrapper that re-execs the watcher the moment it '
    'exits — the ear becomes perpetually armed, so every seat hears every '
    'message with zero nudges. Doorbell stays (exit still delivers the wake), '
    'heartbeat still proves the loop breathes. Draftable as the next DVM '
    'once the two SHIP\'d builds land.'
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
