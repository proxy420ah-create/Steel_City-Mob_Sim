# Live Editor Pipeline — in-game tuning with staged code promotion

**Status**: ✅ Proven (MatRes tab shipped; wood 0.45 graduated to authored live, mid-session)
**Last Updated**: October 13, 2026

---

## The concept

The debug HUD is not just a viewer — it's a **live editor**. Game-mechanic constants
are tuned *while playing*, watching real results, then locked into source code
through a staged promotion pipeline. Iteration happens at the speed of playtesting
instead of the speed of edit-compile-relaunch.

The realization that unlocks it: a C# literal in a source file is just text. The HUD
can select a tuned value, rewrite the literal in place, and call
`AssetDatabase.Refresh()` — Unity does a real recompile + domain reload and every
layer reseeds from the new source. The "commit to code" step that normally requires
stopping, editing, and relaunching happens **inside the running game**.

## The three layers

| Layer | What it is | Lifetime | HUD color |
|---|---|---|---|
| **AUTHORED** (`BaseResistance` / field initializers) | Factory defaults — the locked, shipped baseline | permanent (in code) | 🔵 blue |
| **WORKSHOP** (`WorkshopRes` / pending overrides) | The in-between place — approved-but-pending tuning values that seed each play | in code, clearly marked pending | 🟠 orange |
| **LIVE** (`ResTable` / serialized fields) | What sliders edit each frame | session only — dies on domain reload | 🟢 green |

```
slider → 🟢 green (tuned, unsaved)
       → "promote → workshop" → 🟠 orange (session baseline, seeds each play)
       → select + "promote → AUTHORED" → 🟢🔵 literal rewrite → recompile → blue forever
```

## Mechanics (as implemented in MatRes)

- **Provenance colors** — a value's name is colored by where it *came from*:
  blue = equals authored, orange = equals the workshop baseline, green = differs
  from both. The state of every tunable is legible at a glance.
- **Click-to-select** — material names are flat buttons; `►` marks the selected
  class as the promotion target.
- **Promote → workshop** — copies the live table into the session baseline
  (`PromoteToWorkshop`). Survives the session, seeds every play — but is
  still "pending", not shipped.
- **Promote → AUTHORED** — rewrites the source literal
  (`BaseResistance = { ... }` inside `VoxelDamageMath.cs`) via regex, then
  `AssetDatabase.Refresh()`. Recompile + domain reload → statics reseed → the
  value is factory. *Exits play mode by nature of the reload — this is the
  point of the workflow, not a bug.*
- **Graduation hygiene** — when a workshop value promotes, its pending override
  line is removed so `BuildWorkshop()` only ever holds non-factory values.

## What makes a variable "promotable"

The promotion mechanism needs a **deterministic code target**:

- ✅ A named constant or array literal (`BaseResistance`, `public const float X = ..`,
  `public float field = 0.5f;` initializers) — regex-rewriteable
- ✅ Static tables seeded in code (`BuildWorkshop`-style builders)
- ✅ Serialized fields on **runtime-spawned components** — `GetOrCreate`/`AddComponent`
  at runtime means no serialized data exists anywhere; the initializer IS the only
  truth, so the literal rewrite works cleanly (our `VoxelDamageSystem` case)
- ⚠️ Serialized fields on **scene/prefab-placed components** — see the lifecycle
  trap below
- ❌ Scene topology, prefab wiring, shaders, .meta-linked data — not literals,
  don't live-edit these; they need the real editor

### ⚠️ The serialized-field lifecycle trap

A serialized field has **two sources of truth**:

```csharp
public float pairChance = 1.0f;   // initializer: runs ONCE at construction
```

When a component is saved into a scene/prefab, Unity stores its field values in
the asset YAML. On load: the C# constructor runs the initializer, then
**deserialization overwrites it** — the stored value always wins. The initializer
only matters for brand-new components (fresh `AddComponent`, or components whose
scene data predates the field's existence).

The classic failure this produces: edit `= 0.5f` → `= 1.0f`, play, and the value
is *still 0.5* — the scene YAML held the old number. A literal-rewrite promote on
such a field would look **broken but actually be correct-and-overridden** — the
worst kind of false negative for a live editor.

**Promoting a scene-placed field therefore needs two writes, not one:**

1. **The live scene value** — via `SerializedObject`/`SerializedProperty` +
   `EditorUtility.SetDirty` (Unity-native; dirty-marks the scene). *Never* regex
   the `.unity` YAML of an open scene — Unity owns it and silently overwrites
   external edits on save.
2. **Optionally the initializer** — the literal rewrite, so future components
   also default correctly.

**Design preference for new tuning surfaces:** `static class` const/field tables
(one source of truth, single-writer promote, works everywhere) over per-component
serialized fields — unless per-instance values genuinely differ (e.g. per-vehicle
top speed).

## Adoption contract — new systems join by

1. **Name the constants** — no magic numbers buried in logic; every tunable lives
   in one identifiable literal/table.
2. **Expose live + baseline** — a mutable table the HUD edits, and a code-seeded
   baseline the reset buttons restore.
3. **Register a promote path** — either the generic literal-rewrite (works for
   `const`/field initializers today) or a bespoke writer for structured data.
4. **Show provenance** — colors/markers so unsaved vs. pending vs. locked is
   visible without remembering.

## Candidate applications (roadmap order)

- **Combat metrics** — awareness ring radii, NPC field-of-view arcs, aim-cone
  parameters, time-to-target, moving-target lead coefficients: all feel-critical
  and impossible to tune without watching live behavior
- **Economy balancing** — payout curves, extortion rates, upkeep, price floors:
  watch a week tick over at different numbers, promote when the curve feels right
- **Police** — patrol counts, response latencies, heat decay, detection radii
- **Order execution** — command latencies, travel-time multipliers, retry
  intervals, success thresholds
- **The demolition knobs that started it** — blast radius/power/passes, impulse,
  jitter, land speed (already sliders; promoting writes the serialized fields —
  see the ⚠️ caveat above)

## Guardrails

- **Play-mode exit is inherent** — promoting recompiles. Tune in the session,
  promote between sessions or accept the reload.
- **Workshop is session-static** — pending values persist across plays *in the
  same editor session* via code seeding; they are NOT serialized into builds.
  Permanent means authored.
- **Don't promote what you haven't watched** — the color system exists so a
  glance tells you what's live-vs-baseline; trust it, then lock it.
- **Diff before commit** — promoted literals are real source edits; `git diff`
  shows exactly what graduated.

## Future: a LiveEditor registry

The natural generalization: tunables self-register —
`LiveEditor.Register("combat.awarenessRadius", ref field, min, max, tab, promoteTarget)`
— and the HUD builds sliders, colors, selection, and the promote button for free.
The MatRes table is the hand-rolled first instance; anything that adopts the
contract above is one registration away from the same UX.
