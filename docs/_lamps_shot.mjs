// _lamps_shot.mjs — capture the exhibit-lighting room: door-channel hero shot + wide annex view.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_lsprof_${PORT}`);
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', '--window-size=1400,900', URL0], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });
async function pageWs() { for (let i = 0; i < 60; i++) { try { const t = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)).find(t => t.type === 'page' && t.url.includes('construct')); if (t) return t.webSocketDebuggerUrl; } catch {} await sleep(500); } throw new Error('no page'); }
const ws = new WebSocket(await pageWs()); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
let mid = 0; const pend = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
await cdp('Runtime.enable');
const evl = async expr => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  return r.result && r.result.result ? r.result.result.value : undefined; };
for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
console.log('boot', await evl('!!window.__warmDone'));
await evl('window.__uiClean && window.__uiClean(1)');
await evl('__lamps && __lamps.set(true)');
await sleep(2500);                                                       // pendant fetch + warm-in
// standing in the door channel, facing the centerpiece — all three pendants should pool onto their tables
console.log('door:', await evl('window.__snap("lamps_door", -3.2, 0.62, -2.5, -6.5, 0.35, -2.5)'));
await sleep(400);
// elevated wide over the annex — pendant cones + fixture domes read
console.log('wide:', await evl('window.__snap("lamps_wide", -3.4, 1.5, -0.6, -6.0, 0.2, -3.2)'));
await sleep(400);
// lamps off for contrast — the flat bright default
await evl('__lamps.set(false)'); await sleep(600);
console.log('off :', await evl('window.__snap("lamps_off", -3.2, 0.62, -2.5, -6.5, 0.35, -2.5)'));
await evl('__lamps.set(true)');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(0);
