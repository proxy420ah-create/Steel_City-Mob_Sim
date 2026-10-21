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
        public enum EngagementBeat : byte { None = 0, Settle = 1, TopUp = 2, Rise = 3, Engaged = 4, Duck = 5 }

        [Header("Movement")]
        [Tooltip("Combat hustle speed (m/s) — drives both the transform step and walk anim rate.")]
        public float moveSpeed = 2.4f;
        public float arriveEps = 0.18f;
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
        public float settleSec = 5f;
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
        void SetState(CombatState s) { if (State == s) return; State = s; Notify(); }

        public CombatState State { get; private set; }
        /// <summary>Current engagement beat — the panel's third lane.</summary>
        public EngagementBeat Beat { get; private set; }
        /// <summary>Duck the last Rise solve picked — min duck clearing eye+muzzle over the wall.</summary>
        public float RiseDuck => riseDuck;
        /// <summary>Seconds left on the current beat's timer (Settle/Rise; else 0) — the panel's inline countdown.</summary>
        public float BeatLeft => Beat == EngagementBeat.Settle ? Mathf.Max(0f, beatUntil - Time.unscaledTime) : 0f;   // Rise is event-bound (pose convergence) — no clock left
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
                ? Sensor.TryPickCoverNear(coverAnchor.Value, maxDist, out Vector3 p, out string oid, out float reqD, out coverWallClass)
                : Sensor.TryPickCover(Feet(), out p, out oid, out reqD, out coverWallClass);
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
        bool BeatAllowsFire => State == CombatState.SeekingCover
            || State == CombatState.HoldPosition
            || Beat == EngagementBeat.Rise || Beat == EngagementBeat.Engaged;

        /// <summary>Beat transition — every hop emits Changed for the flow lane.</summary>
        void EnterBeat(EngagementBeat b)
        {
            Beat = b;
            awaitingLoop = false;   // any real beat clears a parked gate
            switch (b)
            {
                case EngagementBeat.Settle:
                    beatUntil = Time.unscaledTime + settleSec;
                    break;
                case EngagementBeat.Duck:
                    loopGate = true;   // the settle after a duck is the re-cycle gate
                    ScriptStageDone = true;   // stage verdict reached (dry/refused/empty/buried/lost)
                    break;
                case EngagementBeat.Engaged:
                    ScriptStageDone = true;   // contact confirmed — the stop produced fire
                    emptyProbes = 0;
                    break;
                case EngagementBeat.Rise:
                    riseDuck = SolveRiseDuck();
                    // No clearable rise → duck, settle, gate. The seam decides
                    // park-vs-restart LIVE at its exit — afterSettle stays
                    // TopUp so a mid-seam toggle-ON still proceeds.
                    if (riseDuck < 0f)
                    {
                        Debug.Log($"🥊 [Combat] {name} — rise refused: {LastRiseDiag}");
                        // The reposition branch: this wall can't be fought
                        // over → re-pick for cover whose rise DOES clear.
                        if (TryRelocateToFightingCover()) return;
                        afterSettle = EngagementBeat.TopUp;
                        EnterBeat(EngagementBeat.Duck); return;
                    }
                    // Atomic beat: the pose converging to riseDuck IS the rise
                    // completing — the contact verdict lands there. No window.
                    break;
            }
            Notify();
        }

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

            float r = SolveRiseAtSpot(transform.position, transform.eulerAngles.y,
                wallTop, tgt, transform.forward,
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
                    if (!reloading) { afterSettle = EngagementBeat.Rise; EnterBeat(EngagementBeat.Settle); }
                    break;
                case EngagementBeat.Rise:
                    // Atomic — no recon window: pose converged to full stand
                    // IS the rise completing (the eye physically IS up). The
                    // contact verdict is taken at that instant on a FRESH
                    // probe, not the cadenced cache.
                    if (anim == null || Mathf.Abs(anim.duck - riseDuck) < 0.02f)
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
                    // Buried-bore verdict: the solve cleared him but the REAL
                    // welded muzzle ray hits the wall — sustained burial =
                    // unfightable cover → relocate (same path as refusal).
                    if (solver.IsAiming && solver.Blend >= 0.7f && BoreBuried(aimPointWorld))
                        buriedSince += Time.unscaledDeltaTime;
                    else buriedSince = 0f;
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
                    if (anim == null || Mathf.Abs(anim.duck - targetDuck) < 0.02f)
                        EnterBeat(EngagementBeat.Settle);
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
            // Live threat feed — the sensor's context tracks the ACTOR, not
            // the entry-time snapshot: ThreatPos updates to his current feet
            // each frame and zones re-paint at the sensor's scan cadence as
            // he moves (awareness stays live through the whole engagement;
            // out of combat the pickedAim sync owns the channel instead).
            if (ThreatCharacter != null)
            {
                ThreatPos = ThreatFeet();
                Sensor.Threat = ThreatPos;
            }
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
                        float wantD = (Beat == EngagementBeat.Rise || Beat == EngagementBeat.Engaged)
                            ? riseDuck : targetDuck;
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
                    var wantPose = ExposureArmed ? CharacterAnimation.AnimState.Aiming
                        : (anim.duck > 1.02f ? CharacterAnimation.AnimState.Crouching
                                             : CharacterAnimation.AnimState.Idle);
                    if (anim.EffectiveState != wantPose)
                        anim.RequestPose(wantPose, CharacterAnimation.PRIORITY_COMBAT);
                }
            }
            if (State != CombatState.SeekingCover) return;

            Vector3 feet = Feet();
            Vector3 to = CoverPoint - feet;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist <= arriveEps)
            {
                // Snap onto the probed ground at the cover point, then take cover.
                var pos = transform.position;
                pos.y += CoverPoint.y - feet.y;
                transform.position = pos;
                SetState(CombatState.InCover);
                Relocating = false;   // the relocate phase ends where the seek ends
                CrouchAndCover();
                // The authored rhythm starts here: gather → top-up → settle → rise-check.
                afterSettle = EngagementBeat.TopUp;
                EnterBeat(EngagementBeat.Settle);
                LastResult = $"in cover behind {CoverObstacleId} ({coverWallClass}, duck {targetDuck:F2})";
                Debug.Log($"🥊 [Combat] {name} — in cover, pose {(coverWallClass == CoverSensor.WallClass.Full ? "STAND (Idle)" : $"duck@{targetDuck:F2}")}");
                return;
            }

            Vector3 dir = to / dist;
            // Duck gait is slower by definition — the same factor that slows
            // his gait cycle slows the approach, so feet stay married to
            // terrain while folded (deep-duck approach = slow crab-walk).
            float step = moveSpeed * (anim != null ? anim.DuckSpeedFactor : 1f) * Time.deltaTime;
            MoveWithCollision(dir * Mathf.Min(step, dist));

            float wantYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, wantYaw, turnSpeedDeg * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
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

            if (canSee)
            {
                LastSeenTarget = vis.centroid;
                LastSeenAt = Time.unscaledTime;
            }

            // Arm gate = exposure + not-reloading + an equipped weapon + beat
            // authority. Arm once, retarget every tick — the centroid moves as
            // the silhouette changes. Any pose may fire if the IK resolves.
            bool clearToArm = !reloading && mount != null && mount.IsEquipped
                           && BeatAllowsFire;
            bool wantAim = canSee && clearToArm;
            if (wantAim)
            {
                aimPointWorld = vis.centroid;
                if (solver.IsAiming) solver.SetTarget(vis.centroid);
                else solver.Arm(vis.centroid);
            }
            // Disarm hold: a lone empty probe must not pump-stow him — hold the
            // arm on lastSeen through brief misses (disarmHoldSec). Losing
            // AUTHORITY (reload starts, weapon gone, beat closed the channel)
            // stows instantly — the arm is still beat-owned.
            bool holding = solver.IsAiming && clearToArm
                        && Time.unscaledTime - LastSeenAt <= disarmHoldSec;
            if (!wantAim && !holding && ExposureArmed && solver.IsAiming)
                solver.Disarm();
            bool armed = (wantAim || holding) && solver.IsAiming;
            if (armed != ExposureArmed) { ExposureArmed = armed; Notify(); }

            // Fire gate: armed + converged (blend raised, bore error inside
            // the gate) + cadence + a live round + a FREE bore path — the
            // welded muzzle ray is the truth; converged-into-the-wall just
            // wastes rounds into brick.
            if (!solver.IsAiming || cylinder <= 0 || Time.unscaledTime < nextFireTime
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
            if (anim != null) anim.coverContext = true;   // Full or ducked — covered is covered
            // Scalar pick: deep band (duck>1) needs the state-5 endpoints; the
            // morph band and Full both ride Idle where icr scales the fold.
            anim?.RequestPose(targetDuck > 1.02f ? CharacterAnimation.AnimState.Crouching
                                                 : CharacterAnimation.AnimState.Idle,
                              CharacterAnimation.PRIORITY_COMBAT);
            Vector3 to = ThreatPos - Feet();
            to.y = 0f;
            if (to.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
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
            Vector3 sx = transform.position + new Vector3(delta.x, 0f, 0f);
            if (Free(sx)) { transform.position = sx; return; }
            Vector3 sz = transform.position + new Vector3(0f, 0f, delta.z);
            if (Free(sz)) transform.position = sz;
        }
    }
}
