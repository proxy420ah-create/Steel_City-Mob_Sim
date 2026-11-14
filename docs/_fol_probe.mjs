// _fol_probe.mjs — end-to-end headless test of the Construct's follower persistence (dvm_fol).
// Drives __console/__construct over CDP: spawn CHAIR → roster written → reload → restored
// at saved order+pos → party handoff wins once + clears → reload → roster governs →
// dismiss → reload → stays gone.
//
//   node _fol_probe.mjs
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_shotprof_${PORT}_${Date.now()}`);                 // unique per run — a reused profile leaks stale localStorage

const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', URL0], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });

let ws = null, mid = 0; const pend = new Map();
const cdp = (method, params = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method, params })); });
const evl = async expr => {
  const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 500));
  return r.result && r.result.result ? r.result.result.value : undefined;
};
async function connect(frag = 'construct') {
  for (let i = 0; i < 60; i++) {
    try {
      const list = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`));
      const t = list.find(t => t.type === 'page' && t.url.includes(frag));
      if (t) {
        ws = new WebSocket(t.webSocketDebuggerUrl);
        await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
        ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
        return;
      }
    } catch {}
    await sleep(500);
  }
  throw new Error('no page target');
}
const waitBoot = async () => { for (let i = 0; i < 120; i++) { if (await evl('!!window.__warmDone')) return; await sleep(500); }
  throw new Error('__warmDone never appeared'); };
const reload = async () => { await evl('location.reload()'); await sleep(1500); await connect(); await waitBoot(); await sleep(800); };

const R = [];
const check = (name, ok, detail) => { R.push({ name, ok: !!ok, detail }); console.log((ok ? 'PASS' : 'FAIL') + '  ' + name + (detail ? '  — ' + detail : '')); };

await connect(); await waitBoot(); await sleep(800);

// 1 — clean slate: no roster, no followers
check('fresh boot: no roster', !(await evl('localStorage.getItem("dvm_fol")')) || (await evl('localStorage.getItem("dvm_fol")')) === '[]');
check('fresh boot: no followers', (await evl('window.__construct.followers().length')) === 0);

// 2 — spawn CHAIR1 through the console path the user uses
await evl('window.__console.spawn({ char: "CHAIR1" })');
for (let i = 0; i < 40; i++) { if (await evl('window.__construct.followers().length') === 1) break; await sleep(250); }
check('spawn: CHAIR1 in room', (await evl('window.__construct.followers().length')) === 1);

// 3 — put him on WAIT somewhere identifiable, let the heartbeat land
await evl('window.__construct.agent.order("wait")');
await evl('window.__construct.agent.go(1.1, -3.2)');                              // tag+goto — then wait freezes him mid-room
await sleep(400);
await evl('window.__construct.agent.order("wait")');
await sleep(3400);                                                              // heartbeat period is 2.5s
const roster = JSON.parse(await evl('localStorage.getItem("dvm_fol")') || 'null');
check('roster written', roster && roster.length === 1 && roster[0].char === 'CHAIR1', JSON.stringify(roster));
check('roster order is wait', roster && roster[0].order === 'wait', JSON.stringify(roster && roster[0]));

// 4 — reload → restored from the roster at his saved spot/order
await reload();
const fol1 = await evl('JSON.stringify(window.__construct.followers())');
const f1 = fol1 && JSON.parse(fol1)[0];
check('reload: CHAIR1 restored', f1 && f1.char === 'CHAIR1', fol1);
check('reload: exactly one (no dup)', (await evl('window.__construct.followers().length')) === 1, fol1);
check('reload: order survived', f1 && f1.order === 'wait', fol1);
check('reload: position near saved', f1 && roster && Math.abs(f1.pos[0] - roster[0].x) < .15 && Math.abs(f1.pos[2] - roster[0].z) < .15,
  `saved ${roster && roster[0].x},${roster && roster[0].z} got ${f1 && f1.pos}`);

// 5 — party handoff still wins, once; sessionStorage clears on consume
await evl('sessionStorage.setItem("dvm_party", JSON.stringify([{ char: "CHAIR1", order: "follow" }]))');
await reload();
const fol2 = await evl('JSON.stringify(window.__construct.followers())');
const f2 = fol2 && JSON.parse(fol2)[0];
check('party handoff wins the boot', f2 && f2.char === 'CHAIR1' && f2.order === 'follow', fol2);
check('handoff consumed (sessionStorage cleared)', (await evl('sessionStorage.getItem("dvm_party")')) === null);
await reload();
check('post-handoff reload: roster governs', (await evl('window.__construct.followers().length')) === 1);

// 6 — dismiss → roster empties → reload stays empty
await evl('window.__console.spawn({ char: "CHAIR1" })');                          // toggleSpawn on a live unit = dismiss
await sleep(400);
check('dismiss clears roster', (await evl('localStorage.getItem("dvm_fol")')) === '[]');
await reload();
check('reload after dismiss: stays gone', (await evl('window.__construct.followers().length')) === 0);

// 7 — goHome while heeling: he departs the roster (BOSS's report — seat him in the
//     Commission, portal back, and the stale roster must not resurrect him)
await evl('window.__console.spawn({ char: "CHAIR1" })');
for (let i = 0; i < 40; i++) { if (await evl('window.__construct.followers().length') === 1) break; await sleep(250); }
await evl('window.__home()');                                                   // heel = follow order → he walks out with you
await sleep(2500);                                                              // veil (420ms) + commission boot
await connect('thecommission');
check('depart: party handoff carries him', ((await evl('sessionStorage.getItem("dvm_party")')) || '').includes('CHAIR1'));
check('depart: roster releases him', (await evl('localStorage.getItem("dvm_fol")')) === '[]',
  await evl('localStorage.getItem("dvm_fol")'));

// 8 — back in the Construct: a unit parked on WAIT stays in the roster through goHome.
//     A real return rides the portal E — which REWRITES dvm_party from his commission-side
//     order (seated → empty). Simulate that write; a bare location.href would leave the
//     stale depart-handoff behind and resurrect him through the party path.
await evl('sessionStorage.setItem("dvm_party", "[]"); location.href = "construct.html"'); await sleep(1500);
await connect(); await waitBoot(); await sleep(800);
check('return: empty room (roster was released)', (await evl('window.__construct.followers().length')) === 0);
await evl('window.__console.spawn({ char: "CHAIR1" })');
for (let i = 0; i < 40; i++) { if (await evl('window.__construct.followers().length') === 1) break; await sleep(250); }
await evl('window.__construct.agent.order("wait")'); await sleep(300);
await evl('window.__home()');                                                   // parked unit does NOT heel → stays in the room
await sleep(2500);
await connect('thecommission');
const parked = await evl('localStorage.getItem("dvm_fol")');
check('goHome: parked wait-unit stays in roster', parked && JSON.parse(parked).length === 1 && JSON.parse(parked)[0].order === 'wait', parked);

const fails = R.filter(r => !r.ok);
console.log(`\n${R.length - fails.length}/${R.length} checks passed` + (fails.length ? ` — FAILED: ${fails.map(f => f.name).join(' | ')}` : ''));
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(fails.length ? 1 : 0);
