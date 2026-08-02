# Crime & Squeal — Steel City: Mob Sim

**Created**: August 2, 2026
**Status**: 📐 In Progress

---

## Overview

Every crime generates suspicion, may generate squealers, and triggers investigations. The escalation ladder (extort → intimidate → assault → torch → bomb) creates a constant tension between effectiveness and heat. Squeal is the mechanism that connects crime to law enforcement response.

---

## Original Implementation

30+ crime types, each with: suspicion (0-100), sentence (years), investigation difficulty, public/private risk, AI priority.

Key crime values from decoded data:

| Crime | Suspicion | Sentence | Investigation | AI Priority |
|-------|-----------|----------|---------------|-------------|
| Extort | 20 | 3 | 2 | 1 |
| Intimidate | 20 | 1 | 2 | 8 |
| Bribe | 20 | 2 | 8 | 9 |
| Raid | 40 | 2 | 8 | 1 |
| Assault | 40 | 5 | 2 | 9 |
| Kidnap | 60 | 6 | 6 | 9 |
| Smash Up | 60 | 4 | 6 | 3 |
| Torch | 80 | 7 | 6 | 3 |
| Bomb | 80 | 8 | 8 | 3 |
| Kill | 100 | 10 | 8 | 10 |
| Ambush | 100 | 10 | 8 | 9 |

Squeal values from Constants: Business Owner 125, Civilian 100, Police 200, FBI 250.

---

## Modernization

### Squeal Events

When a crime happens:
1. Identify NPCs in the block (business owners, civilians, police on patrol)
2. Each NPC rolls against their squeal value (modified by fear — terrified people talk more)
3. If any roll succeeds → squealer generated → investigation triggered
4. Player notification depends on information tier (see INTELLIGENCE_TERRITORY.md)

### Investigation System

**Leads** are the core currency of investigation:

- Crime committed → generates leads (amount based on crime's investigation value)
- Leads accumulate per investigation
- Leads decay over time (if no new crimes feed them)
- When leads reach threshold → arrest warrant issued for involved hoods
- Player can reduce leads: lie low (passive decay boost), clean up (active reduction), bribe cop (suppression)

```json
{
  "investigation": {
    "id": "invest_001",
    "block_id": "block_baker_st",
    "crimes": ["crime_raid_001", "crime_assault_002"],
    "leads": 45,
    "leads_threshold": 100,
    "target_hoods": ["hood_001", "hood_003"],
    "status": "active",
    "detective_id": "officer_045"
  }
}
```

### Investigation Visibility

Show active investigations as status on the block or district:
- "Police investigating: 2 leads" (Aware tier)
- "Detective Morrison investigating the Baker St. raid. 45/100 leads." (Connected tier)
- "Detective Morrison close to an arrest. 85/100 leads. Expect warrant within 2 weeks." (Networked tier)

### Clean Up Orders

After a crime, issue clean-up orders:
- **Remove body**: Eliminates a body (evidence). Requires hood, takes time.
- **Clean scene**: Reduces investigation leads. Requires hood with decent Stealth.
- **Intimidate witnesses**: Raises block fear, lowers future squeal willingness. Doesn't affect current investigation.

### The Escalation Ladder

The fun is in the decision: **how much heat am I willing to take?**

```
Extort (susp 20) → fails
  → Intimidate (susp 20) → fails
    → Assault (susp 40) → works, but witness squeals
      → Investigation begins
        → Kill witness (susp 100) → new investigation
          → Spiral of escalating heat
```

OR:

```
Extort (susp 20) → fails
  → Intimidate (susp 20) → works
    → Block compliant, low heat
      → Done. Clean outcome.
```

The player who escalates carefully succeeds. The player who escalates recklessly spirals.

### The Ideal Outcome

```
High Fear + Low Hostility = complies AND stays quiet
```

- Intimidation (not violence) raises fear without raising hostility much
- Consistent collection visits maintain fear
- Corrupt cop suppresses any squeal that does occur
- Result: compliant block, minimal heat, steady income

### What We Preserve

- Crime table structure (suspicion, sentence, investigation per crime)
- Squeal values per NPC type
- The escalation ladder built into the data
- `lie low`, `remove body`, `investigate` orders from original

### What We Polish

- **Make squeal visible**: Show when a squealer exists, who it is (if intel tier allows)
- **Make investigations visible**: Show lead count, threshold, target hoods, estimated time to warrant
- **Make NPC state visible**: Show fear/hostility/squeal per NPC so player can make informed decisions
- **Cascading consequences**: A raid that uncovers illegal books → tax evasion charge → audit → money laundering uncovered. One thing leads to another.

---

## Data Schema

Preserve original crime table as JSON:

```json
{
  "crimes": [
    {
      "id": "extort",
      "name": "Extort",
      "newspaper_name": "extortion",
      "order_time": 166,
      "manpower_min": 1,
      "manpower_max": 10,
      "suspicion": 20,
      "sentence": 3,
      "investigation": 2,
      "risk_public": 2,
      "risk_private": 2,
      "ai_priority": 1
    }
  ]
}
```

---

## System Interactions

- **Extortion**: Failed extortion → escalation → crime → squeal → investigation
- **Intelligence**: Squealer identification depends on information tier
- **Corruption**: Corrupt cop suppresses investigation leads in their beat
- **Character System**: Hood skills determine crime success. NPC squeal values determine consequences.
- **Combat**: Kill/assault crimes generate combat encounters + squeal risk
- **Economy**: Investigations cost money to defend against (bribes, lawyers, lost productivity)
