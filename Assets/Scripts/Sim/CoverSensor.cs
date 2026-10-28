using System;
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
        public float radius = 8f;   // arena stands sit ~6.4m off the cover rows — 4m saw no wall at all
        [Tooltip("Stand-spot gap off the wall face (m) — ~half body depth = flush. The bore solve needs the eye close behind the face or a descending ray clips the lip.")]
        public float standoff = 0.3f;
        [Tooltip("Columns with a contiguous span below this aren't walls at all (m).")]
        public float wallFloor = 0.15f;

        [Tooltip("outDir·dirToThreat below this = the open side faces away from the threat.")]
        public float facingDot = -0.05f;
        [Tooltip("Threat chest height used for the LOS validation ray (m).")]
        public float threatEye = 0.55f;
        [Tooltip("Seconds between passive rescans.")]
        public float scanInterval = 0.4f;
        [Tooltip("Rescan early if the owner moved this far (m).")]
        public float rescanMoveDist = 0.4f;

        [Header("Influence Field")]
        [Tooltip("Cells per side for the cover field grid.")]
        public int fieldRes = 64;
        [Tooltip("How much proximity weighs vs cover quality in the composite score (0=quality only, 1=distance only).")]
        [Range(0f, 1f)] public float proxWeight = 1f;      // 1 = closest viable cover wins (doctrine), 0 = highest quality

        [Header("Visualization (COVER tab)")]
        [Tooltip("Legacy beam markers — superseded by the V-key cover-field modes; kept for deep debugging.")]
        public bool showRing = false;
        public bool showFootprints = false;
        public bool showWalls = false;
        [Tooltip("With a threat set, dim walls whose open side faces the threat.")]
        public bool tintWallFacing = false;
        public bool showCandidates = false;
        public bool showLos = false;
        [Tooltip("Ground-projected cover influence field — fallback when not using the V-key modes.")]
        public bool showHeat = false;
        public CoverField.Layer heatLayer = CoverField.Layer.Composite;
        public int maxWallBeams = 500;
        public int maxCandBeams = 60;
        [Tooltip("Draw the exposure probe: green eye→sample rays = visible, red = blocked, cyan cross = visible centroid.")]
        public bool showTargetViz = false;
        [Tooltip("Paint impact marks instead of rays: green = ray resolved a target voxel AND it's inside the posed vision cone (actually seen), slate = clear ray outside the cone (reachable, not looking), red = ray died on the occluder. Tan lights up exactly where he's seen — no ray clutter.")]
        public bool showImpactMarks = false;
        public Color visMarkColor = new Color(0.25f, 1f, 0.35f, 0.9f);
        public Color occlMarkColor = new Color(1f, 0.3f, 0.15f, 0.55f);
        public Color offConeMarkColor = new Color(0.55f, 0.5f, 0.8f, 0.45f);  // clear ray but OUTSIDE the posed cone — reachable, not seen
        private VisionSensor coneSensor;   // same-owner eye fan — gates the "seen" color

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
            /// <summary>Minimum duck scalar (0..2) that puts the posed silhouette
            /// under this wall's top — sub-1 = stand→shallow morph, over-1 = deep band.</summary>
            public float reqDepth;
            public float support;        // ground Y at this wall's stand-spot (the floor the solve measured from)
        }

        /// <summary>Last pick's wall math, for the metrics panel.</summary>
        public string PickInfo = "";

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

        /// <summary>Silhouette-top height above the feet — measured live off the
        /// posed model (each group's top voxel through the FK chain), replacing
        /// the retired eye constants. h(d) lerps HShallow→HDeep exactly like the
        /// pose eval; re-authoring a pose re-tunes cover for free.</summary>
        public float HShallow { get; private set; } = 0.50f;
        /// <summary>Silhouette top at crouchDepth=1 — the deepest authored duck.
        /// Defines the usable-cover floor: walls whose top clears THIS height
        /// are duckable at some depth; below it they're genuinely unusable.</summary>
        public float HDeep { get; private set; } = 0.45f;
        /// <summary>Silhouette top in the Idle pose — walls covering this are Full
        /// (stood behind, no duck).</summary>
        public float HStand { get; private set; } = 0.62f;
        /// <summary>World Y of the solid column top under the feet — the support
        /// surface the silhouette is measured from. Standing on voxels is
        /// additive: curbs, rubble, crate tops all raise it.</summary>
        public float SupportY { get; private set; }
        /// <summary>True when the heights were measured off the posed model,
        /// false = fallback constants (no anim data).</summary>
        public bool HeightsMeasured { get; private set; }
        /// <summary>Forward lean of the eye anchor at depth=1.</summary>
        public float EyeFwdDeep { get; private set; }
        /// <summary>Clearance margin solved into every required-depth pick (m) —
        /// keeps the posed silhouette below the wall top, not at the boundary.</summary>
        public float depthMargin = 0.02f;
        /// <summary>Horizontal forward offset of the crouched eye anchor — the
        /// crouch pose leans the head over the knees (bodyLean + proud offset),
        /// so the eyes sit in front of the feet axis toward facing.</summary>
        public float EyeFwd { get; private set; }
        public bool EyePosed { get; private set; }

        private VoxelCollisionWorld world;
        private VoxelCharacter owner;
        private float nextScan;
        private Vector3 lastScanPos = new Vector3(1e9f, 0f, 0f);
        private readonly List<CoverObstacle> obstacleBuf = new();
        private readonly List<CoverWall> wallBuf = new();
        private readonly List<Vector3> candEyes = new();  // crouch-eye points (viz)
        private readonly HashSet<string> beamKeys = new();
        /// <summary>volId → required crouch depth (max over that volume's walls).
        /// Feeds TryPickCover's depth report.</summary>
        private readonly Dictionary<string, float> volDepth = new();
        /// <summary>volId → class of that volume's highest-demand wall — Full
        /// walls are stood behind, not crouched behind.</summary>
        private readonly Dictionary<string, WallClass> volClass = new();
        private VoxelChunkManager chunkManager;
        private bool dataWasLive, ringLive;

        /// <summary>The influence field — splat → validate → score → pick.
        /// Field IS the picker; `candidates` is its tested-cell visualization.</summary>
        private readonly CoverField field = new CoverField();
        public CoverField Field => field;

        // Heatmap quad — same child-GO + Sprites/Default pattern as VisionSensor's wedge.
        private GameObject heatGO;
        private MeshRenderer heatRend;
        private Texture2D heatTex;
        private Color32[] heatPx;
        private Color32[] cellPx;        // per-cell verdicts — stamped to tiles on the walk layers
        private int bakedRes = -1;
        private static readonly Color32 GRID_LINE = new Color32(10, 10, 16, 110);
        private Color32[] fieldPx;       // packed field → texture-array slice

        void Awake()
        {
            world = FindFirstObjectByType<VoxelCollisionWorld>();
            owner = GetComponent<VoxelCharacter>();
            coneSensor = GetComponent<VisionSensor>();
        }

        void OnDisable()
        {
            ClearViz();
            if (vizSlot >= 0)
            {
                fieldParams[vizSlot] = Vector4.zero;
                Shader.SetGlobalVectorArray("_CoverFieldParamsArr", fieldParams);
                slotTaken[vizSlot] = false;
                vizSlot = -1;
            }
        }

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
            // The scan exists only to keep consumers warm — combat picks
            // force-refresh on demand, so when nobody's watching (no threat,
            // no viz toggles, no cover V-mode) the column walk is pure cost.
            if (ConsumersWantData())
            {
                dataWasLive = true;
                if ((p - lastScanPos).sqrMagnitude >= rescanMoveDist * rescanMoveDist) Refresh(true);
                else if (Time.unscaledTime >= nextScan) Refresh();
            }
            else if (dataWasLive) { ClearViz(); dataWasLive = false; }
            if (showRing) { DrawRing(p); ringLive = true; }   // live radius indicator — not scan data
            else if (ringLive)
            {
                var pdr = PathDebugRenderer.Instance;
                if (pdr != null) for (int i = 1; i <= 28; i++) pdr.ClearCustomBeam($"csr_{i}");
                ringLive = false;
            }
        }

        /// <summary>Any live consumer of field data: an armed threat, a beam
        /// viz toggle, or a cover V-mode. Combat picks bypass this entirely —
        /// they force a scan at request time.</summary>
        bool ConsumersWantData()
        {
            if (HasThreat || showFootprints || showWalls || showCandidates
                || showLos || showHeat) return true;
            if (chunkManager == null) chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (chunkManager != null)
            {
                var m = chunkManager.iterVisMode;
                if (m >= VoxelChunkManager.IterVisMode.CoverQuality
                    && m <= VoxelChunkManager.IterVisMode.CoverExposure) return true;
                if (m >= VoxelChunkManager.IterVisMode.CoverWalkable) return true;
            }
            return false;
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
            Notify();
        }

        /// <summary>
        /// Pick cover = the field maximum (quality × proximity composite).
        /// `from` is retained for signature stability — proximity is already
        /// baked into the field around the scan center.
        /// </summary>
        public bool TryPickCover(Vector3 from, out Vector3 point, out string volId)
            => TryPickCover(from, out point, out volId, out _);

        /// <summary>Also reports the crouch depth the picked wall demands —
        /// CombatController eases `crouchDepth` toward it on arrival — and the
        /// wall's class (Full walls are stood behind, not ducked behind).</summary>
        public bool TryPickCover(Vector3 from, out Vector3 point, out string volId, out float reqDepth)
            => TryPickCover(from, out point, out volId, out reqDepth, out _);

        public bool TryPickCover(Vector3 from, out Vector3 point, out string volId,
            out float reqDepth, out WallClass cls, float intNorm = 1f, CoverField.DistMap reach = null)
        {
            point = default; volId = null; reqDepth = 0f; cls = WallClass.Low; chosenIdx = -1;
            if (!field.PickBest(out point, out volId, reach)) return false;
            // The zone centroid IS the destination — same contract the scripted
            // slots ride (ZoneSpot.stand). Was: wall-column midpoint, which is
            // why actors stopped off the painted marker. `intNorm` ranks the
            // stand point centroid→rim inside the zone (the INT axis);
            // `reach` keeps the ranked members legal — unreachable members
            // can't host the stand.
            bool zoned = field.TryRankInZone(field.ChosenCell, intNorm, out Vector3 zp, out int zc, out var z, reach);
            if (zoned)
            {
                point = zp; volId = z.volId; reqDepth = z.reqD; cls = z.cls;
                field.MarkChosen(zc);
            }
            if (volId != null)
            {
                // Volume aggregates are the WORST column anywhere in the volume
                // — wrong for a single gradient wall. Resolve the wall column
                // whose stand-spot is nearest the picked cell (same volume,
                // non-Low) and use ITS depth/class; aggregates are the fallback.
                int wi = -1; float bd = float.MaxValue;
                for (int i = 0; i < walls.Count; i++)
                {
                    var w = walls[i];
                    if (w.cls == WallClass.Low || w.volId != volId) continue;
                    Vector3 spot = w.facePos + w.outDir * (standoff + w.vs * 0.25f);
                    float dx = spot.x - point.x, dz = spot.z - point.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < bd) { bd = d2; wi = i; }
                }
                if (wi >= 0)
                {
                    var pw = walls[wi];
                    if (!zoned)
                    {
                        reqDepth = pw.reqDepth; cls = pw.cls;
                        // Fallback only: zone lookup failed — hug the owning
                        // column's stand-spot inside its uniform band.
                        Vector3 spot = pw.facePos + pw.outDir * (standoff + pw.vs * 0.25f);
                        point = new Vector3(spot.x, pw.support, spot.z);
                    }
                    PickInfo = $"wall top {pw.top:F2} · floor {pw.support:F2} · rise {pw.top - pw.support:F2}"
                             + $" (stand {HStand:F2} crouch {HShallow:F2}→{HDeep:F2}) → {pw.cls} duck{pw.reqDepth:F2}";
                }
                else if (!zoned)
                {
                    if (volDepth.TryGetValue(volId, out float d)) reqDepth = d;
                    if (volClass.TryGetValue(volId, out WallClass c)) cls = c;
                }
            }
            // The issued point must itself be reachable — PickBest gates the
            // cell, but the wall-mark fallback above can land somewhere the
            // actor's map can't serve. Reject the pick, don't strand him.
            if (reach != null && !reach.Reaches(field.CellIndexOf(point)))
            { Redraw(); return false; }
            // Map the chosen cell back onto its candidate post for the cyan beam.
            for (int i = 0; i < candidates.Count; i++)
                if (field.IsChosen(FieldCellOf(candidates[i].point))) { chosenIdx = i; break; }
            Redraw();
            return true;
        }

        /// <summary>Which painted cover zone contains <paramref name="pos"/> —
        /// returns the OWNING column's class + required depth (the zone's
        /// posture verdict). False over open ground, halo rim, and Low
        /// frontage (those claim cells but never paint). When the field was
        /// built against a real threat, only Hidden-validated cells count —
        /// the threat-facing side of a wall still gets claimed + painted
        /// (quality is threat-independent), but it isn't cover from THAT
        /// shooter.</summary>
        public bool TryZoneAt(Vector3 pos, out WallClass cls, out float reqDepth)
        {
            cls = WallClass.Low; reqDepth = 0f;
            if (!field.HasData) return false;
            int idx = field.CellIndexOf(pos);
            if (idx < 0 || field.volIdOf[idx] == null || field.quality[idx] < CoverField.SeedMin)
                return false;
            if (field.BuiltWithThreat && field.state[idx] != CoverField.CellState.Hidden)
                return false;
            cls = (WallClass)field.clsOf[idx];
            reqDepth = field.reqDepthOf[idx];
            return cls != WallClass.Low;
        }

        /// <summary>Debug pick — hide behind the cover `anchor` names. The
        /// wall is resolved by proximity FIRST (splat discs overlap between
        /// adjacent cover, so a cell's owner isn't always the anchored box),
        /// then the field picks that wall's nearest validated Hidden cell.
        /// Reports the wall's depth requirement and class.</summary>
        public bool TryPickCoverNear(Vector3 anchor, float maxDist,
            out Vector3 point, out string volId, out float reqDepth, out WallClass cls,
            float intNorm = 1f, CoverField.DistMap reach = null)
        {
            point = default; volId = null; reqDepth = 0f; cls = WallClass.Low; chosenIdx = -1;
            int wi = -1; float wd = maxDist * maxDist;
            for (int i = 0; i < walls.Count; i++)
            {
                if (walls[i].cls == WallClass.Low) continue;
                float dx = walls[i].facePos.x - anchor.x, dz = walls[i].facePos.z - anchor.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < wd) { wd = d2; wi = i; }
            }
            if (wi < 0) return false;
            var w = walls[wi];
            // Diagnostic — the pick's wall resolution is the usual suspect when
            // class/depth come back wrong; print the nearest candidates so a
            // bad resolve is visible instead of silent.
            var cand = new System.Collections.Generic.List<(float d, int i)>();
            for (int i = 0; i < walls.Count; i++)
            {
                if (walls[i].cls == WallClass.Low) continue;
                float dx = walls[i].facePos.x - anchor.x, dz = walls[i].facePos.z - anchor.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < maxDist * maxDist) cand.Add((Mathf.Sqrt(d2), i));
            }
            cand.Sort((a, b) => a.d.CompareTo(b.d));
            var sb = new System.Text.StringBuilder($"🧱 [CoverPick] anchor {anchor:F2} walls {cand.Count} →");
            for (int k = 0; k < cand.Count && k < 3; k++)
            {
                var cw = walls[cand[k].i];
                sb.Append($" [{cw.volId} face {cw.facePos:F1} top+{cw.top - cw.baseY:F1}m {cw.cls} d{cand[k].d:F1}]");
            }
            Debug.Log(sb.ToString());
            if (!field.PickNear(anchor, maxDist, out point, out _, w.volId, reach)) return false;
            // Zone-ranked destination, same as the generic pick — the
            // anchor selects WHICH zone; `intNorm` picks how deep inside it.
            if (field.TryRankInZone(field.ChosenCell, intNorm, out Vector3 nzp, out int nzc, out var nz, reach))
            {
                point = nzp; volId = nz.volId; reqDepth = nz.reqD; cls = nz.cls;
                field.MarkChosen(nzc);
            }
            else
            {
                volId = w.volId; reqDepth = w.reqDepth; cls = w.cls;
                Vector3 sspot = w.facePos + w.outDir * (standoff + w.vs * 0.25f);
                point = new Vector3(sspot.x, w.support, sspot.z);
            }
            // Same reach gate as the generic pick — the button pick can't
            // commit a destination the map can't serve.
            if (reach != null && !reach.Reaches(field.CellIndexOf(point)))
            { Redraw(); return false; }
            for (int i = 0; i < candidates.Count; i++)
                if (field.IsChosen(FieldCellOf(candidates[i].point))) { chosenIdx = i; break; }
            Redraw();
            return true;
        }

        /// <summary>Minimum DUCK scalar (0..2) that puts the silhouette top
        /// `depthMargin` below `top` (world Y). Piecewise-inverts h(duck):
        ///   duck 0 → stand · 0..1 → stand→shallow morph · 1..2 → shallow→deep.
        /// Every band is continuous — a wall between stand- and shallow-top
        /// yields a partial duck, not a snap to the full crouch.</summary>
        public float DuckForTop(float top, float baseY)
            => Mathf.Clamp(DuckForTopRaw(top, baseY), 0f, 2f);

        /// <summary>Unclamped duck solve — the raw value carries the class:
        /// &lt;= 0 → Full (covers him standing), 0 &lt; d &lt;= 2 → duckable
        /// (sub-1 = morph band, over-1 = deep band), &gt; 2 → too low to hide
        /// behind (Low). DuckForTop clamps for pose-feeding callers.</summary>
        public float DuckForTopRaw(float top, float baseY)
        {
            float clear = top - depthMargin - baseY;
            if (clear >= HStand) return 0f;
            float spanMorph = HStand - HShallow, spanDeep = HShallow - HDeep;
            if (clear >= HShallow && spanMorph > 1e-4f)
                return (HStand - clear) / spanMorph;
            return spanDeep > 1e-4f ? 1f + (HShallow - clear) / spanDeep : 3f;
        }

        private int FieldCellOf(Vector3 p)
        {
            if (field.N <= 0) return -1;
            int cx = Mathf.FloorToInt((p.x - field.Origin.x) / field.CellSize);
            int cz = Mathf.FloorToInt((p.z - field.Origin.z) / field.CellSize);
            if (cx < 0 || cz < 0 || cx >= field.N || cz >= field.N) return -1;
            return cz * field.N + cx;
        }

        // --- Scan pipeline ---

        void Scan(Vector3 pos)
        {
            stats.Reset();
            walls.Clear(); candidates.Clear(); candEyes.Clear();
            chosenIdx = -1;

            // Silhouette-top band = the posed model TOP measured live — "how
            // tall am I when I duck" — no authored constants. The pose eval
            // owns the crouch lerp end-to-end, so h(d) = lerp(HShallow, HDeep, d)
            // exactly like the pose. Eye anchors still provide the pose's
            // forward lean: in deep crouch the head leans out over the toes, so
            // the LOS probe must test THAT point, not the feet axis.
            float hStand = 0.62f, h0 = 0.50f, h1 = 0.45f;
            HeightsMeasured = owner != null
                && owner.TryMeasureTopY(0f, 0f, out hStand)
                && owner.TryMeasureTopY(5f, 0f, out h0)
                && owner.TryMeasureTopY(5f, 1f, out h1);
            HStand = hStand; HShallow = h0; HDeep = h1;

            Vector3 ep = default;
            EyePosed = owner != null && owner.TryGetEyeAnchor(out ep, out _, out _,
                animStateOverride: 5f);
            EyeFwd = EyePosed ? new Vector3(ep.x - pos.x, 0f, ep.z - pos.z).magnitude : 0f;
            EyeFwdDeep = EyeFwd;
            if (owner != null && owner.TryGetEyeAnchor(out Vector3 epd, out _, out _,
                animStateOverride: 5f, animDepthOverride: 1f))
                EyeFwdDeep = new Vector3(epd.x - pos.x, 0f, epd.z - pos.z).magnitude;

            // Support = the solid column top under the feet — standing on
            // voxels is additive: curbs, rubble, crate tops raise the whole
            // silhouette, lowering the required duck.
            float supportY = pos.y;
            if (world.TryColumnTop(pos, 0.5f, 1.5f, out float gy)) supportY = gy;
            SupportY = supportY;

            stats.obstacles = world.QueryVolumes(pos, radius, obstacleBuf);
            // Walls tall enough to matter, with an open side at the deepest
            // silhouette-top band — the permissive superset; per-wall required
            // depth + threat-LOS do the precise validation after.
            // Prefilter is permissive (-0.3m): the owner's own support is only a
            // hint — each wall is classified against the ground at ITS stand-spot.
            world.QueryCoverWalls(pos, radius, wallFloor, supportY + h1 - 0.3f, wallBuf);
            stats.wallCols = wallBuf.Count;

            volDepth.Clear(); volClass.Clear();
            supportCache.Clear();
            Vector3 tp = Threat ?? default;
            bool hasT = Threat.HasValue;
            for (int i = 0; i < wallBuf.Count; i++)
            {
                CoverWall w = wallBuf[i];
                // Class/depth are properties of the wall vs the ground where
                // someone stands behind it — NOT of where the owner happens to
                // be. Same wall, same class, wherever Vinny walks.
                float sy = LocalSupport(w.facePos + w.outDir * (standoff + w.voxelSize * 0.25f), supportY);
                // Floor relief is not a wall: the volume query measures span from
                // the volume's BOTTOM (ground slab included), so a 1-voxel pad,
                // curb or stand plate passes `wallFloor` and — as a Low column —
                // would claim cells at quality 0 and carve holes in real zones.
                // A wall must stand wallFloor above the floor behind it.
                if (w.spanTop - sy < wallFloor) continue;
                WallClass cls = Covers(w, sy + hStand) ? WallClass.Full
                              : Covers(w, sy + h1) ? WallClass.Crouch : WallClass.Low;
                float reqD = cls == WallClass.Low ? 0f : DuckForTop(w.spanTop, sy);
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
                    reqDepth = reqD, support = sy,
                });
                if (cls != WallClass.Low && w.volumeId != null)
                {
                    // Deepest requirement wins when several walls share a volume —
                    // fallback aggregate for the pick when no column resolves.
                    if (!volDepth.TryGetValue(w.volumeId, out float pd) || reqD > pd)
                    { volDepth[w.volumeId] = reqD; volClass[w.volumeId] = cls; }
                }
            }
            stats.facing = 0;
            if (hasT)
            {
                for (int i = 0; i < walls.Count; i++)
                    if (walls[i].cls != WallClass.Low && walls[i].facing < facingDot) stats.facing++;
            }

            // Field rebuilds every scan: the quality pass is threat-independent
            // (cover-height faces are cover no matter who's shooting), so the
            // V-key cover modes paint passively. The threat combine inside
            // Build only runs when a threat exists.
            field.Build(pos, Threat, radius, walls, world, standoff, HShallow, EyeFwd,
                threatEye, facingDot, proxWeight, fieldRes, HDeep, EyeFwdDeep, HStand, depthMargin);
            // Moving vehicles: their occupied + swept cells go into the field's dynamic mask so every
            // flood/pick built on this scan prices them (cars invalidate the map as they drive).
            field.ClearDynBlock();
            VoxelVehicle.PaintMovingFootprints(field, world, 1.5f);
            // The actor's ground signature: one square column, the SAME square
            // the mover claims — every Walkable verdict on this field answers
            // "can the body-square be placed here" at continuous resolution
            // (meters, not cells), so the flood, picks, and corridors all
            // agree with the body by construction.
            var vc = GetComponent<VoxelCharacter>();
            if (vc != null)
            {
                field.bodyHalfMeters = vc.BodyFootprint * 0.5f;   // the character owns the spec
                // Deck tolerance has ONE owner — the mover's stepUpMax (the
                // same constant the flood's EdgeOpen is called with). Two
                // literals would drift: a tuned stepUp would split "can stand"
                // from "can reach".
                var ccl = GetComponent<CombatController>();
                field.bodyStepUp = ccl != null ? ccl.stepUpMax : 0.15f;
                field.walkBodyH = vc.WorldSize.y;   // fit verdicts cache at THIS height next build
            }
            if (hasT) DeriveCandidates(tp);
        }

        /// <summary>
        /// Mirror the field's tested cells into `candidates` for the post/LOS
        /// beam viz. `TryPickCover` reads the field maximum — posts are display.
        /// </summary>
        private void DeriveCandidates(Vector3 threat)
        {
            stats.tested = field.TestedCells;
            stats.valid = field.ValidCells;
            foreach (var kv in field.reasons) stats.reasons[kv.Key] = kv.Value;
            if (field.N <= 0) return;
            for (int i = 0; i < field.state.Length; i++)
            {
                if (field.state[i] == CoverField.CellState.Untested) continue;
                Vector3 c = field.CellCenter(i % field.N, i / field.N);
                var cand = new CandMark
                {
                    volId = field.volIdOf[i] ?? "",
                    point = new Vector3(c.x, field.StandY(i), c.z),
                    valid = field.state[i] == CoverField.CellState.Hidden,
                    reason = field.state[i] == CoverField.CellState.Hidden ? null
                           : field.state[i] == CoverField.CellState.Exposed ? "exposed" : "invalid",
                };
                candidates.Add(cand);
                candEyes.Add(CandTop(cand.point, threat));
            }
        }

        private readonly Dictionary<long, float> supportCache = new();

        /// <summary>Ground height at a stand-spot, cached per scan on a 0.1m
        /// grid so thousands of wall columns share a few probes.</summary>
        private float LocalSupport(Vector3 spot, float fallback)
        {
            long key = ((long)Mathf.FloorToInt(spot.x * 10f) << 32) ^ (uint)Mathf.FloorToInt(spot.z * 10f);
            if (supportCache.TryGetValue(key, out float sy)) return sy;
            sy = world.TryColumnTop(spot, 0.5f, 1.5f, out float gy) ? gy : fallback;
            supportCache[key] = sy;
            return sy;
        }

        /// <summary>Column covers a horizontal ray at world height `eye`: solid band spans it.</summary>
        private static bool Covers(CoverWall w, float eye)
            => w.firstSolidY <= eye && eye < w.spanTop;

        /// <summary>Silhouette-top test point at a candidate: up by the measured
        /// shallow-crouch top, then forward toward the threat by the posed lean —
        /// the most exposed point of the ducked pose, over the toes.</summary>
        Vector3 CandTop(Vector3 point, Vector3 threat)
        {
            Vector3 tdir = threat - point; tdir.y = 0f;
            float tm = tdir.magnitude;
            return point + Vector3.up * HShallow + (tm > 1e-4f ? tdir / tm * EyeFwd : Vector3.zero);
        }

        // --- Exposure probe (docs/systems/EXPOSURE_ENGAGEMENT.md) ---
        // One primitive, read from both ends: "how exposed is the threat to
        // MY eye" is the same ray set that answers "how hidden am I" from
        // his. The aim target is the centroid of whatever is visible — a
        // head band over a parapet aims at the head for free.

        /// <summary>One visibility probe: how much of the target's posed
        /// silhouette the owner's eye can currently see. `visible > 0` IS
        /// the aim/fire gate — zero = fully tucked, the IK stands down.
        /// Note: characters aren't collision-world volumes, so "visible"
        /// = unobstructed path to a world-space sample (no self-occlusion).</summary>
        public struct VisibleSet
        {
            public int visible, tested;
            public float fraction;
            public Vector3 centroid;
            public VoxelCharacter target;
            /// <summary>Samples visible from BOTH the eye and the live muzzle — the SHOOTABLE set.
            /// Equals `visible` when no muzzle probe ran (muzzleGated=false: unknown, permissive).</summary>
            public int shootable;
            public Vector3 shootCentroid;
            public bool muzzleGated;
        }

        /// <summary>Result of the last probe — CombatController's exposure gate.</summary>
        public VisibleSet LastVisible { get; private set; }
        /// <summary>Drop the cached probe result — combat exit hands back a clean slate.</summary>
        public void ClearVisibility() { LastVisible = default; }
        /// <summary>Fired on every display-relevant mutation — field scans and
        /// visibility probes. Debug consumers rebuild on Changed, never poll.</summary>
        public event Action Changed;
        void Notify() => Changed?.Invoke();
        /// <summary>World point the last probe cast from — for HUD/viz.</summary>
        public Vector3 LastProbeEye { get; private set; }
        /// <summary>Muzzle probe — when HasMuzzleProbe, every probe sample is ALSO rayed from
        /// MuzzleProbeOrigin: eye-visible = AWARE, eye∧muzzle-visible = SHOOTABLE (VisibleSet.shootable).</summary>
        public bool HasMuzzleProbe;
        public Vector3 MuzzleProbeOrigin;
        public int LastMuzzleVisible { get; private set; }
        public int LastBothVisible { get; private set; }
        private readonly Vector3[] visBuf = new Vector3[96];
        private int visDrawn;            // tv_s{i} beams alive right now
        private bool visCentroidDrawn;   // tv_c{x,y,z} cross alive
        private int marksDrawn;          // vmk_<name>_{i} impact cubes alive right now

        /// <summary>Sample the target's live posed silhouette (~30–50 points,
        /// every group covered) and ray the owner's posed eye at each sample
        /// against the collision world. Unobstructed = visible. Returns
        /// `visible > 0`; fills `set` + LastVisible either way.</summary>
        /// <summary>True if a segment between two world points hits world
        /// voxels — the bore-line check for the rise solve: does the presented
        /// gun's path to the target clip cover? Character voxels aren't in the
        /// collision world (the probe proves targets reachable at full dist),
        /// so full-distance is the bullet path.</summary>
        public bool LosBlocked(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; float dist = d.magnitude;
            return dist > 1e-4f && world != null && world.Raycast(a, d / dist, dist, out _);
        }

        public bool TryGetVisibleVoxels(VoxelCharacter target, out VisibleSet set)
        {
            set = default;
            LastMuzzleVisible = 0; LastBothVisible = 0;
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (target == null || world == null)
            { LastVisible = set; ClearVisBeams(); Notify(); return false; }

            // Posed-eye anchor — same FK chain the aim uses. Chest-height
            // fallback keeps the probe live on an unanimated rig.
            if (owner == null || !owner.TryGetEyeAnchor(out Vector3 eye, out _, out _))
                eye = SelfPos() + Vector3.up * threatEye;
            LastProbeEye = eye;

            int n = target.SampleSilhouetteWorld(visBuf);
            if (n <= 0) { LastVisible = set; ClearVisBeams(); Notify(); return false; }

            var pdr = showTargetViz ? PathDebugRenderer.Instance : null;
            var pdrMark = PathDebugRenderer.Instance;
            bool wantMarks = showImpactMarks && pdrMark != null;
            int vis = 0, visM = 0, visB = 0; Vector3 sum = Vector3.zero, sumB = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = visBuf[i] - eye;
                float dist = d.magnitude;
                VoxelRayHit rh = default;
                bool clear = dist < 1e-4f || !world.Raycast(eye, d / dist, dist, out rh);
                if (clear) { vis++; sum += visBuf[i]; }
                if (HasMuzzleProbe)
                {
                    Vector3 dm = visBuf[i] - MuzzleProbeOrigin; float dd = dm.magnitude;
                    if (dd < 1e-4f || !world.Raycast(MuzzleProbeOrigin, dm / dd, dd, out _))
                    { visM++; if (clear) { visB++; sumB += visBuf[i]; } }
                }
                if (pdr != null)
                    pdr.SetCustomBeam($"tv_s{i}", eye, visBuf[i], 0.012f,
                        clear ? new Color(0.3f, 1f, 0.4f, 0.5f)
                              : new Color(1f, 0.25f, 0.2f, 0.35f));
                // Impact marks — the probe as a point-cloud, cone-gated per
                // sample: GREEN = resolved AND inside the posed eye fan (the
                // voxels canSee actually counts), SLATE = geometrically clear
                // but outside the fan (reachable if he turned — never "seen"),
                // RED = ray died on the occluder. No cone rig → unconstrained
                // (testbeds stay green-only).
                if (wantMarks)
                {
                    bool inCone = !clear || coneSensor == null || coneSensor.Covers(visBuf[i]);
                    pdrMark.SetXrayCube($"vmk_{name}_{i}", clear ? visBuf[i] : rh.point,
                        Quaternion.identity, 0.045f,
                        !clear ? occlMarkColor : inCone ? visMarkColor : offConeMarkColor);
                }
            }
            if (pdrMark != null)
            {
                for (int i = wantMarks ? n : 0; i < marksDrawn; i++) pdrMark.ClearXrayCube($"vmk_{name}_{i}");
                marksDrawn = wantMarks ? n : 0;
            }
            else marksDrawn = 0;
            set = new VisibleSet
            {
                visible = vis, tested = n,
                fraction = (float)vis / n,
                centroid = vis > 0 ? sum / vis : Vector3.zero,
                target = target,
                muzzleGated = HasMuzzleProbe,
                shootable = HasMuzzleProbe ? visB : vis,
                shootCentroid = HasMuzzleProbe ? (visB > 0 ? sumB / visB : Vector3.zero) : (vis > 0 ? sum / vis : Vector3.zero),
            };
            LastVisible = set;
            LastMuzzleVisible = visM; LastBothVisible = visB;
            Notify();

            if (pdr != null)
            {
                for (int i = n; i < visDrawn; i++) pdr.ClearCustomBeam($"tv_s{i}");
                visDrawn = n;
                if (vis > 0)
                {
                    var c = new Color(0.3f, 0.95f, 1f, 0.9f);
                    pdr.SetCustomBeam("tv_cx", set.centroid + Vector3.left * 0.14f,  set.centroid + Vector3.right * 0.14f,  0.03f, c);
                    pdr.SetCustomBeam("tv_cy", set.centroid + Vector3.down * 0.14f,  set.centroid + Vector3.up * 0.14f,     0.03f, c);
                    pdr.SetCustomBeam("tv_cz", set.centroid + Vector3.back * 0.14f,  set.centroid + Vector3.forward * 0.14f, 0.03f, c);
                    visCentroidDrawn = true;
                }
                else { pdr.ClearCustomBeam("tv_cx"); pdr.ClearCustomBeam("tv_cy"); pdr.ClearCustomBeam("tv_cz"); visCentroidDrawn = false; }
            }
            else ClearVisRays();   // beams off — marks stay owned by their own toggle
            return vis > 0;
        }

        /// <summary>Kill ALL probe viz — early-outs and ClearViz only.</summary>
        void ClearVisBeams() { ClearVisRays(); ClearVisMarks(); }
        void ClearVisRays()
        {
            if (visDrawn == 0 && !visCentroidDrawn) return;
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
            {
                for (int i = 0; i < visDrawn; i++) pdr.ClearCustomBeam($"tv_s{i}");
                pdr.ClearCustomBeam("tv_cx"); pdr.ClearCustomBeam("tv_cy"); pdr.ClearCustomBeam("tv_cz");
            }
            visDrawn = 0; visCentroidDrawn = false;
        }
        void ClearVisMarks()
        {
            if (marksDrawn == 0) return;
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
                for (int i = 0; i < marksDrawn; i++) pdr.ClearXrayCube($"vmk_{name}_{i}");
            marksDrawn = 0;
        }

        // --- Visualization ---

        /// <summary>Full wipe — disable/consumer-loss only. Probe rays and
        /// impact marks are owned by TryGetVisibleVoxels's own trim loops —
        /// the field Redraw must not touch them (it fires on scan cadence and
        /// would erase them between probes).</summary>
        public void ClearViz()
        {
            ClearFieldBeams();
            ClearVisBeams();
        }

        void ClearFieldBeams()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null) foreach (var k in beamKeys) pdr.ClearCustomBeam(k);
            beamKeys.Clear();
            if (heatRend != null) heatRend.enabled = false;
        }

        /// <summary>Cyan radius ring at the owner's live position — a cheap
        /// 28-seg indicator, not scan output. Drawn per-frame while showRing
        /// is on; the scan-derived viz stays gated behind consumers.</summary>
        void DrawRing(Vector3 p)
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr == null) return;
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
            ClearFieldBeams();   // field viz only — probe rays/marks belong to the probe
            Vector3 p = lastScanPos;

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

            DrawHeat();
            PushFieldTexture();
        }

        // ---- Multi-agent field viz ----
        // Every active sensor contributes its local field as ONE slice of a
        // shared Texture2DArray; the shader unions all slices at each hit's
        // world XZ ("the city's shared cover awareness" — two lights
        // overlapping, not two writers racing). Previously all sensors pushed
        // a single _CoverFieldTex global — last-scan-wins made the disc
        // flicker between agents' fields.
        private const int MAX_FIELD_SOURCES = 4;
        private const int FIELD_TEX_RES = 64;   // contributing sensors must run this fieldRes
        private static Texture2DArray fieldTexArr;
        private static readonly Vector4[] fieldParams = new Vector4[MAX_FIELD_SOURCES];
        private static readonly bool[] slotTaken = new bool[MAX_FIELD_SOURCES];
        private static bool slotOverflowWarned;
        private int vizSlot = -1;

        /// <summary>
        /// Upload the packed field (R=score G=quality B=verdict A=proximity)
        /// into this sensor's array slice + write its params slot. Sources
        /// never touch each other's slice — compositing is the shader's job.
        /// </summary>
        void PushFieldTexture()
        {
            if (vizSlot < 0)
            {
                if (!field.HasData || field.N != FIELD_TEX_RES) return;
                for (int s = 0; s < MAX_FIELD_SOURCES; s++)
                    if (!slotTaken[s]) { slotTaken[s] = true; vizSlot = s; break; }
                if (vizSlot < 0)
                {
                    if (!slotOverflowWarned)
                    {
                        slotOverflowWarned = true;
                        Debug.LogWarning($"[CoverSensor] >{MAX_FIELD_SOURCES} field-viz contributors — '{name}' simulates but doesn't paint.");
                    }
                    return;
                }
            }
            if (!field.HasData || field.N != FIELD_TEX_RES)
            {
                if (fieldParams[vizSlot].w != 0f)
                {
                    fieldParams[vizSlot] = Vector4.zero;
                    Shader.SetGlobalVectorArray("_CoverFieldParamsArr", fieldParams);
                }
                return;
            }
            EnsureFieldTexArray();
            if (fieldPx == null || fieldPx.Length != field.N * field.N)
                fieldPx = new Color32[field.N * field.N];
            // Walk-domain V-modes: the slice carries DISPLAY colors (each
            // fighter's own mask/flood — the union reads "any fighter's
            // domain"), painted on the raymarched surfaces themselves. The
            // packed score/verdict channels stay for modes 4-6.
            var vm = chunkManager != null ? chunkManager.iterVisMode
                : (chunkManager = FindFirstObjectByType<VoxelChunkManager>()) != null
                    ? chunkManager.iterVisMode : VoxelChunkManager.IterVisMode.Off;
            if (vm == VoxelChunkManager.IterVisMode.CoverWalkable
                || vm == VoxelChunkManager.IterVisMode.CoverReachable)
            {
                var dcc = GetComponent<CombatController>();
                field.Bake(fieldPx,
                    vm == VoxelChunkManager.IterVisMode.CoverWalkable
                        ? CoverField.Layer.Walkable : CoverField.Layer.Reachable,
                    dcc != null ? dcc.DebugWalkMask : null,
                    dcc != null ? dcc.DebugBodyH : 1.8f,
                    dcc != null ? dcc.DebugMeMap : null);
            }
            else field.BakePacked(fieldPx);
            fieldTexArr.SetPixels32(fieldPx, vizSlot, 0);   // (colors, slice, mip=0)
            fieldTexArr.Apply(false, false);
            float size = field.CellSize * field.N;
            fieldParams[vizSlot] = new Vector4(field.Origin.x, field.Origin.z, 1f / size, 1f);
            Shader.SetGlobalVectorArray("_CoverFieldParamsArr", fieldParams);
        }

        private static void EnsureFieldTexArray()
        {
            if (fieldTexArr != null) return;
            fieldTexArr = new Texture2DArray(FIELD_TEX_RES, FIELD_TEX_RES,
                MAX_FIELD_SOURCES, TextureFormat.RGBA32, false);
            // 1 texel = 1 cell exactly (fieldRes is pinned to FIELD_TEX_RES) —
            // Point sampling paints each cell's own verdict with hard borders.
            // Bilinear interpolated BETWEEN cells, fabricating half-verdicts
            // at seams (a "half-walkable" color doesn't exist).
            fieldTexArr.filterMode = FilterMode.Point;
            fieldTexArr.wrapMode = TextureWrapMode.Clamp;
            Shader.SetGlobalTexture("_CoverFieldTexArr", fieldTexArr);
        }

        /// <summary>
        /// Ground-projected heat quad — the field baked to a texture on a flat
        /// mesh (same child-GO + Sprites/Default pattern as the vision wedge).
        /// The composite layer IS the decision surface: the chosen cell flashes
        /// white so "why there" reads as "the heat max".
        /// </summary>
        void DrawHeat()
        {
            // Walkable/Reachable are threat-independent — the domain exists
            // with or without a fight (a stale field still paints the last
            // build's truth).
            bool walkLayer = heatLayer >= CoverField.Layer.Walkable;
            bool want = showHeat && field.HasData && (HasThreat || walkLayer);
            if (!want)
            {
                if (heatRend != null) heatRend.enabled = false;
                return;
            }
            EnsureHeatQuad();
            if (heatRend == null) return;

            // Rebake when resolution or layer changes (field contents change
            // every scan — bake each Redraw, it's 64² cells; the walk layers
            // stamp GRID texels per cell so the domain reads as tiles).
            int n = field.N;
            int grid = walkLayer ? 4 : 1;
            int res = n * grid;
            if (heatTex.width != res || bakedRes != res)
            {
                heatTex.Reinitialize(res, res);
                heatPx = new Color32[res * res];
                bakedRes = res;
            }
            // The walkable layers bake the domain through the ACTOR's own
            // mask + flood — the field holds channels, the controller owns
            // designation.
            System.Collections.Generic.ICollection<int> wmask = null;
            float wbh = 1.8f;
            CoverField.DistMap reach = null;
            if (walkLayer)
            {
                var cc = GetComponent<CombatController>();
                if (cc != null)
                { wmask = cc.DebugWalkMask; wbh = cc.DebugBodyH; reach = cc.DebugMeMap; }
            }
            // Bake per-cell verdicts first, then stamp tiles for the walk
            // layers: `grid` texels per cell + a dark border — the domain
            // reads as a literal tile grid on the ground, not a color wash.
            int cn = n * n;
            if (cellPx == null || cellPx.Length != cn) cellPx = new Color32[cn];
            field.Bake(cellPx, heatLayer, wmask, wbh, reach);
            heatTex.filterMode = walkLayer ? FilterMode.Point : FilterMode.Bilinear;
            if (grid == 1) System.Array.Copy(cellPx, heatPx, cn);
            else
            {
                for (int cz = 0; cz < n; cz++)
                    for (int cx = 0; cx < n; cx++)
                    {
                        Color32 c = cellPx[cz * n + cx];
                        for (int ty = 0; ty < grid; ty++)
                            for (int tx = 0; tx < grid; tx++)
                                heatPx[(cz * grid + ty) * res + cx * grid + tx] =
                                    (tx == 0 || ty == 0) ? GRID_LINE : c;
                    }
            }
            heatTex.SetPixels32(heatPx);
            heatTex.Apply(false, false);

            float size = field.CellSize * n;
            // Two fighters' quads paint the same arena — vizSlot gives each a
            // 2cm lift so both can display at once without z-fighting.
            float lift = 0.09f + Mathf.Max(0, vizSlot) * 0.02f;
            heatGO.transform.position = new Vector3(
                field.Origin.x + size * 0.5f, field.GroundY + lift,
                field.Origin.z + size * 0.5f);
            heatGO.transform.localScale = new Vector3(size, size, 1f);
            heatRend.enabled = true;
        }

        void EnsureHeatQuad()
        {
            if (heatGO != null) return;
            heatGO = new GameObject("_CoverHeat");
            // Not parented — the quad tracks the field origin, not the character.
            var mf = heatGO.AddComponent<MeshFilter>();
            var m = new Mesh { name = "CoverHeatQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),  new Vector3(0.5f, 0.5f, 0f),
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds();
            mf.sharedMesh = m;
            heatRend = heatGO.AddComponent<MeshRenderer>();
            heatRend.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            heatRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            heatRend.receiveShadows = false;
            // Flat on the ground — quad faces +Y via -90° pitch.
            heatGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            heatTex = new Texture2D(field.N > 0 ? field.N : 64, field.N > 0 ? field.N : 64,
                TextureFormat.RGBA32, false);
            heatTex.filterMode = FilterMode.Bilinear;
            heatTex.wrapMode = TextureWrapMode.Clamp;
            heatRend.sharedMaterial.mainTexture = heatTex;
        }
    }
}
