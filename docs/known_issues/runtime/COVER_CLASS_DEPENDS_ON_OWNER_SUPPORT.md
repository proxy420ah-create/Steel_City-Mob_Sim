# Cover wall class/depth depended on the owner's own ground height

**Date**: 2026-08 (cover heightfield B2 playtest)
**Status**: 🟢 FIXED (pending Unity playtest)
**Severity**: 🟡 HIGH

## Symptoms
- Walking Vinny around changed the painted cover quality (and Full/Crouch/Low wall counts: `92F 282C` vs `148F 120C`) of the same static wall.
- User: "Nothing should be able to change the cover quality — it's an inherent stat."

## Root Cause
`CoverSensor.Scan` probed ONE `supportY` under the owner's feet and classified every wall in the scan radius against it (`Covers(w, supportY + hStand)`, `DepthForTop(top, supportY)`). Standing on the street (y0.10) vs the pad (y0.20) shifted every wall's effective height by 0.1m. The `QueryCoverWalls` prefilter used the same value.

## Resolution
Each wall is classified against the ground at its own stand-spot (`facePos + outDir*(standoff+vs*0.25)`), probed via `LocalSupport` (per-scan cache on a 0.1m grid). Prefilter loosened by 0.3m. Remaining owner-dependence is only the scan radius/field origin (what is in range), not any wall's class.

## Related
- `Assets/Scripts/Sim/CoverSensor.cs` — `Scan`, `LocalSupport`
- `docs/systems/COVER_HEIGHTFIELD.md` — Phase B per-cell support probe
