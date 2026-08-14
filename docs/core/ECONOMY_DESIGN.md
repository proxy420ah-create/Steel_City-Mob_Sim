# Economy & Goods Design Document

**Created**: 2026-08-13  
**Status**: 📐 Design — Informed by decrypted game data from original Gangsters  
**Related**: `GANGSTERS_GAME_DATA.md`, `ZONING_DESIGN.md`, `REVERSE_ENGINEERING_FINDINGS.md`  
**Source Data**: `docs/xtx_decrypted/` (28 decrypted .xtx files)

---

## Table of Contents

1. [Design Philosophy](#1-design-philosophy)
2. [Goods Types](#2-goods-types)
3. [Production & Supply Chain](#3-production--supply-chain)
4. [Warehouses](#4-warehouses)
5. [Export System](#5-export-system)
6. [Counterfeit Money & Laundering](#6-counterfeit-money--laundering)
7. [Business Fronts & Suspicion](#7-business-fronts--suspicion)
8. [Illegal Business Economy](#8-illegal-business-economy)
9. [FBI Suspicion System](#9-fbi-suspicion-system)
10. [Bribe Economy](#10-bribe-economy)
11. [Steel City Adaptations](#11-steel-city-adaptations)

---

## 1. Design Philosophy

The original Gangsters economy operates on a simple but elegant loop:

```
Produce → Distribute → Sell/Launder locally (high profit, high risk)
                  ↘ Store surplus → Export (lower profit, lower risk)
```

**Core tension**: Local sale is always more profitable than export, but local operations generate FBI suspicion. Export is safe but yields diminishing returns. The player must balance aggressive expansion against law enforcement heat.

**Steel City principle**: Preserve this tension. The economy should reward strategic thinking about *where* to place operations, *when* to export vs. sell locally, and *how* to manage FBI heat through accountants, fronts, and careful expansion.

---

## 2. Goods Types

Three illegal goods types, each with distinct production, distribution, and risk profiles:

| Goods Type | Base Export Value | Producer | Consumer | Warehouse Capacity |
|------------|------------------|----------|----------|-------------------|
| **Counterfeit Money** | $1,000/case | Counterfeit Press | Legal businesses (laundering) | 15 cases |
| **Stolen Goods** | $200/case | Raids on businesses | Fencers (Jewellers, Pawn Shops) | 300 cases (shared) |
| **Liquor** | $100/case | Moonshine Still | Speakeasy | 300 cases (shared) |

**Design note**: Counterfeit is the highest-value, lowest-volume good. Liquor is the lowest-value, highest-volume. This creates natural progression — liquor is the entry-level goods type, counterfeit is the endgame.

---

## 3. Production & Supply Chain

### Liquor Chain
```
Moonshine Still (produces liquor, capacity 12)
    → trucks distribute to:
        Speakeasy (sells to customers, capacity 20, consumer=1)
        Surplus → Warehouse (stores up to 300 cases)
            → Export to Docks or Railroad Terminal
```

- **Moonshine Still**: Set-up cost $750, Profit Group 34, Supplier=1
- **Speakeasy**: Set-up cost $650, Profit Group 39, Consumer=1
- Both must be placed in the center location of a commercial or tenement block
- Speakeasies are the most profitable illegal business (PG 39)

### Counterfeit Chain
```
Counterfeit Press (produces counterfeit, capacity 12)
    → auto-distributed during weekly simulation to:
        Legal businesses (laundering — "handing out with change")
        Surplus → Warehouse (stores up to 15 cases)
            → Export to Docks or Railroad Terminal
```

- **Counterfeit Press**: Set-up cost $1,000, Profit Group 34, Supplier=1
- **No direct consumer** — counterfeit is laundered through ALL legal businesses
- Laundering is passive (no order needed), generates suspicion 0 but sentence 5 / investigation 4 if caught
- The manual states: "distributed around your legal businesses for handing out with change to the members of the public"

### Stolen Goods Chain
```
Raid order on rival businesses (produces stolen goods)
    → trucks distribute to:
        Fencers (Jewellers, Pawn Shops — sell locally)
        Surplus → Warehouse (stores up to 300 cases, shared with liquor)
            → Export to Docks or Railroad Terminal
```

- **No dedicated producer business** — stolen goods come from raid orders
- **Fencers**: Jewellers, Pawn Shops, Antiques Dealers (legal businesses that can receive stolen goods)
- The Economics.xtx data shows which businesses can store stolen goods (Stolen=1 column)

### Distribution Mechanic
- Surplus goods are **auto-distributed to warehouses** during the weekly simulation
- The manual only explicitly mentions **trucks for export** (warehouse → Docks/Terminal)
- Distribution to legal businesses for laundering/sale appears to be an abstracted economic tick, not a visible truck movement

---

## 4. Warehouses

| Property | Value |
|----------|-------|
| Business Group | 4 (dedicated) |
| City Capacity | 768 (12 fixed in city) |
| Type | 11 (Warehouse) |
| Capacity | 300 cases liquor/stolen + 15 cases counterfeit |
| Goes bankrupt | **Never** |
| Can be bought | Yes |
| FBI interest | **High** — "the FBI are always interested in warehouses" |

### Design Rules
1. **Fixed count**: 12 warehouses exist in the city at fixed locations
2. **Never go bankrupt**: Only run through economic model if gang-owned
3. **Storage limit**: 300 liquor/stolen + 15 counterfeit — forces export or risk
4. **FBI raid target**: A single raid can destroy entire stock — the weak point
5. **Required for export**: Cannot export without warehouse storage
6. **Can be set up in tenement blocks**: Must shut down the tenement first, buy center square

### Steel City Adaptation
- Keep the fixed warehouse count (or scale with city size)
- Warehouse placement should be strategic — distance to export points matters
- Consider making warehouse raids the primary FBI enforcement mechanism
- The 300/15 capacity split creates interesting tension: counterfeit is valuable but storage-starved

---

## 5. Export System

### Export Points

| Export Point | Classification | Ownable | Placement | Secondary Functions |
|--------------|---------------|---------|-----------|-------------------|
| **Docks** | Industrial (Group 1) | Yes | Industrial zone | Profit business, recruitment site |
| **Railroad Terminal** | Municipal (Group 6) | No | On rail line | None (pure destination) |

### Export Order
- **Order type**: 0x18 (Export Goods)
- **Time cost**: 6,000 ticks (half week)
- **Manpower**: 1-4 hoods
- **Suspicion**: 0 (no criminal risk)
- **Sentence**: 0
- **Risk**: 0
- **Failure message**: "We couldn't do it!"

### Export Pricing — Diminishing Returns

| Cases Exported (all players) | % of Base Value |
|------------------------------|-----------------|
| 0 | 90% |
| 1 | 85% |
| 2 | 80% |
| 3 | 75% |
| 4 | 70% |
| 5 | 65% |
| 6 | 60% |
| 7+ | continues diminishing |
| 63+ | 5% (floor) |

**Formula**: `Money = Factor × Base Value × Number of Cases × Diminishing Returns Factor`

**Key mechanics**:
- Diminishing returns based on **total exports by ALL players** in a game week
- Export is **always less profitable** than local sale
- Beyond 63 cases, each case scores only 5% of base value
- The `/8` divisor in the formula likely represents cases per truck

### Steel City Adaptation
- Two export points creates geographic strategy — which is closer to your warehouse?
- Docks being ownable creates a strategic asset — controlling the docks taxes rival exports
- Railroad Terminal being unownable ensures there's always an export option
- Diminishing returns discourages mass export — rewards diverse income streams

---

## 6. Counterfeit Money & Laundering

### The Counterfeit Cycle

```
1. PRODUCE: Counterfeit Press → counterfeit money (12 cases capacity)
   (OR: Raid rival warehouse/press → steal counterfeit)

2. LAUNDER: Auto-distributed to legal businesses during weekly simulation
   → Business staff "hand out with change" to customers
   → Passive income, suspicion 0, but sentence 5 / investigation 4 if caught

3. SURPLUS: Overflow → nearest Warehouse (15 case limit)
   → FBI "always interested in warehouses" — raid risk

4. EXPORT: Warehouse → trucks → Docks or Railroad Terminal
   → $1,000/case base × diminishing returns (90% → 5%)
   → Zero criminal risk for the export order itself
```

### Why Counterfeit is Special
- **Highest value per case** ($1,000 vs $200 stolen, $100 liquor)
- **Smallest warehouse capacity** (15 cases vs 300) — storage-starved
- **No dedicated consumer** — every legal business launders it
- **Passive distribution** — no order needed, happens automatically
- **Highest investigation risk** if caught (investigation=15, tied with Casino)
- **Cheapest to set up** ($1,000 press vs $1,100 casino)

### Laundering Throughput
Every legal business has `C.feit=1` in the game data, meaning all 117+ commercial businesses can launder counterfeit. However, the **rate** of laundering depends on the business's transaction volume — a bank handles more cash than a florist.

### Steel City Adaptation
- Consider making laundering throughput proportional to business profit/revenue
- The 15-case warehouse limit creates urgency — either launder fast or export at reduced value
- Accountant skill is critical for counterfeit operations (reduces FBI income suspicion)

---

## 7. Business Fronts & Suspicion

### Front Suitability System

From `Business Suspicion.xtx` — values represent **cover quality**, not suspicion level:

| Value | Meaning |
|-------|---------|
| **2** | **Ideal front** — illegal activity blends in, low FBI suspicion |
| **1** | **Acceptable front** — plausible but imperfect, moderate suspicion |
| **0** | **Poor front** — illegal activity stands out, high FBI suspicion |

**Confirmed by community**: [GOG.com forum](https://www.gog.com/forum/gangsters_organized_crime/some_tips) states "2 means the business is an ideal front."

### Front Suitability for Counterfeit

| Rating | Business | Why it's a good cover |
|--------|----------|----------------------|
| **2** (ideal) | Printers | Printing equipment expected — perfect cover for a press |
| **2** (ideal) | Bank, Bank (Large) | Cash handling expected — counterfeit blends into cash flow |
| **2** (ideal) | Hotel, Hotel (Large) | High-volume cash transactions, many guests |
| **2** (ideal) | Department Store | High cash volume, many transactions |
| **2** (ideal) | Stationers | Paper/ink supplies — supports printing cover story |
| **1** (acceptable) | Auction Rooms, Auto Dealers, Cab Company, Finance Company | Cash-related but less volume |
| **0** (poor) | All other businesses | No natural cover for counterfeit activity |

### Front Suitability for Other Crimes (examples)

| Crime | Ideal Fronts (value=2) |
|-------|----------------------|
| **Card Game** | Pool hall, Restaurant, Theater, Trade Union |
| **Speakeasy** | Restaurant, Cafe, Hotel, Bank, Theater, Department Store |
| **Prizefight** | Gym, Pool hall, Cab Company, Furniture Store, Blacksmiths |
| **Moonshine** | Drug Store, Doctors, Department Store, Cosmetics Store, Laundry |
| **Loan Shark** | Bank, Hotel, Finance Company, Real Estate, Lawyers |
| **Casino** | Hotel, Theater, Department Store, Restaurant, Bank |

### Design Principle
The game rewards **logical pairing** of illegal businesses with compatible legal fronts:
- Pool hall + Card Game = natural (people playing games)
- Florist + Counterfeit = suspicious (printing press in a flower shop?)
- Restaurant + Speakeasy = classic Prohibition cover
- Gym + Prizefight = obvious but effective

**Risk/reward**: The best fronts (Banks, Hotels, Department Stores) are the most expensive businesses to acquire. Players start with poor fronts and high risk, then upgrade as they can afford better cover.

### Steel City Adaptation
- Implement front suitability as a multiplier on FBI detection chance
- Value 2 = 0.5× detection, Value 1 = 1.0× detection, Value 0 = 2.0× detection
- Front suitability should be visible to the player as a "cover rating" when selecting a front
- Consider adding a cost premium for high-cover businesses

---

## 8. Illegal Business Economy

### All 14 Illegal Business Types

| Business | PG | Setup Cost | Capacity | Goods | Key Feature |
|----------|-----|-----------|----------|-------|-------------|
| Card Game | 36 | $100 | 3 | — | Poor area, cheap entry |
| Dice Game | 36 | $100 | 3 | — | Poor area, cheap entry |
| Grifters | 36 | $100 | 3 | — | Poor area, cheap entry |
| Prizefight Ring | 36 | $100 | 3 | — | Poor area, needs Gym front |
| Gambling Den | 37 | $200 | 3 | — | Poor-mid area |
| Numbers Racket | 37 | $200 | 3 | — | Poor-mid area |
| Teamsters | 35 | $450 | 3 | — | Transport-related |
| Loan Shark | 38 | $500 | 3 | — | Rich area, needs Bank/Finance front |
| Insider Trading | 38 | $650 | 3 | — | Rich area, needs financial front |
| Speakeasy | 39 | $650 | 20 | Liquor (consumer) | Best profit, needs liquor supply |
| Whorehouse | 38 | $650 | 3 | — | Rich area |
| **Moonshine Still** | 34 | $750 | 12 | Liquor (producer) | Supplies speakeasies |
| **Counterfeit Press** | 34 | $1,000 | 12 | Counterfeit (producer) | Highest-value goods |
| Casino | 40 | $1,100 | 6 | — | Highest profit, highest risk |

### Profit Groups (higher = more income)
- PG 34: Production businesses (Moonshine, Counterfeit) — low direct profit, produces goods
- PG 35-36: Poor-area businesses — low profit, cheap setup
- PG 37-38: Mid-tier businesses — moderate profit
- PG 39: Speakeasy — high profit, consumes liquor
- PG 40: Casino — highest profit, highest investigation risk

### Diminishing Returns (from `Illegal Profit.xtx`)
The more illegal businesses of the same type exist in the city, the lower the profit ratio:
- 1st business: 270% ratio
- 5th business: 230% ratio
- 12th business: 160% ratio

**Inflection point**: ~4-5 businesses of the same type — after that, diminishing returns make additional copies less worthwhile.

### Steel City Adaptation
- Keep the 14 illegal business types or adapt to 1920s theme
- Diminishing returns prevents monoculture strategies
- The producer/consumer relationship (Still → Speakeasy) creates supply chain gameplay
- Consider visualizing profit ratios in the UI so players understand diminishing returns

---

## 9. FBI Suspicion System

### Income Suspicion

From `Constants.xtx`:
- **Threshold**: $5,000 illegal income before any suspicion
- **Formula**: `((Illegal Income / Legal Income) - Accountant Skill / 16) × 2`
- **Accountant skill** (÷16) acts as a reduction factor
- A 5-star accountant (skill ~80) provides 80/16 = 5× reduction

**Practical impact**:
- Without an accountant: stay under 5× illegal-to-legal ratio
- With a 5-star accountant: can go up to ~21× illegal-to-legal ratio
- Counterfeit at $1,000/case means just 5 cases/week triggers the threshold

### Business Suspicion (per illegal business)
All illegal businesses generate **suspicion 20** when operating. The investigation risk varies:

| Business | Investigation Risk | Sentence |
|----------|-------------------|----------|
| Counterfeit Press | 15 (highest) | 15 (highest) |
| Casino | 15 | 10 |
| Speakeasy | 10 | 6 |
| Distillation (Moonshine) | 10 | 6 |
| Prostitution | 10 | 6 |
| Insider Trading | 10 | 6 |
| Gambling Den | 10 | 5 |
| Numbers Racket | 10 | 5 |
| Loan Shark | 5 | 4 |
| Card Game | 5 | 4 |
| Dice Game | 5 | 4 |
| Prizefights | 5 | 4 |
| Teamsters | 5 | 4 |
| Grifting | 5 | 3 |
| Laundering (passive) | 4 | 5 |
| Fencing (passive) | 4 | 0 |

### Warehouse Risk
- FBI "always interested in warehouses"
- A single raid can destroy entire stock (300 liquor/stolen + 15 counterfeit)
- This is the primary enforcement mechanism against goods-based operations

### Steel City Adaptation
- The two-layer suspicion system (income + business) is elegant — preserve it
- Accountant skill as a ratio reducer is intuitive and rewards investment
- Warehouse raids should be the dramatic FBI moment — high stakes, potential huge losses
- Consider adding visual FBI heat indicators (solo agent = suspicion, group = incoming raid)

---

## 10. Bribe Economy

From `Constants.xtx`:

| Official | Bribe Base | Bribe Multiplier | Case Bribe Base | Case Bribe Multiplier |
|----------|-----------|-----------------|----------------|----------------------|
| Judge | $4,000 | $8,000 | $10,000 | $20,000 |
| Mayor | $10,000 | $10,000 | — | — |
| Police Chief | $10,000 | $10,000 | — | — |
| Attorney | $2,000 | $2,000 | $5,000 | $3,000 |
| Police | $500 | $500 | $2,000 | $3,000 |
| Religious Leader | $2,000 | $2,000 | — | — |
| Reporter | $2,000 | $2,000 | — | — |
| Owner | $200 | $300 | $500 | $500 |
| Snitch | $500 | $4 | — | — |
| People | $300 | $300 | $1,000 | $1,000 |

**Key insights**:
- Judges are the most expensive to bribe ($10K base + $20K/case) — but they can dismiss charges
- Police Chiefs and Mayors share the $10K tier — they control police allocation and elections
- Snitches are cheap ($500 + $4/case) but unreliable
- Regular police are relatively affordable ($500 + $500/case)

### Steel City Adaptation
- Bribe costs should scale with the official's power
- The case bribe system (higher cost for active cases) creates strategic timing — bribe before charges are filed
- Consider making bribes temporary (weekly) rather than permanent

---

## 11. Steel City Adaptations

### What to Preserve
1. **Three goods types** with distinct value/volume/risk profiles
2. **Producer → Consumer → Warehouse → Export** supply chain
3. **Two export points** (one ownable, one municipal) with geographic strategy
4. **Diminishing returns** on both export and illegal business count
5. **Front suitability system** — logical pairings reduce FBI heat
6. **Two-layer FBI suspicion** (income + business operation)
7. **Accountant as suspicion reducer**
8. **Warehouse as the weak point** — high value, high risk
9. **Bribe economy** with power-scaled costs

### What to Improve
1. **Visualize laundering throughput** — show players how fast each business launders
2. **Front suitability UI** — show cover rating when selecting a front
3. **Export route visualization** — show truck paths from warehouse to export point
4. **FBI heat indicators** — solo agent = suspicion, group = incoming raid
5. **Profit ratio graphs** — show diminishing returns curve for each business type
6. **Warehouse raid consequences** — show potential loss value before it happens

### What to Simplify
1. **Auto-distribution** — keep it abstracted, don't require micromanagement
2. **Bribe system** — streamline the case/non-case distinction
3. **Skill order bug** — ensure character generation matches displayed stats

### Open Design Questions
1. **Should Steel City add new goods types** beyond the original three?
2. **Should warehouses be buyable or rentable** in Steel City?
3. **Should the Docks generate income** for the owning gang, or just serve as export point?
4. **How many illegal business types** does Steel City need? All 14 or a curated subset?
5. **Should front suitability affect laundering rate**, or just FBI detection chance?
6. **Should export diminishing returns be per-player or global** (all players combined)?
