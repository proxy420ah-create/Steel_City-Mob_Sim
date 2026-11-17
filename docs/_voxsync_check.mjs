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
    if (m) { m.rotation.y = .7; m.userData._probeTag = Math.random(); gs.push(m.userData._probeTag);
      ((m.userData.mini._byName && m.userData.mini._byName.get('tenement_block_0')) || [])
        .forEach(s => s.userData._subTag = true); } });                       // zone children tag separately — surgical restamps swap THEM, not the group
  return gs; })()`);
const [px, py, pz] = [1, H - 2, 1];                  // in-bounds cell → becomes sentinel 4242

// ── the simulated editor stroke ─────────────────────────────────────────
await evl(`new BroadcastChannel('scms-voxel').postMessage(
  { type:'vox-delta', name:'tenement_block_0', dims:[${W},${H},${D}], ops:[[${px},${py},${pz},4242]] })`);

// poll — debounce + remount; fresh = group attached mid-emit, so rotY+deps are the real completion marks
let res = null;
for (let i = 0; i < 40 && !(res && res.tables && res.tables.every(r => r.fresh && r.rotY && r.deps)); i++) {
  await sleep(500);
  res = await evl(`(async () => { try { const f = await _stCache['tenement_block_0'];
    const out = { cache: f.vox[${px} + f.W*(${py} + f.H*${pz})], tables: [] };
    TABLES.forEach((t, i) => { const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
      out.tables.push(m ? (() => { const mn = m.userData.mini;
        if (mn._byName) { const subs = mn._byName.get('tenement_block_0') || [];
          return { fresh: subs.length > 0 && subs.every(s => !s.userData._subTag),            // surgical: children fresh…
                   surg: m.userData._probeTag === ${JSON.stringify(before)}[i],              // …while the group SURVIVES
                   rotY: Math.abs(m.rotation.y - .7) < .01,
                   deps: (mn._deps || new Set()).has('tenement_block_0') }; }
        return { fresh: m.userData._probeTag !== ${JSON.stringify(before)}[i], surg: true,
                 rotY: Math.abs(m.rotation.y - .7) < .01,
                 deps: (mn._deps || new Set()).has('tenement_block_0') }; })()
                        : { fresh:false, rotY:false }); });
    return out; } catch (e) { return { err: String(e && e.stack || e) }; } })()`);
  if (res && res.err) { console.log('  poll', i, 'page err:', res.err.slice(0, 300)); break; }
}
check('stasset cache patched by delta', !!(res && res.cache === 4242));
check('all dependent minis remounted (fresh group)', !!(res && res.tables && res.tables.every(r => r.fresh)), JSON.stringify(res));
check('zone restamped surgically (group survived the delta)', !!(res && res.tables && res.tables.every(r => r.surg !== false)), JSON.stringify(res));
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
const dep = (n, cells, dims = [8, 8, 8]) => fs.writeFileSync(VOX_ITEMS + n + '.json',
  JSON.stringify({ format: 'steelcity_stasset', name: n, dims, voxels: cells }));
dep(v1, [[1, 1, 1, 42], [4, 4, 4, 99]]);
dep(v2, [[1, 1, 1, 42], [5, 5, 5, 77]]);
let catOk = false;
for (let i = 0; i < 15 && !catOk; i++) { await sleep(400);
  catOk = await evl(`(() => { const c = TABLES.find(t => t.spec.id === 'primary').spec.catalog;
    return ['${v1}', '${v2}'].every(n => c.some(e => e.name === n)); })()`); }
check('deposited variants doorbell into the catalog', catOk);
check('LOCK dim while canonical mounts', await evl(`PAD_REGIONS.find(r => r.id === 'bldg:vlock').lit() === false`));
await evl(`__pad.press('bldg:vnext')`); await sleep(1400);                     // cycle [wip, v1, v2] → v1
check('V▶ mounts the variant', await evl(`TABLES.find(t => t.spec.id === 'primary').spec.models[0].name === '${v1}'`));
check('LOCK lights once a variant is staged', await evl(`PAD_REGIONS.find(r => r.id === 'bldg:vlock').lit() === true`));
await evl(`__pad.press('bldg:vlock')`); await sleep(900);                      // LOCK → canonical write
check('LOCK rebases the table to canonical', await evl(`TABLES.find(t => t.spec.id === 'primary').spec.models[0].name === 'wiptest_probe'`));
check('LOCK dims after the commit lands', await evl(`PAD_REGIONS.find(r => r.id === 'bldg:vlock').lit() === false`));
check('canonical cache carries the variant grid', await evl(`_stCache['wiptest_probe'].then(f => f.vox[4 + 8 * (4 + 8 * 4)] === 99)`));
const lockj = JSON.parse(fs.readFileSync(VOX_ITEMS + 'wiptest_probe.json'));
check('LOCK persisted the JSON twin', await evl(`fetch('/voxel/wiptest_probe').then(r => r.ok)`)
  && lockj.voxels.length === 2);
check('LOCK preserved the file envelope (format/name survive)', lockj.format === 'steelcity_stasset' && lockj.name === 'wiptest_probe');
[v1, v2, 'wiptest_probe'].forEach(n => { try { fs.unlinkSync(VOX_ITEMS + n + '.json'); } catch {} });

// ── preview fanout: cycling a variant repaints every dependent live (the zone's
//    barber stamp is the stand-in for "a slice of the city") — canonical cache
//    and file truth stay untouched until LOCK ─────────────────────────────────
const v9 = 'barber_shop__roof_9';
dep(v9, [[1, 1, 1, 42], [60, 30, 60, 99]], [64, 36, 64]);
let catOk2 = false;
for (let i = 0; i < 15 && !catOk2; i++) { await sleep(400);
  catOk2 = await evl(`(TABLES.find(t => t.spec.id === 'primary').spec.catalog || []).some(e => e.name === '${v9}')`); }
check('zone stamps barber_shop', await evl(`(() => { const t = TABLES.find(t => t.spec.id === 'studio');
  const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  return !!m && m.userData.mini._deps && m.userData.mini._deps.has('barber_shop'); })()`));
await evl(`mountModel(TABLES.find(t => t.spec.id === 'primary'), { name: 'barber_shop', fill: .75 })`); await sleep(1800);
await evl(`(() => { const t = TABLES.find(t => t.spec.id === 'studio');
  const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  if (m) { m.userData._pvTag = true;
    ((m.userData.mini._byName && m.userData.mini._byName.get('barber_shop')) || [])
      .forEach(s => s.userData._pvTag = true); } })()`);                          // tag group + barber children — surgical swaps the children only
await evl(`__pad.press('bldg:vnext')`); await sleep(1600);
check('variant cycle sets the preview overlay', await evl(`!!_previewOf['barber_shop']`));
let zoneFresh = false;                                                           // surgical restamp — poll the barber sub-minis
for (let i = 0; i < 15 && !zoneFresh; i++) { await sleep(400);
  zoneFresh = await evl(`(() => { const t = TABLES.find(t => t.spec.id === 'studio');
    const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
    const subs = m && m.userData.mini._byName && m.userData.mini._byName.get('barber_shop');
    return !!m && !!m.userData._pvTag && !!subs && subs.length > 0 && subs.every(s => !s.userData._pvTag); })()`); }
check('zone restamped the preview surgically (group survived)', zoneFresh);
for (let i = 0; i < 6; i++) {                                                    // keep cycling until canonical returns
  if (await evl(`TABLES.find(t => t.spec.id === 'primary').spec.models[0].name === 'barber_shop'`)) break;
  await evl(`__pad.press('bldg:vnext')`); await sleep(900); }
check('cycling back to canonical clears the preview',
  await evl(`!_previewOf['barber_shop'] && TABLES.find(t => t.spec.id === 'primary').spec.models[0].name === 'barber_shop'`));
try { fs.unlinkSync(VOX_ITEMS + v9 + '.json'); } catch {}

// ── Inbound delta — an agent/peer stroke lands in an open editor as an
//    undoable entry (BroadcastChannel, no echo-back) ─────────────────────────
await evl(`window.__ed = open('http://127.0.0.1:8723/studio/voxel_editor.html?model=barber_shop', '_blank');`);
let edUp = false;
for (let i = 0; i < 24 && !edUp; i++) { await sleep(500);
  edUp = await evl(`!!(window.__ed && window.__ed.__room && window.__ed.__room.voxelMap && window.__ed.__room.voxelMap.size > 1000)`); }
if (!edUp) check('editor tab booted with the model', false, 'window.__ed.__room never appeared');
else {
  const applied = await evl(`(() => {
    const w = window.__ed; if (!w || !w.__room) return 'no-hook';
    const ch = new BroadcastChannel('scms-voxel');
    ch.postMessage({ type: 'vox-delta', name: 'barber_shop', seq: -1, dims: [64, 36, 64], ops: [[3, 33, 3, 118]] });
    return new Promise(r => setTimeout(() => r(w.__room.voxelMap.get('3,33,3')), 900)); })()`);
  check('editor applies inbound vox-delta (agent stroke lands undoable)', applied === 118, JSON.stringify(applied));
  await evl(`window.__ed && window.__ed.close()`);
}

// ── Frame gaps during a zone remount — everything synchronous is chunked/pooled now;
//    the floor is the retired composite's GC + the new instance buffers' first-draw
//    GPU upload (a draw call can't yield). >80ms frames capped at 5/8s ──────────
await evl(`(() => { const t = TABLES.find(t => t.spec.id === 'studio');
  window.__gaps = 0; const prev0 = performance.now(); let prev = prev0;
  const tick = () => { const n = performance.now(); if (n - prev > 80) window.__gaps++; prev = n;
    if (n - prev0 < 8000) requestAnimationFrame(tick); };
  requestAnimationFrame(tick); remountMini(t); })()`);
await sleep(8500);
const gaps = await evl('window.__gaps');
check('zone remount stays responsive (≤5 frames >80ms)', gaps <= 5, gaps + ' gaps/8s');

// ── …and the surgical path — the common case: a single-building restamp re-emits
//    ~150k cells, not the 37M composite. >80ms frames capped at 1/5s ────────────
await evl(`(() => { const t = TABLES.find(t => t.spec.id === 'studio');
  const m = t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  window.__gaps2 = 0; const prev0 = performance.now(); let prev = prev0;
  const tick = () => { const n = performance.now(); if (n - prev > 80) window.__gaps2++; prev = n;
    if (n - prev0 < 5000) requestAnimationFrame(tick); };
  requestAnimationFrame(tick); if (m) restampZone(t, m, 'barber_shop'); })()`);
await sleep(5500);
const gaps2 = await evl('window.__gaps2');
check('surgical restamp is near-free (≤1 frame >80ms)', gaps2 <= 1, gaps2 + ' gaps/5s');

finish();
