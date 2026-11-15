// _torch_check.mjs — is the pad tune applied to the torch in place mode?
// Sets a distinctive tune (PWR 9, red, wide), toggles place mode, reads torch internals.
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
await sleep(500);

// stock state
console.log('STOCK', await evl(`JSON.stringify({cfg: __torch.cfg, i: __torch.spot.intensity, f: __torch.fill.intensity, angle: __torch.spot.angle, col: __torch.spot.color.getHexString()})`));
// distinctive tune via the pad path
await evl('__torch.tune("i", 9); __torch.tune("c", 3); __torch.tune("a");');   // PWR→9, red swatch, beam→wide(.65)
await evl('__torch.setOn(true)');
await sleep(400);
console.log('TUNED_FP', await evl(`JSON.stringify({i: __torch.spot.intensity, f: __torch.fill.intensity, angle: __torch.spot.angle, col: __torch.spot.color.getHexString(), d: __torch.dist(__camera)})`));
// place mode — same tune, fly camera
await evl('__place.toggle(true)');
await sleep(600);
console.log('TUNED_FLY', await evl(`JSON.stringify({placeOn: __place.on(), i: __torch.spot.intensity, f: __torch.fill.intensity, angle: __torch.spot.angle, col: __torch.spot.color.getHexString(), d: __torch.dist(__camera), camY: __camera.position.y})`));
// RAW vs AUTO at free-cam inspection range — synthetic camera .25m over the block table
const close = await evl(`JSON.stringify((() => {
  const c = new __THREE.PerspectiveCamera(58, 1.5, .02, 60);
  c.position.set(-6.4, .55, -1.3); c.lookAt(-6.4, .28, -1.3); c.updateMatrixWorld(true);
  const out = {};
  __torch.aim(c); out.auto = { d: __torch.dist(c), i: __torch.spot.intensity, f: __torch.fill.intensity, ang: __torch.spot.angle };
  __torch.tune('x');                                                   // AUTO → RAW
  __torch.aim(c); out.raw = { d: __torch.dist(c), i: __torch.spot.intensity, f: __torch.fill.intensity, ang: __torch.spot.angle };
  __torch.tune('x'); return out; })())`);
console.log('CLOSE_RANGE', close);
await evl('__place.toggle(false); __torch.rst()');
console.log('DONE');
process.exit(0);
