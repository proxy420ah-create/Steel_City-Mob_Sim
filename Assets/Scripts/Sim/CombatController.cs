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
    /// Combat poses ride the PRIORITY_COMBAT lane; debug keys (PRIORITY_DEBUG)
    /// outrank them and stay live during combat — a held debug pose also owns
    /// the crouch-depth channel (the controller's depth ease yields).
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
        [Tooltip("Crouch-depth ease rate (1/s) — the settle into a deeper duck")]
        public float crouchDepthSpeed = 3f;

        // --- Cover parameters live on CoverSensor (the always-on awareness sense);
        // these stay as proxies so existing HUD sliders keep working. ---
        public float coverRadius { get => Sensor.radius; set => Sensor.radius = value; }
        public float coverStandoff { get => Sensor.standoff; set => Sensor.standoff = value; }

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
        /// <summary>Crouch depth the picked cover demands — eased toward on
        /// arrival so a low wall visibly sinks him instead of snapping.</summary>
        private float targetCrouchDepth;
        private CoverSensor.WallClass coverWallClass = CoverSensor.WallClass.Crouch;

        void Awake()
        {
            rig = GetComponent<CharacterRig>();
            vox = GetComponent<VoxelCharacter>();
            sensor = GetComponent<CoverSensor>();
        }

        /// <summary>Enter combat against a threat at `threatPos`. Re-calling re-evaluates cover.</summary>
        public bool EnterCombat(Vector3 threatPos) => EnterCombatCore(threatPos, null, 0f);

        /// <summary>Debug variant — seek the Hidden cell nearest `coverAnchor`
        /// (pit pick buttons). Same field validation; targets a specific box.</summary>
        public bool EnterCombat(Vector3 threatPos, Vector3 coverAnchor, float maxDist = 2.5f)
            => EnterCombatCore(threatPos, coverAnchor, maxDist);

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

            Sensor.Threat = threatPos;
            Sensor.Refresh(true);
            bool picked = coverAnchor.HasValue
                ? Sensor.TryPickCoverNear(coverAnchor.Value, maxDist, out Vector3 p, out string oid, out float reqD, out coverWallClass)
                : Sensor.TryPickCover(Feet(), out p, out oid, out reqD, out coverWallClass);
            if (picked)
            {
                CoverPoint = p;
                CoverObstacleId = oid;
                targetCrouchDepth = reqD;
                State = CombatState.SeekingCover;
                anim.RequestPose(CharacterAnimation.AnimState.Walking, CharacterAnimation.PRIORITY_COMBAT);
                anim.walkSpeed = Mathf.Clamp(moveSpeed, 0.5f, 2f);
                LastResult = $"seeking cover behind {oid} ({coverWallClass}, depth {reqD:F2})";
                Debug.Log($"🥊 [Combat] {name} — picked {oid} ({coverWallClass}, reqDepth {reqD:F2})");
                return true;
            }
            if (coverAnchor.HasValue)
            { LastResult = $"no valid cover near that anchor"; return false; }

            // No cover — hold and crouch in place. Still a state change the sim can react to.
            State = CombatState.HoldPosition;
            CoverPoint = Feet();
            CoverObstacleId = null;
            coverWallClass = CoverSensor.WallClass.Crouch;
            targetCrouchDepth = 0f;
            CrouchAndCover();
            LastResult = $"no cover within {Sensor.radius:F0}m — holding position";
            return true;
        }

        public void ExitCombat()
        {
            targetCrouchDepth = 0f;
            if (anim != null) anim.crouchDepth = 0f;
            anim?.ReleasePose(CharacterAnimation.PRIORITY_COMBAT);
            anim?.SetState(CharacterAnimation.AnimState.Idle);
            if (solver != null && solver.IsAiming) solver.Disarm();
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
            // Ease crouch depth toward the picked cover's requirement — the
            // settle into a deeper duck rides the same pose eval, no snap.
            // Yields while a debug pose holds priority — don't fight the keys.
            if (anim != null && !anim.DebugOwnsPose && !Mathf.Approximately(anim.crouchDepth, targetCrouchDepth))
                anim.crouchDepth = Mathf.MoveTowards(anim.crouchDepth, targetCrouchDepth,
                    crouchDepthSpeed * Time.deltaTime);
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
                State = CombatState.InCover;
                CrouchAndCover();
                LastResult = $"in cover behind {CoverObstacleId} ({coverWallClass}, depth {targetCrouchDepth:F2})";
                Debug.Log($"🥊 [Combat] {name} — in cover, pose {(coverWallClass == CoverSensor.WallClass.Full ? "STAND (Idle)" : $"crouch@{targetCrouchDepth:F2}")}");
                return;
            }

            Vector3 dir = to / dist;
            MoveWithCollision(dir * Mathf.Min(moveSpeed * Time.deltaTime, dist));

            float wantYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, wantYaw, turnSpeedDeg * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // In cover = ducked or stood behind a full wall — hidden either way.
        // Aiming is the pop-up/exposed phase's job (peek → acquire → fire →
        // duck → reload) — the solver also refuses to arm while crouched.
        void CrouchAndCover()
        {
            bool stand = coverWallClass == CoverSensor.WallClass.Full;
            anim?.RequestPose(stand ? CharacterAnimation.AnimState.Idle
                                    : CharacterAnimation.AnimState.Crouching,
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
