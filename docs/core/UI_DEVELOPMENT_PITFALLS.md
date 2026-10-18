# UI Development Pitfalls — Steel City: Mob Sim

**Created**: August 7, 2026
**Status**: Active
**Scope**: Common traps when modifying or extending the Unity UI layer

---

## 1. Runtime-Created Components Are Invisible at Edit Time

**Affected**: `TickHUD`, `EventPlayer`, `StressTestSpawner` agents, `VoxelCharacter` instances

Several key components are created at runtime via `AddComponent<T>()` and are **not present in the scene hierarchy during editing**. This means:

- You cannot inspect or wire them in the Inspector
- `FindFirstObjectByType<T>()` returns null until they're spawned
- Debugging must be done via `Debug.Log` or runtime Inspector (Debug Mode)

**Example**: `TickHUD` is created by `GameUIController` during `StartTickSimulation()`. Any code that needs TickHUD must handle the case where it doesn't exist yet.

**Fix**: Always null-check runtime components. Use `FindFirstObjectByType<T>()` defensively, not in hot paths.

---

## 2. Serialized Field Defaults vs. Inspector Overrides

**Affected**: Any `[SerializeField]` field with a default value in code

Unity's Inspector **saves serialized values to the scene file**. If you change a default value in code (e.g., `characterWalkSpeed = 1.5f` → `2f`), the Inspector will **still show the old saved value** unless you:

1. Right-click the field in Inspector → **Reset**
2. Or remove and re-add the component
3. Or change it manually in the Inspector

**Real example**: Changed `characterWalkSpeed` default from 1.5 to 2 in code, but the scene still had 1.5 saved. User had to reset the field.

**Fix**: After changing any `[SerializeField]` default, tell the user to reset the field in Inspector. For new fields, Unity picks up the code default automatically.

---

## 3. Enum Serialization Quirks

**Affected**: Fields of type `Key`, `KeyCode`, or any enum

Unity serializes enums by **integer value**, not name. If you rename an enum field, Unity may map the old integer to a **different enum value** (or an invalid one), causing `ArgumentOutOfRangeException` at runtime.

**Real example**: Renaming `triggerKey` (KeyCode) to `spawnKey` (Key) fixed an `ArgumentOutOfRangeException` because the old serialized value mapped to an invalid `Key` enum member.

**Fix**: When changing enum types or renaming enum fields, rename the field itself (not just the type) to force Unity to re-serialize with the new default.

---

## 4. UI References Must Be Wired in Inspector

**Affected**: All `[SerializeField]` UI references in `GameUIController`

`GameUIController` has ~20+ serialized UI references (`weekText`, `phaseText`, `treasuryText`, `hoodList`, `blockInfoContent`, etc.). These are **not auto-wired** — they must be assigned in the Inspector or via `GameUIAutoBuilder` (menu: Steel City → Build Game UI).

**Safety net**: `RunPreflightCheck()` logs warnings for any missing references at startup. Always check the console after wiring.

**Fix**: If a UI element isn't updating, first check if its reference is null in the Inspector. Then check `RunPreflightCheck()` output.

---

## 5. TickHUD Panel Position Is Hardcoded

**Affected**: `TickHUD.BuildHUD()`

The TickHUD panel anchors are hardcoded:
```csharp
panelRT.anchorMin = new Vector2(0.72f, 0.02f);  // right side
panelRT.anchorMax = new Vector2(0.98f, 0.98f);
```

Adding new text fields to TickHUD will **shift the event log down** because of the `VerticalLayoutGroup`. If you add fields, you may need to adjust the log's `preferredHeight` or the panel's anchors.

**Fix**: When adding fields to TickHUD, test that the event log scroll area is still visible and usable.

---

## 6. Two Separate UI Systems (Planning vs. Working)

**Affected**: Phase transitions between `GamePhase.Planning` and `GamePhase.Execution`

The Planning phase uses the main Canvas UI (`GameUIController` with TMP_Text fields, tab pages, order buttons). The Working phase creates a **separate overlay** (`TickHUD` with its own Canvas at `sortingOrder = 50`).

Code that updates UI must check which phase is active:
```csharp
if (phase == GamePhase.Planning && phaseText != null)
    phaseText.text = $"PLANNING [{fpsStr}]";
else if (tickHUD != null)
    tickHUD.UpdateFPS(fpsStr, fpsColor);
```

**Pitfall**: Adding a UI update that only targets one phase will silently do nothing in the other phase.

**Fix**: Always handle both phases when adding UI-facing data. Use `TickHUD.UpdatePerfStats()` for Working phase, and the appropriate `TMP_Text` for Planning phase.

---

## 7. FindFirstObjectByType Is Expensive in Update()

**Affected**: Any per-frame `FindFirstObjectByType<T>()` call

`FindFirstObjectByType` is an O(n) scan of all objects. Calling it every frame (or every 10 frames) adds measurable overhead, especially with 100+ agents in the scene.

**Real example**: `StressTestSpawner` called `FindFirstObjectByType<TickHUD>()` every 10 frames. With 100 agents, this scanned hundreds of GameObjects each time.

**Fix**: Cache the reference once in `Start()` or on first access:
```csharp
private TickHUD cachedHUD;
private TickHUD GetHUD()
{
    if (cachedHUD == null) cachedHUD = FindFirstObjectByType<TickHUD>();
    return cachedHUD;
}
```

---

## 8. VoxelCharacter.collisionWorld Must Be Found Per-Spawn

**Affected**: Stress test agent spawning

Each `VoxelCharacter` needs a reference to `VoxelCollisionWorld`. The stress test calls `FindFirstObjectByType<VoxelCollisionWorld>()` **per agent spawn** (100 times). This is acceptable during staggered spawning but would be a problem if done per-frame.

**Fix**: Cache the reference once before the spawn loop and pass it to each character.

---

## 9. Canvas Sorting Order Conflicts

**Affected**: Multiple Canvas overlays

The main game UI Canvas and `TickHUD` Canvas both render as `ScreenSpaceOverlay`. If sorting orders conflict, one will render on top of the other unexpectedly.

**Current setup**:
- Main game UI Canvas: default sorting order (0)
- TickHUD Canvas: `sortingOrder = 50`

**Fix**: When adding new overlay canvases, choose a sorting order that doesn't conflict. Document the order in this file.

---

## 10. Event Log Buffer Has a Hard Cap

**Affected**: `GameUIController.eventLogBuffer` (100 entries) and `TickHUD.logEntries` (30 entries)

Both event log systems silently drop old entries when the buffer is full. If you're debugging a missing log entry, it may have been pushed out by newer entries during a burst.

**Fix**: For debugging, temporarily increase `MaxLogEntries` or add a filter to only log specific categories.

---

## 11. `file://` Protocol CORS Warnings (HTML City Editor Tools)

**Affected**: `city_editor.html`, `zoning_sandbox.html`, and other HTML tools in `VoxelAssetStudio/`

When opening HTML tools directly via `file://` protocol (double-clicking the file), Chrome/Edge emits console warnings:

```
Unsafe attempt to load URL file:///...city_editor.html from frame with URL
file:///...city_editor.html. 'file:' URLs are treated as unique security origins.
```

**This is a harmless browser-level warning**, not a blocking error. The page still works. Browsers treat each `file://` load as a unique origin for security purposes.

### What WILL break under `file://`

- **External script tags** (`<script src="data.js">`) — blocked by CORS. Fix: inline the data directly in the HTML (e.g., `window.REPLICA1_DATA = {...}`).
- **`fetch()` calls** to local files — blocked. Avoid or inline the data.
- **ES module imports** (`import ... from './module.js'`) — blocked. Use inline scripts instead.

### What will NOT break

- Inline `<script>` blocks — work fine
- CDN-loaded libraries (THREE.js, etc.) — work fine
- `window.*` data assignments — work fine
- All rendering, interaction, and export functionality

### Best practice

Keep all HTML tools self-contained (inline scripts + CDN libs). If a tool needs to load external data files, inline them as `window.*` assignments. The `file://` CORS warning in the console can be safely ignored.

---

## 12. OnGUI Fires Multiple Events Per Frame — Finds Inside Draw*Tab() Multiply

**Affected**: Any `Draw*Tab()` / `OnGUI` body that calls `FindFirstObjectByType`, `FindObjectsByType`, or `GetComponent` chains

Pitfall #7 covers per-frame finds — IMGUI is worse: `OnGUI` runs once per **event** (Layout + Repaint + used mouse/key events ≈ 2–4+ calls per rendered frame). A single `FindFirstObjectByType` inside a draw method is therefore a **full scene scan several times per frame**, not once.

**Real example** (Oct 2026): `DrawSfxTab` called `FindFirstObjectByType<AudioListener>()` inline. Opening the SFX tab took FPS 120 → 40 instantly. The same file already throttled `RefreshReferences` ("8 of them per IMGUI event was a measurable stall") — new tab code just wasn't held to the rule.

**Fix**: cache on the existing 1-second cadence, never inline in a draw method:

```csharp
private AudioListener sfxListener;                 // field
// in Update(), inside the existing `clothingListRefreshTimer >= 1f` block:
sfxListener = FindFirstObjectByType<AudioListener>();
// in Draw*Tab(): read the field — plus ONE lazy resolve for first-second UX:
var listener = sfxListener != null ? sfxListener
    : (sfxListener = FindFirstObjectByType<AudioListener>());
```

**Sibling trap**: `XxxSystem.GetOrCreate()` inside a tab method lazily spawns a singleton whose `Update()` then runs every frame *forever after* — even when the user closes the tab. If a newly spawned system's Update does real work, that's a persistent cap, not a while-open one. Keep spawned-system Updates cheap-when-idle (early-out on empty lists).

**Related**: `docs/known_issues/runtime/PERF_DEBUG_BEAM_EMISSION_FLOOD.md` — per-frame `DrawMeshInstanced` debug emitters (beam rings/cones/rays) are the other res-independent 120→40 regression; debug emit flags now default off.

---

## 13. Widget Count × Event Multiplier — Live Tabs Hit a Density Cliff

**Affected**: Any `Draw*Tab()` with live controls (sliders/toggles/buttons), especially loops that draw a row per scene object

**Symptom** (Oct 2026 tab matrix): text-cached tabs (Camera/Render/Path/Keys) ran 120 FPS, BuildLog 100, and every *live* tab — SFX, Clothing, Combat, Cover, TanSuit, Demolitions, MatRes — dropped to ~40 FPS. The original frame-tax fix cached the text tabs but left interactive ones rebuilding all widgets per event.

The cost model: `total ≈ widgets × events/frame` — Layout + Repaint + input ≈ 4–6× per frame, and **loops multiply silently**: `DrawClothingTab` drew one button per `CharacterRig`/`ClothingSystem` (~53 rigs → ~100 buttons per event ≈ 500 control evals/frame). IMGUI does not cull scrolled-off controls — long tabs pay for everything below the fold.

**Fix**: `SecFold(key, title)` foldout headers (`DebugHUDManager`) — a collapsed section costs 1 toggle instead of N controls; state persists for the session; `open all`/`close all` under the tab rows. Sections default open so UX is unchanged until collapsed. Rules for folding:
- Compute that later code reads (`hasPos`/`tgtWorld` in Combat) stays **outside** the fold — fold display only.
- Per-`Find*` calls inside loops/tabs get a 1s-cached property (`TanSuitRef`), not an inline call.
- Watch shared locals across fold boundaries (`bool dirty`, loop counters) — declare them per-fold or hoist.

**Also bounded**: lazy-resolve fallbacks that call `Find*` when the cache is null retry *every event* if the object truly doesn't exist — gate retries on a timestamp (`sfxListenerRetryAt`).

**Update — the real wall was native, not managed**: a `Stopwatch` around the whole `OnGUI` body measured ~1ms/frame while the frame ran 22ms at 44fps. The cost is the **native IMGUI playback** — mesh/text generation billed to `EditorLoop`, scaling with drawn content, invisible to C# instrumentation. A/B: suppressing just the tab body (`hudLite`) restored 120fps with the full window visible. **Resolution**: `hudLocked` — locked tabs render one cached label rebuilt ~4Hz (the text-tab pattern); read-only mirrors keep state observable; `L` key or the LOCKED button unlocks for tuning. Widget-count reduction (foldouts) remains useful but cannot defeat the draw-path cost — text is the cheap architecture.

---

## Summary Checklist

When modifying UI code:

- [ ] Null-check all runtime-created components
- [ ] Reset `[SerializeField]` defaults in Inspector after code changes
- [ ] Rename enum fields when changing enum types
- [ ] Verify Inspector references are wired (check `RunPreflightCheck()`)
- [ ] Handle both Planning and Working phase UI updates
- [ ] Cache `FindFirstObjectByType` references, don't call per-frame
- [ ] Never call Find*/GetComponent scans inline in `OnGUI`/`Draw*Tab()` — events fire several times per frame; cache on the 1s refresh cadence
- [ ] `GetOrCreate` in a tab spawns a permanently-running singleton — keep its idle Update cheap (early-outs)
- [ ] Watch widget count on live tabs — loops over scene objects (rigs/systems/material rows) multiply the per-event tax; wrap dense sections in `SecFold` foldouts
- [ ] Check Canvas sorting order doesn't conflict
- [ ] Test that TickHUD event log is still visible after adding fields
- [ ] For HTML tools: inline external data files, ignore `file://` CORS warnings
