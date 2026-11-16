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

check('lamps rig live + default OFF (bright construct)', await evl('!!window.__lamps && __lamps.on() === false'));
const boot = JSON.parse(await evl('JSON.stringify((() => { const s = [], h = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 4) s.push(o.intensity); if (o.isHemisphereLight) h.push(o.intensity); }); return { s, h, bg: scene.background.getHex() }; })())'));
check('boot bright — spots out, hemi 1.2, VOID cream', boot.s.every(i => i === 0) && boot.h[0] === 1.2 && boot.bg === 0xf4f1e8, JSON.stringify(boot));
await evl('__lamps.set(true)');
const lampState = JSON.parse(await evl(`JSON.stringify((() => {
  const out = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 4) out.push({ i: +o.intensity.toFixed(2), parented: !!o.parent }); });
  return out; })())`));
check('five pendant spots live, all lit (3 tables + rotator + lawdesk)', lampState.length === 5 && lampState.every(l => l.i > 0), JSON.stringify(lampState));
check('rotator lamp hangs on spinG, not the spinning carG', await evl('(() => { let a = false, b = false; spinG.traverse(o => { if (o.isSpotLight && o.distance === 4) a = true; }); carG.traverse(o => { if (o.isSpotLight) b = true; }); return a && !b; })()'))
const house = JSON.parse(await evl('JSON.stringify((() => { const h = []; scene.traverse(o => { if (o.isHemisphereLight || (o.isPointLight && o.distance === 6)) h.push(+o.intensity.toFixed(2)); }); return h; })())'));
check('house dimmed — hemi .22 / key .5', house[0] === .22 && house[1] === .5, JSON.stringify(house));
check('void dimmed — bg/fog/floor go dark', await evl('scene.background.getHex() === 0x13141c && scene.fog.color.getHex() === 0x13141c && floorMat.color.getHex() === 0x13141c'));

/* pendants ride their table — drag the primary, its spot follows */
const before = JSON.parse(await evl(`JSON.stringify((() => { const s = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 4) { const v = new o.position.constructor(); o.getWorldPosition(v); s.push(+v.x.toFixed(2)); } }); return s.sort(); })())`));
await evl('(() => { const rec = __construct.placed.find(p => p.name === "table:primary"); placeApply(rec, -3.6, -1.1); return true; })()');
const after = JSON.parse(await evl(`JSON.stringify((() => { const s = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 4) { const v = new o.position.constructor(); o.getWorldPosition(v); s.push(+v.x.toFixed(2)); } }); return s.sort(); })())`));
await evl('(() => { const rec = __construct.placed.find(p => p.name === "table:primary"); placeApply(rec, -4.1, -1.1); return true; })()');
check('pendant spot world-x followed the table drag', JSON.stringify(before) !== JSON.stringify(after), JSON.stringify({ before, after }));

await evl('__lamps.set(false)');
const dimmed = JSON.parse(await evl('JSON.stringify((() => { const s = [], h = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 4) s.push(o.intensity); if (o.isHemisphereLight) h.push(o.intensity); }); return { s, h }; })())'));
check('off — spots out, house restored', dimmed.s.every(i => i === 0) && dimmed.h[0] === 1.2, JSON.stringify(dimmed));
check('off — void restored to VOID cream', await evl('scene.background.getHex() === 0xf4f1e8 && floorMat.color.getHex() === 0xf4f1e8'));
check('state persisted — dvm_lights=0', await evl('localStorage.getItem("dvm_lights") === "0"'));
await evl('__lamps.set(true)');
check('back on — dvm_lights=1', await evl('localStorage.getItem("dvm_lights") === "1" && __lamps.on() === true'));

/* shades — the fixture meshes hide alone; the light keeps pooling */
await evl('__lamps.setShades(false)');
await sleep(300);
const shadesOff = JSON.parse(await evl('JSON.stringify({ v: __lamps.fixtureVisible(), spots: (() => { const s = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 4) s.push(o.intensity); }); return s; })() })'));
check('shades OFF — all five fixtures hidden, spots still lit', shadesOff.v.length === 5 && shadesOff.v.every(x => x === false) && shadesOff.spots.every(i => i > 0), JSON.stringify(shadesOff));
check('room still dimmed with shades hidden', await evl('scene.background.getHex() === 0x13141c'));
check('shades persisted — dvm_shades=0', await evl('localStorage.getItem("dvm_shades") === "0"'));
await evl('__lamps.setShades(true)');
check('shades back on — fixtures visible', await evl('__lamps.shades() === true && __lamps.fixtureVisible().every(x => x === true)'));
check('two rockers — aimed buttons, not proximity entries', await evl('SW_BTNS.length === 2 && INTERACT.filter(i => Math.abs(i.x + 3.04) < .01 && i.r <= .3).length === 0'));
/* the wire ghost owns the aimed rocker — kiosk law (proximity prompt is gone by design) */
await evl('(() => { pos.set(-2.6, 0, -1.6); __cam(-3.6, .55, -1.67, -3.06, .51, -1.67); return true; })()');
await sleep(200);
check('aim north rocker — LIGHTS btn owns the ray + ghost seats on it', await evl('(() => { const b = swProbe(); return !!(b && /LIGHTS|EXHIBIT/.test(b.label()) && Math.abs(swWire.position.z + .07) < .001); })()'));
await evl('__cam(-3.6, .55, -1.53, -3.06, .51, -1.53)');
await sleep(200);
check('aim south rocker — SHADES btn owns the ray', await evl('(() => { const b = swProbe(); return !!(b && /SHADES/.test(b.label()) && Math.abs(swWire.position.z - .07) < .001); })()'));
await evl('__cam(null); pos.set(0, 0, 0)');

/* the attached light is a property of the rec — pendSet removes/restores it, placeSave carries it */
check('lawdesk carries a pendant by default', await evl('(() => { const d = __construct.placed.find(p => p.name === "commission_lawdesk"); return !!(d && d.pend); })()'));
await evl('(() => { const d = __construct.placed.find(p => p.name === "commission_lawdesk"); __lamps.pendSet(d, false); return true; })()');
const detached = JSON.parse(await evl('JSON.stringify((() => { const s = []; scene.traverse(o => { if (o.isSpotLight && o.distance === 4) s.push(1); }); const d = __construct.placed.find(p => p.name === "commission_lawdesk"); return { n: s.length, pend: !!d.pend, want: d.pendWant, lamps: __lamps.count() }; })())'));
check('pend off — fixture + spot leave the scene and LAMPS', detached.n === 4 && !detached.pend && detached.want === 0 && detached.lamps === 4, JSON.stringify(detached));
await evl('(() => { const d = __construct.placed.find(p => p.name === "commission_lawdesk"); __lamps.pendSet(d, true); return true; })()');
check('pend on — pendant re-attaches to the rec', await evl('(() => { const d = __construct.placed.find(p => p.name === "commission_lawdesk"); return !!(d.pend && d.pendWant === 1) && __lamps.count() === 5; })()'));
check('pend on — spot born lit (no switch-flip wait)', await evl('(() => { const d = __construct.placed.find(p => p.name === "commission_lawdesk"); return d.pend.spot.intensity === 16 && d.pend.glow.intensity === .5; })()'));
check('pend state persisted — dvm_place lawdesk pend=1', await evl('(() => { placeSave(); const o = JSON.parse(localStorage.getItem("dvm_place")); return o.commission_lawdesk && o.commission_lawdesk.pend === 1; })()'));
check('non-table rec defaults to no pendant', await evl('(() => { const s = __construct.placed.find(p => p.name === "commission_stamper"); return s && s.pendWant === 0 && !s.pend; })()'));
const errs = JSON.parse(await evl('JSON.stringify((window.__errs || []).slice(0, 5))'));
check('no page errors', !(errs && errs.length), JSON.stringify(errs));
console.log(fails.length ? 'FAILURES: ' + JSON.stringify(fails) : 'ALL PASS');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(fails.length ? 1 : 0);
