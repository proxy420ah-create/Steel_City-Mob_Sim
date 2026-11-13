# Bake Occupancy Grid Decoded With Wrong Layout

**Date**: 2026-11-12
**Status**: 🟢 FIXED
**Severity**: 🟡 HIGH

## Symptoms
- Agent-measured beam/coffer/wall positions disagreed with what the user saw in the Commission scene.
- The hall pendant was "placed on the beam" four times and landed in a coffer or on the wrong beam each time.

## Root Cause
`office_complex.bake.json` `occ` is a base64 bit-grid laid out as `index = (x*H + y)*D + z`, bits MSB-first (`(byte >> (7 - (i & 7))) & 1`), with `[W,H,D] = dims`. Agent scripts (and the first `_plan.py`) assumed z-major, LSB-first. Results looked plausible but were wrong. A 6cm-step scan also left a further `.03m` error.

## Resolution
- Canonical decode is `solid()` in `thecommission.html` (`loadBuilding`). Copy it, do not reinvent it.
- `_plan.py` now reshapes `(W,H,D)` and transposes to `[z,y,x]`.
- World mapping: `wx=(cx-ax)*vs`, `wz=(cz-az)*vs`, `wy=cy*vs+yBase` (`ax=319, az=78, vs=.03, yBase=-.09`). Cell edges are `(c-ax)*vs .. (c+1-ax)*vs`.
- Ceiling levels: beam underside y39 (wy 1.08), coffer face y42 (wy 1.17). Pendant canopy y = face minus fixture height (.312): beam .768, coffer .858.
- Measure at exact cell resolution (1 cell), then confirm with a `_shot.mjs` probe looking ALONG the beam axis. The user's view is authoritative when it disagrees.

## Related Files
- `docs/_plan.py`, `docs/_shot.mjs`, `docs/thecommission.html`
