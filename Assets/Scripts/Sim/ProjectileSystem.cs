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

        [Header("Audio")]
        [Tooltip("Muzzle report level as a TRIM on the studio file — 1.0 = exactly what audio_studio " +
                 "authored. Recipe: StreamingAssets/sounds/gunshot.stsound (hot-reloads on save).")]
        [Range(0.1f, 2f)] public float shotAudioVolume = 1f;
        [Tooltip("Full-volume radius of the report (m) — the 'strength' zone")]
        [Range(0.5f, 20f)] public float shotRefDistance = 4f;
        [Tooltip("Audible edge of the report (m) — linear fade dies here; this IS the NPC hearing radius")]
        [Range(5f, 200f)] public float shotAudibleRange = 50f;

        [Header("Visuals (PathDebugRenderer beams)")]
        [Tooltip("Draw the in-flight tracer streak behind each bullet.")]
        public bool showTracers = false;
        [Tooltip("Draw red tick marks at impact points (last 8 kept).")]
        public bool showImpactMarks = false;  // red hit ticks — off by default, Combat tab toggle
        public float tracerLength = 0.4f;
        public float tracerWidth = 0.012f;
        public Color tracerColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        [Tooltip("Seconds an impact tick stays on screen (the last 8 are kept).")]
        public float impactSeconds = 12f;
        public Color impactColor = new Color(1f, 0.2f, 0.15f, 0.95f);

        [Header("Spread cone viz (PathDebugRenderer)")]
        [Tooltip("Draw a wireframe cone at EffectiveConeDeg around the live bore axis of aimSource — edge rays + rim. Updates with the sliders and dances with sway/recoil.")]
        public bool showSpreadCone = false;   // wireframe cone — off by default, Combat tab toggle
        [Tooltip("Cone beam length override (world units). 0 = auto: to aimTarget, capped at 8 m.")]
        public float coneVizLength = 0f;
        public Color coneColor = new Color(1f, 0.7f, 0.2f, 0.55f);
        /// <summary>Set by the HUD each frame while its panel is up — the mount the cone follows.</summary>
        public WeaponMount aimSource;
        public Vector3? aimTarget;
        public float aimSourceStamp;

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
        /// <summary>Fires at trigger pull (before flight) — recoil/sound hooks subscribe here. ShotResolved fires at impact.</summary>
        public event Action<WeaponMount, ShotReport> ShotFired;
        public event Action<ShotReport> ShotResolved;

        /// <summary>Cone half-angle for the current skill + base cone.</summary>
        public float EffectiveConeDeg => baseConeDeg * SkillSpreadMultiplier(shooterSkill);

        /// <summary>Skill 10 → ×1 (the base cone), skill 1 → ×7.75. Linear: each skill point below 10 adds 0.75× the base cone.</summary>
        public static float SkillSpreadMultiplier(int skill) => 1f + (10 - Mathf.Clamp(skill, 1, 10)) * 0.75f;

        /// <summary>Palette ids that are see-through (StAssetReader: 112-114 window/storefront glass, 134 cracked glass, 147 auto glass). Display + shatter hook — single list lives in VoxelDamageMath.</summary>
        public static bool IsGlassMaterial(int mat) => VoxelDamageMath.IsGlassMaterial(mat);

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
            // Guarantee the damage consumer exists before this shot can resolve —
            // VDS lazily spawns via GetOrCreate, so a possess-mode shooter firing
            // before anyone opened Combat/Demolitions would otherwise resolve into
            // zero subscribers: tracers + impact marks, but no damage.
            VoxelDamageSystem.GetOrCreate();

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
            ShotFired?.Invoke(mount, r);   // after the direction is sampled — the kick only deflects FOLLOW-UP shots
            PlayShotSound(mount);
            return true;
        }

        /// <summary>Muzzle report — from sounds/gunshot.stsound (audio_studio recipe),
        /// played positionally at the muzzle. GetClip stat-checks the file each shot —
        /// studio saves hot-reload mid-session in editor/dev, bake-once in release.</summary>
        void PlayShotSound(WeaponMount mount)
        {
            var clip = ProceduralAudio.GetClip("sounds/gunshot.stsound");
            if (clip == null || shotAudioVolume <= 0f) return;
            ProceduralAudio.PlayAt(clip, mount.MuzzleWorld, shotAudioVolume,
                shotRefDistance, shotAudibleRange);
            SfxWaves.GetOrCreate().Emit(mount.MuzzleWorld, shotAudibleRange, shotRefDistance, "gunshot");
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
                if (Advance(live[i], dt)) live.RemoveAt(i);

            for (int m = 0; m < MarkerSlots; m++)
                if (markers[m].keys != null && Time.time >= markers[m].expireAt) ClearMarker(m);

            UpdateConeViz();
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
                if (showTracers)
                {
                    Vector3 tail = p.pos - dir * Mathf.Min(tracerLength, p.traveled);
                    pdr.SetCustomBeam(p.beamKey, tail, p.pos, tracerWidth, tracerColor);
                }
                else
                    pdr.ClearCustomBeam(p.beamKey);
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
                if (showImpactMarks) ShowImpact(h.point, h.normal);
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

        /// <summary>lowbias32 integer hash → [0,1). Public: seeded lateral recoil etc. hash off the shot seed.</summary>
        public static float Hash01(uint x)
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
        // Spread cone viz — wireframe of EffectiveConeDeg around the live bore axis
        // ------------------------------------------------------------------

        private const int ConeEdges = 6;
        /// <summary>Rim rings at full / half / quarter length — a single rim at the far end reads as a needle at range distances.</summary>
        private static readonly float[] ConeRingFrac = { 1f, 0.5f, 0.25f };
        private const float ConeBeamWidth = 0.008f;

        /// <summary>
        /// Draws 6 edge rays at the effective cone half-angle around aimSource.AimDirection plus a
        /// rim ring at the tips. Because it follows the live bore axis, the cone visibly dances
        /// with aim-feel sway/recoil and reshapes as the skill/cone sliders move.
        /// </summary>
        private void UpdateConeViz()
        {
            var pdr = PathDebugRenderer.Instance;
            bool show = showSpreadCone && aimSource != null && aimSource.IsEquipped
                        && Time.time - aimSourceStamp < 0.5f;
            if (pdr == null || !show) { ClearCone(pdr); return; }

            Vector3 o = aimSource.MuzzleWorld;
            Vector3 f = aimSource.AimDirection.normalized;
            float len = coneVizLength > 0f ? coneVizLength
                : (aimTarget.HasValue ? Mathf.Min(Vector3.Distance(o, aimTarget.Value), 8f) : 4f);
            float ang = EffectiveConeDeg;

            Vector3 refUp = Mathf.Abs(f.y) < 0.99f ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Cross(refUp, f).normalized;
            Vector3 edge0 = Quaternion.AngleAxis(ang, right) * f;

            for (int i = 0; i < ConeEdges; i++)
                pdr.SetCustomBeam($"cone_e{i}", o,
                    o + Quaternion.AngleAxis(i * (360f / ConeEdges), f) * edge0 * len,
                    ConeBeamWidth, coneColor);

            for (int j = 0; j < ConeRingFrac.Length; j++)
            {
                float r = len * ConeRingFrac[j];
                Vector3 prev = default, first = default;
                for (int i = 0; i < ConeEdges; i++)
                {
                    Vector3 tip = o + Quaternion.AngleAxis(i * (360f / ConeEdges), f) * edge0 * r;
                    if (i == 0) first = tip;
                    if (i > 0) pdr.SetCustomBeam($"cone_r{j}_{i}", prev, tip, ConeBeamWidth, coneColor);
                    prev = tip;
                }
                pdr.SetCustomBeam($"cone_r{j}_0", prev, first, ConeBeamWidth, coneColor);   // close ring
            }
        }

        private void ClearCone(PathDebugRenderer pdr)
        {
            if (pdr == null) return;
            for (int i = 0; i < ConeEdges; i++)
            {
                pdr.ClearCustomBeam($"cone_e{i}");
                for (int j = 0; j < ConeRingFrac.Length; j++)
                    pdr.ClearCustomBeam($"cone_r{j}_{i}");
            }
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

        /// <summary>Hide tracer beams only — in-flight bullets keep simulating.</summary>
        public void ClearTracers()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
                foreach (var p in live) pdr.ClearCustomBeam(p.beamKey);
        }

        /// <summary>Hide all drawn impact tick marks.</summary>
        public void ClearImpactMarks()
        {
            for (int m = 0; m < MarkerSlots; m++) ClearMarker(m);
        }

        /// <summary>Remove all tracers, impact ticks and the spread cone (in-flight bullets are dropped).</summary>
        public void ClearVisuals()
        {
            ClearTracers();
            live.Clear();
            ClearImpactMarks();
            ClearCone(PathDebugRenderer.Instance);
        }
    }
}
