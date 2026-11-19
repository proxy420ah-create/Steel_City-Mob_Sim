import { bootCdp } from './_cdp.mjs';
const { evl, sleep, finish } = await bootCdp({ settle: 2500 });

await evl(`window.__place && window.__place.toggle(true)`);
await sleep(300);

const report = await evl(`(() => {
  const out = [];
  for (const p of PLACED) {
    placeSel = null;
    let grabErr = '', dropErr = '', viaKey = false;
    try { placeGrab(p); } catch(e) { grabErr = String(e && e.stack || e).slice(0,200); }
    const grabbed = placeSel === p;
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    viaKey = placeSel === null;
    if (!viaKey) { try { placeDrop(); } catch(e) { dropErr = String(e && e.stack || e).slice(0,400); } }
    out.push({ name: p.name, kind: p.kind, grabbed, viaKey, grabErr, dropErr,
               tops: p.tops ? p.tops.size : -1, lb: p.lb ? 1 : 0, mount: !!p.mount, src: p.source });
  }
  placeSel = null;
  return JSON.stringify(out);
})()`);

const rows = JSON.parse(report || '[]');
rows.forEach(r => console.log((r.viaKey ? 'OK  ' : 'FAIL'), String(r.name).padEnd(42),
  'kind=' + String(r.kind).padEnd(6), 'tops=' + String(r.tops).padStart(5), 'mount=' + r.mount,
  (r.grabErr || r.dropErr) ? '\n     ERR ' + (r.grabErr || r.dropErr) : ''));
const bad = rows.filter(r => !r.viaKey);
console.log('\nstuck:', bad.length, 'of', rows.length);
finish();
