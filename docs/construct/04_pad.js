'use strict';
/* construct/04_pad.js — debug pad — pages, regions, canvas screen, cursor focus, wall mount.
   Split from construct.html (was lines 774-968); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── DEBUG PAD — a pickable touchscreen slab: black body + coreMat edge trim + a canvas screen.
   Regions are data ({rect,label,fn}) hit-tested through raycast uv — the face is software, so
   future tools/menus are draw code, never new geometry. Two input modes over one region table:
   held → RMB tap frees the cursor (mousemove raycasts the face, LMB presses); placed →
   center-aim + E/click rides the same promptIt machinery as the kiosk/wall panel. */
/* Pad pages — a page is registry data {tab, draw}: the tab strip GENERATES itself from the
   registry, and the trailing ≡ opens the APPS menu — a tile per page, so overflow never crowds
   the strip. v2 — per-table tabs (BLDG/BLOCK/ZONE) own every table-scoped control; the
   proximity remote is gone — the tab IS the binding. Bands share a fixed grid:
   captions at 100/156/212/268, button rows at 104/160/216/272. */
const TAB_TABLE = { bldg: 'primary', block: 'block', zone: 'studio' };
const PHOS = '#39ff8e', DIMTX = '#46525e', CREAM = '#d8d2c4', CYTX = '#35d5ff';      // Construct cyberdeck palette — phosphor readouts are the Matrix nod
const drawTbl = (g, sid, tab) => {                                                 // shared per-table page: title + status + band captions
  const t = TABLES.find(q => q.spec.id === sid); if (!t) return;
  const cur = t.spec.models[0], mdl = t.g.children.find(c => c.userData.mini && !c.userData.mini.static),
    mn = mdl && mdl.userData.mini;
  g.fillStyle = CREAM; g.font = 'bold 15px monospace';
  g.fillText(tab + ' — ' + t.spec.id.toUpperCase() + ' TABLE', 256, 52);
  const st = [(cur ? cur.name.replace(/_/g, ' ') : '—').toUpperCase(),
    'LIFT +' + t.lift.toFixed(2),
    mn && mn.spin ? (mn.spin < 0 ? '◀' : '▶') + Math.round(Math.abs(mn.spin) * 180 / Math.PI) + '°/s' : null,
    t.spec.noSlice ? 'SLICE LOCKED' : (mn ? 'SLICE ' + (mn.axis || 'y').toUpperCase() + ' ' + mn.slice[mn.axis || 'y'].join('–') : null)]
    .filter(Boolean).join('  ·  ');
  g.fillStyle = PHOS; g.font = '12px monospace'; g.fillText(st, 256, 74);
  g.fillStyle = DIMTX; g.font = '10px monospace';
  g.fillText('MODEL', 256, 100); g.fillText('SPIN', 256, 156);
  g.fillText(t.spec.noSlice ? 'SLICE — STREET MODEL, ROADS STAY WHOLE' : 'SLICE', 256, 212); g.fillText('LIFT', 256, 268); };
const PAD_PAGES = {
  bldg:  { tab: 'BLDG',  draw: g => drawTbl(g, 'primary', 'BLDG') },
  block: { tab: 'BLOCK', draw: g => drawTbl(g, 'block', 'BLOCK') },
  zone:  { tab: 'ZONE',  draw: g => drawTbl(g, 'studio', 'ZONE') },
  deck: { tab: 'DECK', draw: g => {
    g.fillStyle = CREAM; g.font = 'bold 15px monospace'; g.fillText('SHOP LIFT — ' + (spinning ? 'TURNING' : 'HELD'), 256, 52);
    g.fillStyle = PHOS; g.font = '12px monospace';
    g.fillText('LIFT +' + spinG.position.y.toFixed(2) + 'm  ·  ' +
      (spinning ? (SPIN_RATE < 0 ? '◀' : '▶') + Math.round(Math.abs(SPIN_RATE) * 180 / Math.PI) + '°/s' : 'HELD'), 256, 74);
    g.fillStyle = DIMTX; g.font = '10px monospace'; g.fillText('SPIN', 256, 156); g.fillText('LIFT', 256, 212); g.fillText('QUICK', 256, 268); } },
  lamp: { tab: 'LAMP', draw: g => { const T = window.__torch;
    g.fillStyle = CREAM; g.font = 'bold 15px monospace'; g.fillText('INSPECTION TORCH — ' + (T.on ? 'ON' : 'OFF'), 256, 52);
    g.fillStyle = PHOS; g.font = '12px monospace';
    g.fillText('PWR ' + TC.i.toFixed(1) + '  FILL ' + TC.f.toFixed(1) + '  ' + ['TIGHT', 'MID', 'WIDE'][TC.a], 256, 74);
    g.fillStyle = DIMTX; g.font = '10px monospace'; g.fillText('OUTPUT', 256, 156); g.fillText('TINT', 256, 212); g.fillText('CONFIG', 256, 268); } },
  apps: { tab: '≡', draw: g => {
    g.fillStyle = CREAM; g.font = 'bold 15px monospace'; g.fillText('ALL TOOLS', 256, 52);
    g.fillStyle = DIMTX; g.font = '10px monospace'; g.fillText('tap a tile — pages keep their state', 256, 312); } },
};
let padPage = 'bldg';
const PAD_REGIONS = [];
const padR = o => PAD_REGIONS.push(o);
Object.keys(TAB_TABLE).forEach(pid => {                                            // per-table bands — every control the table owns, no proximity
  padR({ id: pid + ':prev', page: pid, x: 16,  y: 104, w: 48, h: 40, label: '◀', glyph: '◀', fs: 20, fn: () => padSwap(-1) });
  padR({ id: pid + ':next', page: pid, x: 448, y: 104, w: 48, h: 40, label: '▶', glyph: '▶', fs: 20, fn: () => padSwap(1) });
  if (pid === 'block') padR({ id: 'block:cars', page: 'block', x: 216, y: 104, w: 80, h: 40, label: 'CARS', glyph: 'CARS', fs: 12,
    lit: () => blockTrf.on, fn: () => blockTrf.toggle(padTable()) });
  [[-.9, '◀◀'], [-.25, '◀'], [.25, '▶'], [.9, '▶▶']].forEach(([v, gl], i) =>
    padR({ id: pid + ':sp' + i, page: pid, x: 16 + i * 124, y: 160, w: 110, h: 40, label: 'SPIN ' + gl, glyph: gl, fs: 20,
      lit: () => { const m = padMini(); return m && m.spin === v; }, fn: () => miniSpinSet(v) }));
  ['x', 'y', 'z'].forEach((ax, i) =>
    padR({ id: pid + ':ax' + ax, page: pid, slice: true, x: 16 + i * 48, y: 216, w: 40, h: 40, label: 'SLICE ' + ax.toUpperCase(), glyph: ax.toUpperCase(), fs: 16, axis: ax, fn: () => padAxisSet(ax) }));
  [['MIN−', 'min', -1], ['MIN+', 'min', 1], ['MAX−', 'max', -1], ['MAX+', 'max', 1]].forEach(([gl, bd, d], i) =>
    padR({ id: pid + ':sl' + i, page: pid, slice: true, x: 168 + i * 72, y: 216, w: 62, h: 40, label: 'SLICE ' + gl, glyph: gl, fs: 12, fn: () => miniSliceStep(bd, d) }));
  padR({ id: pid + ':sres', page: pid, slice: true, x: 452, y: 216, w: 44, h: 40, label: 'RESET', glyph: 'RST', fs: 11, fn: () => miniSliceReset() });
  [['▼▼', -.08], ['▼', -.02], ['▲', .02], ['▲▲', .08]].forEach(([gl, d], i) =>
    padR({ id: pid + ':lf' + i, page: pid, x: 16 + i * 100, y: 272, w: 92, h: 40, label: 'LIFT ' + gl, glyph: gl, fs: 20, fn: () => tableLift(padTable(), d) }));
  padR({ id: pid + ':lres', page: pid, x: 416, y: 272, w: 80, h: 40, label: 'RESET', glyph: 'RST', fs: 11, fn: () => tableLiftSet(padTable(), 0) });
});
[[-.9, '◀◀'], [-.25, '◀'], [.25, '▶'], [.9, '▶▶']].forEach(([v, gl], i) =>              // DECK — shop-lift spin
  padR({ id: 'd:sp' + i, page: 'deck', x: 16 + i * 124, y: 160, w: 110, h: 40, label: 'SPIN ' + gl, glyph: gl, fs: 20,
    lit: () => spinning && SPIN_RATE === v, fn: () => spinCtl(v) }));
[['▼▼', -.08], ['▼', -.02], ['▲', .02], ['▲▲', .08]].forEach(([gl, d], i) =>           // DECK — shop-lift raise/lower
  padR({ id: 'd:lf' + i, page: 'deck', x: 16 + i * 124, y: 216, w: 110, h: 40, label: 'LIFT ' + gl, glyph: gl, fs: 20, fn: () => spinLiftStep(d) }));
padR({ id: 'd:stp', page: 'deck', x: 16,  y: 272, w: 150, h: 40, label: 'SPIN',   glyph: '⏻ SPIN', fs: 13, lit: () => spinning, fn: () => { spinning = !spinning; } });
padR({ id: 'd:res', page: 'deck', x: 196, y: 272, w: 120, h: 40, label: 'LIFT 0', glyph: 'LIFT 0', fs: 13, fn: () => spinLiftSet(0) });
padR({ id: 'd:eye', page: 'deck', x: 388, y: 272, w: 110, h: 40, label: 'EYE',    glyph: 'EYE',    fs: 13, fn: () => spinLiftSet(.45) });
[['PWR−', 'i', -1], ['PWR+', 'i', 1], ['FIL−', 'f', -.2], ['FIL+', 'f', .2]].forEach(([gl, k, d], i) =>   // LAMP — output
  padR({ id: 'l:o' + i, page: 'lamp', x: 16 + i * 124, y: 160, w: 110, h: 40, label: gl, glyph: gl, fs: 14, fn: () => torchCfg(k, d) }));
TORCH_COLS.forEach((c, i) =>                                                        // LAMP — tint
  padR({ id: 'l:c' + i, page: 'lamp', x: 16 + i * 96, y: 216, w: 88, h: 40, label: 'TINT', glyph: ['WARM', 'DAY', 'COOL', 'RED', 'GRN'][i], fs: 12, lit: () => TC.c === i, fn: () => torchCfg('c', i) }));
padR({ id: 'l:beam', page: 'lamp', x: 16,  y: 272, w: 150, h: 40, label: 'BEAM',  glyph: '◀BEAM▶',  fs: 13, fn: () => torchCfg('a') });
padR({ id: 'l:tgl',  page: 'lamp', x: 196, y: 272, w: 120, h: 40, label: 'TORCH', glyph: '⏻ TORCH', fs: 13, lit: () => window.__torch.on, fn: () => window.__torch.setOn(!window.__torch.on) });
padR({ id: 'l:res',  page: 'lamp', x: 388, y: 272, w: 110, h: 40, label: 'RESET', glyph: 'RESET',   fs: 13, fn: () => torchReset() });
const padTabIds = Object.keys(PAD_PAGES).filter(id => PAD_PAGES[id].tab);
padTabIds.forEach((id, i) => { const w = 496 / padTabIds.length;                    // the strip builds itself — a new page gets its tab for free
  padR({ id: 'tab:' + id, tab: id, x: 8 + i * w, y: 8, w: w - 5, h: 30,
    label: PAD_PAGES[id].tab, glyph: PAD_PAGES[id].tab, fs: 13, fn: () => padPageSet(id) }); });
padTabIds.forEach((id, i) => {                                                     // the APPS menu — a launch tile per page
  padR({ id: 'app:' + id, page: 'apps', x: 16 + (i % 3) * 166, y: 80 + ((i / 3) | 0) * 84, w: 150, h: 76,
    label: 'OPEN ' + PAD_PAGES[id].tab, glyph: PAD_PAGES[id].tab, fs: 16, fn: () => padPageSet(id) }); });
const padSwap = dir => { const t = padTable(); if (!t) return;
  const cat = t.spec.catalog || t.spec.models, cur = t.spec.models[0],
    i = cat.findIndex(m => m.name === (cur && cur.name)), next = cat[(i + dir + cat.length) % cat.length];
  if (next) { localStorage.setItem('dvm_model:' + t.spec.id, next.name);
    mountModel(t, next).then(() => padDraw()); } };
const padCv = document.createElement('canvas'); padCv.width = 512; padCv.height = 340;
const padTex = new THREE.CanvasTexture(padCv); padTex.colorSpace = THREE.SRGBColorSpace;
let padRec = null, padScr = null, padEdgeMat = null, padFocus = false, padHover = null, padPressId = null;
const padPageSet = id => { padPage = id; padHover = null; padPressId = null; padDraw(); };
const padAxisSet = ax => { const t = padTable(), mdl = t && t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  if (mdl && !(t && t.spec.noSlice)) mdl.userData.mini.axis = ax; padDraw(); };
/* padTable — the pad's table is the TAB's table: BLDG/BLOCK/ZONE own their controls outright,
   no proximity binding (the remote-for-nearest model died with the generic pages).
   Non-table tabs (deck/lamp/apps) fall back to primary for harmless status reads. */
const padTable = () => TABLES.find(t => t.spec.id === (TAB_TABLE[padPage] || 'primary')) || TABLES[0];
const padMini = () => { const t = padTable(), mdl = t && t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  return mdl ? mdl.userData.mini : null; };
const padDraw = () => { const g = padCv.getContext('2d'), t = padTable(), mn = padMini();
  g.fillStyle = '#07090c'; g.fillRect(0, 0, 512, 340);                              // cyberdeck — near-black glass, hairline frame
  g.strokeStyle = '#22303a'; g.lineWidth = 2; g.strokeRect(2, 2, 508, 336);
  g.textAlign = 'center';
  const btn = r => { const hov = padHover === r.id, pr = padPressId === r.id,
      on = (r.tab && r.tab === padPage) || (r.axis && mn && mn.axis === r.axis) || (r.lit && r.lit());
    g.fillStyle = pr ? '#1b2f3c' : on ? '#12242e' : hov ? '#16212a' : '#0d1217'; g.fillRect(r.x, r.y, r.w, r.h);
    g.strokeStyle = on || hov ? CYTX : '#22303a'; g.lineWidth = 2; g.strokeRect(r.x + 1, r.y + 1, r.w - 2, r.h - 2);
    g.fillStyle = pr || on ? CYTX : hov ? CREAM : '#5b6874'; g.font = 'bold ' + (r.fs || 16) + 'px monospace';
    g.fillText(r.glyph, r.x + r.w / 2, r.y + r.h / 2 + (r.fs ? r.fs * .36 : 6)); };
  PAD_REGIONS.filter(r => r.tab || (r.page === padPage && !(r.slice && t && t.spec.noSlice))).forEach(btn);
  const pg = PAD_PAGES[padPage]; if (pg && pg.draw) pg.draw(g, { t, mn });
  padTex.needsUpdate = true; };
const padPress = r => { padPressId = r.id; padDraw(); r.fn();
  setTimeout(() => { padPressId = null; padDraw(); }, 160); };
const padUvRegion = uv => { if (!uv) return null;
  const px = uv.x * 512, py = (1 - uv.y) * 340, noSl = !!(padTable() && padTable().spec.noSlice);
  return PAD_REGIONS.find(r => (r.tab || (r.page === padPage && !(noSl && r.slice)))
    && px >= r.x && px <= r.x + r.w && py >= r.y && py <= r.y + r.h) || null; };
const padSetHover = r => { const id = r ? r.id : null; if (id !== padHover) { padHover = id; padDraw(); } };
const padHudEl = document.createElement('div');
padHudEl.style.cssText = 'position:fixed;left:50%;bottom:14px;transform:translateX(-50%);background:rgba(8,8,14,.92);border:1px solid #6a6a92;color:#e2dbc2;padding:5px 12px;font:10px ui-monospace,Consolas,monospace;letter-spacing:.1em;display:none;z-index:8;pointer-events:none;white-space:nowrap';
padHudEl.textContent = 'PAD — LMB press · RMB tap — mouse look';
document.body.appendChild(padHudEl);
const grabLook = () => { try { const r = el.requestPointerLock(); if (r && r.catch) r.catch(() => {}); } catch {} };   // the API rejects without a user gesture — never an unhandled rejection
const padFocusToggle = () => { padFocus = !padFocus; padSetHover(null);
  if (padFocus && document.exitPointerLock) document.exitPointerLock();
  else if (!padFocus && document.pointerLockElement !== el) grabLook();
  padHudEl.style.display = padFocus ? 'block' : 'none'; };
const emitPad = () => {
  const PAD_W = .18, PAD_D = .12, PAD_H = .012, x = -4.1, z = -.575, rotY = .3;  // the Primary Table's north felt margin (rides the flank position)
  const g = new THREE.Group();
  const slab = new THREE.Mesh(new THREE.BoxGeometry(PAD_W, PAD_H, PAD_D), new THREE.MeshLambertMaterial({ color: 0x0c0e14 }));
  slab.position.y = PAD_H / 2; g.add(slab);
  padEdgeMat = new THREE.LineBasicMaterial({ color: 0x35d5ff, transparent: true, opacity: .9 });
  const edge = new THREE.LineSegments(new THREE.EdgesGeometry(slab.geometry), padEdgeMat); edge.position.y = PAD_H / 2; g.add(edge);
  padScr = new THREE.Mesh(new THREE.PlaneGeometry(.15, .1), new THREE.MeshBasicMaterial({ map: padTex }));
  padScr.rotation.x = -Math.PI / 2; padScr.position.y = PAD_H + .001; padScr.userData.padScreen = true; g.add(padScr);
  const tops = new Map();                                                       // flat slab — every 5mm column at pad height
  for (let ix = -18; ix <= 18; ix++) for (let iz = -12; iz <= 12; iz++) tops.set(TOPK(ix, iz), PAD_H);
  const host = TABLES[0] && TABLES[0].rec, lb = [-PAD_W / 2, PAD_W / 2, -PAD_D / 2, PAD_D / 2];
  const y0 = host ? surfaceAt(host, x, z, 0) : 0;
  padRec = { name: 'debug_pad', kind: 'item', g, tops, lb, x, z, rotY, y0, minB: 0, vs: .005,
    bbox: footB(lb, x, z, rotY), pickable: true, solids: null, source: 'manifest', isPad: true };
  g.userData.pRec = padRec; g.position.set(x, y0, z); g.rotation.y = rotY; scene.add(g); PLACED.push(padRec);
  padDraw(); return padRec;
};
const padScreenHit = () => { if (!padScr) return null;
  _ray.setFromCamera(mouseNDC, camera);
  const h = _ray.intersectObject(padScr, false);
  return h.length ? h[0] : null; };
const padCursorHit = () => { const h = padScreenHit(); return h ? padUvRegion(h.uv) : null; };
const padProbe = () => { if (!padRec || padRec === held) { padSetHover(null); return null; }   // can't aim at your own hand
  _ray.setFromCamera(_ctr, camera);
  const h = _ray.intersectObject(padScr, false);
  if (!h.length || h[0].distance > 1.8) { padSetHover(null); return null; }
  const r = padUvRegion(h[0].uv); padSetHover(r);
  return r ? { isBtn: true, panel: 'pad', label: () => 'E — ' + r.label, fn: () => padPress(r) } : null; };
window.__pad = { rec: () => padRec, regions: PAD_REGIONS, scr: () => padScr, probe: padProbe,
  table: () => padTable() && padTable().spec.id,
  focus: on => { if (!!on !== padFocus) padFocusToggle(); }, isFocus: () => padFocus,
  page: () => padPage, tab: id => padPageSet(id),
  hover: () => padHover, hit: uv => { const r = padUvRegion(uv); return r ? r.id : null; },
  wallTarget: () => padWallTarget(), stick: (x, y, z, n) => { takeIt(padRec); placeIt({ x, y, z, wall: true, n }); },
  drop: (x, y, z) => { takeIt(padRec); placeIt({ x, y, z }); },
  press: id => { const r = PAD_REGIONS.find(q => q.id === id); if (r) padPress(r); }, draw: padDraw };
window.__cars = { on: () => blockTrf.on, lanes: () => blockTrf.L, seek: () => blockTrf.seek(),
  toggle: (ti, on) => blockTrf.toggle(TABLES[ti | 0], on),
  state: () => blockTrf.cars.map(c => ({ st: c.st, s: +c.s.toFixed(3),
    x: +c.rec.g.position.x.toFixed(4), z: +c.rec.g.position.z.toFixed(4), ry: +c.rec.g.rotation.y.toFixed(3),
    tgt: c.tgt ? c.tgt.sc.edge + '@' + Math.round(c.tgt.sc.along) : null })),
  inv: () => (blockTrf.inv || []).map(sl => ({ e: sl.sc.edge, a: Math.round(sl.sc.along), by: !!sl.by })),
  /* diag — why aren't they parking? call in the console while cars lap after toggle-off:
     on=false + inv>0 + free>0 + tgt=null  → claim logic;  inv=0 → lattice build;
     free=0 → held() occupancy;  on=true → the off-press never landed. */
  diag: () => { const inv = blockTrf.inv || [], mn = blockTrf.mn,
      road = mn ? roadProf(mn) : {};
    return { on: blockTrf.on, len: Math.round(blockTrf.len), inv: inv.length,
      model: mn && mn.name, road: Object.keys(road).join('') || 'none',
      free: inv.filter(sl => !sl.by && !blockTrf.held(sl.sc)).length,
      cars: blockTrf.cars.map(c => ({ st: c.st, s: Math.round(c.s),
        tgt: c.tgt ? (c.tgt.sc ? c.tgt.sc.edge + '@' + Math.round(c.tgt.sc.along) : 'origin-fallback') : null })) }; } };
/* pad wall-mount — the pad's local frame: +y = screen normal, +z = its bottom edge. Sticking
   maps +y → the face normal and +z → world down, so the screen reads upright off the wall.
   The target is a raycast against the invisible wall slabs — hit.point + a hair off the face. */
const _pmb = new THREE.Matrix4(), _pmn = new THREE.Vector3(), _pmx = new THREE.Vector3(), _pmz = new THREE.Vector3(0, -1, 0);
const padMountQ = (g, nArr) => { _pmn.set(nArr[0], nArr[1], nArr[2]).normalize();
  _pmx.crossVectors(_pmn, _pmz).normalize();                             // pad +x = normal × down
  _pmb.makeBasis(_pmx, _pmn, _pmz); g.quaternion.setFromRotationMatrix(_pmb); };
const padWallTarget = () => {
  _ray.setFromCamera(_ctr, camera);
  const excl = [ghost, aimDot, camera];
  followers.forEach(f => excl.push(f.CH)); if (selfBody) excl.push(selfBody.CH); if (held) excl.push(held.g); marks.forEach(m => excl.push(m));
  const hits = rayPruned(_ray, scene.children, excl, true);
  const h = hits[0];
  if (!h || !h.object.userData.wallSlab || h.distance > 2.4) return null;   // the wall must OWN the ray — a nearer surface (felt/mini/floor) is the drop, not a mount
  const n = h.face.normal.clone().transformDirection(h.object.matrixWorld);
  if (n.dot(fwdV) > 0) n.negate();                                       // face the side we're standing on
  n.y = 0; if (n.lengthSq() < .5) return null; n.normalize();            // vertical faces only
  const p = h.point.clone().addScaledVector(n, .002);                    // slab back kisses the face
  return { x: p.x, y: p.y, z: p.z, wall: true, n: [n.x, n.y, n.z] };
};
