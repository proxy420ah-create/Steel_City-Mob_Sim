'use strict';
/* construct/14_loop.js — platform step, render loop, compass HUD.
   Split from construct.html (was lines 2011-2089); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* platform-clamped walk + door solids — same contract as the office slide/pushout */
const step = dt => {
  const sp = keys['alt'] ? WALK * .35 : keys['shift'] ? WALK * SPRINT : WALK;   // Alt crawls for careful edges
  const f = (keys['w'] ? 1 : 0) - (keys['s'] ? 1 : 0), r = (keys['d'] ? 1 : 0) - (keys['a'] ? 1 : 0);
  if (f || r) {
    const sy = Math.sin(yaw), cy = Math.cos(yaw), L = Math.hypot(f, r) || 1;
    const px = pos.x, pz = pos.z;
    pos.x += (-sy * f + cy * r) / L * sp * dt; pos.z += (-cy * f - sy * r) / L * sp * dt;   // forward = (-sin,-cos)yaw, right = (cos,-sin)yaw — three.js yaw-0 faces -z
    if (!walkOK(pos.x, pos.z)) {                                            // slide along walls — try each axis alone
      if (walkOK(pos.x, pz)) pos.z = pz; else if (walkOK(px, pos.z)) pos.x = px; else { pos.x = px; pos.z = pz; }
    }
    for (const s of SOLIDS) { const px = pos.x - s[0], pz = pos.z - s[1], rr = s[2] + .15, d = Math.hypot(px, pz);
      if (d < rr && d > 1e-4) { pos.x = s[0] + px / d * rr; pos.z = s[1] + pz / d * rr; } }
  }
};

let last = performance.now();
const loop = now => {
  const dt = Math.min(.1, (now - last) / 1000); last = now;
  placeOn ? flyStep(dt) : step(dt);
  placeStep();
  if (placeSel) { selBox.box.setFromObject(placeSel.g); selBox.visible = true; }
  if (camLock) { camera.position.copy(camLock.p); camera.lookAt(camLock.t); }
  else { if (placeOn) camera.position.copy(flyPos); else camera.position.set(pos.x, EYE, pos.z);
    camera.rotation.set(0, 0, 0, 'YXZ'); camera.rotation.y = yaw; camera.rotation.x = pitch; }
  if (spinning) carG.rotation.y += SPIN_RATE * dt;
  TABLES.forEach(t => t.g.children.forEach(c => { const mn = c.userData.mini;          // the easel turntable —
    if (mn && mn.spin) { c.rotation.y += mn.spin * dt;                                  // tops re-project every ~1.2° (~8.6° for heavy columns — a zone spins smooth, syncs exact on stop)
      if (Math.abs(c.rotation.y - mn.topTheta) > (mn.localCols.length > 1e5 ? .15 : .02)) { mn.topTheta = c.rotation.y; miniSyncTops(t, c); } } }));
  blockTrf.tick(dt);                                                                  // the block's curbside cruise — pull out, lap, repark
  followers.forEach(f => followStep(f, dt));
  selfStep(dt);
  aimStep();
  if (povF) updatePov(dt);                                    // the agent's eyes track whether or not the PiP inset is up
  boundHue(now / 1000);
  if (held) {
    camera.getWorldDirection(fwdV);
    held.g.position.set(camera.position.x + fwdV.x * heldD - fwdV.z * .1,
                        camera.position.y + fwdV.y * heldD - .07,
                        camera.position.z + fwdV.z * heldD + fwdV.x * .1);
    _qYaw.setFromAxisAngle(AX_Y, yaw);
    _qSnap.setFromEuler(_eSnap.set(heldSnap.x * ROT_STEP, heldSnap.y * ROT_STEP, heldSnap.z * ROT_STEP, 'YXZ'));
    held.g.quaternion.copy(_qYaw).multiply(_qSnap).multiply(heldFree);                  // facing × axis-snaps × free tumble
    const t = dropTarget(); ghost.visible = !!t;
    if (t) { ghost.position.set(t.x, t.y + .004, t.z);
      if (t.wall) ghost.quaternion.setFromUnitVectors(AX_Y, tmpV.set(t.n[0], t.n[1], t.n[2]));   // ring sticks to the face
      else ghost.quaternion.identity(); }
  } else ghost.visible = false;
  pickIt = held || placeOn ? null : nearestPick();
  const kb = kioskProbe(), db = padProbe(), sb = swProbe();                  // unconditional — hover state must not freeze
  promptIt = document.pointerLockElement === el && !placeOn ? (kb || db || sb || INTERACT.reduce((b, it) => {
    const d = Math.hypot(pos.x - it.x, pos.z - it.z); if (!(d < it.r)) return b;
    if (it.aimDot) { const dx = (it.x - pos.x) / d, dz = (it.z - pos.z) / d;            // aimDot entries are cam-view offers —
      if (-Math.sin(yaw) * dx + -Math.cos(yaw) * dz < it.aimDot) return b; }           // must face the point within the cone
    return (!b || d < b.d) ? { it, d } : b; }, null)?.it || null) : null;
  if (kioWire) kioWire.visible = !!(promptIt && promptIt.isBtn && !promptIt.panel && !promptIt.sw);   // ghost only on its own panel's button — not the switch's
  if (swWire) swWire.visible = !!(promptIt && promptIt.sw);                                    // the switch ghost rides the aimed rocker
  promptEl.style.display = (held || pickIt || promptIt) ? 'block' : 'none';
  promptEl.textContent = held ? (held.isPad ? 'E — PLACE DEBUG PAD  ·  RMB tap — cursor  ·  wheel dolly  ·  , . yaw · [ ] pitch · ; \' roll · G tumble · 0 level'
                              : 'E — PLACE ' + held.name.replace('commission_', '').toUpperCase() + '  ·  wheel dolly  ·  , . yaw · [ ] pitch · ; \' roll · G tumble · 0 level')
                     : pickIt ? 'E — TAKE ' + pickIt.name.replace('commission_', '').toUpperCase()
                     : promptIt ? promptIt.label() : '';
  window.__compass(camera);
  if (window.__torch.on) window.__torch.aim(camera);
  renderer.render(scene, camera);
  if (povOn) window.__eyes.pip();
  requestAnimationFrame(loop);
};

/* ── Compass HUD — same rose/readout as the Commission: +z = NORTH (the door end),
   +x = EAST (the desk wall), readout = bearing + world x,z,y. Dark chip behind the
   readout — the void is white. */
const _cp = document.createElement('div');
_cp.style.cssText = 'position:fixed;top:44px;left:50%;transform:translateX(-50%);z-index:5;pointer-events:none;font:11px/1.2 ui-monospace,Consolas,monospace;color:#e8d9b0;text-align:center';
_cp.innerHTML = '<svg width="64" height="64" viewBox="-32 -32 64 64" style="display:block;margin:0 auto"><circle r="30" fill="rgba(10,8,6,.55)" stroke="#8a7a50"/><g id="cpRose"><path d="M0-26L5 0H-5Z" fill="#d94a3a"/><path d="M0 26L5 0H-5Z" fill="#6c6650"/><text y="-17" x="0" font-size="9" fill="#fff" text-anchor="middle" transform="translate(0,-4)">N</text><text x="21" y="3" font-size="9" fill="#e8d9b0" text-anchor="middle">E</text><text x="-21" y="3" font-size="9" fill="#e8d9b0" text-anchor="middle">W</text><text y="30" x="0" font-size="9" fill="#e8d9b0" text-anchor="middle" transform="translate(0,-3)">S</text></g></svg><div id="cpTxt" style="display:inline-block;background:rgba(8,8,14,.8);border:1px solid #3a3a4a;padding:2px 6px"></div>';
document.body.appendChild(_cp);
const _cpRose = _cp.querySelector('#cpRose'), _cpTxt = _cp.querySelector('#cpTxt'), _cpDir = new THREE.Vector3();
window.__compass = cam => {
  cam.getWorldDirection(_cpDir);
  const brg = (Math.atan2(_cpDir.x, _cpDir.z) * 180 / Math.PI + 360) % 360;      // 0 = N(+z), 90 = E(+x)
  _cpRose.setAttribute('transform', 'rotate(' + (-brg).toFixed(1) + ')');
  _cpTxt.textContent = ['N','NE','E','SE','S','SW','W','NW'][Math.round(brg / 45) % 8] + ' ' + Math.round(brg) + '°  x ' + cam.position.x.toFixed(2) + '  z ' + cam.position.z.toFixed(2) + '  y ' + cam.position.y.toFixed(2);
};
