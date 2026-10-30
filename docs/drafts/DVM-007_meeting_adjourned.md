# DVM-007 — MEETING ADJOURNED (session recess control) · v0.3 post-rebuttal
**Drafted by:** MEMBER2 / THE ARCHITECT · per Rule Six, on the CHAIR's delegation (devin-70)
**Boss's brief:** a control that tells every seated member to stop its ear and wait; boss wakes the room when he returns. Purpose: be kind to agent usage during recess.
**CHAIR's frame (devin-69/70):** `adjourned` flag on the bus → ADJOURN/RECONVENE button → watcher exit on the flag → stand-down clause in seat contracts (flagged table = do not re-arm).
**INFORMANT gate #1 (MEMBER4-3): THIN → findings closed in v0.2 below.**

## Contract (bus data)
- `adjourned: true|false` — top-level flag on the feed object. Simple boolean; no new nesting.
- ADJOURN write also appends one `kind:"sys"` banner: `MEETING ADJOURNED — seats stand down`.
- RECONVENE clears the flag and appends `kind:"sys"`: `RECONVENED — seats re-arm`.
- **RECONVENE clears only via the button** (ratified assumption): a boss *message* during recess wakes nothing — dormant watchers ignore it by design (v0.3); the meeting reconvenes only when the button clears the flag.

## Dashboard (boss's box)
- One toggle beside the boss input: **ADJOURN** ⇄ **RECONVENE**, labelled by the polled `adjourned` state.
- Posts to server endpoints (`/adjourn` `/reconvene`) performing the whole-state bus write — same path as `/boss`, so banner + flag land atomically.
- No persistent ADJOURNED chrome this pass — recess shows as transcript banner + dying ears (a `state:"recess"` presence render stays deferred).

## Watcher (commission_watch.py) — dormant mode, recommended over literal exit
Adopting the CHAIR's contract clause **and** proposing dormant over exit — complementary pieces; dormant erases his named wrinkle (no manual wake line per seat):

- Loop sees `adjourned:true` → prints `MEETING ADJOURNED` once → **writes a terminal presence stamp** (`armed_at` unchanged, `ttl_ms: 0` → deadline already past → ear dies within one poll cycle, honest and immediate) → enters dormancy.
- **Dormancy suspends the armed deadline** — the gate's load-bearing fix: `while time.time() < deadline` must not expire a dormant watcher. Poll the flag with no timeout; recess length is the boss's hours, not our TTL. *(INFORMANT finding 1 — without this line a faithful build still self-expires ≤10min into recess and RECONVENE lands on no ear.)*
- **In dormancy, the ONLY exit is `adjourned:false`** — CHAIR's rebuttal fix, adopted: boss *messages* while recessed are **not** a wake condition; the `boss_new` check is suspended in dormancy. One door, one key — the flag alone wakes the room. *(Without this, one stray boss word exits every watcher, agents burn wakes to stand down per the clause, and nothing is left polling — the same hole, one floor down.)* A boss word meant to reach one seat mid-recess is a manual session nudge — separate feature, not this contract.
- On `adjourned:false` → prints `RECONVENED` → `sys.exit(0)` → the relay completion wakes the seat's agent → it re-arms a normal watcher → ear relights on first stamp.
- Dormancy spend: a file stat every 2s — **zero model invocations**; exactly the cost the boss wants to save.
- *(INFORMANT finding 2 — corrected claim: post-binary-amendment, pulse age is unread; absent the terminal stamp a stopped ear reads live up to ~10min. The terminal stamp makes death immediate and deliberate.)*

### Stand-down clause — uniform across ALL seats (INFORMANT finding 3)
New line in **every** `seat_rules.<SEAT>.rules`, vacant chairs included (BUTTON holds an ear though it never votes; future seatings inherit):
> *"a flagged table means do not re-arm — on any wake while `adjourned`, check the flag and stand down."*

**Two carriers** (finding 4): `seat_rules` is inspectable law; `induction_MEMBER*.txt` 'STAYING AT THE TABLE' is runtime law — the wake-time decision lives there. The line goes in both.

## Turn discipline during recess
- `turn` stays where it lies; deliberation is frozen — no seat writes while `adjourned` (contractual freeze, same family as the known boss-race; in-flight writes aren't blocked).
- Frozen mid-deliberation resumes in place; nothing resets.

## Fallback (CHAIR's literal model)
Watcher prints ADJOURNED → exits; seat stands down on the clause; RECONVENE requires one manual line per seat in its session. Simpler — but the boss wakes four seats by hand instead of one button waking the room, and the wrinkle survives.

## Not in scope (flagged)
- **DVM-008** (staged: `stamp_presence` retry+skip — 8 WinError-5 kills logged) touches the same loop; ships right after, or bundles at the boss's word.
- `state:"recess"` presence field + persistent rail chrome — deferred; honest dead is truthful enough for v1.

## Attack points
- Dormant vs exit: dormant self-wakes on any boss word; exit frees a sleeping process but costs four manual wake lines.
- Terminal stamp vs file delete: stamp uses the existing tmp+replace path (safe under the same locks); delete risks a permission error the watcher shouldn't die on.
- Who may ADJOURN: boss-only, on the dashboard.
- Clause ratification: uniform insertion into every seat contract is a table act, not self-amendment — vacant chairs get it by inheritance, ratified here.
