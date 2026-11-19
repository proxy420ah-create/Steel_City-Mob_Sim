'use strict';
/* construct/19_chat.js — pad CHAT tab (bench upgrade, BOSS decree via side table:
   "design and create this feature for us to use as a bench upgrade"). Side-table comms
   from inside the room — the pad face draws the transcript + target chips + entry band;
   the entry band IS a real text input: a DOM <input> pinned over the band in screen
   space (typing can't happen on a canvas face, so the field IS the band — click it,
   or aim + E on it, and the keyboard is yours).
     SEND: POST /boss {text:'/aside @TARGET msg'} — DVM-017 boss-aside grammar,
           attention-only to:, zero machinery, wakes the named seats.
     READ: /pulse/BOSS?mb=1 — the mailbox long-poll: ungated, no presence stamp. One held
           connection streams the exchange; boot backfills via one feed fetch, then the
           pulse carries new lines only (own sends render optimistic — pulse drops self-echoes).
     WIRE: kind:'aside' only — the OFFICE lane stays private even where it would be visible. */

const CHAT_TARGETS = [
  { tok: '@CHAIR @DESIGNER', lab: 'GROUP' },
  { tok: '@CHAIR',           lab: '@CHAIR' },
  { tok: '@DESIGNER',        lab: '@DESIGNER' }];
const CHAT_COL = { YOU: '#e6d5b0', BOSS: '#e6d5b0', CHAIR: '#d29922', DESIGNER: '#bc8cff' };   // seat-hex tags — DESIGNER's catalog spec
let chatTarget = CHAT_TARGETS[0].tok, chatN = -1, chatLog = [], chatDead = false, chatPollGen = 0;
const chatTake = m => {                                                      // one feed msg → transcript row
  if (m.kind !== 'aside') return;                                            // side-table wire only — dm/main-table stay off it
  chatLog.push({ who: m.speaker === 'BOSS' ? 'YOU' : m.speaker, text: m.text });
  if (chatLog.length > 48) chatLog.shift();
  if (padPage.split(':')[0] === 'chat') padDrawSoon(); };

/* ── READ — backfill once, then hold the mailbox tail ── */
const chatBackfill = async () => {
  try {
    const src = await (await fetch('/deliberation_feed.js?ts=' + Date.now())).text();
    const m = src.match(/window\.DVM_FEED\s*=\s*(\{[\s\S]*\});?\s*$/);
    const msgs = (JSON.parse(m[1]).messages) || [];
    chatN = msgs.length; msgs.slice(-60).forEach(chatTake);
  } catch (e) { chatDead = true; }
};
const chatPoll = async () => {
  const gen = ++chatPollGen;                                                 // one outstanding hold — a reconnect bumps the gen
  while (gen === chatPollGen) {
    try {
      const j = await (await fetch('/pulse/BOSS?mb=1&since=' + chatN)).json();
      if (j.msgs) j.msgs.forEach(chatTake);
      if (typeof j.n === 'number') chatN = Math.max(chatN, j.n);             // n = total count — the watermark to hold
      if (chatDead && padPage.split(':')[0] === 'chat') padDrawSoon();       // OFFLINE repaints on reconnect — DESIGNER's audit nit
      chatDead = false;
    } catch (e) { chatDead = true; await new Promise(r => setTimeout(r, 3000)); }
  }
};

/* ── SEND — the boss-aside grammar; the chip IS the to:-array ── */
const chatSend = txt => fetch('/boss', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ text: '/aside ' + chatTarget + ' ' + txt }) })
  .then(r => r.json()).then(j => { chatTake({ kind: 'aside', speaker: 'BOSS', text: txt }); return j; })
  .catch(() => { chatTake({ kind: 'aside', speaker: 'SYS', text: '— send failed (server unreachable)' }); });

/* ── the entry band IS the input — a DOM <input> pinned over the band in screen space.
   Always rendered where the band is (chat page + pad on-screen): click it while the
   cursor's free, or aim + E on the band while pointer-locked — either way the field
   takes the keyboard. Enter sends and keeps focus (chat flow); Esc drops it. ── */
const chatEl = document.createElement('input');
chatEl.id = 'chatIn'; chatEl.maxLength = 240; chatEl.autocomplete = 'off'; chatEl.spellcheck = false;
chatEl.style.cssText = 'position:fixed;left:0;top:0;display:none;z-index:22;box-sizing:border-box;background:rgba(13,18,23,.94);border:1px solid #35d5ff;color:#e6d5b0;font:12px ui-monospace,Consolas,monospace;padding:2px 8px;outline:none;box-shadow:0 0 8px rgba(53,213,255,.25) inset';
document.body.appendChild(chatEl);
const chatIn = chatEl;
const chatOpen = () => document.activeElement === chatIn;
const chatSyncTgt = () => { const t = CHAT_TARGETS.find(t => t.tok === chatTarget);
  chatIn.placeholder = (t ? t.lab : chatTarget) + ' ▸  speak — Enter sends · Esc drops'; };
chatIn.addEventListener('keydown', e => { e.stopPropagation();               // typing never reaches the game loop
  if (e.key === 'Enter' && chatIn.value.trim()) { const t = chatIn.value.trim(); chatIn.value = ''; chatSend(t); }
  if (e.key === 'Escape') { chatIn.blur(); grabLook(); } });
chatIn.addEventListener('keyup', e => e.stopPropagation());
const chatFocus = () => { if (document.exitPointerLock) document.exitPointerLock(); chatIn.focus(); chatIn.select(); };
chatSyncTgt();

/* pin the field onto the drawn band — the pad screen's bottom strip in local coords
   (plane .15×.1; canvas band y292..330 → uv .03-.15 → local y -.047..-.035, x inset 3%→97%). */
const _cq = [new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3()];
const chatBandRect = () => {
  if (!padScr || padPage.split(':')[0] !== 'chat') return null;
  const pt = (v, x, y) => v.set(x, y, 0).applyMatrix4(padScr.matrixWorld).project(camera);
  pt(_cq[0], -.069, -.035); pt(_cq[1], .069, -.035); pt(_cq[2], -.069, -.047); pt(_cq[3], .069, -.047);
  let x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9, zok = false;
  for (const v of _cq) {
    if (v.z > -1 && v.z < 1) zok = true;
    const sx = (v.x + 1) / 2 * innerWidth, sy = (1 - v.y) / 2 * innerHeight;
    x0 = Math.min(x0, sx); x1 = Math.max(x1, sx); y0 = Math.min(y0, sy); y1 = Math.max(y1, sy);
  }
  if (!zok || x1 - x0 < 24 || y1 - y0 < 4) return null;                      // degenerate or behind — nothing to type into
  if (x1 < 0 || x0 > innerWidth || y1 < 0 || y0 > innerHeight) return null;  // off-viewport — NDC still projects it, but no band is showing
  return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
};
let chatLast = null;
const chatHide = () => {                                                 // hide + blur — Chrome keeps focus on display:none inputs, and a
  chatEl.style.display = 'none'; chatLast = null;                        // focused chatIn stopPropagations EVERY key — a hidden focused field
  if (document.activeElement === chatEl) chatEl.blur();                  // is a dead keyboard for the whole game
};
const chatPin = () => {                                                  // transform-only moves — compositor, zero layout churn
  const r = chatBandRect();
  if (!r) { chatHide(); return; }
  chatEl.style.display = 'block';
  const x = Math.round(r.x), y = Math.round(r.y), w = Math.round(r.w), h = Math.max(16, Math.round(r.h));
  if (!chatLast || chatLast.x !== x || chatLast.y !== y) chatEl.style.transform = `translate3d(${x}px,${y}px,0)`;
  if (!chatLast || chatLast.w !== w) chatEl.style.width = w + 'px';
  if (!chatLast || chatLast.h !== h) { chatEl.style.height = h + 'px'; chatEl.style.fontSize = Math.max(10, Math.min(15, h * .55)) + 'px'; }
  chatLast = { x, y, w, h };
};
setInterval(() => { if (padPage.split(':')[0] === 'chat' && padScr) chatPin();           // ~30fps track — cheap vector work, only while the chat tab is active
  else if (chatLast || chatEl.style.display !== 'none') chatHide(); }, 33);               // off-tab cleanup — the field must die with the page, not linger focused

/* ── the canvas face — chips mid-row, transcript above, entry band bottom ── */
CHAT_TARGETS.forEach((t, i) => padR({ id: 'chat:t' + i, page: 'chat', x: 16 + i * 164, y: 66, w: 156, h: 32,
  label: t.lab, glyph: t.lab, fs: 13, lit: () => chatTarget === t.tok,
  fn: () => { chatTarget = t.tok; chatSyncTgt(); padDrawSoon(); } }));
padR({ id: 'chat:entry', page: 'chat', x: 16, y: 290, w: 480, h: 40,        // the band itself presses — aim + E hands the field your keyboard
  label: 'TYPE IN FIELD', glyph: '', fs: 15, fn: chatFocus });
function drawChatTab(g) {
  g.textAlign = 'left';
  g.fillStyle = '#8f87a0'; g.font = 'bold 15px monospace';
  g.fillText('SIDE TABLE — ' + (chatDead ? 'OFFLINE' : 'LIVE'), 20, 58);     // ear state rides the header — honest chrome
  const lines = [];                                                        // wrap to pane width; who-tags on first slice only
  chatLog.forEach(e => { const txt = (e.text || '').replace(/\s+/g, ' ').replace(/<[^>]*>/g, '');
    for (let i = 0; i < Math.max(1, txt.length); i += 56) lines.push({ w: e.who, s: txt.substr(i, 56), first: i === 0 }); });
  g.font = '12px monospace';
  lines.slice(-10).forEach((L, i) => { const y = 116 + i * 17;
    g.fillStyle = CHAT_COL[L.w] || '#8f87a0'; g.fillText(L.first ? L.w : '', 20, y);
    g.fillStyle = '#cfc8b0'; g.fillText(L.s, 20 + 82, y); });
  if (!chatLog.length) { g.fillStyle = '#5b5648';
    g.fillText('no traffic yet — pick a target and speak', 20, 120); }
  g.strokeStyle = chatOpen() ? '#35d5ff' : '#22303a'; g.lineWidth = 2;       // the entry band — the DOM field rides it
  g.strokeRect(16, 290, 480, 40);
  g.fillStyle = '#07090c'; g.fillRect(17, 291, 478, 38);
}
window.__chat = { send: chatSend, focus: chatFocus, open: () => padPageSet('chat'),
  log: () => chatLog.map(e => e.who + ': ' + e.text), target: () => chatTarget,
  setTarget: t => { chatTarget = t; chatSyncTgt(); padDrawSoon(); },
  on: chatOpen, dead: () => chatDead, n: () => chatN, band: chatBandRect, draw: drawChatTab };

setTimeout(() => { chatBackfill().then(chatPoll); }, 2500);                  // let the boot fetches land first — the ear is cheap but not in the way
