'use strict';
/* construct/10_self_solids.js — self body (Vinny at your feet), roster persistence, ready gate, SOLIDS.
   Split from construct.html (was lines 1541-1626); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── SELF BODY — the player IS Vinny: the full rig at your position, walk/idle driven by
   your movement, aim channel raising the arm on your laser. Head voxels (Face/Head/Top/Eyes)
   live on a second mesh in layer 1 — follower POVs see the whole body; your camera never
   draws your own skull. */
const HEAD_REGS = new Set([1, 8, 9, 10]);
let selfBody = null;
const emitSelf = () => fetch('/char/Civilian1').then(r => r.json()).then(rig => {
  if (!rig || !rig.voxels) return null;
  const E = poseEngine(rig), N = rig.voxels.length, VS = rig.voxelSize || .01, d = rig.dims || [96, 96, 96];
  const pal = {}; (rig.materials || []).forEach(m => pal[m.id] = new THREE.Color(m.hex));
  const reg = rig.regions || {}, dflt = new THREE.Color(0x888888);
  const CH = new THREE.Group(); scene.add(CH);
  const parts = [[], []];                                                     // [body, head]
  rig.voxels.forEach((v, i) => parts[HEAD_REGS.has(reg[v[0] + ',' + v[1] + ',' + v[2]]) ? 1 : 0].push(i));
  const meshes = parts.map((list, pi) => {
    const im = new THREE.InstancedMesh(new THREE.BoxGeometry(VS, VS, VS), new THREE.MeshLambertMaterial(), list.length);
    im.instanceMatrix.setUsage(THREE.DynamicDrawUsage);                  // rewritten per posed frame — the walk/aim pipeline feeds it at 60Hz
    im.frustumCulled = false;
    if (pi) im.layers.set(1);                                                 // head — povCam layer 1 sees it, your camera doesn't
    im.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(list.length * 3), 3);   // pre-alloc — raw palette writes
    const CA = im.instanceColor.array;
    list.forEach((i, j) => { const v = rig.voxels[i], c = pal[OUTFIT[reg[v[0] + ',' + v[1] + ',' + v[2]]] || v[3]] || dflt;
      CA[j * 3] = c.r; CA[j * 3 + 1] = c.g; CA[j * 3 + 2] = c.b; });
    im.instanceColor.needsUpdate = true;
    const MA = im.instanceMatrix.array;
    for (let k = 0; k < list.length; k++) { const j = k * 16; MA[j] = MA[j + 5] = MA[j + 10] = MA[j + 15] = 1; }
    CH.add(im);
    return { im, list };
  });
  povCam.layers.enable(1);
  const bufA = new Float64Array(3 * N), bufB = new Float64Array(3 * N);
  function uploadMesh(m, A, B, blend, dip) {
    const hx = d[0] / 2, hz = d[2] / 2, MA = m.im.instanceMatrix.array;
    for (let k = 0; k < m.list.length; k++) { const i = m.list[k], j = k * 16 + 12;
      let x = B[3 * i], y = B[3 * i + 1], z = B[3 * i + 2];
      if (A) { x = A[3*i]+(x-A[3*i])*blend; y = A[3*i+1]+(y-A[3*i+1])*blend+dip; z = A[3*i+2]+(z-A[3*i+2])*blend; }
      MA[j] = (x - hx) * VS; MA[j + 1] = (y + .5) * VS; MA[j + 2] = (z - hz) * VS; }
    m.im.instanceMatrix.needsUpdate = true;
  }
  const s = { CH, E, meshes, bufA, bufB, uploadMesh, VS, d,
    yaw: Math.PI, state: 0, prevState: 0, transFrom: 0, transT: -1, blend: 1, dip: 0, walkSpeed: 1, gaitTime: 0 };
  E.evalPose(0, 0, bufB); meshes.forEach(m => uploadMesh(m, null, bufB, 1, 0));
  selfBody = s; return s;
}).catch(e => console.warn('CONSTRUCT — self body unavailable:', e));

const selfStep = dt => {
  if (!selfBody) return; const s = selfBody;
  let dy = yaw - s.yaw; while (dy > Math.PI) dy -= 2 * Math.PI; while (dy < -Math.PI) dy += 2 * Math.PI;
  s.yaw += dy * Math.min(1, dt * 8);                                          // body eases onto your look yaw — feet face where you face
  const ox = s.CH.position.x, oz = s.CH.position.z;
  s.CH.position.set(pos.x, 0, pos.z); s.CH.rotation.y = s.yaw;
  const hs = Math.hypot(pos.x - ox, pos.z - oz) / dt;
  const was = s.state === 1 || s.transT >= 0;
  if (hs > MOVE_EPS) { s.state = 1; s.walkSpeed = Math.min(2, Math.max(.5, hs * .5)); } else s.state = 0;
  if (s.state !== s.prevState) { s.transFrom = s.prevState; s.prevState = s.state; s.transT = 0; }
  s.gaitTime += dt * s.walkSpeed;
  if (s.transT >= 0) { s.transT += dt; const t = Math.min(1, s.transT / TRANS_DUR);
    s.blend = t * t * (3 - 2 * t); s.dip = -Math.sin(Math.PI * t) * TRANS_DIP;
    if (t >= 1) { s.transT = -1; s.blend = 1; s.dip = 0; } }
  let aim = null;                                                             // your laser is HIS arm — same IK channel the Commission's Vinny uses
  if (aimOn) { const lx = aimWorld.x - pos.x, ly = aimWorld.y, lz = aimWorld.z - pos.z, cy = Math.cos(s.yaw), sy = Math.sin(s.yaw);
    aim = s._aim || (s._aim = { T: [0, 0, 0], blend: 1, head: 1 });
    aim.T[0] = (lx * cy - lz * sy) / s.VS + s.d[0] / 2; aim.T[1] = ly / s.VS; aim.T[2] = (lx * sy + lz * cy) / s.VS + s.d[2] / 2; }
  if (was || s.state === 1 || s.transT >= 0 || aim) {
    s.E.evalPose(s.state, s.gaitTime, s.bufB, aim);
    if (s.transT >= 0) { s.E.evalPose(s.transFrom, s.gaitTime, s.bufA, aim); s.meshes.forEach(m => s.uploadMesh(m, s.bufA, s.bufB, s.blend, s.dip)); }
    else s.meshes.forEach(m => s.uploadMesh(m, null, s.bufB, 1, 0));
  }
};

const selfP = emitSelf();
const saveFol = (list = followers) => { try { localStorage.setItem('dvm_fol', JSON.stringify(list.map(f =>   // the roster persists — plain F5 keeps
  ({ char: f.spec.char, order: f.order === 'goto' ? 'wait' : f.order, x: +f.pos.x.toFixed(2), z: +f.pos.z.toFixed(2) })))); } catch {} };  // them in the room; a mid-transit goto lands as wait
const partyP = PARTY.length ? Promise.all(PARTY.map(emitChar))                                    // the portal handoff wins when it exists
  : Promise.all(JSON.parse(localStorage.getItem('dvm_fol') || '[]').map(emitChar));               // otherwise the saved roster restores itself
const ready = Promise.all([doorP, carP, deskP, partyP, selfP]);
MANIFEST.props.push({ kind: 'turntable', x: SPIN_POS.x, z: SPIN_POS.z, r: SPIN_R, asset: 'vehicle_490_touring' },
                    { kind: 'prop-table', x: 2.3, z: -1.1, asset: 'commission_lawdesk',
                      items: ['commission_desklamp', 'commission_bookoflaws', 'commission_stamper'] });

const SOLID_BASE = [[-.33, DOOR_Z, .1], [0, DOOR_Z, .11], [.33, DOOR_Z, .1],   // frame jambs + leaf face
                    [SPIN_POS.x, SPIN_POS.z, SPIN_R],                           // rotator pedestal
                    [MANN_POS.x, MANN_POS.z, .22], [KIOSK_POS.x, KIOSK_POS.z, .28]];   // mannequin + kiosk
const SOLIDS = [];
const refreshSolids = () => {                                                 // placed-derived solids (desk etc.) follow their item
  SOLIDS.length = 0; SOLID_BASE.forEach(s => SOLIDS.push(s));
  PLACED.forEach(p => (p.solids || []).forEach(([lx, lz, r]) => {
    const cy = Math.cos(p.rotY), sy = Math.sin(p.rotY);
    SOLIDS.push([p.x + lx * cy + lz * sy, p.z - lx * sy + lz * cy, r]); }));
};
