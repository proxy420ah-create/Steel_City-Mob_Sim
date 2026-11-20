'use strict';
/* construct/01_shell.js — renderer/scene/camera, lights + inspection torch, MANIFEST tiles, walk law, floor + bounds, MAT_LIB, voxEmit.
   Split from construct.html (was lines 27-197); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

const VOID = 0xf4f1e8;
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 2));
renderer.setSize(innerWidth, innerHeight);
document.body.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(VOID);
scene.fog = new THREE.Fog(VOID, 3.5, 13);
const camera = new THREE.PerspectiveCamera(58, innerWidth / innerHeight, .02, 220);
window.__THREE = THREE; window.__scene = scene; window.__camera = camera; window.__renderer = renderer;

/* lights registered at init, always on — this page's whole scene compiles under one light set */
const hemi = new THREE.HemisphereLight(0xffffff, 0xd8d2c4, 1.2), key = new THREE.PointLight(0xfff6e8, 2.4, 6, 1.2);
key.position.set(0, 1.5, 1.5); scene.add(hemi, key);

/* L-key inspection torch — same rig as the Commission's: a camera-aimed spot + a faint
   near-field fill, parked at intensity 0 until toggled. aim() repoints it before each
   render — main cam, fly cam and the follower POV all feed it through the camera object. */
const _tdir = new THREE.Vector3();
const TORCH_COLS = [0xfff0d0, 0xffffff, 0xdfe8ff, 0xffb09a, 0xb6ffc8];   // warm / day / cool / red / green
const TORCH_BEAMS = [.28, .45, .65];                                     // tight / standard / wide cone
const TORCH_DEF = { i: 5, f: .55, a: 1, c: 0 };                              // the whole tune — spot power, halo fill, cone, tint
const TC = Object.assign({}, TORCH_DEF,                                  // the tune survives F5 — dvm_torch
  JSON.parse(localStorage.getItem('dvm_torch') || '{}'));
for (const k in TC) if (!(k in TORCH_DEF)) delete TC[k];                   // retired tune keys (x/n/p/s — the auto-exposure era) die on load
const torchApply = () => {
  if (!TORCH_COLS[TC.c]) TC.c = 0; if (!TORCH_BEAMS[TC.a]) TC.a = 1;   // stale-save clamp
  torch.color.setHex(TORCH_COLS[TC.c]); torchFill.color.setHex(TORCH_COLS[TC.c]);
  torch.angle = TORCH_BEAMS[TC.a];
  torch.intensity = window.__torch.on ? TC.i : 0; torchFill.intensity = window.__torch.on ? TC.f : 0;
  localStorage.setItem('dvm_torch', JSON.stringify(TC)); };
const torchCfg = (k, v) => {
  if (k === 'a') TC.a = (TC.a + 1) % TORCH_BEAMS.length;                 // beam cycles the cone table
  else if (k === 'c') TC.c = v;                                        // swatch index — spot + fill tint together
  else if (k === 'i') TC.i = Math.max(0, Math.min(12, +(TC.i + v).toFixed(1)));
  else if (k === 'f') TC.f = Math.max(0, Math.min(3, +(TC.f + v).toFixed(2)));
  torchApply(); };
const torchReset = () => { Object.assign(TC, TORCH_DEF); torchApply(); };
const torch = new THREE.SpotLight(TORCH_COLS[TC.c], 0, 10, TORCH_BEAMS[TC.a], .55, 1.2);
const torchFill = new THREE.PointLight(TORCH_COLS[TC.c], 0, 3, 1.4);
const torchTgt = new THREE.Object3D(); scene.add(torch, torchFill, torchTgt); torch.target = torchTgt;
/* dumb rig by design - a camera-aimed spot + a faint near-field fill, nothing adaptive. aim()
   only repoints it before each render; intensity, cone and color are the tune verbatim. (The
   auto-exposure / incidence / proximity-fade / cone-widening / ease / DDA / soft-knee layers
   made the spot unstable while moving - stripped back to this base to build up again clean.) */
window.__torch = { on: false, spot: torch, fill: torchFill, cfg: TC, tune: torchCfg, rst: torchReset,
  setOn(on) { this.on = !!on; torchApply(); },
  set(on) { this.setOn(on); },
  aim(cam) { cam.getWorldPosition(torch.position); cam.getWorldDirection(_tdir);
    torchTgt.position.copy(torch.position).addScaledVector(_tdir, 2);
    torchFill.position.copy(torch.position); } };                                     // fill ON the eye — a .3m lead lit the far side of clipped walls before the camera arrived

/* ── MANIFEST — the procedural expansion seam. tiles = [i,j] grid cells (.6 pitch, i east, j north),
   props = future display placements (mannequin, pedestal, prop table — append, don't redesign). */
const TILE = 1.0;
const MANIFEST = { tiles: [], props: [] };
for (const i of [-2.5,-1.5,-.5,.5,1.5,2.5]) for (const j of [.5,-.5,-1.5,-2.5,-3.5,-4.5,-5.5]) MANIFEST.tiles.push([i, j]);   // room 6m x 7m — edges x±3 z[+1,-6], rotator dead-centre (0,-2.5), door inside the north edge
for (const i of [-3.5,-4.5,-5.5,-6.5,-7.5,-8.5,-9.5]) for (const j of [.5,-.5,-1.5,-2.5,-3.5,-4.5,-5.5]) MANIFEST.tiles.push([i, j]);  // WEST ANNEX — drafting room 7m x 7m, edges x[-10,-3] z[-6,1] (expanded 2m west for the centered 2.4m zone table)

/* ── walk law — the union of allowed rects minus forbidden wall stubs. The doorway is a 1m
   gap in the west edge x=-3 at z[-3,-2] (dead on the rotator axis); the rest of that edge is
   impassable interior wall, enforced by WALL_BLOCKS (capsule-radius-expanded stubs). */
const WALK_RECTS = [
  { x0: -2.78, x1: 2.78, z0: -5.78, z1: .78 },                          // main room (wall faces at ±.07, capsule inset .15)
  { x0: -9.78, x1: -2.78, z0: -5.78, z1: .78 },                         // west annex — meets main at the x=-2.78 seam inside the door
];
const WALL_BLOCKS = [                                                    // interior wall stubs on the shared edge x=-3 (wall x[-3.07,-2.93] + capsule)
  { x0: -3.22, x1: -2.78, z0: -6, z1: -2.825 },                         // south of the door (capsule edge kisses the jamb face at -2.975)
  { x0: -3.22, x1: -2.78, z0: -2.175, z1: 1 },                          // north of the door (jamb face -2.025)
];
const inR = (r, x, z) => x >= r.x0 && x <= r.x1 && z >= r.z0 && z <= r.z1;
const walkOK = (x, z) => WALK_RECTS.some(r => inR(r, x, z)) && !WALL_BLOCKS.some(b => inR(b, x, z));

const floorMat = new THREE.MeshBasicMaterial({ color: VOID, toneMapped: false });              // the Matrix floor — unlit, void-colored: stand on it, never see it
MANIFEST.tiles.forEach(([i, j]) => {
  const t = new THREE.Mesh(new THREE.BoxGeometry(TILE, .05, TILE), floorMat);
  t.position.set(i * TILE, -.026, j * TILE); scene.add(t);
});

/* ── bounds — RGB-style glowing edges tracing the walkable perimeter. Now an explicit spec:
   BOUND_FLOOR lists floor-level band segments (the T-union outline + interior wall stubs), and
   BOUND_ARCH lists lifted "doorway" ∩s — the same wire raised into beams where a gap passes.
   Expansion path: append MANIFEST.tiles (floor) + BOUND_FLOOR/BOUND_ARCH (wire) then rebuildBounds(). */
const boundsG = new THREE.Group(); scene.add(boundsG);
const coreMat = new THREE.MeshBasicMaterial({ color: 0x35d5ff, transparent: true, opacity: .85, depthWrite: false, side: THREE.DoubleSide });
/* hue ride: sine sweep through blue (.58) → purple → pink (.92) and back — never leaves the palette */
const boundHue = t => { const h = .58 + .34 * (0.5 + 0.5 * Math.sin(t * .3)); coreMat.color.setHSL(h, .85, .6);
  if (kioWireMat) kioWireMat.color.copy(coreMat.color);                    // the button ghost-frames ride the same sweep
  if (padEdgeMat) padEdgeMat.color.copy(coreMat.color); };                 // the debug pad's edge trim too
const BND_W = .008;                                                     // wire stock width
const WALL_H = 1.55, WALL_D = .14;                                      // wall datum = the room's cornice — arch lintels stay door-height below it
const wallMat = floorMat;                                               // walls obey the floor's own law — void-colored, opaque, NEVER seen
const BOUND_WALL = [                                                    // [ax, az, bx, bz] wall centerlines — each becomes slab + cap wire
  [-3,-6, 3,-6], [3,-6, 3,1], [3,1, -3,1],                              // main room — S, E, N edges
  [-3,1, -10,1], [-10,1, -10,-6], [-10,-6, -3,-6],                      // annex — N, W, S edges (west wall at -10 after the 2m expansion)
  [-3,-6, -3,-3], [-3,-2, -3,1],                                        // interior wall stubs — the west wall minus the door gap
];
/* hitProxy — an invisible 12-tri box a ray can land on: stands in for instanced meshes that
   rayPruned skips (minis, props, rigs) so the aim point still rests ON what you're pointing at
   instead of passing through to the wall behind. colorWrite/depthWrite off = draws nothing;
   material stays "visible" — Mesh.raycast ignores invisible materials. */
const PROXY_MAT = new THREE.MeshBasicMaterial({ colorWrite: false, depthWrite: false });
const hitProxy = (w, h, d, cx = 0, cy = 0, cz = 0) => {
  const b = new THREE.Mesh(new THREE.BoxGeometry(Math.max(w, .001), Math.max(h, .001), Math.max(d, .001)), PROXY_MAT);
  b.position.set(cx, cy, cz); b.userData.hitProxy = true; return b;
};
const BOUND_ARCH = [ { x: -3, z0: -3, z1: -2, h: 1.05, d: .14 } ];       // doorway: the floor wire lifted into a ∩ — outline on EACH room's face
const ARCH_ENDS = new Set(BOUND_ARCH.flatMap(a => [[a.x, a.z0], [a.x, a.z1]]).map(([x, z]) => x + ',' + z));
function rebuildBounds() {
  boundsG.clear();
  const seam = (ax, az, bx, bz, y = .004) => {                          // one glow band a→b — floor seam, or cornice when lifted
    const g = new THREE.PlaneGeometry(Math.hypot(bx - ax, bz - az), BND_W); g.rotateX(-Math.PI / 2);
    const m = new THREE.Mesh(g, coreMat);
    m.rotation.y = -Math.atan2(bz - az, bx - ax); m.position.set((ax + bx) / 2, y, (az + bz) / 2); m.userData.noAim = true; boundsG.add(m);
  };
  BOUND_WALL.forEach(([ax, az, bx, bz]) => {                            // each centerline → invisible slab + floor-seam glow bands
    const len = Math.hypot(bx - ax, bz - az), ux = (bx - ax) / len, uz = (bz - az) / len;
    const pA = ARCH_ENDS.has(ax + ',' + az) ? 0 : WALL_D / 2, pB = ARCH_ENDS.has(bx + ',' + bz) ? 0 : WALL_D / 2;   // slab pads close corner miters; arch ends stay flush
    const mx = (ax + bx) / 2 + ux * (pB - pA) / 2, mz = (az + bz) / 2 + uz * (pB - pA) / 2;
    const slab = new THREE.Mesh(new THREE.BoxGeometry(len + pA + pB, WALL_H, WALL_D), wallMat);
    slab.rotation.y = -Math.atan2(bz - az, bx - ax); slab.position.set(mx, WALL_H / 2, mz);
    slab.userData.wallSlab = true; boundsG.add(slab);                      // the debug pad's stickable faces
    const so = WALL_D / 2 + BND_W / 2;                                  // seams follow the slab ends — wrap corners, STOP at the reveal (clean door floor)
    [-1, 1].forEach(s => {
      seam(ax - ux * pA - uz * s * so, az - uz * pA + ux * s * so,
           bx + ux * pB - uz * s * so, bz + uz * pB + ux * s * so);
      seam(ax - ux * pA - uz * s * so, az - uz * pA + ux * s * so,        // the cornice — the same wire lifted to wall top,
           bx + ux * pB - uz * s * so, bz + uz * pB + ux * s * so, WALL_H); // makes the invisible slab's height read as the room's ceiling
    });
  });
  BOUND_ARCH.forEach(a => {                                             // pure wire ∩ — the lifted floor wire on each room's face + a floor-plan seam
    const w = BND_W * 1.6, mid = (a.z0 + a.z1) / 2;
    const put = (g, x, y, z) => { const me = new THREE.Mesh(g, coreMat); me.position.set(x, y, z); me.userData.noAim = true; boundsG.add(me); };
    [a.z0, a.z1].forEach(z => {
      put(new THREE.BoxGeometry(w, a.h, w), a.x - a.d / 2, a.h / 2, z);           // outline posts — one per room face
      put(new THREE.BoxGeometry(w, a.h, w), a.x + a.d / 2, a.h / 2, z);
    });
    put(new THREE.BoxGeometry(w, w, Math.abs(a.z1 - a.z0)), a.x - a.d / 2, a.h, mid);   // lintel outlines on both faces
    put(new THREE.BoxGeometry(w, w, Math.abs(a.z1 - a.z0)), a.x + a.d / 2, a.h, mid);   // floor stays clean — seams stop at the reveal
    const head = new THREE.Mesh(new THREE.BoxGeometry(a.d, WALL_H - a.h, Math.abs(a.z1 - a.z0)), wallMat);   // header slab — fills lintel→cornice over the doorway; the raised room left this span open
    head.position.set(a.x, a.h + (WALL_H - a.h) / 2, mid); head.userData.wallSlab = true; boundsG.add(head);
    const soA = a.d / 2 + BND_W / 2;                                                    // cornice wires cross the doorway too — the ceiling line stays unbroken
    [-1, 1].forEach(s => seam(a.x + s * soA, a.z0, a.x + s * soA, a.z1, WALL_H));
  });
}
rebuildBounds(); window.__rebuildBounds = rebuildBounds;

/* global material library — mirrors the studio editor's allMaterials for files that ship
   materials:[] (vehicle JSONs). Only the ids the showroom consumes are carried. */
const MAT_LIB = [{ id: 10, hex: '#cccccc' }, { id: 107, hex: '#996b3f' }, { id: 123, hex: '#c69e33' }, { id: 126, hex: '#0f0f11' },
                 { id: 140, hex: '#3a7d5c' }, { id: 141, hex: '#232826' }, { id: 142, hex: '#c0c8d0' }, { id: 143, hex: '#2d2d30' },
                 { id: 144, hex: '#f5e6af' }, { id: 145, hex: '#b42823' }, { id: 146, hex: '#6b4a2f' }, { id: 147, hex: '#96aab9', glass: true }];

/* shared emit path — same voxEmit contract as the Commission (glass split, site fillM) */
window.__glassMat = new THREE.MeshLambertMaterial({ transparent: true, opacity: .55, depthWrite: false });
window.voxEmit = (m, fillM) => {
  const gids = new Set((m.materials || []).concat(MAT_LIB).filter(mt => mt.glass || /glass/i.test(mt.name || '')).map(mt => mt.id));
  if (m.door && gids.size) {   /* door-contract props: a pane is ONE transparent sheet (median-z cell per column) */
    const cols = new Map(), rest = [];
    m.voxels.forEach(v => { if (gids.has(v[3])) { const k = v[0] + ',' + v[1]; (cols.get(k) || cols.set(k, []).get(k)).push(v); } else rest.push(v); });
    cols.forEach(a => { a.sort((p, q) => p[2] - q[2]); rest.push(a[a.length >> 1]); });
    m = { ...m, voxels: rest };
  }
  const geo = new THREE.BoxGeometry(1, 1, 1);
  const imO = new THREE.InstancedMesh(geo, new THREE.MeshLambertMaterial(), m.voxels.length);
  const imG = gids.size ? new THREE.InstancedMesh(geo, window.__glassMat, m.voxels.length) : null;
  const mats = {}; MAT_LIB.forEach(mt => mats[mt.id] = new THREE.Color(mt.hex));
  (m.materials || []).forEach(mt => mats[mt.id] = new THREE.Color(mt.hex));          // file palette wins when it ships one
  const M = new THREE.Matrix4(), dflt = new THREE.Color(0x888888);
  imO.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(m.voxels.length * 3), 3);   // pre-alloc — writes go raw
  if (imG) imG.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(m.voxels.length * 3), 3);
  const MAo = imO.instanceMatrix.array, CAo = imO.instanceColor.array,
    MAg = imG && imG.instanceMatrix.array, CAg = imG && imG.instanceColor.array;
  let no = 0, ng = 0;
  for (let vi = 0; vi < m.voxels.length; vi++) { const v = m.voxels[vi];
    fillM(M, v); const c = mats[v[3]] || dflt;
    if (imG && gids.has(v[3])) { MAg.set(M.elements, ng * 16); CAg[ng * 3] = c.r; CAg[ng * 3 + 1] = c.g; CAg[ng * 3 + 2] = c.b; ng++; }
    else { MAo.set(M.elements, no * 16); CAo[no * 3] = c.r; CAo[no * 3 + 1] = c.g; CAo[no * 3 + 2] = c.b; no++; } }
  imO.count = no; if (imG) imG.count = ng;                             // count = written — leftover capacity would draw unit boxes with black instance colors
  const out = [imO]; if (imG && ng) out.push(imG);
  return out;
};
