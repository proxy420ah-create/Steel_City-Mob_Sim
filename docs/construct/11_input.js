'use strict';
/* construct/11_input.js — FPS walk, INTERACT registry, key/mouse handlers.
   Split from construct.html (was lines 1628-1723); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── FPS walk — pointer lock look + WASD on the platform. Eye height .55 (chibi-scaled world:
   the .855m door towers correctly; showroom inspects at authored scale, no third person needed). */
const pos = new THREE.Vector3(0, 0, 0); let yaw = 0, pitch = 0;          // yaw 0 faces -z — stepped out of the door-elevator, rotator dead ahead
const EYE = .56, WALK = 1.4, SPRINT = 2.2, keys = {};    // Vinny's authored Eyes region (region id 10) — voxel centroid y56 × .01 pitch
let zoomT = 0, zoomKey = (localStorage.getItem('dvm_key_zoom') || 'z').toLowerCase(),
    zoomFov = +(localStorage.getItem('dvm_zoom_fov') || 26);               // hold-scope — the loop eases 0↔1, FOV pulls 58→zoomFov; key + power tune in OPTIONS (O)
const el = renderer.domElement;
el.addEventListener('click', () => { if (!padFocus && document.pointerLockElement !== el) grabLook(); });
let _mdx = 0, _mdy = 0;                                                   // accumulated deltas — poll-rate events fold into one per-frame apply
addEventListener('mousemove', e => {
  if (padFocus) { mouseNDC.set(e.clientX / innerWidth * 2 - 1, -(e.clientY / innerHeight) * 2 + 1);
    _padHoverDirty = true; return; }                                      // pad mode — hover raycast runs once a frame, not once a packet
  if (document.pointerLockElement !== el) return;
  if (Math.abs(e.movementX) > 500 || Math.abs(e.movementY) > 500) return;   // pointer-lock spike guard — Chrome sporadically emits bogus multi-thousand-px deltas on fast flicks; legit per-event movement stays <~150px
  if (held && keys['g']) { heldTumble(e.movementX, e.movementY); return; }  // G+mouse tumbles the item, not the camera
  _mdx += e.movementX; _mdy += e.movementY; });                           // the math lands in the loop — poll-rate events just sum
window.__lookStep = () => { if (!_mdx && !_mdy) return;                    // consumed once per frame by the loop
  const sens = .0022 * (camera.fov / 58);                                  // sensitivity tracks the lens — scoped aim stays precise
  yaw -= _mdx * sens; pitch = Math.max(-1.45, Math.min(1.45, pitch - _mdy * sens)); _mdx = _mdy = 0; };
/* INTERACT registry — same contract as the Commission: promptIt caches what the HUD shows,
   E falls back to it (shown = pressable). New exhibits append, don't redesign. */
const INTERACT = [
  { x: 0, z: DOOR_Z - .25, r: 1.1, aimDot: .7, label: () => 'E — EXIT THE CONSTRUCT', fn: () => goHome() },   // aimDot — the prompt is a cam-view offer, not a proximity shout: it only appears while the camera actually faces the door
  { x: CONS_POS.x, z: CONS_POS.z - .08, r: .55, label: () => 'E — DOOR CONSOLE', fn: () => openCons() },
];                                       // the rotator's controls live on the pad's DECK page — spin, lift, eye-preset
let promptIt = null;
window.__pos = pos; window.__prompt = () => promptIt ? promptIt.label() : '';   // probe hooks — pos/promptIt read live
addEventListener('mousedown', e => {
  if (padFocus) {
    if (e.button === 2) rmbT0 = performance.now();                       // stamp the tap window HERE — the shared stamp below is skipped in cursor mode, so RMB-tap could never release focus
    if (e.button === 0) {
      const h = padScreenHit();
      if (!h) { padFocusToggle(); return; }                              // LMB off the screen — click-off releases to mouse look (a real gesture, so grabLook re-locks instantly — no Esc cooldown)
      const r = padUvRegion(h.uv); if (r) padPress(r, h.uv); }
    return; }
  if (placeOn) { if (e.button === 0) {
    const pk = placePick();
    if (pk) { if (placeSel !== pk.p) placeGrab(pk.p, pk);              // fresh pick — commits whatever was held
      else { const cy = Math.cos(pk.p.rotY), sy = Math.sin(pk.p.rotY); // re-grab the held item → move the anchor to THIS touch
        pk.p._grab = { lx: (pk.x - pk.p.x) * cy - (pk.z - pk.p.z) * sy, lz: (pk.x - pk.p.x) * sy + (pk.z - pk.p.z) * cy }; }
      if (e.shiftKey) placeSel._drag = true; }                       // ⇧+LMB held = the ray owns it until mouseup
    else if (placeSel) placeDrop();                                  // click empty space = accept + deselect
    return; } }
  if (e.button === 0 && held && held.isPad && !aimOn) { padFocusToggle(); return; }   // carrying the pad — LMB hands the cursor back to it (aiming still tags)
  if (e.button === 2) { rmbAim = true; rmbT0 = performance.now(); }
  if (e.button === 0) { if (promptIt && promptIt.isBtn) promptIt.fn(); else if (aimOn) tagAt(aimWorld); }   // face buttons press by click
});
addEventListener('mouseup',   e => {
  if (e.button === 2) { rmbAim = false;
    if (((held && held.isPad) || padFocus) && performance.now() - rmbT0 < 280) padFocusToggle(); }   // RMB tap ↔ pad cursor
  if (e.button === 0 && placeSel) placeSel._drag = false; });
addEventListener('contextmenu', e => { if (e.target === renderer.domElement || rmbAim) e.preventDefault(); });
addEventListener('wheel', e => { if (!held) return;                              // the carry dolly — scroll down pulls it toward you, up pushes it away
  heldD = Math.max(.18, Math.min(1.6, heldD - Math.sign(e.deltaY) * .06));
  localStorage.setItem('dvm_holdd', heldD.toFixed(3)); e.preventDefault(); }, { passive: false });
addEventListener('keydown', e => { keys[(e.key || '').toLowerCase()] = true;
  const k = (e.key || '').toLowerCase();
  if (k === 'alt') e.preventDefault();                                    // keep Chrome's menu from stealing focus — Alt is our crawl modifier
  if (k === 'escape' && padFocus) { padFocusToggle(); return; }               // Esc gives the look back too
  if (k === 'x' && !e.ctrlKey && !e.metaKey && !e.altKey) { if (padFocus) padFocusToggle(); placeToggle(!placeOn); return; }
  if (placeOn) {
    if ((e.ctrlKey || e.metaKey) && k === 'z') { placeUndoDo(); e.preventDefault(); return; }
    if (k === 'escape') { if (placeSel) placeRevert(); else placeToggle(false); return; }   // selection → back out; nothing → leave
    const p = placeSel, st = e.shiftKey ? .05 : .005;                                      // the Commission's fine/coarse contract
    if (k === 'arrowleft' && p) { placeApply(p, p.x - st, p.z, p.rotY); }
    else if (k === 'arrowright' && p) { placeApply(p, p.x + st, p.z, p.rotY); }
    else if (k === 'arrowup' && p) { placeApply(p, p.x, p.z - st, p.rotY); }
    else if (k === 'arrowdown' && p) { placeApply(p, p.x, p.z + st, p.rotY); }
    else if (k === 'pageup' && p) { p.y0 += st; p.g.position.y = p.y0; if (p.mount) p.mount.y = p.y0; }   // mounted: slides the panel up the wall
    else if (k === 'pagedown' && p) { p.y0 -= st; p.g.position.y = p.y0; if (p.mount) p.mount.y = p.y0; }
    else if (k === 'b' && p) { const skip = new Set([p, ...(p._carry || []).map(c => c.q), held]);
      p.mount = null; p.g.rotation.set(0, p.rotY, 0);                                    // a re-seat unsticks a wall mount first
      p.y0 = placeSurf(p.x, p.z, skip, { surfOnly: p.surf }); p.g.position.y = p.y0; }      // re-seat on the measured surface — furniture never lands on a rider
    else if ((k === ',' || k === '[') && p) { placeApply(p, p.x, p.z, p.rotY + Math.PI / 12); }
    else if ((k === '.' || k === ']') && p) { placeApply(p, p.x, p.z, p.rotY - Math.PI / 12); }
    else if (k === 'enter' && p) { placeDrop(); }                                           // stage-1 accept — deselect, stay in free cam
    else if (k === 'c') { if (!window.__place.copy()) placeSay('SELECT AN ITEM FIRST'); }
    else if (k === 'v') { placePaste(); }
    else if (k === 'delete' || k === 'backspace') { window.__place.del(); }
    else if (k === 'p') { if (!p) placeSay('SELECT AN ITEM FIRST');                       // selection-owned like C — a bare press explains itself
      else if (typeof pendSet === 'function') { pendSet(p, !p.pendWant); placeSave();
        placeSay(p.pendWant ? 'PENDANT ON — ' + p.name : 'PENDANT OFF — ' + p.name); } }   // the attached light — configured while placing or moving, persists with the rec
    else if (k === 'y' && p) { const line = p.name + ' ' + p.x.toFixed(3) + ' ' + p.z.toFixed(3) + ' ' + p.rotY.toFixed(3);
      if (navigator.clipboard) navigator.clipboard.writeText(line).catch(() => {}); placeSay('YANKED → ' + line); }
    else if (k === 'l') window.__torch.set(!window.__torch.on);                             // torch rides the fly cam too — light where you look while placing
    else if (' eftr'.includes(k)) { }                                                       // swallowed while placing — no pickups/tags/aims
    if ('arrowleft arrowright arrowup arrowdown pageup pagedown'.includes(k)) e.preventDefault();
    return;
  }
  if (k === 'escape' && consOpen) { closeCons(); return; }
  if (k === 'e') {
    if (consOpen) { closeCons(); return; }
    if (held) { const t = dropTarget(); if (t) placeIt(t); }
    else if (pickIt) takeIt(pickIt);
    else if (promptIt) promptIt.fn();
  } else if (held && (k === ',' || k === 'n')) { heldSnapStep('y', 1); }
  else if (held && (k === '.' || k === 'm')) { heldSnapStep('y', -1); }
  else if (held && k === '[') { heldSnapStep('x', 1); }
  else if (held && k === ']') { heldSnapStep('x', -1); }
  else if (held && k === ';') { heldSnapStep('z', 1); }
  else if (held && k === '\'') { heldSnapStep('z', -1); }
  else if (held && k === '0') { heldSnap.x = heldSnap.y = heldSnap.z = 0; heldFree.identity(); }   // LEVEL — exactly authored-upright
  else if (k === 'p' && window.__eyes) { povOn ? window.__eyes.hide() : window.__eyes.show(); }
  else if (k === 't') { followers.forEach(f => { f.possessed = false; f.setOrder('goto'); }); }          // GO — walk to the live tag (your hotkey always reclaims)
  else if (k === 'r') { followers.forEach(f => f.possessed = false); clearTags(); }                      // release — clear the tag and the gaze it ordered
  else if (k === 'h') { followers.forEach(f => { f.possessed = false; f.setOrder(f.order === 'follow' ? 'wait' : 'follow'); }); }   // HEEL ⇄ WAIT
  else if (k === 'l') window.__torch.set(!window.__torch.on);                     // inspection torch — light where you look
  else if (k === 'y') { LIVE.on ? window.__live.stop() : window.__live.start(); }   // live link — stream session to the agent
  else if (k === 'k') { ctlOn = !ctlOn; ctlTick(); }                      // control panel toggle
  else if (k === 'o' && window.__optToggle) window.__optToggle(); });     // O ↔ OPTIONS — while it's up, its capture-phase keydown swallows before this handler ever runs
addEventListener('keyup', e => { keys[(e.key || '').toLowerCase()] = false; });
const keysClear = () => { for (const k in keys) keys[k] = false; };                 // stuck-modifier guard —
addEventListener('blur', keysClear);                                               // a lost keyup (Ctrl+W, Alt+Tab)
document.addEventListener('visibilitychange', () => { if (document.hidden) keysClear(); }); // cancels flyStep's u or locks walk speed
