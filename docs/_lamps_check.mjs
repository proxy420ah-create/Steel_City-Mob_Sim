// _lamps_check.mjs — exhibit-lights law: pendants ride their tables; the switch dims
// the house rig + lights the spots; persists through dvm_lights.
import { spawn } from 'node:child_process';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_shotprof_${PORT}_${Date.now()}`);
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
let pass = 0; const fails = [];
const check = (name, ok, detail) => { if (ok) { pass++; console.log('PASS ', name, detail ?? ''); } else { fails.push(name); console.log('FAIL ', name, detail ?? ''); } };
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
await sleep(1500);                                                       // pendant fetch lands async

check('lamps rig live + default on', await evl('!!window.__lamps && __lamps.on() === true'));
const lampState = JSON.parse(await evl(`JSON.stringify((() => {
  const out = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 3.5) out.push({ i: +o.intensity.toFixed(2), parented: !!o.parent }); });
  return out; })())`));
check('three pendant spots live, all lit', lampState.length === 3 && lampState.every(l => l.i > 0), JSON.stringify(lampState));
const house = JSON.parse(await evl('JSON.stringify((() => { const h = []; scene.traverse(o => { if (o.isHemisphereLight || (o.isPointLight && o.distance === 6)) h.push(+o.intensity.toFixed(2)); }); return h; })())'));
check('house dimmed — hemi .22 / key .5', house[0] === .22 && house[1] === .5, JSON.stringify(house));
check('void dimmed — bg/fog/floor go dark', await evl('scene.background.getHex() === 0x13141c && scene.fog.color.getHex() === 0x13141c && floorMat.color.getHex() === 0x13141c'));

/* pendants ride their table — drag the primary, its spot follows */
const before = JSON.parse(await evl(`JSON.stringify((() => { const s = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 3.5) { const v = new o.position.constructor(); o.getWorldPosition(v); s.push(+v.x.toFixed(2)); } }); return s.sort(); })())`));
await evl('(() => { const rec = __construct.placed.find(p => p.name === "table:primary"); placeApply(rec, -3.6, -1.1); return true; })()');
const after = JSON.parse(await evl(`JSON.stringify((() => { const s = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 3.5) { const v = new o.position.constructor(); o.getWorldPosition(v); s.push(+v.x.toFixed(2)); } }); return s.sort(); })())`));
await evl('(() => { const rec = __construct.placed.find(p => p.name === "table:primary"); placeApply(rec, -4.1, -1.1); return true; })()');
check('pendant spot world-x followed the table drag', JSON.stringify(before) !== JSON.stringify(after), JSON.stringify({ before, after }));

await evl('__lamps.set(false)');
const dimmed = JSON.parse(await evl('JSON.stringify((() => { const s = [], h = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 3.5) s.push(o.intensity); if (o.isHemisphereLight) h.push(o.intensity); }); return { s, h }; })())'));
check('off — spots out, house restored', dimmed.s.every(i => i === 0) && dimmed.h[0] === 1.2, JSON.stringify(dimmed));
check('off — void restored to VOID cream', await evl('scene.background.getHex() === 0xf4f1e8 && floorMat.color.getHex() === 0xf4f1e8'));
check('state persisted — dvm_lights=0', await evl('localStorage.getItem("dvm_lights") === "0"'));
await evl('__lamps.set(true)');
check('back on — dvm_lights=1', await evl('localStorage.getItem("dvm_lights") === "1" && __lamps.on() === true'));
const errs = JSON.parse(await evl('JSON.stringify((window.__errs || []).slice(0, 5))'));
check('no page errors', !(errs && errs.length), JSON.stringify(errs));
console.log(fails.length ? 'FAILURES: ' + JSON.stringify(fails) : 'ALL PASS');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(fails.length ? 1 : 0);
