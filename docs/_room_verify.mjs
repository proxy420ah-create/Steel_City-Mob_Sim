// _room_verify.mjs — annex expansion check: walk bounds, table positions/sizes, aisle widths
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_roomprof_${PORT}_${Date.now()}`);
const get = u => new Promise((res, rej) => http.get(u, r => { let b = ''; r.on('data', d => b += d); r.on('end', () => res(b)); }).on('error', rej));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--disable-extensions', 'http://127.0.0.1:8723/construct.html'], { stdio: 'ignore' });
process.on('exit', () => { try { chrome.kill(); } catch {} });
let ws = null, mid = 0; const pend = new Map();
const cdp = (m, p = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method: m, params: p })); });
const evl = async e => { const r = await cdp('Runtime.evaluate', { expression: e, awaitPromise: true, returnByValue: true }); return r.result && r.result.result ? r.result.result.value : undefined; };
let pass = 0, fail = 0;
const check = (n, ok, d = '') => { console.log((ok ? '  ok  ' : ' FAIL ') + n + (d ? '  — ' + d : '')); ok ? pass++ : fail++; };
for (let i = 0; i < 60; i++) { try { const l = JSON.parse(await get(`http://127.0.0.1:${PORT}/json/list`)); const t = l.find(t => t.type === 'page' && t.url.includes('construct'));
  if (t) { ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
    ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pend.has(m.id)) { pend.get(m.id)(m); pend.delete(m.id); } };
    await cdp('Runtime.enable'); break; } } catch {} await sleep(500); }
for (let i = 0; i < 240; i++) { if (await evl('window.__warmDone === true')) break; await sleep(500); }

// room extents
check('annex west walk reaches x -9.7', await evl('walkOK(-9.7, -2.5)') === true);
check('old west edge x -9.9 is wall', await evl('walkOK(-9.9, -2.5)') === false);
check('annex full z reach', await evl('walkOK(-6.5, -5.7)') === true && await evl('walkOK(-6.5, .7)') === true);
check('door channel between flanks walkable', await evl('walkOK(-4.1, -2.5)') === true);
check('approach to zone table edge walkable', await evl('walkOK(-5.0, -2.5)') === true);

// tables
const tabs = await evl(`JSON.stringify(__construct.tables().map(t => ({ id: t.id, x: t.x, z: t.z, size: t.size })))`);
const T = JSON.parse(tabs);
const st = T.find(t => t.id === 'studio'), pr = T.find(t => t.id === 'primary'), bl = T.find(t => t.id === 'block');
check('studio centered at (-6.5,-2.5) size 2.4', st && st.x === -6.5 && st.z === -2.5 && st.size === 2.4, JSON.stringify(st));
check('primary flank (-4.1,-1.1) size 1.2', pr && pr.x === -4.1 && pr.z === -1.1 && pr.size === 1.2, JSON.stringify(pr));
check('block flank (-4.1,-3.9) size 1.2', bl && bl.x === -4.1 && bl.z === -3.9 && bl.size === 1.2, JSON.stringify(bl));

// aisles — walk points around the big table (its WALL_BLOCKS spans ±1.35)
check('west aisle behind zone table', await evl('walkOK(-8.8, -2.5)') === true);
check('north aisle beside zone table', await evl('walkOK(-6.5, -4.6)') === true);
check('south aisle beside zone table', await evl('walkOK(-6.5, -.4)') === true);
check('zone table blocks its own footprint', await evl('walkOK(-6.5, -2.5)') === false);

// models still mounted on each table
const models = await evl(`JSON.stringify(__construct.tables().map(t => ({ id: t.id, n: t.models.length })))`);
const M = JSON.parse(models);
check('all tables carry minis', M.every(m => m.n > 0), JSON.stringify(M));
const errs = await evl('JSON.stringify(window.__errs || [])');
check('no page errors', errs === '[]', errs);
console.log(fail ? `FAIL (${fail})` : `ALL PASS (${pass})`);
chrome.kill(); process.exit(fail ? 1 : 0);
