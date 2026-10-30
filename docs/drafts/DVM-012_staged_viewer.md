# DVM-012 — Board Staging Area (STAGED vs TABLED + in-board draft reader) · v0.2 post-gate
**Drafted by:** THE ARCHITECT · per Rule Six, on the CHAIR's delegation (CHAIR-95)
**INFORMANT gate #1 (INFORMANT-7): THIN → finding closed below.**
**Boss's brief:** two sections separating staged from tabled; every staged entry carries a tooltip icon expanding a sleek dark-cyber panel that reads the full staged DVM — no file explorer.
**CHAIR's kernel:** staging is only table speech today — make it real data. DVM-011 becomes the section's first resident.

## The kernel — `board.staged[]` as real data
```
board.staged: [
  { id: 'DVM-011',
    title: 'Presence & cleanup — fossil, relay-gap rail, SSE retry, WinError-5 residue',
    path: 'docs/drafts/DVM-011_*.md',
    detail: '<full markdown text>',
    staged_by: 'CHAIR', ts: 1793… }
]
```
- `detail` carries the **full markdown on the bus** — deliberate: the draft's text lives on the table where every seat and the boss can read it, and the viewer works under `file://` exactly as under `http://` (no fetch dependency — the file:// constraint that birthed script-tag loading applies here too).
- Convention (new contract line, goes in the feed header + protocol): *a staged draft registers in `board.staged` with its full text when the stager writes it.* Staging stops being table speech; it becomes a queue anyone can audit. **Header edit rule (gate note):** the line is written without embedding the literal feed-assignment string — the two-scar rule stands.
- Lifecycle: entry lives while staged; tabling **evicts it** — `proposal` populates, the staged entry drops (`closed[]` is already the history).
- **Eviction has TWO write paths — both covered** (gate finding): the CHAIR's manual table-write evicts by convention, **and `server.py`'s `/table` branch evicts too** (~3 lines: match the tabled text/id against `staged[]` ids, drop hits). A boss-typed `/table` on a staged id can never orphan residue — the cleanup feature must not birth the class it cleans.

## The two sections
- **TABLED**: the existing proposal card stays — it's the live item.
- **STAGED**: new section in the LEDGER tab, rendering `board.staged[]` — each row: `id` chip, title, `staged_by`, and the tooltip icon (`⬈`/`▸` styled, matching the seat `rules ▸` language).
- Rows accordion like the seat contracts — one staged preview open at a time (`expandedStaged`, same pattern as `expandedSeat`).

## The preview panel (dark cyber, in-board)
- Expands below the row: a bordered panel using the existing theme vars — `--surface` body, `--accent` left border, monospace body, subtle glow shadow. No new palette — the rail's own cyber language extended.
- **Mini markdown renderer** (~30 lines): `#`→headers, `**b**`→`<b>`, `*i*`→`<i>`, `` `x` ``→`<code>`, `- `→bullets, `>`→blockquote, ` ``` `→code-block. Drafts are written in this subset — they render clean.
- Payload source: `entry.detail` straight off the polled feed — zero new endpoints, zero fetch, zero file reads from the page.

## Server — one change (gate finding)
`server.py` `/table` branch: after populating `proposal`, drop any `board.staged[]` entry whose `id` matches the tabled id — so a boss-typed `/table` on a staged item never orphans residue. Watcher: unchanged.
Also: TABLED keeps its bare card, but gets a matching **section header** for the boss's two-sections sanity ask (gate note — near-zero cost, reads cleaner).

## Migration (first resident)
- BUTTON seeds `board.staged` with DVM-011's entry in the build write — id, title, path, the full detail text, `staged_by: CHAIR`. The section is born populated, proving itself on arrival.

## Attack points
- Detail-in-bus vs path+fetch: chosen for file://-parity and the single-source invariant; a few KB per staged item is a fair price for a viewer that works everywhere.
- Drop-on-table vs keep-with-status: drop recommended — `closed[]` already owns history; `staged[]` should read as a queue, not an archive.
- Mini-md vs raw `<pre>`: the boss asked for sleek — a light renderer is worth its thirty lines; raw pre is the fallback if the render misbehaves (guard it in `safe()` like every block post-DVM-004).
