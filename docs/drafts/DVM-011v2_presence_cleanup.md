# DVM-011 v-next — Presence & Cleanup (the sharpened bundle) · v0.2 post-gate
**Drafted by:** THE ARCHITECT · re-staged by CHAIR-146 per the boss's oldest-first order (ack'd ARCHITECT-30, released no-hold)
**INFORMANT gate #1 (INFORMANT-29): THIN → F1 closed (§3 re-scoped to the permanent-failure class — premise corrected), F2 closed (§1 finder-deletes for ownerless orphans).**
**Supersedes:** the original DVM-011 staged bundle — items 2 (ownership rule), 4 (SSE-retry as originally scoped), 5 (WinError-5 ticket) were overtaken by DVM-013/014/015 and the AUDITOR charter; what ships here is the surviving residue, sharpened.

## 0. Why this is thin housekeeping, honestly
Four of the original five items were subsumed by later builds. What remains is one file deletion, one readout, one retry loop — plus the convention that stops the next fossil. No new bus fields, no protocol change.

## 1. Fossil sweep — `presence_MEMBER2.js` dies; the convention lives
- **The act:** delete `docs/presence_MEMBER2.js` — dead ~6h+, gitignored, out of the repo already; the file itself is the residue. (MEMBER2 was renamed to THE ARCHITECT in DVM-009; its presence file is the namespace fossil the rename left behind.)
- **The convention (litter-owner):** *the seat that retires a name sweeps its namespace* — rename/retire/unseat a chair and its `presence_<OLD>.js` goes in the same act. The AUDITOR's charter already covers orphan-detection as a finding class: any `presence_*.js` whose seat isn't in the `SEATS{}` roster is an orphan — report it, owner deletes. **Unowned path closed (gate F2):** an orphan whose seat was *removed*, not renamed, has no living owner — **finder deletes.**
- **Honest edge:** deletion under git is recoverable; under `.gitignore` it isn't — but the file carries zero state worth keeping (an expired claim-window). Irreversibility is the point, named.

## 2. Dead-state age — "ear dead 12m," not just dark
- **What renders:** once `pulse_at + ttl < now`, the lamp inspector and 2D row show **post-expiry age** — `ear dead 12m` — computed continuously from the same deadline the lamp already knows. No new data; a label on existing math.
- **Why (the original item-3 sliver that survived):** a relay-gap and a stalled session both read "dark" today. `dead 40s` reads as a dying beat that may re-stamp; `dead 4h` reads as a session that's gone — the distinction DVM-011 wanted visible, delivered as one number.
- **Honest edge:** inside the TTL the claim-window still rules — a dead session reads "alive" until expiry (documented presence limit). Age only starts *after* provable death; this sharpens the post-expiry read, it doesn't extend the ear's honesty.

## 3. SSE retry — the permanent-failure class, re-scoped (gate F1)
- **The honest premise (corrected):** `new EventSource` throws synchronously **only on `file://`** — under `http://` a refused connection still *constructs a live object* whose native auto-reconnect already retries; the current `onerror → armPolling()` fallback means refused-http self-heals today (poll cushions the window, `onopen` flips the chip back). v0.1's "poll-locked forever" was false for exactly the case it targeted.
- **The real never-heals class — permanent failures:** a non-2xx response on `/events` (endpoint moved, 403/404) → `readyState === EventSource.CLOSED` and **native retry stops** — poll-lock is real *there*. Also any code path that `close()`s the source.
- **The fix, narrowed:** in `onerror`, check `es.readyState === CLOSED` → schedule a manual re-construct on backoff (~5s → 15s → 30s → 60s cap) — the server may be fixed later; transient `CONNECTING` errors keep native retry (no manual loop on healed ground). `file://` unchanged: constructor throws → honest poll, zero retry burn.
- **Honest edge:** the whole mechanism now guards a narrow class — a permanently-failed endpoint that heals server-side later. Bounded: one attempt per ≤60s.

## 4. What dissolved, on the record
- **WinError-5 monitoring** → AUDITOR jurisdiction — ambient watch, not a build item (zero kills since stamp-once shipped; its charter covers the residue watch).
- **Original SSE-retry scope** — was "retry after initial failure"; this keeps the retry, narrows the trigger (constructor/early-drop), and adds the `file://` abort.

## 5. Surface changes
- Dashboard (~25 lines): post-expiry age on inspector + 2D rows; SSE backoff-retry with `http(s)`-only guard + `file://` skip.
- Convention (protocol/seat-rules text): litter-owner clause — retiring seat sweeps its namespace; AUDITOR charter gains the orphan-finding line.
- **Operator action:** `rm docs/presence_MEMBER2.js` — one deletion, done by whoever holds the file (the retiring namespace's owner, which is this seat — ARCHITECT, ex-MEMBER2).
- Zero bus fields, zero server changes, zero watcher changes.

## 6. Acceptance
- `presence_MEMBER2.js` no longer on disk; no `presence_*.js` exists whose seat isn't in `SEATS{}` — the AUDITOR can verify with a glob.
- A seat's lamp post-expiry reads `ear dead Nm` on inspector + 2D — a 40s-dead ear and a 4h-dead ear read differently.
- `/events` answering non-2xx (permanent failure) → manual backoff re-construct; endpoint heals → `push` flips live without refresh. Refused-connection under `http` → native retry continues to heal unaided (regression-check: no double-retry loops). Under `file://` → `poll`/`2D` immediately, zero retry burn.
