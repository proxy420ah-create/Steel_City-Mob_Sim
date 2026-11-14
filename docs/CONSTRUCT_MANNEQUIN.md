# Construct — Mannequin Station

**Status**: shipped (suit-color swaps); accessories are the designed-for next phase.
**Where**: northwest corner of the Construct floor.

The mannequin station is a physical "dressing corner": a static duplicate of the
Civilian1/Vinny rig standing beside a floor pedestal whose tilted face carries a
canvas screen and two large pressable arrow buttons. It exists to preview and
author outfit/accessory loadouts on a real body, in the room, without menus.

## The mannequin

- Emitted via `emitChar({ char:'Civilian1', static:true, x, z, yaw, outfit })`.
- `static:true` is the isolation contract: the record **never** enters `dvm_fol`,
  never runs `followStep`, never claims the PiP/agent-eyes camera, and cannot be
  ordered. It is set-dressing, not a unit.
- Because statics skip `followStep` (where follower groups are normally
  positioned), `emitChar` sets `CH.position`/`CH.rotation` at emit time for
  every character — harmless for followers, which re-set both every step.
- Position: `MANN_POS = { x:-2.5, z:.48 }`, `MANN_YAW = 2.45` (faces the room).
- Collision: a solid circle `[MANN_POS.x, MANN_POS.z, .22]` in `SOLID_BASE`.

## Outfits = region→material remaps

An outfit is not a mesh swap — it is a remap of the rig's authored **regions**
onto palette **materials**, applied per-voxel through the character's instanced
colors (`recolorChar(f, map)` walks `f.rig.regions`).

Current presets (`OUTFITS`):

| Name        | Torso/Arms | Legs | Feet |
|-------------|-----------|------|------|
| SUIT BLUE   | 126 Black Fabric | 126 | 105 Cobblestone |
| SUIT GREY   | 102 Concrete     | 110 | 118 Tar         |
| SUIT BROWN  | 122 Painted Brown| 122 | 118 Tar         |
| SUIT WHITE  | 127 White Fabric | 127 | 118 Tar         |
| WORK RED    | 120 Painted Red  | 109 Dark Iron | 118 Tar |

Persistence: the selected index lives in `localStorage["dvm_outfit"]`; on boot
the mannequin re-emits already wearing it.

## The kiosk

`KIOSK_POS = { x:-1.85, z:.6 }`, `KIOSK_YAW = Math.PI - .28` — the `+z`-facing
screen must point **south into the room**; a small yaw faces the approach angle.

- Pedestal column `.44×.45×.26` on a `.52×.05×.34` foot; screen plate at
  `y=.68`, `rotation.x=-.18` — a slight lean-back that keeps the whole face
  clear of the column top and readable at standing eye height.
- The face is a canvas `CanvasTexture` on a plane, framed by a `coreMat` rim
  (`DoubleSide` — it reads as a hue edge even from behind).
- Two physical button meshes (`userData.btn`) sit on the face at plate-local
  `±.14`. `kioskProbe()` raycasts them while the player is within 1.8 m;
  the aimed button resolves the `INTERACT` prompt (`E — SUIT ◀/▶`), and a left
  click on an aimed button presses it directly.
- Pressing cycles `OUTFITS` modulo, recolors instantly, saves `dvm_outfit`,
  and flashes the pressed arrow.

## Debug/probe surface

`window.__mann` exposes `f()` (record), `outfit()` (index), `buttons`, and
`press(i)`. `_place_probe.mjs` covers: static emit, no roster write, two
buttons, aim resolution, cycling, `dvm_outfit` persistence, and reload
re-emission in the saved outfit.

## Expansion contract (accessories)

The rig carries 13 `attachmentPoints` (`left_hand`, `right_hand`, `seat`,
`pivot_0..9`) plus region `9 "Top of Head"` as a natural hat slot. The next
phase adds a second button row (or a paged screen) that parents props to named
attachment points — the station's press→apply→persist loop stays identical;
only the applied payload grows from "recolor regions" to "attach/detach prop".

## Gotchas learned

- A `static` emit still needs `CH.position` set at emit — followers only get it
  via `followStep`.
- `camLock` only applies inside the rAF loop; `__snap` must copy the camera and
  `lookAt` inline before `renderer.render` or headless shots read the stale
  camera.
- The screen plate's lean is easy to overshoot: more than ~`-.2` rad tips the
  normal skyward and the (culled) face disappears behind the rim.
