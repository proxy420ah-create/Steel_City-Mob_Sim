# Playtesting Insights — Original Game Manual Study

**Created**: August 6, 2026
**Status**: 📋 Active Research
**Source**: `manual_text.txt` (official Gangsters: Organized Crime manual) + live playtesting

---

## Overview

Insights gathered from close study of the original game manual and live playtesting sessions. These findings refine the Steel City design docs with mechanics confirmed from the manual that weren't fully captured in the binary analysis alone.

---

## 1. Fear / Hostility / Squeal — Three Independent Axes

### Confirmed from Manual + Binary

Fear, Hostility, and Squeal are **separate citizen metrics**, not opposites. The game tracks all three simultaneously per NPC.

| Metric | What It Measures | Base (Business Owner) |
|--------|-----------------|----------------------|
| Fear | How scared of you | 100 (starts at 80) |
| Hostility | How angry at you | Varies |
| Squeal | Likelihood to inform police | 125 |

### The Four Combinations

| Fear | Hostility | Behavior |
|------|-----------|----------|
| High | Low | Complies AND stays quiet — **IDEAL** |
| High | High | Complies but hates you — may squeal out of anger despite fear |
| Low | Low | Indifferent — won't resist, won't stay quiet if crime happens nearby |
| Low | High | Resists AND squeals — **WORST CASE** |

### Critical Counterintuitive Finding

**High fear INCREASES squealing.** Terrified people talk more — they go to police seeking protection FROM you. Fear helps with **compliance** (paying protection) but hurts with **silence** (not squealing).

This means the ideal block isn't "maximum fear" — it's "enough fear to comply, low enough hostility to not rage-squeal, with a corrupt cop to catch any squeal that slips through."

### What Raises Fear (Minimal Hostility)

- **Intimidation order** — the scalpel. Raises fear without much hostility.
- **Consistent collection visits** — maintains fear over time passively.
- **Assault** — raises fear high but also raises hostility + squeal risk (witnesses)

### What Raises Hostility

- **Violent crimes** (smash up, torch, bomb, assault, kill) against owner or property
- **Failing to protect** — owner blames YOU when rival attacks and you don't defend
- **Excessive protection demands** — overcharging triggers refusal + squealing
- **Priests and reporters** — sermons and newspaper stories shift public opinion against you
- **Damage near municipal buildings** — "always produces a negative response"

### What Lowers Hostility

- **Donations to charity/churches** — "Making donations ensures that you are looked on favourably"
- **Soup kitchens** — "immediate effect of making you incredibly popular with the local people"
- **Bribes** — direct payment to business owner
- **Owning the newspaper** — suppresses negative stories about you
- **Getting priests on your side** — their sermons shift public opinion

### Steel City Design Implication

The Fear/Hostility/Squeal model in `CRIME_SQUEAL.md` and `EXTORTION_TERRITORY.md` is confirmed correct. The "High Fear + Low Hostility = ideal" formula is already documented. The key refinement: **fear should have a diminishing/negative return on squeal suppression at high levels**. A terrified NPC should be MORE likely to squeal, not less. This creates a natural tension — you can't just max out fear and forget about it.

---

## 2. Extortion Mechanics — What Actually Matters

### Key Skill: Intimidation (NOT Intelligence)

From manual page 40: "The hoods performing this order need to be good at **intimidation**."

From manual page 93: "Intimidation — This is a **key skill for the business side of the empire**. Hoods who are good at intimidation are valuable people, since they can establish large amounts of protection money."

Intelligence is NOT used for extortion. Intelligence is used for: bribery, recruitment, bombing, arson, killing, and Lieutenant order allocation.

### Distance Penalty — Measured from Nearest Office

From manual page 39: "The further away from your **base** you try to extort, the more likely people are to reject your attempts."

From manual page 39: "The closer the site to one of your **offices**, the quicker the hood will get there."

"Base" = nearest office, not just starting HQ. Multiple offices reduce the distance penalty across your territory. This means **expanding your office network** is a territorial strategy, not just a convenience.

### Manpower Matters

From manual page 38: "Increased manpower comes in most useful when you are giving area orders such as **collect protection and extort**, performing orders in **another Gang's territory**, or when securing your own area."

More hoods on an extortion run = more intimidation pressure = higher success rate, especially in contested areas.

### Protection Is a Service Contract, Not a Flag

From manual page 91: "Business Owners who are within your protection may be extremely stubborn. If they are paying, they will expect **good service**, and a **succession of attacks from another source** may see them **leaving your empire in droves**."

Key implications:
- Protection is **not permanent** — owners leave if you don't defend them
- Rival gangs can **steal your protection** by attacking your businesses faster than you defend
- The binary contains a "Take Over Protection" order (case 0x30) — a distinct action from basic extortion
- Re-extorting a lost business can fail because the owner's **hostility toward you has increased** (you failed to protect them)

### Steel City Design Implication

The `EXTORTION_TERRITORY.md` territory strength model is correct. The refinement: add **office proximity** as a factor in extortion success rate. Also, the "Take Over Protection" order should be a distinct action from "Extort" — it specifically targets rival-protected businesses and has different success factors (rival's defense strength vs. your intimidation + manpower).

---

## 3. Information Tiers — The Squealer Identification Problem

### The Core Design: Information Asymmetry

The game intentionally gates what you know based on your **intelligence infrastructure**. Squealers are hidden by default.

### What's Available Without a Lawyer

- **Most Wanted report** — which of your hoods the FBI is watching (always available)
- **Suspicion graph** (F6) — overall suspicion level, but not WHO caused it
- **Clipboard → People → Business Owners** — has a squealer sub-filter, but no map highlight
- **Bomb order target highlighting** — valid targets include squealer-occupied buildings (indirect detection by elimination)

### What Requires a Lawyer

- **Squealers report** — direct list of people who informed on you, with map locations
- **Crimes report** — detailed crime list with per-crime "risk of arrest"
- **Legal Proceedings** — who's on trial, judge/DA names, case status
- **Elections report** — only appears during election season
- **Employed Police** — list of cops on your payroll

### What Requires Bribing a DA

- **Witness list** for an active trial (first bribe)
- **Juror list** for an active trial (second bribe)
- These are trial defense tools, not squealer identification tools

### Conditional Reports

Reports only appear when there's something to report. The Squealers report doesn't show up (or is greyed out) when no one has squealed. Same pattern as Elections (only during elections) and Legal Proceedings (only when hoods are arrested).

### The Deduction Game

Without intelligence infrastructure, you're forced to deduce who squealed from indirect signals:
1. Business owner refused protection → prime squealer candidate
2. Crimes report shows sudden "risk of arrest" spike → someone talked about that crime
3. Police patrolling a specific area more → someone in that area squealed
4. A hood gets arrested → someone squealed on that hood's activities
5. Suspicion graph jumps → heat is building, source unknown

The game gives you **consequences before causes**. You see the police response and work backward.

### Steel City Design Implication

The `INTELLIGENCE_TERRITORY.md` tier system is confirmed. The refinement: add **Lawyer as an intelligence infrastructure requirement** for the squealer identification tier. Without a Lawyer-equivalent, the player should only see indirect signals. The Bomb order trick (indirect squealer detection via valid target highlighting) is a clever emergent detection method worth preserving as a "deduction" mechanic.

---

## 4. Territory Strategy — "Baby and Scare"

### The Confirmed Optimal Strategy

**In your territory / planned expansion areas:**
- Intimidate (raises fear, minimal hostility)
- Donate to charity, set up soup kitchens (keeps hostility low)
- Patrol and defend (protect businesses from rival attacks)
- Bribe local cops (suppresses squeal in their beat)
- Own the newspaper / get priests on side (shifts public opinion)
- Set up offices nearby (reduces distance penalty)

**In rival territory:**
- Raid, smash up, torch, bomb their protected businesses
- Owners blame the rival for failing to protect → hostility toward RIVAL rises → they leave rival's protection
- Ambush rival hoods to reduce their ability to maintain territory

**In neutral territory:**
- Do NOT raid or cause mayhem — you'll raise hostility against yourself
- Donate to charity first (lowers baseline hostility)
- Then intimidate key owners (raises fear, low hostility)
- Then extort with high-intimidation hoods
- Then patrol to defend the new territory
- Set up an office nearby to reduce distance penalty

### The Fear Trap

Over-intimidating causes fear to rise so high that squealing INCREASES. The sweet spot:
- High enough fear → they pay protection
- Low enough hostility → they don't resist or hate you
- Corrupt cop on payroll → catches any squeal that slips through despite fear

### Steel City Design Implication

The strategy emerges naturally from the Fear/Hostility/Squeal model. No special "territory strategy" system needs to be built — it should emerge from the interaction of existing systems. The key is making sure all the tools exist: intimidate order, charity donations, patrol/defense, cop bribery, newspaper ownership, priest influence, office placement.

---

## 5. Legal System — After Arrest

### The Chain

1. **Hood arrested** (police catch them walking, or investigation reaches warrant threshold)
2. **Legal Proceedings report appears** (requires Lawyer) — shows who's awaiting trial, judge, DA, case status
3. **Lawyer automatically defends** the hood in court
4. **Player can influence outcome**:
   - **Bribe the Judge** — "a suitably applied bribe will result in the case being immediately thrown out and your hood released"
   - **Bribe the DA** — first bribe gets witness OR juror list, second gets the other
   - **Intimidate witnesses** — persuade them to "forget what they saw"
   - **Intimidate jurors** — persuade them to state "Not guilty"
5. **Driving hoods are immune** to arrest checks en route — only walking hoods are vulnerable

### Steel City Design Implication

The legal system is a **post-arrest response system**, not a prevention system. Prevention is done through: lie low orders, clean up orders, bribing cops, having hoods drive instead of walk. The legal system kicks in when prevention fails. This should be a late-game system — early game, the player just loses hoods to arrests and learns to avoid heat.

---

## 6. Illegal Business — Front Matching

### The Similarity Rule

From manual page 39: "Try to set up an illegal business that is **similar in operation to** the legal business that will be the front. The more similar they are in their **operation or produce**, the less likely the F.B.I. are to find the site."

### Confirmed Illegal Business Types

| Illegal Business | Purpose | Best Front |
|-----------------|---------|------------|
| Speakeasy | Sells liquor, huge profit. "Second only to casinos." | Leisure/food venues |
| Casino | Most profitable illegal business | Entertainment venues |
| Still | Produces liquor for speakeasies | "Cost little, produce huge quantities" |
| Counterfeit Press | Produces counterfeit money | Not specified |
| Teamsters | Election fixing, skims union money | **Union buildings** (explicitly stated) |
| Office | Starting point for hoods, no income | Any (HQ starts with one) |
| Soup Kitchen | Increases popularity (costs money) | Commercial blocks |

### Goods System

Three types of illegal goods: **Liquor**, **Stolen Goods**, **Counterfeit Money**.
- Liquor → produced by stills → distributed to speakeasies → surplus to warehouses
- Stolen goods → taken from raided businesses → distributed to jewellers/pawn shops
- Counterfeit → produced on presses → distributed to legal businesses for change-making

### Steel City Design Implication

The front-matching system should use a **similarity score** between legal and illegal business types. The FBI detection rate is inversely proportional to this score. This creates interesting strategic decisions — buying the right legal business as a front matters as much as the illegal business itself.

---

## 7. Diplomacy System — Five Levels

### Confirmed from Manual

Five diplomacy levels between gang leaders, directly influencing hood behavior:

1. **Alliance** — Hoods cooperate, shared territory access
2. **Peace** — Normal relations, hoods ignore each other
3. **Cease-Fire** — Tense peace, hoods cautious
4. **Aggression** — Hoods attack on sight in contested areas
5. **Gang Warfare** — Full conflict, planned hits, territory invasion

From manual: "The more violent the setting, the more inclined hoods are to avoid a rival's territory and the more likely they are to open fire if they see any hoods from the opposing Gang."

### Snitches in Diplomacy

From manual page 91: "The snitches walk from one Gang area to another, hanging around with hoods and picking up information on Gang membership. This information is then sold to the other Gang Leaders in the Diplomacy Section."

Only **three snitches exist at one time** in the city. They're a limited resource that feeds intelligence to the diplomacy system.

### Steel City Design Implication

The diplomacy system should directly modify hood AI behavior in contested areas. The five-level system is simple enough to implement as an enum with behavior modifiers. Snitches should be a rare, contested resource — three max, walking the city, selling gang composition intel to whoever's in the diplomacy screen.

---

## Open Questions for Further Playtesting

- [ ] Does the Bomb target trick reliably identify squealers without a Lawyer?
- [ ] How fast does hostility decay naturally after stopping attacks?
- [ ] Does donating to charity in a hostile area actually lower hostility enough to re-extort?
- [ ] What's the practical fear threshold where squealing starts increasing?
- [ ] How does the "Take Over Protection" order (case 0x30) actually work in-game?
- [ ] Does manpower on extort orders noticeably increase success rate?
- [ ] How quickly do business owners leave your protection after rival attacks?
- [ ] Does owning a newspaper actually suppress negative stories about your crimes?

---

## System Interactions Summary

```
Territory Strategy
├── Intimidation (raises fear, low hostility) → Extortion success
├── Charity/Donations (lowers hostility) → Extortion success
├── Patrol/Defense (prevents rival attacks) → Owner retention
├── Cop Bribery (suppresses squeal) → Investigation prevention
├── Office Placement (reduces distance penalty) → Extortion success
├── Newspaper/Priest (shifts public opinion) → Hostility management
└── Rival Territory Attacks (raises rival hostility) → Owner defection from rival

Information Pipeline
├── No Lawyer → Indirect signals only (deduction game)
├── Lawyer → Squealers report, Crimes report, Legal Proceedings
├── DA Bribe → Witness/juror lists for active trials
└── Snitches (3 max) → Gang composition intel for diplomacy

Legal System (post-arrest)
├── Lawyer auto-defends
├── Bribe Judge → case dismissed
├── Bribe DA → witness/juror lists
├── Intimidate witnesses → "forget what they saw"
└── Intimidate jurors → "not guilty" verdict
```
