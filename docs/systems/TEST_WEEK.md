# Test Week — The Open-Week Debug Order

**Created**: March 2026
**Status**: ✅ Active — official debug entry point
**Formerly**: `target_practice` (renamed when the order outgrew the range)

---

## What It Is

**Test Week is the unified debug harness for the "amusement park" of feature
testbeds.** Issue it like any order during planning — the week resolves
normally, then *stays open* in the `Dwelling` sim state instead of walking
home. While dwelling, the debug tabs, voxel picks, block-click harnesses, and
combat/cover test lots are all live. `Enter` ends the week.

It started life as `target_practice` ("walk to the firing position, shoot at
the range") and grew into something bigger: the order isn't about practice —
it's about *holding a week open inside a controlled test environment*. The
rename officiates that.

## The Mechanism

| Stage | What happens |
|---|---|
| **Order issued** | `test_week` orderType; button auto-created by cloning Lie Low (teal label "Test Week") — no scene wiring needed |
| **Arrival** | Walks to the block's `firing_position` (or `cover_stand_a`-aliased stand point); `testWeekSkipWalk` teleports instead of walking (default on) |
| **Hold** | `testWeekDwells` (default on) → `SimState.Dwelling` — sim consumes no ticks until `ReleaseDwell()` |
| **Exit** | `Enter` key → `ReleaseDwell()` → week completes normally |

**Inspector options** (`GameUIController` → ORDERS):
- `testWeekDwells` — uncheck to end the week normally on arrival (disables the harness entirely)
- `testWeekSkipWalk` — uncheck to walk instead of teleport (useful when testing pathing itself)

## The Testbed Registry

Testbeds register through **authored attachment points** in their `.stasset`.
While dwelling, `HandleBlockClicked` checks each clicked block for known
combat anchors and opens the right debug tab:

| Anchor point | Opens | Testbed |
|---|---|---|
| `firing_position` / `firing_position_N` | Combat tab → lane UI | `shooting_range` (block_46): 3 lanes, targets, backstop, ballistics bench + glass mold |
| `cover_stand_a` + `cover_stand_b` | Combat tab → pit branch | `cover_pit` (block_45): symmetric duel — SETUP PIT / START DUEL, opponent toggle (dummy/AI) |
| `blast_target*` | Demolitions picks | `debris_test_lot` (block_48): blast/structural-integrity testing |

`firing_position`/`firing_target` double as **aliases** — the cover pit maps
them to its stand points so the order's walk/facing resolution treats every
testbed uniformly. New testbeds should follow the same convention: authored
points → alias into `firing_*` if the order should land the character there.

## Why It's One Order, Not N

Every testbed shares the same lifecycle: *place character → resolve → hold
open → debug → release*. Keeping one order means:

- One Inspector toggles the whole park (`testWeekDwells` / `testWeekSkipWalk`)
- New testbeds inherit the entry path for free — just author attachment points
- The block click decides *which* harness opens, not the order type — the
  order is the door; the lot is the room

## Adding a New Testbed

1. Author a `.stasset` (`Tools/gen_*.py` → `json_to_stasset.py`) with painted
   stand/anchor points; alias into `firing_*` where the order should land.
2. Register it in `city_layout.json` (`building_types` + a block's buildings).
3. Extend `HandleBlockClicked`'s anchor check if it opens a new UI branch.
4. Everything else — dwell, teleport, facing, Enter-release — is inherited.

## Files

- `GameUIController.cs` — order button, dwell/skip toggles, arrival + snap logic
- `SimulationManager.cs` — `testWeekDwells` hold, `ReleaseDwell()`, OrderTicks `166`
- `DebugHUDManager.cs` — `HandleBlockClicked` anchor routing, per-testbed tab branches
- `Tools/gen_cover_pit.py`, `Tools/gen_shooting_range.py`, `Tools/gen_debris_lot.py` — authored testbeds

## Animation debug panel — pose axes on the pad

The numpad is a physical pose panel (no Windows menu-beep, no WASD conflict;
the DEBUG latch holds over combat/locomotion until `Num0`/`NumEnter`):

```
[7] T-Pose   [8] Down     [9] Sit
[4] Aim      [5] Crouch   [6] Flinch
[1] Walk     [2] Look     [3] AimWalk
[0] release  [.] AimSweep [Enter] release      [+/-] anim speed
```

Two live axes ride under every state change — tune them per rig in the
Clothing tab (Character Control → Pose transition / crouch depth):

- **Pose transition** — on any state change the outgoing pose freezes and the
  pose eval dual-evals both ends (snapshot → target lerp + settle dip).
  Knobs: enabled / duration / dip / ease. Previewed identically in
  `character_animator.html` — **author destinations, the engine owns the path.**
- **Crouch depth** — `crouchDepth` 0..1 lerps every authored scalar
  `crouching → crouchDeep`. The combat solver sets it from picked cover
  height (`CoverSensor` evaluates the posed eye at both endpoints); the slider
  previews any depth manually — `Num5` + drag. Authoring: `crouchDeep` block
  in `.anim.json` (same fields as `crouching`); absent → cloned shallow =
  inert. See COMBAT_COVER_STATE.md → "Adaptive crouch depth".

## Tooling rule — no selection ceremonies

Debug actions **resolve their own context**. A control that needs a state,
a click order, or a hidden flag must satisfy it internally or say why it
can't — never silently require a ritual first (the "state lock" failure
mode, hit twice in one session: dwell-gated pit setup; a pose-hotkey flag
with no code path that set it).

- **Gate on missing *things*, not *modes*** — "no weapon equipped" is a
  real gate; "sim isn't Dwelling" is ceremony.
- **Disabled buttons announce their reason** — a grey button without an
  explanation reads as broken, not blocked.
- **Discovery over selection** — the pit is found by probing blocks for
  `cover_stand_a`; tools find their targets, they don't wait to be aimed.
- **`Dwelling` remains a *sim* concept** (Test Week holds the week open —
  that's the feature), but it is never a *key turn* for debug tooling.
