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
    ///   The IK solver stays armed at the crosshair hit point the whole time,
    ///   so shots leave the muzzle wherever the arm actually points.
    /// Release (G again): solver disarms, rig hotkeys resume, the camera stays
    /// wherever you left it (SetCameraFocus frozen → "lock the shot" and keep
    /// testing with the normal camera controls).
    /// </summary>
    public class PossessController : MonoBehaviour
    {
        public static bool Active { get; private set; }

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

        private CityMap3D cityMap;
        private VoxelCollisionWorld world;
        private VoxelCharacter vinny;
        private WeaponMount mount;
        private ArmAimSolver solver;
        private CharacterAnimation anim;
        private CharacterRig rig;
        private bool prevControllable;
        private bool prevFreedom;
        private float prevOrbit;
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

            // F8 freeze = inspection mode: the sim is halted, so possess yields
            // camera authority to the map free-cam (WASD fly / MMB orbit / wheel).
            // Keep look angles synced to the free-cam so control resumes without
            // a view snap on unfreeze.
            if (Time.timeScale < 0.001f)
            {
                lookYaw = cityMap.GetCameraYaw();
                lookPitch = cityMap.GetCameraPitch();
                return;
            }

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
                float sp = walkSpeed * (kb.shiftKey.isPressed ? sprintMult : 1f);
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

            // --- Anim state: aim-walk when moving, aim when still ---
            if (anim != null)
                anim.RequestPose(moving ? CharacterAnimation.AnimState.AimWalk
                                        : CharacterAnimation.AnimState.Aiming,
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

            // --- Fire ---
            fireCooldown -= Time.deltaTime;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                && EventSystemIsFree() && fireCooldown <= 0f
                && mount != null && mount.IsEquipped)
            {
                ProjectileSystem.GetOrCreate()
                    .Fire(mount, solver != null ? solver.LastAngularErrorDeg : 0f, aimPoint);
                fireCooldown = 0.18f;
            }
        }

        /// <summary>Move with voxel-wall blocking: sample feet and chest at the
        /// destination; try X/Z slide when the combined move is blocked.</summary>
        private void MoveWithCollision(Transform body, Vector3 delta)
        {
            if (world == null) { body.position += delta; return; }

            Vector3 dims = vinny.WorldSize;
            Vector3 half = new Vector3(dims.x * 0.5f, 0f, dims.z * 0.5f);

            bool Free(Vector3 corner)
            {
                Vector3 feet = corner + half + Vector3.up * 0.06f;
                Vector3 chest = corner + half + Vector3.up * (dims.y * 0.6f);
                var f = world.SamplePoint(feet);
                if (f.kind == VoxelCollisionWorld.PointKind.TerrainSolid ||
                    f.kind == VoxelCollisionWorld.PointKind.VolumeSolid) return false;
                var c = world.SamplePoint(chest);
                return c.kind != VoxelCollisionWorld.PointKind.TerrainSolid &&
                       c.kind != VoxelCollisionWorld.PointKind.VolumeSolid;
            }

            if (Free(body.position + delta)) { body.position += delta; return; }
            Vector3 dx = new Vector3(delta.x, 0f, 0f);
            Vector3 dz = new Vector3(0f, 0f, delta.z);
            if (Free(body.position + dx)) body.position += dx;
            else if (Free(body.position + dz)) body.position += dz;
        }

        /// <summary>Orbit pivot: head height, so the camera frames over his
        /// shoulder rather than orbiting his waist.</summary>
        private Vector3 EyePoint()
        {
            Vector3 dims = vinny.WorldSize;
            return vinny.WorldCenter + Vector3.up * (dims.y * 0.35f);
        }

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
            if (rig != null) { prevControllable = rig.Controllable; rig.Controllable = false; }
            if (solver != null) { prevSightBeam = solver.showSightBeam; solver.showSightBeam = false; }

            // Borrow the F6 freedom clamps without its debris/rebake side effects.
            prevFreedom = cityMap.debugCameraFreedom;
            cityMap.debugCameraFreedom = true;
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

            solver.Arm(vinny.WorldCenter + vinny.transform.forward * 10f);
            Active = true;
            Debug.Log("[Possess] G=take control ON — WASD move, mouse look, LMB fire; G releases (camera holds)");
        }

        private void Release()
        {
            Active = false;
            if (solver != null && solver.IsAiming) solver.Disarm();
            if (anim != null)
            {
                anim.ReleasePose(CharacterAnimation.PRIORITY_DEBUG);
                anim.SetState(CharacterAnimation.AnimState.Idle);
            }
            if (rig != null) rig.Controllable = prevControllable;
            if (solver != null) solver.showSightBeam = prevSightBeam;
            if (cityMap != null)
            {
                cityMap.debugCameraFreedom = prevFreedom;
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
        }
    }
}
