'use strict';
/* construct/13_place.js — place mode — selection, carry, settleOrphans, undo, paste, __place.
   Split from construct.html (was lines 1794-2009); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── PLACE MODE — X toggles the furniture editor (the Commission's X-mode, Construct edition):
   free-fly camera off your body (Vinny stays planted), LMB selects a placed asset which then
   rides your aim ray and seats on measured surfaces; [ ] rotate 15°, C copies its spec,
   V pastes a duplicate (auto-selected), DEL removes. Layout persists to localStorage —
   manifest items by name (dvm_place), pasted copies as dvm_extra re-emitted at boot. */
let placeOn = false, placeSel = null, placeClip = null;
const flyPos = new THREE.Vector3(0, .6, -1), _fd = new THREE.Vector3();
const placeEl = document.createElement('div');
placeEl.style.cssText = 'position:fixed;left:50%;bottom:14px;transform:translateX(-50%);background:rgba(8,8,14,.92);border:1px solid #6a6a92;color:#e2dbc2;padding:5px 12px;font:10px ui-monospace,Consolas,monospace;letter-spacing:.1em;display:none;z-index:8;pointer-events:none;white-space:nowrap';
const PLACE_LEGEND = 'PLACE — WASD/QE fly · ALT crawl · LMB select · CTRL+LMB table · SHIFT+LMB drag · ARROWS move (SHIFT coarse · ALT 1mm) · , . rotate (ALT 1°) · PgUp/Dn height · B re-seat · N cell-fine · P pendant · ENTER confirm · ESC revert · C copy · V paste · DEL remove · Z scope · ^Z undo · X exit';
placeEl.textContent = PLACE_LEGEND;
document.body.appendChild(placeEl);
const placeStat = document.createElement('div');
placeStat.style.cssText = placeEl.style.cssText + ';bottom:40px;border-color:#4a4a62;color:#b8b0a0';
document.body.appendChild(placeStat);
let placeSayT = 0;
const placeSay = msg => { placeEl.textContent = msg; clearTimeout(placeSayT);
  placeSayT = setTimeout(() => { placeEl.textContent = PLACE_LEGEND; }, 1600); };
const selBox = new THREE.Box3Helper(new THREE.Box3(), 0xffd070); selBox.visible = false; scene.add(selBox);
const origBox = new THREE.Box3Helper(new THREE.Box3(), 0x8090c0); origBox.visible = false; scene.add(origBox);
const placeGrid = new THREE.GridHelper(9.4, 94, 0x8a7a64, 0xbdb4a4); placeGrid.position.set(0, .002, -2.85);
placeGrid.visible = false; scene.add(placeGrid);                                       // .1 cells over the platform — snaps drag to measured increments
const _ray = new THREE.Raycaster(), _ctr = new THREE.Vector2(0, 0), mouseNDC = new THREE.Vector2();
const PLACE_SNAP = .1, placeUndo = [];
const placeSnapOp = op => { placeUndo.push(op); if (placeUndo.length > 32) placeUndo.shift(); };

const placeSave = () => {                                                        // manifest items persist by name; pastes persist as extras
  const over = {}, extra = [];
  PLACED.forEach(p => { if (p._parked) return;                                    // parked cars are the TABLE's scenery — their x/z are miniG-LOCAL: writing them as name overrides stamps local coords onto the shared asset name (the spinner-car displacement class)
    const o = { x: +p.x.toFixed(3), z: +p.z.toFixed(3), rotY: +p.rotY.toFixed(3), mv: p.mv || 0, pend: p.pendWant | 0 };   // the attached light is a saved property of the rec
    if (!p.mount) o.y = +p.y0.toFixed(3);                                       // the placed seat height — the restore cap honors it (mounts carry their own y)
    const tl = tiltOf(p); if (tl) o.tilt = tl;
    if (p.mount) o.mount = { x: +p.mount.x.toFixed(3), y: +p.mount.y.toFixed(3), z: +p.mount.z.toFixed(3), n: p.mount.n };
    if (p.vsScale && p.vsScale !== 1) o.vsScale = p.vsScale;                   // scaled miniatures keep their shrink across reloads
    if (p.outfit != null) o.outfit = p.outfit;                                 // char wardrobe keeps its suit across reloads
    if (p.source === 'extra') extra.push({ name: p.name, kind: p.kind, pickable: p.pickable, solids: p.solids, ...o });
    else over[p.name] = o; });
  localStorage.setItem('dvm_place', JSON.stringify(over));
  localStorage.setItem('dvm_extra', JSON.stringify(extra));
};
const placeApply = (p, x, z, rotY) => {                                          // tops stay local — surface math survives any move
  p.x = x; p.z = z; if (rotY !== undefined) p.rotY = rotY;
  p.mount = null;                                                              // any move unsticks a wall mount
  p.g.position.set(x, p.y0, z); p.g.rotation.set(0, p.rotY, 0);
  p.bbox = footB(p.lb, x, z, p.rotY);                                            // footprint tracks the item — carry/settle/re-seat see it live
  if (p._block) { const s2 = (p.lb[1] - p.lb[0]) / 2;                            // a table's walk-block follows its drag, same as solids
    p._block.x0 = x - s2 - .15; p._block.x1 = x + s2 + .15; p._block.z0 = z - s2 - .15; p._block.z1 = z + s2 + .15; }
  refreshSolids();
};
const placePick = allowHost => {                                                 // ray through screen centre → {p, x,y,z hit point}
  _ray.setFromCamera(_ctr, camera);
  const hits = rayPruned(_ray, PLACED.filter(p => p !== held && p.g.parent === scene).map(p => p.g)); // the selection stays pickable — ⇧+LMB re-grabs it into drag
  for (const h of hits) { let o = h.object; while (o && !o.userData.pRec) o = o.parent;
    if (o && (allowHost || !o.userData.pRec.name.startsWith('table:')))            // stage hosts skip plain LMB — a stray click grabbed the whole table and nudged the set under the dolls; CTRL+LMB still grabs
      return { p: o.userData.pRec, x: h.point.x, y: h.point.y, z: h.point.z }; }
  return null;
};
const placeGrab = (p, hit) => {                                                  // select — the Commission's stage-1: highlighted, keyboard-owned
  if (placeSel && placeSel !== p) placeDrop();                                   // one selection at a time — a fresh grab commits the old
  placeSel = p; p._carry = carryOf(p); p._drag = false;
  const cy = Math.cos(p.rotY), sy = Math.sin(p.rotY);
  const dx = hit ? hit.x - p.x : 0, dz = hit ? hit.z - p.z : 0;
  p._grab = { lx: dx * cy - dz * sy, lz: dx * sy + dz * cy };                      // local-frame grab offset — rotates with the item
  p._orig = { x: p.x, z: p.z, rotY: p.rotY, y0: p.y0 };
  origBox.box.setFromObject(p.g); origBox.visible = true;                          // the "was" marker, Commission-style
  placeGrid.visible = true;
  return true;
};
const inB = (q, b) => q.x > b[0] && q.x < b[1] && q.z > b[2] && q.z < b[3];
const settleOrphans = (moved, oldB, newB, oldTop) => {                           // re-seat ONLY items the move touched — a global
  PLACED.forEach(q => {                                                         // sweep lets co-located siblings (door↔frame) leapfrog
    if (q === moved || q === held || q.g.parent !== scene || ((moved && moved._carry) || []).some(c => c.q === q)) return;   // parented recs (parked cars, the spinner's car) — local x/z false-positive world boxes, and the orphan sweep can't see their frame
    if (oldB && inB(q, oldB) || newB && inB(q, newB)) {
      const s = worldSurfaceAt(q.x, q.z, q, { surfOnly: q.surf });
      if (s < q.y0 && q.y0 <= oldTop + .02) { q.y0 = s; q.g.position.y = s; } } }); };                 // orphans drop; lifted floaters keep their height
const placeRevert = () => {                                                      // Esc on a selection = back out — restore the pre-grab pose
  if (!placeSel) return; const p = placeSel, o = p._orig;
  if (o) { const eb = new THREE.Box3().setFromObject(p.g);                        // the edited pose's bounds — orphans sat inside it
    const eB = [eb.min.x - .05, eb.max.x + .05, eb.min.z - .05, eb.max.z + .05];
    p.y0 = o.y0; placeApply(p, o.x, o.z, o.rotY);
    (p._carry || []).forEach(c => { c.q.x = c.ox; c.q.z = c.oz; c.q.y0 = c.oy; c.q.g.position.set(c.ox, c.oy, c.oz); });
    settleOrphans(p, eB, p.bbox, o.y0 + (eb.max.y - p.y0)); }                    // p.bbox still holds the grab-time footprint
  p._carry = null; p._orig = null; p._drag = false;
  placeSel = null; selBox.visible = false; origBox.visible = false; placeGrid.visible = false;
  placeStat.textContent = '';
  refreshSolids(); placeSay('REVERTED'); };
const placeUndoDo = () => {                                                      // ^Z — one op back, whole-layout truth preserved
  const op = placeUndo.pop(); if (!op) return;
  if (op.t === 'move' && PLACED.includes(op.p)) { const p = op.p, ob = p.bbox;    // stale bounds = the post-move footprint orphans sat in
    p.y0 = op.prev.y0;
    if (op.prev.mount) { p.mount = op.prev.mount; p.x = op.prev.x; p.z = op.prev.z; p.rotY = op.prev.rotY;   // undo back to a wall mount stays on the wall
      p.g.position.set(op.prev.mount.x, op.prev.mount.y, op.prev.mount.z); padMountQ(p.g, op.prev.mount.n); p.bbox = footB(p.lb, p.x, p.z, p.rotY); refreshSolids(); }
    else placeApply(p, op.prev.x, op.prev.z, op.prev.rotY);
    const bb = new THREE.Box3().setFromObject(p.g); p.bbox = [bb.min.x - .05, bb.max.x + .05, bb.min.z - .05, bb.max.z + .05];
    (op.prev.carry || []).forEach(c => { if (PLACED.includes(c.q)) { c.q.x = c.ox; c.q.z = c.oz; c.q.y0 = c.oy; c.q.g.position.set(c.ox, c.oy, c.oz); } });
    settleOrphans(p, ob, p.bbox, op.prev.y0 + (bb.max.y - p.y0)); }
  else if (op.t === 'add') { const i = PLACED.indexOf(op.p);
    if (i >= 0) { scene.remove(op.p.g); PLACED.splice(i, 1); settleOrphans(op.p, op.p.bbox, null, Infinity); } }   // host gone — drop what rode it
  else if (op.t === 'del') { (op.spec._spec
      ? Promise.resolve(emitTable(op.spec._spec)).then(t => { op.spec._spec.models.forEach(m => (m.zone ? emitZone : m.slots ? emitBlock : emitBldg)(t, m));
          placeApply(t.rec, op.spec.x, op.spec.z, op.spec.rotY);
          if (t.rec.pendWant && typeof pendAttach === 'function') pendAttach(t.rec); })   // the restored table's attached light comes back with it
      : emitAsset(op.spec.name, { kind: op.spec.kind, x: op.spec.x, z: op.spec.z,
    rotY: op.spec.rotY, pickable: op.spec.pickable, solids: op.spec.solids, source: op.spec.source, pendant: op.spec.pendant }));
    if (op.spec.source !== 'extra') { const d = new Set(JSON.parse(localStorage.getItem('dvm_del') || '[]'));
      d.delete(op.spec.name); localStorage.setItem('dvm_del', JSON.stringify([...d])); } }                  // un-delete lifts the tombstone
  refreshSolids(); placeSave(); placeSay('UNDO');
};
const carryOf = sel => PLACED.filter(q => q !== sel && q !== held && q.g.parent === scene &&   // passengers: items hosted on the selection ride along
  q.y0 > sel.y0 + .02 && q.x > sel.bbox[0] && q.x < sel.bbox[1] && q.z > sel.bbox[2] && q.z < sel.bbox[3])
  .map(q => { const cy = Math.cos(sel.rotY), sy = Math.sin(sel.rotY), dx = q.x - sel.x, dz = q.z - sel.z;
    return { q, lx: dx * cy - dz * sy, lz: dx * sy + dz * cy, dy: q.y0 - sel.y0, ox: q.x, oz: q.z, oy: q.y0 }; });
const carrySync = p => {                                                          // passengers ride whenever the host moves — any source
  const cy = Math.cos(p.rotY), sy = Math.sin(p.rotY);
  const skip = new Set([p, ...(p._carry || []).map(c => c.q)]);
  (p._carry || []).forEach(({ q, lx, lz, dy }) => {
    q.x = p.x + lx * cy + lz * sy; q.z = p.z - lx * sy + lz * cy;
    const top = surfaceAt(p, q.x, q.z);
    q.y0 = top > 0 ? top : placeSurf(q.x, q.z, skip);                              // off the host's columns → whatever's beneath
    q.g.position.set(q.x, q.y0, q.z); });
};
const placeSurf = (wx, wz, skipSet, opt = {}) => { let t = 0, cap = opt.cap ?? Infinity;
  PLACED.forEach(p => { if (skipSet.has(p) || (opt.surfOnly && !p.surf)) return;                    // furniture never floors on riders
    let bx = wx, bz = wz; const par = p.g.parent;
    if (par && par !== scene) { _svW.set(wx, 0, wz); par.worldToLocal(_svW); bx = _svW.x; bz = _svW.z; }   // parented host (spinner/parked cars): the stored bbox is parent-LOCAL — test in that frame or it never rejects (the parked-cars march hitch)
    if (bx + .12 < p.bbox[0] || bx - .12 > p.bbox[1] || bz + .12 < p.bbox[2] || bz - .12 > p.bbox[3]) return;   // bbox reject — the drop-march ran every host's column scan every ray-step (the carry hitch)
    window.__scanN = (window.__scanN || 0) + 1;
    const s = surfaceAt(p, wx, wz); if (s > t && s <= cap) t = s; }); return t; };                  // cap = settle-only: nothing climbs at re-seat
const _sn = new THREE.Vector3();
const placeSpot = () => {                                                        // where the aim ray lands — the measured-surface law
  _ray.setFromCamera(_ctr, camera);
  const hits = rayPruned(_ray, PLACED.filter(p => p !== placeSel && p !== held && p.g.parent === scene).map(p => p.g));
  let wx, wz, landed = false;
  for (const h of hits) { if (h.face) { _sn.copy(h.face.normal).transformDirection(h.object.matrixWorld);
      if (_sn.y <= .5) continue; }                                               // wall/side faces are no landing — dragging across a facade used to teleport the item onto that column's roof
    wx = h.point.x; wz = h.point.z; landed = true; break; }
  if (!landed) { if (hits.length) return null;                                   // every hit was a wall — drag holds its last good spot
    const d = _ray.ray.direction; if (d.y > -.01) return null;                     // level/up — no floor crossing
    const t = -_ray.ray.origin.y / d.y; wx = _ray.ray.origin.x + d.x * t; wz = _ray.ray.origin.z + d.z * t; }
  const skip = new Set([placeSel, ...((placeSel && placeSel._carry) || []).map(c => c.q)]);
  return { x: wx, y: placeSurf(wx, wz, skip, { surfOnly: placeSel && placeSel.surf }), z: wz };
};
/* ── CELL-FINE stage (N) — the second placement stage for work ON voxel models: once a
   selection is held, N binds nudges to the lattice of the grid-bearing mini beneath it.
   Arrows walk one model cell along the host's own axes, ,. rotate 1°, PgUp/Dn one voxel
   of height, drag + nudges snap to cell centres, and the seat reads the exact column top
   from mn._grid — no 5mm column quantisation, no drift. Off-lattice → 1mm fallback. */
let placeCell = false;
const _cv = new THREE.Vector3(), _cq = new THREE.Quaternion(), _ce = new THREE.Euler();
const cellHost = p => {                                                          // grid mini beneath the selection → {o, mn, vs, W, D} | null
  let best = null, bs = 1e9;
  PLACED.forEach(h => { if (h === p || !h.g) return;
    h.g.traverse(o => { const mn = o.userData && o.userData.mini;
      if (!mn || !mn._grid) return;
      _cv.set(p.x, p.y0 + .02, p.z); o.worldToLocal(_cv);
      const vs = mn.vs, W = mn.dims[0], D = mn.dims[2];
      const cx = Math.floor(_cv.x / vs + W / 2), cz = Math.floor(_cv.z / vs + D / 2);   // centred lattice — fx/fz place cell i at (i − W/2 + .5)·vs
      if (cx < 0 || cx >= W || cz < 0 || cz >= D || !mn.cellOf) return;
      const g = mn.cellOf(_cv.x, _cv.z), t = mn._grid.tops[g[0] + W * g[1]], d = Math.abs(_cv.y - (t + 1) * vs);
      if (d < bs) { bs = d; best = { o, mn, vs, W, D }; } }); });
  return best;                                                                  // x/z containment is the host test — y only ranks overlaps (measured seats quantize above the exact cell top)
};
const cellSnap = (host, wx, wz) => {                                             // world → host cell centres → world (x/z only)
  _cv.set(wx, 0, wz); host.o.worldToLocal(_cv);
  _cv.x = (Math.floor(_cv.x / host.vs + host.W / 2) - host.W / 2 + .5) * host.vs;
  _cv.z = (Math.floor(_cv.z / host.vs + host.D / 2) - host.D / 2 + .5) * host.vs;
  host.o.localToWorld(_cv); return { x: _cv.x, z: _cv.z }; };
const cellSeat = (host, wx, wz) => {                                             // exact column-top world y at a point — null off-lattice
  _cv.set(wx, 0, wz); host.o.worldToLocal(_cv);
  const cx = Math.floor(_cv.x / host.vs + host.W / 2), cz = Math.floor(_cv.z / host.vs + host.D / 2);
  if (cx < 0 || cx >= host.W || cz < 0 || cz >= host.D || !host.mn.cellOf) return null;
  const g = host.mn.cellOf(_cv.x, _cv.z), t = host.mn._grid.tops[g[0] + host.W * g[1]]; if (t < 0) return null;
  _cv.set(0, (t + 1) * host.vs, 0); host.o.localToWorld(_cv); return _cv.y; };
const cellAxes = host => {                                                        // host lattice's world-frame yaw — arrows walk model axes
  host.o.getWorldQuaternion(_cq); _ce.setFromQuaternion(_cq, 'YXZ'); return _ce.y; };

const placeDrop = () => {                                                        // commit the drag — write the override
  if (!placeSel) return; const moved = placeSel;
  moved.y0 = moved.g.position.y;
  const ob = moved._orig ? footB(moved.lb, moved._orig.x, moved._orig.z, moved._orig.rotY) : footB(moved.lb, moved.x, moved.z, moved.rotY);   // pre-move footprint — the orphans lived here
  settleOrphans(moved, ob, moved.bbox, (moved._orig ? moved._orig.y0 : moved.y0) + topOf(moved));
  const o = moved._orig || moved;
  if (o.x !== moved.x || o.z !== moved.z || o.rotY !== moved.rotY || o.y0 !== moved.y0)
    placeSnapOp({ t: 'move', p: moved, prev: { x: o.x, z: o.z, rotY: o.rotY, y0: o.y0,
      carry: (moved._carry || []).map(c => ({ q: c.q, ox: c.ox, oz: c.oz, oy: c.oy })) } });   // passengers return with the host
  moved._carry = null; moved._orig = null; moved._drag = false;
  placeSave(); placeSel = null; selBox.visible = false; origBox.visible = false; placeGrid.visible = false;
  placeStat.textContent = '';
};
const placePaste = () => {
  if (!placeClip) { placeSay('NOTHING COPIED — C FIRST'); return; }
  const s = placeSpot(); if (!s) { placeSay('AIM AT A SURFACE'); return; }
  emitAsset(placeClip.name, { kind: placeClip.kind,
    x: Math.round(s.x / PLACE_SNAP) * PLACE_SNAP, z: Math.round(s.z / PLACE_SNAP) * PLACE_SNAP,
    rotY: placeClip.rotY, pickable: placeClip.pickable, solids: placeClip.solids, source: 'extra', seatY: s.y,
    pendant: placeClip.pendant, vsScale: placeClip.vsScale, fit: placeClip.outfit })
    .then(r => { if (r) { if (placeSel) placeDrop();                             // the paste takes the selection — commit whatever was held
      placeSel = r; r._carry = []; r._drag = false; r._grab = { lx: 0, lz: 0 };
      r._orig = { x: r.x, z: r.z, rotY: r.rotY, y0: r.y0 };
      placeSnapOp({ t: 'add', p: r }); placeSave(); placeSay('PASTED ' + r.name); } });
};
const placeToggle = on => {
  placeOn = on;
  if (on) { flyPos.set(pos.x, EYE, pos.z); placeEl.style.display = 'block'; placeStat.style.display = 'block';
    camera.layers.enable(1); }                                              // you're outside your body — your head renders too
  else { placeEl.style.display = 'none'; placeStat.style.display = 'none';
    if (placeSel) placeDrop(); selBox.visible = false;
    camera.layers.disable(1); }
};
const flyStep = dt => {                                                          // camera off the body — full-3D fly
  const sp = (keys['alt'] ? WALK * .35 : keys['shift'] ? SPRINT : WALK) * 1.6;  // Alt crawls the fly cam too — careful framing
  const f = (keys['w'] ? 1 : 0) - (keys['s'] ? 1 : 0), r = (keys['d'] ? 1 : 0) - (keys['a'] ? 1 : 0),
        u = ((keys[' '] || keys['e']) ? 1 : 0) - ((keys['control'] || keys['q']) ? 1 : 0);
  camera.getWorldDirection(_fd);
  const cy = Math.cos(yaw), sy = Math.sin(yaw);
  flyPos.addScaledVector(_fd, f * sp * dt);
  flyPos.x += cy * r * sp * dt; flyPos.z += -sy * r * sp * dt; flyPos.y += u * sp * dt;
  flyPos.y = Math.max(.05, flyPos.y);                                         // unrestricted roam — X re-homes to the body; only the floor stays
};
const placeStep = () => {
  if (!placeSel) return;
  const p = placeSel;
  if (p._drag) {                                                                 // ⇧+LMB held — the ray owns it (anchor + grid snap)
    const s = placeSpot();
    if (s) { const cy = Math.cos(p.rotY), sy = Math.sin(p.rotY), g = p._grab || { lx: 0, lz: 0 };
      if (placeCell) { const h = cellHost(p), sn = h ? cellSnap(h, s.x, s.z) : s;  // cell stage rides the host's lattice
        const seat = h ? cellSeat(h, sn.x, sn.z) : null;
        p.y0 = seat != null ? seat : s.y;
        placeApply(p, sn.x - g.lx * cy - g.lz * sy, sn.z + g.lx * sy - g.lz * cy, p.rotY); }
      else { const sx = Math.round(s.x / PLACE_SNAP) * PLACE_SNAP, sz = Math.round(s.z / PLACE_SNAP) * PLACE_SNAP;
        p.y0 = s.y;
        placeApply(p, sx - g.lx * cy - g.lz * sy, sz + g.lx * sy - g.lz * cy, p.rotY); } } }
  carrySync(p);                                                                  // passengers ride regardless of what moved the host
  const _sig = p.x.toFixed(3) + '|' + p.z.toFixed(3) + '|' + p.y0.toFixed(3) + '|' + p.rotY.toFixed(3) + '|' + (p._carry || []).length + (p._drag ? '|D' : '') + '|' + placeCell;   // textContent churn was a per-frame layout feed — write only on change
  if (_sig !== p._statSig) { p._statSig = _sig;
    placeStat.textContent = 'SEL ' + p.name + '  x ' + p.x.toFixed(3) + '  z ' + p.z.toFixed(3) +
      '  y ' + p.y0.toFixed(3) + '  rotY ' + p.rotY.toFixed(3) + '  · ' + (p._carry || []).length + ' passengers' +
      (p._drag ? '  · DRAGGING' : '') + (placeCell ? '  · CELL ' + ((cellHost(p) || {}).vs ? (cellHost(p).vs * 1000).toFixed(2) + 'mm' : '1mm') : ''); }
};
window.__place = { on: () => placeOn, toggle: placeToggle, sel: () => placeSel && placeSel.name,
  fly: (x, y, z) => { if (x === undefined) return flyPos.toArray(); flyPos.set(x, y, z); return true; },   // pin the free cam — flyPos was module-scoped; free-cam probes couldn't reach it
  look: (y, p) => { if (y === undefined) return [yaw, pitch]; yaw = y; pitch = p; return true; },          // free cam's orientation axis — the loop writes camera.rotation from yaw/pitch in place mode
  grab: n => { const p = PLACED.find(p => p.name === n && !p._parked); return p ? placeGrab(p, null) : false; },
  grabIdx: i => { const p = PLACED[i]; return p ? placeGrab(p, null) : false; },
  drop: placeDrop, revert: placeRevert, spot: placeSpot, pick: () => { const p = placePick(); return p && p.p.name; },
  copy: () => { if (!placeSel) return false; placeClip = { name: placeSel.name, kind: placeSel.kind, rotY: placeSel.rotY, pickable: placeSel.pickable, solids: placeSel.solids, pendant: placeSel.pendWant, vsScale: placeSel.vsScale, outfit: placeSel.outfit };
    placeSay('COPIED ' + placeSel.name); return true; },
  paste: placePaste,
  cell: v => { placeCell = v === undefined ? !placeCell : !!v; return placeCell; },
  cellHost, cellSnap, cellSeat,
  del: () => { if (!placeSel) return false;
    placeSnapOp({ t: 'del', spec: { name: placeSel.name, kind: placeSel.kind, x: placeSel.x, z: placeSel.z,
      rotY: placeSel.rotY, pickable: placeSel.pickable, solids: placeSel.solids, source: placeSel.source,
      pendant: placeSel.pendWant, _spec: placeSel._spec && { ...placeSel._spec, pendant: !!placeSel.pendWant } } });
    if (placeSel.source !== 'extra') { const d = new Set(JSON.parse(localStorage.getItem('dvm_del') || '[]'));
      d.add(placeSel.name); localStorage.setItem('dvm_del', JSON.stringify([...d])); }         // tombstone — manifest items stay deleted
    const dp = placeSel;
    if (dp._block) WALL_BLOCKS.splice(WALL_BLOCKS.indexOf(dp._block), 1);                    // walk-block dies with its table
    if (dp.pend && typeof pendDetach === 'function') pendDetach(dp);                       // the attached light dies with its host — no orphan fixtures or stale LAMPS entries
    scene.remove(placeSel.g); PLACED.splice(PLACED.indexOf(placeSel), 1);
    for (let i = PLACED.length - 1; i >= 0; i--) for (let n = PLACED[i].g; n && n !== scene; n = n.parent)
      if (n === dp.g) { PLACED.splice(i, 1); break; }                            // descendant recs (a mini's parked cars) die with their table — no ghost hosts on a detached group
    settleOrphans(dp, dp.bbox, null, Infinity);                                  // host deleted — its passengers drop to what's beneath
    placeSel = null; selBox.visible = false; origBox.visible = false; placeGrid.visible = false;
    placeStat.textContent = '';
    refreshSolids(); placeSave(); placeSay('REMOVED'); return true; },
  dump: () => JSON.stringify(PLACED.map(p => ({ name: p.name, x: +p.x.toFixed(3), z: +p.z.toFixed(3), y: +p.y0.toFixed(3), rotY: +p.rotY.toFixed(3), src: p.source, surf: p.surf, solids: (p.solids || []).length }))),
  save: placeSave };
