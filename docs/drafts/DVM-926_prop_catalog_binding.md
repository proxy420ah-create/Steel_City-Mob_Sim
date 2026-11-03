# DVM-926 — Prop Catalog + Binding: registry, anchors, pose-prop field
**Draft v0.1.1 — ARCHITECT** | Tabled as C24 | Tier-B (schema + loader + renderer)

## Purpose
Future-proof the animator for props of all types. The `voxel_items/` library IS
the catalog; poses declare what they need; anchors bind props to bodies or the
scene. The sit-chair and the aim-pistol are two binding classes of one machine.

## Three pieces

### 1. Prop registry (`prop_catalog.json`)
Keyed by name; each entry declares its voxels + anchor behavior.
```json
{
  "chair":        { "file": "chair.json",        "class": "PLACED",  "anchor": "seat_pad" },
  "model_10":     { "file": "model_10.json",     "class": "MOUNTED", "anchor": "hand_r"  },
  "desk":         { "file": "desk.json",         "class": "PLACED",  "anchor": "desk_spot" }
}
```
- `file` — path under `voxel_items/` (relative to catalog root)
- `class` — `PLACED` | `MOUNTED`
- `anchor` — named anchor the loader resolves

### 2. Anchor schema — two binding classes
| class | binds to | merge path | rides |
|---|---|---|---|
| `MOUNTED` | a named **body anchor** on the pose (e.g. `hand_r`) | onto the posed group | the group's transform — moves with the limb |
| `PLACED` | a named **world anchor** in the scene (e.g. `seat_pad`, `desk_spot`) | onto the static path (group-0) | never posed — world-fixed |

A pose never bakes a specific prop: the anchor names the *socket*, the
registry names the *thing*. Swap the prop file and the same pose re-binds.

### 3. Pose-props declaration (per pose)
```json
{ "pose": "sit",        "props": ["chair"]              }
{ "pose": "aim_pistol", "props": ["model_10"]           }
{ "pose": "interrogate","props": ["chair","model_10"]   }
```
`props[]` is a list, not a scalar — a pose may seat AND hold. Loader
resolves each entry `prop → catalog → file + class + anchor`, grafts at
its own anchor, done. Absent or empty `props` = pose carries no props
(back-compat).

## The seam, stated plain
Poses are body-state; props are scene-state; **the anchor is the seam.**
- `vinny_seated_preview.json` stays valid — the *preview composite* (chair baked
  in for display). The animator's runtime wants the *catalog* for swappable props.

## Changes
1. `prop_catalog.json` — new file at `voxel_items/` root; entries as above.
2. Loader — resolve each `pose.props[]` entry → registry → graft at its anchor (class-dependent).
   ~30 lines: lookup + class switch + existing merge call reused.
3. Anchor map — named anchor coordinates per pose/scene; stored beside poses
   (or a flat `anchors.json`). ~15 lines + data.
4. Inductions — one clause: "props register in the catalog; poses declare
   `props[]`; anchors bind."

## Acceptance predicate (AUDITOR's drill)
1. `sit` pose with `props:["chair"]` → chair loads PLACED at `seat_pad`, static path.
2. `aim_pistol` pose with `props:["model_10"]` → pistol loads MOUNTED at `hand_r`,
   follows the arm through the pose.
3. A `wave` pose with no `props` field → loads clean, no prop, no error.
4. Swap `model_10`'s file for a new gun, keep `anchor:"hand_r"` → same pose
   binds the new model with zero pose edits.
5. Registry miss (`props:["missing_item"]`) → named error, pose still loads.
