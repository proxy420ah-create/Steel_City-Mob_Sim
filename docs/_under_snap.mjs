// _under_snap.mjs — underside/inside views of block + zone miniatures via __snap,
// plus a scan for car-material voxels at/below each composite's ground layer.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_usprof_${PORT}_${Date.now()}`);
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', '--window-size=1600,900', 'http://127.0.0.1:8723/construct.html'], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });
let ws = null, mid = 0; const pend = new Map();
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
const evl = async e => { const r = await cdp('Runtime.evaluate', { expression: e, awaitPromise: true, returnByValue: true }); return r.result && r.result.result ? r.result.result.value : undefined; };
for (let i = 0; i < 60; i++) { try { const l = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)); const t = l.find(t => t.type === 'page' && t.url.includes('construct'));
  if (t) { ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
    ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
    await cdp('Runtime.enable'); break; } } catch {} await sleep(500); }
for (let i = 0; i < 120; i++) { if (await evl('!!window.__place && !!window.__warmDone')) break; await sleep(500); }
for (let i = 0; i < 80; i++) { const n = await evl('__construct.placed.filter(p=>p._parked).length'); if (n === 2) break; await sleep(500); }
await sleep(800);
// 1 — where do the block miniature's ground voxels sit (world y histogram of layer 0/1)?
console.log('BLOCKSCAN', await evl(`JSON.stringify((() => { const t = __construct.tables().find(t => t.id === 'block'), mn = t && t.models[0];
  const out = { roadTop: null, loY: [], mat143: 0, matLow: {} };
  if (!mn) return out;
  // sample instance matrices — lowest instance y per mesh
  mn.g ? 0 : 0; return out; })())`));
// simpler: raycast DOWN through the road from above and UP from below to see layer stack
console.log('LAYERSTACK', await evl(`JSON.stringify((() => { const T = __THREE, R = new T.Raycaster();
  const mn = __scene.children.find(o => o.userData && o.userData.miniG) || null;
  // find the block miniG via a parked car's parent
  const car = __construct.placed.filter(p => p._parked)[0], g = car.g.parent;
  const up = new T.Vector3(0,1,0), dn = new T.Vector3(0,-1,0), hits = [];
  // under the south car at (-6.397,-.972): cast UP from below the plate, record each face hit
  R.set(new T.Vector3(-6.397, .20, -0.972), up);
  R.intersectObject(g, true).forEach(h => hits.push(+h.point.y.toFixed(4)));
  R.set(new T.Vector3(-6.397, .40, -0.972), dn);
  R.intersectObject(g, true).forEach(h => hits.push(+h.point.y.toFixed(4)));
  return hits.slice(0, 20); })())`));
// 2 — snap: from below the block miniature looking up along the plate (mimic user's frame)
for (const [name, px,py,pz, tx,ty,tz] of [
  ['under_blk',  -6.30, .200, -0.700,  -6.45, .265, -1.05],
  ['under_blk2', -6.50, .240, -0.750,  -6.35, .262, -1.00],
  ['under_zone', -6.30, .180, -3.30,   -6.45, .262, -3.80],
]) console.log('SNAP', name, await evl(`__snap('${name}', ${px},${py},${pz}, ${tx},${ty},${tz})`));
process.exit(0);
