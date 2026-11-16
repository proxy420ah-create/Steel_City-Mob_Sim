// _fly_check.mjs — unrestricted fly cam: place-mode roam unclamped in x/z,
// y floor+far ceiling hold, F re-homes, fog untouched.
import { spawn } from 'node:child_process';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_flyprof_${PORT}_${Date.now()}`);
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

await evl('__place.toggle(true)');
await sleep(400);
check('place mode on', await evl('__place.on() === true'));
check('fog unchanged in place mode', await evl('scene.fog.near === 3.5 && scene.fog.far === 13'));

// pin far — the old x[-10.5,3.4] clamp would snap this back on the next frame
await evl('__place.fly(25, 8, -30)');
await sleep(300);
const far = JSON.parse(await evl('JSON.stringify(__place.fly())'));
check('far pin holds — x/z unclamped', Math.abs(far[0] - 25) < .01 && Math.abs(far[2] + 30) < .01 && Math.abs(far[1] - 8) < .01, JSON.stringify(far));
check('far clip widened', await evl('camera.far === 220'));

await evl('__place.fly(0, 400, 0)'); await sleep(300);
const hi = JSON.parse(await evl('JSON.stringify(__place.fly())'));
check('no y ceiling — 400 holds', Math.abs(hi[1] - 400) < .01, JSON.stringify(hi));
await evl('__place.fly(0, -5, 0)'); await sleep(300);
const lo = JSON.parse(await evl('JSON.stringify(__place.fly())'));
check('y floor .05 holds', Math.abs(lo[1] - .05) < .01, JSON.stringify(lo));

await evl('__place.toggle(false)');
await sleep(200);
check('F re-homes — cam back at body eye', await evl('Math.abs(camera.position.y - 0.56) < .02'));
const errs = JSON.parse(await evl('JSON.stringify((window.__errs || []).slice(0, 5))'));
check('no page errors', !(errs && errs.length), JSON.stringify(errs));
console.log(fails.length ? 'FAILURES: ' + JSON.stringify(fails) : 'ALL PASS');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(fails.length ? 1 : 0);
