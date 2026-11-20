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

let last = performance.now(), _dropT = 0, _dropCache = null, _zf = -1, _ptLast = null,
    _selBoxSig = null;     // dropTarget marched 39 ray-steps at 60Hz —
/* ── perf probe — `?perf` in the URL or `__perf(1)` samples every frame; `__perf()` dumps a
   console.table of per-stage avg/max/outlier counts. 'frame' = start-to-start interval;
   'idle' = browser/compositor time between the last loop marker and the next frame start.
   ~10 performance.now() calls/frame when armed, one early-return per marker when off. */
let _pfOn = /[?&]perf/.test(location.search), _pfT = performance.now(), _pfF0 = _pfT;
const _pfStats = {};
const _pfRec = (n, d) => { const s = _pfStats[n] || (_pfStats[n] = { n: 0, tot: 0, max: 0, o8: 0, o33: 0 });
  s.n++; s.tot += d; if (d > s.max) s.max = d; if (d > 8) s.o8++; if (d > 33) s.o33++; };
const _pfMark = n => { if (!_pfOn) return; const t = performance.now(); _pfRec(n, t - _pfT); _pfT = t; };
window.__perfStats = _pfStats;
window.__perf = on => { if (on) { for (const k in _pfStats) delete _pfStats[k];
    _pfOn = true; _pfF0 = _pfT = performance.now(); return; }
  _pfOn = false;
  console.table(Object.keys(_pfStats).map(k => { const s = _pfStats[k];
    return { stage: k, calls: s.n, avg: +(s.tot / s.n).toFixed(2), max: +s.max.toFixed(1), '>8ms': s.o8, '>33ms': s.o33 }; })); };

const loop = now => {                                                       // the ghost only needs ~15Hz of freshness
  const dt = Math.min(.1, (now - last) / 1000); last = now;
  if (_pfOn) { const t = performance.now(); _pfRec('idle', t - _pfT); _pfRec('frame', t - _pfF0); _pfF0 = _pfT = t; }
  if (window.__lookStep) window.__lookStep();                             // mouse deltas land once a frame — poll-rate packets fold into one apply
  _pfMark('input');                                                      // __lookStep + any queued handler time
  placeOn ? flyStep(dt) : step(dt);
  placeStep();
  _pfMark('move');
  if (placeSel) { const _sig = placeSel.x + ',' + placeSel.z + ',' + placeSel.y0 + ',' + placeSel.rotY;   // selBox only re-bounds when the selection moved — setFromObject traverses every instance each frame
    if (_sig !== _selBoxSig) { _selBoxSig = _sig; selBox.box.setFromObject(placeSel.g); } selBox.visible = true; }
  else { _selBoxSig = null; selBox.visible = false; }
  const zt = (keys[zoomKey] && (zoomKey === 'control' || zoomKey === 'meta' || !keys['control'] && !keys['meta'])) ? 1 : 0;   // hold-scope — bare key only; a ctrl/meta BIND exempts itself from the modifier gate
  if (zoomT !== zt || zoomFov !== _zf) { _zf = zoomFov; zoomT += (zt - zoomT) * Math.min(1, dt * 9);   // Ctrl+Z is place-undo, never a zoom; _zf — a slider move re-pulls the lens even mid-scope
    if (Math.abs(zt - zoomT) < 1e-3) zoomT = zt;
    camera.fov = 58 - (58 - zoomFov) * zoomT; camera.updateProjectionMatrix(); }
  if (camLock) { camera.position.copy(camLock.p); camera.lookAt(camLock.t); }
  else { if (placeOn) camera.position.copy(flyPos); else camera.position.set(pos.x, EYE, pos.z);
    camera.rotation.set(0, 0, 0, 'YXZ'); camera.rotation.y = yaw; camera.rotation.x = pitch; }
  camera.updateMatrixWorld();                                            // else every setFromCamera raycast (aim/probes/ghost/hover) reads LAST frame's matrix — a one-frame swim while turning
  _pfMark('cam');                          // selBox + zoom + camera write
  if (spinning) carG.rotation.y += SPIN_RATE * dt;
  TABLES.forEach(t => t.g.children.forEach(c => { const mn = c.userData.mini;          // the easel turntable —
    if (mn && mn.spin) { c.rotation.y += mn.spin * dt;                                  // tops re-project every ~2.9° (~8.6° for heavy columns — a zone spins smooth, syncs exact on stop). The light threshold was .02 rad (~1.1°) = ~6 syncs/sec at 0.9 rad/s; .05 keeps the visual smoothness at arm's length while cutting the ~700k-op sync by 2.5×.
      if (Math.abs(c.rotation.y - mn.topTheta) > (mn.localCols.length > 1e5 ? .15 : .05)) { mn.topTheta = c.rotation.y;
        if (mn.localCols.length > 1e5) { if (!mn._topsQ) { mn._topsQ = true; miniSyncTopsQ(t, c).finally(() => mn._topsQ = false); } }   // heavy composites sync chunked — a synchronous ~100ms rewrite mid-spin hitches mouse-look; exact tops still land on stop
        else miniSyncTops(t, c); } } }));
  _pfMark('spin');             // turntable + tops sync
  blockTrf.tick(dt);                                                                  // the block's curbside cruise — pull out, lap, repark
  followers.forEach(f => followStep(f, dt));
  selfStep(dt);
  _pfMark('npc');                                       // blockTrf + followers + selfStep
  aimStep();
  if (povF) updatePov(dt);                                    // the agent's eyes track whether or not the PiP inset is up
  boundHue(now / 1000);
  _pfMark('aim');                               // aimStep + updatePov + boundHue
  if (held) {
    camera.getWorldDirection(fwdV);
    held.g.position.set(camera.position.x + fwdV.x * heldD - fwdV.z * .1,
                        camera.position.y + fwdV.y * heldD - .07,
                        camera.position.z + fwdV.z * heldD + fwdV.x * .1);
    _qYaw.setFromAxisAngle(AX_Y, yaw);
    _qSnap.setFromEuler(_eSnap.set(heldSnap.x * ROT_STEP, heldSnap.y * ROT_STEP, heldSnap.z * ROT_STEP, 'YXZ'));
    held.g.quaternion.copy(_qYaw).multiply(_qSnap).multiply(heldFree);                  // facing × axis-snaps × free tumble
    if (now - _dropT > 66) { _dropT = now; _dropCache = dropTarget(); window.__dropN = (window.__dropN || 0) + 1; }
    const t = _dropCache; ghost.visible = !!t;
    if (t) { ghost.position.set(t.x, t.y + .004, t.z);
      if (t.wall) ghost.quaternion.setFromUnitVectors(AX_Y, tmpV.set(t.n[0], t.n[1], t.n[2]));   // ring sticks to the face
      else ghost.quaternion.identity(); }
  } else ghost.visible = false;
  pickIt = held || placeOn ? null : nearestPick();
  if (_padHoverDirty) { _padHoverDirty = false; padSetHover(padCursorHit()); }
  _pfMark('pick');                       // held + nearestPick + hover
  const kb = kioskProbe(), db = padProbe(), sb = swProbe();                  // unconditional — hover state must not freeze
  promptIt = null;
  if (document.pointerLockElement === el && !placeOn) {
    promptIt = kb || db || sb; let _id = Infinity;                                     // plain loop — a reduce closure per frame was GC feed
    if (!promptIt) for (const it of INTERACT) {
      const d = Math.hypot(pos.x - it.x, pos.z - it.z); if (!(d < it.r) || d >= _id) continue;
      if (it.aimDot) { const dx = (it.x - pos.x) / d, dz = (it.z - pos.z) / d;           // aimDot entries are cam-view offers —
        if (-Math.sin(yaw) * dx + -Math.cos(yaw) * dz < it.aimDot) continue; }          // must face the point within the cone
      promptIt = it; _id = d;
    }
  }
  if (kioWire) kioWire.visible = !!(promptIt && promptIt.isBtn && !promptIt.panel && !promptIt.sw);   // ghost only on its own panel's button — not the switch's
  if (swWire) swWire.visible = !!(promptIt && promptIt.sw);                                    // the switch ghost rides the aimed rocker
  const _pt = held ? 'E — PLACE ' + held.name.replace('commission_', '').replace(/_/g, ' ').toUpperCase()   // the carry hotkeys live on the FOLLOWER panel (K-hidable), not the reticle
            : pickIt ? 'E — TAKE ' + pickIt.name.replace('commission_', '').toUpperCase()
            : promptIt ? promptIt.label() : '';
  if (_pt !== _ptLast) { _ptLast = _pt; promptEl.textContent = _pt; promptEl.style.display = _pt ? 'block' : 'none'; }   // DOM writes only on change — textContent churn was GC+layout feed
  window.__compass(camera);
  if (window.__torch.on) window.__torch.aim(camera);
  _pfMark('hud');   // probes + prompt DOM + compass + torch
  renderer.render(scene, camera);
  _pfMark('render');
  if (povOn) window.__eyes.pip();   // every frame — the 20Hz-gated draw strobed POV-vs-main on a non-preserved buffer
  _pfMark('pip');                      // unconditional — next frame's 'idle' measures from here
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
let _cpLast = null;       // DOM writes only when the rounded bearing/position changes — toFixed+textContent churn was a per-frame layout feed
window.__compass = cam => {
  cam.getWorldDirection(_cpDir);
  const brg = (Math.atan2(_cpDir.x, _cpDir.z) * 180 / Math.PI + 360) % 360;      // 0 = N(+z), 90 = E(+x)
  const txt = ['N','NE','E','SE','S','SW','W','NW'][Math.round(brg / 45) % 8] + ' ' + Math.round(brg) + '°  x ' + cam.position.x.toFixed(2) + '  z ' + cam.position.z.toFixed(2) + '  y ' + cam.position.y.toFixed(2);
  if (txt !== _cpLast) { _cpLast = txt;
    _cpRose.setAttribute('transform', 'rotate(' + (-brg).toFixed(1) + ')');
    _cpTxt.textContent = txt; }
};
