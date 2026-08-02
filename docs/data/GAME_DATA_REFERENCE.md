# Game Data Reference — Extracted from Original Game

**Created**: August 2, 2026
**Status**: ✅ Complete (extracted from decoded .xtx files)

---

## Overview

All values extracted from Gangsters: Organized Crime decoded .xtx files.
These serve as the baseline for Steel City's data files. Values may be
adjusted during playtesting.

Source files: `gangsters_decoded/Data/*.txt`
Visual codex: `gangsters_decoded/index.html`

---

## 1. Constants

### City Population
| Parameter | Value |
|-----------|-------|
| Civilians | 2000 |
| Police | 400 |
| FBI | 100 |
| Judges | 12 |
| Attorneys | 12 |

### Gang Start
| Parameter | Value |
|-----------|-------|
| Starting Hoods | 5 |
| Starting Explosives | 3 |
| Starting Businesses | 1 |
| Starting Vehicles | 1 |
| Starting Money | $6,000 |

### Fear (base values by NPC type)
| NPC Type | Base Fear | Modifier |
|----------|----------|----------|
| Business Owner | 100 | -20 |
| Civilian | 100 | -20 |
| Hood | 128 | 0 |
| Police | 128 | 0 |
| FBI | 128 | 0 |
| Judge | 128 | 0 |
| Mayor | 128 | 0 |

### Squeal (likelihood to inform)
| NPC Type | Squeal Value |
|----------|-------------|
| Business Owner | 125 |
| Civilian | 100 |
| Hood | 100 |
| Police | 200 |
| Police Chief | 250 |
| FBI | 250 |
| Judge | 200 |
| Mayor | 250 |

### Bribe Prices
| Target | Base Cost | Multiplier |
|--------|----------|------------|
| Business Owner (case) | $500 | ×$500 |
| Snitch | $500 | ×$4 |
| Attorney (case) | $5,000 | ×$3,000 |
| Judge (case) | $10,000 | ×$20,000 |
| Mayor | $10,000 | ×$10,000 |
| Police (case) | $2,000 | ×$3,000 |
| Police Chief | $10,000 | ×$10,000 |

### FBI Suspicion
| Parameter | Value |
|-----------|-------|
| Base Illegal Income Threshold | $5,000 |
| Accountant Skill Divisor | 16 |
| Suspicion Multiplier | 2× |

### Elections
| Parameter | Value |
|-----------|-------|
| Min Blocks to Enter | 100 |
| Cost Divisor | 4 |

### Gang Splitting
| Parameter | Value |
|-----------|-------|
| Loyalty Threshold | 192 |
| Hostility-Fear Threshold | 64 |

---

## 2. Character Generation — 18 Archetypes

| Archetype | Weight | INT Base | INT Range |
|-----------|--------|----------|-----------|
| Poor Hood | 25 | 64 | 64 |
| Poor Lieutenant | 25 | 64 | 192 |
| Average Hood | 12 | 64 | 64 |
| Average Lieutenant | 12 | 128 | 128 |
| Superhood | 1 | 160 | 96 |
| Recruiter/Investigator | 5 | 160 | 96 |
| Business Hood | 5 | 96 | 64 |
| Business Lieutenant | 5 | 128 | 128 |
| Extortion Hood | 5 | 96 | 64 |
| Extortion Lieutenant | 5 | 128 | 128 |
| Arson Hood | 5 | 96 | 64 |
| Arson Lieutenant | 5 | 128 | 128 |
| Explosives Hood | 5 | 96 | 64 |
| Explosives Lieutenant | 5 | 128 | 128 |
| Firearms Hood | 5 | 96 | 64 |
| Firearms Lieutenant | 5 | 128 | 128 |
| Fighting Hood | 5 | 96 | 64 |
| Fighting Lieutenant | 5 | 128 | 128 |

Skills (all 10, 6-bit range 0-63):
Organisation, Business, Firearms, Fists, Knives, Arson, Explosives, Intimidation, Driving, Stealth

---

## 3. Crime Table

| Crime | Time | Max Men | Suspicion | Sentence | Investigation | Risk Pub | Risk Priv | AI Pri |
|-------|------|---------|-----------|----------|---------------|---------|-----------|--------|
| Goto Order | 12000 | 4 | 0 | 0 | 0 | 0 | 0 | 10 |
| Guard Business | 12000 | 4 | 0 | 0 | 0 | 0 | 0 | 4 |
| Collect Protection | 166 | 10 | 0 | 0 | 0 | 0 | 0 | 0 |
| Explore | 83 | 10 | 0 | 0 | 0 | 0 | 0 | 6 |
| Recruit | 166 | 10 | 0 | 0 | 0 | 0 | 0 | 7 |
| Bribe | 500 | 4 | 20 | 2 | 8 | 0 | 0 | 9 |
| Extort | 166 | 10 | 20 | 3 | 2 | 2 | 2 | 1 |
| Intimidate | 333 | 10 | 20 | 1 | 2 | 2 | 2 | 8 |
| Kidnap | 6000 | 4 | 60 | 6 | 6 | 2 | 2 | 9 |
| Raid | 500 | 4 | 40 | 2 | 8 | 4 | 4 | 1 |
| Ambush | 12000 | 4 | 100 | 10 | 8 | 8 | 4 | 9 |
| Assault | 6000 | 4 | 40 | 5 | 2 | 6 | 4 | 9 |
| Bomb | 333 | 4 | 80 | 8 | 8 | 8 | 6 | 3 |
| Kill | 6000 | 4 | 100 | 10 | 8 | 8 | 4 | 10 |
| Smash Up | 333 | 4 | 60 | 4 | 6 | 6 | 4 | 3 |
| Torch | 333 | 4 | 80 | 7 | 6 | 8 | 4 | 3 |
| Evade Tax | 12000 | 1 | 10 | 10 | 10 | 0 | 0 | 10 |
| Bribe Officials | 12000 | 1 | 20 | 8 | 10 | 0 | 0 | 10 |

Illegal business crimes (passive, suspicion 20):

| Business | Sentence | Investigation |
|----------|----------|---------------|
| Card Game | 0 | 0 |
| Casino | 10 | 15 |
| Counterfeit | 15 | 15 |
| Loan Shark | 2 | 10 |
| Prostitution | 4 | 5 |
| Speakeasy | 5 | 10 |
| Teamsters | 10 | 15 |
| Laundering | 5 | 4 |
| Fencing | 5 | 4 |

---

## 4. Hit Table (9 weapons × 8 ranges)

| Weapon | R1 | R2 | R3 | R4 | R5 | R6 | R7 | R8 |
|--------|-----|-----|-----|-----|-----|-----|-----|-----|
| Pistol | 50 | 21 | 14 | 11 | 7 | 0 | 0 | 0 |
| Tommy Gun | 12 | 10 | 9 | 8 | 6 | 5 | 4 | 0 |
| Rifle | 34 | 50 | 50 | 50 | 24 | 20 | 17 | 0 |
| Shotgun | 52 | 100 | 52 | 24 | 20 | 0 | 0 | 0 |
| Fist | 52 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Bat/Crowbar | 52 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Knife | 52 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Kick | 105 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Twin Pistols | 60 | 45 | 30 | 26 | 22 | 20 | 15 | 0 |

Formula: `Hit = ((Attacker Skill + 1) / (Defender Skill + 1)) × Range Factor`

---

## 5. Damage Table

4 wound states: 1=Winded, 2=Lightly Wounded, 3=Badly Wounded, 4=Dead

Damage index: `Range × 8 + rand(0-7)` → lookup per weapon

(56-entry table per weapon — see `gangsters_decoded/Data/Damage Table_decoded.txt` for full values)

---

## 6. Illegal Businesses (14 types)

| Business | Profit Grp | Setup Cost | Capacity | Populace | Inherent Profit |
|----------|-----------|------------|----------|----------|-----------------|
| Card Game | 36 | $100 | 3 | 6 | $10 |
| Casino | 40 | $1,100 | 6 | 15 | $100 |
| Counterfeit Press | 34 | $1,000 | 12 | 0 | $30 |
| Dice Game | 36 | $100 | 3 | 6 | $10 |
| Gambling Den | 37 | $200 | 3 | 10 | $15 |
| Grifters | 36 | $100 | 3 | 0 | $10 |
| Insider Trading | 38 | $650 | 3 | 2 | $10 |
| Loan Shark | 38 | $500 | 3 | 8 | $10 |
| Moonshine Still | 34 | $750 | 12 | 0 | $20 |
| Numbers Racket | 37 | $200 | 3 | 4 | $15 |
| Office | 34 | $200 | 3 | 0 | $10 |
| Prizefight Ring | 36 | $100 | 3 | 4 | $20 |
| Speakeasy | 39 | $650 | 20 | 12 | $50 |
| Teamsters | 35 | $450 | 3 | 0 | $20 |
| Whorehouse | 38 | $650 | 3 | 10 | $75 |

---

## 7. Market Share Decay

| Businesses Owned | Efficiency % |
|-----------------|-------------|
| 1 | 100 |
| 5 | 80 |
| 10 | 79 |
| 15 | 65 |
| 20 | 57 |
| 27 | 50 |
| 30 | 47 |
| 35 | 42 |
| 40 | 3 |

---

## 8. Empty Land Cost

| Land Value | Cost |
|-----------|------|
| 0 | $40 |
| 5 | $240 |
| 10 | $440 |
| 15 | $640 |

Linear: $40 per land value level.

---

## 9. Running Costs (8 groups × 16 levels)

| Group | Size 1 (LV0) | Size 1 (LV15) | Size 8 (LV0) | Size 8 (LV15) |
|-------|-------------|---------------|-------------|---------------|
| Other | 0 | 0 | 0 | 0 |
| Comm 1 | 40 | 340 | 200 | 1700 |
| Comm 3 | 100 | 850 | 200 | 1700 |
| Comm 8 | 200 | 1700 | 200 | 1700 |
| Industrial | 160 | 1360 | 160 | 1360 |
| Residential | 140 | 1190 | 140 | 1190 |
| Warehouse | 120 | 1020 | 120 | 1020 |
| Empty Land | 20 | 170 | — | — |

---

## 10. Profit Table Factors (4 tiers × 16 levels)

| LV | Tier 1 | Tier 2 | Tier 3 | Tier 4 |
|----|--------|--------|--------|--------|
| 0 | 50 | 100 | 150 | 20 |
| 5 | 250 | 500 | 750 | 90 |
| 8 | 400 | 800 | 1200 | 150 |
| 15 | 800 | 1600 | 2400 | 30 |

---

## 11. Scenarios (10)

| # | Name | File |
|---|------|------|
| 1 | Fat Chance | Scene1.dat |
| 2 | Gold Rush | Scene2.dat |
| 3 | Industrial | Scene3.dat |
| 4 | Open Season | Scene4.dat |
| 5 | Ricochet | Scene5.dat |
| 6 | South of the River | Scene6.dat |
| 7 | Suburban Slaughter | Scene7.dat |
| 8 | Surrounded | Scene8.dat |
| 9 | Treasure Island | Scene9.dat |
| 10 | Where Angels Fear to Tread | Scene10.dat |

---

## 12. Business Suspicion Matrix

15 illegal activities × 155 legal businesses. Values: 0=None, 1=Some, 2=High.

Most suspicious legal businesses (highest cumulative suspicion):
1. Cab Company — flagged on nearly all illegal activities
2. Department Store — flagged on most activities
3. Auction Rooms — flagged on gambling, counterfeiting, teamsters

Least suspicious:
- Clothiers, Chiropodists — only flagged on Teamsters (2)

Full matrix: `gangsters_decoded/Data/Business Suspicion_decoded.txt`
