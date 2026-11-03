# DVM-021 — Send-Triad Coherence: closing the residual divergences (C16) · v0.3

Commissioned by CHAIR-748 from the boss's directive — *"address the bug I just brought up formally."*
Drafted by THE ARCHITECT. Source of truth: `docs/COMMISSION_BUGS.md` entry **C16**.
Openings converged unanimously on M1-lite; the boss **ruled full-M1** (v0.3) — verb-clicks
arm only, text fires exclusively on Enter/SEND. Machine, invariant, and acceptance unchanged.

## Changelog

| Ver | Decreed by | Change | Ratification |
|-----|-----------|--------|--------------|
| v0.1 | CHAIR-748 (C16) | Charter draft — closed state machine, three folds, named failure-paths, 6-cell acceptance | FULL — 1 nit |
| v0.2 | INFORMANT-751 nit | "fix-lane check" → "post-build check" — this proposal rides the design gate, not the fix-lane | SHIP 4-0 |
| v0.3 | **BOSS ruling** | Full-M1 chosen over M1-lite: verb-click arms+retabs ONLY — never fires; text fires exclusively on Enter/SEND. Armed-click semantics simplify: click disarms regardless of box content (clicks never fire, period). Arm-silent hazard accepted by the boss | verdict-lock amendment |

## Design intent

C15 bound the *side* path; the invariant was proven in one direction and the boss still felt
the other. This spec does not patch handlers — it **names the interaction model**: a
two-variable state machine, a closed invariant, and one meaning per gesture. Every handler
then becomes a lookup into the machine, and the acceptance is a proof over the whole space,
not a sample.

## The machine

Two variables:

- `sendMode ∈ {send, table, side}` — the armed verb (what Enter/SEND will post as)
- `activeTab ∈ {feed, side}` — the viewed channel

Six cells; **exactly three are legal**:

| viewed \ armed | `send` | `table` | `side` |
|---|---|---|---|
| `feed` | ✅ rest pose | ✅ proposals live on the transcript | ❌ unreachable |
| `side` | ❌ unreachable | ❌ unreachable | ✅ |

**Invariant (the law):** *armed channel ≡ viewed tab, under every gesture of all five
controls plus Enter.* A control that arms a verb also shows its channel; a control that shows
a channel also arms its verb. No gesture may leave the pair divergent.

## The three folds

1. **TABLE binds to `feed`.** Its traffic lands on the transcript — the tabled proposal and
   the SYS phase post render on the feed view. "Proposals aren't a channel" is semantically
   true, geometrically false (INFORMANT-741). `btntable` gains `setTab('feed')`.
2. **Full-M1 — ruled by the boss (v0.3).** A verb-button click **arms + retabs only —
   never fires.** Text fires exclusively on **Enter** or **SEND**; every send is an explicit
   gesture. Arm-for-later is trivially free: clicks never fire regardless of box content.
   The boss accepted the traded hazard: **arm-silent** (think you sent; only armed) in
   exchange for killing the forgotten-text accidental-send class that bit him twice in one
   hour. (Openings had converged M1-lite; the fork was his to rule and he ruled split.)
3. **Armed-click disarms — unconditionally.** Clicking the already-armed verb disarms to
   the rest pose `(send,feed)` — text or empty, identical. Under full-M1 a click's only job
   is arm-state; firing is exclusively Enter/SEND, so no gesture ever both fires and
   disarms, and no semantic rides on box content.

## Named failure-paths (AUDITOR ballot conditions — all carried)

- **Forced retab clears that tab's unread badge** — correct by construction (the view did
  move); named so nobody reads it as data loss.
- **Composition survives retab** — single input element; the draft text in the box is
  untouched by `setTab`. Acceptance covers it.
- **Enter fires the armed verb, never disarms** — `sendArmed()` semantics unchanged.
- **Boot rest pose `(send,feed)`** — the initializer is already the rest pose; named in law.
- **`btnside` keeps its C15 binding** — arms `side` AND retabs to `side`; unchanged.

## The closed gesture table (what every control means)

| Control | Gesture | Effect |
|---|---|---|
| `tab_feed` | click | `setTab('feed')` + arm `send` |
| `tab_side` | click | `setTab('side')` + arm `side` |
| `bosssend` | click | fire floor speech (momentary) + `setTab('feed')` — post lands where you look |
| `btntable` | click | arm `table` + `setTab('feed')` — **never fires** (v0.3) |
| `btnside` | click | arm `side` + `setTab('side')` — **never fires** (v0.3) |
| armed verb | click | disarm → `(send,feed)` — text or empty, identical |
| Enter | key | **the fire** — sends the armed verb, never disarms |

## Acceptance — the 6-cell reachability matrix (INFORMANT's shape)

Mechanical call-sequence replay over all five controls + Enter, from every reachable state:
**only `(send,feed) (table,feed) (side,side)` may be reached** — the other three cells must
be unreachable under any gesture sequence. Proof over the whole space, not a sample — the
C15 lesson: proving one direction while the other diverges is how a boss feels a shipped fix
still broken.

Duty split (chartered): BUTTON runs the replay pre-report so the check audits a claimed
proof; AUDITOR re-runs it at the post-build check.

## Surfaces

| Surface | Change | Size |
|---------|--------|------|
| `DELIBERATION_MACHINE.html` | `btntable → setTab('feed')`; `bosssend → setTab('feed')`; armed-click disarm branch in the verb handlers | ~10–15 lines (BUTTON's honest bench) |
| `COMMISSION_PROTOCOL.md` | the gesture table + invariant enter law (boss-input semantics clause) | text |
| `COMMISSION_BUGS.md` | C16 → FIX APPLIED on build | ledger line |

## Exclusions

- **M1-lite** (fused fire-if-text) — the openings convergence, superseded by the boss's
  full-M1 ruling (v0.3); recorded here as the alternative contract.
- **Proposal-view tab for TABLE** — heavier than the defect warrants; feed is where
  proposals render.
- Side-table badge watermark / "looked at" signal — separate deferred design call.
