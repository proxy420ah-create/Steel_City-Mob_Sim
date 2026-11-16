import io
p = r'docs/construct/04_pad.js'
src = io.open(p, encoding='utf-8').read()
a = src.index('/* Pad pages')
b = src.index('const padSwap')
new = u"""/* Pad pages — a page is registry data {tab, draw}: the tab strip GENERATES itself from the
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
"""
src = src[:a] + new + src[b:]
io.open(p, 'w', encoding='utf-8').write(src)
print('patched: pages + regions replaced, span', b - a, '->', len(new))
