'use strict';
/* construct/06_followers.js — followers — emitChar rig instancing + followStep nav/gait/gaze.
   Split from construct.html (was lines 1121-1210); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

const followers = [];
const emitChar = spec => fetch('/char/' + spec.char).then(r => r.json()).then(rig => {
  if (!rig || !rig.voxels) return null;
  const E = poseEngine(rig), N = rig.voxels.length, VS = rig.voxelSize || .01, d = rig.dims || [96, 96, 96];
  const inst = new THREE.InstancedMesh(new THREE.BoxGeometry(VS, VS, VS), new THREE.MeshLambertMaterial(), N);
  inst.instanceMatrix.setUsage(THREE.DynamicDrawUsage);                  // rewritten per posed frame — static buffers make the driver orphan/migrate on every upload
  inst.frustumCulled = false;                          // matrices rewrite per pose — load-time bounds go stale
  const pal = {}; (rig.materials || []).forEach(m => pal[m.id] = new THREE.Color(m.hex));
  const reg = rig.regions || {}, dflt = new THREE.Color(0x888888), out = spec.outfit || OUTFIT;    // spec.outfit = mannequin's wardrobe
  rig.voxels.forEach((v, i) => inst.setColorAt(i, pal[out[reg[v[0] + ',' + v[1] + ',' + v[2]]] || v[3]] || dflt));
  inst.instanceColor.needsUpdate = true;
  const CH = new THREE.Group(); CH.add(inst); scene.add(CH);
  CH.add(hitProxy(d[0] * VS, d[1] * VS, d[2] * VS, 0, d[1] * VS / 2, 0));   // the aim lands on the body, not the wall behind
  const bufA = new Float64Array(3 * N), bufB = new Float64Array(3 * N);
  const MA = inst.instanceMatrix.array;
  for (let i = 0; i < N; i++) MA[16 * i] = MA[16 * i + 5] = MA[16 * i + 10] = MA[16 * i + 15] = 1;
  function upload(A, B, blend, dip) {                  // same pipeline as the Commission's — voxel-index → CH-local metres
    const hx = d[0] / 2, hz = d[2] / 2;
    for (let i = 0, j = 12; i < N; i++, j += 16) {
      let x = B[3 * i], y = B[3 * i + 1], z = B[3 * i + 2];
      if (A) { x = A[3 * i] + (x - A[3 * i]) * blend; y = A[3 * i + 1] + (y - A[3 * i + 1]) * blend + dip; z = A[3 * i + 2] + (z - A[3 * i + 2]) * blend; }
      MA[j] = (x - hx) * VS; MA[j + 1] = (y + .5) * VS; MA[j + 2] = (z - hz) * VS;
    }
    inst.instanceMatrix.needsUpdate = true;
  }
  const eyeIdx = []; rig.voxels.forEach((v, i) => { if (reg[v[0] + ',' + v[1] + ',' + v[2]] === 10) eyeIdx.push(i); });   // region 10 = gaze anchor
  const slot = followers.length;                                        // heel slot — lateral offset around the player so bodies form up, not stack
  const heelOff = slot ? (slot % 2 ? 1 : -1) * Math.ceil(slot / 2) * .45 : 0;
  const f = { spec, rig, E, inst, pal, CH, bufA, bufB, eyeIdx, N, VS, d, upload, heelOff,
    pos: new THREE.Vector3(spec.x ?? (.4 + slot * .16), 0, spec.z ?? .45), yaw: spec.yaw ?? Math.PI,   // steps out of the door-elevator behind you
    order: spec.order || 'follow', state: 0, prevState: 0, transFrom: 0, transT: -1, blend: 1, dip: 0, walkSpeed: 1, gaitTime: 0,
    gazeT: null };
  f.setOrder = o => { f.order = { follow: 'follow', goto: 'goto', wait: 'wait' }[o] || 'follow'; };
  E.evalPose(0, 0, bufB); upload(null, bufB, 1, 0);
  CH.position.set(f.pos.x, 0, f.pos.z); CH.rotation.y = f.yaw;         // statics never run followStep — plant at emit
  if (!spec.static) {                                                // mannequin — never a follower: no roster, no heel, no PiP claim
    followers.push(f); saveFol();
    if (!povF) povF = f;                                             // first body in the room owns the eyes — party or console, same claim
  }
  return f;
}).catch(e => { window.__lastPartyErr = String(e && e.stack || e); console.warn('CONSTRUCT — follower emit failed:', spec.char, e); });

/* follow-nav — the Construct's simpler world needs less than the Commission's ring choreography:
   seek the player at polite distance, SOLIDS pushout, walk-rect clamp, measured-speed gait */
const F_TURN = 10, F_WALK = 1.4, MOVE_EPS = .1, TRANS_DUR = .32, TRANS_DIP = .45, FOLLOW_D = .5;
const followStep = (f, dt) => {
  let moving = false;
  let tx = null, tz = 0, arrive = FOLLOW_D;                              // order → target: follow heels you, goto plants on the live mark
  if (f.order === 'follow') {                                            // heel slot fans out along your right axis
    tx = pos.x + f.heelOff * Math.cos(yaw); tz = pos.z - f.heelOff * Math.sin(yaw);
    const h = f._heel || (f._heel = { x: 1e9, z: 0 });                     // deadband — a look-around turn swings the slot
    if (Math.abs(tx - h.x) + Math.abs(tz - h.z) < .25) { tx = h.x; tz = h.z; } else { h.x = tx; h.z = tz; }   // on a .45m arm; re-heel only on real moves
  }
  else if (f.order === 'goto' && marks.length) { tx = marks[0].position.x; tz = marks[0].position.z; arrive = .3; }
  if (tx !== null) {
    const dx = tx - f.pos.x, dz = tz - f.pos.z, d = Math.hypot(dx, dz);
    if (d > arrive) {
      const vx = dx / d * F_WALK, vz = dz / d * F_WALK;
      let dyaw = Math.atan2(vx, vz) - f.yaw;
      while (dyaw > Math.PI) dyaw -= 2 * Math.PI; while (dyaw < -Math.PI) dyaw += 2 * Math.PI;
      f.yaw += Math.sign(dyaw) * Math.min(Math.abs(dyaw), F_TURN * dt);
      const fnx = f.pos.x + vx * dt, fnz = f.pos.z + vz * dt;               // axis-slide against walls (union rects − stubs)
      if (walkOK(fnx, fnz)) { f.pos.x = fnx; f.pos.z = fnz; }
      else if (walkOK(fnx, f.pos.z)) f.pos.x = fnx; else if (walkOK(f.pos.x, fnz)) f.pos.z = fnz;
      for (const s of SOLIDS) { const px = f.pos.x - s[0], pz = f.pos.z - s[1], rr = s[2] + .18, dd = Math.hypot(px, pz);
        if (dd < rr && dd > 1e-4) { f.pos.x = s[0] + px / dd * rr; f.pos.z = s[1] + pz / dd * rr; } }
      for (const g of followers) { if (g === f) continue; const px = f.pos.x - g.pos.x, pz = f.pos.z - g.pos.z, dd = Math.hypot(px, pz);   // bodies don't stack
        if (dd < .3 && dd > 1e-4) { f.pos.x = g.pos.x + px / dd * .3; f.pos.z = g.pos.z + pz / dd * .3; } }
      moving = true;
    }
  }
  const pxs = f.pos.x - pos.x, pzs = f.pos.z - pos.z, ds = Math.hypot(pxs, pzs);        // never inside the player either
  if (ds < .28 && ds > 1e-4) { f.pos.x = pos.x + pxs / ds * .28; f.pos.z = pos.z + pzs / ds * .28; }
  const ox = f.pos.x, oz = f.pos.z;
  f.CH.position.set(f.pos.x, 0, f.pos.z); f.CH.rotation.y = f.yaw;
  const hs = Math.hypot(f.pos.x - ox, f.pos.z - oz) / dt;
  const was = f.state === 1 || f.transT >= 0;
  if (hs > MOVE_EPS) { f.state = 1; f.walkSpeed = Math.min(2, Math.max(.5, hs * .5)); } else f.state = 0;
  if (f.state !== f.prevState) { f.transFrom = f.prevState; f.prevState = f.state; f.transT = 0; }
  f.gaitTime += dt * f.walkSpeed;
  if (f.transT >= 0) { f.transT += dt; const t = Math.min(1, f.transT / TRANS_DUR);
    f.blend = t * t * (3 - 2 * t); f.dip = -Math.sin(Math.PI * t) * TRANS_DIP;
    if (t >= 1) { f.transT = -1; f.blend = 1; f.dip = 0; } }
  let gaze = null;                                            // a live tag wins; else idle & close → his head tracks you (region-10 channel, arm untouched)
  const gT = f.gazeT ? f.gazeT : (!moving && Math.hypot(pos.x - f.pos.x, pos.z - f.pos.z) < 2.2 ? _playerHead : null);
  if (gT) {
    const lx = gT.x - f.pos.x, lz = gT.z - f.pos.z, ly = gT.y, cy = Math.cos(f.yaw), sy = Math.sin(f.yaw);
    gaze = f._gaze || (f._gaze = { T: [0, 0, 0], blend: 0, head: 1 });    // reuse — per-frame objects were GC feed
    gaze.T[0] = (lx * cy - lz * sy) / f.VS + f.d[0] / 2; gaze.T[1] = ly / f.VS; gaze.T[2] = (lx * sy + lz * cy) / f.VS + f.d[2] / 2;
  }
  let dirty = was || f.state === 1 || f.transT >= 0;                       // motion/transition — repaint every frame
  if (!dirty && gaze) {                                                  // idle head-track — repaint only when the aim moved (voxel eps)
    const T = gaze.T, k = f._gk || (f._gk = [1e9, 1e9, 1e9]);
    if (Math.abs(T[0] - k[0]) + Math.abs(T[1] - k[1]) + Math.abs(T[2] - k[2]) > .8) { dirty = true; k[0] = T[0]; k[1] = T[1]; k[2] = T[2]; }
  }
  if (dirty) {
    f.E.evalPose(f.state, f.gaitTime, f.bufB, gaze);
    if (f.transT >= 0) { f.E.evalPose(f.transFrom, f.gaitTime, f.bufA, gaze); f.upload(f.bufA, f.bufB, f.blend, f.dip); }
    else f.upload(null, f.bufB, 1, 0);
  }
};
