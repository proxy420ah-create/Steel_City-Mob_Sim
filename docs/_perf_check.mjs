// _perf_check.mjs — work-per-frame instrumentation: wrap evalPose/upload/renderer.render
// once, then zero counters per config and read calls+ms over a 3s window.
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp();
await evl(`localStorage.removeItem('dvm_followers'); sessionStorage.clear()`);

const instrument = () => evl(`
window.__perf = { evals: 0, evalMs: 0, uploads: 0, uploadMs: 0, renders: 0, renderMs: 0 };
followers.forEach(f => { if (f.__wrapped) return; f.__wrapped = true;
  const e = f.E.evalPose, u = f.upload;
  f.E = { N: f.E.N, evalPose: (...a) => { const t = performance.now(); const r = e(...a); __perf.evalMs += performance.now() - t; __perf.evals++; return r; } };
  f.upload = (...a) => { const t = performance.now(); const r = u(...a); __perf.uploadMs += performance.now() - t; __perf.uploads++; return r; }; });
if (!window.__rWrapped) { window.__rWrapped = true; const R = renderer.render.bind(renderer);
  renderer.render = (...a) => { const t = performance.now(); const r = R(...a); __perf.renderMs += performance.now() - t; __perf.renders++; return r; }; } true`);

const read = async label => { await evl(`__perf = { evals: 0, evalMs: 0, uploads: 0, uploadMs: 0, renders: 0, renderMs: 0 }`);
  await sleep(3000); const d = JSON.parse(await evl(`JSON.stringify(__perf)`)), s = 3;
  console.log(label.padEnd(9), 'evalPose:', d.evals, `(${(d.evalMs / s).toFixed(0)}ms/s)`,
    'uploads:', d.uploads, `(${(d.uploadMs / s).toFixed(0)}ms/s)`,
    'renders:', d.renders, `(${(d.renderMs / s).toFixed(0)}ms/s)`); return d; };

check('construct live', !!(await evl(`typeof emitChar === 'function'`)));
await evl(`emitChar({ char: 'CHAIR1', order: 'follow', x: pos.x + .8, z: pos.z + .4, yaw: 0 })`);
await evl(`emitChar({ char: 'DESIGNER1', order: 'follow', x: pos.x - .8, z: pos.z + .4, yaw: 0 })`);
await sleep(1500);
console.log('followers:', await evl(`followers.length`));
await instrument(); await read('2 NEAR');

await evl(`followers.forEach(f => { f.pos.x = pos.x + 12; f.pos.z = pos.z + 12; f.CH.position.set(f.pos.x, 0, f.pos.z); })`);
await read('2 FAR');

await evl(`followers.forEach(f => { f.pos.x = pos.x + .8; f.pos.z = pos.z + .4; f.CH.position.set(f.pos.x, 0, f.pos.z); }); window.__eyes.show()`);
await read('2 + POV');
await evl(`window.__eyes.hide(); localStorage.removeItem('dvm_followers')`);
finish();
