# Combat System — Steel City: Mob Sim

**Created**: August 2, 2026
**Status**: 📐 In Progress

---

## Overview

Combat is auto-resolved. The player is a manager, not a fighter. When a street encounter triggers, the simulation resolves it based on hood stats, intelligence, environment, and equipment. The player's job is to assign the right hoods to the right jobs — not to micromanage fights.

---

## Original Implementation

### Hit Probability

```
Hit Chance = ((Attacker Skill + 1) / (Defender Skill + 1)) × Range Factor
```

9 weapons × 8 range bands. Range factor is a lookup table per weapon.

### Damage

```
Damage Index = Range(in locations) × 8 + rand(0-7)
```

Lookup table per weapon → 4 wound states: Winded, Lightly Wounded, Badly Wounded, Dead.

### Weapons

| Weapon | Range 1 Hit | Range 2 Hit | Effective Range |
|--------|------------|------------|-----------------|
| Shotgun | 52 | 100 | Close only |
| Rifle | 34 | 50 | Mid-range king |
| Pistol | 50 | 21 | Short |
| Tommy Gun | 12 | 10 | Multiple rounds compensate |
| Twin Pistols | 60 | 45 | Best all-rounder |

---

## Modernization

### Design Principle: Intelligence as Tactical AI

**Intelligence governs decision quality in auto-resolved combat.**

A high-INT hood in a street fight:
- Sees cover and moves to it
- Prioritizes targets (biggest threat first)
- Knows when to retreat (outnumbered, outgunned)
- Uses environment (cars, walls, dumpsters)

A low-INT hood:
- Stands in the open and shoots
- May panic and fire wildly
- Doesn't recognize when outmatched
- But might have raw Firearms skill that makes even dumb shooting effective

**The elegant part**: Intelligence already exists in the original (8-bit, 0-255). We don't add a new stat — we make an existing stat **do more**. A smart hood with mediocre gun skills may beat a dumb hood with great gun skills because the smart one took cover.

### Combat Resolution Pipeline

```
1. Encounter triggers (rival hoods in same block, raid, ambush, etc.)
2. Determine participants (hoods, weapons, numbers)
3. Assess environment (cover availability, civilians, time of day)
4. For each round of combat:
   a. Intelligence check → tactical decision (advance, take cover, retreat, fire)
   b. Skill check → hit probability (original formula)
   c. Damage roll → wound state (original lookup table)
   d. Morale check → fight, flee, surrender
5. Repeat until one side is defeated, fled, or surrendered
6. Generate combat log for player review
```

### Environment Factors

| Factor | Effect |
|--------|--------|
| Cover available | Reduces hit chance against hoods who use it (INT check) |
| Time of day (night) | Reduced visibility → lower hit chances, higher stealth effectiveness |
| Civilian presence | Collateral damage risk → increases squeal risk if civilians hurt |
| Open street | No cover → everyone is exposed → raw skill matters more |
| Indoors | Close range → shotguns devastating, rifles cramped |

### Morale

Simple check each round:
- Hood watches ally go down → morale roll (based on Loyalty + Intelligence)
- Low morale → flee or surrender
- High morale → fight harder (small accuracy bonus)
- Outnumbered 3:1 → morale penalty
- Outgunned (pistols vs tommy guns) → morale penalty

### What the Player Does

The player's role is **before and after**, not during:

**Before**: Assign the right hoods. Send the smart ones to dangerous encounters. Send the dumb but skilled ones to straightforward fights. Equip appropriately. Bring enough manpower.

**After**: Review the combat log. "Vinny took cover behind the car and picked off two rivals. Frankie stood in the open and got shot — he's badly wounded. The third rival fled." Adjust strategy: send smarter hoods next time, or bring more guys.

### Combat Log Example

```
ENCOUNTER: Baker St. — 3 hoods vs 2 rival hoods
ROUND 1:
  Vinny (INT 180) → takes cover behind car
  Frankie (INT 45) → stands in open, fires at rival A
    Hit! Rival A: Lightly Wounded
  Rival A → fires at Frankie (exposed)
    Hit! Frankie: Badly Wounded
  Rival B → fires at Vinny (behind cover)
    Miss (cover penalty)
  Sal (INT 90) → moves to doorway, fires at Rival B
    Hit! Rival B: Dead
ROUND 2:
  Frankie morale check → fails (badly wounded, outnumbered feeling) → flees
  Vinny → fires at Rival A from cover
    Hit! Rival A: Dead
RESULT: Victory. 1 rival dead, 1 rival dead, 1 fled. Frankie badly wounded.
```

---

## Data Schema

Preserve original hit/damage tables as data files:

```json
{
  "weapons": [
    {
      "name": "Pistol",
      "hit_factors": [50, 21, 14, 11, 7, 0, 0, 0],
      "damage_table": [1,1,2,2,3,3,4,4, 1,1,2,2,3,3,4,4, ...]
    }
  ],
  "environment_modifiers": {
    "cover_hit_reduction": 0.5,
    "night_visibility_reduction": 0.3,
    "civilian_squeal_bonus": 20
  }
}
```

---

## System Interactions

- **Character System**: Skills determine hit chance. Intelligence determines tactical decisions. Loyalty determines morale.
- **Crime**: Combat generates crimes (kill, assault) with associated suspicion/squeal
- **Intelligence**: Rival hood sightings (from territory radar) warn player before encounters
- **Corruption**: Corrupt cop may suppress combat-generated suspicion in their beat
- **Economy**: Wounded hoods can't work. Medical costs. Equipment costs.
