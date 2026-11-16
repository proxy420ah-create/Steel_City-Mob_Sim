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
   the strip. New tools = a PAD_PAGES entry + optional page regions; the chrome never changes. */
const PAD_PAGES = {
  model: { tab: 'MODEL', draw: (g, { t, cat, cur, idx }) => {
    g.fillStyle = '#cfc8b0'; g.font = '16px monospace';
    g.fillText('MODEL — ' + (t ? t.spec.id.toUpperCase() : '—') + (idx >= 0 ? '   ' + (idx + 1) + '/' + cat.length : ''), 256, 96);
    g.fillStyle = '#e8d9b0'; g.font = 'bold 24px monospace';
    g.fillText(cur ? cur.name.replace(/_/g, ' ').toUpperCase() : '…', 256, 196);
    g.fillStyle = '#5b5648'; g.font = '13px monospace'; g.fillText('RMB tap — cursor · E — place', 256, 312); } },
  rot: { tab: 'ROT', draw: (g, { t, mn }) => {
    g.fillStyle = '#cfc8b0'; g.font = '16px monospace'; g.fillText('TURNTABLE — ' + (t ? t.spec.id.toUpperCase() : '—'), 256, 96);
    const degs = mn ? Math.round(Math.abs(mn.spin) * 180 / Math.PI) : 0;
    g.fillStyle = '#e8d9b0'; g.font = 'bold 22px monospace';
    g.fillText(mn && mn.spin ? (mn.spin < 0 ? '◀ ' : '▶ ') + degs + '°/s' : 'STOPPED', 256, 266); } },
  slice: { tab: 'SLICE', draw: (g, { t, mn }) => {
    if (t && t.spec.noSlice) {                                                   // street tables don't slice — the measured lane law needs whole roads
      g.fillStyle = '#cfc8b0'; g.font = '16px monospace'; g.fillText('SLICE — ' + t.spec.id.toUpperCase(), 256, 146);
      g.fillStyle = '#e8d9b0'; g.font = 'bold 20px monospace'; g.fillText('LOCKED — STREET MODEL', 256, 226);
      g.fillStyle = '#5b5648'; g.font = '13px monospace'; g.fillText('roads must stay whole for the cars', 256, 266); return; }
    const ax = mn ? (mn.axis || 'y') : 'y', s = mn ? mn.slice[ax] : [0, 0], dim = mn ? mn.dims['xyz'.indexOf(ax)] : 0;
    g.fillStyle = '#cfc8b0'; g.font = '16px monospace';
    g.fillText('SLICE — ' + (t ? t.spec.id.toUpperCase() : '—') + '   ' + ax.toUpperCase() + '   0–' + (dim - 1), 256, 146);
    g.fillStyle = '#e8d9b0'; g.font = 'bold 20px monospace';
    g.fillText('MIN   ' + s[0], 256, 186); g.fillText('MAX   ' + s[1], 256, 261); } },
  table: { tab: 'LIFT', draw: (g, { t }) => {
    g.fillStyle = '#cfc8b0'; g.font = '16px monospace'; g.fillText('PISTON LIFT — ' + (t ? t.spec.id.toUpperCase() : '—'), 256, 96);
    g.fillStyle = '#e8d9b0'; g.font = 'bold 22px monospace';
    g.fillText('TOP  ' + (t ? tableTop(t).toFixed(2) : '—') + 'm   ·   LIFT +' + (t ? t.lift.toFixed(2) : '0.00'), 256, 266);
    g.fillStyle = '#5b5648'; g.font = '13px monospace'; g.fillText('eye .55 — raise past it to look up · drops to the floor', 256, 312); } },
  deck: { tab: 'DECK', draw: g => {
    g.fillStyle = '#cfc8b0'; g.font = '16px monospace'; g.fillText('SHOP LIFT — ' + (spinning ? 'TURNING' : 'HELD'), 256, 96);
    g.fillStyle = '#e8d9b0'; g.font = 'bold 22px monospace';
    g.fillText('LIFT +' + spinG.position.y.toFixed(2) + 'm   ·   ' +
      (spinning ? (SPIN_RATE < 0 ? '◀ ' : '▶ ') + Math.round(Math.abs(SPIN_RATE) * 180 / Math.PI) + '°/s' : 'HELD'), 256, 266);
    g.fillStyle = '#5b5648'; g.font = '13px monospace'; g.fillText('raise it — work the car at eye level', 256, 312); } },
  lamp: { tab: 'LAMP', draw: g => { const T = window.__torch;
    g.fillStyle = '#cfc8b0'; g.font = '16px monospace'; g.fillText('INSPECTION TORCH — ' + (T.on ? 'ON' : 'OFF'), 256, 96);
    g.fillStyle = '#e8d9b0'; g.font = 'bold 20px monospace';
    g.fillText('PWR ' + TC.i.toFixed(1) + '   FILL ' + TC.f.toFixed(1) + '   ' + ['TIGHT', 'MID', 'WIDE'][TC.a], 256, 124);
    TORCH_COLS.forEach((c, i) => { g.fillStyle = '#' + c.toString(16).padStart(6, '0');   // a chip of each swatch's true color
      g.fillRect(22 + i * 100, 258, 78, 7); });
    g.fillStyle = '#5b5648'; g.font = '13px monospace'; g.fillText('L — key toggle · the tune persists (dvm_torch)', 256, 312); } },
  apps: { tab: '≡', draw: g => {
    g.fillStyle = '#cfc8b0'; g.font = '16px monospace'; g.fillText('ALL TOOLS', 256, 96);
    g.fillStyle = '#5b5648'; g.font = '13px monospace'; g.fillText('tap a tile — pages keep their state', 256, 312); } },
};
let padPage = 'model';
const PAD_REGIONS = [
  { id: 'prev', page: 'model', x: 30,  y: 140, w: 90, h: 96, label: '◀ MODEL', glyph: '◀', fn: () => padSwap(-1) },
  { id: 'next', page: 'model', x: 392, y: 140, w: 90, h: 96, label: 'MODEL ▶', glyph: '▶', fn: () => padSwap(1) },
  { id: 'cars', page: 'model', x: 16,  y: 284, w: 96, h: 42, label: 'CARS', glyph: 'CARS', fs: 18, lit: () => blockTrf.on, fn: () => blockTrf.toggle(padTable()) },
  { id: 'rll', page: 'rot', x: 16,  y: 140, w: 110, h: 96, label: 'SPIN ◀◀', glyph: '◀◀', fs: 34, rate: -.9,  fn: () => miniSpinSet(-.9) },
  { id: 'rl',  page: 'rot', x: 140, y: 140, w: 110, h: 96, label: 'SPIN ◀',  glyph: '◀',  fs: 40, rate: -.25, fn: () => miniSpinSet(-.25) },
  { id: 'rr',  page: 'rot', x: 264, y: 140, w: 110, h: 96, label: 'SPIN ▶',  glyph: '▶',  fs: 40, rate: .25,  fn: () => miniSpinSet(.25) },
  { id: 'rrr', page: 'rot', x: 388, y: 140, w: 110, h: 96, label: 'SPIN ▶▶', glyph: '▶▶', fs: 34, rate: .9,   fn: () => miniSpinSet(.9) },
  { id: 'rres', page: 'rot', x: 196, y: 284, w: 120, h: 42, label: 'ROT RESET', glyph: 'RESET', fs: 20, fn: () => miniRotReset() },
  { id: 'lDD', page: 'table', x: 16,  y: 140, w: 110, h: 96, label: 'LOWER ▼▼', glyph: '▼▼', fs: 34, fn: () => tableLift(padTable(), -.08) },
  { id: 'lD',  page: 'table', x: 140, y: 140, w: 110, h: 96, label: 'LOWER ▼',  glyph: '▼',  fs: 40, fn: () => tableLift(padTable(), -.02) },
  { id: 'lR',  page: 'table', x: 264, y: 140, w: 110, h: 96, label: 'RAISE ▲',  glyph: '▲',  fs: 40, fn: () => tableLift(padTable(), .02) },
  { id: 'lRR', page: 'table', x: 388, y: 140, w: 110, h: 96, label: 'RAISE ▲▲', glyph: '▲▲', fs: 34, fn: () => tableLift(padTable(), .08) },
  { id: 'lres', page: 'table', x: 196, y: 284, w: 120, h: 42, label: 'LIFT RESET', glyph: 'RESET', fs: 20, fn: () => tableLiftSet(padTable(), 0) },
  { id: 'dsll', page: 'deck', x: 16,  y: 140, w: 110, h: 64, label: 'SPIN ◀◀', glyph: '◀◀', fs: 30, lit: () => spinning && SPIN_RATE === -.9,  fn: () => spinCtl(-.9) },
  { id: 'dsl',  page: 'deck', x: 140, y: 140, w: 110, h: 64, label: 'SPIN ◀',  glyph: '◀',  fs: 34, lit: () => spinning && SPIN_RATE === -.25, fn: () => spinCtl(-.25) },
  { id: 'dsr',  page: 'deck', x: 264, y: 140, w: 110, h: 64, label: 'SPIN ▶',  glyph: '▶',  fs: 34, lit: () => spinning && SPIN_RATE === .25,  fn: () => spinCtl(.25) },
  { id: 'dsrr', page: 'deck', x: 388, y: 140, w: 110, h: 64, label: 'SPIN ▶▶', glyph: '▶▶', fs: 30, lit: () => spinning && SPIN_RATE === .9,   fn: () => spinCtl(.9) },
  { id: 'dLD',  page: 'deck', x: 16,  y: 212, w: 110, h: 60, label: 'LOWER ▼▼', glyph: '▼▼', fs: 30, fn: () => spinLiftStep(-.08) },
  { id: 'dLd',  page: 'deck', x: 140, y: 212, w: 110, h: 60, label: 'LOWER ▼',  glyph: '▼',  fs: 34, fn: () => spinLiftStep(-.02) },
  { id: 'dLu',  page: 'deck', x: 264, y: 212, w: 110, h: 60, label: 'RAISE ▲',  glyph: '▲',  fs: 34, fn: () => spinLiftStep(.02) },
  { id: 'dLU',  page: 'deck', x: 388, y: 212, w: 110, h: 60, label: 'RAISE ▲▲', glyph: '▲▲', fs: 30, fn: () => spinLiftStep(.08) },
  { id: 'dstp', page: 'deck', x: 16,  y: 284, w: 150, h: 42, label: 'SPIN ON/OFF', glyph: '⏻ SPIN', fs: 18, lit: () => spinning, fn: () => { spinning = !spinning; } },
  { id: 'dres', page: 'deck', x: 196, y: 284, w: 120, h: 42, label: 'LIFT 0', glyph: 'LIFT 0', fs: 18, fn: () => spinLiftSet(0) },
  { id: 'deye', page: 'deck', x: 388, y: 284, w: 110, h: 42, label: 'EYE LVL', glyph: 'EYE', fs: 18, fn: () => spinLiftSet(.45) },
  { id: 'ax:x', page: 'slice', x: 116, y: 76, w: 90, h: 50, label: 'SLICE X', glyph: 'X', fs: 26, axis: 'x', fn: () => padAxisSet('x') },
  { id: 'ax:y', page: 'slice', x: 211, y: 76, w: 90, h: 50, label: 'SLICE Y', glyph: 'Y', fs: 26, axis: 'y', fn: () => padAxisSet('y') },
  { id: 'ax:z', page: 'slice', x: 306, y: 76, w: 90, h: 50, label: 'SLICE Z', glyph: 'Z', fs: 26, axis: 'z', fn: () => padAxisSet('z') },
  { id: 'min-', page: 'slice', x: 30,  y: 150, w: 70, h: 60, label: 'MIN −', glyph: '◀', fn: () => miniSliceStep('min', -1) },
  { id: 'min+', page: 'slice', x: 412, y: 150, w: 70, h: 60, label: 'MIN +', glyph: '▶', fn: () => miniSliceStep('min', 1) },
  { id: 'max-', page: 'slice', x: 30,  y: 225, w: 70, h: 60, label: 'MAX −', glyph: '◀', fn: () => miniSliceStep('max', -1) },
  { id: 'max+', page: 'slice', x: 412, y: 225, w: 70, h: 60, label: 'MAX +', glyph: '▶', fn: () => miniSliceStep('max', 1) },
  { id: 'sres', page: 'slice', x: 196, y: 292, w: 120, h: 38, label: 'SLICE RESET', glyph: 'RESET', fs: 20, fn: () => miniSliceReset() },
  { id: 'br-', page: 'lamp', x: 16,  y: 140, w: 110, h: 70, label: 'PWR −', glyph: 'PWR−', fs: 22, fn: () => torchCfg('i', -1) },
  { id: 'br+', page: 'lamp', x: 140, y: 140, w: 110, h: 70, label: 'PWR +', glyph: 'PWR+', fs: 22, fn: () => torchCfg('i', 1) },
  { id: 'fl-', page: 'lamp', x: 264, y: 140, w: 110, h: 70, label: 'FILL −', glyph: 'FIL−', fs: 22, fn: () => torchCfg('f', -.2) },
  { id: 'fl+', page: 'lamp', x: 388, y: 140, w: 110, h: 70, label: 'FILL +', glyph: 'FIL+', fs: 22, fn: () => torchCfg('f', .2) },
  { id: 'c0', page: 'lamp', x: 16,  y: 222, w: 90, h: 50, label: 'WARM', glyph: 'WARM', fs: 15, lit: () => TC.c === 0, fn: () => torchCfg('c', 0) },
  { id: 'c1', page: 'lamp', x: 116, y: 222, w: 90, h: 50, label: 'DAY',  glyph: 'DAY',  fs: 15, lit: () => TC.c === 1, fn: () => torchCfg('c', 1) },
  { id: 'c2', page: 'lamp', x: 216, y: 222, w: 90, h: 50, label: 'COOL', glyph: 'COOL', fs: 15, lit: () => TC.c === 2, fn: () => torchCfg('c', 2) },
  { id: 'c3', page: 'lamp', x: 316, y: 222, w: 90, h: 50, label: 'RED',  glyph: 'RED',  fs: 15, lit: () => TC.c === 3, fn: () => torchCfg('c', 3) },
  { id: 'c4', page: 'lamp', x: 416, y: 222, w: 90, h: 50, label: 'GRN',  glyph: 'GRN',  fs: 15, lit: () => TC.c === 4, fn: () => torchCfg('c', 4) },
  { id: 'beam', page: 'lamp', x: 16,  y: 284, w: 96,  h: 42, label: 'BEAM', glyph: '◀BEAM▶', fs: 18, fn: () => torchCfg('a') },
  { id: 'tgl',  page: 'lamp', x: 120, y: 284, w: 112, h: 42, label: 'ON/OFF', glyph: '⏻ TORCH', fs: 16, lit: () => window.__torch.on, fn: () => window.__torch.setOn(!window.__torch.on) },
  { id: 'tres', page: 'lamp', x: 416, y: 284, w: 80,  h: 42, label: 'RESET', glyph: 'RESET', fs: 16, fn: () => torchReset() },
];
const padTabIds = Object.keys(PAD_PAGES).filter(id => PAD_PAGES[id].tab);
padTabIds.forEach((id, i) => { const w = 496 / padTabIds.length;                // the strip builds itself — a new page gets its tab for free
  PAD_REGIONS.push({ id: 'tab:' + id, tab: id, x: 8 + i * w, y: 10, w: w - 5, h: 44,
    label: PAD_PAGES[id].tab, glyph: PAD_PAGES[id].tab, fs: 20, fn: () => padPageSet(id) }); });
padTabIds.forEach((id, i) => {                                                 // the APPS menu — a launch tile per page
  PAD_REGIONS.push({ id: 'app:' + id, page: 'apps', x: 16 + (i % 3) * 166, y: 80 + ((i / 3) | 0) * 84, w: 150, h: 76,
    label: 'OPEN ' + PAD_PAGES[id].tab, glyph: PAD_PAGES[id].tab, fs: 22, fn: () => padPageSet(id) }); });
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
/* padTable — the pad is a remote for its NEAREST table: its own position is the truth
   (held → follows your hand; placed → bound to the table it rests/hangs beside). */
const padTable = () => { const pp = padRec ? padRec.g.position : pos;
  let best = TABLES[0], bd = Infinity;
  TABLES.forEach(t => { const d = Math.hypot(t.rec.x - pp.x, t.rec.z - pp.z); if (d < bd) { bd = d; best = t; } });
  return best; };
const padMini = () => { const t = padTable(), mdl = t && t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  return mdl ? mdl.userData.mini : null; };
const padDraw = () => { const g = padCv.getContext('2d'), t = padTable(),
    cat = t ? (t.spec.catalog || t.spec.models) : [], cur = t && t.spec.models[0],
    idx = cur ? cat.findIndex(m => m.name === cur.name) : -1, mn = padMini();
  g.fillStyle = '#101218'; g.fillRect(0, 0, 512, 340);
  g.strokeStyle = '#3a3a4a'; g.lineWidth = 6; g.strokeRect(3, 3, 506, 334);
  g.textAlign = 'center';
  const btn = r => { const hov = padHover === r.id, pr = padPressId === r.id,
      on = (r.tab && r.tab === padPage) || (r.axis && mn && mn.axis === r.axis) || (r.rate !== undefined && mn && mn.spin === r.rate) || (r.lit && r.lit());
    g.fillStyle = pr ? '#2a3d4a' : on ? '#1e2e38' : hov ? '#1d2733' : '#14161c'; g.fillRect(r.x, r.y, r.w, r.h);
    g.strokeStyle = on || hov ? '#7fd4ff' : '#3a3a4a'; g.lineWidth = 4; g.strokeRect(r.x + 2, r.y + 2, r.w - 4, r.h - 4);
    g.fillStyle = pr || on ? '#7fd4ff' : '#5b5648'; g.font = 'bold ' + (r.fs || 40) + 'px monospace';
    g.fillText(r.glyph, r.x + r.w / 2, r.y + r.h / 2 + (r.fs ? r.fs * .36 : 14)); };
  PAD_REGIONS.filter(r => r.tab || (r.page === padPage && !(t && t.spec.noSlice && r.page === 'slice'))).forEach(btn);
  const pg = PAD_PAGES[padPage]; if (pg && pg.draw) pg.draw(g, { t, cat, cur, idx, mn });
  padTex.needsUpdate = true; };
const padPress = r => { padPressId = r.id; padDraw(); r.fn();
  setTimeout(() => { padPressId = null; padDraw(); }, 160); };
const padUvRegion = uv => { if (!uv) return null;
  const px = uv.x * 512, py = (1 - uv.y) * 340, noSl = !!(padTable() && padTable().spec.noSlice);
  return PAD_REGIONS.find(r => (r.tab || (r.page === padPage && !(noSl && r.page === 'slice')))
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
