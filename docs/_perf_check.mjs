// _perf_check.mjs — steady-state loop budget probe. Boots ?perf, then simulates
// continuous mouse-look (yaw sweep) while a heavy mini spins — exactly the case
// the user reports as stuttery. Dumps __perfStats after ~8s.
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp({ q: 'perf' });
await sleep(4000);                                        // boot mounts settle
await evl(`__perf(1)`);                                   // reset counters post-boot
await evl(`(() => { const m = TABLES.map(t => t.g.children.find(c => c.userData.mini && !c.userData.mini.static)).find(Boolean);
  if (m) m.userData.mini.spin = .9;                       // spin the heaviest mounted composite
  window.__simT = setInterval(() => { yaw += .015; }, 16);   // ~57°/s pan — continuous look-around
  return m && m.userData.mini.localCols ? m.userData.mini.localCols.length : -1; })()`);
await sleep(8000);
const stats = await evl(`Object.fromEntries(Object.keys(__perfStats).map(k => { const s = __perfStats[k];
  return [k, { calls: s.n, avg: +(s.tot / s.n).toFixed(2), max: +s.max.toFixed(1), '>8ms': s.o8, '>33ms': s.o33 }]; }))`);
await evl(`clearInterval(__simT); TABLES.forEach(t => t.g.children.forEach(c => { if (c.userData.mini) c.userData.mini.spin = 0; }))`);
console.table ? console.table(stats) : console.log(JSON.stringify(stats, null, 1));
finish();
