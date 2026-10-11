# Business Taxonomy → Asset Generation — Cross-Layer Contract

**Created**: October 9, 2026
**Status**: 🔒 ACTIVE — pins the generator roadmap to RE data
**Source data**: `docs/xtx_decrypted/Economics.txt` (171 types), `Illegal Economics.txt` (14 types), `Business Suspicion.txt` (155 lines)
**Related**: `GANGSTERS_GAME_DATA.md`, `ZONING_DESIGN.md`, `CITY_LAYOUT_PIPELINE.md` (canonical block dims), `VOXEL_GLASS_TRANSPARENCY.md` (pane convention), `Assets/docs/VOXEL_ASSET_PIPELINE.md`

---

## Purpose

The asset generators don't build "buildings" — they build the **visual vocabulary of the economy**. This document is the contract between three layers:

1. **RE taxonomy** — what businesses exist in the original game's data
2. **Asset generation** — which voxel archetypes must exist to represent them
3. **City pipeline** — how Phase-3 seeding assigns types to slots so the simulated economy has a consistent visual surface

When the simulated economy rolls out, every business the sim seeds must resolve to a `.stasset`. This doc defines the type→archetype→generator mapping so that rollout is mechanical, not improvised.

---

## 1. The Original's Taxonomy (RE: Economics.xtx)

171 legal business types across **8 groups**:

| Group | Name | Count | Role | Asset class |
|---|---|---|---|---|
| 0 | Commercial | 117 | Extortion economy — shops, banks, hotels, restaurants | 64v slot businesses |
| 1 | Industrial | 33 | Factories, Docks, Steel Mill | Industrial tiles |
| 2 | Empty Land | — | Vacant plots | terrain tile |
| 3 | Residential | ~500 blocks | Plain apartments, landlord-run, **no gang income** | backdrop facades |
| 4 | Warehouse | **12 fixed** | Goods storage (300+15 cases), never bankrupt, FBI raid target | signature buildings |
| 5 | Charity | ~5 | Churches ×4, Orphanage | civic placements |
| 6 | Municipal | **14** | City Hall, Courthouse, Police/FBI HQ, Hospital, Museum, Schools, Power/Water Plant, Fire Dept, Public Baths, US Post, Employment Exchange, Railroad Terminal | EC civic buildings |
| 7 | **Interactive Residential** | — | **Tenement blocks** — buyable, extortable, profit group 3, $10k setup | **full-block mega-buildings** |

Plus **14 illegal businesses** (speakeasy, casino, still, press, card/dice, numbers, whorehouse, loan shark, prizefight, teamsters, insider trading, grifters, office) — **all occupy the block CENTER** (`Size=0` = center-only in RE data). The 3×3 block has positional semantics: **the courtyard slot is where the hidden economy lives**, concealed behind a legal front rated 0/1/2 by `Business Suspicion.xtx`.

## 2. The BRef Discovery — The Original's Own Archetype System

`Economics.xtx` column 23 (`building reference`) reveals the original's visual-archetype mapping: **171 types collapse to 7 building visuals**:

| BRef | Types | Membership | Our archetype |
|---|---|---|---|
| 0 | 17 | Signature civic/charity: Church, Orphanage, City Hall, Courthouse, FBI HQ, Police HQ, Fire Dept, Hospital, Museum, Power/Water Plant, Public Baths, RR Terminal, School, US Post, Empty Land | Unique signature assets (one-off or small family) |
| 1 | **101** | Standard small commercial: Accountants → Butchers → Jewelers → Shoe Store… | **"Storefront" archetype** — one 64v generator w/ signage/palette variants |
| 2 | 14 | Larger commercial: Auction Rooms, Auto Dealers, Bank, Hotel, Theater, Restaurant, Movie Theater, Dept stores | **"Large venue" archetype** — grander 64v or multi-frontage |
| 3 | 3 | Department Store, Bank (Large), Hotel (Large) | Signature large commercial |
| 4 | **33** | All Group-1 industrials: Abattoir → Weavers, Docks, Steel Mill | **"Factory" archetype** — industrial 64v/multi-slot |
| 5 | 2 | Residential, **Tenement block** | Residential facades + **full-block tenement** (Group 7 is interactive/buyable vs Group 3 backdrop) |
| 6 | 1 | Warehouse | Warehouse archetype |

The original drew ~7-10 building visuals for its entire economy. Our generator count needed: **~7 archetype families + ~17 signature civic assets** — not 171 generators.

### Deliberate divergence: per-type uniqueness (decided 2026-10-09)

BRef survives as a **placement contract and coverage fallback only** — never a visual plan. Each business type gets a generator producing *structurally* unique buildings: massing, setbacks, floor plans, story counts, window rhythm, and roofline all vary per type, and seeding varies them per instance. Two Butchers on adjacent blocks are different *shapes*, not recolors. The existing generator set proves the model (`generate_butcher_shop` vs `generate_diner` vs `generate_speakeasy` are structurally different programs — a diner's streamline band is not a bakery's gabled shopfront).

**The lot is the only invariant**: 64×64v footprint (or 192×192v full-block). Everything above the lot line is free. Variety tiers:

1. **Per-type generators** — unique structure per business type (the goal: a player learns blocks by their actual buildings, not by sprite#1)
2. **Per-instance structural variance** — seeded differences in massing/layout, not just dressing
3. **BRef fallback** — a type without its own generator still lands on a valid lot-fitting archetype, so coverage is never blocked

Identity work applies in **NP order** (~top 20 commercial types = most of the visible streetscape). Documented departure from the original (see `DIFFERENTIATION_FROM_ORIGINAL.md` §2).

### Generator lifecycle: loose → locked → catalog

| Phase | Mode | What varies | Purpose |
|---|---|---|---|
| **A — Loose** | Grammar fully open | Massing, wings, setbacks, floor plans, all per seed | Concept-art machine — generate candidates, curate the winners |
| **B — Locked** | Grammar pinned as constants | Seed drives dressing only (signage, palette drift, wear, clutter) | The type's design language is frozen — the reference model's measured spec IS the lock |
| **C — Catalog** | Locked + context params | Factor-driven variation: zone, land value, era | A stable design per type; remaining variance carries world information (industrial butcher ≠ EC butcher) |

Locking is mechanical: seeds already make generation deterministic, so "locked" = grammar branches become constants and the seeded surface shrinks to dressing params. Hand-authored reference assets (e.g. `tenement_block_0`) are Phase-B locks expressed in voxels — measure them into the generator.

## 3. Number Present (NP) — Empirical Priority

`NP` (column 7) = how many of each type exist at city start — the visual-frequency ranking:

| Rank | Business | NP | BRef |
|---|---|---|---|
| 1 | **Tenement block** | 32,000 | 5 |
| 2 | **Residential** | 9,984 | 5 |
| 3+ | Small commercial (Ladieswear, Drug/Gift/Laundry/Pawn/Perfume/Pool/Shoe…) | 1,152–2,304 each | **1** |
| … | Industrial types | 96–512 | 4 |
| … | Civic/municipal | 1–5 each | 0 |

**Residential = ~80% of the city's visual surface.** The tenement generator is unambiguously the highest-leverage asset — matching the session conclusion that `gen_tenement_block` is the crown jewel. Second is the BRef=1 storefront archetype: one generator covers **101 business types** including the highest-NP commercial entries.

## 4. Slot Semantics (Canonical Contract)

From `CITY_LAYOUT_PIPELINE.md` — slot = 64×64v, block = 192×192v (3×3), height free-form:

| Placement | Content | Semantics |
|---|---|---|
| Center slot (index 4) | Illegal businesses + courtyard | **Concealment** — RE `Size=0` = center-only. Front business on a perimeter slot; the center asset reads as a rear/service structure |
| Perimeter slots | Legal businesses (BRef 1–3) | Street-frontage facing outward |
| Full block (all 9) | Tenements (BRef 5), mega-structures | Self-contained courtyard, Group 7 |
| Industrial tiles | BRef 4 factories, Docks, warehouses | May warrant multi-slot spans (gap: no span mechanism today) |
| EC civic slots | BRef 0 signature buildings | `isEC` flag gates placement (ZONING_DESIGN) |

**Conventions all generators inherit** (baked 2026-10-09): windows = 1v pane + air bore, 8v doors, hollow interiors, `SLOT_W=64`/`BLOCK_W=192` constants, `analyze_building.py` validates footprint.

## 5. Current Coverage vs Needed

**Live in `BUILDING_GENERATORS`**: `butcher`, `bakery`, `barber`, `tenement_block`, `empty_land`.
**Trimmed** (awaiting 8v-door rebuild): diner, garage, apartments, speakeasy, casino, police_station, card_game, loan_shark, hq.

| Archetype (BRef) | Status | Target generator |
|---|---|---|
| Residential facade + Tenement (5) | 📐 tenement being rebuilt as `gen_tenement_block` | full-block, courtyard, NP-king |
| Small commercial storefront (1) | ⚠️ 3 live, others trimmed | `gen_storefront` — parametric (signage, awning, palette) |
| Large venue (2) | ❌ none | `gen_venue` — theater/bank/hotel frontage |
| Signature large commercial (3) | ❌ none | bespoke, low volume |
| Factory/industrial (4) | ❌ none | `gen_factory` — sheds, stacks, loading |
| Warehouse (6) | ❌ none | `gen_warehouse` — 12 fixed instances |
| Civic signatures (0) | ❌ none | 17 one-offs, tune by hand after archetypes |

## 6. Roadmap Order (NP-driven)

1. **`gen_tenement_block.py`** — Group 7 full-block, courtyard, reference-model spec (in progress)
2. **`gen_storefront.py`** — BRef=1 parametric storefront; unlocks ~101 types via type→archetype map
3. **Residential backdrop variants** — Group 3 plain facades (cheaper tenement cousins)
4. **`gen_factory.py`** — BRef=4 industrial; feeds warehouses/docks family
5. **`gen_venue.py` + signature assets** — civic/large commercial, hand-tuned
6. **Illegal-center assets** — speakeasy/casino rear-structure variants for slot 4

**Type→archetype resolution**: keep a `BREF_MAP` (business type → bref → generator) seeded from `Economics.txt` — the column literally exists for this purpose.

## 7. Pipeline Tie-In

```
RE data (Economics.txt: type, group, CC, NP, BRef)
        │
        ▼
Phase 2 zoning JSON (zone per block + civic placements)
        │
        ▼
Phase 3 seeding — distribute types per zone by CC/NP weights
        │   center slot reserved for illegal ops (front on perimeter)
        ▼
city_template.json "businesses" → generate_city_assets.py
        │
        ▼
BREF_MAP: biz_type → archetype → generate_*(seed) → .stasset → slot
```

**Invariants**:
- Every type the sim can seed must resolve through `BREF_MAP` to a live generator (fallback = storefront or empty_land, flagged).
- `city_template.json` currently holds only `empty_land` — Phase 3 output populates real types; do NOT overwrite `city_layout.json` (bespoke, references tenement_block_0 + shooting_range).
- Generators emit at canonical dims natively — no `np.repeat` upscale path (that produced the 4-deep-glass relic).

## 8. Open Questions

- Multi-slot spans for large industrial buildings — needs a `slot`-spanning placer feature (not modeled today).
- Front-pairing: does the speakeasy center asset inherit facade hints from its legal front type (suspicion matrix) or is it generic?
- Group 3 vs 7 visual distinction — should backdrop residential differ visibly from interactive tenements (owned = gameplay-relevant = should read as such)?
- `building_reference` reuse: original drew 1 sprite per BRef; we can exceed that (variants per archetype by seed) — confirm visual variety budget vs sector instance caps (CITY_SCALE_ARCHITECTURE O-series).
