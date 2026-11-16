// _cars_verify.mjs — curbside-cruise law vs the Unity implementation:
//   1. parked cars face their lane's travel direction (authored front = +z — Unity LookRotation)
//   2. departures happen TOGETHER (same keypress) on a ~1.4 m angled diagonal —
//      yaw swings off the lane heading mid-maneuver, at drive speed (RequestDepart)
//   3. cruise speed = real 3.0 m/s = 60 composite cells/s (VehicleTestSpawner.driveSpeed)
//   4. cruise cars nose along velocity (no rear-first driving)
//   5. pull-in splices an angled diagonal into the CLAIMED slot at drive speed
//      (TrySpliceParking — park-anywhere: NearestFreeAhead, not the origin slot)
//   6. the car ADOPTS the claimed lattice slot — pose + heading restored verbatim
//   node _cars_verify.mjs     — fresh headless chrome, exits 0 on all-pass
import { spawn } from 'node:child_process';
import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CHROME = 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe';
const URL0 = 'http://127.0.0.1:8723/construct.html';
const PORT = 9333 + Math.floor(Math.random() * 400);
const prof = path.join(HERE, `_cvprof_${PORT}`);
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
  else if (m.method === 'Runtime.exceptionThrown') pageErrs.push(JSON.stringify(m.params.exceptionDetails).slice(0, 200)); };
const cdp = (method, params = {}) => new Promise(res => { const id = ++mid; pend.set(id, res); ws.send(JSON.stringify({ id, method, params })); });
await cdp('Runtime.enable');
const evl = async expr => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true });
  if (r.result && r.result.exceptionDetails) console.error('PAGE ERR:', JSON.stringify(r.result.exceptionDetails).slice(0, 400));
  return r.result && r.result.result ? r.result.result.value : undefined; };
const results = [];
const check = (name, ok, detail = '') => { results.push([name, !!ok]); console.log((ok ? '  ok  ' : ' FAIL ') + name + (detail ? '  — ' + detail : '')); };

for (let i = 0; i < 240; i++) { if (await evl('!!window.__warmDone')) break; await sleep(500); }
check('warm boot', await evl('!!window.__warmDone'));

// ── 1. parked facing: nose (front = +z) points along the curb lane's travel dir ──
//     expected world headings per edge (cell hX*mirX/hZ, mirX=-1): s→−x west, w→+z south
//     (the rendered-RHT law — PARK_EDGE headings are the mirror-inverse of the raw cell legal set)
const parked = await evl(`__construct.placed.filter(p => p._parked).map(p => ({
  edge: p._slotC.edge, ry: +p.g.rotation.y.toFixed(4), slot: p._slot }))`);
check('two parked cars', parked.length === 2, JSON.stringify(parked));
const wantRy = { s: Math.atan2(-1, 0), n: Math.atan2(1, 0), e: Math.atan2(0, -1), w: Math.atan2(0, 1) };
const dAng = (a, b) => Math.abs(Math.atan2(Math.sin(a - b), Math.cos(a - b)));
for (const p of parked) check(`parked ${p.edge}-edge nose = travel dir`, dAng(p.ry, wantRy[p.edge]) < .01,
  `ry ${p.ry} vs want ${wantRy[p.edge].toFixed(4)}`);

// ── 2. departures: together + angled diagonal at drive speed (Unity RequestDepart) ──
await evl('__cars.toggle(1, true)');
const edges = await evl('blockTrf.cars.map(c => c.rec._slotC.edge)');
const lens = await evl('blockTrf.cars.map(c => [+c.lenD.toFixed(1), +c.lenA.toFixed(1)])');
const tD0 = Date.now(), tLv = [], tLp = [], mxDv = [];
for (let i = 0; i < 200; i++) {
  const st = await evl('__cars.state()'), now = (Date.now() - tD0) / 1000;
  st.forEach((c, j) => {
    if (c.st === 'leave') { if (tLv[j] === undefined) tLv[j] = now;
      mxDv[j] = Math.max(mxDv[j] || 0, dAng(c.ry, wantRy[edges[j]])); }
    else if (tLv[j] !== undefined && tLp[j] === undefined) tLp[j] = now; });
  if (st.length && st.every(c => c.st === 'loop')) break;
  await sleep(50); }
check('departures together (same keypress — no stagger)', tLv.length === 2 && Math.abs(tLv[0] - tLv[1]) < .5, JSON.stringify(tLv));
for (let i = 0; i < lens.length; i++) {
  check(`car${i} pull-out diagonal ≈1.4 m`, lens[i][0] > 24 && lens[i][0] < 40, `lenD ${lens[i][0]} cells`);
  check(`car${i} angles out of the slot (yaw swings off the lane heading)`, (mxDv[i] || 0) > .12, `max dev ${(mxDv[i] || 0).toFixed(3)} rad`);
  const exp = lens[i][0] / 60, dur = (tLp[i] || 0) - (tLv[i] || 0);
  check(`car${i} pull-out at drive speed`, tLp[i] !== undefined && Math.abs(dur - exp) < .35, `dur ${dur.toFixed(2)}s vs len/v ${exp.toFixed(2)}s`);
}

// ── 3. cruise speed = 60 cells/s = 3.0 m/s real ──────────────────────────────
const s0 = await evl('__cars.state().map(c => ({ st: c.st, s: c.s, ry: c.ry }))');
await sleep(1500);
const s1 = await evl('__cars.state().map(c => ({ st: c.st, s: c.s, x: c.x, z: c.z, ry: c.ry }))');
await sleep(1500);
const s2 = await evl('__cars.state().map(c => ({ st: c.st, s: c.s, x: c.x, z: c.z, ry: c.ry }))');
const lapLen = await evl('blockTrf.len');
for (let i = 0; i < s1.length; i++) {
  const a = s0[i], b = s2[i];
  if (a.st !== 'loop' || b.st !== 'loop') { check(`car${i} cruising`, false, a.st + '→' + b.st); continue; }
  const dS = ((b.s - a.s) % lapLen + lapLen) % lapLen;
  const ms = dS * .05 / 3.0;                                                    // cells → m (1 cell = .05 m) over the 3 s window
  check(`car${i} speed ≈ 3.0 m/s`, ms > 2.4 && ms < 3.6, `dS ${dS.toFixed(1)} cells → ${ms.toFixed(2)} m/s`);
}
// ── 4. nose on the path tangent; chord-vs-ry only on straight legs ───────────
//     (a 1.5 s pos-delta across a corner arc measures the CHORD, not the tangent —
//      up to ~45° off by construction. Legs: chord ≈ tangent, the check is honest.)
const mir = await evl('blockTrf.mn.mirX ? -1 : 1');
const tans = await evl(`[${s2.map(c => c.s).join(',')}].map((sv, i) => { const q = blockTrf.at(sv),
    sg = sv => { const g = blockTrf.segs.find(g => sv >= g.s0 && sv < g.s0 + g.len); return g ? { ax: !!g.ax, s0: g.s0 } : { ax: false, s0: -1 }; };
    return { hx: q.hx, hz: q.hz, a: sg([${s1.map(c => c.s).join(',')}][i]), b: sg(sv) }; })`);
for (let i = 0; i < s1.length; i++) {
  const a = s1[i], b = s2[i], t = tans[i];
  if (a.st !== 'loop' || b.st !== 'loop') continue;
  const tanRy = Math.atan2(t.hx * mir, t.hz);
  let d = Math.atan2(Math.sin(b.ry - tanRy), Math.cos(b.ry - tanRy));
  check(`car${i} nose on path tangent`, Math.abs(d) < .15, `ry ${b.ry} vs tangent ${tanRy.toFixed(3)} (Δ ${d.toFixed(3)})`);
  if (!(t.a.ax && t.b.ax && t.a.s0 === t.b.s0)) continue;               // chord is only a fair proxy when both samples share one straight leg
  const dx = b.x - a.x, dz = b.z - a.z;
  if (Math.hypot(dx, dz) < .002) { check(`car${i} moving`, false, 'delta too small'); continue; }
  const velRy = Math.atan2(dx, dz);
  d = Math.atan2(Math.sin(b.ry - velRy), Math.cos(b.ry - velRy));
  check(`car${i} nose leads (ry ≈ velocity dir)`, Math.abs(d) < .25, `ry ${b.ry} vs velRy ${velRy.toFixed(3)} (Δ ${d.toFixed(3)})`);
}
// ── 5. pull-in: park-anywhere — claim nearest free slot ahead, splice the diagonal ──
//     (the claim lands inside tick — give it a beat before reading/seeking)
await evl('__cars.toggle(1, false)');
await sleep(350);
const claims = await evl('__cars.state().map(c => c.tgt)');
check('both cars claimed a free slot ahead (park-anywhere)', claims.length === 2 && claims.every(t => !!t), JSON.stringify(claims));
const claimed = await evl('blockTrf.cars.map(c => c.tgt ? { e: c.tgt.sc.edge, a: Math.round(c.tgt.sc.along), lenA: +c.tgt.lenA.toFixed(1) } : null)');
check('claimed slots are distinct', claimed[0] && claimed[1] && (claimed[0].e !== claimed[1].e || claimed[0].a !== claimed[1].a), JSON.stringify(claimed));
await evl('__cars.seek()');
const tP0 = Date.now(), tPk = [], tSl = [], mxPk = [];
for (let i = 0; i < 200; i++) {
  const st = await evl('__cars.state()'), now = (Date.now() - tP0) / 1000;
  st.forEach((c, j) => {
    if (c.st === 'park') { if (tPk[j] === undefined) tPk[j] = now;
      mxPk[j] = Math.max(mxPk[j] || 0, dAng(c.ry, wantRy[edges[j]])); }
    else if (tPk[j] !== undefined && tSl[j] === undefined && c.st === 'slot') tSl[j] = now; });
  if (st.length && st.every(c => c.st === 'slot')) break;
  await sleep(50); }
for (let i = 0; i < lens.length; i++) {
  check(`car${i} pulls in at an ANGLE (yaw swings into the curb)`, (mxPk[i] || 0) > .12, `max dev ${(mxPk[i] || 0).toFixed(3)} rad`);
  const exp = (claimed[i] ? claimed[i].lenA : 0) / 60, dur = (tSl[i] || 0) - (tPk[i] || 0);
  check(`car${i} pull-in at drive speed`, tSl[i] !== undefined && Math.abs(dur - exp) < .35, `dur ${dur.toFixed(2)}s vs len/v ${exp.toFixed(2)}s`);
}
// ── 6. adoption: parked pose = the claimed lattice slot, verbatim ────────────
for (let i = 0; i < 60; i++) { const st = await evl('__cars.state().map(c => c.st).join(",")'); if (st === 'slot,slot') break; await sleep(500); }
const parked2 = await evl(`__construct.placed.filter(p => p._parked).map(p => ({ edge: p._slotC.edge,
  along: p._slotC.along === undefined ? null : Math.round(p._slotC.along), ry: +p.g.rotation.y.toFixed(4) }))`);
for (const p of parked2) check(`reparked ${p.edge}-edge nose restored`, dAng(p.ry, wantRy[p.edge]) < .01, `ry ${p.ry}`);
for (let i = 0; i < claimed.length; i++)
  check(`car${i} ADOPTED its claimed slot (${claimed[i] && claimed[i].e}@${claimed[i] && claimed[i].a})`,
    parked2[i] && claimed[i] && parked2[i].edge === claimed[i].e && Math.abs(parked2[i].along - claimed[i].a) <= 1,
    `adopted ${JSON.stringify(parked2[i])} vs claim ${JSON.stringify(claimed[i])}`);
const latOK = await evl(`__construct.placed.filter(p => p._parked).map(p =>
  blockTrf.inv.some(sl => sl.sc.edge === p._slotC.edge && Math.abs(sl.sc.along - p._slotC.along) < 1))`);
latOK.forEach((ok, i) => check(`car${i} slot sits on the parking lattice`, !!ok));
const ovlp = await evl(`(() => { const P = __construct.placed.filter(p => p._parked), bad = [],
    ax = e => (e === 's' || e === 'n') ? 'cx' : 'cz';
  for (let i = 0; i < P.length; i++) for (let j = i + 1; j < P.length; j++) {
    const a = P[i]._slotC, b = P[j]._slotC; if (a.edge !== b.edge) continue;
    const d = Math.abs(a[ax(a.edge)] - b[ax(b.edge)]);
    if (d < (a.carL + b.carL) / 2) bad.push(i + 'v' + j + ':' + d.toFixed(1)); }
  return bad; })()`);
check('no parked cars overlap', ovlp.length === 0, ovlp.join(','));
// ── 7. re-departure: cars pull out of their ADOPTED slots and cruise again ────
await evl('__cars.toggle(1, true)');
for (let i = 0; i < 200; i++) { const st = await evl('__cars.state().map(c => c.st).join(",")');
  if (st === 'loop,loop') break; await sleep(60); }
const st3 = await evl('__cars.state().map(c => c.st).join(",")');
check('cars re-depart from adopted slots', st3 === 'loop,loop', st3);
const edges2 = await evl('blockTrf.cars.map(c => c.rec._slotC.edge)');
check('re-arm uses adopted slot geometry', edges2.join(',') === [claimed[0].e, claimed[1].e].join(','), JSON.stringify(edges2));
check('no page errors', pageErrs.length === 0, pageErrs.join(' | '));

const bad = results.filter(r => !r[1]);
console.log(bad.length ? `\n${bad.length} FAIL` : '\nALL PASS');
chrome.kill(); setTimeout(() => { try { fs.rmSync(prof, { recursive: true, force: true }); } catch {} }, 800);
process.exit(bad.length ? 1 : 0);
