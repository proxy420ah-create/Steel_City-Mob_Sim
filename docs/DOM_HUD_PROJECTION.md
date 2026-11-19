# DOM HUD Projection — a real `<input>` floating on a 3D prop face

**Status**: retired from `19_chat.js` (replaced by the canvas-native field, BOSS decree —
the PiP layering report), preserved here as a reusable technique.
**Code retrieval**: `git show 7fa6182:docs/construct/19_chat.js` — the last commit carrying it,
lines ~56–109 (`chatEl`, `chatPin`, `chatBandRect`, `chatHide`, `chatLast`).

---

## The problem it solves

A `CanvasTexture` on a mesh can *draw* anything — a field's chrome, placeholder text,
transcript rows — but it cannot **receive** input. Canvas has no caret, no IME pipeline,
no selection model, no `keydown` target. If a 3D prop needs real typing, something DOM
has to exist. This technique puts the DOM element *on* the prop by projection: the
element never joins the scene graph; it floats in the compositor layer, re-anchored to
the prop's screen-space rect every frame.

Result: a fully native text field (caret, IME, selection, spellcheck, clipboard) that
appears glued to a moving 3D surface — reads as "HUD emitting from the item."

## The mechanism

```
pad-local band corners (±.069, -.035/-.047 on the .15×.1 screen plane)
  → corner.applyMatrix4(padScr.matrixWorld)        // pad space → world
  → corner.project(camera)                          // world → NDC
  → sx = (ndc.x+1)/2 * innerWidth                   // NDC → screen px
    sy = (1-ndc.y)/2 * innerHeight
  → CSS transform: translate3d(x,y,0), width, height, fontSize(h*.55)
```

- **Pin loop**: `setInterval(chatPin, 33)` — ~30fps tracking, only while the owning
  page/tab is active. Cheap: 4 corner projections + a few style writes.
- **Write-on-change**: `chatLast` caches {x,y,w,h}; styles only touch when a value
  actually changed — `translate3d` + width/height are compositor work, zero layout
  churn. Per-frame GC discipline from the Construct's perf audit applies: no
  allocations in the pin path.
- **Fixed position, high z-index** (`z-index:22`), styled to *match* the band the
  canvas draws underneath — same fill, same border color, same font. The canvas
  still draws the band chrome; the element is an invisible-looking window into it.
- Band geometry was derived from the canvas texture coords → plane-local coords:
  canvas band `y290..330` of 340 → uv `.03–.15` → local `y -.047..-.035`, x inset
  `3%→97%` → `±.069`. Keep this derivation in mind — it's how you map any drawn
  region to a pin rect.

## The guards (the hard-won part — don't skip these)

1. **Hide AND blur on degenerate rects.** Pad behind the camera, edge-on, off-
   viewport, or projected under ~24px → `chatHide()`: `display:none` **plus**
   `.blur()`. Chrome keeps focus on hidden inputs, and a focused input that
   `stopPropagation`s every key is a dead keyboard for the whole app — that was
   `CONSTRUCT_CHAT_FIELD_STEALS_KEYBOARD`. The z-check: any corner with NDC
   `z ∈ (-1,1)` means part of the band is in front; all-corners-behind hides.
2. **Die with the page.** The pin only runs while the owning tab is active; leaving
   the tab triggers the hide+blur path on the next tick. A field must never
   outlive the surface it's pinned to.
3. **Pointer-lock interplay.** While pointer-locked, DOM is unreachable — the
   band was a pressable region on the pad: aim+E on it → `exitPointerLock()` +
   `focus()` + `select()`. Esc → `blur()` + re-grab look. The field borrows the
   lock state; it doesn't fight it.
4. **`stopPropagation` on keydown AND keyup** on the element — typing never
   reaches the game loop. (Bubble-phase element listeners sufficed because the
   input was the event target; a window-capture version is needed if the element
   can lose target status mid-press.)
5. **Enter sends, keeps focus** — chat flow, not one-shot dialog.

## The layering wall — why it was retired here

DOM compositing sits **above** the WebGL canvas, unconditionally. Any HUD element
rendered *inside* the canvas — a scissored PiP inset, a render-target quad, an
in-engine overlay — can never draw over a pinned field, and no z-index changes
that. When the follower PiP opened bottom-right, the projected chat field drew
over the inset and there was no layering fix: the options were move the inset
or move the field into the world. BOSS chose the latter.

**The rule of thumb**: DOM-pin is the right tool when the field is the most
important thing on screen at that moment (no in-canvas HUD competes for its
region), or when native input behavior is worth more than diegesis.

## Reuse decision — DOM-pin vs canvas-native

| Need | Pick |
|---|---|
| Real editing: caret nav, selection, copy/paste, IME/CJK | **DOM-pin** |
| Field must be visible through other cameras / PiP / spectators | **canvas-native** |
| Field should look like it lives *in* the world for everyone | **canvas-native** |
| Long text, accessibility, mobile keyboards | **DOM-pin** |
| In-canvas HUD overlays can overlap the pin region | **canvas-native** |
| Short bursts, monospace, few keys | **canvas-native** (cheap to reimplement) |

## Minimal recipe for reuse

1. Draw the field chrome on the canvas face; derive band corners → plane-local
   coords (texture px → uv → local).
2. One DOM element, `position:fixed`, styled to match the drawn chrome.
3. Pin loop ~30fps while the surface is "live": corner projection → `translate3d`
   + w/h + font-size, write-on-change.
4. Guards: z-degenerate/offscreen → hide+blur; owner-page gate; pointer-lock
   handshake if the app uses it; `stopPropagation` on keys; explicit drop path.
5. Decide the layering contract up front: what in-canvas HUD can ever overlap
   this region, and which wins.

*Note for Unity reuse*: this is a browser-compositor trick. The Unity analog is a
world-space `Canvas` + `InputField`, which Unity composites in-scene natively —
no projection needed, and no DOM-vs-canvas wall either (world-space canvases
sort with the scene). The pattern to carry over isn't the projection math, it's
the **guards**: die-with-surface, reachability handshake, input capture
discipline.
