# Gang Organizer UI — Setup Guide (v3: Tabbed Layout + 3D Map)

**Updated**: August 2, 2026

## What Changed (v3)
- Info panel redesigned from 6 stacked scrollable sections to a **7-tab notebook** (Hoods / Block / Orders / Finance / Police / Invest / Log)
- Event log moved from the bottom bar into the **Log tab** (tab index 6) with proper scrolling
- Bottom bar simplified to just the **Run Week button** (centered)
- InfoPanel extends lower (0.09 to 0.93 normalized Y) to use the space freed by the smaller bottom bar
- Camera viewport aligned to match (0.09 to 0.93 Y, 0.6 to 1.0 X)
- Pre-flight check on Start() validates all 28 UI references

See [UI_TABBED_LAYOUT.md](UI_TABBED_LAYOUT.md) for full architecture details and [UI_LAYOUT_GOTCHAS.md](UI_LAYOUT_GOTCHAS.md) for critical Unity uGUI pitfalls.

---

## Step 1: Run the Auto-Builder

1. In Unity's menu bar: **Steel City -> Build Game UI**
2. This creates:
   - `GameCanvas` (top bar, tabbed info panel with 7 tabs, bottom bar with Run Week button)
   - `CityMap3D` GameObject (empty — builds its 3D blocks + camera automatically at runtime)
   - `GameUIController` GameObject (all 28 references wired up automatically)
3. Press **Play**. You should see:
   - Top bar: Week / Planning / Treasury
   - Left 60%: Tabbed info panel — click tabs to switch between Hoods, Block, Orders, Finance, Police, Invest, Log
   - Right 40%: 3D isometric city blocks (auto-camera, auto-lighting)
   - Bottom: Run Week button (centered)
   - Console: Pre-flight check results (28 refs validated)

---

## Step 2: Style It (Optional, Visual Only)

Everything the auto-builder creates is a normal Unity object — tweak freely in the Editor:

- **Panel colors**: select any panel object, edit its `Image` color
- **Text colors/fonts**: select any TMP text object, edit in Inspector
- **Tab colors**: active/inactive colors are serialized fields on `GameUIController` (`tabActiveColor`, `tabInactiveColor`)
- **Map camera angle**: select `CityMap3D` -> `MapCamera` child, adjust rotation/position for a different iso angle
- **Block size/spacing**: select `CityMap3D` GameObject, tweak `Cell Spacing` / `Block Height` fields in Inspector
- **Add visual flair**: add particle effects, glow shaders, borders, drop shadows — the controller only touches text content, button interactability, and block colors, so visuals are yours to enhance

## Step 3: Optional — Hood Card Prefab

By default hoods are rendered with a simple fallback layout (name, skills, order). For a nicer look:

1. Create a GameObject with **Image + Button + Vertical Layout Group**
2. Add 3 child TMP texts named exactly: `HoodName`, `HoodSkills`, `HoodOrder`
3. Drag it into a prefab
4. Select `GameUIController` -> drag your prefab into **Hood Card Prefab** field

---

## Re-running the Builder

If you want to reset the UI layout, just run **Steel City -> Build Game UI** again — it will prompt to destroy and recreate `GameCanvas`. Any styling you did will be lost (rebuild from scratch), so only do this if you want a clean slate.

---

## Architecture Notes

- `CityMap3D.cs` — 3D scene: builds cube blocks positioned by `Block.row`/`Block.col`, isometric orthographic camera with `Rect` viewport covering the right 40% of the screen (x: 0.6-1.0, y: 0.09-0.93), raycast click detection, world-space TMP labels above each block.
- `GameUIController.cs` — runtime controller: game state, tab switching (ShowTab), all Refresh methods, event log (buffer + rebuild pattern), pre-flight check, order assignment, Run Week logic.
- `GameUIAutoBuilder.cs` — Editor-only tool: generates Canvas hierarchy (top bar, tabbed info panel, bottom bar), wires all 28 `SerializedObject` references onto `GameUIController` automatically.

## Tab Reference

| Tab | Content |
|-----|---------|
| Hoods | Your gang's hood cards (name, skills, current order) |
| Block | Selected block info (name, type, owner, strength) |
| Orders | Order buttons (Extort, Collect, Patrol, Intimidate, Lie Low) + selected block/hood |
| Finance | Weekly income, expenses, net, treasury balance |
| Police | Police officers and their corruption/bribe status |
| Invest | Active investigations (block, leads/threshold) |
| Log | Event log (auto-switches here after Run Week) |
