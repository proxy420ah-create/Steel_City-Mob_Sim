# Design Philosophy — Steel City: Mob Sim

**Created**: August 2, 2026
**Status**: 📐 FOUNDATIONAL — These principles guide all design decisions

---

## 1. Simple Mechanics, Complex Interactions

This is the single most important principle. Each individual system should be
trivially simple — a roll against a number, a lookup in a table, a comparison
of two values. The depth of the game comes from **how these simple systems
connect and feed back into each other**.

### Example: The Extortion Loop

- Extortion mechanic: roll hood's Intimidation vs. owner's Fear/Hostility. Simple.
- Crime escalation: pick a crime from a table, apply effects. Simple.
- Squeal mechanic: roll squeal value, trigger investigation on success. Simple.

But the chain is emergent:

```
Refusal → Player chooses response → Response changes NPC state →
NPC state changes squeal risk → Squeal triggers investigation →
Investigation threatens hoods → Player must respond →
Response costs resources → Resource cost affects expansion →
Expansion affects territory → Territory affects intel → ...
```

Each link is simple. The chain produces stories without scripting.

### Anti-Pattern: Over-Engineering Individual Systems

The original game avoided complex subsystems. Bribery isn't a negotiation
minigame — you pay, they look the other way. Combat isn't a tactical
minigame — stats resolve it. This rigidity is a **feature**, not a bug.
It keeps the player's cognitive load on strategic decisions, not on
mastering subsystem mechanics.

---

## 2. Data-Driven Architecture

All game balance lives in external, moddable data files. The original used
custom `.xtx` text tables. We use JSON or TOML. No game balance values
should be hardcoded in source code.

### What Goes in Data Files

- Character archetypes and stat ranges
- Crime definitions (suspicion, sentence, investigation, risk)
- Weapon hit/damage tables
- Business definitions (profit, costs, capacity, type)
- Constants (population counts, fear bases, bribe prices)
- Scenario definitions
- City generation parameters

### What Stays in Code

- Simulation tick logic (order of system evaluation)
- UI rendering
- Save/load serialization
- AI decision-making framework (but AI priorities are data-driven)

---

## 3. Simulation Produces Narrative

No scripted stories. No dialogue trees. No quest system. The butcher who
refuses to pay, the cop who gets bribed, the lieutenant who betrays you —
all of these emerge from simulation state interacting with player decisions.

### What This Means

- NPCs have persistent state (fear, hostility, squeal, relationships)
- Player actions modify NPC state
- NPC state determines future NPC behavior
- The chain of cause-and-effect IS the story
- Every playthrough produces different stories because the simulation state
  evolves differently based on player choices

### The Butcher Example

The butcher on Baker St. isn't a quest giver. He's a simulation entity with
fear and hostility values. When he refuses to pay, that's not a scripted
event — it's `fear < hostility` this week. When the player intimidates him
and he squeals to the cops, that's not a branching dialogue — it's the
squeal roll succeeding. The butcher becomes memorable because of what
happened to him in *this* playthrough, not because a writer wrote his story.

---

## 4. Auto-Resolved Everything

The player is a **manager**, not an action hero. The boss assigns orders,
allocates resources, and reviews results. The simulation executes.

### Combat

Auto-resolved based on:
- Hood skills (Firearms, Fists, Knives, etc.)
- Intelligence (governs tactical decision quality — cover, retreat, targeting)
- Environment (cover availability, civilian presence, time of day)
- Equipment (weapon type, modifiers)
- Numerical advantage

The player's job is to **assign the right hoods to the right jobs**. If a
fight goes badly, the player reviews what happened and adjusts — send
smarter hoods, bring more manpower, equip better weapons. This is the
management game, not the action game.

### Investigations

Auto-resolved based on:
- Investigation leads (accumulate from crimes, decay over time)
- Police competence vs. hood's "lie low" efforts
- Corrupt cop suppression
- Evidence cleanup

### Business Operations

Auto-resolved based on:
- Business type and location
- Market share (diminishing returns)
- Land value
- Competition
- Employee availability

---

## 5. Faithful Modernization

We are **not reinventing** the genre. We are taking a proven 1998 design
and polishing it for modern players. The original's core loop is sound:
Gang Organizer (plan) → Working Week (execute) → Results (review).

### What We Preserve

- The two-phase game loop (planning + execution)
- Data-driven game balance
- Isometric city view
- Extortion/territory as core mechanic
- Crime escalation ladder
- NPC fear/hostility/squeal system
- Business ownership (legal + illegal)
- Recruitment and hood management
- Rival AI gangs
- Law enforcement response

### What We Polish

- **Transparency**: Make simulation state visible. The original was opaque —
  you couldn't see why a business owner refused or how close the police were
  to an arrest. We show the numbers.
- **Interconnection**: Make systems talk to each other. The original's crimes
  don't cascade, businesses don't interact, hoods don't have relationships.
  We wire them together.
- **UI/UX**: Modern interface design. Cleaner Gang Organizer. Better map
  overlays. Readable notifications (no spam). Filterable reports.
- **Procedural city**: Every game generates a new city layout with districts,
  demographics, and business distribution. The original's cities are static.

### What We Don't Do

- No real-time tactical combat (no XCOM-style encounters)
- No scripted narrative (no story missions, no cutscenes)
- No dialogue systems
- No 3D graphics (isometric 2D, modern resolution)
- No microtransactions, no live service, no multiplayer (initially)

---

## Design Decision Framework

When evaluating any new feature or system change, ask:

1. **Does it serve the core loop?** (Extort → Expand → Defend → Manage)
2. **Is the mechanic simple?** (Can it be explained in one sentence?)
3. **Does it interact with other systems?** (If isolated, it doesn't belong)
4. **Is it data-driven?** (Can a modder change it without touching code?)
5. **Does it produce emergent stories?** (Or is it just a number going up?)
6. **Does the original have something similar?** (If yes, we're polishing. If no, we're adding carefully.)

If a feature fails any of these tests, it needs strong justification before inclusion.

---

## 6. Mafia Tycoon, Not City Builder

**Full document**: `docs/core/MAFIA_TYCOON_DESIGN_PRINCIPLE.md`

The economy exists as a **weapon and a consequence**, not as a management
interface. The player is a crime boss, not an economist.

**The player disrupts the economy through crime. The player does NOT manage
the economy through business controls.**

### Quick Test

When evaluating any economic feature, ask:
1. Does the player interact with this through crime? → Mafia tycoon ✅
2. Does the player need a dashboard to understand it? → City builder ❌
3. Can the player ignore this and still play? → If no, it's management — cut it
4. Does this produce emergent consequences from criminal actions? → Mafia tycoon ✅
5. Does this require the player to optimize numbers? → Business management ❌

### Information Rule

Economic information is **gated behind gang activity** (spying, casing, scouting).
The player never sees raw economic data without gang intelligence gathering.

### Action Rule

Economic change happens **through crime, not through management**.
The player bombs, raids, extorts, and intimidates. They do not set prices,
adjust wages, or manage supply chains.
