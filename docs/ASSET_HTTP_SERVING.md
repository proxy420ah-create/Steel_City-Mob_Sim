# Asset Serving over HTTP — findings (2026-11-03 night)

> Why the studio/studio-adjacent HTML tools moved off `file://` onto the
> commission server, what that unlocked, the security model, and the
> follow-on build list. Referenced by RECENT_CHANGES night-3 batch.

## 1. What `file://` was silently costing

- Browsers treat file pages as a **null origin**: `fetch()`, ES modules,
  and texture/font loads from disk are blocked. The editor could only load
  a model via a human using the file picker — `loadDefaultModel()`'s
  HTTP path was dead code under `file://`.
- `localStorage` under `file://` is **one shared bucket for every file
  page** (Chrome keys it to the `file://` scheme) — WIP lists, camera
  bookmarks, and view prefs collide across tools and can be cleared
  between sessions.
- Every HTML tool under `file://` is an island: it sees only what a human
  hands it through a picker.

## 2. What HTTP serving unlocked (verified live)

New **read-only** routes on `commission_server.py` (port 8723, loopback):

| Route | Serves | Purpose |
|---|---|---|
| `/studio/<name>` | `VoxelAssetStudio/<name>` | the editor itself over http |
| `/Assets/<path>` | `Assets/<path>` | StreamingAssets — models auto-load via fetch |
| `/voxel/<name>` | `voxel_items/<name>.json` | (pre-existing) scene items |
| `/char/<name>` | `voxel_characters/<name>.json` | (pre-existing) characters |

All pass the same `re.fullmatch(r'[\w\-]+', name)` + realpath-containment
checks; traversal (`/studio/../docs/...`) verified 404.

Verified 2026-11-03: `GET /studio/voxel_editor.html` → 200;
`GET /Assets/StreamingAssets/voxel_items/commission_office.json` → 200
(3.15 MB auto-loads on Room preset select — no picker).

Implications that matter for upcoming work:

1. **Deep-linked assets** — `?model=<name>` is a one-line addition; tools
   stop needing pickers. The asset store becomes addressable.
2. **One asset store, many tools** — `Assets/StreamingAssets/` is the
   shared library: editor, commission scene, future animator/builder HTML
   all fetch from it. Same directory Unity reads → HTML tooling and game
   share a source of truth.
3. **Same-origin tool suite** — everything on `127.0.0.1:8723` shares an
   origin → shared fetch, `postMessage`, `localStorage`. `/studio/`,
   `/` (commission), future `/anim/`, `/builder/` = one app shell.
4. **Save-back (the big one, not built yet)** — a `POST /Assets/...` route
   with containment + Origin checks would make "Save" write straight into
   the asset dir; F5 the scene and the edit is live. Current flow remains
   blob-download → drop the file over StreamingAssets.
5. **Discovery** — a `/voxel/list`-style route turns the room library
   into data (dropdown of assets) instead of a single hardcoded default.
6. **Live reload** — the server already SSE-pushes file mtimes for the
   commission; the editor could subscribe and refresh on disk changes.

## 3. Save/export under http — confirmed working

Every save path in the editor is `URL.createObjectURL(blob)` +
`a.download` — identical on any origin (Save Project, Export Model JSON,
.stasset, groups/regions/parts). `localStorage` is *better* on http:
namespaced to `http://127.0.0.1:8723` instead of the shared `file://`
bucket. Nothing lost moving to http; the file version was the one missing
a capability (default-model fetch).

## 4. Security model

- **Loopback only** (`127.0.0.1`) — nothing off-machine can reach it.
- **Read-only is what makes it safe today.** The day a write endpoint
  lands it needs an **Origin/CSRF check**: browsers can POST cross-origin
  (form posts don't need CORS preflight), so without a check any open
  webpage could write into `Assets/`. Containment checks alone aren't
  enough — they stop *where* it writes, not *who* triggers the write.
- No version safety on save-back writes → git is the safety net.
- One more process to run: if the server is down the studio URL is dead.
  Fold "ensure server" into `run_voxel_studio.bat` so it self-starts.

## 5. The enter-building fit

For "character enters building → switch to interior scene": HTTP serving
is the mechanism — the building record names a room asset URL, the
interior scene fetches it. Pieces already line up:

- `stampRoom(x0, z0)` = the spec (4.5m plate, 4.5x1.8 walls, coffered
  ceiling at y1.71 — bare cell, caps/furnishings per layout)
- `commission_office.json` = the first room asset (merged 3-wall +
  floor + ceiling volume, `.03m` voxel, 240,648 voxels, 12 materials)
- `/Assets/` = the delivery; Room preset in the editor = the authoring lane

## 6. Live model index + URL convention

`GET /models` → JSON index of every `voxel_items/*.json` with name,
assetType, dims, voxel count, and a ready `studio` deep link. This is the
canonical "list of loadable URLs".

Studio deep links: `?model=<name|path>` + optional `&type=<preset>`
(`/studio/voxel_editor.html?type=room&model=commission_office_double`).
Bare names resolve to `voxel_items/<name>.json`; paths are used verbatim.

**Everything served is a real file on disk** — generated variants (slices,
merged rooms) are written once into `voxel_items/` and are permanent
assets; `?model=` only addresses them. "Not on file" means "not generated
yet" — generation IS the save.

Current room assets (2026-11-03):

| File | Dims | Contents |
|---|---|---|
| `commission_office` | 150x69x150 | module-1 stamp: felt floor + 3 walls + coffered ceiling |
| `commission_office_center` | 50x69x150 | center-third slice (x 50-99), corridor cutaway |
| `commission_office_double` | 150x69x300 | full doubled office: both floor plates, continuous side walls, 2 ceilings, back wall + mirrored far cap |

## 7. Follow-on build list (not yet built)

- `run_voxel_studio.bat` → ensure server + open `http://127.0.0.1:8723/studio/voxel_editor.html`
- `POST /Assets/...` save-back (containment + Origin check mandatory)
- Editor model dropdown fed by `/models` (replaces hardcoded default)
- Editor SSE subscription → live reload on disk change
- Room metadata in asset JSON (room id, door anchors, building link) for
  scene-switch plumbing
