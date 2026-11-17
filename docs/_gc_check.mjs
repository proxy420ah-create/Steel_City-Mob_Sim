// _gc_check.mjs — prove/disprove the GC-hitch theory: sample rAF gaps + JS heap while
// two followers walk (simulated player movement). Heap sawtooth drops aligned with
// >25ms frames = allocation-driven GC pauses.
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp();
await evl(`localStorage.removeItem('dvm_followers'); sessionStorage.clear()`);
check('construct live', !!(await evl(`typeof emitChar === 'function' && typeof performance.memory === 'object'`)));

await evl(`emitChar({ char: 'CHAIR1', order: 'follow', x: pos.x + .8, z: pos.z + .4, yaw: 0 })`);
await evl(`emitChar({ char: 'DESIGNER1', order: 'follow', x: pos.x - .8, z: pos.z + .4, yaw: 0 })`);
await sleep(1500);

// oscillate the player so both bodies gait — the "walking around" state
const r = await evl(`new Promise(res => {
  const gaps = [], heap = []; let last = performance.now(), f = 0, dir = 1;
  const ox = pos.x;
  const tick = t => {
    const dt = t - last; last = t;
    if (dt > 25) gaps.push(+(t / 1000).toFixed(2) + 's:+' + dt.toFixed(0) + 'ms');
    pos.x = ox + Math.sin(f / 20) * 2; f++;
    if (f < 600) { if (!(f % 10)) heap.push(performance.memory.usedJSHeapSize / 1048576); requestAnimationFrame(tick); }
    else res({ gaps, heap });
  };
  requestAnimationFrame(tick);
})`);
console.log('long frames:', JSON.stringify(r.gaps));
console.log('heap MB (1/10 frames):', r.heap.map(h => h.toFixed(0)).join(' '));
check('data collected', r.heap.length > 40);
await evl(`localStorage.removeItem('dvm_followers')`);
finish();
