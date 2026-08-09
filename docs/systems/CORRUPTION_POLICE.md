# Corruption & Police — Steel City: Mob Sim

**Created**: August 2, 2026
**Status**: 📐 In Progress

---

## Overview

Police aren't a monolithic faction. They're individual officers with geographic jurisdiction (beats). Corruption is personal and geographic — you bribe specific cops who cover specific areas. Simple mechanic, complex spatial implications.

---

## Original Implementation

- 400 police in the city (from Constants)
- Bribe prices: Police $2,000 base × $3,000 per case, Police Chief $10,000 base × $10,000
- "Bribe" as a crime type: suspicion 20, sentence 2, investigation 8
- Police are a global faction — suspicion applies globally

---

## Modernization

### Design Principle: Simple Mechanic, Complex Interactions

**Bribe cop → Pay weekly cost → They suppress heat in their beat.**

That's it. No negotiation minigame, no risk tolerance tiers, no multi-step approach pipeline. The complexity comes from the **map**, not the mechanic.

### Beat Officers

Every police officer is assigned a **beat** — a set of 4-6 blocks they patrol.

```json
{
  "officer": {
    "id": "officer_001",
    "name": "Patrolman O'Brien",
    "beat": ["block_baker_st", "block_maple_ave", "block_oak_rd", "block_pine_st"],
    "shift": "night",
    "bribe_cost": 500,
    "on_payroll": true,
    "payroll_gang": "gang_player"
  }
}
```

### What Corrupt Cops Do

**Suppression** (passive):
- Petty crimes in their beat generate less suspicion
- Extortion, intimidation, protection collection — cop looks the other way
- Effect is **geographic**, not global

**Warning** (passive):
- When investigation targets your operations in their beat, you get notified
- "Officer O'Brien reports Detective Morrison is asking questions about the Baker St. raid"
- Gives time to react before investigation arrives

### The Cost Structure

- Weekly payroll per cop
- Operating across 5 districts = 5+ cops on payroll = real money
- Stop paying → cop goes back to normal, may generate one-time suspicion spike for crimes they covered
- Rival gangs can bribe same cop (outbid) or bribe different cops in same area

### Natural Ceiling

- Can't corrupt the whole department
- Police chief, internal affairs, FBI — beyond reach (or require political connections, not cash)
- More cops on payroll = higher exposure risk = each new cop costs more
- There's always a layer of honest law enforcement that's dangerous

### What We Don't Do (Avoiding Over-Engineering)

- ~~No negotiation minigame~~ — pay the cost, it works
- ~~No risk tolerance tiers~~ — cop is either on payroll or not
- ~~No multi-step approach pipeline~~ — you see the cop on the map, you bribe them
- ~~No cop personality stats~~ — honesty is implicit in bribe_cost (expensive = honest, cheap = corrupt)

---

## Data Schema

```json
{
  "police_roster": [
    {
      "id": "officer_001",
      "name": "Patrolman O'Brien",
      "rank": "patrolman",
      "beat": ["block_baker_st", "block_maple_ave", "block_oak_rd", "block_pine_st"],
      "shift": "night",
      "bribe_cost_weekly": 500,
      "on_payroll": false,
      "payroll_gang": null
    }
  ]
}
```

---

## System Interactions

- **Extortion**: Corrupt cop in beat → extortion generates less suspicion → safer expansion
- **Crime & Squeal**: Cop suppresses squeal-generated investigations in their beat
- **Intelligence**: Corrupt cop provides department intel (investigation warnings)
- **Economy**: Payroll is significant weekly expense — competes with business investment
- **Rival AI**: Rivals have their own corruption networks — can outbid or expose
- **Territory**: Corrupt cops only help in their beat → expanding territory needs new cops → costs scale
