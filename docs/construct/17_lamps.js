'use strict';
/* construct/17_lamps.js — EXHIBIT LIGHTS: a wall switch by the door dims the house rig
   (the piercing white hemi/key) and lights a commission pendant over each table.
   Pendants are scenery — parented to the table group (they ride drags + lifts, and stay
   out of PLACED so no bbox can poison the drop scanner). State persists in dvm_lights;
   probe hook __lamps (on/set). */

const LAMPS = [];                                                        // { spot, glow, fixture } per exhibit
let lampsOn = localStorage.getItem('dvm_lights') === '1';                // default OFF — bright construct; the switch dims on demand
let shadesOn = localStorage.getItem('dvm_shades') !== '0';               // default ON — fixtures visible; independent of the light

/* pendant — commission_pendant (green-dome banker fixture), ~.30m tall, origin = dome bottom.
   All fixtures ceiling-mount at local y 1.2: canopy ~1.5 kisses the 1.55 cornice, exhibits get
   ~.7–1.2 of clearance. The wider cone pools across the whole slab/disc from the longer throw. */
const PEND_Y = 1.2, SPOT_I = 16, GLOW_I = .5;
const hangPendant = (parent, drop = -.4, y = PEND_Y) => {
  const pg = new THREE.Group(); pg.position.set(0, y, 0); parent.add(pg);
  const spot = new THREE.SpotLight(0xffe2b0, 0, 4, .85, .65, 1);
  spot.position.set(0, .27, 0); pg.add(spot); spot.target.position.set(0, drop, 0); pg.add(spot.target);
  const glow = new THREE.PointLight(0xcfffb0, 0, .8, 1);                 // the dome's own glass-glow
  glow.position.set(0, .08, 0); pg.add(glow);
  const fixture = new THREE.Group(); fixture.visible = shadesOn; pg.add(fixture);   // the shade — hides alone; lights keep emitting (three.js skips invisible parents when collecting lights)
  const e = { pg, spot, glow, fixture }; LAMPS.push(e);
  fetch('/voxel/commission_pendant').then(r => r.json()).then(m => {
    const vs = m.voxelSize || .012, dcx = m.dims[0] / 2 - .5, dcz = m.dims[2] / 2 - .5;
    window.voxEmit(m, (M, v) => M.makeScale(vs, vs, vs).setPosition(
      (v[0] - dcx) * vs, (v[1] + .5) * vs, (v[2] - dcz) * vs)).forEach(mm => fixture.add(mm));
  }).catch(e => console.warn('CONSTRUCT — pendant emit failed:', e));
  return e;
};
/* the attached-light lifecycle — a pendant is a property OF THE REC: it parents to p.g so
   drags/lifts/deletes carry it, pendWant persists through dvm_place, and P in place mode
   configures it while placing or moving (BO's law: tables come with one, optionally). */
const pendAttach = p => { if (p.pend) return p.pend;
  p.pend = hangPendant(p.g, (p.topY ?? .26) - PEND_Y);                     // aim lands on the rec's own top
  p.pendWant = 1; return p.pend; };
const pendDetach = p => { if (!p.pend) return;
  const i = LAMPS.indexOf(p.pend); if (i >= 0) LAMPS.splice(i, 1);
  p.g.remove(p.pend.pg); p.pend = null; p.pendWant = 0; };
const pendSet = (p, on) => { if (on) pendAttach(p); else pendDetach(p); };
PLACED.forEach(p => { if (p.pendWant) pendAttach(p); });                 // boot sweep — everything already emitted (tables etc.) dresses
hangPendant(spinG, -1.2);                                                // the rotator's museum spot — spinG stays put while the tall touring turns under it; aim lands on the deck

/* the switch — a wide dark plate with TWO proud rockers on the annex face of the interior wall:
   LIGHTS (north nub) dims the room; SHADES (south nub) shows/hides the pendant fixtures alone —
   light on + shades off = dark room, pools on the exhibits, invisible sources. 1.5cm standoff
   kills the coplanar-face washout; the dark plate reads against the cream wall. Scenery group,
   not a rec — it can't be carried or re-placed. */
const SW_POS = { x: -3.04, y: .5, z: -1.6 };
const swG = new THREE.Group(); swG.position.set(SW_POS.x, SW_POS.y, SW_POS.z); scene.add(swG);
const swPlate = new THREE.Mesh(new THREE.BoxGeometry(.03, .11, .26),
  new THREE.MeshLambertMaterial({ color: 0x2e3240 })); swG.add(swPlate);
const swNub = new THREE.Mesh(new THREE.BoxGeometry(.05, .034, .02),
  new THREE.MeshLambertMaterial({ color: 0xe8d9b0 })); swNub.position.set(-.02, .012, -.07); swG.add(swNub);
const swPip = new THREE.Mesh(new THREE.BoxGeometry(.022, .012, .014),
  new THREE.MeshBasicMaterial({ color: 0x7fd4ff })); swPip.position.set(-.02, .04, -.07); swG.add(swPip); // lit tell
const swShade = new THREE.Mesh(new THREE.BoxGeometry(.05, .034, .02),
  new THREE.MeshLambertMaterial({ color: 0xe8d9b0 })); swShade.position.set(-.02, .012, .07); swG.add(swShade);
const swShadePip = new THREE.Mesh(new THREE.BoxGeometry(.022, .012, .014),
  new THREE.MeshBasicMaterial({ color: 0xffcf6a })); swShadePip.position.set(-.02, .04, .07); swG.add(swShadePip); // amber tell

/* the dim law — the "piercing white" is the unlit VOID itself (background + fog + the shared
   void-colored floor/wall material), not the lights. Dim the void to a dark museum depth, drop
   the house rig to a navigable dusk, and the pendants' warm pools carry the room. */
const VOID_DIM = 0x13141c;
const lampsApply = () => {
  scene.background.setHex(lampsOn ? VOID_DIM : VOID);
  scene.fog.color.setHex(lampsOn ? VOID_DIM : VOID);
  floorMat.color.setHex(lampsOn ? VOID_DIM : VOID);                       // walls share floorMat — the whole shell follows
  hemi.intensity = lampsOn ? .22 : 1.2;
  key.intensity = lampsOn ? .5 : 2.4;
  LAMPS.forEach(l => { l.spot.intensity = lampsOn ? SPOT_I : 0; l.glow.intensity = lampsOn ? GLOW_I : 0; });
  swNub.rotation.x = lampsOn ? -.4 : .4;                                 // the nub flips — the state tell
  swPip.material.color.setHex(lampsOn ? 0x7fd4ff : 0x3a3a4a);
  localStorage.setItem('dvm_lights', lampsOn ? '1' : '0');
};
const shadesApply = () => {                                              // fixture meshes only — the lights are not touched
  LAMPS.forEach(l => { l.fixture.visible = shadesOn; });
  swShade.rotation.x = shadesOn ? -.4 : .4;
  swShadePip.material.color.setHex(shadesOn ? 0xffcf6a : 0x3a3a4a);
  localStorage.setItem('dvm_shades', shadesOn ? '1' : '0');
};
INTERACT.push({ x: SW_POS.x, z: SW_POS.z - .07, r: .3, isBtn: true,      // north rocker — LIGHTS
  label: () => 'E — ' + (lampsOn ? 'LIGHTS UP' : 'EXHIBIT LIGHTS'),
  fn: () => { lampsOn = !lampsOn; lampsApply(); } });
INTERACT.push({ x: SW_POS.x, z: SW_POS.z + .07, r: .3, isBtn: true,      // south rocker — SHADES (nearest-wins disambiguates)
  label: () => 'E — ' + (shadesOn ? 'HIDE SHADES' : 'SHOW SHADES'),
  fn: () => { shadesOn = !shadesOn; shadesApply(); } });
lampsApply(); shadesApply();

window.__lamps = { on: () => lampsOn, set: b => { lampsOn = !!b; lampsApply(); },
  shades: () => shadesOn, setShades: b => { shadesOn = !!b; shadesApply(); },
  fixtureVisible: () => LAMPS.map(l => l.fixture.visible),
  pendSet, pendAttach, pendDetach, count: () => LAMPS.length };
