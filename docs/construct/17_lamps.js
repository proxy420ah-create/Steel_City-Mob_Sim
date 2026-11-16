'use strict';
/* construct/17_lamps.js — EXHIBIT LIGHTS: a wall switch by the door dims the house rig
   (the piercing white hemi/key) and lights a commission pendant over each table.
   Pendants are scenery — parented to the table group (they ride drags + lifts, and stay
   out of PLACED so no bbox can poison the drop scanner). State persists in dvm_lights;
   probe hook __lamps (on/set). */

const LAMPS = [];                                                        // { t, spot, glow } per table
let lampsOn = localStorage.getItem('dvm_lights') !== '0';                // default ON — the exhibit look is the feature

/* pendant — commission_pendant (green-dome banker fixture), ~.30m tall, origin = dome bottom.
   Local y .62 inside the table group: canopy tops out ~.92 — just under the 1.05 wall line. */
const PEND_Y = .62, SPOT_I = 14, GLOW_I = .5;
TABLES.forEach(t => {
  const pg = new THREE.Group(); pg.position.set(0, PEND_Y, 0); t.g.add(pg);
  const spot = new THREE.SpotLight(0xffe2b0, 0, 3.5, .78, .6, 1);
  spot.position.set(0, .27, 0); pg.add(spot); spot.target.position.set(0, -.4, 0); pg.add(spot.target);
  const glow = new THREE.PointLight(0xcfffb0, 0, .8, 1);                 // the dome's own glass-glow
  glow.position.set(0, .08, 0); pg.add(glow);
  LAMPS.push({ t, spot, glow });
  fetch('/voxel/commission_pendant').then(r => r.json()).then(m => {
    const vs = m.voxelSize || .012, dcx = m.dims[0] / 2 - .5, dcz = m.dims[2] / 2 - .5;
    window.voxEmit(m, (M, v) => M.makeScale(vs, vs, vs).setPosition(
      (v[0] - dcx) * vs, (v[1] + .5) * vs, (v[2] - dcz) * vs)).forEach(mm => pg.add(mm));
  }).catch(e => console.warn('CONSTRUCT — pendant emit failed:', e));
});

/* the switch — a little plate + nub on the annex face of the interior wall, just north
   of the door jamb. Scenery group, not a rec — it can't be carried or re-placed. */
const SW_POS = { x: -3.06, y: .5, z: -1.6 };
const swG = new THREE.Group(); swG.position.set(SW_POS.x, SW_POS.y, SW_POS.z); scene.add(swG);
const swPlate = new THREE.Mesh(new THREE.BoxGeometry(.02, .11, .07),
  new THREE.MeshLambertMaterial({ color: 0xd8d2c4 })); swG.add(swPlate);
const swRim = new THREE.Mesh(new THREE.BoxGeometry(.022, .015, .05),
  new THREE.MeshLambertMaterial({ color: 0x3a3a4a })); swRim.position.y = -.02; swG.add(swRim);
const swNub = new THREE.Mesh(new THREE.BoxGeometry(.028, .035, .018),
  new THREE.MeshLambertMaterial({ color: 0xe8d9b0 })); swNub.position.y = .015; swG.add(swNub);
const swPip = new THREE.Mesh(new THREE.BoxGeometry(.022, .012, .012),
  new THREE.MeshBasicMaterial({ color: 0x7fd4ff })); swPip.position.y = .04; swG.add(swPip);   // lit tell

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
  swNub.rotation.x = lampsOn ? -.55 : .55;                               // the nub flips — the state tell
  swPip.material.color.setHex(lampsOn ? 0x7fd4ff : 0x3a3a4a);
  localStorage.setItem('dvm_lights', lampsOn ? '1' : '0');
};
INTERACT.push({ x: SW_POS.x, z: SW_POS.z, r: .8, isBtn: true,
  label: () => 'E — ' + (lampsOn ? 'LIGHTS UP' : 'EXHIBIT LIGHTS'),
  fn: () => { lampsOn = !lampsOn; lampsApply(); } });
lampsApply();

window.__lamps = { on: () => lampsOn, set: b => { lampsOn = !!b; lampsApply(); } };
