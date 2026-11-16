'use strict';
/* construct/08_panels.js — in-scene panels — door console, spec plaque, mannequin kiosk (OUTFITS, BUTTONS).
   Split from construct.html (was lines 1322-1459); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── door console — the spawn panel. A hue-rimmed dark reader floating on the invisible
   elevator wall beside the door; E opens the roster dialog — load units in place, no
   crossing required. The roster is a list so future units just append a row. */
const CONS_POS = { x: -.58, y: .5, z: .66 };
const consG = new THREE.Group(); consG.position.set(CONS_POS.x, CONS_POS.y, CONS_POS.z); consG.rotation.y = Math.PI; scene.add(consG);
{
  const back = new THREE.Mesh(new THREE.PlaneGeometry(.17, .13), coreMat); back.position.z = -.008; consG.add(back);   // hue-riding rim — -.008 clears the face box's rear surface (coplanar = z-fight shimmer)
  const face = new THREE.Mesh(new THREE.BoxGeometry(.155, .115, .012), new THREE.MeshLambertMaterial({ color: 0x14161c })); consG.add(face);
  const pip = new THREE.Mesh(new THREE.BoxGeometry(.012, .012, .014), coreMat); pip.position.set(.055, .042, .002); consG.add(pip);          // status light
}
const consEl = document.createElement('div');
consEl.style.cssText = 'position:fixed;left:50%;top:46%;transform:translate(-50%,-50%);min-width:290px;background:rgba(12,12,18,.94);border:1px solid #34344a;padding:14px 16px;font:12px ui-monospace,Consolas,monospace;color:#cfc8b0;display:none;z-index:20';
document.body.appendChild(consEl);
let consOpen = false;
const ROSTER = [{ char: 'CHAIR1', label: 'THE CHAIR', desc: 'civilian unit — commission seat' }];
function renderCons() {
  consEl.innerHTML = '<div style="letter-spacing:.25em;color:#8f87a0;margin-bottom:10px">DOOR CONSOLE — PERSONNEL</div>' +
    ROSTER.map((r, i) => {
      const on = followers.some(f => f.spec.char === r.char);
      return '<div style="display:flex;align-items:center;gap:10px;padding:6px 0;border-top:1px solid #26242f">' +
        '<div style="flex:1"><div>' + r.label + '</div><div style="color:#6f6a58;font-size:10px">' + r.desc + '</div></div>' +
        '<button data-i="' + i + '" style="font:inherit;background:' + (on ? '#4a1f28' : '#1f3a2a') + ';color:#e8e0c8;border:1px solid ' + (on ? '#a33' : '#3a5') + ';padding:4px 10px;cursor:pointer">' + (on ? 'DISMISS' : 'SPAWN') + '</button></div>';
    }).join('') +
    '<div style="margin-top:10px;color:#5b5648;font-size:10px">E / Esc — close &nbsp;·&nbsp; click canvas to re-engage look</div>';
  consEl.querySelectorAll('button[data-i]').forEach(b => b.onclick = () => toggleSpawn(ROSTER[+b.dataset.i], b));
}
function toggleSpawn(r, btn) {
  const f = followers.find(x => x.spec.char === r.char);
  if (f) { scene.remove(f.CH); followers.splice(followers.indexOf(f), 1); if (povF === f) { window.__eyes.hide(); povF = followers[0] || null; } saveFol(); renderCons(); }
  else { btn.textContent = 'LOADING…'; emitChar({ char: r.char, order: 'follow', x: .4, z: .45 }).then(() => renderCons()); }
}
const openCons = () => { consOpen = true; renderCons(); consEl.style.display = 'block'; if (document.exitPointerLock) document.exitPointerLock(); };
const closeCons = () => { consOpen = false; consEl.style.display = 'none'; };
window.__console = { open: openCons, close: closeCons, on: () => consOpen, render: renderCons, spawn: r => toggleSpawn(r, { textContent: '' }) };

/* ── SPEC PLAQUE — the spec↔unity voxelSize table as a literal wall plate east of the door.
   Self-lit face (MeshBasic — always readable, never lit-dependent); brass-rimmed dark plate
   on the same floating-panel convention as the door console. */
const specCv = document.createElement('canvas'); specCv.width = 640; specCv.height = 460;
{ const g = specCv.getContext('2d');
  g.fillStyle = '#14121a'; g.fillRect(0, 0, 640, 460);
  g.strokeStyle = '#b08d4a'; g.lineWidth = 10; g.strokeRect(5, 5, 630, 450);
  g.fillStyle = '#e8d9b0'; g.font = 'bold 32px monospace'; g.textAlign = 'left';
  g.fillText('SPEC ↔ UNITY', 36, 60);
  g.fillStyle = '#9a9480'; g.font = '18px monospace';
  g.fillText('voxelSize · meters per cell', 36, 92);
  const specRows = [
    ['VEHICLE',   '.01',  '→ .01f',  '#5fd48a'],
    ['BUILDING',  '.1',   '→ 0.1',   '#f0a44a'],
    ['CHARACTER', '.01',  '→ .01f',  '#7fd4ff'],
    ['ITEM/DECOR','.005', '→ file',  '#ff9d5c'],
    ['FURN/PROP', '.0075','→ file',  '#e0c060'],
    ['ROOM',      '.03',  '→ —',     '#c9a0ff'],
  ];
  specRows.forEach((r, i) => { const y = 142 + i * 52;
    g.fillStyle = r[3]; g.fillRect(34, y - 24, 8, 32);
    g.fillStyle = '#e8d9b0'; g.font = 'bold 24px monospace'; g.fillText(r[0], 56, y);
    g.font = '24px monospace'; g.fillStyle = '#cfc8b0'; g.fillText(r[1], 310, y);
    g.fillStyle = r[3]; g.fillText(r[2], 430, y); });
}
const specTex = new THREE.CanvasTexture(specCv); specTex.colorSpace = THREE.SRGBColorSpace; specTex.anisotropy = 4;
const specBack = new THREE.Mesh(new THREE.BoxGeometry(.6, .43, .012), new THREE.MeshLambertMaterial({ color: 0x141018 }));
specBack.position.set(1.55, .6, .678); scene.add(specBack);
const specPlaque = new THREE.Mesh(new THREE.PlaneGeometry(.56, .4), new THREE.MeshBasicMaterial({ map: specTex }));
specPlaque.position.set(1.55, .6, .67); specPlaque.rotation.y = Math.PI;               // faces south into the room
scene.add(specPlaque);

/* ── MANNEQUIN — the NW-corner dressing stand: a second Vinny posed STATIC (never a
   follower — no roster, no heel, no PiP claim) + a floor pedestal kiosk whose screen
   face carries big pressable buttons. Aim + E (or LMB on the face) presses — outfits
   are region→material remaps, same law as the Commission's ApplyCivilianOutfits. */
const OUTFITS = [
  { name: 'SUIT BLUE',  map: { 3: 126, 4: 126, 6: 126, 7: 105 } },   // the canonical Civilian_01 remap
  { name: 'SUIT GREY',  map: { 3: 102, 4: 102, 6: 110, 7: 118 } },
  { name: 'SUIT BROWN', map: { 3: 122, 4: 122, 6: 122, 7: 118 } },
  { name: 'SUIT WHITE', map: { 3: 127, 4: 127, 6: 127, 7: 118 } },
  { name: 'WORK RED',   map: { 3: 120, 4: 120, 6: 109, 7: 118 } },
];
let mannOutfit = Math.max(0, Math.min(OUTFITS.length - 1, +localStorage.getItem('dvm_outfit') || 0));
const recolorChar = (f, map) => { const reg = f.rig.regions || {}, dflt = new THREE.Color(0x888888);
  f.rig.voxels.forEach((v, i) => f.inst.setColorAt(i, f.pal[map[reg[v[0] + ',' + v[1] + ',' + v[2]]] || v[3]] || dflt));
  f.inst.instanceColor.needsUpdate = true; };
const MANN_POS = { x: -2.5, z: .48 }, MANN_YAW = -.69;                 // char rig fwd = (-sin,-cos) — faces SE into the room
const KIOSK_POS = { x: -2.15, z: .06 }, KIOSK_YAW = 2.45;              // small floor stand dead ahead of him; +z screen faces the approach
const BUTTONS = [];                                                  // pressable face targets — aim + E/LMB
let kioWire = null, kioWireMat = null;                               // wireframe ghost-frame — plate child, slips onto the aimed button
let mannF = null;
const kioG = new THREE.Group(); kioG.position.set(KIOSK_POS.x, 0, KIOSK_POS.z); kioG.rotation.y = KIOSK_YAW; scene.add(kioG);
const mannCv = document.createElement('canvas'); mannCv.width = 512; mannCv.height = 340;
const mannTex = new THREE.CanvasTexture(mannCv); mannTex.colorSpace = THREE.SRGBColorSpace;
const mannDraw = press => { const g = mannCv.getContext('2d');
  g.fillStyle = '#101218'; g.fillRect(0, 0, 512, 340);
  g.strokeStyle = '#3a3a4a'; g.lineWidth = 6; g.strokeRect(3, 3, 506, 334);
  g.fillStyle = '#8f87a0'; g.font = '22px monospace'; g.textAlign = 'center'; g.fillText('— MANNEQUIN —', 256, 48);
  g.fillStyle = '#cfc8b0'; g.font = '18px monospace'; g.fillText('OUTFIT', 256, 110);
  g.font = 'bold 30px monospace';
  g.fillStyle = press === -1 ? '#7fd4ff' : '#5b5648'; g.fillText('◀', 88, 212);
  g.fillStyle = '#e8d9b0'; g.fillText(OUTFITS[mannOutfit].name, 256, 212);
  g.fillStyle = press === 1 ? '#7fd4ff' : '#5b5648'; g.fillText('▶', 424, 212);
  g.fillStyle = '#5b5648'; g.font = '15px monospace'; g.fillText('aim + E — or click the arrows', 256, 300);
  mannTex.needsUpdate = true; };
{
  const foot = new THREE.Mesh(new THREE.BoxGeometry(.4, .06, .32), new THREE.MeshLambertMaterial({ color: 0x0e1016 }));
  foot.position.y = .03; kioG.add(foot);                                          // low slab base
  const body = new THREE.Mesh(new THREE.BoxGeometry(.24, .16, .18), new THREE.MeshLambertMaterial({ color: 0x14161c }));
  body.position.y = .14; kioG.add(body);                                          // stub neck — the stand is knee-height, never hides the mannequin
  const plate = new THREE.Group(); plate.position.set(0, .26, .06); plate.rotation.x = -.88; kioG.add(plate);  // steep up-tilt — the face lies back toward the eye like a floor plaque
  const rim = new THREE.Mesh(new THREE.PlaneGeometry(.52, .36), coreMat); rim.position.z = -.003; plate.add(rim); // hue rim behind
  const scr = new THREE.Mesh(new THREE.PlaneGeometry(.46, .3), new THREE.MeshBasicMaterial({ map: mannTex }));
  plate.add(scr);
  kioWireMat = new THREE.LineBasicMaterial({ color: 0x35d5ff, transparent: true, opacity: .95 });
  kioWire = new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.BoxGeometry(.15, .1, .045)), kioWireMat);
  kioWire.visible = false; plate.add(kioWire);                        // wireframe ghost — only while a button is the aimed interact
  const arrShape = new THREE.Shape();                                // the little ▶ glyph — mirrored for ◀
  arrShape.moveTo(-.022, .026); arrShape.lineTo(.028, 0); arrShape.lineTo(-.022, -.026); arrShape.closePath();
  const arrGeo = new THREE.ShapeGeometry(arrShape);
  const mkBtn = (x, dir) => { const b = new THREE.Mesh(new THREE.BoxGeometry(.11, .08, .02),
      new THREE.MeshLambertMaterial({ color: 0x232733 })); b.position.set(x, -.033, .012); plate.add(b);
    const ar = new THREE.Mesh(arrGeo, coreMat); ar.scale.x = dir; ar.position.z = .011; b.add(ar);   // coreMat glyph — rides the trim's hue sweep
    const bd = { m: b, isBtn: true, label: () => 'E — ' + (dir < 0 ? '◀ SUIT' : 'SUIT ▶'),
      fn: () => { mannOutfit = (mannOutfit + dir + OUTFITS.length) % OUTFITS.length;
        localStorage.setItem('dvm_outfit', mannOutfit);
        if (mannF) recolorChar(mannF, OUTFITS[mannOutfit].map);
        mannDraw(dir); setTimeout(() => mannDraw(0), 160); } };
    b.userData.btn = bd; BUTTONS.push(bd); };
  mkBtn(-.14, -1); mkBtn(.14, 1);                                                 // ◀ ▶ flank the outfit name on the face
  mannDraw(0);
}
emitChar({ char: 'Civilian1', static: true, x: MANN_POS.x, z: MANN_POS.z, yaw: MANN_YAW, outfit: OUTFITS[mannOutfit].map })
  .then(f => { mannF = f; });
const kioskProbe = () => { if (Math.hypot(pos.x - KIOSK_POS.x, pos.z - KIOSK_POS.z) > 1.8) return null;
  _ray.setFromCamera(_ctr, camera);
  const h = _ray.intersectObjects(BUTTONS.map(b => b.m), false);
  if (h.length && kioWire) kioWire.position.copy(h[0].object.position);   // ghost slips onto the aimed button (same plate space)
  return h.length ? h[0].object.userData.btn : null; };
window.__mann = { f: () => mannF, outfit: () => mannOutfit, buttons: BUTTONS, press: i => BUTTONS[i] && BUTTONS[i].fn(),
  probe: kioskProbe, wire: () => kioWire,
  btnWorld: i => { const v = new THREE.Vector3(); BUTTONS[i].m.getWorldPosition(v); return [+v.x.toFixed(3), +v.y.toFixed(3), +v.z.toFixed(3)]; } };
