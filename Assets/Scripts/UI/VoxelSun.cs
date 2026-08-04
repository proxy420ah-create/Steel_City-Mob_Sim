using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Drives voxel raymarch lighting via a visible sun GameObject that orbits the city.
    /// Computes light direction from its world position toward the city center,
    /// then passes direction + intensity + color tint to VoxelChunkManager each frame.
    ///
    /// Day/night cycle: timeOfDay (0-24 hours) drives the sun's arc.
    ///   6:00  = sunrise (east horizon, warm orange)
    ///   12:00 = noon (directly overhead, bright white)
    ///   18:00 = sunset (west horizon, warm orange)
    ///   0:00  = midnight (below horizon, dim blue moonlight)
    ///
    /// The sun is a wireframe sphere visible in the Scene view (and optionally in-game).
    /// </summary>
    public class VoxelSun : MonoBehaviour
    {
        [Header("Time")]
        [Tooltip("Current time of day in hours (0-24). 12 = noon.")]
        [Range(0f, 24f)]
        [SerializeField] private float timeOfDay = 10f;  // 10 AM — sun at angle, lights walls

        [Tooltip("Speed of time progression. 1 = real-time (1 hour per real hour). 0 = frozen.")]
        [SerializeField] private float timeSpeed = 0f;

        [Header("Orbit")]
        [Tooltip("Center of the city — sun orbits around this point.")]
        [SerializeField] private Vector3 cityCenter = new Vector3(0f, 0f, -100f);

        [Tooltip("Radius of the sun's orbit from city center.")]
        [SerializeField] private float orbitRadius = 60f;

        [Tooltip("How far north/south the sun's arc tilts (0 = directly overhead at noon, 23.5 = Earth-like).")]
        [SerializeField] private float axialTilt = 20f;

        [Header("Lighting Presets")]
        [SerializeField] private DayLightPreset noon = new()
        {
            intensity = 1.0f,
            ambient = 0.65f,
            fill = 0.35f,
            tint = new Color(1f, 0.98f, 0.95f, 1f)
        };
        [SerializeField] private DayLightPreset golden = new()
        {
            intensity = 0.65f,
            ambient = 0.35f,
            fill = 0.20f,
            tint = new Color(1f, 0.75f, 0.45f, 1f)
        };
        [SerializeField] private DayLightPreset night = new()
        {
            intensity = 0.15f,
            ambient = 0.12f,
            fill = 0.08f,
            tint = new Color(0.4f, 0.5f, 0.8f, 1f)
        };

        [Header("Visual")]
        [Tooltip("Show the sun as a wireframe sphere in the scene.")]
        [SerializeField] private bool showWireframe = true;
        [SerializeField] private float wireframeSize = 2f;

        private VoxelChunkManager chunkManager;
        private GameObject sunVisual;
        private Vector3 sunWorldPos;  // Computed sun position — NEVER move our own transform

        [System.Serializable]
        private struct DayLightPreset
        {
            public float intensity;
            public float ambient;
            public float fill;
            public Color tint;
        }

        /// <summary>Current time of day (0-24). Set to jump to a specific time.</summary>
        public float TimeOfDay
        {
            get => timeOfDay;
            set => timeOfDay = Mathf.Repeat(value, 24f);
        }

        void Awake()
        {
            if (showWireframe)
                CreateSunVisual();
        }

        void Start()
        {
            // Look up chunkManager in Start() — all Awake() calls have completed
            chunkManager = GetComponent<VoxelChunkManager>();
            if (chunkManager == null)
            {
                var cityMap = FindFirstObjectByType<CityMap3D>();
                if (cityMap != null)
                    chunkManager = cityMap.GetComponent<VoxelChunkManager>();
            }

            Debug.Log($"[VoxelSun] Start: chunkManager={(chunkManager != null ? "FOUND" : "NULL")}, " +
                $"cityCenter={cityCenter}, timeOfDay={timeOfDay}");

            // Force an initial lighting update
            UpdateSunPosition();
            UpdateLighting();
        }

        void Update()
        {
            if (timeSpeed > 0f)
                timeOfDay = Mathf.Repeat(timeOfDay + timeSpeed * Time.deltaTime, 24f);

            UpdateSunPosition();
            UpdateLighting();
        }

        /// <summary>
        /// Compute sun world position from timeOfDay and place the visual.
        /// Sun arcs from east (6:00) to west (18:00) through overhead (12:00).
        /// </summary>
        private void UpdateSunPosition()
        {
            // Convert time to angle: 6:00 = 0° (east horizon), 12:00 = 90° (overhead), 18:00 = 180° (west)
            float dayAngle = (timeOfDay - 6f) / 12f * Mathf.PI; // 0 to PI for 6:00-18:00
            float sunHeight = Mathf.Sin(dayAngle);              // 0 at horizon, 1 at noon
            float sunHorizontal = Mathf.Cos(dayAngle);          // 1 at east, -1 at west

            // Apply axial tilt — shifts the arc slightly south
            float tiltRad = axialTilt * Mathf.Deg2Rad;
            float yOffset = Mathf.Sin(tiltRad) * sunHorizontal;
            float zOffset = Mathf.Cos(tiltRad) * sunHorizontal;

            sunWorldPos = cityCenter + new Vector3(
                sunHorizontal * orbitRadius,
                sunHeight * orbitRadius + yOffset * orbitRadius * 0.3f,
                -zOffset * orbitRadius * 0.3f
            );

            // Only move the visual sphere — NEVER move our own transform
            // (VoxelSun is attached to CityMap3D's GameObject)
            if (sunVisual != null)
                sunVisual.transform.position = sunWorldPos;
        }

        /// <summary>
        /// Compute lighting parameters from sun height and push to VoxelChunkManager.
        /// Sun height: 1 = noon (bright white), 0 = horizon (golden hour), -1 = night (dim blue).
        /// </summary>
        private void UpdateLighting()
        {
            if (chunkManager == null) return;

            // Direction from city center TO sun = light direction
            Vector3 lightDir = (sunWorldPos - cityCenter).normalized;

            // Sun height determines which preset to blend toward
            float sunHeight = sunWorldPos.y - cityCenter.y;
            float normalizedHeight = Mathf.Clamp(sunHeight / orbitRadius, -1f, 1f);

            DayLightPreset current;
            if (normalizedHeight > 0.3f)
            {
                // High sun — noon lighting
                current = noon;
            }
            else if (normalizedHeight > 0f)
            {
                // Near horizon — golden hour
                float t = normalizedHeight / 0.3f;
                current = BlendPreset(golden, noon, t);
            }
            else if (normalizedHeight > -0.2f)
            {
                // Just below horizon — fading golden to night
                float t = (normalizedHeight + 0.2f) / 0.2f;
                current = BlendPreset(night, golden, t);
            }
            else
            {
                // Deep night
                current = night;
            }

            chunkManager.SetLighting(lightDir, current.intensity, current.ambient, current.fill, current.tint);

            if (!hasLoggedLighting)
            {
                hasLoggedLighting = true;
                Debug.Log($"[VoxelSun] Lighting pushed: dir={lightDir}, intensity={current.intensity}, " +
                    $"ambient={current.ambient}, fill={current.fill}, tint={current.tint}, " +
                    $"sunPos={sunWorldPos}, height={normalizedHeight:F2}");
            }
        }

        private bool hasLoggedLighting = false;

        private static DayLightPreset BlendPreset(DayLightPreset a, DayLightPreset b, float t)
        {
            return new DayLightPreset
            {
                intensity = Mathf.Lerp(a.intensity, b.intensity, t),
                ambient = Mathf.Lerp(a.ambient, b.ambient, t),
                fill = Mathf.Lerp(a.fill, b.fill, t),
                tint = Color.Lerp(a.tint, b.tint, t)
            };
        }

        private void CreateSunVisual()
        {
            sunVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sunVisual.name = "VoxelSun_Visual";
            sunVisual.transform.localScale = Vector3.one * wireframeSize;

            // Make it wireframe-style: use a simple unlit material with high emission
            var rend = sunVisual.GetComponent<Renderer>();
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = new Color(1f, 0.9f, 0.6f, 0.5f);
            rend.material = mat;

            // Remove collider — sun is purely visual
            var col = sunVisual.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Parent to this transform so it follows
            sunVisual.transform.SetParent(transform, false);
        }

        void OnDrawGizmos()
        {
            if (!showWireframe) return;
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
            Gizmos.DrawWireSphere(sunWorldPos, wireframeSize);

            // Draw line from sun to city center (light direction)
            Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.3f);
            Gizmos.DrawLine(sunWorldPos, cityCenter);
        }
    }
}
