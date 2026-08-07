using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    public class FollowCamera : MonoBehaviour
    {
        [Header("Follow Settings")]
        [Tooltip("Target to follow (Vinny's VoxelCharacter).")]
        public Transform target;

        [Tooltip("VoxelCharacter component for center-based aiming.")]
        public VoxelCharacter character;

        [Tooltip("Camera offset behind and above target.")]
        public Vector3 offset = new Vector3(0f, 3f, -4f);

        [Tooltip("How smoothly camera follows position (higher = snappier).")]
        public float followSpeed = 3f;

        [Tooltip("How smoothly camera rotates to look at target.")]
        public float lookSpeed = 3f;

        [Tooltip("How smoothly camera yaw catches up to movement direction (lower = lazier).")]
        public float chaseYawSpeed = 2f;

        [Tooltip("Field of view for the follow camera.")]
        public float fieldOfView = 50f;

        [Header("Chase Camera")]
        [Tooltip("If true, camera positions behind character's movement direction (chase-cam). If false, uses orbit mode.")]
        public bool chaseMode = true;
        [Tooltip("Distance behind character in chase mode.")]
        public float chaseDistance = 6f;
        [Tooltip("Height above character in chase mode.")]
        public float chaseHeight = 3.5f;
        [Tooltip("Extra pitch angle (degrees) for chase camera.")]
        public float chasePitch = 15f;

        [Header("Debug Controls")]
        [Tooltip("Show on-screen camera debug HUD.")]
        public bool showDebugHUD = true;
        [Tooltip("Distance from target (orbit mode).")]
        public float distance = 5f;
        [Tooltip("Height above target (orbit mode).")]
        public float height = 3f;

        private Camera cam;
        private Camera originalMapCamera;
        private VoxelChunkManager chunkManager;
        private VoxelRenderBridge renderBridge;
        private VoxelRenderBridge originalMapRenderBridge;
        private List<GameObject> hiddenUIChildren = new List<GameObject>();
        private Vector3 velocity = Vector3.zero;
        private bool loggedFirstFrame = false;
        private float currentYaw;
        private float currentPitch = 20f;
        private float lookYaw = 0f;
        private float lookPitch = 0f;
        private bool freeLook = false;
        private Vector3 lastTargetPos;
        private float chaseYaw;
        private bool chaseYawInitialized;

        // --- Cached OnGUI resources (avoid per-frame allocation) ---
        private GUIStyle cachedLabelStyle;
        private GUIStyle cachedBgStyle;
        private Texture2D cachedBgTex;

        public void Initialize(Transform followTarget, Camera mapCamera, VoxelCharacter voxelChar = null)
        {
            target = followTarget;
            character = voxelChar;
            originalMapCamera = mapCamera;

            Debug.Log("[FollowCamera] Initialize START — target=" + (target?.name ?? "null") +
                      ", mapCamera=" + (mapCamera != null ? mapCamera.name : "null") +
                      ", character=" + (character != null ? character.name : "null"));

            cam = GetComponent<Camera>();
            if (cam == null)
                cam = gameObject.AddComponent<Camera>();

            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.depth = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.063f, 0.063f, 0.106f, 1f);
            cam.rect = new Rect(0f, 0f, 1f, 1f);

            Debug.Log("[FollowCamera] Camera configured: fov=" + cam.fieldOfView +
                      ", depth=" + cam.depth + ", rect=" + cam.rect);

            var audioListener = GetComponent<AudioListener>();
            if (audioListener == null)
                gameObject.AddComponent<AudioListener>();

            // Find chunk manager and swap render camera
            chunkManager = FindFirstObjectByType<VoxelChunkManager>();
            if (chunkManager != null)
            {
                chunkManager.SetRenderCamera(cam);
                Debug.Log("[FollowCamera] VoxelChunkManager render camera swapped to follow camera");
            }
            else
            {
                Debug.LogWarning("[FollowCamera] No VoxelChunkManager found — voxels won't render!");
            }

            // Add VoxelRenderBridge to follow camera so raymarch fires on this camera's render
            renderBridge = GetComponent<VoxelRenderBridge>();
            if (renderBridge == null)
                renderBridge = gameObject.AddComponent<VoxelRenderBridge>();
            renderBridge.chunkManager = chunkManager;
            Debug.Log("[FollowCamera] VoxelRenderBridge attached to follow camera");

            // Disable the map camera and its render bridge
            if (originalMapCamera != null)
            {
                originalMapRenderBridge = originalMapCamera.GetComponent<VoxelRenderBridge>();
                if (originalMapRenderBridge != null)
                    originalMapRenderBridge.enabled = false;
                originalMapCamera.enabled = false;
                Debug.Log("[FollowCamera] Map camera disabled: " + originalMapCamera.name);
            }

            // Hide game UI panels (but keep canvases enabled so raymarch RawImage overlay stays visible)
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            hiddenUIChildren.Clear();
            foreach (var c in canvases)
            {
                // Disable all direct children of the canvas except RaymarchOverlay and TransitionOverlay
                for (int i = 0; i < c.transform.childCount; i++)
                {
                    var child = c.transform.GetChild(i);
                    if (!child.name.Contains("RaymarchOverlay") && !child.name.Contains("Transition") && child.gameObject.activeSelf)
                    {
                        child.gameObject.SetActive(false);
                        hiddenUIChildren.Add(child.gameObject);
                        Debug.Log("[FollowCamera] Hidden UI child: " + child.name + " (parent canvas: " + c.gameObject.name + ")");
                    }
                }
            }

            // Initialize manual camera angles from current offset
            distance = offset.magnitude;
            height = offset.y;
            currentYaw = Mathf.Atan2(offset.x, -offset.z) * Mathf.Rad2Deg;
            currentPitch = Mathf.Atan2(height, Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z)) * Mathf.Rad2Deg;

            chaseYawInitialized = false;
            lastTargetPos = Vector3.zero;

            Debug.Log("[FollowCamera] Initialize COMPLETE — follow camera active");
            Debug.Log($"[FollowCamera] Initial: distance={distance:F2}, height={height:F2}, yaw={currentYaw:F1}, pitch={currentPitch:F1}, chaseMode={chaseMode}");
            loggedFirstFrame = false;
        }

        public void Shutdown()
        {
            Debug.Log("[FollowCamera] Shutdown START");

            // Restore hidden UI children
            foreach (var child in hiddenUIChildren)
            {
                if (child != null)
                {
                    child.SetActive(true);
                    Debug.Log("[FollowCamera] Restored UI child: " + child.name);
                }
            }
            hiddenUIChildren.Clear();

            if (originalMapCamera != null)
            {
                originalMapCamera.enabled = true;
                Debug.Log("[FollowCamera] Map camera re-enabled: " + originalMapCamera.name);
            }

            if (originalMapRenderBridge != null)
            {
                originalMapRenderBridge.enabled = true;
                Debug.Log("[FollowCamera] Map VoxelRenderBridge re-enabled");
            }

            if (chunkManager != null && originalMapCamera != null)
            {
                chunkManager.SetRenderCamera(originalMapCamera);
                Debug.Log("[FollowCamera] VoxelChunkManager render camera restored to map camera");
            }

            if (renderBridge != null)
                renderBridge.enabled = false;

            Debug.Log("[FollowCamera] Shutdown COMPLETE");
        }

        void Update()
        {
            if (target == null || cam == null) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            // Toggle debug HUD with H
            if (kb.hKey.wasPressedThisFrame)
            {
                showDebugHUD = !showDebugHUD;
            }

            // Capture to log with C
            if (kb.cKey.wasPressedThisFrame)
            {
                CaptureToLog();
            }

            // Toggle free-look mode with Left Shift (hold) or T (toggle)
            freeLook = kb.leftShiftKey.isPressed;

            // T toggles chase mode
            if (kb.tKey.wasPressedThisFrame)
            {
                chaseMode = !chaseMode;
                Debug.Log($"[FollowCamera] Chase mode toggled: {chaseMode}");
            }

            // Z resets look offsets to zero (re-center view)
            if (kb.zKey.wasPressedThisFrame)
            {
                lookYaw = 0f;
                lookPitch = 0f;
                Debug.Log("[FollowCamera] Look offsets reset to zero");
            }

            if (freeLook)
            {
                // FREE LOOK: rotate camera view in-place (arrows = look around)
                float lookYawDelta = 0f, lookPitchDelta = 0f;
                if (kb.leftArrowKey.isPressed) lookYawDelta -= 90f * Time.deltaTime;
                if (kb.rightArrowKey.isPressed) lookYawDelta += 90f * Time.deltaTime;
                if (kb.upArrowKey.isPressed) lookPitchDelta += 45f * Time.deltaTime;
                if (kb.downArrowKey.isPressed) lookPitchDelta -= 45f * Time.deltaTime;

                lookYaw += lookYawDelta;
                lookPitch = Mathf.Clamp(lookPitch + lookPitchDelta, -80f, 80f);

                // Q/E still control distance, R/F height, +/- FOV
                if (kb.qKey.isPressed) distance = Mathf.Max(1f, distance - 5f * Time.deltaTime);
                if (kb.eKey.isPressed) distance = Mathf.Min(50f, distance + 5f * Time.deltaTime);
                if (kb.rKey.isPressed) height = Mathf.Min(30f, height + 3f * Time.deltaTime);
                if (kb.fKey.isPressed) height = Mathf.Max(-5f, height - 3f * Time.deltaTime);
                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed)
                    fieldOfView = Mathf.Min(120f, fieldOfView + 20f * Time.deltaTime);
                if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed)
                    fieldOfView = Mathf.Max(10f, fieldOfView - 20f * Time.deltaTime);
            }
            else
            {
                // ORBIT MODE: arrows orbit around target, look offsets persist
                float yawDelta = 0f, pitchDelta = 0f;
                if (kb.leftArrowKey.isPressed) yawDelta -= 60f * Time.deltaTime;
                if (kb.rightArrowKey.isPressed) yawDelta += 60f * Time.deltaTime;
                if (kb.upArrowKey.isPressed) pitchDelta += 30f * Time.deltaTime;
                if (kb.downArrowKey.isPressed) pitchDelta -= 30f * Time.deltaTime;

                currentYaw += yawDelta;
                currentPitch = Mathf.Clamp(currentPitch + pitchDelta, -10f, 85f);

                if (kb.qKey.isPressed) distance = Mathf.Max(1f, distance - 5f * Time.deltaTime);
                if (kb.eKey.isPressed) distance = Mathf.Min(50f, distance + 5f * Time.deltaTime);
                if (kb.rKey.isPressed) height = Mathf.Min(30f, height + 3f * Time.deltaTime);
                if (kb.fKey.isPressed) height = Mathf.Max(-5f, height - 3f * Time.deltaTime);
                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed)
                    fieldOfView = Mathf.Min(120f, fieldOfView + 20f * Time.deltaTime);
                if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed)
                    fieldOfView = Mathf.Max(10f, fieldOfView - 20f * Time.deltaTime);
            }

            if (cam != null) cam.fieldOfView = fieldOfView;
        }

        void LateUpdate()
        {
            if (target == null || cam == null) return;

            // Aim at the character's world center, not the corner of the voxel volume
            Vector3 aimPoint = character != null ? character.WorldCenter : target.position + Vector3.up * 0.5f;

            if (chaseMode && !freeLook)
            {
                // === CHASE CAM ===
                // Track movement direction and position camera behind character

                Vector3 currentPos = aimPoint;
                if (!chaseYawInitialized)
                {
                    // Initialize chase yaw from current camera position relative to target
                    Vector3 toCam = transform.position - currentPos;
                    chaseYaw = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
                    chaseYawInitialized = true;
                }
                else
                {
                    // Compute movement direction from position delta
                    Vector3 delta = currentPos - lastTargetPos;
                    float moveDist = new Vector2(delta.x, delta.z).magnitude;
                    if (moveDist > 0.001f)
                    {
                        // Movement heading (degrees, 0 = +Z)
                        float moveYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                        // Camera should be behind movement, so add 180
                        float desiredChaseYaw = moveYaw + 180f;
                        // Smoothly interpolate yaw (lazy follow)
                        chaseYaw = Mathf.LerpAngle(chaseYaw, desiredChaseYaw, chaseYawSpeed * Time.deltaTime);
                    }
                }
                lastTargetPos = currentPos;

                // Position camera behind character at chase distance/height
                float yawRad = chaseYaw * Mathf.Deg2Rad;
                Vector3 desiredPos = new Vector3(
                    currentPos.x + Mathf.Sin(yawRad) * chaseDistance,
                    currentPos.y + chaseHeight,
                    currentPos.z + Mathf.Cos(yawRad) * chaseDistance);

                transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, 1f / followSpeed);

                // Look at character with slight pitch offset
                Vector3 lookTarget = currentPos + Vector3.up * (chaseHeight * 0.3f);
                Quaternion baseRot = Quaternion.LookRotation(lookTarget - transform.position);
                Quaternion lookOffset = Quaternion.Euler(lookPitch, lookYaw, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, baseRot * lookOffset, lookSpeed * Time.deltaTime);
            }
            else
            {
                // === ORBIT / FREE LOOK ===
                float yawRad = currentYaw * Mathf.Deg2Rad;
                float pitchRad = currentPitch * Mathf.Deg2Rad;
                float horizDist = distance * Mathf.Cos(pitchRad);
                Vector3 computedOffset = new Vector3(
                    horizDist * Mathf.Sin(yawRad),
                    height,
                    -horizDist * Mathf.Cos(yawRad));

                Vector3 desiredPos = aimPoint + computedOffset;
                transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, 1f / followSpeed);

                // Look at aim point, then apply free-look offsets on top
                Quaternion baseRot = Quaternion.LookRotation(aimPoint - transform.position);
                Quaternion lookOffset = Quaternion.Euler(lookPitch, lookYaw, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, baseRot * lookOffset, lookSpeed * Time.deltaTime);
            }

            if (!loggedFirstFrame)
            {
                loggedFirstFrame = true;
                Debug.Log($"[FollowCamera] First LateUpdate — aimPoint={aimPoint}, camPos={transform.position}, " +
                          $"targetPos={target.position}, charWorldCenter={(character != null ? character.WorldCenter.ToString() : "null")}, " +
                          $"charWorldSize={(character != null ? character.WorldSize.ToString() : "null")}, " +
                          $"chaseMode={chaseMode}");
            }
        }

        void CaptureToLog()
        {
            Vector3 aimPoint = character != null ? character.WorldCenter : (target != null ? target.position : Vector3.zero);
            Debug.Log("[FollowCamera] === CAMERA CAPTURE ===");
            Debug.Log($"[FollowCamera] offset = new Vector3({offset.x:F2}f, {offset.y:F2}f, {offset.z:F2}f);");
            Debug.Log($"[FollowCamera] distance={distance:F2}, height={height:F2}, yaw={currentYaw:F1}, pitch={currentPitch:F1}");
            Debug.Log($"[FollowCamera] lookYaw={lookYaw:F1}, lookPitch={lookPitch:F1}");
            Debug.Log($"[FollowCamera] fov={fieldOfView:F1}, followSpeed={followSpeed:F1}, lookSpeed={lookSpeed:F1}");
            Debug.Log($"[FollowCamera] camPos={transform.position}, aimPoint={aimPoint}");
            Debug.Log($"[FollowCamera] camRot={transform.rotation.eulerAngles}");
            Debug.Log("[FollowCamera] === END CAPTURE ===");
        }

        void OnGUI()
        {
            if (!showDebugHUD) return;

            // Initialize cached styles once
            if (cachedLabelStyle == null)
            {
                cachedLabelStyle = new GUIStyle(GUI.skin.label);
                cachedLabelStyle.fontSize = 14;
                cachedLabelStyle.normal.textColor = new Color(1f, 1f, 0.4f);
            }
            if (cachedBgTex == null)
            {
                cachedBgTex = new Texture2D(1, 1);
                cachedBgTex.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.12f, 0.85f));
                cachedBgTex.Apply();
            }
            if (cachedBgStyle == null)
            {
                cachedBgStyle = new GUIStyle(GUI.skin.box);
                cachedBgStyle.normal.background = cachedBgTex;
            }

            var style = cachedLabelStyle;
            var bgStyle = cachedBgStyle;

            float w = 340f, h = 260f;
            GUILayout.BeginArea(new Rect(10, 10, w, h), bgStyle);
            GUILayout.Label("<b>FOLLOW CAMERA DEBUG</b>", style);
            GUILayout.Label("", style);
            GUILayout.Label($"Mode: {(chaseMode ? "CHASE" : "ORBIT")}{(freeLook ? " + FREE LOOK" : "")}", style);
            GUILayout.Label("", style);
            if (chaseMode && !freeLook)
            {
                GUILayout.Label($"Chase Dist:  {chaseDistance:F2}", style);
                GUILayout.Label($"Chase Hgt:   {chaseHeight:F2}", style);
                GUILayout.Label($"Chase Yaw:   {chaseYaw:F1} deg", style);
            }
            else
            {
                GUILayout.Label($"Distance:  {distance:F2}  (Q/E)", style);
                GUILayout.Label($"Height:    {height:F2}  (R/F)", style);
                GUILayout.Label($"Yaw:       {currentYaw:F1} deg  (Left/Right)", style);
                GUILayout.Label($"Pitch:     {currentPitch:F1} deg  (Up/Down)", style);
            }
            GUILayout.Label($"FOV:       {fieldOfView:F1}  (+/-)", style);
            GUILayout.Label($"FollowSpd: {followSpeed:F1}", style);
            GUILayout.Label($"LookSpd:   {lookSpeed:F1}", style);
            GUILayout.Label($"LookYaw:   {lookYaw:F1} deg", style);
            GUILayout.Label($"LookPitch: {lookPitch:F1} deg", style);
            GUILayout.Label("", style);
            GUILayout.Label($"Aim: {character?.WorldCenter.ToString() ?? "null"}", style);
            GUILayout.Label($"Cam: {transform.position}", style);
            GUILayout.Label("", style);
            GUILayout.Label("[T] Chase/Orbit  [Shift] Free-Look  [Z] Reset", style);
            GUILayout.Label("[C] Capture  [H] Hide HUD", style);
            GUILayout.EndArea();
        }
    }
}
