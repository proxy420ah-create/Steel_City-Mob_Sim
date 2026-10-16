using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Standing cover-awareness sense — the "mobster who always knows what's
    /// around him". Periodically scans registered volumes inside `radius` and
    /// derives cover-capable wall columns from each volume's lazy column profile
    /// (first-solid + contiguous span — an open-cockpit canopy reads as air at
    /// crouch height, so it can't masquerade as cover).
    ///
    /// Two layers:
    ///   PASSIVE (no threat): which walls exist, classed Low / Crouch / Full.
    ///   ACTIVE  (Threat set): only walls whose open side faces away from the
    ///     threat generate stand-spot candidates, each validated by ground probe,
    ///     free-space checks, and an authoritative threat→crouch-eye LOS ray.
    ///
    /// Every layer is drawable through PathDebugRenderer keyed beams (COVER tab
    /// toggles). CombatController is one consumer; the tab inspects it directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoverSensor : MonoBehaviour
    {
        [Header("Awareness")]
        [Tooltip("Radius scanned for cover obstacles (m).")]
        public float radius = 14f;
        [Tooltip("Gap between the stand spot and the obstacle face (m).")]
        public float standoff = 0.55f;
        [Tooltip("Crouched eye height — walls must cover this to be usable (m).")]
        public float crouchEye = 0.7f;
        [Tooltip("Standing cover height — walls reaching this are 'full' cover (m).")]
        public float standEye = 1.35f;
        [Tooltip("Columns with a contiguous span below this aren't walls at all (m).")]
        public float wallFloor = 0.25f;
        [Tooltip("Grid cell for de-duping stand spots along a wall (m).")]
        public float spotSpacing = 0.35f;
        [Tooltip("outDir·dirToThreat below this = the open side faces away from the threat.")]
        public float facingDot = -0.05f;
        [Tooltip("Threat chest height used for the LOS validation ray (m).")]
        public float threatEye = 1.3f;
        [Tooltip("Seconds between passive rescans.")]
        public float scanInterval = 0.4f;
        [Tooltip("Rescan early if the owner moved this far (m).")]
        public float rescanMoveDist = 0.4f;

        [Header("Visualization (COVER tab)")]
        public bool showRing = true;
        public bool showFootprints = true;
        public bool showWalls = true;
        [Tooltip("With a threat set, dim walls whose open side faces the threat.")]
        public bool tintWallFacing = true;
        public bool showCandidates = true;
        public bool showLos = false;
        public int maxWallBeams = 500;
        public int maxCandBeams = 60;

        /// <summary>Threat position — set by the COVER tab's PICK THREAT (shared
        /// with Combat's PICK AIM) and by CombatController.EnterCombat.</summary>
        public Vector3? Threat;
        public bool HasThreat => Threat.HasValue;

        public enum WallClass : byte { Low = 0, Crouch = 1, Full = 2 }

        public struct WallMark
        {
            public Vector3 facePos;      // world point on the open face — the spot anchor
            public Vector3 outDir;
            public float baseY, top;     // covered band (world Y)
            public WallClass cls;
            public float facing;         // outDir·dirToThreat — negative = faces the threat
            public string volId;
            public float vs;
        }

        public struct CandMark
        {
            public Vector3 point;
            public bool valid;
            public string reason;
            public string volId;
        }

        public struct ScanStats
        {
            public int obstacles, wallCols, wallsLo, wallsCrouch, wallsFull, facing, tested, valid;
            public Dictionary<string, int> reasons;
            public void Reset() { obstacles = wallCols = wallsLo = wallsCrouch = wallsFull = facing = tested = valid = 0;
                reasons ??= new Dictionary<string, int>(); reasons.Clear(); }
        }

        public ScanStats stats;
        public readonly List<WallMark> walls = new();
        public readonly List<CandMark> candidates = new();
        public int chosenIdx = -1;

        /// <summary>World-space head height above own ground in the Crouching pose —
        /// read off the posed skeleton when anim data exists (pose-authoritative),
        /// else the crouchEye slider fallback. Recomputed per scan.</summary>
        public float EyeOffset { get; private set; } = 0.7f;
        public bool EyePosed { get; private set; }

        private VoxelCollisionWorld world;
        private VoxelCharacter owner;
        private float nextScan;
        private Vector3 lastScanPos = new Vector3(1e9f, 0f, 0f);
        private readonly List<CoverObstacle> obstacleBuf = new();
        private readonly List<CoverWall> wallBuf = new();
        private readonly List<Vector3> candEyes = new();  // crouch-eye points (viz)
        private readonly HashSet<string> beamKeys = new();
        private readonly HashSet<long> spotGrid = new();

        void Awake()
        {
            world = FindFirstObjectByType<VoxelCollisionWorld>();
            owner = GetComponent<VoxelCharacter>();
        }

        void OnDisable() => ClearViz();

        /// <summary>Self position used for the scan center — XZ center like CombatController.Feet.</summary>
        private Vector3 SelfPos()
        {
            Vector3 p = transform.position;
            if (owner != null)
            {
                var d = owner.Dims;
                p += new Vector3(d.x * owner.voxelSize * 0.5f, 0f, d.z * owner.voxelSize * 0.5f);
            }
            return p;
        }

        void Update()
        {
            Vector3 p = SelfPos();
            if ((p - lastScanPos).sqrMagnitude >= rescanMoveDist * rescanMoveDist) Refresh(true);
            else if (Time.unscaledTime >= nextScan) Refresh();
        }

        /// <summary>Run the scan (rate-limited by callers — pass force for demand scans).</summary>
        public void Refresh(bool force = false)
        {
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (world == null) return;
            if (!force && Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + scanInterval;
            lastScanPos = SelfPos();
            Scan(lastScanPos);
            Redraw();
        }

        /// <summary>Nearest valid candidate to `from` (XZ distance). Sets chosenIdx.</summary>
        public bool TryPickCover(Vector3 from, out Vector3 point, out string volId)
        {
            point = default; volId = null; chosenIdx = -1;
            float best = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].valid) continue;
                Vector3 c = candidates[i].point;
                float d = (c.x - from.x) * (c.x - from.x) + (c.z - from.z) * (c.z - from.z);
                if (d >= best) continue;
                best = d; point = c; volId = candidates[i].volId; chosenIdx = i;
            }
            if (chosenIdx >= 0) Redraw();
            return chosenIdx >= 0;
        }

        // --- Scan pipeline ---

        void Scan(Vector3 pos)
        {
            stats.Reset();
            walls.Clear(); candidates.Clear(); candEyes.Clear();
            chosenIdx = -1;

            // Eye band = the head joint's height in the Crouching pose — "where
            // will my head be when I duck" — read live so re-authoring the pose
            // re-tunes cover automatically. Slider is the no-anim-data fallback.
            Vector3 hp = default;
            EyePosed = owner != null && owner.TryGetJointWorld(VoxelCharacter.JointHead,
                out hp, out _, animStateOverride: 5f);
            EyeOffset = EyePosed ? hp.y - pos.y : crouchEye;

            stats.obstacles = world.QueryVolumes(pos, radius, obstacleBuf);
            // Walls tall enough to matter, with an open side at crouch-eye band.
            world.QueryCoverWalls(pos, radius, wallFloor, pos.y + EyeOffset, wallBuf);
            stats.wallCols = wallBuf.Count;

            Vector3 tp = Threat ?? default;
            bool hasT = Threat.HasValue;
            for (int i = 0; i < wallBuf.Count; i++)
            {
                CoverWall w = wallBuf[i];
                WallClass cls = Covers(w, pos.y + standEye) ? WallClass.Full
                              : Covers(w, pos.y + EyeOffset) ? WallClass.Crouch : WallClass.Low;
                float facing = 0f;
                if (hasT)
                {
                    Vector3 dt = tp - w.facePos; dt.y = 0f;
                    facing = dt.sqrMagnitude > 1e-6f ? Vector3.Dot(w.outDir, dt.normalized) : 0f;
                }
                if (cls == WallClass.Full) stats.wallsFull++;
                else if (cls == WallClass.Crouch) stats.wallsCrouch++;
                else stats.wallsLo++;
                walls.Add(new WallMark
                {
                    facePos = w.facePos, outDir = w.outDir,
                    baseY = w.firstSolidY, top = w.spanTop, cls = cls,
                    facing = facing, volId = w.volumeId, vs = w.voxelSize,
                });
            }
            stats.facing = 0;
            if (hasT)
            {
                for (int i = 0; i < walls.Count; i++)
                    if (walls[i].cls != WallClass.Low && walls[i].facing < facingDot) stats.facing++;
                GenCandidates(pos, tp);
            }
        }

        /// <summary>Column covers a horizontal ray at world height `eye`: solid band spans it.</summary>
        private static bool Covers(CoverWall w, float eye)
            => w.firstSolidY <= eye && eye < w.spanTop;

        /// <summary>Threat-facing walls → stand spots → ground/free/LOS validation.</summary>
        private void GenCandidates(Vector3 pos, Vector3 threat)
        {
            spotGrid.Clear();
            float spacing = Mathf.Max(0.15f, spotSpacing);
            Vector3 threatChest = threat + Vector3.up * threatEye;
            for (int i = 0; i < walls.Count; i++)
            {
                WallMark w = walls[i];
                if (w.cls == WallClass.Low || w.facing >= facingDot) continue;
                Vector3 spot = w.facePos + w.outDir * (standoff + w.vs * 0.25f);
                // De-dupe spots along a wall run so candidates space out.
                long key = (long)Mathf.FloorToInt(spot.x / spacing) * 65536 + Mathf.FloorToInt(spot.z / spacing);
                if (!spotGrid.Add(key)) continue;

                stats.tested++;
                var cand = new CandMark { volId = w.volId, point = spot };
                // Ground probe — land on whatever is under the spot.
                if (!world.Raycast(spot + Vector3.up * 1.6f, Vector3.down, 3.2f, out VoxelRayHit gh))
                { cand.reason = "no-ground"; Reject(cand); continue; }
                cand.point = new Vector3(spot.x, gh.point.y, spot.z);
                Vector3 eye = cand.point + Vector3.up * EyeOffset;
                // Free space at ankle + crouch chest.
                var a = world.SamplePoint(cand.point + Vector3.up * 0.15f);
                var c = world.SamplePoint(eye);
                if (a.kind == VoxelCollisionWorld.PointKind.TerrainSolid || a.kind == VoxelCollisionWorld.PointKind.VolumeSolid
                 || c.kind == VoxelCollisionWorld.PointKind.TerrainSolid || c.kind == VoxelCollisionWorld.PointKind.VolumeSolid)
                { cand.reason = "occupied"; Reject(cand); continue; }
                // Authoritative check: threat chest → crouch eye must be blocked.
                Vector3 seg = eye - threatChest;
                float dist = seg.magnitude;
                if (dist < 1e-4f || !world.Raycast(threatChest, seg / dist, dist - 0.05f, out VoxelRayHit bh))
                { cand.reason = "exposed"; Reject(cand); continue; }
                cand.valid = true;
                stats.valid++;
                candidates.Add(cand);
                candEyes.Add(eye);
            }
        }

        void Reject(CandMark c)
        {
            c.valid = false;
            stats.reasons[c.reason] = stats.reasons.TryGetValue(c.reason, out int n) ? n + 1 : 1;
            candidates.Add(c);
            candEyes.Add(c.point + Vector3.up * EyeOffset);
        }

        // --- Visualization ---

        public void ClearViz()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) { beamKeys.Clear(); return; }
            foreach (var k in beamKeys) pdr.ClearCustomBeam(k);
            beamKeys.Clear();
        }

        void Beam(string key, Vector3 a, Vector3 b, float w, Color col)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;
            pdr.SetCustomBeam(key, a, b, w, col);
            beamKeys.Add(key);
        }

        void Redraw()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;
            ClearViz();
            Vector3 p = lastScanPos;

            if (showRing)
            {
                const int seg = 28;
                var col = new Color(0.3f, 0.85f, 1f, 0.5f);
                Vector3 prev = p + new Vector3(radius, 0.05f, 0f);
                for (int i = 1; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    Vector3 cur = p + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
                    Beam($"csr_{i}", prev, cur, 0.035f, col);
                    prev = cur;
                }
            }

            if (showFootprints)
            {
                var col = new Color(1f, 1f, 1f, 0.4f);
                for (int i = 0; i < obstacleBuf.Count; i++)
                {
                    var o = obstacleBuf[i];
                    // Local-frame rect (true silhouette, not the inflated AABB).
                    if (!world.TryGetVolume(o.id, out _, out int vw, out _, out int vd,
                            out Vector3 corner, out float vs, out float yaw)) continue;
                    Vector3 ctr = corner + new Vector3(vw * vs * 0.5f, 0f, vd * vs * 0.5f);
                    float rad = yaw * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                    Vector3 Rot(Vector3 lp) => ctr + new Vector3(lp.x * c + lp.z * s, 0f, -lp.x * s + lp.z * c);
                    Vector3 hx = new Vector3(vw * vs * 0.5f, 0f, 0f), hz = new Vector3(0f, 0f, vd * vs * 0.5f);
                    Vector3 a = Rot(-hx - hz), b = Rot(hx - hz), cc = Rot(hx + hz), d = Rot(-hx + hz);
                    a.y = b.y = cc.y = d.y = corner.y + 0.05f;
                    Beam($"csf_{i}_0", a, b, 0.03f, col);
                    Beam($"csf_{i}_1", b, cc, 0.03f, col);
                    Beam($"csf_{i}_2", cc, d, 0.03f, col);
                    Beam($"csf_{i}_3", d, a, 0.03f, col);
                }
            }

            if (showWalls)
            {
                int stride = Mathf.Max(1, walls.Count / Mathf.Max(1, maxWallBeams));
                int bi = 0;
                for (int i = 0; i < walls.Count; i += stride)
                {
                    WallMark w = walls[i];
                    Color col = w.cls == WallClass.Full ? new Color(0.2f, 1f, 0.45f, 0.8f)
                              : w.cls == WallClass.Crouch ? new Color(1f, 0.85f, 0.2f, 0.75f)
                              : new Color(1f, 0.3f, 0.25f, 0.45f);
                    if (tintWallFacing && HasThreat && w.cls != WallClass.Low && w.facing >= facingDot)
                        col *= 0.35f;   // coverable height, but the open side faces the threat
                    Vector3 c0 = new Vector3(w.facePos.x, w.baseY + 0.02f, w.facePos.z);
                    Vector3 c1 = new Vector3(w.facePos.x, w.top, w.facePos.z);
                    Beam($"csw_{bi++}", c0, c1, 0.04f, col);
                }
            }

            if (showCandidates && HasThreat)
            {
                int n = Mathf.Min(candidates.Count, maxCandBeams);
                for (int i = 0; i < n; i++)
                {
                    var cm = candidates[i];
                    bool chosen = i == chosenIdx;
                    Beam($"csc_{i}", cm.point, cm.point + Vector3.up * (chosen ? 1.2f : 0.9f),
                        chosen ? 0.08f : 0.04f,
                        chosen ? new Color(0.2f, 0.95f, 1f, 0.95f)
                        : cm.valid ? new Color(0.2f, 1f, 0.4f, 0.8f)
                                   : new Color(1f, 0.25f, 0.2f, 0.45f));
                }
            }

            if (showLos && HasThreat)
            {
                Vector3 tc = Threat.Value + Vector3.up * threatEye;
                int n = Mathf.Min(candidates.Count, 32);
                for (int i = 0; i < n; i++)
                {
                    Vector3 eye = candEyes[i];
                    Beam($"csl_{i}", tc, eye, 0.02f,
                        candidates[i].valid ? new Color(0.3f, 1f, 0.9f, 0.55f)
                                            : new Color(1f, 0.3f, 0.3f, 0.4f));
                }
            }
        }
    }
}
