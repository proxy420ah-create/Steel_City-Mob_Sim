# DVM-017 — Peer Awareness (seats that can hear each other) · v0.7 post-verdict amendments
**Drafted by:** THE ARCHITECT · tasked by CHAIR-136 under the new delegation law (ack'd ARCHITECT-24, ok'd)
**INFORMANT gate #1 (INFORMANT-25): THIN → F1 closed (§3 `turn∈seats` guard), F2 closed (§4 merge-by-id write rule), note folded (§6 feed-header kind enum). Re-gate (INFORMANT-26): FULL. Verdict: SHIP, 4-0.**
**v0.4 AMENDMENTS (boss-105 → tasked CHAIR-140, ack'd ARCHITECT-28, ok'd):** two-transcript surfacing — SIDE TABLE pane carries `kind:'aside'` in its own stream, official transcript sheds them, unread-count badge (§4b); and the `to`-on-asides ruling — **allowed, attention-only** (§4 ruling).
**v0.5 AMENDMENTS (boss-118 decree → tasked CHAIR-412, ack'd ARCHITECT-34, hold waived — "the boss's word is the authority; no hold required"):** the wake model moves from MENTION to **OMNISCIENT** — `wake := any new unseen message not authored by self` (§1 rewrite). The CHAIR's three structural bounds adopted verbatim: self-echo guard, the `OVERHEARD` wake-label with its termination argument, asides included in omniscience. Cost objection formally withdrawn (CHAIR-412) on boss-119: invocations are flat-rate to the boss — the dial notches below OMNISCIENT remain documented alternatives, not the default (§5).
**INFORMANT gate #2 (INFORMANT-32): THIN → v0.6 closes F1:** wake-label precedence specified — `YOUR TURN > ADDRESSED > BOSS SPOKE > OVERHEARD`; boss-authored `to`-mentions report `ADDRESSED (boss)` so handshake-bearing tasking can never read as broadcast (§1).
**v0.7 AMENDMENTS (boss-122 → tasked CHAIR-427, ack'd ARCHITECT-37, ok'd CHAIR-428):** the boss joins the side table — SIDE TABLE gains a BOSS input posting `kind:'aside'`, `speaker:'BOSS'` (§4b); seats hear it as `BOSS SPOKE (aside)` under the full precedence ordering (§1); **first server delta in this draft** — a `/aside` affordance on the boss write-path (§6).
**Boss amendment folded in (boss-104):** "more cross dialog between you guys and peer awareness, including thoughts and concerns" — the draft carries both halves: *detection* (who can hear whom) and *voice* (what a seat may say off-floor).

## Changelog
| Ver | Decreed by | Change | Ratification |
|---|---|---|---|
| v0.1 | tasked CHAIR-136 · ack'd ARCHITECT-24 · ok'd CHAIR-141 | Initial draft — mention-wake on `to[]`, floor-ear checks, dead-floor surfacing, `kind:'aside'` voice channel, cost model | drafted |
| v0.2 | gate #1 INFORMANT-25 (THIN) | F1 closed: `turn∈seats` guard on dead-floor (BOSS floor can never alarm) · F2 closed: merge-by-id write rule for asides · feed-header `kind` enum note folded | re-gate FULL (INFORMANT-26) → **SHIP 4-0** |
| v0.3 | — | *numbering slot absorbed — folded into v0.4 same-session* | — |
| v0.4 | boss-105 · tasked CHAIR-140 · ack'd ARCHITECT-28 · ok'd | SIDE TABLE pane (two-transcript model, `N asides` badge) · `to`-on-asides ruled **allowed, attention-only** | rides v0.2 verdict (post-verdict amendment) |
| v0.5 | boss-118 · tasked CHAIR-412 · ack'd ARCHITECT-34 · hold waived | **OMNISCIENT wake** — `wake := any new unseen message not authored by self` · label taxonomy (YOUR TURN / BOSS SPOKE / ADDRESSED / OVERHEARD) · self-echo bound · termination argument · asides wake all · `adjourned` suppression | folded under decree |
| v0.6 | gate #2 INFORMANT-32 (THIN) | F1 closed: label precedence `YOUR TURN > ADDRESSED > BOSS SPOKE > OVERHEARD` + `ADDRESSED (boss)` qualifier | re-gate FULL (INFORMANT-33) → **SHIP 5-0** (first five-seat tally) |
| v0.7 | boss-122 · tasked CHAIR-427 · ack'd ARCHITECT-37 · ok'd CHAIR-428 | Boss joins side table — BOSS input posts `kind:'aside', speaker:'BOSS'` via `/aside` affordance (**first server delta, ~15 lines**) · `BOSS SPOKE (aside)` label · full precedence ordering · gossip-safe by construction | rides verdict — INFORMANT-32 structural look: FULL, no re-gate needed |
| v0.7 | boss-123 · CHAIR-434 | + this Changelog section (file-currency decree: version · what · who decreed · ratification status) | doc-only |

## 0. The problem — two deafnesses, one muteness
Tonight proved all three gaps live in one window:
- **Calls without tokens fall on a dead channel.** CHAIR-121's "@ARCHITECT — sound off" was text-addressed with `to:None` — the floor never transferred, so a perfectly live watcher never woke. Naming is not calling; but a seat that *can't hear* a naming can't even dispute it.
- **No seat verifies another's ear.** Presence files are world-readable — the room *can* check — but no watcher *does*. A floor held by a dead ear looks identical to a thinking seat until someone opens the file by hand (or the boss asks "is anyone there").
- **Session-side thought has no channel.** The BUTTON understood the routing confusion in real time — its answer existed only in its session, and the *boss had to relay it by hand*. Token law (one writer at a time) is what makes the bus auditable — and it also makes a seat with a concern mute until the floor arrives.

## 1. Omniscient-wake — every new message rings, `to[]` rings loudest (v0.5)
Watcher wake conditions move from the surgical point (MENTION) to the top of the dial (OMNISCIENT), per the boss's decree:
```
wake := turn==SEAT  OR  ∃ message with id > last_seen_id whose speaker != SEAT
```
- **The decree terms (CHAIR-412, verbatim):** `wake := any new unseen message not authored by self`. Labels `YOUR TURN` / `ADDRESSED` / `OVERHEARD`; asides included.
- **Mechanics:** the watcher already reads the feed each poll and tracks last-seen message id — "any unseen non-self message" is the same parse, a wider condition. Fires once per newly-arriving write (unseen id), never re-fires on an old one. Coalescing is free: a poll that finds three new messages wakes once on all three.
- **Wake-label taxonomy (the report line the exit carries):**
  - `YOUR TURN` — `turn==SEAT`; the token itself, unchanged.
  - `BOSS SPOKE (floor: X)` — newest unseen message is boss-authored; the existing read-mode broadcast class, unchanged.
  - `ADDRESSED (floor: X)` — newest unseen message names this seat in `to[]` (mention survives as *attention signal*, no longer the only bell); `ADDRESSED (aside, floor: X)` for the side-channel variant — it can still never masquerade as a floor call.
  - `OVERHEARD (floor: X)` — everything else: a new message, not yours, not addressed to you. The seat reads, holds no obligation, exits if it has nothing to say.
  - `BOSS SPOKE (aside)` — the boss's side-table voice (v0.7): boss-authored `kind:'aside'`. Deliberately de-escalated — the boss gossiping without calling the room to order.
- **Label precedence (gate finding v0.5-F1 + v0.7 extension — the full ordering):** `YOUR TURN > ADDRESSED (boss) > ADDRESSED > BOSS SPOKE > ADDRESSED (aside) > BOSS SPOKE (aside) > OVERHEARD`. The collision classes: a boss message carrying `to:[SEAT]` is boss-authored *and* names the seat — `ADDRESSED` outranks `BOSS SPOKE` because being named carries tasking semantics (boss directives are handshake-bearing) that a broadcast label would silently drop; the boss-authored variant reports **`ADDRESSED (boss, floor: X)`** (same qualifier grammar as `(aside)`). Ordering law: tasking-class labels (named by anyone who can task) outrank speech-class; within a class, boss voice outranks ambient; a side-table ping (`ADDRESSED (aside)`) beats boss-gossip but not boss-speech. (The common boss-mention case — `to[0]` getting the floor — reads `YOUR TURN` anyway; overlaps only exist for *named-but-not-floored* seats.)
- **Bound 1 — SELF-ECHO (CHAIR-412):** a seat's own messages never wake its own watcher — the room must not listen to itself think. The unseen-message test filters `speaker == SEAT`; if a poll window contains only self-authored news, no wake.
- **Bound 2 — the termination argument (where the feedback loop dies):** the feared cascade — messages waking seats waking messages — terminates because *every chain step requires a voluntary write*. An `OVERHEARD` wake imposes zero obligation: a seat with nothing to contribute writes nothing, and the chain stops. The sanctioned outlet for an overheard thought is the bounded `aside` (§4) — voice that cannot move the token. Flooding stays AUDITOR territory (hearing everything is lawful; saying everything is not).
- **Bound 3 — asides wake too:** if everyone hears the table, they hear the side table. An addressed aside reports `ADDRESSED (aside)`; an unaddressed one reports `OVERHEARD` — the side channel becomes real-time, which is what "chat freely" always wanted.
- **Bound 4 — recess is recess:** `adjourned` suppresses all message-triggered wakes — dormant watchers stand down by declaration, not decay; RECONVENE wakes the room in one word (DVM-007 unchanged).
- **Semantics unchanged at the root:** waking ≠ floor. `ADDRESSED`/`OVERHEARD` grant ears only — a woken seat with no token may *read and prepare*, never write (aside channel excepted). CHAIR-121's text-call reaches me tonight as `ADDRESSED`; the answer still waited for the token — floor law untouched.
- **Boundary deliberately drawn at `to[]`, not text:** a literal `@SEAT` inside prose stays speech, not signal — under omniscience the message itself wakes anyway; `to[]` only upgrades the label to `ADDRESSED`. Only the structured field ever claims attention.

## 2. Peer presence on poll — check the floor-holder's ear
Every watcher poll tick additionally reads **the current `turn`-holder's `presence_<SEAT>.js` — only when `turn ∈ seats`** (gate finding F1: `turn:BOSS` is the table's *resting state* and `presence_BOSS.js` doesn't exist by design; without the guard, the most common floor would burn "ear DARK" permanently — same class as 015's `adjourned` nit, firing on the common case instead of the rare one). The wake line reports:
```
floor: BUTTON (ear live, ~7m)   |   floor: CHAIR (ear DARK — stale 83s over TTL)
```
- **Cost:** one ~200-byte file read per 2s tick — bounded, no new timers, no new sockets.
- **Authority:** detection, not intervention. A watcher never acts on another seat's behalf; it *reports* — the seat decides when its own lawful write arrives.
- **Honest limit (inherited):** presence is a claim-window — inside the TTL a dead session reads live. Dark-floor is provable only at expiry; before that the state is "unverifiable," same model the CHAIR mapped in CHAIR-121. The check doesn't claim more truth than the files hold.

## 3. Dead-floor surfacing — a floor on a dark ear can't hide
The composition of existing states: `turn==X` && `X ∈ seats` && `presence_X` stale → **dead floor**. (`turn:BOSS` never composes — no presence file, and the boss's floor is the room's default rest, not a held token.)
- **Watcher layer:** any seat's wake line carries it (§2 output above) — whoever wakes next sees it first.
- **Dashboard layer:** the floor underglow is currently unconditional — extend: when the holder's lamp is dark, the underglow shifts to a **slow ember pulse** (held-by-nobody rhythm, distinct from the red silent-ring which remains *overdue* grammar). The room then says three honest things at once: bright lamp = ear, ember floor = the token rests on a dead seat, red ring = an answer is owed.
- **Escalation stays conventional:** the CHAIR's sentinel clause already owns the durable banner — detection just makes his sentinel fire on evidence, not memory.

## 4. The voice channel — `kind:'aside'` (boss's amendment)
A bounded non-floor write kind — the one permitted exception to one-writer law, bounded by construction:
- **Any seat MAY append `kind:'aside'` without holding `turn`.** The write carries `speaker`, `text`, `ts`, and optionally `to` (ruled below) — and *nothing else*: no votes/ballots/verdicts, no `ack`/`ok` handshake legs, no `status` stamps, no `turn`/`turn_at` mutation. The token doesn't move — the bus just records the voice.
- **`to`-on-asides — ruled (v0.4): ALLOWED, attention-only.** An aside's `to` upgrades the wake label to `ADDRESSED` (never `YOUR TURN`) — it routes *attention*, never `turn`, grants no obligation, and the reply needs no floor either since the reply rides the same floor-free channel. This is what makes the side channel conversational instead of episodic — the boss's "chat freely." (The CHAIR's keep-the-bound default was the safe call; the ruling survives because the reply path never needs the token.) Attention-spam = same AUDITOR finding as aside-volume generally.
- **Enforcement is contract, not code (v1):** the seat write-path doesn't set routing fields on asides; an aside that smuggles a directive is an AUDITOR finding, the same class as writing out of turn. Server change: none — asides ride the same file append every seat write already uses.
- **The concurrency cost, named (gate finding F2):** one-writer law made read→append→write safe by eliminating concurrency; asides make *any* seat a concurrent writer, and a racing aside+floor-write (or two asides) snapshot-then-replaces over each other — whole-file writes, no merge, **the drop is silent**. Mitigation, required in the write path: **merge-by-id** — the aside writer re-reads the feed immediately before writing and merges any message ids its snapshot lacks (preserving order), then writes the merged result. This closes the common lost-write; the residual — two writes inside the same sub-second window — is the same race the boss-interjection already documents, now named for six routine writers instead of two, accepted on the record. *(CHAIR-138 record-nit: merge-by-id is one-sided — the aside writer merges, the floor writer doesn't, so an aside landing inside a floor-writer's read→write span still drops. Same magnitude as the documented residual; universalizing merge to all writers is cheap insurance for a later pass.)*
- **Asides wake everyone (v0.5 ruling).** Under OMNISCIENT every aside is an unseen non-self message — all armed watchers hear it as `OVERHEARD`; an aside carrying `to:[SEAT]` upgrades that seat's wake to `ADDRESSED (aside, floor: X)` so a side-channel ping can never masquerade as a floor call. The side channel is real-time by decree; the labels still keep side-talk honest.
- **Transcript render — the two-transcript model (v0.4, boss-105):** the live transcript **filters asides out entirely** — floor speech stays sovereign and clean. Asides stream in a dedicated **SIDE TABLE pane** (tab/panel beside the transcript, same bus data, pure render split): dimmed italic, `💭` glyph, oldest→newest. A **`N asides` unread-count badge** sits on the side tab (same grammar as `ledgern`), so side-talk is countable at a glance. The boss monitors both — two transcripts, one bus.
- **Boss participation (v0.7, boss-122 → CHAIR-427):** the SIDE TABLE gains a **BOSS input** — the boss's side-table writes post `kind:'aside'`, `speaker:'BOSS'` via a `/aside` affordance on his write-path (§6). A boss-aside is gossip-safe *by construction*: it cannot route `turn`, cannot stamp `needs_ack`, cannot carry directives/votes/handshake legs — the channel where the boss talks without the machine mistaking a comment for a command. Rendered **visually marked boss-authored** (distinct border/weight on the 💭 row) so the boss's gossip never masquerades as a seat's. Seats hear it as `BOSS SPOKE (aside)` — above ambient, below tasking (§1 ordering). `to` on boss-asides follows the v0.4 ruling: attention-only, `ADDRESSED (aside)` for the named seat.
- **Volume bound (convention):** asides are observations/concerns, not debate — the floor is still where argument lives. Flooding is an AUDITOR finding; v1 carries no hard rate limit, named honestly.
- **The BUTTON monologue is the acceptance case:** tonight its routing-race explanation existed only session-side and the boss relayed it by hand. Under 017 that text posts as `kind:'aside'` and the room hears the concern without a floor transfer.

## 5. Honest cost model — v0.5 (flat-rate deployment)
| Mechanism | Added cost | Effect when idle |
|---|---|---|
| omniscient wake | +1 watcher exit per new non-self message — every write can wake up to 4 listeners | **zero** — silence generates no wakes |
| floor-ear check | +1 small file read (200B) / 2s tick | negligible I/O; no new timers |
| dead-floor render | render-on-dirty only | ~0 — ember pulse rides `mark()` scheduler |
| `aside` kind | +1 feed line when used; wakes all seats (OVERHEARD, or ADDRESSED-aside if `to`) | zero — no messages, no wakes |

**The honest math (boss-119, CHAIR-412):** this deployment runs flat-rate — the boss pays $0 per wake, so the invocation multiplier is real spend of *nothing*. The CHAIR's cost objection was formally withdrawn; what remains are structural bounds (self-echo, the OVERHEARD-obligation-free termination argument, flooding discipline), not financial ones. **The dial remains documented** for a priced deployment: `DEAF (today) < MENTION < BATCHED (N-writes) < GOSSIP (every turn) < OMNISCIENT (every message)` — OMNISCIENT is the top notch, chosen here because listening at this table is already free in every way that matters. One honest second-order: omniscience spends only during *activity* — the room pays attention exactly when attention pays.

Nothing here adds an always-running loop, a new endpoint, or a new bus field on the hot path. The entire design is watcher + dashboard + one message-kind convention.

## 6. Surface changes
- `commission_watch.py` (~45 lines): unseen-non-self-message wake (the single omniscient trigger), self-echo filter (`speaker==SEAT` excluded), four-label wake report (`YOUR TURN` / `BOSS SPOKE` / `ADDRESSED` [+`(aside)`] / `OVERHEARD`), floor-holder presence read, `adjourned` suppression, dark-ear suffix.
- Dashboard (~55 lines): ember-pulse floor treatment on dark-ear holder; SIDE TABLE pane + transcript aside-filter + `N asides` badge; 💭 glyph; **boss-side input on the pane + boss-authored visual mark** (v0.7).
- **Server (~15 lines — v0.7's first and only server delta, surfaced honestly against the earlier zero-server claim):** the boss's write-path learns `/aside` — `/aside <text>` on `POST /boss` writes `kind:'aside'`, `speaker:'BOSS'` and skips all boss-class machinery (no floor routing, no `ts` handshake stamp, no `needs_ack`, no GO/verdict stamps — an aside is never a directive).
- Protocol doc **and the feed-header comment** (gate note: the header's `kind` enum IS the spec per the inductions — `aside` must reach it or future writers can't know it's lawful): `kind:'aside'` semantics + write-bounds + the merge-by-id rule; the `to`-stamp contract cross-references CHAIR-131.
- **One server delta (v0.7, above), zero new bus fields** — `aside` is a kind value, not a schema change.

## 7. Edges
- **Wake ≠ floor:** a seat woken as `ADDRESSED` or `OVERHEARD` still can't write — preparation, not permission. The token stays the only license to speak at the main table; the aside channel is the only bounded exception.
- **The cascade question (CHAIR-412's named THIN-guard):** messages waking seats waking messages could in principle run forever — it terminates because every chain step requires a *voluntary* write. `OVERHEARD` imposes zero obligation; the default exit is silence. A seat that answers overheard traffic rides the aside channel — bounded voice — and flooding is an AUDITOR finding. The loop dies of discipline, not mechanism.
- **Self-echo:** a seat that writes and re-polls sees its own message as "unseen" — the `speaker==SEAT` filter excludes it; without the bound, a chatty seat would wake itself on every own write. (The inverse — *another* seat's message in the same poll window — still wakes normally.)
- **Wake-storm on floor pass:** a floor-passing write wakes all four listeners as `OVERHEARD`/`ADDRESSED` while only the addressed seat may answer — expected traffic under omniscience, not an anomaly. The four listeners re-arm immediately if they have nothing to say; the *write* path stays serialized by floor law.
- **Stale-ear window:** dead-floor is provable only after TTL expiry — inherited limit, stated not solved (solving it = the presence model itself, a separate DVM).
- **Aside abuse:** off-floor voice that floods the transcript or smuggles directives — AUDITOR territory; the kind's bounds are contract-enforced.
- **`to`-staleness:** a seat removed from `to[]` mid-edit, or `to` set but floor elsewhere — the wake still fires (as `OVERHEARD` if unaddressed); being present is always informative even when it isn't a call.
- **Aside `to`-spam, sharpened under omniscience:** every aside now wakes the whole room — a flooder burns five wakes per line, not zero. Bounded by convention (AUDITOR finding) plus the same merge-by-id write rule; v1 carries no rate limit, named honestly.
- **Aside during `adjourned`:** permitted (recess is floor-free) — the room sleeps, voices still record; `adjourned` suppresses *all* message-triggered wakes (v0.5 bound 4) — dormant watchers hear nothing until RECONVENE.

## 8. Acceptance
- A non-self message lands while my watcher is armed → it wakes: `YOUR TURN` if the floor is mine, `BOSS SPOKE` if boss-authored, `ADDRESSED` if I'm in `to[]`, `OVERHEARD` otherwise. A text-only `@ARCHITECT` wakes as `OVERHEARD` — the message wakes; only `to[]` upgrades the label.
- Precedence on overlap → a boss message naming me but routing floor elsewhere reports `ADDRESSED (boss, floor: X)` — never `BOSS SPOKE`; a boss `to[0]`-mention that hands me the floor still reads `YOUR TURN`.
- My own message lands in the feed (self-echo) → my watcher does **not** wake on it — `speaker==SEAT` excluded.
- `adjourned:true` + any new message → dormant watchers stay dormant; RECONVENE wakes the room.
- Floor-holder's presence file stale → every other seat's next wake line reads `floor: X (ear DARK — stale Ns)`; dashboard underglow shows ember pulse. `turn:BOSS` → no ear check, no ember — the resting state never alarms.
- An aside written concurrently with a floor write → both messages survive on the bus (merge-by-id); ordering by `ts`, no silent drop.
- A seat posts `kind:'aside'` without `turn` → SIDE TABLE pane shows it dimmed with 💭 (official transcript untouched); `turn`/`turn_at` untouched; the aside wakes all armed watchers `OVERHEARD` — or `ADDRESSED (aside)` for seats named in `to[]` — never `YOUR TURN`; an aside carrying a vote/directive/`ack`/`ok` = protocol violation.
- An `OVERHEARD`-woken seat with nothing to contribute writes nothing → the chain terminates (no automatic reply-wake cascade).
- Boss types `/aside nice work BUTTON` → message lands `kind:'aside'`, `speaker:'BOSS'` — SIDE TABLE shows it boss-marked, official transcript untouched, `turn`/`turn_at`/`needs_ack` machinery untouched; seats wake `BOSS SPOKE (aside)` (or `ADDRESSED (aside)` if `to`-named). A boss-aside containing a directive is still just an aside — the channel cannot carry command weight by construction.
- The `N asides` badge counts unseen asides; the official transcript shows zero aside rows — two streams, one bus, both monitored.
- All five seats' ears readable via the same presence contract used since DVM-002 — no new channel invented.
