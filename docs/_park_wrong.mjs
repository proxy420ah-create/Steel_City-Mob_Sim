// _park_wrong.mjs — CARS pressed while padTable resolves a NON-block table.
//   Pre-fix: rebuild on the wrong model → cars 'gone'/frozen or empty lattice → never park.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_pwprof_${PORT}`);
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', URL0], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });
async function pageWs() { for (let i = 0; i < 60; i++) { try { const t = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)).find(t => t.type === 'page' && t.url.includes('construct')); if (t) return t.webSocketDebuggerUrl; } catch {} await sleep(500); } throw new Error('no page'); }
const ws = new WebSocket(await pageWs()); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
let mid = 0; const pend = new Map(); const pageErrs = [];
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } else if (m.method === 'Runtime.exceptionThrown') pageErrs.push(JSON.stringify(m.params.exceptionDetails).slice(0, 300)); };
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
await cdp('Runtime.enable');
const evl = async expr => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 400));
  return r.result && r.result.result ? r.result.result.value : undefined; };
for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
// arm cars at the block, drive a bit, then press OFF while "standing at" each other table
console.log('arm:', await evl('__cars.toggle(1, true)'));
await sleep(3000);
console.log('cruise:', await evl('__cars.state().map(c=>c.st+":"+Math.round(c.s)).join(" | ")'));
// simulate the wrong-table press: pass studio + primary table objects explicitly
for (const ti of [2, 0]) {
  console.log(`press on TABLES[${ti}] (${await evl('TABLES[' + ti + '].spec.id')}):`, await evl(`(() => { blockTrf.toggle(TABLES[${ti}], false); return blockTrf.on; })()`));
  await sleep(300);
  console.log('  states:', await evl('__cars.state().map(c=>c.st+":"+Math.round(c.s)+":"+(c.tgt||"-")).join(" | ")'), '| g ok:', await evl('blockTrf.cars.every(c => c.rec.g.parent === blockTrf.g)'));
}
// the real park: off-press resolved the host itself → cars should still claim + park
await sleep(6000);
console.log('6s later:', await evl('__cars.state().map(c=>c.st+":"+Math.round(c.s)).join(" | ")'));
console.log('parked edges:', await evl('__construct.placed.filter(p=>p._parked).map(p=>p._slotC.edge+"@"+Math.round(p._slotC.along)).join(" | ")'));
console.log('errs:', pageErrs.length ? pageErrs.join('||') : 'none');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(0);
