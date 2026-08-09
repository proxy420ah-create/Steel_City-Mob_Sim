# Vertical Slice — End-to-End Test Design

**Created**: August 2, 2026
**Status**: 📐 In Progress

---

## Goal

Build a minimal end-to-end test that exercises the full game loop:
**Gang Organizer (plan) → Working Week (simulate) → Results (review)**.

Design for scale from the start — systems should work with 9 blocks or 400 blocks without code changes.

---

## Slice Parameters

| Parameter | Value | Notes |
|---|---|---|
| City size | 3×3 grid (9 blocks) | Mixed residential + commercial |
| Factions | 2 (Player + 1 Rival AI) | Minimal but tests competition |
| Blocks | 9 | Mix of apartments, commercial businesses |
| Businesses per block | 1-3 | Butcher, bakery, barbershop, apartment building, etc. |
| NPCs per block | 10-20 | Citizens with fear/hostility/squeal |
| Player start | 3 hoods, $3,000, 1 business | Scaled down from original's 5 hoods / $6K |
| Rival start | 3 hoods, $3,000, 1 business | Symmetric for fair testing |
| Police | 2 beat officers | Each covers ~4-5 blocks |
| Weeks to test | 5-10 | Enough to see escalation, territory shifts, economy |

---

## Block Layout (3×3)

```
┌──────────┬──────────┬──────────┐
│ BLOCK 1  │ BLOCK 2  │ BLOCK 3  │
│ NW       │ N        │ NE       │
│ Apts (2) │ Butcher  │ Bakery   │
│          │ Apt (1)  │ Apt (1)  │
├──────────┼──────────┼──────────┤
│ BLOCK 4  │ BLOCK 5  │ BLOCK 6  │
│ W        │ CENTER   │ E        │
│ Barber   │ EMPTY    │ Casino   │
│ Apt (1)  │ LAND     │ (illegal)│
├──────────┼──────────┼──────────┤
│ BLOCK 7  │ BLOCK 8  │ BLOCK 9  │
│ SW       │ S        │ SE       │
│ Diner    │ Garage   │ Apts (2) │
│ Apt (1)  │ Apt (1)  │          │
└──────────┴──────────┴──────────┘

Player HQ: Block 7 (SW corner)
Rival HQ: Block 3 (NE corner)
Police Station: Block 5 (center)
```

---

## Systems to Test (Priority Order)

### 1. City Generation
- Generate 9 blocks with businesses, NPCs, population
- Load from JSON data files (constants, archetypes, crimes)
- Assign starting conditions (player HQ, rival HQ, police beats)

### 2. Character Generation
- Generate 3 hoods per faction using weighted archetypes
- Generate NPCs per block with fear/hostility/squeal values
- Generate 2 police officers with beat assignments

### 3. Gang Organizer (Minimal)
- List hoods with stats
- List businesses with income
- Assign orders: extort, collect protection, patrol, lie low
- Show block info (owner, strength, NPCs, businesses)
- Show finances (income, expenses, balance)

### 4. Working Week Simulation
- Resolve orders (extortion rolls, collection, patrol)
- NPC compliance checks (fear vs hostility)
- Squeal generation (NPCs roll against squeal value)
- Investigation creation and lead tracking
- Rival AI takes simple actions (extort unowned blocks)
- Economy tick (business income, payroll expenses)
- Police patrol checks (suspicion suppression if bribed)

### 5. Results & Reports
- Per-order results (success, failure, reason)
- Financial summary
- Territory changes (new blocks, lost blocks, strength changes)
- Investigation status
- Notifications (tiered: green/yellow/red)

---

## What We Build With

**Python for the simulation prototype.** Fastest iteration, no compile step, easy to test. The simulation is pure logic — JSON in, events out. No rendering needed yet.

Once systems are validated, port to C# for the Unity build. The data files (JSON) carry over unchanged.

```
Prototype:
  Python simulation engine
    ├── loads data/*.json
    ├── generates city, hoods, NPCs
    ├── runs Gang Organizer (text/terminal interface)
    ├── runs Working Week (time-sliced simulation)
    └── outputs results (text reports + event log)

Production (later):
  C# simulation engine (port from Python)
    ├── same data/*.json
    ├── Unity Gang Organizer UI (2D)
    └── Unity 3D Working Week renderer
```

---

## File Structure for Prototype

```
SteelCityMobSim/
├── src/
│   ├── sim/
│   │   ├── __init__.py
│   │   ├── engine.py          — Main game loop, phase management
│   │   ├── city.py            — City generation, blocks, districts
│   │   ├── character.py       — Hood and NPC generation
│   │   ├── police.py          — Beat officers, corruption
│   │   ├── economy.py         — Business income, expenses, market share
│   │   ├── crime.py           — Crime resolution, squeal, investigations
│   │   ├── combat.py          — Auto-battle resolution
│   │   ├── territory.py       — Extortion, territory strength, info tiers
│   │   ├── rival_ai.py        — Rival gang decision-making
│   │   └── events.py          — Event types, event stream
│   ├── data/
│   │   ├── __init__.py
│   │   └── loader.py          — JSON data file loader
│   ├── ui/
│   │   ├── __init__.py
│   │   ├── organizer.py       — Gang Organizer (text interface)
│   │   └── reports.py         — Results and reports (text output)
│   └── main.py                — Entry point
├── data/
│   ├── constants.json         — (exists)
│   ├── archetypes.json        — (exists)
│   ├── crimes.json            — (exists)
│   ├── weapons.json           — (exists)
│   ├── businesses.json        — NEW: business definitions
│   └── city_template.json     — NEW: 9-block test city template
└── tests/
    └── test_vertical_slice.py — End-to-end test
```

---

## Success Criteria

The vertical slice is complete when:

1. ✅ City generates with 9 blocks, businesses, NPCs, 2 factions, 2 police
2. ✅ Player can view hoods, businesses, blocks, finances in Gang Organizer
3. ✅ Player can assign orders (extort, collect, patrol, lie low)
4. ✅ Working Week resolves all orders with meaningful outcomes
5. ✅ Extortion refusal chain works (refuse → intimidate → comply or escalate)
6. ✅ Squeal generates investigations with lead tracking
7. ✅ Rival AI takes at least one action per week
8. ✅ Economy produces income/expenses
9. ✅ Territory strength changes based on actions
10. ✅ Results report shows what happened clearly
11. ✅ All of the above works with 9 blocks AND scales to 400 without code changes
