// _park_repro.mjs — reproduce the USER path: pad CARS button (padTable-nearest), not __cars.toggle.
//   Watches blockTrf internals at toggle-off: on flag, car states, inv lattice, claims.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_prprof_${PORT}`);
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', URL0], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });

async function pageWs() {
  for (let i = 0; i < 60; i++) {
    try { const t = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)).find(t => t.type === 'page' && t.url.includes('construct'));
      if (t) return t.webSocketDebuggerUrl; } catch {}
    await sleep(500); }
  throw new Error('no page target');
}
const ws = new WebSocket(await pageWs());
await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
let mid = 0; const pend = new Map(); const pageErrs = [];
ws.onmessage = e => { const m = JSON.parse(e.data);
  if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); }
  else if (m.method === 'Runtime.exceptionThrown') pageErrs.push(JSON.stringify(m.params.exceptionDetails).slice(0, 300)); };
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
await cdp('Runtime.enable');
const evl = async expr => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 500));
  return r.result && r.result.result ? r.result.result.value : undefined; };

for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
console.log('boot', await evl('!!window.__warmDone'));

// stand AT the block table + HOLD the pad — the user's real flow (held pad tracks the camera)
await evl('(() => { const t = __construct.tables().find(q => q.id === "block"); pos.x = t.x; pos.z = t.z + 1.2; takeIt(__pad.rec()); })()');
await sleep(200);   // a frame so held.g.position lands near the camera
console.log('padTable →', await evl('padTable().spec.id'));
// USER PATH: press CARS on the pad
console.log('press cars →', await evl('__pad.press("cars"), blockTrf.on'));
await sleep(3500);   // let them pull out + cruise
console.log('mid-cruise states:', await evl('__cars.state().map(c=>c.st+":"+Math.round(c.s)).join(" | ")'));
// toggle OFF the same way — a second pad press
console.log('press cars again →', await evl('__pad.press("cars"), blockTrf.on'));
await sleep(400);
console.log('inv lattice:', await evl('JSON.stringify(__cars.inv())'));
console.log('inv size:', await evl('blockTrf.inv ? blockTrf.inv.length : "null"'));
console.log('after-off states+tgt:', await evl('__cars.state().map(c=>c.st+":"+Math.round(c.s)+":tgt="+c.tgt).join(" | ")'));
// try a manual claim to see what it returns
console.log('manual claim test:', await evl('blockTrf.cars.map(c => { try { const t = blockTrf.claim(c, c.s); return t ? t.sc.edge + "@" + Math.round(t.sc.along) : "null"; } catch(e) { return "THROW:" + e.message; } }).join(" | ")'));
await sleep(5000);
console.log('5s later:', await evl('__cars.state().map(c=>c.st+":"+Math.round(c.s)+":tgt="+c.tgt).join(" | ")'));
console.log('page errors:', pageErrs.length ? pageErrs.join(' || ') : 'none');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(0);
