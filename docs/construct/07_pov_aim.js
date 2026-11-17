'use strict';
/* construct/07_pov_aim.js — follower POV eyes + PiP (__eyes), laser/tag/aim machinery (rayPruned, tagAt, aimStep).
   Split from construct.html (was lines 1212-1320); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── follower's eyes — region-10 posed centroid → POV cam; P toggles the inset, __eyes.shot captures.
   Gaze target is always the player's head — his POV watches you, like his head does. */
const povCam = new THREE.PerspectiveCamera(72, 16 / 9, .005, 20); povCam.rotation.order = 'YXZ';
let povOn = false, povYaw = Math.PI, povPitch = 0, povF = null;
const povEl = document.createElement('div');
povEl.style.cssText = 'position:fixed;right:12px;bottom:12px;border:1px solid rgba(255,255,255,.35);box-shadow:0 0 0 1px rgba(0,0,0,.25);color:#b0a88f;font:10px ui-monospace,Consolas,monospace;letter-spacing:.2em;display:none;z-index:6;pointer-events:none;padding:4px 6px';
povEl.textContent = 'FOLLOWER POV'; document.body.appendChild(povEl);
const sizePovEl = () => { const w = innerWidth, iw = Math.floor(w * .30), ih = Math.floor(iw * 9 / 16); povEl.style.width = iw + 'px'; povEl.style.height = ih + 'px'; povEl.style.padding = '0'; povEl.style.lineHeight = '1'; povEl.style.boxSizing = 'border-box'; povEl.style.textAlign = 'left'; povEl.style.verticalAlign = 'bottom'; povEl.innerHTML = '<span style="position:absolute;left:0;bottom:0;padding:3px 6px;background:rgba(0,0,0,.35)">FOLLOWER POV</span>'; };
sizePovEl(); addEventListener('resize', sizePovEl);
const _eyeL = [0, 0, 0], _eyeW = new THREE.Vector3(), _pfwd = new THREE.Vector3();
function eyeWorld(f) {
  let x = 0, y = 0, z = 0;
  for (const i of f.eyeIdx) { x += f.bufB[3 * i]; y += f.bufB[3 * i + 1]; z += f.bufB[3 * i + 2]; }
  if (f.transT >= 0) { let ax = 0, ay = 0, az = 0;
    for (const i of f.eyeIdx) { ax += f.bufA[3 * i]; ay += f.bufA[3 * i + 1]; az += f.bufA[3 * i + 2]; }
    x = ax + (x - ax) * f.blend; y = ay + (y - ay) * f.blend; z = az + (z - az) * f.blend; }
  const n = f.eyeIdx.length || 1, hx = f.d[0] / 2, hz = f.d[2] / 2;
  _eyeL[0] = (x / n - hx) * f.VS; _eyeL[1] = (y / n + .5) * f.VS; _eyeL[2] = (z / n - hz) * f.VS;
  const cy = Math.cos(f.yaw), sy = Math.sin(f.yaw);
  _eyeW.set(f.pos.x + _eyeL[0] * cy + _eyeL[2] * sy, _eyeL[1], f.pos.z - _eyeL[0] * sy + _eyeL[2] * cy);
  return _eyeW;
}
function updatePov(dt) {
  if (!povF) return;
  const e = eyeWorld(povF);
  const g = povF.gazeT || _playerHead;                            // tagged → his POV rides the mark; else he watches you
  const gx = g.x - e.x, gy = g.y - e.y, gz = g.z - e.z, hz = Math.hypot(gx, gz) || 1e-4;
  const wy = Math.atan2(-gx, -gz), wp = Math.atan2(gy, hz);
  let dyw = wy - povYaw; while (dyw > Math.PI) dyw -= 2 * Math.PI; while (dyw < -Math.PI) dyw += 2 * Math.PI;
  const k = Math.min(1, 10 * dt);
  povYaw += dyw * k; povPitch += (wp - povPitch) * k;
  _pfwd.set(-Math.sin(povYaw) * Math.cos(povPitch), Math.sin(povPitch), -Math.cos(povYaw) * Math.cos(povPitch));
  povCam.position.copy(e).addScaledVector(_pfwd, .075);         // push out of the skull
  povCam.rotation.set(povPitch, povYaw, 0);
}
window.__eyes = {
  on: () => povOn,
  show: () => { if (povF) { povOn = true; povEl.style.display = 'block'; } },
  hide: () => { povOn = false; povEl.style.display = 'none'; },
  pip: () => {                                                   // scissored second pass — same trick as the Commission's __eyes.pip
    if (!povOn || !povF) return;
    const cv = renderer.domElement, w = cv.width / (renderer.getPixelRatio() || 1), h = cv.height / (renderer.getPixelRatio() || 1);
    const iw = Math.floor(w * .30), ih = Math.floor(iw * 9 / 16), ix = Math.floor(w - iw - 12), iy = 12;
    povCam.aspect = iw / ih; povCam.updateProjectionMatrix();
    renderer.setScissorTest(true); renderer.setScissor(ix, iy, iw, ih); renderer.setViewport(ix, iy, iw, ih);
    renderer.clearDepth();
    const ac = renderer.autoClear; renderer.autoClear = false;
    if (window.__torch && window.__torch.on) window.__torch.aim(povCam);
    renderer.render(scene, povCam);
    renderer.autoClear = ac; renderer.setScissorTest(false); renderer.setViewport(0, 0, w, h); renderer.setScissor(0, 0, w, h);
  },
  shot: () => {                                                  // full-res POV frame → PNG data URL (same task render+read)
    if (!povF) return null;
    const cv = renderer.domElement, pr = renderer.getPixelRatio() || 1;
    povCam.aspect = cv.width / cv.height; povCam.updateProjectionMatrix();
    renderer.setViewport(0, 0, cv.width / pr, cv.height / pr);
    if (window.__torch && window.__torch.on) window.__torch.aim(povCam);
    renderer.render(scene, povCam);
    return cv.toDataURL('image/png');
  },
  snap: name => { const u = window.__eyes.shot(); if (!u) return Promise.resolve(false);
    return fetch('/shot', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: 'probe_' + (name || 'follower'), png: u }) }).then(r => r.json()).then(j => !!j.ok); },
};

/* ── laser + tag — F-hold or RMB-hold paints the live dot (first-person has no body to point with,
   so the dot IS the laser); LMB while aiming plants a persistent mark. The tag is shared truth:
   followers gaze at it (head IK), T sends them to it (goto), the agent API writes the same
   pipeline — so a tag is identical whether it came from your laser or my prompt. */
const _playerHead = new THREE.Vector3();
const aimRay = new THREE.Raycaster(), _zv = new THREE.Vector2(0, 0), aimWorld = new THREE.Vector3();
/* raycast pruner — instanced subtrees are skipped whole (a 426K-instance mini would matrix-test
   every voxel per ray — the aim+walk slideshow). allInst skips EVERY InstancedMesh (the per-frame
   aim path — tags want surfaces); default skips only miniVox (click paths keep props selectable).
   Exclusions honored at EVERY level, not just the root list. */
const _rcList = [];
const rcCollect = (o, excl, allInst) => { if (!o.visible || o.isLine || o.isPoints || o.userData.noAim || (allInst ? o.isInstancedMesh : o.userData.miniVox) || (o.userData.hitProxy && !allInst) || (excl && excl.includes(o))) return;
  _rcList.push(o); o.children.forEach(c => rcCollect(c, excl, allInst)); };
const rayPruned = (rc, roots, excl, allInst) => { _rcList.length = 0;
  roots.forEach(o => { if (!excl || !excl.includes(o)) rcCollect(o, excl, allInst); });
  return rc.intersectObjects(_rcList, false); };
let aimOn = false, rmbAim = false, rmbT0 = 0;
const markGeo = new THREE.BoxGeometry(.05, .05, .05), markMat = new THREE.MeshBasicMaterial({ color: 0xff3344 }), marks = [];
const aimDot = new THREE.Mesh(new THREE.BoxGeometry(.03, .03, .03), markMat); aimDot.visible = false; scene.add(aimDot);
const xhair = document.createElement('div');
xhair.style.cssText = 'position:fixed;left:50%;top:50%;width:5px;height:5px;margin:-2.5px 0 0 -2.5px;border-radius:50%;background:#ff3344;display:none;z-index:5;pointer-events:none';
document.body.appendChild(xhair);
const tagAt = p => {
  while (marks.length) scene.remove(marks.pop());                            // one live mark — a new tag replaces the old
  const m = new THREE.Mesh(markGeo, markMat); m.position.copy(p); scene.add(m); marks.push(m);
  followers.forEach(f => { f.gazeT = (f.gazeT || new THREE.Vector3()).copy(p); });   // the tag is a gaze order — his eyes center on it
};
const clearTags = () => { while (marks.length) scene.remove(marks.pop()); followers.forEach(f => { f.gazeT = null; }); };
window.__aim = { on: () => aimOn, mark: tagAt, clear: clearTags, world: () => aimWorld.toArray() };

let _aimExcl = null;                                                 // lazy — ghost/aimDot live in later-loaded siblings (12_carry.js); a module-level literal would hit TDZ at eval time
const aimStep = () => {
  aimOn = !!(keys.f || rmbAim);
  _playerHead.set(pos.x, EYE, pos.z);
  if (aimOn) {
    aimRay.setFromCamera(_zv, camera);
    if (!_aimExcl) _aimExcl = [ghost, aimDot, camera];                // first call materializes the fixed head; later calls reuse
    _aimExcl.length = 3;                                              // truncate to the 3 fixed entries, append the live ones
    followers.forEach(f => _aimExcl.push(f.CH)); if (selfBody) _aimExcl.push(selfBody.CH); if (held) _aimExcl.push(held.g); marks.forEach(m => _aimExcl.push(m));
    const hits = rayPruned(aimRay, scene.children, _aimExcl, true);
    camera.getWorldDirection(fwdV);
    if (hits.length) aimWorld.copy(hits[0].point); else aimWorld.copy(camera.position).addScaledVector(fwdV, 4);
    aimDot.position.copy(aimWorld); aimDot.visible = true;
  } else aimDot.visible = false;
  xhair.style.display = aimOn ? 'block' : 'none';
};
