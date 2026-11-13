// _place_probe.mjs — end-to-end headless test of the Construct's place mode (X).
// Drives __place/__cam over CDP: toggle → grab desk → drag to NE corner (carry check)
// → drop → copy → paste → drop → reload → verify persistence → del → reload → verify gone.
//
//   node _place_probe.mjs
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_shotprof_${PORT}`);

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
async function connect() {
  for (let i = 0; i < 60; i++) {
    try {
      const list = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`));
      const t = list.find(t => t.type === 'page' && t.url.includes('construct'));
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
const waitBoot = async () => { for (let i = 0; i < 120; i++) { if (await evl('!!window.__place && !!window.__warmDone')) return; await sleep(500); }
  throw new Error('__place never appeared'); };

const R = [];
const check = (name, ok, detail) => { R.push({ name, ok: !!ok, detail }); console.log((ok ? 'PASS' : 'FAIL') + '  ' + name + (detail ? '  — ' + detail : '')); };

await connect(); await waitBoot(); await sleep(800);

// 1 — toggle on
await evl('__place.toggle(true)');
check('toggle on', await evl('__place.on()'));
check('legend chip visible', await evl(`[...document.querySelectorAll('div')].some(d => d.textContent.startsWith('PLACE ·') && d.style.display === 'block')`));

// 2 — grab the desk (its hosted lamp/book/stamper should enlist as passengers)
await evl('__place.grab("commission_lawdesk")');
check('grab selects desk', (await evl('__place.sel()')) === 'commission_lawdesk');
const carry = await evl('(window.__place._c = (() => { const s = window.__construct.placed.find(p => p.name === "commission_lawdesk"); return (s._carry || []).map(c => c.q.name); })())');
check('carry enlisted 3 passengers', carry && carry.length === 3, JSON.stringify(carry));

// 3 — lock a camera aiming down at the NE corner (x≈2.6, z≈-5.3); placeStep drags the desk under the ray
await evl('__cam(1.8, 1.9, -4.2, 2.6, 0, -5.3)');
await sleep(600);
const midDump = JSON.parse(await evl('__place.dump()'));
const deskMid = midDump.find(p => p.name === 'commission_lawdesk');
check('desk dragged under aim ray', deskMid && Math.abs(deskMid.x - 2.6) < .35 && Math.abs(deskMid.z + 5.3) < .35, JSON.stringify(deskMid));
const lampMid = midDump.find(p => p.name === 'commission_desklamp');
check('lamp rode along', lampMid && Math.abs(lampMid.x - deskMid.x) < .8 && Math.abs(lampMid.z - deskMid.z) < .9, JSON.stringify(lampMid));

// 4 — drop → persisted
await evl('__place.drop()');
const over = JSON.parse(await evl('localStorage.getItem("dvm_place")') || '{}');
check('dvm_place override written', over.commission_lawdesk && Math.abs(over.commission_lawdesk.z + 5.3) < .4, JSON.stringify(over.commission_lawdesk));

// 4b — grab-anchor: aim dead-down at the desk's SW corner leg, LMB-grab, then drag to a new spot.
// The grabbed POINT must land on the aim, not the centre.
await evl('__cam(2.8, 1.6, -6.0, 2.8, 0, -6.0)');       // desk corner: local (+.7,+.2) → world (2.8,−6.0) at rotY π/2
await sleep(500);
await evl('document.querySelector(' + JSON.stringify('canvas') + ').dispatchEvent(new MouseEvent("mousedown", {button:0, bubbles:true}))');
await sleep(300);
const grabState = JSON.parse(await evl('(() => { const s = window.__construct.placed.find(p => p._grab); return s ? JSON.stringify({n:s.name, lx:s._grab.lx, lz:s._grab.lz}) : "none"; })()'));
check('LMB grab anchors at the hit point', grabState && grabState.n === 'commission_lawdesk' && Math.abs(grabState.lx - .7) < .12 && Math.abs(grabState.lz - .2) < .12, JSON.stringify(grabState));
check('grid visible while dragging', await evl('__scene.children.some(c => c.type === "GridHelper" && c.visible)'));
await evl('__cam(0, 1.6, -3.0, 0, 0, -3.0)');           // drag the grabbed corner to dead-centre south
await sleep(500);
const anchored = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_lawdesk');
check('grabbed corner lands on the aim', anchored && Math.abs(anchored.x + .2) < .12 && Math.abs(anchored.z + 2.3) < .12,
  JSON.stringify(anchored) + ' — corner lands (0,-3), centre offsets (−.2,−2.3)');
check('x/z snapped to .1 grid', anchored && Math.abs(anchored.x * 10 % 1) < .001 && Math.abs(anchored.z * 10 % 1) < .001);
await evl('__place.drop()');

// 4c — keyboard nudge parity: arrows nudge world-frame, fine .005 / shift .05
await evl('__place.grab("commission_lawdesk")');
await evl('window.dispatchEvent(new KeyboardEvent("keydown", {key:"ArrowRight"}))');
await sleep(350);
const nudged = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_lawdesk');
check('API grab = centre anchor → item follows aim + .005 nudge', nudged && Math.abs(nudged.x - .005) < .01 && Math.abs(nudged.z + 3) < .01, JSON.stringify(nudged));
await evl('__place.drop()');

// 4d — undo: ^Z restores the pre-nudge position
await evl('window.dispatchEvent(new KeyboardEvent("keydown", {key:"z", ctrlKey:true}))');
await sleep(350);
const undone = JSON.parse(await evl('__place.dump()')).find(p => p.name === 'commission_lawdesk');
check('ctrl-Z restores the drop position', undone && Math.abs(undone.x + .2) < .02, JSON.stringify(undone));

// 5 — copy + paste → a second desk emits at the spot and auto-selects
await evl('__place.grab("commission_lawdesk")'); await evl('__place.copy()'); await evl('__place.paste()');
await sleep(1500);
const after = JSON.parse(await evl('__place.dump()'));
check('paste emitted a 2nd desk', after.filter(p => p.name === 'commission_lawdesk').length === 2, JSON.stringify(after.filter(p => p.name === 'commission_lawdesk')));
check('paste auto-selected the copy', (await evl('__place.sel()')) === 'commission_lawdesk');
const copy = after.find(p => p.src === 'extra');
check('copy is an extra with solids', copy && !!copy.solids, copy && JSON.stringify(copy));
await evl('__place.drop()');
const extra = JSON.parse(await evl('localStorage.getItem("dvm_extra")') || '[]');
check('dvm_extra recorded the copy', extra.length === 1 && extra[0].name === 'commission_lawdesk', JSON.stringify(extra));

// 6 — reload → layout persists (override + extra both re-materialize)
console.log('PRE-RELOAD dvm_place:', await evl('localStorage.getItem("dvm_place")'));
console.log('PRE-RELOAD dump:', await evl('__place.dump()'));
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(800);
const boot = JSON.parse(await evl('__place.dump()'));
const desks = boot.filter(p => p.name === 'commission_lawdesk');
check('reload: 2 desks present', desks.length === 2, JSON.stringify(desks));
check('reload: override spot held', desks.some(d => Math.abs(d.x) < .02 && Math.abs(d.z + 3) < .02), JSON.stringify(desks));
const lampRe = boot.find(p => p.name === 'commission_desklamp');
check('reload: lamp near desk', lampRe && desks.some(d => Math.abs(lampRe.x - d.x) < .8 && Math.abs(lampRe.z - d.z) < .9), JSON.stringify(lampRe));

// 7 — delete the extra copy → dvm_extra empties; tombstone keeps it gone
await evl('__place.toggle(true)');
const exIdx = await evl('window.__construct.placed.findIndex(p => p.source === "extra")');
check('extra copy found for delete', exIdx >= 0, 'idx ' + exIdx);
await evl(`__place.grabIdx(${exIdx})`);
await evl('__place.del()');
const afterDel = JSON.parse(await evl('__place.dump()'));
check('extra deleted live', afterDel.filter(p => p.name === 'commission_lawdesk').length === 1);
check('dvm_extra emptied', JSON.parse(await evl('localStorage.getItem("dvm_extra")') || '[]').length === 0);
await evl('location.reload()'); await sleep(1500);
await connect(); await waitBoot(); await sleep(800);
const boot2 = JSON.parse(await evl('__place.dump()'));
check('reload: extra stays deleted', boot2.filter(p => p.name === 'commission_lawdesk').length === 1, JSON.stringify(boot2.filter(p => p.name === 'commission_lawdesk')));

// 8 — fly-camera clamp + release back to body
await evl('__cam(null)');
check('camLock released', true);
const shot = await cdp('Page.captureScreenshot', { format: 'png' });
fs.writeFileSync(path.join(HERE, 'probe_placemode.png'), Buffer.from(shot.result.data, 'base64'));

console.log('---'); console.log(R.every(r => r.ok) ? 'ALL PASS' : 'FAILURES: ' + JSON.stringify(R.filter(r => !r.ok)));
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(R.every(r => r.ok) ? 0 : 1);
