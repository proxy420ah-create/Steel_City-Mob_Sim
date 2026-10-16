using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Autonomous combat v1 (docs/systems/COMBAT_COVER_STATE.md).
    ///
    /// EnterCombat(threatPos) → scan `coverRadius` for a verified cover point →
    /// SeekingCover steers to it (walk anim, XZ slide on voxel collision) →
    /// InCover: crouch pose + ArmAimSolver aims over the obstacle at the threat.
    /// No cover in radius → HoldPosition: crouch+aim in place.
    ///
    /// While combat holds, rig.Controllable is suppressed (combat owns the pose
    /// lanes via CharacterAnimation.PRIORITY_COMBAT). ExitCombat restores it.
    /// </summary>
    [RequireComponent(typeof(CharacterRig))]
    public class CombatController : MonoBehaviour
    {
        public enum CombatState : byte { Idle = 0, SeekingCover = 1, InCover = 2, HoldPosition = 3 }

        [Header("Movement")]
        [Tooltip("Combat hustle speed (m/s) — drives both the transform step and walk anim rate.")]
        public float moveSpeed = 2.4f;
        public float arriveEps = 0.18f;
        public float turnSpeedDeg = 540f;

        // --- Cover parameters live on CoverSensor (the always-on awareness sense);
        // these stay as proxies so existing HUD sliders keep working. ---
        public float coverRadius { get => Sensor.radius; set => Sensor.radius = value; }
        public float coverStandoff { get => Sensor.standoff; set => Sensor.standoff = value; }
        public float crouchEyeHeight { get => Sensor.crouchEye; set => Sensor.crouchEye = value; }
        public bool showCoverMarkers { get => Sensor.showCandidates; set => Sensor.showCandidates = value; }

        public CombatState State { get; private set; }
        public Vector3 ThreatPos { get; private set; }
        public Vector3 CoverPoint { get; private set; }
        public string CoverObstacleId { get; private set; }
        public string LastResult { get; private set; } = "";
        public CoverSensor.ScanStats lastStats => Sensor.stats;

        private CoverSensor sensor;
        private CoverSensor Sensor =>
            sensor != null ? sensor : (sensor = GetComponent<CoverSensor>() ?? gameObject.AddComponent<CoverSensor>());

        private CharacterRig rig;
        private CharacterAnimation anim;
        private ArmAimSolver solver;
        private VoxelCharacter vox;
        private VoxelCollisionWorld world;
        private bool prevControllable;

        void Awake()
        {
            rig = GetComponent<CharacterRig>();
            vox = GetComponent<VoxelCharacter>();
            sensor = GetComponent<CoverSensor>();
        }

        /// <summary>Enter combat against a threat at `threatPos`. Re-calling re-evaluates cover.</summary>
        public bool EnterCombat(Vector3 threatPos)
        {
            if (world == null) world = FindFirstObjectByType<VoxelCollisionWorld>();
            if (anim == null) anim = GetComponent<CharacterAnimation>();
            if (solver == null) solver = GetComponent<ArmAimSolver>();
            if (rig == null || anim == null || world == null)
            { LastResult = "missing rig/anim/collision world"; return false; }

            if (State == CombatState.Idle)
            {
                prevControllable = rig.Controllable;
                rig.Controllable = false;
            }
            ThreatPos = threatPos;

            Sensor.Threat = threatPos;
            Sensor.Refresh(true);
            if (Sensor.TryPickCover(Feet(), out Vector3 p, out string oid))
            {
                CoverPoint = p;
                CoverObstacleId = oid;
                State = CombatState.SeekingCover;
                anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                anim.walkSpeed = Mathf.Clamp(moveSpeed, 0.5f, 2f);
                LastResult = $"seeking cover behind {oid}";
                return true;
            }

            // No cover — hold, crouch, aim. Still a state change the sim can react to.
            State = CombatState.HoldPosition;
            CoverPoint = Feet();
            CoverObstacleId = null;
            CrouchAndAim();
            LastResult = $"no cover within {Sensor.radius:F0}m — holding position";
            return true;
        }

        public void ExitCombat()
        {
            anim?.ReleasePose(CharacterAnimation.PRIORITY_COMBAT);
            anim?.SetState(CharacterAnimation.AnimState.Idle);
            if (solver != null && solver.IsAiming) solver.Disarm();
            if (rig != null) rig.Controllable = prevControllable;
            State = CombatState.Idle;
            CoverObstacleId = null;
            LastResult = "";
            // Threat released — the sensor keeps its passive wall awareness
            // drawn (the COVER tab owns those toggles).
            if (sensor != null) { sensor.Threat = null; sensor.Refresh(true); }
        }

        void Update()
        {
            if (State == CombatState.Idle) return;
            // Direct control wins — possessing Vinny hands the rig to the player.
            if (PossessController.Active) { ExitCombat(); return; }
            if (State != CombatState.SeekingCover) return;

            Vector3 feet = Feet();
            Vector3 to = CoverPoint - feet;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist <= arriveEps)
            {
                // Snap onto the probed ground at the cover point, then crouch+aim.
                var pos = transform.position;
                pos.y += CoverPoint.y - feet.y;
                transform.position = pos;
                State = CombatState.InCover;
                CrouchAndAim();
                LastResult = $"in cover behind {CoverObstacleId}";
                return;
            }

            Vector3 dir = to / dist;
            MoveWithCollision(dir * Mathf.Min(moveSpeed * Time.deltaTime, dist));

            float wantYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, wantYaw, turnSpeedDeg * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void CrouchAndAim()
        {
            anim?.RequestPose(CharacterAnimation.AnimState.Crouching, CharacterAnimation.PRIORITY_COMBAT);
            Vector3 to = ThreatPos - Feet();
            to.y = 0f;
            if (to.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
            if (solver != null)
            {
                solver.Arm(ThreatPos);
                solver.SetTarget(ThreatPos);
                aimTick = true;
            }
        }

        // Keep the solver's target pinned while in cover (threat is static v1,
        // but the solver expects per-frame target refresh like possess does).
        private bool aimTick;
        void LateUpdate()
        {
            if (!aimTick) return;
            if (solver != null && solver.IsAiming) solver.SetTarget(ThreatPos);
            else aimTick = false;
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
