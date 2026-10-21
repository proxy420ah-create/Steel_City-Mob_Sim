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
        public enum Layer : byte { Composite = 0, Exposure = 1, Proximity = 2, Quality = 3 }

        /// <summary>Cell verdict after validation — Exposure layer colors this directly.</summary>
        public enum CellState : byte { Untested = 0, Hidden = 1, Exposed = 2, Invalid = 3 }

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
        public float[] groundY;                            // probed ground height per tested cell
        public float[] eyeTopOf;                           // owning column's silhouette top (validation eye height)
        public float[] eyeFwdOf;                           // owning column's eye forward offset
        private float[] floorOf;                           // per-cell ground Y (NaN unprobed, NoGround = no hit)
        const float NoGround = -9999f;
        VoxelCollisionWorld bWorld;
        float bHShallow, bHDeep, bFwdShallow, bFwdDeep, bStandH, bMargin;
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

        /// <summary>No threat → the field is empty; the heatmap idles transparent.</summary>
        public void Clear()
        {
            HasData = false;
            ChosenCell = -1;
            TestedCells = ValidCells = 0;
            if (state != null) System.Array.Clear(state, 0, state.Length);
            if (score != null) System.Array.Clear(score, 0, score.Length);
        }

        // Rejected-cell reason tallies reuse the sensor's ScanStats.reasons.
        public readonly Dictionary<string, int> reasons = new();

        private static readonly Color32 COL_HIDDEN  = new Color32(40, 200, 255, 160);  // hidden → cool teal
        private static readonly Color32 COL_EXPOSED = new Color32(255, 60, 50, 170);   // exposed → hot red
        private static readonly Color32 COL_INVALID = new Color32(90, 90, 110, 90);    // unusable → dim slate
        private static readonly Color32 COL_CLEAR   = new Color32(0, 0, 0, 0);

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
            groundY = new float[cells];
            eyeTopOf = new float[cells];
            eyeFwdOf = new float[cells];
            clsOf = new byte[cells];
            reqDepthOf = new float[cells];
            floorOf = new float[cells];
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
            bWorld = world; bHShallow = topOffset; bHDeep = hDeep; bFwdShallow = topFwd;
            bFwdDeep = eyeFwdDeep; bStandH = standH; bMargin = depthMargin;
            for (int i = 0; i < floorOf.Length; i++) floorOf[i] = float.NaN;
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

            System.Array.Clear(state, 0, state.Length);
            System.Array.Clear(quality, 0, quality.Length);
            System.Array.Clear(proximity, 0, proximity.Length);
            System.Array.Clear(score, 0, score.Length);
            System.Array.Clear(facing, 0, facing.Length);
            System.Array.Clear(groundY, 0, groundY.Length);
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
                SplatRect(w, Mathf.Max(w.vs * 0.5f, halfW), depth);
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
                float fy = floorOf[idx];
                if (float.IsNaN(fy)) fy = ProbeFloor(c);
                if (fy == NoGround)
                { state[idx] = CellState.Invalid; Tally("no-ground"); continue; }
                groundY[idx] = fy;
                Vector3 standPt = new Vector3(c.x, fy, c.z);
                // Adaptive crouch: the cell's own verdict dictates the pose —
                // the LOS probe sits at the silhouette top for THIS cell's
                // required depth (standing for Full, interpolated for crouch).
                float cellTop = eyeTopOf[idx] > 0f ? eyeTopOf[idx] : topOffset;
                Vector3 eye = standPt + Vector3.up * cellTop + DirTo(tp, standPt) * eyeFwdOf[idx];

                var a = world.SamplePoint(standPt + Vector3.up * 0.15f);
                var s = world.SamplePoint(eye);
                if (a.kind == VoxelCollisionWorld.PointKind.TerrainSolid || a.kind == VoxelCollisionWorld.PointKind.VolumeSolid
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

        /// <summary>Ground Y under a cell centre (NoGround when nothing solid).</summary>
        private float ProbeFloor(Vector3 c)
            => bWorld.Raycast(c + Vector3.up * 1.6f, Vector3.down, 3.2f, out VoxelRayHit gh)
                ? gh.point.y : NoGround;

        /// <summary>Uniform rectangular zone behind a wall column: column-wide
        /// × <paramref name="depth"/> out along its outDir. Same footprint for
        /// every wall, so cover areas can't gerrymander the pick; nearest
        /// column still owns overlapping cells at corners. The verdict for each
        /// cell is solved against THAT cell's floor (curbs/debris raise him).</summary>
        private void SplatRect(CoverSensor.WallMark w, float halfW, float depth)
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

                // Per-cell verdict from this cell's own floor.
                float fy = floorOf[idx];
                if (float.IsNaN(fy)) fy = floorOf[idx] = ProbeFloor(c);
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

        /// <summary>Hottest Hidden cell — the field-native cover pick. Sets ChosenCell.</summary>
        public bool PickBest(out Vector3 point, out string volId)
        {
            point = default; volId = null;
            float best = -1f; int bi = -1;
            for (int i = 0; i < score.Length; i++)
            {
                if (state[i] != CellState.Hidden || score[i] <= best) continue;
                best = score[i]; bi = i;
            }
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
            string requireVol = null)
        {
            point = default; volId = null;
            float best = maxDist * maxDist; int bi = -1;
            for (int cz = 0; cz < N; cz++)
            for (int cx = 0; cx < N; cx++)
            {
                int i = cz * N + cx;
                if (state[i] != CellState.Hidden) continue;
                if (requireVol != null && volIdOf[i] != requireVol) continue;
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

        /// <summary>One painted cover AREA = one stand point. A zone is a
        /// connected component of Hidden cells sharing owner volume, wall
        /// class and required-duck band (¼-duck quantum — stepped terraces
        /// split per step, corner overlaps split per volume). `stand` = the
        /// cells' centroid at their mean probed ground — literally "the
        /// middle of that color gradient", not a per-face anchor.</summary>
        public struct ZoneSpot
        {
            public Vector3 stand;       // cell centroid, y = mean probed ground
            public string volId;        // uniform — the split key includes it
            public float reqD;          // mean required duck over the zone
            public CoverSensor.WallClass cls;  // uniform — the split key includes it
            public int cells;
            public int cell;            // member-cell index nearest the raw centroid
        }

        static readonly int[] ZoneDX = { 1, -1, 0, 0 }, ZoneDZ = { 0, 0, 1, -1 };
        private bool[] zoneSeen;
        private int[] zoneOf;                    // cell → zone index (indices into zonesBuilt)
        private readonly Queue<int> zoneQueue = new Queue<int>();
        private readonly List<int> zoneCells = new List<int>(512);
        private readonly List<ZoneSpot> zonesBuilt = new List<ZoneSpot>(64);
        private readonly List<int[]> zoneMembers = new List<int[]>(64);   // parallel to zonesBuilt — member cell indices
        private readonly List<(float d2, int cell)> rankScratch = new List<(float, int)>(512);

        /// <summary>The painted zone a cell belongs to — the auto-pick's
        /// centroid resolver (the scripted slots already land on
        /// `ZoneSpot.stand`; the picks were landing on wall-column
        /// midpoints instead of the marked middle). Re-enumerates fresh at
        /// call time — picks are rare (combat entry / relocate).</summary>
        public bool TryZoneOfCell(int cellIdx, out ZoneSpot z)
        {
            z = default;
            if (cellIdx < 0 || state == null || !BuiltWithThreat) return false;
            EnumerateZones(zonesBuilt);
            int zi = zoneOf[cellIdx];
            if (zi < 0) return false;
            z = zonesBuilt[zi];
            return true;
        }

        /// <summary>Point the chosen post at the cell the actor will
        /// actually stand on (the zone centroid cell, not the scanned pick).</summary>
        public void MarkChosen(int idx) => ChosenCell = idx;

        /// <summary>INT-modulated stand point inside a zone (COMBAT_COVER_STATE
        /// → "Cover positioning quality — the INT axis"): members ranked by
        /// distance² from the centroid; t=1 lands on the centroid cell (max
        /// depth), t=0 on the rim member (Hidden but zero margin — "sticking
        /// out"). Linear between. `point` = the ranked cell's world stand
        /// (per-cell probed ground); `standCell` = its index for MarkChosen.</summary>
        public bool TryRankInZone(int cellIdx, float intNorm, out Vector3 point, out int standCell, out ZoneSpot z)
        {
            point = default; standCell = -1; z = default;
            if (!TryZoneOfCell(cellIdx, out z)) return false;
            int zi = zoneOf[cellIdx];
            var members = zoneMembers[zi];
            rankScratch.Clear();
            for (int i = 0; i < members.Length; i++)
            {
                var c = CellCenter(members[i] % N, members[i] / N);
                float dx = c.x - z.stand.x, dz = c.z - z.stand.z;
                rankScratch.Add((dx * dx + dz * dz, members[i]));
            }
            rankScratch.Sort((a, b) => a.d2.CompareTo(b.d2));
            int rank = Mathf.RoundToInt((1f - Mathf.Clamp01(intNorm)) * (rankScratch.Count - 1));
            standCell = rankScratch[rank].cell;
            var sc = CellCenter(standCell % N, standCell / N);
            point = new Vector3(sc.x, groundY[standCell], sc.z);
            return true;
        }

        /// <summary>Flood-fills the Hidden cells into zones; appends one
        /// ZoneSpot per component. `stand` is the member CELL nearest the
        /// raw centroid — visually identical (cells are centimetres), but
        /// guaranteed to sit inside the painted area even for concave zones.
        /// Returns the count.</summary>
        public int EnumerateZones(List<ZoneSpot> out_)
        {
            out_.Clear();
            zoneMembers.Clear();
            if (state == null || !BuiltWithThreat) return 0;
            if (zoneSeen == null || zoneSeen.Length != state.Length)
                zoneSeen = new bool[state.Length];
            else System.Array.Clear(zoneSeen, 0, zoneSeen.Length);
            if (zoneOf == null || zoneOf.Length != state.Length)
                zoneOf = new int[state.Length];
            System.Array.Fill(zoneOf, -1);
            for (int s = 0; s < state.Length; s++)
            {
                if (zoneSeen[s] || state[s] != CellState.Hidden || volIdOf[s] == null) continue;
                string vol = volIdOf[s];
                byte cls = clsOf[s];
                int q = Mathf.RoundToInt(reqDepthOf[s] * 4f);
                zoneSeen[s] = true;
                zoneQueue.Clear(); zoneQueue.Enqueue(s);
                zoneCells.Clear();
                float sx = 0f, sz = 0f, gy = 0f, rd = 0f;
                while (zoneQueue.Count > 0)
                {
                    int i = zoneQueue.Dequeue();
                    zoneCells.Add(i);
                    var c = CellCenter(i % N, i / N);
                    sx += c.x; sz += c.z; gy += groundY[i]; rd += reqDepthOf[i];
                    int cx = i % N, cz = i / N;
                    for (int d = 0; d < 4; d++)
                    {
                        int x = cx + ZoneDX[d], z = cz + ZoneDZ[d];
                        if (x < 0 || x >= N || z < 0 || z >= N) continue;
                        int j = z * N + x;
                        if (zoneSeen[j] || state[j] != CellState.Hidden) continue;
                        if (volIdOf[j] != vol || clsOf[j] != cls) continue;
                        if (Mathf.RoundToInt(reqDepthOf[j] * 4f) != q) continue;
                        zoneSeen[j] = true; zoneQueue.Enqueue(j);
                    }
                }
                int n = zoneCells.Count;
                float mx = sx / n, mz = sz / n;
                int bestCell = zoneCells[0]; float bd = float.MaxValue;
                foreach (var i in zoneCells)
                {
                    var c = CellCenter(i % N, i / N);
                    float d = (c.x - mx) * (c.x - mx) + (c.z - mz) * (c.z - mz);
                    if (d < bd) { bd = d; bestCell = i; }
                }
                var standC = CellCenter(bestCell % N, bestCell / N);
                int zoneIdx = out_.Count;
                out_.Add(new ZoneSpot
                {
                    stand = new Vector3(standC.x, gy / n, standC.z),
                    volId = vol, reqD = rd / n,
                    cls = (CoverSensor.WallClass)cls, cells = n, cell = bestCell
                });
                foreach (var i in zoneCells) zoneOf[i] = zoneIdx;
                var mem = new int[zoneCells.Count];
                zoneCells.CopyTo(mem);
                if (zoneIdx < zoneMembers.Count) zoneMembers[zoneIdx] = mem;
                else zoneMembers.Add(mem);
            }
            return out_.Count;
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

        /// <summary>
        /// Bake the active layer into a color buffer for the heatmap texture.
        /// Exposure colors every TESTED cell by verdict; the value layers show
        /// the splat/proximity fields; Composite shows score with the chosen
        /// cell flashed white.
        /// </summary>
        public void Bake(Color32[] px, Layer layer)
        {
            for (int i = 0; i < px.Length; i++)
            {
                switch (layer)
                {
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
