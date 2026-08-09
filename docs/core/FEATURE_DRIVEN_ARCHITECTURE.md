# Feature-Driven Architecture Pipeline

**Created**: August 6, 2026
**Status**: 🔒 ACTIVE — Megaplan methodology for Steel City technical foundation

---

## Purpose

Every technical decision in Steel City must be grounded in an actual gameplay reason. This document defines the workflow for evaluating each proposed differentiator (new system, expanded mechanic, or modern improvement over Gangsters OC) through the full technical stack before locking it in.

---

## The Pipeline (Per Feature)

```
┌─────────────────────────────────────────────────────┐
│  1. DIFFERENTIATOR                                   │
│  • What does Steel City do that Gangsters OC didn't? │
│  • Why does this make the game better?               │
│  • Is this a core feature or nice-to-have?           │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│  2. SIM + DATA + AI                                  │
│  • How is this feature modeled in the simulation?    │
│  • What data structures store it?                    │
│  • What AI behaviors drive it?                       │
│  • How does it interact with existing systems?       │
│  • What entity fields/components does it require?    │
│  • Does it change the tick budget per entity?        │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│  3. RENDERING + PERFORMANCE                          │
│  • What does this feature cost to render?            │
│  • Does it add draw calls, materials, or meshes?     │
│  • Does it increase per-tick CPU cost?               │
│  • Are we still within the frame budget?             │
│  • Do we need LOD, culling, or batching for it?      │
│  • Does this push us toward needing DOTS/ECS?        │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│  4. LOCK-IN                                          │
│  • Record final design decision                      │
│  • Note performance impact                           │
│  • Add to feature tracker below                     │
│  • Move to next differentiator                       │
└─────────────────────────────────────────────────────┘
```

---

## Running Feature Tracker

### Core Features (Must Have)

| # | Feature | Differentiator | Sim/Data/AI | Render/Perf | Status |
|---|---------|---------------|-------------|-------------|--------|
| 1 | Individual citizen economy | Per-entity bank accounts, employment, housing, alignment, desperation, aspiration | +26 bytes/entity, weekly order system (~0.2ms/week), full employment+housing graph, unified entity model (all statuses are citizens) | Zero render cost, 0.3ms frame budget used, no DOTS needed | ✅ Locked |

### Nice-to-Haves (Stretch Goals)

| # | Feature | Differentiator | Sim/Data/AI | Render/Perf | Status |
|---|---------|---------------|-------------|-------------|--------|
| — | — | — | — | — | — |

### Deferred / Rejected

| # | Feature | Reason | Date |
|---|---------|--------|------|
| — | — | — | — |

---

## Performance Budget Tracker

Updated after each feature lock-in. Running tally of estimated per-frame cost.

| Category | Budget | Used | Remaining | Notes |
|----------|--------|------|-----------|-------|
| Entity AI (CPU) | 4.0ms | 0.1ms | 3.9ms | ~3300 entities, weekly citizen orders |
| Rendering (GPU) | 8.0ms | 0.0ms | 8.0ms | 60 FPS target = 16.6ms total |
| Sim Logic (CPU) | 2.0ms | 0.2ms | 1.8ms | Weekly economic sim (F#1) |
| Overhead (CPU) | 1.6ms | 0.0ms | 1.6ms | Audio, input, UI, GC |
| **Total** | **16.0ms** | **0.3ms** | **15.7ms** | **~60 FPS target, 1.9% used** |

---

## Baseline Constants (from RE + Constants.xtx)

Entity counts the original game targets — our minimum parity baseline.

| Entity Type | Count | Source |
|-------------|-------|--------|
| Civilians | 2,000 | `Constants.xtx` |
| Police | 400 | `Constants.xtx` |
| FBI | 100 | `Constants.xtx` |
| Judges | 12 | `Constants.xtx` |
| Attorneys | 12 | `Constants.xtx` |
| Player Hoods | 100 (10 lt + 90 hoods) | User gameplay knowledge |
| Rival Hoods | 300 (100 × 3 rival gangs) | User gameplay knowledge |
| Vehicles | ~100-200 | Estimated from gameplay |
| **Total** | **~3,100-3,300** | **Parity baseline** |

Entity record in original: 132 bytes (0x84 stride), pre-allocated array.
SIM_TICK processes one entity per call, 16,980 bytes of code per entity.

---

## Discussion Log

Each completed pipeline cycle gets a summary entry here for traceability.

### Cycle 1: Individual Citizen Economy

**Proposed by**: User
**Differentiator**: Every entity (citizens, hoods, police, officials) has individual bank accounts, employment, housing, alignment, desperation, and aspiration. The economy is a motivation engine — the player disrupts it through crime, never manages it directly.

**Key design decisions**:
- **Mafia Tycoon principle established** — economy as weapon, not management interface. Full document: `MAFIA_TYCOON_DESIGN_PRINCIPLE.md`
- **Full employment + housing graph** — citizens assigned to specific businesses (employer) and tenement blocks (shelter). Bombing either directly impacts named individuals.
- **Unified entity model** — all statuses (civilian, hood, police, judge, mayor, etc.) are citizens with different starting conditions. Everyone participates in the economic simulation.
- **Hoods embedded in citizen flow** — hoods leave from their own homes at week start, not from gang offices. Solves the car-dispatch exploit from Gangsters OC without fog of war.
- **Desperation × alignment × skills matrix** — drives citizen behavior, recruitment cost, corruption susceptibility, and alignment drift
- **Weekly citizen order system** — parallel to gang orders, runs during working week, spread across first ~100 ticks

**Sim/Data/AI decision**: +26 bytes/entity (finances, employer ptr, shelter ptr, alignment, desperation, aspiration). Weekly evaluation pass (~0.2ms). Full employment + housing graph with event-driven maintenance. Alignment is 0-255 byte (legal 0-127, criminal 128-255) with drift based on desperation.

**Render/Perf decision**: Zero additional rendering cost. 0.3ms total frame budget used (1.9%). No DOTS/ECS needed. Economic sim is weekly, not per-tick. 111KB total memory overhead.

**Status**: ✅ Locked — August 6, 2026
