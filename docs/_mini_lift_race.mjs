// _mini_lift_race.mjs — boot with a saved block-table lift + a delayed block stasset so
// emitVox straddles the lift-restore window (entry pre-restore, miniG flagging after).
// Polls liftT/flagT to prove ordering, then reports miniG.position.y vs tables()[1].top.
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
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', 'about:blank'], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });
let ws = null, mid = 0; const pend = new Map();
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
const evl = async e => { const r = await cdp('Runtime.evaluate', { expression: e, awaitPromise: true, returnByValue: true }); return r.result && r.result.result ? r.result.result.value : undefined; };
for (let i = 0; i < 60; i++) { try { const l = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)); const t = l.find(t => t.type === 'page');
  if (t) { ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
    ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
    await cdp('Runtime.enable'); await cdp('Page.enable'); break; } } catch {} await sleep(500); }
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: `
  localStorage.setItem('dvm_lift:block', '0.25');
  const _st = window.setTimeout;
  window.setTimeout = (f, t, ...a) => _st(f, (t || 0) + 400, ...a);            // stretch emitVox chunk yields — widen the vulnerable window
  const _f = window.fetch;
  window.fetch = (u, ...a) => (typeof u === 'string' && u.includes('block_26')
    ? _f(u, ...a).then(r => new Promise(res => _st(() => res(r), 900)))         // emitVox enters pre-restore, flags after
    : _f(u, ...a));
  window.__race = { liftT: null, flagT: null, createT: null };
  const iv = _st(function poll() {
    try {
      const tb = window.__construct && __construct.tables()[1];
      if (tb && tb.lift !== 0 && !__race.liftT) __race.liftT = Math.round(performance.now());
      const rec = window.__construct && __construct.placed.find(p => p.name === 'table:block');
      const mg = rec && rec.g.children.find(c => c.userData.mini);
      const ug = rec && rec.g.children.find(c => c.userData && c.type === 'Group');
      if (ug && !__race.createT) __race.createT = Math.round(performance.now());
      if (mg && !__race.flagT) __race.flagT = Math.round(performance.now());
      if (!__race.flagT) _st(poll, 25);
    } catch (e) { _st(poll, 25); }
  }, 0);` });
await cdp('Page.navigate', { url: 'http://127.0.0.1:8723/construct.html' });
for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
await sleep(1500);
console.log('LIFT', await evl(`JSON.stringify((() => {
  const tb = __construct.placed.find(p => p.name === 'table:block');
  const minis = tb.g.children.filter(c => c.userData.mini).map(c => ({ y: +c.position.y.toFixed(4), name: c.userData.mini.name }));
  return { race: window.__race, tableTop: __construct.tables()[1].top, lift: __construct.tables()[1].lift, minis,
    cars: __construct.placed.filter(p => p._parked).map(c => +c.g.getWorldPosition(new __THREE.Vector3()).y.toFixed(4)) }; })())`));
process.exit(0);
