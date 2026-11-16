// _noslice_verify.mjs — regression for the street-table slice lock + miniSettle +
// parked-car await in _mountP. Boots construct.html headless and asserts:
//   1. spec.noSlice on block+studio, not primary
//   2. slice controls dead on 'block' (fn guard + uv filter), alive on 'primary'
//   3. miniSettle — a rider follows its column top DOWN on a y-slice and UP on reset
//   4. extras-on-roof — a dvm_extra at a parked car's x,z seats on the ROOF after reload
//        (pre-fix _mountP resolved before the car emitted → the extra read the road top)
//   node _noslice_verify.mjs        — fresh headless chrome, exits 0 on all-pass
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_nsvprof_${PORT}`);
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
  else if (m.method === 'Runtime.exceptionThrown') pageErrs.push(JSON.stringify(m.params.exceptionDetails).slice(0, 200)); };
const cdp = (method, params = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method, params })); });
await cdp('Runtime.enable');
const evl = async expr => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 400));
  return r.result && r.result.result ? r.result.result.value : undefined; };

const results = [];
const check = (name, ok, detail = '') => { results.push([name, !!ok]); console.log((ok ? '  ok  ' : ' FAIL ') + name + (detail ? '  — ' + detail : '')); };

// ── Phase 0: warm boot ──────────────────────────────────────────────────────
for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
check('warm boot', await evl('!!window.__warmDone'));

// ── Phase 1: spec flags ─────────────────────────────────────────────────────
const flags = await evl('__construct.manifest.tables.map(t => [t.id, !!t.noSlice])');
check('noSlice flags = [primary:false, block:true, studio:true]',
  JSON.stringify(flags) === '[["primary",false],["block",true],["studio",true]]', JSON.stringify(flags));

// ── Phase 2: slice lock on the block table ──────────────────────────────────
await evl(`__pad.tab('block')`);                                                  // the tab IS the binding now — no proximity placement needed
check('pad bound to block', (await evl('__pad.table()')) === 'block', await evl('__pad.table()'));
const sl0 = await evl('JSON.stringify(__construct.tables()[1].models.find(m => !m.static).slice)');   // models[0] is the static ring now — the core is the slice target
await evl(`__pad.press('block:sl2'); __pad.press('block:sl1'); __pad.press('block:axx'); __pad.press('block:sres')`);  // MAX− MIN+ axis-X RST — every verb is guarded
const sl1 = await evl('JSON.stringify(__construct.tables()[1].models.find(m => !m.static).slice)');
check('slice controls no-op on street table', sl0 === sl1, sl0 + ' → ' + sl1);
check('slice band uv-filtered (hit → null)', (await evl('__pad.hit({x:.389,y:.306})')) === null, String(await evl('__pad.hit({x:.389,y:.306})')));

// ── Phase 3: miniSettle — rider follows its top through a slice + reset ──────
await evl(`__pad.tab('bldg')`);                                                  // BLDG owns the primary — slice alive here
check('pad bound back to primary', (await evl('__pad.table()')) === 'primary', await evl('__pad.table()'));
await evl(`__construct.take('commission_desklamp') && __construct.place(-4.1, -1.1)`);   // lamp onto the primary model's roof
await sleep(300);
const lampY0 = await evl(`__construct.placed.find(p => p.name === 'commission_desklamp').y0`);
check('lamp seated on the model', lampY0 > .35, 'y0=' + lampY0);
const truth0 = await evl(`worldSurfaceAt(-4.1, -1.1, __construct.placed.find(p => p.name === 'commission_desklamp'))`);
check('seat = measured truth', Math.abs(lampY0 - truth0) < 1e-4, lampY0 + ' vs ' + truth0);
await evl(`for (let i = 0; i < 6; i++) __pad.press('bldg:sl2')`);                // MAX− ×6 — cut the y-window down
await sleep(300);
const lampY1 = await evl(`__construct.placed.find(p => p.name === 'commission_desklamp').y0`);
const truth1 = await evl(`worldSurfaceAt(-4.1, -1.1, __construct.placed.find(p => p.name === 'commission_desklamp'))`);
check('lamp dropped with the cut (settle, not float)', lampY1 < lampY0 - .005 && Math.abs(lampY1 - truth1) < 1e-4,
  lampY0 + ' → ' + lampY1 + ' (truth ' + truth1 + ')');
await evl(`__pad.press('bldg:sres')`);                                           // reset → tops restore → settle climbs back
await sleep(300);
const lampY2 = await evl(`__construct.placed.find(p => p.name === 'commission_desklamp').y0`);
const truth2 = await evl(`worldSurfaceAt(-4.1, -1.1, __construct.placed.find(p => p.name === 'commission_desklamp'))`);
check('lamp climbs back on reset (settle is two-way)', lampY2 > lampY1 + .005 && Math.abs(lampY2 - truth2) < 1e-4,
  lampY1 + ' → ' + lampY2 + ' (truth ' + truth2 + ')');

// ── Phase 4: extras-on-roof race — seed dvm_extra at a parked car, reload ────
const carXZ = await evl(`(function(){ const V = new THREE.Vector3(), c = __construct.placed.find(p => p._parked);
  c.g.getWorldPosition(V); return [+V.x.toFixed(4), +V.z.toFixed(4)]; })()`);
check('parked car found', Array.isArray(carXZ) && carXZ.length === 2, JSON.stringify(carXZ));
await evl(`localStorage.setItem('dvm_extra', JSON.stringify([{ name: 'commission_stamper', kind: 'item', x: ${carXZ[0]}, z: ${carXZ[1]} }]));
  localStorage.removeItem('dvm_place'); location.reload();`);                    // seed + reload — the boot awaits _mountP now
await sleep(1000);
for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
check('re-warm after reload', await evl('!!window.__warmDone'));
const stY = await evl(`(__construct.placed.find(p => p.name === 'commission_stamper' && p.source === 'extra') || {}).y0`);
check('extra seated on the car ROOF, not the road (.2664)', stY > .29, 'y0=' + stY);
check('no page errors the whole run', pageErrs.length === 0, pageErrs.join(' | '));

const bad = results.filter(r => !r[1]);
console.log(bad.length ? `\n${bad.length} FAIL` : '\nALL PASS');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(bad.length ? 1 : 0);
