// _designer_char_check.mjs — DESIGNER1 rig end-to-end: emit through the real
// path, follower registry, eye-region POV anchor, teal outfit retint,
// agent possession layer (order + gaze).
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp();

// emit through the live pipeline — spec like a party handoff
await evl(`emitChar({ char:'DESIGNER1', order:'wait', yaw:0 })`);
await sleep(2500);

check('DESIGNER1 in follower roster', await evl(`followers.some(f=>f.spec.char==='DESIGNER1')`));
check('no emit error', await evl(`!window.__lastPartyErr`), await evl(`window.__lastPartyErr||''`));

// eye-region camera anchor (region 10 → eyeWorld → povCam)
const eye = await evl(`(() => { const f=followers.find(f=>f.spec.char==='DESIGNER1');
  const e=eyeWorld(f); return e&&isFinite(e.x)&&isFinite(e.y)&&isFinite(e.z)?[e.x,e.y,e.z]:null; })()`);
check('eyeWorld anchor finite', !!eye, eye && eye.map(n=>n.toFixed(2)).join(','));
check('eyeIdx populated', await evl(`followers.find(f=>f.spec.char==='DESIGNER1').eyeIdx.length>0`));
check('owns the eyes (povF)', await evl(`povF && povF.spec.char==='DESIGNER1'`));

// purple retint — instance color on a torso (region 3) voxel ≈ #bc8cff
// instanceColor stores LINEAR (THREE.Color sRGB→linear): #bc8cff ≈ (0.504,0.262,1.0)
const teal = await evl(`(() => { const f=followers.find(f=>f.spec.char==='DESIGNER1');
  const rg=f.rig.regions, i=f.rig.voxels.findIndex(v=>rg[v[0]+','+v[1]+','+v[2]]===3);
  if(i<0||!f.inst.instanceColor) return null;
  const c=f.inst.instanceColor; return [c.getX(i),c.getY(i),c.getZ(i)]; })()`);
check('torso renders DESIGNER purple', teal && Math.abs(teal[0]-0.504)<0.08 && Math.abs(teal[1]-0.262)<0.08 && Math.abs(teal[2]-1.0)<0.05,
  teal && 'linear ' + teal.map(n=>n.toFixed(3)).join(',') + ' (srgb ' + teal.map(n=>Math.round(Math.pow(n,1/2.2)*255)).join(',') + ')');

// agent possession layer — same orders, same tags
await evl(`window.__construct.agent.order('follow')`);
check('agent order applied', await evl(`followers.find(f=>f.spec.char==='DESIGNER1').order==='follow'`));
await evl(`window.__construct.agent.gaze(0.4,1.5,0.45)`);
check('agent gaze lands (gazeT)', await evl(`!!followers.find(f=>f.spec.char==='DESIGNER1').gazeT`));

finish();
