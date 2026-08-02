# Extortion & Territory — Steel City: Mob Sim

**Created**: August 2, 2026
**Status**: 📐 In Progress

---

## Overview

Extortion is the core gameplay loop. The player assigns hoods to extort blocks, collect protection money, and maintain territorial control. NPC business owners may refuse, triggering an escalation chain that generates heat, squeal risk, and emergent narratives.

---

## Original Implementation

- `extort` crime: suspicion 20, sentence 3, investigation 2, time 166, max manpower 10
- `collectprotection` order: time 166, max manpower 10, no suspicion
- `intimidate` crime: suspicion 20, sentence 1, investigation 2
- Extorted blocks count as family territory
- Market share decay: 100% at 1 business → 3% at 40 businesses

---

## Modernization

### Territory Strength (0-100)

Not binary (yours/not yours). Each extorted block has a strength value:

- **Freshly extorted**: starts at ~20 strength
- **Consistent collection visits**: +5-10 per week (up to cap)
- **Neglected (no visits)**: -5 per week, decays to 0 (lost territory)
- **Rival poaching**: if a rival gang extorts a block you hold at low strength, they can steal it

### Block Information Tiers

Derived from territory ownership and strength:

| Tier | Condition | What Player Sees |
|------|-----------|-----------------|
| Blind | No ownership, no extortion | Nothing. Black box. |
| Aware | Extorted (low strength) | Rough activity. "Something's happening on Baker St." |
| Informed | Extorted (medium strength) | Delayed rival sightings. "Two hoods passed through an hour ago." |
| Connected | Extorted (high) OR business owned | Real-time alerts. Squealer identity. |
| Networked | Business owned + high extortion + friendly NPC | Predictive intel. Raid warnings. |

### The Refusal Chain

This is the core fun. When a hood is assigned to extort:

1. **Hood arrives at block** → finds target business
2. **Compliance roll**: Hood's Intimidation skill vs. NPC's `fear - hostility`
   - If `fear > hostility`: NPC complies, pays protection money. Done.
   - If `hostility >= fear`: NPC refuses. Order fails. Player notified.
3. **Player decides response** (next week):

| Response | Suspicion | Fear Impact | Squeal Risk | Notes |
|----------|-----------|-------------|-------------|-------|
| Intimidate | 20 | Moderate | Low | Soft threat. May still refuse. |
| Assault | 40 | High | Medium | Violent. Witnesses may see. |
| Torch | 80 | Massive (block-wide) | High | Shop destroyed. No more income. Block submits. |
| Bomb | 80 | Massive | High | Same as torch but bigger message. |
| Walk away | 0 | Decays | None | Lost territory. Rivals may move in. |
| Try again | 20 | No change | Low | Might refuse again. Wasted manpower. |

4. **Escalation consequences**:
   - Intimidation raises fear but may also raise hostility (nobody likes being threatened)
   - Assault raises fear high but generates squeal risk (witnesses)
   - Torch eliminates the business but terrifies the entire block into compliance
   - Each step up the ladder = more heat, more effectiveness

5. **Squeal cascade**:
   - If a crime generates a squealer → police investigation begins
   - Player may or may not know about the squealer (depends on information tier)
   - Investigation threatens hoods → player must respond (lie low, clean up, bribe cop)

### The Ideal State

```
High Fear + Low Hostility = complies AND stays quiet
```

Achieving this requires:
- Enough intimidation to raise fear above hostility
- Not so much violence that hostility spikes
- Consistent presence (collection visits maintain fear without new violence)
- Corrupt cop coverage to suppress any squeal that does occur

### Market Share Decay

Preserve original's diminishing returns:

| Businesses | Efficiency |
|-----------|-----------|
| 1 | 100% |
| 5 | 80% |
| 10 | 79% |
| 20 | 57% |
| 27 | 50% |
| 40 | 3% |

This prevents monopolies and encourages quality over quantity. The player must decide: own 40 businesses at 3% each ($120 total) or 5 businesses at 80% each ($400 total)?

---

## Data Schema

```json
{
  "block": {
    "id": "block_baker_st",
    "district": "lower_east_side",
    "owner_gang": "gang_player",
    "extortion_strength": 45,
    "businesses": ["biz_butcher", "biz_bakery", "biz_barbers"],
    "population": 120,
    "police_presence": 2,
    "information_tier": "informed",
    "squeal_risk": 35,
    "active_investigations": [],
    "recent_crimes": []
  }
}
```

```json
{
  "extortion_order": {
    "hood_id": "hood_001",
    "block_id": "block_baker_st",
    "type": "extort",
    "target_business": "biz_butcher"
  }
}
```

---

## System Interactions

- **Character System**: Hood's Intimidation skill vs. NPC Fear/Hostility
- **Crime & Squeal**: Failed extortion → escalation → crime → squeal → investigation
- **Intelligence**: Territory strength determines information tier → what player can see
- **Corruption**: Corrupt cop in block's beat suppresses squeal-generated suspicion
- **Economy**: Protection money is income. Escalation costs (damaged businesses stop producing)
- **Combat**: If rival hoods are in the block during extortion, encounter may trigger
