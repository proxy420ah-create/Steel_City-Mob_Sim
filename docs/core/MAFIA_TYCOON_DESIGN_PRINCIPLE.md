# Mafia Tycoon Design Principle

**Created**: August 6, 2026
**Status**: 🔒 ACTIVE — Guardrail against economic over-engineering

---

## The Line

Steel City is a **mafia tycoon** game, not a business management game or a city builder. The player is a crime boss, not an economist. The economy exists as a **weapon and a consequence**, not as a management interface.

### ✅ Mafia Tycoon (Right Side of the Line)

The player interacts with the economy through **criminal actions**:

- Bomb a rival's speakeasy → liquor supply drops → prices rise → rival loses income
- Torch a tenement block → land value drops → buy cheap → develop territory
- Raid a still → booze supply disrupted → speakeasies can't sell → rival gang weakens
- Extort a business → owner pays → your income rises → owner's financial stress increases
- Case a neighborhood with hoods → discover rival speakeasies → infer low supply / high demand → plan your move
- Buy a business as a front → legal income masks illegal operations → FBI suspicion stays low

The player **discovers** economic information through gang activity (spying, casing, scouting), not through dashboards. The player **disrupts** the economy through crime, not through market manipulation. The player **benefits** from economic consequences, not from economic management.

### ❌ Business / City Management (Wrong Side of the Line)

The player does NOT:

- Set liquor prices manually
- Manage citizen tax rates
- Adjust wages at businesses
- View supply/demand charts or spreadsheets
- Balance a city budget
- Manage supply chains directly (logistics, routing, inventory management)
- Set production quotas
- Adjust interest rates or inflation
- See raw economic data without gang intelligence gathering

### The Information Rule

Economic information is **gated behind gang activity**:

| What the Player Wants | How They Get It (Mafia Tycoon) | How They DON'T Get It (City Builder) |
|-----------------------|-------------------------------|--------------------------------------|
| Is there a speakeasy nearby? | Send a hood to case the area | Open a business report dashboard |
| Is liquor supply low? | Spy reveals few barrels at rival still | Check a supply/demand chart |
| Is a business struggling? | Owner is more compliant during extortion | View their P&L statement |
| Is land cheap? | Neighborhood looks run-down, buildings damaged | Check real estate listings |
| Is unemployment high? | Lots of idle citizens on streets, more recruits available | View unemployment statistics |
| Is a rival gang losing money? | Their hoods are fewer / less equipped | Check rival gang's financial report |

### The Action Rule

Economic change happens **through crime, not through management**:

| Player Goal | Mafia Tycoon Method | City Builder Method (Forbidden) |
|-------------|--------------------|-------------------------------|
| Reduce rival income | Bomb their speakeasy | Undercut their prices |
| Gain territory | Intimidate owner, take over | Buy property on open market (mostly) |
| Increase own income | Extort more businesses, run illegal goods | Optimize business operations |
| Disrupt rival supply chain | Raid their still, intercept shipments | Buy out their suppliers |
| Create cheap land | Bomb tenement blocks, drive out residents | Wait for market downturn |
| Recruit desperate people | Target unemployed citizens in poor areas | Post job listings |

---

## Why This Matters

The original Gangsters: Organized Crime already has economic systems (business states, goods flow, land value, income groups). Steel City deepens these systems with individual citizen finances and dynamic supply/demand. This creates a risk of scope creep into full economic management.

**The economy should be something the player disrupts, not something the player manages.**

When designing any economic feature, ask:

1. **Does the player interact with this through crime?** If yes, it's mafia tycoon.
2. **Does the player need a dashboard to understand it?** If yes, it's city builder — rework it.
3. **Can the player ignore this and still play?** If no, it's a management requirement — cut it or make it optional.
4. **Does this produce emergent consequences from criminal actions?** If yes, it's mafia tycoon.
5. **Does this require the player to optimize numbers?** If yes, it's business management — simplify it.

---

## Application to Current Design

### Individual Citizen Bank Accounts (Differentiator #1)

- ✅ **Mafia tycoon**: Citizens with empty accounts become desperate → easier to recruit, more likely to turn to crime, more susceptible to intimidation
- ✅ **Mafia tycoon**: Bombing a business puts employees out of work → unemployment rises in the area → more recruits available → more crime → more police attention
- ❌ **City builder**: Player sees individual citizen bank balances and tax records
- ❌ **City builder**: Player must ensure citizens have enough money to survive
- ❌ **City builder**: Player manages welfare, social programs, or employment policy

The bank account system is a **motivation engine for AI behavior**, not a player-facing management interface. The player never sees individual finances — they see the *effects* (desperate citizens, compliant owners, available recruits).

### Supply/Demand Economy

- ✅ **Mafia tycoon**: Hood cases a neighborhood → discovers rival speakeasy → infers demand is being met by competitor → plans raid to disrupt supply
- ✅ **Mafia tycoon**: Player's speakeasy is the only one in the area → high demand, high prices, high income → rival notices and bombs it
- ❌ **City builder**: Player views a supply/demand curve and adjusts production accordingly
- ❌ **City builder**: Player manually sets liquor prices to optimize profit
- ❌ **City builder**: Player manages logistics of barrel shipments between stills and speakeasies

Supply/demand is **simulated, not managed**. The player discovers it through scouting and feels it through income changes. They manipulate it through crime, not through market controls.
