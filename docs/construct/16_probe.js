'use strict';
/* construct/16_probe.js — probe surface — camLock, __cam/__snap, __construct agent API, __envShot/__uiClean.
   Split from construct.html (was lines 2135-2176); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* probe hooks — same contract as thecommission.html so _shot.mjs drives this page too */
let camLock = null;
window.__cam = (px, py, pz, tx, ty, tz) => { if (px === null) { camLock = null; return; }   // __cam(null) releases
  camLock = { p: new THREE.Vector3(px, py, pz), t: new THREE.Vector3(tx, ty, tz) }; };
window.__snap = (name, px, py, pz, tx, ty, tz) => {
  if (px !== undefined) { window.__cam(px, py, pz, tx, ty, tz);
    camera.position.copy(camLock.p); camera.lookAt(camLock.t); }        // apply now — the rAF loop may not tick before the capture
  const T = window.__torch, tw = T && T.on;                               // --torch shots light the set like the L key
  if (window.__snapTorch && T) { T.set(true); T.aim(camera); }
  renderer.render(scene, camera);
  if (T && T.on !== tw) T.set(tw);
  const u = renderer.domElement.toDataURL('image/png');
  return fetch('/shot', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: 'probe_' + name, png: u }) }).then(r => r.json()).then(j => !!j.ok);
};
window.__construct = { manifest: MANIFEST, pos, spin: () => spinning, held: () => held, placed: PLACED, yaw: () => yaw,
  walkOK, bounds: () => boundsG.children.length, surfaceAt,
  tables: () => TABLES.filter(t => PLACED.includes(t.rec)).map(t => ({ id: t.spec.id, x: t.rec.x, z: t.rec.z, top: tableTop(t), size: t.spec.size,
    specTop: t.spec.top, lift: t.lift, slabY: +t.parts.slab.position.y.toFixed(4), legY: +t.parts.legs[0].position.y.toFixed(4),
    scale: t.spec.scale, catalog: (t.spec.catalog || []).map(m => m.name),
    models: t.g.children.filter(c => c.userData.mini).map(c => { const m = c.userData.mini; return { name: m.name, static: !!m.static, dims: m.dims, vox: m.vox, scale: m.scale, fill: m.fill,
      cols: m.localCols.length, plate: !!m.plate, prox: c.children.filter(ch => ch.userData.hitProxy).length,
      rotY: +c.rotation.y.toFixed(4), spin: m.spin, axis: m.axis,
      slice: { x: m.slice.x.slice(), y: m.slice.y.slice(), z: m.slice.z.slice() } }; }) })),
  followers: () => followers.map(f => ({ char: f.spec.char, order: f.order, pos: f.pos.toArray() })),
  self: () => selfBody && ({ x: pos.x, z: pos.z, yaw: selfBody.yaw, state: selfBody.state,
    bodyMask: selfBody.meshes[0].im.layers.mask, headMask: selfBody.meshes[1].im.layers.mask,
    bodyVox: selfBody.meshes[0].list.length, headVox: selfBody.meshes[1].list.length }),
  agent: {                                                                   // the agent's possession layer — same tags, same orders
    gaze: (x, y, z) => { followers.forEach(f => f.gazeT = new THREE.Vector3(x, y, z)); return [x, y, z]; },   // head-aim only — never moves the mark or his walk target
    go: (x, z) => { tagAt(new THREE.Vector3(x, 0, z)); followers.forEach(f => f.setOrder('goto')); },
    order: o => followers.forEach(f => f.setOrder(o)),
    heel: () => followers.forEach(f => f.setOrder('follow')),
    shot: () => window.__eyes.shot(),
    pos: () => followers.map(f => f.pos.toArray()),
    zoom: t => window.__eyes.zoom(t),                                            // POV scope — level 0..1, eased
    torch: on => __eyes.torch(on),                                               // the POV's own lamp — the boss's L-rig stays his
  },
  place: () => window.__place,
  take: n => { const p = PLACED.find(p => p.name === n && !p._parked); if (p) takeIt(p); return !!p; },
  place: (x, z) => { if (!held) return false; const y = worldSurfaceAt(x, z, held); placeIt({ x, y, z }); return true; },
  tumble: (dx, dy) => heldTumble(dx, dy), heldSnap: () => ({ ...heldSnap }),
  heldQ: () => held ? held.g.quaternion.toArray() : null, dropTarget };
window.__envShot = () => {};                                   // probe contract — no sealed/env split on this page
window.__uiClean = v => { document.getElementById('hud').style.display = v ? 'none' : ''; };
