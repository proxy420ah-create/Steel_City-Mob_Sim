// _cdp.mjs — shared headless-Chrome/CDP harness for docs probes.
// Every probe used to hand-roll this same ~50 lines and one-offs kept
// mis-rolling it (bare `ws.onopen` await hangs forever on socket error;
// profile dirs colliding keep Chrome from booting silently). One import
// now owns: spawn, target discovery, ws with deadlines on EVERY await,
// page exception capture, pass/fail tally, cleanup.
//
//   import { bootCdp } from './_cdp.mjs';
//   const { evl, sleep, check, finish } = await bootCdp();
//   check('the law', await evl('2 + 2 === 4'));
//   finish();                                  // tallies + kills chrome + exit code
//
// Contract: server on :8723, Node >=22 (built-in WebSocket). Checks read
// page-side lexicals — eval'd expressions may throw; those land in errs
// and fail the run so page noise never passes silently.
import { spawn } from 'node:child_process';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const deadline = (ms, what) => new Promise((_, rej) => setTimeout(() => rej(new Error('timeout — ' + what)), ms));

export async function bootCdp(o = {}) {
  const page = o.page || 'construct';
  const url = `http://127.0.0.1:8723/${page}.html` + (o.q ? '?' + o.q : '');
  const PORT = 9333 + Math.floor(Math.random() * 400);
  const prof = path.join(HERE, `_cdpprof_${PORT}_${Date.now()}`);          // timestamped — leftover dirs from killed runs can never collide
  const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
    '--no-first-run', '--disable-extensions', '--window-size=1400,900', url], { stdio: 'ignore' });
  const kill = () => { try { chrome.kill(); } catch {} try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} };
  process.on('exit', kill);

  let ws = null, mid = 0; const pend = new Map(), errs = [];
  const cdp = (m, p = {}) => Promise.race([deadline(15000, 'cdp ' + m),
    new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); })]);
  const evl = async e => { const r = await cdp('Runtime.evaluate', { expression: e, awaitPromise: true, returnByValue: true });
    if (r.result && r.result.exceptionDetails) errs.push('eval: ' + JSON.stringify(r.result.exceptionDetails).slice(0, 300));
    return r.result && r.result.result ? r.result.result.value : undefined; };

  let up = false;
  for (let i = 0; i < 60 && !up; i++) {
    try {
      const l = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`));
      const t = l.find(t => t.type === 'page' && t.url.includes(page));
      if (t) {
        ws = new WebSocket(t.webSocketDebuggerUrl);
        await Promise.race([deadline(10000, 'ws.open'), new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; })]);
        ws.onmessage = e => { const m = JSON.parse(e.data);
          if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); }
          else if (m.method === 'Runtime.exceptionThrown') errs.push('page: ' + JSON.stringify(m.params.exceptionDetails).slice(0, 300)); };
        await cdp('Runtime.enable'); up = true;
      }
    } catch (e) { if (String(e).startsWith('Error: timeout')) throw e; }
    if (!up) await sleep(500);
  }
  if (!up) { kill(); throw new Error('cdp: page target never appeared (server down? port ' + PORT + ')'); }
  for (let i = 0; i < 120; i++) { if (await evl(o.ready || '!!window.__warmDone')) break; await sleep(500); }
  await sleep(o.settle ?? 1200);

  let pass = 0; const fails = [];
  const check = (name, v, detail) => { if (v) { pass++; console.log('PASS ', name, detail ?? ''); } else { fails.push(name); console.log('FAIL ', name, detail ?? ''); } };
  const finish = () => { console.log(`\n${pass} passed, ${fails.length} failed`);
    if (fails.length) console.log('FAILED:', fails.join(' | '));
    if (errs.length) console.log('PAGE/EVAL ERRORS:', errs.join(' | '));
    kill(); process.exit(fails.length || errs.length ? 1 : 0); };
  return { evl, cdp, sleep, check, finish, errs, port: PORT, chrome };
}
