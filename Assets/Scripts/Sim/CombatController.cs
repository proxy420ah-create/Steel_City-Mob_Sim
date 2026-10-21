using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Autonomous combat v1 (docs/systems/COMBAT_COVER_STATE.md).
    ///
    /// EnterCombat(threatPos) → scan `coverRadius` for a verified cover point →
    /// SeekingCover steers to it (always Walking pick; combat owns the approach
    /// posture — anim.duck eases to the pick's requirement → wd() renders the
    /// crumple under the gait) →
    /// InCover: stationary = authored Crouching hold; ArmAimSolver is gated
    /// off while ducked — aiming is the pop-up/exposed phase.
    /// No cover in radius → HoldPosition: crouch+aim in place.
    ///
    /// Combat poses ride the PRIORITY_COMBAT lane; debug keys (PRIORITY_DEBUG)
    /// outrank them and stay live during combat — a held debug pose also owns
    /// the crouch-depth channel (the controller's depth ease yields).
    /// </summary>
    [RequireComponent(typeof(CharacterRig))]
    public class CombatController : MonoBehaviour
    {
        public enum CombatState : byte { Idle = 0, SeekingCover = 1, InCover = 2, HoldPosition = 3 }
        /// <summary>The engagement rhythm inside InCover — the authored beat
        /// the gates answer to. None = steady hold (arm stowed; beats own the
        /// arm, not the exposure probe).</summary>
        public enum EngagementBeat : byte { None = 0, Settle = 1, TopUp = 2, Rise = 3, Engaged = 4, Duck = 5, Swing = 6 }

        [Header("Movement")]
        [Tooltip("Combat hustle speed (m/s) — drives both the transform step and walk anim rate.")]
        public float moveSpeed = 2.4f;
        /// <summary>XZ arrival tolerance — the committed destination is a cell
        /// center on open floor, so the residual should be visual-noise scale,
        /// not a third of a body width. StepToward clamps the final step at
        /// Min(step,dist), so the point is reachable exactly; a blocked last
        /// inch surfaces as a leg abort, not a silent offset.</summary>
        public float arriveEps = 0.06f;
        public float turnSpeedDeg = 540f;
        [Tooltip("Duck fold-in rate during SeekingCover (duck/s) — the advance is combat posture: he adopts the destination wall's requirement for the whole approach, not when a painted zone reaches him.")]
        public float seekDuckEase = 2.5f;
        // (crouchDepthSpeed removed — duck writes are immediate now)

        [Header("Exposure fire (docs/systems/EXPOSURE_ENGAGEMENT.md)")]
        [Tooltip("The body being fought — a bare feet position can't be silhouette-sampled. Set via EnterCombat(threatPos, threatBody).")]
        public VoxelCharacter ThreatCharacter;
        [Tooltip("Seconds between posed-silhouette visibility probes.")]
        public float visInterval = 0.12f;
        [Tooltip("Revolver cadence — deliberate double-action seconds between shots.")]
        public float fireInterval = 0.9f;
        [Tooltip("Model 10 cylinder — after this many shots he lowers the gun for the reload beat.")]
        public int cylinderCapacity = 6;
        [Tooltip("Staggered reload — once the wheel runs dry, one round seats every N seconds; the arm stays dipped until all chambers are full (no reload anim exists — the dip IS the beat).")]
        public float roundLoadInterval = 1.5f;
        [Tooltip("Max bore-vs-centroid error (deg) that still counts as converged.")]
        public float aimGateDeg = 14f;

        [Header("Engagement beats (docs/systems/ENGAGEMENT_BEATS.md)")]
        [Tooltip("The reusable seam — dwell between every beat (top-up wait, post-arrival gather, post-duck gather). 5s while debugging so each seam is watchable.")]
        /// <summary>Per-beat dwell seconds — the debug tempo FLOOR. Indexed
        /// by EngagementBeat (0 = None, unused). Event verdicts stay honest:
        /// a beat can't exit before its dwell, but physical verdicts (probe
        /// contact, reload done) still fire — the timer gates the LEAVE,
        /// never the observation. HUD TEMPO table edits these live.</summary>
        public float[] beatDwell = { 0f, 5f, 5f, 5f, 5f, 5f, 5f };
        [Tooltip("The arm survives a dropped probe this long — a single empty scan must not pump-stow him. Authority loss (reload/no weapon/beat closed) still stows instantly.")]
        public float disarmHoldSec = 0.35f;
        [Tooltip("Consecutive empty visibility probes in Engaged before he drops — a dropped-frame probe must not bounce him (4 ≈ 0.5s at visInterval). Event-count, not a clock.")]
        public int emptyProbesToDrop = 4;
        [Tooltip("Duck ease while rising / dropping back into cover (duck/s).")]
        public float riseDuckEase = 3f;
        [Tooltip("Clearance margin above the wall top for the rise solve (m).")]
        public float riseMargin = 0.02f;
        [Tooltip("Bore sits ~this far BELOW the eye when aimed — sights sit above the bore line (sight radius). The presented muzzle is eye−sightGap, not the wrist pivot.")]
        public float sightGap = 0.045f;
        [Tooltip("Armed reach toward the target — how far the presented gun extends past the eye anchor horizontally (barrel poked over a parapet).")]
        public float muzzleFwd = 0.3f;
        [Tooltip("When a rhythm run ends (rise refused / no contact / target lost), loop back through the seam and try again instead of parking at None.")]
        public bool loopBeats = true;
        [Tooltip("Hide-only combatant: seeks first cover exactly like a fighter, then parks in the settle seam forever — no rise, no fire authority anywhere. The 3-stage-search test dummy.")]
        public bool holdInCover;
        [Tooltip("Cover-positioning intelligence 0..1 (Hoods.xtx INT normalized): 1 = nests at the zone centroid, 0 = parks on the Hidden rim — zero margin, 'sticking out'. COMBAT_COVER_STATE → INT axis.")]
        [Range(0f, 1f)] public float coverIntNorm = 1f;
        /// <summary>Settle posture facing: back to the wall (either side can
        /// side-peek) or a committed side for the lateral peek. Random rolls
        /// per settle entry — the rolled side IS the peek commitment.</summary>
        public enum SettleFace : byte { Random, Back, Left, Right }
        public SettleFace settleFace = SettleFace.Random;
        /// <summary>The rolled settle commitment: 0 = back to cover (either
        /// side open for a swing-peek), −1/+1 = facing left/right of the
        /// threat bearing — the side the Swing beat will step out on.</summary>
        public sbyte PeekSide { get; private set; }
        private float settleYawDeg, engageYawDeg;
        /// Event-driven belief: contactSeen edges snap ThreatPos to computed
        /// centroids; lastConfirmed caches the centroid while contact holds.
        private bool contactSeen;
        private Vector3 lastConfirmed;
        /// Rise endpoints — captured at EnterBeat so the turn rides the SAME
        /// normalized progress as the unfold (one motion, not two eases).
        private float riseFromDuck, riseFromYawDeg;
        /// Duck endpoints — the fold-back rides the same coupling.
        private float foldFromDuck, foldFromYawDeg;

        /// THE single root-yaw write path: declaration sites set a MODE
        /// (intent — the roll's settleYawDeg, a destination, a live body,
        /// or an endpoint curve); <see cref="TickFacing"/> renders it
        /// (effect — the only SetYaw caller graph). Contracts: sites always
        /// declare SOME mode per relevant tick/entry (a kill-switch declares
        /// Free rather than leaving stale intent); the mode LATCHES until
        /// replaced, so EnterBeat declarations hold for the beat's life.
        public enum FacingMode : byte { Free, Hold, Track, Curve }
        private FacingMode faceMode = FacingMode.Free;
        private float faceFrom, faceTo;
        private Vector3 facePoint;
        private Func<float> faceT;
        private string faceWho = "free";

        void FaceFree() { faceMode = FacingMode.Free; faceWho = "free"; }
        void FaceHold(float deg, string who) { faceMode = FacingMode.Hold; faceTo = deg; faceWho = who; }
        void FaceTrack(Vector3 p, string who) { faceMode = FacingMode.Track; facePoint = p; faceWho = who; }
        void FaceCurve(float from, float to, Func<float> t, string who)
        { faceMode = FacingMode.Curve; faceFrom = from; faceTo = to; faceT = t; faceWho = who; }

        // Curve progress sources — the duck scalar IS the transition clock.
        float RiseT() => anim == null || Mathf.Abs(riseFromDuck - riseDuck) < 0.02f
            ? 1f : Mathf.InverseLerp(riseFromDuck, riseDuck, anim.duck);
        float FoldT() => anim == null || Mathf.Abs(foldFromDuck - targetDuck) < 0.02f
            ? 1f : Mathf.InverseLerp(foldFromDuck, targetDuck, anim.duck);

        void TickFacing()
        {
            if (!dbgFacing) { FaceDemandDeg = 0f; return; }
            float cur = transform.eulerAngles.y;
            switch (faceMode)
            {
                case FacingMode.Curve:
                {
                    // The curve IS the ease — written verbatim, deadbanded.
                    float t = Mathf.Clamp01(faceT != null ? faceT() : 0f);
                    float want = Mathf.LerpAngle(faceFrom, faceTo, Mathf.SmoothStep(0f, 1f, t));
                    FaceDemandDeg = Mathf.DeltaAngle(cur, want);
                    if (Mathf.Abs(FaceDemandDeg) > 0.5f)
                        SetYaw(want, faceWho);
                    break;
                }
                case FacingMode.Hold:
                case FacingMode.Track:
                {
                    float want = faceMode == FacingMode.Track
                        ? BearingDeg(facePoint - Feet()) : faceTo;
                    FaceDemandDeg = Mathf.DeltaAngle(cur, want);
                    if (Mathf.Abs(FaceDemandDeg) > 0.5f)
                        SetYaw(Mathf.MoveTowardsAngle(cur, want,
                            turnSpeedDeg * Time.deltaTime), faceWho);
                    break;
                }
                default: FaceDemandDeg = 0f; break;
            }
        }
        /// The CoverPoint the facing commitment was rolled for — a new spot
        /// re-rolls; the same spot KEEPS its posture across every seam.
        private Vector3? settleSpot;
        /// Duck's off-spot return — phase C (posture turn) is owned by the
        /// return, not the seam: the turn converges BEFORE Settle begins.
        private bool returnTurning;

        /// <summary>Swing beat (lateral peek): step out past the zone edge on
        /// PeekSide's edge, run the Looking sweep while the vision cone is the
        /// detection channel + he's exposed out there, step back if empty.
        /// `swingMargin` = clearance past the painted rim (m).</summary>
        public float swingMargin = 0.4f;
        [Range(0f, 1f), Tooltip("Dwell posture — the corner lean, shallower than the cover fold. Dwell TIME rides beatDwell[Swing].")]
        public float swingDuck = 0.35f;
        private byte swingPhase;                 // 0 step-out · 1 dwell · 2 step-back
        private Vector3 swingDest, swingStuckPos;
        private float swingDwellUntil, swingStuckT;
        /// Per-leg unstick budget — perpendicular sidesteps spent before a
        /// StepToward leg reports "done off-target" (caller re-verifies).
        private int stuckNudges, stuckNudgeSide;
        private bool returnWalked;             // Duck's off-spot return leg began
        /// SeekingCover's last move is the posture turn to the rolled facing —
        /// set when he first lands on the spot; the state only completes when
        /// position ∧ facing BOTH converge (arrival ≠ just "reached the point").
        private bool coverArrived;
        private VisionSensor vis;

        /// <summary>Yaw forensic channel: every combat write goes through
        /// <see cref="SetYaw"/> tagged by owner; LateUpdate catches yaw that
        /// changed with NO combat writer (external contamination — the
        /// camera/orbit → rig leak under test).</summary>
        [Tooltip("Log yaw writes + external writers — chase camera→rig wiggle.")]
        public bool yawWatch = true;

        [Header("DEBUG — channel kill-switches (bisect the jitter)")]
        [Tooltip("Master gate on the facing driver — off = ZERO combat yaw writes, guaranteed.")]
        public bool dbgFacing = true;
        [Tooltip("Engaged live-tracks the belief target (last-known-pos facing). Off = holds entry engageYawDeg.")]
        public bool dbgEngagedTrack = true;
        [Tooltip("Arm solver may raise at all (wantAim/holding/swingAim). Off = permanently stowed in combat.")]
        public bool dbgArmSolver = true;
        [Tooltip("Movement drivers may write yaw (seek / swing steps / relocate). Off = strafe-slide.")]
        public bool dbgMoveYaw = true;
        [Tooltip("Swing dwell feeds the vision cone the LIVE threat point (the cone-detection channel).")]
        public bool dbgVisionFeed = true;

        private float yawAtLastCheck, lastYawLogT;
        private string lastYawWriter = "init";

        // ── Yaw force monitor (FORCES tab) — signed °/s actually written,
        // per owner. SetYaw accumulates Δdeg per frame; LateUpdate folds it
        // into an EMA (decays to silence when a writer stops) so the HUD
        // can draw L/0/R bars and watch owners fire in real time.
        public readonly Dictionary<string, float> YawForce = new();
        readonly Dictionary<string, float> yawAccum = new();
        public FacingMode DebugFaceMode => faceMode;
        public string DebugFaceOwner => faceWho;
        /// <summary>Signed ° the driver wants to close right now — the
        /// demand, even when the deadband means nothing is written.</summary>
        public float FaceDemandDeg { get; private set; }

        void SetYaw(float y, string who)
        {
            float cur = transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, y, 0f);
            float d = Mathf.DeltaAngle(cur, y);
            yawAccum[who] = (yawAccum.TryGetValue(who, out var acc) ? acc : 0f) + d;
            // First write per owner + a heartbeat while an ease runs —
            // enough to name the driver without spamming every frame.
            if (yawWatch && Mathf.Abs(d) > 0.1f
                && (who != lastYawWriter || Time.unscaledTime - lastYawLogT > 0.5f))
            {
                Debug.Log($"[Yaw] {name} ← {who} · {cur:F1}→{y:F1} · beat {Beat}");
                lastYawLogT = Time.unscaledTime;
            }
            lastYawWriter = who;
        }

        void LateUpdate()
        {
            float y = transform.eulerAngles.y;
            // Idle = outside combat ownership — possess, teleports and the
            // sim all legitimately rotate him there; only combat is policed.
            if (State != CombatState.Idle)
            {
                // Residual = observed Δ minus THIS frame's combat writes —
                // an external writer can't hide inside a written frame.
                float observed = Mathf.DeltaAngle(yawAtLastCheck, y);
                float combatDelta = 0f;
                foreach (var v in yawAccum.Values) combatDelta += v;
                float residual = Mathf.DeltaAngle(combatDelta, observed);
                if (Mathf.Abs(residual) > 0.1f)
                {
                    yawAccum["EXTERNAL"] = (yawAccum.TryGetValue("EXTERNAL", out var ea) ? ea : 0f) + residual;
                    if (yawWatch)
                        Debug.Log($"[Yaw?] {name} EXTERNAL Δ{residual:F1}° → {y:F1} · beat {Beat} state {State}"
                                + $" (residual over combat writers — last: {lastYawWriter})");
                }
            }
            yawAtLastCheck = y;

            // Fold this frame's impulses into the smoothed monitor — writers
            // convert to °/s, silent owners decay toward zero.
            float fdt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            float k = 1f - Mathf.Exp(-8f * fdt);
            foreach (var kvp in yawAccum)
                YawForce[kvp.Key] = Mathf.Lerp(
                    YawForce.TryGetValue(kvp.Key, out var cur2) ? cur2 : 0f,
                    kvp.Value / fdt, k);
            var keys = new List<string>(YawForce.Keys);
            foreach (var key in keys)
                if (!yawAccum.ContainsKey(key))
                    YawForce[key] = Mathf.Lerp(YawForce[key], 0f, k);
            yawAccum.Clear();
        }

        // --- Cover parameters live on CoverSensor (the always-on awareness sense);
        // these stay as proxies so existing HUD sliders keep working. ---
        public float coverRadius { get => Sensor.radius; set => Sensor.radius = value; }
        public float coverStandoff { get => Sensor.standoff; set => Sensor.standoff = value; }

        public bool showCoverMarkers { get => Sensor.showCandidates; set => Sensor.showCandidates = value; }

        /// <summary>One subscription covers every display-relevant mutation —
        /// state transitions, picks/verdicts, armed edges, shots, reloads.
        /// Event-driven contract: consumers rebuild on Changed, never poll.
        /// (Flags are marked dirty, not rebuilt inside the event — the
        /// mutation sequence finishes first.)</summary>
        public event Action Changed;
        /// <summary>Successful shots this engagement — the flow chart's FIRE beat.</summary>
        public int ShotsFired { get; private set; }
        void Notify() => Changed?.Invoke();
        void SetState(CombatState s)
        {
            if (State == s) return;
            State = s;
            if (s == CombatState.SeekingCover)
            {
                coverArrived = false;
                swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0;
            }
            Notify();
        }

        public CombatState State { get; private set; }
        /// <summary>Current engagement beat — the panel's third lane.</summary>
        public EngagementBeat Beat { get; private set; }
        /// <summary>Duck the last Rise solve picked — min duck clearing eye+muzzle over the wall.</summary>
        public float RiseDuck => riseDuck;
        /// <summary>Seconds left on the current beat's timer (Settle/Rise; else 0) — the panel's inline countdown.</summary>
        public float BeatLeft => Beat == EngagementBeat.None ? 0f
            : Mathf.Max(0f, beatUntil - Time.unscaledTime);   // every beat carries a dwell floor now
        /// <summary>The active beat's dwell floor — progress-bar denominators.</summary>
        public float BeatDwellNow => DwellFor(Beat);
        /// <summary>The seam's branch point — which beat this Settle exits to.</summary>
        public EngagementBeat NextAfterSettle => loopGate && !loopBeats
            ? EngagementBeat.None : afterSettle;   // honest preview of the gated exit
        /// <summary>Diagnosis of the last Rise attempt — what the solve found
        /// or why it refused / expired. Panel + log surface.</summary>
        public string LastRiseDiag { get; private set; }
        /// <summary>Class of the wall he's picked (Full = stand behind it).</summary>
        public CoverSensor.WallClass CoverWallClass => coverWallClass;
        /// <summary>Duck scalar (0..2) the picked cover demands — sub-1 = morph
        /// band, over-1 = deep band. Possess freezes this while combatHold.</summary>
        public float TargetDuck => targetDuck;
        public CoverSensor.WallClass CoverClass => coverWallClass;
        public Vector3 ThreatPos { get; private set; }
        public Vector3 CoverPoint { get; private set; }
        public string CoverObstacleId { get; private set; }
        public string LastResult { get; private set; } = "";
        public CoverSensor.ScanStats lastStats => Sensor.stats;

        /// <summary>The threat's visible-silhouette centroid the last time any
        /// voxel resolved — the aim point's memory. Pop-up + search phases
        /// re-query against it.</summary>
        public Vector3? LastSeenTarget { get; private set; }
        public float LastSeenAt { get; private set; }
        /// <summary>Rounds left in the Model 10 cylinder.</summary>
        public int Cylinder => cylinder;
        /// <summary>Combat has the arm up — exposure-gated (visible voxels &gt; 0
        /// and not in the reload beat). Drives the Aim pose lanes.</summary>
        public bool ExposureArmed { get; private set; }
        /// <summary>The sensor's last visibility probe of ThreatCharacter.</summary>
        public CoverSensor.VisibleSet Visibility => sensor != null ? sensor.LastVisible : default;
        /// <summary>Time of the last successful shot — the flow panel's FIRE node.</summary>
        public float LastShotAt { get; private set; } = -1f;
        /// <summary>Seconds until the cadence window opens (0 = clear to fire).</summary>
        public float NextFireIn => Mathf.Max(0f, nextFireTime - Time.unscaledTime);
        /// <summary>True while a dry cylinder is being re-seated round-by-round.</summary>
        public bool Reloading => reloading;
        /// <summary>Seconds until the wheel is full again — next seat plus the
        /// remaining chambers' intervals (0 unless reloading).</summary>
        public float ReloadLeft => reloading
            ? Mathf.Max(0f, nextRoundLoad - Time.unscaledTime) + (cylinderCapacity - cylinder - 1) * roundLoadInterval
            : 0f;
        /// <summary>Seconds until the NEXT chamber seats — the flow dots' next tick.</summary>
        public float NextRoundIn => reloading ? Mathf.Max(0f, nextRoundLoad - Time.unscaledTime) : 0f;

        /// <summary>Spend one chamber — the shared trigger debit for BOTH the
        /// AI fire loop and the possess LMB path. No-op on an empty wheel.</summary>
        public bool SpendRound()
        { if (cylinder <= 0) return false; cylinder--; Notify(); return true; }

        /// <summary>Begin the staggered reload — automatic on a dry wheel in
        /// combat, manual (R) in possess. Idempotent: no-op while reloading or
        /// already full.</summary>
        public void StartReload()
        {
            if (reloading || cylinder >= cylinderCapacity) return;
            reloading = true;
            nextRoundLoad = Time.unscaledTime + roundLoadInterval;
            Notify();
        }

        /// <summary>Test-harness loadout — the pit duel START button calls this
        /// so each arena run begins with a full wheel. Mode transitions do NOT
        /// refill: the cylinder is physical state that carries over.</summary>
        public void RefillCylinder() { cylinder = cylinderCapacity; reloading = false; Notify(); }

        private CoverSensor sensor;
        private CoverSensor Sensor =>
            sensor != null ? sensor : (sensor = GetComponent<CoverSensor>() ?? gameObject.AddComponent<CoverSensor>());

        private CharacterRig rig;
        private CharacterAnimation anim;
        private ArmAimSolver solver;
        private VoxelCharacter vox;
        private VoxelCollisionWorld world;
        /// <summary>Duck scalar (0..2) the picked cover demands — written
        /// directly to anim.duck; sub-1 renders the morph band.</summary>
        private float targetDuck;
        private CoverSensor.WallClass coverWallClass = CoverSensor.WallClass.Crouch;
        private WeaponMount mount;
        private int cylinder;
        private bool reloading;
        private float nextVisProbe, nextFireTime, nextRoundLoad;
        // Loop-gate state: `loopGate` arms on every Duck entry — the settle that
        // follows a duck is the re-cycle gate, decided LIVE at its exit (not
        // at duck-entry time, so a mid-seam toggle takes effect). `awaitingLoop`
        // marks a parked gate — a live toggle-ON restarts the rhythm.
        private bool loopGate, awaitingLoop;
        // The REAL bore as detector: the welded muzzle ray — not the solve's
        // sight-line model — is what actually clears or clips the wall once
        // the arm is presented. `aimPointWorld` = the last point the solver
        // was aimed at; `buriedSince` = sustained-buried hysteresis.
        private float buriedSince;
        private Vector3 aimPointWorld;
        [Tooltip("How long the real muzzle ray must stay blocked before the wall is declared unfightable (buried-bore verdict → relocate).")]
        public float boreBurySec = 0.4f;
        /// <summary>Scratch for EnumerateZones — relocation candidates are the
        /// field's painted cover AREAS (connected Hidden components), one
        /// centroid each, not per-face anchors.</summary>
        private readonly List<CoverField.ZoneSpot> zonesScratch = new List<CoverField.ZoneSpot>(32);

        // ---- Scripted engagement director --------------------------------
        /// <summary>A user-authored cover order — one filled dropdown slot:
        /// "go stand at this centroid". The HUD writes slots; combat consumes
        /// them in order and runs the full rhythm at each stop. `stand` is the
        /// zone centroid — a precise feet target, already resolved.</summary>
        public struct ScriptSlot
        {
            public bool has;
            public Vector3 stand;
            public string volId;
            public CoverSensor.WallClass cls;
            public float reqD;
        }
        /// <summary>When on, EnterCombat skips the auto pick — he holds until
        /// the first filled slot issues his first order. Filling the NEXT slot
        /// is the "advance": it fires the moment the current stage reaches a
        /// verdict (first Engaged contact, a refused/buried rise, or a parked
        /// seam) — never mid-move, so actions can't step on each other.</summary>
        public bool scriptedMode;
        public readonly List<ScriptSlot> scriptSlots = new(new ScriptSlot[8]);
        /// <summary>Index of the slot currently being executed (−1 = none
        /// consumed yet — holding for the user's first order).</summary>
        public int ScriptCursor { get; private set; } = -1;
        /// <summary>The consumed slot produced a verdict (contact or failure)
        /// — the queue may advance when the next slot is filled.</summary>
        public bool ScriptStageDone { get; private set; }

        void ConsumeNextScriptSlot()
        {
            for (int i = ScriptCursor + 1; i < scriptSlots.Count; i++)
            {
                var s = scriptSlots[i];
                if (!s.has) continue;
                ScriptCursor = i; ScriptStageDone = false;
                ScriptedGoTo(i, s);
                return;
            }
        }

        /// <summary>Issue a scripted order — identical commitment to a
        /// fighting-cover relocation (atomic: cached stand point, reset beat
        /// state, SeekingCover re-runs the arrival → rhythm pipeline).</summary>
        void ScriptedGoTo(int idx, ScriptSlot s)
        {
            CoverPoint = s.stand; CoverObstacleId = s.volId;
            targetDuck = s.reqD; coverWallClass = s.cls;
            Beat = EngagementBeat.None; afterSettle = EngagementBeat.None;
            loopGate = false; buriedSince = 0f;
            Relocating = idx > 0;   // slot 1 = the initial seek (SEEK); later slots = RELOCATE
            if (anim != null) anim.coverContext = false;
            SetState(CombatState.SeekingCover);
            if (anim != null)
            {
                anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                anim.walkSpeed = Mathf.Clamp(moveSpeed, 0.5f, 2f);
            }
            LastRelocateDiag = $"script {idx + 1} → {s.volId} stand@({s.stand.x:F0},{s.stand.z:F0}) · duck {s.reqD:F2}";
            LastResult = $"scripted → {s.volId}";
            Debug.Log($"🥊 [Combat] {name} — {LastRelocateDiag}");
            Notify();
        }
        /// <summary>Last relocation action, for the metrics panel.</summary>
        public string LastRelocateDiag { get; private set; } = "—";
        /// <summary>True while a relocation-driven SeekingCover is in progress
        /// — the phase is the initial-seek primitive reused (duck-gait move +
        /// armed opportunity channel while reloaded), cleared on arrival.</summary>
        public bool Relocating { get; private set; }

        /// <summary>The welded muzzle is the ground truth: does the REAL bore
        /// path to the aim point clip world voxels? The rise solve's
        /// eye−sightGap model can say "cleared" while the actual barrel
        /// kisses the lip — this catches it at runtime.</summary>
        bool BoreBuried(Vector3 aimPoint)
        {
            if (mount == null || !mount.IsEquipped || sensor == null) return false;
            return sensor.LosBlocked(mount.MuzzleWorld, aimPoint);
        }
        private float beatUntil, riseDuck;
        private int emptyProbes;                     // consecutive zero-visible probes (Engaged drop gate)
        private EngagementBeat afterSettle;

        void Awake()
        {
            rig = GetComponent<CharacterRig>();
            vox = GetComponent<VoxelCharacter>();
            sensor = GetComponent<CoverSensor>();
        }

        /// <summary>Enter combat against a threat at `threatPos`. Re-calling re-evaluates cover.</summary>
        public bool EnterCombat(Vector3 threatPos)
        { ThreatCharacter = null; return EnterCombatCore(threatPos, null, 0f); }

        /// <summary>Combat against a live body — the visibility probe samples
        /// his posed silhouette; `threatPos` still keys the cover field.</summary>
        public bool EnterCombat(Vector3 threatPos, VoxelCharacter threatBody)
        { ThreatCharacter = threatBody; return EnterCombatCore(threatPos, null, 0f); }

        /// <summary>Debug variant — seek the Hidden cell nearest `coverAnchor`
        /// (pit pick buttons). Same field validation; targets a specific box.</summary>
        public bool EnterCombat(Vector3 threatPos, Vector3 coverAnchor, float maxDist = 2.5f)
        { ThreatCharacter = null; return EnterCombatCore(threatPos, coverAnchor, maxDist); }

        /// <summary>Anchor-pick against a live body — pit picks + exposure fire.</summary>
        public bool EnterCombat(Vector3 threatPos, VoxelCharacter threatBody, Vector3 coverAnchor, float maxDist = 2.5f)
        { ThreatCharacter = threatBody; return EnterCombatCore(threatPos, coverAnchor, maxDist); }

        bool EnterCombatCore(Vector3 threatPos, Vector3? coverAnchor, float maxDist)
        {
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (anim == null) anim = GetComponent<CharacterAnimation>();
            // Get-or-Add, not Get-only — solver.Arm() at cover arrival is
            // unguarded, and the weapons tab only lazily attached the solver
            // when drawn (latent NullRef if combat started before that tab
            // ever opened). Same pattern as `Sensor` above.
            if (solver == null) solver = GetComponent<ArmAimSolver>() ?? gameObject.AddComponent<ArmAimSolver>();
            if (rig == null || anim == null || world == null)
            { LastResult = "missing rig/anim/collision world"; return false; }

            // Debug pose panel stays live in combat — the rig only reads pose
            // keys (no locomotion), and PRIORITY_DEBUG already outranks
            // PRIORITY_COMBAT's holds. But a pick means "run the sim fresh" —
            // a stale debug latch would silently refuse every combat request
            // (50 > 30), leaving him crouch-sliding to the wall in whatever
            // pose was left held. Clear all holds on entry; keys pressed
            // AFTER this still win normally.
            anim.ReleasePose(-1);
            ThreatPos = threatPos;

            // Exposure loop state — engagement counter + aim memory reset, but
            // NOT the cylinder: the wheel is physical state that carries over
            // from whatever fired last (possess or a previous engagement).
            ShotsFired = 0;
            Beat = EngagementBeat.None; afterSettle = EngagementBeat.None; riseDuck = 0f;
            FaceFree();   // fresh combat = fresh facing intent
            contactSeen = false;
            loopGate = false; awaitingLoop = false; failedFaces.Clear();
            nextFireTime = 0f; nextVisProbe = 0f; LastShotAt = -1f;
            ExposureArmed = false;
            LastSeenTarget = null; LastSeenAt = -1f;
            if (mount == null) mount = GetComponent<WeaponMount>();

            Sensor.Threat = threatPos;
            Sensor.Refresh(true);
            // Scripted director: no auto pick — he holds at spawn until the
            // first filled slot issues the order. The user literally writes
            // the sequence live (first cover → relocate → re-engage).
            if (scriptedMode)
            {
                ScriptCursor = -1; ScriptStageDone = false;
                SetState(CombatState.HoldPosition);
                CoverPoint = Feet(); CoverObstacleId = null;
                coverWallClass = CoverSensor.WallClass.Crouch; targetDuck = 0f;
                LastResult = "scripted — awaiting slot 1";
                Debug.Log($"🥊 [Combat] {name} — scripted mode: holding for slot 1");
                Notify();
                return true;
            }
            bool picked = coverAnchor.HasValue
                ? Sensor.TryPickCoverNear(coverAnchor.Value, maxDist, out Vector3 p, out string oid, out float reqD, out coverWallClass, coverIntNorm)
                : Sensor.TryPickCover(Feet(), out p, out oid, out reqD, out coverWallClass, coverIntNorm);
            if (picked)
            {
                CoverPoint = p;
                CoverObstacleId = oid;
                targetDuck = reqD;
                SetState(CombatState.SeekingCover);
                anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                anim.walkSpeed = Mathf.Clamp(moveSpeed, 0.5f, 2f);
                LastResult = $"seeking cover behind {oid} ({coverWallClass}, duck {reqD:F2})";
                Debug.Log($"🥊 [Combat] {name} — picked {oid} ({coverWallClass}, reqDuck {reqD:F2})");
                return true;
            }
            if (coverAnchor.HasValue)
            { LastResult = $"no valid cover near that anchor"; Notify(); return false; }

            // No cover — hold and crouch in place. Still a state change the sim can react to.
            SetState(CombatState.HoldPosition);
            CoverPoint = Feet();
            CoverObstacleId = null;
            coverWallClass = CoverSensor.WallClass.Crouch;
            targetDuck = 1f;   // shallow hold — the old "depth 0" on state 5
            CrouchAndCover();
            // Exposed last stand faces the threat — no wall to turn on.
            // Track mode: keeps easing toward the anchor while he holds.
            Vector3 ht = AimAnchor() - Feet(); ht.y = 0f;
            if (ht.sqrMagnitude > 1e-6f)
                FaceTrack(AimAnchor(), "hold-position");
            LastResult = $"no cover within {Sensor.radius:F0}m — holding position";
            return true;
        }

        public void ExitCombat()
        {
            targetDuck = 0f;
            if (anim != null) { anim.duck = 0f; anim.coverContext = false; }
            anim?.ReleasePose(CharacterAnimation.PRIORITY_COMBAT);
            anim?.SetState(CharacterAnimation.AnimState.Idle);
            if (solver != null && solver.IsAiming) solver.Disarm();
            ExposureArmed = false;
            Beat = EngagementBeat.None; afterSettle = EngagementBeat.None;
            loopGate = false; awaitingLoop = false; Relocating = false; buriedSince = 0f;
            contactSeen = false;
            FaceFree();   // combat releases the facing channel on exit
            ScriptCursor = -1; ScriptStageDone = false;   // slots persist — the script survives iterations
            // (reloading left running — the wheel seats across mode exits,
            // same carryover rule as the cylinder itself.)
            ThreatCharacter = null;
            LastSeenTarget = null;   // idle must not report a stale memory point
            SetState(CombatState.Idle);
            CoverObstacleId = null;
            LastResult = "";
            // Threat released — the sensor keeps its passive wall awareness
            // drawn (the COVER tab owns those toggles); the exposure cache
            // goes too, or the flow panel keeps a ghost PROBE lit.
            if (sensor != null) { sensor.Threat = null; sensor.ClearVisibility(); sensor.Refresh(true); }
            Notify();
        }

        /// <summary>Per-beat fire authority — the arm is beat-owned now:
        /// SeekingCover (opportunity channel) and HoldPosition (no parapet =
        /// always-engaged lane) run exposure-gated; in cover only Rise and
        /// Engaged may hold the gun out. A stray positive probe during Settle
        /// must never pop the arm up.</summary>
        bool BeatAllowsFire => !holdInCover && (State == CombatState.SeekingCover
            || State == CombatState.HoldPosition
            || Beat == EngagementBeat.Rise || Beat == EngagementBeat.Engaged);

        /// <summary>Beat transition — every hop emits Changed for the flow lane.</summary>
        /// <summary>Dwell floor for a beat — the tempo table, length-guarded
        /// (a truncated Inspector array falls back to the 5s debug default).</summary>
        float DwellFor(EngagementBeat b) =>
            beatDwell != null && beatDwell.Length > (int)b ? beatDwell[(int)b] : 5f;

        void EnterBeat(EngagementBeat b)
        {
            Beat = b;
            FaceFree();             // fresh intent per beat — no stale facing leaks in
            awaitingLoop = false;   // any real beat clears a parked gate
            // Universal dwell floor — EVERY beat holds beatDwell[b] seconds
            // minimum (the debug tempo; HUD TEMPO table edits the table live).
            beatUntil = Time.unscaledTime + DwellFor(b);
            switch (b)
            {
                case EngagementBeat.Settle:
                    // Settle = the breath between actions — it OWNS NO
                    // MOVEMENT: no roll (that lives at arrivals — see
                    // CrouchAndCover), no fresh demand; it just holds the
                    // committed cover-facing the arriving beat left him in.
                    FaceHold(settleYawDeg, "settle");
                    break;
                case EngagementBeat.None:
                case EngagementBeat.TopUp:
                    FaceHold(settleYawDeg, b == EngagementBeat.None ? "none" : "topup");
                    break;
                case EngagementBeat.Duck:
                    returnWalked = false;   // the off-spot return leg starts cold
                    returnTurning = false;
                    loopGate = true;   // the settle after a duck is the re-cycle gate
                    ScriptStageDone = true;   // stage verdict reached (dry/refused/empty/buried/lost)
                    // Fold-back endpoints — turn-to-settle-facing rides the
                    // fold's progress, same coupling as the rise.
                    foldFromDuck = anim != null ? anim.duck : 0f;
                    foldFromYawDeg = transform.eulerAngles.y;
                    FaceCurve(foldFromYawDeg, settleYawDeg, FoldT, "duck");
                    break;
                case EngagementBeat.Engaged:
                    ScriptStageDone = true;   // contact confirmed — the stop produced fire
                    emptyProbes = 0;
                    // The Swing→Engaged path skips Rise — engage facing must
                    // exist for the intent lane regardless of the entry beat.
                    {
                        Vector3 et = AimAnchor() - Feet(); et.y = 0f;
                        if (et.sqrMagnitude > 1e-4f) engageYawDeg = BearingDeg(et);
                    }
                    break;
                case EngagementBeat.Rise:
                    riseDuck = SolveRiseDuck();
                    // No clearable rise → duck, settle, gate. The seam decides
                    // park-vs-restart LIVE at its exit — afterSettle stays
                    // TopUp so a mid-seam toggle-ON still proceeds.
                    if (riseDuck < 0f)
                    {
                        Debug.Log($"🥊 [Combat] {name} — rise refused: {LastRiseDiag}");
                        // The wall can't be fought OVER — try looking AROUND
                        // it first (lateral peek). Scripted mode skips it:
                        // the script owns position.
                        if (!scriptedMode) { EnterBeat(EngagementBeat.Swing); return; }
                        if (TryRelocateToFightingCover()) return;
                        afterSettle = EngagementBeat.TopUp;
                        EnterBeat(EngagementBeat.Duck); return;
                    }
                    // Atomic beat: the pose converging to riseDuck IS the rise
                    // completing — the contact verdict lands there. No window.
                    // Capture the endpoints — the turn rides the unfold's
                    // normalized progress (turn-and-stand as ONE motion).
                    riseFromDuck = anim != null ? anim.duck : 0f;
                    riseFromYawDeg = transform.eulerAngles.y;
                    FaceCurve(riseFromYawDeg, engageYawDeg, RiseT, "rise");
                    break;
                case EngagementBeat.Swing:
                    if (!TryStartSwing())
                    {
                        // No zone edge to step from — the old refusal path.
                        if (TryRelocateToFightingCover()) return;
                        afterSettle = EngagementBeat.TopUp;
                        EnterBeat(EngagementBeat.Duck); return;
                    }
                    break;
            }
            Notify();
        }

        /// <summary>The point the body treats as "the threat" for facing and
        /// solves — lastSeen (belief) first, the live threat as stand-in.</summary>
        Vector3 AimAnchor()
        {
            // Belief only — the freshest sighting, else the entry report.
            // Aiming at live truth would be omniscient; truth belongs to
            // the physical verdicts (probe / cone), never to intent.
            if (LastSeenTarget.HasValue) return LastSeenTarget.Value;
            return ThreatPos;
        }
        static float BearingDeg(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        /// <summary>Can he fight over his own wall's top — at FULL STAND.
        /// Firing states are posture-uniform now: rise = stand up, no duck
        /// band to search — the verdict is "does the standing eye + presented
        /// muzzle clear the lip": eye alone isn't enough if the gun's still
        /// buried. -1 when unsolvable (over-tall parapet / no wall data).</summary>
        float SolveRiseDuck()
        {
            if (vox == null || sensor == null)
            { LastRiseDiag = "no vox/sensor"; return -1f; }
            // The picked wall's covered-band top — the nearest face mark on
            // his own cover obstacle to where he stands.
            float wallTop = float.MinValue, best = float.MaxValue;
            foreach (var w in sensor.walls)
            {
                if (w.volId != CoverObstacleId) continue;
                float d2 = (w.facePos - CoverPoint).sqrMagnitude;
                if (d2 < best) { best = d2; wallTop = w.top; }
            }
            if (wallTop == float.MinValue || best > 4f)
            {
                LastRiseDiag = $"no wall mark on {CoverObstacleId} within 2m of stand";
                return -1f;
            }

            // The bore line needs a target — the rise re-queries lastSeen, the
            // live threat as a stand-in. None = eye-only solve (a look, not a shot).
            Vector3? tgt = LastSeenTarget;
            if (!tgt.HasValue && ThreatCharacter != null)
                tgt = ThreatCharacter.transform.position + Vector3.up * 0.5f;

            // Solve at the ENGAGE yaw — where he'll end after the turn-to-
            // engage — not whatever the settle posture left him facing.
            engageYawDeg = BearingDeg(AimAnchor() - transform.position);
            Vector3 engageFwd = Quaternion.Euler(0f, engageYawDeg, 0f) * Vector3.forward;
            float r = SolveRiseAtSpot(transform.position, engageYawDeg,
                wallTop, tgt, engageFwd,
                out int verdict, out float bestEye);
            if (r >= 0f)
            {
                LastRiseDiag = verdict == 4
                    ? $"cleared(eye-only): stand · wall {wallTop:F2}"
                    : $"cleared: stand · wall {wallTop:F2}";
                return r;
            }
            LastRiseDiag = verdict == 1 ? "eye eval failed"
                : verdict == 3 ? $"sees but can't shoot: wall {wallTop:F2} — bore path blocked at stand"
                : $"can't see over: wall {wallTop:F2}+{riseMargin:F2} vs eye≤{bestEye:F2}";
            return -1f;
        }

        /// <summary>The rise verdict at an arbitrary stand spot — single
        /// evaluation, FULL STAND (firing posture is upright; the crouch-band
        /// candidate search is suspended). The eye anchor is evaluated at the
        /// live transform, then re-anchored: the local offset (yaw-unrotated)
        /// is carried to `stand` under `yawDeg`.
        /// verdict: 0 cleared · 1 eye-eval-fail · 2 eye can't clear ·
        /// 3 bore blocked · 4 cleared eye-only. Returns 0 (the stand duck) or −1.</summary>
        float SolveRiseAtSpot(Vector3 stand, float yawDeg, float wallTop,
            Vector3? tgt, Vector3 fwdHint, out int verdict, out float bestEye)
        {
            verdict = 1;
            Quaternion invYaw = Quaternion.Euler(0f, -transform.eulerAngles.y, 0f);
            Quaternion toYaw = Quaternion.Euler(0f, yawDeg, 0f);
            if (!vox.TryGetEyeAnchor(out Vector3 eyeW, out _, out _, 0f, 0f, 0f))
            { bestEye = float.MinValue; return -1f; }
            Vector3 eye = stand + toYaw * (invYaw * (eyeW - transform.position));
            bestEye = eye.y;
            if (eye.y <= wallTop + riseMargin) { verdict = 2; return -1f; }

            // Eye clears. The muzzle gate is the BORE LINE, anchored to the
            // sight line, not the wrist pivot: aimed fire aligns the sights
            // to the eye, so the presented muzzle sits sightGap below the eye
            // pushed muzzleFwd toward the target — the ray rides the same
            // world collision the probe uses.
            if (!tgt.HasValue) { verdict = 4; return 0f; }
            Vector3 hd = tgt.Value - eye; hd.y = 0f;
            Vector3 hdn = hd.sqrMagnitude > 1e-6f ? hd.normalized : fwdHint;
            Vector3 muzzle = eye - Vector3.up * sightGap + hdn * muzzleFwd;
            // The gun rides the lip at minimum — a marginal eye would dip the
            // pushed muzzle inside the wall footprint and fail a shot that
            // presenting over the parapet actually makes.
            if (muzzle.y < wallTop + riseMargin) muzzle.y = wallTop + riseMargin;
            if (!sensor.LosBlocked(muzzle, tgt.Value)) { verdict = 0; return 0f; }
            verdict = 3;
            return -1f;
        }

        /// <summary>Set up the lateral peek: side = committed `PeekSide`
        /// (back-facing rolls one at peek time); step-out destination = the
        /// zone's painted rim on that side + `swingMargin` past it, so the
        /// step clears the corner regardless of cover width. False when no
        /// zone geometry exists to step from.</summary>
        bool TryStartSwing()
        {
            sbyte side = PeekSide != 0 ? PeekSide
                : (sbyte)(UnityEngine.Random.value < 0.5f ? -1 : 1);
            Vector3 toT = AimAnchor() - Feet(); toT.y = 0f;
            if (toT.sqrMagnitude < 1e-4f) return false;
            Vector3 threatDir = toT.normalized;
            Vector3 lat = Vector3.Cross(Vector3.up, threatDir);   // right of threat-facing
            Vector3 stepDir = lat * side;
            int ci = sensor != null ? sensor.Field.CellIndexOf(CoverPoint) : -1;
            if (ci < 0 || !sensor.Field.TryZoneEdge(ci, stepDir, out Vector3 rim, out _)) return false;
            swingDest = rim + stepDir * swingMargin;
            // The step faces its destination — declared per-tick in
            // StepToward as FaceTrack(dest) (the driver computes the
            // bearing — off-axis edges can't produce a stale heading).
            swingPhase = 0; swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0;
            PeekSide = side;   // the rolled side is the committed side now
            if (vis == null) vis = GetComponent<VisionSensor>();
            Debug.Log($"🥊 [Combat] {name} — swing-peek: side {(side < 0 ? "left" : "right")} step→({swingDest.x:F1},{swingDest.z:F1})");
            return true;
        }

        /// <summary>Walk one step toward `dest` at combat gait, yawing to
        /// `yawDeg` — the swing's own mini-steering (SeekingCover stays the
        /// relocation channel). True at arrival.</summary>
        bool StepToward(Vector3 dest, string who)
        {
            Vector3 feet = Feet();
            Vector3 to = dest - feet; to.y = 0f;
            float dist = to.magnitude;
            if (dist <= arriveEps)
            {
                // Arrival means ON the point: inside eps is still off it, so
                // take the remaining sub-step as final placement (collision-
                // checked — a refused last inch leaves him at eps, still
                // legally arrived). INT chooses WHICH point; this is footwork.
                if (dist > 0.005f) MoveWithCollision(to);
                return true;
            }
            float step = moveSpeed * (anim != null ? anim.DuckSpeedFactor : 1f) * Time.deltaTime;
            MoveWithCollision((to / dist) * Mathf.Min(step, dist));
            // Movement-facing = Track-the-destination — declared, not written.
            if (dbgMoveYaw) FaceTrack(dest, who); else FaceFree();
            // Stuck guard — ~0.6s of no progress. Stuck is NOT arrival:
            // first the leg spends sidestep nudges (±perpendicular hops —
            // the greedy axis slide can't wrap a convex corner lip), and
            // only an exhausted nudge budget reports "leg done" — CALLERS
            // re-verify distance to distinguish arrived from aborted.
            if ((Feet() - swingStuckPos).sqrMagnitude > 0.02f)
            { swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0; }
            else if (Time.unscaledTime - swingStuckT > 0.6f)
            {
                if (stuckNudges < 3)
                {
                    stuckNudges++;
                    stuckNudgeSide = stuckNudges == 1 ? 1 : -stuckNudgeSide;
                    Vector3 perp = new Vector3(-to.z, 0f, to.x).normalized * (0.28f * stuckNudgeSide);
                    MoveWithCollision(perp);
                    if (dbgMoveYaw) FaceTrack(feet + perp.normalized, who);
                    swingStuckPos = Feet(); swingStuckT = Time.unscaledTime;
                    return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>Faces this combat has already failed a rise on — keyed by
        /// FACE position, not volId: one volume carries faces at different
        /// heights (the lower parapet section beside the tall one).</summary>
        private readonly System.Collections.Generic.HashSet<string> failedFaces
            = new System.Collections.Generic.HashSet<string>();
        static string FaceKey(CoverSensor.WallMark w)
            => $"{w.volId}@{Mathf.RoundToInt(w.facePos.x * 4f)},{Mathf.RoundToInt(w.facePos.z * 4f)}";

        /// <summary>Rise refused = this wall can't be fought over — re-pick for
        /// FIGHTING cover: the nearest hidden-validated face whose OWN rise
        /// solve clears (the spec's reposition branch). The failed face is
        /// remembered so the re-pick never returns it. True when a relocation
        /// was issued — SeekingCover re-runs the whole rhythm at the new spot.</summary>
        bool TryRelocateToFightingCover()
        {
            if (sensor == null || sensor.walls == null) return false;
            // Scripted mode: the user's slots own position — auto-relocation
            // would fight the script. The verdict still counts the stage done
            // so a filled next slot takes over; unfilled = the seam gate holds.
            if (scriptedMode)
            {
                ScriptStageDone = true;
                LastRelocateDiag = "auto-relocate suppressed — script owns position";
                return false;
            }
            // Mark the face that just failed — nearest mark on his obstacle.
            float bd = float.MaxValue;
            foreach (var w in sensor.walls)
            {
                if (w.volId != CoverObstacleId) continue;
                float d2 = (w.facePos - CoverPoint).sqrMagnitude;
                if (d2 < bd) { bd = d2; failedFaces.Add(FaceKey(w)); }
            }

            Vector3? tgt = LastSeenTarget;
            if (!tgt.HasValue && ThreatCharacter != null)
                tgt = ThreatCharacter.transform.position + Vector3.up * 0.5f;

            // Candidates = the field's painted cover AREAS — connected Hidden
            // components (per volume + class + ¼-duck band), one centroid
            // each: "the middle of each color gradient section", not one spot
            // per wall-face mark. Each zone still borrows its owner volume's
            // nearest face mark for the wall top the solve needs.
            CoverField.ZoneSpot best = default;
            float bestReq = 0f, bestD2 = float.MaxValue; bool found = false;
            sensor.Field.EnumerateZones(zonesScratch);
            foreach (var z in zonesScratch)
            {
                float md = float.MaxValue; CoverSensor.WallMark m = default; bool hasMark = false;
                foreach (var w in sensor.walls)
                {
                    if (w.volId != z.volId) continue;
                    // The zone's own face — same-volume marks on other faces
                    // would solve against the wrong wall's top/outDir.
                    if (w.outDir.x * z.outDir.x + w.outDir.z * z.outDir.y < 0.999f) continue;
                    float d2m = (w.facePos - z.stand).sqrMagnitude;
                    if (d2m < md) { md = d2m; m = w; hasMark = true; }
                }
                if (!hasMark || md > 4f || failedFaces.Contains(FaceKey(m))) continue;
                Vector3 spot = z.stand;
                // Re-validate at the centroid — a concave zone's middle can
                // sit in a different cell than its rim.
                if (!sensor.TryZoneAt(spot, out _, out float rd)) continue;
                float yaw = tgt.HasValue
                    ? Mathf.Atan2(tgt.Value.x - spot.x, tgt.Value.z - spot.z) * Mathf.Rad2Deg
                    : Mathf.Atan2(-m.outDir.x, -m.outDir.z) * Mathf.Rad2Deg;
                // Solve at the stand point itself — the verdict applies to
                // where he'll actually stand, not the column anchor.
                if (SolveRiseAtSpot(spot, yaw, m.top, tgt,
                        -m.outDir, out _, out _) < 0f) continue;
                float d2 = (spot - transform.position).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = z; bestReq = rd; found = true; }
            }
            if (!found)
            {
                LastRelocateDiag = $"refused — {failedFaces.Count} face(s) failed, none fightable";
                LastRiseDiag += " — no fightable cover";
                return false;
            }

            CoverPoint = best.stand; CoverObstacleId = best.volId;
            targetDuck = bestReq; coverWallClass = best.cls;
            Beat = EngagementBeat.None; afterSettle = EngagementBeat.None;
            loopGate = false; buriedSince = 0f; Relocating = true;
            if (anim != null) anim.coverContext = false;   // fresh-seek parity with EnterCombat
            SetState(CombatState.SeekingCover);
            anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
            anim.walkSpeed = Mathf.Clamp(moveSpeed, 0.5f, 2f);
            LastRelocateDiag = $"→ {best.volId} stand@({best.stand.x:F0},{best.stand.z:F0}) · duck {bestReq:F2}";
            LastResult = $"relocating to fightable cover {best.volId} ({best.cls}, duck {bestReq:F2})";
            Debug.Log($"🥊 [Combat] {name} — rise refused → {LastResult}");
            Notify();
            return true;
        }

        /// <summary>The authored rhythm (ENGAGEMENT_BEATS.md) — runs inside
        /// InCover only: arrival Settle → TopUp → Settle → Rise → Engaged →
        /// Duck → seam loop. Settle is the reusable seam; `afterSettle` is the
        /// future branch point (reload | re-check | reposition).</summary>
        void TickBeat()
        {
            switch (Beat)
            {
                case EngagementBeat.Settle:
                    if (Time.unscaledTime >= beatUntil)
                    {
                        // Hide-only: the seam IS the destination — he sought
                        // cover and now holds it. Parks forever, arm stowed.
                        if (holdInCover) { EnterBeat(EngagementBeat.Settle); break; }
                        var next = afterSettle;
                        bool parking = false;
                        if (loopGate)
                        {
                            loopGate = false;
                            // The re-cycle gate, decided at exit — a live
                            // toggle-OFF parks here (awaitingLoop lets a
                            // toggle-ON restart), ON proceeds to TopUp.
                            if (!loopBeats) { next = EngagementBeat.None; parking = true; }
                        }
                        EnterBeat(next);
                        // AFTER EnterBeat — it clears the flag on every entry.
                        if (parking) awaitingLoop = true;
                    }
                    break;
                case EngagementBeat.None:
                    // Parked at the re-cycle gate — a live toggle-ON re-arms
                    // the rhythm from the seam, same as a fresh cycle.
                    if (awaitingLoop && loopBeats)
                    { afterSettle = EngagementBeat.TopUp; EnterBeat(EngagementBeat.Settle); }
                    break;
                case EngagementBeat.TopUp:
                    if (cylinder < cylinderCapacity && !reloading) StartReload();
                    // Reload starts instantly (physical); the beat LEAVES on
                    // the tempo floor + a full cylinder.
                    if (!reloading && Time.unscaledTime >= beatUntil)
                    { afterSettle = EngagementBeat.Rise; EnterBeat(EngagementBeat.Settle); }
                    break;
                case EngagementBeat.Rise:
                    // Facing = the EnterBeat FaceCurve — the driver's Curve
                    // mode renders turn+unfold as ONE authored motion.
                    // Atomic — no recon window: pose converged to full stand
                    // IS the rise completing (the eye physically IS up). The
                    // contact verdict is taken at that instant on a FRESH
                    // probe, not the cadenced cache.
                    if (Time.unscaledTime >= beatUntil
                        && (anim == null || Mathf.Abs(anim.duck - riseDuck) < 0.02f))
                    {
                        bool contact = ThreatCharacter != null && Sensor != null
                            && Sensor.TryGetVisibleVoxels(ThreatCharacter, out _);
                        if (contact)
                        {
                            LastRiseDiag = "contact at stand → Engaged";
                            EnterBeat(EngagementBeat.Engaged); break;
                        }
                        LastRiseDiag = "risen — zero contact (target occluded from this stand)";
                        Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                        afterSettle = EngagementBeat.TopUp;
                        EnterBeat(EngagementBeat.Duck);
                    }
                    break;
                case EngagementBeat.Engaged:
                    // The facing declaration — live body while contact holds
                    // (truth is smooth), frozen belief while blind. The
                    // kill-switch demotes the track to a static Hold.
                    if (dbgEngagedTrack)
                        FaceTrack(contactSeen && ThreatCharacter != null
                            ? ThreatFeet() : AimAnchor(), "engaged");
                    else
                        FaceHold(engageYawDeg, "engaged");
                    // Buried-bore verdict: the solve cleared him but the REAL
                    // welded muzzle ray hits the wall — sustained burial =
                    // unfightable cover → relocate (same path as refusal).
                    if (solver.IsAiming && solver.Blend >= 0.7f && BoreBuried(aimPointWorld))
                        buriedSince += Time.unscaledDeltaTime;
                    else buriedSince = 0f;
                    // Tempo floor — he holds the stand for the dwell even
                    // when the verdict is already in (buried keeps counting).
                    if (Time.unscaledTime < beatUntil) break;
                    if (cylinder <= 0 || reloading)
                    { afterSettle = EngagementBeat.TopUp; EnterBeat(EngagementBeat.Duck); }
                    else if (buriedSince >= boreBurySec)
                    {
                        buriedSince = 0f;
                        LastRiseDiag = "bore buried at the wall — real muzzle ray blocked";
                        Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                        if (!TryRelocateToFightingCover())
                        { afterSettle = EngagementBeat.TopUp; EnterBeat(EngagementBeat.Duck); }
                    }
                    else if (emptyProbes >= emptyProbesToDrop)
                    {
                        LastRiseDiag = $"lost him — {emptyProbesToDrop} empty probes in a row";
                        afterSettle = EngagementBeat.TopUp;
                        EnterBeat(EngagementBeat.Duck);
                    }
                    break;
                case EngagementBeat.Duck:
                {
                    // Facing = the EnterBeat FaceCurve (fold-back coupled to
                    // the collapse progress — one authored motion).
                    // Arrival posture turn FIRST — return-owned, converges
                    // before the seam (and kills the stale Track-on-self
                    // bearing singularity while he's standing on the point).
                    if (returnTurning)
                    {
                        FaceHold(settleYawDeg, "return");
                        bool folded2 = anim == null || Mathf.Abs(anim.duck - targetDuck) < 0.02f;
                        if (folded2 && Time.unscaledTime >= beatUntil
                            && Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, settleYawDeg)) <= 1f)
                        { returnTurning = false; EnterBeat(EngagementBeat.Settle); }
                        break;
                    }
                    bool folded = anim == null || Mathf.Abs(anim.duck - targetDuck) < 0.02f;
                    if (!folded || Time.unscaledTime < beatUntil) break;
                    // Off-spot when the fold lands (rim-peek engaged from the
                    // edge / stuck-guard arrival) — duck-walk back to the
                    // cover centroid BEFORE the seam holds: settling here
                    // would park him in the open.
                    Vector3 home = CoverPoint - Feet(); home.y = 0f;
                    if (home.magnitude > arriveEps)
                    {
                        if (!returnWalked)
                        {
                            returnWalked = true;
                            swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0;
                        }
                        if (anim != null && anim.EffectiveState != CharacterAnimation.AnimState.Walking)
                            anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                        if (StepToward(CoverPoint, "return"))
                        {
                            Vector3 h = CoverPoint - Feet(); h.y = 0f;
                            if (h.magnitude <= arriveEps)
                            {
                                var p = transform.position;
                                p.y += CoverPoint.y - Feet().y;
                                transform.position = p;
                            }
                            else
                            {
                                // Aborted off-target — the nudge budget is
                                // spent; he stands where the leg ended and
                                // the readout says so (no fake arrival).
                                LastResult = $"return stalled {h.magnitude:F2}m off centroid";
                                Debug.Log($"⚠️ [Combat] {name} — return leg aborted {h.magnitude:F2}m off the centroid");
                            }
                            CrouchAndCover();      // rolls facing at HIS landing spot
                            returnTurning = true;  // phase C — turn under the return
                        }
                        break;
                    }
                    EnterBeat(EngagementBeat.Settle);
                    break;
                }
                case EngagementBeat.Swing:
                    if (swingPhase == 0)
                    {
                        // Step out — the fold rides the gait (walking base,
                        // duck scalar carries the crouch), yaw = the way
                        // he's actually moving.
                        if (anim != null && anim.EffectiveState != CharacterAnimation.AnimState.Walking)
                            anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                        if (StepToward(swingDest, "swing-p0"))
                        {
                            var p = transform.position;
                            p.y += swingDest.y - Feet().y;
                            transform.position = p;
                            // The authored head-yaw sweep — physically sweeps
                            // the posed vision cone across the threat region.
                            anim?.RequestPose(CharacterAnimation.AnimState.Looking, CharacterAnimation.PRIORITY_COMBAT);
                            swingDwellUntil = Time.unscaledTime + DwellFor(EngagementBeat.Swing);
                            swingPhase = 1;
                        }
                    }
                    else if (swingPhase == 1)
                    {
                        // Dwell at the corner — the driver tracks the aim
                        // anchor (belief) while the sweep rides; he is
                        // physically EXPOSED out here (probes can hit him).
                        FaceTrack(AimAnchor(), "swing-dwell");
                        // Honest verdict — the cone sees the threat wherever
                        // he actually is (point fed live each frame); the
                        // aimed silhouette probe is the second channel.
                        bool contact = false;
                        if (ThreatCharacter != null)
                        {
                            if (dbgVisionFeed && vis != null) { vis.SetTarget(ThreatFeet()); if (vis.AnyVisible) contact = true; }
                            if (!contact && sensor != null && sensor.TryGetVisibleVoxels(ThreatCharacter, out _)) contact = true;
                        }
                        if (contact)
                        {
                            // Reacquired where he IS — refresh belief:
                            // zones/aim converge on the new position.
                            LastSeenTarget = ThreatFeet();
                            LastSeenAt = Time.unscaledTime;
                            ThreatPos = LastSeenTarget.Value;
                            LastRiseDiag = "peek — contact at the corner";
                            Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag} → Engaged");
                            EnterBeat(EngagementBeat.Engaged);
                        }
                        else if (Time.unscaledTime >= swingDwellUntil)
                        {
                            LastRiseDiag = "peek — zero contact, stepping back";
                            Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                            swingPhase = 2;
                            // The dwell left the step guard's no-progress clock
                            // stale — reset it or the first return tick reads
                            // "stuck 0.6s+" and declares arrival at the rim.
                            swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0;
                        }
                    }
                    else
                    {
                        // Step back into cover — the seam re-orients the
                        // settle facing from here.
                        if (anim != null && anim.EffectiveState != CharacterAnimation.AnimState.Walking)
                            anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                        if (StepToward(CoverPoint, "swing-p2"))
                        {
                            Vector3 h = CoverPoint - Feet(); h.y = 0f;
                            if (h.magnitude <= arriveEps)
                            {
                                var p = transform.position;
                                p.y += CoverPoint.y - Feet().y;
                                transform.position = p;
                                CrouchAndCover();   // real arrival — rolls
                            }
                            // else: aborted off-spot — Duck's return leg
                            // owns the walk home; ITS arrival rolls.
                            afterSettle = EngagementBeat.TopUp;
                            EnterBeat(EngagementBeat.Duck);
                        }
                    }
                    break;
            }
        }

        void Update()
        {
            // The wheel is world-physical — keep seating rounds even at Idle so
            // a reload started in one mode finishes in whatever mode he's in.
            // Except inside the rhythm: the seam is pure — a carryover reload
            // holds mid-motion until TopUp (defer the deadline so the readouts
            // stay honest). Manual possess reloads aren't rhythm-owned.
            bool beatHoldsReload = !PossessController.Active
                && State == CombatState.InCover && Beat != EngagementBeat.TopUp;
            if (reloading && beatHoldsReload)
                nextRoundLoad += Time.unscaledDeltaTime;
            else if (reloading && Time.unscaledTime >= nextRoundLoad)
            {
                cylinder++;
                nextRoundLoad = Time.unscaledTime + roundLoadInterval;
                if (cylinder >= cylinderCapacity) reloading = false;
                Notify();   // the flow chart's cylinder dots tick on this event
            }
            if (State == CombatState.Idle) return;
            // Direct control wins — possessing Vinny hands the rig to the player.
            // Keep the current duck: possess's cover drive owns the scalar from
            // here and re-derives it from the wall — zeroing it would pop him up.
            if (PossessController.Active)
            {
                float keep = anim != null ? anim.duck : 0f;
                ExitCombat();
                if (anim != null) anim.duck = keep;
                return;
            }
            // Belief feed — ThreatPos is the BELIEVED threat position:
            // written at combat entry and on positive sightings (the probe
            // write below), NOT live feet. Cover zones re-paint against
            // where he was last SEEN. Truth (ThreatFeet) is reserved for
            // physical verdicts — the silhouette probe and the vision cone.
            if (ThreatCharacter != null)
                Sensor.Threat = ThreatPos;
            // Scripted director — consume the next filled slot once the current
            // stage has produced a verdict (first contact / refused or buried
            // rise / parked seam). Never mid-move: while SeekingCover the stage
            // is by definition unfinished, so fills queue until arrival.
            if (scriptedMode && (ScriptCursor < 0 || ScriptStageDone))
                ConsumeNextScriptSlot();
            // Exposure fire — probe the threat's posed silhouette, arm the IK
            // while any voxel resolves, fire on cadence/convergence/cylinder.
            // Runs before the pose pick: ExposureArmed selects the Aim lanes.
            TickExposureFire();
            if (State == CombatState.InCover) TickBeat();

            // Combat owns the approach posture: while seeking he folds into
            // the DESTINATION wall's duck requirement — the painted zone only
            // marks the stand spot now and never reaches out (see CoverField:
            // uniform standoff band). The fold eases in once at combat start
            // rather than snapping per zone edge; InCover/HoldPosition snap
            // to the requirement on arrival. Yields while a debug pose holds
            // priority.
            if (anim != null && !anim.DebugOwnsPose)
            {
                if (State == CombatState.SeekingCover)
                {
                    anim.duck = Mathf.MoveTowards(anim.duck, targetDuck, seekDuckEase * Time.deltaTime);
                    // Seeking IS movement — the aim lane upgrades the march to
                    // AimWalk while voxels resolve; the duck scalar rides the
                    // gait via wd() (crumple deltas) either way.
                    var wantPose = ExposureArmed ? CharacterAnimation.AnimState.AimWalk
                                                 : CharacterAnimation.AnimState.Walking;
                    if (anim.EffectiveState != wantPose)
                        anim.RequestPose(wantPose, CharacterAnimation.PRIORITY_COMBAT);
                }
                else
                {
                    // Stationary lanes — InCover hands the duck scalar to the
                    // beat (riseDuck while risen, targetDuck in seams, eased);
                    // HoldPosition has no beats — snap to the requirement.
                    if (State == CombatState.InCover)
                    {
                        // Swing dwell unfolds to `swingDuck` — the corner
                        // lean, not the cover fold; step phases keep it.
                        float wantD = (Beat == EngagementBeat.Rise || Beat == EngagementBeat.Engaged)
                            ? riseDuck
                            : Beat == EngagementBeat.Swing && swingPhase == 1
                                ? swingDuck : targetDuck;
                        anim.duck = Mathf.MoveTowards(anim.duck, wantD, riseDuckEase * Time.deltaTime);
                    }
                    else if (!Mathf.Approximately(anim.duck, targetDuck))
                        anim.duck = targetDuck;    // arrival/hold — committed posture, immediate
                    // Stationary lanes follow the exposure gate: armed = the
                    // Aiming hold (the gun-forward pose the IK needs); stowed =
                    // the cover hold (deep band) or Idle (morph/Full). Picked
                    // on the LIVE duck, not targetDuck — state 5 can only
                    // express duck>1.02, so a rise below that boundary must
                    // flip to Idle's icr channel or the eye never clears.
                    // Swing is exempt: the beat owns its own poses (Walking
                    // step, Looking+armed dwell) — the lane would stomp them.
                    if (Beat != EngagementBeat.Swing)
                    {
                        var wantPose = ExposureArmed ? CharacterAnimation.AnimState.Aiming
                            : (anim.duck > 1.02f ? CharacterAnimation.AnimState.Crouching
                                                 : CharacterAnimation.AnimState.Idle);
                        if (anim.EffectiveState != wantPose)
                            anim.RequestPose(wantPose, CharacterAnimation.PRIORITY_COMBAT);
                    }
                }
            }
            if (State == CombatState.SeekingCover)
            {
                Vector3 feet = Feet();
                Vector3 to = CoverPoint - feet;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist <= arriveEps)
                {
                    // Position arrived — but ARRIVAL = position ∧ facing:
                    // the turn to the rolled settle-facing is the seek's
                    // last move, owned HERE ("arrive"), not by the seam.
                    var pos = transform.position;
                    pos.y += CoverPoint.y - feet.y;
                    transform.position = pos;
                    if (!coverArrived) { coverArrived = true; CrouchAndCover(); }
                    FaceHold(settleYawDeg, "arrive");
                    if (Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, settleYawDeg)) <= 1f)
                    {
                        // Arrival = position ∧ facing — the seek completes only
                        // once he's converged on the rolled settle facing.
                        SetState(CombatState.InCover);
                        Relocating = false;   // the relocate phase ends where the seek ends
                        // The authored rhythm starts here: gather → top-up → settle → rise-check.
                        afterSettle = EngagementBeat.TopUp;
                        EnterBeat(EngagementBeat.Settle);
                        LastResult = $"in cover behind {CoverObstacleId} ({coverWallClass}, duck {targetDuck:F2})";
                        Debug.Log($"🥊 [Combat] {name} — in cover, pose {(coverWallClass == CoverSensor.WallClass.Full ? "STAND (Idle)" : $"duck@{targetDuck:F2}")}");
                    }
                }
                else
                {
                    // Same leg mechanics as the beat steps — duck gait, axis
                    // slide, unstick nudges. An exhausted nudge budget aborts
                    // (the centroid proved unreachable) → same honest last
                    // stand as the no-cover path: hold position facing him.
                    if (StepToward(CoverPoint, "seek"))
                    {
                        Debug.Log($"⚠️ [Combat] {name} — cover unreachable ({dist:F2}m out) — holding position");
                        SetState(CombatState.HoldPosition);
                        CoverPoint = Feet();
                        CoverObstacleId = null;
                        coverWallClass = CoverSensor.WallClass.Crouch;
                        targetDuck = 1f;
                        CrouchAndCover();
                        FaceTrack(AimAnchor(), "hold-position");
                        LastResult = $"cover unreachable — holding position";
                    }
                }
            }
            // THE single write path runs LAST — after every intent is declared.
            TickFacing();
        }

        /// <summary>The exposure beat (EXPOSURE_ENGAGEMENT.md): probe the
        /// threat's posed silhouette on a cadence; while any voxel resolves,
        /// the IK rides the live centroid and the revolver fires on
        /// convergence + cadence. Zero visible = tucked → stow + crouch-idle,
        /// holding lastSeenTarget for the pop-up phase. The cylinder running
        /// dry starts the reload beat — the arm dips and one chamber seats
        /// every `roundLoadInterval` until full, then re-engages if geometry
        /// still allows (the authored trigger for a deliberate pop-up stays
        /// deferred).</summary>
        void TickExposureFire()
        {
            if (solver == null) solver = GetComponent<ArmAimSolver>();
            if (mount == null) mount = GetComponent<WeaponMount>();
            if (solver == null) return;

            // Cadenced probe — the visible-count answer is the ONLY gate on
            // whether geometry supports firing at all. The consecutive-empty
            // count is the Engaged drop gate (event-shaped, not a clock).
            if (ThreatCharacter != null && Time.unscaledTime >= nextVisProbe)
            {
                nextVisProbe = Time.unscaledTime + visInterval;
                emptyProbes = Sensor.TryGetVisibleVoxels(ThreatCharacter, out _)
                    ? 0 : emptyProbes + 1;
            }
            var vis = (ThreatCharacter != null && sensor != null
                       && sensor.LastVisible.target == ThreatCharacter)
                ? sensor.LastVisible : default;
            bool canSee = vis.visible > 0;

            // (The staggered reload tick lives at the top of Update — the wheel
            // is physical, not combat-state.)

            // Belief is EVENT-driven — it snaps at acquisition and loss edges
            // to a computed centroid and NEVER drifts mid-contact. While
            // contact holds, the live body (ThreatFeet) is the truth channel;
            // the belief only matters once he's out of sight again.
            if (canSee)
            {
                LastSeenAt = Time.unscaledTime;
                lastConfirmed = ThreatFeet();   // the RING — entity position
                                                // at confirmation, not the
                                                // wandering pixel centroid
                if (!contactSeen)
                {
                    LastSeenTarget = lastConfirmed;
                    ThreatPos = lastConfirmed;   // reacquired WHERE he stands
                }
            }
            else if (contactSeen)
            {
                // Loss edge — freeze belief where he was last confirmed.
                ThreatPos = lastConfirmed;
                LastSeenTarget = lastConfirmed;
            }
            contactSeen = canSee;

            // Arm gate = exposure + not-reloading + an equipped weapon + beat
            // authority. Arm once, retarget every tick — the centroid moves as
            // the silhouette changes. Any pose may fire if the IK resolves.
            bool clearToArm = !reloading && mount != null && mount.IsEquipped
                           && BeatAllowsFire;
            // Swing dwell = aim-without-fire: the IK presents at the aim
            // anchor (belief) under the Looking sweep — the arm is raised
            // but the trigger stays beat-owned (fire gate below).
            bool swingAim = Beat == EngagementBeat.Swing && swingPhase == 1
                         && !reloading && mount != null && mount.IsEquipped;
            bool wantAim = dbgArmSolver && ((canSee && clearToArm) || swingAim);
            if (wantAim)
            {
                aimPointWorld = canSee ? vis.centroid : AimAnchor();
                if (solver.IsAiming) solver.SetTarget(aimPointWorld);
                else solver.Arm(aimPointWorld);
            }
            // Disarm hold: a lone empty probe must not pump-stow him — hold the
            // arm on lastSeen through brief misses (disarmHoldSec). Losing
            // AUTHORITY (reload starts, weapon gone, beat closed the channel)
            // stows instantly — the arm is still beat-owned.
            bool holding = dbgArmSolver && solver.IsAiming && clearToArm
                        && Time.unscaledTime - LastSeenAt <= disarmHoldSec;
            // The arm is beat-owned outright: an arm this tick didn't grant
            // is an ORPHAN (a leftover from possess-entry, a HUD ARM test,
            // a pre-combat carryover) — it would keep solving toward a stale
            // point and fight the settle facing. ExposureArmed only records
            // the display flag; it is not the ownership check.
            if (!wantAim && !holding && solver != null && solver.IsAiming)
                solver.Disarm();
            bool armed = (wantAim || holding) && solver.IsAiming;
            if (armed != ExposureArmed) { ExposureArmed = armed; Notify(); }

            // Fire gate: armed + converged (blend raised, bore error inside
            // the gate) + cadence + a live round + a FREE bore path — the
            // welded muzzle ray is the truth; converged-into-the-wall just
            // wastes rounds into brick.
            if (!solver.IsAiming || cylinder <= 0 || !BeatAllowsFire
                || Time.unscaledTime < nextFireTime
                || solver.Blend < 0.7f || solver.LastAngularErrorDeg > aimGateDeg
                || BoreBuried(aimPointWorld)) return;
            nextFireTime = Time.unscaledTime + fireInterval;
            if (ProjectileSystem.GetOrCreate().Fire(mount, solver.LastAngularErrorDeg, vis.centroid)
                && SpendRound())
            {
                ShotsFired++;
                LastShotAt = Time.unscaledTime;
                // Combat reloads automatically on dry — but inside the cover
                // rhythm the TopUp beat owns the wheel; a dry Engaged ducks
                // and settles first, reload only starts at the beat.
                if (cylinder <= 0 && State != CombatState.InCover) StartReload();
                Notify();   // FIRE beat — the flow chart lights on THIS event, not a poll window
            }
        }

        // In cover = ducked or stood behind a full wall — hidden either way.
        // The exposure tick may still arm the IK if his eye clears the
        // parapet; hidden = zero voxels = he simply can't fire back yet.
        void CrouchAndCover()
        {
            // Physical arrival at the centroid = THE facing-commitment
            // moment. The roll lives HERE — the turn rides whichever beat
            // actually moved him (fold, return walk, approach) — Settle is
            // the breath after; it never owns a re-face.
            bool returning = settleSpot == CoverPoint;   // re-arriving at the same spot
            settleSpot = CoverPoint;
            sbyte side;
            if (settleFace == SettleFace.Random)
            {
                // On a return, never the same facing twice — the other
                // two directions, equally likely.
                do { side = (sbyte)(UnityEngine.Random.Range(0, 3) - 1); }   // −1 left · 0 back · +1 right
                while (returning && side == PeekSide);
            }
            else side = settleFace == SettleFace.Back ? (sbyte)0
                : settleFace == SettleFace.Left ? (sbyte)-1 : (sbyte)1;
            PeekSide = side;
            float tb = BearingDeg(AimAnchor() - Feet());
            settleYawDeg = side == 0 ? tb + 180f : tb + side * 90f;
            if (anim != null) anim.coverContext = true;   // Full or ducked — covered is covered
            // Scalar pick: deep band (duck>1) needs the state-5 endpoints; the
            // morph band and Full both ride Idle where icr scales the fold.
            anim?.RequestPose(targetDuck > 1.02f ? CharacterAnimation.AnimState.Crouching
                                                 : CharacterAnimation.AnimState.Idle,
                              CharacterAnimation.PRIORITY_COMBAT);
            // Facing is beat-owned — the ARRIVING beat turns him to the
            // rolled side/back ("arrive"/"return" Hold or Duck's FaceCurve);
            // no threat-facing snap here.
        }

        /// <summary>Character transform sits at the volume corner — feet = XZ center.</summary>
        private Vector3 Feet()
        {
            var t = transform.position;
            if (vox != null)
            {
                var d = vox.Dims;
                t += new Vector3(d.x * vox.voxelSize * 0.5f, 0f, d.z * vox.voxelSize * 0.5f);
            }
            return t;
        }

        /// <summary>Same corner→center for the threat — his LIVE feet.</summary>
        private Vector3 ThreatFeet()
        {
            var t = ThreatCharacter.transform.position;
            var d = ThreatCharacter.Dims;
            return t + new Vector3(d.x * ThreatCharacter.voxelSize * 0.5f, 0f, d.z * ThreatCharacter.voxelSize * 0.5f);
        }

        /// <summary>Feet+chest SamplePoint walk-block, with X/Z slide — same shape as possess.</summary>
        private void MoveWithCollision(Vector3 delta)
        {
            if (world == null) { transform.position += delta; return; }
            Vector3 dims = vox != null ? vox.WorldSize : new Vector3(0.6f, 1.8f, 0.6f);
            Vector3 half = new Vector3(dims.x * 0.5f, 0f, dims.z * 0.5f);

            bool Free(Vector3 corner)
            {
                Vector3 feet = corner + half + Vector3.up * 0.06f;
                Vector3 chest = corner + half + Vector3.up * (dims.y * 0.6f);
                var f = world.SamplePoint(feet);
                if (f.kind == VoxelCollisionWorld.PointKind.TerrainSolid ||
                    f.kind == VoxelCollisionWorld.PointKind.VolumeSolid) return false;
                var c = world.SamplePoint(chest);
                return c.kind != VoxelCollisionWorld.PointKind.TerrainSolid
                    && c.kind != VoxelCollisionWorld.PointKind.VolumeSolid;
            }

            Vector3 dst = transform.position + delta;
            if (Free(dst)) { transform.position = dst; return; }
            // Slide along the DOMINANT component first — corner-wrap needs
            // the free axis tried before the blocked one; a fixed X-then-Z
            // order kept feeding him back into the wall lip.
            bool xDom = Mathf.Abs(delta.x) >= Mathf.Abs(delta.z);
            Vector3 a = xDom ? new Vector3(delta.x, 0f, 0f) : new Vector3(0f, 0f, delta.z);
            Vector3 b = delta - a; b.y = 0f;
            Vector3 pa = transform.position + a;
            if (Free(pa)) { transform.position = pa; return; }
            Vector3 pb = transform.position + b;
            if (Free(pb)) transform.position = pb;
        }
    }
}
