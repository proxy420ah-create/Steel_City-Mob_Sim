# Character System — Steel City: Mob Sim

**Created**: August 2, 2026
**Status**: 📐 In Progress

---

## Overview

Two populations with separate stat systems:
- **Citizens** (NPCs in the world): Fear, Hostility, Squeal — determines how the population reacts to your operations
- **Gang Members** (your hoods): Skills, Intelligence, Loyalty — determines how effectively your organization operates

---

## Gang Members (Hoods)

### Original Implementation

- 18 weighted archetypes (Poor Hood 25%, Superhood 1%, etc.)
- 10 skills at 6-bit (0-63): Organisation, Business, Firearms, Fists, Knives, Arson, Explosives, Intimidation, Driving, Stealth
- Intelligence at 8-bit (0-255)
- Lieutenants get higher Intelligence (128-192) but same skill ranges
- Appearance indices (head, hair, eyes, nose, mouth), ethnic group, name indices
- Static once generated — no growth or change

### Modernization

**Preserve**:
- 6-bit skill range (0-63) — compact, sufficient granularity
- 8-bit Intelligence (0-255) — meaningful distinction between hoods and lieutenants
- Weighted archetype generation — natural rarity tiers
- 10 skills — the original set covers all gameplay situations

**Polish**:
- **Skill growth**: Use-based progression. A hood who runs arsons gets better at arson. Skills decay if unused for long periods.
- **Intelligence as tactical AI**: In combat and encounters, Intelligence governs decision quality (take cover, retreat, prioritize targets). High-INT hood with mediocre gun skills may beat low-INT hood with great gun skills.
- **Loyalty stat**: Replaces the original's gang splitting threshold (loyalty < 192). Loyalty is per-hood, modified by treatment, success, pay, and events.
- **Relationships**: Hoods have interpersonal relationships (trust, rivalry, mentorship). Promoting one over their rival creates tension. A mentor's betrayal hits harder.

**Don't add** (over-engineering):
- No personality minigames
- No dialogue systems
- No complex psychological modeling
- No skill trees or perk systems

### Data Schema

```json
{
  "archetypes": [
    {
      "name": "Poor Hood",
      "weight": 25,
      "intelligence": {"base": 64, "range": 64},
      "skills": {
        "organisation": {"base": 0, "range": 40},
        "business": {"base": 0, "range": 40},
        "firearms": {"base": 0, "range": 40},
        "fists": {"base": 0, "range": 40},
        "knives": {"base": 0, "range": 40},
        "arson": {"base": 0, "range": 40},
        "explosives": {"base": 0, "range": 40},
        "intimidation": {"base": 0, "range": 40},
        "driving": {"base": 0, "range": 40},
        "stealth": {"base": 0, "range": 40}
      }
    }
  ]
}
```

### Hood Instance (Runtime)

```json
{
  "id": "hood_001",
  "name": "Vinny \"Knuckles\" Moretti",
  "intelligence": 128,
  "skills": {
    "organisation": 32,
    "business": 16,
    "firearms": 45,
    "fists": 28,
    "knives": 12,
    "arson": 5,
    "explosives": 8,
    "intimidation": 40,
    "driving": 22,
    "stealth": 30
  },
  "loyalty": 180,
  "health": "healthy",
  "status": "available",
  "assigned_order": null,
  "relationships": {
    "hood_002": {"trust": 75, "type": "mentor"},
    "hood_005": {"trust": 20, "type": "rival"}
  }
}
```

---

## Citizens (NPCs)

### Original Implementation

Fear, Hostility, and Squeal are **citizen metrics** — they apply to business owners, civilians, police, FBI, judges, and mayors. Not to recruited gang members.

| NPC Type | Fear (base) | Squeal |
|----------|------------|--------|
| Business Owner | 100 (-20) | 125 |
| Civilian | 100 (-20) | 100 |
| Hood | 128 (0) | 100 |
| Police | 128 (0) | 200 |
| Police Chief | 128 (0) | 250 |
| FBI | 128 (0) | 250 |
| Judge | 128 (0) | 200 |
| Mayor | 128 (0) | 250 |

### Modernization

**Preserve**: The triad of Fear, Hostility, Squeal as the core NPC personality system.

**Polish**:
- **Make state visible**: When a hood reports a refusal, show *why*: "The butcher on Baker St. refused. He's not scared of us (fear: 30) and he's stubborn (hostility: 80)."
- **Show consequences**: After an assault: "The butcher is now paying (fear: 120). But a witness was spotted (squeal risk: high)."
- **NPC memory**: State persists. A butcher you terrorized remembers. If you lose territory and return later, his hostility is still high.

### NPC Instance (Runtime)

```json
{
  "id": "npc_butcher_001",
  "name": "Tony the Butcher",
  "type": "business_owner",
  "block_id": "block_baker_st",
  "business_id": "biz_butcher_baker",
  "fear": 30,
  "hostility": 80,
  "squeal": 125,
  "compliance": false,
  "alive": true,
  "relationships": {
    "gang_player": {"trust": 10, "history": ["refused_extortion", "intimidated"]}
  }
}
```

### Compliance Logic

```
if fear > hostility:
    compliant = true
    squeal_modified = squeal * (fear / 255)  # terrified people still talk
else:
    compliant = false
    squeal_modified = squeal * 0.5  # brave people talk less when unafraid
```

**Key Insight**: Fear > Hostility = compliance. But high fear also increases squeal — terrified witnesses talk to police. The ideal state is high fear + low hostility (complies AND stays quiet). Achieving this requires careful escalation — too much violence raises both fear AND hostility.

---

## System Interactions

- **Extortion**: Hood's Intimidation skill vs. NPC's Fear/Hostility determines success
- **Crime**: Crimes committed near NPCs trigger squeal rolls
- **Combat**: Hood skills and Intelligence determine auto-battle outcomes
- **Intelligence**: NPC relationships (trust level) determine information flow
- **Corruption**: Police NPCs can be bribed (simple mechanic, geographic coverage)
