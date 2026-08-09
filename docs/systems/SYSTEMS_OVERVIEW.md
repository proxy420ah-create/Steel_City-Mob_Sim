# Systems Overview — Steel City: Mob Sim

**Created**: August 2, 2026
**Status**: 📐 In Progress

---

## System Interaction Map

```
                    ┌──────────────┐
                    │  EXTORTION   │◄──── core gameplay loop
                    │  & TERRITORY │
                    └──────┬───────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐
        │INTELLIGENCE│ │  CRIME   │ │ ECONOMICS│
        │ (territory)│ │ & SQUEAL │ │ (business)│
        └──────┬─────┘ └────┬─────┘ └────┬─────┘
               │            │            │
               ▼            ▼            ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐
        │CORRUPTION │ │   LAW    │ │ CHARACTERS│
        │ & POLICE  │ │ENFORCEMENT│ │ & HOODS  │
        └──────┬─────┘ └────┬─────┘ └────┬─────┘
               │            │            │
               └────────────┼────────────┘
                            ▼
                    ┌──────────────┐
                    │   COMBAT     │
                    │ (auto-battle)│
                    └──────────────┘
```

## Core Gameplay Loop

```
1. PLAN (Gang Organizer)
   ├── Assign hoods to orders (extort, intimidate, patrol, recruit, etc.)
   ├── Manage businesses (buy, sell, set up illegal ops)
   ├── Bribe cops (pay weekly, maintain coverage)
   ├── Review intelligence (territory info, rival sightings, investigations)
   └── Manage finances (payroll, expenses, investments)

2. EXECUTE (Working Week)
   ├── Resolve all orders (success/fail based on stats vs. NPC state)
   ├── Run combat encounters (auto-resolved)
   ├── Generate squeal events (NPCs roll against squeal values)
   ├── Update investigations (leads accumulate/decay)
   ├── Update economy (business income, expenses, market share)
   ├── Rival AI takes actions (same rules as player)
   └── Law enforcement responds (patrols, investigations, arrests)

3. REVIEW (Reports)
   ├── Order results (success, failure, casualties, arrests)
   ├── Financial summary (income, expenses, net)
   ├── Intelligence updates (new sightings, investigation status)
   ├── Notifications (tiered: green/yellow/red)
   └── Adjust strategy → back to PLAN
```

## System Priority for Prototyping

| Priority | System | Why First |
|----------|--------|-----------|
| 1 | Extortion & Territory | Core loop — the game doesn't exist without it |
| 2 | Character System | Hoods are the player's tools — need stats to resolve orders |
| 3 | Crime & Squeal | Consequences for extortion — makes the loop interesting |
| 4 | Economy | Money in/out — funds the operation |
| 5 | Combat | Encounters happen during territory disputes |
| 6 | Corruption & Police | Heat management layer |
| 7 | Intelligence | Emerges from territory — add once territory works |
| 8 | Rival AI | Need opponents — same rules as player |
| 9 | Politics | Endgame layer — add after core loop is fun |
| 10 | Procedural City | Replayability — add once systems are tuned |

## System Interactions (Key Feedback Loops)

### Loop 1: Extortion → Squeal → Investigation → Heat
```
Extort block → NPC refuses or complies
  → If crime committed → squeal roll → investigation triggered
    → Investigation builds leads → leads threaten hoods
      → Player must lie low / clean up / bribe cop
        → Costs money/time → affects expansion plans
```

### Loop 2: Territory → Intelligence → Decisions
```
Extort block → territory strength increases
  → Information tier improves → more intel visible
    → Player sees rival movements / squealer identities
      → Better strategic decisions → more effective operations
        → More territory → more intel → ...
```

### Loop 3: Economy → Payroll → Corruption
```
Business income → funds operations
  → Pay corrupt cops weekly → suppress heat in their beat
    → Safer operations → more extortion → more income
      → But payroll costs scale → need more territory to afford
        → More territory → more heat → need more cops → ...
```

### Loop 4: Crime → Escalation → Consequences
```
Extort (susp 20) → fails → Intimidate (susp 20) → fails
  → Assault (susp 40) → works but witness squeals
    → Investigation → hoods at risk
      → Kill witness (susp 100) → new investigation
        → Spiral of escalating heat
```
