// _pose_check.mjs — pooled-evalPose correctness: idle finite + standing height, gait animates
// while walking, head still tracks a gaze tag. Behavior-parity gate for the alloc diet.
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp();
await evl(`localStorage.removeItem('dvm_followers'); sessionStorage.clear()`);
check('construct live', !!(await evl(`typeof emitChar === 'function'`)));
await evl(`emitChar({ char: 'CHAIR1', order: 'wait', x: .8, z: .4, yaw: 0 })`);
await sleep(1200);
check('rig emitted', await evl('followers.length === 1'));
const idle = await evl(`(() => { const f = followers[0]; let ok = true, mn = 1e9, mx = -1e9;
  for (let i = 0; i < f.N * 3; i++) { const v = f.bufB[i]; if (!isFinite(v)) ok = false; if (i % 3 === 1) { mn = Math.min(mn, v); mx = Math.max(mx, v); } }
  return { ok, mn: +mn.toFixed(1), mx: +mx.toFixed(1) }; })()`);
check('idle pose finite + standing height', idle.ok && idle.mx > 50, JSON.stringify(idle));
await evl(`window.__aim.mark({x: followers[0].pos.x - 1.2, y: 0, z: followers[0].pos.z}); followers[0].setOrder('goto')`);
const pos1 = await evl('followers[0].pos.x');
const snap1 = await evl('Array.from(followers[0].eyeIdx.slice(0, 8).map(i => followers[0].bufB[3 * i + 1]))');
await sleep(400);
const pos2 = await evl('followers[0].pos.x');
const snap2 = await evl('Array.from(followers[0].eyeIdx.slice(0, 8).map(i => followers[0].bufB[3 * i + 1]))');
console.log('pos', pos1, '->', pos2, 'state', await evl('followers[0].state'), 'order', await evl('followers[0].order'));
check('body walks toward mark', Math.abs(pos2 - pos1) > .2, pos1 + ' -> ' + pos2);
check('gait animates while walking', snap1.some((v, i) => Math.abs(v - snap2[i]) > .001), JSON.stringify(snap1.map(v => +v.toFixed(3))) + ' vs ' + JSON.stringify(snap2.map(v => +v.toFixed(3))));
check('all finite mid-gait', await evl('followers[0].bufB.every(isFinite)'));
const head1 = await evl('followers[0].bufB[3 * followers[0].eyeIdx[0] + 2]');
await evl(`followers[0].setOrder('wait'); followers[0].gazeT = new THREE.Vector3(pos.x + 1, .6, pos.z)`);
await sleep(300);
const head2 = await evl('followers[0].bufB[3 * followers[0].eyeIdx[0] + 2]');
check('head tracks gaze target', Math.abs(head1 - head2) > .2, head1.toFixed(2) + ' -> ' + head2.toFixed(2));
await evl(`localStorage.removeItem('dvm_followers')`);
finish();
