// _roadprof_scan.mjs — ring-architecture sanity: the static street ring carries the road
//   geometry (constant — every core shares it); cores swap underneath. Dumps ring profile/
//   slots + the mounted core's identity per swap.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_rpprof_${PORT}`);
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
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 400));
  return r.result && r.result.result ? r.result.result.value : undefined; };
for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }

const dump = async tag => console.log(tag, await evl(`(() => { const t = TABLES.find(q => q.spec.id === 'block'),
    ring = t.g.children.find(c => c.userData.mini && c.userData.mini.static),
    core = t.g.children.find(c => c.userData.mini && !c.userData.mini.static),
    mn = ring && ring.userData.mini, cm = core && core.userData.mini;
  if (!mn) return 'no ring';
  const R = roadProf(mn), inv = slotInv(mn, 38, 31);
  return 'ring: ' + mn.name + ' dims=' + mn.dims.join('x') + ' _blkM=' + mn._blkM +
    ' road=' + JSON.stringify(Object.fromEntries(Object.entries(R).map(([k, v]) => [k, v.road]))) +
    ' slots=' + inv.length + ' | core: ' + (cm ? cm.name + ' ' + cm.dims.join('x') : 'none'); })()`));

await dump('mounted:');
// mount each catalog model explicitly (padSwap may not cycle through all)
await evl('mountModel(TABLES.find(q => q.spec.id === "block"), __construct.manifest.tables.find(q => q.id === "block").catalog[1])');
await sleep(5000);
await dump('barber:');
// deep: what does the strip scan actually see on the ring?
console.log('edge scan detail:', await evl(`(() => { const t = TABLES.find(q => q.spec.id === 'block'),
    g = t.g.children.find(c => c.userData.mini && c.userData.mini.static), mn = g && g.userData.mini;
  if (!mn) return 'no ring';
  const W = mn.dims[0], D = mn.dims[2];
  if (!mn._colTop) { const T = mn._colTop = new Int16Array(W * D).fill(-1);
    mn.localCols.forEach(c => { T[c.x + W * c.z] = Math.max.apply(null, c.ys); }); }
  const T = mn._colTop, CURB = 4, out = {};
  [['s', 0, D - 1, 1], ['e', 1, W - 1, 1], ['n', 0, 0, -1], ['w', 1, 0, -1]].forEach(q => {
    const e = q[0], ax = q[1], t0 = q[2], rd = q[3], span = ax ? D : W,
      probes = [.25, .375, .5, .625, .75].map(f => Math.round(span * f));
    const hits = [];
    for (let t = t0, i = 0; i < 140 && t >= 0 && t <= (ax ? W : D) - 1; t -= rd, i++) {
      const n = probes.filter(u => (ax ? T[t + W * u] : T[u + W * t]) > CURB).length;
      if (n >= 3) hits.push(t); }
    out[e] = hits.length ? hits[0] + '..' + hits[hits.length - 1] : 'NONE'; });
  return JSON.stringify(out); })()`));
console.log('errs:', pageErrs.length ? pageErrs.join('||') : 'none');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(0);
