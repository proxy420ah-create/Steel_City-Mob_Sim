'use strict';
/* construct/18_options.js — OPTIONS overlay (O): rebindable hotkeys + key probe.
   While open, a capture-phase keydown swallows ALL input before the game's bubble
   handlers see it — rebind takes the next real keypress, and every press lands on the
   probe line (key/code) so any key can be auditioned in any browser. Esc cancels a
   capture and closes the menu — it can never be bound: browsers consume Esc for
   pointer-lock exit before the page sees it. Binds persist via localStorage
   (dvm_key_<act>); adding a row = declare a `let xKey` in the owning file + get/set it. */

const OPT = [
  { act: 'zoom', label: 'ZOOM / SCOPE — HOLD', store: 'dvm_key_zoom', def: 'z',
    get: () => zoomKey, set: v => { zoomKey = v; } },
];

/* keys already spoken for — binding over one still takes, it just warns (testing is the point) */
const OPT_TAKEN = {
  w: 'MOVE FWD', a: 'MOVE LEFT', s: 'MOVE BACK', d: 'MOVE RIGHT', shift: 'SPRINT', alt: 'CRAWL',
  e: 'INTERACT', x: 'PLACE MODE', l: 'TORCH', p: 'POV EYES', t: 'GO ORDER', r: 'RELEASE ORDER',
  h: 'HEEL / WAIT', k: 'CONTROL STRIP', y: 'LIVE LINK', o: 'OPTIONS', g: 'TUMBLE ITEM',
  b: 'RE-SEAT', c: 'COPY', v: 'PASTE', n: 'SNAP +Y', m: 'SNAP −Y', '[': 'SNAP +X', ']': 'SNAP −X',
  ';': 'SNAP +Z', "'": 'SNAP −Z', '0': 'LEVEL', ',': 'ROTATE −Y', '.': 'ROTATE +Y',
  arrowup: 'NUDGE', arrowdown: 'NUDGE', arrowleft: 'NUDGE', arrowright: 'NUDGE',
  pageup: 'RAISE', pagedown: 'LOWER', enter: 'ACCEPT', delete: 'DELETE', backspace: 'DELETE',
};

/* settings sliders — same persist contract as OPT, but rendered ONCE (rebuilding the
   <input> inside optRender would snap a drag mid-pull whenever a probe key lands) */
const SLIDERS = [
  { act: 'power', label: 'ZOOM POWER — SCOPED FOV', store: 'dvm_zoom_fov', def: 26, min: 6, max: 50,
    get: () => zoomFov, set: v => { zoomFov = v; }, fmt: v => (58 / v).toFixed(1) + '× · FOV ' + v },
];

let optOpen = false, optCap = null, optLast = null;
const keyName = k => ({ ' ': 'SPACE', control: 'CTRL', meta: 'META', escape: 'ESC', arrowup: '↑',
  arrowdown: '↓', arrowleft: '←', arrowright: '→' }[k] || (k || '—').toUpperCase());

const optEl = document.createElement('div');
optEl.style.cssText = 'position:fixed;inset:0;z-index:20;display:none;align-items:center;justify-content:center;' +
  'background:rgba(244,241,232,.55);font:12px/1.7 ui-monospace,Consolas,monospace;user-select:none';
optEl.innerHTML =
  '<div style="background:rgba(20,16,10,.94);color:#e8dfc8;border:1px solid #4a3f28;padding:14px 18px;min-width:380px;letter-spacing:.14em">' +
  '<div style="color:#b0a88f;letter-spacing:.3em;margin-bottom:8px">OPTIONS</div>' +
  '<div id="optRows"></div>' +
  '<div id="optSlds" style="margin-top:4px"></div>' +
  '<div id="optProbe" style="margin-top:10px;color:#8a8168;font-size:11px"></div>' +
  '<div id="optWarn" style="min-height:16px;color:#d94a3a;font-size:11px"></div>' +
  '<div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">' +
  '<span id="optReset" style="cursor:pointer;border:1px solid #4a3f28;padding:2px 10px;color:#b0a88f">RESET</span>' +
  '<span id="optClose" style="cursor:pointer;border:1px solid #4a3f28;padding:2px 10px">CLOSE</span>' +
  '<span style="color:#8a8168;font-size:10px">O / ESC / BACKDROP CLOSES — ZOOM WORKS UNLOCKED TOO, JUST HOLD THE KEY</span>' +
  '</div></div>';
document.body.appendChild(optEl);

const optRows = optEl.querySelector('#optRows'), optProbe = optEl.querySelector('#optProbe'),
      optWarn = optEl.querySelector('#optWarn'), hkZoom = document.getElementById('hkZoom');

const sldEls = SLIDERS.map(s => {
  const row = document.createElement('div');
  row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:4px 8px;margin:2px 0;border:1px solid #3a3125';
  const lab = document.createElement('span'); lab.style.flex = '1'; lab.textContent = s.label;
  const inp = document.createElement('input');
  inp.type = 'range'; inp.min = s.min; inp.max = s.max; inp.step = 1;
  inp.style.cssText = 'width:120px;accent-color:#d94a3a';
  const val = document.createElement('span');
  val.style.cssText = 'color:#e8d9b0;min-width:104px;text-align:right';
  inp.addEventListener('input', () => {
    s.set(+inp.value); localStorage.setItem(s.store, inp.value);
    val.textContent = s.fmt(s.get()); });
  row.append(lab, inp, val); optEl.querySelector('#optSlds').appendChild(row);
  return { s, inp, val };
});

const optRender = () => {
  optRows.innerHTML = OPT.map(o =>
    '<div class="optRow" data-act="' + o.act + '" style="display:flex;justify-content:space-between;gap:16px;' +
    'padding:4px 8px;margin:2px 0;cursor:pointer;border:1px solid ' + (optCap === o ? '#d94a3a' : '#3a3125') + '">' +
    '<span>' + o.label + '</span><span style="color:' + (optCap === o ? '#d94a3a' : '#e8d9b0') + '">' +
    (optCap === o ? 'PRESS A KEY — ESC CANCELS' : keyName(o.get())) + '</span></div>').join('');
  optProbe.textContent = optCap ? 'WAITING FOR A KEY — CONFLICTS WARN, THEY NEVER REFUSE' :
    optLast ? 'LAST KEY —  key ' + JSON.stringify(optLast.key) + '   code ' + (optLast.code || '—')
            : 'PRESS ANY KEY — THE PROBE REPORTS WHAT THIS BROWSER SENDS';
  optWarn.textContent = OPT.map(o => OPT_TAKEN[o.get()]
    ? '⚠ ' + keyName(o.get()) + ' ALSO DOES: ' + OPT_TAKEN[o.get()] : '').filter(Boolean).join('   ');
  const z = OPT.find(o => o.act === 'zoom');
  if (hkZoom && z) hkZoom.textContent = keyName(z.get());            // the HUD's HOLD x SCOPE chip tracks the bind
  sldEls.forEach(({ s, inp, val }) => { inp.value = s.get(); val.textContent = s.fmt(s.get()); });
};

const optToggle = f => {
  optOpen = f === undefined ? !optOpen : f;
  optEl.style.display = optOpen ? 'flex' : 'none';
  optCap = null;
  if (optOpen) { if (document.pointerLockElement) document.exitPointerLock(); keysClear(); }
  optRender();                                                       // keysClear — a held key can't ride the menu open
};
window.__optToggle = optToggle;

/* capture phase on window runs before the game's bubble handlers — the menu owns the
   keyboard while it's up; the loop keeps rendering, so a re-bound key eases the FOV
   the moment the menu closes */
addEventListener('keydown', e => {
  if (!optOpen) return;
  e.stopPropagation(); e.preventDefault();
  const k = (e.key || '').toLowerCase();
  optLast = { key: e.key || '', code: e.code || '' };
  if (optCap) { if (k !== 'escape') { optCap.set(k); localStorage.setItem(optCap.store, k); } optCap = null; }
  else if (k === 'escape' || k === 'o') { optToggle(false); return; }
  optRender();
}, true);

/* overlay clicks must never reach the canvas — a swallowed click can't re-lock the
   pointer or tag an aim behind the menu; a press on the backdrop itself closes */
['mousedown', 'mouseup', 'click', 'dblclick', 'contextmenu', 'wheel'].forEach(t =>
  optEl.addEventListener(t, e => e.stopPropagation()));
optEl.addEventListener('mousedown', e => { if (e.target === optEl) optToggle(false); });

optRows.addEventListener('click', e => {
  const r = e.target.closest('.optRow'); if (!r) return;
  optCap = OPT.find(o => o.act === r.dataset.act); optRender();
});
optEl.querySelector('#optReset').addEventListener('click', () => {
  OPT.forEach(o => { o.set(o.def); localStorage.setItem(o.store, o.def); });
  SLIDERS.forEach(s => { s.set(s.def); localStorage.setItem(s.store, s.def); });
  optCap = null; optRender(); });
optEl.querySelector('#optClose').addEventListener('click', () => optToggle(false));

optRender();
