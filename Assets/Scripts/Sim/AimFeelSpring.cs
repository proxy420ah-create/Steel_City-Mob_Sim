using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Second-order spring on a small angular aim offset (radians) — the "physics-feel
    /// layer" from COMBAT_VEHICLE_DESIGN.md §14. Axis convention: x = up(+), y = right(+)
    /// in the aim frame; the consumer converts to a world-space rotation.
    ///
    /// The equilibrium is a wandering target (sway noise); recoil arrives as an impulse —
    /// a velocity kick plus a partial position snap (recoil is fast, ~50-80 ms, so part of
    /// it must land in a single frame or it reads as mush). Semi-implicit Euler is
    /// unconditionally stable for our stiffness range at 60 Hz.
    ///
    /// Stacking is bounded: a second impulse while the spring is still displaced adds
    /// velocity to a non-zero position, so rapid fire sprays progressively wider —
    /// but pos/vel are clamped (anti-windup, the iMax lesson from the SteelTide
    /// PIDAssistant) so it saturates instead of flailing.
    /// </summary>
    public struct AimFeelSpring
    {
        public Vector2 pos;   // accumulated offset (rad)
        public Vector2 vel;   // angular velocity (rad/s)

        /// <summary>Hard cap on total offset (rad) — the arm can never diverge further than this.</summary>
        public float maxPos;
        /// <summary>Hard cap on angular velocity (rad/s).</summary>
        public float maxVel;

        /// <summary>
        /// Integrate toward a (possibly moving) equilibrium offset. Steps at a fixed 1/120 s
        /// regardless of frame dt — explicit integration of a stiff spring (skill-10 gains
        /// are ωn≈31 rad/s) blows up on a single hitchy frame otherwise.
        /// </summary>
        public void Step(Vector2 target, float stiffness, float damping, float dt)
        {
            const float h = 1f / 120f;
            int guard = 64;   // hard ceiling on substeps per call
            while (dt > 0f && guard-- > 0)
            {
                float s = Mathf.Min(h, dt);
                Vector2 a = (target - pos) * stiffness - vel * damping;
                vel += a * s;
                pos += vel * s;
                dt -= s;
            }
            pos = Vector2.ClampMagnitude(pos, maxPos);
            vel = Vector2.ClampMagnitude(vel, maxVel);
        }

        /// <summary>
        /// Recoil kick: instant displacement (the visible muzzle jump) plus a velocity
        /// carry for follow-through. The snap scales by remaining headroom to the cap, so
        /// hammering FIRE saturates smoothly instead of walking the arm off-target forever.
        /// </summary>
        public void Impulse(Vector2 kick)
        {
            float headroom = Mathf.Clamp01(1f - pos.magnitude / Mathf.Max(1e-6f, maxPos));
            pos += kick * (0.35f * headroom);
            vel += kick * 8f;
            pos = Vector2.ClampMagnitude(pos, maxPos);
            vel = Vector2.ClampMagnitude(vel, maxVel);
        }

        public void Reset()
        {
            pos = Vector2.zero;
            vel = Vector2.zero;
        }

        /// <summary>
        /// Stiffness/damping from a desired settle time and damping ratio.
        /// settleSec ≈ time to return within ~2% of equilibrium; zeta 0.7-0.9 gives a
        /// fast, slightly-wobbly recovery (lower = more ring, higher = dead return).
        /// </summary>
        public static void Gains(float settleSec, float zeta, out float k, out float c)
        {
            float wn = 4f / Mathf.Max(0.02f, zeta * settleSec);
            k = wn * wn;
            c = 2f * zeta * wn;
        }
    }
}
