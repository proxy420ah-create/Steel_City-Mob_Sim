using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// Possess mode — take direct third-person control of the spawned character.
    /// Toggle with G while the map camera is up (F6 freedom not required but works).
    ///
    /// While possessed:
    ///   WASD   — move Vinny camera-relative (Shift = run); walls block via
    ///            VoxelCollisionWorld.SamplePoint, gravity is volume-aware
    ///   Mouse  — third-person orbit look around his head (cursor locked;
    ///            camera snaps behind his back on grab)
    ///   LMB    — fire the equipped weapon through the camera crosshair
    ///   Cover  — the adjacent-cover drive face-probes column tops around his
    ///            feet and writes anim.duck to whatever the tallest nearby
    ///            wall requires (the gradient-wall testbed: stand at Full,
    ///            fold through the morph band, deepen past duck 1, stand again
    ///            past the null tail). Duck also scales his move speed.
    ///   The IK solver stays armed at the crosshair hit point the whole time,
    ///   so shots leave the muzzle wherever the arm actually points.
    /// Release (G again): solver disarms, rig hotkeys resume, the camera stays
    /// wherever you left it (SetCameraFocus frozen → "lock the shot" and keep
    /// testing with the normal camera controls).
    /// </summary>
    public class PossessController : MonoBehaviour
    {
        public static bool Active { get; private set; }
        /// <summary>WHICH body is possessed — combat controllers gate their
        /// stand-down on this, not on Active alone: possessing Vinny hands
        /// HIS rig to the player while every other combatant keeps fighting.
        /// </summary>
        public static VoxelCharacter Body { get; private set; }

        [Header("Feel")]
        [SerializeField] private float walkSpeed = 2.5f;
        [SerializeField] private float sprintMult = 2.2f;
        [SerializeField] private float turnSpeed = 10f;
        [SerializeField] private float yawSens = 0.18f;
        [SerializeField] private float pitchSens = 0.15f;
        [SerializeField] private float orbitSuggest = 4f;
        [SerializeField] private float aimRange = 400f;
        [Header("Camera boom")]
        [Tooltip("Padding kept between the camera and the voxel surface the boom hits")]
        [SerializeField] private float camPad = 0.35f;
        [Tooltip("Boom can't collapse closer than this — keeps the camera out of Vinny's head")]
        [SerializeField] private float camMinOrbit = 0.9f;
        [SerializeField] private float boomReturnSpeed = 7f;   // ease back out after a clip
        [Header("Cover drive")]
        [SerializeField] private bool coverDrive = true;
        [SerializeField] private float coverProbeRadius = 0.5f;  // min reach for the horizontal face-find rays
        // (coverDriveEase removed — crouchDepth writes are immediate now)
        [SerializeField] private float crouchMoveFactor = 0.45f; // speed multiplier at full duck (2) — deep crouch shuffles
        // Step law lives on VoxelCharacter.stepUpMax — the same spec the
        // flood, footprint deck rule, and combat mover read. One owner.

        private bool combatHold;          // possessed straight out of InCover — combat's pose/depth rules until he leaves the spot
        private float holdDepth;          // combat's duck at possess-time — frozen while combatHold
        public static bool DriveCrouch, HoldActive;
        public static readonly bool[] ProbeFull = new bool[8];
        private Vector3 holdPos;
        private CoverSensor coverSensor;
        private Vector3? pinnedThreat;   // threat captured at possess — CombatController exits on possess and nulls sensor.Threat, which would blind the threat-keyed field paint

        // Cover-drive diagnostics — CoverMetricsPanel mirrors these read-only.
        public static bool DriveActive, ThreatPinned;
        public static float DriveBestD = float.NaN, DriveReq, DriveSupport;
        public static int DriveBestDir = -1;
        // (gap latch + coverDriveEase removed — the field is immediate now:
        // the painted cell under his feet IS the posture, same frame)
        public static CoverSensor.WallClass ZoneCls;   // zone under his feet (Low = no zone)
        public static float ZoneD;                     // that zone's required depth
        public static readonly float[] ProbeTop = new float[8];   // NaN = ray miss
        public static readonly float[] ProbeD = new float[8];
        public static readonly string[] ProbeVol = new string[8]; // hit volume id, null = terrain/none

        private CityMap3D cityMap;
        private VoxelCollisionWorld world;
        private VoxelCharacter vinny;
        private WeaponMount mount;
        private ArmAimSolver solver;
        private CombatController cc;   // shared cylinder — same wheel as the AI fire path
        private CharacterAnimation anim;
        private CharacterRig rig;
        private CharacterAnimation.AnimState? testPose; // Alt+mnemonic latch — beats the per-frame aim request
        private bool prevControllable;
        private bool prevFreedom;
        private float prevOrbit;
        private float stuckT;            // wedged-with-input accumulator → unstuck teleport
        private float nextUnstuckProbe;  // retry cadence — the ring scan isn't free
        private bool prevSightBeam;

        private Vector3 aimPoint;          // current crosshair hit / fallback point
        private float lookYaw, lookPitch;  // owned look angles — driven 1:1 by mouse delta
        private float fireCooldown;
        private float boomCur = -1f;       // smoothed collision-clamped orbit distance
        private Texture2D crossTex;

        void Awake() => cityMap = GetComponent<CityMap3D>();

        void OnDisable() { if (Active) Release(); }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.gKey.wasPressedThisFrame)
            {
                if (Active) Release();
                else TryPossess();
            }

            if (!Active) return;
            if (vinny == null) { Release(); return; }

            var cam = cityMap != null ? cityMap.MapCamera : null;
            if (cam == null) { Release(); return; }

            // Free-cam owns input (F6 freedom or F8 freeze — CamFree covers
            // both): possess yields ALL of WASD/mouse/fire to the map fly —
            // otherwise held WASD walks Vinny mid-test while the fly sits
            // suppressed on the Active flag. Look angles stay synced so
            // control resumes without a view snap.
            if (cityMap.CamFree)
            {
                lookYaw = cityMap.GetCameraYaw();
                lookPitch = cityMap.GetCameraPitch();
                // The fly owns the cursor too — a locked invisible pointer
                // can't hit UI or pick blocks in the inspect pass. Re-locks
                // below the moment control returns.
                if (Cursor.lockState != CursorLockMode.None)
                { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                return;
            }
            if (Cursor.lockState != CursorLockMode.Locked)
            { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

            // --- Crosshair target: center of the map viewport into the voxel world ---
            aimPoint = cam.transform.position + cam.transform.forward * 60f;
            if (world != null)
            {
                Ray cr = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (world.Raycast(cr.origin, cr.direction, aimRange, out VoxelRayHit ch))
                    aimPoint = ch.point;
            }
            if (solver != null && solver.IsAiming) solver.SetTarget(aimPoint);

            // --- Mouse look: cursor is locked, so delta is unlimited — drive
            // the orbit angles immediately (no smoothing lag on the look itself).
            if (Mouse.current != null)
            {
                Vector2 d = Mouse.current.delta.ReadValue();
                if (d.sqrMagnitude > 0f)
                {
                    lookYaw += d.x * yawSens;
                    lookPitch = Mathf.Clamp(lookPitch - d.y * pitchSens, -85f, 85f);
                    cityMap.SetCameraAnglesImmediate(lookYaw, lookPitch);
                }
            }

            // Combat exits on possess and clears sensor.Threat — re-pin the
            // captured threat so the field keeps evaluating vs the enemy
            // while you inspect (threat V-modes, LOS verdicts, candidates).
            if (coverSensor != null && pinnedThreat.HasValue)
                coverSensor.Threat = pinnedThreat;
            ThreatPinned = coverSensor != null && pinnedThreat.HasValue;

            // --- Adjacent-cover drive (heightfield Phase B2): ring-probe the
            // column tops around his feet; the most protective column (lowest
            // DuckForTop) sets the request — d <= 0 Full (stands), 0 < d <= 2
            // duckable (sub-1 morph band), d > 2 or no column → release.
            // COMBAT-GATED per doctrine: crouching only exists under a threat
            // context (combat or armed pit cross) — no threat, no duck. The
            // scalar feeds the speed factor below and the pose pick further down.
            // COMBAT HAND-OFF: entering possess from InCover inherits combat's
            // pose + depth (crouched, gun down) and holds it until he actually
            // walks away from the cover spot; only then does the wall-driven
            // drive take over (and the IK arms).
            if (combatHold)
            {
                var hp = vinny.transform.position;
                if ((new Vector3(hp.x - holdPos.x, 0f, hp.z - holdPos.z)).sqrMagnitude > 0.25f * 0.25f)
                {
                    combatHold = false;
                    if (solver != null && !solver.IsAiming) solver.Arm(aimPoint);
                }
                else if (anim != null)
                {
                    anim.duck = holdDepth;          // snap — immediate doctrine
                    anim.coverContext = true;          // inherited cover holds the IK gate
                }
            }
            HoldActive = combatHold;
            // Hold exit's one-shot Arm above races the pose transition — the
            // state is still Crouching on the flip frame, the posture gate
            // rejects it, and nobody retried. Retry every frame: the gate
            // keeps refusing while the cover drive holds him ducked, and the
            // arm lands the frame the pick finally leaves Crouching — IK aim
            // returns exactly when he's free of the wall, not before.
            if (!combatHold && solver != null && solver.enabled && !solver.IsAiming)
                solver.Arm(aimPoint);
            DriveActive = !combatHold && coverDrive && world != null && anim != null
                       && coverSensor != null && coverSensor.HasThreat;
            if (DriveActive)
            {
                Vector3 wc = vinny.WorldCenter;
                Vector3 feet = new Vector3(wc.x, vinny.transform.position.y, wc.z);
                float supportY = feet.y;
                world.TryColumnTop(feet, 0.5f, 2f, out supportY);
                float bestD = float.MaxValue;
                int bestDir = -1;
                // Horizontal face-find replaces the fixed down-ring: each probe
                // measures the wall's REAL distance — a hugged face can't be
                // overshot, a face beyond reach can't fake cover. The axial gate
                // (~cos 25°) rejects grazing/diagonal hits — wall ends and seams
                // don't fabricate cover he isn't actually behind.
                float reach = Mathf.Max(coverProbeRadius, coverSensor.standoff + 0.25f);
                Vector3 probeOrigin = feet + Vector3.up * 0.25f;   // knee-height: crouch walls still register
                for (int i = 0; i < 8; i++)
                {
                    float a = i * (Mathf.PI * 0.25f);
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    if (world.Raycast(probeOrigin, dir, reach, out VoxelRayHit fh)
                        && Vector3.Dot(dir, -fh.normal) >= 0.85f)
                    {
                        // Threat-side gate — same test the field applies
                        // (facing = outDir·dirToThreat): if the face's open
                        // side points AT the shooter, he's on the EXPOSED side
                        // and the wall isn't cover from this threat at all.
                        // fh.normal ≈ the face's outDir (toward him).
                        Vector3 toThreat = coverSensor.Threat.Value - fh.point; toThreat.y = 0f;
                        if (toThreat.sqrMagnitude > 1e-4f
                            && Vector3.Dot(fh.normal, toThreat.normalized) >= coverSensor.facingDot)
                        { ProbeTop[i] = float.NaN; ProbeD[i] = float.NaN; ProbeVol[i] = null; }
                        // Column top just inside the face — the nudge keeps thin
                        // faces from sampling the open column behind the wall.
                        else if (world.TryColumnTop(fh.point + dir * 0.05f, 2f, 3f, out float wt))
                        {
                            ProbeTop[i] = wt;
                            // RAW duck solve — DuckForTop clamps to [0,2] for
                            // pose feeders; raw d carries the class: <=0 Full,
                            // 0..2 duckable (sub-1 = morph band), >2 = can't hide.
                            ProbeD[i] = coverSensor.DuckForTopRaw(wt, supportY);
                            ProbeVol[i] = fh.volumeId ?? "terrain";
                        }
                        else { ProbeTop[i] = float.NaN; ProbeD[i] = float.NaN; ProbeVol[i] = null; }
                    }
                    else { ProbeTop[i] = float.NaN; ProbeD[i] = float.NaN; ProbeVol[i] = null; }
                    // Class by the SAME rule the sensor uses: Full only when the
                    // top clears his standing height; depth<=0 alone just means
                    // the SHALLOW crouch already hides him.
                    ProbeFull[i] = !float.IsNaN(ProbeTop[i]) && ProbeTop[i] - supportY > coverSensor.HStand;
                    if (ProbeD[i] < bestD) { bestD = ProbeD[i]; bestDir = i; }
                }
                float req = bestD <= 2f ? Mathf.Clamp(bestD, 0f, 2f) : 0f;
                DriveCrouch = bestDir >= 0 && bestD <= 2f && !ProbeFull[bestDir];
                // The painted ZONES dictate posture too: the zone is a
                // uniform standoff band (the stand spot, flush to the face —
                // no corridor), so stepping in ducks him and stepping out
                // releases him, simple. The deeper of the ring hit and the
                // zone verdict wins.
                ZoneCls = CoverSensor.WallClass.Low; ZoneD = 0f;
                if (coverSensor.TryZoneAt(feet, out CoverSensor.WallClass zc, out float zd))
                {
                    ZoneCls = zc; ZoneD = zd;
                    if (zc == CoverSensor.WallClass.Crouch)
                    {
                        // Zone owns posture; the LIVE ring solve owns depth.
                        // The zone's d is baked vs the owning wall's global top
                        // — a lie on gradient/stepped walls where the column at
                        // HIS spot differs. Only when the ring is blind (no
                        // crouch wall found) does the baked number fill in.
                        if (!DriveCrouch) req = Mathf.Max(req, zd);
                        DriveCrouch = true;
                    }
                }
                // IMMEDIATE contract: the painted cell under his feet IS the
                // posture — no ease, no dropout latch. Color says what he does,
                // the same frame it says it.
                bool detected = (bestDir >= 0 && bestD <= 2f) || ZoneCls != CoverSensor.WallClass.Low;
                anim.duck = req;   // duck-space — morph band (sub-1) reaches icr/wd
                anim.coverContext = detected;   // Full or crouch — covered is covered
                DriveBestD = bestD; DriveReq = req; DriveSupport = supportY; DriveBestDir = bestDir;
            }
            else if (anim != null && !combatHold)
            {
                DriveCrouch = false;
                anim.duck = 0f;
                anim.coverContext = false;
                DriveBestD = float.NaN; DriveReq = 0f;
            }

            // --- WASD move, camera-yaw relative ---
            Vector2 mv = Vector2.zero;
            if (kb.wKey.isPressed) mv.y += 1f;
            if (kb.sKey.isPressed) mv.y -= 1f;
            if (kb.dKey.isPressed) mv.x += 1f;
            if (kb.aKey.isPressed) mv.x -= 1f;

            bool moving = mv.sqrMagnitude > 0f;
            if (moving)
            {
                mv.Normalize();
                float yaw = cityMap.GetCameraYaw() * Mathf.Deg2Rad;
                Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                Vector3 rgt = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
                Vector3 wish = (fwd * mv.y + rgt * mv.x);
                float sp = walkSpeed * (kb.shiftKey.isPressed ? sprintMult : 1f)
                         * Mathf.Lerp(1f, crouchMoveFactor, anim != null ? anim.duck * 0.5f : 0f);
                MoveWithCollision(vinny.transform, wish * sp * Time.deltaTime);
            }

            // --- Face the crosshair direction (turret-style while aiming) ---
            Vector3 toAim = aimPoint - vinny.WorldCenter;
            toAim.y = 0f;
            if (toAim.sqrMagnitude > 0.04f)
            {
                Quaternion want = Quaternion.LookRotation(toAim.normalized, Vector3.up);
                vinny.transform.rotation = Quaternion.Slerp(
                    vinny.transform.rotation, want, turnSpeed * Time.deltaTime);
            }

            // --- Anim test poses: Alt + mnemonic latches a PRIORITY_DEBUG
            // override (same letters as the test rigs — Alt keeps them clear
            // of WASD/camera). Re-requested per frame so nothing outranks it;
            // Alt+X releases back to the normal aim pose.
            if (kb.altKey.isPressed)
            {
                CharacterAnimation.AnimState? pick = null;
                if (kb.iKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Idle;
                else if (kb.wKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Walking;
                else if (kb.lKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Looking;
                else if (kb.jKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.AimWalk;
                else if (kb.aKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Aiming;
                else if (kb.cKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Crouching;
                else if (kb.fKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Flinching;
                else if (kb.dKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Down;
                else if (kb.tKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.TPose;
                else if (kb.sKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.AimSweep;
                else if (kb.rKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Reloading;
                else if (kb.eKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.ReloadCrouch;
                else if (kb.bKey.wasPressedThisFrame) pick = CharacterAnimation.AnimState.Sitting;
                else if (kb.xKey.wasPressedThisFrame)
                { testPose = null; Debug.Log("[Possess] test pose released — back to aim"); }
                if (pick.HasValue)
                { testPose = pick.Value; Debug.Log($"[Possess] test pose → {testPose.Value} (Alt+X releases)"); }
            }

            // --- Anim state: latched test pose wins; else the cover verdict
            // drives posture — IMMEDIATE (no ease): the cell under his feet IS
            // the pose. Duck discriminates endpoint set by movement: stationary
            // = authored hold (Crouching), moving = crumple-gait (Walking + wd).
            // Any detected cover (Full included) drops the aim lane entirely —
            // covered is covered: he walks/stands in cover, never poses aimed.
            if (anim != null)
                // Pure scalar pick — the drive writes duck 0..2 and the state
                // merely chooses HOW that duck renders: deep band (>1.02) needs
                // the state-5 endpoint path; the morph band (0<duck<=1) rides
                // Idle/Walking where icr/wd scale it continuously.
                anim.RequestPose(testPose ?? (cc != null && cc.Reloading
                                         ? (anim.duck > 1.02f ? CharacterAnimation.AnimState.ReloadCrouch
                                                              : CharacterAnimation.AnimState.Reloading)
                                         : anim.duck > 1.02f
                                         ? (moving ? CharacterAnimation.AnimState.Walking
                                                   : CharacterAnimation.AnimState.Crouching)
                                     : anim.coverContext          // Full or morph — covered
                                         ? (moving ? CharacterAnimation.AnimState.Walking
                                                   : CharacterAnimation.AnimState.Idle)
                                     : combatHold ? CharacterAnimation.AnimState.Idle
                                     : moving ? CharacterAnimation.AnimState.AimWalk
                                              : CharacterAnimation.AnimState.Aiming),
                                 CharacterAnimation.PRIORITY_DEBUG);

            // --- Orbit pivot pins to his head (over-the-shoulder framing) ---
            cityMap.SetCameraFocus(EyePoint());

            // --- Boom collision: raycast along the boom, clamp the orbit before
            // the camera can clip through voxels (roads on pitch-up, walls on
            // yaw). Snap in hard, ease back out — no pop when the blocker clears.
            if (world != null)
            {
                float yR = lookYaw * Mathf.Deg2Rad, pR = lookPitch * Mathf.Deg2Rad;
                float horiz = Mathf.Cos(pR);
                Vector3 boomDir = new Vector3(-horiz * Mathf.Sin(yR), Mathf.Sin(pR), -horiz * Mathf.Cos(yR));
                float want = orbitSuggest;
                if (world.Raycast(EyePoint(), boomDir, want + camPad, out VoxelRayHit bh))
                    want = Mathf.Max(camMinOrbit, bh.distance - camPad);
                boomCur = boomCur < 0f || want < boomCur ? want
                        : Mathf.Lerp(boomCur, want, boomReturnSpeed * Time.deltaTime);
                cityMap.debugCameraOrbitDistance = boomCur;
            }

            // --- Fire — shares the CombatController cylinder: same six
            // chambers, same rules. LMB spends a round; dry wheel wants a
            // deliberate R reload (manual here, auto in combat). ---
            if (cc == null) cc = vinny.GetComponent<CombatController>()
                              ?? vinny.gameObject.AddComponent<CombatController>();
            fireCooldown -= Time.deltaTime;
            if (kb != null && kb.rKey.wasPressedThisFrame) cc.StartReload();
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                && EventSystemIsFree() && fireCooldown <= 0f
                && mount != null && mount.IsEquipped
                && cc.Cylinder > 0 && !cc.Reloading)
            {
                if (ProjectileSystem.GetOrCreate()
                        .Fire(mount, solver != null ? solver.LastAngularErrorDeg : 0f, aimPoint))
                {
                    cc.SpendRound();
                    fireCooldown = 0.18f;
                }
            }
        }

        /// <summary>Move with voxel-wall blocking: the SHARED body-square
        /// verdict at the destination — possess mode collides with the
        /// identical BodyFootprint square the field certifies and the combat
        /// mover sweeps (the footprint spec now genuinely gates WASD); try
        /// X/Z slide when the combined move is blocked.</summary>
        private void MoveWithCollision(Transform body, Vector3 delta)
        {
            if (world == null) { body.position += delta; return; }

            Vector3 dims = vinny.WorldSize;
            // `half` offsets the min-corner transform to the MODEL center;
            // the square = the spec'd footprint — one owner, all consumers.
            Vector3 half = new Vector3(dims.x * 0.5f, 0f, dims.z * 0.5f);
            float bodyHalf = vinny.BodyFootprint * 0.5f;

            // TryMove = the shared placement verdict — square free, else a
            // step-up top offered and the raised square free. One primitive
            // (TryBodySquare) owns the composition: the field certifies the
            // identical question this mover executes.
            bool TryMove(Vector3 dst, out Vector3 placed)
            {
                placed = dst;
                if (world.TryBodySquare(dst + half, bodyHalf, dims.y, vinny.stepUpMax,
                                        out float py))
                { placed.y = py; return true; }
                return false;
            }

            if (TryMove(body.position + delta, out var p0)) { body.position = p0; stuckT = 0f; return; }
            Vector3 dx = new Vector3(delta.x, 0f, 0f);
            Vector3 dz = new Vector3(0f, 0f, delta.z);
            if (TryMove(body.position + dx, out var p1)) { body.position = p1; stuckT = 0f; return; }
            if (TryMove(body.position + dz, out var p2)) { body.position = p2; stuckT = 0f; return; }

            // Wedged — every lane refused while input is held. A routed actor
            // can't reach this state (edges are transition-certified now),
            // but a PLAYER can steer into a pocket the slide can't exit.
            // Hold ~0.75s → teleport to the nearest free square on same-level
            // ground: an honest "slip out", not a nudge fighting geometry.
            if (delta.sqrMagnitude < 1e-6f) return;
            stuckT += Time.deltaTime;
            if (stuckT < 0.75f || Time.unscaledTime < nextUnstuckProbe) return;
            nextUnstuckProbe = Time.unscaledTime + 0.2f;
            if (world.TryNearestFreeSquare(body.position + half, bodyHalf, dims.y,
                                           2f, out var free))
            {
                Vector3 fc = body.position + half;
                string report = BuildUnstuckReport(fc, fc + delta, free, delta);
                body.position = new Vector3(free.x - half.x, free.y, free.z - half.z);
                stuckT = 0f;
                Debug.Log(report);
            }
        }

        /// <summary>One-emit snag report — WHY every lane refused at the wedge:
        /// the identical 9-column square re-probed at the blocked destination
        /// (failed sample names its height + the owning volume id), the
        /// step-up verdict, and the field's own opinion of both cells. A
        /// green+reachable wedge cell means the MAP lied; red means the map
        /// knew and the player walked past the warning.</summary>
        private string BuildUnstuckReport(Vector3 fc, Vector3 blocked, Vector3 free, Vector3 delta)
        {
            var sb = new System.Text.StringBuilder(384);
            sb.Append($"[Possess] UNSTUCK t={Time.time:F1} held={stuckT:F2}s\n");
            float esc = Mathf.Sqrt((free.x - fc.x) * (free.x - fc.x) + (free.z - fc.z) * (free.z - fc.z));
            sb.Append($"  pos ({fc.x:F2},{fc.y:F2},{fc.z:F2}) → free ({free.x:F2},{free.y:F2},{free.z:F2})  esc {esc:F2}m\n");
            sb.Append($"  input ({delta.x:F3},{delta.z:F3})  body sq {vinny.BodyFootprint:F2} stepup {vinny.stepUpMax:F2}\n");
            sb.Append($"  blocked ({blocked.x:F2},{blocked.y:F2},{blocked.z:F2})\n  sq9: ");
            world.ExplainSquare(blocked, vinny.BodyFootprint * 0.5f, vinny.WorldSize.y, sb);
            sb.Append('\n');
            sb.Append(world.TrySquareStepUp(blocked, vinny.BodyFootprint * 0.5f, vinny.stepUpMax, out float topY)
                ? $"  stepup: top={topY:F2} offered but raised square refused\n"
                : "  stepup: no ledge in band\n");
            if (world.TryColumnTop(blocked, 0.5f, 2f, out float dg))
                sb.Append($"  dst ground={dg:F2} (Δfeet {dg - blocked.y:+0.00;-0.00})\n");
            var f = coverSensor != null ? coverSensor.Field : null;
            if (f != null && f.HasData)
            {
                int ci = f.CellIndexOf(fc), di = f.CellIndexOf(blocked);
                var map = cc != null ? cc.DebugMeMap : null;
                if (ci < 0)
                {
                    // Off the scan window entirely — the field has no opinion
                    // here; say how far out, not just "no cell".
                    Vector3 o = f.Origin; float span = f.N * f.CellSize;
                    float ox = Mathf.Max(0f, Mathf.Max(o.x - fc.x, fc.x - (o.x + span)));
                    float oz = Mathf.Max(0f, Mathf.Max(o.z - fc.z, fc.z - (o.z + span)));
                    sb.Append($"  field: OFF-WINDOW by ({ox:F1},{oz:F1})m of {span:F0}m field at ({o.x:F1},{o.z:F1})");
                }
                else
                {
                    sb.Append($"  field: cell[{ci}] walk={f.Walkable(ci, vinny.WorldSize.y)}");
                    if (map != null && map.stamp == f.BuildStamp) sb.Append($" reach={map.Reaches(ci)}");
                }
                if (di >= 0) sb.Append($"  dst[{di}] walk={f.Walkable(di, vinny.WorldSize.y)}");
                sb.Append('\n');
            }
            sb.Append($"  zone={ZoneCls} reqD={ZoneD:F2}  combat={(cc != null ? cc.State.ToString() : "none")}  duck={(anim != null ? anim.duck : 0f):F2}");
            return sb.ToString();
        }

        /// <summary>Orbit pivot: head height, so the camera frames over his
        /// shoulder rather than orbiting his waist.</summary>
        private Vector3 EyePoint()
        {
            Vector3 dims = vinny.WorldSize;
            return vinny.WorldCenter + Vector3.up * (dims.y * 0.35f);
        }

        /// <summary>Duck scalar label — 0 = stand, 0..1 = stand→shallow morph,
        /// 1 = shallow hold, 1..2 = shallow→deep, 2 = deep hold.</summary>
        public static string CrouchLabel(float duck)
            => duck <= 0.03f ? "FULL (standing)"
             : duck < 0.97f  ? $"morph {duck:0.00} (stand→shallow)"
             : duck <= 1.03f ? "SHALLOW (1.00)"
             : duck < 1.97f  ? $"deep {duck - 1f:0.00} (shallow→deep)"
             :                 "DEEP (2.00)";

        private bool EventSystemIsFree() =>
            UnityEngine.EventSystems.EventSystem.current == null ||
            !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        private void TryPossess()
        {
            vinny = cityMap != null ? cityMap.SpawnedCharacter : null;
            if (vinny == null)
            {
                Debug.Log("[Possess] no spawned character to possess");
                return;
            }
            world = cityMap.GetComponent<VoxelCollisionWorld>();
            mount = vinny.GetComponent<WeaponMount>();
            solver = vinny.GetComponent<ArmAimSolver>();
            if (solver == null) solver = vinny.gameObject.AddComponent<ArmAimSolver>();
            anim = vinny.GetComponent<CharacterAnimation>();
            rig = vinny.GetComponent<CharacterRig>();
            coverSensor = vinny.GetComponent<CoverSensor>();
            if (coverSensor == null) coverSensor = vinny.gameObject.AddComponent<CoverSensor>();
            coverSensor.Refresh(true);   // measured silhouette heights → DuckForTop
            pinnedThreat = coverSensor.Threat;
            if (rig != null) { prevControllable = rig.Controllable; rig.Controllable = false; }
            if (solver != null) { prevSightBeam = solver.showSightBeam; solver.showSightBeam = false; }

            // Borrow the freedom CLAMPS without claiming the MODE — setting
            // debugCameraFreedom itself doubles as CamFree and made the yield
            // return early every frame (Active but all input dead). The user's
            // own F6 flag stays theirs: toggling it mid-possession is the
            // inspect handoff, and it persists through Release.
            prevFreedom = cityMap.borrowedFreedomClamps;
            cityMap.borrowedFreedomClamps = true;
            prevOrbit = cityMap.debugCameraOrbitDistance;
            cityMap.debugCameraOrbitDistance = orbitSuggest;

            // Snap behind him — classic third-person: camera yaw = his facing,
            // slight high angle, orbit pivot pinned at head height.
            Vector3 f0 = vinny.transform.forward;
            f0.y = 0f;
            if (f0.sqrMagnitude < 0.001f) f0 = Vector3.forward;
            lookYaw = Mathf.Atan2(f0.x, f0.z) * Mathf.Rad2Deg;
            lookPitch = 12f;
            boomCur = -1f;
            cityMap.SetCameraAnglesImmediate(lookYaw, lookPitch);
            cityMap.SetCameraFocus(EyePoint());
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            var combat = vinny.GetComponent<CombatController>();
            combatHold = combat != null && combat.State == CombatController.CombatState.InCover;
            if (combatHold)
            {
                holdDepth = Mathf.Max(anim != null ? anim.duck : 0f, combat.TargetDuck);
                holdPos = vinny.transform.position;
            }
            else solver.Arm(vinny.WorldCenter + vinny.transform.forward * 10f);
            Active = true; Body = vinny;
            Debug.Log("[Possess] G=take control ON — WASD move, mouse look, LMB fire; G releases (camera holds)");
        }

        private void Release()
        {
            Active = false; Body = null;
            combatHold = false; HoldActive = false;
            testPose = null;
            pinnedThreat = null;
            if (solver != null && solver.IsAiming) solver.Disarm();
            if (anim != null)
            {
                anim.ReleasePose(CharacterAnimation.PRIORITY_DEBUG);
                anim.SetState(CharacterAnimation.AnimState.Idle);
                anim.duck = 0f;
                anim.coverContext = false;
            }
            if (rig != null) rig.Controllable = prevControllable;
            if (solver != null) solver.showSightBeam = prevSightBeam;
            if (cityMap != null)
            {
                cityMap.borrowedFreedomClamps = prevFreedom;
                cityMap.debugCameraOrbitDistance = prevOrbit;
                // Camera focus deliberately left where it is — that's the "lock".
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("[Possess] released — camera holds current view");
        }

        void OnGUI()
        {
            if (!Active) return;
            var cam = cityMap != null ? cityMap.MapCamera : null;
            if (cam == null) return;

            // Crosshair at the map viewport center
            if (crossTex == null)
            {
                crossTex = new Texture2D(1, 1);
                crossTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.9f));
                crossTex.Apply();
            }
            float cx = cam.rect.x * Screen.width + cam.rect.width * Screen.width * 0.5f;
            float cy = (1f - cam.rect.y) * Screen.height - cam.rect.height * Screen.height * 0.5f;
            GUI.DrawTexture(new Rect(cx - 8f, cy - 0.75f, 16f, 1.5f), crossTex);
            GUI.DrawTexture(new Rect(cx - 0.75f, cy - 8f, 1.5f, 16f), crossTex);

            // Latched test-pose readout under the crosshair
            if (testPose.HasValue)
                GUI.Label(new Rect(cx + 14f, cy - 8f, 260f, 18f),
                    $"pose: {testPose.Value}  (Alt+X releases)");
            // Cover-drive duck readout — the gradient-wall inspection number.
            // Scalar-gated (not state-gated): shows while moving ducked too —
            // the cover-walk pick is Walking pose now, not Crouching.
            if (coverDrive && anim != null && anim.coverContext)
                GUI.Label(new Rect(cx + 14f, cy + 10f, 260f, 18f),
                    $"cover: {CrouchLabel(anim.duck)}");
        }
    }
}
