# Construct Probe Discipline — regression scripts under docs/ + repo edit-law

Standing law for writing/maintaining `docs/_*.mjs` probes (the CDP-over-Chrome
regression suite) and for text edits across this repo. These recur — a
room/table change that re-breaks the suite, or a patch that misfires, means
one of these laws was ignored.

## 1. Derive coordinates, never bake them

- Table positions/sizes come from `__construct.tables()` or the spec — never a
  literal `x,z` copied from a previous layout. The ONE exception is an assert
  whose purpose *is* the layout contract (e.g. "studio centered at (−6.5,−2.5)
  size 2.4") — name it accordingly so a deliberate re-layout knows to update it.
- Points *inside* a table exhibit = table center + offset. Write them as
  `T.x + dx`, `T.z + dz` so the offset law survives the next move.
- Zone-internal reads scale with the table: when a table's `size` changes, cell
  offsets scale by `size_new/size_old` and surface **heights above the felt
  scale by the same factor**. Assert relative heights
  (`roof > alley + .08`) or spec constants (felt `.26`, road top) — not a magic
  `.5563` that only exists at one scale.
- Aim rays need a clear lane: the interior wall at x≈−3 blocks annex-bound rays
  outside the door channel (z∈[−3,−2]); the south stub covers z<−2.825. Check
  `walkOK`/`WALL_BLOCKS` before choosing an aim lane.

## 2. `models.find(m => !m.static)` on ring tables

- Table 2 (`block`) mounts TWO minis: `street_ring` (`static:true` — roads,
  curbs, parked cars, emitted once) + the swappable lot core.
- `models[0]` is the ring. Any probe reading the swappable model MUST use
  `models.find(m => !m.static)`. Same for "the table's mini" group lookups.

## 3. Runs are ONE call — `docs/_run.mjs`, never babysit

- `node _run.mjs _place_probe` — spawns the probe, writes `docs/_logs/<name>.log`,
  kills its Chrome on a 15-min cap, prints `VERDICT name: N pass / M FAIL` + the
  failure names at exit. Exits 0/1. That is the whole interaction — launch it
  once, read the verdict once.
- **Never** run the probe piped to `tail`, never loop `sleep`+`grep` against the
  log, never sit in `get_output` polling. If a verdict is needed before the run
  ends, grep the *log file* — the file is the record.
- **Triage before patching.** A suite FAIL list is the spec for the patch — run
  once, collect every failure, classify each (stale literal vs stale law),
  patch once. Do NOT fix one assert then re-run the whole suite.
- **One-assert re-verification** = a focused `_xxx_check.mjs` (CDP boot + the
  single eval + the law) — see `_repark_check.mjs`. Full suite re-runs are for
  pre-commit confidence, not assert iteration.
- **One-off probes never hand-roll the CDP boilerplate.** `docs/_cdp.mjs`'s
  `bootCdp()` owns spawn, target discovery, a websocket with **deadlines on
  every await**, page-exception capture, the pass/fail tally, and Chrome +
  profile cleanup — import `{ evl, sleep, check, finish }`. The stall it
  kills: a bare `await new Promise(r => ws.onopen = r)` (no `onerror`, no
  deadline) hangs forever when the socket errors — that is what stalled
  `_dbg_held`. Un-timestamped profile dirs are the second trap — leftovers
  from killed runs make Chrome boot silently fail.
- **Diag strings are never truthy or'ed.** `return cond || JSON.stringify(d)`
  returns a truthy string on failure → the check PASSES while red. Diags go
  in `check()`'s third arg (eval a `window.__diag` you stashed), not in the
  boolean.

## 4. Multi-coordinate patches → script files, never heredocs

- `python - <<EOF` heredocs truncate mid-script (observed on this box). Write a
  `_patch_<name>.py` with the substitution table, run it, syntax-check the
  result, then delete the script. >3 coordinate edits in one file = a patch
  script, per the self-scripting rule.
- **`python -c "..."` is the same hazard.** Bash command-substitutes backticks
  inside double quotes — any pattern containing `code spans`, `$()`, or
  `$(())` gets eaten live (`command not found` spew + a mangled script). If the
  text mentions identifiers in backticks (RECENT_CHANGES entries always do),
  it goes in a patch FILE — or use the `edit` tool directly.

## 6. Text-edit hazards — patterns, quotes, chains

- **Unicode punctuation bites patterns.** Source files carry `−` (U+2212),
  `—` (em dash), `’` — a pattern authored with ASCII `-`/`'`/`"` silently
  misses. Anchor replacements on ASCII-only substrings; when a replace must
  match punctuation, read the exact line first (or match a longer unique
  ASCII span around it).
- **Assert before writing.** A patch script that `assert`s each pattern
  *before* any `open(w)` can't leave a partial patch — collect every
  replacement, verify all matched, write once.
- **Quote-type parity.** When authoring JS strings by hand, check the opener
  matches the closer — `evl('...` + `` ` `` + `)` twice produced SyntaxError
  this session. `node --check` every `.js`/`.mjs` immediately after writing,
  before running anything.
- **`;` breaks failure chains.** `cmd1 && cmd2 ; git add` runs the git even
  when cmd1 died — and if cmd1 was a `cd`, git runs from the wrong dir
  (pathspec error). Keep dependent steps in `&&` or use `git -C <repo>` /
  the `workdir` param instead of `cd`.

## 5. After geometry changes: the sweep

- `grep -nE -- "-[0-9]+\.[0-9]+" docs/_*.mjs` and reconcile EVERY literal against
  the new layout — misses hide in comments, `__cam` lanes, `surfaceAt` points,
  `walkOK` boxes, and expected-value strings.
- Re-run: `_place_probe` (full), `_noslice_verify`, `_car_measure`,
  `_ring_verify`, `_cars_verify`, `_room_verify`, `_lamps_check`,
  `_fly_check` — plus whatever focused check owns the touched law.
