using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Pure geometry for the aim rig's bore-on-line arm solve (AIM_RIG.md phase 3).
    ///
    /// With the hand group's cumulative orientation fixed (bore → aim direction, sights → up) the elbow is the only
    /// free joint, so the muzzle can only sit on a SPHERE of radius L1 (upper-arm length) around
    /// <c>shoulder + qCum·(forearm + gunOffset)</c>. "Bore on the eye line" is therefore a line ∩ sphere problem:
    /// two intersections when reachable (take the far one — arm extended), otherwise the point on the sphere
    /// nearest the line (graceful degrade — always closer to the eye line than the legacy shoulder→target solve).
    /// </summary>
    internal static class AimRigGeometry
    {
        /// <param name="lineOrigin">A point on the sight line (eye, dropped by the sight gap).</param>
        /// <param name="lineDir">Unit direction of the sight line.</param>
        /// <param name="sphereCenter">Muzzle sphere center.</param>
        /// <param name="radius">Muzzle sphere radius (upper-arm length).</param>
        /// <param name="minAhead">The muzzle must be at least this far along the line.</param>
        /// <param name="residual">0 when the line is reachable; otherwise the distance the muzzle misses the line by.</param>
        public static Vector3 MuzzleOnLine(Vector3 lineOrigin, Vector3 lineDir, Vector3 sphereCenter,
            float radius, float minAhead, out float residual)
        {
            Vector3 q = lineOrigin - sphereCenter;
            float b = Vector3.Dot(lineDir, q);
            float disc = b * b - (q.sqrMagnitude - radius * radius);
            if (disc >= 0f)
            {
                residual = 0f;
                float s = -b + Mathf.Sqrt(disc);                 // far root: arm extended forward of the eye
                return lineOrigin + lineDir * s;
            }
            float sc = Mathf.Max(-b, minAhead);                  // closest approach of the line to the sphere center
            Vector3 pl = lineOrigin + lineDir * sc;
            Vector3 dir = pl - sphereCenter;
            float dl = dir.magnitude;
            if (dl < 1e-6f) { residual = 0f; return pl; }
            residual = dl - radius;
            return sphereCenter + dir / dl * radius;             // nearest point on the muzzle sphere
        }
    }
}
