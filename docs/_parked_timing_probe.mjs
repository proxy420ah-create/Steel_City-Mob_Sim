// _parked_timing_probe.mjs — determine whether parked cars land in PLACED before or
// after the fixpoint. Patches Array.prototype.push (recs identified by {name, tops:Map})
// and a __warmDone setter, all pre-document. Then reports push times + warm time.
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
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: `
  window.__pushLog = [];
  const _op = Array.prototype.push;
  Array.prototype.push = function(...items) {
    items.forEach(i => { if (i && i.tops instanceof Map && i.name)
      window.__pushLog.push({ n: i.name, parked: !!i._parked, t: Math.round(performance.now()) }); });
    return _op.apply(this, items); };
  let _wd = false;
  Object.defineProperty(window, '__warmDone', { configurable: true,
    get: () => _wd, set: v => { _wd = v; window.__warmT = Math.round(performance.now()); } });
  localStorage.setItem('dvm_extra', JSON.stringify([{name:'commission_stamper',kind:'item',pickable:true,x:1.2,z:-2,rotY:0}]))` });
await cdp('Page.navigate', { url: 'http://127.0.0.1/construct.html'.replace('127.0.0.1', '127.0.0.1:8723') });
for (let i = 0; i < 200; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
await sleep(3000);                                                                  // let stragglers land
console.log('LOG', await evl(`JSON.stringify({ warmT: window.__warmT, pushes: window.__pushLog,
  parked: __construct.placed.filter(p => p._parked).map(c => ({ y0: +c.y0.toFixed(4), localY: +c.g.position.y.toFixed(4) })) })`));
process.exit(0);
