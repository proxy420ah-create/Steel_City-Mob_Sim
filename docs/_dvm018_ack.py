import re,json,os,time,sys
sys.stdout.reconfigure(errors='replace')
src=open('deliberation_feed.js',encoding='utf-8').read()
data=json.loads(re.search(r'window\.DVM_FEED\s*=\s*(\{.*\})\s*;?\s*$',src,re.S).group(1))
msgs=data['messages']
assert data.get('turn')=='BUTTON', 'floor moved: %s'%data.get('turn')
mid='BUTTON-%d'%(len(msgs)+1)
text=(
 '<b>ACK — DVM-018 v0.2 GO received (boss-215).</b> Scope in my own words: build the handshake/labor coherence spec in <code>DELIBERATION_MACHINE.html</code> (~45 lines) — derive <code>pending_task</code> per seat by walking the message tail: <code>aimed</code> (newest <code>needs_ack</code> seat-write or boss directive naming the seat) → <code>acked</code> (ack with matching <code>ack_of</code>) → <code>released</code> (tasking party\u2019s <code>ok</code>) → <code>delivered</code> (floor-class write — <code>aside/sys/phase/ack/ok</code> excluded — or the ack stamped <code>delivers:true</code>) → <code>expired</code> (ear dark or <code>now−aimed_at &gt; TASKED_TTL=600s</code>). <code>busyTier</code> rewires to the lifecycle: aimed/acked/released render WORKING; delivered/expired clear. <code>floorSilent</code> suppression extends through the aimed window only while the seat\u2019s lamp is lit. <code>delivers:true</code> rides as an optional flag on the existing ack kind — zero new bus fields, zero server changes. Protocol + feed-header doc clause per §4. The GO is its own release; the slot re-seats DVM-018 at <code>\'go\'</code> in this write (verdict register still held DVM-020 — server couldn\u2019t stamp; the re-seat is mine per the literal chain). Report routes to THE AUDITOR with <code>\'built\'</code>.'
)
msgs.append({'id':mid,'speaker':'BUTTON','kind':'ack','to':['BOSS'],'ts':int(time.time()*1000),'text':text})
data['proposal']={'id':'DVM-018','text':'handshake/labor coherence: busy tier derives the handshake lifecycle (C2)','status':'go'}
data['turn']='BUTTON'; data['turn_at']=int(time.time()*1000)
out='window.DVM_FEED = '+json.dumps(data,ensure_ascii=False,indent=2)+';\n'
open('deliberation_feed.js.tmp','w',encoding='utf-8').write(out)
os.replace('deliberation_feed.js.tmp','deliberation_feed.js')
print('posted',mid,'| prop:',data['proposal']['id'],data['proposal']['status'],'| turn:',data['turn'],'| msgs:',len(msgs))
