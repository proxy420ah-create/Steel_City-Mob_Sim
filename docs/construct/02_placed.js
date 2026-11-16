'use strict';
/* construct/02_placed.js — PLACED registry, emitAsset, surfaceAt — plus the canonical exhibits (door, rotator, desk).
   Split from construct.html (was lines 199-287); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── emitAsset — the canonical display contract. Per-file pitch/palette/truthView; every placed
   asset registers a column-top surface map in PLACED so any item can seat ON anything —
   measured, not authored (attachmentPoints stay semantic: axles, seats, grips). */
const PLACED = [];
let held = null, pickIt = null;                                          // placement state — hoisted: liftRiders/surfaceAt can run at module eval (saved dvm_lift) before the input section initializes
const ROUTES = { item: '/voxel/', vehicle: '/veh/', char: '/char/' };
const footB = (lb, x, z, rotY) => {                                            // world footprint from local bounds — ALWAYS live, never stale
  const cy = Math.cos(rotY), sy = Math.sin(rotY);
  const cs = [[lb[0], lb[2]], [lb[0], lb[3]], [lb[1], lb[2]], [lb[1], lb[3]]].map(([lx, lz]) => [x + lx * cy + lz * sy, z - lx * sy + lz * cy]);
  return [Math.min(...cs.map(c => c[0])) - .05, Math.max(...cs.map(c => c[0])) + .05,
          Math.min(...cs.map(c => c[1])) - .05, Math.max(...cs.map(c => c[1])) + .05];
};
const TOPK = (ix, iz) => (ix + 2048) * 4096 + (iz + 2048);                     // numeric tops key — string keys in the carry-frame hot loop were GC churn
const SURF_TYPES = new Set(['furniture', 'vehicle', 'room', 'building']);     // department tag → surface role
const emitAsset = (name, o = {}) => {
  const { kind = 'item', parent = scene, x = 0, z = 0, rotY = 0, on = null, seatY = null, vsOverride = null } = o;
  return fetch(ROUTES[kind] + name).then(r => r.json()).then(m => {
    if (!m.voxels) return null;
    const surf = o.surf ?? (SURF_TYPES.has(m.assetType) || !o.pickable);        // surf = floors on surfaces only; riders conform
    const vs = vsOverride || m.voxelSize || .0075;
    const dcx = m.dims[0] / 2 - .5, dcz = m.dims[2] / 2 - .5, flip = m.truthView === 'unity';
    let minB = Infinity, ly1 = 0, lx0 = Infinity, lx1 = -Infinity, lz0 = Infinity, lz1 = -Infinity;
    const tops = new Map();                                                  // tops keyed LOCAL — survives the item moving
    m.voxels.forEach(v => {
      const lx = (v[0] - dcx) * vs, lz = ((flip ? m.dims[2] - 1 - v[2] : v[2]) - dcz) * vs;
      if (v[1] * vs < minB) minB = v[1] * vs;
      if ((v[1] + 1) * vs > ly1) ly1 = (v[1] + 1) * vs;
      if (lx < lx0) lx0 = lx; if (lx > lx1) lx1 = lx; if (lz < lz0) lz0 = lz; if (lz > lz1) lz1 = lz;
      const t = (v[1] + 1) * vs, k = TOPK(Math.round(lx * 200), Math.round(lz * 200));    // 5mm columns
      if (t > (tops.get(k) ?? -1e9)) tops.set(k, t);
    });
    const seat = on ? surfaceAt(on.host, x, z)
      : (seatY === 'auto' ? worldSurfaceAt(x, z, null, { surfOnly: surf }) : (seatY ?? 0));   // 'auto' = conform within my role
    const g = new THREE.Group();
    window.voxEmit(m, (M, v) => M.makeScale(vs, vs, vs).setPosition(
      (v[0] - dcx) * vs, (v[1] + .5) * vs - minB, ((flip ? m.dims[2] - 1 - v[2] : v[2]) - dcz) * vs)).forEach(mm => g.add(mm));
    g.add(hitProxy(lx1 - lx0 + vs, ly1 - minB, lz1 - lz0 + vs,            // aim can land ON the prop —
      (lx0 + lx1) / 2, (ly1 - minB) / 2, (lz0 + lz1) / 2));               // instance tests stay skipped
    g.position.set(x, seat, z); g.rotation.y = rotY; parent.add(g);
    const rec = { name, kind, g, tops, lb: [lx0, lx1, lz0, lz1], x, z, rotY, y0: seat, minB, vs, surf,
                  topY: ly1 - minB,                                        // mesh top above the group origin — pendant aim lands here
                  pendWant: (o.pendant ?? (/desk(?!lamp)|table|counter|bench|dresser|console/.test(name) ? 1 : 0)) ? 1 : 0,   // optional attached light — table-like hosts default on (desklamp isn't a desk; lawdesk is), P in place mode toggles any rec
                  bbox: footB([lx0, lx1, lz0, lz1], x, z, rotY),
                  pickable: !!o.pickable, solids: o.solids || null, source: o.source || 'manifest', mv: o.mv || 0 };
    g.userData.pRec = rec;
    PLACED.push(rec);
    if (rec.pendWant && typeof pendAttach === 'function') pendAttach(rec);  // 17_lamps owns the pendant lifecycle (async — defined by call time)
    return rec;
  }).catch(e => console.warn('CONSTRUCT — emit failed:', name, e));
};
/* surfaceAt — the measured tabletop query: world point → host-local columns → highest top (world y).
   Pick-up/drop skips the carried item (its map would otherwise claim its own column). */
const _svW = new THREE.Vector3(), _svQ = new THREE.Quaternion(), _svE = new THREE.Euler();
const surfaceAt = (host, wx, wz, r = .07) => {
  if (!host || host === held) return 0;
  host.g.getWorldPosition(_svW); host.g.getWorldQuaternion(_svQ); _svE.setFromQuaternion(_svQ, 'YXZ');  // WORLD transform — parented items (car in carG) don't ghost at their rec x/z
  const dx = wx - _svW.x, dz = wz - _svW.z, cy = Math.cos(_svE.y), sy = Math.sin(_svE.y);
  const lx = dx * cy - dz * sy, lz = dx * sy + dz * cy;                        // world → local (inverse rotY)
  const qx = Math.round(lx * 200), qz = Math.round(lz * 200), R = Math.max(1, Math.round(r * 200));
  let top = 0;
  for (let ix = qx - R; ix <= qx + R; ix++) for (let iz = qz - R; iz <= qz + R; iz++) {
    const t = host.tops.get(TOPK(ix, iz));
    if (t !== undefined && _svW.y + t > top) top = _svW.y + t;
  }
  return top;
};

const DOOR_Z = .72;
const doorP = Promise.all([emitAsset('commission_doorframe', { x: 0, z: DOOR_Z, rotY: Math.PI }),    // file fronts are z+ — π faces the platform (−z)
                           emitAsset('commission_door', { x: 0, z: DOOR_Z, rotY: Math.PI })]);

/* ── THE ROTATOR — dead-centre turntable. Invisible deck (same void-material law as the floor):
   what you see is a glowing ring and the car floating over it. E pauses/resumes. */
const SPIN_POS = { x: 0, z: -2.5 }, SPIN_R = 1.02;
const spinG = new THREE.Group(); spinG.position.set(SPIN_POS.x, 0, SPIN_POS.z); scene.add(spinG);
const deck = new THREE.Mesh(new THREE.CylinderGeometry(SPIN_R, SPIN_R, .05, 48), floorMat); deck.position.y = -.026; spinG.add(deck);
const rimShape = new THREE.Shape(); rimShape.absarc(0, 0, SPIN_R + .004, 0, Math.PI * 2);
const rimHole = new THREE.Path(); rimHole.absarc(0, 0, SPIN_R - .012, 0, Math.PI * 2); rimShape.holes.push(rimHole);
const rimG = new THREE.ShapeGeometry(rimShape); rimG.rotateX(Math.PI / 2);
const rim = new THREE.Mesh(rimG, coreMat); rim.position.y = .004; spinG.add(rim);      // rides the same hue sweep as the bounds
const carG = new THREE.Group(); spinG.add(carG);
let spinning = true, SPIN_RATE = .35;                                 // ~18s/rev turntable
const carP = emitAsset('vehicle_490_touring', { kind: 'vehicle', parent: carG, x: 0, z: 0, seatY: .001, mv: 1 });   // mv:1 — a parked car's miniG-local coords once wrote a name-shared override that displaced the spinner; the stale mv:0 entry self-invalidates

/* ── exhibit: the prop desk — east side, facing the rotator. Items seat on the host's measured
   column-tops via the on:{host} contract — desk emits first, cargo seats on its surface. */
const deskP = emitAsset('commission_lawdesk', { x: 2.3, z: -1.1, rotY: Math.PI / 2, solids: [[-.5, 0, .35], [.5, 0, .35]] }).then(desk =>
  Promise.all([
    emitAsset('commission_desklamp',   { x: 2.3, z: -1.6,  rotY: Math.PI / 2, on: { host: desk }, pickable: true }),
    emitAsset('commission_bookoflaws', { x: 2.35, z: -1.0, rotY: .3,          on: { host: desk }, pickable: true }),
    emitAsset('commission_stamper',    { x: 2.25, z: -1.25,                   on: { host: desk }, pickable: true }),
  ]));
