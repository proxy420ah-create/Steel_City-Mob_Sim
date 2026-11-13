// _shot.mjs — headless-Chrome probe for thecommission.html. Teleports the camera via
// window.__cam/__snap over CDP; the page renders + POSTs to /shot → docs/probe_<name>.png.
//
//   node _shot.mjs <name> --cam=px,py,pz --look=tx,ty,tz [--env] [--ui] [--torch] [--settle=4500]
//   node _shot.mjs hall --cam=-6,1.05,5.4 --look=-6,0.6,3.3
//
// Node >=22 (built-in WebSocket + fetch). Chrome headless via --remote-debugging-port;
// NB: --user-data-dir MUST be an absolute non-default path or the port silently disables.
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const PAGE = (process.argv.find(a => a.startsWith('--page=')) || '').split('=')[1] || 'thecommission';
const arg = Object.fromEntries(process.argv.slice(2).filter(a => a.startsWith('--')).map(a => { const i = a.indexOf('='); return i < 0 ? [a.slice(2), true] : [a.slice(2, i), a.slice(i + 1)]; }));
const URL0 = 'http://127.0.0.1:8723/' + PAGE + '.html' + (arg.q ? '?' + arg.q : '');
const positional = process.argv.slice(2).filter(a => !a.startsWith('--'));
const NAME = positional[0] || 'shot';
const cam = (arg.cam || '0,3.1,4.9').split(',').map(Number);
const look = (arg.look || '0,.8,0').split(',').map(Number);
const SETTLE = Number(arg.settle || 4500);
const PORT = 9333 + Math.floor(Math.random() * 400);

const get = (u) => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));

const prof = path.join(HERE, `_shotprof_${PORT}`);
const ATTACH = arg.attach ? Number(arg.attach) : null;            // --attach=<debug port> → drive the USER's live Chrome instead of spawning headless
const DBG = ATTACH || PORT;
let chrome = null;
if (!ATTACH) {
  chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
    '--no-first-run', '--disable-extensions', '--window-size=1400,900', URL0], { stdio: 'ignore' });
  process.on('exit', () => { try { chrome.kill(); } catch {} });
}

async function pageWs() {
  for (let i = 0; i < 60; i++) {
    try {
      const list = JSON.parse(await get(`http://127.0.0.1:${DBG}/json/list`));
      const t = list.find(t => t.type === 'page' && t.url.includes(PAGE));
      if (t) return t.webSocketDebuggerUrl;
    } catch {}
    await sleep(500);
  }
  throw new Error('no page target');
}

const ws = new WebSocket(await pageWs());
await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
let mid = 0; const pend = new Map();
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
const cdp = (method, params = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method, params })); });
const evl = async (expr) => {
  const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 400));
  return r.result && r.result.result ? r.result.result.value : undefined;
};

for (let i = 0; i < 120; i++) { if (await evl('!!window.__snap')) break; await sleep(500); }
if (!await evl('!!window.__snap')) throw new Error('__snap never appeared');
await sleep(SETTLE);

if (arg.env !== '0') await evl('window.__envShot(1)');
if (arg.ui !== '0') await evl('window.__uiClean(1)');
if (arg.torch) await evl('window.__snapTorch=true');
if (arg.pre) { await evl(arg.pre); }
const ok = await evl(`window.__snap(${JSON.stringify(NAME)},${cam.join(',')},${look.join(',')})`);
console.log(JSON.stringify({ ok, name: 'probe_' + NAME + '.png', cam, look }));
if (arg.full) {                                                              // composited page shot — DOM overlays included (canvas-only __snap misses dialogs/HUD)
  const shot = await cdp('Page.captureScreenshot', { format: 'png' });
  const fp = path.join(HERE, 'probe_' + NAME + '_full.png');
  fs.writeFileSync(fp, Buffer.from(shot.result.data, 'base64'));
  console.log(JSON.stringify({ full: 'probe_' + NAME + '_full.png' }));
}
if (arg.eval) console.log('EVAL:', JSON.stringify(await evl(arg.eval)));
if (chrome) { chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800); }
process.exit(ok ? 0 : 1);
