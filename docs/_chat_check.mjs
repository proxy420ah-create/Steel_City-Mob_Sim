// _chat_check.mjs — pad CHAT tab (bench upgrade): registry tab, backfill, chips,
// composer DOM, /boss send → feed, live /pulse/BOSS?mb=1 tail, dm-lane privacy.
import { bootCdp } from './_cdp.mjs';
const { evl, sleep, check, finish } = await bootCdp({ page: 'construct' });
await sleep(4000);                                                        // backfill: one 2.26MB feed fetch + parse

check('chat module live', await evl(`!!window.__chat`));
check('CHAT tab generated on the strip', await evl(`!!PAD_PAGES.chat && PAD_REGIONS.some(r => r.id === 'tab:chat')`));
check('chips registered', await evl(`PAD_REGIONS.filter(r => r.page === 'chat').length >= 4`));
await evl(`__chat.open()`);
check('chat page set', await evl(`padPage === 'chat'`));
check('backfill ran (n>0)', await evl(`__chat.n() > 0`), await evl(`__chat.n()`));
check('transcript has lines', await evl(`__chat.log().length > 0`));
check('no dm leaked into transcript', await evl(`!__chat.log().some(l => l.startsWith('dm-'))`));

// chip → target selection
await evl(`__chat.setTarget('@CHAIR')`);
check('chip target @CHAIR', await evl(`__chat.target() === '@CHAIR'`));
await evl(`__chat.setTarget('@CHAIR @DESIGNER')`);
check('chip target GROUP', await evl(`__chat.target() === '@CHAIR @DESIGNER'`));

// entry band → real input pinned to the pad face (camera over the table so the pad's in frame)
await evl(`window.__cam(-4.1, 1.7, -0.1, -4.1, 0.4, -0.575)`); await sleep(150);
const band = await evl(`__chat.band()`);
check('band rect on the pad face', band && band.w > 24 && band.h > 4, band && (band.w|0) + 'x' + (band.h|0) + ' @' + (band.x|0) + ',' + (band.y|0));
check('input pinned over the band', await evl(`(() => { const r = __chat.band(); if (!r) return false;
  const i = document.getElementById('chatIn'), cs = i.getBoundingClientRect();
  return i.style.display !== 'none' && Math.abs(cs.left - r.x) < 2 && Math.abs(cs.width - r.w) < 2; })()`));
check('input hides off-viewport', await evl(`(() => { window.__cam(0, 1.5, 5, 0, 1, -5); return new Promise(res => setTimeout(() =>
  res(__chat.band() === null && document.getElementById('chatIn').style.display === 'none'), 120)); })()`));
await evl(`window.__cam(-4.1, 1.7, -0.1, -4.1, 0.4, -0.575)`); await sleep(150);
await evl(`__chat.focus()`);
check('input takes keyboard', await evl(`document.activeElement && document.activeElement.id === 'chatIn'`));
check('placeholder carries the target', await evl(`document.getElementById('chatIn').placeholder.includes('GROUP')`));
await evl(`window.__cam(null)`);

// SEND — /boss aside grammar: GROUP spells @CHAIR @DESIGNER
const sent = await evl(`__chat.send('probe ping — chat tab wire check').then(j => j && j.msgs && j.msgs[0] ? {to: j.msgs[0].to, kind: j.msgs[0].kind} : j)`);
check('send landed on feed', sent && sent.kind === 'aside' && Array.isArray(sent.to)
  && sent.to.includes('CHAIR') && sent.to.includes('DESIGNER'), sent && JSON.stringify(sent.to));
check('own send echoed as YOU', await evl(`__chat.log().some(l => l.startsWith('YOU: probe ping'))`));

// LIVE TAIL — a seat's aside to BOSS must arrive via the held mb-pulse (no reload)
await evl(`fetch('/post', {method:'POST', headers:{'Content-Type':'application/json'},
  body: JSON.stringify({speaker:'CHAIR', kind:'aside', to:['BOSS'], text:'probe reply — live tail check'})})`);
await sleep(2500);
check('seat aside streamed in live', await evl(`__chat.log().some(l => l.startsWith('CHAIR: probe reply'))`));

// dm lane stays private — a dm addressed to BOSS must NOT render on the chat wire
await evl(`fetch('/dm', {method:'POST', headers:{'Content-Type':'application/json'},
  body: JSON.stringify({who:'CHAIR', to:['BOSS'], text:'probe dm — should stay off the chat wire'})})`);
await sleep(2500);
check('dm to BOSS excluded from chat', await evl(`!__chat.log().some(l => l.includes('probe dm'))`));

finish();
