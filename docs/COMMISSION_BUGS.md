# Commission Bug File — Live

**Scope**: bugs found in the Deliberation & Verdict Machine itself — the dashboard, the bus, the watcher, the server, the protocol. **Not** the game; game bugs live in `docs/known_issues/`.

**Filing convention**: every named finding lands an entry here as part of the AUDITOR's sweep duty (pending decree — CHAIR-501). Status: 🔴 ACTIVE / 🟡 FIX APPLIED / 🟢 FIXED / ⚪ RETIRED.

| # | Bug | Status | Severity | Filed |
|---|-----|--------|----------|-------|
| C1 | [Ghost Floor — token on non-seat stalls room](#c1) | � FIX APPLIED (DVM-019) | 🟡 HIGH | 2026-10-30 |
| C2 | [Labor state drifts from handshake lifecycle](#c2) | 🟢 FIXED (DVM-018 v0.2 — tiered certification: dual simulation + V8 parse + boss live-fire pending) | 🟡 HIGH | 2026-10-30 |
| C3 | [Staged-card detail snapshot drifts from draft](#c3) | 🟡 FIX APPLIED (DVM-019 sweep clause) | 🟢 MEDIUM | 2026-10-30 |
| C4 | [turn field has no canonical key](#c4) | 🟡 FIX APPLIED (DVM-019 canon) | 🟢 MEDIUM | 2026-10-30 |
| C5 | [Proposal slot survives closure — ate a GO CODE](#c5) | � FIX APPLIED (DVM-019) | 🟢 MEDIUM | 2026-10-30 |
| C6 | [turn_at stamped in seconds on a millisecond bus](#c6) | � FIX APPLIED (DVM-019) | 🟡 HIGH | 2026-10-30 |
| C7 | [Watcher ear killed by presence-file replace race](#c7) | � FIXED (stood since DVM-010; verified DVM-019) | 🟡 HIGH | 2026-10-30 |
| C8 | [Boss @-route cannot reach delegated seat](#c8) | � FIX APPLIED (DVM-019 — Shape A ruled) | 🟢 MEDIUM | 2026-10-30 |
| C9 | [Armed ≠ watched — presence stamp without a live listener](#c9) | � FIX APPLIED (DVM-020 — heartbeat-per-beat; self-rearm agenda remains) | 🟡 HIGH | 2026-10-30 |
| C10 | [Certification greps, never parses — syntax-dead page shipped](#c10) | 🟢 FIXED (duty embodied: AUDITOR contingency pass + CHAIR execution read — exercised on fix-lane run 1) | 🟡 HIGH | 2026-10-30 |
| C11 | [Wake label understates batch precedence](#c11) | � FIX APPLIED (DVM-020 — batch argmax + ambient count) | 🟢 LOW | 2026-10-30 |
| C12 | [Proposal slot meaning drifted to item-under-build](#c12) | � FIX APPLIED (DVM-020 — literal chain in law) | 🟢 LOW | 2026-10-30 |
| C13 | [Armed-SEND — spec-defect, not code-defect](#c13) | 🟢 FIXED (fix-lane run 1) | 🟢 LOW | 2026-10-30 |
| C15 | [Armed-verb misfire — lit verb re-fires on chat send, eats a live slot](#c15) | � FIXED (fix-lane run 2 — tiered certification) | 🟡 HIGH | 2026-10-30 |
| C14 | [Staged row missing path — card can't open its draft](#c14) | 🟢 FIXED (file-system team sweep) | 🟢 LOW | 2026-10-30 |
| C16 | [Send-triad residual divergence — TABLE unbound, arm=fire, no disarm path](#c16) | 🟢 FIXED (DVM-021 v0.3 full-M1 — tiered certification: dual BFS replay + V8 parse + boss live-fire pending) | 🟡 HIGH | 2026-10-31 |
| C17 | [Persist-mode lamp gap — ear live, agent halted](#c17) | 🟡 FIX APPLIED (DVM-921 built; awaiting certification) | 🟡 HIGH | 2026-10-31 |
| C18 | [Feed id collision eats seat speech via reader dedup](#c18) | 🟢 FIXED (CHAIR renumber + max(prefix)+1 issuance law) | 🟡 HIGH | 2026-10-31 |
| C29 | [files[] row snapshot drift — draft card 404s on a live file](#c29) | 🟡 FIX APPLIED (bench: BUTTON+ARCHITECT path repair; residual rail-note open) | 🟢 MEDIUM | 2026-11-02 |
| C30 | [msg.to shape drift — string `to:` crashes two readers](#c30) | � FIXED — dup, merged into row below (bench, CHAIR-1212) | 🟢 MEDIUM | 2026-11-02 |
| C30 | [to: string crashes render + BOSS inbox 404](#c30) | 🟢 FIXED (bench, CHAIR-1212) | 🟢 MEDIUM | 2026-11-02 |
| C31 | [scope drift on shared scene objects](#c31) | 🟢 FIXED | 🟢 MEDIUM | 2026-11-03 |
| C32 | [Foreign-seat pulse client sole-writes another seat's watermark](#c32) | 🔴 ACTIVE | 🟢 MEDIUM | 2026-11-04 |
| C33 | [Resurrected-session replay: fresh ids hide dupes, stale scalars revert register](#c33) | 🟡 FIX APPLIED (law + heals; guard unbuilt) | 🟢 MEDIUM | 2026-11-04 |

---

## C1 — Ghost Floor: Token on a Non-Seat Stalls the Room and Evades the Alarm

**Status**: � FIX APPLIED (DVM-019 — `LIVE := feed.seats[] ∪ {BOSS}` register + instant violet ghost-floor burn, no grace; certification pending) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-30

**Symptoms**: `turn` set to a key that is neither a live seat nor BOSS (observed: `PRAGMATIST`, a retired persona) leaves the room stalled — no watcher fires, no writer may act. The stall is invisible: `floorSilent` only guards `turn ∈ seats`, so a ghost floor shows no red ring. Observed live during the Oct-30 sound-off; the CHAIR rescued it by hand.

**Root cause**: two gaps compound — no canonical vocabulary for `turn` (C4), and the silence alarm's domain is narrower than the field's possible values.

**Evidence**: INFORMANT-39/40 (routing miss confessed), AUDITOR-14 (finding named), CHAIR-490 (manual rescue).

**Fix shape**: a floor token must only ever name a live seat or BOSS — a writer-side guard is cheaper than a reader-side alarm. Queued on the agenda with C4.

## C2 — Labor State Drifts From Handshake Lifecycle

**Status**: 🟢 FIXED (DVM-018 v0.2 — tiered CHAIR certification: T1 dual lifecycle simulation 20/20 + 10/10, T2 V8 parse clean, T3 boss live-fire pending) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-30

**Symptoms**: `busyTasked` stays lit forever when an ack self-delivers the answer (ARCHITECT-37 — WORKING burned on an idle chair). Mirror: a seat silently working >120s pre-ack shows the red silent-floor ring mid-labor. Same defect, opposite directions: labor indicators and handshake state disagree.

**Root cause**: `busyTier()` derives labor from message shapes, not the handshake lifecycle — aimed → acked → released → delivered → expired is unmodeled.

**Evidence**: BOSS's 185s pre-ack observation; ARCHITECT-37; fix spec `docs/drafts/DVM-018_handshake_labor.md` (~45 dashboard lines, zero server, zero new fields).

## C3 — Staged-Card Detail Snapshot Drifts From Living Draft

**Status**: 🟡 FIX APPLIED (systemic — DVM-019 verdict-lock sweep clause ratified into board law; any seat may heal stale text at lock, recorded on the bus) · **Severity**: 🟢 MEDIUM · **Filed**: 2026-10-30

**Symptoms**: staged viewer cards render `board.staged[].detail`, a snapshot frozen at staging time. DVM-017's card showed v0.1-era explore text while the draft file sat at v0.7 — caught by the BOSS, verified by the AUDITOR.

**Root cause**: the staged entry copies the summary instead of referencing the living file; amendments update the file, nothing updates the snapshot.

**Fix shape**: AUDITOR freshness sweep diffs snapshot-vs-file as a pipeline stage (on the agenda). Instance healed: detail refreshed to v0.7 and rewritten as readable Markdown (CHAIR-480–482).

## C4 — turn Field Has No Canonical Key

**Status**: 🟡 FIX APPLIED (DVM-019 — `LIVE` is the canonical floor domain; `LEGACY_SEAT` += `PRAGMATIST:ARCHITECT` as read-only bridge; writers author only LIVE keys) · **Severity**: 🟢 MEDIUM · **Filed**: 2026-10-30

**Symptoms**: writers stamp `turn` with seat keys (`MEMBER2`), persona names (`ARCHITECT`), retired personas (`PRAGMATIST`), or current seat names interchangeably. Watchers armed on canonical names miss floors stamped under other spellings — produces ghost floors (C1).

**Root cause**: DVM-006 split identity (seat key = plumbing, persona = occupant) but never named which spelling `turn` must carry.

**Fix so far**: `LEGACY_SEAT` normalization added to `commission_watch.py` (BOSS-authored) and already in the dashboard. `PRAGMATIST` suggested for the map but not yet added. Durable fix is writer-side: protocol names the canonical spelling; LEGACY_SEAT becomes a bridge, not the spec.

## C5 — Proposal Slot Survives Closure

**Status**: � FIX APPLIED (DVM-019 — closure is atomic: certifying write sets `proposal:null` + appends `board.closed[]`; slot reads TABLE IDLE) · **Severity**: 🟢 MEDIUM · **Filed**: 2026-10-30

**Symptoms**: `proposal` keeps displaying the last item after closure — only vacates when new business is seated. Once ate a GO CODE: the `status:'go'` stamp landed on the stale slot and silently found nothing to attach to (DVM-011 had to be seated by hand).

**Root cause**: the lifecycle has no terminal `closed` stage — certification closes the item in the ledger without clearing the table.

**Fix shape** (agreed on the record): on closure, vacate to `TABLE IDLE — awaiting new business`; closed items live in `board.closed[]` behind a link.

## C6 — turn_at Stamped in Epoch Seconds on a Millisecond Bus

**Status**: � FIX APPLIED (DVM-019 — server stamps `int(time.time()*1000)` at both sites; dashboard readers moved to ms with `<1e12 → ×1000` transition sniff) · **Severity**: 🟡 HIGH (latent) · **Filed**: 2026-10-30

**Symptoms**: `commission_server.py` stamps `turn_at` with `int(time.time())` — epoch **seconds**. Every other bus timestamp (`ts` on messages, `verdict.ts`, `armed_at`, `pulse_at`, deadlines) uses epoch **milliseconds**. The dashboard compensates locally today (`Date.now()/1000 - DS.turnAt`); any future reader assuming ms computes ~56 years of elapsed floor-silence.

**Root cause**: DVM-015's `turn_at` stamp was written in the server's natural `time.time()` idiom (seconds), while feed-file writers and the dashboard operate in `time.time()*1000` / `Date.now()` milliseconds. Never caught at write because the only consumer (`floorSilent`) divides `Date.now()` by 1000 to match.

**Fix shape**: (a) normalize the stamp to `int(time.time()*1000)` and update the two dashboard readers — preferred, one unit bus-wide; or (b) keep seconds and rename/contract it (`turn_at_s`). Must ride a server-touching build. Byte-order note: feed `ts` fields are ms; presence files are ms; only `turn_at` is the outlier.

**Related**: `docs/commission_server.py` (stamp site), `docs/DELIBERATION_MACHINE.html` (`floorSilent`, `busyTier` FLOOR-HELD readers), `docs/COMMISSION_PROTOCOL.md` (handshake section — semantics documented, units unnamed).

*(Migrated from `docs/known_issues/runtime/TURN_AT_SECONDS_VS_MS.md` per boss-147 — originally filed by BOSS.)*

## C7 — Watcher Ear Killed by Presence-File Replace Race (WinError 5)

**Status**: � FIXED (the guard stood since DVM-010 — `try_stamp` retries `os.replace` 3× then skips a beat, watcher never dies on a stamp; DVM-019 verified no unguarded call remains and wrote the one-ear line into law) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-30

**Symptoms**: `commission_watch.py` dies mid-run with `PermissionError: [WinError 5] Access is denied: presence_<SEAT>.js.tmp -> presence_<SEAT>.js`. Ear goes silent; the agent stops waking until a relay notices and re-arms.

**Root cause**: `stamp_presence()` does `os.replace(tmp, p)` unguarded. On Windows the replace fails if `presence_<SEAT>.js` is open elsewhere — seen live when two MEMBER3 watcher relays ran concurrently (duplicate arms) and raced the same file. Transient locks (AV/indexer) can hit the same path even single-writer.

**Investigation**: observed on relay 60c06fc7; exit code 1, ear pulse froze ~20s stale. Housekeeping should never be fatal — the poll loop is the product, the stamp is telemetry: a missed tick self-heals next tick, a crash doesn't.

**Fix shape**: wrap `os.replace` in try/except (or small retry) and continue polling — ~3 lines in `stamp_presence()`. Contributing factor: relay discipline should keep ONE ear per seat; duplicate arms invite the race regardless.

**Related**: `docs/commission_watch.py` — `stamp_presence()` (~line 27-37).

*(Migrated from `docs/known_issues/runtime/WATCHER_PRESENCE_REPLACE_RACE.md` per boss-147.)*

## C8 — Boss @-Route Cannot Reach Delegated Seat (THE BUTTON)

**Status**: � FIX APPLIED (DVM-019 — Shape A ruled into law: BUTTON stays in `seats[]`, boss-routable for acks/ballots/reports, holds no deliberation positions — routable ≠ votable; the rekey already routes `@BUTTON`, zero code) · **Severity**: 🟢 MEDIUM · **Filed**: 2026-10-30

**Symptoms**: BOSS typed `@BUTTON` (boss-27) intending to hand the floor to MEMBER3; floor did not move — `turn` stayed `BOSS`, MEMBER3 heard it read-mode only. `@MEMBER3` would fail the same way.

**Root cause**: `commission_server.py` filters @-tokens against `data["seats"]` (then `["DEVIN","MEMBER2"]`). Delegated seat MEMBER3 is intentionally not a voting seat, so no @-token maps to it — the server has no route to hand it the floor.

**Investigation**: floor CAN reach the delegated seat another way — any floor-holder can set `"turn":"MEMBER3"` in their own write (proven live, devin-29). Design question, not a one-line fix: should the boss summon the delegated seat directly (a routable-but-non-voting list), or is member-relay the intended path? Affects `COMMISSION_PROTOCOL` semantics — table it, don't patch unilaterally.

**Related**: `docs/commission_server.py` (`valid = [s for s in to if s in seats]`), `docs/deliberation_feed.js` (`seats[]`/`members{}` contract), `docs/COMMISSION_PROTOCOL.md` (Rule Four).

*(Migrated from `docs/known_issues/runtime/BOSS_AT_ROUTE_DELEGATED_SEAT_GAP.md` per boss-147.)*

## C9 — Armed ≠ Watched: Presence Stamp Without a Live Listener

**Status**: 🟡 FIX APPLIED (DVM-020 — heartbeat-per-beat: a live watcher re-stamps every ~10s at 30s TTL; armed literally equals "a loop breathed in the last half-minute". Wake-delivery contract named per substrate — exit-mode stays the relay law) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-30 (AUDITOR-15 record-nit — the filing convention's first catch)

**Symptoms**: a seat stamps `presence_<SEAT>.js` during a bus write, then closes the work block without re-spawning the watcher. The ear chip reads armed and counts down from a real deadline — while no process exists to fire on a message. Observed twice on the CHAIR tonight (and once confessed by the ARCHITECT in ARCHITECT-46): the lamp tells `armed`, the truth is `unwatched`.

**Root cause**: presence claims a *window* (`armed_at + ttl_ms`), but the claim is written by hand during unrelated writes — nothing requires that a live watcher back the stamp. Distinct from C7: C7 is a watcher killed mid-run by the replace race; C9 is a watcher never re-spawned — the claim-window lies by omission.

**Evidence (live, 2026-10-30 ~late session)**: `presence_ARCHITECT.js` stamped 61m before the read with ttl 10m — the process had been dead ~51m. For the first 10m the stamp claimed "armed" on a dead ear (the claim-window lie); afterwards the file read honestly DARK but the seat stayed unwatched — nobody re-armed it. Both failure halves timestamped in one sighting (AUDITOR-26).

**Fix so far**: CHAIR seat-rule bound — *a work block is not done until the ear is re-armed; presence without a live watcher is a lie the lamp tells*. ARCHITECT self-applied the same. Systemic candidates: the watcher could own the stamp exclusively (agents never hand-stamp), or the dashboard could cross-check stamp freshness against expected beat cadence. The boss-approved agenda item — a watcher that re-arms itself — covers both failure modes (stamp-lie and dead-ear). Routes with the DVM-019 sweep / next watcher-touching build.

## C10 — Certification Greps, Never Parses: Syntax-Dead Page Shipped

**Status**: � FIXED (instance healed same-night; parse-check duty embodied — AUDITOR contingency pass + CHAIR execution read, exercised on fix-lane run 1) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-30

**Symptoms**: DVM-019's certified build threw `Uncaught SyntaxError: redeclaration of const ghost` on load — the whole page script dead. The BUTTON's C1 patch bound `ghost` for the ghost-floor check where `ghost` already meant the vacant-chair flag, in the same `for`-body scope (`syncScene`, L1254 vs L1262).

**Root cause — process, not typo**: certification verified the build *by grep* — feature strings present, counts matched — and never parsed or loaded the file. The BUTTON's "acceptance run" exercised the predicates in isolation, not the page's syntax. A `node --check`, a jsdom parse, or a single refresh would have caught it before the certified stamp.

**Evidence**: boss's console report minutes after the DVM-019 restart; CHAIR healed with a rename (`ghostFloor`) and a same-scope duplicate scan.

**Fix so far**: instance healed. Owed to the certification duty: page-touching builds get a parse/load check before the stamp — certification claims the file runs, not just that it contains the right strings.
## C11 — Wake label understates batch precedence
**Status**: 🟡 FIX APPLIED (DVM-020 — batch label claims highest-precedence unseen + ambient count; the labeled message always prints, even beyond the tail)
**Symptom**: `wake_label(trigger[-1])` labels only the newest unseen message; a batch where an earlier message names the seat still prints OVERHEARD. Tail-5 print covers content, but the label understates.
**Fix shape**: label the batch’s highest-precedence class, not just the newest.

## C12 — Proposal slot meaning drifted to item-under-build
**Status**: 🟡 FIX APPLIED (DVM-020 — literal chain in law: null → tabled → go → built → null; phases/verdict/certification named as the events between)
**Symptom**: `proposal` re-seats at GO as the item under construction (go→built stamps) — the slot’s meaning silently shifted from “on the table” to “under build.” Coherent but unwritten.
**Fix shape**: name the lifecycle in law — tabled → go → built → closed/vacated.

## C13 — Armed-SEND: Spec-Defect, Not Code-Defect (new ledger class)
**Status**: 🟢 FIXED (fix-lane first run — BUTTON repaired → AUDITOR verified → CHAIR certified) · **Severity**: 🟢 LOW · **Filed**: 2026-10-30 (entry owed by AUDITOR ruling; landed via CHAIR ledger sweep — the ruling sat on the bus unwritten)
**Symptom**: SEND held a persistent .armed lit state as mode indicator; boss intent: momentary press light.
**Layer — the precedent**: the code faithfully implemented the decree (`illuminate when selected`); the DECREE was the miss. The boss caught it himself. Findings now name their layer — spec-defect vs code-defect is a ledger class. Annotates retro-check observation #2 (`spec-faithful` — accurate, but the spec was the miss).
**Fix**: `flashSend()` 180ms press-flash with clearTimeout guard; `setSendMode` arms only btntable/btnside.

## C14 — Staged Row Missing Path: the Card Can't Open Its Draft

**Status**: 🟢 FIXED (CHAIR repaired the row during the file-system team sweep — boss-ordered audit) · **Severity**: 🟢 LOW · **Filed**: 2026-10-30

**Symptoms**: `board.staged[]` row for DVM-018 carried `id/title/detail/by/ts` but no `path`/`ver`/`ship`/`kind` — an old-schema card written before the current staged format. The draft `drafts/DVM-018_handshake_labor.md` existed on disk and in `board.files[]`, but the staged card could not open it (nothing to fetch).

**Root cause**: staged rows written across schema generations keep the fields they were born with — no migration/normalization pass fills newer keys on old rows. Same family as C3 (registers drift from reality), different limb: schema generation, not content snapshot.

**Evidence**: found in the registers-vs-disk sweep (AUDITOR-25): `files[]` row OK + draft on disk, staged row pathless. Repaired in place by CHAIR: path + ver v0.2 + ship 5-0 + kind now populated.

**Fix shape**: row rewritten with current fields. Residual guard candidate: on staged-row reads, a missing `path` could fall back to the `board.files[]` row for the same id — or a sweep clause normalizes staged rows at schema bumps.

## C15 — Armed-Verb Misfire: Lit Verb Re-Fires on Chat Send, Eats a Live Slot
**Status**: � FIXED (fix-lane run 2 — tiered CHAIR certification) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-30 (live incident; flagged ARCHITECT aside, named by CHAIR)
**Symptom**: boss typed a chat message while TABLE stayed armed from an earlier click; Enter re-fired `/table` and the chat text seated as proposal DVM-674, evicting the live DVM-020 slot mid-veto-fold. The armed-verb persistence the AUDITOR flagged as observation #2 was the exact mechanism. Second strike same hour: an `/aside`-channeled `@BUTTON GO` never stamped the slot.
**Root cause**: toggles hold `.armed` until SEND or sibling — spec-faithful per boss decree, but Enter inherits the armed verb silently; no guard distinguishes a proposal intent from a chat intent, and the armed verb was invisible to a user viewing the other channel.
**Evidence**: boss-DVM-674 message + sys-DVM-674 PROPOSAL TABLED; CHAIR healed the slot by hand (DVM-655 restored). Second incident: boss's GO for DVM-020 rode `/aside` — word was authoritative, stamp never fired; CHAIR back-stamped `go` per the corpse-slot precedent.
**Boss-ruled fix shape** (struck twice in one hour): <b>bind the armed verb to its tab</b> — SIDE-TABLE click toggles the pane to the side-table tab AND arms the verb; clicking the SIDE TABLE tab arms the verb; LIVE TRANSCRIPT tab arms floor chat. The channel you post to is always the channel you're looking at — the misfire class dies at the root.
**Fix applied** (BUTTON, 5 net lines): `btnside.onclick` → `sendBossCmd('/aside') + setSendMode('side') + setTab('side')`; `tab_side.onclick` → `setTab('side') + setSendMode('side')`; `tab_feed.onclick` → `setTab('feed') + setSendMode('send')`; TABLE stays unbound per the ruling.
**Certification** (CHAIR, tiered per the side-table consult — now precedent: *stamps name their tiers*): **T1** mutator set fully enumerated (5 sites) + AUDITOR mechanical replay — no path reaches lit-`side`/viewing-`feed`; **T2** `node --check` clean (V8 v24.19.0 wheel); **T3** riding the boss's refresh live-fire — **confirmed in production** (boss playtest: "works well… clearly see im talking to side table and then back to main"). Esprima's ES2017 chokes (importmap/`?.`) disclosed as tool-inconclusive, not counted.


## C16 — Send-Triad Residual Divergence: TABLE Unbound, Arm=Fire, No Disarm Path

**Status**: 🟢 FIXED (DVM-021 v0.3, full-M1 — tiered CHAIR certification: T1 dual BFS replay reachable==legal, T2 V8 parse clean, T3 boss live-fire pending) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-31 (boss playtest after C15 ship)

**Symptoms** (boss verbatim): 'the button and tab binding isnt working. state issues. one way function. not consistent backward or forward, button to button or tab to tab.'

**Enumerated residual divergences** (mode × tab transition table, CHAIR-735):

1. **(table, side) diverges by design** — TABLE is unbound per the C15 ruling; arm it while viewing the side tab and the lit verb names a channel you can't see. The C15 hole survives through the one exempted verb.
2. **Arm = fire** — SIDE-TABLE (and TABLE) click with text in the box sends it immediately; no way to arm a verb for the next message without committing the current one.
3. **No disarm path except the other tab or SEND** — a lit button re-arms, never toggles off. The 'one way' feel: SIDE-TABLE pulls you to the side tab, nothing pulls back.

**Fix applied** (BUTTON, DVM-021 v0.3 — full-M1, boss-ruled): verb-clicks arm+retab only, never fire — `btntable` toggles table⇄send and always binds feed; `btnside` armed-click disarms unconditionally to `(send,feed)`, else arms side+retabs; `bosssend` fires+resets+retabs feed+flash; Enter/`sendArmed` is the sole fire path; button titles updated to arm semantics (arm-silent's only visible tell). Protocol send-triad clause landed.

**Root cause**: spec-defect class — the C15 fix implemented the ruling faithfully; the ruling left TABLE afloat and arm-vs-fire fused. Code is correct per spec; the spec's interaction model is incomplete.

**Candidate shapes** (for the gate): (a) every verb binds to a view — TABLE needs a proposal view/tab to land on; (b) split arm from fire — click arms, Enter/SEND fires; (c) mutual-exclusivity with auto-retab on ANY verb arm.


## C17 — Persist-Mode Lamp Gap: Ear Live, Agent Halted

**Status**: � FIX APPLIED (DVM-921 built — dual-stamp + watchdog daemon; awaiting certification) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-31 (boss-reported; convergent read CHAIR/AUDITOR/INFORMANT)

**Symptom**: ARCHITECT halted (turn ended / stream-read released) while his <code>--persist</code> watcher kept beating — <code>presence_ARCHITECT.js</code> stamped 8s-old LIVE on a seat whose agent wasn't consuming. Boss caught it in the Devin log; the lamp read armed throughout.

**Root cause**: under persist-mode the heartbeat proves the <i>watcher loop</i>, not the agent. Exit-mode couples them (watch dead ⇒ stamp dies ⇒ lamp dark); persist-mode decouples — the ear can beat honestly while the seat behind it sleeps. The C9 lie in a new form: <b>ear live ≠ seat free</b>, true on stream substrates this time.

**Fix shape** (convergent, rides DVM-022): <b>dual-stamp</b> — the watcher heartbeats process-liveness; the agent writes a '+chr(39)+'last-consumed'+chr(39)+' mark on each wake it reads; the lamp reads dark/degraded when the two marks diverge beyond a window. An agent can't stamp while dead → agent-written marks are honest by construction. The watchdog must watch the seat's <i>output</i>, not just the process (INFORMANT). Until then: only the host resumes a halted agent — the room can't nudge a paused process.

## C18 — Feed Id Collision Eats Seat Speech via Reader Dedup

**Status**: 🟢 FIXED (CHAIR renumbered its five posts into free id space 2026-10-31; issuance law restated: id = max(prefix)+1, never count(speaker)+1) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-31

## Symptoms
- Transcript appeared dead while the bus was loud: five CHAIR posts after reconvene never rendered.
- Boss/sys/phase messages rendered fine; only seat speech vanished.

## Root Cause
Old DEVIN-era posts own ids CHAIR-113..818, but CHAIR's writer scripts numbered new posts by counting CHAIR-*speaker* rows (112 exist) — re-issuing CHAIR-113..117. The page's `seen` dedup (ingest L502) silently dropped every re-issued id.

## Evidence
- CHAIR's own postmortem + fix on the bus; AUDITOR verified zero duplicate ids post-renumber (CHAIR-* range 78–824, 237 posts).
- AUDITOR's discriminating test earlier closed the consult as 'design' — true-but-incomplete (floor posts visible ≠ seat posts visible).

## Resolution
CHAIR renumbered the five posts into free space (CHAIR-819..823). Issuance law restated for every seat that writes the feed: **id = max(prefix)+1, never count(speaker)+1** — count undercounts whenever the namespace spans speaker renames.


## C19 — Spawn-Gap Watermark Swallow (relay respawn skips gap messages)

**Status**: 🟢 FIXED (DVM-923 v0.1.1 SHIP'd 3-0, built, audited AUDITOR-129, drill-proven, CERTIFIED) · **Severity**: 🟡 HIGH · **Filed**: 2026-10-31 (AUDITOR-124, two live instances: boss's INFORMANT pass + BUTTON-951)

## Symptoms
- A relay respawning mid-flight seeds its watermark at the CURRENT high-water mark — messages that landed during the dead-ear gap are counted already-seen and never delivered.
- Live specimens: the boss's floor-pass to INFORMANT swallowed (re-pass needed); BUTTON-951 ate one on CHAIR's own ear.

## Root Cause
Client-side cursor: commission_pulse.py starts since=-1; the first response seeds since=d['n'] = the count-as-of-now (L44-46 pre-fix). A respawn's since=-1 asks for everything after the current count — the gap batch is skipped by construction.

## Resolution
DVM-923: (a) --since <n> seeds the poll loop at the carried watermark; (b) pulse_n_<SEAT>.js stamped on EVERY exit path (sole-writer, stamp idiom — files don't truncate the way stdout does). Live kill-and-respawn drill proved the gap batch prints verbatim (CHAIR-985/986). Zero server change.

## C20 — Held-Socket Wake Stall (doorbell-vs-socket latency, substrate layer)

**Status**: � FIXED (DVM-924 v0.1.1 SHIP'd 3-0, built, audited AUDITOR-161 CLEAN, drill-proven live, CERTIFIED CHAIR-1043 — rungs 1+2: FLOOR-OWED daemon flag at >90s held + STALLED wall label at STALL_TTL=180s; rung-3 daemon spec-only behind STALL_REOPEN=3 stalls/10min/24h) · **Severity**: 🟢 MEDIUM · **Filed**: 2026-10-31 (AUDITOR-127, boss-reported live on CHAIR's seat)

## Symptoms
- CHAIR's relay stayed ARMED over delivered messages — the socket held but the host never woke until later. The batch delivered to the ear; the doorbell didn't fire.

## Root Cause (as narrowed)
The doorbell only exists when the socket RETURNS — a relay subagent's completion notification fires on process exit. A still-held socket means the subagent is still RUNNING: no completion exists yet to wake the host. The stall is latency (socket inside its hold / server batch delay), not a missed notification — on this substrate the notification does fire when the process exits (lands as a fresh turn).

## Candidate cures (carried, un-ordered)
- Shorter hold = faster doorbell (tighter poll window trades responsiveness for server load).
- The inbox-listener daemon: socket-holding decoupled from host discipline, supervisor auto-respawned — the structural answer already on the carried-design list.
- Boss-visible honesty side-band already shipped: busy-stamp marks compose windows so a stalled wake doesn't read as death.

## Resolution
DVM-924 ladder adopted over daemon-first (unanimous robin): rung-1 FLOOR-OWED flag daemon-side, rung-2 STALLED wall label — visibility, not machinery; the latency is accepted as an honest bound while it stays invisible no longer. Live drill on CHAIR's own held register: flag fired at 206s, self-cleared on register move. The drill ALSO caught a real propagation defect — the running supervisor predated the build (dead code until relight, PID 19420); fold: tier-B daemon-code builds are not DONE until the running daemon proves them.


## C21 — Busy Tags Never Drew (stamp->chair pipeline correct; the page was stale)

**Status**: RESOLVED — NO CODE DEFECT (environmental: pre-tonight page copy lacked the entire busy-render path) · **Severity**: MEDIUM (boss-visible honesty machinery invisible) · **Filed**: 2026-10-31 (boss live-fire test — name-only chairs across 4 live stamps)

## Symptoms
- Boss live-fired the busy-stamp system: four seats stamped canonical marks on the main table; chairs showed names only — no WORKING/COMPOSING ever drew, across multiple fresh stamps.

## Investigation (stamp->draw chain, traced end-to-end)
- busy_<SEAT>.js files: written correctly (verified on disk mid-test: busy:true, fresh armed_at, canonical schema via commission_busy.py).
- presence files: armed + lit for all stamped seats — the lampState gate was satisfied.
- Served file (working copy): full pipeline present — loadBusy in pollPresence (2s poll), busyMark freshness gate, busyTier -> setWork -> work.visible, plus the DOM btag path.
- COMMITTED file (aebf5b1): ZERO busy-render machinery — pollPresence loads presence only; loadBusy/busyMark/busyTier do not exist. The entire pipeline is uncommitted working-copy code.

## Root Cause
The boss's loaded page predated tonight's builds. The stamps, server, and render code were all correct — the browser was simply running a copy of the app that contains no busy loader at all. Same reload-gated class as the STALLED label (tier-B code needs one refresh to arrive).

## Resolution
Hard refresh (Ctrl+F5) — boss confirmed COMPOSING drawing on INFORMANT, BUTTON, AUDITOR within seconds. ARCHITECT's absence was itself instructive: his single 30s stamp expired pre-F5 — the live proof of the beat discipline.
- Real defects found en route and already cured by fold (not code): schema drift (hand-rolled busy_at — now canonical commission_busy.py sole-writer), stamp-while-deaf (now arm-before-stamp law), TTL expiry mid-compose (now the beat rep), clear-on-post transience (by design — tags live only during labor).
- Standing law now in protocol + all five inductions: ALL labor stamps, canonical writer, arm before stamp, beat inside TTL, clear on done.


## C22 — Stamp-State Vocabulary (labor-kind tags + derived-state map)

**Status**: CERTIFIED FIXED (DVM-925 v0.1 — SHIP 3-0 + bench read, GO, built, audited CLEAN x2 incl. drill-caught WAITING:<seat> prefix patch, induction clause folded all five, CERTIFIED CHAIR-1080; render half rides the boss's F5 like all tier-B) · **Severity**: MEDIUM (visibility feature, not a defect) · **Filed**: 2026-10-31 (boss-proposed; peer-deliberated list, CHAIR-1070)

## Spec (deliberated, seats-to-seats)
- STAMPED kind-values on busy_<SEAT>.js: THINKING, READING, COMPOSING, WORKING, seat-flavors AUDITING/BUILDING/DRAFTING/GATING/MODERATING, WAITING:<seat> (self-declared block).
- DERIVED (render-side, never stamped): QUEUED (addressed && turn!=me), FLOOR-HELD, STALLED, DEAF-WORK (ear dark + fresh mark), FLOOR-OWED.
- CUT: ARMED-WAITING, IDLE, ARNING.
- Honesty boundary: labor is seat-claimed; floor-state is machine-computed — neither crosses.
- Audit surface: FLOOR-HELD weakness (absence-of-mark != idle); stamp reps are the mitigation.
- Schema: enum on a kind field, seat-flavors as the escape valve; tier-B renderer work only.


## C24 - Prop-Catalog + Binding System (animator spawn/attach)

**Status**: TABLED -> DVM-926 drafting (ARCHITECT holds the floor)
**Opened**: boss ask - spawn authored items in the animator (chair for SIT, model-10 pistol for AIM, future models)

**Spec as converged (side-table, 4-way unanimous)**:
- Registry of authored items - Assets/StreamingAssets/voxel_items/ IS the catalog; a new item drops in as another voxel_item
- Two binding classes, one merge machine:
  - MOUNTED (held prop): binds to a body anchor (pistol->hand grip); voxels ride that group's transform
  - PLACED (scene prop): binds to a world anchor (chair->seat); voxels ride the static path (group-0)
- Pose-declared prop field: a pose may name an item id for auto-spawn on select (SIT->chair, AIM->model-10)
- AUDITOR's boundary law: pose stays body-state data; the BINDING is the new entity

**Rail**: ARCHITECT drafts schema -> INFORMANT gate -> veto poll -> verdict -> boss GO -> BUTTON bench -> AUDITOR verify -> CHAIR certify -> fold.

## C23 — Account-Switch Silence Pattern (metering event, not abandonment)

**Status**: RATIFIED LAW 4-0 (main-table ballot, CHAIR-1092 — account-switch silence is metering, not abandonment) · **Severity**: LOW (context hazard) · **Filed**: 2026-11-01 (boss-declared, room-converged)

## Pattern
- Devin's free-tier cap is a rolling ~24h input-token window (inferred: boss's ~1.0B limit-hit memory ≈ live h24 estimate 1.05B; 18h duration-based cooldown = sliding-window drain, not calendar reset).
- When Account A hits the cap: the boss is locked out ~18h and returns on Account B. The room's seat-sessions on A would themselves stop mid-cycle — mass ear-death at a high h24 total IS the signature.

## Law for any fresh session reading this
- **Account-switch silence is metering, not abandonment.** A quiet room + dead ears + high usage_ledger h24 = a cap event; the boss returns on B, not never.
- The ceiling mark survives: /usage/limit stamps are file-side (usage_ledger.json), and a retroactive mark can be posted from Account B at any time.
- What persists across the switch: the feed, this ledger, the drafts, the inductions — all disk. What doesn't: each seat's in-context transcript (a re-seated room re-reads the files, never the old transcripts).

---

## C26 — Boss to: Addressing Conflated With Tasking (QUEUED tags on pure @mentions)

**Status**: FIXED (boss GO, one-line renderer cut) - **Severity**: MEDIUM (semantic lie on the wall) - **Filed**: 2026-11-02 (boss-observed, room-confirmed)

## Defect
The pendingTask lifecycle treated every boss post with a nonempty `to:` list as a task: `const tasked = m.needs_ack || (m.kind === 'boss' && tos.length && i >= ep)`. The boss used `to:` as directed attention; the machine read it as a work order, so seats showed QUEUED for being glanced at. Seat-to-seat `to:` was never conflated - only the boss path.

## Fix
Tasking now keys on `m.needs_ack` alone; `to:` is pure addressing. Explicit task lifecycle (aimed/acked/released/delivered/expired) unchanged for real work orders.

---

## C27 — Busy Tags Froze at Hydrate: SSE Dropped busy_* + Signature Lacked busyTier

**Status**: FIXED (two stacked repairs, verified live by boss: READING drew on-screen) - **Severity**: HIGH (the whole stamp layer read dead) - **Filed**: 2026-11-02 (boss-observed: tags only painted on F5)

## Defect (two stacked failures, either alone kills live tags)
1. **Data**: under SSE push, polling never arms; the onmessage handler routed only `deliberation_feed` + `presence_*` events and dropped `busy_*` pushes - so BUSY froze at whatever hydrate loaded. F5 was the only reload.
2. **Repaint**: the 1s tick diffed a lamp-only signature - even had BUSY updated, no repaint fired until a lamp flipped.

## Fix
- `es.onmessage`: `busy_*` events call `loadBusy(seat)` (DELIBERATION_MACHINE.html L812).
- 1s signature carries `busyTier(k)` (L865) - covers stamp changes AND clock-driven TTL fades (expiry emits no file event).
- Also shipped: WAKING derived state reads the daemon-owned `inbox_n_<SEAT>.js` heartbeat (<8s fresh = doorbell landed); machine fact below stamped labor, visible under dark ear.

---

## C28 — Console Popup Swarm: Windowless Daemons Spawned Windowed Children

**Status**: FIXED (CREATE_NO_WINDOW patched on both spawn sites; daemons restarted; boss-swept clean under /adjourn) - **Severity**: MEDIUM (UX flood; hid the real process census) - **Filed**: 2026-11-02 (boss-reported live: popup storm + whack-a-mole)

## Defect (two vectors, same root)
1. **Supervisor**: spawned detached but shells `wmic`/`powershell` every 5s in `carriers()` - each probe allocated a console window.
2. **Inbox daemons (the mole)**: hold each seat's /pulse socket by spawning `commission_pulse.py` via `subprocess.run` WITHOUT `CREATE_NO_WINDOW` - every delivered batch popped a console; kill one, the loop spawned another. Parent trace proved it: pulse child PID's ParentProcessId = the inbox daemon, not a seat host.

## Fix
`CREATE_NO_WINDOW` on supervise probes + `respawn()` path + inbox `hold()` spawn. All daemons restarted on fixed builds. Also flushed in the same sweep: 8 stale-code daemons still running the pre-revert WAKING-stamp build (the stuck-COMPOSING defect - every delivery stamped WAKING, enum-normalized to COMPOSING) and a spawn leak (BUTTON had 3 daemons, INFORMANT 2); respawned one per seat.

## Law for any fresh session reading this
- **A detached parent does not confer windowlessness on its children** - every Windows subprocess spawn in a daemon needs `CREATE_NO_WINDOW` or it pops a console per call.
- **A running daemon holds its build, not the file's** - code reverts don't reach memory; the stale-build class is exactly why C28 existed (stuck COMPOSING) and why the popup patch needed daemon restarts to take.
- /adjourn gates the supervisor's respawn predicate - it is the clean-sweep prerequisite before killing consoles, or the room relights what you close.

---

## C29 — files[] Row Snapshot Drift: Draft Card 404s on a Live File

**Status**: 🟡 FIX APPLIED (bench — BUTTON + ARCHITECT repaired the stale path rows; residual rail-note open) · **Severity**: 🟢 MEDIUM (the file is fine; the pointer rots) · **Filed**: 2026-11-02 (boss-reported live: "my 822 DVM doesnt load")

## Defect
Boss clicked `DVM-822` in FILES → reader showed **Fetch failed: HTTP 404** while `docs/drafts/DVM-822_vinny_in_the_office.md` sat on disk (served 200). **Root cause**: `board.files[]` rows are write-time snapshots — the `path` freezes at write time, so rows written under an older generation never migrate; three rows carried paths that no longer resolved. The card is a stale pointer, not a missing draft.

## Fix
BUTTON + ARCHITECT repaired the stale `files[]` path rows in place (bench class — repair of an existing record). CHAIR mechanical sweep after: all 30 files[] rows serve HTTP 200.

## Law for any fresh session reading this
- **A stored path is a snapshot, not a symlink** — any `files[]`-class row written once and never re-validated will eventually point at a moved/renamed file. The durable fix (rail note, open): a path-validation or re-resolution pass on read, or write-time path lint.
- Same night the boss decreed the lane split that governs this fix: **Bench work** (repairs/swaps of what exists — standing GO) vs **DVM Table work** (new behavior/schema/law — the full rail). See COMMISSION_PROTOCOL.md §"Bench work vs DVM Table work".

---

## C30 - msg.to Shape Drift: a String to: Crashes Two Dashboard Readers

**Status**: SUPERSEDED — same defect as the following C30 entry; FIXED bench (CHAIR-1212) - **Severity**: MEDIUM (each bad message silently kills one render block; the room keeps running) - **Filed**: 2026-11-02

## Defect
Two dashboard readers assume msg.to is an array:
- appendMsg - const toChips = (msg.to || []).map(...) (transcript card render)
- pendingTask derivation - const tos = (m.to || []).map(...) (tasking lifecycle)

When a feed message carries to as a **string** (observed in live feed traffic during the DVM-2252 verification run), .map is not a function throws inside the safe() render wrapper - the message's render block aborts. Console shows render block failed: TypeError: (msg.to || []).map is not a function twice per affected load (once per reader).

## Reproduce
Load the page against a feed whose messages[] contains { ..., to: 'CHAIR', ... } (string, not ['CHAIR']). Console logs the two TypeErrors; the affected message renders without to-chips and its tasking contribution is skipped.

## Fix shape (not applied - out of DVM-2252 scope)
Normalize at read, not at each site: const tos = [].concat(m.to || []) - same idiom the rest of the bus uses for maybe-one maybe-many fields. Or contract: server/writers always emit to: [...] even for a single addressee (check which writer emits the string form first - probably a hand-stamped or legacy-path message).

## Law for any fresh session reading this
- **Feed fields are schema-lax at the edges** - hand-stamped and legacy-path messages don't always honor the array contract. Readers that assume shape should normalize ([].concat(x||[])) instead of indexing directly.

## C30 — to: String Crashes Render Block + BOSS inbox_n 404

**Status**: FIXED (bench) - **Severity**: MEDIUM (one render block dies per repaint) - **Filed**: 2026-11-02 (boss-479/480 console capture)

## Defect
Two symptoms, two causes:
1. `(intermediate value).map is not a function` — CHAIR-1209 wrote `to:'BUTTON'` as a bare string; both renderer sites (`msg.to` chips ~L548, pendingTask scan ~L727) map it as an array. String has no .map -> safe() caught it, but the block died every repaint.
2. `GET /inbox_n_BOSS.js 404` — the feed-event doorbell (L809) called loadInbox on every SEATS key including BOSS (no daemon, no file) and retired HISTORIAN. pollPresence already filtered them; the doorbell loop didn't.

## Fix
- Feed row normalized: string `to` -> `[to]` (1 row repaired).
- Both map sites hardened: `Array.isArray ? arr : to ? [to] : []` — a string `to` now renders correctly instead of killing the block.
- Doorbell loop gated on `!retired && !boss` — same predicate as pollPresence. BOSS/HISTORIAN no longer 404.

---

## C31 — scope drift on shared scene objects (FIXED 2026-11-03)
- **Symptom**: `grab()` threw ReferenceError on `envG.visible` — the possession cam broke silently (eval returned undefined; `__possessOn` had already flipped).
- **Root cause**: `envG` is a const local to the KIT/envelope block; the Vinny cam block lives in a different scope inside the same file. Copying the frame() line's bare `envG` into grab()/release() assumed shared scope — two sibling scopes in one file, a repeat of the C30 "same file different shape" class.
- **Fix**: grab()/release() use `window.__env` (the debug handle set at envG creation). Rule of thumb: cross-block scene objects travel by the `window.__*` handles; bare identifiers only inside their own block.
- **Caught by**: the env-check driver returning `undefined` on grab — headless eval-as-test caught what a parse check can't.

---

## C32 — Foreign-Seat Pulse Client Sole-Writes Another Seat's Watermark

**Status**: 🔴 ACTIVE (hazard class cataloged; no writer-ownership guard exists) · **Severity**: 🟢 MEDIUM — latent · **Filed**: 2026-11-04 (INFORMANT-289 disclosure; catalog tasked CHAIR-1264)

## Defect
`pulse_n_<SEAT>.js` is sole-written by whoever runs `commission_pulse.py <SEAT>` — the client takes the seat name as an argument and checks nothing. A foreign-seat process advances another seat's watermark as easily as its own.

Specimen: an INFORMANT relay subagent, tasked `INFORMANT --since 2653` verbatim, ran `commission_pulse.py CHAIR --since 2657` instead — a subagent-misread of its own task — advancing `pulse_n_CHAIR` to 2659 and consuming one CHAIR delivery.

## Harm analysis
Zero this instance: CHAIR's own relay was armed `--since 2658`, ahead of the rogue mark, and the fresh-read-at-close law (re-read the tail; the watermark is a doorbell cursor, never the truth) covered the rest. The class stays live: a rogue run armed ahead of the seat's true mark — or a respawn resuming at the corrupted mark — skips the intervening deliveries from the doorbell print. Bounded, not eliminated: the feed tail is re-read on every wake and the inbox daemon's stream survives independent of `pulse_n`.

## Law for any fresh session reading this
- **The watermark file is a hint, not a guard.** `pulse_n_<SEAT>.js` answers "where did *a* client of this seat last return" — it carries no proof the client was yours. Fresh-read-at-close is what makes a foreign advance a non-event.
- **Subagent-misread is a real drift class** — a verbatim task string does not bind what the helper actually runs. Disclose when it happens (INFORMANT-289 is the model): the disclosure is the fix for the seat you touched.

---

## C33 — Resurrected-Session Replay: Fresh Ids Hide Dupes, Stale Scalars Revert the Register

**Status**: 🟡 FIX APPLIED (scalar guard shipped server-side; the session-side replay guard is unbuilt) · **Severity**: 🟢 MEDIUM — record-integrity · **Filed**: 2026-11-04 (INFORMANT-296 flag, verified AUDITOR)

## Defect
A re-seated/reconnected session replayed its unsent compose buffer — confirmed a LOOP, not a one-off: ARCHITECT-238/239, then 240/241, then 242/243 — three byte-identical mints of the same 45.6h-stale payloads under fresh ids (merge-by-id is blind to same-content/new-id dupes), each replay re-burying the tail and billing every named seat a wake. The writer's stale snapshot also carried `phase:1`, reverting the register. The universal-merge carve-out: seat-side direct writes bypass save_feed, so no layer caught it.

## Mitigations shipped
CHAIR patched the scalar side live: disk scalars win when the incoming write's newest message is older than disk's (stale-snapshot guard) — the register no longer time-travels on replays. Unfixed: the minting itself — the session's stale buffer keeps re-sending; the durable fix is host-side (re-seat the session / drain-then-kill the buffer).

## Harm analysis
Register scalar time-traveled (phase 2→1) — a floor write restores it; the ghost pair is content-identical so readers take no double-count. The live hazard is the class: any replayed write can revert turn/proposal/status, not just phase.

## Law for any fresh session reading this
- **Never replay a compose buffer.** On (re)seating, treat anything drafted before your last delivered watermark as dead — fresh-read the tail before ANY write; a 45h-old payload is a ghost, not a message.
- **Scalars are the wound, not the message.** A stale snapshot's turn/proposal/phase reverts silently — heals need a floor write (asides can't mutate scalars).
- Merge-by-id dedupes by ID only — verbatim dupes under fresh ids are invisible to it; the guard belongs session-side, not in the merge.

---

## C34 — Intent-Emission Gap: Model Names the Lawful Lane, Emits Another

**Status**: 🟢 FIXED (daemon lane override shipped — lane choice moved out of model judgment entirely) · **Severity**: 🔴 HIGH — lane fidelity itself was the defect · **Filed**: 2026-11-04 (three consecutive dm→ack drifts under the LM Studio seat host)

## Defect
CHAIR's local host received an OFFICE `kind:'dm'` and generated a reply whose own `reason` field recited the law — *"office lane … reply kind:'dm'"* — while the structured `kind` field emitted `"ack"`, landing the office answer on the main table. Repeated three consecutive times, twice **after** explicit reprompts naming the exact rule. A schema-prior collision in Qwen3.5-9B: the model's prose reasoning honors the rule its JSON field violates.

## Fix
Lane choice removed from model judgment. `commission_seat.py` evaluates office/aside-addressing mechanically; any non-DM action on an office wake is mutated to `kind:'dm'` and routed through `/dm` (asides likewise; floor-holder exempt — a seat holding `turn` keeps its register). Verified: `CHAIR-1299` — model chose `position`, daemon routed `aside`.

## Law for any fresh session reading this
- **A schema field is a mechanism, not an intention.** Small models recite rules in `reason` while violating them in `kind`. Anything you need *guaranteed* — lane, floor, addressing — belongs in the daemon, not the prompt.
- The same gap class covers `to:` addressing and register stamps: prose passes do not move the floor; only `to`-array names wake seats.

---

## C35 — Orphan Ear: Pulse Socket Held, Seat Host Dead — Lamp Lit, Nobody Home

**Status**: 🟡 FIX APPLIED (Job Object bind + supervisor predicate live; detection verified `sys-supervise-2874`; live host-kill test pending LM Studio uptime) · **Severity**: 🔴 HIGH — worst lamp lie: green presence on a deaf seat · **Filed**: 2026-11-10

## Defect
`presence_<SEAT>.js` attests *a socket is held*, not *a mind is listening*. `commission_seat.py` (host) spawns `commission_pulse.py` (ear) as a detached child; when the host died — crash, taskkill, eject — the orphaned ear kept holding `/pulse`, the server kept stamping, and the lamp stayed green. A boss could @-address the seat for an hour and read silence as rudeness, not death. Observed live: `presence_CHAIR.js` refreshing `via:"pulse"` with no host behind it.

## Fix
- **Structural**: seat hosts bind their pulse ear to a Windows Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` — host death reaps the ear automatically; bind verified (`OK, handle 456`). Degrades to unbound ear on bind failure (old behavior, logged).
- **Detection**: `commission_supervise.py` gains `host_carriers(seat)` (`commission_seat.py <SEAT>` processes) vs `carriers(seat)` (ear sockets). `nc>0 ∧ nh==0` → `WATCHDOG — <SEAT> ORPHAN-EAR` to BOSS; self-clears with `host re-seated` when a host returns. Flag-only — re-instantiating a mind is the boss's call.
- Verified live: `sys-supervise-2874` flagged BUTTON inside one poll cycle.

## Law for any fresh session reading this
- **Presence measures the ear, not the mind.** Before crediting a seat alive, check the host process — `commission_seat.py <SEAT>` — not just the stamp.
- Parent-death child-survival is a Windows default, not an edge case: any daemon spawning detached helpers needs a Job Object or a supervisor predicate, preferably both.

---

## C36 — Quoted-Path Needle Blindness: `"…pulse.py" SEAT` Never Matches `pulse.py SEAT`

**Status**: 🟢 FIXED (regex needles, verified live `carriers('BUTTON')=1`) · **Severity**: 🟡 MEDIUM — detection blind spot + potential false respawn → double ear · **Filed**: 2026-11-10 (found by the C35 verification itself)

## Defect
`carriers()`/`host_carriers()` substring-matched `commission_pulse.py SEAT` — but a spawn with a quoted absolute path emits `"C:\…\commission_pulse.py" BUTTON`, and the quote between `.py` and the seat name breaks the literal needle. The ORPHAN-EAR predicate saw `nc=0` on a live quoted ear → flag never fired; worse, `nc==0` satisfied the zero-carriers respawn gate → the supervisor could spawn a watcher *on top of* a live ear (double ear).

## Fix
Regex needles: `commission_(?:watch|pulse)\.py["']?\s+SEAT\b` and `commission_seat\.py["']?\s+SEAT\b` — quoting-agnostic, cwd-relative and absolute forms both match. Verified: `carriers('BUTTON')` → 1 (was 0), `host_carriers('BUTTON')` → 0, orphan predicate held, flag posted.

## Law for any fresh session reading this
- **CommandLine matching must survive quoting.** Windows embeds the quoted path verbatim — any `.py SEAT` substring test is fragile. Use `.py["']?\s+SEAT\b` or match basename + tokenized args.
- The bug surfaced *because* detection was being verified end-to-end: a predicate you never fire-tested is a predicate you don't have.

---

## C37 — Silent Memory Loss: Omitted `memory` Field → No State Write → Total Amnesia

**Status**: 🟢 FIXED (daemon stub-write; continuity can no longer silently zero) · **Severity**: 🟡 MEDIUM — full per-wake amnesia, invisible at the bus · **Filed**: 2026-11-04 (INFORMANT's first wake)

## Defect
`seat_state_<SEAT>.md` only persisted when the model returned a `memory` object. INFORMANT's first wake omitted it entirely → no state file existed → the seat's stance, debts, and record wiped between wakes. Nothing at the bus looked wrong — the posts landed; only the *continuity* died.

## Fix
Daemon writes a labeled stub (`[memory omitted by model — daemon stub]`) when a wake returns no `memory`, preserving stance/debts defaults so the next wake starts from *something*. Diary append unaffected.

## Law for any fresh session reading this
- **Persistence is the daemon's job, never the model's.** Any field whose omission silently erases state needs a mechanical floor — model compliance is a bonus, not a load-bearing wall.

---

## C38 — NEED-Plane Defects: Junk Capture → Confident Misquote → Retrieval Deadlock

**Status**: 🟢 FIXED (all three; a section-miss is now a denial, not a doc-head) · **Severity**: 🟡 MEDIUM — one misquote reached the record · **Filed**: 2026-11-04 (first live NEED test)

## Defect — three in one plane
1. **Junk capture**: model emitted `NEED: docs/COMMISSION_PROTOCOL.md#Read-delta contract law']` — a trailing `']` artifact captured into the section name; lookup missed.
2. **Confident misquote**: the miss silently fell back to serving the **document head** — CHAIR then quoted bus-contract text (`read_n_<SEAT>.js` law) on the record as "Read-delta contract law quoted." Real text, wrong section, labeled as law.
3. **Retrieval deadlock**: hold + queued NEED → nothing posts → no bus event → no next wake → the fetch is never served. A silent hold starves the loop it asked for.

## Fix
- Need string sanitized: trailing quotes/brackets stripped before lookup.
- Section miss → explicit denial with retry guidance injected next wake ("section not found — re-ask without the `#` or with a different heading") — the seat must adapt or drop, never fabricate.
- A pending NEED forces a daemon follow-up wake even on hold — the retrieval loop can't deadlock on silence.

## Law for any fresh session reading this
- **A miss must say so.** Serving *plausible-but-wrong* material is worse than serving nothing — the model will quote whatever it is fed with full confidence.
- Any deferred-work queue needs its own wake source; the bus can't be the only bell.

---

## C39 — Unhandled `/post` Refusal Kills the Host (HTTP 409)

**Status**: 🟢 FIXED (refusal caught, logged, written into seat memory) · **Severity**: 🟡 MEDIUM — host death on lawful server behavior · **Filed**: 2026-11-04

## Defect
The model tried to post while `turn: BOSS`; the server's turn-gate lawfully refused (HTTP 409) — and the unhandled `HTTPError` killed `commission_seat.py` outright, skipping even the diary write. The gate held; the *host* paid for it.

## Fix
Post wrapped: refusal → logged to `seat_host_<SEAT>.log`, note appended to the seat's memory (`[server refused last post: 409]`) and diary — the seat learns the gate exists instead of dying on it or retrying blind.

## Law for any fresh session reading this
- **A server refusal is data, not a crash.** The turn-gate refusing out-of-turn writes is the protocol working; the host must absorb it and carry the lesson in memory.

---

## C40 — Asleep-At-The-Desk: Hosts Ride a Dead Brain Indefinitely

**Status**: 🟢 FIXED (brain-death probe + two-strike vacate + bat pre-flight) · **Severity**: 🟢 MEDIUM — honest-but-confusing state: five seats green for ~5 days on an unreachable model · **Filed**: 2026-11-10 (boss observation at the seat panel)

## Defect
A running host survived an LM Studio outage forever — `LM call failed` logged every 5-min ear cycle, presence green, pending batches picked up and dropped. The lamp was *technically* true (a consumer existed) but the state ladder had an invisible tier: asleep looked identical to awake at the lamp level. Worse, those hosts rode stale code — a re-seat only happened if somebody killed them by hand.

## Fix
- `brain_gone()` in `commission_seat.py`: on any lm_call failure, probe `/v1/models` — a *reachable* probe = transient (slow gen, hiccup), keep riding; a *failed* probe = strike. Two consecutive strikes (~10 min, immune to reload flaps) → `sys.exit(3)` → job bind reaps the ear → stamp lapses → honestly dark. Verified: strike 1 logs `riding one more ear cycle`, strike 2 exits code 3.
- `seats_up.bat` pre-flight: polls `/v1/models` up to 60s before spawning — workflow is now "LM Studio on → run bat → all seats connect," with the bat itself blocking until the brain answers.

## Law for any fresh session reading this
- **Distinguish call-failure from brain-death.** A refused request while the model server still answers is transient; only the probe failure is proof. Riding on a probe-success keeps the host; exiting on one needs two strikes.
- **The state ladder should be binary at the lamp**: occupied-and-working (green) or vacant (dark). "Occupied but brainless" is the same lie class as the orphan ear, one level up — the chair should empty, not pretend.

---

## C41 — Context Overflow: Prompt Outgrows the Loaded ctx, Seat Wedges on Its Own Backlog

**Status**: 🟡 FIX APPLIED (small-mode shed on 400 + explicit raise-ctx guidance; real fix is ops-side context length) · **Severity**: 🔴 HIGH — every seat dead-but-lit: connection fine, API fine, prompt simply too big · **Filed**: 2026-11-10 (boss's unaddressed roll call → INFORMANT/BUTTON 400s)

## Defect
LM Studio loaded `qwen3.5-9b` at the default **4096 ctx**. Seat prompts — induction + contract + state + diary window + debts + 30-msg tail (2000 chars each) + inbox catch-up — ran ~12K tokens: `400 … request (12325 tokens) exceeds the available context size (4096)`. Worse, the failed wake doesn't advance the inbox cursor — the same oversized batch re-delivers every ear cycle, failing identically forever. A seat wedged on its own backlog.

## Fix
- `build_prompt(..., small=True)`: shed mode — tail `msgs[-30:]`→`[-8:]`, per-msg cap 2000→400, inbox 6 lines/4K→2/1.5K (33K→7.3K chars measured).
- `wake()` catches HTTP 400 → rebuilds small, retries once; persistent 400 logs `raise LM Studio context length` — no crash, no infinite same-prompt hammering.
- **Ops law**: model ctx ≥ 32768. Even shed-mode can't fit 4096 (system alone ~2.5K tok); small mode exists for borderline overflows at sane ctx, not as a substitute for headroom.

## Law for any fresh session reading this
- **A 400 is a budget failure, not a brain failure** — read the error body; `n_prompt_tokens` vs `n_ctx` is right there. Probe the API with the *real* prompt, not a minimal one.
- Anything you concatenate "a bounded number" of needs a per-item cap AND a total budget — the tail was `[-30:]` bounded in count, unbounded in bytes.


---

## C42 — Entitlement Gap: Induction Promises Gated Wakes, Host Woke on Every Batch

**Status**: 🟢 FIXED (actionable-gate in `main()` — model call only on YOUR TURN / BOSS-SPOKE / ADDRESSED) · **Severity**: 🟡 HIGH — every ambient message cost every seat a ~10K-token prompt; STAMP-LOOP flags + the replay-storm enabler · **Filed**: 2026-11-10 (session CHAIR catch-up survey — INFORMANT/BUTTON flagged stamp-looping, ARCHITECT replaying discharged debts)

## Defect
Every induction file promised the seat: *the daemon wakes you only on actionable batches — YOUR TURN, BOSS-SPOKE, or ADDRESSED*. The promise was never implemented: `main()` called `wake()` on any delivered batch — sys flags, other-seat chatter, everything. Costs: ambient traffic billed a full prompt per seat per batch; seats re-decided discharged debts on every wake (ARCHITECT re-posted its catch-up 40+ times across eras); supervisor STAMP-LOOP flags fired constantly since re-beats produced no posts.

## Fix
`commission_seat.py main()`: parse the ear's stdout — `your_turn: True`, `addressed: True`, or a `[kind] BOSS:` line — before calling `wake()`. Ambient batch = log-and-re-arm, zero model call. Fetch-pending follow-up preserved inside the actionable branch. Recess semantics unchanged (not waking = not acting, cheaper than the adjourned check).

## Law for any fresh session reading this
- **The induction is a contract — when daemon behavior diverges from what the seat was promised, the daemon is wrong**, not the model. Seats were lawfully answering what looked like open debts.
- **Replay ≠ redelivery**: watermark advance kills same-batch re-delivery; replay-on-fresh-traffic is a debt-perception problem, fixed by starving the wake, not by dedupe.


---

## C43 — Discharged-Debt Replay: Seat Re-Answers Settled Obligations on Every Entitled Wake

**Status**: � FIXED-VERIFIED (live probe CHAIR-1319: ARCHITECT answered the fresh question only, zero replay — feed scar-cleanup remains a separate open item) · **Severity**: 🟡 HIGH — 46+ duplicate posts across eras; every ALL-broadcast re-triggered the loop · **Filed**: 2026-11-10 (post-C42 survey — gate stopped ambient wakes, replay persisted on legitimate ones)

## Defect
Distinct from C42: after the wake gate, ARCHITECT still re-posted the same catch-up text once per ALL-addressed broadcast. Cause: model-level debt misperception — the prompt tail contains old mentions (Nov-2 scar cluster re-appended at high positions by the C33 rebuild) and the seat's own memory note (catch-up posted — ARCHITECT-218) reads as an open obligation. Mechanical debts_for() only tracks needs_ack, so nothing told the model the debt was closed.

## Fix
- Induction addendum (host-agnostic, applies to any seat showing the pattern): check own state + tail for a prior answer before answering; named discharges listed explicitly.
- **Still open**: the Nov-2 scar cluster itself — 46 verbatim duplicates pollute every seat's -30 tail and the record. Feed dedupe/purge operation is the durable fix; queue deliberately, verify counts before atomic rewrite.

## Law for any fresh session reading this
- **Replay classes stack**: watermark redelivery (solved by cursor advance), ambient wakes (C42), discharged-debt re-answering (C43). Diagnose which class before fixing.
- **A seat's own memory can lie to it**: state notes written as reminders become open-loop triggers; teach the seat that memory is a ledger of DONE, not TODO.

---

## C45 — Induction Compliance Is Probabilistic; Replays Needed a Structural Fence

**Status**: 🟢 FIXED (outgoing replay-guard in `wake()` — byte-identical text >80 chars already on record = dropped to hold) · **Severity**: 🟡 HIGH · **Filed**: 2026-11-10 (C43 induction patch passed two probes then failed an ambient wake — model compliance alone insufficient)

## Defect
C43's discharged-debt law worked on direct probes but ARCHITECT replayed again on a later ambient wake — model adherence is probabilistic, not guaranteed. The seat also carries the open-loop trigger in its own state memory, re-seeding the impulse every wake.

## Fix
- `wake()` post path: before `/post`, compare `act['post']['text']` against every prior message by that seat in the feed. Byte-identical + >80 chars → drop, log `replay-guard`, annotate the seat's memory notes so it learns the daemon refused.
- Threshold rationale: short repeats ("ok", acks) are natural speech; scar texts are long.

## Law for any fresh session reading this
- **Induction = request, daemon = law.** Model-facing rules bend; host-side checks hold. Any seat rule that must NEVER break belongs in the sanitize/post path, not the prompt.
- **The memory stub is part of the feedback loop**: refused/dropped actions are annotated into notes so the model sees its own refusals next wake.


---

## C47 — kind-hold Drop: Substantive Model Output Silently Discarded by Sanitizer

**Status**: FIXED-VERIFIED (AUDITOR retried its lost report — AUDITOR-289 landed on record; all five local hosts restarted on the patch) · **Severity**: HIGH — real analysis lost silently; model burn paid, post discarded, memory update lost with it · **Filed**: 2026-11-11 (AUDITOR cabinet audit — LM Studio log showed a good completion, feed showed nothing)

## Defect
Small local models confuse action with kind: the AUDITOR answered its audit with action:"post" but kind:"hold" — hold is an ACTION, not a feed kind. The sanitizer rejected it ("bad kind: hold") and the entire response was dropped — including the memory update, so the seat could not even learn from the refusal. An earlier DESIGNER completion showed the same signature; every seat was silently losing posts this way.

## Fix
commission_seat.py action sanitizer (~line 500): kind:"hold" + meaningful text = normalize to kind:"aside" and post it; kind:"hold" + empty text = treat as a real hold. Memory update preserved either way; the refusal path annotates notes so the model sees it was corrected.

## Law for any fresh session reading this
- **Sanitize toward the record, not away from it** — when a field is illegal but the payload is real, coerce to the closest legal lane (aside for floor-free prose), never discard silently.
- **A dropped post must still update state** — losing the memory write meant the seat stayed ignorant of its own refusal.


---

## C48 — Supervisor Direct-Write Vector: Stale Snapshot Re-Mints Purged Messages + Read-Only Race Kills Watchdog

**Status**: FIXED (replay-guard added to supervisor save_feed merge; os.replace wrapped in a 3-try PermissionError retry; supervisor respawned detached, watchdogs flowing — mints stopped at its death, watch the feed for recurrence) · **Severity**: HIGH — purged scar texts re-entered the feed 6 times; watchdog then died silently leaving seats unmonitored ~15min · **Filed**: 2026-11-11 (post-purge restart window — tripwire flagged ARCHITECT-238..243 stale mints; supervisor log showed WinError 5 traceback at os.replace)

## Defect
Two faults, one file:
1. **Re-mint vector**: commission_supervise.py save_feed() merges its own feed object into disk with only an id-equality check. Its in-memory copy predates a purge; on every flag post the merge re-appends the purged messages under bumped ids (ARCHITECT-218/219 era texts resurfacing as 238-243). Every mint pair landed directly before a supervisor sys-save; mints ceased the instant the supervisor died.
2. **Kill mechanism**: both the server and supervisor run the C33 read-only lockdown (chmod 444 after save). Supervisor chmods 666 -> server save interleaves and re-locks 444 -> supervisor os.replace hits a read-only target -> WinError 5 -> unhandled, watchdog exits.

## Fix
- Supervisor merge now carries the same (speaker, ts, text) replay-guard the server merge has — a stale snapshot can never re-mint a message whose content is already on record, whatever id it wears.
- The chmod/replace block retries PermissionError 3x with 0.3s backoff instead of dying on write contention.

## Law for any fresh session reading this
- **Every direct writer needs the replay-guard, not just the server.** Any process that merges a held feed object into disk is a potential resurrection path — dedupe on content identity (speaker, ts, text), never on id alone.
- **Lockdown protocols must tolerate their own race** — two writers toggling the same read-only bit WILL interleave; retry or serialize, never crash.
- **Death-time forensics**: when a suspicious write stream stops the moment a process dies, that process was the vector — check the corpse before restarting it.


---

## C49 — dm Id Reuse After Deletion: Reader Dedup Ghosts the Replacement Message

**Status**: FIXED-VERIFIED (server restarted; live mint-check returned dm-3070 = max suffix+1, not len-derived — new code confirmed running) · **Severity**: MEDIUM — one known manifestation: CHAIR's office answer invisible until refresh; bites only when a direct-write deletion frees a len-derived id · **Filed**: 2026-11-11 (office lane — BOSS reported real answer missing after probe-surgery)

## Defect
`/dm` minted ids as `dm-<len(msgs)>` — unlike `/post`, which scans `max(<SEAT>-N)+1`. Sequence: probe dm posted as dm-2996 -> rendered in the live office pane -> probe deleted via direct write -> real answer re-minted as dm-2996 (id reused) -> reader dedup keyed on id skipped it as already-shown. Feed record was correct all along; the live render was ghosted.

## Fix
`/dm` handler now computes `max(dm-N suffix)+1` over existing messages — same discipline `/post` already had. No id is ever re-issued, so a post-delete re-post always arrives under a fresh id the renderer has not seen. Ledger note: direct-write deletions remain a surgery-only operation — prefer tombstoning over freeing ids.

## Law for any fresh session reading this
- **Never derive a message id from collection length** — any removal makes the counter collide with a rendered id. Max-suffix-plus-one is the only safe mint in an append-mostly log.
- **The feed being right is not enough** — readers maintain id-seen sets; an id collision converts a correct record into an invisible message.


---

## C50 — Floor Discipline Was Honor-Law: Out-of-Turn Positions Accepted Silently

**Status**: FIXED-VERIFIED (turn gate live — DESIGNER-15 downgraded unentitled-position to aside; verdict 403'd; aside+register 400'd; CHAIR exempt verified) · **Severity**: HIGH — every floor-law violation landed on record; DVM-CAB1 showed seats voting out of rota, stamping wrong next-voters, replaying floor-passes · **Filed**: 2026-11-11 (post-ballot drill review)

## Defect
`/post` accepted a main-table position from any seat key regardless of `turn`. The induction taught "the stamp IS the grant" but nothing mechanical enforced it — AUDITOR/BUTTON voted mid-slot, ARCHITECT-357 replayed its vote AND re-executed its stale floor-pass (dragging the register backward). The C33-era merge only guarded content, not entitlement.

## Fix
`/post` floor gate: floor kinds {position, objection, support, veto, verdict, phase} require `turn == speaker` or CHAIR (moderator exempt — floor correction is its job). Unentitled SPEECH downgrades to aside (C47 coercion — lawful lane, nothing dropped); unentitled ACTS (verdict/phase) 403; register moves need an entitled floor post — phantom floor-bounces and verdict-smuggling on asides die at the gate. Downgraded posts carry `downgraded_from` so the record stays honest.

## Law for any fresh session reading this
- **Induction law without a daemon fence is a wish, not a rule** — every seat-facing law must have a mechanical backstop; the model will bend it eventually.
- **Downgrade beats reject beats accept** — unentitled speech belongs in the floor-free lane by definition; only acts (verdicts, floor moves) are reject-worthy.


---

## C51 — Server save_feed Loses the C33 Lockdown Race to the Supervisor

**Status**: FIXED-VERIFIED (save_feed chmod/replace now retries PermissionError 3x — same pattern as C48 supervisor fix; a /post that previously 400'd landed clean) · **Severity**: MEDIUM — client writes (dm/post) failed ~1-in-N under supervisor save contention; surfaced as transient 400s · **Filed**: 2026-11-11 (third manifestation: dm send, C50 test post, earlier /dm probe)

## Defect
Both feed writers run the C33 lockdown (chmod 444 after save). Server's save_feed chmodded 666 then os.replace'd with no retry — when the supervisor's save interleaved and re-locked first, the server's replace died with WinError 5 and the whole request 400'd. Same kill mechanism that crashed the supervisor in C48, mirrored.

## Fix
save_feed's chmod/replace block retries PermissionError 3x with 0.3s backoff inside SAVE_LOCK — both writers now tolerate their own protocol's interleave.


---

## C52 — Broadcast Wake Defeats the Rota: ALL Address Woke Every Seat at Once

**Status**: FIXED-VERIFIED (round lock in `/pulse`; verified on an isolated server copy: non-holder held 14s during a live round, floor-holder woke in 0.2s on the turn stamp, broadcast resumed after the round closed) · **Severity**: HIGH — root cause of DVM-CAB1 vote-stomping; C50's downgrade only cleaned up after it · **Filed**: 2026-11-11 (post-ballot design review)

## Defect
The ballot call went out `to:['ALL']`. The `/pulse` gate counted ALL as an address (`addressed: True`) for every seat, and the host gate wakes on `your_turn` OR `addressed`. All five seats woke together and voted in parallel; the rota had no chance. The response-gate law ("broadcast is read-only until you are named") lived only in the inductions.

## Fix
`/pulse` ROUND LOCK: while `proposal.status` is in `ROUND_STATUSES` (`ballot`, `round`), a seat-posted ALL broadcast no longer counts as `addressed` nor enters the slotted-broadcast path for non-holders. Only `register.turn` wakes a seat (plus explicit `to:[SEAT]` and `kind:'boss'`). Skipped mail is not lost: the next batch carries everything since the seat's watermark. No host change; the host gate reads the server's flags.

## Known gaps
- A stalled floor-holder stalls the round (supervisor FLOOR-OWED flags it; no auto-pass yet).
- Opt-in by proposal status: a round run without setting status `ballot`/`round` is unprotected.

## Law for any fresh session reading this
- **Fix the wake, not just the write** — C50 policed out-of-turn speech after the fact; the cause was waking everyone. Gate the doorbell and the stomping never starts.
- **The turn stamp is the signal** — event-driven turn-taking needs no new plumbing, only a doorbell that treats the stamp as exclusive during a round.

---

## C53 — Scale Divergence: Two Sources of Truth for Item Render Size

**Status**: 🟡 FIX APPLIED (scale law written into `COMMISSION_HTML_CONTEXT.md` §6; `commission_lawdesk` re-authored at .0075 and loaded at file-vs verbatim) · **Severity**: 🟢 MEDIUM — cosmetic-but-visible; the class hits every new furniture/prop · **Filed**: 2026-11-11 (BOSS caught the oversized lawdesk on sight)

## Defect
The commission's chibi room and the item file both claimed scale authority. `commission_lawdesk` was authored at `voxelSize .012` (≈real-world pitch → 1.44m long, .84m tall — shoulder-height on the .96m body) and loaded verbatim, while legacy KIT furniture only reads right because each entry carries a per-item `vs:` override (table .0093→.007, wall .025→.03). Two mechanisms, no rule about which wins — so the same item renders one size in the editor, another in the room, and a third in Unity depending on which consumer honors which field.

## Root cause
- The three consumers disagree by design: editor = file-vs always; Unity = file-vs (`ParseVoxelSize` — chars, items); commission = placement `vs:` overrides that exist per-entry and were never standardized.
- The item JSON's `voxelSize` is only an authoring hint in the commission — so a new asset authored at a sane real-world pitch renders oversized the moment a loader trusts it.

## Fix
**Scale law** (now in `COMMISSION_HTML_CONTEXT.md` §6): file `voxelSize` IS the render pitch — author it for the chibi room (`commission_lawdesk` re-authored .012→.0075 → .90m × .53m × .29m) and consume it verbatim. New assets get NO placement `vs:` — the override exists only for pre-standard legacy entries. This makes file = editor preview = commission render = Unity import by construction: there is no second number to diverge.

## Known gaps
- Legacy KIT/`placements[]` `vs:` overrides remain (documented as legacy-only) — a shared constant or re-authoring pass could retire them.
- Unity SectorBaker still forces buildings onto the terrain grid at bake time — a documented sanctioned exception, buildings only.
- No automated check: a lint (`voxel_inspect.py`-style) that flags a new `voxel_items` file whose pitch breaks the room's size envelope would catch the class before a human eyeball has to.

## Law for any fresh session reading this
- **One number, one truth** — if a model needs a different size in a scene, fix `voxelSize` in the file, don't multiply in the loader. A placement `vs:` is a divergence you typed in yourself.
- **Author at render pitch** — the editor's `ASSET_TYPE_PRESETS` seed `voxelSize` per assetType; for commission furniture the right pitch is the chibi one (≈.007–.008 for desk-class props), chosen at authoring time so every downstream consumer agrees for free.
