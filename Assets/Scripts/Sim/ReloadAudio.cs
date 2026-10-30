using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Reload audio — owns the CombatController.RoundSeated family. The Model 10
    /// wheel seats one chamber per `roundLoadInterval`; every seat invokes the
    /// event and this plays sounds/reload_click.stsound at the fighter's chest.
    /// Single file today — sounds/LiveTesting/reload_click.stsound shadows it
    /// while the recipe is being tuned (delete the twin and the shipped file
    /// returns). Per-file volume trim rides the shared FileMix sidecar law;
    /// sample-level sidecar gain doesn't apply on the cached-clip path. The
    /// "reload" wave is the debug layer only — AI reacts to "gunshot" only.
    /// </summary>
    public class ReloadAudio : MonoBehaviour
    {
        public static ReloadAudio Instance { get; private set; }

        public static ReloadAudio GetOrCreate()
        {
            if (Instance != null) return Instance;
            var found = FindFirstObjectByType<ReloadAudio>();
            if (found != null) { Instance = found; return found; }
            var go = new GameObject("ReloadAudio");
            Instance = go.AddComponent<ReloadAudio>();
            return Instance;
        }

        [Header("Gate")]
        public bool reloadAudioEnabled = true;

        [Header("Mix")]
        [Range(0.1f, 1f)] public float reloadAudioVolume = 1.0f;
        [Tooltip("Full-volume zone (m) — a near-field foley click.")]
        [Range(0.2f, 20f)] public float reloadRefDistance = 2.5f;
        [Range(1f, 50f)] public float reloadAudibleRange = 10f;

        /// <summary>Last seat served, for the SFX panel + logs.</summary>
        public string LastInfo { get; private set; } = "—";

        const string Rel = "sounds/reload_click.stsound";

        void Awake()
        {
            Instance = this;
            CombatController.RoundSeated += OnRoundSeated;
        }

        void OnDestroy()
        {
            CombatController.RoundSeated -= OnRoundSeated;
            if (Instance == this) Instance = null;
        }

        void OnRoundSeated(CombatController cc)
        {
            if (!reloadAudioEnabled || cc == null) return;
            var clip = ProceduralAudio.GetClip(Rel);
            if (clip == null) { LastInfo = "clip missing: reload_click.stsound"; return; }
            var mix = SurfaceImpactAudio.FileMix(Rel);   // shared per-file sidecar law (volume trim)
            Vector3 pos = cc.transform.position + Vector3.up * 1.2f;   // the wheel is worked at chest height
            ProceduralAudio.PlayAt(clip, pos, reloadAudioVolume * mix.volume, reloadRefDistance, reloadAudibleRange);
            SfxWaves.GetOrCreate().Emit(pos, reloadAudibleRange, reloadRefDistance, "reload");
            LastInfo = $"{cc.name} seat {cc.Cylinder}/{cc.cylinderCapacity}";
            Debug.Log($"[ReloadAudio] 🔊 {LastInfo}");
        }
    }
}
