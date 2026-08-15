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

### Design Principle: Firearm Combat Has Diverged

The original resolved all combat as a **single instant dice roll** — plug skills + range into the formula, get hit/miss, lookup damage, done. Steel City **diverges** for firearm combat by wrapping the original formula inside a **round-based tactical simulation with physical projectiles**.

The formula is preserved as the base skill check, but the resolution method is fundamentally different:
- **Cone of fire** replaces binary hit/miss — bullet direction = aim + perturbation, skill tightens cone, movement widens it
- **Physical projectiles** travel through space, can be blocked by cover geometry, can hit stray targets
- **Cover** is a physical ray intersection against `VoxelCollisionWorld`, not a stat modifier
- **Era-appropriate inaccuracy** — 1920s firearms are inherently inaccurate, making firefights messy and collateral-heavy

**Melee/CQB combat** is under review — may preserve the original instant resolution with added INT/environment tactical layer (hybrid approach).

### Design Principle: Intelligence as Adaptive Cover AI

**Intelligence governs tactical decision quality in combat, especially cover utilization.**

A high-INT hood in a street fight:
- Seeks cover proactively, crouches for better coverage ratio
- Aligns body to minimize exposed surface area from known threat angles
- Repositions when cover is flanked
- Prioritizes targets (biggest threat first)
- Knows when to retreat (outnumbered, outgunned)

A low-INT hood:
- Stands in the open and shoots
- May panic and fire wildly
- Doesn't recognize when outmatched
- But might have raw Firearms skill that makes even dumb shooting effective

**The elegant part**: Intelligence already exists in the original (8-bit, 0-255). We don't add a new stat — we make an existing stat **do more**. A smart hood with mediocre gun skills may beat a dumb hood with great gun skills because the smart one took cover and maximized their coverage ratio.

### Cone of Fire Model

Instead of binary hit/miss, each shot spawns a physical projectile with direction = aim direction + random perturbation:

```
Projectile direction = aim_direction + RandomWithinCone(half_angle)
half_angle = weapon_base_inaccuracy - (skill_factor × skill_reduction) + (movement_penalty)
```

- **Skill tightens the cone** — a skilled shooter's bullets go closer to where they aimed
- **Movement widens the cone** — firing while running is wild
- **Weapon base inaccuracy** — era-appropriate values (see table below)

The bullet goes *somewhere* — just maybe not where aimed. Missed shots naturally become stray bullets that hit civilians, buildings, other hoods. **No separate "miss" calculation needed** — the projectile simply misses the target and continues until it hits *something*.

#### Era-Appropriate Weapon Inaccuracy (1920s)

| Weapon | Base Inaccuracy | Notes |
|--------|----------------|-------|
| Revolver | High | Short barrel, heavy trigger, no real sights → wild past 15 yards |
| Colt 1911 | Medium | Better design but still not precision by modern standards |
| Tommy Gun | High (full auto) | High fire rate + heavy recoil = area saturation, not marksmanship. Cone widens with sustained fire |
| Shotgun | Spread pattern | Devastating close range, spread naturally creates cone, useless at distance |
| Rifle | Low | Most accurate of the era — but slow rate of fire |

**Design driver**: 1920s firearms were genuinely terrible for accuracy. This makes firefights inherently messy and collateral-heavy — exactly the gangster-induced mayhem we want.

### Cover as Physical Blocker

Cover is **not a stat modifier** — it is a **physical ray intersection** against `VoxelCollisionWorld`. A projectile's trajectory is checked against solid voxels along its path. If cover geometry intersects the bullet's ray, the bullet hits the cover, not the target.

- **Full wall**: Bullet stopped completely — target unreachable from that angle
- **Half wall**: Bullet stopped if trajectory intersects — crouching hood has smaller exposed profile
- **Penetration**: Material-dependent — brick stops all, wood may let some through, car door stops pistol but not rifle
- **Coverage ratio**: Percentage of hood's bounding volume occluded by cover from attacker's angle. High-INT hoods actively maximize this

**Key principle**: Cover effectiveness is **emergent from geometry**, not a dice roll. A hood behind a full wall is simply unreachable. A hood behind a half-wall is reachable from certain angles.

### Combat Resolution Pipeline (Firearms)

```
1. Encounter triggers (rival hoods in same block, raid, ambush, etc.)
2. Determine participants (hoods, weapons, numbers)
3. For each round of combat:
   a. Intelligence check → tactical decision (advance, take cover, retreat, fire)
      - High INT: seek cover, maximize coverage ratio, align body, prioritize threats
      - Low INT: stand in open, fire wildly
   b. Fire action → spawn physical projectile(s) with cone of fire
      - Projectile direction = aim + RandomWithinCone(half_angle)
      - half_angle = weapon_base_inaccuracy - (skill × reduction) + (movement_penalty)
   c. Projectile travel → spatial hash query at projectile position each frame
      - Check instance hits (hoods, cops, civilians)
      - Check cover geometry via VoxelCollisionWorld ray intersection
      - Stray bullets continue until they hit something or TTL expires
   d. On hit → damage roll → wound state (original lookup table)
   e. Nerve check → fight, flee, surrender
5. Repeat until one side is defeated, fled, or surrendered
6. Generate combat log for player review
```

### Combat Resolution Pipeline (Melee — UNDER REVIEW)

```
1. Encounter triggers (rival hoods in same block, bar fight, etc.)
2. Determine participants (hoods, weapons, numbers)
3. For each exchange:
   a. Intelligence check → tactical decision (close distance, flank, multiple-attacker positioning)
   b. Spatial hash distance check → is target in melee range?
   c. Skill check → hit probability (original formula, preserved)
   d. Damage roll → wound state (original lookup table)
   e. Nerve check → fight, flee, surrender
4. Repeat until one side is defeated, fled, or surrendered
5. Generate combat log for player review
```

**Melee design note**: Hybrid approach proposed — preserve original formula + add INT/environment tactical layer, but keep instant resolution per exchange. No projectiles, no travel time, no stray punches. Environment (tight alley, multiple attackers) modifies effectiveness through INT-governed positioning, not stat modifiers.

### Environment Factors — DEFERRED

Stat-based environment modifiers are deferred to a future pass. Cover (geometry-based) stays in scope as a physical projectile interaction.

| Factor | Effect | Status |
|--------|--------|--------|
| Cover (geometry) | Physical ray intersection blocks projectiles — not a stat modifier | 📝 In scope |
| Time of day (night) | Reduced visibility → lower hit chances, higher stealth | ⏸️ Deferred |
| Civilian presence | Collateral damage risk → increases squeal risk if civilians hurt | ⏸️ Deferred (collateral still happens via stray projectiles) |
| Open street | No cover → everyone is exposed → raw skill matters more | ⏸️ Deferred (emergent from cover geometry) |
| Indoors | Close range → shotguns devastating, rifles cramped | ⏸️ Deferred |

### Nerve as Morale Stat

The original game has **Nerve** as a hood stat but did not use it for combat morale. Steel City repurposes Nerve as the **will-to-fight** stat:

- Hood watches ally go down → Nerve check (modified by Loyalty)
- Low Nerve → panic, freeze, or flee
- High Nerve → cool under fire, small accuracy bonus, fights harder
- Outnumbered 3:1 → Nerve penalty
- Outgunned (pistols vs tommy guns) → Nerve penalty
- Low Nerve + high Loyalty → may still hold position for the boss
- Low Nerve + low Loyalty → breaks immediately when pressured

**Stat reuse**: Nerve already exists in original hood data. No new stat invented.

### What the Player Does

The player's role is **before and after**, not during:

**Before**: Assign the right hoods. Send the smart ones to dangerous encounters. Send the dumb but skilled ones to straightforward fights. Equip appropriately. Bring enough manpower.

**After**: Review the combat log. "Vinny took cover behind the car and picked off two rivals. Frankie stood in the open and got shot — he's badly wounded. The third rival fled." Adjust strategy: send smarter hoods next time, or bring more guys.

### Combat Log Example

```
ENCOUNTER: Baker St. — 3 hoods vs 2 rival hoods
ROUND 1:
  Vinny (INT 180) → takes cover behind car, crouches (coverage ratio 0.85)
  Frankie (INT 45) → stands in open, fires at rival A
    Cone of fire (Colt 1911, medium inaccuracy) → projectile spawns
    Hit! Rival A: Lightly Wounded
  Rival A → fires at Frankie (exposed, no cover)
    Hit! Frankie: Badly Wounded
  Rival B → fires at Vinny (behind car cover)
    Projectile trajectory intersects car voxel → blocked, bullet stopped
  Sal (INT 90) → moves to doorway, fires at Rival B
    Cone of fire → projectile spawns, slight perturbation
    Hit! Rival B: Dead
  Stray bullet from Frankie's burst → hits storefront window (no casualty)
ROUND 2:
  Frankie Nerve check → fails (badly wounded, low Nerve) → flees
  Vinny → fires at Rival A from cover (peeking, coverage ratio 0.70)
    Cone of fire → tight (high skill) → projectile on target
    Hit! Rival A: Dead
RESULT: Victory. 2 rivals dead, 1 fled. Frankie badly wounded. 1 storefront damaged.
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
      "damage_table": [1,1,2,2,3,3,4,4, 1,1,2,2,3,3,4,4, ...],
      "base_inaccuracy": 8.0,
      "penetration": {
        "brick": 0,
        "wood": 0.3,
        "metal_thin": 0.1,
        "glass": 0.9
      }
    }
  ]
}
```

**Note**: Environment modifiers (night, civilian squeal, indoor) are deferred. Cover is handled physically via ray intersection, not as a data modifier.

---

## System Interactions

- **Character System**: Skills determine cone of fire tightness. Intelligence determines tactical decisions (cover-seeking, coverage ratio maximization). Nerve determines morale/will-to-fight. Loyalty modifies Nerve checks.
- **Crime**: Combat generates crimes (kill, assault) with associated suspicion/squeal
- **Intelligence**: Rival hood sightings (from territory radar) warn player before encounters
- **Corruption**: Corrupt cop may suppress combat-generated suspicion in their beat
- **Economy**: Wounded hoods can't work. Medical costs. Equipment costs.
