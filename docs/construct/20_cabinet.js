'use strict';
/* construct/20_cabinet.js — the drafting cabinet browser. The Commission's filing-cabinet
   contract replicated on the Construct's placed commission_filecabinet prop: oak+brass+
   manila accordion drawers, read-only file cards, E at the cabinet opens (INTERACT
   registry), Esc/E/✕/backdrop closes. Content scope (BOSS decree): the in-work buildings
   (tenement, barber + variants), terrain & lots (roads, alleys, empty plots), what's
   currently deployed in the room, and the design law they ride. Meta rides /voxel/<name>
   fetches — dims/voxelSize/materials are live reads, not copied numbers. */

const cabEl = document.createElement('div');
cabEl.id = 'cabinet';
cabEl.style.cssText = 'position:fixed;inset:0;z-index:21;display:none;align-items:center;justify-content:center;background:rgba(13,11,8,.82);font-family:Consolas,monospace';
document.body.appendChild(cabEl);
const cabSt = document.createElement('style');
cabSt.textContent = `
  #cabinet .cb { width:min(620px,94%); height:min(88%,560px); display:flex; flex-direction:column;
    background:#241a12; border:2px solid #3c241e; border-radius:8px; box-shadow:0 18px 60px rgba(0,0,0,.6); }
  #cabinet .cb-head { display:flex; align-items:center; padding:8px 14px; border-bottom:2px solid #3c241e; color:#d8c9a3; letter-spacing:.22em; font-size:.72em; }
  #cabinet .cb-x { margin-left:auto; cursor:pointer; color:#8a7a63; border:1px solid #8a7a63; border-radius:5px; padding:0 8px; }
  #cabinet .cb-x:hover { color:#e6d5b0; border-color:#e6d5b0; }
  #cabinet .cb-drawers { flex:1; display:flex; flex-direction:column; gap:6px; padding:8px; min-height:0; }
  .cb-drawer { flex:none; display:flex; flex-direction:column; min-height:0; }
  .cb-drawer.open { flex:1; }
  .cb-face { flex:none; display:flex; align-items:center; gap:10px; height:34px; padding:0 14px; cursor:pointer;
    background:linear-gradient(#3c241e,#2e1d16); border:1px solid #4a3226; border-radius:4px;
    color:#d8c9a3; letter-spacing:.18em; font-size:.7em; user-select:none; }
  .cb-face:hover { border-color:#8a7a63; }
  .cb-face .cb-pull { margin-left:auto; width:46px; height:8px; border-radius:4px; background:#8a7a63; flex:none; }
  .cb-inner { flex:0 0 auto; min-height:0; max-height:0; overflow:hidden; padding:0 10px;
    background:#241a12; border:1px solid transparent; border-radius:0 0 4px 4px; scrollbar-width:thin;
    transition:max-height .3s ease, padding .2s ease; }
  .cb-drawer.open .cb-inner { flex:1; max-height:70vh; overflow-y:auto; padding:8px 10px; border-color:#4a3226; }
  .cb-file, .cb-note { display:flex; align-items:center; gap:8px; padding:5px 9px; margin-bottom:5px;
    background:#d8c9a3; color:#241a12; border-radius:3px; font-size:.78em; }
  .cb-note { background:#b8b0a0; cursor:default; }
  .cb-file { cursor:default; }
  .cb-file .cb-ft { white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .cb-file .fk { background:rgba(255,255,255,.35); }
  .cb-sub { padding:2px 9px 6px 26px; color:#d8c9a3; font-size:.68em; letter-spacing:.04em; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .cb-sub b { color:#e6d5b0; font-weight:normal; }
  .cb-h { color:#d8c9a3; letter-spacing:.14em; font-size:.68em; margin:8px 0 4px; }
  .cb-swatch { display:inline-block; width:9px; height:9px; border-radius:2px; margin-right:3px; vertical-align:-1px; border:1px solid rgba(0,0,0,.4); }`;
document.head.appendChild(cabSt);

/* ── the card set — static rows + live drawers. `fetch` names pull /voxel/<name> meta once. ── */
const CAB_DRAWERS = ['D1 · BUILDINGS', 'D2 · TERRAIN & LOTS', 'D3 · DEPLOYED', 'D4 · DESIGN LAW'];
const VI = 'voxel_items/', VB = 'voxel_buildings/', VV = 'voxel_vehicles/';
const CAB_FILES = [
  { d: 1, k: 'bldg', t: 'TENEMENT BLOCK 0 — hero facade, spans the full 3×3 interior', v: 'tenement_block_0',
    paths: [VB + 'tenement_block_0.stasset', VI + 'tenement_block_0.json'] },
  { d: 1, k: 'bldg', t: 'BARBER SHOP — single-lot storefront (slot 2 of block_41)', v: 'barber_shop',
    paths: [VB + 'barber_shop.stasset', VI + 'barber_shop.json'] },
  { d: 1, k: 'variant', t: 'barber_shop__roof_1 — staged roof variant', v: 'barber_shop__roof_1', paths: [VI + 'barber_shop__roof_1.json'] },
  { d: 1, k: 'variant', t: 'barber_shop__roof_2 — staged roof variant', v: 'barber_shop__roof_2', paths: [VI + 'barber_shop__roof_2.json'] },
  { d: 2, k: 'law', t: 'TILE CONTRACT — 322v² tile: 65v margin (20 walk + 45 half-road) + 192v interior',
    sub: 'BLK in <b>construct/03_tables.js</b> — the port of <b>Assets/Scripts/UI/VoxelTerrainBuilder.cs</b> GeneratePerBlockTerrain' },
  { d: 2, k: 'recipe', t: '102 CONCRETE — the sidewalk: 20v property-line ring at road grade, curb-cut at alley mouths', sub: 'fillBlockGround → FillGroundTile — material band, never raised geometry' },
  { d: 2, k: 'recipe', t: '104 + 105 — asphalt half-roads; cobble stripe mid-half → adjacent tiles read dual lane lines', sub: 'FillCorridors — seam law: zones lay strict halves, solo dioramas paint both' },
  { d: 2, k: 'recipe', t: '101 STONE — lot interior base: 3×3 of 64v lots under the sidewalk ring', sub: 'lane bitmask N1 E2 S4 W8 — mouths cut the ring, interior lanes cross-section' },
  { d: 2, k: 'lot', t: 'EMPTY LAND — the authored vacant-lot tile; every unstamped lot gets it', v: 'empty_land', paths: [VB + 'empty_land.stasset', VI + 'empty_land.json'] },
  { d: 2, k: 'lot', t: 'ALLEY LANE / _NS — empty placeholder slots; the lane IS the cut in the ground', v: 'alley_lane', paths: [VB + 'alley_lane.stasset', VB + 'alley_lane_ns.stasset', VI + 'alley_lane.json'] },
  { d: 2, k: 'lot', t: 'ALLEY BLOCK / _NS — the alley-bearing block interior', v: 'alley_block', paths: [VB + 'alley_block.stasset', VB + 'alley_block_ns.stasset', VI + 'alley_block.json'] },
  { d: 2, k: 'lot', t: 'ROAD TILE 0 — standalone road slab', v: 'road_tile_0', paths: [VB + 'road_tile_0.stasset', VI + 'road_tile_0.json'] },
  { d: 3, k: 'lot', t: 'DEBRIS TEST LOT — the rubble field stamped in the zone exhibit', v: 'debris_test_lot', paths: [VB + 'debris_test_lot.stasset', VI + 'debris_test_lot.json'] },
  { d: 3, k: 'vehicle', t: 'VEHICLE 490 TOURING — the parked curb cars', paths: [VV + 'vehicle_490_touring.stasset', VV + 'vehicle_490_touring.json'] },
  { d: 3, k: 'prop', t: 'COMMISSION FILECABINET — this cabinet', v: 'commission_filecabinet', paths: [VI + 'commission_filecabinet.json'] },
];

let cabOn = false, cabDrawer = 1, cabWasLocked = false, cabMeta = {};
const cabRec = () => PLACED.find(p => p.name === 'commission_filecabinet');
const cabMetaOf = n => { const p = cabMeta[n] || (cabMeta[n] = fetch('/voxel/' + n).then(r => r.json()).catch(() => null)); return p; };
const cabRow = f => {
  const head = `<div class="cb-file"><span class="fk k-${f.k}">${f.k}</span><span class="cb-ft">${f.t}</span></div>`;
  const subs = [];
  if (f.sub) subs.push(`<div class="cb-sub">${f.sub}</div>`);
  if (f.paths) subs.push(`<div class="cb-sub">${f.paths.map(p => '· ' + p).join(' &nbsp; ')}</div>`);
  if (f.v) { subs.push(`<div class="cb-sub" data-meta="${f.v}">· reading meta…</div>`);
    cabMetaOf(f.v).then(d => { if (!d) return;
      const el2 = cabEl.querySelector(`[data-meta="${f.v}"]`); if (!el2) return;
      const [W, H, D] = d.dims || [0, 0, 0], vs = d.voxelSize || 0;
      const sw = (d.materials || []).slice(0, 10).map(m => `<i class="cb-swatch" style="background:${m.hex}" title="${m.name}"></i>`).join('');
      el2.innerHTML = `· ${W}×${H}×${D} @ ${vs}m → <b>${(W * vs).toFixed(2)}×${(H * vs).toFixed(2)}×${(D * vs).toFixed(2)}m</b> · ${(d.materials || []).length} mats ${sw}`; }); }
  return head + subs.join('');
};

function renderCab() {
  const bodies = [];
  bodies[0] = CAB_FILES.filter(f => f.d === 1).map(cabRow).join('');
  bodies[1] = CAB_FILES.filter(f => f.d === 2).map(cabRow).join('');
  bodies[2] = `<div class="cb-h">TABLES — live mounts</div>` +
    TABLES.map(t => `<div class="cb-note"><span class="cb-ft"><b>${t.spec.id}</b> — ${(t.spec.catalog || []).map(m => m.name).join(', ') || 'empty'}</span></div>`).join('') +
    `<div class="cb-h">PLACED — in the room</div>` +
    PLACED.map(r => `<div class="cb-note"><span class="cb-ft">${r.name} @ ${r.x.toFixed(1)},${r.z.toFixed(1)}${r.source === 'extra' ? ' · extra' : ''}</span></div>`).join('') +
    CAB_FILES.filter(f => f.d === 3).map(cabRow).join('');
  bodies[3] = `<div class="cb-note"><span class="cb-ft">reading DESIGN_CATALOG.md…</span></div>`;
  cabEl.innerHTML = `<div class="cb"><div class="cb-head">THE FILE CABINET — drafting room archive<span class="cb-x" id="cb_x">✕</span></div>` +
    `<div class="cb-drawers">` + CAB_DRAWERS.map((n, i) =>
      `<div class="cb-drawer${cabDrawer === i + 1 ? ' open' : ''}"><div class="cb-face" data-d="${i + 1}"><span>${n}</span><span class="cb-pull"></span></div>` +
      `<div class="cb-inner">${bodies[i]}</div></div>`).join('') + `</div></div>`;
  cabEl.querySelector('#cb_x').onclick = () => cabClose();
  fetch('/DESIGN_CATALOG.md').then(r => r.text()).then(md => {
    if (!cabOn) return;
    const d4 = cabEl.querySelectorAll('.cb-inner')[3]; if (!d4) return;
    /* parse the catalog's entry contract — '### name' + '- domain/value/source/verified/notes' rows —
       so the drawer reads as law cards, not a table of contents */
    const entries = [], intro = [];
    let fence = false;
    for (const ln of md.split('\n')) {
      if (ln.startsWith('```')) { fence = !fence; continue; }
      if (fence) continue;                                                // the '## Entry format' block is a template, not an entry
      const h = ln.match(/^###\s+(.+)/), kv = ln.match(/^-\s*(domain|value|source|verified|notes)\s*:\s*(.+)/);
      if (h) entries.push({ name: h[1].trim() });
      else if (kv && entries.length) entries[entries.length - 1][kv[1]] = kv[2].trim();
      else if (!entries.length && ln.trim() && !ln.startsWith('#') && !ln.startsWith('```')) intro.push(ln.trim()); }
    const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');
    d4.innerHTML = (entries.length
      ? entries.map(en =>
          `<div class="cb-file"><span class="fk">${esc(en.domain || 'law')}</span><span class="cb-ft">${esc(en.name)}</span></div>` +
          (en.value ? `<div class="cb-sub">${esc(en.value)}</div>` : '') +
          `<div class="cb-sub">· ${esc(en.source || '')}${en.verified ? ' &nbsp;· verified ' + esc(en.verified) : ''}</div>` +
          (en.notes ? `<div class="cb-sub">· ${esc(en.notes)}</div>` : '')).join('')
      : '<div class="cb-note"><span class="cb-ft">catalog unreadable</span></div>') +
      (intro.length ? `<div class="cb-h">${esc(intro[0].replace(/\*\*/g, ''))}</div>` : '');
  }).catch(() => {});
}
const cabOpen = () => { renderCab(); cabOn = true; cabEl.style.display = 'flex';
  cabWasLocked = !!document.pointerLockElement; if (cabWasLocked) document.exitPointerLock(); };   // browsing wants a cursor — re-grab on close
const cabClose = () => { cabOn = false; cabEl.style.display = 'none';
  if (cabWasLocked) { cabWasLocked = false; grabLook(); } };
cabEl.addEventListener('click', e => { const face = e.target.closest('.cb-face');
  if (face) { cabDrawer = +face.dataset.d === cabDrawer ? 0 : +face.dataset.d; renderCab(); }
  else if (e.target === cabEl) cabClose(); });                                          // backdrop click = put the folder back
addEventListener('keydown', e => { if (!cabOn) return;
  const k = (e.key || '').toLowerCase();
  if (k === 'escape' || k === 'e') { e.preventDefault(); e.stopPropagation(); cabClose(); } }, true);   // capture — close eats the key, game never sees it

/* the prop is furniture, not carry stock — flip pickable inside the scan getters so 'E — TAKE'
   can never preempt 'E — OPEN', however the rec got here (boot, emit, paste). Runs every frame:
   emits land pickable:true and would revert a one-shot fix. place-mode still moves it — placePick
   keys off parent, not the flag. */
INTERACT.push({ get x() { const r = cabRec(); if (r && r.pickable) r.pickable = false; return r ? r.x : 1e9; },
                get z() { const r = cabRec(); return r ? r.z : 1e9; },
                r: 1.0, label: () => 'E — OPEN CABINET', fn: cabOpen });
window.__cabinet = { open: cabOpen, close: cabClose, on: () => cabOn, drawer: () => cabDrawer };
