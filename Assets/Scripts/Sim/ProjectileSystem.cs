using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Physical projectiles — no Rigidbody, no Collider, no PhysX (COMBAT_VEHICLE_DESIGN.md §13).
    ///
    /// A shot leaves the weapon's measured muzzle along its bore axis (WeaponMount.MuzzleWorld /
    /// AimDirection — i.e. wherever the IK solver actually pointed the gun, residual aim error
    /// included), perturbed by a seeded random cone (COMBAT_AUTOBATTLE.md "Cone of Fire Model":
    /// skill tightens the cone). It then flies at muzzleSpeed; every frame the swept segment it
    /// covered is ray-cast against the voxel world (VoxelCollisionWorld.Raycast: terrain grid +
    /// registered building volumes), so a fast bullet can never tunnel through a thin wall.
    ///
    /// On impact the system records a ShotReport — exact volume, voxel cell, material id, face
    /// normal, distance, flight time and how far the shot line passed from the intended target.
    /// That report is also precisely what later systems need: glass shatter wants the pane voxel,
    /// cover wants the blocking volume, damage wants the material.
    ///
    /// Not here yet (by design): character/vehicle hits (spatial hash, §13), penetration tables,
    /// damage, bullet holes. Tracers and impact ticks are drawn through PathDebugRenderer beams.
    /// </summary>
    public class ProjectileSystem : MonoBehaviour
    {
        public static ProjectileSystem Instance { get; private set; }

        public static ProjectileSystem GetOrCreate()
        {
            if (Instance != null) return Instance;
            var found = FindFirstObjectByType<ProjectileSystem>();
            if (found != null) { Instance = found; return found; }
            return new GameObject("ProjectileSystem").AddComponent<ProjectileSystem>();
        }

        [Header("Shooter")]
        [Tooltip("Firearms skill 1-10 (10 = best). The cone is baseConeDeg at skill 10 and widens as skill drops — see SkillSpreadMultiplier. Placeholder for the hood's Firearms stat.")]
        [Range(1, 10)] public int shooterSkill = 10;
        [Tooltip("Cone half-angle in degrees for a skill-10 shooter.")]
        [Range(0f, 5f)] public float baseConeDeg = 1f;
        [Tooltip("0 = fresh random seed per shot. Any other value reproduces the identical spread every shot (regression testing).")]
        public uint forcedSeed = 0;

        [Header("Ballistics")]
        [Tooltip("World units per second. The city is a miniature, so real-world m/s do not apply; ~40 crosses the 8 m range in ~0.2 s.")]
        public float muzzleSpeed = 40f;
        [Tooltip("Despawn after this much travel (world units).")]
        public float maxRange = 80f;
        [Tooltip("Despawn after this many seconds in flight.")]
        public float maxLifetime = 6f;
        [Tooltip("Downward acceleration (world units/s²). 0 = straight line.")]
        public float gravity = 0f;

        [Header("Visuals (PathDebugRenderer beams)")]
        public float tracerLength = 0.4f;
        public float tracerWidth = 0.012f;
        public Color tracerColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        [Tooltip("Seconds an impact tick stays on screen (the last 8 are kept).")]
        public float impactSeconds = 12f;
        public Color impactColor = new Color(1f, 0.2f, 0.15f, 0.95f);

        /// <summary>Everything about one resolved (or in-flight) shot.</summary>
        public struct ShotReport
        {
            public int id;
            public uint seed;
            public int skill;
            public float coneHalfDeg;        // effective cone half-angle used for this shot
            public float spreadDeg;          // angle actually drawn inside the cone
            public float aimErrorDeg;        // IK solver residual at the trigger pull (bore vs muzzle→target)
            public Vector3 muzzle;
            public Vector3 aimDir;           // bore axis at the trigger pull
            public Vector3 shotDir;          // after the cone perturbation
            public bool resolved;            // false while the bullet is still in flight
            public bool hit;                 // false = expired without touching anything
            public string target;            // volume id, "terrain", or "none"
            public int material;
            public Vector3Int cell;
            public Vector3 point;
            public Vector3 normal;
            public float distance;           // muzzle → impact (travelled path), world units
            public float flightTime;
            public bool hasIntended;
            public Vector3 intendedTarget;
            public float lateralMiss;        // closest approach of the shot LINE to the intended target
            public float impactOffset;       // impact point → intended target
        }

        public bool HasShot { get; private set; }
        public ShotReport LastShot { get; private set; }
        public int ShotsFired => shotCounter;
        public int InFlight => live.Count;
        public event Action<ShotReport> ShotResolved;

        /// <summary>Cone half-angle for the current skill + base cone.</summary>
        public float EffectiveConeDeg => baseConeDeg * SkillSpreadMultiplier(shooterSkill);

        /// <summary>Skill 10 → ×1 (the base cone), skill 1 → ×7.75. Linear: each skill point below 10 adds 0.75× the base cone.</summary>
        public static float SkillSpreadMultiplier(int skill) => 1f + (10 - Mathf.Clamp(skill, 1, 10)) * 0.75f;

        /// <summary>Palette ids that are see-through (StAssetReader: 112-114 window/storefront glass, 147 auto glass). Display + future shatter hook only.</summary>
        public static bool IsGlassMaterial(int mat) => mat == 112 || mat == 113 || mat == 114 || mat == 147;

        private sealed class Live
        {
            public ShotReport report;
            public Vector3 start, pos, vel;
            public float traveled, age;
            public string beamKey;
        }

        private struct Marker { public string[] keys; public float expireAt; }

        private const int MarkerSlots = 8;
        private readonly List<Live> live = new List<Live>();
        private readonly Marker[] markers = new Marker[MarkerSlots];
        private int markerCursor;
        private int shotCounter;
        private VoxelCollisionWorld world;

        void Awake() { Instance = this; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ClearVisuals();
        }

        /// <summary>
        /// Fire from the mount's muzzle along its bore axis + a seeded cone perturbation.
        /// aimErrorDeg / intendedTarget are bookkeeping for the report only.
        /// </summary>
        public bool Fire(WeaponMount mount, float aimErrorDeg = 0f, Vector3? intendedTarget = null)
        {
            if (mount == null || !mount.IsEquipped)
            {
                Debug.LogWarning("[Projectile] Fire rejected: no equipped weapon.");
                return false;
            }
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (world == null || !world.HasRaycastGeometry)
            {
                Debug.LogWarning("[Projectile] Fire rejected: no VoxelCollisionWorld geometry registered.");
                return false;
            }

            shotCounter++;
            uint seed = forcedSeed != 0 ? forcedSeed : MakeSeed(shotCounter);
            float cone = EffectiveConeDeg;
            Vector3 aim = mount.AimDirection.normalized;
            Vector3 dir = SampleCone(aim, cone, seed);

            var live1 = new Live
            {
                start = mount.MuzzleWorld,
                pos = mount.MuzzleWorld,
                vel = dir * muzzleSpeed,
                beamKey = $"proj_tracer_{shotCounter}",
                report = new ShotReport
                {
                    id = shotCounter,
                    seed = seed,
                    skill = shooterSkill,
                    coneHalfDeg = cone,
                    spreadDeg = Vector3.Angle(aim, dir),
                    aimErrorDeg = aimErrorDeg,
                    muzzle = mount.MuzzleWorld,
                    aimDir = aim,
                    shotDir = dir,
                    target = "none",
                    hasIntended = intendedTarget.HasValue,
                    intendedTarget = intendedTarget ?? Vector3.zero,
                }
            };
            var r = live1.report;
            if (r.hasIntended) r.lateralMiss = DistancePointToRay(r.intendedTarget, r.muzzle, dir);
            live1.report = r;
            live.Add(live1);
            return true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
                if (Advance(live[i], dt)) live.RemoveAt(i);

            for (int m = 0; m < MarkerSlots; m++)
                if (markers[m].keys != null && Time.time >= markers[m].expireAt) ClearMarker(m);
        }

        /// <summary>Move one projectile by dt, sweeping the covered segment through the voxel world. True when finished.</summary>
        private bool Advance(Live p, float dt)
        {
            p.age += dt;
            if (gravity != 0f) p.vel += Vector3.down * (gravity * dt);
            float speed = p.vel.magnitude;
            float step = speed * dt;
            Vector3 dir = speed > 1e-6f ? p.vel / speed : p.report.shotDir;

            if (step > 0f && world != null && world.Raycast(p.pos, dir, step, out VoxelRayHit hit))
            {
                p.traveled += hit.distance;
                p.pos = hit.point;
                Resolve(p, true, hit);
                return true;
            }

            p.pos += p.vel * dt;
            p.traveled += step;
            if (p.traveled >= maxRange || p.age >= maxLifetime)
            {
                Resolve(p, false, default);
                return true;
            }

            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
            {
                Vector3 tail = p.pos - dir * Mathf.Min(tracerLength, p.traveled);
                pdr.SetCustomBeam(p.beamKey, tail, p.pos, tracerWidth, tracerColor);
            }
            return false;
        }

        private void Resolve(Live p, bool hit, VoxelRayHit h)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null) pdr.ClearCustomBeam(p.beamKey);

            var r = p.report;
            r.resolved = true;
            r.hit = hit;
            r.flightTime = p.age;
            r.distance = p.traveled;
            if (hit)
            {
                r.target = h.volumeId ?? "terrain";
                r.material = h.material;
                r.cell = h.cell;
                r.point = h.point;
                r.normal = h.normal;
                if (r.hasIntended) r.impactOffset = Vector3.Distance(h.point, r.intendedTarget);
                ShowImpact(h.point, h.normal);
            }
            p.report = r;
            LastShot = r;
            HasShot = true;
            ShotResolved?.Invoke(r);

            Debug.Log(hit
                ? $"[Projectile] #{r.id} HIT {r.target} mat {r.material}{(IsGlassMaterial(r.material) ? " (glass)" : "")} cell {r.cell} dist {r.distance:F2}m flight {r.flightTime:F3}s | skill {r.skill} cone ±{r.coneHalfDeg:F2}° spread {r.spreadDeg:F2}° seed {r.seed:X8} aimErr {r.aimErrorDeg:F2}°" +
                  (r.hasIntended ? $" | lateral miss {r.lateralMiss:F3}m, impact→target {r.impactOffset:F3}m" : "")
                : $"[Projectile] #{r.id} MISS (expired after {r.distance:F1}m / {r.flightTime:F2}s) | skill {r.skill} cone ±{r.coneHalfDeg:F2}° seed {r.seed:X8}");
        }

        // ------------------------------------------------------------------
        // Spread
        // ------------------------------------------------------------------

        /// <summary>
        /// Deterministic direction inside a cone of the given half-angle around axis — uniform over the
        /// cone's disc of directions. Same seed → same direction.
        /// </summary>
        public static Vector3 SampleCone(Vector3 axis, float halfAngleDeg, uint seed)
        {
            axis.Normalize();
            if (halfAngleDeg <= 0f) return axis;
            float u1 = Hash01(seed);
            float u2 = Hash01(seed + 0x9E3779B9u);
            float theta = halfAngleDeg * Mathf.Deg2Rad * Mathf.Sqrt(u1);
            float phi = 2f * Mathf.PI * u2;
            Vector3 refUp = Mathf.Abs(axis.y) < 0.99f ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Cross(refUp, axis).normalized;
            Vector3 up = Vector3.Cross(axis, right);
            return (axis * Mathf.Cos(theta) + (right * Mathf.Cos(phi) + up * Mathf.Sin(phi)) * Mathf.Sin(theta)).normalized;
        }

        /// <summary>lowbias32 integer hash → [0,1).</summary>
        private static float Hash01(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352dU;
                x ^= x >> 15; x *= 0x846ca68bU;
                x ^= x >> 16;
                return (x & 0x00FFFFFFu) / 16777216f;
            }
        }

        private static uint MakeSeed(int shot)
        {
            unchecked
            {
                uint s = (uint)shot * 2654435761u ^ (uint)Environment.TickCount * 40503u ^ (uint)Time.frameCount * 2246822519u;
                return s == 0 ? 1u : s;
            }
        }

        private static float DistancePointToRay(Vector3 p, Vector3 origin, Vector3 dir)
        {
            float along = Vector3.Dot(p - origin, dir);
            return (p - (origin + dir * along)).magnitude;
        }

        // ------------------------------------------------------------------
        // Impact ticks + cleanup
        // ------------------------------------------------------------------

        private void ShowImpact(Vector3 point, Vector3 normal)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;

            int slot = markerCursor++ % MarkerSlots;
            ClearMarker(slot);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            normal.Normalize();
            Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(normal, t1);
            Vector3 p = point + normal * 0.004f;

            var keys = new[] { $"proj_hit_{slot}_n", $"proj_hit_{slot}_a", $"proj_hit_{slot}_b" };
            pdr.SetCustomBeam(keys[0], p, p + normal * 0.16f, 0.008f, impactColor);
            pdr.SetCustomBeam(keys[1], p - t1 * 0.06f, p + t1 * 0.06f, 0.008f, impactColor);
            pdr.SetCustomBeam(keys[2], p - t2 * 0.06f, p + t2 * 0.06f, 0.008f, impactColor);
            markers[slot] = new Marker { keys = keys, expireAt = Time.time + impactSeconds };
        }

        private void ClearMarker(int slot)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null && markers[slot].keys != null)
                foreach (var k in markers[slot].keys) pdr.ClearCustomBeam(k);
            markers[slot] = default;
        }

        /// <summary>Remove all tracers and impact ticks (in-flight bullets are dropped).</summary>
        public void ClearVisuals()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
                foreach (var p in live) pdr.ClearCustomBeam(p.beamKey);
            live.Clear();
            for (int m = 0; m < MarkerSlots; m++) ClearMarker(m);
        }
    }
}
