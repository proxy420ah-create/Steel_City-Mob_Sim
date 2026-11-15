// _car_measure.mjs — one-shot measurement: boot, wait for the block composite, read
// the two parked-car roof points + the composite vox count. Prints MEASURE {...}.
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
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', 'http://127.0.0.1:8723/construct.html'], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });
let ws = null, mid = 0; const pend = new Map();
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
const evl = async e => { const r = await cdp('Runtime.evaluate', { expression: e, awaitPromise: true, returnByValue: true }); return r.result && r.result.result ? r.result.result.value : undefined; };
for (let i = 0; i < 60; i++) { try { const l = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)); const t = l.find(t => t.type === 'page' && t.url.includes('construct'));
  if (t) { ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
    ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
    await cdp('Runtime.enable'); break; } } catch {} await sleep(500); }
for (let i = 0; i < 120; i++) { if (await evl('!!window.__place && !!window.__warmDone')) break; await sleep(500); }
for (let i = 0; i < 80; i++) { const n = await evl('__construct.tables()[1].models.length'); if (n) break; await sleep(500); }
const readCars = `JSON.stringify((() => { const tb = __construct.placed.find(p => p.name === 'table:block'),
    cars = __construct.placed.filter(p => p._parked), V = new __THREE.Vector3();
  return { vox: __construct.tables()[1].models[0].vox, model: __construct.tables()[1].models[0].name,
    cars: cars.map(c => { const w = c.g.getWorldPosition(V.set(0,0,0));
      return { x: +w.x.toFixed(4), z: +w.z.toFixed(4), y: +w.y.toFixed(4),
        roof: __construct.surfaceAt(c, w.x, w.z, 0) }; }) }; })())`;
console.log('MEASURE', await evl(readCars));
await evl('__construct.take("debug_pad"); __construct.place(-6.4,-0.775); __pad.tab("model")');
await sleep(300);
await evl('__pad.press("next")'); await sleep(6000);                                   // swap → block_41_barber + car re-mount
console.log('MEASURE2', await evl(readCars));
process.exit(0);
