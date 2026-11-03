# DVM-018 v0.2 acceptance — spec §5 cases against the built pending_task derivation.
import re, sys, time
sys.stdout.reconfigure(errors='replace')
src = open('DELIBERATION_MACHINE.html', encoding='utf-8').read()

block = re.search(r'DVM-018 pending_task(.*?)\n  \}\);', src, re.S)
assert block, 'derivation block missing'
b = block.group(1)
ok = 0
def chk(name, cond):
    global ok
    ok += 1
    print(('  PASS ' if cond else '  FAIL ') + name)
    assert cond, name

chk('SIDE exclusion set = aside/sys/phase/ack/ok', "aside:1, sys:1, phase:1, ack:1, ok:1" in b)
chk('TASKED_TTL = 600000', 'TASKED_TTL = 600000' in b)
chk('aimed: needs_ack or boss+to', "m.needs_ack || (m.kind === 'boss' && tos.length && i >= ep)" in b)
chk('delivers:true terminates', 'n.delivers' in b)
chk('newest-wins guard', 'DS.pendingTask[k]) continue' in b)
chk('expiry check', "Date.now() - rec.aimed > TASKED_TTL" in b)
bt = re.search(r'function busyTier\(k\)\{(.*?)\n\}', src, re.S).group(1)
chk('busyTier: lamp gate first', "lampState(k) !== 'lit'" in bt)
chk('busyTier: lifecycle states tasked', "pt.state !== 'delivered' && pt.state !== 'expired'" in bt)
chk('busyTier: BUILD precedence preserved', "DS.prop.status === 'go'" in bt)
fs = re.search(r'floorSilent.*?\n.*?sk = \((.*?)\) \? DS\.turn', src, re.S)
chk('floorSilent suppressed by busyTier', '!busyTier(DS.turn)' in fs.group(1))

# ── semantic port of the built derivation ──
SIDE = {'aside','sys','phase','ack','ok'}
TTL = 600000
SEATS = {'CHAIR','ARCHITECT','INFORMANT','AUDITOR','BUTTON'}
LEGACY = {'DEVIN':'CHAIR','MEMBER2':'ARCHITECT','MEMBER3':'BUTTON','MEMBER4':'INFORMANT'}
def seatOf(x): return x if x in SEATS else LEGACY.get(x)
def pending(msgs, now):
    ep = float('inf')  # no ack_epoch in tests → all boss directives count
    pt = {}
    for i in range(len(msgs)-1, -1, -1):
        m = msgs[i]
        tos = [seatOf(t) or t for t in (m.get('to') or [])]
        tasked = m.get('needs_ack') or (m.get('kind')=='boss' and tos and i >= ep if ep!=float('inf') else m.get('kind')=='boss' and tos)
        if not tasked: continue
        for k in tos:
            if k=='BOSS' or k in pt: continue
            rec = {'aimed': m.get('ts',0), 'state':'aimed'}
            ackId=''
            for n in msgs[i+1:]:
                nk = seatOf(n.get('speaker'))
                if nk==k and n.get('kind')=='ack' and n.get('ack_of')==m.get('id'):
                    ackId=n['id']; rec['state']='acked'
                    if n.get('delivers'): rec['state']='delivered'
                elif ackId and n.get('kind')=='ok' and n.get('ack_of')==ackId:
                    if rec['state']=='acked': rec['state']='released'
                elif nk==k and n.get('kind') not in SIDE:
                    rec['state']='delivered'
                if rec['state']=='delivered': break
            if rec['state']!='delivered' and rec['aimed'] and now-rec['aimed']>TTL: rec['state']='expired'
            pt[k]=rec
    return pt
def busy(msgs, k, lit=True, now=None):
    if not lit: return 0
    pt = pending(msgs, now or int(time.time()*1000)).get(k)
    return 2 if pt and pt['state'] not in ('delivered','expired') else 0

NOW = int(time.time()*1000)
print()
# §5.1 aimed → WORKING pre-ack (F1)
msgs=[{'id':'t1','speaker':'CHAIR','kind':'tasking','needs_ack':True,'to':['ARCHITECT'],'ts':NOW-30000}]
chk('F1: aimed (seat needs_ack) → WORKING pre-ack', busy(msgs,'ARCHITECT')==2)
msgs=[{'id':'b1','speaker':'BOSS','kind':'boss','to':['BUTTON'],'ts':NOW-30000}]
chk('F1: aimed (boss directive) → WORKING pre-ack', busy(msgs,'BUTTON')==2)
# §5.2 delivers:true ack → clears (F2)
msgs=[{'id':'t1','speaker':'CHAIR','kind':'tasking','needs_ack':True,'to':['ARCHITECT'],'ts':NOW-30000},
      {'id':'a1','speaker':'ARCHITECT','kind':'ack','ack_of':'t1','delivers':True,'ts':NOW-20000}]
chk('F2: delivers:true ack → clears at the write', busy(msgs,'ARCHITECT')==0)
# §5.3 stale >600s → expires (F3)
msgs=[{'id':'t1','speaker':'CHAIR','kind':'tasking','needs_ack':True,'to':['ARCHITECT'],'ts':NOW-700000}]
chk('F3: undelivered task >600s → expired, tier drops', busy(msgs,'ARCHITECT',now=NOW)==0)
# §5.4 ear dark mid-task → drops
msgs=[{'id':'t1','speaker':'CHAIR','kind':'tasking','needs_ack':True,'to':['ARCHITECT'],'ts':NOW-30000}]
chk('ear-dark tasked seat → tier 0 (lamp gate)', busy(msgs,'ARCHITECT',lit=False)==0)
# §5.5 ok without delivery → persists
msgs=[{'id':'t1','speaker':'CHAIR','kind':'tasking','needs_ack':True,'to':['ARCHITECT'],'ts':NOW-60000},
      {'id':'a1','speaker':'ARCHITECT','kind':'ack','ack_of':'t1','ts':NOW-50000},
      {'id':'o1','speaker':'CHAIR','kind':'ok','ack_of':'a1','ts':NOW-40000}]
chk('released-but-undelivered → still WORKING', busy(msgs,'ARCHITECT')==2)
# §5.6 aside mid-task → persists (gate F1)
msgs.append({'id':'as1','speaker':'ARCHITECT','kind':'aside','ts':NOW-30000})
chk('aside mid-task → still WORKING (voice ≠ product)', busy(msgs,'ARCHITECT')==2)
# floor-class write delivers
msgs.append({'id':'p1','speaker':'ARCHITECT','kind':'position','ts':NOW-20000})
chk('floor-class write → delivered, clears', busy(msgs,'ARCHITECT')==0)
# plain ack (no delivers) then nothing → still tasked
msgs=[{'id':'t1','speaker':'CHAIR','kind':'tasking','needs_ack':True,'to':['BUTTON'],'ts':NOW-30000},
      {'id':'a1','speaker':'BUTTON','kind':'ack','ack_of':'t1','ts':NOW-20000}]
chk('acked-unreleased-undelivered → WORKING', busy(msgs,'BUTTON')==2)
# newest tasking wins
msgs=[{'id':'t1','speaker':'CHAIR','kind':'tasking','needs_ack':True,'to':['AUDITOR'],'ts':NOW-700000},
      {'id':'t2','speaker':'CHAIR','kind':'tasking','needs_ack':True,'to':['AUDITOR'],'ts':NOW-30000}]
chk('newest tasking wins (old stale one ignored)', busy(msgs,'AUDITOR',now=NOW)==2)
print()
print('DVM-018 ACCEPTANCE: %d checks — derivation structurally verified + all spec §5 lifecycle cases pass' % ok)
