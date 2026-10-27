# Render Floats Above Grounded Anchor (crouch pose beside wall)

**Date**: 2026-10-22
**Status**: 🟡 INVESTIGATING
**Severity**: 🟡 HIGH — visual truth violation; the rendered body doesn't sit where physics/nav say he is

## Symptoms
- Screenshot (2026-10-22): Vinny rendered ~1m off the ground beside a tall
  wall, legs bent in an awkward crouch. The green `cov_feet` X marker —
  `Feet()`, the nav anchor — sits **on the ground** at the correct XZ spot.
- Body clearly floats: legs dangling/bent, no contact with the floor plane.

## Root Cause
Unknown — investigating. What the screenshot rules OUT: the nav anchor is
grounded (green X on the floor = `transform.position + halfDims` at ground
level), so this is a **pose/render-side lift**, not a movement bug. The
posed voxel cluster renders above where the anchor says it stands.

## Hypotheses (ordered)
1. **Duck fold doesn't drop the pelvis** — the pose fold shortens/folds
   the legs but the render root stays at stand height → crouched body
   floats. (The duck lean is expected to displace mass *horizontally* —
   `eyeFwdOf` counts it — but no evidence it should lift Y. If the fold
   only reposes voxels without translating the root down, a mid/deep duck
   on open ground reads exactly like the screenshot.)
2. **coverContext/pose offset** applied while off-wall — a context flag
   intended for wall-braced crouch adding an up-bias on open ground.
3. **Terrace/step ground truth** — he may be standing at the edge of a
   raised cell: the `TryZoneEdge` rim point takes `groundY[bc]`; if the
   swing/stepped position is on a raised terrace the pose is honest but
   the *camera angle* reads as floating. Less likely — the green X
   grounds the anchor at floor level.

## Investigation Notes
- Discriminating check: watch the `off`/`pos` readout + which beat is live
  when it happens, and whether `anim.duck` is mid-range (0.3–0.7) — a
  floating body at partial fold while the anchor reads grounded proves
  hypothesis 1.
- Anchor-vs-render contract (GOTCHAS #13 method): `cov_feet` = anchor,
  `cov_dst` = destination, body = render. Here anchor ✓ grounded,
  render ✗ floating — the bug is downstream of the transform.
- Possibly related to the scout-posture duck channel (`searchCovered`
  folding on locally-Hidden ground) or the `Swing`/return leg's duck —
  repro during the search orbit test.

## Related Files
- `Assets/Scripts/Sim/CharacterAnimation.cs` — `anim.duck` channel, pose requests
- `Assets/Resources/Shaders/CharacterPoseCompute.compute` — the posed cluster the renderer draws
- `Assets/Scripts/Sim/CombatController.cs` — `targetDuck` writer, swing/return legs
- `docs/systems/VOXEL_ENGINE_GOTCHAS.md` — #13 anchor-vs-render diagnostic method

## Resolution
**2026-10-22 (part 2)** — The reported instance is explained and fixed at the
*selection* layer, not the render layer: the duck fold was being written by the
cover-context lanes (`targetDuck` destination req, scout `lreq`) **while the
arm was out and firing**. Armed + deep fold = a crumpled body with the gun
extended — the "deformed crouch" — and the fold's own lift/drop composition
mid-gait reads as floating. The fix is the fire-and-maneuver consolidation:
`ExposureArmed` (the canonical presenting verdict — same signal that picks
Aiming/AimSweep and gates fire) now owns the fold everywhere outside the rise
solve. Armed → duck eases to 0 (stand) in seek, scout, and HoldPosition lanes;
unarmed keeps the context fold. The only legal armed fold remains `riseDuck`
(the bore-clearance solve — firing over a parapet genuinely requires a partial
fold). OPEN REMAINDER: whether a *legitimately unarmed* deep fold (e.g.,
InCover settle behind a low wall) still renders the body above the anchor —
hypothesis 1 (fold drops not the pelvis) was NOT root-caused; the drop
channels (`bodyLower`/`modelLower`) exist in both CPU and GPU paths with
consistent sign, so if a clean unarmed crouch still floats, the loss is in
param propagation (e.g., `_AnimStaticParams` not carrying the wd/icr offsets
for this character), not in the fold math itself.
