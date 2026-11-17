// _voxsync_check.mjs — live-sync probe: simulates an editor's vox-delta broadcast
// on BroadcastChannel('scms-voxel') and asserts every dependent Construct mini
// rebuilds in place. tenement_block_0 rides ALL THREE default tables (standalone
// mini, block_26_tenement stamp, battlefield zone stamp) — one message must
// trickle into all of them. Second half: the variant carousel — file-deposited
// `__` variants doorbell into the catalog, V◀V▶ cycles them, LOCK canonicalizes.
import { bootCdp } from './_cdp.mjs';
import fs from 'node:fs';

const { evl, sleep, check, finish } = await bootCdp();
await sleep(2500);                                   // boot mounts; zone emit is the slow one

// ── deps recorded at emit ──────────────────────────────────────────────
const deps = await evl(`TABLES.map(t => { const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  return m ? [...(m.userData.mini._deps || [])] : null; })`);
check('mounted minis carry _deps', deps.every(d => d && d.length));
check('tenement_block_0 in every _deps set', deps.every(d => d.includes('tenement_block_0')));

// ── baseline: real dims, per-table group identity + a rotY marker ────────
const [W, H, D] = await evl(`_stCache['tenement_block_0'].then(f => [f.W, f.H, f.D])`);
const before = await evl(`(() => { const gs = []; TABLES.forEach(t => {
    const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
    if (m) { m.rotation.y = .7; m.userData._probeTag = Math.random(); gs.push(m.userData._probeTag); } });
  return gs; })()`);
const [px, py, pz] = [1, H - 2, 1];                  // in-bounds cell → becomes sentinel 4242

// ── the simulated editor stroke ─────────────────────────────────────────
await evl(`new BroadcastChannel('scms-voxel').postMessage(
  { type:'vox-delta', name:'tenement_block_0', dims:[${W},${H},${D}], ops:[[${px},${py},${pz},4242]] })`);

// poll — 160ms debounce + remount (the zone's merged emit is the long pole)
let res = null;
for (let i = 0; i < 40 && !(res && res.tables && res.tables.every(r => r.fresh)); i++) {
  await sleep(500);
  res = await evl(`(async () => { try { const f = await _stCache['tenement_block_0'];
    const out = { cache: f.vox[${px} + f.W*(${py} + f.H*${pz})], tables: [] };
    TABLES.forEach((t, i) => { const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
      out.tables.push(m ? { fresh: m.userData._probeTag !== ${JSON.stringify(before)}[i],
                            rotY: Math.abs(m.rotation.y - .7) < .01,
                            deps: (m.userData.mini._deps || new Set()).has('tenement_block_0') }
                        : { fresh:false, rotY:false }); });
    return out; } catch (e) { return { err: String(e && e.stack || e) }; } })()`);
  if (res && res.err) { console.log('  poll', i, 'page err:', res.err.slice(0, 300)); break; }
}
check('stasset cache patched by delta', !!(res && res.cache === 4242));
check('all dependent minis remounted (fresh group)', !!(res && res.tables && res.tables.every(r => r.fresh)), JSON.stringify(res));
check('rotY survives the remount', !!(res && res.tables && res.tables.every(r => r.rotY)), JSON.stringify(res));

// ── dims-mismatch path: a delta under foreign dims must trigger need-full,
//    NOT corrupt the cached grid ────────────────────────────────────────
const needFull = await evl(`(async () => { const got = new Promise(r => {
    const ch = new BroadcastChannel('scms-voxel'); ch.onmessage = e => { if (e.data.type === 'need-full') r(e.data); };
    setTimeout(() => r(null), 3000); });
  new BroadcastChannel('scms-voxel').postMessage({ type:'vox-delta', name:'tenement_block_0', dims:[999,999,999], ops:[[0,0,0,1]] });
  return got; })()`);
check('dims mismatch requests need-full', !!(needFull && needFull.name === 'tenement_block_0'));

// ── cold-boot catalog: pre-deposited building JSONs (empty_land is committed but
//    not in the static catalog) merge from /models without any doorbell ────────
let bootOk = false;
for (let i = 0; i < 15 && !bootOk; i++) { await sleep(400);
  bootOk = await evl(`(TABLES.find(t => t.spec.id === 'primary').spec.catalog || []).some(e => e.name === 'empty_land')`); }
check('cold-boot merges /models buildings into the catalog', bootOk);

// ── the WIP-folder workflow: a never-shipped name arrives via vox-full →
//    lands in primary's catalog → mounts file-free → deltas rebuild it ────
const WIP = 'wiptest_probe';
await evl(`(async () => { const v = new Uint16Array(8 * 8 * 8);
    v[1 + 8 * (1 + 8 * 1)] = 42; v[2 + 8 * (1 + 8 * 1)] = 42;
    new BroadcastChannel('scms-voxel').postMessage({ type:'vox-full', name:'${WIP}', dims:[8,8,8], vox: v.buffer }); })()`);
await sleep(400);
check('unknown name joins primary catalog', await evl(`(TABLES.find(t => t.spec.id === 'primary').spec.catalog || []).some(e => e.name === '${WIP}')`));
const mountOk = await evl(`(async () => { const t = TABLES.find(t => t.spec.id === 'primary');
    const g = await mountModel(t, { name:'${WIP}', fill:.75 });
    return g && g.userData.mini._deps && g.userData.mini._deps.has('${WIP}'); })()`);
check('WIP mounts file-free from the cached broadcast', !!mountOk);
await evl(`(() => { const t = TABLES.find(t => t.spec.id === 'primary');
    const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
    if (m) m.userData._wipTag = true; })()`);                                  // tag the mount — remount swaps the group
await evl(`new BroadcastChannel('scms-voxel').postMessage({ type:'vox-delta', name:'${WIP}', dims:[8,8,8], ops:[[3,1,1,7]] })`);
let wipFresh = false;
for (let i = 0; i < 20 && !wipFresh; i++) { await sleep(400);
  wipFresh = await evl(`(() => { const t = TABLES.find(t => t.spec.id === 'primary');
    const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
    return !!m && !m.userData._wipTag; })()`); }                               // tag gone = fresh group = the delta remounted it
check('WIP mini remounts on delta', wipFresh);

// ── variant carousel: file-deposited `base__feat` JSONs doorbell via /events →
//    liveCatalog → V◀V▶ cycles the base group → LOCK writes the canonical grid ─
const VOX_ITEMS = '../Assets/StreamingAssets/voxel_items/';
const v1 = 'wiptest_probe__roof_1', v2 = 'wiptest_probe__roof_2';
const dep = (n, cells) => fs.writeFileSync(VOX_ITEMS + n + '.json',
  JSON.stringify({ format: 'steelcity_stasset', name: n, dims: [8, 8, 8], voxels: cells }));
dep(v1, [[1, 1, 1, 42], [4, 4, 4, 99]]);
dep(v2, [[1, 1, 1, 42], [5, 5, 5, 77]]);
let catOk = false;
for (let i = 0; i < 15 && !catOk; i++) { await sleep(400);
  catOk = await evl(`(() => { const c = TABLES.find(t => t.spec.id === 'primary').spec.catalog;
    return ['${v1}', '${v2}'].every(n => c.some(e => e.name === n)); })()`); }
check('deposited variants doorbell into the catalog', catOk);
await evl(`__pad.press('bldg:vnext')`); await sleep(1400);                     // cycle [wip, v1, v2] → v1
check('V▶ mounts the variant', await evl(`TABLES.find(t => t.spec.id === 'primary').spec.models[0].name === '${v1}'`));
await evl(`__pad.press('bldg:vlock')`); await sleep(900);                      // LOCK → canonical write
check('LOCK rebases the table to canonical', await evl(`TABLES.find(t => t.spec.id === 'primary').spec.models[0].name === 'wiptest_probe'`));
check('canonical cache carries the variant grid', await evl(`_stCache['wiptest_probe'].then(f => f.vox[4 + 8 * (4 + 8 * 4)] === 99)`));
check('LOCK persisted the JSON twin', await evl(`fetch('/voxel/wiptest_probe').then(r => r.ok)`)
  && JSON.parse(fs.readFileSync(VOX_ITEMS + 'wiptest_probe.json')).voxels.length === 2);
[v1, v2, 'wiptest_probe'].forEach(n => { try { fs.unlinkSync(VOX_ITEMS + n + '.json'); } catch {} });

finish();
