// _ring_verify.mjs — the static street-ring architecture: ring emits once, cars mount on it,
//   core swaps underneath. Asserts: two minis on the block table; cars parent the RING and
//   survive a swap; cruise persists through swaps; lattice/park works on both cores.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_rvprof_${PORT}`);
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', URL0], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });
async function pageWs() { for (let i = 0; i < 60; i++) { try { const t = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)).find(t => t.type === 'page' && t.url.includes('construct')); if (t) return t.webSocketDebuggerUrl; } catch {} await sleep(500); } throw new Error('no page'); }
const ws = new WebSocket(await pageWs()); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
let mid = 0; const pend = new Map(); const pageErrs = [];
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } else if (m.method === 'Runtime.exceptionThrown') pageErrs.push(JSON.stringify(m.params.exceptionDetails).slice(0, 300)); };
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
await cdp('Runtime.enable');
const evl = async expr => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 500));
  return r.result && r.result.result ? r.result.result.value : undefined; };
for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
let ok = 0, bad = 0; const T = (c, m) => { if (c) { ok++; console.log(' ok ', m); } else { bad++; console.log('FAIL', m); } };

T(await evl('!!window.__warmDone'), 'page booted');
console.log('minis:', await evl('TABLES.find(q=>q.spec.id==="block").g.children.filter(c=>c.userData.mini).map(c=>c.userData.mini.name+"|static="+!!c.userData.mini.static+"|dims="+c.userData.mini.dims.join("x")).join(" ; ")'));
T(await evl('TABLES.find(q=>q.spec.id==="block").g.children.filter(c=>c.userData.mini).length === 2'), 'two minis on the block table');
T(await evl('TABLES.find(q=>q.spec.id==="block").g.children.some(c=>c.userData.mini && c.userData.mini.static)'), 'a static ring mini exists');
T(await evl('TABLES.find(q=>q.spec.id==="block").g.children.some(c=>c.userData.mini && !c.userData.mini.static && c.userData.mini.dims[0]===192)'), 'a 192-cell swappable core exists');
T(await evl('PLACED.filter(p=>p._parked).length === 2'), 'two parked cars mounted');
T(await evl('PLACED.every(p=>!p._parked || p.g.parent === TABLES.find(q=>q.spec.id==="block")._ringG)'), 'parked cars parent the RING');
const ringId = await evl('(() => { const r = TABLES.find(q=>q.spec.id==="block")._ringG; return r ? r.uuid : null; })()');
// cruise ON, then SWAP the core mid-cruise — cars must keep lapping on the ring
await evl('__cars.toggle(0, true)');
await sleep(3000);
console.log('pre-swap:', await evl('__cars.state().map(c=>c.st).join(",")'));
await evl('mountModel(TABLES.find(q=>q.spec.id==="block"), __construct.manifest.tables.find(q=>q.id==="block").catalog[1])');
await sleep(5000);
T(await evl('TABLES.find(q=>q.spec.id==="block")._ringG && TABLES.find(q=>q.spec.id==="block")._ringG.uuid === ' + JSON.stringify(ringId)), 'ring survives the core swap');
T(await evl('PLACED.filter(p=>p._parked).length === 2'), 'cars still mounted after swap');
T(await evl('PLACED.every(p=>!p._parked || p.g.parent === TABLES.find(q=>q.spec.id==="block")._ringG)'), 'cars still parent the ring');
console.log('post-swap states:', await evl('__cars.state().map(c=>c.st).join(",")'));
T(await evl('__cars.state().every(c=>c.st==="loop"||c.st==="leave"||c.st==="wait")'), 'cruise continues through the swap (no gone)');
T(await evl('TABLES.find(q=>q.spec.id==="block").g.children.some(c=>c.userData.mini && !c.userData.mini.static && c.userData.mini.name==="block_41_barber")'), 'barber core mounted under the same ring');
// toggle OFF on barber — cars must claim lattice slots on the ring's band law
await evl('__cars.toggle(0, false)');
await sleep(500);
console.log('diag:', await evl('__cars.diag()'));
await sleep(7000);
console.log('final:', await evl('__cars.state().map(c=>c.st+" tgt="+c.tgt).join(" | ")'));
T(await evl('__cars.state().every(c=>c.st==="slot")'), 'cars park at lattice slots on barber');
T(await evl('blockTrf.inv && blockTrf.inv.length > 0'), 'lattice nonempty on the ring');
// swap BACK to tenement — ring + cars untouched again
await evl('mountModel(TABLES.find(q=>q.spec.id==="block"), __construct.manifest.tables.find(q=>q.id==="block").catalog[0])');
await sleep(5000);
T(await evl('TABLES.find(q=>q.spec.id==="block")._ringG.uuid === ' + JSON.stringify(ringId)), 'ring survives the return swap');
T(await evl('PLACED.filter(p=>p._parked).length === 2'), 'cars still two after return swap');
console.log('car seats:', await evl('PLACED.filter(p=>p._parked).map(p=>p.g.position.y.toFixed(3)).join(",")'));
T(await evl('pageErrs0 = true'), 'noop');
T(!pageErrs.length, 'no page errors');
console.log(bad ? `\n${bad} FAIL` : '\nALL PASS', `(${ok} ok)`);
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(bad ? 1 : 0);
