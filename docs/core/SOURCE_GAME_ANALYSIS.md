# Source Game Analysis — Gangsters: Organized Crime (1998)

**Created**: August 2, 2026
**Status**: ✅ Complete — All 30 .xtx files decoded and analyzed

---

## Overview

Gangsters: Organized Crime was developed by Hothouse Creations and published
in 1998 by Eidos Interactive. The game is a systems-heavy organized crime
simulation set in a fictional 1920s American city called New Temperance.

All game data is stored in 30 `.xtx` files using a custom encoding scheme.
These files have been fully decoded and analyzed as the foundation for
Steel City: Mob Sim.

---

## .xtx File Encoding

### Encoding Scheme

- **Type**: Repeating 4-byte XOR obfuscation
- **Key**: `[0xAF, 0xDE, 0xDE, 0xFA]`
- **Behavior**: Bytes ≥ 0x80 are XOR'd with the repeating key. Bytes < 0x80
  (standard ASCII, CRLF) are passed through unmodified.
- **Cracking Method**: Known-plaintext attack using tutorial title strings
  ("Tutorial 1: The Gang Organizer") to derive the key.

### Decode Scripts

- `scripts/crack_xtx.py` — Frequency analysis and known-plaintext attack
- `scripts/decode_all_xtx.py` — Batch decoder for all .xtx files
- `scripts/generate_codex.py` — Generates HTML visual codex from decoded data

### Decoded Files

All output stored in `gangsters_decoded/` (gitignored — original game's IP).

---

## Data Tables Summary

### Constants (`Constants.xtx` — 181 lines)

Global simulation parameters:

| Category | Key Values |
|----------|-----------|
| City Population | 2000 civilians, 400 police, 100 FBI, 12 judges, 12 attorneys |
| Gang Start | 5 hoods, 3 explosives, 1 business, 1 vehicle, $6,000 |
| Fear (base) | Business Owner: 100(-20), Civilian: 100(-20), Hood: 128(0), Police: 128(0) |
| Squeal | Business Owner: 125, Civilian: 100, Police: 200, FBI: 250, Judge: 200, Mayor: 250 |
| Bribe Prices | Snitch: $500×4, Attorney: $5K×3K, Judge: $10K×20K, Police: $2K×3K, Chief: $10K×10K |
| FBI Suspicion | Base threshold: $5K illegal income, Accountant skill divisor: 16, Multiplier: 2× |
| Elections | Min 100 blocks to enter, Cost divisor: 4 |
| Gang Splitting | Loyalty threshold: 192, Hostility-Fear threshold: 64 |

**Key Insight**: Fear/Hostility/Squeal are **citizen metrics**, not gang member
metrics. Once recruited, hoods operate on a different system (loyalty, orders).
This dual-system design is preserved in Steel City.

### Character Generation (`Character Generation.xtx` — 330 lines)

18 weighted archetypes for procedural hood generation:

| Archetype | Weight | Intelligence | Skill Pattern |
|-----------|--------|-------------|---------------|
| Poor Hood | 25 | 64±64 | All skills 0±40 |
| Poor Lieutenant | 25 | 64±192 | Org 32±32, rest 0±40 |
| Average Hood | 12 | 64±64 | All skills 16±32 |
| Average Lieutenant | 12 | 128±128 | Org 32±32, rest 16±32 |
| Superhood | 1 | 160±96 | All skills 40±24 |
| Recruiter/Investigator | 5 | 160±96 | Stealth 32±32, rest 0±40 |
| Business Hood | 5 | 96±64 | Bus/Org 32±32, Firearms 32±16 |
| Extortion Hood | 5 | 96±64 | Intimidation 32±32 |
| Arson Hood | 5 | 96±64 | Arson 32±32, Explosives 32±16 |
| Explosives Hood | 5 | 96±64 | Explosives 32±32, Arson 32±16 |
| Firearms Hood | 5 | 96±64 | Firearms 32±32 |
| Fighting Hood | 5 | 96±64 | Fists 32±32, Knives 32±32 |
| (+ Lt variants) | 5 each | 128±128 | Same specialty, higher INT |

**Key Insights**:
- Skills use 6-bit values (0-63). Intelligence uses 8-bit (0-255).
- Lieutenants get higher Intelligence but same skill ranges — leadership is brains.
- Weighted generation creates natural rarity tiers (Superhoods at 1%).
- Specialists are pre-optimized for their role (Arson Hood has high Arson, zero Business).

### Crime Table (`Crime.xtx` — 65 lines)

30+ crime/order types, each with:

| Field | Description |
|-------|-------------|
| Order Time | When the order can be executed (12000 = all week, 166 = limited) |
| Manpower | Min/max hoods assignable |
| Suspicion | 0-100, how much heat it generates |
| Sentence | Years in prison if convicted |
| Investigation | Investigation difficulty (higher = harder to solve) |
| Risk (Public/Private) | Visibility risk |
| AI Priority | How much the AI values this action (1-10) |

**Notable Crimes**:

| Crime | Suspicion | Sentence | AI Priority |
|-------|-----------|----------|-------------|
| Extort | 20 | 3 | 1 |
| Intimidate | 20 | 1 | 8 |
| Raid | 40 | 2 | 1 |
| Assault | 40 | 5 | 9 |
| Torch (Arson) | 80 | 7 | 3 |
| Bomb | 80 | 8 | 3 |
| Kill | 100 | 10 | 10 |
| Ambush | 100 | 10 | 9 |
| Bribe | 20 | 2 | 9 |
| Evade Tax | 10 | 10 | 10 |

**Key Insight**: The escalation ladder is built into the data. Each step up
is more effective but more dangerous. The player constantly weighs heat vs. results.

### Hit Table (`Hit Table.xtx` — 40 lines)

9 weapons × 8 range bands. Hit probability formula:

```
Hit Chance = ((Attacker Skill + 1) / (Defender Skill + 1)) × Range Factor
```

| Weapon | Range 1 | Range 2 | Range 3 | Range 4 | Range 5-8 |
|--------|---------|---------|---------|---------|-----------|
| Pistol | 50 | 21 | 14 | 11 | 7→0 |
| Tommy Gun | 12 | 10 | 9 | 8 | 6→0 |
| Rifle | 34 | 50 | 50 | 50 | 24→0 |
| Shotgun | 52 | 100 | 52 | 24 | 20→0 |
| Twin Pistols | 60 | 45 | 30 | 26 | 22→0 |

**Key Insight**: Shotguns devastating at close range (100% at range 2). Rifles
maintain effectiveness at mid-range. Tommy Gun has lowest per-shot hit but
presumably fires multiple rounds. The formula is elegant — skill ratio × range.

### Damage Table (`Damage Table.xtx` — 37 lines)

Damage resolution: `Range(in locations) × 8 + rand(0-7)` → lookup table.
4 wound states: 1=Winded, 2=Lightly Wounded, 3=Badly Wounded, 4=Dead.

### Economics (`Economics.xtx` — 269 lines)

171 legal businesses across 8 groups:
- Commercial (117 types), Industrial, Residential, Warehouse, Charity,
  Municipal, Interactive Residential, Empty Land

Each business has: profit group, running cost group, city capacity, land value
range, setup time, protection value, contents, produce type, union type,
building reference, setup cost, police guard/patrol ranges, FBI guard, capacity.

### Illegal Economics (`Illegal Economics.xtx` — 26 lines)

14 illegal business types:

| Business | Profit Group | Setup Cost | Capacity |
|----------|-------------|------------|----------|
| Card Game | 36 | $100 | 3 |
| Casino | 40 | $1,100 | 6 |
| Counterfeit Press | 34 | $1,000 | 12 |
| Gambling Den | 37 | $200 | 3 |
| Loan Shark | 38 | $500 | 3 |
| Moonshine Still | 34 | $750 | 12 |
| Speakeasy | 39 | $650 | 20 |
| Whorehouse | 38 | $650 | 3 |

**Key Insight**: Illegal businesses cannot be protected (no police guard).
Setup costs range from $100 to $1,100. Speakeasy has highest capacity (20).

### Business Suspicion Matrix (`Business Suspicion.xtx` — 155 lines)

Cross-reference: which legal businesses attract suspicion for which illegal
activities. Values: 0=None, 1=Some, 2=High.

**Key Insight**: Cab Companies and Department Stores are the most suspicious
(nearly all activities flagged). This creates emergent gameplay — don't run
a casino near a Cab Company.

### Market Share (`Market Share.xtx` — 49 lines)

Diminishing returns curve:

| Businesses Owned | Efficiency Factor |
|-----------------|-------------------|
| 1 | 100% |
| 5 | 80% |
| 10 | 79% |
| 20 | 57% |
| 27 | 50% |
| 40 | 3% |

**Key Insight**: Linear decay prevents monopolies. At 40 businesses, each
generates only 3% of base profit. Encourages quality over quantity.

### Scenarios (`Scenario.xtx` — 60 lines)

10 single-player scenarios, each with narrative description and .dat file reference.

### Other Files

- `Hoods.xtx` (1525 lines) — 40 predefined named hoods with full stat blocks
- `Names/Team Names.xtx` — AI gang names by specialty
- `Empty Land Cost.xtx` — Linear cost scaling ($40-$640 by land value 0-15)
- `Income Groups.xtx` — 41 income groups × 16 land value tiers
- `RunningCosts.xtx` — 8 cost groups × 16 land value tiers
- `ProfitTableFactors.xtx` — 4 profit tiers × 16 land value levels
- `Cart.xtx` (466 lines) — Combat animation/reaction data (16 attack types)
- `Tutorial.xtx` — Tutorial scenario data

---

## Architecture Observations

### What the Original Does Well

1. **Data-driven design** — All balance in external text files. Brilliant for 1998.
2. **Weighted random generation** — Creates natural rarity tiers without scripting.
3. **Dual NPC/gang system** — Citizens use Fear/Hostility/Squeal. Gang members use skills/loyalty. Two separate systems for two separate populations.
4. **Crime escalation ladder** — Built into the data. Each step more effective but more dangerous.
5. **Market share decay** — Elegant anti-snowball mechanic.
6. **Suspicion matrix** — 2D cross-reference creates emergent placement decisions.

### What the Original Does Poorly

1. **Opacity** — You can't see why a business owner refused. You can't see how close police are to an arrest. The simulation is a black box.
2. **System isolation** — Crimes don't cascade. A raid doesn't uncover tax evasion. Each crime is an isolated event.
3. **No relationships** — Hoods are interchangeable stat blocks. No trust, rivalry, or mentorship.
4. **Static city** — Same map every game. No procedural generation.
5. **Notification spam** — Business owner sightings flood the player with low-value alerts.
6. **No intelligence layer** — You can see everything on the map. No fog of war.
7. **Monolithic police** — "The police" as a faction, not individual officers with jurisdiction.

### What Steel City Preserves vs. Polishes

| Original Feature | Steel City Approach |
|-----------------|---------------------|
| Data-driven .xtx files | Data-driven JSON/TOML files |
| 18 weighted archetypes | Preserve, add personality depth |
| Crime table | Preserve structure, add cascading consequences |
| Hit/damage tables | Preserve formulas, add environment factors |
| Fear/Hostility/Squeal | Preserve, make visible to player |
| Business economics | Preserve, add supply/demand dynamics |
| Market share decay | Preserve, consider exponential variant |
| Suspicion matrix | Preserve, expand to continuous values |
| Static city | Replace with procedural generation |
| Monolithic police | Replace with individual beat officers |
| No relationships | Add hood relationship web |
| No fog of war | Add territory-based intelligence |
| Notification spam | Aggregate and tier notifications |
