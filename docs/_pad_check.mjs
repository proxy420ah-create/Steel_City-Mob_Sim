// _pad_check.mjs — pad v2 law: BLDG/BLOCK/ZONE tabs own their tables (no proximity remote),
// regions fit the 512×340 deck and never overlap, slice hides on street tabs, DECK keeps working.
import { spawn } from 'node:child_process';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_padprof_${PORT}_${Date.now()}`);
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
await sleep(1200);

check('boot page is BLDG', await evl('__pad.page() === "bldg"'));
check('required tabs exist — BLDG BLOCK ZONE DECK LAMP ≡', await evl(
  '["bldg","block","zone","deck","lamp","apps"].every(id => PAD_REGIONS.some(r => r.id === "tab:" + id))'));
check('generic pages are gone (model/rot/slice/table)', await evl(
  '!PAD_PAGES.model && !PAD_PAGES.rot && !PAD_PAGES.slice && !PAD_PAGES.table'));
const bind = JSON.parse(await evl(`JSON.stringify((() => { const o = {};
  __pad.tab('bldg'); o.bldg = padTable().spec.id;
  __pad.tab('block'); o.block = padTable().spec.id;
  __pad.tab('zone'); o.zone = padTable().spec.id; return o; })())`));
check('tabs bind their tables — bldg→primary block→block zone→studio',
  bind.bldg === 'primary' && bind.block === 'block' && bind.zone === 'studio', JSON.stringify(bind));
check('binding ignores proximity — move the eye, tab still owns', await evl(`(() => {
  const ox = pos.x, oz = pos.z; pos.x = 99; pos.z = -99;
  const ok = (__pad.tab('zone'), padTable().spec.id === 'studio') && (__pad.tab('bldg'), padTable().spec.id === 'primary');
  pos.x = ox; pos.z = oz; return ok; })()`));
check('every region fits the 512×340 deck', await evl(
  'PAD_REGIONS.every(r => r.x >= 0 && r.y >= 0 && r.x + r.w <= 512 && r.y + r.h <= 340)'));
const overlap = JSON.parse(await evl(`JSON.stringify((() => { const bad = [];
  Object.keys(PAD_PAGES).forEach(p => { const rs = PAD_REGIONS.filter(r => r.tab || r.page === p);
    for (let i = 0; i < rs.length; i++) for (let j = i + 1; j < rs.length; j++) { const a = rs[i], b = rs[j];
      if (a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h) bad.push(p + ':' + a.id + ' x ' + b.id); } });
  return bad; })())`));
check('no button overlap on any page', overlap.length === 0, overlap.join(', '));
check('slice band hides on street tabs', await evl(`(() => {
  const uv = { x: (168 + 31) / 512, y: 1 - (216 + 20) / 340 };
  __pad.tab('block'); const b = __pad.hit(uv);
  __pad.tab('bldg'); const g = __pad.hit(uv);
  return b === null && g === 'bldg:sl0'; })()`));
check('CARS rides only the BLOCK tab', await evl(
  'PAD_REGIONS.filter(r => r.id === "block:cars").length === 1 && !PAD_REGIONS.some(r => /^(bldg|zone):cars/.test(r.id))'));
check('spin press binds the tab\'s table', await evl(`(() => {
  __pad.tab('block'); __pad.press('block:sp3');
  const m1 = TABLES[1].g.children.find(c => c.userData.mini && !c.userData.mini.static);
  __pad.tab('bldg'); __pad.press('bldg:sp1');
  const m0 = TABLES[0].g.children.find(c => c.userData.mini && !c.userData.mini.static);
  const ok = m1.userData.mini.spin === .9 && m0.userData.mini.spin === -.25;
  m1.userData.mini.spin = 0; m0.userData.mini.spin = 0; return ok; })()`));
check('lift press binds the tab\'s table', await evl(`(() => {
  __pad.tab('bldg'); __pad.press('bldg:lf2');
  const ok = TABLES[0].lift === .02 && TABLES[1].lift === 0;
  __pad.tab('zone'); __pad.press('zone:lres');
  return ok && TABLES[2].lift === 0 && TABLES[0].lift === .02 ? (TABLES[0].lift = 0, tableLiftSet(TABLES[0], 0), true) : false; })()`));
const modelBefore = await evl('TABLES[1].spec.models[0].name');
await evl('__pad.tab("block"); __pad.press("block:next")');
let modelAfter = modelBefore;
for (let i = 0; i < 40; i++) { modelAfter = await evl('TABLES[1].spec.models[0].name'); if (modelAfter !== modelBefore) break; await sleep(250); }
check('model swap binds the tab\'s table (block cycles, primary untouched)',
  modelAfter !== modelBefore && await evl('TABLES[0].spec.models[0].name === "tenement_block_0"'),
  modelBefore + ' -> ' + modelAfter);
await evl('__pad.press("block:prev")');
check('DECK still owns the shop lift', await evl(`(() => {
  __pad.tab('deck'); __pad.press('d:eye'); const up = spinG.position.y === .45;
  __pad.press('d:res'); return up && spinG.position.y === 0; })()`));
check('padDraw renders every page without throwing', await evl(`(() => {
  try { Object.keys(PAD_PAGES).forEach(p => { __pad.tab(p); padDraw(); }); __pad.tab('bldg'); return true; }
  catch (e) { return e.message; } })()`));
check('held dolly — wheel zooms the carry distance', await evl(`(() => {
  __construct.take('debug_pad');
  const d0 = heldD;
  dispatchEvent(new WheelEvent('wheel', { deltaY: 120 }));           // scroll down → toward you
  const near = heldD;
  dispatchEvent(new WheelEvent('wheel', { deltaY: -120 }));          // scroll up → away
  const far = heldD;
  for (let i = 0; i < 40; i++) dispatchEvent(new WheelEvent('wheel', { deltaY: 120 }));
  const lo = heldD;
  for (let i = 0; i < 40; i++) dispatchEvent(new WheelEvent('wheel', { deltaY: -120 }));
  const hi = heldD;
  const pers = localStorage.getItem('dvm_holdd'), dist = held.g.position.distanceTo(camera.position);
  localStorage.removeItem('dvm_holdd'); heldD = .38; __construct.place(-4.1, -.575);
  return near === d0 - .06 && far === d0 && lo === .18 && hi === 1.6 && pers === '1.600' && dist > 1.5;
})()`));

console.log(`\n${pass} passed, ${fails.length} failed`); if (fails.length) { console.log('FAILED:', fails.join(' | ')); process.exit(1); }
try { chrome.kill(); } catch {}
try { fs.rmSync(prof, { recursive: true, force: true }); } catch {}
