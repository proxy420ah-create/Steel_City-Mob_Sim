'use strict';
/* construct/03_tables.js — drafting tables: emitTable, piston lift, stasset/block/zone emit, emitRing/emitCore (static street + swappable interior), emitVox, parked cars, blockTrf cruise, mini sync/slice/spin, mountModel + table boot.
   Split from construct.html (was lines 289-772); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ═══ DRAFTING ROOM — the west annex's exhibit layer. MANIFEST.tables is the expansion seam:
   each entry builds one square low table (dark furniture stock + the floor-wire seam lifted to
   the tabletop rim — the same trick the doorway uses) plus its miniature buildings. The table is
   a surf-role PLACED rec, so the measured-surface law hosts items on it natively; each miniature
   writes its own per-column top into the host's tops map (a model's roof and open courtyard read
   as different heights — measured, never authored). ═══ */
const BLDG_VS = .05;                                                    // the in-game voxel — terrain AND buildings (VoxelChunkManager forces .05)
const BLDG_MAT = {                                                       // mob_materials.py — MUST match StAssetReader.cs
  10: [.80,.80,.80],                                                     // vehicle trim (MAT_LIB #cccccc) — stamped cars ride this palette too
  100: [.58,.26,.20], 101: [.48,.42,.34], 102: [.58,.58,.54], 103: [.82,.80,.74], 104: [.18,.18,.20], 105: [.42,.38,.34],
  106: [.30,.18,.10], 107: [.60,.42,.25], 108: [.42,.36,.26], 109: [.28,.24,.22], 110: [.42,.40,.36], 111: [.90,.88,.82],
  112: [.45,.55,.65], 113: [.45,.55,.65], 114: [.55,.65,.70], 115: [.95,.15,.15], 116: [.15,.30,.95], 117: [.15,.85,.25],
  118: [.28,.24,.20], 119: [.55,.32,.20], 120: [.45,.12,.10], 121: [.15,.28,.18], 122: [.22,.12,.08], 123: [.78,.62,.20],
  124: [1,.85,.50],   125: [.82,.68,.55], 126: [.06,.06,.07], 127: [.88,.86,.82], 128: [.12,.08,.06], 129: [.12,.20,.45],
  134: [.82,.88,.92], 137: [.10,.25,.45], 140: [.23,.49,.36], 141: [.14,.16,.15], 142: [.75,.78,.82], 143: [.18,.18,.19],
  144: [.96,.90,.69], 145: [.71,.16,.14], 146: [.42,.29,.18], 147: [.59,.67,.73] };
const BLDG_GLASS = new Set([112, 113, 114, 134, 147]);                  // glass ids — the voxEmit transparency contract
const darkMat = new THREE.MeshLambertMaterial({ color: 0x14161c });      // the Construct's furniture stock (consoles/kiosk)
const rodMat = new THREE.MeshLambertMaterial({ color: 0x2e3442 });       // machined piston rod — a half-tone off the barrel
MANIFEST.tables = [                                                    // PRIMARY TABLE — south flank of the door approach (-4.1,-1.1)
  { id: 'primary', x: -4.1, z: -1.1, size: 1.2, top: .26, scale: 1 / 64, mv: 2,   // scale = shared-table law; a model may declare `fill` instead —
    models: [{ name: 'tenement_block_0', fill: .75 }],                   // mounted exhibit — one model at a time, .75 of the tabletop
    catalog: [{ name: 'tenement_block_0', fill: .75 },                   // the swappable universe — the wall panel cycles it
              { name: 'barber_shop', fill: .75 }] },
  { id: 'block', x: -4.1, z: -3.9, size: 1.2, top: .26, scale: 1 / 64, mv: 3,   // BLOCK TABLE — north flank of the door approach (9 lots of 64 vox = 192 span)
    noSlice: true,                                                            // streets stay whole — roadProf's measured lanes are a whole-model law, a cut would orphan them
    ring: true,                                                               // static street exhibit: emitRing paints roads/curbs once, cars mount on it forever — swaps touch only the interior core
    parked: [{ edge: 's', along: 160 }, { edge: 'w', along: 140 }],             // the street scene is the TABLE's, not the model's —
    models: [{ name: 'block_26_tenement', fill: .75, slots: { 0: 'tenement_block_0' } }],   // every block composite carries the same roads + parked cars
    catalog: [{ name: 'block_26_tenement', fill: .75, slots: { 0: 'tenement_block_0' } },
              { name: 'block_41_barber', fill: .75, slots: { 2: 'barber_shop' } }] },       // block_41's authored lot — the rest = empty_land
  { id: 'studio', x: -6.5, z: -2.5, size: 2.4, top: .26, scale: 1 / 64, mv: 2,  // STUDIO TABLE — the ZONE tier centerpiece: 2.4m, dead-center of the expanded annex; a 3×3 rectangle of live city_layout blocks, "The Battlefield" (r3-5 × c0-2)
    noSlice: true,                                                            // zones + block composites are street models — same whole-road law
    models: [{ name: 'battlefield', fill: .75, plate: true, noProxy: true,      // plate: full-coverage ground → flat-tops path; noProxy: emitZone lays per-stamp proxies instead
               zone: { src: 'city_layout.json', r0: 3, r1: 5, c0: 0, c1: 2 } }],
    catalog: [{ name: 'battlefield', fill: .75, plate: true, noProxy: true,
                zone: { src: 'city_layout.json', r0: 3, r1: 5, c0: 0, c1: 2 } },
              { name: 'tenement_block_0', fill: .75 },
              { name: 'barber_shop', fill: .75 },
              { name: 'block_26_tenement', fill: .75, slots: { 0: 'tenement_block_0' } },
              { name: 'block_41_barber', fill: .75, slots: { 2: 'barber_shop' } }] },
];
const TABLES = [];
const SLEEVE_H = .12;                                                    // piston barrel — the fixed lower leg section
const emitTable = spec => {
  const g = new THREE.Group(); g.position.set(spec.x, 0, spec.z); scene.add(g);
  const s = spec.size, h = spec.top;
  const slab = new THREE.Mesh(new THREE.BoxGeometry(s, .04, s), darkMat); slab.position.y = h - .02; g.add(slab);
  const rods = [], legs = [];                                            // piston legs — barrel + collar + telescoping rod
  [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([a, b]) => { const lg = new THREE.Group();
    lg.position.set(a * (s / 2 - .11), 0, b * (s / 2 - .11)); g.add(lg);
    const sleeve = new THREE.Mesh(new THREE.BoxGeometry(.09, SLEEVE_H, .09), darkMat);
    sleeve.position.y = SLEEVE_H / 2; lg.add(sleeve);
    const collar = new THREE.Mesh(new THREE.BoxGeometry(.105, .02, .105), darkMat);   // the gland the rod slides through
    collar.position.y = SLEEVE_H; lg.add(collar);
    const rod = new THREE.Mesh(new THREE.BoxGeometry(.05, 1, .05), rodMat);            // unit-height rod — scale.y is the reach
    const len = h - .04 - SLEEVE_H; rod.scale.y = len; rod.position.y = SLEEVE_H + len / 2; lg.add(rod);
    rods.push(rod); legs.push(lg); });
  const rims = [];
  const w = BND_W * 1.6;                                                 // tabletop rim — the floor seam lifted to h, like the door ∩
  [[0, -1, s + w, w], [0, 1, s + w, w], [-1, 0, w, s + w], [1, 0, w, s + w]].forEach(([a, b, lx, lz]) => {
    const e = new THREE.Mesh(new THREE.BoxGeometry(lx, w, lz), coreMat);
    e.position.set(a * s / 2, h, b * s / 2); g.add(e); rims.push(e); });
  const block = { x0: spec.x - s / 2 - .15, x1: spec.x + s / 2 + .15,      // capsule-expanded footprint — exact square edge-slide
                  z0: spec.z - s / 2 - .15, z1: spec.z + s / 2 + .15 };
  WALL_BLOCKS.push(block);
  const tops = new Map();                                                // flat measured top at h over the whole footprint (5mm columns)
  const c0 = Math.round(-s / 2 * 200), c1 = Math.round(s / 2 * 200);
  for (let ix = c0; ix <= c1; ix++) for (let iz = c0; iz <= c1; iz++) tops.set(TOPK(ix, iz), h);
  const rec = { name: 'table:' + spec.id, kind: 'table', g, tops, lb: [-s / 2, s / 2, -s / 2, s / 2],
    x: spec.x, z: spec.z, rotY: 0, y0: 0, minB: 0, vs: 1, surf: true, topY: h,
    pendWant: spec.pendant !== false ? 1 : 0,                            // tables ship with the attached light — spec.pendant:false or P in place mode removes it
    bbox: footB([-s / 2, s / 2, -s / 2, s / 2], spec.x, spec.z, 0), pickable: false, solids: null, source: 'manifest', mv: spec.mv || 0, _block: block, _spec: spec };
  g.userData.pRec = rec; PLACED.push(rec);
  const t = { spec, rec, g, lift: 0, parts: { slab, rims, rods, legs } }; TABLES.push(t); return t;
};
/* tableTop = authored top + live lift — every top-derivation reads it (mount, tops, sync). */
const tableTop = t => t.spec.top + t.lift;
/* liftRiders — the column rides with its pedestal: anything standing above the old top inside the
   footprint shifts by the lift delta (stacked items keep their offsets — every level +same dy). */
const liftRiders = (t, dy, oldTop) => PLACED.forEach(q => {
  if (q === t.rec || q === held || q.mount || q.g.parent !== scene) return;  // a wall-mounted pad stays hung; parented scenery (parked cars, the spinner's car) rides its host's frame — inB reads local x/z against a world box
  if (inB(q, t.rec.bbox) && q.y0 >= oldTop - .005) { q.y0 += dy; q.g.position.y = q.y0; } });
/* tableLiftSet — the piston verb: geometry reposes, measured tops shift by delta, riders ride. */
const tableLiftSet = (t, lift) => { const oldTop = tableTop(t);
  t.lift = Math.max(-.22, Math.min(.4, lift));                            // floor notch: slab bottom reaches y=0 at H=.04
  const dy = tableTop(t) - oldTop; if (Math.abs(dy) < 1e-6) return;
  const H = tableTop(t), gap = H - .04;                                    // gap = the slab's underside
  t.parts.slab.position.y = H - .02;
  t.parts.rims.forEach(e => e.position.y = H);
  t.parts.legs.forEach((lg, i) => { const rod = t.parts.rods[i];
    lg.position.y = Math.min(0, gap - SLEEVE_H - .01);                     // past the collar tops the barrel sinks below the floor — full retraction
    const top = lg.position.y + SLEEVE_H, len = Math.max(.004, gap - top);
    rod.scale.y = len; rod.position.y = top + len / 2; });
  t.g.children.forEach(c => { if (c.userData.mini) c.position.y = H; });   // the exhibit rides its pedestal
  t.rec.tops.forEach((v, k) => t.rec.tops.set(k, v + dy));                 // measured surface follows the lift
  liftRiders(t, dy, oldTop);
  localStorage.setItem('dvm_lift:' + t.spec.id, String(t.lift)); };        // the height persists like the layout
const tableLift = (t, dy) => tableLiftSet(t, t.lift + dy);
/* stassetFetch — one parsed volume per name, cached (a block's 9 slots share fetches:
   empty_land arrives once and stamps every vacant lot). */
const _stCache = {};
const stassetFetch = name => _stCache[name] || (_stCache[name] =
  fetch('/Assets/StreamingAssets/voxel_buildings/' + name + '.stasset')
    .then(r => r.ok ? r.arrayBuffer() :                                            // vehicles live one folder over —
      fetch('/Assets/StreamingAssets/voxel_vehicles/' + name + '.stasset')         // baked 5cm derivatives (touring et al)
        .then(r2 => r2.arrayBuffer())).then(buf => {
      const dv = new DataView(buf);
      if (dv.getUint32(0, false) !== 0x53544153) throw new Error('not a stasset: ' + name);
      const W = dv.getUint16(6, true), H = dv.getUint16(8, true), D = dv.getUint16(10, true);
      return { W, H, D, vox: new Uint16Array(buf.slice(16, 16 + W * H * D * 2)) }; }));
/* emitBldg — a .stasset volume as a tabletop maquette. Dense u16 grid (STAS: x-major), surface-only
   instancing, per-column max-height written back into the host's tops. All models on a table share
   the table's declared scale — buildings stay comparable, like a real architect's set. */
const emitBldg = (t, m) => stassetFetch(m.name)
  .then(f => emitVox(t, m, f.W, f.H, f.D, f.vox))
  .catch(e => console.warn('CONSTRUCT — bldg emit failed:', m.name, e));
/* BLK — the game's per-block terrain contract: slot 64, block interior 192 (3×3 lots),
   margin M = 65v (20v sidewalk + 45v half-road) → tile 322. emitBlock builds one tile;
   emitZone tiles them — the ground/lot recipe lives once in fillBlockGround/stampSlots. */
const BLK = { S: 64, BW: 192, SIDE: 20, ROAD: 45 }; BLK.M = BLK.SIDE + BLK.ROAD; BLK.TILE = BLK.BW + 2 * BLK.M;
/* BLKF — the single-block diorama's "far" variant: a lone block exhibit has no neighbor tile to
   supply the other half-street, so it paints BOTH halves itself (band = 2×ROAD: far half + seam
   at 45 + our half; stripes at each half's center = the dual lane lines seen in-game). The rim
   is the far curb — no far sidewalk (that's the neighbor's, outside this model). emitZone keeps
   strict seam law: interior seams already read as full streets and the perimeter is the cut. */
const BLKF = { S: BLK.S, BW: BLK.BW, SIDE: BLK.SIDE, ROAD: 2 * BLK.ROAD };
BLKF.M = BLKF.SIDE + BLKF.ROAD; BLKF.TILE = BLKF.BW + 2 * BLKF.M;
/* fillBlockGround — VoxelTerrainBuilder.GeneratePerBlockTerrain's recipe, one tile at (X0,Z0):
   each block owns its ground tile (20v sidewalk ring + stone plot) plus HALF the road on each
   side (45v asphalt, 1v cobble stripe centered in the half — two adjacent tiles' halves read as
   the dual lane lines). seams {n,e,s,w} = layout seam types ('road' stripes, anything else reads
   as a plain asphalt boulevard); lanes (bits N=1 E=2 S=4 W=8) cuts curb mouths + the interior
   lane cross-section for alley catalogs. */
const fillBlockGround = (set, X0, Z0, seams, lanes, B = BLK) => { const { ROAD, M, TILE } = B;
  for (let z = 0; z < ROAD; z++) for (let x = 0; x < TILE; x++) {          // FillCorridors — horizontal bands first…
    set(X0 + x, Z0 + z,           seams.n === 'road' && (z === 22 || z === ROAD - 23) ? 105 : 104);   // …cobble stripe centered in each half…
    set(X0 + x, Z0 + TILE - 1 - z, seams.s === 'road' && (z === 22 || z === ROAD - 23) ? 105 : 104); }
  for (let x = 0; x < ROAD; x++) for (let z = 0; z < TILE; z++) {          // …vertical strips own the corners
    set(X0 + x,         Z0 + z, seams.w === 'road' && (x === 22 || x === ROAD - 23) ? 105 : 104);
    set(X0 + TILE - 1 - x, Z0 + z, seams.e === 'road' && (x === 22 || x === ROAD - 23) ? 105 : 104); }
  const m0 = M + 80, m1 = M + 112, L0 = M + 64, L1 = M + 128;
  for (let z = ROAD; z < TILE - ROAD; z++) for (let x = ROAD; x < TILE - ROAD; x++) {   // FillGroundTile
    const eW = x < M, eE = x >= TILE - M, eS = z < M, eN = z >= TILE - M;
    let id = 101;
    if (eW || eE || eS || eN) {                                        // sidewalk ring — curb cuts at mouths
      const mouth = (eW && (lanes & 8) && z >= m0 && z < m1) || (eE && (lanes & 2) && z >= m0 && z < m1) ||
                    (eS && (lanes & 4) && x >= m0 && x < m1) || (eN && (lanes & 1) && x >= m0 && x < m1);
      id = mouth ? 104 : 102;
    } else if (lanes) {                                                // interior lane cross-section
      if ((lanes & 10) && z >= L0 && z < L1) id = (z >= m0 && z < m1) ? 104 : 102;
      else if ((lanes & 5) && x >= L0 && x < L1) id = (x >= m0 && x < m1) ? 104 : 102; }
    set(X0 + x, Z0 + z, id); } };
/* stampSlots — a block's 3×3 lots into its interior (origin X0,Z0 = the tile's corner in the
   composite grid; the composite stride is W2, slab offset +2). Each explicit slot stamps its
   asset at its 64-voxel offset; a multi-slot asset marks every lot it covers; the rest take
   empty_land — the game's own vacant-lot tile. Returns the per-stamp rects for aim proxies. */
const stampSlots = (vox, W2, H, files, slots, X0, Z0, B = BLK) => { const { S, BW, M } = B;
  const covered = new Set();                                          // which lots does explicit content already own?
  Object.entries(slots).forEach(([s, n]) => { const f = files[n], sc = +s % 3, sr = (+s / 3) | 0;
    for (let r = sr; r < sr + f.D / S; r++) for (let c = sc; c < sc + f.W / S; c++) covered.add(r * 3 + c); });
  const stamped = [], stamp = (f, sx, sz) => {
    stamped.push({ x: X0 + M + sx, z: Z0 + M + sz, w: Math.min(f.W, BW - sx), d: Math.min(f.D, BW - sz), h: f.H });
    for (let z = 0; z < f.D && sz + z < BW; z++)
      for (let y = 0; y < f.H; y++) for (let x = 0; x < f.W && sx + x < BW; x++) {
        const id = f.vox[x + f.W * (y + f.H * z)]; if (id) vox[(X0 + M + sx + x) + W2 * (y + 2 + H * (Z0 + M + sz + z))] = id; } };
  for (let s = 0; s < 9; s++) { const n = slots[s]; if (n) stamp(files[n], (s % 3) * S, ((s / 3) | 0) * S); }
  for (let s = 0; s < 9; s++) if (!covered.has(s) && !slots[s]) stamp(files['empty_land'], (s % 3) * S, ((s / 3) | 0) * S);
  return stamped; };
/* roadProf — the street's own geometry, measured off the composite's column tops: the
   sidewalk is the raised run hugging the property line inside each road band (the plate's
   flat cells never list in localCols). Per edge {road, dir}: `road` = the cell the car's
   curb edge sits on (just lane-side of the strip); `dir` = which way the lane runs. The
   bare band law parked cars straddling the strip — wheels under the curb = sunken look. */
const roadProf = mn => { if (mn._road) return mn._road;                            // cached for the mount's life — legal BECAUSE street tables are noSlice: a cut could never retire a lane
  const W = mn.dims[0], D = mn.dims[2], CURB = 4;
  if (!mn._colTop) { const T = mn._colTop = new Int16Array(W * D).fill(-1);
    mn.localCols.forEach(c => { T[c.x + W * c.z] = Math.max.apply(null, c.ys); }); }
  const T = mn._colTop, R = {};
  [['s', 0, D - 1, 1], ['e', 1, W - 1, 1], ['n', 0, 0, -1], ['w', 1, 0, -1]].forEach(q => {
    const e = q[0], ax = q[1], t0 = q[2], rd = q[3], span = ax ? D : W,             // rd = rim direction; scan runs INWARD — the first raised run off the rim is our curb strip (a diorama's far half is flat and reads as nothing)
      probes = [.25, .375, .5, .625, .75].map(f => Math.round(span * f));
    let first = -1, last = -1, flat = 0;
    for (let t = t0, i = 0; i < 140 && t >= 0 && t <= (ax ? W : D) - 1; t -= rd, i++) {
      const raised = probes.filter(u => (ax ? T[t + W * u] : T[u + W * t]) > CURB).length >= 3;
      if (raised) { if (first < 0) first = t; last = t; flat = 0; }
      else if (first >= 0 && ++flat > 4) break; }
    if (first >= 0) R[e] = { road: first + rd, dir: rd }; });                      // road = our half's curb-side boundary cell
  return Object.keys(R).length ? mn._road = R : R; };                              // a found-nothing scan isn't worth caching — a fuller localCols may yet see strips
/* blockTrf — a curbside cruise for the table's parked cars: pull out of the slot, lap the
   right-of-travel lanes, pull back in. The circuit is the MEASURED street (roadProf's lane
   centers + quarter arcs at the corners) — the game's road files carry no lane/direction
   data, so the lanes are the part we derive here. Cars stay miniG-parented — spin/lift/swap
   ride free; positions are cell coords converted through mn.fx exactly like the mount did. */
const LEAD = 1.2 / BLDG_VS;                                                                  // Unity's maneuver lead 1.2 m → composite cells (RequestDepart merge / TrySpliceParking approach)
const blockTrf = { on: false, mn: null, g: null, L: null, segs: [], len: 0, cars: [], V: 3.0,   // V = real m/s — Unity VehicleTestSpawner.driveSpeed = 3.0f
  build(mn) { const R = roadProf(mn), HALF = BLK.ROAD * .5, r = 10, D = mn.dims,               // lane center = road-half center (roadWidth/4 off the street centerline) — the cobble stripe IS the lane line
      off = (mn._blkM || BLK.M) - BLK.SIDE - HALF,                                             // bare-band fallback: our-half center = margin − sidewalk − half-half
      L = { s: R.s ? R.s.road + HALF : D[2] - off, n: R.n ? R.n.road - HALF : off,
            e: R.e ? R.e.road + HALF : D[0] - off, w: R.w ? R.w.road - HALF : off },
      leg = (ax, f, a0, a1, hx, hz) => ({ ax, f, a0, a1, hx, hz, len: Math.abs(a1 - a0) }),
      arc = (cx, cz, a0, sgn) => ({ cx, cz, r, a0, sgn, len: r * Math.PI / 2 }),
      segs = [ leg('x', L.s, L.w + r, L.e - r, 1, 0),  arc(L.e - r, L.s - r, 90, -1),       // the RENDERED circuit is right-hand traffic: cell space runs
               leg('z', L.e, L.s - r, L.n + r, 0, -1), arc(L.e - r, L.n + r, 0, -1),       // counterclockwise (s→e→n→w), arcs sweep negative — the fx
               leg('x', L.n, L.e - r, L.w + r, -1, 0), arc(L.w + r, L.n + r, 270, -1),     // mirror turns it clockwise with right turns on screen
               leg('z', L.w, L.n + r, L.s - r, 0, 1),  arc(L.w + r, L.s - r, 180, -1) ];
    let s = 0; segs.forEach(g => { g.s0 = s; s += g.len; });
    this.L = L; this.segs = segs; this.len = s; this.mn = mn; this.r = r; },
  at(s) { s = ((s % this.len) + this.len) % this.len;
    const g = this.segs.find(q => s < q.s0 + q.len) || this.segs[this.segs.length - 1], t = s - g.s0;
    if (g.ax) { const d = g.a1 > g.a0 ? 1 : -1, a = g.a0 + d * t;
      return g.ax === 'x' ? { x: a, z: g.f, hx: g.hx, hz: g.hz } : { x: g.f, z: a, hx: g.hx, hz: g.hz }; }
    const a = (g.a0 + g.sgn * t / g.r * 180 / Math.PI) * Math.PI / 180;
    return { x: g.cx + g.r * Math.cos(a), z: g.cz + g.r * Math.sin(a), hx: -Math.sin(a) * g.sgn, hz: Math.cos(a) * g.sgn }; },
  stn(sc, lead) { const L = this.L, r = this.r, x = sc.edge === 's' || sc.edge === 'n';   // maneuver endpoints, Unity's law: depart merges at lanePos + dir·lead, park splices at lanePos − dir·lead
    const a = x ? Math.min(Math.max(sc.cx + sc.hx * lead, L.w + r + 2), L.e - r - 2)      // clamped inside the straight leg so sOf always resolves
                : Math.min(Math.max(sc.cz + sc.hz * lead, L.n + r + 2), L.s - r - 2);
    return x ? { x: a, z: sc.edge === 's' ? L.s : L.n }
             : { x: sc.edge === 'w' ? L.w : L.e, z: a }; },
  sOf(p) { const g = this.segs.find(q => q.ax &&
      (q.ax === 'x' ? Math.abs(p.z - q.f) < .5 && Math.min(q.a0, q.a1) <= p.x && p.x <= Math.max(q.a0, q.a1)
                    : Math.abs(p.x - q.f) < .5 && Math.min(q.a0, q.a1) <= p.z && p.z <= Math.max(q.a0, q.a1)));
    return g ? g.s0 + Math.abs((g.ax === 'x' ? p.x : p.z) - g.a0) : 0; },
  /* toggle — the CARS press is GLOBAL, not aimed at a table: the cruise lives wherever the
     parked recs live, so the host miniG is whichever table's current mini actually parents
     _parked recs — padTable()'s nearest-table guess can resolve a car-less table mid-session
     and a wrong-table call must never rebuild a live cruise's circuit. */
  toggle(t, on) { const p0 = PLACED.find(p => p._parked && p._slotC && p.g && p.g.parent),
      g = p0 && p0.g.parent, mn = g && g.userData.mini;                        // the parked rec knows its host group — the static street ring (a table may carry several minis now)
    this.on = on === undefined ? !this.on : !!on;
    if (!mn) { this.on = false; return false; }
    this.build(mn); this.g = g;
    if (!this.on) return false;                                                // off = cruise out the lap, park on arrival — cars keep their state
    this.cars = PLACED.filter(p => p._parked && p._slotC && p.g.parent === g).map(p => {
      const sc = p._slotC, Pd = this.stn(sc, LEAD), Pa = this.stn(sc, -LEAD), sl = p._slot;
      p.g.position.x = sl.x; p.g.position.z = sl.z; p.g.rotation.y = sl.rotY;   // re-arm restarts from the curb
      return { rec: p, st: 'wait', delay: 0, s: 0, sD: this.sOf(Pd), sA: this.sOf(Pa), prog: 0, yaw: sl.rotY, tgt: null,   // delay 0 — Unity departs every car on the same keypress, together
        slot: [sc.cx, sc.cz], dPt: [Pd.x, Pd.z], aPt: [Pa.x, Pa.z],
        lenD: Math.hypot(Pd.x - sc.cx, Pd.z - sc.cz) || 1, lenA: Math.hypot(Pa.x - sc.cx, Pa.z - sc.cz) || 1 }; });
    this.inv = null;                                                           // claims live on inv entries — fresh lattice next off-session
    if (!this.cars.length) this.on = false;
    return this.on; },
  seek() { this.cars.forEach(c => { if (c.st === 'loop') c.s = (c.tgt ? c.tgt.sA : c.sA) - 2; }); },
  /* slotMap — the inv lattice with per-slot maneuver endpoints (approach/merge s, leg lens,
     home pose) precomputed once per off-session — stn/sOf need the live circuit. */
  slotMap() { const c0 = this.cars[0] && this.cars[0].rec._slotC; if (!c0) return [];
    return slotInv(this.mn, c0.carW, c0.carL).map(sc => {
      const Pd = this.stn(sc, LEAD), Pa = this.stn(sc, -LEAD);
      return { sc, sD: this.sOf(Pd), sA: this.sOf(Pa), aPt: [Pa.x, Pa.z], dPt: [Pd.x, Pd.z],
        home: [sc.cx, sc.cz], by: null,
        lenA: Math.hypot(Pa.x - sc.cx, Pa.z - sc.cz) || 1,
        lenD: Math.hypot(Pd.x - sc.cx, Pd.z - sc.cz) || 1 }; })
    .filter(sl => sl.sA > .5); },                                                    // an approach that misses every leg (sOf→0) is a dead target — the crossing test can never fire on it
  /* held — physical occupancy of a lattice slot: any _parked rec seated on/near it along the
     curb axis. 'leave'/'loop' recs already released (Unity releases at RequestDepart); a
     'park'-state rec counts by its DESTINATION, not its origin. */
  held(sc) { const ax = sc.axis === 'x' ? 'cx' : 'cz';
    return PLACED.some(p => { if (!p._parked || !p._slotC || !p.g || p.g.parent !== this.g || p._slotC.edge !== sc.edge) return false;
      const o = this.cars.find(k => k.rec === p);
      if (o && (o.st === 'leave' || o.st === 'loop')) return false;
      const cs = o && o.st === 'park' && o.tgt ? o.tgt.sc : p._slotC;
      return Math.abs(cs[ax] - sc[ax]) < (sc.carL + cs.carL) / 2 + 4; }); },
  /* claim — NearestFreeAhead's law: of the free lattice slots, the one whose approach-s has
     the smallest forward wrap distance. Overshot slots wrap to ~len and lose naturally; an
     all-held inventory returns null and the car keeps seeking — same as Unity. */
  claim(c, s) { const inv = this.inv || (this.inv = this.slotMap()); let best = null, bd = Infinity;
    inv.forEach(sl => { if (sl.by || this.held(sl.sc)) return;
      const dS = (sl.sA - s + this.len) % this.len;
      if (dS < bd) { bd = dS; best = sl; } });
    if (best) best.by = c.rec; return best; },
  snap(c) { const rec = c.rec, mn = this.mn, D2 = mn.dims[2], vs = mn.vs, mir = mn.mirX ? -1 : 1;
    if (c.tgt && c.tgt.by === rec) { const sc = c.tgt.sc;                        // arrived at a claimed slot — adopt it as the new home slot
      rec._slotC = sc;
      rec._slot = { x: mn.fx(sc.cx) * vs, z: (sc.cz - D2 / 2) * vs, rotY: Math.atan2(sc.hx * mir, sc.hz) };
      c.tgt.by = null; }
    c.tgt = null;
    const sl = rec._slot; rec.g.position.x = sl.x; rec.g.position.z = sl.z;
    rec.g.rotation.y = sl.rotY; c.st = 'slot'; },
  tick(dt) { if (!this.cars.length || !this.mn) return;
    const mn = this.mn, vs = mn.vs, vc = this.V / BLDG_VS, D2 = mn.dims[2], mir = mn.mirX ? -1 : 1,
      putY = (g, x, z, yaw) => { g.position.x = mn.fx(x) * vs; g.position.z = (z - D2 / 2) * vs; g.rotation.y = yaw; },
      put = (g, x, z, hx, hz) => putY(g, x, z, Math.atan2(hx * mir, hz));              // authored front = +z (Unity LookRotation drives +z onto dir) — the −z law ran cars tail-first
    this.cars.forEach(c => { const g = c.rec.g;
      if (!g || g.parent !== this.g) { c.st = 'gone'; return; }
      if (c.st === 'wait') { if (!this.on) { c.st = 'slot'; return; }
        if ((c.delay -= dt) <= 0) { c.st = 'leave'; c.yaw = g.rotation.y;
          c.a = c.slot; c.b = c.dPt; c.len = c.lenD; } return; }                          // the departure diagonal — slot → merge point
      if (c.st === 'leave' || c.st === 'park') { const fw = c.st === 'leave' ? this.on : true,
          a = c.a, b = c.b, len = c.len;                                                 // the diagonal legs — position lerps straight, yaw slerps onto the leg dir (Unity Slerp→LookRotation(seg) @6/s → the visible angle)
        c.prog += (fw ? 1 : -1) * vc * dt / len;                                       // same vc = 3.0 m/s — maneuvers run at drive speed
        if (c.prog >= 1) { if (c.st === 'park') { this.snap(c); return; } c.s = c.sD; c.st = 'loop'; c.tgt = null; return; }
        if (c.prog <= 0) { this.snap(c); return; }
        const x = a[0] + (b[0] - a[0]) * c.prog, z = a[1] + (b[1] - a[1]) * c.prog,
          sgn = fw ? 1 : -1, ty = Math.atan2((b[0] - a[0]) * sgn * mir, (b[1] - a[1]) * sgn),
          dw = ty - c.yaw, wr = Math.atan2(Math.sin(dw), Math.cos(dw));
        c.yaw += wr * Math.min(1, 6 * dt);
        putY(g, x, z, c.yaw); return; }
      if (c.st === 'loop') { let v = vc;
        this.cars.forEach(o => { if (o !== c && o.st === 'loop') {
          const d = ((o.s - c.s) % this.len + this.len) % this.len;
          if (d > 0 && d < 30) v = Math.min(v, vc * Math.max(0, (d - 8) / 22)); } });
        const prev = c.s, ns = prev + v * dt;
        if (!this.on) {
          if (!c.tgt) c.tgt = this.claim(c, ns) ||                                     // park-anywhere: claim the nearest free slot ahead —
            { sc: c.rec._slotC, sA: c.sA, aPt: c.aPt, home: c.slot, lenA: c.lenA, by: null };   // empty/failed lattice still parks it at home
          if (c.tgt && prev < c.tgt.sA && ns >= c.tgt.sA) {                            // the lane crosses the claimed approach — splice the pull-in
            c.st = 'park'; c.prog = 0; c.yaw = g.rotation.y;
            c.a = c.tgt.aPt; c.b = c.tgt.home; c.len = c.tgt.lenA; return; } }
        c.s = ns % this.len; const q = this.at(c.s); put(g, q.x, q.z, q.hx, q.hz); } }); } };
/* mountParked — the game's curbside parking law, rendered as full-fidelity overlay
   miniatures rather than composite cells: the authored 1 cm asset mounts at the
   composite's own display scale (vs × voxelSize/BLDG_VS — every authored voxel present,
   just drawn smaller). Position law (ParkingMap.Build): parkOffset = roadWidth/2 − carW/2
   off the lane center + curb-push → center 50v − carW/2 off the block boundary; heading
   per edge is the legal right-side-of-travel direction (cell space — fx mirrors x); the
   KeepClear 40v no-park margin clamps `along`. Parented to miniG → spin/lift/swap ride
   free; the car is a real PLACED host, so items seat on it through surfaceAt. */
const PARK_CLEAR = 40;
/* PARK_EDGE — per-edge curb law in cell space: heading = the legal right-of-travel direction
   (mirrored to world by fx downstream). axis = which coordinate runs along the street. */
const PARK_EDGE = { s: { axis: 'x', hX: 1, hZ: 0 },  n: { axis: 'x', hX: -1, hZ: 0 },      // rendered-RHT law: fx mirrors x, so the cell-space
                    e: { axis: 'z', hX: 0, hZ: -1 }, w: { axis: 'z', hX: 0, hZ: 1 } };     // headings must read LEFT-handed for the mirrored view
                                                                                          // to show right-side parking + right turns (a mirrored RHT renders LHT)
/* PARK_PUSH — Unity's `extraParkOffset = 0.25f` (`ParkingMap.s.pos = station + side·(parkOffset
   + extraOffset)`): the parked center slides .25 m PAST the curb face, so the curb-side wheels
   overhang the sidewalk edge — the game's parked stance, not a flush park. */
const PARK_PUSH = Math.round(.25 / BLDG_VS);                                             // .25 m → 5 cells
/* curbC — the one curb law mounts and slots share: the measured strip's lane edge when the
   model carries a raised sidewalk, else the band's inner boundary. Flat-ground composites
   (barber's localCols lists interior cells only — no raised strip exists to find) still park
   at the real curb. `h` carries PARK_PUSH — the home point sits .25 m curb-ward of flush. */
const curbC = (e, mn, carW) => { const st = roadProf(mn)[e], h = Math.max(carW / 2 - PARK_PUSH, 0),
      ib = (mn._blkM || BLK.M) - BLK.SIDE;
    return st ? (st.dir > 0 ? st.road + h : st.road - h)
              : { s: mn.dims[2] - ib + h, n: ib - h, e: mn.dims[0] - ib + h, w: ib - h }[e]; };
/* slotInv — ParkingMap.Build's law: a fixed lattice of curbside slots per edge, carL+gap pitch
   (Unity's carLength + .5 gap ≈ 2.0 m) inside the keep-clear margins, curb position from the
   same curbC law the mounts use. Each slot is sc-shaped — stn()/the park path read
   it exactly like an authored _slotC. */
const slotInv = (mn, carW, carL) => { const W = mn.dims[0], D = mn.dims[2],
      ib = (mn._blkM || BLK.M) - BLK.SIDE, pitch = carL + Math.ceil(.5 / BLDG_VS), S = [];
    for (const e of ['s', 'n', 'e', 'w']) { const E = PARK_EDGE[e],
      c = curbC(e, mn, carW), dim = E.axis === 'x' ? W : D,
        lo = ib + PARK_CLEAR + carL / 2, hi = dim - ib - PARK_CLEAR - carL / 2;
      for (let a = lo; a <= hi; a += pitch)
        S.push({ edge: e, axis: E.axis, hx: E.hX, hz: E.hZ, carW, carL, along: a,
                 cx: (E.axis === 'x' ? a : c) - .5, cz: (E.axis === 'x' ? c : a) - .5 }); }
    return S; };
const _vehC = {};
const vehFetch = name => _vehC[name] || (_vehC[name] = fetch(ROUTES.vehicle + name).then(r => r.json()));
const mountParked = (t, miniG, parked) => { const mn = miniG.userData.mini, vs = mn.vs,
      W2 = mn.dims[0], D2 = mn.dims[2], MM = mn._blkM || BLK.M, ib = MM - BLK.SIDE;
  return Promise.all((parked || []).map(p => { const E = PARK_EDGE[p.edge]; if (!E) return null;   // AWAITED — _mountP covers the whole street, extras seat on car roofs at boot
    return vehFetch(p.name || 'vehicle_490_touring').then(veh => {
      const cvs = (veh.voxelSize || .01) / BLDG_VS,                                      // car cells per composite cell (1:5 for the touring)
            carW = veh.dims[0] * cvs, carL = veh.dims[2] * cvs;                          // real footprint in composite cells
      const lo = ib + PARK_CLEAR + carL / 2, hi = (E.axis === 'x' ? W2 : D2) - ib - PARK_CLEAR - carL / 2,
        a = Math.max(lo, Math.min(hi, p.along == null ? (lo + hi) / 2 : p.along)),
        c = curbC(p.edge, mn, carW),                                                     // hug the STRIP's lane edge — the bare band law straddled the curb
        cx = (E.axis === 'x' ? a : c) - .5, cz = (E.axis === 'x' ? c : a) - .5;            // cell-space center → cell coords
      const vvs = vs * cvs,                                                              // authored voxel at the composite's scale
        hx = mn.mirX ? -E.hX : E.hX, hz = E.hZ,                                          // fx mirrors x — cell headings mirror with it
        rotY = Math.atan2(hx, hz);                                                     // authored front = +z (Unity LookRotation drives +z onto dir) → rotate onto the heading
      return emitAsset(p.name || 'vehicle_490_touring', { kind: 'vehicle', parent: miniG,
        x: mn.fx(cx) * vs, z: (cz - D2 / 2) * vs, rotY, seatY: 2 * vs + vvs * 1.5, vsOverride: vvs, pickable: false, source: 'parked' })   // +1.5 car-voxels: at grazing angles the black tire ring merges into the road's dark top — flush and half-voxel both read sunk; a voxel-and-a-half of air lets the whole ring draw against the road face
        .then(rec => { if (!rec) return;
          if (!miniG.parent) { const i = PLACED.indexOf(rec); if (i >= 0) PLACED.splice(i, 1); rec.g.parent && rec.g.parent.remove(rec.g); return; }   // model swapped mid-fetch — no ghost cars
          rec._parked = true;
          rec._slot = { x: rec.g.position.x, z: rec.g.position.z, rotY };                 // the exact curb pose — repark restores it verbatim
          rec._slotC = { cx, cz, edge: p.edge, hx: E.hX, hz: E.hZ, carW, carL };          // cell-space slot + heading — the cruise circuit's frame
          rec.g.children.forEach(ch => { ch.userData.miniVox = true; });                  // pruned aim — 78k cells never ray-test
          const pw = (E.axis === 'x' ? veh.dims[2] : veh.dims[0]) * vvs,                   // proxy footprint swaps on quarter-turn
                pd = (E.axis === 'x' ? veh.dims[0] : veh.dims[2]) * vvs, ph = veh.dims[1] * vvs;
          rec.g.add(hitProxy(pw + vvs, ph, pd + vvs, 0, ph / 2, 0)); }); })                  // the proxy rides the car — it can drive now
      .catch(e => console.warn('CONSTRUCT — parked mount failed:', p.name, e)); })); };      // one bad car sinks neither its siblings nor the block
/* emitRing — the block table's STATIC street exhibit: the BLKF diorama ground (roads, curbs,
   sidewalks, the empty interior pad) with NO lot stamps. Emitted once for the table's life;
   parked cars mount on it and survive every model swap. `mn.static` keeps it out of the
   unmount sweep; `_road = {}` pins the flat-scan truth (no raised strips exist) so curbC/
   slotInv/blockTrf all run the deterministic band law — identical for every core. */
const emitRing = (t, fill) => { const W2 = BLKF.TILE, H = 4;
  const vox = new Uint16Array(W2 * H * W2);
  const set = (x, z, id) => { vox[x + W2 * (H * z)] = vox[x + W2 * (1 + H * z)] = id; };
  fillBlockGround(set, 0, 0, { n: 'road', e: 'road', s: 'road', w: 'road' }, 0, BLKF);
  return emitVox(t, { name: 'street_ring', fill, plate: true, _blkM: BLKF.M, static: true }, W2, H, W2, vox)
    .then(ringG => { if (ringG) { const mn = ringG.userData.mini; mn._road = {}; t._ringVs = mn.vs; } return ringG; })
    .catch(e => console.warn('CONSTRUCT — ring emit failed:', e)); };
/* emitCore — a block model's interior only: its 3×3 lots stamped into the 192-cell core that
   seats inside the table's static ring (same center; _vs pins the ring's voxel size so the
   two minis' cells align 1:1). The swap target — every block model shares the ring's roads. */
const emitCore = (t, m) => { const BW2 = BLK.BW, slots = m.slots || {};
  const names = new Set(['empty_land']); Object.values(slots).forEach(n => names.add(n));
  return Promise.all([...names].map(n => stassetFetch(n).then(f => [n, f]))).then(es => {
    const files = Object.fromEntries(es);
    const H = Math.max(2, Math.max(...[...names].map(n => files[n].H)) + 2);             // +2: the slab pair — stamps sit at y+2 flush on the ring's pad
    const vox = new Uint16Array(BW2 * H * BW2);
    stampSlots(vox, BW2, H, files, slots, 0, 0, { S: BLK.S, BW: BW2, M: 0 });
    return emitVox(t, { ...m, _vs: t._ringVs, plate: true }, BW2, H, BW2, vox); })
    .catch(e => console.warn('CONSTRUCT — block core emit failed:', m.name, e)); };
/* emitBlock — a 3×3 city block as ONE miniature: the tile recipe + lot stamps merged into a
   single composite at 1:1, tops written back into the host. ROT/SLICE/persistence unchanged.
   The non-ring path — a block composite mounted on a ring-less table (e.g. studio) still
   carries its own streets. */
const emitBlock = (t, m) => { const W2 = BLKF.TILE, slots = m.slots || {};
  const parked = (t.spec.parked || []).concat(m.parked || []);                 // the street scene is the TABLE's — every block composite carries it; a spec can still add
  const names = new Set(['empty_land']); Object.values(slots).forEach(n => names.add(n));
  return Promise.all([...names].map(n => stassetFetch(n).then(f => [n, f]))).then(es => {
    const files = Object.fromEntries(es);
    const H = Math.max(2, Math.max(...[...names].map(n => files[n].H)) + 2);
    const vox = new Uint16Array(W2 * H * W2);
    const set = (x, z, id) => { vox[x + W2 * (H * z)] = vox[x + W2 * (1 + H * z)] = id; };
    fillBlockGround(set, 0, 0, { n: 'road', e: 'road', s: 'road', w: 'road', ...(m.seams || {}) }, m.lanes | 0, BLKF);
    stampSlots(vox, W2, H, files, slots, 0, 0, BLKF);
    return emitVox(t, { ...m, plate: true, _blkM: BLKF.M }, W2, H, W2, vox)   // composites have full-coverage ground — the flat tops path applies
      .then(miniG => miniG ? mountParked(t, miniG, parked).then(() => miniG) : miniG); })   // cars overlay + AWAITED — authored fidelity, never downsampled
    .catch(e => console.warn('CONSTRUCT — block emit failed:', m.name, e)); };
/* layoutFetch — the live city layout (blocks+seams) is the zone's sole source of truth, cached. */
const _layoutC = {};
const layoutFetch = file => _layoutC[file] || (_layoutC[file] =
  fetch('/Assets/StreamingAssets/' + file).then(r => r.json()));
/* emitZone — an r0..r1 × c0..c1 rectangle of city_layout blocks as ONE miniature ("a zone" —
   the drafting room's third tier). Each block tiles its own ground (margins carry the roads —
   interior seams are just two neighboring half-roads, the game's own chunk law) and stamps its
   authored slots; the ground slab reads as a flat `plate` so the ~934k-column composite keeps a
   building-only localCols. Aim proxies are per-stamp + one ground plate — the zone's own tops. */
const emitZone = (t, m) => { const { M, TILE } = BLK, z = m.zone;
  const R = z.r1 - z.r0 + 1, C = z.c1 - z.c0 + 1, W2 = C * TILE, D2 = R * TILE;
  return layoutFetch(z.src || 'city_layout.json').then(L => {
    const blks = L.blocks.filter(b => b.row >= z.r0 && b.row <= z.r1 && b.col >= z.c0 && b.col <= z.c1);
    const stName = s => (s.stasset || s.type + '.stasset').split('/').pop().replace(/\.stasset$/, '');   // layout stores paths — basename is the fetch name
    const names = new Set(['empty_land']);
    blks.forEach(b => b.buildings.forEach(s => names.add(stName(s))));
    return Promise.all([...names].map(n => stassetFetch(n).then(f => [n, f]))).then(es => {
      const files = Object.fromEntries(es);
      const H = 2 + Math.max(...[...names].map(n => files[n].H));
      const vox = new Uint16Array(W2 * H * D2);
      const set = (x, zz, id) => { vox[x + W2 * (H * zz)] = vox[x + W2 * (1 + H * zz)] = id; };
      const hAt = (r, c) => (r >= 0 && r < L.hSeamRows.length && c >= 0 && c < L.hSeamRows[r].cells.length) ? L.hSeamRows[r].cells[c] : 'road';
      const vAt = (r, c) => (r >= 0 && r < L.vSeamRows.length && c >= 0 && c < L.vSeamRows[r].cells.length) ? L.vSeamRows[r].cells[c] : 'road';
      const stamped = [];
      blks.forEach(b => { const X0 = (b.col - z.c0) * TILE, Z0 = (b.row - z.r0) * TILE;
        const seams = { n: hAt(b.row - 1, b.col), e: vAt(b.row, b.col), s: hAt(b.row, b.col), w: vAt(b.row, b.col - 1) },
          slots = {}, types = new Set(); b.buildings.forEach(s => { slots[s.slot] = stName(s); types.add(s.type); });
        const lanes = (types.has('alley_lane') ? 10 : 0) | (types.has('alley_lane_ns') ? 5 : 0);   // a lane slot IS the cut — the stasset is an empty placeholder; the mouth+cross-section draw in the ground recipe
        fillBlockGround(set, X0, Z0, seams, lanes);
        stamped.push(...stampSlots(vox, W2, H, files, slots, X0, Z0)); });
      return emitVox(t, m, W2, H, D2, vox).then(miniG => {
        if (miniG) { const mn = miniG.userData.mini, vs = mn.vs;
          miniG.add(hitProxy(W2 * vs, 2 * vs, D2 * vs, 0, vs, 0));               // ground plate — roads/lots aim at slab top
          stamped.forEach(s => { const cx = (Math.min(mn.fx(s.x), mn.fx(s.x + s.w - 1)) + Math.max(mn.fx(s.x), mn.fx(s.x + s.w - 1))) / 2;
            miniG.add(hitProxy(s.w * vs, (s.h + 2) * vs, s.d * vs, cx, (s.h + 2) * vs / 2, (s.z + s.d / 2 - D2 / 2) * vs)); }); }
        return miniG; }); }); })
    .catch(e => console.warn('CONSTRUCT — zone emit failed:', m.name, e)); };
/* emitVox — one volume's instances + tops + mini rec. The shared body for single mounts,
   block composites, and zones: `m` supplies name/fill/x/z/flip; the grid is already assembled.
   Async — a zone's 100M-cell scan yields to the loop (chunked cooperative scan). Two culls:
   interior-bottom voxels (y=0 under full cover — their only exposed face is the model's unseen
   underside) and, with m.plate, flat [0,1] columns skip localCols (the ground plate writes its
   own rotated-rect tops — building columns stay per-column for spin/slice fidelity). */
const emitVox = async (t, m, W, H, D, vox) => {
    const vs = m._vs || (m.fill ? (m.fill * t.spec.size) / Math.max(W, D) : BLDG_VS * t.spec.scale);   // fill: span = fill × table size (_vs pins a core to its ring's scale)
    const mgen = t._mountGen, ox = m.x || 0, oz = m.z || 0;                          // mount generation at entry — staleness test for mid-emit swaps
    const mirX = m.flip !== false;                                       // .stasset grids are authored under Unity truth —
    const fx = x => mirX ? (W - 1 - x) - W / 2 + .5 : x - W / 2 + .5;      // the editor's UNITY VIEW is scale.x=-1, so a right-handed render mirrors x to match (opt out with flip:false)
    const at = (x, y, z) => (x < 0 || y < 0 || z < 0 || x >= W || y >= H || z >= D) ? 0 : vox[x + W * (y + H * z)];
    const surf = (x, y, z) => !(at(x-1,y,z) && at(x+1,y,z) && at(x,y-1,z) && at(x,y+1,z) && at(x,y,z-1) && at(x,y,z+1));
    const under = (x, z) => at(x-1,0,z) && at(x+1,0,z) && at(x,0,z-1) && at(x,0,z+1) && at(x,1,z);   // interior-bottom: exposed face is only the unseen underside
    const tick = () => new Promise(r => setTimeout(r));
    let ng = 0, gx0 = W, gx1 = -1, gz0 = D, gz1 = -1, gy1 = -1;
    const colYs = new Array(W * D);                                      // per-column voxel ys — slice/rotation-aware tops need the list
    for (let y = 0; y < H; y++) { if (!(y & 15)) await tick();
      for (let z = 0; z < D; z++) for (let x = 0; x < W; x++) {
      const id = at(x, y, z); if (!id) continue;
      if (x < gx0) gx0 = x; if (x > gx1) gx1 = x; if (z < gz0) gz0 = z; if (z > gz1) gz1 = z; if (y > gy1) gy1 = y;
      (colYs[x + W * z] = colYs[x + W * z] || []).push(y);
      if (surf(x, y, z) && !(y === 0 && under(x, z))) ng++;
    } }
    if (t._mountGen !== mgen) return null;                                         // swapped mid-scan — attaching now would ghost over the new model
    const miniG = new THREE.Group(); miniG.position.set(ox, tableTop(t), oz); t.g.add(miniG);   // fresh top — the boot lift-restore may land mid-emit
    const geo = new THREE.BoxGeometry(1, 1, 1);
    const clipMin = { value: new THREE.Vector3(-1e9, -1e9, -1e9) }, clipMax = { value: new THREE.Vector3(1e9, 1e9, 1e9) };
    const mkClipMat = base => { const m = base.clone();                  // per-model clone → own slice uniforms
      m.onBeforeCompile = sh => { sh.uniforms.uVoxClipMin = clipMin; sh.uniforms.uVoxClipMax = clipMax;
        sh.vertexShader = sh.vertexShader
          .replace('#include <common>', '#include <common>\nvarying vec3 vVoxOrigin;')
          .replace('#include <begin_vertex>', '#include <begin_vertex>\nvVoxOrigin = vec3(instanceMatrix[3]);');
        sh.fragmentShader = sh.fragmentShader
          .replace('#include <common>', '#include <common>\nvarying vec3 vVoxOrigin;\nuniform vec3 uVoxClipMin;\nuniform vec3 uVoxClipMax;')
          .replace('#include <clipping_planes_fragment>', `#include <clipping_planes_fragment>
            if (vVoxOrigin.x < uVoxClipMin.x || vVoxOrigin.y < uVoxClipMin.y || vVoxOrigin.z < uVoxClipMin.z ||
                vVoxOrigin.x >= uVoxClipMax.x || vVoxOrigin.y >= uVoxClipMax.y || vVoxOrigin.z >= uVoxClipMax.z) discard;`); };
      return m; };
    const imO = new THREE.InstancedMesh(geo, mkClipMat(new THREE.MeshLambertMaterial()), ng);
    const imG = new THREE.InstancedMesh(geo, mkClipMat(window.__glassMat), ng);
    const M = new THREE.Matrix4(), C = new THREE.Color(), dflt = new THREE.Color(.55, .55, .55);
    let no = 0, gi = 0;
    for (let y = 0; y < H; y++) { if (!(y & 15)) await tick();
      for (let z = 0; z < D; z++) for (let x = 0; x < W; x++) {
      const id = at(x, y, z); if (!id) continue;
      if (!surf(x, y, z) || (y === 0 && under(x, z))) continue;
      M.makeScale(vs, vs, vs).setPosition(fx(x) * vs, (y + .5) * vs, (z - D / 2 + .5) * vs);
      const col = BLDG_MAT[id]; C.setRGB(col ? col[0] : .55, col ? col[1] : .55, col ? col[2] : .55);
      if (BLDG_GLASS.has(id)) { imG.setMatrixAt(gi, M); imG.setColorAt(gi, C); gi++; }
      else { imO.setMatrixAt(no, M); imO.setColorAt(no, C); no++; }
    } }
    imO.count = no; imG.count = gi; imO.userData.miniVox = imG.userData.miniVox = true;   // flag the miniature's instanced walls — rayPruned skips them whole (426K matrix tests per ray)
    if (gi) miniG.add(imG); miniG.add(imO);
    const px0 = (Math.min(fx(gx0), fx(gx1)) - .5) * vs, px1 = (Math.max(fx(gx0), fx(gx1)) + .5) * vs,
      pz0 = (gz0 - D / 2) * vs, pz1 = (gz1 + 1 - D / 2) * vs;
    imO.boundingSphere = imG.boundingSphere = new THREE.Sphere(             // O(1) bounds — the lazy compute iterates
      new THREE.Vector3((px0 + px1) / 2, (gy1 + 1) * vs / 2, (pz0 + pz1) / 2),  // every instance on first frustum test
      Math.hypot(px1 - px0, (gy1 + 1) * vs, pz1 - pz0) / 2 + vs);           // (1.36M matrices = a guaranteed first-look hitch)
    if (gy1 >= 0 && !m.noProxy) {                                         // aim proxy = occupied bounds — the dot rests ON the model
      miniG.add(hitProxy(px1 - px0, (gy1 + 1) * vs, pz1 - pz0, (px0 + px1) / 2, (gy1 + 1) * vs / 2, (pz0 + pz1) / 2)); }
    const localCols = [];                                                // grid columns → local offsets + the ys list
    for (let z = 0; z < D; z++) for (let x = 0; x < W; x++) {
      const ys = colYs[x + W * z]; if (!ys) continue;
      if (m.plate && ys.length === 2 && ys[0] === 0 && ys[1] === 1) continue;   // flat ground — the plate writes these tops
      localCols.push({ x, z, dx: fx(x) * vs, dz: (z - D / 2 + .5) * vs, ys }); }
    miniG.userData.mini = { name: m.name, dims: [W, H, D], vox: ng, scale: vs / BLDG_VS, fill: m.fill, vs, _blkM: m._blkM, static: !!m.static,
      localCols, plate: m.plate ? { x0: px0, x1: px1, z0: pz0, z1: pz1, y: 1 } : null,
      written: new Set(), spin: 0, topTheta: 0, fx, mirX, axis: 'y',
      slice: { x: [0, W - 1], y: [0, H - 1], z: [0, D - 1] }, clipMin, clipMax };
    if (t._mountGen !== mgen) { t.g.remove(miniG); return null; }                  // swapped mid-emit — the unflagged group survived mountModel's cleanup; detach before it flags + writes tops
    miniSyncSlice(miniG); miniSyncTops(t, miniG);                        // the model joins the host's surface truth
    miniG.position.y = tableTop(t);                                      // settle on the CURRENT top — entry's capture predates a mid-emit lift-restore; the mount lands on its pedestal's real height
    miniSettle(t);                                                       // tops are truth — guests re-derive under the new map (a mount/swap left them on the old)
    return miniG;
  };
/* miniSyncSlice — the editor's xyz window, same contract: a per-axis index range shader-clips
   instances whose origins fall outside. Clip bounds are MODEL-LOCAL (mirrored x handled via fx). */
const miniSyncSlice = mdl => { const mn = mdl.userData.mini, s = mn.slice, vs = mn.vs, D = mn.dims[2];
  const lx0 = Math.min(mn.fx(s.x[0]), mn.fx(s.x[1])), lx1 = Math.max(mn.fx(s.x[0]), mn.fx(s.x[1]));
  mn.clipMin.value.set((lx0 - .5) * vs, s.y[0] * vs, (s.z[0] - D / 2) * vs);
  mn.clipMax.value.set((lx1 + .5) * vs, (s.y[1] + 1) * vs, (s.z[1] + 1 - D / 2) * vs); };
/* miniSyncTops — rewrite the model's host tops under the CURRENT rotation + slice: written keys
   revert to felt, then surviving columns re-project (rotated offsets, top = tallest y in window).
   A `plate` model (zones/block composites — full-coverage ground slab) writes its flat top as a
   rotated rect of columns first; per-column localCols only carry the raised content above it. */
const miniSyncTops = (t, mdl) => { const mn = mdl.userData.mini, s = mn.slice, D = mn.dims[2];
  mn.written.forEach(k => t.rec.tops.set(k, tableTop(t))); mn.written.clear();
  const th = mdl.rotation.y, c = Math.cos(th), sn = Math.sin(th), ox = mdl.position.x, oz = mdl.position.z;
  if (mn.plate && s.y[0] <= mn.plate.y && s.y[1] >= mn.plate.y) {          // ground plate survives the y window → fill its rect
    const sx0 = Math.min(mn.fx(s.x[0]), mn.fx(s.x[1])) - .5, sx1 = Math.max(mn.fx(s.x[0]), mn.fx(s.x[1])) + .5,   // slice window, local
      rx0 = Math.max(mn.plate.x0, sx0 * mn.vs), rx1 = Math.min(mn.plate.x1, sx1 * mn.vs),
      rz0 = Math.max(mn.plate.z0, (s.z[0] - D / 2) * mn.vs), rz1 = Math.min(mn.plate.z1, (s.z[1] + 1 - D / 2) * mn.vs);
    if (rx0 < rx1 && rz0 < rz1) { const v = tableTop(t) + (mn.plate.y + 1) * mn.vs,
      cs = [[rx0, rz0], [rx1, rz0], [rx0, rz1], [rx1, rz1]].map(([dx, dz]) => [ox + dx * c - dz * sn, oz + dx * sn + dz * c]),
      bx0 = Math.min(...cs.map(p => p[0])), bx1 = Math.max(...cs.map(p => p[0])),
      bz0 = Math.min(...cs.map(p => p[1])), bz1 = Math.max(...cs.map(p => p[1]));
      for (let ix = Math.round(bx0 * 200); ix <= Math.round(bx1 * 200); ix++)
        for (let iz = Math.round(bz0 * 200); iz <= Math.round(bz1 * 200); iz++) {
          const dx = (ix / 200 - ox) * c + (iz / 200 - oz) * sn, dz = -(ix / 200 - ox) * sn + (iz / 200 - oz) * c;   // world → local
          if (dx >= rx0 && dx <= rx1 && dz >= rz0 && dz <= rz1) {
            const k = TOPK(ix, iz); if (v > (t.rec.tops.get(k) ?? -1e9)) { t.rec.tops.set(k, v); mn.written.add(k); } } } } }
  for (const col of mn.localCols) {
    if (col.x < s.x[0] || col.x > s.x[1] || col.z < s.z[0] || col.z > s.z[1]) continue;
    let ym = -1; for (const y of col.ys) if (y >= s.y[0] && y <= s.y[1] && y > ym) ym = y;
    if (ym < 0) continue;
    const lx = ox + col.dx * c - col.dz * sn, lz = oz + col.dx * sn + col.dz * c;
    const k = TOPK(Math.round(lx * 200), Math.round(lz * 200)), v = tableTop(t) + (ym + 1) * mn.vs;
    if (v > (t.rec.tops.get(k) ?? -1e9)) t.rec.tops.set(k, v);           // several model cells share a 5mm column — keep the true max
    mn.written.add(k); } };
/* miniSpinSet — the turntable: rate in rad/s, toggling the active rate stops it. Tops re-project
   every >.02 rad during spin (the render loop owns the integration) and exactly on stop. */
const miniSpinSet = rate => { const t = padTable(), mdl = t && t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  if (!mdl) return; const mn = mdl.userData.mini;
  mn.spin = (mn.spin === rate) ? 0 : rate;
  if (!mn.spin) { mn.topTheta = mdl.rotation.y; miniSyncTops(t, mdl); miniSettle(t); } };   // exact tops on stop → riders re-seat
/* miniRotReset — home the easel: stop any spin, face the model at rotY 0, exact tops re-sync. */
const miniRotReset = () => { const t = padTable(), mdl = t && t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  if (!mdl) return; const mn = mdl.userData.mini;
  mn.spin = 0; mdl.rotation.y = 0; mn.topTheta = 0; miniSyncTops(t, mdl); miniSettle(t); };
/* miniSettle — tops-change law: after an exact sync, anything standing above the slab inside
   the footprint re-derives its seat — stale reads (lagged spin tops, a swap, a cut) leave
   riders embedded or floating; the measured map is the truth, settle to it. */
const miniSettle = t => { const H = tableTop(t);
  PLACED.forEach(q => { if (q === t.rec || q === held || q.mount || q.g.parent !== scene) return;
    if (inB(q, t.rec.bbox) && q.y0 > H + .005) {
      const s = placeSurf(q.x, q.z, new Set([q]), { surfOnly: q.surf });
      if (Math.abs(s - q.y0) > 1e-4) { q.y0 = s; q.g.position.y = s; } } }); };
/* sliceStep — nudge the active slice bound by dim/16 steps (min 1), clamped against its twin. */
const miniSliceStep = (bound, dir) => { const t = padTable(), mdl = t && t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  if (!mdl || t.spec.noSlice) return; const mn = mdl.userData.mini, ax = mn.axis || 'y', dim = mn.dims['xyz'.indexOf(ax)], s = mn.slice[ax],
    st = Math.max(1, dim >> 4), i = bound === 'min' ? 0 : 1;
  s[i] = Math.max(i ? s[0] : 0, Math.min(i ? dim - 1 : s[1], s[i] + dir * st));
  miniSyncSlice(mdl); miniSyncTops(t, mdl); miniSettle(t); };
const miniSliceReset = () => { const t = padTable(), mdl = t && t.g.children.find(c => c.userData.mini && !c.userData.mini.static);
  if (!mdl || t.spec.noSlice) return; const mn = mdl.userData.mini;
  mn.slice = { x: [0, mn.dims[0] - 1], y: [0, mn.dims[1] - 1], z: [0, mn.dims[2] - 1] };
  miniSyncSlice(mdl); miniSyncTops(t, mdl); miniSettle(t); };
/* mountModel — the easel's swap verb: unmount the old mini (its tops columns revert to felt),
   mount the catalog spec. models[] always reads as "what's on the table now". */
const mountModel = (t, mspec) => {
  t._mountGen = (t._mountGen || 0) + 1;                                                // generation — an in-flight emit of a swapped-out model bails instead of ghosting a second composite
  t.g.children.filter(c => c.userData.mini && !c.userData.mini.static).forEach(c => {  // static minis (the street ring) survive every swap — cars stay parked through it
    (c.userData.mini.written || []).forEach(k => t.rec.tops.set(k, tableTop(t)));
    for (let i = PLACED.length - 1; i >= 0; i--) if (PLACED[i].g && PLACED[i].g.parent === c) PLACED.splice(i, 1);   // parked overlays die with their model — no ghost hosts
    c.children.forEach(ch => { ch.traverse ? ch.traverse(o => { o.dispose && o.dispose(); o.material && o.material.dispose && o.material.dispose(); })
      : (ch.dispose && ch.dispose(), ch.material && ch.material.dispose && ch.material.dispose()); });              // traverse — overlay recs carry their own meshes
    t.g.remove(c); });
  t.g.children.filter(c => c.userData.mini && c.userData.mini.static).forEach(c => miniSyncTops(t, c));   // the core's revert dropped overlapping keys to felt — restore the ring's tops underneath
  t.spec.models = [mspec];
  return (t._mountP = (mspec.zone ? emitZone : mspec.slots ? (t._ringG ? emitCore : emitBlock) : emitBldg)(t, mspec));   // tracked — boot awaits it before extras/fixpoint read tops
};
MANIFEST.tables.forEach(spec => { const t = emitTable(spec);
  const savedLift = +localStorage.getItem('dvm_lift:' + spec.id);           // the pistons remember their reach
  if (savedLift) t._savedLift = savedLift;                                  // applied in boot AFTER overrides — a rider's saved seat precedes its ride (eval-time calls are still TDZ landmines: liftRiders reaches late-bound helpers)
  const saved = localStorage.getItem('dvm_model:' + spec.id), cat = spec.catalog || spec.models,
    pick = cat.find(m => m.name === saved) || spec.models[0];
  if (spec.ring) t._mountP = emitRing(t, pick.fill).then(ringG => {               // the street exhibit is the table's — ring first, cars on it, then the interior core
    if (!ringG) return null;
    t._ringG = ringG;
    return mountParked(t, ringG, spec.parked || [])
      .then(() => mountModel(t, pick)); });                                     // mountModel rewrites t._mountP to the core emit — still the last promise standing
  else mountModel(t, pick); });
