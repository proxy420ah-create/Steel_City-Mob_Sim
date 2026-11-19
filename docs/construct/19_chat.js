'use strict';
/* construct/19_chat.js — pad CHAT tab (bench upgrade, BOSS decree via side table:
   "design and create this feature for us to use as a bench upgrade"). Side-table comms
   from inside the room — the pad face draws the transcript + target chips + entry band;
   the entry band is a CANVAS-NATIVE field: typing mode captures keys at the window
   and draws buffer + caret onto the pad face itself (BOSS decree — the DOM-input-on-
   projection seam let the field overlap the follower PiP; a field drawn into the
   texture lives in the world, rides every camera, and can't fight HUD layers).
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
let chatTarget = CHAT_TARGETS[0].tok, chatN = -1, chatLog = [], chatDead = false, chatPollGen = 0, chatScrollN = 0;
const CHAT_WRAP = 50;                                                        // transcript wraps narrow — the ▲▼ strip owns the right edge
const chatLines = () => { const lines = [];
  chatLog.forEach(e => { const txt = (e.text || '').replace(/\s+/g, ' ').replace(/<[^>]*>/g, '');
    for (let i = 0; i < Math.max(1, txt.length); i += CHAT_WRAP) lines.push({ w: e.who, s: txt.substr(i, CHAT_WRAP), first: i === 0 }); });
  return lines; };
const chatTake = m => {                                                      // one feed msg → transcript row
  if (m.kind !== 'aside') return;                                            // side-table wire only — dm/main-table stay off it
  const txt = (m.text || '').replace(/\s+/g, ' ').replace(/<[^>]*>/g, '');
  if (chatScrollN > 0) chatScrollN += Math.max(1, Math.ceil(txt.length / CHAT_WRAP));   // scrolled up = hold the viewed window; tail stays pinned
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

/* ── the entry band is canvas-native — buffer + caret drawn on the pad face, keys
   captured at the window while typing. Click the band (cursor mode) or aim + E
   (locked) to take the keyboard; Enter sends and stays, Esc drops. Click places
   the caret — monospace means px/char-width IS the index math. ── */
let chatTyping = false, chatBuf = '', chatCaret = 0, chatBlink = 1;
const CHAT_NCH = 48;                                                     // visible cells inside the 478px band @15px mono
const chatOpen = () => chatTyping;
const chatScroll = () => Math.max(0, Math.min(chatCaret - CHAT_NCH + 1, chatBuf.length - CHAT_NCH));   // keep the caret visible, tail-anchored
const chatDrop = () => { chatTyping = false; padDrawSoon(); };
const chatFocus = uv => {                                                // band press — uv → caret at the clicked cell (aim-E passes no uv → end)
  chatTyping = true; chatBlink = 1;
  chatCaret = uv ? Math.max(0, Math.min(chatBuf.length, chatScroll() + Math.round((uv.x * 512 - 24) / 9))) : chatBuf.length;
  padDrawSoon();
};
addEventListener('keydown', e => {
  if (!chatTyping) return;
  e.stopPropagation();                                                   // capture phase — typing never reaches the game loop
  const k = e.key;
  if (k === 'Enter') { e.preventDefault(); const t = chatBuf.trim(); chatBuf = ''; chatCaret = 0; if (t) chatSend(t); }
  else if (k === 'Escape') { e.preventDefault(); chatTyping = false; }
  else if (k === 'Backspace') { e.preventDefault(); if (chatCaret) { chatBuf = chatBuf.slice(0, chatCaret - 1) + chatBuf.slice(chatCaret); chatCaret--; } }
  else if (k === 'Delete') { e.preventDefault(); chatBuf = chatBuf.slice(0, chatCaret) + chatBuf.slice(chatCaret + 1); }
  else if (k === 'ArrowLeft') { e.preventDefault(); chatCaret = Math.max(0, chatCaret - 1); }
  else if (k === 'ArrowRight') { e.preventDefault(); chatCaret = Math.min(chatBuf.length, chatCaret + 1); }
  else if (k === 'Home') { e.preventDefault(); chatCaret = 0; }
  else if (k === 'End') { e.preventDefault(); chatCaret = chatBuf.length; }
  else if (k.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey && chatBuf.length < 240) {
    e.preventDefault(); chatBuf = chatBuf.slice(0, chatCaret) + k + chatBuf.slice(chatCaret); chatCaret++; }
  padDrawSoon();
}, true);
addEventListener('keyup', e => { if (chatTyping) e.stopPropagation(); }, true);
document.addEventListener('pointerlockchange', () => { if (!document.pointerLockElement && chatTyping) chatDrop(); });   // locked Esc never keydowns — the unlock IS the drop
setInterval(() => {                                                      // caret blink + the field dies with the page
  if (chatTyping && padPage.split(':')[0] !== 'chat') chatTyping = false;
  if (chatTyping) { chatBlink ^= 1; padDrawSoon(); }
}, 450);

/* ── the canvas face — chips mid-row, transcript above, entry band bottom ── */
CHAT_TARGETS.forEach((t, i) => padR({ id: 'chat:t' + i, page: 'chat', x: 16 + i * 164, y: 66, w: 156, h: 32,
  label: t.lab, glyph: t.lab, fs: 13, lit: () => chatTarget === t.tok,
  fn: () => { chatTarget = t.tok; chatSyncTgt(); padDrawSoon(); } }));
padR({ id: 'chat:entry', page: 'chat', x: 16, y: 290, w: 480, h: 40,        // the band itself presses — aim + E hands the field your keyboard
  label: 'TYPE IN FIELD', glyph: '', fs: 15, fn: uv => chatFocus(uv) });    // uv → caret at the clicked cell
padR({ id: 'chat:up', page: 'chat', x: 468, y: 112, w: 36, h: 88,           // transcript scrollback — one line per press
  label: 'SCROLL UP', glyph: '▲', fs: 20, lit: () => chatLines().length > 10 + chatScrollN,
  fn: () => { chatScrollN++; padDrawSoon(); } });
padR({ id: 'chat:dn', page: 'chat', x: 468, y: 202, w: 36, h: 88,
  label: 'SCROLL DOWN', glyph: '▼', fs: 20, lit: () => chatScrollN > 0,
  fn: () => { chatScrollN = Math.max(0, chatScrollN - 1); padDrawSoon(); } });
function drawChatTab(g) {
  g.textAlign = 'left';
  g.fillStyle = '#8f87a0'; g.font = 'bold 15px monospace';
  g.fillText('SIDE TABLE — ' + (chatDead ? 'OFFLINE' : 'LIVE'), 20, 58);     // ear state rides the header — honest chrome
  if (chatScrollN) { g.fillStyle = '#35d5ff'; g.fillText('▲ ' + chatScrollN + ' older', 350, 58); }   // scrollback marker — where you are vs the tail
  const lines = chatLines();                                               // wrap to pane width; who-tags on first slice only
  chatScrollN = Math.min(chatScrollN, Math.max(0, lines.length - 10));       // clamp — a trimmed log can't scroll past its head
  const a0 = Math.max(0, lines.length - 10 - chatScrollN);                   // the viewed window — tail-anchored minus scrollback
  g.font = '12px monospace';
  lines.slice(a0, a0 + 10).forEach((L, i) => { const y = 116 + i * 17;
    g.fillStyle = CHAT_COL[L.w] || '#8f87a0'; g.fillText(L.first ? L.w : '', 20, y);
    g.fillStyle = '#cfc8b0'; g.fillText(L.s, 20 + 82, y); });
  if (!chatLog.length) { g.fillStyle = '#5b5648';
    g.fillText('no traffic yet — pick a target and speak', 20, 120); }
  g.strokeStyle = chatOpen() ? '#35d5ff' : '#22303a'; g.lineWidth = 2;       // the entry band — the canvas field lives here now
  g.strokeRect(16, 290, 480, 40);
  g.fillStyle = '#07090c'; g.fillRect(17, 291, 478, 38);
  g.font = '15px monospace';
  const i0 = chatScroll(), vis = chatBuf.slice(i0, i0 + CHAT_NCH);
  if (vis || chatTyping) { g.fillStyle = '#e6d5b0'; g.fillText(vis, 24, 314);
    if (chatTyping && chatBlink) { g.fillStyle = '#35d5ff';                 // the caret — a bright bar, blink rides the 450ms tick
      g.fillRect(24 + g.measureText(chatBuf.slice(i0, chatCaret)).width, 297, 2, 24); } }
  else { g.fillStyle = '#5b5648';                                          // idle hint — the chip owns the target name
    const t = CHAT_TARGETS.find(t => t.tok === chatTarget);
    g.fillText((t ? t.lab : chatTarget) + '  \u25b8  speak \u2014 Enter sends \u00b7 Esc drops', 24, 314); }
}
window.__chat = { send: chatSend, focus: chatFocus, open: () => padPageSet('chat'),
  log: () => chatLog.map(e => e.who + ': ' + e.text), target: () => chatTarget,
  setTarget: t => { chatTarget = t; padDrawSoon(); },
  on: chatOpen, dead: () => chatDead, n: () => chatN, buf: () => chatBuf,
  scroll: () => chatScrollN, draw: drawChatTab };

setTimeout(() => { chatBackfill().then(chatPoll); }, 2500);                  // let the boot fetches land first — the ear is cheap but not in the way
