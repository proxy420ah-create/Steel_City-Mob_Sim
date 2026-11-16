// _park_repro2.mjs — deeper: instrument claim/held INSIDE the live page to see why slots skip.
//   User path: hold pad at block, press cars, cruise ~4 s, press again, watch 20 s.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_pr2prof_${PORT}`);
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
  else if (m.method === 'Runtime.exceptionThrown') pageErrs.push(JSON.stringify(m.params.exceptionDetails).slice(0, 400)); };
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
await cdp('Runtime.enable');
const evl = async expr => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 500));
  return r.result && r.result.result ? r.result.result.value : undefined; };

for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
// instrument: wrap claim + held to dump the decision inputs the first N calls
await evl(`window.__dbg = []; const oc = blockTrf.claim.bind(blockTrf), oh = blockTrf.held.bind(blockTrf);
  blockTrf.held = sc => { const r = oh(sc); if (window.__dbg.length < 60) window.__dbg.push({ t: 'held', e: sc.edge, a: Math.round(sc.along), r }); return r; };
  blockTrf.claim = (c, s) => { const inv = blockTrf.inv || [], r = oc(c, s);
    if (window.__dbg.length < 60) window.__dbg.push({ t: 'claim', s: Math.round(s), inv: inv.length,
      free: inv.filter(sl => !sl.by).length, r: r ? r.sc.edge + '@' + Math.round(r.sc.along) : null });
    return r; };`);
// user path
await evl('(() => { const t = __construct.tables().find(q => q.id === "block"); pos.x = t.x; pos.z = t.z + 1.2; takeIt(__pad.rec()); })()');
await sleep(200);
console.log('padTable →', await evl('padTable().spec.id'));
console.log('press1 on →', await evl('__pad.press("cars"), blockTrf.on'));
await sleep(4000);
console.log('cruise:', await evl('__cars.state().map(c=>c.st+":"+Math.round(c.s)).join(" | ")'));
console.log('press2 off →', await evl('__pad.press("cars"), blockTrf.on'));
for (let i = 0; i < 20; i++) {
  await sleep(1000);
  const st = await evl('__cars.state().map(c=>c.st+":"+Math.round(c.s)+":"+(c.tgt||"-")).join(" | ")');
  console.log(`t+${i + 1}s`, st);
  if ((st.match(/slot/g) || []).length === 2) break;
}
console.log('inv:', await evl('JSON.stringify(__cars.inv())'));
console.log('dbg:', await evl('JSON.stringify(window.__dbg.slice(0, 40))'));
console.log('page errors:', pageErrs.length ? pageErrs.join(' || ') : 'none');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(0);
