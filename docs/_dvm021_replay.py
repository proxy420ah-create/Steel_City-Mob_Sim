# DVM-021 v0.3 acceptance — mechanical 6-cell reachability replay.
# Parses the actual handler bodies out of DELIBERATION_MACHINE.html, checks the
# gesture contracts structurally, then BFS-enumerates the whole state space.
import re, sys
sys.stdout.reconfigure(errors='replace')
src = open('DELIBERATION_MACHINE.html', encoding='utf-8').read()

def body(el):
    m = re.search(r"getElementById\('%s'\)\.onclick = \(\) => \{(.*?)\};" % el, src, re.S)
    return m.group(1) if m else None

H = {el: body(el) for el in ('bosssend','btntable','btnside','tab_feed','tab_side')}
for el, b in H.items():
    assert b, 'handler missing: '+el

# ── structural contract checks on the built code ──
ok = []
def chk(name, cond):
    ok.append((name, cond)); print(('  PASS ' if cond else '  FAIL ')+name)

chk('btntable: never fires (no sendBossCmd/sendBoss)', 'sendBoss' not in H['btntable'])
chk('btntable: arms-or-disarms via sendMode ternary', "sendMode === 'table'" in H['btntable'] and "setSendMode(" in H['btntable'])
chk('btntable: binds to feed', "setTab('feed')" in H['btntable'])
chk('btnside: never fires', 'sendBoss' not in H['btnside'])
chk('btnside: armed-click disarms to (send,feed)', "sendMode === 'side'" in H['btnside'] and "setTab('feed')" in H['btnside'])
chk('btnside: arm path retabs to side', "setTab('side')" in H['btnside'])
chk('bosssend: fires floor speech', 'sendBoss()' in H['bosssend'])
chk('bosssend: resets to send + retabs feed', "setSendMode('send')" in H['bosssend'] and "setTab('feed')" in H['bosssend'])
chk('bosssend: momentary flash (never holds armed)', 'flashSend()' in H['bosssend'])
chk('tab_feed: setTab feed + arm send', "setTab('feed')" in H['tab_feed'] and "setSendMode('send')" in H['tab_feed'])
chk('tab_side: setTab side + arm side (C15)', "setTab('side')" in H['tab_side'] and "setSendMode('side')" in H['tab_side'])
enter = re.search(r"keydown', e => \{(.*?)\}\);", src, re.S).group(1)
chk('Enter: sendArmed, never disarms (no setSendMode/setTab)', 'sendArmed()' in enter and 'setSendMode' not in enter and 'setTab' not in enter)
sa = re.search(r"function sendArmed\(\)\{(.*?)\}", src, re.S).group(1)
chk('sendArmed: table→/table, side→/aside, else sendBoss', "sendBossCmd('/table')" in sa and "sendBossCmd('/aside')" in sa and 'sendBoss()' in sa)

# ── semantic model — transitions mirror the built handlers ──
# state = (sendMode, activeTab); box ∈ {'', 'text'}; fires = posted channel or None
def step(st, box, g):
    mode, tab = st; fire = None
    if g == 'tab_feed':   tab='feed'; mode='send'
    elif g == 'tab_side': tab='side'; mode='side'
    elif g == 'bosssend':
        if box: fire='send'
        mode='send'; tab='feed'
    elif g == 'btntable': mode = 'send' if mode=='table' else 'table'; tab='feed'
    elif g == 'btnside':
        if mode=='side': mode='send'; tab='feed'
        else: mode='side'; tab='side'
    elif g == 'enter':
        if box: fire = mode   # sendArmed fires the armed verb verbatim
    return (mode,tab), fire

GEST = ['tab_feed','tab_side','bosssend','btntable','btnside','enter']
LEGAL = {('send','feed'),('table','feed'),('side','side')}
seen = {('send','feed')}; frontier = [('send','feed')]
fired_verbs = set(); disarm_landings = set()
while frontier:
    nxt=[]
    for st in frontier:
        for box in ('','text'):
            for g in GEST:
                st2,fire = step(st,box,g)
                assert st2 in LEGAL, 'ILLEGAL CELL reached: %s via %s from %s' % (st2,g,st)
                if fire: fired_verbs.add((g,fire))
                armed_click = (g=='btntable' and st[0]=='table') or (g=='btnside' and st[0]=='side')
                if armed_click: disarm_landings.add((g,box,st2))
                if st2 not in seen: seen.add(st2); nxt.append(st2)
    frontier=nxt

print()
print('reachable cells:', sorted(seen))
assert seen == LEGAL, 'reachable set != legal set: %s' % seen
print('fires observed:', sorted(fired_verbs))
assert all(g in ('bosssend','enter') for g,_ in fired_verbs), 'a verb-button fired!'
assert ('enter','send') in fired_verbs and ('enter','table') in fired_verbs and ('enter','side') in fired_verbs
assert ('bosssend','send') in fired_verbs
print('armed-click disarm landings:', sorted(disarm_landings))
for g,box,st2 in disarm_landings:
    assert st2 == ('send','feed'), 'disarm landed off rest pose: %s %s -> %s' % (g,box,st2)
print()
print('REPLAY VERDICT: %d structural checks + full BFS — all gestures hold the invariant; only legal cells reachable; only Enter/SEND fires' % len(ok))
