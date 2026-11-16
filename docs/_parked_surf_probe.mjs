// _parked_surf_probe.mjs — boot normally, then re-run the fixpoint's exact placeSurf math
// against each parked car's STORED x/z (miniG-local treated as world) to see what a
// re-seat would compute. Also sweeps settleOrphans exposure: which candidate world
// boxes would false-positive a parked car via inB.
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
for (let i = 0; i < 160; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
for (let i = 0; i < 80; i++) { const n = await evl('__construct.placed.filter(p => p._parked).length'); if (n >= 2) break; await sleep(500); }
console.log('PROBE', await evl(`JSON.stringify((() => {
  const out = [];
  __construct.placed.filter(p => p._parked).forEach(car => {
    // exact fixpoint call: placeSurf(p.x, p.z, {p}, { surfOnly: p.surf, cap: p.y0 + .01 })
    const wx = car.x, wz = car.z, cap = car.y0 + .01;
    let t = 0; const hits = [];
    __construct.placed.forEach(p => { if (p === car || !p.surf) return;
      let bx = wx, bz = wz; const par = p.g.parent;
      if (par && par !== __scene) { const v = new __THREE.Vector3(wx, 0, wz); par.worldToLocal(v); bx = v.x; bz = v.z; }
      if (bx + .12 < p.bbox[0] || bx - .12 > p.bbox[1] || bz + .12 < p.bbox[2] || bz - .12 > p.bbox[3]) return;
      const s = __construct.surfaceAt(p, wx, wz); if (s > 0) hits.push({ host: p.name, s: +s.toFixed(4) });
      if (s > t && s <= cap) t = s; });
    const w = car.g.getWorldPosition(new __THREE.Vector3());
    out.push({ recXZ: [+wx.toFixed(4), +wz.toFixed(4)], y0: +car.y0.toFixed(4), worldY: +w.y.toFixed(4),
               fixpointWouldSeat: +t.toFixed(4), hostHits: hits });
  });
  return out; })())`));
// settleOrphans exposure — car local coords as world: inside which placed recs' world bboxes?
console.log('INB', await evl(`JSON.stringify((() => {
  return __construct.placed.filter(p => p._parked).map(car => ({
    recXZ: [+car.x.toFixed(3), +car.z.toFixed(3)],
    insideBoxes: __construct.placed.filter(q => q !== car && !q._parked &&
      car.x > q.bbox[0] && car.x < q.bbox[1] && car.z > q.bbox[2] && car.z < q.bbox[3]).map(q => q.name) }));
  })())`));
process.exit(0);
