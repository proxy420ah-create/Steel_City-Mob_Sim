// _parked_drop_repro.mjs — boot with a seeded dvm_extra so parked cars land in PLACED
// BEFORE the fixpoint runs. Prints each parked car's world y (expect .26 = dropped bug,
// ~.2664 = held its street-law seat).
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_shotprof_${PORT}_${Date.now()}`);
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', 'about:blank'], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });
let ws = null, mid = 0; const pend = new Map();
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
const evl = async e => { const r = await cdp('Runtime.evaluate', { expression: e, awaitPromise: true, returnByValue: true }); return r.result && r.result.result ? r.result.result.value : undefined; };
for (let i = 0; i < 60; i++) { try { const l = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)); const t = l.find(t => t.type === 'page');
  if (t) { ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
    ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
    await cdp('Runtime.enable'); await cdp('Page.enable'); break; } } catch {} await sleep(500); }
// seed one extra BEFORE any page script runs — it extends the boot wait so the parked
// cars' emit chain lands before the fixpoint sweep
await cdp('Page.addScriptToEvaluateOnNewDocument', { source:
  `localStorage.setItem('dvm_extra', JSON.stringify(Array.from({length: 40}, (_, i) =>
    ({name:'commission_stamper',kind:'item',pickable:true,x:1.2 + (i % 8) * .08,z:-2 - ((i / 8) | 0) * .08,rotY:0}))))` });
await cdp('Page.navigate', { url: 'http://127.0.0.1:8723/construct.html' });
for (let i = 0; i < 160; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
for (let i = 0; i < 80; i++) { const n = await evl('__construct.placed.filter(p => p._parked).length'); if (n >= 2) break; await sleep(500); }
console.log('PARKED', await evl(`JSON.stringify(__construct.placed.filter(p => p._parked).map(c => {
  const w = c.g.getWorldPosition(new __THREE.Vector3());
  return { localY: +c.g.position.y.toFixed(4), y0: +c.y0.toFixed(4), worldY: +w.y.toFixed(4), parent: c.g.parent.type }; }))`));
console.log('SPINNER', await evl(`JSON.stringify((() => { const c = __construct.placed.find(p => p.name === 'vehicle_490_touring' && !p._parked);
  return c ? { localY: +c.g.position.y.toFixed(4), y0: +c.y0.toFixed(4) } : 'none'; })())`));
process.exit(0);
