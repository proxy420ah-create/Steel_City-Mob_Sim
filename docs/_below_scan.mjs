// _below_scan.mjs — scan block + zone miniatures for voxels at/below the ground layer,
// and localize any clusters that could "poke through" the plate.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_bsprof_${PORT}_${Date.now()}`);
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
for (let i = 0; i < 120; i++) { const z = await evl('__construct.tables().find(t=>t.id==="studio") && __construct.tables().find(t=>t.id==="studio").models.length'); if (z) break; await sleep(500); }
await sleep(500);
const SCAN = `JSON.stringify((() => { const M = new __THREE.Matrix4(), P = new __THREE.Vector3(), Q = new __THREE.Quaternion(), S = new __THREE.Vector3();
  const dump = (tid) => { const t = __construct.tables().find(t => t.id === tid); if (!t || !t.models[0]) return null;
    const g = __scene.getObjectByProperty('uuid', null) || null;
    const rec = __construct.placed.find(p => p.name === 'table:' + tid); if (!rec) return null;
    const mg = rec.g.children.find(c => c.userData && c.userData.mini); if (!mg) return null;
    const rows = [];
    mg.traverse(o => { if (!o.isInstancedMesh) return;
      let lo = 1e9, hi = -1e9, loC = 0, n = o.count;
      for (let i = 0; i < n; i++) { o.getMatrixAt(i, M); M.decompose(P, Q, S);
        if (P.y < lo) lo = P.y; if (P.y > hi) hi = P.y; if (P.y < -0.0002) loC++; }
      rows.push({ count: n, loY: +lo.toFixed(5), hiY: +hi.toFixed(5), below0: loC, mat: (o.material.userData && o.material.userData.mid) ?? null }); });
    return rows; };
  return { block: dump('block'), zone: dump('studio') }; })())`;
const r = JSON.parse(await evl(SCAN));
console.log('BLOCK'); (r.block || []).forEach(m => console.log(' ', JSON.stringify(m)));
console.log('ZONE'); (r.zone || []).forEach(m => console.log(' ', JSON.stringify(m)));
process.exit(0);
