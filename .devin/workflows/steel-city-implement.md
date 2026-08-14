---
description: Mandatory pre-implementation doc review before writing ANY Steel City code
---

# Steel City: Architecture-First Implementation

**Use this workflow before writing or modifying ANY code in SteelCityMobSim.**

## Step 1: Read the Index
- Read `docs/core/DOCUMENTATION_INDEX.md` completely

## Step 2: Identify Governing Docs
List every doc relevant to the task. When in doubt, include more.

## Step 3: Read Governing Docs COMPLETELY
Read each identified doc in full. Do NOT rely on session summaries, checkpoint context, or memory of prior reads. Read the actual file.

## Step 4: State Alignment
Before writing code, state:
- Which doc sections govern this work (cite file + section)
- What the documented architecture specifies
- How the planned implementation aligns with it

## Step 5: Divergence Check
If the planned implementation would diverge from what the docs specify:
- STOP
- Flag the divergence to the user with specific doc citations
- Do NOT write code until the user confirms whether to follow docs or update them

## Step 6: Implement
Only after steps 1-5 pass, write code.

## Key Docs (Never Skip)
- `docs/core/DESIGN_PHILOSOPHY.md`
- `docs/systems/3D_CITY_RENDERING.md`
- `docs/core/ENGINE_INTEGRATION_PLAN.md`
- `docs/core/REVERSE_ENGINEERING_FINDINGS.md`
- `docs/systems/SYSTEMS_OVERVIEW.md`
- `docs/VERTICAL_SLICE_DESIGN.md`

## When This Workflow Triggers
- Any new file in `Assets/Scripts/`
- Any modification to simulation, rendering, UI, or data systems
- Any time a session summary or checkpoint suggests an approach — verify against docs first
