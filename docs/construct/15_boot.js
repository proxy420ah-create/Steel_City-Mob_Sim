'use strict';
/* construct/15_boot.js — warm-up: ready.then tombstones/overrides/lifts/mounts/extras/fixpoint, rAF kick, resize.
   Split from construct.html (was lines 2091-2133); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* warm-up: compile every program + upload every buffer behind the veil — same law as the Commission */
ready.then(async () => {
  emitPad();                                                                  // the pad joins PLACED before overrides/tombstones — full citizen
  JSON.parse(localStorage.getItem('dvm_del') || '[]').forEach(n => {               // tombstones first — a deleted host must not seat anything
    const p = PLACED.find(q => q.name === n && q.source !== 'extra' && !q._parked);  // parked scenery isn't tombstone-able by name
    if (p) { if (p._block) WALL_BLOCKS.splice(WALL_BLOCKS.indexOf(p._block), 1);
      scene.remove(p.g); PLACED.splice(PLACED.indexOf(p), 1); } });
  const over = JSON.parse(localStorage.getItem('dvm_place') || '{}');              // your saved layout overrides manifest positions —
  PLACED.forEach(p => { const o = over[p.name];                                   // but only at the same manifest rev: an authored move
    if (!o || p._parked || p.source === 'extra' || (o.mv || 0) !== (p.mv || 0)) return;  // parked scenery takes no overrides — and (bumped spec.mv) invalidates stale entries
    if (o.mount) { p.mount = o.mount; p.x = o.x; p.z = o.z; p.y0 = o.mount.y; p.rotY = o.rotY;   // wall mounts restore as mounts
      p.g.position.set(o.mount.x, o.mount.y, o.mount.z); padMountQ(p.g, o.mount.n); p.bbox = footB(p.lb, p.x, p.z, p.rotY);
      gripRestore(p, o.tilt); }                                                              // and the grip comes back on pickup
    else { gripRestore(p, o.tilt); if (o.y !== undefined) p.y0 = o.y; placeApply(p, o.x, o.z, o.rotY); } });
  TABLES.forEach(t => { if (t._savedLift) { tableLiftSet(t, t._savedLift); delete t._savedLift; } });   // lifts ride AFTER saved seats restore — riders bump from their own o.y; extras then seat on the lifted tops
  await Promise.all(TABLES.map(t => t._mountP));                                   // mini mounts are async — tops must be live before extras/fixpoint read them, or a roof seat caps down to felt on a slow stasset load
  const extras = JSON.parse(localStorage.getItem('dvm_extra') || '[]');            // extras emit LAST — they seat on the final layout, not manifest spots
  for (const e of extras) { const r = await emitAsset(e.name, { kind: e.kind, x: e.x, z: e.z, rotY: e.rotY, pickable: e.pickable, solids: e.solids,
    source: 'extra', seatY: 'auto' });
    if (r) gripRestore(r, e.tilt); }
  for (let pass = 0; pass < 4; pass++) {                                      // orphan-law re-seat to fixpoint: EVERY item re-derives height on the
    let changed = false;                                                      // final layout — saves carry no y0. Declared roles: surf items floor
    [...PLACED].sort((a, b) => b.y0 - a.y0).forEach(p => {                      // ONLY on other surf items (a rider can never host a desk);
      if (p.mount || p.g.parent !== scene) return;                             // wall mounts keep their seat — nothing re-seats a stuck pad; and
                                                                              // parented scenery (parked cars ride miniG, the showroom car carG)
                                                                              // answers to its host's seat law — rec x/z are parent-local, a
                                                                              // world-coords sweep can't address them (it seats them to the slab floor)
      const s = placeSurf(p.x, p.z, new Set([p]), { surfOnly: p.surf, cap: p.y0 + .01 });   // riders conform — but the cap makes re-seat settle-only,
      if (Math.abs(s - p.y0) > 1e-4) { p.y0 = s; p.g.position.y = s; changed = true; } });  // so co-located siblings can't leapfrog. Highest first.
    if (!changed) break;
  }
  refreshSolids();
  try { if (renderer.compileAsync) await renderer.compileAsync(scene, camera); else renderer.compile(scene, camera); } catch (e) {}
  const _uncull = []; scene.traverse(o => { if (o.frustumCulled) { _uncull.push(o); o.frustumCulled = false; } });
  renderer.render(scene, camera);                                          // the Commission's frustumCulled-off render —
  _uncull.forEach(o => { o.frustumCulled = true; });                       // every buffer/VAO uploads behind the veil, not on first look
  setTimeout(() => { veil.style.opacity = 0; }, 350);
  if (localStorage.getItem('dvm_live') === '1') window.__live.start();              // live link survives a reload — agent keeps eyes
  setInterval(() => { if (!window.__leaving) saveFol(); }, 2500);                 // roster heartbeat — order + last position stay fresh; skip during the goHome veil (it just wrote the post-departure roster)
  window.__warmDone = true;
});
requestAnimationFrame(loop);
addEventListener('resize', () => { camera.aspect = innerWidth / innerHeight; camera.updateProjectionMatrix(); renderer.setSize(innerWidth, innerHeight); });
