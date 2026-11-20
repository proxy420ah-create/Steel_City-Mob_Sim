'use strict';
/* construct/09_live_ctl.js — agent live link (/peek, /shot, cmd dispatch) + follower control strip.
   Split from construct.html (was lines 1461-1539); classic <script defer> siblings share one
   page-wide scope, load order = the old top-down eval order. */

/* ── live link — streams the session to the server so the agent sees YOUR real setup:
   /peek gets pos/aim/marks/follower orders+gaze at ~2Hz; /shot probe_live_pov gets his
   actual POV frame ~1Hz (shot()'s fullscreen render is restored in the same task — no flicker). */
const liveEl = document.createElement('div');
liveEl.style.cssText = 'position:fixed;right:12px;top:12px;color:#7fd4ff;background:rgba(8,8,14,.85);border:1px solid #3a3a4a;padding:3px 7px;font:10px ui-monospace,Consolas,monospace;letter-spacing:.25em;display:none;z-index:6;pointer-events:none';
liveEl.textContent = '◉ LIVE'; document.body.appendChild(liveEl);
const agentEl = document.createElement('div');
agentEl.style.cssText = 'position:fixed;right:12px;top:32px;color:#ff9d5c;background:rgba(8,8,14,.85);border:1px solid #3a3a4a;padding:3px 7px;font:10px ui-monospace,Consolas,monospace;letter-spacing:.25em;display:none;z-index:6;pointer-events:none';
agentEl.textContent = '◈ AGENT'; document.body.appendChild(agentEl);
let cmdAck = 0, _peekErr = null;
const livePost = () => fetch('/peek', { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ t: Date.now(), pos: pos.toArray(), yaw, aimOn, aim: aimWorld.toArray(), held: held && held.name,
    marks: marks.map(m => m.position.toArray()), povOn, cmdAck, placeOn: !!placeOn,
    placed: PLACED.map(p => [p.name, +p.x.toFixed(2), +p.z.toFixed(2), +p.y0.toFixed(2), p.source]),
    ls: (() => { const o = {}; let n = 0;                                       // bounded dvm_* dump — a saved zone capture alone can top the server's 256KB peek limit
      for (const k of Object.keys(localStorage)) { if (!k.startsWith('dvm_')) continue;
        const v = localStorage.getItem(k); n += v.length;
        o[k.slice(4)] = n < 200e3 ? v : { len: v.length, head: v.slice(0, 80) }; }   // over budget → size + head preview keeps the debugging info without the bulk
      return o; })(),
    followers: followers.map(f => ({ char: f.spec.char, order: f.order, pos: f.pos.toArray(), gaze: f.gazeT ? f.gazeT.toArray() : null, possessed: !!f.possessed })) }) })
  .then(r => r.json()).then(r => {
    if (r && r.err && r.err !== _peekErr) { _peekErr = r.err; console.warn('[peek]', r.err); }   // server rejected — say why once, not per retry
    if (r && r.cmd && r.cmd.seq > cmdAck) {                                    // agent command rode back on the stream
      cmdAck = r.cmd.seq; const A = window.__construct.agent, c = r.cmd;
      const _csig = c.seq + '|' + c.agent + '|' + JSON.stringify(c.args || []);
      if (localStorage.getItem('dvm_cmdsig') === _csig) return;                // already executed this exact order — cmdAck resets per session, the sig survives reloads (was the Civilian1 totem)
      localStorage.setItem('dvm_cmdsig', _csig);
      if (c.agent === 'gaze') A.gaze(...c.args);
      else if (c.agent === 'go') A.go(...c.args);
      else if (c.agent === 'heel') A.heel();
      else if (c.agent === 'order') A.order(c.args[0]);
      else if (c.agent === 'emit') { const [n, kind, x, z, ry, sc, fit] = c.args;   // agent-furnished asset — kind + scale + wardrobe ride args
        emitAsset(n, { kind: kind || 'item', x: +x || 0, z: +z || 0, rotY: +ry || 0, pickable: true, source: 'extra',
          seatY: 'auto', vsScale: +sc || 1, height: +sc || null, fit: fit === undefined ? null : +fit }).then(() => placeSave()); }   // char emits: sc = target height (m), fit = OUTFITS index
      else if (c.agent === 'move') { const [n, x, z, ry] = c.args;               // relocate an existing item — the surface law seats it
        const p = PLACED.find(q => q.name === n && q.source === 'extra') || PLACED.find(q => q.name === n && !q._parked);
        if (p) { const ob = footB(p.lb, p.x, p.z, p.rotY); placeApply(p, +x || 0, +z || 0, ry === undefined ? p.rotY : +ry);
          p.y0 = placeSurf(p.x, p.z, new Set([p]), { surfOnly: p.surf }); p.g.position.y = p.y0;
          settleOrphans(p, ob, p.bbox, p.y0 + topOf(p)); refreshSolids(); placeSave(); } }
      else if (c.agent === 'del') { const p = PLACED.find(q => q.name === c.args[0] && q.source === 'extra') || PLACED.find(q => q.name === c.args[0] && !q._parked);
        if (p) { if (p.source !== 'extra') { const d = new Set(JSON.parse(localStorage.getItem('dvm_del') || '[]'));
            d.add(p.name); localStorage.setItem('dvm_del', JSON.stringify([...d])); }
          scene.remove(p.g); PLACED.splice(PLACED.indexOf(p), 1);
          for (let i = PLACED.length - 1; i >= 0; i--) for (let n = PLACED[i].g; n && n !== scene; n = n.parent)
            if (n === p.g) { PLACED.splice(i, 1); break; }                       // descendant recs (parked cars) die with their table — no ghost hosts
          settleOrphans(p, p.bbox, null, Infinity); refreshSolids(); placeSave(); } }
      else if (c.agent === 'zoom') A.zoom(...c.args);                            // agent POV scope — level 0..1
      else if (c.agent === 'torch') A.torch(...c.args);                          // agent flips the inspection torch
      else if (c.agent === 'tune') window.__tune && window.__tune(...c.args);    // lab metric retune — side/road/raise in cells, rebuilds the table
      else A.order(c.agent);                                                   // bare order names: wait/goto/follow/post
      const released = c.agent === 'heel' || c.agent === 'follow' || (c.agent === 'order' && c.args[0] === 'follow');
      if (!['emit', 'move', 'del', 'zoom', 'torch', 'tune'].includes(c.agent)) followers.forEach(f => f.possessed = !released);   // movement verbs hold the wheel; furniture + camera ops don't
    } }).catch(() => {});
const liveShot = () => { const u = window.__eyes.shot();
  if (window.__torch && window.__torch.on) window.__torch.aim(camera);           // shot() aimed it at povCam — re-aim before the restore render or his light jumps ~1Hz
  renderer.render(scene, camera);
  if (u) fetch('/shot', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: 'probe_live_pov', png: u }) }).catch(() => {}); };
const LIVE = { on: false, iv: 0, sv: 0 };
window.__live = {
  on: () => LIVE.on,
  start: () => { if (LIVE.on) return; LIVE.on = true; livePost(); liveShot(); LIVE.iv = setInterval(livePost, 400); LIVE.sv = setInterval(liveShot, 900); liveEl.style.display = 'block'; localStorage.setItem('dvm_live', '1'); },
  stop: () => { LIVE.on = false; clearInterval(LIVE.iv); clearInterval(LIVE.sv); liveEl.style.display = 'none'; localStorage.setItem('dvm_live', '0'); },
  lsPull: () => fetch('/peek').then(r => r.json()).then(d => {               // cross-browser scene mirror — localStorage is per-browser; the source browser's livePost already ships its dvm_* set
    const ls = (d && d.ls) || {}; let n = 0; const skip = [];
    for (const k in ls) { const v = ls[k];
      if (v && typeof v === 'object') { skip.push(k); continue; }            // {len,head} stub — source overran the pack budget; mirroring it would write a truncated scene
      localStorage.setItem('dvm_' + k, v); n++; }
    console.log('[lsPull] mirrored ' + n + ' dvm_* keys' + (skip.length ? ' — SKIPPED over-budget stubs: ' + skip.join(', ') : '') + ' — reloading');
    setTimeout(() => location.reload(), 400); }),
};
window.__lsPull = window.__live.lsPull;

/* ── follower control — clickable orders + hotkey legend, bottom-left. Re-renders only when
   follower state actually changes (orders move via keys too), so button presses never get eaten. */
const ctlEl = document.createElement('div');
ctlEl.style.cssText = 'position:fixed;left:12px;bottom:12px;background:rgba(8,8,14,.97);border:1px solid #6a6a92;box-shadow:0 2px 14px rgba(0,0,0,.45);padding:10px 12px;font:11px ui-monospace,Consolas,monospace;color:#e2dbc2;z-index:8;pointer-events:auto;display:none';
document.body.appendChild(ctlEl);
let ctlOn = true, ctlSig = '';
function renderCtl() {
  const f = followers[0], o = f && f.order;
  ctlEl.innerHTML =
    '<div style="letter-spacing:.2em;color:#b0a9cc;margin-bottom:6px">FOLLOWER</div>' +
    (f ?
      '<div style="display:flex;gap:6px;margin-bottom:8px">' +
      ['follow', 'wait', 'goto'].map(ord =>
        '<button data-o="' + ord + '" style="font:inherit;font-size:10px;padding:3px 8px;cursor:pointer;color:#e8e0c8;background:' + (o === ord ? '#3a4a2a' : '#1c1c26') + ';border:1px solid ' + (o === ord ? '#7a5' : '#3a3a4a') + '">' +
        (ord === 'goto' ? 'GO ▸ TAG' : ord.toUpperCase()) + '</button>').join('') + '</div>' +
      (f.possessed ? '<div style="color:#ff9d5c;font-size:10px;margin-bottom:6px;letter-spacing:.15em">◈ agent has the wheel — your keys reclaim</div>' : '')
    : '<div style="color:#9a9480;font-size:10px;margin-bottom:8px">none in room — door console spawns</div>') +
    (held ?
      '<div style="letter-spacing:.2em;color:#35d5ff;border-top:1px solid #3a3a4a;margin:8px 0 4px;padding-top:6px">HELD — ' +
        held.name.replace('commission_', '').replace(/_/g, ' ').toUpperCase() + '</div>' +
      '<div style="color:#8a8474;font-size:10px;line-height:1.6">' +
      'E place · wheel dolly · , . yaw<br>' +
      '[ ] pitch · ; \' roll · G tumble · 0 level' +
      (held.isPad ? ' · RMB tap — cursor' : '') + '</div>'
    : '') +
    '<div style="color:#8a8474;font-size:10px;line-height:1.6;margin-top:6px">' +
    'F/RMB aim · LMB tag · T go-tag · R clear<br>' +
    'H heel · P pov · Y live · K panel · hold-Z scope</div>';
  ctlEl.querySelectorAll('button[data-o]').forEach(b => b.onclick = () => { if (f) { f.possessed = false; f.setOrder(b.dataset.o); } ctlTick(); });
}
const ctlTick = () => {
  const sig = followers.length + '|' + followers.map(f => f.order + (f.possessed ? '*' : '')).join() + '|' + ctlOn + '|' + (held ? held.name + (held.isPad ? '#' : '') : '');   // held rides the sig — pickup/drop re-renders the HELD block
  if (sig !== ctlSig) { ctlSig = sig; renderCtl(); ctlEl.style.display = ctlOn ? 'block' : 'none'; }
  agentEl.style.display = followers.some(f => f.possessed) ? 'block' : 'none';
};
setInterval(ctlTick, 400); ctlTick();
