# Gangsters: Organized Crime — Extracted Game Data

**Source**: Decrypted `.xtx` files from the original game installation  
**Decryption**: XOR with 4-byte repeating key `AF DE DE FA`  
**Tool**: [GangstersDecrypter](https://github.com/arklid/GangstersDecrypter)  
**Date**: August 2026  

---

## Table of Contents

1. [Constants](#1-constants)
2. [Economics — Legal Businesses](#2-economics--legal-businesses)
3. [Illegal Economics — Illegal Businesses](#3-illegal-economics--illegal-businesses)
4. [Illegal Profit — Profit Ratios](#4-illegal-profit--profit-ratios)
5. [Export Ratio — Export Pricing](#5-export-ratio--export-pricing)
6. [Crime — Order/Crime Table](#6-crime--ordercrime-table)
7. [Damage Table — Combat Results](#7-damage-table--combat-results)
8. [Hit Table — Combat Probabilities](#8-hit-table--combat-probabilities)
9. [Business Suspicion](#9-business-suspicion)
10. [Income Groups](#10-income-groups)
11. [Running Costs](#11-running-costs)
12. [Character Generation](#12-character-generation)
13. [Hoods — Predefined Characters](#13-hoods--predefined-characters)
14. [Cart — Combat Animation Data](#14-cart--combat-animation-data)
15. [Scenario — Game Setup](#15-scenario--game-setup)
16. [Market Share](#16-market-share)
17. [Miscellaneous Files](#17-miscellaneous-files)
18. [Design Implications for Steel City](#18-design-implications-for-steel-city)

---

## 1. Constants

**File**: `Constants.xtx` (5,939 bytes)

### City Population
| Value | Description |
|-------|-------------|
| 2000 | Number of civilians in city |
| 12 | Number of judges |
| 12 | Number of attorneys |
| 400 | Number of police in city |
| 100 | Number of FBI in city |

### Gang Starting Conditions
| Value | Description |
|-------|-------------|
| 5 | Initial number of hoods in a gang |
| 3 | Starting number of explosives |
| 1 | Number of businesses to start with |
| 1 | Number of vehicles a gang starts with |
| 6000 | Starting money for a gang ($6,000) |

### Fear Constants (Range / Base)
| Entity | Fear Range | Fear Base |
|--------|-----------|-----------|
| Owner | 100 | -20 |
| Civilian | 100 | -20 |
| Illegal Person | 128 | 0 |
| Hood | 128 | 0 |
| Police | 128 | 0 |
| Police Chief | 128 | 0 |
| FBI | 128 | 0 |
| FBI Head | 128 | 0 |
| Attorney | 128 | 0 |
| Judge | 128 | 0 |
| Mayor | 128 | 0 |
| Reporter | 128 | 0 |
| Religious Leader | 128 | 0 |

**Note**: Fear ranges from -20 to +80 for owners/civilians, 0 to +128 for all others. Negative base means owners and civilians start slightly fearful.

### Hostility Constants (Range / Base)
| Entity | Hostility Range | Hostility Base |
|--------|----------------|----------------|
| Owner | 130 | 20 |
| Civilian | 130 | 20 |
| Illegal Person | 256 | 0 |
| Hood | 256 | 0 |
| Police | 256 | 0 |
| Police Chief | 128 | 128 |
| FBI | 256 | 0 |
| FBI Head | 128 | 128 |
| Attorney | 128 | 128 |
| Judge | 128 | 128 |
| Mayor | 128 | 128 |
| Reporter | 128 | 128 |
| Religious Leader | 128 | 128 |

**Key insight**: Police Chief, FBI Head, Attorney, Judge, Mayor, Reporter, and Religious Leader all start at **base hostility 128** (maximum for their range) — they're inherently hostile to gang activity. Owners and civilians start at base 20 with a wide range (20-150).

### Squeal Constants (Range / Base)
| Entity | Squeal Range | Squeal Base |
|--------|-------------|-------------|
| Owner | 0 | 125 |
| Civilian | 0 | 100 |
| Illegal Person | 0 | 100 |
| Hood | 0 | 100 |
| Police | 0 | 200 |
| Police Chief | 0 | 250 |
| FBI | 0 | 250 |
| FBI Head | 0 | 250 |
| Attorney | 0 | 200 |
| Judge | 0 | 200 |
| Mayor | 0 | 250 |
| Reporter | 0 | 200 |
| Religious Leader | 0 | 200 |

**Key insight**: All squeal ranges are 0 — squeal is a **fixed value**, not random. Law enforcement (Police Chief, FBI, FBI Head, Mayor) have the highest squeal base at 250. Owners squeal at 125, civilians/hoods at 100.

### Loyalty
| Value | Description |
|-------|-------------|
| 192 | Loyalty range |
| 64 | Loyalty base |

Loyalty ranges from 64 to 256. Gang splitting compares loyalty to 192 and (hostility - fear) to 64.

### Bribe Prices
| Entity | Case Bribe Base | Case Bribe Multiplier | Bribe Base | Bribe Multiplier |
|--------|----------------|----------------------|------------|-----------------|
| Owner | 500 | 500 | 200 | 300 |
| Snitch | — | — | 500 | 4 |
| Attorney | 5,000 | 3,000 | 2,000 | 2,000 |
| Judge | 10,000 | 20,000 | 4,000 | 8,000 |
| Mayor | — | — | 10,000 | 10,000 |
| People | 1,000 | 1,000 | 300 | 300 |
| Police | 2,000 | 3,000 | 500 | 500 |
| Religious Leader | — | — | 2,000 | 2,000 |
| Reporter | — | — | 2,000 | 2,000 |
| Police Chief | — | — | 10,000 | 10,000 |

**Judges are the most expensive** to bribe — $10,000 base + $20,000 per case. Police Chiefs and Mayors share the $10,000 base tier.

### Election Constants
| Value | Description |
|-------|-------------|
| 100 | Minimum blocks to enter election |
| 4 | Cost divisor (not 0!) |

### FBI Suspicion of Income
| Value | Description |
|-------|-------------|
| 5,000 | Base illegal income before any suspicion |
| 16 | Amount to divide accountant skill by |
| 2 | Suspicion increase multiplier: `((Illegal Income / Legal Income) - Accountant Skill Factor) * 2` |

**Mechanic**: FBI suspicion starts when illegal income exceeds $5,000. The accountant's skill (divided by 16) acts as a reduction factor. The ratio of illegal to legal income drives suspicion growth.

### Police Allocation
| Value | Description |
|-------|-------------|
| 1,320 | Mayor allocation of police for patrols divided by this |

### Recruit Test Variables
| Value | Description |
|-------|-------------|
| 40 | Addition to intelligence when testing against highest skill |
| 36 | Addition to our gang size when testing against average skill |
| 45 | Addition to our gang size when testing against highest skill |

### Order Timing Constants
| Value | Description |
|-------|-------------|
| 140 | Location-based order time |
| 1,500 | Block-based order time |
| 96 | Block-based but only visit one location |

---

## 2. Economics — Legal Businesses

**File**: `Economics.xtx` (39,782 bytes, 268 lines)

### Business Groups
| Group # | Name |
|---------|------|
| 0 | Commercial |
| 1 | Industrial |
| 2 | Empty Land |
| 3 | Residential |
| 4 | Warehouse |
| 5 | Charity |
| 6 | Municipal |
| 7 | Interactive Residential (Tenements) |

**Total**: 171 business types across 8 groups.

### Column Format
```
Type, Profit Group, City Capacity, CC Min, CC Max, Number Present,
Running Cost Group, LV Min, LV Max, Set up Time factor, Float Max, Protection,
Content, Populace, Produce, Type, [Size],
Clothes, Inherent Costs, Stock Value, Inherent Profit, building reference,
Set up cost, Union, min Police Guard, max Police Guard, min Police Patrol,
max Police Patrol, FBI Guard, Capacity,
Liquor, Stolen, Counterfeit, Supplier, Consumer, Min POut, Max POut,
A(n), The, At the, Of the, On the
```

### Key Data Definitions
- **Union**: 0=No, 1=Manufacturing & Dockers, 2=Service & Dockers, 3=Food & Dockers
- **Contents**: 0=Nothing, 1=Cars, 2=Goods, 3=Trucks, 4=Guns
- **Produce**: 0=Nothing, 1=Food, 2=Services, 3=Health, 4=Entertainment, 5=Leisure, 6=Luxuries, 7=Investment, 8=Clothing, 9=Hardware, 10=Children, 11=General store (Other), 12=Miscellaneous, 13=Industrial, 14=Charity
- **Type**: 0=Commercial, 1=Industrial, 2=Residential, 3=Department store, 4=Main bank, 5=Large hotel, 6=Small Bank, 7=Church, 8=Radio station, 9=Newspaper, 10=Orphanage, 11=Warehouse, 12=All

### Notable Businesses

#### Docks (Industrial, Group 1)
| Property | Value |
|----------|-------|
| Profit Group | 9 (very high) |
| City Capacity | 64 |
| CC Min/Max | 32 / 96 |
| Number Present | 5 |
| Running Cost Group | 50 |
| LV Min/Max | 180 / 4 |
| Set up Time | 25,000 |
| Float Max | 240 |
| Contents | 1 (Cars) |
| Produce | 3 (Health) |
| Type | 13 (Industrial) |
| Union | 2 (Service & Dockers) |
| Set up cost | $5,000 |
| Capacity | 4 |
| Can store Liquor | No |
| Can store Stolen | No |
| Can store Counterfeit | No |

#### Warehouse (Group 4)
| Property | Value |
|----------|-------|
| Profit Group | 2 (low) |
| City Capacity | 768 |
| CC Min/Max | 384 / 1,152 |
| Number Present | 768 |
| Type | 11 (Warehouse) |
| Contents | 0 (Nothing by default) |
| Capacity | 30 |
| Can store Liquor | Yes |
| Can store Stolen | Yes |
| Can store Counterfeit | Yes |
| **Never goes bankrupt** | True |
| Notes | Only goes through economic model if owned by a gang leader |

**12 warehouses** exist in the city. They store up to 300 cases of liquor/stolen goods + 15 cases counterfeit money.

#### Railroad Terminal (Municipal, Group 6)
| Property | Value |
|----------|-------|
| Profit Group | 0 (no income) |
| City Capacity | 0 (random) |
| Contents | 0 (Nothing) |
| Type | 9 (Newspaper — shared code) |
| No produce, no stock, no profit | Purely a destination point |

#### Other Municipal Buildings (Group 6)
14 municipal types: City Hall, Courthouse, Employment Exchange, FBI Headquarters, Fire Department, Hospital, Museum, Police Headquarters, Power Plant, Public Baths, Railroad Terminal, School, US Post, Water Plant.

#### Residential (Group 3)
- 500 residential blocks, run by a landlord
- Profit Group: 0 (no income for gang)
- LV Min/Max: 9984 (fixed)
- Type: 2 (Residential)

#### Tenement Block (Interactive Residential, Group 7)
- Profit Group: 3
- City Capacity: 32,000
- Set up cost: $10,000
- Type: 2 (Residential)
- Can store goods: No

### Industrial Businesses (Group 1) — 33 Types
All industrial businesses share:
- Running Cost Group: 50
- Produce: 13 (Industrial)
- Type: 1 (Industrial)
- Union: 1 (Manufacturing & Dockers) or 2 (Service & Dockers) or 3 (Food & Dockers)

Notable industrial types: Abattoir, Builders, Cannery, Cement Factory, Ceramics, Chandler, Docks, Engineers, Excavators, Fabricators, Factory, Food Processors, Freight Forwarding, Furriers, Glaziers, Ice House, Ironmongers, Joiners, Junk Yard, Lumber Yard, Milk Yard, Newspaper, Packaging Plant, Paper Mill, Radio Station, Sheet Workers, **Steel Mill**, Stone Masons, Tanners, Textiles, Weavers, Wheelwrights.

---

## 3. Illegal Economics — Illegal Businesses

**File**: `Illegal Economics.xtx` (3,282 bytes, 25 lines)

### Column Format
```
Type, Profit Group, Running Cost Group, LV Min, LV Max,
Float Max, Content, Populace, Inherent Costs, Stock Value, Size,
Inherent Profit, Building Reference, Set up, Profit Ratio Table,
Union, Capacity, Liquor, Stolen, Counterfeit, Supplier, Consumer,
A(n), The, At the, Of the, On the
```

### All 14 Illegal Business Types

| Business | PG | RCG | LV Min | LV Max | Float | Content | Popul | IC | SV | Size | IP | BRef | Setup | Union | Capac | Liquor | Stolen | C.feit | S.plier | Consumer |
|----------|-----|-----|--------|--------|-------|---------|-------|-----|-----|------|-----|------|-------|-------|-------|--------|--------|--------|---------|----------|
| Card Game | 36 | 2 | 1 | 0 | 100 | 2 | 0 | 6 | 10 | 0 | 1 | 10 | 1 | 100 | 0 | 3 | 0 | 0 | 0 | 0 |
| Casino | 40 | 3 | 1 | 128 | 255 | 4 | 0 | 15 | 100 | 0 | 1 | 100 | 1 | 1100 | 0 | 6 | 0 | 0 | 0 | 0 |
| Counterfeit Press | 34 | 2 | 1 | 0 | 255 | 2 | 0 | 0 | 30 | 0 | 1 | 30 | 1 | 1000 | 0 | 12 | 0 | 0 | 1 | 1 |
| Dice Game | 36 | 2 | 1 | 0 | 100 | 2 | 0 | 6 | 10 | 0 | 1 | 10 | 1 | 100 | 0 | 3 | 0 | 0 | 0 | 0 |
| Gambling Den | 37 | 2 | 1 | 0 | 128 | 2 | 0 | 10 | 15 | 0 | 1 | 15 | 1 | 200 | 0 | 3 | 0 | 0 | 0 | 0 |
| Grifters | 36 | 2 | 1 | 0 | 100 | 2 | 0 | 0 | 10 | 0 | 1 | 10 | 1 | 100 | 0 | 3 | 0 | 0 | 0 | 0 |
| Insider Trading | 38 | 2 | 1 | 0 | 255 | 2 | 0 | 2 | 10 | 0 | 1 | 10 | 1 | 650 | 0 | 3 | 0 | 0 | 0 | 0 |
| Loan Shark | 38 | 1 | 1 | 0 | 255 | 2 | 0 | 8 | 10 | 0 | 1 | 10 | 1 | 500 | 0 | 3 | 0 | 0 | 0 | 0 |
| **Moonshine Still** | 34 | 2 | 1 | 0 | 255 | 2 | 0 | 0 | 20 | 0 | 1 | 20 | 1 | 750 | 0 | 12 | **1** | 0 | 0 | 1 | 0 |
| Numbers Racket | 37 | 2 | 1 | 0 | 128 | 2 | 0 | 4 | 15 | 0 | 1 | 15 | 1 | 200 | 0 | 3 | 0 | 0 | 0 | 0 |
| Office | 34 | 0 | 1 | 0 | 255 | 2 | 0 | 0 | 10 | 0 | 1 | 10 | 1 | 200 | 0 | 3 | 0 | 0 | 0 | 0 |
| Prizefight Ring | 36 | 1 | 1 | 0 | 100 | 2 | 0 | 4 | 20 | 0 | 1 | 20 | 1 | 100 | 0 | 3 | 0 | 0 | 0 | 0 |
| **Speakeasy** | 39 | 1 | 1 | 0 | 255 | 2 | 0 | 12 | 50 | 0 | 1 | 50 | 1 | 650 | 0 | 20 | **1** | 0 | 0 | 0 | **1** |
| Teamsters | 35 | 2 | 1 | 0 | 255 | 2 | 0 | 0 | 20 | 0 | 1 | 20 | 1 | 450 | 0 | 3 | 0 | 0 | 0 | 0 |
| Whorehouse | 38 | 1 | 1 | 0 | 255 | 2 | 0 | 10 | 75 | 0 | 1 | 75 | 1 | 650 | 0 | 3 | 0 | 0 | 0 | 0 |

### Key Observations

- **Casino** has the highest profit group (40) and highest set-up cost ($1,100)
- **Speakeasy** is the consumer of liquor (Consumer=1) and the second-highest profit (PG 39, setup $650)
- **Moonshine Still** is the supplier of liquor (Supplier=1, Liquor=1) — it produces liquor
- **Counterfeit Press** is the supplier of counterfeit money (Supplier=1, C.feit=1)
- **No illegal business can store stolen goods** (Stolen=0 for all)
- **Only Speakeasy and Moonshine Still interact with liquor** (Liquor=1)
- Illegal businesses **cannot be protected** (no protection column)
- All illegal businesses occupy the **center location** of a block (Size=0 means center-only)

### Goods Flow
```
Moonshine Still (produces liquor) → trucks → Speakeasy (sells liquor) → surplus → Warehouse
Counterfeit Press (produces counterfeit) → trucks → Legal businesses (launder) → surplus → Warehouse
Raids on businesses (steal goods) → trucks → Jewellers/Pawn Shops (fence) → surplus → Warehouse
Warehouse → trucks → Docks or Railroad Terminal → Export (cash, but less than local sale)
```

---

## 4. Illegal Profit — Profit Ratios

**File**: `Illegal Profit.xtx` (801 bytes)

Profit ratios replace the normal City Capacity / Number Present system for illegal businesses.

### Format
```
Number of business type present, Profit Ratio * 100
```

### Profit Ratio Table (by count of same type in city)
| Count | Ratio 1 | Ratio 2 | Ratio 3 | Ratio 4 | Ratio 5 |
|-------|---------|---------|---------|---------|---------|
| 1 | 270 | 300 | 350 | 150 | — |
| 2 | 260 | 280 | 300 | 120 | — |
| 3 | 250 | 260 | 250 | 100 | — |
| 4 | 240 | 240 | 200 | 80 | — |
| 5 | 230 | 220 | 150 | 60 | — |
| 6 | 220 | 200 | 100 | 40 | — |
| 7 | 210 | 180 | 90 | 20 | — |
| 8 | 200 | 160 | 80 | 0 | — |
| 9 | 190 | 140 | 70 | 0 | — |
| 10 | 180 | 120 | 60 | 0 | — |
| 11 | 170 | 110 | 50 | 0 | — |
| 12 | 160 | 100 | 45 | 0 | — |

**Mechanic**: The more illegal businesses of the same type exist in the city, the lower the profit ratio. This is a **diminishing returns** system — the first Casino earns at 270% ratio, the 12th earns at 160%. The 5 columns likely correspond to different difficulty levels or game configurations.

---

## 5. Export Ratio — Export Pricing

**File**: `Export Ratio.xtx` (575 bytes)

### Formula
```
Money = Factor × Value of Case × Number Sent in Total by All Players in Game Week
Number of Cases / 8 = bucket, Proportional Value of each crate in dollars × 100
TOTAL HAS TO BE DIVIDED BY 100
```

### Export Case Values
| Goods Type | Base Value per Case |
|------------|-------------------|
| Counterfeit Currency | $1,000 |
| Fenced Antiques (Stolen Goods) | $200 |
| Liquor | $100 |

### Diminishing Returns Table
| Cases Exported (all players) | % of Base Value |
|------------------------------|-----------------|
| 0 | 90% |
| 1 | 85% |
| 2 | 80% |
| 3 | 75% |
| 4 | 70% |
| 5 | 65% |
| 6 | 60% |
| 7+ | (continues diminishing) |
| 63+ | 5% (floor) |

**Key mechanics**:
- Export value is **always less** than selling within the city
- Diminishing returns based on **total exports by ALL players** in a game week
- Beyond 63 cases, each case scores only 5% of base value
- The `/8` divisor likely represents cases per truck

---

## 6. Crime — Order/Crime Table

**File**: `Crime.xtx` (3,678 bytes, 64 lines)

### Format
```
order_time (12000 = all week), manpower, max_manpower, suspicion, sentence, investigation, risk_public, risk_private, AI_priority, crime_string, newspaper_crime_string, # type
```

### All Order Types

| Time | Min | Max | Susp | Sent | Invest | Risk Pub | Risk Priv | AI Pri | Name | Type |
|------|-----|-----|------|------|--------|----------|-----------|--------|------|------|
| 12000 | 1 | 4 | 0 | 0 | 0 | 0 | 0 | 0 | goto order | gototype |
| 12000 | 1 | 4 | 0 | 0 | 0 | 0 | 0 | 0 | guard business | guardbusiness |
| 12000 | 1 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | own business | ownbusiness |
| 6000 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | kill | killtype |
| 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | stand | standtype |
| 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | adjust protection | adjustprotection |
| 12000 | 1 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | buy premises | buypremesis |
| 166 | 1 | 10 | 0 | 0 | 0 | 0 | 0 | 0 | collect protection | collectprotection |
| 166 | 1 | 4 | 0 | 0 | 0 | 0 | 0 | 0 | donate | donate |
| 12000 | 1 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | set up business | setupbusiness |
| 1000 | 1 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | set up meeting | setupmeeting |
| 12000 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | rendezvous | rendevouswithspy |
| 83 | 1 | 10 | 0 | 0 | 0 | 0 | 0 | 0 | explore | explore |
| 12000 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | lie low | lielow |
| 12000 | 1 | 4 | 0 | 0 | 0 | 0 | 0 | 0 | patrol | patrol |
| 166 | 1 | 10 | 0 | 0 | 0 | 0 | 0 | 0 | recruit | recruit |
| 12000 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | return to base | returntobase |
| 500 | 1 | 4 | 20 | 2 | 8 | 0 | 0 | 9 | bribe | bribe |
| 166 | 1 | 10 | 20 | 3 | 2 | 2 | 21 | 0 | extort | extort |
| 333 | 1 | 10 | 20 | 1 | 2 | 2 | 28 | 0 | intimidate | intimidate |
| 6000 | 1 | 4 | 60 | 6 | 6 | 2 | 29 | 0 | kidnap | kidnap |
| 500 | 1 | 4 | 40 | 2 | 8 | 4 | 41 | 0 | raid | raid |
| 12000 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | spy | spy |
| 12000 | 1 | 4 | 100 | 10 | 8 | 8 | 49 | 0 | ambush | ambush |
| 6000 | 1 | 4 | 40 | 5 | 2 | 6 | 49 | 0 | assault | assault |
| 333 | 1 | 4 | 80 | 8 | 8 | 8 | 63 | 0 | bomb | bomb |
| 6000 | 1 | 4 | 100 | 10 | 8 | 8 | 410 | 0 | kill | kill |
| 333 | 1 | 4 | 60 | 4 | 6 | 6 | 43 | 0 | smash up | smashup |
| 333 | 1 | 4 | 80 | 7 | 6 | 8 | 43 | 0 | torch | torch |
| 6000 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | tail | tail |
| 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | wait | waitorder |
| **6000** | **1** | **4** | **0** | **0** | **0** | **0** | **0** | **9** | **export** | **export** |
| 333 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | investigate | investigate |
| 333 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | remove body | removebody |
| 12000 | 1 | 1 | 10 | 10 | 10 | 0 | 0 | 0 | evade tax | evadetax |
| 12000 | 1 | 1 | 20 | 8 | 10 | 0 | 0 | 0 | bribe officials | bribeofficials |

### Illegal Business "Crimes" (auto-run, not orders)
| Susp | Sent | Invest | Name |
|------|------|--------|------|
| 20 | 0 | 0 | antiques racket |
| 20 | 4 | 5 | card game |
| 20 | 10 | 15 | casino |
| 20 | 15 | 15 | counterfeit |
| 20 | 4 | 5 | dice game |
| 20 | 6 | 10 | distillation |
| 20 | 5 | 10 | gambling den |
| 20 | 3 | 5 | grifting |
| 20 | 6 | 10 | insider dealing |
| 20 | 4 | 5 | loan shark |
| 20 | 5 | 10 | numbers racket |
| 20 | 6 | 10 | prostitution |
| 20 | 4 | 5 | prize fights |
| 20 | 6 | 10 | speakeasy |
| 20 | 4 | 5 | teamsters |
| 0 | 0 | 0 | laundering money |
| 0 | 0 | 0 | fencing goods |

### Key Insights

- **Time costs**: 12000 = all week, 6000 = half week, 1000 = ~1/12 week, 333 = ~1/36 week, 166 = ~1/72 week, 83 = ~1/144 week
- **Most dangerous orders**: Ambush and Kill (suspicion 100, sentence 10, investigation 8)
- **Bomb** has the highest public risk (8) and private risk (8)
- **Export** has zero suspicion, zero risk — it's a business order, not a crime
- **Explore** is the fastest order (83 time) and can use up to 10 hoods
- **Recruit** is also fast (166 time) with up to 10 hoods
- **All illegal businesses generate suspicion 20** — the investigation risk varies by type
- **Casino and Counterfeit** have the highest investigation risk (15)
- **Laundering money and fencing goods** generate zero suspicion — they're passive activities

---

## 7. Damage Table — Combat Results

**File**: `Damage Table.xtx` (1,362 bytes)

### Damage States
1. Winded
2. Lightly Wounded
3. Badly Wounded
4. Dead

### Damage Calculation
```
Result = Table[Range × 8 + RAND(0..7)]
```

### Weapons (8 total)
1. **Pistol** — lethal at close range, useless beyond range 5
2. **Tommy Gun** — most consistent damage across all ranges, lethal at range 1-2
3. **Rifle** — excellent at all ranges (1-7), consistent performance
4. **Shotgun** — devastating at point-blank (range 1), decent at range 2-3, useless beyond
5. **Fist** — only effective at point-blank (range 1), max damage = lightly wounded
6. **Baseball Bat/Crowbar** — only effective at point-blank, can kill at range 1
7. **Knife** — only effective at point-blank, can kill at range 1
8. **Kick** — only effective at point-blank, can kill at range 1

**Tommy Gun** is the best all-round weapon — consistent damage at all ranges. **Rifle** is the best long-range weapon. **Shotgun** is devastating up close but drops off fast.

---

## 8. Hit Table — Combat Probabilities

**File**: `Hit Table.xtx` (554 bytes)

### Hit Probability Formula
```
Hit Chance = ((Attacker Skill + 1) / (Defender Skill + 1)) × Range Factor
```

### Range Factors (9 weapons, 8 range bands)

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

**Key findings**:
- **Shotgun at range 2** has 100% hit factor — guaranteed hit at point-blank+1
- **Kick** has 105 at range 1 — higher than any weapon at point-blank (can exceed 100%)
- **Rifle** maintains 50% hit factor at ranges 2-4 — best medium-range weapon
- **Tommy Gun** has the lowest point-blank hit chance (12) but maintains usable hit rates at all ranges
- **Twin Pistols** significantly outperform single pistol at all ranges
- All weapons have **0 hit chance at range 8** (maximum range is effectively 7)

---

## 9. Business Suspicion

**File**: `Business Suspicion.xtx` (7,091 bytes, 155 lines)

Defines how suitable each legal business is as a **front** for each illegal business type. Despite the filename, the values represent **cover quality**, not suspicion level:

| Value | Meaning |
|-------|---------|
| **2** | **Ideal front** — illegal activity blends in naturally, low FBI suspicion |
| **1** | **Acceptable front** — plausible but not perfect, moderate suspicion |
| **0** | **Poor front** — illegal activity would stand out, high FBI suspicion |

**Example**: Pool hall as front for Card Game = 2 (ideal — people playing games is normal). Butcher as front for Card Game = 0 (poor — card games in a butcher shop draw attention).

**Confirmed by community**: [GOG.com forum](https://www.gog.com/forum/gangsters_organized_crime/some_tips) states "2 means the business is an ideal front." [Cheat guide](https://www.games-cheats.org/all_cheats_for/pc_games/102503/gangsters.html) confirms Printers and Banks are best for Counterfeit, Restaurants/Cafes for Speakeasy — all value 2.

*(Full table in decrypted file — 155 lines covering all 117 commercial + 33 industrial + 12 warehouse business types with front suitability for all 15 illegal business types.)*

---

## 10. Income Groups

**File**: `Income Groups.xtx` (4,952 bytes, 37 lines)

Defines profit and running cost multipliers for each income group (0-40+). Higher group = more income/cost.

---

## 11. Running Costs

**File**: `RunningCosts.xtx` (762 bytes, 23 lines)

Defines running cost values for each running cost group.

---

## 12. Character Generation

**File**: `Character Generation.xtx` (5,587 bytes, 330 lines)

### Hood Types and Weighting

| Type | Weighting | Description |
|------|-----------|-------------|
| Average Lt | 12 | Balanced lieutenant with good organisation |
| Superhood | 1 | Extremely rare, all skills very high |
| Recruiter/Investigator | 5 | High intelligence + stealth, no combat |
| Business Hood | (more types...) | Business + organisation focused |

### Skill Weighting System
Each hood type has min/max weightings for all 11 skills:
- Intelligence, Organisation, Business, Firearms, Fists, Knives, Arson, Explosives, Intimidation, Driving, Stealth

### Known Bug (from modding community)
The skill order in the file is **wrong** — it doesn't match the in-game order:
```
xtx-file          → ingame stat
Intelligence      → Intelligence
Organisation      → Arson
Business          → Fists
Firearms          → Driving
Fists             → Business
Knives            → Stealth
Arson             → Explosives
Explosives        → Intimidation
Intimidation      → Firearms
Driving           → Knives
Stealth           → Organisation
```

This means lieutenant bonuses meant for Organisation actually go to Arson, explaining why there are so many high-Arson hoods in the game.

---

## 13. Hoods — Predefined Characters

**File**: `Hoods.xtx` (36,793 bytes, 1,525 lines)

### 40 Predefined Hoods

Each hood has:
- Gender (0=male, 1=female)
- Intelligence (0-255)
- 10 Skills (0-63 each): Organisation, Business, Firearms, Fists, Knives, Arson, Explosives, Intimidation, Driving, Stealth
- Ethnic group (0-14)
- First name index, Second name index, Nick name index
- Head index (0-63), Hair (0-3), Eyes (0-7), Nose (0-1), Mouth (0-7)

#### Notable Predefined Hoods

**#1 Ian "Lucky" Livingstone** — Intelligence 255 (max), all skills 63 (max). A true "superhood."

**#2 Charles "Diamond" Cornwall** — Intelligence 255, high Business (63), Intimidation (63), Driving (63). A business/diplomacy specialist.

*(38 more predefined hoods in the full file, each with unique stat distributions.)*

---

## 14. Cart — Combat Animation Data

**File**: `Cart.xtx` (9,302 bytes, 465 lines)

### 16 Attack Types

Each attack type defines:
- Non-hit results (from behind and from front)
- Hit results (from behind and from front)
- Result ID, Trigger Frame, Wait Time, Miss Turns, Damage

### Result IDs
| ID | Description |
|----|-------------|
| 0 | Head pulling or hit back |
| 1 | Head dodge or hit forward |
| 2 | Ducking down |
| 3 | Falling to knees |
| 4 | Falling forward |
| 5 | Spinning fall forward |
| 6 | Twitch while down |
| 7 | Do nothing |
| 8 | Head forward on knees |
| 9 | Fall forward from knees |
| 10 | Get up from knees |
| 11 | Get up from fall to knees |
| 12 | Get up from fall |

Attack types include: downward club attack, straight fist, side fist, uppercut, kick, knife slash, pistol whip, pistol shot, tommy gun burst, rifle shot, shotgun blast, twin pistol shot, and more.

---

## 15. Scenario — Game Setup

**File**: `Scenario.xtx` (2,043 bytes, 59 lines)

Defines scenario parameters for single-player games including starting conditions, gang count, difficulty modifiers, and victory conditions.

---

## 16. Market Share

**File**: `Market Share.xtx` (425 bytes, 49 lines)

Defines market share calculations for business dominance in the city.

---

## 17. Miscellaneous Files

### Empty Land Cost (`Empty Land Cost.xtx`)
Land cost values for different block types.

### Land Value Reductions (`Land Value Reductions.xtx`)
Factors that reduce land value (violence, proximity to crime, etc.).

### ProfitTableFactors (`ProfitTableFactors.xtx`)
Multipliers applied to profit calculations.

### DefaultHST (`DefaultHST.txt`)
Default high score table entries.

### LastWeekReport (`LastWeekReport.txt`)
Template for end-of-week summary reports.

### ReportGroups (`ReportGroups.txt`)
7 report groups: Priority Orders, Waiting, All Orders, Order Failed, Law Enforcement, Gangster Activity, City Events.

### ReportMessages (`ReportMessages.txt`)
All in-week report message templates with `%s` placeholders for hood names.

### OrderFailed (`OrderFailed.txt`)
Failure messages for each order type. Export failure: "We couldn't do it!"

### Tutorial (`Tutorial.txt`)
4 tutorial scenarios with descriptions.

### History (`History.txt`)
Development history note: "10-08-98 Placed in Balancing Folder"

---

## 18. Design Implications for Steel City

### Export System
1. **Two export points**: Docks (industrial, profitable, buyable) and Railroad Terminal (municipal, free, unbuyable)
2. **Diminishing returns**: More exports = less per-case value (90% at 0 cases → 5% at 63+ cases)
3. **Base values**: Counterfeit $1,000/case, Stolen $200/case, Liquor $100/case
4. **Always less profitable** than local sale
5. **Zero criminal risk** for the export order itself — FBI risk is on the warehouse, not the export

### Goods Economy
1. **3 goods types**: Liquor (Still → Speakeasy), Counterfeit (Press → Legal businesses), Stolen (Raids → Fencers)
2. **Surplus auto-distributed** to warehouses during weekly simulation (trucks explicitly mentioned only for export to Docks/Terminal)
3. **Warehouses**: 12 fixed, never bankrupt, store 300+15 cases, required for export
4. **FBI interest**: Warehouses are the weak point — a single raid can destroy stock

### Combat System
1. **8 weapons** with distinct range/damage profiles
2. **4 damage states**: Winded → Lightly Wounded → Badly Wounded → Dead
3. **Hit chance**: `((Attacker Skill + 1) / (Defender Skill + 1)) × Range Factor`
4. **Shotgun** is guaranteed hit at range 2 (100% factor)
5. **Rifle** is the best all-range weapon (50% at ranges 2-4)
6. **Melee** only works at point-blank (range 1)

### Fear/Hostility/Squeal System
1. **Fear**: Modifies NPC behavior — fearful NPCs more compliant
2. **Hostility**: Determines attack likelihood — law enforcement starts at max hostility (128)
3. **Squeal**: Fixed (no range) — determines likelihood of reporting to police. Police/FBI/Mayor squeal at 250, civilians at 100
4. **Gang splitting**: Compares loyalty (base 64, range 192) against hostility-fear (threshold 64)

### FBI Suspicion
1. **Threshold**: $5,000 illegal income before suspicion starts
2. **Accountant skill** (divided by 16) reduces suspicion
3. **Ratio-driven**: (Illegal Income / Legal Income) × 2 = suspicion growth rate
4. **All illegal businesses generate suspicion 20** — investigation risk varies by type (Casino/Counterfeit highest at 15)

### City Population
- 2,000 civilians, 400 police, 100 FBI, 12 judges, 12 attorneys
- Gang starts with 5 hoods, 1 business, 1 vehicle, 3 explosives, $6,000

### Bribe Economy
- Judges: Most expensive ($10,000 base + $20,000/case)
- Police Chiefs & Mayors: $10,000 base
- Regular police: $500 base + $500/case
- Snitches: $500 base + $4/case (cheap but unreliable)

---

## Appendix: XTX Decryption

All `.xtx` files in the Gangsters data directory are encrypted with a simple XOR cipher:

**Key**: `AF DE DE FA` (4-byte repeating)  
**Method**: `decoded[i] = encrypted[i] ^ key[i % 4]`

The key was discovered by the Gangsters modding community and is documented at:
- [GangstersDecrypter (GitHub)](https://github.com/arklid/GangstersDecrypter)
- [Neoseeker Forums](https://www.neoseeker.com/forums/3269/t2097474-gangsters-organized-crime-modding-xtx-files/)

Decrypted files are stored in: `docs/xtx_decrypted/`
