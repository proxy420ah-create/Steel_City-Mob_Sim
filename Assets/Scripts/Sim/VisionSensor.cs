using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Vision sense — a standing perception primitive (hoods ID'ing enemies,
    /// police observing crime, civilians spotting gunmen).
    ///
    /// The eye + forward come from the POSED head joint each frame — the
    /// Looking state's authored head-yaw sweep physically sweeps this cone, so
    /// the scan animation IS the mechanic: a target inside the cone between
    /// sweeps is only intermittently exposed, and detection accumulates over
    /// time-in-cone rather than being binary.
    ///
    /// Perception test per target:
    ///   within `range` && yaw-angle to target ≤ `halfAngleDeg`
    ///   && VoxelCollisionWorld LOS ray eye→target unblocked
    /// — the same occlusion law cover uses: cover that blocks a bullet also
    /// blocks the gaze.
    ///
    /// Detection: 0..1 per target — rises while visible (riseTime to full),
    /// decays while out of the cone (decayTime to forget). Sweep gaps produce
    /// natural detection stalls.
    ///
    /// Testbed: targets arrive as points (VISION tab uses the shared PICK AIM
    /// point as "the thing Vinny is looking at"); unit targets plug in later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VisionSensor : MonoBehaviour
    {
        [Header("Perception")]
        [Tooltip("Vision range (m).")]
        public float range = 25f;
        [Tooltip("Half the cone's horizontal opening (deg).")]
        public float halfAngleDeg = 55f;
        [Tooltip("Seconds of continuous visibility to reach full detection.")]
        public float riseTime = 0.6f;
        [Tooltip("Seconds of no-visibility to fully forget.")]
        public float decayTime = 2.0f;
        [Tooltip("Eye height when no anim data exists (m).")]
        public float eyeFallback = 0.55f;

        [Header("Visualization (VISION tab)")]
        public bool showCone = false;
        [Tooltip("Sprite wedge fill — renders BEHIND the raymarched world (depth-tested scene mesh); superseded by V-mode 7 painting the cone on the voxels. Off by default.")]
        public bool showConeFill = false;
        public bool showTargetRay = false;
        public int coneArcSegs = 14;

        /// <summary>Live eye anchor — painted Eyes/Face centroid through the
        /// posed head chain when the asset carries regions; head-joint then
        /// body fallback below it.</summary>
        public Vector3 EyePos { get; private set; }
        public Vector3 EyeDir { get; private set; } = Vector3.forward;
        public bool EyePosed { get; private set; }
        /// <summary>HUD readout — mirrors the animator's anchor status line.</summary>
        public string EyeAnchorDesc { get; private set; } = "unresolved";

        /// <summary>Highest current detection level (0..1) across targets.</summary>
        public float MaxDetection { get; private set; }
        /// <summary>Any target fully detected right now.</summary>
        public bool AnyDetected { get; private set; }
        /// <summary>Any target currently inside an unblocked cone.</summary>
        public bool AnyVisible { get; private set; }

        private VoxelCollisionWorld world;
        private VoxelCharacter owner;
        private readonly List<Vector3> targets = new();
        private readonly Dictionary<Vector3, float> detection = new();
        private readonly List<Vector3> pruneBuf = new();
        private readonly HashSet<string> beamKeys = new();

        void Awake()
        {
            world = FindFirstObjectByType<VoxelCollisionWorld>();
            owner = GetComponent<VoxelCharacter>();
        }

        /// <summary>Only ONE sensor may drive the shared _VisConeA/B shader
        /// globals — every sensor's Update used to push them, so the last writer
        /// won and the painted cone could follow Tan Suit instead of the
        /// character being inspected. null = no owner → legacy all-write.</summary>
        public static VisionSensor ConeOwner;

        void OnDisable()
        {
            ClearViz();
            if (ConeOwner == null || ConeOwner == this)
            {
                Shader.SetGlobalVector("_VisConeB", Vector4.zero);   // mode-7 cone off
                ConeOwner = null;
            }
        }

        /// <summary>Testbed target source — a single observed point.</summary>
        public void SetTarget(Vector3? t)
        {
            targets.Clear();
            if (t.HasValue) targets.Add(t.Value);
        }

        public void AddTarget(Vector3 t) { if (!targets.Contains(t)) targets.Add(t); }
        public bool TryGetDetection(Vector3 t, out float level) => detection.TryGetValue(t, out level);

        void Update()
        {
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (world == null) return;
            ResolveEye();
            Evaluate(Time.deltaTime);
            PushConeGlobals();
            Redraw();
        }

        /// <summary>V-mode 7 (VisionCone) reads these globals to paint the cone
        /// on the raymarched voxels — the scene-space wedge mesh can't overlay
        /// a raymarch+RT pipeline, so the shader channel is the real display.
        /// Pushed every frame: two SetGlobalVector calls.</summary>
        void PushConeGlobals()
        {
            if (ConeOwner != null && ConeOwner != this) return;
            Vector3 d = DirXZ(EyePos, EyePos + EyeDir);
            Shader.SetGlobalVector("_VisConeA", new Vector4(EyePos.x, EyePos.z, d.x, d.z));
            Shader.SetGlobalVector("_VisConeB", new Vector4(
                Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad), range, 0.35f,
                showCone ? 1f : 0f));
        }



        /// <summary>Eye = painted region centroid posed through the head chain
        /// (live — rides Looking sweeps); head-joint fallback; fixed body-height
        /// eye when no anim data exists.</summary>
        void ResolveEye()
        {
            Vector3 p = default, f = Vector3.forward;
            VoxelCharacter.EyeAnchorKind src = VoxelCharacter.EyeAnchorKind.None;
            EyePosed = owner != null && owner.TryGetEyeAnchor(out p, out f, out src);
            if (EyePosed)
            {
                EyePos = p;
                EyeDir = f;
                EyeAnchorDesc = src == VoxelCharacter.EyeAnchorKind.Eyes
                    ? $"eyes centroid ({owner.LastEyeAnchorVox} vox) — posed"
                    : src == VoxelCharacter.EyeAnchorKind.Face
                    ? $"face centroid ({owner.LastEyeAnchorVox} vox) — posed"
                    : "head joint — posed";
            }
            else
            {
                Vector3 dims = owner != null ? owner.WorldSize : new Vector3(0.6f, 1.8f, 0.6f);
                EyePos = transform.position + new Vector3(dims.x * 0.5f, eyeFallback, dims.z * 0.5f);
                EyeDir = transform.forward;
                EyeAnchorDesc = "body fallback (no anim data)";
            }
        }

        /// <summary>Yaw-projected direction to a world point (the cone is a horizontal fan).</summary>
        static Vector3 DirXZ(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        void Evaluate(float dt)
        {
            MaxDetection = 0f; AnyDetected = false; AnyVisible = false;
            Vector3 eyeDirXZ = DirXZ(EyePos, EyePos + EyeDir);
            for (int i = 0; i < targets.Count; i++)
            {
                Vector3 t = targets[i];
                Vector3 to = t - EyePos;
                float dist = to.magnitude;
                bool inCone = dist <= range;
                if (inCone)
                {
                    float ang = Vector3.Angle(eyeDirXZ, DirXZ(EyePos, t));
                    inCone = ang <= halfAngleDeg;
                }
                bool visible = false;
                if (inCone && dist > 1e-4f)
                    visible = !world.Raycast(EyePos, to / dist, dist - 0.05f, out _);

                float lvl = detection.TryGetValue(t, out float cur) ? cur : 0f;
                lvl = Mathf.Clamp01(lvl + dt * (visible ? 1f / Mathf.Max(0.01f, riseTime)
                                                      : -1f / Mathf.Max(0.01f, decayTime)));
                detection[t] = lvl;
                if (visible) AnyVisible = true;
                if (lvl > MaxDetection) MaxDetection = lvl;
                if (lvl >= 0.99f) AnyDetected = true;
            }
            // Prune fully-decayed entries so the dict stays small.
            if (detection.Count > 16)
            {
                pruneBuf.Clear();
                foreach (var kv in detection) if (kv.Value <= 0f) pruneBuf.Add(kv.Key);
                foreach (var k in pruneBuf) detection.Remove(k);
            }
        }

        // --- Visualization ---
        // The cone's fill is a real translucent wedge mesh (triangle fan at eye
        // height) — a debug overlay that can't silently vanish like GL lines.
        // Beams carry the crisp parts: rim arc, edge rays, pitched center ray,
        // and per-target sightlines.

        private GameObject wedgeGO;
        private Mesh wedgeMesh;
        private MeshRenderer wedgeRenderer;
        private MaterialPropertyBlock wedgeMpb;
        private float wedgeRange = -1f, wedgeHalf = -1f;
        private int wedgeSegs = -1;

        void EnsureWedge()
        {
            if (wedgeGO != null) return;
            wedgeGO = new GameObject("_VisionWedge");
            wedgeGO.transform.SetParent(transform, false);
            var mf = wedgeGO.AddComponent<MeshFilter>();
            wedgeMesh = new Mesh { name = "VisionWedge" };
            mf.sharedMesh = wedgeMesh;
            wedgeRenderer = wedgeGO.AddComponent<MeshRenderer>();
            // Sprites/Default — proven resolvable in builds (PathDebugRenderer's
            // own fallback); unlit + transparent + always bundled.
            wedgeRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            wedgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wedgeRenderer.receiveShadows = false;
            wedgeMpb = new MaterialPropertyBlock();
            wedgeRange = -1f; // force first build
        }

        void RebuildWedge()
        {
            int segs = Mathf.Max(4, coneArcSegs);
            var verts = new Vector3[segs + 2];
            var tris = new int[segs * 3];
            verts[0] = Vector3.zero; // apex at the eye
            float half = halfAngleDeg * Mathf.Deg2Rad;
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(-half, half, (float)i / segs);
                verts[i + 1] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * range;
            }
            for (int i = 0; i < segs; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 2;   // +Z-forward winding, up-facing
                tris[i * 3 + 2] = i + 1;
            }
            wedgeMesh.Clear();
            wedgeMesh.vertices = verts;
            wedgeMesh.triangles = tris;
            wedgeMesh.RecalculateBounds();
            wedgeRange = range; wedgeHalf = halfAngleDeg; wedgeSegs = coneArcSegs;
        }

        public void ClearViz()
        {
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
                foreach (var k in beamKeys) pdr.ClearCustomBeam(k);
            beamKeys.Clear();
            if (wedgeRenderer != null) wedgeRenderer.enabled = false;
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
            if (!showCone && !showTargetRay) return;

            // Alertness colors: idle pale blue → amber while detecting → red when seen.
            Color coneCol = AnyDetected ? new Color(1f, 0.3f, 0.2f, 0.6f)
                          : MaxDetection > 0.01f ? new Color(1f, 0.75f, 0.2f, 0.5f)
                          : new Color(0.45f, 0.75f, 1f, 0.35f);

            if (showCone)
            {
                Vector3 dirXZ = DirXZ(EyePos, EyePos + EyeDir);
                float baseA = Mathf.Atan2(dirXZ.x, dirXZ.z);

                if (showConeFill)
                {
                    // Translucent wedge fill — scene-space mesh; loses ZTest to
                    // the raymarched voxels, so it reads "underneath" the world.
                    // Superseded by V-mode 7 for the real cone display.
                    EnsureWedge();
                    if (wedgeRange != range || wedgeHalf != halfAngleDeg || wedgeSegs != coneArcSegs)
                        RebuildWedge();
                    Vector3 yawFwd = dirXZ.sqrMagnitude > 1e-6f ? dirXZ.normalized : Vector3.forward;
                    wedgeGO.transform.SetPositionAndRotation(
                        EyePos, Quaternion.LookRotation(yawFwd, Vector3.up));
                    wedgeMpb.SetColor("_Color", new Color(1f, 0.55f, 0.1f, 0.16f));
                    wedgeRenderer.SetPropertyBlock(wedgeMpb);
                    wedgeRenderer.enabled = true;
                }

                // Rim arc + edge rays — the crisp outline over the fill.
                float half = halfAngleDeg * Mathf.Deg2Rad;
                Vector3 prev = EyePos + new Vector3(Mathf.Sin(baseA - half), 0f, Mathf.Cos(baseA - half)) * range;
                Beam("vs_e0", EyePos, prev, 0.035f, coneCol);
                for (int s = 1; s <= coneArcSegs; s++)
                {
                    float a = baseA + Mathf.Lerp(-half, half, (float)s / coneArcSegs);
                    Vector3 tip = EyePos + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * range;
                    Beam($"vs_a{s}", prev, tip, 0.035f, coneCol);
                    prev = tip;
                }
                Beam("vs_e1", EyePos, prev, 0.035f, coneCol);
                // True eye ray (pitched — shows head pitch, not just yaw).
                Beam("vs_center", EyePos, EyePos + EyeDir * range, 0.035f, coneCol);
            }

            if (showTargetRay)
            {
                for (int i = 0; i < targets.Count && i < 8; i++)
                {
                    Vector3 t = targets[i];
                    float lvl = detection.TryGetValue(t, out float l) ? l : 0f;
                    Beam($"vs_t{i}", EyePos, t, 0.025f,
                        lvl >= 0.99f ? new Color(1f, 0.25f, 0.2f, 0.8f)
                        : lvl > 0.01f ? new Color(1f, 0.75f, 0.2f, 0.6f)
                        : new Color(0.5f, 0.9f, 1f, 0.35f));
                }
            }
        }
    }
}
