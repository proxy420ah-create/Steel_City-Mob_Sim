'use strict';
/* construct/12_carry.js — held-item grip/drop (takeIt/placeIt/worldSurfaceAt), drop ghost, goHome.
   Split from construct.html (was lines 1725-1792); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── pick-up / drop — free item manipulation on the measured-surface law.
   held rides the camera; the ghost ring marks where E seats it (ray steps forward until it
   crosses a surface; worldSurfaceAt answers for every placed asset, floor = 0). */
const fwdV = new THREE.Vector3(), tmpV = new THREE.Vector3();
/* held-item orientation = facing-yaw × axis-snaps × free-tumble. Axis snaps are 15° counters about
   the item's axes — pressing one CLEARS the free tumble, so level is always one key away (0 resets all).
   Free tumble (hold G + mouse) is a trackball about world X/Y. Placed items persist yaw only and
   always seat level — the measured-surface contract (tops/bbox) is authored-upright. */
const ROT_STEP = Math.PI / 12;
const heldSnap = { x: 0, y: 0, z: 0 };
const heldFree = new THREE.Quaternion(), _qYaw = new THREE.Quaternion(), _qSnap = new THREE.Quaternion(),
      _eSnap = new THREE.Euler(), _qt = new THREE.Quaternion();
const AX_X = new THREE.Vector3(1, 0, 0), AX_Y = new THREE.Vector3(0, 1, 0);
const heldTumble = (dx, dy) => { heldFree                                          // G+mouse trackball — world-frame grab
  .premultiply(_qt.setFromAxisAngle(AX_Y, -dx * .008))
  .premultiply(_qt.setFromAxisAngle(AX_X, -dy * .008)); };
const heldSnapStep = (ax, d) => { heldSnap[ax] += d; heldFree.identity(); };       // axis rotation resets free rotation — level stays reachable
const tiltOf = p => p._snap && p._free && (p._snap.x || p._snap.y || p._snap.z || p._free.x || p._free.y || p._free.z)
  ? { s: [p._snap.x, p._snap.y, p._snap.z], f: p._free.toArray().map(v => +v.toFixed(5)) } : null;
const gripRestore = (p, t) => { if (!t) return;                                    // the saved grip returns to HAND on takeIt — placed poses stay level/mounted
  p._snap = { x: t.s[0], y: t.s[1], z: t.s[2] }; p._free = new THREE.Quaternion().fromArray(t.f); };
const worldSurfaceAt = (wx, wz, skip, opt) => placeSurf(wx, wz, new Set(skip ? [skip] : []), opt);
const dropTarget = () => {
  camera.getWorldDirection(fwdV);
  if (held && held.isPad) { const w = padWallTarget(); if (w) return w; }   // the pad sticks to wall faces
  for (let t = .08; t <= 2.4; t += .06) {
    const px = camera.position.x + fwdV.x * t, py = camera.position.y + fwdV.y * t, pz = camera.position.z + fwdV.z * t;
    const s = worldSurfaceAt(px, pz, held);
    if (py <= s + .02) return { x: px, y: s, z: pz };
  }
  return null;
};
const nearestPick = () => {
  camera.getWorldDirection(fwdV); let best = null, ba = .55;
  PLACED.forEach(p => { if (!p.pickable || p === held) return;
    tmpV.set(p.x - camera.position.x, p.y0 + p.minB + .04 - camera.position.y, p.z - camera.position.z);
    const d = tmpV.length(); if (d > 1.5 || d < .01) return; tmpV.normalize();
    const a = tmpV.dot(fwdV); if (a > ba) { ba = a; best = p; } });
  return best;
};
const takeIt = p => { held = p;                                            // the grip remembers — the pose you last held returns
  if (p._snap) { heldSnap.x = p._snap.x; heldSnap.y = p._snap.y; heldSnap.z = p._snap.z; heldFree.copy(p._free); }
  else { heldSnap.x = heldSnap.y = heldSnap.z = 0; heldFree.identity(); }
  p.mount = null; };
const placeIt = t => { const p = held; held = null;
  p._snap = { ...heldSnap }; p._free = heldFree.clone();                   // stash the grip on the rec — yaw already rides p.rotY
  placeSnapOp({ t: 'move', p, prev: { x: p.x, z: p.z, rotY: p.rotY, y0: p.y0, mount: p.mount } });
  p.x = t.x; p.z = t.z; p.y0 = t.y;
  if (t.wall) { p.mount = { x: t.x, y: t.y, z: t.z, n: t.n };               // stuck to a face — screen out, upright
    p.g.position.set(t.x, t.y, t.z); padMountQ(p.g, t.n); }
  else { p.mount = null;
    p.rotY = ((yaw + heldSnap.y * ROT_STEP) % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);   // yaw persists — pitch/roll drop, placement seats level
    p.g.position.set(t.x, t.y, t.z); p.g.rotation.set(0, p.rotY, 0); }
  p.bbox = footB(p.lb, p.x, p.z, p.rotY);
  refreshSolids(); placeSave(); };
const ghostShape = new THREE.Shape(); ghostShape.absarc(0, 0, .05, 0, Math.PI * 2);
const ghole = new THREE.Path(); ghole.absarc(0, 0, .038, 0, Math.PI * 2); ghostShape.holes.push(ghole);
const ghostGeo = new THREE.ShapeGeometry(ghostShape); ghostGeo.rotateX(Math.PI / 2);
const ghost = new THREE.Mesh(ghostGeo, coreMat); ghost.visible = false; scene.add(ghost);

const veil = document.getElementById('veil'), promptEl = document.getElementById('prompt');
const veilRun = (text, fn) => { veil.textContent = text; veil.style.opacity = 1;
  setTimeout(() => { fn && fn(); }, 420); };
const goHome = () => { if (window.__leaving) return; window.__leaving = true;
  sessionStorage.setItem('dvm_party', JSON.stringify(followers.filter(f => f.order === 'follow').map(f => ({ char: f.spec.char, order: 'follow' }))));
  saveFol(followers.filter(f => f.order !== 'follow'));                                // departing units leave the roster — they walked out with you
  veilRun('RETURNING', () => { location.href = 'thecommission.html?from=construct'; }); };
window.__home = goHome;
