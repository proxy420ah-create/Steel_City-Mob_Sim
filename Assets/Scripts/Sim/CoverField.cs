using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Cover influence field — the influence-map layer of cover selection.
    /// Instead of evaluating a handful of discrete stand spots, each
    /// threat-facing wall column SPLATS cover potential into a ground-aligned
    /// grid; every touched cell gets the full validation (ground probe,
    /// free-space, authoritative threat→crouch-eye LOS) and a composite score
    /// of quality × proximity. The picker reads the field maximum; the heatmap
    /// texture renders the same values the decision consumes — the pipeline
    /// (exposure → quality → proximity → choice) is the visualization.
    ///
    /// Architecturally this is the shared, per-threat computation agents will
    /// eventually share: many combatants behind the same threat read one field
    /// locally rather than each running private scans (the flow-field /
    /// Dijkstra-map pattern). CoverSensor owns an instance today; wrapping it
    /// in a threat-bucketed service is the multi-agent landing.
    /// </summary>
    public sealed class CoverField
    {
        public enum Layer : byte { Composite = 0, Exposure = 1, Proximity = 2, Quality = 3,
            Walkable = 4,   // the declared domain — what the actor's mask admits + a body fits
            Reachable = 5 } // the actor's flood domain — walkable ∧ a corridor exists

        /// <summary>Cell verdict after validation — Exposure layer colors this directly.</summary>
        public enum CellState : byte { Untested = 0, Hidden = 1, Exposed = 2, Invalid = 3 }

        /// <summary>Declared walkability facts — the field owns what a cell IS:
        /// Grounded = the support-column probe found floor (material in floorMat);
        /// FitOk/FitBad = the 3-point body column verdict (tri-state: neither bit =
        /// untested). A cell is walkable for an actor iff Grounded ∧ FitOk ∧
        /// (empty floor mask, or floorMat ∈ mask). Consumers read; nothing
        /// re-derives.</summary>
        [System.Flags] public enum CellFlags : byte { Grounded = 1, FitOk = 2, FitBad = 4, WalkOk = 8, WalkBad = 16, SqOk = 32, SqBad = 64 }

        // Quality scale: Full 0.85..1.0, Crouch 0.8..0.2. Halo cells
        // (HaloDecay x neighbour) must stay below SeedMin <= every seed.
        public const float SeedMin = 0.19f;
        const float HaloDecay = 0.15f;
        float[] claim;   // per-cell squared distance to the owning splat

        public int N { get; private set; }                 // cells per side
        public float CellSize { get; private set; }
        public Vector3 Origin { get; private set; }        // world min corner (XZ snapped)
        public float GroundY { get; private set; }

        public CellState[] state;                          // verdict per cell
        public float[] quality;                            // wall-class weight (0..1), dilated for visuals
        public float[] proximity;                          // 1 at scan center → 0 at radius edge
        public float[] score;                              // composite (quality × proximity), Hidden cells only
        public float[] facing;                             // contributing wall's threat-facing dot
        public string[] volIdOf;                           // wall volume that generated the cell (null none)
        public Vector2[] outDirOf;                         // owning face's outward normal (XZ) — per-face zone key
        public short[] wallIdxOf;                          // owning wall-column index (COVER_HEIGHTFIELD Phase B field)
        public float[] groundY;                            // THE lazy ground cache — NaN unprobed, NoGround on miss (probed heights live for the Build)
        public float[] eyeTopOf;                           // owning column's silhouette top (validation eye height)
        public float[] eyeFwdOf;                           // owning column's eye forward offset
        public int[] floorMat;                             // material id of the probed floor voxel (0 = air/none)
        public CellFlags[] flags;                          // grounded/fit verdicts — the declared cell facts
        private ushort[] edgeFlags;                      // per cell: bits 0-7 = edge tested, 8-15 = edge open (dir index)
        const float NoGround = -9999f;
        VoxelCollisionWorld bWorld;
        List<CoverSensor.WallMark> bWalls;
        float bHShallow, bHDeep, bFwdShallow, bFwdDeep, bStandH, bMargin, bStandoff;
        public byte[] clsOf;                               // zone's WallClass verdict — from THIS cell's own floor
        public float[] reqDepthOf;                         // owning column's required crouch depth
        private float[] scratch;                           // dilation buffer

        public int TestedCells { get; private set; }
        public int ValidCells { get; private set; }
        public int ChosenCell { get; private set; } = -1;
        public bool HasData { get; private set; }
        /// <summary>True when the last Build ran against a real threat — cell
        /// states carry genuine Hidden/Exposed verdicts; false = passive
        /// quality-only paint (every state stays Untested).</summary>
        public bool BuiltWithThreat { get; private set; }

        /// <summary>Bumped every Build/Clear — map consumers key their
        /// rebuilds to it (a map stamped to an older build reads stale
        /// channels; never hold one across a scan).</summary>
        public int BuildStamp { get; private set; }
        private int walkMatStamp = -1;   // build stamp the walk-mat tally last ran on

        /// <summary>No threat → the field is empty; the heatmap idles transparent.</summary>
        public void Clear()
        {
            BuildStamp++;
            HasData = false;
            ChosenCell = -1;
            TestedCells = ValidCells = 0;
            if (state != null) System.Array.Clear(state, 0, state.Length);
            if (score != null) System.Array.Clear(score, 0, score.Length);
            zonesBuilt.Clear();
            zoneMembers.Clear();
            if (zoneOf != null) System.Array.Fill(zoneOf, -1);
        }

        // Rejected-cell reason tallies reuse the sensor's ScanStats.reasons.
        public readonly Dictionary<string, int> reasons = new();

        private static readonly Color32 COL_HIDDEN  = new Color32(40, 200, 255, 160);  // hidden → cool teal
        private static readonly Color32 COL_EXPOSED = new Color32(255, 60, 50, 170);   // exposed → hot red
        private static readonly Color32 COL_INVALID = new Color32(90, 90, 110, 90);    // unusable → dim slate
        private static readonly Color32 COL_CLEAR   = new Color32(0, 0, 0, 0);
        private static readonly Color32 COL_WALK      = new Color32(60, 230, 90, 150);   // walkable + preferred floor → green
        private static readonly Color32 COL_WALK_OFF  = new Color32(150, 210, 60, 140);  // walkable, unstamped floor → olive ("can, not preferred")
        private static readonly Color32 COL_NOWALK    = new Color32(255, 70, 50, 120);   // physically blocked → red
        private static readonly Color32 COL_REACHFAIL = new Color32(255, 140, 30, 180);  // walkable, no corridor → amber

        /// <summary>(Re)allocates buffers for a new resolution.</summary>
        public void EnsureSize(int n)
        {
            if (N == n && state != null) return;
            N = n;
            int cells = n * n;
            state = new CellState[cells];
            quality = new float[cells];
            proximity = new float[cells];
            score = new float[cells];
            facing = new float[cells];
            volIdOf = new string[cells];
            outDirOf = new Vector2[cells];
            wallIdxOf = new short[cells];
            zoneSeen = new bool[cells];
            zoneOf = new int[cells];
            groundY = new float[cells];
            eyeTopOf = new float[cells];
            eyeFwdOf = new float[cells];
            clsOf = new byte[cells];
            reqDepthOf = new float[cells];
            floorMat = new int[cells];
            flags = new CellFlags[cells];
            edgeFlags = new ushort[cells];
            scratch = new float[cells];
            claim = new float[cells];
        }

        /// <summary>
        /// Rebuild the field around <paramref name="pos"/> for the given threat.
        /// Each wall column paints a uniform standoff band behind its face
        /// (column-width × standoff — identical footprint per class), then
        /// every touched cell gets validated (ground, free-space, threat→eye
        /// LOS at the OWNING column's silhouette top).
        /// Score = proxWeight·proximity + (1−proxWeight)·quality on Hidden
        /// cells — proximity leads: the pick is the closest viable cover.
        /// </summary>
        public void Build(Vector3 pos, Vector3? threat, float radius, List<CoverSensor.WallMark> walls,
            VoxelCollisionWorld world, float standoff, float topOffset, float topFwd, float threatEye,
            float facingDot, float proxWeight, int res,
            float hDeep, float eyeFwdDeep, float standH = 0.62f, float depthMargin = 0.02f)
        {
            EnsureSize(res);
            BuildStamp++;
            bWorld = world; bHShallow = topOffset; bHDeep = hDeep; bFwdShallow = topFwd;
            bFwdDeep = eyeFwdDeep; bStandH = standH; bMargin = depthMargin;
            bWalls = walls; bStandoff = standoff;
            for (int i = 0; i < groundY.Length; i++) groundY[i] = float.NaN;
            System.Array.Clear(floorMat, 0, floorMat.Length);
            System.Array.Clear(flags, 0, flags.Length);
            System.Array.Clear(edgeFlags, 0, edgeFlags.Length);
            float size = radius * 2f;
            CellSize = size / N;
            HasData = true;
            // Snap origin to cell multiples so small moves don't shimmer the map.
            Origin = new Vector3(
                Mathf.Floor((pos.x - radius) / CellSize) * CellSize,
                pos.y,
                Mathf.Floor((pos.z - radius) / CellSize) * CellSize);
            GroundY = pos.y;
            BuiltWithThreat = threat.HasValue;

            // Shared walk layer: bulk-fill the declared-facts channels
            // (groundY, floorMat, Grounded/Fit flags) from the world-fixed
            // cache — each world cell probed ONCE ever, dirtied only by voxel
            // writes; every field window is a copy, not a re-derivation. When
            // the layer can't serve this cell size the arrays stay NaN and
            // the lazy probe paths below take over exactly as before.
            if (bWorld != null && bWorld.Walk != null)
                bWorld.Walk.FillWindow(Origin, N, CellSize, GroundY, walkBodyH,
                                       groundY, floorMat, flags,
                                       bodyHalfMeters, bodyStepUp);

            System.Array.Clear(state, 0, state.Length);
            System.Array.Clear(quality, 0, quality.Length);
            System.Array.Clear(proximity, 0, proximity.Length);
            System.Array.Clear(score, 0, score.Length);
            System.Array.Clear(facing, 0, facing.Length);
            System.Array.Clear(outDirOf, 0, outDirOf.Length);
            System.Array.Clear(wallIdxOf, 0, wallIdxOf.Length);
            System.Array.Clear(eyeTopOf, 0, eyeTopOf.Length);
            System.Array.Clear(eyeFwdOf, 0, eyeFwdOf.Length);
            System.Array.Clear(clsOf, 0, clsOf.Length);
            System.Array.Clear(reqDepthOf, 0, reqDepthOf.Length);
            for (int i = 0; i < claim.Length; i++) claim[i] = float.MaxValue;
            System.Array.Clear(volIdOf, 0, volIdOf.Length);
            reasons.Clear();
            TestedCells = ValidCells = 0;
            ChosenCell = -1;

            // Proximity is pure geometry — fill it for the whole disc once.
            float r2 = radius * radius;
            for (int cz = 0; cz < N; cz++)
            for (int cx = 0; cx < N; cx++)
            {
                Vector3 c = CellCenter(cx, cz);
                float dx = c.x - pos.x, dz = c.z - pos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                proximity[cz * N + cx] = 1f - Mathf.Sqrt(d2) / radius;
            }

            // ── PHASE 1: QUALITY (threat-independent) ──────────────────────
            // Every cover-capable wall paints its zone regardless of threat
            // facing — a crouch-height face is cover potential no matter who's
            // shooting; that judgement belongs to phase 2.
            // UNIFORM zone footprint: column-width × fixed depth rectangle
            // behind each face — identical shape for every column, so a tall
            // wall can't out-splat a closer low one (the old 0.55m discs
            // bulged past neighbours and stole cells via nearest-claim).
            // Depth = the old disc's outer reach; half-width = column width
            // or half a cell, whichever guarantees ≥1 cell painted.
            // UNIFORM STANDOFF BAND: the zone marks the STAND SPOT, flush to
            // the face — it no longer reaches out to fold him early. Early
            // duck during a combat approach is combat posture (the controller
            // drives anim.duck from the pick's requirement for the whole
            // advance); possess stays zone-bound — in the band = duck, out =
            // release.
            // The verdict (class, required depth, heat, validation eye) is
            // computed PER CELL from that cell's own floor — a curb or debris
            // under a cell raises him, shrinking the wall's rise there, so the
            // zone deepens his duck or drops out where the extra height pushes
            // him past usable cover. Height scale (red = stand, lighter =
            // stepping down through the crouch interpolation, unpainted =
            // too low; Low cells still CLAIM theirs so a taller neighbour
            // can't paint over a lower step):
            //   Full   rise > stand   → 0.85..1.0 by headroom above standing
            //   Crouch depth 0..1     → 0.80..0.20 (never overlaps Full)
            float halfW = CellSize * 0.5f;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                // Uniform standoff band — same footprint for every class; the
                // requirement-scaled corridor (2x..4x) is gone. Depth covers
                // the stand spot; guarantee ≥1 painted row on coarse grids.
                float depth = Mathf.Max(standoff, CellSize);
                SplatRect(w, Mathf.Max(w.vs * 0.5f, halfW), depth, i);
            }

            // A gap narrower than a body is not a gap: a lone Low column (notch,
            // step edge, seam between blocks) must not slit a continuous wall's
            // zone — and with it the posture verdict — in two.
            CloseGaps();

            // One dilation ring — smooths the splat into a continuous band so
            // the quality layer reads as zones, not polka dots. Decay 0.55×
            // keeps the ring below the test threshold (seeds carry the rays).
            System.Array.Copy(quality, scratch, quality.Length);
            // Coarse grids skip the halo — one ring would be metres wide and
            // read as fake cover.
            for (int cz = 1; cz < N - 1 && CellSize <= 0.5f; cz++)
            for (int cx = 1; cx < N - 1; cx++)
            {
                int idx = cz * N + cx;
                if (scratch[idx] > 0f) continue;
                float m = 0f;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    m = Mathf.Max(m, scratch[idx + dz * N + dx]);
                if (m > 0f) { quality[idx] = m * HaloDecay; }
            }

            // ── ZONES ARE A QUALITY-LAYER PRODUCT ────────────────────────
            // The uniform bands flood into per-face rectangles NOW — before
            // the threat pass — so the zone's shape and its centroid/stand
            // point are threat-independent geometry. Phase 2 decides which
            // zones are usable for THIS threat; it never reshapes them.
            BuildZones();

            // ── PHASE 2: THREAT (cheap combine) ────────────────────────────
            // Only quality cells get validated: ground probe → free-space →
            // threat-chest → silhouette-top LOS. Threat-facing filter skips
            // walls whose open side faces the shooter (LOS would expose anyway).
            // No threat → phase 2 skips entirely; the quality/proximity layers
            // still paint (passive cover awareness = the field's resting state).
            if (!threat.HasValue) return;
            Vector3 tp = threat.Value;
            Vector3 threatChest = tp + Vector3.up * threatEye;
            for (int cz = 0; cz < N; cz++)
            for (int cx = 0; cx < N; cx++)
            {
                int idx = cz * N + cx;
                if (volIdOf[idx] == null) continue;      // untouched — no wall contributes
                if (quality[idx] < SeedMin) continue;    // dilated halo — visual only
                // Band sits on the threat's side of its face — genuinely no
                // cover from this direction. Paint Exposed, not clear: the
                // threat view then shows the SAME footprint as quality, with
                // every seeded cell carrying its directional verdict.
                if (facing[idx] >= facingDot) { state[idx] = CellState.Exposed; Tally("facing"); continue; }
                TestedCells++;
                Vector3 c = CellCenter(cx, cz);

                // Phase 1 already probed this cell's floor — reuse it (gap-
                // filled cells were never probed, so those fall back to a ray).
                float fy = GroundYAt(idx);
                if (fy == NoGround)
                { state[idx] = CellState.Invalid; Tally("no-ground"); continue; }
                Vector3 standPt = new Vector3(c.x, fy, c.z);
                // Adaptive crouch: the cell's own verdict dictates the pose —
                // the LOS probe sits at the silhouette top for THIS cell's
                // required depth (standing for Full, interpolated for crouch).
                float cellTop = eyeTopOf[idx] > 0f ? eyeTopOf[idx] : topOffset;
                Vector3 eye = standPt + Vector3.up * cellTop + DirTo(tp, standPt) * eyeFwdOf[idx];

                var a = world.SamplePoint(standPt + Vector3.up * 0.15f);
                // Mid-band inside the column the pose actually needs — a
                // car's door band (or any cavity lip between shin and eye)
                // can't slip the low/high samples anymore. Scaled to cellTop
                // so a deep-crouch cell's legal overhead isn't over-rejected.
                var m = world.SamplePoint(standPt + Vector3.up * (cellTop * 0.5f));
                var s = world.SamplePoint(eye);
                if (a.kind == VoxelCollisionWorld.PointKind.TerrainSolid || a.kind == VoxelCollisionWorld.PointKind.VolumeSolid
                 || m.kind == VoxelCollisionWorld.PointKind.TerrainSolid || m.kind == VoxelCollisionWorld.PointKind.VolumeSolid
                 || s.kind == VoxelCollisionWorld.PointKind.TerrainSolid || s.kind == VoxelCollisionWorld.PointKind.VolumeSolid)
                { state[idx] = CellState.Invalid; Tally("occupied"); continue; }

                Vector3 seg = eye - threatChest;
                float dist = seg.magnitude;
                if (dist < 1e-4f || !world.Raycast(threatChest, seg / dist, dist - 0.05f, out _))
                { state[idx] = CellState.Exposed; Tally("exposed"); continue; }

                state[idx] = CellState.Hidden;
                ValidCells++;
                // Doctrine: take the IMMEDIATE cover — the closest valid cell —
                // quality only shades the choice (proxWeight 1 = pure closest,
                // 0 = pure best). "Better cover" seeking is a later,
                // intelligence-gated behaviour, not part of the first pick.
                score[idx] = proxWeight * proximity[idx] + (1f - proxWeight) * quality[idx];
            }
        }

        /// <summary>The cell's support column, probed lazily ONCE per Build —
        /// ground Y and the floor voxel's material id land together (the
        /// designation stamp rides the same cast). groundY semantics:
        /// NaN = unprobed, NoGround = probed with nothing under it.</summary>
        public float GroundYAt(int idx)
        {
            float gy = groundY[idx];
            if (float.IsNaN(gy))
            {
                Vector3 c = CellCenter(idx % N, idx / N);
                if (bWorld != null && bWorld.Raycast(c + Vector3.up * 1.6f,
                        Vector3.down, 3.2f, out VoxelRayHit gh))
                { groundY[idx] = gy = gh.point.y; floorMat[idx] = gh.material; flags[idx] |= CellFlags.Grounded; }
                else groundY[idx] = gy = NoGround;
            }
            return gy;
        }

        /// <summary>Uniform rectangular zone behind a wall column: column-wide
        /// × <paramref name="depth"/> out along its outDir. Same footprint for
        /// every wall, so cover areas can't gerrymander the pick; nearest
        /// column still owns overlapping cells at corners. The verdict for each
        /// cell is solved against THAT cell's floor (curbs/debris raise him).</summary>
        private void SplatRect(CoverSensor.WallMark w, float halfW, float depth, int wi)
        {
            Vector3 face = w.facePos, outDir = w.outDir;
            float tx = -outDir.z, tz = outDir.x;   // wall tangent
            float fx0 = face.x - tx * halfW, fz0 = face.z - tz * halfW;
            float fx1 = face.x + tx * halfW, fz1 = face.z + tz * halfW;
            float bx0 = fx0 + outDir.x * depth, bz0 = fz0 + outDir.z * depth;
            float bx1 = fx1 + outDir.x * depth, bz1 = fz1 + outDir.z * depth;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(fx0, fx1, bx0, bx1) - Origin.x) / CellSize));
            int x1 = Mathf.Min(N - 1, Mathf.FloorToInt((Mathf.Max(fx0, fx1, bx0, bx1) - Origin.x) / CellSize));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(fz0, fz1, bz0, bz1) - Origin.z) / CellSize));
            int z1 = Mathf.Min(N - 1, Mathf.FloorToInt((Mathf.Max(fz0, fz1, bz0, bz1) - Origin.z) / CellSize));
            for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                Vector3 c = CellCenter(cx, cz);
                float rx = c.x - face.x, rz = c.z - face.z;
                float u = rx * outDir.x + rz * outDir.z;   // distance out from the face
                float v = rx * tx + rz * tz;               // lateral offset along the wall
                if (u < 0f || u > depth || v < -halfW || v > halfW) continue;
                int idx = cz * N + cx;
                float d2 = u * u + v * v;
                if (d2 >= claim[idx]) continue;
                claim[idx] = d2;
                facing[idx] = w.facing;
                outDirOf[idx] = new Vector2(outDir.x, outDir.z);
                wallIdxOf[idx] = (short)wi;

                // Per-cell verdict from this cell's own floor.
                float fy = GroundYAt(idx);
                if (fy == NoGround) fy = w.support;   // phase 2 flags no-ground
                float rise = w.top - fy;
                // Duck-space solve (0..2): piecewise-inverts the same h(duck)
                // the renderer runs — stand→shallow morph (0..1), shallow→deep
                // (1..2). >2 = Low (no pose hides him). The morph band makes
                // mid-height walls yield a partial duck, never a quantized snap.
                float clear = rise - bMargin;
                float spanMorph = bStandH - bHShallow, spanDeep = bHShallow - bHDeep;
                byte cls; float d, qw, eyeTop, eyeFwd;
                if (rise > bStandH)
                {   // Full — hides a standing silhouette. Quality = peak, flat:
                    // covered is covered — surplus wall height is not a grade.
                    cls = (byte)CoverSensor.WallClass.Full; d = 0f; qw = 1f;
                    eyeTop = bStandH; eyeFwd = 0f;
                }
                else if (spanDeep <= 1e-4f && spanMorph <= 1e-4f)
                {   // No duck band exists at all — below stand = unhideable
                    cls = (byte)CoverSensor.WallClass.Low; d = 0f; qw = 0f; eyeTop = eyeFwd = 0f;
                }
                else
                {
                    float rawDuck = clear >= bHShallow && spanMorph > 1e-4f
                        ? (bStandH - clear) / spanMorph
                        : 1f + (bHShallow - clear) / Mathf.Max(1e-4f, spanDeep);
                    if (rawDuck > 2f)
                    {   // Too low from this floor — claims the cell, paints nothing
                        cls = (byte)CoverSensor.WallClass.Low; d = 0f; qw = 0f; eyeTop = eyeFwd = 0f;
                    }
                    else
                    {   // Duckable — d is the duck scalar for this cell
                        cls = (byte)CoverSensor.WallClass.Crouch;
                        d = Mathf.Clamp(rawDuck, 0f, 2f);
                        // HEAT follows the wall's actual rise (the height of the
                        // cover itself), not the pose depth: depth is 0 for the whole
                        // shallow band, which would paint every step in it the same.
                        // lo = rise where the deepest duck just hides him; stand = Full.
                        float lo = bHDeep + bMargin;
                        float frac = Mathf.Clamp01((rise - lo) / Mathf.Max(1e-3f, bStandH - lo));
                        qw = 0.2f + 0.6f * frac;
                        // Eye anchors ride the same piecewise h(duck).
                        eyeTop = d <= 1f ? Mathf.Lerp(bStandH, bHShallow, d)
                                         : Mathf.Lerp(bHShallow, bHDeep, d - 1f);
                        eyeFwd = d <= 1f ? Mathf.Lerp(0f, bFwdShallow, d)
                                         : Mathf.Lerp(bFwdShallow, bFwdDeep, d - 1f);
                    }
                }
                quality[idx] = qw;
                volIdOf[idx] = qw > 0f ? w.volId : null;
                eyeTopOf[idx] = eyeTop;
                eyeFwdOf[idx] = eyeFwd;
                clsOf[idx] = cls;
                reqDepthOf[idx] = d;
            }
        }

        /// <summary>Hottest Hidden cell — the field-native cover pick. Sets
        /// ChosenCell. `reach` prefilters: a cell the actor's map can't reach
        /// is not a pick (the unreachable-reject tally names it).</summary>
        public bool PickBest(out Vector3 point, out string volId, DistMap reach = null)
        {
            point = default; volId = null;
            float best = -1f; int bi = -1; int unreached = 0;
            for (int i = 0; i < score.Length; i++)
            {
                if (state[i] != CellState.Hidden || score[i] <= best) continue;
                if (reach != null && !reach.Reaches(i)) { unreached++; continue; }
                best = score[i]; bi = i;
            }
            if (unreached > 0) Tally("unreachable");
            if (bi < 0) return false;
            ChosenCell = bi;
            Vector3 c = CellCenter(bi % N, bi / N);
            point = new Vector3(c.x, groundY[bi], c.z);
            volId = volIdOf[bi];
            return true;
        }

        /// <summary>Nearest Hidden cell to `anchor` within `maxDist` — the
        /// debug "use THIS cover" pick (pit buttons). Same field, same
        /// validation; targets a specific box instead of the score max.
        /// `requireVol` pins the owning wall: splat discs overlap between
        /// adjacent cover, so the spatially-nearest cell can belong to a
        /// different box than the anchor names.</summary>
        public bool PickNear(Vector3 anchor, float maxDist, out Vector3 point, out string volId,
            string requireVol = null, DistMap reach = null)
        {
            point = default; volId = null;
            float best = maxDist * maxDist; int bi = -1;
            for (int cz = 0; cz < N; cz++)
            for (int cx = 0; cx < N; cx++)
            {
                int i = cz * N + cx;
                if (state[i] != CellState.Hidden) continue;
                if (requireVol != null && volIdOf[i] != requireVol) continue;
                if (reach != null && !reach.Reaches(i)) continue;
                Vector3 c = CellCenter(cx, cz);
                float dx = c.x - anchor.x, dz = c.z - anchor.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; bi = i; }
            }
            if (bi < 0) return false;
            ChosenCell = bi;
            Vector3 c2 = CellCenter(bi % N, bi / N);
            point = new Vector3(c2.x, groundY[bi], c2.z);
            volId = volIdOf[bi];
            return true;
        }

        /// <summary>Unpainted runs up to this wide (m), bounded by painted cells on
        /// both sides along a grid row/column, are filled from the LESS protective
        /// neighbour — roughly a body width.</summary>
        public const float GapCloseMeters = 0.3f;
        private readonly List<(int dst, int src)> fills = new();

        private void CloseGaps()
        {
            int g = Mathf.Max(1, Mathf.RoundToInt(GapCloseMeters / CellSize));
            fills.Clear();
            for (int pass = 0; pass < 2; pass++)
            for (int outer = 0; outer < N; outer++)
            {
                int last = -1;
                for (int inner = 0; inner < N; inner++)
                {
                    int idx = pass == 0 ? outer * N + inner : inner * N + outer;
                    if (volIdOf[idx] == null) continue;
                    int gap = inner - last - 1;
                    if (last >= 0 && gap > 0 && gap <= g)
                    {
                        int li = pass == 0 ? outer * N + last : last * N + outer;
                        int src = quality[li] <= quality[idx] ? li : idx;
                        for (int k = last + 1; k < inner; k++)
                            fills.Add((pass == 0 ? outer * N + k : k * N + outer, src));
                    }
                    last = inner;
                }
            }
            for (int i = 0; i < fills.Count; i++)
            {
                var (d, s) = fills[i];
                if (volIdOf[d] != null) continue;
                quality[d] = quality[s]; volIdOf[d] = volIdOf[s]; facing[d] = facing[s];
                outDirOf[d] = outDirOf[s];
                wallIdxOf[d] = wallIdxOf[s];
                eyeTopOf[d] = eyeTopOf[s]; eyeFwdOf[d] = eyeFwdOf[s];
                clsOf[d] = clsOf[s]; reqDepthOf[d] = reqDepthOf[s];
            }
        }

        /// <summary>Grid index of the cell containing pos, or −1 outside the
        /// field. Consumers use it to ask "which zone am I standing in".</summary>
        public int CellIndexOf(Vector3 pos)
        {
            int cx = Mathf.FloorToInt((pos.x - Origin.x) / CellSize);
            int cz = Mathf.FloorToInt((pos.z - Origin.z) / CellSize);
            return cx >= 0 && cx < N && cz >= 0 && cz < N ? cz * N + cx : -1;
        }

        public Vector3 CellCenter(int cx, int cz)
            => new Vector3(Origin.x + (cx + 0.5f) * CellSize, GroundY, Origin.z + (cz + 0.5f) * CellSize);

        public bool IsChosen(int idx) => idx == ChosenCell;

        /// <summary>One painted cover AREA = one stand point. Zones form on the
        /// QUALITY layer — a connected component of claimed cells (`volIdOf`
        /// set, quality ≥ SeedMin) sharing owner volume, wall class, required-
        /// duck band (¼-duck quantum) and FACE NORMAL — a box's front and side
        /// bands split at the corner instead of merging into an L whose
        /// centroid would fall inside the wall. The shape is threat-
        /// INDEPENDENT: the threat layer decides whether members are Hidden
        /// (usable); it never reshapes the zone. `stand` = the member cell
        /// nearest the band's GEOMETRIC center — the owning columns' face
        /// rectangle projected out by the band depth — squarely behind the
        /// wall's middle even when rasterization is ragged.</summary>
        public struct ZoneSpot
        {
            public Vector3 stand;       // band's geometric center, y = mean member ground
            public string volId;        // uniform — the split key includes it
            public Vector2 outDir;      // uniform — the owning face's outward normal
            public float reqD;          // mean required duck over the zone
            public CoverSensor.WallClass cls;  // uniform — the split key includes it
            public int cells;
            public int cell;            // member-cell index nearest the geometric center
        }

        static readonly int[] ZoneDX = { 1, -1, 0, 0 }, ZoneDZ = { 0, 0, 1, -1 };
        private bool[] zoneSeen;
        private int[] zoneOf;                    // cell → zone index (indices into zonesBuilt)
        private readonly Queue<int> zoneQueue = new Queue<int>();
        private readonly List<int> zoneCells = new List<int>(512);
        private readonly HashSet<int> ownerScratch = new HashSet<int>();
        private readonly List<ZoneSpot> zonesBuilt = new List<ZoneSpot>(64);
        private readonly List<int[]> zoneMembers = new List<int[]>(64);   // parallel to zonesBuilt — member cell indices
        private readonly List<(float d2, int cell)> rankScratch = new List<(float, int)>(512);

        /// <summary>The painted zone a cell belongs to — the auto-pick's
        /// centroid resolver (the scripted slots already land on
        /// `ZoneSpot.stand`; the picks were landing on wall-column
        /// midpoints instead of the marked middle). Zones are enumerated
        /// once per Build on the quality layer — this is an O(1) lookup.</summary>
        public bool TryZoneOfCell(int cellIdx, out ZoneSpot z)
        {
            z = default;
            if (zoneOf == null || cellIdx < 0 || cellIdx >= zoneOf.Length) return false;
            int zi = zoneOf[cellIdx];
            if (zi < 0) return false;
            z = zonesBuilt[zi];
            return true;
        }

        /// <summary>Point the chosen post at the cell the actor will
        /// actually stand on (the zone centroid cell, not the scanned pick).</summary>
        public void MarkChosen(int idx) => ChosenCell = idx;

        /// <summary>The farthest HIDDEN zone member along `dirXZ` — the
        /// safety boundary on that side for this threat. The Swing beat
        /// steps just past it to clear the corner (step-out distance
        /// adapts to the cover's width instead of a fixed margin). Zone
        /// membership is the quality band; only hidden members bound where
        /// it's safe to stand.</summary>
        public bool TryZoneEdge(int cellIdx, Vector3 dirXZ, out Vector3 point, out ZoneSpot z)
        {
            point = default; z = default;
            if (!TryZoneOfCell(cellIdx, out z)) return false;
            var members = zoneMembers[zoneOf[cellIdx]];
            float best = float.MinValue; int bc = -1;
            for (int i = 0; i < members.Length; i++)
            {
                int m = members[i];
                if (state[m] != CellState.Hidden) continue;
                var c = CellCenter(m % N, m / N);
                float d = (c.x - z.stand.x) * dirXZ.x + (c.z - z.stand.z) * dirXZ.z;
                if (d > best) { best = d; bc = m; }
            }
            if (bc < 0) return false;
            var p = CellCenter(bc % N, bc / N);
            point = new Vector3(p.x, groundY[bc], p.z);
            return true;
        }

        /// <summary>INT-modulated stand point inside a zone (COMBAT_COVER_STATE
        /// → "Cover positioning quality — the INT axis"): HIDDEN members ranked
        /// by distance² from the quality-layer centroid; t=1 lands on the
        /// hidden member nearest the center (max margin), t=0 on the farthest
        /// hidden rim cell (Hidden but zero margin — "sticking out"). Linear
        /// between. The zone's shape/centroid are quality-owned; the threat
        /// layer only gates which members are legal. `point` = the ranked
        /// cell's world stand (per-cell probed ground); `standCell` = its
        /// index for MarkChosen.</summary>
        public bool TryRankInZone(int cellIdx, float intNorm, out Vector3 point, out int standCell, out ZoneSpot z,
            DistMap reach = null)
        {
            point = default; standCell = -1; z = default;
            if (!TryZoneOfCell(cellIdx, out z)) return false;
            int zi = zoneOf[cellIdx];
            var members = zoneMembers[zi];
            rankScratch.Clear();
            for (int i = 0; i < members.Length; i++)
            {
                int m = members[i];
                if (state[m] != CellState.Hidden) continue;
                if (reach != null && !reach.Reaches(m)) continue;   // unreachable members can't host the stand
                var c = CellCenter(m % N, m / N);
                float dx = c.x - z.stand.x, dz = c.z - z.stand.z;
                rankScratch.Add((dx * dx + dz * dz, m));
            }
            if (rankScratch.Count == 0) return false;
            rankScratch.Sort((a, b) => a.d2.CompareTo(b.d2));
            int rank = Mathf.RoundToInt((1f - Mathf.Clamp01(intNorm)) * (rankScratch.Count - 1));
            standCell = rankScratch[rank].cell;
            var sc = CellCenter(standCell % N, standCell / N);
            point = new Vector3(sc.x, groundY[standCell], sc.z);
            return true;
        }

        /// <summary>The zones enumerated by the last Build — zones are a
        /// quality-layer product, cached per build; this just copies the
        /// list. Returns the count.</summary>
        public int EnumerateZones(List<ZoneSpot> out_)
        {
            out_.Clear();
            out_.AddRange(zonesBuilt);
            return out_.Count;
        }

        /// <summary>Member ground Y — the phase-1 floor probe, lazily probed
        /// for gap-closed cells (fills inherit no floor). NoGround cells fall
        /// back to the scan height; they're Invalid for play anyway.</summary>
        private float MemberY(int i) => StandY(i);

        /// <summary>Flood-fills the QUALITY footprint into per-face zones;
        /// runs once per Build, before the threat pass. Membership = claimed
        /// band cells (`volIdOf` set, quality ≥ SeedMin — the uniform
        /// rectangles, not the threat-carved Hidden residue); split key =
        /// (volume, class, ¼-duck band, face normal). `stand` is the member
        /// CELL nearest the band's GEOMETRIC center — the owning wall
        /// columns' face extent (`wallIdxOf` → their `facePos`/`vs`) projected
        /// out by the band depth. A cell mean would follow rasterization
        /// asymmetries (corner claim-stealing, gap fills); the projected
        /// rectangle is the physical offer. Threat verdicts gate member
        /// legality at query time (TryRankInZone/TryZoneEdge pick Hidden
        /// members only); they never reshape the zone.</summary>
        private void BuildZones()
        {
            zonesBuilt.Clear();
            zoneMembers.Clear();
            if (state == null) return;
            System.Array.Clear(zoneSeen, 0, zoneSeen.Length);
            System.Array.Fill(zoneOf, -1);
            for (int s = 0; s < state.Length; s++)
            {
                if (zoneSeen[s] || volIdOf[s] == null || quality[s] < SeedMin) continue;
                string vol = volIdOf[s];
                byte cls = clsOf[s];
                int q = Mathf.RoundToInt(reqDepthOf[s] * 4f);
                Vector2 outD = outDirOf[s];
                zoneSeen[s] = true;
                zoneQueue.Clear(); zoneQueue.Enqueue(s);
                zoneCells.Clear();
                float sx = 0f, sz = 0f, gy = 0f, rd = 0f;
                while (zoneQueue.Count > 0)
                {
                    int i = zoneQueue.Dequeue();
                    zoneCells.Add(i);
                    var c = CellCenter(i % N, i / N);
                    sx += c.x; sz += c.z; gy += MemberY(i); rd += reqDepthOf[i];
                    int cx = i % N, cz = i / N;
                    for (int d = 0; d < 4; d++)
                    {
                        int x = cx + ZoneDX[d], z = cz + ZoneDZ[d];
                        if (x < 0 || x >= N || z < 0 || z >= N) continue;
                        int j = z * N + x;
                        if (zoneSeen[j] || volIdOf[j] == null || quality[j] < SeedMin) continue;
                        if (volIdOf[j] != vol || clsOf[j] != cls) continue;
                        if (Mathf.RoundToInt(reqDepthOf[j] * 4f) != q) continue;
                        // Corner split: a box's two faces share the volume —
                        // the face normal must match too or their bands merge
                        // into an L whose centroid lands in the concavity.
                        if (Vector2.Dot(outDirOf[j], outD) < 0.999f) continue;
                        zoneSeen[j] = true; zoneQueue.Enqueue(j);
                    }
                }
                int n = zoneCells.Count;
                float mx = sx / n, mz = sz / n;
                // GEOMETRIC CENTER — the member-cell mean is a raster
                // statistic: claim-stealing at corners, gap fills and terrace
                // raggedness all skew it. The true stand is the CENTER OF
                // THE FACE'S GROUND PROJECTION — the owning columns' face
                // line extended out by the band depth (the wireframe box's
                // middle). Owner columns' facePos give the real extents.
                Vector2 t2 = new Vector2(-outD.y, outD.x);   // face tangent
                float uMin = float.MaxValue, uMax = float.MinValue;
                float tMin = float.MaxValue, tMax = float.MinValue;
                ownerScratch.Clear();
                foreach (var i in zoneCells)
                {
                    int wi = wallIdxOf[i];
                    if (wi < 0 || wi >= bWalls.Count || !ownerScratch.Add(wi)) continue;
                    var m = bWalls[wi];
                    float uf = m.facePos.x * outD.x + m.facePos.z * outD.y;
                    float tc = m.facePos.x * t2.x + m.facePos.z * t2.y;
                    float hw = m.vs * 0.5f;
                    if (uf < uMin) uMin = uf;
                    if (uf > uMax) uMax = uf;
                    if (tc - hw < tMin) tMin = tc - hw;
                    if (tc + hw > tMax) tMax = tc + hw;
                }
                if (uMin <= uMax)
                {
                    // Band rect: u ∈ [uMin, uMax+depth], t ∈ [tMin, tMax].
                    float uC = (uMin + uMax + Mathf.Max(bStandoff, CellSize)) * 0.5f;
                    float tC = (tMin + tMax) * 0.5f;
                    mx = outD.x * uC + t2.x * tC;
                    mz = outD.y * uC + t2.y * tC;
                }
                int bestCell = zoneCells[0]; float bd = float.MaxValue;
                foreach (var i in zoneCells)
                {
                    var c = CellCenter(i % N, i / N);
                    float d = (c.x - mx) * (c.x - mx) + (c.z - mz) * (c.z - mz);
                    if (d < bd) { bd = d; bestCell = i; }
                }
                var standC = CellCenter(bestCell % N, bestCell / N);
                int zoneIdx = zonesBuilt.Count;
                zonesBuilt.Add(new ZoneSpot
                {
                    stand = new Vector3(standC.x, gy / n, standC.z),
                    volId = vol, outDir = outD, reqD = rd / n,
                    cls = (CoverSensor.WallClass)cls, cells = n, cell = bestCell
                });
                foreach (var i in zoneCells) zoneOf[i] = zoneIdx;
                var mem = new int[zoneCells.Count];
                zoneCells.CopyTo(mem);
                zoneMembers.Add(mem);
            }
        }

        /// <summary>World XZ of the chosen cell center (for the post marker).</summary>
        public Vector3 ChosenPoint => ChosenCell >= 0 ? CellCenter(ChosenCell % N, ChosenCell / N) : default;

        private static Vector3 DirTo(Vector3 to, Vector3 from)
        {
            Vector3 d = to - from; d.y = 0f;
            float m = d.magnitude;
            return m > 1e-4f ? d / m : Vector3.zero;
        }

        private void Tally(string reason)
            => reasons[reason] = reasons.TryGetValue(reason, out int n) ? n + 1 : 1;

        // ─── Declared walkability ───────────────────────────────────────
        // The field owns what a cell IS: Grounded (support column found),
        // Fits (the 3-point body column clears at that ground), Floor
        // (probed material ∈ the actor's designated set). Consumers ask —
        // nobody re-derives. All facts are lazy: probing rides GroundYAt
        // and verdicts cache in flags for the Build's life.

        /// <summary>The actor's ground signature — HALF the body column's
        /// square side in METERS (0 = point column, the legacy test). Stamped
        /// by the sensor from VoxelCharacter.BodyFootprint: every Walkable
        /// call certifies the CONTINUOUS square the mover sweeps — one
        /// footprint, one predicate, answered in meters not cells. No K
        /// rounding, no grid padding: the erosion lands exactly half a body
        /// off the hazard face.</summary>
        public float bodyHalfMeters = 0f;
        /// <summary>Deck tolerance across the footprint — the square may
        /// straddle a curb, never a ledge (mirrors the movers' stepUp).</summary>
        public float bodyStepUp = 0.15f;
        /// <summary>The actor's body height the shared walk layer should cache
        /// fit verdicts for — stamped by the sensor beside the footprint spec
        /// (one build's lag is irrelevant; the value is stable per fighter).</summary>
        public float walkBodyH = 1.8f;
        /// <summary>A cell is walkable for an actor iff Grounded ∧ the body
        /// column fits ∧ the whole footprint square anchored there is
        /// physically placeable — and "placeable" IS the mover's own verdict:
        /// TryBodySquare (square free, else a step-up offered + raised square
        /// free) at the anchor's ground point. Map and body now run the
        /// LITERAL same question — certified ≡ executable by construction.
        /// PURE PHYSICS, no designation gate: "can" is the body's question;
        /// "should" (material preference) lives on the flood as cost, never
        /// as a ban. The verdict is cached per cell per build (the flood asks
        /// up to 8× per cell; probing and the fit test run lazily and cache).</summary>
        public bool Walkable(int idx, float bodyH)
        {
            if (bWorld == null || groundY == null) return false;
            var w = flags[idx] & (CellFlags.WalkOk | CellFlags.WalkBad);
            if (w != 0) return w == CellFlags.WalkOk;
            bool ok = GroundYAt(idx) != NoGround
                      && Passable(idx, bodyH)
                      && (bodyHalfMeters <= 0f || SquareOk(idx, bodyH));
            flags[idx] |= ok ? CellFlags.WalkOk : CellFlags.WalkBad;
            return ok;
        }

        /// <summary>The footprint-square verdict — filled from the world walk
        /// layer when it serves this window (spec-keyed cache), or lazily
        /// probed + flagged here on non-canonical grids.</summary>
        bool SquareOk(int idx, float bodyH)
        {
            var w = flags[idx] & (CellFlags.SqOk | CellFlags.SqBad);
            if (w != 0) return w == CellFlags.SqOk;
            bool ok = bWorld.TryBodySquare(GroundPoint(idx), bodyHalfMeters,
                                           bodyH, bodyStepUp, out _);
            flags[idx] |= ok ? CellFlags.SqOk : CellFlags.SqBad;
            return ok;
        }

        /// <summary>The point-column verdict at ONE cell — the atomic fit
        /// primitive the footprint square composes (K² of these per call).</summary>
        bool Passable(int idx, float bodyH)
        {
            if ((flags[idx] & (CellFlags.FitOk | CellFlags.FitBad)) == 0)
                flags[idx] |= ColumnFits(idx, bodyH) ? CellFlags.FitOk : CellFlags.FitBad;
            return (flags[idx] & CellFlags.FitOk) != 0;
        }

        /// <summary>Edge between two walkable cells: the ground delta must be
        /// climbable AND the leg's midpoint must host the skinned square on
        /// real ground within step of BOTH anchors. Anchors certify positions;
        /// the hop between them was never swept — a pillar edge or seam pinch
        /// sitting between two certified cells snagged a body mid-leg (the
        /// flowfield-ped snag class: peds follow edges blind, so a certified
        /// edge must be an executable leg). One midpoint is enough: the leg
        /// is one cell (~0.2m) and the skinned square's span (±~0.25m)
        /// reaches across it; residual off-center pinches are the corridor
        /// string-pull's job (0.03m sweep), not the flood's.</summary>
        public bool EdgeOpen(int a, int b, float bodyH, float stepUp)
        {
            // Memoized per build per DIRECTION — the flood asks every edge
            // twice (once per endpoint) and multiple maps flood per scan; the
            // verdict is symmetric so a computed answer stamps both (a→b)
            // and (b→a). Same lazily-cached contract as the cell flags.
            int d = -1;
            if (edgeFlags != null)
            {
                int dx = (b % N) - (a % N), dz = (b / N) - (a / N);
                int i3 = (dz + 1) * 3 + (dx + 1);
                if (i3 >= 0 && i3 <= 8 && i3 != 4) d = i3 > 4 ? i3 - 1 : i3;
                if (d >= 0 && (edgeFlags[a] & (1 << d)) != 0)
                    return (edgeFlags[a] & (0x100 << d)) != 0;
            }
            // World-baked edge — the same midpoint on the same geometry under
            // the same body spec, amortized across every field and agent.
            // Canonical-size windows are cell-aligned, so field cells ARE
            // world cells here.
            bool canon = bWorld != null && bWorld.Walk != null
                      && Mathf.Abs(CellSize - WalkLayer.CellSize) < 1e-4f;
            int wax = 0, waz = 0;
            if (canon && d >= 0)
            {
                Vector3 ca = CellCenter(a % N, a / N);
                wax = Mathf.FloorToInt(ca.x / WalkLayer.CellSize);
                waz = Mathf.FloorToInt(ca.z / WalkLayer.CellSize);
                bool? baked = bWorld.Walk.TryEdge(wax, waz, d, bodyHalfMeters, bodyH, stepUp);
                if (baked.HasValue)
                {
                    edgeFlags[a] |= (ushort)(1 << d);
                    edgeFlags[b] |= (ushort)(1 << (7 - d));
                    if (baked.Value)
                    {
                        edgeFlags[a] |= (ushort)(0x100 << d);
                        edgeFlags[b] |= (ushort)(0x100 << (7 - d));
                    }
                    return baked.Value;
                }
            }
            bool verdict = EdgeOpenCompute(a, b, bodyH, stepUp);
            if (d >= 0)
            {
                int o = 7 - d;   // opposite direction — the edge is symmetric
                edgeFlags[a] |= (ushort)(1 << d);
                edgeFlags[b] |= (ushort)(1 << o);
                if (verdict)
                {
                    edgeFlags[a] |= (ushort)(0x100 << d);
                    edgeFlags[b] |= (ushort)(0x100 << o);
                }
                if (canon)
                    bWorld.Walk.SetEdge(wax, waz, d, verdict, bodyHalfMeters, bodyH, stepUp);
            }
            return verdict;
        }

        bool EdgeOpenCompute(int a, int b, float bodyH, float stepUp)
        {
            float ga = groundY[a], gb = groundY[b];
            if (float.IsNaN(ga) || float.IsNaN(gb) || ga == NoGround || gb == NoGround) return false;
            if (Mathf.Abs(ga - gb) > stepUp) return false;
            Vector3 mid = (CellCenter(a % N, a / N) + CellCenter(b % N, b / N)) * 0.5f;
            mid.y = Mathf.Max(ga, gb);
            // Real midpoint ground — the average would bury the feet sample
            // inside a curb face. A top out of step of either anchor is a
            // mid-leg wall (can't reach it) or gutter (can't climb out).
            if (!bWorld.TryColumnTop(mid, 0.5f, 2f, out float topY)
                || Mathf.Abs(topY - ga) > stepUp || Mathf.Abs(topY - gb) > stepUp)
                return false;
            mid.y = topY;
            return bWorld.SquareFree(mid, bodyHalfMeters, Mathf.Max(0.2f, bodyH));
        }

        /// <summary>Cell center at the cell's OWN probed ground — the point
        /// floods and corridor pulls work from. No-ground cells fall back to
        /// scan height (callers gate on Walkable first; the Y only matters
        /// on legal cells).</summary>
        public Vector3 GroundPoint(int idx)
        {
            Vector3 c = CellCenter(idx % N, idx / N);
            float gy = GroundYAt(idx);
            if (gy != NoGround) c.y = gy;
            return c;
        }

        /// <summary>Stand/display Y — lazily probed ground, falling back to
        /// scan height on no-ground cells so markers don't dive to the sentinel.</summary>
        public float StandY(int idx)
        {
            float gy = GroundYAt(idx);
            return gy == NoGround ? GroundY : gy;
        }

        /// <summary>The body column at the cell's ground point — delegates to
        /// the shared VoxelCollisionWorld.ColumnFree primitive (feet/waist/
        /// mid/chest/head at 0.06 / 0.35h / 0.55h / 0.75h / 0.9h — identical sampling to the movers'
        /// SquareFree and the corridor string-pull). A cell the field calls
        /// walkable is a cell the body can occupy.</summary>
        bool ColumnFits(int idx, float bodyH)
        {
            Vector3 p = CellCenter(idx % N, idx / N);
            p.y = groundY[idx];
            return bWorld.ColumnFree(p, Mathf.Max(0.2f, bodyH));
        }

        // ── Goal-seeded Dijkstra maps (Phase 1 of the unified field) ────────
        // The cell-resolution sibling of Pathfinder's FlowField: ONE flood
        // per goal-set, every consumer reads the map — reachability is a
        // lookup, not a per-pick re-derivation. Edge gates are IDENTICAL to
        // the old leg flood (Walkable ∧ EdgeOpen ∧ no corner-cutting), so
        // "the map reaches it" ≡ "a corridor exists" structurally.

        /// <summary>A reverse-Dijkstra product over the declared channel:
        /// dist = meters-to-nearest-seed along legal edges, next = the hop
        /// toward that seed. Stamped to a Build — a map must never outlive
        /// the channels it was flooded over.</summary>
        public sealed class DistMap
        {
            public float[] dist;
            public int[] next;              // hop toward nearest seed; −1 = seed or unreached
            public int stamp = -1;          // the BuildStamp this map was flooded against
            public int seed = -1;           // single-seed maps expose it for rebuild keys
            public int ReachedCount { get; internal set; }   // cells the flood served — the fenced diagnostic
            public const float INF = float.PositiveInfinity;

            public void Ensure(int cells)
            {
                if (dist == null || dist.Length < cells)
                { dist = new float[cells]; next = new int[cells]; }
            }
            public bool Reaches(int i) => i >= 0 && i < dist.Length && dist[i] < INF;
            public float CostFrom(int i) => Reaches(i) ? dist[i] : INF;
            public int Next(int i) => i >= 0 && i < next.Length ? next[i] : -1;
        }

        /// <summary>The cell itself if walkable, else the nearest walkable
        /// neighbor scanned by ring out to ring-2 — endpoint snapping for
        /// seeds and destinations (a body-width corner may clip the exact
        /// cell; ring-1 alone couldn't see over a one-cell pocket).</summary>
        public int NearestWalkable(int idx, float bodyH)
        {
            if (idx < 0) return -1;
            if (Walkable(idx, bodyH)) return idx;
            int cx = idx % N, cz = idx / N;
            for (int r = 1; r <= 2; r++)
            for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;   // ring cells only — nearer rings won first
                int nx = cx + dx, nz = cz + dz;
                if (nx < 0 || nx >= N || nz < 0 || nz >= N) continue;
                int ni = nz * N + nx;
                if (Walkable(ni, bodyH)) return ni;
            }
            return -1;
        }

        /// <summary>Cost multiplier for an edge INTO a cell whose floor
        /// material isn't in the actor's preferred set — designation is a
        /// preference (cheaper to path on), never a gate: "if we can, we
        /// should" — the domain is physics; the paint is the discount.</summary>
        public const float UnstampedCost = 2.0f;

        /// <summary>Goal-seeded Dijkstra over the walkable channel — one
        /// flood, all consumers read. Seeds are snapped to walkable cells;
        /// unreachable cells keep dist = INF (the prefilter verdict).
        /// `preferred` tilts the flood toward designated floor — edges into
        /// unstamped cells cost UnstampedCost×, so routes prefer the paint
        /// without it ever walling them in.</summary>
        public void BuildDistMap(DistMap map, int seedCell,
            System.Collections.Generic.ICollection<int> preferred, float bodyH, float stepUp)
            => BuildDistMap(map, new[] { seedCell }, preferred, bodyH, stepUp);

        public void BuildDistMap(DistMap map, int[] seeds,
            System.Collections.Generic.ICollection<int> preferred, float bodyH, float stepUp)
        {
            int cells = N * N;
            map.Ensure(cells);
            for (int i = 0; i < cells; i++) { map.dist[i] = DistMap.INF; map.next[i] = -1; }
            var open = new PriorityQueue<int, float>();
            map.seed = -1; map.ReachedCount = 0;
            bool pref = preferred != null && preferred.Count > 0;
            for (int k = 0; k < seeds.Length; k++)
            {
                int s = NearestWalkable(seeds[k], bodyH);
                if (s < 0) continue;
                if (map.dist[s] == 0f) continue;   // seeds may snap onto the same cell
                map.dist[s] = 0f; map.ReachedCount++;
                map.seed = s;
                open.Enqueue(s, 0f);
            }
            while (open.Count > 0)
            {
                int c = open.Dequeue();
                int cx = c % N, cz = c / N;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nx >= N || nz < 0 || nz >= N) continue;
                    int ni = nz * N + nx;
                    if (!Walkable(ni, bodyH)) continue;
                    if (!EdgeOpen(c, ni, bodyH, stepUp)) continue;
                    // No corner-cutting — a diagonal needs both orthogonals
                    // walkable (the body's axis slide can't squeeze a pinch).
                    if (dx != 0 && dz != 0 &&
                        (!Walkable(cz * N + cx + dx, bodyH) ||
                         !Walkable((cz + dz) * N + cx, bodyH)))
                        continue;
                    float step = (dx != 0 && dz != 0 ? 1.4142f : 1f) * CellSize;
                    if (pref && !preferred.Contains(floorMat[ni])) step *= UnstampedCost;
                    float gc = map.dist[c] + step;
                    if (gc >= map.dist[ni]) continue;
                    if (map.dist[ni] == DistMap.INF) map.ReachedCount++;
                    map.dist[ni] = gc; map.next[ni] = c;
                    open.Enqueue(ni, gc);
                }
            }
            map.stamp = BuildStamp;
        }

        /// <summary>Walk the map's next-hops from `cell` back to its seed —
        /// appends [cell … seed] to `out_` (reverse for seed→cell travel).
        /// False when the cell isn't reached.</summary>
        public bool ChainToSeed(DistMap map, int cell, System.Collections.Generic.List<int> out_)
        {
            if (!map.Reaches(cell)) return false;
            for (int c = cell; c >= 0; c = map.next[c])
            {
                out_.Add(c);
                if (map.next[c] < 0) break;
            }
            return out_.Count > 0;
        }

        /// <summary>Does the map reach ANY HIDDEN member of the zone that owns
        /// `cell`? Only hidden members can host a stand — a reachable Exposed
        /// member doesn't make the zone serve. The stand cell alone can read
        /// unreachable while a neighboring member connects — gate on the zone,
        /// not one cell.</summary>
        public bool ZoneReachable(int cell, DistMap map)
        {
            if (map == null) return true;
            if (zoneOf == null || cell < 0 || cell >= zoneOf.Length) return false;
            int zi = zoneOf[cell];
            if (zi < 0) return map.Reaches(cell);
            var members = zoneMembers[zi];
            for (int i = 0; i < members.Length; i++)
                if (state[members[i]] == CellState.Hidden && map.Reaches(members[i])) return true;
            return false;
        }

        /// <summary>
        /// Bake the active layer into a color buffer for the heatmap texture.
        /// Exposure colors every TESTED cell by verdict; the value layers show
        /// the splat/proximity fields; Composite shows score with the chosen
        /// cell flashed white. Walkable/Reachable read the DECLARED channels —
        /// the actor's own mask/body/map arrive as parameters (the field
        /// doesn't own an actor's designation).
        /// </summary>
        public void Bake(Color32[] px, Layer layer,
            System.Collections.Generic.ICollection<int> walkMask = null,
            float bodyH = 1.8f, DistMap reach = null)
        {
            // Reject-cause tally for red cells: once per build (Bake runs
            // per-frame but reasons only reset at Build — tallying unguarded
            // would inflate the count every frame).
            bool tallyMats = layer == Layer.Walkable && walkMatStamp != BuildStamp;
            if (tallyMats) walkMatStamp = BuildStamp;
            for (int i = 0; i < px.Length; i++)
            {
                switch (layer)
                {
                    case Layer.Walkable:
                        // THE domain, collapsed: unreachable IS unwalkable.
                        // green = fits ∧ flood-served on designated floor;
                        // olive = in-domain but unstamped material (cost,
                        // never a ban); amber = physically legal but the
                        // flood can't serve it — a sealed pocket, an island
                        // ("walk-island": can stand, can't get there — OUT
                        // of domain); red = physically impossible; clear =
                        // no ground. With no map (idle/pre-combat) the
                        // verdict falls back to the physical domain.
                        if (Walkable(i, bodyH))
                        {
                            bool island = reach != null && reach.stamp == BuildStamp
                                          && !reach.Reaches(i);
                            if (island)
                            {
                                px[i] = COL_REACHFAIL;
                                if (tallyMats) Tally("walk-island");
                                break;
                            }
                            bool unstamped = walkMask != null && walkMask.Count > 0
                                             && !walkMask.Contains(floorMat[i]);
                            px[i] = unstamped ? COL_WALK_OFF : COL_WALK;
                            if (unstamped && tallyMats) Tally($"walk-unstamped mat {floorMat[i]}");
                        }
                        else if ((flags[i] & CellFlags.Grounded) != 0)
                        {
                            px[i] = COL_NOWALK;
                            if (tallyMats)
                            {
                                if ((flags[i] & CellFlags.FitBad) != 0) Tally("walk-fit");
                                else Tally("walk-square");   // fits at center — the body's reach failed
                            }
                        }
                        else px[i] = COL_CLEAR;
                        break;
                    case Layer.Reachable:
                        // The flood domain: bright near the seed fading with
                        // real corridor cost; amber = the fenced diagnosis —
                        // designation says floor but no corridor exists
                        // (a sealed pocket reads as painted-but-unreachable).
                        if (!Walkable(i, bodyH))
                        {
                            px[i] = (flags[i] & CellFlags.Grounded) != 0
                                ? COL_NOWALK : COL_CLEAR;
                            break;
                        }
                        // No map → "reachable" is undefined, not failed:
                        // fall back to the walkable read. Amber only ever
                        // means a real flood declared the cell unreachable.
                        px[i] = reach == null ? COL_WALK
                            : reach.Reaches(i)
                                ? Heat(1f - Mathf.Clamp01(reach.CostFrom(i) / 12f), 200)
                                : COL_REACHFAIL;
                        break;
                    case Layer.Exposure:
                        px[i] = state[i] == CellState.Hidden ? COL_HIDDEN
                              : state[i] == CellState.Exposed ? COL_EXPOSED
                              : state[i] == CellState.Invalid ? COL_INVALID : COL_CLEAR;
                        break;
                    case Layer.Proximity:
                        px[i] = proximity[i] > 0f
                            ? Heat(proximity[i], 120)
                            : COL_CLEAR;
                        break;
                    case Layer.Quality:
                        // Whole-disc paint: cover-capable cells show the heat
                        // gradient; open air reads as a dim exposed-red wash —
                        // "the street sees you here."
                        px[i] = quality[i] > 0f ? Heat(quality[i], 170)
                              : proximity[i] > 0f ? new Color32(255, 60, 50, 34)
                              : COL_CLEAR;
                        break;
                    default: // Composite
                        px[i] = state[i] == CellState.Hidden ? Heat(score[i], 190)
                              : state[i] == CellState.Exposed ? COL_INVALID : COL_CLEAR;
                        if (i == ChosenCell) px[i] = new Color32(255, 255, 255, 220);
                        break;
                }
            }
        }

        /// <summary>
        /// Bake the field as a PACKED data texture for the raymarch debug view
        /// (V-key cover modes): R=score, G=quality, B=verdict code
        /// (0/.4/.7/.85/1.0), A=proximity. The shader decodes + colorizes.
        /// </summary>
        public void BakePacked(Color32[] px)
        {
            for (int i = 0; i < px.Length; i++)
            {
                byte verdict = i == ChosenCell ? (byte)217          // .85 — hidden + chosen
                             : state[i] == CellState.Hidden ? (byte)255
                             : state[i] == CellState.Exposed ? (byte)179
                             : state[i] == CellState.Invalid ? (byte)102
                             : (byte)0;
                px[i] = new Color32(
                    (byte)(Mathf.Clamp01(score[i]) * 255f),
                    (byte)(Mathf.Clamp01(quality[i]) * 255f),
                    verdict,
                    (byte)(Mathf.Clamp01(proximity[i]) * 255f));
            }
        }

        /// <summary>Blue → green → yellow → red heat ramp, 0..1.</summary>
        private static Color32 Heat(float t, byte alpha)
        {
            t = Mathf.Clamp01(t);
            byte r = (byte)(Mathf.Clamp01(t * 2f - 0.5f) * 255f);
            byte g = (byte)(Mathf.Clamp01(t < 0.5f ? t * 2f : 2f - t * 2f) * 220f);
            byte b = (byte)(Mathf.Clamp01(1f - t * 2f) * 255f);
            return new Color32(r, g, b, alpha);
        }
    }
}
