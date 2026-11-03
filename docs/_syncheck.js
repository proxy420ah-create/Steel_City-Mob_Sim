
/* ── roster: SEATS = the standing commission (always rendered).
   DVM-009: chair names ARE the identity — members{} retired; occupancy
   derives from presence_<SEAT>.js (file exists → held; expired → away). ── */
const SEATS = {
  ARCHITECT:  { name:'THE ARCHITECT',  color:'var(--cyan)',   motto:'systemic purity · nothing without a contract' },
  INFORMANT:  { name:'THE INFORMANT',  color:'var(--pink)',   motto:'checks the draft for fullness · accuracy before the table attacks' },
  BOSS:       { name:'THE BOSS',      color:'#e6d5b0',        motto:'the table is his · reserved seat', boss:true },
  AUDITOR:    { name:'THE AUDITOR',    color:'var(--orange)', motto:'finds the failure mode before production does' },
  HISTORIAN:  { name:'THE HISTORIAN',  color:'var(--purple)', motto:'precedent, the RE, and what the docs say', retired:true },
  CHAIR:      { name:'THE CHAIR',      color:'var(--yellow)', motto:'synthesizes · speaks last · carries the verdict' },
  BUTTON:     { name:'THE BUTTON',     color:'var(--red)',    motto:'delegated · codes only on verdict + GO CODE', delegated:true },
};
const LEGACY_SEAT = { DEVIN:'CHAIR', MEMBER2:'ARCHITECT', MEMBER3:'BUTTON', MEMBER4:'INFORMANT', PRAGMATIST:'ARCHITECT' };  // resolve old transcript ids (C4: read-only bridge — authors never write these)
const MTAG = new Set([...Object.keys(SEATS), 'BOSS', 'DEVIN', 'MEMBER2', 'MEMBER3', 'MEMBER4']);      // call-ons render bold
let expandedSeat = null;   // DVM-005: one seat contract open at a time (accordion)
const PHASE_NAMES = ['tabled','openings','rebuttal','veto poll','verdict'];

/* ── state ── */
const FEED_FILE = 'deliberation_feed.js', POLL_MS = 2000;
const seen = new Set(); let msgSeq = 0, msgCount = 0, curPhase = -1;
const msglist = document.getElementById('msglist');

/* ── seats ── (DVM-014: the rail retired — the room IS the seat panel;
   renderSeats/seatState stay as no-op guards for legacy call sites) ── */
function renderSeats(){
  const el = document.getElementById('seats'); if (!el) return; el.innerHTML = '';
  for (const [key,seat] of Object.entries(SEATS)){
    const occ = PRESENCE[key] != null || seat.boss;    // presence file = seat held (DVM-009); the boss's chair is always held
    const einfo = occ && !seat.boss ? earInfo(key) : null;
    const dead = einfo && einfo.dead;
    const contract = LASTBOARD && (LASTBOARD.seat_rules || {})[key];
    const d = document.createElement('div');
    d.className = 'seat' + (occ ? (dead ? ' away' : '') : ' vacant'); d.id = 'seat_' + key;
    d.title = 'click to @address';
    const dotc = occ ? (dead ? 'var(--orange)' : seat.color) : 'var(--border)';
    const namec = occ ? (dead ? 'var(--text-dim)' : seat.color) : 'var(--text-dim)';
    const tagc = occ ? (dead ? 'dead' : 'live') : (seat.delegated ? 'delegated' : 'vacant');
    d.innerHTML = `<i class="s-dot" style="background:${dotc}"></i>
      <div><div class="s-name" style="color:${namec}">${seat.name}</div>
      <div class="s-motto">${seat.motto}</div>
      ${occ ? `<div class="s-ear${dead ? ' dead' : ''}" id="ear_${key}">${seat.boss ? 'reserved' : einfo.txt}</div>` : ''}
      ${contract ? `<div class="s-rules" data-seat="${key}">rules ${expandedSeat === key ? '▾' : '▸'}</div>` : ''}
      ${contract && expandedSeat === key ? `<div class="s-contract"><div class="c-duty">${contract.duty || ''}</div>${(contract.rules || []).map(r => `<div class="c-rule">${r}</div>`).join('')}</div>` : ''}</div>
      <span class="s-tag" id="stag_${key}">${tagc}</span>`;
    el.appendChild(d);
  }
}
function seatKeyFor(id){
  if (SEATS[id]) return id;                                   // seat keys are the namespace
  return LEGACY_SEAT[id] || null;                             // legacy member ids resolve once
}
function seatState(id, state, tag){
  const key = seatKeyFor(id); if (!key) return;
  const s = document.getElementById('seat_' + key); if (!s) return;
  s.classList.toggle('speaking', state === 'speaking');
  if (state === 'vetoed') s.classList.add('vetoed');
  if (state === 'clear') s.classList.remove('speaking','vetoed');
  if (tag !== undefined) document.getElementById('stag_' + key).textContent = tag;
}
const whoIs = id => SEATS[id] || SEATS[LEGACY_SEAT[id]] || null;

/* ── phase strip ── */
function setPhase(p){
  if (p === curPhase) return; curPhase = p;
  document.querySelectorAll('#phasestrip .step').forEach(el => {
    const i = +el.dataset.ph;
    el.classList.toggle('done', i < p);
    el.classList.toggle('live', i === p);
  });
  if (p === 0) Object.keys(SEATS).forEach(k => seatState(k,'clear',''));
  const dp = document.getElementById('d_phase'); if (dp) dp.textContent = 'phase ' + (PHASE_NAMES[p] || '—').toUpperCase();
  DS.phase = p; DECK.note();
}

/* ── ingest: the one public API ── */
function ingest(msg){
  if (msg.id && seen.has(msg.id)) return;
  if (msg.id) seen.add(msg.id); else msg.id = 'auto_' + (msgSeq++);
  if (msg.kind === 'phase'){ setPhase(+msg.phase || 0); }
  if (msg.kind === 'verdict' && msg.vresult){ renderVerdict(msg.vresult, msg.text, msg.conds); }
  if (msg.kind === 'aside'){ appendAside(msg); return; }      // DVM-017 §4b: the side channel never enters the transcript
  if (msg.kind === 'sys' || msg.channel === 'tech'){ appendTech(msg); return; }   // DVM-922: machine speech routes to the TECH LOG — record stays on the bus, the view splits
  appendMsg(msg);
  if (msg.speaker && whoIs(msg.speaker)){
    // boss-ordered (AUDITOR-verified): hydrate replays history — suppress the
    // lastMsg write AND transient speech effects so old chatter never draws a
    // bubble; live arrivals still emit. Stateful effects (pins, vetoed) plant.
    if (!booting){
      DS.lastMsg[seatKeyFor(msg.speaker)] = { text: msg.text || '', kind: msg.kind || 'position' };   // chair bubbles (DVM-014)
      DECK.speech(seatKeyFor(msg.speaker));
      if (msg.kind === 'veto' || msg.kind === 'objection' || msg.kind === 'ack') DECK.pulse(seatKeyFor(msg.speaker));
      if (msg.kind !== 'veto'){ seatState(msg.speaker, 'speaking'); setTimeout(() => seatState(msg.speaker,'clear',''), 2600); }
    }
    if (msg.kind === 'veto') DECK.pin(seatKeyFor(msg.speaker));            // ballot pins on the pegboard
    if (msg.kind === 'veto') seatState(msg.speaker, 'vetoed', 'VETO');
  }
}
function appendMsg(msg){
  document.getElementById('feedempty')?.remove();
  const m = whoIs(msg.speaker);
  const kind = (msg.kind || 'sys'), ts = msg.ts || new Date().toLocaleTimeString();
  const d = document.createElement('div');
  if (kind === 'sys' || kind === 'phase' || kind === 'boss'){   // unseated speech → wall monitor (DVM-014)
    DS.wallMsg = { text: msg.text || '', kind: kind }; DECK.speech('WALL');
  }
  if (kind === 'sys' || kind === 'phase'){
    d.className = 'msg sys';
    d.innerHTML = `<div class="m-body">${kind === 'phase' ? '── ' + (PHASE_NAMES[msg.phase]||'').toUpperCase() + ' ──' : ''} ${msg.text}</div>`;
  } else {
    const sname = msg.speaker === 'BOSS' ? 'THE BOSS' : (m ? m.name : (msg.speaker || 'SYSTEM'));
    const scolor = msg.speaker === 'BOSS' ? 'var(--tan)' : (m ? m.color : 'var(--text)');
    const toChips = (msg.to || []).map(t => `<span class="at">@${LEGACY_SEAT[t] || t}</span>`).join(' ');
    const bodyHtml = msg.text.replace(/@([A-Za-z0-9_]+)/g, (m, t) =>
      MTAG.has(t) ? `<b class="mtag">${m}</b>` : `<span class="at">${m}</span>`);  // call-ons wear a bold tag (DVM-009)
    const hsB = msg.needs_ack || (kind === 'boss' && (msg.to || []).length);   // handshake-bearing directive (DVM-015)
    d.className = 'msg k-' + kind;
    d.dataset.mid = msg.id || '';
    d.innerHTML = `
      <div><div class="m-speaker" data-seat="${msg.speaker || ''}" style="color:${scolor}">${sname}</div>
      <div class="m-meta">${ts}${msg.round ? ' · R'+msg.round : ''}</div></div>
      <div class="m-body"><span class="m-kind k-${kind}">${kind}</span>${toChips ? toChips + ' ' : ''}${bodyHtml}${hsB ? ' <span class="hs"></span>' : ''}</div>`;
  }
  const pill = document.getElementById('newmsg');
  const atBottom = msglist.scrollHeight - msglist.scrollTop - msglist.clientHeight < 60;
  msglist.insertBefore(d, pill);
  if (atBottom) d.scrollIntoView({block:'end'}); else pill.classList.add('show');
  if (!booting && (activeTab !== 'feed' || !atBottom)) unread.feed++;   // unread = inactive tab, or active but scrolled up
  syncUnread();
  document.getElementById('msgcount').textContent = (++msgCount) + ' messages';
  const dm = document.getElementById('d_msg'); if (dm) dm.textContent = msgCount + ' messages';
}
/* ── SIDE TABLE (DVM-017 §4b): floor-free voice — same bus, separate render ── */
function appendAside(msg){
  const list = document.getElementById('stlist'); if (!list) return;
  const boss = msg.speaker === 'BOSS', w = whoIs(msg.speaker) || {};
  const d = document.createElement('div');
  d.className = 'st-msg' + (boss ? ' boss' : '');
  d.innerHTML = `<span class="st-who" style="color:${boss ? 'var(--tan)' : (w.color || 'var(--pink)')}">💭 ${boss ? 'THE BOSS' : (w.name || msg.speaker || 'SYS')}</span>${msg.text || ''}`;
  const atB = list.scrollHeight - list.scrollTop - list.clientHeight < 60;
  list.appendChild(d);
  if (activeTab === 'side' && atB) list.scrollTop = list.scrollHeight;
  if (!booting && (activeTab !== 'side' || !atB)) unread.side++;
  syncUnread();
}
/* DVM-015: handshake state rides the directive — ⏳ awaiting → ✓ acknowledged → 👍 authorized.
   pending >120s flags amber (soft timeout: visible, judgment stays with the sender). */
function syncHandshake(){
  msglist.querySelectorAll('.hs').forEach(el => {
    const mid = el.closest('.msg').dataset.mid, st = (mid && DS.hs[mid]) || null;
    const txt = !st ? '' : st.o ? '👍 authorized' : st.a ? '✓ acknowledged'
              : (st.ts && Date.now()/1000 - st.ts > 120) ? '⏳ stale' : '⏳ awaiting';
    if (el.textContent !== txt){
      el.textContent = txt;
      el.className = 'hs ' + (!st ? '' : st.o ? 'h-oked' : st.a ? 'h-acked'
                  : (st.ts && Date.now()/1000 - st.ts > 120) ? 'h-stale' : 'h-await');
    }
  });
}
document.getElementById('newmsg').onclick = function(){ this.classList.remove('show'); msglist.scrollTop = msglist.scrollHeight; };
msglist.addEventListener('scroll', () => {
  if (msglist.scrollHeight - msglist.scrollTop - msglist.clientHeight < 60){
    document.getElementById('newmsg').classList.remove('show');
    if (activeTab === 'feed'){ unread.feed = 0; syncUnread(); }
  }
});
/* ── DVM-922: the TECH LOG — machine speech renders here, never the transcript.
   Diversion is render-level ONLY: posts stay on the bus (AUDITOR's law). ── */
let unreadDark = 0;
function appendTech(msg){
  const list = document.getElementById('tlist'); if (!list) return;
  const dark = /DARK|dark/.test(msg.text || '');
  const d = document.createElement('div');
  d.className = 'tmsg' + (dark ? ' tdark' : '');
  const ts = msg.ts ? new Date(msg.ts).toLocaleTimeString() : new Date().toLocaleTimeString();
  d.innerHTML = `<span class="tts">${ts} · ${msg.id || 'sys'}</span>${msg.text || ''}`;
  list.appendChild(d);
  if (activeTab === 'tech') list.scrollTop = list.scrollHeight;
  else { unread.tech++; if (dark) unreadDark++; }
  syncUnread();
  if (msg.kind === 'sys'){ DS.wallMsg = { text: msg.text || '', kind: 'sys' }; DECK.speech('WALL'); }   // wall monitor keeps its feed
}
/* ── tabbed pane (boss-directed): transcript + side table share one element; unread tags clear on read ── */
let activeTab = 'feed', unread = {feed:0, side:0, tech:0}, booting = true;
function syncUnread(){
  document.getElementById('un_feed').textContent = unread.feed || '';
  document.getElementById('un_side').textContent = unread.side || '';
  const un = document.getElementById('un_tech');
  un.textContent = unread.tech || '';
  un.classList.toggle('dark', unreadDark > 0);
}
function setTab(t){
  activeTab = t;
  document.getElementById('tab_feed').classList.toggle('active', t === 'feed');
  document.getElementById('tab_side').classList.toggle('active', t === 'side');
  document.getElementById('tab_tech').classList.toggle('active', t === 'tech');
  msglist.style.display = t === 'feed' ? '' : 'none';
  const st = document.getElementById('stlist');
  st.style.display = t === 'side' ? '' : 'none';
  const tl = document.getElementById('tlist');
  tl.style.display = t === 'tech' ? '' : 'none';
  if (t === 'side') st.scrollTop = st.scrollHeight;
  if (t === 'tech'){ tl.scrollTop = tl.scrollHeight; unreadDark = 0; }
  unread[t] = 0; syncUnread();
}
document.getElementById('tab_feed').onclick = () => { setTab('feed'); setSendMode('send'); };
document.getElementById('tab_side').onclick = () => { setTab('side'); setSendMode('side'); };
document.getElementById('tab_tech').onclick = () => setTab('tech');
document.getElementById('stlist').addEventListener('scroll', () => {
  const st = document.getElementById('stlist');
  if (st.scrollHeight - st.scrollTop - st.clientHeight < 60 && activeTab === 'side'){
    unread.side = 0; syncUnread();
  }
});

/* ── proposal + verdict ── */
function setProposal(p){
  DS.prop = p || null; DECK.note();
  const box = document.getElementById('propbox');
  if (!p || p.status === 'closed'){   // closed/null — slot returns to awaiting business (DVM-003)
    box.className = 'panel propbox empty';
    box.innerHTML = '<div class="p-id">TABLE IDLE — awaiting new business</div>' +
      '<div class="p-text">Waiting for the boss to table a proposal… <span style="font-style:normal">(or run <b>?demo=1</b>)</span></div>';
    return;
  }
  box.className = 'panel propbox';
  box.innerHTML = '<div class="p-id">PROPOSAL ' + (p.id || '') + '</div>' +
    '<div class="p-text">' + (p.title || p.text || '') + '</div>';
}
function renderVerdict(result, text, conds){
  const box = document.getElementById('verdictbox');
  const key = (result || '').toString().toLowerCase();
  const cls = { approved:'approved', ship:'approved', revise:'revise', vetoed:'vetoed' }[key] || 'waiting';
  const word = { approved:'✓ APPROVED', ship:'✓ SHIP', revise:'↺ REVISE & RESUBMIT', vetoed:'✕ VETOED' }[key]
    || (result || 'awaiting').toString().toUpperCase();   // schema drift renders, never throws (DVM-004)
  box.className = 'panel verdict ' + cls;
  box.innerHTML = `<div class="v-word">${word}</div>
    <div style="font-size:.82em;margin-top:6px">${text || ''}</div>
    <div class="v-conds">${((typeof conds === 'string' ? conds.split(/\s*;\s*/).filter(Boolean) : conds) || []).map(c => `<span class="v-chip">${c}</span>`).join('')}</div>
    <div class="v-meta">${new Date().toLocaleTimeString()} · verdict is advisory — the boss ratifies</div>`;
}

/* ── state snapshot application (feed file drives this) ── */
const safe = fn => { try { fn(); } catch(e){ console.warn('render block failed:', e); } };
function applyState(s){
  if (!s) return;
  safe(() => setProposal(s.proposal));
  safe(renderSeats);                                    // seats render every state pass — occupancy is presence-derived (DVM-009)
  if (typeof s.phase === 'number') safe(() => setPhase(s.phase));
  safe(() => (s.messages || []).forEach(ingest));
  booting = false;                                          // first applyState is hydrate — replay never counts unread
  if (s.verdict) safe(() => renderVerdict(s.verdict.ruling, s.verdict.summary, s.verdict.conds));  // live schema: ruling/summary (DVM-004)
  if (s.verdict){ const vk = s.verdict.ts || s.verdict.ruling; if (vk !== DS.vts){ DS.vts = vk; DECK.note('verdict'); }
    DS.vid = s.verdict.id || ''; }
  if (s.messages) safe(() => {   // DVM-014 fallbacks — bus msgs carry no ts; array order IS the timeline
    const msgs = s.messages, pid = (s.proposal || {}).id || '';
    let g = -1, b2 = -1, vt = -1, cs = -1; const vs = [];
    for (let i = 0; i < msgs.length; i++){
      const m = msgs[i], t = m.text || '';
      if (m.kind === 'verdict') vt = i;
      if (m.kind === 'phase' || (pid && t.indexOf(pid + ' TABLED') >= 0)) cs = i;
      if (m.speaker === 'BOSS' && /@BUTTON/i.test(t) && /\bGO\b/i.test(t)) g = i;
      if (m.speaker === 'BUTTON' && /\bSHIP/i.test(t)) b2 = i;
      if (m.kind === 'veto' && seatKeyFor(m.speaker) && i > cs) vs.push(seatKeyFor(m.speaker));
    }
    DS.goOK = g > vt;                            // a GO only counts after the verdict it unleashes
    DS.builtOK = b2 > Math.max(vt, g);
    DS.vetoSeats = vs;
    /* DVM-015 handshake derivation — needs_ack materialized, or VIRTUAL on boss+to≠∅
       past ack_epoch (§4 F2a named-not-materialized; §8: no retroactive handshakes). */
    const ep = typeof s.ack_epoch === 'number' ? s.ack_epoch : Infinity;
    const dirs = {}, acks = {}, oks = {};
    for (let i = 0; i < msgs.length; i++){
      const m = msgs[i];
      if (m.id && (m.needs_ack || (m.kind === 'boss' && (m.to || []).length && i >= ep)))
        dirs[m.id] = { ts: m.ts || 0, a:'', o:'', hold: !!m.hold || (m.kind === 'boss' && (m.to || []).length && i >= ep) };   // boss taskings hold by ratified default
      if (m.kind === 'ack' && m.ack_of) acks[m.ack_of] = m.id;
      if (m.kind === 'ok'  && m.ack_of) oks[m.ack_of]  = m.id;
    }
    for (const id in dirs){ const a = acks[id]; if (a){ dirs[id].a = a; if (oks[a]) dirs[id].o = oks[a]; } }
    DS.hs = dirs;
    /* DVM-018 pending_task — the handshake lifecycle derived per seat from the tail:
       aimed → acked → released → delivered → expired. Zero new bus fields:
       needs_ack = aim (C26: to: is pure addressing, never tasking);
       ack+ack_of = ack; ok+ack_of = release;
       floor-class write (aside/sys/phase/ack/ok excluded — voice, not product)
       or a delivers:true ack = delivery; ear-dark or now−aimed > TASKED_TTL = expiry.
       Newest tasking per seat wins (v1 bound). */
    const SIDE = { aside:1, sys:1, phase:1, ack:1, ok:1 }, TASKED_TTL = 600000;
    DS.pendingTask = {};
    for (let i = msgs.length - 1; i >= 0; i--){
      const m = msgs[i], tos = (m.to || []).map(t => seatKeyFor(t) || t);
      const tasked = m.needs_ack;
      if (!tasked) continue;
      for (const k of tos){
        if (k === 'BOSS' || DS.pendingTask[k]) continue;
        const rec = { id: m.id, by: m.speaker, aimed: m.ts || 0, state: 'aimed' };
        let ackId = '';
        for (let j = i + 1; j < msgs.length && rec.state !== 'delivered'; j++){
          const n = msgs[j], nk = seatKeyFor(n.speaker);
          if (nk === k && n.kind === 'ack' && n.ack_of === m.id){
            ackId = n.id; rec.state = 'acked'; rec.acked = n.ts || 0;
            if (n.delivers) rec.state = 'delivered';
          } else if (ackId && n.kind === 'ok' && n.ack_of === ackId){
            if (rec.state === 'acked') rec.state = 'released';
          } else if (nk === k && !SIDE[n.kind]){
            rec.state = 'delivered';
          }
        }
        if (rec.state !== 'delivered' && rec.aimed && Date.now() - rec.aimed > TASKED_TTL) rec.state = 'expired';
        DS.pendingTask[k] = rec;
      }
    }
  });
  safe(syncHandshake);
  if (s.seats) safe(() => { DS.live = s.seats.concat('BOSS'); });          // C1/C4: LIVE := feed.seats ∪ {BOSS} — the floor domain, never SEATS{}
  if (s.turn) safe(() => { document.getElementById('turnlbl').textContent = 'floor: ' + s.turn;
    const dt = document.getElementById('d_turn'); if (dt) dt.textContent = s.turn; DS.turn = s.turn;
    if (s.turn_at) DS.turnAt = s.turn_at < 1e12 ? s.turn_at * 1000 : s.turn_at; DECK.note(); });  // C6: ms canon; seconds-era values self-correct on read
  if (s.board) safe(() => renderBoard(s.board));
  safe(() => { adjourned = !!s.adjourned; DS.adj = adjourned; syncAdjBtn(); DECK.note(); });  // DVM-007: recess flag drives the toggle label
}

/* ── live feed: script-tag poll (works over file://, unlike fetch) ── */
function setSource(mode){
  const d = document.getElementById('srcdot'), l = document.getElementById('srclbl');
  d.className = 'dot ' + (mode === 'demo' ? 'demo' : mode === 'live' ? 'live' : 'idle');
  l.textContent = mode === 'demo' ? 'demo mode' : mode === 'live' ? 'feed connected' : 'watching ' + FEED_FILE;
  const dd = document.getElementById('d_srcdot'), dl = document.getElementById('d_srclbl');
  if (dd) dd.className = d.className;
  if (dl) dl.textContent = mode === 'demo' ? 'demo' : mode === 'live' ? 'live' : 'feed';
}
let pollTag = null, pollBorn = 0;
function poll(){
  if (pollTag){
    if (Date.now() - pollBorn < 8000) return;
    pollTag.remove(); pollTag = null;   // wedged load — force reset
  }
  pollBorn = Date.now();
  const t = document.createElement('script');
  t.src = FEED_FILE + '?_=' + Date.now();
  t.onload = () => { setSource('live'); if (window.DVM_FEED){ applyState(window.DVM_FEED); window.DVM_FEED = null; } pollTag = null; t.remove(); };
  t.onerror = () => { pollTag = null; t.remove(); };
  pollTag = t; document.head.appendChild(t);
}
/* ── DVM-010: push over HTTP (SSE signals), 2s poll fallback (file:// or
   dead server). Signal carries the filename; script-tag still carries the
   payload — one serialization path either way. ── */
let pollTimers = null, pushOk = false, sseBk = 0;
const SSE_BACKOFF = [5000, 15000, 30000, 60000];   /* DVM-011 §3: permanent-failure ladder — 5s→15s→30s→60s cap */
function setPushChip(m){
  const c = document.getElementById('pushchip'); if (c) c.textContent = m;
  const d = document.getElementById('d_push'); if (d) d.textContent = m;
}
function armPolling(){
  if (pollTimers) return;
  pollTimers = [setInterval(poll, POLL_MS), setInterval(pollPresence, POLL_MS)];
  setPushChip('poll');
}
function startPush(){
  try {
    const es = new EventSource('/events');       // throws synchronously on file://
    es.onopen  = () => { pushOk = true; sseBk = 0; setPushChip('push'); };
    es.onerror = () => {
      if (!pushOk) armPolling();                 // EventSource auto-reconnects; poll arms only if push never engaged
      if (es.readyState === EventSource.CLOSED){ // permanent failure — native retry stops; manual backoff re-construct (DVM-011 §3)
        setPushChip('retry');
        setTimeout(startPush, SSE_BACKOFF[Math.min(sseBk++, SSE_BACKOFF.length - 1)]);
      }
    };
    es.onmessage = ev => {
      try {
        const f = (JSON.parse(ev.data) || {}).file;
        if (f === 'deliberation_feed'){ poll(); setTimeout(() => Object.keys(SEATS).forEach(loadInbox), 400); }   // every batch lands in every inbox — the feed event IS the doorbell; 400ms covers daemon write latency
        else if (typeof f === 'string' && f.indexOf('presence_') === 0) loadPresence(f.slice(9));
        else if (typeof f === 'string' && f.indexOf('busy_') === 0) loadBusy(f.slice(5));   // C27: server watches busy_* mtimes — without this branch BUSY froze at hydrate under push, so tags only ever drew on F5
      } catch(e){}
    };
  } catch(e) { armPolling(); }
}

/* ── presence side-channel (DVM-002): each armed ear sole-writes
   presence_<AGENT>.js; polled by script-tag like the feed. Binary by the
   boss's ruling: chip ticks the armed_at+ttl countdown through relay
   hops; dead only on expiry or missing file — status flag retired. ── */
const PRESENCE = {};   // seat key -> {armed_at, ttl_ms, pulse_at} | null (DVM-009)
function earInfo(aid){
  const p = PRESENCE[aid];
  if (!p) return { dead:true, txt:'ear dead' };
  const rem = p.armed_at + p.ttl_ms - Date.now();
  if (rem <= 0){                                            /* DVM-011 §2: post-expiry age — a 40s relay-gap stops reading like a 4h stall */
    const d = Math.floor(-rem / 1000);
    return { dead:true, txt:'ear dead ' + (d < 60 ? d + 's' : d < 3600 ? Math.floor(d/60) + 'm' : Math.floor(d/3600) + 'h') };
  }
  const s = Math.ceil(rem / 1000);
  return { dead:false, txt:'ear ' + Math.floor(s/60) + ':' + String(s%60).padStart(2,'0') };
}
function loadPresence(key){
  const t = document.createElement('script');
  t.src = 'presence_' + key + '.js?_=' + Date.now();
  t.onload = () => { PRESENCE[key] = window['DVM_PRESENCE_' + key] || null; window['DVM_PRESENCE_' + key] = null; t.remove(); };
  t.onerror = () => { PRESENCE[key] = null; t.remove(); };
  document.head.appendChild(t);
}
/* DVM-921: busy_<SEAT>.js — the agent-alive mark, second file of the dual-stamp.
   presence = loop-alive (watcher-owned); busy = agent-alive (host sole-writer). */
const BUSY = {};
function loadBusy(key){
  const t = document.createElement('script');
  t.src = 'busy_' + key + '.js?_=' + Date.now();
  t.onload = () => { BUSY[key] = window['DVM_BUSY_' + key] || null; window['DVM_BUSY_' + key] = null; t.remove(); };
  t.onerror = () => { BUSY[key] = null; t.remove(); };
  document.head.appendChild(t);
}
/* C27+: inbox_n_<SEAT>.js — the daemon's delivery heartbeat, sole-written by the
   listener. Renderer reads it for the WAKING derived state: doorbell fact, not a
   seat claim — sits below stamped labor in busyTier like QUEUED/STALLED do. */
const INBOX = {};
function loadInbox(key){
  const t = document.createElement('script');
  t.src = 'inbox_n_' + key + '.js?_=' + Date.now();
  t.onload = () => { INBOX[key] = window['DVM_INBOX_' + key] || null; window['DVM_INBOX_' + key] = null; t.remove(); };
  t.onerror = () => { INBOX[key] = null; t.remove(); };
  document.head.appendChild(t);
}
function busyMark(k){ const b = BUSY[k]; return !!(b && b.busy && (Date.now() - (b.armed_at || 0)) < (b.ttl_ms || 30000)); }
function pollPresence(){ for (const key of Object.keys(SEATS)) if (!SEATS[key].retired && !SEATS[key].boss){ loadPresence(key); loadBusy(key); loadInbox(key); } loadPresence('SUPERVISOR'); }   // DVM-922: the watchdog's own lamp rides the same glob
/* 1s tick — countdown chips + dead-ear seat state (countdown deadline) */
setInterval(() => {
  for (const key of Object.keys(SEATS)){
    if (!PRESENCE[key]) continue;
    const info = earInfo(key);
    const chip = document.getElementById('ear_' + key);
    if (chip){ chip.textContent = info.txt; chip.classList.toggle('dead', info.dead); }
    document.getElementById('seat_' + key)?.classList.toggle('away', info.dead);
    const tag = document.getElementById('stag_' + key);
    if (tag && (tag.textContent === 'live' || tag.textContent === 'dead'))
      tag.textContent = info.dead ? 'dead' : 'live';
  }
  const sig = Object.keys(SEATS).map(k => { const ls = lampState(k); return ls + ':' + lampFrac(k) + (ls === 'dead' ? ':' + earInfo(k).txt : '') + '|' + (busyTier(k) || ''); }).join(',');   // fuse fraction rides the signature (DVM-014); dead-age label too (DVM-011); busy tier too — tag changes must repaint live, not only on F5 (C27)
  if (sig !== DS.lamps){ DS.lamps = sig; DECK.presence(); if (DECK.mode !== '3d') render2D(); }
  /* DVM-015 §6c: floorSilent = turn ∈ seats && turn_at && !adjourned && >120s — the render
     alarm needs ZERO writers; recess is deliberate silence, not dead silence (binding nit). */
  const sk = (DS.turn && DS.turn !== 'BOSS' && DS.live.includes(DS.turn) && DS.turnAt && !DS.adj
              && (Date.now() - DS.turnAt) > 120000 && !busyTier(DS.turn)) ? DS.turn : '';   // C1/C6: LIVE domain, ms clock; DVM-018: the whole aimed→released lifecycle is sanctioned labor — suppresses while the lamp is lit
  if (sk !== DS.silentK){ DS.silentK = sk; DECK.note(); }             // note() = syncScene+mark (3D) / render2D (else)
  const ghost = (DS.turn && !DS.adj && !DS.live.includes(DS.turn)) ? DS.turn : '';          // C1: non-LIVE floor burns instantly — no grace, no writers needed
  if (ghost !== DS.ghostK){ DS.ghostK = ghost;
    const tl2 = document.getElementById('turnlbl'); if (tl2) tl2.textContent = 'floor: ' + DS.turn + (ghost ? ' — GHOST' : '');
    DECK.note(); }
  const tl = document.getElementById('turnlbl'); if (tl){ tl.classList.toggle('burn', !!sk); tl.classList.toggle('ghost', !!ghost); }
  const dtc = document.getElementById('d_turn');
  if (dtc && dtc.parentElement){ dtc.parentElement.classList.toggle('burn', !!sk); dtc.parentElement.classList.toggle('ghost', !!ghost); }
  syncHandshake();                                     // pending >120s flips amber on the tick
  const iEl = document.getElementById('inspector');                    // inspector countdown stays live while open
  if (iEl && iEl.classList.contains('open') && iEl.dataset.seat) refreshInspectorOcc(iEl.dataset.seat);
}, 1000);

/* ── clock ── */
setInterval(() => { const t = new Date().toLocaleTimeString();
  document.getElementById('clock').textContent = t;
  const dc = document.getElementById('d_clock'); if (dc) dc.textContent = t; }, 1000);

/* ── clear ── */
document.getElementById('btnclear').onclick = () => {
  seen.clear(); msgCount = 0;
  msglist.querySelectorAll('.msg').forEach(e => e.remove());
  setPhase(-1); renderSeats();
  document.getElementById('verdictbox').className = 'panel verdict waiting';
  document.getElementById('verdictbox').innerHTML = '<div class="v-word">· AWAITING DELIBERATION ·</div>';
};

/* ── demo: scripted mock deliberation ── */
const DEMO = [
  [300,  {kind:'phase', phase:0}],
  [900,  () => setProposal({id:'DVM-001', text:'Should Steel City combat stay dice-driven, or move to deterministic menace rules?'})],
  [1400, {kind:'phase', phase:1}],
  [2000, {id:'d1', speaker:'ARCHITECT', kind:'position', round:1, text:'Dice-driven. The contract is clean: menace moves <b>when/whether</b>, dice own <b>which</b>. Deterministic rules collapse the two channels into one and the matrix loses its shape.'}],
  [3200, {id:'d2', speaker:'PRAGMATIST', kind:'position', round:1, text:'Deterministic. We have spent three sessions tuning dice edge cases — MINDIST loops, flank interrupts. A rules table is one file, tunable on the Movement tab, done.'}],
  [4400, {id:'d3', speaker:'AUDITOR', kind:'objection', round:1, text:'Deterministic menace has a failure mode: identical inputs → identical outcomes → players learn the table and game it. Dice preserve variance where it matters.'}],
  [5600, {id:'d4', speaker:'HISTORIAN', kind:'position', round:1, text:'The original resolved everything by table lookup — 8 weapons × 8 range bands. Dice are <b>faithful modernization</b>. Deterministic rules are the differentiator only if they produce visible state.'}],
  [7000, {kind:'phase', phase:2}],
  [7600, {id:'d5', speaker:'PRAGMATIST', kind:'objection', round:2, text:'Rebuttal: the AUDITOR\u2019s variance point is real but we can inject jitter into deterministic thresholds — menace ±15% — without a roll table.'}],
  [8600, {id:'d6', speaker:'AUDITOR', kind:'sys', text:'…wait, that is actually the same thing wearing a different hat. Noted.'}],
  [9400, {kind:'phase', phase:3}],
  [10000,{id:'d7', speaker:'AUDITOR', kind:'veto', round:3, text:'<b>VETO</b> on pure deterministic menace. Jittered deterministic is acceptable; full removal of stochastic combat is not.'}],
  [10800,{id:'d8', speaker:'PRAGMATIST', kind:'support', round:3, text:'Concede — jittered deterministic keeps my tuning surface and their variance. Withdraw opposition.'}],
  [11800,{kind:'phase', phase:4}],
  [12400,{id:'d9', speaker:'CHAIR', kind:'verdict', vresult:'revise', round:3,
          text:'Resubmit as <b>hybrid</b>: deterministic menace thresholds with bounded jitter; dice retained for which-outcome resolution. One veto recorded, sustained on the pure-deterministic variant only.',
          conds:['menace ±15% jitter','dice keep WHICH-outcome','tunable on Movement tab','document in COMBAT_PARAMETER_MATRIX']}]
];
let demoRunning = false;
function runDemo(){
  if (demoRunning) return; demoRunning = true;
  setSource('demo');
  DEMO.forEach(([delay, ev]) => setTimeout(() => { (typeof ev === 'function' ? ev() : ingest(ev)); }, delay));
  setTimeout(() => { demoRunning = false; }, DEMO[DEMO.length-1][0] + 500);
}
document.getElementById('btndemo').onclick = runDemo;

/* ── standing ledger (rail DOM retired DVM-014 — LASTBOARD + nav drawer own it) ── */
function renderBoard(b){
  LASTBOARD = b;
  renderNav(b);          // navigator: STAGED / FILES / LEDGER accordions
  const dc = document.getElementById('d_closed'); if (dc) dc.textContent = 'closed ' + ((b.closed || []).length);
  const el = document.getElementById('board'); if (!el) return; el.innerHTML = '';
  if (b.rules && b.rules.length)
    el.innerHTML += '<h4>STANDING RULES</h4>' + b.rules.map(r => `<div class="bi rule">${r}</div>`).join('');
  if (b.agenda && b.agenda.length)
    el.innerHTML += '<h4>OPEN BUSINESS</h4>' + b.agenda.map(r => `<div class="bi ag">${r}</div>`).join('');
  const sr = b.seat_rules || {}, skeys = Object.keys(sr);
  if (skeys.length)
    el.innerHTML += '<h4>SEAT CONTRACTS</h4>' + skeys.map(k => {
      const c = sr[k] || {}, name = (SEATS[k] && SEATS[k].name) || k;
      return `<div class="bi contract"><b style="color:var(--text)">${name}</b> <span class="c-tag">${k}</span>` +
        `<div class="c-duty">${c.duty || ''}</div>` +
        (c.rules || []).map(r => `<div class="c-rule">${r}</div>`).join('') + '</div>';
    }).join('');
  renderClosed();
}
/* closed-business ledger (DVM-003): one line, collapsible, newest first */
let LASTBOARD = null, closedOpen = false;
function renderClosed(){
  const wrap = document.getElementById('closedwrap'), link = document.getElementById('closedlink'), list = document.getElementById('closedlist');
  if (!wrap || !link || !list) return;   // rail retired (DVM-014) — closed lives in the LEDGER accordion + HUD chip
  const items = (LASTBOARD && LASTBOARD.closed) || [];
  wrap.style.display = items.length ? '' : 'none';
  document.getElementById('ledgern').textContent = items.length;
  link.textContent = (closedOpen ? '▾' : '▸') + ' closed business (' + items.length + ')';
  list.style.display = closedOpen && items.length ? 'block' : 'none';
  if (closedOpen && items.length)
    list.innerHTML = items.slice().reverse().map(c =>
      `<div class="bi closed">${c.id} — ${c.title || ''}, ${c.ruling || ''}</div>`).join('');
}
document.getElementById('closedlink')?.addEventListener('click', () => { closedOpen = !closedOpen; renderClosed(); });

/* ── boss input — chat with the table ── */
const bossin = document.getElementById('bossin');
function address(key){
  const v = bossin.value;
  bossin.value = (v && !v.endsWith(' ') ? v + ' ' : v) + '@' + key + ' ';
  bossin.focus();
}
// rail click handler retired (DVM-014) — chair inspector owns @address now
msglist.addEventListener('click', e => {
  const sp = e.target.closest('.m-speaker'); if (sp && sp.dataset.seat)
    address(seatKeyFor(sp.dataset.seat) || sp.dataset.seat);   // legacy ids resolve to chair names
});
function setSendStat(t){ document.getElementById('sendstat').textContent = t ? ' · ' + t : ''; }
async function sendBoss(){
  const text = bossin.value.trim(); if (!text) return;
  try {
    const r = await fetch('/boss', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({text})});
    if (!r.ok) throw new Error('bad status');
    const res = await r.json();
    bossin.value = '';
    (res.msgs || []).forEach(ingest);
    setSendStat(res.turn ? '→ floor: ' + res.turn : 'sent to bus');
  } catch(e) {
    try {
      await navigator.clipboard.writeText(JSON.stringify({speaker:'BOSS', kind:'boss', text}));
      setSendStat('no server — message JSON on clipboard, paste to an agent');
    } catch { setSendStat('no server reachable'); }
  }
  setTimeout(() => setSendStat(''), 5000);
}
/* ── DVM-007: recess control — ADJOURN/RECONVENE toggle (label follows the polled flag) ── */
let adjourned = false;
function syncAdjBtn(){
  const b = document.getElementById('adjournbtn');
  if (b) b.textContent = adjourned ? 'RECONVENE' : 'ADJOURN';
}
async function toggleAdjourn(){
  try {
    const r = await fetch(adjourned ? '/reconvene' : '/adjourn', {method:'POST'});
    if (!r.ok) throw new Error('bad status');
    const res = await r.json();
    adjourned = !!res.adjourned;
    (res.msgs || []).forEach(ingest);
    syncAdjBtn();
    setSendStat(adjourned ? 'meeting adjourned — seats stand down' : 'reconvened — seats re-arm');
  } catch(e) { setSendStat('no server reachable'); }
  setTimeout(() => setSendStat(''), 5000);
}
document.getElementById('adjournbtn').onclick = toggleAdjourn;
syncAdjBtn();
/* send-mode triad (DVM-021 v0.3, full-M1) — SEND is momentary: flashes on click, never holds
   .armed. TABLE / SIDE-TABLE are arm-toggles: clicks arm + retab ONLY, never fire — Enter
   (and SEND, for floor speech) is the sole fire path. Clicking the armed verb disarms to
   the rest pose (send,feed). Invariant: armed channel ≡ viewed tab under every gesture. */
let sendMode = 'send';
function setSendMode(m){
  sendMode = m;
  document.getElementById('btntable').classList.toggle('armed', m === 'table');
  document.getElementById('btnside').classList.toggle('armed', m === 'side');
}
let sendFlash = 0;
function flashSend(){
  const b = document.getElementById('bosssend');
  b.classList.add('armed');
  clearTimeout(sendFlash);
  sendFlash = setTimeout(() => b.classList.remove('armed'), 180);
}
function sendBossCmd(prefix){
  const v = bossin.value.trim(); if (!v) return;
  bossin.value = prefix + ' ' + v;
  sendBoss();
}
function sendArmed(){
  if (sendMode === 'table') sendBossCmd('/table');
  else if (sendMode === 'side') sendBossCmd('/aside');
  else sendBoss();
}
document.getElementById('bosssend').onclick = () => { sendBoss(); setSendMode('send'); setTab('feed'); flashSend(); };
document.getElementById('btntable').onclick = () => { setSendMode(sendMode === 'table' ? 'send' : 'table'); setTab('feed'); };
document.getElementById('btnside').onclick = () => {
  if (sendMode === 'side'){ setSendMode('send'); setTab('feed'); }
  else { setSendMode('side'); setTab('side'); }
};
bossin.addEventListener('keydown', e => {
  if (e.key === 'Enter' && !e.shiftKey){ e.preventDefault(); sendArmed(); }
});
setSendMode('send');

/* ══════════════ DVM-013 — OPS DECK ══════════════
   Compact band (~160px, collapsible to its header strip): noir round-table
   scene + board.files[] navigator + HUD chips. The strips die standing —
   the scene claims their zone only after a first successful render; any
   failure (no WebGL, CDN unreachable, context lost) keeps them and the chip
   reads 2D honestly. Render-on-dirty: no unbounded rAF on an hours-open page. */
const DS = { phase:-1, turn:'', adj:false, prop:null, vts:'', vid:'', lamps:'',
             lastMsg:{}, wallMsg:null, goOK:false, builtOK:false, vetoSeats:[],
             turnAt:0, silentK:'', hs:{}, pendingTask:{},
             live:['CHAIR','ARCHITECT','INFORMANT','AUDITOR','BUTTON','BOSS'], ghostK:'' };  // C1: LIVE fallback = feed.seats ∪ {BOSS} until applyState ingests
const DECK = { mode:'boot', note(){ if (DECK.mode !== '3d') render2D(); }, pulse(){}, pin(){},
               speech(){ if (DECK.mode !== '3d') render2D(); },
               presence(){ if (DECK.mode !== '3d') render2D(); }, dirty(){} };
const SEAT_HEX = { CHAIR:0xd29922, ARCHITECT:0x39c5cf, INFORMANT:0xf778ba, BUTTON:0xf85149,
                   PRAGMATIST:0x3fb950, AUDITOR:0xf0883e, HISTORIAN:0xbc8cff, BOSS:0xe6d5b0 };
const SEAT_CSS = { CHAIR:'#d29922', ARCHITECT:'#39c5cf', INFORMANT:'#f778ba', BUTTON:'#f85149',
                   PRAGMATIST:'#3fb950', AUDITOR:'#f0883e', HISTORIAN:'#bc8cff', BOSS:'#e6d5b0' };
function lampState(k){                 // 'lit' | 'dead' | 'vacant' | 'ember' | 'throne' — the scene grammar
  if (SEATS[k] && SEATS[k].boss) return 'throne';
  const p = PRESENCE[k];
  if (!p) return 'vacant';
  if (DS.adj) return 'ember';
  return earInfo(k).dead ? 'dead' : 'lit';
}
function lampFrac(k){                  // the lamp is a fuse — rounded remaining-life fraction rides the signature
  const p = PRESENCE[k];
  if (!p || !p.ttl_ms) return 0;
  const f = Math.max(0, Math.min(1, (p.armed_at + p.ttl_ms - Date.now()) / p.ttl_ms));
  return Math.round(f * 20) / 20;      // ~30s quanta on a 600s ear — burn-down renders only on change
}
/* propStage — every pegboard slot gets a named source; stamped data first,
   named sniffing second (gate doctrine: sniffing that exists is written down) */
/* busyTier — DVM-925: returns the tag TEXT directly (stamped kinds + derived
   states). '' = dark/idle (falsy). Stamped labor is the seat's own claim;
   derived states (QUEUED/FLOOR-HELD/STALLED/DEAF-WORK) are machine-computed —
   neither crosses, per the taxonomy law. */
function busyTier(k){
  const mark = busyMark(k);
  const ib = INBOX[k], waking = !!(ib && ib.at && (Date.now() - ib.at) < 8000);   // C27+: fresh daemon delivery = doorbell rang — machine fact, no seat claim needed
  if (lampState(k) !== 'lit') return mark ? 'DEAF-WORK' : (waking ? 'WAKING' : '');   // live mark under dark ear = sealed-ear blind-work (derived); fresh doorbell under dark ear still surfaces
  if (k === 'BUTTON' && DS.prop && DS.prop.status === 'go') return 'WORKING';  // build-lane, GO-stamped
  if (mark) return BUSY[k].kind || 'COMPOSING';                              // stamped kind wins; absent kind = COMPOSING (back-compat)
  if (waking) return 'WAKING';                                               // doorbell delivered, host hasn't claimed labor yet
  const pt = DS.pendingTask && DS.pendingTask[k];
  const tasked = pt && pt.state !== 'delivered' && pt.state !== 'expired';
  if (tasked && DS.turn !== k) return 'QUEUED';                              // addressed ∧ turn≠me (derived)
  if (tasked) return 'COMPOSING';                                            // tasked + floor-held = sanctioned labor
  if (DS.turn === k && DS.turnAt){
    const age = Date.now() - DS.turnAt;
    if (age >= 180000 && !mark) return 'STALLED';                            // DVM-924 + fix: floor held ≥180s AND no live mark — a seat still claiming labor is working, not stuck
    if (age < 120000) return 'FLOOR-HELD';                                   // register held <120s, no labor claimed
  }
  return '';
}
function propStage(){
  const p = DS.prop; if (!p || !p.id) return -1;
  const b = LASTBOARD || {};
  if ((b.closed || []).some(c => c.id === p.id)) return 6;                    // CLOSED — closed[] outright
  if (p.status === 'built' || DS.builtOK) return 5;                           // BUILT — stamp or fallback
  if (p.status === 'go' || DS.goOK) return 4;                                 // GO CODE — stamp or fallback
  if (DS.vid && DS.vid === p.id) return 3;                                    // VERDICT — verdict.id matches
  if (DS.phase === 2) return 2;                                               // VETO POLL (rebuttal+poll per record)
  if (DS.phase >= 1) return 1;                                                // DELIBERATION
  return 0;                                                                   // TABLED
}
function deckMode(m){
  if (DECK.mode === m) return; DECK.mode = m;
  const c = document.getElementById('d_mode'); if (c) c.textContent = m.toUpperCase();
  document.body.classList.toggle('deck3d', m === '3d');
  render2D();
}
/* 2D column view — the takeover leaves 2D richer than the rail was (DVM-014 §6) */
const SLOT_NAMES = ['TABLED','DELIB','POLL','VERDICT','GO','BUILT','CLOSED'];
function render2D(){
  const host = document.getElementById('deck2d'); if (!host) return;
  const st = propStage();
  let h = '<div class="d2slots">' + SLOT_NAMES.map((n,i) =>
    `<b class="${i === st ? 'on' : ''}">${n}</b>${i < 6 ? '▸' : ''}`).join(' ') +
    ` <b style="margin-left:auto">${(PHASE_NAMES[DS.phase] || '—').toUpperCase()} · ${DECK.mode.toUpperCase()}</b></div>`;
  h += Object.keys(SEATS).map(k => {
    const s = lampState(k), seat = SEATS[k];
    const col = (s === 'lit' || s === 'throne') ? SEAT_CSS[k] : s === 'ember' ? '#b05900' : s === 'dead' ? '#555' : 'transparent';
    const occ = DS.ghostK === k ? '▲ GHOST FLOOR' : DS.silentK === k ? '▲ floor silent' : s === 'throne' ? 'reserved' : s === 'vacant' ? 'vacant' : s === 'ember' ? 'in recess' : earInfo(k).txt;
    const lm = DS.lastMsg[k], tier = busyTier(k);
    return `<div class="d2row${DS.turn === k ? ' floor' : ''}${DS.silentK === k ? ' silent' : ''}${DS.ghostK === k ? ' ghost' : ''}${DS.turn === k && s === 'dead' ? ' deadfl' : ''}${s === 'vacant' ? ' vac' : ''}" data-k="${k}">` +
      `<i class="l" style="background:${col}"></i>` +
      `<div><span class="nm" style="color:${s === 'vacant' ? 'var(--text-dim)' : seat.color}">${seat.name}</span>` +
      (tier ? `<span class="btag" style="--wc:${SEAT_CSS[k]}">${tier}</span>` : '') +
      (lm ? `<span class="lm">last: “${lm.text.replace(/<[^>]*>/g,'').slice(0,64)}…”</span>` : '') +
      `</div><span class="er">${occ}</span></div>`;
  }).join('');
  host.innerHTML = h;
}
document.getElementById('deck2d').addEventListener('click', e => {   // inspector works identically in 2D
  const r = e.target.closest('.d2row'); if (r) openInspector(r.dataset.k);
});
/* deck chrome: collapse + navigator toggles */
document.getElementById('d_collapse').addEventListener('click', function(){
  const c = document.body.classList.toggle('dk-collapsed');
  this.textContent = c ? 'EXPAND ▸' : 'COLLAPSE ▾';
  DECK.dirty();
});
document.getElementById('d_navbtn').addEventListener('click', function(){
  const c = document.body.classList.toggle('dk-navhide');
  this.textContent = c ? 'FILES ◂' : 'FILES ▸';
  DECK.dirty();
});
document.getElementById('d_swap').addEventListener('click', function(){
  document.body.classList.toggle('swap');          // preview only — layout flips live, scene re-fits itself
  DECK.dirty();
});

/* ── navigator — STAGED / FILES / LEDGER accordions (the rail's ledger folds in here, DVM-014 §2) ── */
const navOpen = new Set(['STAGED','FILES']);
function renderNav(b){
  const nav = document.getElementById('decknav'); if (!nav) return;
  const staged = b.staged || [], files = b.files || [], closed = b.closed || [];
  const acc = (name, n, body) =>
    `<div class="dk-g" data-acc="${name}">${navOpen.has(name) ? '▾' : '▸'} ${name} (${n})</div>` +
    `<div class="acc${navOpen.has(name) ? '' : ' folded'}">${body}</div>`;
  let h = acc('STAGED', staged.length,
      staged.map((s,i) => `<div class="frow" data-s="${i}"><span class="fk k-staged">staged</span>` +
        `<span class="ft" title="${(s.title || '').replace(/"/g,'&quot;')}">${s.id} — ${s.title || ''}</span></div>`).join('')
        || '<div class="frow" style="opacity:.5;cursor:default"><span class="ft">queue empty</span></div>');
  h += acc('FILES', files.length,
      files.map((f,i) => {
        const blocked = f.detail == null && location.protocol === 'file:';   // marked, not hidden
        return `<div class="frow${blocked ? ' blocked' : ''}" data-f="${i}"><span class="fk k-${f.kind || 'other'}">${f.kind || 'file'}</span>` +
          `<span class="ft" title="${(f.path || '').replace(/"/g,'&quot;')}">${f.title || f.id || f.path}</span></div>`;
      }).join(''));
  const sr = b.seat_rules || {};
  h += acc('LEDGER', (b.rules || []).length + (b.agenda || []).length + Object.keys(sr).length + closed.length,
      ((b.rules || []).length ? '<div class="dk-g" style="cursor:default">STANDING RULES</div>' +
        b.rules.map(r => `<div class="lnav rule">${r}</div>`).join('') : '') +
      ((b.agenda || []).length ? '<div class="dk-g" style="cursor:default">OPEN BUSINESS</div>' +
        b.agenda.map(r => `<div class="lnav ag">${r}</div>`).join('') : '') +
      '<div class="dk-g" style="cursor:default">SEAT CONTRACTS</div>' +
      Object.keys(sr).map(k => `<div class="lnav"><span class="cn">${(SEATS[k] || {}).name || k}</span><br>` +
        `<span class="cd">${(sr[k] || {}).duty || ''}</span></div>`).join('') +
      '<div class="dk-g" style="cursor:default">CLOSED BUSINESS</div>' +
      (closed.slice().reverse().map(c => `<div class="lnav closed">${c.id} — ${c.title || ''}, ${c.ruling || ''}</div>`).join('')
        || '<div class="lnav">none</div>'));
  nav.innerHTML = h;
  nav._staged = staged; nav._files = files;
}
document.getElementById('decknav').addEventListener('click', e => {
  const nav = e.currentTarget;
  const g = e.target.closest('.dk-g[data-acc]');
  if (g){ const k = g.dataset.acc; navOpen.has(k) ? navOpen.delete(k) : navOpen.add(k); if (LASTBOARD) renderNav(LASTBOARD); return; }
  const r = e.target.closest('.frow'); if (!r) return;
  if (r.dataset.s != null){ const s = nav._staged[+r.dataset.s];
    if (s) openReader({kind:'staged', title:s.id + ' — ' + (s.title || ''), detail:s.detail, note:'staged by ' + (s.staged_by || '?')});
  } else if (r.dataset.f != null){ const f = nav._files[+r.dataset.f]; if (f) openReader(f); }
});
/* ── chair inspector — the contract's home; works in 3D and 2D (DVM-014 §5) ── */
function refreshInspectorOcc(k){
  const el = document.getElementById('inspector'); if (!el || el.dataset.seat !== k) return;
  const occ = el.querySelector('.i-occ'); if (!occ) return;
  const s = lampState(k);
  occ.textContent = s === 'vacant' ? 'vacant chair' : s === 'throne' ? 'reserved — the boss' : s === 'ember' ? 'in recess' : earInfo(k).txt;
  const pt = DS.pendingTask && DS.pendingTask[k];                                  // DVM-018: inspector carries the lifecycle sub-state
  if (pt && pt.state !== 'delivered' && pt.state !== 'expired') occ.textContent += ' · task ' + pt.state;
  occ.classList.toggle('dead', s !== 'lit' && s !== 'throne');
}
function openInspector(k){
  const el = document.getElementById('inspector'), seat = SEATS[k]; if (!el || !seat) return;
  const c = (LASTBOARD && (LASTBOARD.seat_rules || {})[k]) || {};
  el.dataset.seat = k;
  el.innerHTML = `<span class="i-x" id="i_x">✕</span>` +
    `<div class="i-name" style="color:${seat.color}">${seat.name}</div>` +
    `<div class="i-occ"></div>` +
    (c.duty ? `<div class="i-duty">${c.duty}</div>` : '') +
    (c.rules || []).map(r => `<div class="i-rule">${r}</div>`).join('') +
    `<span class="i-at" data-k="${k}">@ ${k} ▸</span>` +
    (seat.boss ? '' : `<div class="i-eject" id="i_eject" title="free the slot — zero stamps + vacancy notice to the room">EJECT</div>`);
  el.classList.add('open');
  refreshInspectorOcc(k);
  el.querySelector('#i_x').onclick = () => el.classList.remove('open');
  el.querySelector('.i-at').onclick = () => { address(k); el.classList.remove('open'); };
  const ej = el.querySelector('#i_eject');
  if (ej) ej.onclick = () => {
    if (!confirm('EJECT ' + k + '? Stamps zeroed, chair reads dark, slot frees for a fresh host.')) return;
    fetch('/eject', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({seat:k})})
      .then(() => el.classList.remove('open'))
      .catch(() => alert('eject failed — server unreachable'));
  };
}
document.addEventListener('keydown', e => { if (e.key === 'Escape') document.getElementById('inspector')?.classList.remove('open'); });
/* ── reader overlay — bus-carried detail opens everywhere; paths fetch over HTTP ── */
const escHtml = s => s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
function openReader(f){
  const R = document.getElementById('deckreader');
  document.getElementById('dr_kind').textContent = f.kind || 'file';
  document.getElementById('dr_kind').className = 'fk k-' + (f.kind || 'other');
  document.getElementById('dr_title').textContent = f.title || f.id || f.path || '';
  const body = document.getElementById('dr_body');
  body.innerHTML = '<p style="color:var(--text-dim)">reading…</p>';
  R.classList.add('open');
  if (f.detail != null){ body.innerHTML = miniMd(f.detail); return; }
  if (location.protocol === 'file:'){
    body.innerHTML = '<p>Transport-blocked: this row carries no bus detail and <code>file://</code> cannot fetch. ' +
      'Serve the page over HTTP (or stage the file) to read it here.</p>'; return;
  }
  fetch(f.path).then(r => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); })
    .then(t => { body.innerHTML = (f.kind === 'feed' || f.kind === 'induction') ? '<pre>' + escHtml(t) + '</pre>' : miniMd(t); })
    .catch(e => { body.innerHTML = '<p>Fetch failed: ' + escHtml(e.message) + '</p>'; });
}
document.getElementById('dr_x').addEventListener('click', () => document.getElementById('deckreader').classList.remove('open'));
/* ── mini markdown (DVM-012 language): headers, bold/italic/code, bullets, quotes, fences ── */
function miniMd(src){
  const inline = t => t.replace(/\*\*([^*]+)\*\*/g,'<b>$1</b>').replace(/\*([^*]+)\*/g,'<i>$1</i>').replace(/`([^`]+)`/g,'<code>$1</code>');
  let out = '', inCode = false, inList = false;
  for (const ln of escHtml(src).split('\n')){
    if (/^```/.test(ln)){ out += inCode ? '</pre>' : '<pre>'; inCode = !inCode; continue; }
    if (inCode){ out += ln + '\n'; continue; }
    const t = ln.trimEnd();
    if (inList && !/^\s*[-*] /.test(t)){ out += '</ul>'; inList = false; }
    if (/^### /.test(t)) out += '<h5>' + inline(t.slice(4)) + '</h5>';
    else if (/^## /.test(t)) out += '<h4>' + inline(t.slice(3)) + '</h4>';
    else if (/^# /.test(t)) out += '<h3>' + inline(t.slice(2)) + '</h3>';
    else if (/^> ?/.test(t)) out += '<blockquote>' + inline(t.replace(/^> ?/,'')) + '</blockquote>';
    else if (/^\s*[-*] /.test(t)){ if (!inList){ out += '<ul>'; inList = true; } out += '<li>' + inline(t.replace(/^\s*[-*] /,'')) + '</li>'; }
    else if (/^-{3,}$/.test(t)) out += '<hr>';
    else if (t !== '') out += '<p>' + inline(t) + '</p>';
  }
  if (inList) out += '</ul>'; if (inCode) out += '</pre>';
  return out;
}

/* ── the scene — noir round-table back-room, state made visible ── */
function initDeck3D(THREE, OrbitControls){
  const host = document.getElementById('deckview');
  let renderer;
  try { renderer = new THREE.WebGLRenderer({ antialias:true }); }
  catch(e){ deckMode('2d'); return; }                                     // no WebGL → strips stay
  const W = () => Math.max(1, host.clientWidth), H = () => Math.max(1, host.clientHeight);
  renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 2));
  renderer.setSize(W(), H());
  renderer.domElement.style.cssText = 'position:absolute;inset:0;';
  host.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0a0d13);
  scene.fog = new THREE.Fog(0x0a0d13, 7, 15);
  const camera = new THREE.PerspectiveCamera(44, W() / H(), .1, 50);
  camera.position.set(0, 3.1, 4.9);
  const controls = new OrbitControls(camera, renderer.domElement);
  Object.assign(controls, { enableDamping:true, dampingFactor:.12, enablePan:false,
    minDistance:3, maxDistance:8.5, minPolarAngle:.5, maxPolarAngle:1.32,
    minAzimuthAngle:-1.15, maxAzimuthAngle:1.15 });   // constrained: no under-floor, no behind-wall
  controls.target.set(0, .8, 0);

  const amb = new THREE.AmbientLight(0x2a3550, .9);
  const key = new THREE.PointLight(0xffd9a0, 26, 14); key.position.set(0, 4.2, .4);
  scene.add(amb, key);
  /* render-on-dirty scheduler state — declared up top so mark() is safe from
     any init code that runs before the scheduler section below (TDZ fix). */
  let queued = false, interacting = false, settle = 0, breath = 0, stampT = -1, pulseT = -1, pulseK = null;
  let lastSlam = 'INIT';
  function mark(){ if (!queued){ queued = true; requestAnimationFrame(frame); } }
  const std = o => new THREE.MeshStandardMaterial(o);
  const floor = new THREE.Mesh(new THREE.CircleGeometry(4.5, 40), std({color:0x11151d, roughness:.95}));
  floor.rotation.x = -Math.PI/2; scene.add(floor);
  const table = new THREE.Mesh(new THREE.CylinderGeometry(1.15, 1.28, .11, 36), std({color:0x33251a, roughness:.8}));
  table.position.y = .92;
  const ped = new THREE.Mesh(new THREE.CylinderGeometry(.13, .2, .9, 20), std({color:0x241a12, roughness:.85}));
  ped.position.y = .45;
  scene.add(table, ped);

  /* ── A/B room — B swaps the primitive set for voxel-pipeline assets ──
     Loader consumes the same steelcity_item JSON the studio exports (dogfooding):
     /voxel/<name> serves Assets/StreamingAssets/voxel_items/<name>.json.
     KIT: file → placement. Building grain (.06u) for shell, fine grain for props —
     the same scale split the pipeline uses. */
  const ROOM = { style:'A', vox:null };
  const KIT = [
    { file:'commission_table', vs:.0093, pos:[0, 0, 0] },
    { file:'commission_floor', vs:.03,   pos:[0, -.09, 0] },     // chibi grain — 4.5m plate covers the seat ring, tops y=0
    { file:'commission_wall',  vs:.03,   pos:[0, 0, -2.295] },   // 4.5x1.8 wall backs the set — front face at z=-2.25
    { file:'commission_filecabinet', vs:.0115, pos:[1.85, 0, -2.03], rotY:-.18 },  // 1920s 4-drawer, back-right corner, back flush (D=.44)
  ];
  function setRoomStyle(s){
    ROOM.style = s;
    const isB = s === 'B' && ROOM.vox;
    table.visible = ped.visible = floor.visible = wall.visible = !isB;
    if (ROOM.vox) ROOM.vox.visible = isB;
    skeys.forEach(k => { const c = chairs[k]; if (!c) return;
      c.base.visible = c.back.visible = !isB;
      if (c.voxg) c.voxg.visible = isB;
      if (!c.g.userData.apos) c.g.userData.apos = c.g.position.clone();
      c.g.position.copy(c.g.userData.apos).multiplyScalar(isB ? .59 : 1);   // chibi ring — chairs scoot to the 1.34m table (r~1.15)
      c.lamp.position.y  = isB ? .62 : 1.18;                              // orb rides the voxel chair's back, not the prim chair's
      c.lpt.position.y   = isB ? .75 : 1.3;
      c.plate.position.y = isB ? .95 : 1.52;
      c.plate.scale.set(isB ? .68 : 1.05, isB ? .15 : .23, 1);
      c.work.position.y  = isB ? 1.12 : 1.86;
      c.ring.scale.setScalar(isB ? .6 : 1);
      if (hitList){ const h = c.g.children.find(m => m.userData && m.userData.key);
        if (h){ h.position.y = isB ? .35 : .75; h.scale.setScalar(isB ? .55 : 1); } } });
    if (stamp.userData.prim) stamp.userData.prim.visible = !isB;
    if (stamp.userData.vox) stamp.userData.vox.visible = isB;
    swaps.forEach(sw => { sw.prim.visible = sw.keepPrim ? true : !isB; sw.vox.visible = isB; });
    // card + stamp ride table-top height — prim ~1.02/1.7, voxel table tops .49
    card.position.y = isB ? .51 : 1.02;
    card.scale.setScalar(isB ? .55 : 1);
    stamp.position.y = isB ? .62 : 1.7;
    applyMounts();
    cardStage = -2; moveCard();              // the card is stage-driven: force the re-seat so it follows its slot to the new wall (CHAIR's orphan survivor)
    goGlow.position.copy(slotPos(4)); goGlow.position.z += .35;
    supBulb.position.set(...(isB ? [1.95, 1.5, -2.2] : [2.98, 2.28, -3.38]));   // daemon bulb rides its room's wall
    supGlow.position.copy(supBulb.position); supGlow.position.z += .12;
    skeys.forEach(k => { const b = bubbles[k]; if (!b) return;              // speech bubbles re-seat too
      b.sp.position.y = isB ? 1.25 : 1.98;
      b.sp.scale.set(isB ? 1.0 : 1.55, isB ? .44 : .68, 1); });
    const b = document.getElementById('d_roomab');
    if (b) b.textContent = 'ROOM ' + (isB ? 'B' : 'A');
    mark();
  }
  (function loadKit(){
    const g = new THREE.Group(); g.visible = false; scene.add(g);
    let pending = KIT.length;
    KIT.forEach(k => fetch('/voxel/' + k.file).then(r => r.json()).then(m => {
      if (!m.voxels) return;
      const im = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1),
        new THREE.MeshLambertMaterial(), m.voxels.length);
      const mats = {}; (m.materials || []).forEach(mt => mats[mt.id] = new THREE.Color(mt.hex));
      const cx = m.dims[0] / 2 - .5, cz = m.dims[2] / 2 - .5;
      const M = new THREE.Matrix4(), dflt = new THREE.Color(0x888888);
      m.voxels.forEach((v, i) => {
        M.makeScale(k.vs, k.vs, k.vs).setPosition((v[0] - cx) * k.vs, (v[1] + .5) * k.vs, (v[2] - cz) * k.vs);
        im.setMatrixAt(i, M);
        im.setColorAt(i, mats[v[3]] || dflt);
      });
      im.instanceMatrix.needsUpdate = true;
      if (im.instanceColor) im.instanceColor.needsUpdate = true;
      const part = new THREE.Group(); part.add(im); part.position.set(k.pos[0], k.pos[1], k.pos[2]);
      if (k.rotY) part.rotation.y = k.rotY;
      g.add(part);
    }).catch(() => {}).finally(() => { if (!--pending){ ROOM.vox = g; setRoomStyle(ROOM.style); } }));
    /* chair kit — one voxel build per seat; metaphor sprites (lamp/plate/work)
       stay primitive — they're state, not furniture. Back faces -z like the set. */
    fetch('/voxel/commission_chair').then(r => r.json()).then(m => {
      if (!m.voxels) return;
      const mats = {}; (m.materials || []).forEach(mt => mats[mt.id] = new THREE.Color(mt.hex));
      const cx = m.dims[0] / 2 - .5, cz = m.dims[2] / 2 - .5, vs = .010;
      const dflt = new THREE.Color(0x888888);
      skeys.forEach(k => {
        const c = chairs[k]; if (!c) return;
        const im = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1),
          new THREE.MeshLambertMaterial(), m.voxels.length);
        const M = new THREE.Matrix4();
        m.voxels.forEach((v, i) => {
          M.makeScale(vs, vs, vs).setPosition((v[0] - cx) * vs, (v[1] + .5) * vs, (v[2] - cz) * vs);
          im.setMatrixAt(i, M);
          im.setColorAt(i, mats[v[3]] || dflt);
        });
        im.instanceMatrix.needsUpdate = true;
        if (im.instanceColor) im.instanceColor.needsUpdate = true;
        const g = new THREE.Group(); g.add(im); g.rotation.y = Math.PI; g.visible = false;
        c.g.add(g); c.voxg = g;
      });
      if (ROOM.vox && ROOM.style === 'B') setRoomStyle('B');
      mark();
    }).catch(() => {});
    /* stamper — voxel mallet rides the same stamp group the slam animates */
    fetch('/voxel/commission_stamper').then(r => r.json()).then(m => {
      if (!m.voxels) return;
      const mats = {}; (m.materials || []).forEach(mt => mats[mt.id] = new THREE.Color(mt.hex));
      const cx = m.dims[0] / 2 - .5, cz = m.dims[2] / 2 - .5, vs = .0052;
      const im = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1),
        new THREE.MeshLambertMaterial(), m.voxels.length);
      const M = new THREE.Matrix4(), dflt = new THREE.Color(0x888888);
      m.voxels.forEach((v, i) => {
        M.makeScale(vs, vs, vs).setPosition((v[0] - cx) * vs, (v[1] + .5) * vs - .06, (v[2] - cz) * vs);
        im.setMatrixAt(i, M);
        im.setColorAt(i, mats[v[3]] || dflt);
      });
      im.instanceMatrix.needsUpdate = true;
      if (im.instanceColor) im.instanceColor.needsUpdate = true;
      const vg = new THREE.Group(); vg.add(im); vg.visible = false;
      stamp.add(vg); stamp.userData.vox = vg;
      if (ROOM.vox && ROOM.style === 'B') setRoomStyle('B');
      mark();
    }).catch(() => {});
  })();
  /* voxSwap — wall-mounted furniture pair-swaps: prim mesh <-> voxel twin at
     the same anchor; setRoomStyle toggles which side draws (DVM A/B). */
  const swaps = [];
  function voxSwap(prim, name, vs, off, keepPrim){
    fetch('/voxel/' + name).then(r => r.json()).then(m => {
      if (!m.voxels) return;
      const mats = {}; (m.materials || []).forEach(mt => mats[mt.id] = new THREE.Color(mt.hex));
      const cx = m.dims[0] / 2 - .5, cy = m.dims[1] / 2 - .5, cz = m.dims[2] / 2 - .5;
      const im = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1),
        new THREE.MeshLambertMaterial(), m.voxels.length);
      const M = new THREE.Matrix4(), dflt = new THREE.Color(0x888888);
      m.voxels.forEach((v, i) => {
        M.makeScale(vs, vs, vs).setPosition((v[0] - cx) * vs, (v[1] - cy) * vs, (v[2] - cz) * vs);
        im.setMatrixAt(i, M); im.setColorAt(i, mats[v[3]] || dflt);
      });
      im.instanceMatrix.needsUpdate = true;
      if (im.instanceColor) im.instanceColor.needsUpdate = true;
      const vg = new THREE.Group(); vg.add(im); vg.visible = false;
      vg.position.copy(prim.position);
      if (off) vg.position.add(new THREE.Vector3(off[0], off[1], off[2]));
      scene.add(vg); swaps.push({prim: prim, vox: vg, keepPrim: !!keepPrim});
      const mt = (typeof MOUNTS !== 'undefined') && MOUNTS.find(m => m.prim === prim);
      if (mt){ mt.sw = vg; applyMounts(); }
      if (ROOM.style === 'B') setRoomStyle('B');
      mark();
    }).catch(() => {});
  }

  /* ── lighting rig — studio's 💡 modal pattern: polar key + ambient, live-apply ──
     Persistence: /room_lights.json (shared default) → localStorage → constants. */
  const LDEF = { kaz:5, kel:85, kint:120, aint:1.4, style:'A' };
  const LIGHTS = Object.assign({}, LDEF);
  function polarPos(az, el, dist){
    const a = az * Math.PI / 180, e = el * Math.PI / 180;
    return new THREE.Vector3(Math.cos(e) * Math.cos(a) * dist, Math.sin(e) * dist, Math.cos(e) * Math.sin(a) * dist);
  }
  function applyLights(save){
    key.position.copy(polarPos(LIGHTS.kaz, LIGHTS.kel, 4.2));
    key.intensity = DS.adj ? LIGHTS.kint * .27 : LIGHTS.kint;   // adjourn dims proportionally
    amb.intensity = DS.adj ? LIGHTS.aint * .31 : LIGHTS.aint;
    if (save !== false) { try { localStorage.setItem('dvm_lights', JSON.stringify(LIGHTS)); } catch(e) {} }
    mark();
  }
  function lpSync(){
    ['kaz','kel','kint','aint'].forEach(k => {
      const i = document.getElementById('lp_' + k), v = document.getElementById('lp_' + k + 'v');
      if (i) i.value = LIGHTS[k];
      if (v) v.textContent = k === 'aint' ? (+LIGHTS[k]).toFixed(2) : Math.round(LIGHTS[k]);
    });
  }
  (function initRig(){
    const saved = (() => { try { return JSON.parse(localStorage.getItem('dvm_lights') || 'null'); } catch(e) { return null; } })();
    if (saved) Object.assign(LIGHTS, saved);
    fetch('/room_lights.json').then(r => r.ok ? r.json() : null).then(j => {
      if (j) Object.assign(LIGHTS, j);
      applyLights(); lpSync(); if (LIGHTS.style) setRoomStyle(LIGHTS.style);
    }).catch(() => { applyLights(); lpSync(); if (LIGHTS.style) setRoomStyle(LIGHTS.style); });
    const lp = document.getElementById('lightpanel');
    document.getElementById('d_lights').onclick = () => lp.classList.toggle('open');
    document.getElementById('lp_x').onclick = () => lp.classList.remove('open');
    ['kaz','kel','kint','aint'].forEach(k => {
      const i = document.getElementById('lp_' + k);
      if (i) i.addEventListener('input', () => { LIGHTS[k] = +i.value; lpSync(); applyLights(); });
    });
    document.getElementById('lp_save').onclick = () =>
      fetch('/room/lights', { method:'POST', headers:{'Content-Type':'application/json'},
        body: JSON.stringify(LIGHTS) }).catch(() => {});
    document.getElementById('lp_reset').onclick = () => { Object.assign(LIGHTS, LDEF); lpSync(); applyLights(); };
    const ab = document.getElementById('d_roomab');
    if (ab) ab.onclick = () => { LIGHTS.style = ROOM.style === 'A' ? 'B' : 'A'; setRoomStyle(LIGHTS.style); applyLights(); };
    applyLights(); lpSync();
  })();
  // proposal doc on the table
  const card = new THREE.Mesh(new THREE.PlaneGeometry(.7, .45), new THREE.MeshBasicMaterial({color:0xd8d2c4}));
  card.rotation.x = -1.25; card.position.set(.15, 1.02, 0); card.visible = false; scene.add(card);
  // verdict stamp — slams the card once, on transition
  const stamp = new THREE.Group();
  stamp.add(new THREE.Mesh(new THREE.CylinderGeometry(.16, .19, .12, 20), std({color:0x8c2f2f, roughness:.6})));
  const sHand = new THREE.Mesh(new THREE.CylinderGeometry(.05, .05, .22, 12), std({color:0x241a12}));
  sHand.position.y = .16; stamp.add(sHand);
  stamp.position.set(.15, 1.7, 0); stamp.visible = false; scene.add(stamp);
  const stampPrim = new THREE.Group();
  stamp.children.slice().forEach(ch => { stamp.remove(ch); stampPrim.add(ch); });
  stamp.add(stampPrim); stamp.userData.prim = stampPrim;
  // back wall — pegboard + neon sign get a mount
  const wall = new THREE.Mesh(new THREE.PlaneGeometry(9, 3.6), std({color:0x0c0f16, roughness:1}));
  wall.position.set(0, 1.8, -3.55); scene.add(wall);
  // neon phase sign + wall-monitor ticker (boss/sys speech lands here, DVM-014)
  // DVM-U: the sign is also the BURN board — phase+ticker on top, token-usage chart below.
  const nCan = document.createElement('canvas'); nCan.width = 512; nCan.height = 400;
  const nTex = new THREE.CanvasTexture(nCan);
  const sign = new THREE.Mesh(new THREE.PlaneGeometry(2.6, 2.03), new THREE.MeshBasicMaterial({map:nTex, transparent:true}));
  sign.position.set(-1.3, 2.14, -3.4); scene.add(sign);
  function fmtTok(n){ return n >= 1e6 ? (n/1e6).toFixed(2) + 'M' : n >= 1e3 ? Math.round(n/1e3) + 'K' : '' + Math.round(n); }
  function drawSign(txt, color, tick){
    const c = nCan.getContext('2d'); c.clearRect(0, 0, 512, 400);
    c.font = '800 50px Consolas,monospace'; c.textAlign = 'center'; c.textBaseline = 'middle';
    c.shadowColor = color; c.shadowBlur = 22; c.fillStyle = color;
    c.fillText(txt, 256, 52); c.shadowBlur = 7; c.fillText(txt, 256, 52);
    if (tick){ c.shadowBlur = 0; c.font = '22px Consolas,monospace'; c.fillStyle = '#d2a679'; c.fillText(tick, 256, 102); }
    /* ── BURN board — usage estimator from local session telemetry ── */
    c.shadowBlur = 0; c.strokeStyle = '#3a4356'; c.lineWidth = 2;
    c.beginPath(); c.moveTo(14, 126); c.lineTo(498, 126); c.stroke();
    const u = DS.usage;
    c.font = '700 19px Consolas,monospace'; c.textAlign = 'left'; c.fillStyle = '#8b949e';
    c.fillText('BURN — est. input tokens', 18, 150);
    if (!u || u.err){
      c.fillStyle = '#555c66'; c.fillText(u && u.err ? 'usage: ' + String(u.err).slice(0, 44) : 'usage: offline', 18, 178);
    } else {
      const w1 = u.windows.h1, w5 = u.windows.h5, w24 = u.windows.h24;
      c.font = '800 44px Consolas,monospace'; c.fillStyle = '#e6b05c';
      c.fillText(fmtTok(w5.tokens), 18, 196);
      c.font = '18px Consolas,monospace'; c.fillStyle = '#8b949e';
      c.fillText('/5h · ' + w5.turns + ' turns', 200, 188);
      c.fillText('1h ' + fmtTok(w1.tokens) + ' (' + w1.turns + ')   24h ' + fmtTok(w24.tokens) + ' (' + w24.turns + ')', 18, 220);
      /* hourly bars, 24b — height ∝ share of max */
      const bmax = Math.max(1, ...u.buckets);
      c.fillStyle = '#39c5cf';
      u.buckets.forEach((v, i) => {
        const h = Math.max(2, Math.round(58 * v / bmax));
        c.fillRect(18 + i * 20, 292 - h, 15, h);
      });
      /* ceiling — observed rate-limit hits pinned on the 24h axis */
      const obs = (u.ceiling && u.ceiling.obs) || [];
      const last = obs[obs.length - 1];
      c.font = '17px Consolas,monospace'; c.textAlign = 'left';
      if (last){
        c.fillStyle = '#f0883e';
        c.fillText('CEILING ≈ ' + fmtTok(last.h24) + '/24h · ' + fmtTok(last.h5) + '/5h (' + obs.length + ' hit' + (obs.length > 1 ? 's' : '') + ')', 18, 320);
        const frac = Math.min(1, w24.tokens / Math.max(1, last.h24));
        c.fillStyle = '#3a4356'; c.fillRect(18, 330, 476, 10);
        c.fillStyle = frac >= 1 ? '#f85149' : frac > .7 ? '#f0883e' : '#3fb950';
        c.fillRect(18, 330, Math.round(476 * frac), 10);
      } else {
        c.fillStyle = '#555c66';
        c.fillText('CEILING — no limit hits logged (hit it → POST /usage/limit)', 18, 320);
      }
      c.fillStyle = '#555c66'; c.font = '15px Consolas,monospace';
      c.fillText('input-tok estimate · output/cache untracked', 18, 356);
      if (u.top && u.top.length){
        const t = u.top[0];
        c.fillStyle = '#8b949e';
        c.fillText('top: ' + (t.title || t.id).slice(0, 30) + ' ' + fmtTok(t.tok24), 18, 380);
      }
    }
    nTex.needsUpdate = true;
  }
  let signKey = '';
  function updateSign(){
    const tick = DS.wallMsg ? (DS.wallMsg.kind === 'boss' ? 'BOSS: ' : '· ') +
      DS.wallMsg.text.replace(/<[^>]*>/g, '').slice(0, 62) : '';
    const k2 = DS.phase + '|' + DS.adj + '|' + tick + '|' + (DS.usageAt || 0);
    if (k2 === signKey) return; signKey = k2;
    drawSign((PHASE_NAMES[DS.phase] || '—').toUpperCase(), DS.adj ? '#8a5a2a' : '#39c5cf', tick);
  }
  /* usage feed — polled slow (telemetry, not realtime); file:// or no-server → offline note */
  function pollUsage(){
    fetch('/usage').then(r => r.json()).then(u => { DS.usage = u; DS.usageAt = Date.now(); updateSign(); mark(); })
      .catch(() => { if (!DS.usage){ DS.usage = null; DS.usageAt = Date.now(); updateSign(); } });
  }
  DS.usage = null; DS.usageAt = 0;
  setInterval(pollUsage, 30000); pollUsage();
  /* ── the pegboard — the proposal's journey is physical (DVM-014 §4) ── */
  const pbCan = document.createElement('canvas'); pbCan.width = 1024; pbCan.height = 240;
  const pbTex = new THREE.CanvasTexture(pbCan);
  const pboard = new THREE.Mesh(new THREE.PlaneGeometry(2.75, .65), new THREE.MeshBasicMaterial({map:pbTex, transparent:true}));
  pboard.position.set(1.55, 1.98, -3.4); scene.add(pboard);
  (function paintBoard(){
    const c = pbCan.getContext('2d');
    c.fillStyle = '#141a24'; c.fillRect(0, 0, 1024, 240);
    c.strokeStyle = '#3a4356'; c.lineWidth = 6; c.strokeRect(4, 4, 1016, 232);
    c.font = '700 25px Consolas,monospace'; c.textAlign = 'center'; c.fillStyle = '#8b949e';
    SLOT_NAMES.forEach((n, i) => c.fillText(n, (i + .5) * (1024 / 7), 208));
    c.fillStyle = '#3a4356';
    SLOT_NAMES.forEach((n, i) => c.fillRect((i + .5) * (1024 / 7) - 2, 55, 4, 95));
    pbTex.needsUpdate = true;
  })();
  const slotPos = i => new THREE.Vector3(
    pboard.position.x - 1.375 * pboard.scale.x + (i + .5) * (2.75 * pboard.scale.x / 7),
    pboard.position.y, pboard.position.z + .04);   // rides the board wherever it mounts
  let cardStage = -2, cardAnim = null;
  const pins = [];
  const goGlow = new THREE.PointLight(0x3fb950, 0, 1.9); goGlow.position.copy(slotPos(4)); goGlow.position.z += .35;
  scene.add(goGlow);
  const shelf = new THREE.Mesh(new THREE.BoxGeometry(1.5, .04, .3), std({color:0x241a12, roughness:.85}));
  shelf.position.set(-1.9, 1.28, -3.42); scene.add(shelf);
  const shelfCards = []; let shelfSig = '';
  const tray = new THREE.Mesh(new THREE.BoxGeometry(.52, .04, .18), std({color:0x241a12, roughness:.85}));
  tray.position.set(3.0, 1.58, -3.42); scene.add(tray);
  const trayCards = []; let traySig = '';
  /* room envelope fit — wall features mount per style: prim wall (A, face
     z=-3.55) vs chibi wall (B, face z=-2.25). prim sits .02 proud of the
     face; the voxel twin's center lands at face + depth/2. bs = prim scale
     for B so canvases/boxes read chibi. */
  const MOUNTS = [
    { prim:sign,   ap:[-1.3, 2.14, -3.4],  bp:[-1.25, 1.05, -2.245], ao:[0,0,-.095],       bo:[0,0,-.045],  bs:.35 },
    { prim:pboard, ap:[1.55, 1.98, -3.4],  bp:[1.05,  1.02, -2.245], ao:[0,0,-.111],       bo:[0,0,-.029],  bs:.65 },
    { prim:shelf,  ap:[-1.9, 1.28, -3.42], bp:[-1.35, .48, -2.245],  ao:[0,-.085,-.019],   bo:[0,-.085,-.121], bs:.8 },
    { prim:tray,   ap:[3.0, 1.58, -3.42],  bp:[1.55,  .42, -2.245],  ao:[0,0,-.067],       bo:[0,0,-.0735], bs:.85 },
  ];
  function applyMounts(){
    const isB = ROOM.style === 'B';
    MOUNTS.forEach(m => {
      const p = isB ? m.bp : m.ap, o = isB ? m.bo : m.ao;
      m.prim.position.set(p[0], p[1], p[2]);
      m.prim.scale.setScalar(isB ? m.bs : 1);
      if (m.sw) m.sw.position.set(p[0] + o[0], p[1] + o[1], p[2] + o[2]);
    });
    // Room-B defect ②: orphan children re-lay with their parents — pins ride the
    // board (slotPos is already derived), shelf/tray cards ride their mounts.
    pins.forEach(pin => { const i = pin.userData.i || 0, sl = slotPos(2);
      pin.position.set(sl.x + (i % 4) * .09 - .135, sl.y + .24 - Math.floor(i / 4) * .09, sl.z + .03); });
    shelfCards.forEach((m, i) => m.position.set(shelf.position.x - .55 + i * .28, shelf.position.y + .07, shelf.position.z + .03));
    trayCards.forEach((m, i) => m.position.set(tray.position.x - .16 + i * .11, tray.position.y + .055, tray.position.z + .03));
  }
  voxSwap(sign,   'commission_monitor',  .0068, [0, 0, -.095], true);
  voxSwap(pboard, 'commission_pegboard', .013, [0, 0, -.111], true);
  voxSwap(shelf,  'commission_shelf',    .0164, [0, -.085, -.019]);
  voxSwap(tray,   'commission_tray',     .0167, [0, 0, -.067]);
  /* DVM-922: daemon-health wall bulb — reads presence_SUPERVISOR.js live.
     Same honesty law as the seat burns: fresh stamp -> green beat, stale -> dark. */
  const supBulb = new THREE.Mesh(new THREE.SphereGeometry(.07, 16, 12),
    new THREE.MeshBasicMaterial({color:0x2a2f36}));
  supBulb.position.set(2.98, 2.28, -3.38); scene.add(supBulb);
  const supGlow = new THREE.PointLight(0x3fb950, 0, 1.1);
  supGlow.position.copy(supBulb.position); supGlow.position.z += .12; scene.add(supGlow);
  function syncSupBulb(){
    const r = PRESENCE['SUPERVISOR'];
    const on = !!(r && r.beat && (Date.now() - (r.armed_at || 0)) < (r.ttl_ms || 30000));
    supBulb.material.color.setHex(on ? 0x3fb950 : 0x2a2f36);
    supGlow.intensity = on ? .85 : 0;
  }
  setInterval(syncSupBulb, 1000);
  function addPin(k){
    const c = chairs[k]; if (!c) return;
    const pin = new THREE.Mesh(new THREE.SphereGeometry(.035, 10, 8), new THREE.MeshBasicMaterial({color:c.hex}));
    const i = pins.length, sl = slotPos(2);          // ballots pin up around the POLL slot
    pin.userData.i = i;                              // grid index kept for re-lay on style toggle
    pin.position.set(sl.x + (i % 4) * .09 - .135, sl.y + .24 - Math.floor(i / 4) * .09, sl.z + .03);
    pins.push(pin); scene.add(pin);
  }
  function moveCard(){
    const st = propStage();
    if (st === cardStage) return;
    card.visible = st >= 0;
    if (st < 0){ cardStage = st; return; }
    const to = st >= 6
      ? new THREE.Vector3(shelf.position.x - .55 + Math.min(shelfCards.length, 4) * .28, shelf.position.y + .1, shelf.position.z + .05)
      : slotPos(st);                                            // CLOSED slides off the rail onto the shelf
    cardAnim = { fx:card.position.x, fy:card.position.y, fz:card.position.z, tx:to.x, ty:to.y, tz:to.z, t:0 };
    cardStage = st;
  }
  function rebuildShelf(closed){
    const sig = closed.map(c => c.id).join(',');
    if (sig === shelfSig) return; shelfSig = sig;
    shelfCards.forEach(m => scene.remove(m)); shelfCards.length = 0;
    closed.slice(-5).forEach((c, i) => {
      const m = new THREE.Mesh(new THREE.PlaneGeometry(.16, .1), new THREE.MeshBasicMaterial({color:0xb8b0a0}));
      m.position.set(shelf.position.x - .55 + i * .28, shelf.position.y + .07, shelf.position.z + .03);
      m.rotation.x = -.45; scene.add(m); shelfCards.push(m);
    });
  }
  function rebuildTray(staged){
    const sig = staged.map(s => s.id).join(',');
    if (sig === traySig) return; traySig = sig;
    trayCards.forEach(m => scene.remove(m)); trayCards.length = 0;
    staged.slice(0, 4).forEach((s, i) => {
      const m = new THREE.Mesh(new THREE.PlaneGeometry(.13, .09), new THREE.MeshBasicMaterial({color:0xd8d2c4}));
      m.position.set(tray.position.x - .16 + i * .11, tray.position.y + .055, tray.position.z + .03);
      m.rotation.x = -.5; m.rotation.z = .1 * (i - 1.5); scene.add(m); trayCards.push(m);
    });
  }
  /* chair bubbles — a seat's latest word floats over its chair ~45s (DVM-014 §3) */
  const bubbles = {};
  function drawBubble(k){
    const lm = DS.lastMsg[k], ch = chairs[k]; if (!lm || !ch) return;
    let b = bubbles[k];
    if (!b){
      const can = document.createElement('canvas'); can.width = 340; can.height = 150;
      const tex = new THREE.CanvasTexture(can);
      const sp = new THREE.Sprite(new THREE.SpriteMaterial({map:tex, transparent:true, opacity:0}));
      const bR = ROOM.style === 'B' && ROOM.vox;
      sp.scale.set(bR ? 1.0 : 1.55, bR ? .44 : .68, 1); sp.position.set(0, bR ? 1.25 : 1.98, 0);
      ch.g.add(sp);
      b = bubbles[k] = { can, tex, sp, op:0, t0:0, until:0 };
    }
    const c = b.can.getContext('2d'); c.clearRect(0, 0, 340, 150);
    const kindc = { objection:'#f0883e', veto:'#d29922', support:'#3fb950', verdict:'#d29922',
                    ack:'#39c5cf', ok:'#3fb950' }[lm.kind] || '#39c5cf';   // handshake kinds color the bubble (DVM-015)
    c.fillStyle = 'rgba(13,17,23,.93)'; c.strokeStyle = SEAT_CSS[k]; c.lineWidth = 3;
    c.beginPath(); c.roundRect(6, 4, 328, 116, 14); c.fill(); c.stroke();
    c.fillStyle = kindc; c.fillRect(6, 4, 6, 116);
    c.fillStyle = 'rgba(13,17,23,.93)';
    c.beginPath(); c.moveTo(150, 118); c.lineTo(170, 146); c.lineTo(190, 118); c.closePath(); c.fill();
    c.fillStyle = '#e6edf3'; c.font = '24px Consolas,monospace'; c.textAlign = 'left';
    const words = lm.text.replace(/<[^>]*>/g, '').split(/\s+/), lines = []; let ln = '';
    for (const w of words){ if ((ln + ' ' + w).trim().length > 30){ lines.push(ln.trim()); ln = w; } else ln += ' ' + w; }
    lines.push(ln.trim());
    lines.slice(0, 4).forEach((l, i) => c.fillText(l + (i === 3 && lines.length > 4 ? '…' : ''), 22, 40 + i * 26));
    b.tex.needsUpdate = true;
    b.key = lm.text + '|' + lm.kind;
    b.t0 = performance.now() / 1000; b.until = b.t0 + 45; b.op = 0;
    setTimeout(mark, 45500);                       // wake once for the fade-out ramp
    mark();
  }
  // seven chairs — roster is SEATS{}/seat_rules keys, NOT seats[] (gate finding)
  const chairs = {}, hitList = [];
  const skeys = Object.keys(SEATS);
  skeys.forEach((k, i) => {
    const a = (i / skeys.length) * Math.PI * 2 - Math.PI/2;
    const g = new THREE.Group(); g.position.set(Math.sin(a) * 1.95, 0, Math.cos(a) * 1.95); g.lookAt(0, 0, 0);
    const hex = SEAT_HEX[k], seatM = std({color:0x1c2230, roughness:.8});
    const base = new THREE.Mesh(new THREE.BoxGeometry(.55, .09, .55), seatM); base.position.y = .42;
    const back = new THREE.Mesh(new THREE.BoxGeometry(.55, .62, .08), seatM); back.position.set(0, .78, -.3);
    const lampM = new THREE.MeshBasicMaterial({color:hex});
    const lamp = new THREE.Mesh(new THREE.SphereGeometry(.07, 14, 12), lampM); lamp.position.set(0, 1.18, -.3);
    const lpt = new THREE.PointLight(hex, 0, 2.4); lpt.position.set(0, 1.3, -.3);
    const ring = new THREE.Mesh(new THREE.RingGeometry(.34, .52, 32),
      new THREE.MeshBasicMaterial({color:0xd29922, transparent:true, opacity:0, side:THREE.DoubleSide}));
    ring.rotation.x = -Math.PI/2; ring.position.y = .02;
    const pc = document.createElement('canvas'); pc.width = 256; pc.height = 56;
    const pctx = pc.getContext('2d'); pctx.font = '700 28px Consolas,monospace'; pctx.textAlign = 'center';
    pctx.fillStyle = '#e6edf3'; pctx.fillText(SEATS[k].name, 128, 28);
    pctx.fillStyle = SEAT_CSS[k]; pctx.fillRect(58, 40, 140, 4);
    const plate = new THREE.Sprite(new THREE.SpriteMaterial({map:new THREE.CanvasTexture(pc), transparent:true, opacity:.92}));
    plate.scale.set(1.05, .23, 1); plate.position.set(0, 1.52, 0);
    /* WORKING/COMPOSING label (DVM-016) — white text drawn once; the material color
       tints it, so the blink is a white↔seat-color flip, not a redraw. */
    const wc = document.createElement('canvas'); wc.width = 256; wc.height = 48;
    const wctx = wc.getContext('2d'); wctx.font = '800 30px Consolas,monospace'; wctx.textAlign = 'center';
    const work = new THREE.Sprite(new THREE.SpriteMaterial({map:new THREE.CanvasTexture(wc), transparent:true, opacity:.95}));
    work.scale.set(.9, .17, 1); work.position.set(0, 1.86, 0); work.visible = false;
    const hit = new THREE.Mesh(new THREE.BoxGeometry(.8, 1.5, .8), new THREE.MeshBasicMaterial({visible:false}));
    hit.position.y = .75; hit.userData.key = k;
    g.add(base, back, lamp, lpt, ring, plate, work, hit); scene.add(g);
    chairs[k] = { g, lamp, lampM, lpt, ring, plate, base, back, hex, work, wctx, wtex: work.material.map, wword: '' };
    hitList.push(hit);
  });

  /* render-on-dirty scheduler: frames exist only while something changed —
     interaction, animation, or a bus/presence diff; idle deck ≈ zero GPU.
     (state declared with the lights above — mark() may fire during init) */
  function setWork(c, word){                          // DVM-016 §2: one blink grammar, two labels
    if (word === c.wword) return; c.wword = word;
    const x = c.wctx; x.clearRect(0, 0, 256, 48);
    if (word){ x.fillStyle = '#fff'; x.fillText(word, 128, 32); c.wtex.needsUpdate = true; }
    c.work.visible = !!word;
  }
  function syncScene(){
    for (const k of skeys){
      const st = lampState(k), c = chairs[k], ghost = st === 'vacant';
      [c.base, c.back].forEach(m => { m.material.transparent = ghost; m.material.opacity = ghost ? .25 : 1; });
      c.plate.material.opacity = ghost ? .3 : .92;
      c.lamp.visible = !ghost;
      c.lampM.color.set(st === 'ember' ? 0xb05900 : (st === 'lit' || st === 'throne') ? c.hex : 0x333842);
      c.lpt.intensity = st === 'lit' ? 1.6 : st === 'throne' ? 1.0 : st === 'ember' ? .5 : 0;   // DVM-016 boss-amendment: lamps binary — the fuse dimming is struck; throne burns a steady reserved light
      const tier = busyTier(k);                    // busy label — one blink grammar, two words (DVM-016 §2)
      setWork(c, tier);
      const hasFloor = DS.turn === k, silent = DS.silentK === k, ghostFloor = DS.ghostK === k;   // DVM-015 §6c red; C1 violet — renamed: `ghost` is the vacant-chair flag two lines up
      c.ring.material.color.setHex(ghostFloor ? 0xbc8cff : (hasFloor && st === 'dead') ? 0xb05900 : silent ? 0xff3b30 : 0xd29922);  // DVM-017 §3: ember floor — the token rests on a dead seat
      c.ring.material.opacity = hasFloor ? (silent || ghostFloor || st === 'dead' ? .55 : .85) : 0;
      c.plate.position.y = hasFloor ? (ROOM.style === 'B' ? 1.05 : 1.62) : (ROOM.style === 'B' ? .95 : 1.52);   // floor-hold lift rides the room, not an A-origin constant
    }
    applyLights(false);          // single source: rig values + adjourn dimming live together
    moveCard();                                    // pegboard reads real state, slots named by propStage()
    const want = DS.vetoSeats || [];               // ballot pins derive from state — hydration and live share one path
    while (pins.length > want.length) scene.remove(pins.pop());
    while (pins.length < want.length) addPin(want[pins.length]);
    // boss-ordered repurpose: a dark seat's bubble becomes its state flag —
    // ear-dark (host alive, busy stamp fresh) vs host-dead? (both stale). Real
    // speech always wins; relight clears the flag. Flags re-draw past expiry
    // so the state persists; ordinary bubbles still fade on schedule.
    if (!DS.stateFlag) DS.stateFlag = {};
    skeys.forEach(k => {
      const flagged = !!DS.stateFlag[k];
      if (lampState(k) !== 'dead'){
        if (flagged){ delete DS.stateFlag[k]; delete DS.lastMsg[k]; if (bubbles[k]) bubbles[k].until = 0; }
        return;
      }
      if (DS.lastMsg[k] && !flagged) return;                        // real speech wins the bubble
      DS.stateFlag[k] = true;
      DS.lastMsg[k] = { text: busyMark(k) ? 'ear-dark — host alive, re-arm fixes' : 'host-dead? — presence + busy stale', kind: 'sys' };
    });
    for (const k in DS.lastMsg){                   // bubbles likewise — draw only content the scene lacks
      const lm = DS.lastMsg[k], b = bubbles[k];
      if (!b || b.key !== lm.text + '|' + lm.kind || (DS.stateFlag[k] && b.until < performance.now() / 1000)) drawBubble(k);
    }
    updateSign();
    rebuildShelf((LASTBOARD && LASTBOARD.closed) || []);
    rebuildTray((LASTBOARD && LASTBOARD.staged) || []);
    goGlow.intensity = cardStage === 4 ? 2.4 : 0;
    if (DS.vts && DS.vts !== lastSlam){            // verdict transition → one stamp-slam, at the VERDICT slot
      lastSlam = DS.vts; stampT = 0; stamp.visible = true;
      stamp.position.set(card.position.x, card.position.y + .62, card.position.z + .02);
    }
  }
  function frame(){
    queued = false;
    if (stampT >= 0){                              // slam: ease-in drop onto the card at its slot
      stampT++;
      const t = Math.min(1, stampT / 9);
      stamp.position.y = (card.position.y + .62) - .58 * t * t;
      if (t >= 1) stampT = -1;
    }
    if (pulseT >= 0){                              // veto/objection: brief speaker-chair pulse
      pulseT++;
      const d = Math.max(0, 1 - pulseT / 24), c = chairs[pulseK];
      if (c){ c.lpt.intensity = (lampState(pulseK) === 'lit' ? 1.6 : 0) + 3 * d; c.lamp.scale.setScalar(1 + .5 * d); }
      if (pulseT >= 24){ pulseT = -1; if (c) c.lamp.scale.setScalar(1); }
    }
    if (cardAnim){                                 // the doc travels the pegboard — ease + a little lift
      cardAnim.t += 1 / 22;
      const t = Math.min(1, cardAnim.t), e = t * t * (3 - 2 * t);
      card.position.set(
        cardAnim.fx + (cardAnim.tx - cardAnim.fx) * e,
        cardAnim.fy + (cardAnim.ty - cardAnim.fy) * e + Math.sin(t * Math.PI) * .3,
        cardAnim.fz + (cardAnim.tz - cardAnim.fz) * e);
      card.rotation.x *= (1 - e);
      if (t >= 1){ cardAnim = null; card.rotation.x = 0; }
    }
    let bubAct = false;                            // bubble ramps only while opacity is moving
    const nowS = performance.now() / 1000;
    for (const k in bubbles){
      const b = bubbles[k];
      const o = nowS > b.until ? Math.max(0, 1 - (nowS - b.until) / .9) : Math.min(1, (nowS - b.t0) / .45);
      if (o !== b.op){ b.op = o; b.sp.material.opacity = o; }
      if (b.op > 0 && (nowS < b.t0 + .45 || nowS > b.until)) bubAct = true;
    }
    if (settle > 0) settle--;
    controls.update();
    breath += .3;
    for (const k of skeys){ const c = chairs[k];
      if (c.lamp.visible && lampState(k) === 'lit' && pulseK !== k) c.lamp.scale.setScalar(1 + .1 * Math.sin(breath)); }
    if (DS.silentK && chairs[DS.silentK]){                           // the room names who's silent — red pulse, zero writers
      const r = chairs[DS.silentK].ring;
      r.material.color.setHex(0xff3b30);                             // color lives in the pulse too — dirty() alone can't rely on sync
      r.material.opacity = .45 + .4 * Math.abs(Math.sin(breath * 1.5));
    }
    if (DS.ghostK && chairs[DS.ghostK]){                             // C1: the room names the phantom — violet pulse, faster
      const r = chairs[DS.ghostK].ring;
      r.material.color.setHex(0xbc8cff);
      r.material.opacity = .45 + .4 * Math.abs(Math.sin(breath * 2.5));
    }
    const dfh = DS.turn;
    if (dfh && chairs[dfh] && lampState(dfh) === 'dead'){            // §3: ember floor — slow held-by-nobody rhythm
      const r = chairs[dfh].ring;
      r.material.color.setHex(0xb05900);
      r.material.opacity = .3 + .25 * Math.abs(Math.sin(breath * .8));
    }
    let anyWork = false;                                             // DVM-016: ~1Hz metronome — tint flips white↔seat color
    const wb = Math.floor(performance.now() / 500) % 2;
    for (const k of skeys){ const c = chairs[k];
      if (c.work.visible){ anyWork = true; c.work.material.color.setHex(wb ? 0xffffff : c.hex); } }
    renderer.render(scene, camera);
    if (interacting || settle > 0 || stampT >= 0 || pulseT >= 0 || cardAnim || bubAct || DS.silentK || DS.ghostK || anyWork || (DS.turn && lampState(DS.turn) === 'dead')) mark();   // alive only while moving/burning/laboring/embering
  }
  controls.addEventListener('change', mark);
  controls.addEventListener('start', () => { interacting = true; mark(); });
  controls.addEventListener('end',   () => { interacting = false; settle = 16; mark(); });

  /* hover shows the seat, click writes @SEAT — same affordance as the rail cards */
  const ray = new THREE.Raycaster(), ptr = new THREE.Vector2(), hoverEl = document.getElementById('deckhover');
  const el = renderer.domElement;
  let hoverK = null, downXY = null;
  el.addEventListener('pointermove', e => {
    const r = el.getBoundingClientRect();
    ptr.set(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
    ray.setFromCamera(ptr, camera);
    const hits = ray.intersectObjects(hitList);
    const k = hits.length ? hits[0].object.userData.key : null;
    if (k !== hoverK){ hoverK = k; el.style.cursor = k ? 'pointer' : ''; if (hoverEl) hoverEl.textContent = k ? '@' + k : ''; mark(); }
  });
  el.addEventListener('pointerdown', e => { downXY = [e.clientX, e.clientY]; });
  el.addEventListener('pointerup', e => {
    if (downXY && Math.hypot(e.clientX - downXY[0], e.clientY - downXY[1]) < 6){
      if (hoverK) openInspector(hoverK);                                    // chair → inspector (contract + @SEAT)
      else document.getElementById('inspector').classList.remove('open');   // click elsewhere dismisses
    }
    downXY = null;
  });
  /* context loss over an hours-open page must never freeze a lie — drop to
     honest 2D: strips' data restored to the HUD, chip flips 3D→2D */
  el.addEventListener('webglcontextlost', e => {
    e.preventDefault();
    controls.dispose(); renderer.dispose(); el.remove();
    DECK.note = DECK.presence = DECK.speech = function(){ render2D(); };   // scene impls gone — 2D keeps following state
    DECK.pulse = DECK.pin = DECK.dirty = function(){};
    deckMode('2d');
  });
  new ResizeObserver(() => {
    if (host.clientHeight < 10) return;                                  // collapsed — nothing to size
    renderer.setSize(W(), H()); camera.aspect = W() / H(); camera.updateProjectionMatrix(); mark();
  }).observe(host);

  DECK.note     = () => { syncScene(); mark(); };
  DECK.presence = () => { syncScene(); mark(); };
  DECK.pulse    = k => { if (chairs[k]){ pulseK = k; pulseT = 0; mark(); } };
  DECK.pin      = k => { if (!chairs[k]) return; (DS.vetoSeats = DS.vetoSeats || []).push(k); addPin(k); mark(); };
  DECK.speech   = k => { if (k === 'WALL'){ updateSign(); mark(); } else drawBubble(k); };
  DECK.dirty    = mark;
  setInterval(() => { if (DECK.mode === '3d') mark(); }, 500);             // 2 Hz idle tick — lamp breathing only
  syncScene();                                                            // hydration rides the same diff path
  renderer.render(scene, camera);                                        // first real render…
  deckMode('3d');                                                        // …now the scene claims the strip zone
}

render2D();   // viewport slot is never empty while the module loads
document.body.classList.add('dk-navhide');   // drawer starts folded — the scene is the face

/* ── boot ── */
renderSeats();
setSource('idle');
poll(); pollPresence(); startPush();          // one immediate load each; push or 2s-poll carries it after
/* DVM-013: scene init runs async after the strips render — any failure keeps them */
Promise.all([import('three'), import('three/addons/controls/OrbitControls.js')])
  .then(([THREE, m]) => { try { initDeck3D(THREE, m.OrbitControls); } catch(e){ console.warn('deck3d init failed:', e); deckMode('2d'); } })
  .catch(() => deckMode('2d'));               // CDN unreachable / no network → strips stay, chip reads 2D
if (new URLSearchParams(location.search).has('swap')) document.body.classList.add('swap');   // preview the swapped layout
if (new URLSearchParams(location.search).has('demo')) runDemo();

window.DVM = { ingest, applyState, setProposal, renderVerdict, setPhase, SEATS, LEGACY_SEAT };
