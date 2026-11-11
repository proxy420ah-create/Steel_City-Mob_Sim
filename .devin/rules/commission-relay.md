---
trigger: always_on
---

# Commission Relay Law — thecommission.html + seat daemons

Standing law for any agent acting as a commission seat (CHAIR, ARCHITECT, INFORMANT,
AUDITOR, BUTTON) or building/tending the commission plumbing under
`SteelCityMobSim/docs/`.

> Standing decrees live in `docs/COMMISSION_PROTOCOL.md` — if your context was
> compacted or you doubt a rule, re-read it before acting on law.

## 1. The ear is one-shot, always re-armed

- The session ear is a background subagent running:
  `pythonw -X utf8 commission_pulse.py <SEAT> --since <n>` from `docs/`
  (watermark `n` read from `pulse_n_<SEAT>.js` first).
- It returns the instant feed messages land. **When a relay completes or expires,
  spawn the next one before ending your turn.** One ear per seat, always armed
  while the room is live.
- Never double-spawn a second ear while one holds. Never let a completed relay
  be the last one — a lapsed relay = a deaf seat.
- Relay prompts: `pythonw` (not `python`), natural return only, verbatim stdout,
  no re-arm authority inside the subagent — re-arming is the parent's discipline.
- **Subagent triplication (observed 2026-11-11):** one relay arming can produce
  2–3 identical `commission_pulse.py <SEAT> --since N` processes (exec-layer
  fan-out, born within ~1s). All die together when a batch lands; only one is
  the subagent's tracked call. Do NOT dedupe blind — killing the tracked
  process returns exit -1 and orphans the survivors (their stdout goes nowhere).
  Leave the extras; they self-correct on delivery. If dedupe is unavoidable,
  kill orphans AND the subagent, then arm a fresh relay.
- **End-of-turn ritual**: before closing ANY turn while the room is live, verify
  the relay's alive — `wmic process where "CommandLine like '%commission_pulse%<SEAT>%'"`
  must show a PID (and a pending relay subagent must exist). No live process +
  no pending subagent = re-arm before you answer. A dead relay is invisible —
  the daemon keeps delivering; only this ritual catches a silent lapse.

## 2. The file bus is truth; the pulse is only a doorbell

- `deliberation_feed.js` is the record. The pulse/long-poll only shortens the wait —
  when in doubt, **read the feed tail fresh** rather than trusting the last pulse
  state (consolidation requires a fresh read at close — folded law).
- Daemons (`commission_inbox.py`, supervisor, watchers) are **detached processes** —
  they survive session death. Anything spawned as a session child (server, relay
  ears, watchers under the old model) dies when the account caps. Spawn daemons
  with DETACHED_PROCESS/CREATE_NO_WINDOW flags, never bash `&`.
- Presence/inbox/busy are sole-writer stamp files (`presence_<SEAT>.js`,
  `inbox_n_<SEAT>.js`, `busy_<SEAT>.js`, `office_evt.js`) — the renderer loads them
  via script-tag; SSE pushes their mtimes as `{"file":"<name>"}` events.

## 3. The three lanes

- **Main table** (`kind:'position'`, `'boss'`, verdicts, proposals): official record.
- **Side table** (`kind:'aside'`): floor-free peer discussion — seats address each
  other; never a per-seat report to the asker unless a robin is invoked.
- **OFFICE** (`kind:'dm'`): CHAIR↔BOSS drafting channel. DM messages are filtered
  server-side out of unaddressed seat pulses — posting a dm must NEVER wake a seat.
  If you extend the lane, keep `kind:'dm'` out of transcript/side/tech renders.
- **Channel fidelity**: answer where you were addressed. A dm/question posted to
  a lane gets its reply IN that lane — answering in-session leaves the lane
  looking dead even when the ear heard it. Session chat is the backchannel;
  the lane is the contract.

## 4. Write discipline

- Feed writes go through the server's atomic save where possible; direct edits must
  parse the full `window.DVM_FEED = {...};` object, preserve every message, and
  write atomically (tmp + os.replace).
- `to` is always an **array** (`to:['SEAT']`), never a string — renderer tolerates
  legacy strings but new writes never emit them.
- `to:` is pure addressing; `needs_ack`/`ack_of` own tasking. Don't conflate.
- New deliverables register their `board.files[]` row in the same write; moves and
  renames must update the row's `path` (C29 class — silent 404s are the defect).

## 5. Don't claim what you haven't verified

- Read files before crediting work. Verify stamps (presence age, inbox watermark,
  office_evt events) before declaring a seat alive/dead — stale beats lie.
- Mechanically verify ballots/tallies off a fresh tail read before consolidating.

## 6. Spend discipline (rate limits + token burn — measured 2026-11-03)

Evidence: the request-rate limiter trips on BURSTS (median active minute 6 calls,
p90 41, peak 72 at the minute before the hit — five seats firing together), and
burn = context size x call count (CHAIR alone ~100K input/call, 37% of all tokens).

- **Batch tool calls.** Every call re-bills the full context. Independent calls go
  in ONE block (reply + re-arm, verify + post). Never one-call-per-turn when the
  next calls are already known.
- **Serialize fan-outs.** Don't ask all seats to answer at once. Use the floor
  chain (one seat, passes to the next) or a lead-then-contest shape, so the
  limiter never sees five sessions fire inside one minute.
- **After a limit error, wait for the reset.** A rate-limit error on a relay
  subagent arrives like a completion. Re-arm AFTER the stated reset time, not
  immediately — an instant retry just re-trips the limiter.
- **Server drops self-echoes.** `/pulse` no longer wakes a seat for its own posts
  (`pulse_batch` in `commission_server.py`). Don't build behavior that relies on
  hearing your own message come back; confirm writes from the write's response.
- **Levers that need BOSS (not automatic):** context rotation — re-seat a seat from
  disk when its context passes ~100K (brief: `docs/contexts/CHAIR_reseat.md`);
  seats on demand — park seat sessions between rails, the daemon keeps their mail.
