// _voxsync_check.mjs — live-sync probe: simulates an editor's vox-delta broadcast
// on BroadcastChannel('scms-voxel') and asserts every dependent Construct mini
// rebuilds in place. tenement_block_0 rides ALL THREE default tables (standalone
// mini, block_26_tenement stamp, battlefield zone stamp) — one message must
// trickle into all of them.
import { bootCdp } from './_cdp.mjs';

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

finish();
