// _repark_check.mjs — targeted repark law: cruise → off+seek → cars dock at a legal
// lattice pose (curb band |coord|≈.257 + axis-aligned heading). Park-anywhere means
// the docked slot needn't be the origin pose — the law is the slot shape, not the spot.
import { spawn } from 'node:child_process';
import fs from "node:fs";
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
await evl('__cars.toggle(1, true)'); await sleep(2500);
await evl('__cars.toggle(1, false); __cars.seek()');
let d = null;
for (let i = 0; i < 80; i++) { d = JSON.parse(await evl('JSON.stringify(__cars.state())')); if (d && d.length >= 2 && d.every(c => c.st === 'slot')) break; await sleep(300); }
const ok = d && d.every(c => c.st === 'slot') &&
  d.every(c => (Math.abs(Math.abs(c.x) - .257) < .02 || Math.abs(Math.abs(c.z) - .257) < .02) && Math.abs(Math.sin(2 * c.ry)) < .001);
console.log((ok ? 'PASS  repark law — curb band + axis heading' : 'FAIL  repark law — curb band + axis heading'), JSON.stringify(d));
chrome.kill();
setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(ok ? 0 : 1);
