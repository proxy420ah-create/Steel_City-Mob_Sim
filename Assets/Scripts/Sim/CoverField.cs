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
        private float[] scratch;                           // dilation buffer

        public int TestedCells { get; private set; }
        public int ValidCells { get; private set; }
        public int ChosenCell { get; private set; } = -1;
        public bool HasData { get; private set; }

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
            scratch = new float[cells];
        }

        /// <summary>
        /// Rebuild the field around <paramref name="pos"/> for the given threat.
        /// Splats each usable wall's stand-spot disc into cells, then validates
        /// each touched cell exactly once (ground, free-space, threat→eye LOS).
        /// Score = quality × (proxBlend + proxTerm·proximity) on Hidden cells.
        /// </summary>
        public void Build(Vector3 pos, Vector3? threat, float radius, List<CoverSensor.WallMark> walls,
            VoxelCollisionWorld world, float standoff, float topOffset, float topFwd, float threatEye,
            float facingDot, float proxWeight, int res,
            Dictionary<string, Vector2> volTop = null)
        {
            EnsureSize(res);
            float size = radius * 2f;
            CellSize = size / N;
            HasData = true;
            // Snap origin to cell multiples so small moves don't shimmer the map.
            Origin = new Vector3(
                Mathf.Floor((pos.x - radius) / CellSize) * CellSize,
                pos.y,
                Mathf.Floor((pos.z - radius) / CellSize) * CellSize);
            GroundY = pos.y;

            System.Array.Clear(state, 0, state.Length);
            System.Array.Clear(quality, 0, quality.Length);
            System.Array.Clear(proximity, 0, proximity.Length);
            System.Array.Clear(score, 0, score.Length);
            System.Array.Clear(facing, 0, facing.Length);
            System.Array.Clear(groundY, 0, groundY.Length);
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
            // Every cover-capable wall splats its stand-spot onto the field —
            // regardless of threat facing. A crouch-height wall face is cover
            // potential no matter who's shooting; that judgement belongs to
            // phase 2. Adjacent splats merge: wide cover (mid-car) accumulates
            // a hotter contiguous zone than a thin wheel edge.
            float spotR = standoff;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w.cls == CoverSensor.WallClass.Low) continue;
                float qw = w.cls == CoverSensor.WallClass.Full ? 1f : 0.7f;
                Vector3 spot = w.facePos + w.outDir * (standoff + w.vs * 0.25f);
                Splat(spot, spotR, qw, w.volId, w.facing);
            }

            // One dilation ring — smooths the splat into a continuous band so
            // the quality layer reads as zones, not polka dots. Decay 0.55×
            // keeps the ring below the test threshold (seeds carry the rays).
            System.Array.Copy(quality, scratch, quality.Length);
            for (int cz = 1; cz < N - 1; cz++)
            for (int cx = 1; cx < N - 1; cx++)
            {
                int idx = cz * N + cx;
                if (scratch[idx] > 0f) continue;
                float m = 0f;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    m = Mathf.Max(m, scratch[idx + dz * N + dx]);
                if (m > 0f) { quality[idx] = m * 0.55f; }
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
                if (quality[idx] < 0.5f) continue;       // dilated halo — visual only
                if (facing[idx] >= facingDot) { Tally("facing"); continue; }
                TestedCells++;
                Vector3 c = CellCenter(cx, cz);

                if (!world.Raycast(c + Vector3.up * 1.6f, Vector3.down, 3.2f, out VoxelRayHit gh))
                { state[idx] = CellState.Invalid; Tally("no-ground"); continue; }
                groundY[idx] = gh.point.y;
                Vector3 standPt = new Vector3(c.x, gh.point.y, c.z);
                // Adaptive crouch: the cell's wall dictates the duck depth, so
                // the LOS probe sits at that wall's required-depth silhouette
                // top — a low wall validates only if the deep-ducked top clears it.
                float cellTop = topOffset, cellFwd = topFwd;
                if (volTop != null && volIdOf[idx] != null &&
                    volTop.TryGetValue(volIdOf[idx], out Vector2 ve))
                { cellTop = ve.x; cellFwd = ve.y; }
                Vector3 eye = standPt + Vector3.up * cellTop + DirTo(tp, standPt) * cellFwd;

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
                score[idx] = quality[idx] * ((1f - proxWeight) + proxWeight * proximity[idx]);
            }
        }

        /// <summary>Accumulate a wall's stand-spot disc into touched cells.
        /// The strongest contributor owns the cell's facing + volume.</summary>
        private void Splat(Vector3 center, float r, float qw, string vol, float face)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - r - Origin.x) / CellSize));
            int x1 = Mathf.Min(N - 1, Mathf.FloorToInt((center.x + r - Origin.x) / CellSize));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((center.z - r - Origin.z) / CellSize));
            int z1 = Mathf.Min(N - 1, Mathf.FloorToInt((center.z + r - Origin.z) / CellSize));
            for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                Vector3 c = CellCenter(cx, cz);
                float dx = c.x - center.x, dz = c.z - center.z;
                if (dx * dx + dz * dz > r * r) continue;
                int idx = cz * N + cx;
                if (qw > quality[idx]) { quality[idx] = qw; volIdOf[idx] = vol; facing[idx] = face; }
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

        public Vector3 CellCenter(int cx, int cz)
            => new Vector3(Origin.x + (cx + 0.5f) * CellSize, GroundY, Origin.z + (cz + 0.5f) * CellSize);

        public bool IsChosen(int idx) => idx == ChosenCell;

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
