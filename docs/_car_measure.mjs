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
    cars = __construct.placed.filter(p => p._parked), V = new __THREE.Vector3(),
    core = __construct.tables()[1].models.find(m => !m.static) || {};   // models[] includes the static street ring — the swappable core is the model
  return { vox: core.vox, model: core.name,
    cars: cars.map(c => { const w = c.g.getWorldPosition(V.set(0,0,0));
      return { x: +w.x.toFixed(4), z: +w.z.toFixed(4), y: +w.y.toFixed(4),
        roof: __construct.surfaceAt(c, w.x, w.z, 0) }; }) }; })())`;
console.log('MEASURE', await evl(readCars));
// scene census — where are the ring + the cars + the rotator pieces, in world space
console.log('CENSUS', await evl(`JSON.stringify((() => { const out = { rings: [], cars: [], spin: null };
  __scene.traverse(o => { const w = o.getWorldPosition(new __THREE.Vector3());
    if (o.geometry && o.geometry.type === 'ShapeGeometry') out.rings.push({ x: +w.x.toFixed(3), z: +w.z.toFixed(3), y: +w.y.toFixed(3) });
    if (o.name === 'spinG' || (o.userData && o.userData.spinG)) out.spin = [w.x, w.y, w.z]; });
  __construct.placed.filter(p => p.name === 'vehicle_490_touring').forEach(p => {
    const w = p.g.getWorldPosition(new __THREE.Vector3());
    out.cars.push({ parked: !!p._parked, local: p.g.position.toArray().map(v => +v.toFixed(3)), world: [w.x, w.y, w.z].map(v => +v.toFixed(3)), parent: p.g.parent.name || p.g.parent.type }); });
  return out; })())`));
// spinner car — local + world placement vs the rotator ring (spinG at (0,0,-2.5))
console.log('SPINNER', await evl(`JSON.stringify((() => { const car = __construct.placed.find(p => p.name === 'vehicle_490_touring' && !p._parked);
  if (!car) return 'no spinner rec';
  const w = car.g.getWorldPosition(new __THREE.Vector3());
  return { gLocal: car.g.position.toArray().map(v => +v.toFixed(4)), gWorld: [w.x, w.y, w.z].map(v => +v.toFixed(4)),
    parentPos: car.g.parent.position.toArray(), recXZ: [car.x, car.z], lb: car.lb.map(v => +v.toFixed(4)),
    children: car.g.children.map(ch => ({ t: ch.type, px: +ch.position.x.toFixed(4), pz: +ch.position.z.toFixed(4), vis: ch.visible })) }; })())`));
await evl('__construct.take("debug_pad"); __construct.place(-4.1,-3.375); __pad.tab("model")');
await sleep(300);
await evl('__pad.press("next")'); await sleep(6000);                                   // swap → block_41_barber core; ring + cars untouched
console.log('MEASURE2', await evl(readCars));
process.exit(0);
