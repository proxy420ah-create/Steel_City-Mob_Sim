using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Closed-form 2-bone arm IK for the voxel-group rig — ports the SteelTide
    /// ArmIKDrive math (law-of-cosines elbow placement, twist-constrained joint
    /// bases, reach clamping) onto the compute-shader pose pipeline.
    ///
    /// Instead of driving ConfigurableJoints it writes model-space rotation
    /// overrides into two channels that must stay in sync:
    ///   1. VoxelCharacterAnimator.ikOverrides — CPU pose (drives the WeaponMount
    ///      weld, so the muzzle follows the solved hand exactly).
    ///   2. InstancedCharacter.ik* fields — packed into _InstanceIKData and
    ///      applied inside CharacterPoseCompute.CSPose for the rendered voxels.
    ///
    /// Shoulder (arm gid) solves elbow placement via law of cosines; forearm
    /// (hand gid) slams the measured bore axis (bore_root → muzzle) onto the
    /// target line directly — the weapon's own authored geometry is the aim
    /// truth, not an assumed hand axis.
    /// </summary>
    public class ArmAimSolver : MonoBehaviour
    {
        [Header("Tuning")]
        [Tooltip("Fraction of full arm extension allowed — keeps the elbow slightly bent at max reach")]
        [Range(0.5f, 1f)] public float reachFraction = 0.97f;
        [Tooltip("Blend ramp speed on engage (higher = snappier raise)")]
        public float blendInSpeed = 2.5f;
        [Tooltip("Blend ramp speed on release")]
        public float blendOutSpeed = 3.5f;
        [Tooltip("Elbow preferred direction in model space — projected perp to the aim line")]
        public Vector3 elbowHint = new Vector3(0.4f, -1f, 0.2f);
        [Tooltip("Roll reference for the upper arm basis (model space)")]
        public Vector3 armTwistRef = new Vector3(0f, 0f, 1f);
        [Tooltip("Azimuth offset applied to the target direction (deg, model space Y)")]
        public float azimuthOffsetDeg = 0f;
        [Tooltip("Elevation offset applied to the target direction (deg)")]
        public float elevationOffsetDeg = 0f;

        [Header("Torso Assist (turret)")]
        [Tooltip("Rotate the upper body (gid 0) toward the target — torso takes the gross yaw, arm does fine aim")]
        public bool torsoAssist = true;
        [Tooltip("Fraction of the yaw error the torso absorbs; the arm solves the residual")]
        [Range(0f, 1f)] public float torsoShare = 0.6f;
        [Tooltip("Max torso yaw (deg)")]
        public float maxTorsoYawDeg = 70f;
        [Tooltip("Sign flip if the torso yaws away from the target (model-forward convention)")]
        public float torsoYawSign = 1f;
        [Tooltip("Gun-side blade (AIM_RIG phase 2): extra waist yaw that swings the gun shoulder's azimuth toward the aim azimuth — the boxing-stance lead. SignedAngle makes it handedness-correct automatically.")]
        [Range(0f, 1f)] public float torsoBladeShare = 0.5f;
        [Tooltip("Max blade yaw (deg)")]
        public float torsoBladeMaxDeg = 40f;
        [Tooltip("Forward stance pitch share of target elevation — posture hunch, not aim correction")]
        [Range(0f, 1f)] public float torsoPitchShare = 0.25f;
        [Tooltip("Max stance pitch (deg)")]
        public float torsoPitchMaxDeg = 12f;
        [Tooltip("Sign flip if the stance pitch hunches the wrong way")]
        public float torsoPitchSign = 1f;
        [Tooltip("Allow the torso stance during AimWalk (aiming while locomoting): legs run the route, upper body tracks the target — reads far more natural than arms-only aim on the move. Plain Walking stays suspended.")]
        public bool bladeWhileAimWalk = true;
        public bool debugLogs = false;

        [Header("Aim Feel — sway + recoil (COMBAT_VEHICLE_DESIGN.md §14)")]
        [Tooltip("Second-order spring offsets the target point before the IK solve: sway wanders the equilibrium, each shot imparts a recoil impulse. The bored direction — and therefore the next shot — physically deflects and recovers.")]
        public bool aimFeelEnabled = true;
        [Tooltip("Shooter steadiness 1-10 — placeholder for the hood's Firearms stat; same scale as ProjectileSystem.shooterSkill")]
        [Range(1, 10)] public int feelSkill = 10;
        [Tooltip("Sway amplitude at skill 10 (deg). Scales up ×SkillSpreadMultiplier as skill drops.")]
        [Range(0f, 3f)] public float swayDeg = 1.5f;
        [Tooltip("Recoil kick magnitude (deg of aim deflection) — a property of the weapon, not the shooter")]
        [Range(0f, 6f)] public float kickDeg = 5f;
        [Tooltip("Seconds to re-settle after a kick at skill 10; widens ~0.3× per missing skill point")]
        [Range(0.05f, 0.6f)] public float settleSec = 0.5f;
        [Tooltip("Damping ratio: <1 rings (lively but wobbly), 1 = critical, >1 overdamped (calm, no overshoot). Anti-flail lives here.")]
        [Range(0.6f, 1.6f)] public float dampingRatio = 1.05f;
        [Tooltip("Hard cap on aim offset (deg) — anti-windup; the arm can never be pushed further than this")]
        [Range(2f, 20f)] public float maxOffsetDeg = 11f;
        [Tooltip("Draw the cyan muzzle→target sight beam while aiming (PathDebugRenderer).")]
        public bool showSightBeam = false;   // off by default — Combat/Sfx debugs opt in
        [Tooltip("Head track blend while aiming (AIM_RIG phase 1): the head's gid-1 quat looks along eye→target — same solve the JS animator previews as ikAim.headTrack. 0 = authored head pose.")]
        [Range(0f, 1f)] public float headTrack = 1f;

        // --- State ---
        public bool IsAiming { get; private set; }
        public float Blend { get; private set; }
        public Vector3 TargetWorld { get; private set; }
        public string LastError { get; private set; }
        public float LastAngularErrorDeg { get; private set; }
        public float LastTargetDistanceM { get; private set; }
        public int ArmGid { get; private set; } = -1;
        public int ForearmGid { get; private set; } = -1;

        private VoxelCharacter character;
        private CharacterAnimation animRef;   // posture gate — crouched = taking cover
        private WeaponMount mount;
        private VoxelChunkManager.InstancedCharacter instHandle;
        private string sightBeamKey;
        private Quaternion torsoQuat = Quaternion.identity; // gid-0 override from torso assist
        private Quaternion headQuat = Quaternion.identity;  // gid-1 override — head aim (headTrack)
        private bool headOn;
        const float NeckClampDeg = 77.2f;   // head solve yaw clamp (±1.35 rad) — torso shares up past this
        private AimFeelSpring feelSpring;
        private Vector2 swaySeed;
        private bool shotHooked;

        /// <summary>Current spring offset magnitude in degrees — live wobble readout for the HUD.</summary>
        public float AimOffsetDeg => feelSpring.pos.magnitude * Mathf.Rad2Deg;

        /// <summary>Engage the solver toward a world-space target. Call once per toggle-on.</summary>
        public bool Arm(Vector3 targetWorld)
        {
            string err = Validate();
            if (err != null)
            {
                LastError = err;
                Debug.LogWarning($"[ArmAimSolver] Arm rejected: {err}");
                return false;
            }
            // Exposure doctrine (EXPOSURE_ENGAGEMENT.md): ANY pose may aim —
            // the visibility gate upstream decides armed-vs-stowed, not
            // posture. Deep duck drops torso ASSIST inside Solve (the assist
            // yaw would un-fold the crouch lean — the IK_TORSO_OVERRIDE bug),
            // but the arm chain still reaches on its own.
            TargetWorld = targetWorld;
            IsAiming = true;
            LastError = null;

            // Recoil impulse source — subscribed once; kicks land via OnShotFired.
            if (!shotHooked)
            {
                ProjectileSystem.GetOrCreate().ShotFired += OnShotFired;
                shotHooked = true;
                int h = GetInstanceID();
                swaySeed = new Vector2((h * 2654435761u % 1000u) / 1000f * 37f, (h * 2246822519u % 1000u) / 1000f * 51f);
            }

            if (debugLogs)
                Debug.Log($"[ArmAimSolver] Engaged — arm gid {ArmGid}, forearm gid {ForearmGid}, target {targetWorld}");
            return true;
        }

        /// <summary>Release — blend ramps down, then the override channel clears.</summary>
        public void Disarm()
        {
            IsAiming = false;
            if (debugLogs) Debug.Log("[ArmAimSolver] Released");
        }

        /// <summary>Move the aim point while engaged (also safe pre-arm).</summary>
        public void SetTarget(Vector3 targetWorld) => TargetWorld = targetWorld;

        /// <summary>Null when the solver has everything it needs; otherwise a failure reason.</summary>
        public string Validate()
        {
            if (character == null) character = GetComponent<VoxelCharacter>();
            if (character == null || !character.IsInitialized) return "no initialized VoxelCharacter";
            if (mount == null) mount = GetComponent<WeaponMount>();
            if (mount == null || !mount.IsEquipped) return "no weapon mounted";
            if (mount.Animator == null) return "mount animator not ready";
            if (mount.HandGid < 0) return "no hand attachment gid";

            int fore = mount.HandGid;
            int arm = mount.Animator.GetParentGroup(fore);
            if (!mount.Pivots.ContainsKey(fore)) return $"no pivot for hand group {fore}";
            if (arm >= 0 && !mount.Pivots.ContainsKey(arm)) return $"no pivot for arm group {arm}";
            ForearmGid = fore;
            ArmGid = arm; // -1 → single-bone fallback (bore aimed at the shoulder group itself)
            return null;
        }

        void Update()
        {
            if (animRef == null) animRef = GetComponent<CharacterAnimation>();
            if (!IsAiming && Blend <= 0f) return;

            Blend = Mathf.MoveTowards(Blend, IsAiming ? 1f : 0f,
                Time.deltaTime * (IsAiming ? blendInSpeed : blendOutSpeed));

            string err = Validate();
            if (err != null)
            {
                // Context lost mid-aim (weapon unequipped etc) — release rather than freeze.
                LastError = err;
                IsAiming = false;
            }
            else if (IsAiming) LastError = null;

            if (aimFeelEnabled)
                StepAimFeel();
            else if (feelSpring.pos != Vector2.zero || feelSpring.vel != Vector2.zero)
                feelSpring.Reset();

            if (Blend > 0.0001f && ForearmGid >= 0 && mount != null && mount.Animator != null)
            {
                Solve(PerturbTarget(TargetWorld), out Quaternion qArm, out Quaternion qFore);
                WriteChannels(qArm, qFore);
            }

            if (!IsAiming && Blend <= 0f)
                ReleaseInstant();
        }

        /// <summary>Drop every IK channel now — shared by blend-out completion
        /// and the crouch gate (which skips the ramp entirely).</summary>
        private void ReleaseInstant()
        {
            IsAiming = false;
            Blend = 0f;
            feelSpring.Reset();
            if (mount != null && mount.Animator != null)
            {
                mount.Animator.ikOverrides.Clear();
                mount.Animator.ikBlend = 1f;
            }
            if (instHandle != null) { instHandle.ikEnabled = false; instHandle.ikTorsoEnabled = false; instHandle.ikHeadEnabled = false; instHandle.ikPair2Enabled = false; }
        }

        // ------------------------------------------------------------------
        // Aim feel — sway + recoil spring (COMBAT_VEHICLE_DESIGN.md §14)
        // ------------------------------------------------------------------

        /// <summary>
        /// Integrate the spring toward a wandering sway offset. Both the wander amplitude and
        /// the settle time scale with feelSkill — a steady shooter sways less and recovers faster.
        /// </summary>
        private void StepAimFeel()
        {
            // Lazy caps (struct can't field-init) — the anti-windup bounds.
            if (feelSpring.maxPos <= 0f)
            {
                feelSpring.maxPos = maxOffsetDeg * Mathf.Deg2Rad;
                feelSpring.maxVel = 5f;   // rad/s
            }
            else
                feelSpring.maxPos = maxOffsetDeg * Mathf.Deg2Rad;   // live-tunable cap

            float mult = ProjectileSystem.SkillSpreadMultiplier(feelSkill);           // sway amplitude
            float settle = settleSec * (1f + (10 - Mathf.Clamp(feelSkill, 1, 10)) * 0.30f); // recovery time
            float swayRad = swayDeg * Mathf.Deg2Rad * mult;
            AimFeelSpring.Gains(settle, dampingRatio, out float k, out float c);
            feelSpring.Step(SwayTarget(swayRad), k, c, Time.deltaTime);
        }

        /// <summary>
        /// Two-octave Perlin wander, per-instance seeded. Slow breathing-scale drift plus a
        /// gentler tremor component — frequencies kept low so the arm reads as a steady hand
        /// correcting, not a servo chasing jitter. Returns radians on (x = up, y = right).
        /// </summary>
        private Vector2 SwayTarget(float amp)
        {
            if (amp <= 0f) return Vector2.zero;
            float t = Time.time;
            float x = (Mathf.PerlinNoise(t * 0.22f + swaySeed.x, 3.7f) * 2f - 1f)
                    + (Mathf.PerlinNoise(t * 0.9f + swaySeed.x, 8.1f) * 2f - 1f) * 0.3f;
            float y = (Mathf.PerlinNoise(3.7f, t * 0.19f + swaySeed.y) * 2f - 1f)
                    + (Mathf.PerlinNoise(8.1f, t * 0.8f + swaySeed.y) * 2f - 1f) * 0.3f;
            return new Vector2(x, y) * (amp / 1.4f);
        }

        /// <summary>
        /// Rotate the aim point about the muzzle by the current spring offset (scaled by Blend),
        /// upstream of the whole solve — torso assist and the elbow both chase the perturbed
        /// point, and WeaponMount reports the deflected bore axis to the next shot.
        /// </summary>
        private Vector3 PerturbTarget(Vector3 target)
        {
            if (!aimFeelEnabled || mount == null) return target;
            Vector2 o = feelSpring.pos * Blend;   // rad: x = up(+), y = right(+)
            if (o.sqrMagnitude < 1e-10f) return target;

            Vector3 to = target - mount.MuzzleWorld;
            if (to.sqrMagnitude < 1e-6f) return target;
            Vector3 right = Vector3.Cross(Vector3.up, to.normalized);
            if (right.sqrMagnitude < 1e-6f) return target;
            right.Normalize();

            Quaternion off = Quaternion.AngleAxis(-o.x * Mathf.Rad2Deg, right)   // +x offset = muzzle up
                           * Quaternion.AngleAxis(o.y * Mathf.Rad2Deg, Vector3.up);
            return mount.MuzzleWorld + off * to;
        }

        /// <summary>
        /// Recoil impulse from ProjectileSystem — subscribed in Arm(). Only kicks when this
        /// solver's own weapon fired while aiming; lateral component seeded by the shot so
        /// replayed seeds reproduce the same kick direction.
        /// </summary>
        private void OnShotFired(WeaponMount firedMount, ProjectileSystem.ShotReport shot)
        {
            if (firedMount != mount || !aimFeelEnabled) return;
            float kickRad = kickDeg * Mathf.Deg2Rad;
            float lateral = (ProjectileSystem.Hash01(shot.seed ^ 0xA5A5A5A5u) - 0.5f) * 2f * 0.4f;
            feelSpring.Impulse(new Vector2(kickRad, kickRad * lateral));   // mostly up, slight seeded yaw
        }

        /// <summary>
        /// Solve shoulder + forearm ownRot overrides in uncentered voxel space
        /// (the frame the FK chain rotates in). qFore is invalid when ArmGid &lt; 0.
        /// </summary>
        private void Solve(Vector3 targetWorld, out Quaternion qArm, out Quaternion qFore)
        {
            qArm = Quaternion.identity;
            qFore = Quaternion.identity;

            var dims = mount.Dims;
            float vs = mount.CharVoxelSize;
            Vector3 dimsF = new Vector3(dims.x, dims.y, dims.z);

            // Same world↔voxel bridge as WeaponMount's weld:
            // world = center + yawRot * ((v + 0.5)*vs - half)
            Vector3 half = dimsF * vs * 0.5f;
            Vector3 center = transform.position + half;
            Quaternion yawRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            Vector3 sv = ScalePivot(mount.Pivots[ArmGid >= 0 ? ArmGid : ForearmGid], dimsF);
            Vector3 tv = WorldToVoxel(targetWorld, center, yawRot, half, vs);

            // Torso assist (turret): yaw gid 0 toward the target about the body
            // pivot, then solve the arm in the PRE-torso frame — the chain applies
            // bodyRot outermost (T * qArm * qFore), so transforming the target by
            // the effective inverse yaw keeps the bore landing exactly.
            // EXCEPT the deep band (duck>1.02): the assist yaw un-folds the
            // crouch lean (IK_TORSO_OVERRIDE), so deep-duck aiming is arms-only.
            // AND while WALKING UN-AIMED: the root owns heading then — a second
            // heading driver on the same spine is the seek-time tug-of-war.
            // AimWalk keeps the stance (bladeWhileAimWalk): aiming at a live
            // target while moving demands the upper body track too — legs run
            // the route, torso serves the aim.
            bool noStance = animRef != null
                && (animRef.EffectiveState == CharacterAnimation.AnimState.Walking
                    || (animRef.EffectiveState == CharacterAnimation.AnimState.AimWalk && !bladeWhileAimWalk));
            torsoQuat = Quaternion.identity;
            if (torsoAssist && !noStance && (animRef == null || animRef.duck <= 1.02f))
            {
                Vector3 stFlat = tv - sv;
                stFlat.y = 0f;
                if (stFlat.sqrMagnitude > 1e-6f)
                {
                    Vector3 bp = mount.Pivots.TryGetValue(0, out var bp0)
                        ? ScalePivot(bp0, dimsF)
                        : dimsF * 0.5f;

                    // Yaw blade — fraction of the flat aim error. Share-UP rule
                    // (AIM_RIG §3.2): when the residual yaw after the torso's
                    // share would still clamp the neck short of F, the torso
                    // absorbs more so head + torso together reach.
                    float yawErr = Vector3.SignedAngle(Vector3.forward, stFlat, Vector3.up);
                    float share = torsoShare;
                    float yawAbs = Mathf.Abs(yawErr);
                    if (yawAbs > NeckClampDeg)
                        share = Mathf.Max(share, 1f - NeckClampDeg / yawAbs);
                    Quaternion qYaw = Quaternion.AngleAxis(
                        Mathf.Clamp(yawErr * torsoYawSign * share,
                            -maxTorsoYawDeg, maxTorsoYawDeg), Vector3.up);

                    // Blade — stance yaw about the waist (AIM_RIG phase 2):
                    // swing the gun-side shoulder's azimuth toward the aim
                    // azimuth so the gun side leads (boxing stance). SignedAngle
                    // is handedness-correct: the solver's own arm pivot picks
                    // the side. Composes with the aim-share yaw on the same axis.
                    Quaternion qBlade = Quaternion.identity;
                    Vector3 sOff = sv - bp; sOff.y = 0f;
                    Vector3 aFlat = tv - bp; aFlat.y = 0f;
                    if (torsoBladeShare > 0f
                        && sOff.sqrMagnitude > 1e-6f && aFlat.sqrMagnitude > 1e-6f)
                    {
                        float bladeDeg = Mathf.Clamp(
                            Vector3.SignedAngle(sOff.normalized, aFlat.normalized, Vector3.up)
                                * torsoBladeShare * torsoYawSign,
                            -torsoBladeMaxDeg, torsoBladeMaxDeg);
                        qBlade = Quaternion.AngleAxis(bladeDeg, Vector3.up);
                    }

                    // Stance pitch — a modest hunch with target elevation
                    // (posture, not aim correction). Sign param: model-space
                    // conventions are verified at runtime, not assumed.
                    Quaternion qPitch = Quaternion.identity;
                    if (torsoPitchShare > 0f)
                    {
                        Vector3 dAim = tv - sv;
                        Vector3 rAxis = Vector3.Cross(dAim, Vector3.up);
                        if (rAxis.sqrMagnitude > 1e-6f && dAim.sqrMagnitude > 1e-6f)
                        {
                            float elev = Mathf.Asin(Mathf.Clamp(dAim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                            qPitch = Quaternion.AngleAxis(
                                Mathf.Clamp(-elev * torsoPitchShare * torsoPitchSign,
                                    -torsoPitchMaxDeg, torsoPitchMaxDeg), rAxis.normalized);
                        }
                    }

                    torsoQuat = qPitch * qBlade * qYaw;   // extrinsic: blade+share yaw → stance pitch
                    Quaternion torsoEff = Quaternion.Slerp(Quaternion.identity, torsoQuat, Blend);
                    tv = bp + Quaternion.Inverse(torsoEff) * (tv - bp);
                }
            }

            // Az/el tuning offsets rotate the target about the shoulder
            if (azimuthOffsetDeg != 0f || elevationOffsetDeg != 0f)
            {
                Vector3 toRaw = tv - sv;
                Vector3 up = Vector3.up;
                Vector3 right = Vector3.Cross(up, toRaw.normalized);
                if (right.sqrMagnitude > 1e-6f)
                    toRaw = Quaternion.AngleAxis(azimuthOffsetDeg, up)
                          * Quaternion.AngleAxis(elevationOffsetDeg, right.normalized)
                          * toRaw;
                tv = sv + toRaw;
            }

            Vector3 st = tv - sv;
            float dist = st.magnitude;
            Vector3 toT = dist > 1e-6f ? st / dist : Vector3.forward;
            LastTargetDistanceM = dist * vs;

            // Weapon bore axis in the hand group's model-space frame:
            // worldBore = yawRot * qR * basisB * attachRot * boreAxisLocal
            Vector3 K = (mount.BasisB * mount.ItemAttachRot * mount.BoreAxisLocal).normalized;
            Vector3 wUp = mount.BasisB * mount.ItemAttachRot * Vector3.up;

            // Desired cumulative rotation on the hand's group: bore → toT, sights → up.
            Quaternion qRDesired = BasisDelta(K, toT, wUp, Vector3.up);

            if (ArmGid < 0)
            {
                // No elbow available — slam the bore axis at the hand's own group.
                qArm = qRDesired;
            }
            else
            {
                Vector3 ev = ScalePivot(mount.Pivots[ForearmGid], dimsF);
                Vector3 hv = mount.HandPoint;
                float L1 = (ev - sv).magnitude;
                float L2 = (hv - ev).magnitude;

                if (L1 < 1e-4f || L2 < 1e-4f)
                {
                    qArm = qRDesired; // degenerate chain — single-bone fallback
                }
                else
                {
                    // Law-of-cosines elbow: clamp reach, place elbow in the bend plane
                    float d = Mathf.Clamp(dist, Mathf.Abs(L1 - L2) + 0.01f, (L1 + L2) * reachFraction);
                    float cosA = Mathf.Clamp((L1 * L1 + d * d - L2 * L2) / (2f * L1 * d), -1f, 1f);
                    float angA = Mathf.Acos(cosA);
                    Vector3 bendRef = PerpOf(elbowHint, toT);
                    Vector3 elbowDir = (toT * Mathf.Cos(angA) + bendRef * Mathf.Sin(angA)).normalized;

                    // Shoulder: bind arm direction → solved elbow direction, twist-stable
                    Vector3 bindArm = (ev - sv).normalized;
                    qArm = BasisDelta(bindArm, elbowDir, armTwistRef, armTwistRef);

                    // Forearm: cumulative must equal qRDesired → ownRot = inv(qArm) * qRDesired
                    qFore = Quaternion.Inverse(qArm) * qRDesired;
                }
            }

            // Head aim (AIM_RIG phase 1) — the unified gid-1 quat channel, solved in
            // the SAME pre-torso frame as the arm (tv already carries the torso-assist
            // inverse): look the bind-forward head at the target, neck-clamped —
            // yaw ±1.35 rad (existing clamp), pitch ±0.9. headTrack blends it.
            headOn = false;
            if (headTrack > 0.001f && mount.Pivots.TryGetValue(1, out var hpiv))
            {
                headOn = true;
                Vector3 hv = ScalePivot(hpiv, dimsF);
                Vector3 dh = tv - hv;
                if (dh.sqrMagnitude > 1e-6f)
                {
                    dh /= dh.magnitude;
                    float hy = Mathf.Clamp(Mathf.Atan2(dh.x, dh.z), -1.35f, 1.35f);
                    float hp2 = Mathf.Clamp(Mathf.Atan2(dh.y, Mathf.Sqrt(dh.x * dh.x + dh.z * dh.z)), -0.9f, 0.9f);
                    var dc = new Vector3(Mathf.Sin(hy) * Mathf.Cos(hp2), Mathf.Sin(hp2), Mathf.Cos(hy) * Mathf.Cos(hp2));
                    headQuat = Quaternion.Slerp(Quaternion.identity,
                        BasisDelta(Vector3.forward, dc, Vector3.up, Vector3.up), headTrack);
                }
                else headOn = false;
            }

            // Real angular error: welded bore dir vs muzzle→target (1 frame stale — ground truth)
            Vector3 toTargetWorld = targetWorld - mount.MuzzleWorld;
            if (mount.IsEquipped && toTargetWorld.sqrMagnitude > 1e-8f)
                LastAngularErrorDeg = Vector3.Angle(mount.AimDirection, toTargetWorld);
        }

        /// <summary>Push the solve into both the CPU animator (weld) and GPU per-instance channel.</summary>
        private void WriteChannels(Quaternion qArm, Quaternion qFore)
        {
            var animator = mount.Animator;
            animator.ikBlend = Blend;
            int armSlot = ArmGid >= 0 ? ArmGid : ForearmGid;
            animator.ikOverrides[armSlot] = qArm;
            if (ArmGid >= 0) animator.ikOverrides[ForearmGid] = qFore;
            // Torso override rides gid 0 — propagates to arm/forearm chains via
            // the body-transform path (outermost rotation in the FK chain).
            if (torsoAssist) animator.ikOverrides[0] = torsoQuat;
            else animator.ikOverrides.Remove(0);
            // Head override rides gid 1 — CPU and GPU get the identical quat.
            if (headOn) animator.ikOverrides[1] = headQuat;
            else animator.ikOverrides.Remove(1);

            instHandle = character.GetInstancedHandle(); // re-fetch — handle can change on re-registration
            if (instHandle != null)
            {
                instHandle.ikEnabled = true;
                instHandle.ikBlend = Blend;
                instHandle.ikGidA = armSlot;
                instHandle.ikQuatA = qArm;
                instHandle.ikGidB = ArmGid >= 0 ? ForearmGid : -1;
                instHandle.ikQuatB = ArmGid >= 0 ? qFore : Quaternion.identity;
                instHandle.ikTorsoEnabled = torsoAssist;
                instHandle.ikTorsoQuat = torsoQuat;
                instHandle.ikHeadEnabled = headOn;
                instHandle.ikHeadQuat = headOn ? headQuat : Quaternion.identity;
            }

            // Sight beam — muzzle → target while engaged (residual aim error is
            // visible against WeaponMount's red bore ray).
            var pdr = PathDebugRenderer.Instance;
            if (pdr != null)
            {
                if (sightBeamKey == null) sightBeamKey = $"aim_sight_{GetInstanceID()}";
                if (IsAiming && showSightBeam)
                    pdr.SetCustomBeam(sightBeamKey, mount.MuzzleWorld, TargetWorld,
                        0.006f, new Color(0.2f, 1f, 1f, 0.55f));
                else
                    pdr.ClearCustomBeam(sightBeamKey);
            }
        }

        // --- Math helpers (voxel-space, matching animator/shader conventions) ---

        internal static Vector3 ScalePivot(Vector3 normalizedPivot, Vector3 dimsF)
            => new Vector3(normalizedPivot.x * dimsF.x, normalizedPivot.y * dimsF.y, normalizedPivot.z * dimsF.z);

        /// <summary>Inverse of the weld's world = center + yawRot * ((v+0.5)*vs - half).</summary>
        internal static Vector3 WorldToVoxel(Vector3 w, Vector3 center, Quaternion yawRot, Vector3 half, float vs)
        {
            Vector3 local = Quaternion.Inverse(yawRot) * (w - center);
            return (local + half) / vs - Vector3.one * 0.5f;
        }

        internal static Vector3 PerpOf(Vector3 refV, Vector3 f)
        {
            Vector3 u = refV - f * Vector3.Dot(refV, f);
            if (u.sqrMagnitude < 1e-6f)
                u = Vector3.Cross(f, Mathf.Abs(f.y) < 0.9f ? Vector3.up : Vector3.right);
            return u.normalized;
        }

        /// <summary>
        /// Twist-stable rotation taking bindF→solF (SteelTide SolveJointRotationWithTwist port):
        /// full bases built from projected refs, so no arbitrary roll about the bone axis.
        /// refB/refS are projected perp to bindF/solF respectively.
        /// </summary>
        internal static Quaternion BasisDelta(Vector3 bindF, Vector3 solF, Vector3 refB, Vector3 refS)
        {
            Quaternion bBind = Quaternion.LookRotation(bindF, PerpOf(refB, bindF));
            Quaternion bSol = Quaternion.LookRotation(solF, PerpOf(refS, solF));
            return bSol * Quaternion.Inverse(bBind);
        }


    }
}
