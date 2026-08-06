# Intelligence System — Steel City: Mob Sim

**Created**: August 2, 2026
**Updated**: August 6, 2026
**Status**: 📐 In Progress (refined with playtesting insights)

---

## Overview

Intelligence isn't a separate module — it's a **property of territory**. What you own determines what you know. A fresh gang with no territory is blind. An established org has eyes everywhere. This creates a natural progression curve where expanding territory simultaneously expands revenue AND visibility.

---

## Core Principle

**What you own is what you know.**

---

## Block Information Tiers

Derived from territory ownership and extortion strength:

| Tier | Condition | What Player Sees |
|------|-----------|-----------------|
| **Blind** | No ownership, no extortion | Nothing. Block is a black box. |
| **Aware** | Extorted (low strength 1-33) | Rough activity level. "Something's happening on Baker St." Vague, delayed. |
| **Informed** | Extorted (medium strength 34-66) | Rival hood sightings with delay. "Two hoods passed through an hour ago." |
| **Connected** | Extorted (high strength 67+) OR business owned | Real-time rival movement alerts. Squealer identification. "The shopkeeper saw everything — it was the guy in apartment 3C." |
| **Networked** | Business owned + high extortion + friendly NPC | Predictive intel. "Police have been asking questions — expect a raid next week." |

The map's fog of war lifts organically as you consolidate control.

---

## Squealer Identification Pipeline

When a crime generates a squealer:

1. **Crime committed** → NPCs in the block roll against squeal value
2. **Squealer exists** → Investigation triggered (police start building leads)
3. **Player knowledge depends on information tier**:

| Tier | Notification | Identity? |
|------|-------------|-----------|
| Blind | None. Find out when police arrive. | No |
| Aware | "Police are asking questions on Baker St." | No |
| Informed | "Someone talked to the cops about the raid." | No, but you know it happened |
| Connected | "The baker on Baker St. squealed. He's in apartment 3C." | Yes |
| Networked | "The baker is talking to police. Detective coming tomorrow." | Yes + time window |

### Information Infrastructure Requirements (Playtesting Insight)

The original game gates squealer identification behind **recruiting a Lawyer**:

| Infrastructure | What Player Sees |
|----------------|-----------------|
| No Lawyer | Blind to squealers. Only indirect signals: police activity, arrest risk spikes, Bomb order target highlighting (by elimination) |
| Lawyer recruited | Squealers report — direct list with map locations. Also: Crimes report, Legal Proceedings, Elections |
| DA bribed (active trial only) | Witness/juror lists for trial defense (NOT squealer identification) |

**Conditional reports**: The Squealers report only appears when someone has actually squealed — same pattern as Elections (only during elections) and Legal Proceedings (only when hoods arrested). No squealers = no report button.

**Indirect detection without Lawyer**:
- Bomb order target highlighting includes squealer-occupied buildings (by elimination — not labeled as squealers)
- Clipboard → Business Owners has a squealer sub-filter (no map highlight)
- Sudden "risk of arrest" spike on Crimes report → someone talked about that crime
- Increased police patrol in an area → someone squealed there

The game gives you **consequences before causes**. You see the police response and work backward. This is intentional information asymmetry, not a UI limitation.

4. **Player response options** (if identity known):
   - **Intimidate the block** — raise fear, lower future squeal. Doesn't stop current investigation.
   - **Intimidate the squealer** — direct pressure to recant. Risk: witness intimidation is itself a crime.
   - **Bribe the squealer** — pay them off. Cleanest but expensive. Not always available.
   - **Silence the squealer** — kill/kidnap. Eliminates witness but generates NEW crime with its own squeal risk.
   - **Clean up evidence** — reduces investigation leads. Buys time.
   - **Lie low** — pull hoods out. Reduces further leads. Lets existing leads decay.

---

## Business Radar (Refined from Original)

The original had business owners reporting enemy hood sightings — great idea, ruined by notification spam.

### Notification Quality over Quantity

- Don't spam "Hood spotted on Baker St." for every movement
- Aggregate: "3 unknown hoods entered your territory on Baker St. in the last hour. They left after 20 minutes."
- Only ping for **anomalous activity**: rival hoods entering territory, unusual police concentration, squeal risk spiking

### Alert Tiers

| Tier | Color | Example |
|------|-------|---------|
| Green | Normal | No alerts. Visible in block panel if player looks. |
| Yellow | Noteworthy | "Rival hoods spotted near your business on Baker St." |
| Red | Threat | "Police raid imminent on Baker St." or "Rival gang extorting your block on Maple Ave." |

### Radar is Territory-Dependent

- Business owned → real-time alerts for that block and adjacent blocks
- Extorted block → delayed alerts for that block only
- Uncontrolled blocks → nothing
- **Territory density matters**: scattered empire has blind spots. Consolidated territory has comprehensive coverage.

---

## Friendly NPCs as Informants

Individual NPCs can become personal informants:

- A business owner treated well (fair prices, protection from rivals) volunteers information
- A cop on payroll feeds investigation status
- A civilian you helped becomes a loyal neighborhood source

These are named individuals with reliability and risk of exposure. If a rival gang identifies your informant, they can turn or silence them.

---

## Spy & Investigate Orders (Infrastructure-Dependent)

The original's `spy` and `investigate` orders become **territory-dependent**:

- **Spy on rival**: Requires Connected or Networked tier in target block. Can't spy where you have no presence — must first establish a foothold.
- **Investigate**: Requires at least Aware tier. Asking your local network what they know. Blind investigation (no local presence) possible but expensive — send a hood to physically scout, takes time, carries risk.

Rival gangs have the same limitations — they can't see into your territory unless they've established presence. Both sides play the same fog-of-war game.

---

## Data Schema

Intelligence is a **view layer** on top of territory — no separate data structure needed:

```json
{
  "block": {
    "id": "block_baker_st",
    "extortion_strength": 45,
    "businesses_owned_by_player": 1,
    "friendly_npcs": ["npc_butcher_001"],
    "information_tier": "connected"  // derived from above fields
  }
}
```

```json
{
  "squealer_event": {
    "id": "squeal_001",
    "crime_id": "crime_raid_001",
    "block_id": "block_baker_st",
    "npc_id": "npc_baker_001",
    "discovered_by_player": true,  // based on information tier
    "investigation_id": "invest_001"
  }
}
```

---

## System Interactions

- **Extortion & Territory**: Territory strength determines information tier
- **Crime & Squeal**: Squealer identification depends on information tier
- **Corruption**: Corrupt cops provide department intel (a type of informant)
- **Combat**: Rival hood sightings come from territory-based radar
- **Economy**: Businesses you own provide better intel than extorted blocks
