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
        [Tooltip("Max ledge height the body can step UP — curbs, arena aprons, thresholds. Applied to the walk gate, route corridors, and the cell flood's neighbor edges.")]
        /// The body's step law — single-owned by VoxelCharacter (the flood's
        /// edge gate, the footprint deck rule, and the mover's climb probes
        /// all read the identical value). Kept as a property so existing
        /// cc.stepUpMax reads keep working.
        public float stepUpMax => vox != null ? vox.stepUpMax : 0.15f;
                                          // curb lips/thresholds only — 0.5m walls stay walls
        [Tooltip("Designated floor material ids (MAT_*). Empty = physics-only walkable — every grounded, body-fitting cell carries feet. Painted floor stamps narrow this to authored intent.")]
        public int[] walkableMaterials;
        /// <summary>The ARENA duel lot's designated floor family: the sidewalk
        /// pad (MAT_SIDEWALK 102 — the same id VoxelTerrainBuilder paints on
        /// city sidewalks and WaypointGraph measures) plus the floor-class
        /// marker plates fighters legally stand on (dark-wood pads 106,
        /// painted-metal stands 111). Wall/curb/cover materials are absent —
        /// terracing can't make a staircase out of designation. A semantic
        /// MAT_FLOOR class range replaces these finish-material ids later.</summary>
        public static readonly int[] ArenaWalkableMaterials = { 102, 106, 111 };

        [Header("Exposure fire (docs/systems/EXPOSURE_ENGAGEMENT.md)")]
        [Tooltip("The body being fought — a bare feet position can't be silhouette-sampled. Set via EnterCombat(threatPos, threatBody).")]
        public VoxelCharacter ThreatCharacter;
        [Tooltip("Seconds between posed-silhouette visibility probes — 0 = every frame (probe verdicts are clock-based now).")]
        public float visInterval = 0f;
        [Tooltip("Revolver cadence — deliberate double-action seconds between shots.")]
        public float fireInterval = 0.9f;
        [Tooltip("Model 10 cylinder — after this many shots he lowers the gun for the reload beat.")]
        public int cylinderCapacity = 6;
        [Tooltip("Staggered reload — once the wheel runs dry, one round seats every N seconds; the arm stays dipped until all chambers are full (no reload anim exists — the dip IS the beat).")]
        public float roundLoadInterval = 1.5f;
        [Tooltip("Max bore-vs-centroid error (deg) that still counts as converged.")]
        public float aimGateDeg = 14f;
        [Tooltip("Seconds the bore must hold the CURRENT aim point inside aimGateDeg before the trigger falls — kills the instant shot on the acquisition edge (the error channel is a frame stale, so one sample after a retarget is measured against the OLD point).")]
        public float fireSettleSec = 0.15f;

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
        [Tooltip("Sustained no-contact seconds in Engaged before he drops — a dropped-frame probe must not bounce him. A CLOCK, not a count — correct at any visInterval including 0 (probe every frame).")]
        public float dropAfterSec = 0.5f;
        [Tooltip("Duck ease while rising / dropping back into cover (duck/s).")]
        public float riseDuckEase = 3f;
        [Tooltip("Clearance margin above the wall top for the rise solve (m).")]
        public float riseMargin = 0.02f;
        [Tooltip("Bore sits ~this far BELOW the eye when aimed — sights sit above the bore line (sight radius). The presented muzzle is eye−sightGap, not the wrist pivot.")]
        public float sightGap = 0.045f;
        [Tooltip("When a rhythm run ends (rise refused / no contact / target lost), loop back through the seam and try again instead of parking at None.")]
        public bool loopBeats = true;
        [Tooltip("Hide-only combatant: seeks first cover exactly like a fighter, then parks in the settle seam forever — no rise, no fire authority anywhere. The 3-stage-search test dummy.")]
        public bool holdInCover;
        [Tooltip("Cover-positioning intelligence 0..1 (Hoods.xtx INT normalized): 1 = nests at the zone centroid, 0 = parks on the Hidden rim — zero margin, 'sticking out'. COMBAT_COVER_STATE → INT axis.")]
        [Range(0f, 1f)] public float coverIntNorm = 1f;
        [Header("Lost-contact orbit search")]
        [Tooltip("Search intelligence 0..1 — bearing precision for hop picks: 1 = demands a near-exact bearing around lastSeen, 0 = accepts sloppy hops (tolerance 15°..60°).")]
        [Range(0f, 1f)] public float searchIntNorm = 1f;
        [Tooltip("Every Nth orbit hop is a RECON hop — lands the full settle→peek loop. The other N-1 are TRANSIT — land, brief settle, next step (2 transit → 1 recon at the default 3).")]
        [Range(1, 6)] public int orbitReconEvery = 3;
        [Tooltip("A/B flag — layer the lookScan head sweep over open-ground search legs (off = eyes forward; the AimWalk gait still reads armed-alert).")]
        public bool dbgSearchLook = false;
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
        private Vector3 riseRefusedAt = new Vector3(float.NaN, 0f, 0f);   // stand that already burned one swing attempt — second refusal relocates
        private float boreBuriedSince = -1f;     // sustained sees-but-can't-shoot clock (−1 = bore clear)
        [Range(0.5f, 15f), Tooltip("PINNED verdict — a stand that can't fight, can't swing-peek, and has no fightable relocation parks this long between re-tries.")]
        public float pinRecheckSec = 4f;
        private bool pinned;                   // hide-only verdict: park at the gate, re-arm one rhythm cycle on pinUntil
        private float pinUntil;                // next re-verdict time while pinned

        // ── Seam arbiter (Tab F) — intents scored on the live blackboard
        //    dispatch motor verbs; every program returns a verdict to the
        //    seam. Beat stays the posture channel — poses, BeatAllowsFire
        //    and peekAim key off it; the arbiter owns only the sequencing.
        [Header("Seam arbiter — intent-scored dispatch")]
        [Tooltip("F-model: the seam scores intents on the blackboard and dispatches verbs; beats become programs that post verdicts back. Off = authored beat chain.")]
        public bool seamArbiter = false;
        [Range(0.1f, 2f), Tooltip("Post-action breath at the seam — the only dwell the arbiter uses; verdicts and contact edges bypass it.")]
        public float seamBreath = 0.35f;
        private bool seamNow;
        private int zeroStreak;
        private bool orbitDone;
        private bool seamReconBoost;
        private float arbHoldUntil;
        private int lastMoveCell = -1;
        private Vector3 probeDeadAt = new Vector3(float.NaN, 0f, 0f);
        private Vector3 fireDeadAt  = new Vector3(float.NaN, 0f, 0f);   // stand where the bore buried — can't deliver
        private readonly HashSet<int> arbBlocked = new HashSet<int>();
        private string arbRunning = "—", arbVerdict = "—", arbHist = "", arbBoard = "";
        private readonly float[] arbScores = new float[10];
        public static readonly string[] ArbNames =
            { "SeekCover", "Relocate", "Engage", "OrbitSearch", "Recon",
              "FinalConfirm", "Reload", "StandDlv", "Watch", "Pinned" };
        public bool   ArbActive  => seamArbiter && !scriptedMode;
        public string ArbRunning => arbRunning;
        public string ArbVerdict => arbVerdict;
        public string ArbHist    => arbHist;
        public string ArbBoard   => arbBoard;
        public float[] ArbScores => arbScores;
        private Vector3 lastPos;
        private float movedAccum;              // XZ distance covered since EnterCombat — wedge diagnostic
        private float swingDwellUntil, swingStuckT;
        /// Per-leg unstick budget — perpendicular sidesteps spent before a
        /// StepToward leg reports "done off-target" (caller re-verifies).
        private int stuckNudges, stuckNudgeSide;
        private float stuckBest = float.MaxValue;   // best waypoint distance seen this leg — progress is GOAL-closing, not body-motion
        private bool returnWalked;             // Duck's off-spot return leg began
        /// Micro-leg routing — every point move consumes the map's corridor
        /// (BuildRoute chain + string-pull), so a corner the flood routes
        /// around is a corner the mover ROUNDS, not clips. `routedLeg` = the
        /// live leg materialized a corridor; the *Built latches build once
        /// per leg, reset where the leg's step-guard resets.
        private bool routedLeg;
        private int routeStamp = -1;   // field BuildStamp the route was materialized against
        private bool swingP0Built, swingP2Built, returnBuilt;
        /// Post-combat watch — a fight that ended its own hunt (final look
        /// clear, blind abandon, unresumable) stands down but keeps the last
        /// opponent under the posed cone + the heard-shot channel; a
        /// reacquisition or a bang re-enters the full lifecycle — the duel
        /// loops until somebody can't answer. External/manual exits (HUD,
        /// possess, setup teardown) stand ALL the way down.
        private VoxelCharacter watchTarget;
        private bool watching;
        private float nextWatchProbe;
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
        public bool yawWatch = false;

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
        [Tooltip("Bore-state orb at the real muzzle while the arm presents: green = path to aim point clear, red = buried.")]
        public bool dbgBoreOrb = true;
        private bool boreOrbDrawn;
        private string fireHoldLogged;          // last logged fire-gate refusal (transition-only logging)
        private float nextFireHoldLog;          // 5Hz coalescer — flapping reasons report as ×N flips, not 60 lines/sec
        private int fireHoldFlips;

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
            // One global cadence — alternating writers used to bypass the
            // heartbeat via `who != lastYawWriter` and emit per-frame; at
            // Debug.Log prices that IS the frame-rate tank. 4Hz keeps a
            // writer flap legible (the `who` field still alternates) at a
            // bounded cost.
            if (yawWatch && Mathf.Abs(d) > 0.1f
                && Time.unscaledTime - lastYawLogT > 0.25f)
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
                swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0; stuckBest = float.MaxValue;
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
        public EngagementBeat NextAfterSettle => pinned
            ? EngagementBeat.None : loopGate && !loopBeats
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
        /// <summary>XZ distance actually covered since EnterCombat — the wedge
        /// diagnostic: SeekingCover with ~0 means picked but can't execute.</summary>
        public float DebugMoved => movedAccum;
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
        /// <summary>The fire gate's first refusal this tick — null = gate clear.
        /// The flow strip lights FIRE on the event; this names WHY it held.</summary>
        public string FireHold { get; private set; }
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
        private float convergeSince = -1f;   // unscaledTime the bore has CONTINUOUSLY held aim inside the gate (−1 = not holding); acquisition edges reset it
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
            CoverPoint = CertifiedStand(s.stand); CoverObstacleId = s.volId;
            targetDuck = s.reqD; coverWallClass = s.cls;
            Beat = EngagementBeat.None; afterSettle = EngagementBeat.None;
            loopGate = false; buriedSince = 0f; pinned = false;
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
        private float emptyProbeSince = -1f;         // first empty probe's timestamp — the drop gate's clock (−1 = contact holds)
        private EngagementBeat afterSettle;
        // ── Lost-contact ORBIT search (PERCEPTION_AWARENESS → Search &
        // resolution): the procedure collapsed to ONE rule — accumulate
        // ~90° of bearing around the belief anchor on a rolled side,
        // hopping Hidden zones at ≥ the buffer radius held when contact
        // was lost. Each hop reuses the position-1 loop (settle→peek).
        // pendingSearch = a miss armed the driver; the Settle seam
        // consumes it via TryOrbitStep. searchActive = episode live.
        private bool pendingSearch;
        private bool orbitFinalLook;             // terminal-stand confirming rise — its zero-contact verdict exits instead of re-arming
        private bool searchActive;                 // the orbit episode is live (arm → reacquire/exit)
        private float searchOriginBearing;         // deg — my bearing around the anchor at the first miss (the orbit's 0°)
        private float searchTargetBearing;         // deg — the orbit's goal spoke (origin + 90·side)
        private float searchProgress;              // deg accumulated along the rolled side (0..180+)
        private float searchBufferRadius;          // m — standoff at loss → the orbit's preferred radius (the ring = hot path; not an exclusion — hops at any depth are legal, on-ring scores best)
        private sbyte orbitSide;                   // ±1 — rolled CW/CCW at episode start (the only discrete call)
        private int orbitHops;                     // orbit legs issued this episode (drives the recon cadence)
        private bool orbitLegRecon;                // the live orbit leg ends in a full peek; transit legs keep moving
        private bool transitPending;               // a transit leg landed — the Settle seam re-issues the next step
        private Vector3 searchAnchorPos;           // the belief point the orbit bearings are measured around
        private readonly System.Collections.Generic.HashSet<int> searchVisited
            = new System.Collections.Generic.HashSet<int>();   // zone cells spent (committed or aborted) — prevents ping-pong
        private bool searching;                    // the live seek was issued by an orbit step (scout posture channel)
        private bool searchCovered;                // posture lane's local verdict (cached for the aim channel a frame later)
        private bool savedLookScan;                // pre-search lookScan — restored when the search seek ends
        private readonly System.Collections.Generic.List<CoverField.ZoneSpot> zoneScratch
            = new System.Collections.Generic.List<CoverField.ZoneSpot>(64);

        // ── Leg routing: zones are the destination NODES; the actor-seeded
        // DistMap (goal-seeded Dijkstra over the field's declared channel —
        // the cell-scale sibling of Pathfinder's FlowField) owns reachability
        // + cost; legs materialize by walking its next-hops, so a corridor
        // the map can't reach is never even attempted. Ring preference is a
        // SCORING term in TryOrbitStep now (geodesic |dist−buffer|), not a
        // flood-time bias.
        [Header("Leg routing")]
        public bool routeLegs = true;
        private readonly List<Vector3> legRoute = new();   // pulled waypoint chain, last = exact CoverPoint
        private int legRouteIdx;
        private Vector3 legRouteTarget = new Vector3(float.NaN, 0f, 0f);   // the CoverPoint the route was built for
        private HashSet<int> walkMaskSet;   // lazy — walkableMaterials → membership test
        private string routeFail;                                        // WHY BuildRoute returned false — surfaced into the no-corridor verdict
        private CoverField.DistMap meMap;        // the actor map — reachability/cost FROM my cell
        private int meMapSeed = -1, meMapStamp = -1;
        private CoverField.DistMap anchorMap;    // the belief map — geodesic distance FROM the search anchor
        private int anchorMapSeed = -1, anchorMapStamp = -1;
        private readonly List<int> chainScratch = new List<int>(128);

        /// <summary>Arm the search driver — every "peeked and saw nothing"
        /// verdict (Engaged drop, zero-contact rise, zero-contact swing) is
        /// the same miss. The FIRST miss of an episode records the orbit
        /// frame: anchor = belief, origin bearing = where I stand, buffer =
        /// the standoff held at the loss (the orbit's preferred radius —
        /// hop scoring hugs it, any depth is legal), side = rolled CW/CCW.
        /// Scripted mode never arms — slots own him.</summary>
        void ArmSearch()
        {
            if (scriptedMode || pendingSearch) return;
            pendingSearch = true;
            if (!searchActive)
            {
                searchActive = true;
                searchAnchorPos = AimAnchor();
                searchOriginBearing = BearingAround(searchAnchorPos, Feet());
                Vector3 d = Feet() - searchAnchorPos; d.y = 0f;
                searchBufferRadius = d.magnitude;
                orbitSide = (sbyte)(UnityEngine.Random.value < 0.5f ? -1 : 1);
                searchProgress = 0f;
                searchTargetBearing = searchOriginBearing + orbitSide * 90f;
                orbitHops = 0; orbitLegRecon = false;
                searchVisited.Clear();
                // The zone he's peeking FROM is spent — he already looked here.
                int ci = sensor != null && sensor.Field != null
                    ? sensor.Field.CellIndexOf(Feet()) : -1;
                if (ci >= 0 && sensor.Field.TryZoneOfCell(ci, out var own))
                    searchVisited.Add(own.cell);
            }
        }

        /// <summary>Full episode teardown — pending flag, orbit frame,
        /// visited set, and any live leg's posture claim.</summary>
        /// <summary>Heard-shot stimulus — the opponent's report IS last-known
        /// intel: sound carries his true firing position. Snaps the belief
        /// channel (ThreatPos / LastSeenTarget / lastConfirmed — cover zones
        /// re-paint for free) and re-anchors a live orbit on the bang — every
        /// shot re-bases the hunt at the real spot; silence lets the last
        /// arc complete. Deliberately untouched: contactSeen, LastSeenAt,
        /// convergeSince — a report is intel, not a sighting; the arm-hold
        /// grace and the drop clock keep running on the eye channel's truth.</summary>
        public void OnShotHeard(Vector3 shotPos)
        {
            if (State == CombatState.Idle)
            {
                // Post-combat watch — a bang while standing down is still
                // intel: the fighter re-enters the whole lifecycle at the
                // heard position (belief = the report, same as mid-fight).
                if (watching && watchTarget != null)
                {
                    var heard = watchTarget; watching = false; watchTarget = null;
                    Debug.Log($"🥊 [Combat] {name} — watch: heard a shot — re-entering combat");
                    EnterCombat(shotPos, heard);
                }
                return;
            }
            ThreatPos = shotPos;
            LastSeenTarget = shotPos;
            lastConfirmed = shotPos;
            if (searching) ArmSearch();   // re-anchor the orbit on the bang
            if (seamArbiter) seamNow = true;   // intel moved — instant re-eval
        }

        void ResetSearch()
        {
            pendingSearch = false; searchActive = false; transitPending = false; orbitFinalLook = false;
            searchProgress = 0f; orbitSide = 0; orbitHops = 0; orbitLegRecon = false;
            searchVisited.Clear();
            EndSearchSeek();   // ends a live leg's posture claim (restores lookScan)
        }

        /// <summary>Post-combat watch — runs at Idle: the last opponent stays
        /// under the posed vision cone (the SAME detection law combat uses —
        /// silhouette probe ∧ cone coverage, not geometric proximity). A
        /// reacquisition re-enters the full lifecycle where he actually
        /// stands; OnShotHeard covers the hear channel.</summary>
        void TickWatch()
        {
            if (!watching) return;
            if (watchTarget == null) { watching = false; return; }
            if (PossessController.Body == vox) return;   // the player owns THIS body — others keep watching
            if (Time.unscaledTime < nextWatchProbe) return;
            nextWatchProbe = Time.unscaledTime + Mathf.Max(visInterval, 0.1f);
            if (sensor == null) return;
            if (vis == null) vis = GetComponent<VisionSensor>();
            if (!sensor.TryGetVisibleVoxels(watchTarget, out var pv) || !ConeCovers(pv.centroid)) return;
            var d = watchTarget.Dims;
            Vector3 tpos = watchTarget.transform.position
                + new Vector3(d.x * watchTarget.voxelSize * 0.5f, 0f, d.z * watchTarget.voxelSize * 0.5f);
            var tgt = watchTarget; watching = false; watchTarget = null;
            Debug.Log($"🥊 [Combat] {name} — watch: {tgt.name} re-acquired in the cone — re-entering combat");
            EnterCombat(tpos, tgt);
        }

        /// <summary>The orbit terminated — satisfied angle or exhausted hops —
        /// and he's standing AT a committed stand (the seam only). Instead of
        /// abandoning blind, commit ONE confirming rise-look from here: its
        /// contact verdict re-engages (ResetSearch clears the flag), its
        /// zero-contact or refused verdict exits instead of re-arming the
        /// finished driver. Second entry = the look already ran → exit.</summary>
        void FinalLookOrExit()
        {
            if (orbitFinalLook) { ExitCombat(true); return; }
            orbitFinalLook = true;
            Debug.Log($"🥊 [Combat] {name} — orbit done at {searchProgress:F0}° — final confirming look");
            EnterBeat(EngagementBeat.Rise);
        }

        /// <summary>deg — orbit progress accumulated along the rolled side.</summary>
        public float SearchProgress => searchProgress;
        /// <summary>The belief point the search orbit is measured around (debug markers).</summary>
        public Vector3 SearchAnchor => searchAnchorPos;
        /// <summary>deg — bearing of my feet around the anchor at the first miss (the orbit's 0°).</summary>
        public float SearchOriginBearing => searchOriginBearing;
        /// <summary>deg — the orbit's goal bearing (origin + 90·side).</summary>
        public float SearchTargetBearing => searchTargetBearing;
        /// <summary>m — the standoff at the loss = the orbit's preferred radius (the hot path).</summary>
        public float SearchBufferRadius => searchBufferRadius;
        /// <summary>The live leg's routed corridor (waypoints, last = CoverPoint) — the HUD draws it as the polyline.</summary>
        public IReadOnlyList<Vector3> LegRoute => legRoute;
        /// <summary>Index of the waypoint currently being stepped to.</summary>
        public int LegRouteIndex => legRouteIdx;
        /// <summary>+1 CCW / −1 CW — the rolled orbit direction.</summary>
        public int OrbitSide => orbitSide;
        /// <summary>An orbit leg is currently walking (the scout posture seek).</summary>
        public bool SearchLegOut => searching;
        /// <summary>The live orbit leg's class — true = recon (lands a peek), false = transit.</summary>
        public bool OrbitLegRecon => orbitLegRecon;
        /// <summary>The orbit search procedure is live — armed at the miss,
        /// chaining cover hops toward the 90° flank, cleared by a reacquire
        /// or ExitCombat.</summary>
        public bool Searching => pendingSearch || searchActive;

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
            movedAccum = 0f; lastPos = transform.position;
            contactSeen = false;
            watching = false; watchTarget = null;   // a live engagement replaces the watch
            loopGate = false; awaitingLoop = false; failedFaces.Clear(); pinned = false;
            emptyProbeSince = -1f;
            nextFireTime = 0f; nextVisProbe = 0f; LastShotAt = -1f;
            ExposureArmed = false;
            LastSeenTarget = null; LastSeenAt = -1f;
            ResetSearch();
            seamNow = false; zeroStreak = 0; orbitDone = false; seamReconBoost = false;
            probeDeadAt = new Vector3(float.NaN, 0f, 0f); fireDeadAt = probeDeadAt;
            arbBlocked.Clear(); lastMoveCell = -1;
            arbRunning = "—"; arbVerdict = "threat registered"; arbHist = "";
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
                ? Sensor.TryPickCoverNear(coverAnchor.Value, maxDist, out Vector3 p, out string oid, out float reqD, out coverWallClass, coverIntNorm, MeMap())
                : Sensor.TryPickCover(Feet(), out p, out oid, out reqD, out coverWallClass, coverIntNorm, MeMap());
            if (picked)
            {
                CoverPoint = CertifiedStand(p);
                CoverObstacleId = oid;
                targetDuck = reqD;
                SetState(CombatState.SeekingCover);
                anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                anim.walkSpeed = Mathf.Clamp(moveSpeed, 0.5f, 2f);
                if (seamArbiter) arbRunning = "MOVE(SeekCover)";   // the zero-dwell first dispatch
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
            if (seamArbiter) arbRunning = "HOLD(Watch)";
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

        public void ExitCombat() => ExitCombat(false);

        /// <summary>keepWatch = the combat ended its OWN hunt (orbit verdict,
        /// blind abandon, unresumable leg) — he stands down but the last
        /// opponent stays under the watch cone; the next sighting or heard
        /// shot re-enters the lifecycle. Manual exits stand all the way
        /// down.</summary>
        public void ExitCombat(bool keepWatch)
        {
            watching = keepWatch && !scriptedMode && ThreatCharacter != null;
            watchTarget = watching ? ThreatCharacter : null;
            seamNow = false; zeroStreak = 0; orbitDone = false; arbBlocked.Clear();
            probeDeadAt = new Vector3(float.NaN, 0f, 0f); fireDeadAt = probeDeadAt;
            arbRunning = "—"; arbVerdict = "exit"; arbHist = "";
            nextWatchProbe = 0f;
            targetDuck = 0f;
            if (anim != null) { anim.duck = 0f; anim.coverContext = false; }
            anim?.ReleasePose(CharacterAnimation.PRIORITY_COMBAT);
            anim?.SetState(CharacterAnimation.AnimState.Idle);
            if (solver != null && solver.IsAiming) solver.Disarm();
            ExposureArmed = false;
            Beat = EngagementBeat.None; afterSettle = EngagementBeat.None;
            loopGate = false; awaitingLoop = false; Relocating = false; buriedSince = 0f; pinned = false;
            contactSeen = false;
            legRoute.Clear(); legRouteTarget = new Vector3(float.NaN, 0f, 0f);
            riseRefusedAt = legRouteTarget; boreBuriedSince = -1f;
            if (boreOrbDrawn) { PathDebugRenderer.Instance?.ClearXrayCube($"bore_{name}"); boreOrbDrawn = false; }
            ResetSearch();
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

        /// <summary>The armed stand-down — combat exited organically and he
        /// keeps the last opponent under the cone; a sighting or heard shot
        /// re-enters. Null when stood all the way down (manual exit).</summary>
        public bool Watching => watching;
        public VoxelCharacter WatchTarget => watching ? watchTarget : null;

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
            beatUntil = Time.unscaledTime
                + (seamArbiter && b == EngagementBeat.Settle ? seamBreath : DwellFor(b));
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
                    emptyProbeSince = -1f;
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
                        bool burned = riseRefusedAt == CoverPoint;
                        riseRefusedAt = CoverPoint;
                        // Terminal-stand look — the SAME peek doctrine as any
                        // other beat: can't see over → step around the corner
                        // and look again. The swing's return lands at a
                        // second Rise whose verdict still owns the exit;
                        // a burned peek (already swung from this stand) =
                        // no confirm physically exists here — abandon.
                        if (orbitFinalLook)
                        {
                            if (!scriptedMode && !burned) { EnterBeat(EngagementBeat.Swing); return; }
                            orbitFinalLook = false;
                            LastRiseDiag = $"final look refused{(burned ? " — peek already burned at this stand" : "")} — abandoning";
                            Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                            ExitCombat(true); return;
                        }
                        // "Sees but can't shoot" is a RELOCATE verdict, not
                        // a firing position: the FIRST refusal at a stand
                        // still gets the lateral peek, but a refusal after
                        // the swing already burned its chance means this
                        // cover isn't fightable — re-pick a FIGHTING cover
                        // instead of looping peek→swing→peek forever.
                        if (seamArbiter && !scriptedMode)
                        {
                            // PROBE's second solver — one swing-around try per
                            // stand; when both refuse the seam marks the stand
                            // probe-dead and the verdict returns.
                            if (!burned) { EnterBeat(EngagementBeat.Swing); return; }
                            probeDeadAt = CoverPoint;
                            SeamDone("probe refused — rise + swing both dead");
                            return;
                        }
                        if (!scriptedMode && burned && TryRelocateToFightingCover()) return;
                        // The wall can't be fought OVER — try looking AROUND
                        // it first (lateral peek). Scripted mode skips it:
                        // the script owns position.
                        if (!scriptedMode) { EnterBeat(EngagementBeat.Swing); return; }
                        if (TryRelocateToFightingCover()) return;
                        pinned = true; pinUntil = Time.unscaledTime + pinRecheckSec;
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
                        // No rim to step from — under the terminal look that
                        // IS the verdict: the arc is done and no confirm
                        // exists at this stand. Abandon rather than pin.
                        if (orbitFinalLook)
                        {
                            orbitFinalLook = false;
                            LastRiseDiag = "final look — no peek edge, abandoning";
                            Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                            ExitCombat(true); return;
                        }
                        if (seamArbiter && !scriptedMode)
                        {
                            probeDeadAt = CoverPoint;
                            SeamDone("no peek rim — stand probe-dead");
                            return;
                        }
                        // No zone edge to step from — the old refusal path.
                        if (TryRelocateToFightingCover()) return;
                        // Every exit failed: no over-top line, no rim to peek
                        // around, nowhere fightable to move to. The stand is
                        // hide-only for this fight — PIN at the gate and
                        // re-test on a slow clock (a real rise = a real probe,
                        // so a moved threat wakes it) instead of flapping
                        // settle→topup→rise→duck forever.
                        pinned = true; pinUntil = Time.unscaledTime + pinRecheckSec;
                        LastRiseDiag = "pinned — stand can't fight, no peek edge, no fightable cover";
                        Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
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

            // Target presence picks the verdict tier: with one (lastSeen, or
            // the live threat as stand-in) the bore clearance is evaluated
            // (cleared / bore under lip); without one it's an eye-only solve
            // — a look, not a shot.
            Vector3? tgt = LastSeenTarget;
            if (!tgt.HasValue && ThreatCharacter != null)
                tgt = ThreatCharacter.transform.position + Vector3.up * 0.5f;

            // Solve at the ENGAGE yaw — where he'll end after the turn-to-
            // engage — not whatever the settle posture left him facing.
            engageYawDeg = BearingDeg(AimAnchor() - transform.position);
            float r = SolveRiseAtSpot(transform.position, engageYawDeg,
                wallTop, tgt,
                out int verdict, out float bestEye);
            if (r >= 0f)
            {
                LastRiseDiag = verdict == 4
                    ? $"cleared(eye-only): stand · wall {wallTop:F2}"
                    : $"cleared: stand · wall {wallTop:F2}";
                return r;
            }
            LastRiseDiag = verdict == 1 ? "eye eval failed"
                : verdict == 3 ? $"bore under lip: muzzle {bestEye - sightGap:F2} < wall {wallTop:F2}+{riseMargin:F2}"
                : $"can't see over: wall {wallTop:F2}+{riseMargin:F2} vs eye≤{bestEye:F2}";
            return -1f;
        }

        /// <summary>The rise verdict at an arbitrary stand spot — single
        /// evaluation, FULL STAND (firing posture is upright; the crouch-band
        /// candidate search is suspended). The eye anchor is evaluated at the
        /// live transform, then re-anchored: the local offset (yaw-unrotated)
        /// is carried to `stand` under `yawDeg`.
        /// verdict: 0 cleared · 1 eye-eval-fail · 2 eye can't clear ·
        /// 3 bore can't clear the lip vertically · 4 cleared eye-only.
        /// Returns 0 (the stand duck) or −1.</summary>
        float SolveRiseAtSpot(Vector3 stand, float yawDeg, float wallTop,
            Vector3? tgt, out int verdict, out float bestEye)
        {
            verdict = 1;
            Quaternion invYaw = Quaternion.Euler(0f, -transform.eulerAngles.y, 0f);
            Quaternion toYaw = Quaternion.Euler(0f, yawDeg, 0f);
            if (!vox.TryGetEyeAnchor(out Vector3 eyeW, out _, out _, 0f, 0f, 0f))
            { bestEye = float.MinValue; return -1f; }
            Vector3 eye = stand + toYaw * (invYaw * (eyeW - transform.position));
            bestEye = eye.y;
            if (eye.y <= wallTop + riseMargin) { verdict = 2; return -1f; }

            // Eye clears. The muzzle gate is VERTICAL lip clearance — the
            // cover-side question "can the presented bore ride over this
            // parapet": the aimed muzzle rides sightGap below the eye, so
            // it must clear the lip on its own. Whether THIS shot reaches
            // the target is the fire gate's truth (BoreBuried on the real
            // welded-muzzle ray at trigger time) — the solve must not fail
            // a stand because the TARGET happens to sit behind the cover.
            if (!tgt.HasValue) { verdict = 4; return 0f; }
            if (eye.y - sightGap >= wallTop + riseMargin) { verdict = 0; return 0f; }
            verdict = 3;
            return -1f;
        }

        /// <summary>Set up the lateral peek: side = the better opening
        /// (Exposed &gt; open &gt; Hidden at the final destination; ties go to
        /// the nearer end, committed `PeekSide` only breaks a dead heat);
        /// step-out destination = the FARTHER of the zone's hidden rim and
        /// the wall's physical end on that side + `swingMargin` past it, so
        /// the step clears the corner regardless of cover width. False when
        /// no zone geometry exists to step from.</summary>
        bool TryStartSwing()
        {
            Vector3 toT = AimAnchor() - Feet(); toT.y = 0f;
            if (toT.sqrMagnitude < 1e-4f) return false;
            Vector3 threatDir = toT.normalized;
            Vector3 lat = Vector3.Cross(Vector3.up, threatDir);   // right of threat-facing
            int ci = sensor != null ? sensor.Field.CellIndexOf(CoverPoint) : -1;
            if (ci < 0) return false;
            // Evaluate BOTH sides before committing: a peek only breaches
            // cover if the stepped destination can see the belief point —
            // and the field already knows the verdict. Stepping along a
            // long wall lands on another Hidden cell = peeking AT the wall
            // (the observed bug); the corner side's dest is Exposed/open.
            bool hasL = sensor.Field.TryZoneEdge(ci, lat * -1f, out Vector3 rimL, out _);
            bool hasR = sensor.Field.TryZoneEdge(ci, lat * +1f, out Vector3 rimR, out _);
            if (!hasL && !hasR) return false;
            // The WALL's physical ends on the cover face — zone rims measure
            // the hidden band, which can wrap the corner asymmetrically:
            // under-shoot peeks at more wall (the "wall side" bug), over-
            // shoot wastes the step. Take the farther of shadow-vs-geometry
            // per side, and on tied scores prefer the NEARER end — the open
            // side wins ties instead of the stale PeekSide roll.
            float latMin = float.MaxValue, latMax = float.MinValue;
            bool anyEnd = false;
            if (sensor.Field.TryZoneOfCell(ci, out var zo))
                foreach (var w in sensor.walls)
                {
                    if (w.volId != CoverObstacleId) continue;
                    if (Vector2.Dot(new Vector2(w.outDir.x, w.outDir.z), zo.outDir) < 0.7f) continue;
                    float proj = w.facePos.x * lat.x + w.facePos.z * lat.z;
                    if (proj < latMin) latMin = proj;
                    if (proj > latMax) latMax = proj;
                    anyEnd = true;
                }
            float Lat(Vector3 p) => p.x * lat.x + p.z * lat.z;
            float extL = anyEnd ? Mathf.Max(0f, Lat(hasL ? rimL : Feet()) - latMin) : 0f;
            float extR = anyEnd ? Mathf.Max(0f, latMax - Lat(hasR ? rimR : Feet())) : 0f;
            Vector3 destL = (hasL ? rimL : Feet()) - lat * (extL + swingMargin);
            Vector3 destR = (hasR ? rimR : Feet()) + lat * (extR + swingMargin);
            int sL = hasL || anyEnd ? SwingSideScore(destL) : int.MinValue;
            int sR = hasR || anyEnd ? SwingSideScore(destR) : int.MinValue;
            sbyte side;
            if (sL != sR) side = (sbyte)(sL > sR ? -1 : 1);   // the opening side wins (a side with no rim scores MinValue and loses)
            else
            {
                float dL = (destL - Feet()).sqrMagnitude, dR = (destR - Feet()).sqrMagnitude;
                side = Mathf.Abs(dL - dR) > 0.02f
                    ? (sbyte)(dL < dR ? -1 : 1)              // nearer open end beats the stale roll
                    : (PeekSide != 0 ? PeekSide
                        : (sbyte)(UnityEngine.Random.value < 0.5f ? -1 : 1));
            }
            Vector3 stepDir = lat * side;
            swingDest = side < 0 ? destL : destR;
            // The step faces its destination — declared per-tick in
            // StepToward as FaceTrack(dest) (the driver computes the
            // bearing — off-axis edges can't produce a stale heading).
            swingPhase = 0; swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0; stuckBest = float.MaxValue;
            swingP0Built = false;   // phase-0 routes through the map corridor
            PeekSide = side;   // the rolled side is the committed side now
            if (vis == null) vis = GetComponent<VisionSensor>();
            Debug.Log($"🥊 [Combat] {name} — swing-peek: side {(side < 0 ? "left" : "right")} step→({swingDest.x:F1},{swingDest.z:F1}) verdict L{sL}/R{sR}");
            return true;
        }

        /// <summary>Does a stepped-out destination actually OPEN a sightline —
        /// the field's own verdict at that cell: Exposed (2) breaches cover,
        /// out-of-field open ground (1), Hidden/Invalid/untested (0) means
        /// he'd just be peeking at more wall.</summary>
        int SwingSideScore(Vector3 dest)
        {
            var f = sensor.Field;
            int di = f.CellIndexOf(dest);
            if (di < 0) return 1;
            // The peek must be IN DOMAIN — physically hostable AND flood-
            // served from his feet (unreachable = unwalkable): a step-out
            // he can't route to can't serve a look no matter what it faces.
            var map = MeMap();
            if (map == null || !map.Reaches(di)) return 0;
            var s = f.state[di];
            return s == CoverField.CellState.Exposed ? 2 : s == CoverField.CellState.Hidden ? 0 : 0;
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
            // Stuck guard — ~0.6s of no PROGRESS toward the waypoint. Body
            // motion is not progress: a corner-vertex trap lets a perp nudge
            // shuttle him sideways forever while the goal distance never
            // shrinks (the old position-delta reset made that loop infinite).
            // Stuck spends sidestep nudges (±perpendicular hops — the greedy
            // axis slide can't wrap a convex corner lip); an exhausted budget
            // reports "leg done" — CALLERS re-verify distance to distinguish
            // arrived from aborted.
            Vector3 dwp = dest - Feet(); dwp.y = 0f; float wpDist = dwp.magnitude;
            if (wpDist < stuckBest - 0.05f)
            { stuckBest = wpDist; swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0; }
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

        /// <summary>The actor's PREFERRED floor set — null until
        /// walkableMaterials is painted with MAT_* ids. Designation is a
        /// flood-cost preference (stamped tiles are cheaper), never a gate:
        /// "can" is physics, "should" is routing. Fed to BuildDistMap and the
        /// walkable-viz tint — the domain itself no longer consults it.</summary>
        HashSet<int> WalkMask => walkableMaterials != null && walkableMaterials.Length > 0
            ? (walkMaskSet ??= new HashSet<int>(walkableMaterials)) : null;

        /// <summary>Body height the fit column scales to — same source
        /// CorridorFree uses.</summary>
        float BodyH => vox != null ? vox.WorldSize.y : 1.8f;
        /// <summary>The character's ground signature — ONE square column the
        /// size of the body's wider axis. The field certifies cell blocks of
        /// it; the mover and corridor pull test the same square — agreement
        /// between map and body by construction.</summary>
        // The mover's square = the SAME square the field certifies — owned by
        // VoxelCharacter.BodyFootprint (0-override → measured dims). One spec,
        // identical verdicts: the map can never certify what the body isn't.
        float BodySq => vox != null ? vox.BodyFootprint : 0.6f;
        float BodySqHalf => BodySq * 0.5f;

        /// <summary>Debug-viz read surface — the sensor's heat quad bakes the
        /// Walkable/Reachable layers through the actor's own designation +
        /// flood (the field holds channels; these are his).</summary>
        public System.Collections.Generic.ICollection<int> DebugWalkMask => WalkMask;
        public float DebugBodyH => BodyH;
        public float DebugBodySq => BodySq;
        /// <summary>Certification is continuous now — the field certifies the
        /// identical square the mover sweeps, so this reads DebugBodySq and
        /// the HUD's second outline self-hides. Kept so a future discrete
        /// layer that diverges from the body would re-surface here.</summary>
        public float DebugCertMeters => BodySq;
        public CoverField.DistMap DebugMeMap => MeMap();

        /// <summary>Cell center at the cell's OWN probed ground — delegates to
        /// the field (CellCenter carries the uniform scan GroundY, which
        /// buries feet samples inside the slab on two-level fields).</summary>
        Vector3 CellGroundPoint(CoverField f, int cx, int cz)
            => f.GroundPoint(cz * f.N + cx);

        /// <summary>The actor-seeded distance map — "what can I reach and at
        /// what real cost". One flood per field Build (and per seed-cell
        /// move); picks, orbit hops, relocation and route building all read
        /// it. Null when no walkable cell can host him.</summary>
        CoverField.DistMap MeMap()
        {
            var f = sensor?.Field;
            if (f == null || !f.HasData) return null;
            int s = f.NearestWalkable(f.CellIndexOf(Feet()), BodyH);
            if (s < 0) return null;
            if (meMap == null || meMapStamp != f.BuildStamp || meMapSeed != s)
            {
                meMap ??= new CoverField.DistMap();
                f.BuildDistMap(meMap, s, WalkMask, BodyH, stepUpMax);
                meMapSeed = s; meMapStamp = f.BuildStamp;
            }
            return meMap;
        }

        /// <summary>The belief-seeded distance map — geodesic distance FROM
        /// the search anchor (the threat's believed spot). Rebuilt when the
        /// anchor moves (a heard shot re-arms it). Used to score how far a
        /// hop REALLY is from him — around walls, not through them.</summary>
        CoverField.DistMap AnchorMap()
        {
            var f = sensor?.Field;
            if (f == null || !f.HasData) return null;
            int s = f.NearestWalkable(f.CellIndexOf(searchAnchorPos), BodyH);
            if (s < 0) return null;
            if (anchorMap == null || anchorMapStamp != f.BuildStamp || anchorMapSeed != s)
            {
                anchorMap ??= new CoverField.DistMap();
                f.BuildDistMap(anchorMap, s, WalkMask, BodyH, stepUpMax);
                anchorMapSeed = s; anchorMapStamp = f.BuildStamp;
            }
            return anchorMap;
        }

        /// <summary>Feet+chest SamplePoint clearance along a segment — the
        /// string-pull's straightness check, identical semantics to
        /// MoveWithCollision.</summary>
        bool CorridorFree(Vector3 a, Vector3 b)
        {
            if (world == null) return true;
            if (float.IsNaN(a.y) || float.IsNaN(b.y)) return false;   // no-ground cells never corridor
            float h = vox != null ? vox.WorldSize.y : 1.8f;
            // STEP transition: endpoints on different decks within step range —
            // sample the whole run at the UPPER level (the climb happens AT the
            // boundary face; a lerp would march the samples through the step's
            // side and reject a legal climb). Larger Δy keeps the lerp — tall
            // faces still reject.
            float yFlat = Mathf.Abs(a.y - b.y) <= stepUpMax
                ? Mathf.Max(a.y, b.y) : float.NaN;
            // A pull is legal iff the WHOLE square can slide a→b — the
            // shared SquareFree at every interpolation step: the identical
            // verdict the mover executes, so a certified corridor can't
            // refuse the body mid-leg (a post on the square's forward edge
            // used to slip between the old 3 perpendicular columns).
            // Spacing ≤ the mover's own frame step (~0.03m): coarser sampling
            // straddles a convex corner VERTEX — certifies a leg whose
            // square catches the vertex mid-slide, the exact arena wedge.
            Vector3 d = b - a; float len = d.magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.03f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = a + d * (i / (float)steps);
                if (!float.IsNaN(yFlat)) p.y = yFlat;
                if (!world.SquareFree(p, BodySqHalf, h)) return false;
            }
            return true;
        }

        /// <summary>The leg materializer: the actor map's next-hop chain IS
        /// the corridor (one flood per Build already answered reachability —
        /// a second per-leg flood would re-derive the same truth). Reaches()
        /// gives the unreachable verdict up front — "map can't serve it" is
        /// the fenced diagnosis now. String-pull unchanged: from each anchor,
        /// take the FARTHEST cell a voxel-clear corridor reaches; last
        /// waypoint = the exact stand point.</summary>
        bool BuildRoute(Vector3 from, Vector3 to)
        {
            legRoute.Clear(); legRouteIdx = 0;
            var f = sensor?.Field;
            if (!routeLegs || f == null || world == null || !f.HasData) return false;
            int n = f.N;
            int s0 = f.CellIndexOf(from), d0 = f.CellIndexOf(to);
            if (s0 < 0 || d0 < 0) { routeFail = $"cell-of fail (s{s0} d{d0})"; return false; }
            var map = MeMap();
            if (map == null) { routeFail = "no walkable cell at from"; return false; }
            int d = f.NearestWalkable(d0, BodyH);
            if (d < 0) { routeFail = "no walkable cell at to"; return false; }
            if (!map.Reaches(d))
            { routeFail = $"map unreachable — flood serves {map.ReachedCount}/{n * n} cells"; return false; }
            // The map's next-hops from d lead back to the feet seed — reverse
            // for the travel order (identical shape to the old prev[] walk).
            chainScratch.Clear();
            f.ChainToSeed(map, d, chainScratch);
            chainScratch.Reverse();
            Vector3 anchor = from;
            int i = 0;
            while (i < chainScratch.Count)
            {
                int j = chainScratch.Count - 1;
                for (; j > i; j--)
                    if (CorridorFree(anchor, CellGroundPoint(f, chainScratch[j] % n, chainScratch[j] / n))) break;
                Vector3 p = j == chainScratch.Count - 1 ? to
                    : CellGroundPoint(f, chainScratch[j] % n, chainScratch[j] / n);
                legRoute.Add(p); anchor = p; i = j + 1;
            }
            return legRoute.Count > 0;
        }

        /// <summary>Step the ROUTED leg — waypoint chain instead of the
        /// straight ray. Same contract as StepToward on the final point:
        /// true = arrived at finalDest or the corridor aborted (callers
        /// re-verify distance).</summary>
        bool StepRoute(string who, Vector3 finalDest)
        {
            if (legRouteIdx < legRoute.Count)
            {
                Vector3 wp = legRoute[legRouteIdx];
                if (!StepToward(wp, who)) return false;
                if (legRouteIdx == legRoute.Count - 1) return true;   // final = dest: arrive/abort
                Vector3 d = wp - Feet(); d.y = 0f;
                if (d.magnitude <= arriveEps + 0.04f)
                {
                    legRouteIdx++;   // fresh nudge budget per segment
                    swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0; stuckBest = float.MaxValue;
                    return false;
                }
                return true;   // waypoint aborted → corridor blocked → leg report
            }
            return StepToward(finalDest, who);
        }

        /// <summary>Every point move consumes the map's corridor — the same
        /// chain + string-pull the seek legs walk — so a corner the flood
        /// routes AROUND is a corner the mover rounds, not clips into. The
        /// *Built latch materializes once per leg (reset where the leg's
        /// step-guard resets); falls back to the bare StepToward line only
        /// when the map can't serve (routeLegs off / no field / unreached
        /// dest — e.g. an out-of-field swing rim) — the pre-map contract.</summary>
        bool StepRouted(Vector3 dest, string who, ref bool built)
        {
            var fld = sensor?.Field;
            // Stale-route guard — the field rebuilds under threat motion /
            // voxel changes; a materialized leg re-routes from the feet when
            // its cert world has moved (else the actor walks a ghost map).
            if (!built || (routedLeg && fld != null && fld.BuildStamp != routeStamp))
            {
                built = true;
                routedLeg = BuildRoute(Feet(), dest);
                routeStamp = fld != null ? fld.BuildStamp : -1;
                if (!routedLeg && routeLegs)
                    Debug.Log($"⚠️ [Combat] {name} — {who}: no corridor to step-dest ({routeFail}) — bare line");
            }
            return routedLeg ? StepRoute(who, dest) : StepToward(dest, who);
        }

        /// <summary>Bearing of `p` orbiting `origin` — the search's orbit
        /// frame around lastSeen (deg, Unity compass: atan2(dx, dz)).</summary>
        static float BearingAround(Vector3 origin, Vector3 p)
            => Mathf.Atan2(p.x - origin.x, p.z - origin.z) * Mathf.Rad2Deg;

        /// <summary>Vision-cone coverage of a point — the cone is THE
        /// detection contract: geometric unocclusion outside the posed eye
        /// fan is not contact. No sensor → no cone gate (bare testbeds keep
        /// geometric sight).</summary>
        bool ConeCovers(Vector3 point)
        {
            if (vis == null) return true;
            return vis.Covers(point);   // one definition — the posed eye fan owns it
        }

        /// <summary>One step along the search ORBIT (PERCEPTION_AWARENESS →
        /// Search & resolution — collapsed doctrine): the goal is ~90° of
        /// bearing around the belief anchor on the rolled side, reached
        /// through HOWEVER MANY Hidden-zone hops the geometry offers —
        /// across the street, around the block, overshoot is legal.
        /// Candidates: Hidden under live belief, unvisited, cover face
        /// FACING AWAY from the anchor (outDir = the wall's outward normal
        /// INTO the stand area — coherent cover has it parallel to
        /// anchor→stand, dot > +0.3; a zone hidden by accident rather than
        /// by its own wall is fragile cover), and advancing the arc —
        /// the buffer ring is the HOT PATH:
        /// the pick scores |stand-to-anchor distance − orbit radius| +
        /// travel distance (hops at ANY depth are legal, ring-hugging is
        /// preferred; the flow-field path cost is the declared upgrade).
        /// Every `orbitReconEvery`th leg is a RECON (lands the full peek
        /// loop); the rest are TRANSIT (land → brief settle → next step).
        /// False = the arc exhausted or the flank already satisfied (≥90°)
        /// → the caller abandons via ExitCombat.</summary>
        bool TryOrbitStep()
        {
            if (sensor == null || sensor.Field == null || !searchActive)
                return false;
            if (searchProgress >= 90f)
            {
                Debug.Log($"🥊 [Combat] {name} — orbit satisfied: flank {searchProgress:F0}° resolved");
                return false;
            }
            Vector3 ls = searchAnchorPos, feet = Feet();
            sensor.Field.EnumerateZones(zoneScratch);
            // The maps answer reachability + real cost — zones the actor map
            // can't serve are not candidates (no more commit-then-corridor-
            // dies); the anchor map gives geodesic ring distance (around
            // walls, not through them), Euclidean when the anchor can't serve.
            var me = MeMap(); var anchor = AnchorMap();
            // Two passes: prefer a ≥15° stride (no micro-hops), then any
            // forward progress at all — a map may only offer slivers.
            for (int pass = 0; pass < 2; pass++)
            {
                float minStep = pass == 0 ? 15f : 0.01f;
                CoverField.ZoneSpot best = default;
                float bestCost = float.MaxValue, bestProg = 0f;
                for (int i = 0; i < zoneScratch.Count; i++)
                {
                    var z = zoneScratch[i];
                    if (searchVisited.Contains(z.cell)) continue;
                    if (sensor.Field.state[z.cell] != CoverField.CellState.Hidden) continue;
                    if (me != null && !sensor.Field.ZoneReachable(z.cell, me)) continue;
                    float ax = z.stand.x - ls.x, az = z.stand.z - ls.z;
                    float dAnchor = Mathf.Sqrt(ax * ax + az * az);
                    // Threat-direction relevancy: outDir is the wall face's
                    // outward normal INTO the stand area — coherent cover
                    // has the stand on the far side of the wall from the
                    // anchor, so outDir ≈ anchor→stand (dot > +0.3). A zone
                    // hidden by a third party or sideways to the threat
                    // line isn't a coherent step in a cover chain.
                    Vector2 aDir = new Vector2(ax, az).normalized;
                    if (Vector2.Dot(z.outDir, aDir) < 0.3f) continue;
                    float dx = z.stand.x - feet.x, dz = z.stand.z - feet.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < 0.5f) continue;
                    float prog = orbitSide * Mathf.DeltaAngle(searchOriginBearing,
                        BearingAround(ls, z.stand));
                    if (prog <= searchProgress + minStep) continue;
                    // Hot path: the ring is the score attractor — prefer the
                    // stand closest to the orbit radius, travel cost as the
                    // tiebreak. Both terms are geodesic when the maps serve:
                    // ring distance from the anchor map, travel cost from the
                    // actor map (detours priced, not assumed).
                    float ringD = anchor != null && anchor.Reaches(z.cell)
                        ? anchor.CostFrom(z.cell) : dAnchor;
                    float travel = me != null && me.Reaches(z.cell)
                        ? me.CostFrom(z.cell) : d;
                    float cost = Mathf.Abs(ringD - searchBufferRadius) + travel * 0.15f;
                    if (cost < bestCost) { bestCost = cost; best = z; bestProg = prog; }
                }
                if (bestCost < float.MaxValue)
                {
                    searchVisited.Add(best.cell);
                    float step = bestProg - searchProgress;
                    searchProgress = bestProg;
                    orbitHops++;
                    // The cadence: every Nth leg is recon — it lands and
                    // runs the peek; transit legs relay straight through.
                    orbitLegRecon = orbitHops % orbitReconEvery == 0;
                    CommitSearchSeek(best,
                        $"orbit {searchProgress:F0}°/90 ({(orbitSide < 0 ? "CW" : "CCW")} → {best.volId} · +{step:F0}°, score {bestCost:F1} · {(orbitLegRecon ? "recon" : "transit")})");
                    return true;
                }
            }
            // The arc exhausted — report the per-gate rejection census so a
            // give-up names its killer instead of going silent.
            {
                int nVis = 0, nHid = 0, nUnr = 0, nFace = 0, nArc = 0, nClose = 0;
                for (int i = 0; i < zoneScratch.Count; i++)
                {
                    var z = zoneScratch[i];
                    if (searchVisited.Contains(z.cell)) { nVis++; continue; }
                    if (sensor.Field.state[z.cell] != CoverField.CellState.Hidden) { nHid++; continue; }
                    if (me != null && !sensor.Field.ZoneReachable(z.cell, me)) { nUnr++; continue; }
                    float ax = z.stand.x - ls.x, az = z.stand.z - ls.z;
                    Vector2 aDir = new Vector2(ax, az).normalized;
                    if (Vector2.Dot(z.outDir, aDir) < 0.3f) { nFace++; continue; }
                    float dx = z.stand.x - feet.x, dz = z.stand.z - feet.z;
                    if (dx * dx + dz * dz < 0.25f) { nClose++; continue; }
                    if (orbitSide * Mathf.DeltaAngle(searchOriginBearing,
                        BearingAround(ls, z.stand)) <= searchProgress) { nArc++; continue; }
                }
                Debug.Log($"🥊 [Combat] {name} — orbit exhausted at {searchProgress:F0}°/90 ({(orbitSide < 0 ? "CW" : "CCW")}): " +
                    $"{zoneScratch.Count} zones → visited {nVis} · notHidden {nHid} · unreachable {nUnr} · wrongFace {nFace} · noAdvance {nArc} · tooClose {nClose}");
            }
            return false;
        }

        /// <summary>Nearest usable zone to current feet — the "park local
        /// cover and restart engagement there" half of the mid-hop sighting
        /// roll. Re-targets the live seek; false when no Hidden zone exists.</summary>
        bool TryNearestCover()
        {
            if (sensor == null || sensor.Field == null) return false;
            sensor.Field.EnumerateZones(zoneScratch);
            var me = MeMap();
            CoverField.ZoneSpot best = default;
            float bestD = float.MaxValue;
            for (int i = 0; i < zoneScratch.Count; i++)
            {
                var z = zoneScratch[i];
                if (sensor.Field.state[z.cell] != CoverField.CellState.Hidden) continue;
                if (me != null && !sensor.Field.ZoneReachable(z.cell, me)) continue;
                float dx = z.stand.x - Feet().x, dz = z.stand.z - Feet().z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { best = z; bestD = d; }
            }
            if (bestD == float.MaxValue) return false;
            CommitSearchSeek(best, $"sighted — park local cover → {best.volId}");
            return true;
        }

        /// <summary>The search-seek's posture claim ends — scout flag down
        /// and the pre-search lookScan restored (whatever the user/HUD had
        /// it set to before the hop took it over).</summary>
        void EndSearchSeek()
        {
            if (!searching) return;
            searching = false; searchCovered = false;
            if (anim != null) anim.lookScan = savedLookScan;
        }

        /// <summary>The map's answer to "nearest standable spot to this
        /// painted point": a committed stand whose own cell fails the body's
        /// FootprintOK cert (skin erosion, seam lip — the painted centroid
        /// averages certified AND uncertified cells) snaps to the nearest
        /// walkable ring-1 cell. Commit to what the flood certifies, not what
        /// the paint averaged — else every leg dies ~a cell short of the
        /// point. Identity when certified or no field.</summary>
        Vector3 CertifiedStand(Vector3 p)
        {
            var f = sensor?.Field;
            if (f == null || !f.HasData) return p;
            int c0 = f.CellIndexOf(p);
            if (c0 < 0) return p;
            int d = f.NearestWalkable(c0, BodyH);
            return (d >= 0 && d != c0) ? f.GroundPoint(d) : p;
        }

        /// <summary>Shared search commit — same atomic shape as ScriptedGoTo:
        /// point + posture + beat hygiene, then the SeekingCover leg.</summary>
        void CommitSearchSeek(CoverField.ZoneSpot z, string diag)
        {
            CoverPoint = CertifiedStand(z.stand); CoverObstacleId = z.volId;
            targetDuck = z.reqD; coverWallClass = z.cls;
            Beat = EngagementBeat.None; afterSettle = EngagementBeat.None;
            loopGate = false; buriedSince = 0f; pinned = false;
            if (anim != null)
            {
                savedLookScan = anim.lookScan;
                anim.coverContext = false;
                anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                anim.walkSpeed = Mathf.Clamp(moveSpeed, 0.5f, 2f);
            }
            searching = true; Relocating = true;
            legRouteTarget = new Vector3(float.NaN, 0f, 0f);   // force the flood to rebuild for this commit
            riseRefusedAt = legRouteTarget; boreBuriedSince = -1f;   // new stand = fresh refusal accounting
            LastRiseDiag = diag; LastRelocateDiag = diag;
            Debug.Log($"🥊 [Combat] {name} — {diag}");
            if (State != CombatState.SeekingCover) SetState(CombatState.SeekingCover);
            else { coverArrived = false; swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0; stuckBest = float.MaxValue; }
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
            // Mark the face that just failed — THE single nearest mark on his
            // obstacle, the one the rise solved against. (The old loop added
            // every successively-nearer mark — order-dependent, could fail
            // ALL the obstacle's faces at once and starve the re-pick pool.
            // The 2m gate matches SolveRiseDuck's "no wall mark" cap.)
            float bd = float.MaxValue; CoverSensor.WallMark wFail = default; bool hasFail = false;
            foreach (var w in sensor.walls)
            {
                if (w.volId != CoverObstacleId) continue;
                float d2 = (w.facePos - CoverPoint).sqrMagnitude;
                if (d2 < bd) { bd = d2; wFail = w; hasFail = true; }
            }
            if (hasFail && bd <= 4f) failedFaces.Add(FaceKey(wFail));

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
            var me = MeMap();
            sensor.Field.EnumerateZones(zonesScratch);
            foreach (var z in zonesScratch)
            {
                // The map's verdict: a zone no corridor can reach is not a
                // relocation candidate (the corridor-dead livelock class dies
                // at pick time instead of post-commit).
                if (me != null && !sensor.Field.ZoneReachable(z.cell, me)) continue;
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
                        out _, out _) < 0f) continue;
                float d2 = (spot - transform.position).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = z; bestReq = rd; found = true; }
            }
            if (!found)
            {
                LastRelocateDiag = $"refused — {failedFaces.Count} face(s) failed, none fightable";
                LastRiseDiag += " — no fightable cover";
                return false;
            }

            CoverPoint = CertifiedStand(best.stand); CoverObstacleId = best.volId;
            targetDuck = bestReq; coverWallClass = best.cls;
            Beat = EngagementBeat.None; afterSettle = EngagementBeat.None;
            loopGate = false; buriedSince = 0f; pinned = false; Relocating = true;
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

        /// <summary>Program completion → verdict → return to the seam.
        /// The verdict is data the next eval reads; the program itself never
        /// picks its successor.</summary>
        void SeamDone(string verdict)
        {
            arbVerdict = verdict;
            arbHist = $"t{Time.unscaledTime:F1} {arbRunning} → {verdict}" +
                (arbHist.Length > 0 ? "  ·  " + arbHist : "");
            if (arbHist.Length > 400) arbHist = arbHist.Substring(0, 400);
            Debug.Log($"🥊 [Combat] {name} — seam verdict: {verdict}");
            Notify();
            EnterBeat(EngagementBeat.Settle);
        }

        /// <summary>The seam — Tab F's arbiter. Runs where NO program is
        /// running (Beat == Settle breath, Beat == None parked, or the
        /// HoldPosition park): score every intent on the live blackboard,
        /// dispatch the winner's verb through the real machinery, record the
        /// program. A failed dispatch is itself a verdict — suppress and take
        /// the next-best, bounded; nothing dispatchable = PinnedGate.</summary>
        void SeamEval()
        {
            if (reloading)
            {   // A RELOAD program owns the wait wherever it was started.
                arbHoldUntil = Time.unscaledTime + 0.4f;
                arbRunning = "RELOAD";
                return;
            }
            seamNow = false;
            // ── Blackboard — every term is a real controller signal. ──
            bool contact  = contactSeen;
            bool threat   = ThreatCharacter != null;
            bool covered  = sensor != null && sensor.TryZoneAt(Feet(), out _, out _);
            bool standable = State == CombatState.InCover;
            bool probeDead = standable && probeDeadAt == CoverPoint;
            bool fireDead  = standable && fireDeadAt  == CoverPoint;
            bool orbitLive = searchActive && !orbitDone;
            float cyl = cylinderCapacity > 0 ? (float)cylinder / cylinderCapacity : 1f;
            float stale = threat
                ? Time.unscaledTime - Mathf.Max(0f, LastSeenAt)
                : 999f;
            arbBoard = $"{(contact ? "◉contact" : threat ? $"◬lk {stale:F0}s" : "·idle")}" +
                $" · {(covered ? "cover" : "EXPOSED")}" +
                $" · cyl {cylinder}/{cylinderCapacity}" +
                $" · zero×{zeroStreak}" +
                (orbitLive ? $" · orbit {searchProgress:F0}°" : "") +
                (orbitDone ? " · arc-done" : "") +
                (probeDead ? " · probe-dead" : "");
            // ── Scores — weights from the lab, driven by the board. ──
            var s = arbScores;
            for (int i = 0; i < s.Length; i++) s[i] = 0f;
            s[8] = 0.12f;                                                  // Watch — ambient floor
            s[9] = 0.01f;                                                  // PinnedGate — fallback only
            s[0] = !covered ? (contact ? 0.9f : 0.7f) : 0.03f;             // SeekCover
            s[1] = covered ? (probeDead ? 0.55f : (!contact ? 0.25f : 0.05f)) : 0.05f;  // Relocate
            s[2] = contact ? (probeDead ? 0.65f : (covered ? 0.15f : 0.3f)) : 0f;       // Engage — MOVE to fightable
            s[3] = threat && !contact && orbitLive
                 ? 0.4f + 0.1f * Mathf.Min(zeroStreak, 3) : 0f;            // OrbitSearch — arc subroutine
            s[4] = threat && !contact && standable && !probeDead
                 ? 0.4f + Mathf.Min(stale * 0.05f, 0.3f) + (seamReconBoost ? 0.35f : 0f) : 0f;  // Recon → PROBE
            s[5] = threat && !contact && orbitDone && standable ? 0.9f : 0f;              // FinalConfirm
            s[6] = cyl < 0.999f
                 ? (0.15f + 0.7f * (1f - cyl)) * (contact && !covered ? 0.45f : 1f) : 0f; // Reload
            s[7] = contact && covered && !probeDead ? 0.85f : 0f;          // StandDeliver → HOLD(armed)
            seamReconBoost = false;
            if (holdInCover) { for (int i = 0; i < 8; i++) s[i] = 0f; s[8] = 1f; }
            // ── Argmax with bounded fallback — a refused dispatch suppresses
            //    this round; exhaustion parks at the pin gate. ──
            for (int tries = 0; tries < 4; tries++)
            {
                int best = -1; float bw = 0f;
                for (int i = 0; i < s.Length; i++)
                    if (s[i] > bw) { bw = s[i]; best = i; }
                if (best < 0 || bw <= 0f) break;
                if (ArbDispatch(best)) { Notify(); return; }
                s[best] = 0f;
            }
            ArbPark();
        }

        /// <summary>The verb dispatch — MOVE commits legs through the
        /// certified-cover path, PROBE enters the Rise program (whose refusal
        /// internally selects the Swing mode), RELOAD/HOLD are beat programs,
        /// Watch/PinnedGate park the seam. Returns false when the intent has
        /// no dispatchable target this eval.</summary>
        bool ArbDispatch(int i)
        {
            switch (i)
            {
                case 0: if (!ArbMoveHidden(false, ArbNames[0])) return false;
                        arbRunning = "MOVE(SeekCover)";  return true;
                case 1: if (!ArbMoveHidden(true, ArbNames[1]))  return false;
                        arbRunning = "MOVE(Relocate)";   return true;
                case 2: if (!ArbMoveFightable())               return false;
                        arbRunning = "MOVE(Engage)";     return true;
                case 3:                                                    // OrbitSearch
                    if (!searchActive) ArmSearch();
                    pendingSearch = false;
                    if (TryOrbitStep())
                    { arbRunning = "MOVE(OrbitSearch)"; return true; }
                    orbitDone = true;                                      // arc spent → FinalConfirm arms
                    return false;
                case 4:                                                    // Recon → PROBE
                    if (State != CombatState.InCover) return false;
                    orbitFinalLook = false;
                    EnterBeat(EngagementBeat.Rise);
                    arbRunning = "PROBE(Recon)"; return true;
                case 5:                                                    // FinalConfirm — terminal look
                    if (State != CombatState.InCover) return false;
                    orbitFinalLook = true;
                    EnterBeat(EngagementBeat.Rise);
                    arbRunning = "PROBE(FinalConfirm)"; return true;
                case 6:                                                    // Reload
                    if (cylinder >= cylinderCapacity) return false;
                    if (State == CombatState.InCover) EnterBeat(EngagementBeat.TopUp);
                    else
                    {   // Parked in the open — reload rides the physical
                        // channel; the HoldPosition seam waits it out.
                        StartReload();
                        EnterBeat(EngagementBeat.None);
                        arbHoldUntil = Time.unscaledTime + 1f;
                    }
                    arbRunning = "RELOAD"; return true;
                case 7:                                                    // StandDeliver → HOLD(armed)
                    if (State == CombatState.InCover)
                    { EnterBeat(EngagementBeat.Engaged); }
                    else
                    {   // Standing in the open — the hold IS the program;
                        // the contact overlay does the firing.
                        EnterBeat(EngagementBeat.None);
                        arbHoldUntil = Time.unscaledTime + 1.2f;
                    }
                    arbRunning = "HOLD(StandDlv)"; return true;
                case 8:                                                    // Watch — park + recheck cadence
                    EnterBeat(EngagementBeat.None);
                    arbHoldUntil = Time.unscaledTime + (contactSeen ? 0.8f : 1.6f);
                    arbRunning = "HOLD(Watch)"; return true;
                case 9:                                                    // PinnedGate
                    pinned = true; pinUntil = Time.unscaledTime + pinRecheckSec;
                    EnterBeat(EngagementBeat.None);
                    arbHoldUntil = pinUntil;
                    arbRunning = "HOLD(PinnedGate)"; return true;
            }
            return false;
        }

        /// <summary>Nothing dispatchable — the pin gate parks him on the
        /// existing pinned machinery (slow re-verdict clock).</summary>
        void ArbPark()
        {
            pinned = true; pinUntil = Time.unscaledTime + pinRecheckSec;
            arbHoldUntil = pinUntil;
            if (State == CombatState.InCover || State == CombatState.HoldPosition)
                EnterBeat(EngagementBeat.None);
            arbRunning = "HOLD(PinnedGate)";
            arbVerdict = "no dispatchable intent — gated";
            Notify();
        }

        /// <summary>MOVE's hidden-zone pick — SeekCover takes the nearest
        /// (current stand legal), Relocate excludes the zone he's standing in.
        /// Aborted-leg zones are excluded for the episode.</summary>
        bool ArbMoveHidden(bool excludeCurrent, string why)
        {
            var f = sensor?.Field;
            if (f == null || !f.HasData) return false;
            f.EnumerateZones(zoneScratch);
            var me = MeMap();
            int curCell = -1;
            int ci = f.CellIndexOf(Feet());
            if (ci >= 0 && f.TryZoneOfCell(ci, out var own)) curCell = own.cell;
            CoverField.ZoneSpot best = default; float bestD = float.MaxValue;
            Vector3 feet = Feet();
            for (int i = 0; i < zoneScratch.Count; i++)
            {
                var z = zoneScratch[i];
                if (f.state[z.cell] != CoverField.CellState.Hidden) continue;
                if (me != null && !f.ZoneReachable(z.cell, me)) continue;
                if (arbBlocked.Contains(z.cell)) continue;
                if (excludeCurrent && z.cell == curCell) continue;
                float dx = z.stand.x - feet.x, dz = z.stand.z - feet.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < 0.25f) continue;
                if (d2 < bestD) { bestD = d2; best = z; }
            }
            if (bestD == float.MaxValue) return false;
            lastMoveCell = best.cell;
            CommitSearchSeek(best, $"arbiter — {why} → {best.volId}");
            return true;
        }

        /// <summary>MOVE's fightable-zone pick — the Engage intent: a Hidden
        /// zone whose OWN rise solve clears (stand-and-deliver capable), the
        /// brave advance. Same law TryRelocateToFightingCover enforces, minus
        /// the failure-mark — this is a voluntary advance, not a refusal.</summary>
        bool ArbMoveFightable()
        {
            var f = sensor?.Field;
            if (f == null || !f.HasData || sensor.walls == null) return false;
            Vector3? tgt = LastSeenTarget;
            if (!tgt.HasValue && ThreatCharacter != null)
                tgt = ThreatCharacter.transform.position + Vector3.up * 0.5f;
            var me = MeMap();
            f.EnumerateZones(zonesScratch);
            int curCell = -1;
            int ci = f.CellIndexOf(Feet());
            if (ci >= 0 && f.TryZoneOfCell(ci, out var own)) curCell = own.cell;
            CoverField.ZoneSpot best = default; float bestD2 = float.MaxValue; bool found = false;
            foreach (var z in zonesScratch)
            {
                if (z.cell == curCell || arbBlocked.Contains(z.cell)) continue;
                if (f.state[z.cell] != CoverField.CellState.Hidden) continue;
                if (me != null && !f.ZoneReachable(z.cell, me)) continue;
                float md = float.MaxValue; CoverSensor.WallMark m = default; bool hasMark = false;
                foreach (var w in sensor.walls)
                {
                    if (w.volId != z.volId) continue;
                    if (w.outDir.x * z.outDir.x + w.outDir.z * z.outDir.y < 0.999f) continue;
                    float d2m = (w.facePos - z.stand).sqrMagnitude;
                    if (d2m < md) { md = d2m; m = w; hasMark = true; }
                }
                if (!hasMark || md > 4f || failedFaces.Contains(FaceKey(m))) continue;
                if (!sensor.TryZoneAt(z.stand, out _, out float rd)) continue;
                float yaw = tgt.HasValue
                    ? Mathf.Atan2(tgt.Value.x - z.stand.x, tgt.Value.z - z.stand.z) * Mathf.Rad2Deg
                    : Mathf.Atan2(-m.outDir.x, -m.outDir.z) * Mathf.Rad2Deg;
                if (SolveRiseAtSpot(z.stand, yaw, m.top, tgt, out _, out _) < 0f) continue;
                float d2 = (z.stand - Feet()).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = z; found = true; }
            }
            if (!found) return false;
            lastMoveCell = best.cell;
            CommitSearchSeek(best, $"arbiter — Engage → {best.volId}");
            return true;
        }

        /// <summary>Mark the current stand's face dead — the buried-bore
        /// verdict's bookkeeping write (the same mark TryRelocate stamps).</summary>
        void MarkFaceFailed()
        {
            if (CoverObstacleId == null || sensor?.walls == null) return;
            float bd = float.MaxValue; CoverSensor.WallMark wFail = default; bool hasFail = false;
            foreach (var w in sensor.walls)
            {
                if (w.volId != CoverObstacleId) continue;
                float d2 = (w.facePos - CoverPoint).sqrMagnitude;
                if (d2 < bd) { bd = d2; wFail = w; hasFail = true; }
            }
            if (hasFail && bd <= 4f) failedFaces.Add(FaceKey(wFail));
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
                    if (ArbActive)
                    {   // THE SEAM — dwell floor or interrupt → evaluate.
                        if (seamNow || Time.unscaledTime >= beatUntil) SeamEval();
                        break;
                    }
                    if (Time.unscaledTime >= beatUntil)
                    {
                        // Hide-only: the seam IS the destination — he sought
                        // cover and now holds it. Parks forever, arm stowed.
                        if (holdInCover) { EnterBeat(EngagementBeat.Settle); break; }
                        // TRANSIT seam — the landed leg wasn't a recon stop;
                        // issue the next orbit step straight off this seam
                        // (no peek — the cadence says he's still moving).
                        if (transitPending)
                        {
                            transitPending = false;
                            if (!TryOrbitStep()) FinalLookOrExit();
                            break;
                        }
                        // SEARCH seam — a miss armed the driver; the next
                        // orbit step issues from here (or the search abandons
                        // to ExitCombat when the arc is exhausted / the flank
                        // angle is satisfied).
                        if (pendingSearch)
                        {
                            pendingSearch = false;
                            if (!TryOrbitStep()) FinalLookOrExit();
                            break;
                        }
                        // PINNED seam — every exit failed at this stand; park
                        // at the gate. Beat.None re-arms one rhythm cycle
                        // every pinRecheckSec — a REAL rise = a real probe,
                        // so a moved threat or cleared bore wakes it.
                        if (pinned) { EnterBeat(EngagementBeat.None); break; }
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
                    if (ArbActive)
                    {
                        if (reloading) break;              // RELOAD's wait owns the park
                        if (pinned && Time.unscaledTime >= pinUntil)
                        { pinned = false; arbVerdict = "pin recheck"; EnterBeat(EngagementBeat.Settle); }
                        else if (seamNow || Time.unscaledTime >= arbHoldUntil)
                            EnterBeat(EngagementBeat.Settle);
                        break;
                    }
                    // Parked at the re-cycle gate — a live toggle-ON re-arms
                    // the rhythm from the seam, same as a fresh cycle.
                    if (awaitingLoop && loopBeats)
                    { afterSettle = EngagementBeat.TopUp; EnterBeat(EngagementBeat.Settle); }
                    // Parked PINNED — the re-verdict clock fired: release one
                    // rhythm cycle. Still unfightable = the refusal path
                    // re-pins for another pinRecheckSec.
                    else if (pinned && Time.unscaledTime >= pinUntil)
                    {
                        pinned = false;
                        Debug.Log($"🥊 [Combat] {name} — pin re-check: testing the stand again");
                        afterSettle = EngagementBeat.TopUp; EnterBeat(EngagementBeat.Settle);
                    }
                    break;
                case EngagementBeat.TopUp:
                    if (cylinder < cylinderCapacity && !reloading) StartReload();
                    // Reload starts instantly (physical); the beat LEAVES on
                    // the tempo floor + a full cylinder.
                    if (!reloading && Time.unscaledTime >= beatUntil)
                    {
                        if (ArbActive) SeamDone("loaded");
                        else { afterSettle = EngagementBeat.Rise; EnterBeat(EngagementBeat.Settle); }
                    }
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
                            orbitFinalLook = false;   // the look did its job — found him
                            if (ArbActive)
                            {   // Interrupt verdict — zero-dwell re-eval;
                                // StandDeliver wins the seam on contact.
                                LastSeenTarget = ThreatFeet(); LastSeenAt = Time.unscaledTime;
                                ThreatPos = LastSeenTarget.Value;
                                contactSeen = true;      // the crest probe IS the contact determination
                                zeroStreak = 0; arbVerdict = "contact at crest";
                                SeamEval(); break;
                            }
                            LastRiseDiag = "contact at stand → Engaged";
                            EnterBeat(EngagementBeat.Engaged); break;
                        }
                        // The terminal-stand confirming look found nothing —
                        // the flank angle is clear of him; the search ends.
                        if (orbitFinalLook)
                        {
                            orbitFinalLook = false;
                            LastRiseDiag = "final look — flank clear of him, abandoning";
                            Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                            ExitCombat(true); return;
                        }
                        LastRiseDiag = "risen — zero contact (target occluded from this stand)";
                        Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                        // A peek that finds nothing is the same MISS as the
                        // Engaged drop — arm the search so the Settle seam
                        // branches into the hop (this is ALSO how a fight
                        // where he was never seen at all begins searching).
                        ArmSearch();
                        if (ArbActive)
                        { zeroStreak++; arbVerdict = "zero contact @crest"; }
                        else afterSettle = EngagementBeat.TopUp;
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
                    {
                        if (ArbActive) { arbVerdict = "cylinder dry"; }
                        else afterSettle = EngagementBeat.TopUp;
                        EnterBeat(EngagementBeat.Duck);
                    }
                    else if (buriedSince >= boreBurySec)
                    {
                        buriedSince = 0f;
                        LastRiseDiag = "bore buried at the wall — real muzzle ray blocked";
                        Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                        if (ArbActive)
                        {   // The stand can't fight — the face-mark is the
                            // verdict's bookkeeping; the seam picks the reply
                            // (Engage/Relocate → a stand that can fight).
                            MarkFaceFailed();
                            arbVerdict = "bore buried";
                            EnterBeat(EngagementBeat.Duck);
                        }
                        else if (!TryRelocateToFightingCover())
                        {
                            // Buried AND nowhere fightable — same pinned
                            // verdict as the exhausted rise-refusal path.
                            pinned = true; pinUntil = Time.unscaledTime + pinRecheckSec;
                            afterSettle = EngagementBeat.TopUp; EnterBeat(EngagementBeat.Duck);
                        }
                    }
                    else if (emptyProbeSince >= 0f
                          && Time.unscaledTime - emptyProbeSince >= dropAfterSec)
                    {
                        // MISS — stage 1 is miss-in-position (this Duck→Settle
                        // is it). The seam is the branch point: pendingSearch
                        // arms the hop driver; the FIRST miss also records the
                        // origin bearing hop-2 measures ±90° off of.
                        LastRiseDiag = $"lost him — no contact for {dropAfterSec:F1}s";
                        ArmSearch();
                        if (ArbActive) { zeroStreak++; arbVerdict = "lost contact"; }
                        else afterSettle = EngagementBeat.TopUp;
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
                            swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0; stuckBest = float.MaxValue;
                            returnBuilt = false;   // the off-spot return routes
                        }
                        if (anim != null && anim.EffectiveState != CharacterAnimation.AnimState.Walking)
                            anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                        if (StepRouted(CoverPoint, "return", ref returnBuilt))
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
                        if (StepRouted(swingDest, "swing-p0", ref swingP0Built))
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
                            ResetSearch();   // corner reacquire — search answered
                            if (ArbActive)
                            {   // Standing AT the rim — verdict + instant
                                // re-eval; StandDeliver continues the fight.
                                contactSeen = true;      // the rim probe saw him — honest write
                                zeroStreak = 0; arbVerdict = "contact @rim";
                                SeamEval(); break;
                            }
                            LastRiseDiag = "peek — contact at the corner";
                            Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag} → Engaged");
                            EnterBeat(EngagementBeat.Engaged);
                        }
                        else if (Time.unscaledTime >= swingDwellUntil)
                        {
                            LastRiseDiag = "peek — zero contact, stepping back";
                            Debug.Log($"🥊 [Combat] {name} — {LastRiseDiag}");
                            ArmSearch();   // the rim peek found nothing — same miss
                            if (ArbActive) { zeroStreak++; arbVerdict = "zero @rim"; }
                            swingPhase = 2;
                            // The dwell left the step guard's no-progress clock
                            // stale — reset it or the first return tick reads
                            // "stuck 0.6s+" and declares arrival at the rim.
                            swingStuckPos = Feet(); swingStuckT = Time.unscaledTime; stuckNudges = 0; stuckBest = float.MaxValue;
                            swingP2Built = false;   // the walk home routes too
                        }
                    }
                    else
                    {
                        // Step back into cover — the seam re-orients the
                        // settle facing from here.
                        if (anim != null && anim.EffectiveState != CharacterAnimation.AnimState.Walking)
                            anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                        if (StepRouted(CoverPoint, "swing-p2", ref swingP2Built))
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
                            if (!ArbActive) afterSettle = EngagementBeat.TopUp;
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
            bool beatHoldsReload = PossessController.Body != vox   // the possessed body reloads free (manual); others hold mid-motion
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
            if (State == CombatState.Idle) { TickWatch(); return; }
            // Distance actually covered while in combat — the wedge
            // diagnostic: SeekingCover with mv≈0 means the route was picked
            // but the body can't execute it (embedded square, pocket leg).
            Vector3 dpos = transform.position - lastPos; dpos.y = 0f;
            movedAccum += dpos.magnitude; lastPos = transform.position;
            // Direct control wins — possessing Vinny hands HIS rig to the
            // player (Body == this controller's VoxelCharacter). Every other
            // combatant keeps fighting — possessing must not stand the whole
            // world down. Keep the current duck: possess's cover drive owns
            // the scalar from here and re-derives it from the wall — zeroing
            // it would pop him up.
            if (PossessController.Body == vox)
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
            else if (ArbActive && State == CombatState.HoldPosition
                  && !reloading
                  && (seamNow || Time.unscaledTime >= arbHoldUntil))
            {   // Parked in the open IS a seam state under the arbiter —
                // the eval runs on the recheck clock and interrupts.
                SeamEval();
            }

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
                    if (searching)
                    {
                        // SCOUT posture — context-aware (the search is high
                        // alert): locally-Hidden ground keeps the fold at the
                        // LOCAL wall's requirement (moving along cover), open
                        // ground stands him up armed at the belief point.
                        float lreq = 0f;
                        bool covered = sensor != null
                            && sensor.TryZoneAt(Feet(), out var lcls, out lreq);
                        searchCovered = covered;
                        // ENGAGE vs MANEUVER — ExposureArmed is the verdict:
                        // presenting/firing always stands (the rise solve owns
                        // the only legal armed fold); the cover requirement
                        // only folds an UNARMED body moving along cover.
                        anim.duck = Mathf.MoveTowards(anim.duck,
                            covered && !ExposureArmed ? lreq : 0f,
                            seekDuckEase * Time.deltaTime);
                        var wantPose2 = (!covered || ExposureArmed)
                            ? CharacterAnimation.AnimState.AimWalk
                            : CharacterAnimation.AnimState.Walking;
                        if (anim.EffectiveState != wantPose2)
                            anim.RequestPose(wantPose2, CharacterAnimation.PRIORITY_COMBAT);
                        // LOOKING sweep over open legs — A/B flag; restores the
                        // pre-search setting whenever cover re-folds him.
                        anim.lookScan = covered ? savedLookScan : dbgSearchLook;
                    }
                    else
                    {
                        // Armed mid-seek (contact resolved, firing on the
                        // move) = ENGAGE posture — the destination's fold must
                        // not crumple a firing body; unarmed keeps the pre-fold
                        // so he arrives already in the cover's posture.
                        anim.duck = Mathf.MoveTowards(anim.duck,
                            ExposureArmed ? 0f : targetDuck,
                            seekDuckEase * Time.deltaTime);
                        // Seeking IS movement — the aim lane upgrades the march to
                        // AimWalk while voxels resolve; the duck scalar rides the
                        // gait via wd() (crumple deltas) either way.
                        var wantPose = ExposureArmed ? CharacterAnimation.AnimState.AimWalk
                                                     : CharacterAnimation.AnimState.Walking;
                        if (anim.EffectiveState != wantPose)
                            anim.RequestPose(wantPose, CharacterAnimation.PRIORITY_COMBAT);
                    }
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
                            ? Mathf.Max(0f, riseDuck)   // swing-corner Engaged inherits a REFUSED rise (−1) — over-stand lifts the feet; engaged posture never folds below stand
                            : Beat == EngagementBeat.Swing && swingPhase == 1
                                ? swingDuck : targetDuck;
                        anim.duck = Mathf.MoveTowards(anim.duck, wantD, riseDuckEase * Time.deltaTime);
                    }
                    else if (!Mathf.Approximately(anim.duck, targetDuck))
                        // HoldPosition seam — same verdict: an armed body holds
                        // firing posture (stand), an unarmed one folds to req.
                        anim.duck = ExposureArmed ? 0f : targetDuck;    // arrival/hold — committed posture, immediate
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
                        // Armed pose splits on the probe verdict: a resolved
                        // target (marks painting) gets the IK stare; a blind
                        // rise/peek gets AimSweep — arm holds last-known while
                        // the head sweeps the sector (the cone physically
                        // scans — sweep = "target hidden, keep looking").
                        var wantPose = ExposureArmed
                            ? (contactSeen ? CharacterAnimation.AnimState.Aiming
                                           : CharacterAnimation.AnimState.AimSweep)
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
                        // A TRANSIT orbit leg doesn't stop to peek — flag the
                        // seam to re-issue the next step; a RECON leg (or any
                        // non-search seek) runs the authored rhythm below.
                        if (ArbActive)
                        {   // MOVE leg complete — the verdict returns to the
                            // seam; orbitLegRecon survives as a cadence hint
                            // (recon-flagged arrivals bias the next eval to
                            // peek — same meaning, scored not scheduled).
                            seamReconBoost = orbitLegRecon;
                            arbVerdict = searching
                                ? (orbitLegRecon ? "hop arrived (recon)" : "hop arrived")
                                : "arrived";
                            EndSearchSeek();
                            EnterBeat(EngagementBeat.Settle);
                        }
                        else
                        {
                            if (searching && !orbitLegRecon) transitPending = true;
                            EndSearchSeek();      // search hop legs land the posture too
                            // The authored rhythm starts here: gather → top-up → settle → rise-check.
                            afterSettle = EngagementBeat.TopUp;
                            EnterBeat(EngagementBeat.Settle);
                        }
                        LastResult = $"in cover behind {CoverObstacleId} ({coverWallClass}, duck {targetDuck:F2})";
                        Debug.Log($"🥊 [Combat] {name} — in cover, pose {(coverWallClass == CoverSensor.WallClass.Full ? "STAND (Idle)" : $"duck@{targetDuck:F2}")}");
                    }
                }
                else
                {
                    // Routed legs: the cell flood materializes once per
                    // committed CoverPoint (zones = nodes, corridor = the
                    // route between them). No corridor → the straight ray
                    // stands in. Same leg mechanics as the beat steps —
                    // duck gait, axis slide, unstick nudges. An exhausted
                    // nudge budget aborts (the centroid proved unreachable)
                    // → same honest last stand as the no-cover path.
                    var legFld = sensor?.Field;
                    if (legRouteTarget != CoverPoint
                        || (legRoute.Count > 0 && legFld != null && legFld.BuildStamp != routeStamp))
                    {
                        legRouteTarget = CoverPoint;
                        if (!BuildRoute(Feet(), CoverPoint)) legRoute.Clear();
                        routeStamp = legFld != null ? legFld.BuildStamp : -1;
                    }
                    // With a live cover field, "no corridor" IS the
                    // unreachable verdict — the straight-ray fallback would
                    // grind him into whatever stands in the way: slide along
                    // the face, bearing whips through the node's XZ-line
                    // (the pillar loop), nudges burn, abort, orbit re-picks
                    // behind the same wall. Off-field legs still take the ray.
                    bool noCorridor = routeLegs && world != null
                        && sensor != null && sensor.Field != null && sensor.Field.HasData
                        && legRouteTarget == CoverPoint && legRoute.Count == 0;
                    if (noCorridor)
                    {
                        // A committed-but-unreachable stand is a FAILED face —
                        // without marking it the relocate pool re-picks the
                        // same destination every refusal cycle: refuse →
                        // relocate → corridor-dead → hold → refuse forever
                        // instead of exhausting the pool to the pin verdict.
                        if (sensor != null && CoverObstacleId != null)
                        {
                            float cf = float.MaxValue; CoverSensor.WallMark wDead = default; bool hasDead = false;
                            foreach (var w in sensor.walls)
                            {
                                if (w.volId != CoverObstacleId) continue;
                                float d2f = (w.facePos - CoverPoint).sqrMagnitude;
                                if (d2f < cf) { cf = d2f; wDead = w; hasDead = true; }
                            }
                            if (hasDead && cf <= 4f) failedFaces.Add(FaceKey(wDead));
                        }
                        Debug.Log($"⚠️ [Combat] {name} — no corridor to {CoverObstacleId} ({dist:F1}m) — leg ruled unreachable ({routeFail})");
                    }
                    if (StepRoute("seek", CoverPoint) || noCorridor)
                    {
                        // An aborted ORBIT leg isn't the end — that zone was
                        // spent at commit; re-pick the next arc step from
                        // where he actually is. Only full arc exhaustion (or
                        // an already-satisfied flank) ends the episode.
                        bool resumed = false;
                        if (searching)
                        {
                            EndSearchSeek();
                            if (ArbActive)
                            {   // Orbit leg aborted — the zone was spent at
                                // commit; the seam re-picks the next arc step.
                                arbVerdict = "orbit leg blocked";
                            }
                            else
                            {
                                resumed = TryOrbitStep();
                                if (!resumed) { ExitCombat(true); return; }
                            }
                        }
                        if (!resumed)
                        {
                            Debug.Log($"⚠️ [Combat] {name} — cover unreachable ({dist:F2}m out) — holding position");
                            LastResult = noCorridor
                                ? $"no corridor to {CoverObstacleId} ({routeFail}) — holding position"
                                : $"cover unreachable — holding position";
                            EndSearchSeek();
                            SetState(CombatState.HoldPosition);
                            CoverPoint = Feet();
                            CoverObstacleId = null;
                            coverWallClass = CoverSensor.WallClass.Crouch;
                            targetDuck = 1f;
                            if (ArbActive)
                            {   // MOVE verdict: blocked. The dead zone is
                                // avoid-listed; the parked seam re-evals NOW.
                                if (lastMoveCell >= 0) arbBlocked.Add(lastMoveCell);
                                arbVerdict = "leg blocked"; arbRunning = "HOLD(Watch)";
                                seamNow = true; arbHoldUntil = Time.unscaledTime;
                            }
                            CrouchAndCover();
                            FaceTrack(AimAnchor(), "hold-position");
                        }
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
            if (this.vis == null) this.vis = GetComponent<VisionSensor>();
            if (solver == null) return;

            // Cadenced probe — the visible-count answer gated THROUGH the
            // vision cone: geometric unocclusion alone is not detection.
            // Voxels resolved outside the posed eye fan don't count — the
            // cone supersedes all aiming.
            if (ThreatCharacter != null && Time.unscaledTime >= nextVisProbe)
            {
                nextVisProbe = Time.unscaledTime + visInterval;
                bool probeContact = Sensor.TryGetVisibleVoxels(ThreatCharacter, out var pv)
                                 && ConeCovers(pv.centroid);
                emptyProbeSince = probeContact ? -1f
                    : (emptyProbeSince < 0f ? Time.unscaledTime : emptyProbeSince);
            }
            var vis = (ThreatCharacter != null && sensor != null
                       && sensor.LastVisible.target == ThreatCharacter)
                ? sensor.LastVisible : default;
            bool canSee = vis.visible > 0 && ConeCovers(vis.centroid);

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
                    // Acquisition edge: the aim point is about to JUMP to
                    // the live centroid, but LastAngularErrorDeg is a frame
                    // stale — measured against the OLD point. The settle
                    // latch re-arms here; the trigger waits for the bore to
                    // actually hold the NEW target, not fire on stale truth.
                    convergeSince = -1f;
                    // Contact = the search answered itself — any armed/pending
                    // state clears. Mid-orbit-seek the roll fires: heads he
                    // continues to the committed cover (the opportunity
                    // channel owns the shot); tails he parks at the nearest
                    // local cover and restarts the engagement from there.
                    // `searching` must be captured BEFORE ResetSearch —
                    // EndSearchSeek clears the flag the roll reads.
                    bool wasOrbitLeg = searching && State == CombatState.SeekingCover;
                    ResetSearch();   // contact = the search answered itself
                    if (seamArbiter) seamNow = true;   // contact edge → instant seam re-eval
                    else if (wasOrbitLeg && UnityEngine.Random.value < 0.5f)
                        TryNearestCover();
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
            // THE ARM collapses to ONE IK channel with two modes:
            //   TARGETED — solve to a world point: the live centroid on
            //              contact, or the BELIEF anchor (AimAnchor) while
            //              peeking at memory — rise and swing dwell both
            //              paint last-known while the eye probes.
            //   AT-READY — the forward present along facing: scout legs
            //              carry the gun out front where he's scanning.
            // Both are aim-without-fire — the trigger below owns canSee.
            bool armOk = !reloading && mount != null && mount.IsEquipped;
            bool peekAim = armOk && (Beat == EngagementBeat.Rise
                          || (Beat == EngagementBeat.Swing && swingPhase == 1));
            bool searchAim = armOk && searching && State == CombatState.SeekingCover
                         && !searchCovered;
            bool wantAim = dbgArmSolver && ((canSee && clearToArm) || peekAim || searchAim);
            if (wantAim)
            {
                // Scout arm points FORWARD — his facing (movement-track) is
                // where he's scanning; training the gun on lastSeen mid-leg
                // would twist the torso toward memory the whole walk.
                if (searchAim && !canSee)
                {
                    Vector3 fwd = transform.forward; fwd.y = 0f;
                    aimPointWorld = Feet() + fwd.normalized * 2.5f + Vector3.up * 0.9f;
                }
                else aimPointWorld = canSee ? vis.centroid : AimAnchor();
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

            // EYES-ON-THREAT — the cone is the POV authority, but nothing ever
            // posed the head at a live target: walking legs read headYaw=0
            // (eyes welded to body-forward → destination), so the fan could
            // never hold the threat on the approach and 'cone off' held the
            // trigger forever. A threat-armed SeekingCover leg now poses the
            // head onto the belief anchor — feet run the route, eyes ride the
            // target. In-cover beats keep their authored head poses.
            if (vox != null)
            {
                if (State == CombatState.SeekingCover && ThreatCharacter != null)
                {
                    Vector3 et = AimAnchor() - transform.position; et.y = 0f;
                    // Animator RotationY is the transposed convention —
                    // positive headYaw = NEGATIVE Unity yaw — so the body→target
                    // delta flips sign here (and the ±1.35rad neck clamp rides).
                    vox.HeadYawOverride = et.sqrMagnitude > 1e-4f
                        ? Mathf.Clamp(-Mathf.DeltaAngle(transform.eulerAngles.y,
                              Mathf.Atan2(et.x, et.z) * Mathf.Rad2Deg) * Mathf.Deg2Rad,
                              -1.35f, 1.35f)
                        : float.NaN;
                }
                else vox.HeadYawOverride = float.NaN;
            }

            // Convergence = the bore HOLDING the current aim inside the gate,
            // not one sample — a retarget reads a stale error until the solver
            // re-solves, so convergence must persist fireSettleSec to count.
            bool errLive = solver.IsAiming && solver.Blend >= 0.7f
                        && solver.LastAngularErrorDeg <= aimGateDeg;
            convergeSince = errLive ? (convergeSince < 0f ? Time.unscaledTime : convergeSince) : -1f;

            // Sustained "sees but can't shoot" is a RELOCATE verdict, not a
            // firing position: cone holds him, bore never will — parked at
            // a buried muzzle is a staring-contest livelock. 1.2s of buried
            // bore under live contact → re-pick FIGHTING cover.
            // One welded-muzzle eval per frame — feeds the relocate clock
            // (needs contact+authority) AND the bore orb (needs only aim).
            bool boreBuriedNow = solver.IsAiming && BoreBuried(aimPointWorld);
            bool boreBlocked = boreBuriedNow && canSee && BeatAllowsFire;
            if (boreBlocked)
            {
                if (boreBuriedSince < 0f) boreBuriedSince = Time.unscaledTime;
                else if (Time.unscaledTime - boreBuriedSince > 1.2f)
                {
                    boreBuriedSince = -1f;
                    if (TryRelocateToFightingCover())
                        Debug.Log($"🥊 [Combat] {name} — bore permanently buried at {CoverObstacleId} → relocating to fighting cover");
                }
            }
            else boreBuriedSince = -1f;

            // Bore-state orb — a floating lamp welded to the real muzzle while
            // the arm presents: green = bore path to the aim point is clear,
            // red = buried. Off whenever the IK isn't holding the gun out
            // (stowed, reload, no weapon) — "matters" = IsAiming.
            var pdrOrb = dbgBoreOrb ? PathDebugRenderer.Instance : null;
            string orbKey = $"bore_{name}";
            if (pdrOrb != null && solver.IsAiming && mount != null && mount.IsEquipped)
            {
                pdrOrb.SetXrayCube(orbKey, mount.MuzzleWorld, Quaternion.identity, 0.05f,
                    boreBuriedNow ? new Color(1f, 0.15f, 0.1f, 0.95f)
                                  : new Color(0.2f, 1f, 0.3f, 0.9f));
                boreOrbDrawn = true;
            }
            else if (boreOrbDrawn)
            { PathDebugRenderer.Instance?.ClearXrayCube(orbKey); boreOrbDrawn = false; }

            // Fire gate: armed + CONVERGED-AND-HELD ON A VISIBLE SILHOUETTE —
            // canSee is the trigger's contract: aim-without-fire channels
            // (searchAim, peekAim, disarm-hold) may keep the gun up, but the
            // hammer only falls on resolved voxels. + cadence + a live round
            // + a FREE bore path — the welded muzzle ray is the truth;
            // converged-into-the-wall just wastes rounds into brick.
            // The gate reports its first refusal so a lit EXPOSED..CONVERGED
            // chain that still holds FIRE shows WHY instead of guessing.
            FireHold =
                !solver.IsAiming ? "stowed"
                : !canSee ? (vis.visible > 0 ? "cone off" : "no voxels")
                : cylinder <= 0 ? "dry"
                : !BeatAllowsFire ? "beat closed"
                : Time.unscaledTime < nextFireTime ? "cadence"
                : convergeSince < 0f ? "losing lock"
                : Time.unscaledTime - convergeSince < fireSettleSec ? "settling"
                : BoreBuried(aimPointWorld) ? "bore buried"
                : null;
            // Transition log — the strip is too busy to read a transient hold;
            // log on reason change, coalesce fast flapping into a ×N count so
            // a canSee flicker doesn't flood the console at frame rate.
            if (FireHold != fireHoldLogged)
            {
                fireHoldFlips++;
                if (Time.unscaledTime >= nextFireHoldLog)
                {
                    Debug.Log($"🥊 [Combat] {name} — fire gate: {FireHold ?? "CLEAR"}"
                            + $" (vis {vis.visible} · cone {(ConeCovers(vis.centroid) ? "on" : "off")}"
                            + $" · err {solver.LastAngularErrorDeg:F1}° blend {solver.Blend:F2}"
                            + $" · {Beat}/{State})"
                            + (fireHoldFlips > 1 ? $" ×{fireHoldFlips} flips" : ""));
                    nextFireHoldLog = Time.unscaledTime + 0.2f;
                    fireHoldFlips = 0;
                }
                fireHoldLogged = FireHold;
            }
            if (FireHold != null) return;
            nextFireTime = Time.unscaledTime + fireInterval;
            if (ProjectileSystem.GetOrCreate().Fire(mount, solver.LastAngularErrorDeg, vis.centroid)
                && SpendRound())
            {
                ShotsFired++;
                LastShotAt = Time.unscaledTime;
                // The trigger pull IS a decision event — log it (the gate's
                // CLEAR transition may have been eaten by the flap coalescer).
                Debug.Log($"🥊 [Combat] {name} — FIRED #{ShotsFired} → ({vis.centroid.x:F0},{vis.centroid.z:F0})"
                        + $" (vis {vis.visible} · err {solver.LastAngularErrorDeg:F1}° · {Beat}/{State})");
                // The report is a spatially-exact stimulus: the opponent hears
                // the bang — his last-known snaps to the firing position and a
                // live orbit re-anchors on it. Scoped to the declared threat;
                // a radius broadcast is the crowd-scale version of this hook.
                ThreatCharacter?.GetComponent<CombatController>()?.OnShotHeard(Feet());
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
            // `half` offsets the min-corner transform to the MODEL center;
            // the square the body occupies is the SPEC'd one (BodySqHalf =
            // vc.BodyFootprint/2 — the identical square the field certifies
            // and the debug box draws). One primitive, one answer: the
            // mover now collides with exactly what the map promised.
            Vector3 half = new Vector3(dims.x * 0.5f, 0f, dims.z * 0.5f);

            // TryMove = the shared placement verdict — square free, else a
            // step-up top offered and the raised square free. One primitive
            // (TryBodySquare) owns the composition: the field certifies the
            // identical question this mover executes.
            bool TryMove(Vector3 dst, out Vector3 placed)
            {
                placed = dst;
                if (world.TryBodySquare(dst + half, BodySqHalf, dims.y, stepUpMax,
                                        out float py))
                { placed.y = py; return true; }
                return false;
            }

            Vector3 dst = transform.position + delta;
            if (TryMove(dst, out var p0)) { transform.position = p0; return; }
            // Slide along the DOMINANT component first — corner-wrap needs
            // the free axis tried before the blocked one; a fixed X-then-Z
            // order kept feeding him back into the wall lip.
            bool xDom = Mathf.Abs(delta.x) >= Mathf.Abs(delta.z);
            Vector3 a = xDom ? new Vector3(delta.x, 0f, 0f) : new Vector3(0f, 0f, delta.z);
            Vector3 b = delta - a; b.y = 0f;
            Vector3 pa = transform.position + a;
            if (TryMove(pa, out var p1)) { transform.position = p1; return; }
            Vector3 pb = transform.position + b;
            if (TryMove(pb, out var p2)) transform.position = p2;
        }
    }
}
