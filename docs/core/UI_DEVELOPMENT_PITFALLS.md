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

## Summary Checklist

When modifying UI code:

- [ ] Null-check all runtime-created components
- [ ] Reset `[SerializeField]` defaults in Inspector after code changes
- [ ] Rename enum fields when changing enum types
- [ ] Verify Inspector references are wired (check `RunPreflightCheck()`)
- [ ] Handle both Planning and Working phase UI updates
- [ ] Cache `FindFirstObjectByType` references, don't call per-frame
- [ ] Check Canvas sorting order doesn't conflict
- [ ] Test that TickHUD event log is still visible after adding fields
- [ ] For HTML tools: inline external data files, ignore `file://` CORS warnings
