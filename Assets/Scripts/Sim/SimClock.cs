using UnityEngine;
using UnityEngine.InputSystem;

namespace SteelCity.Sim
{
    /// <summary>
    /// The simulation clock — sim-seconds advanced in FIXED steps (0.08s, the
    /// RE tickInterval). Decision logic (combat dice, cooldowns, dwells) reads
    /// <see cref="Now"/>; bodies/poses keep interpolating on frame time.
    /// Driven by SCALED frame time × <see cref="Speed"/>, so F8's timeScale
    /// freeze, <see cref="Paused"/> and fast-forward all stop/scale decisions
    /// together. Now only changes on step boundaries → decisions are frame-rate
    /// independent and replayable from a seed.
    /// </summary>
    public static class SimClock
    {
        public const float Step = 0.08f;
        const int MaxStepsPerFrame = 8;
        /// <summary>Offset so "-1 = never" sentinels (LastSeenAt etc.) read as long-ago at boot, as wall-clock did.</summary>
        const float Epoch = 1000f;

        public static float Now { get; private set; } = Epoch;
        public static float Dt { get; private set; }
        public static long Ticks { get; private set; }
        public static float Speed = 1f;
        public static bool Paused;

        static float acc;
        static int stepRequests;

        /// <summary>While paused, advance exactly one step on the next frame.</summary>
        public static void StepOnce() { if (Paused) stepRequests++; }

        public static void Advance(float scaledFrameDt)
        {
            int steps = 0;
            if (!Paused)
            {
                acc = Mathf.Min(acc + scaledFrameDt * Mathf.Max(0f, Speed), Step * MaxStepsPerFrame);
                while (acc >= Step && steps < MaxStepsPerFrame) { acc -= Step; steps++; }
            }
            else { acc = 0f; steps = stepRequests; }
            stepRequests = 0;
            Dt = steps * Step;
            Ticks += steps;
            Now = Epoch + Ticks * Step;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Now = Epoch; Dt = 0f; Ticks = 0; Speed = 1f; Paused = false; acc = 0f; stepRequests = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("SimClock") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SimClockDriver>();
        }
    }

    [DefaultExecutionOrder(-10000)]
    public class SimClockDriver : MonoBehaviour
    {
        bool stepping;

        /// <summary>
        /// FROZEN STEP — while F8 holds timeScale at 0, [.] runs the world for
        /// exactly one sim step: timeScale is set so the NEXT frame's deltaTime
        /// is ~Step (bodies move one step), that frame forces one tick, then
        /// timeScale returns to 0. Free-cam authority (CityMap3D.Frozen) lapses
        /// for that single frame.
        /// </summary>
        void Update()
        {
            if (stepping)
            {
                SimClock.Advance(SimClock.Step);
                Time.timeScale = 0f;
                stepping = false;
                return;
            }
            SimClock.Advance(Time.deltaTime);
            var kb = Keyboard.current;
            if (kb != null && kb.periodKey.wasPressedThisFrame && Time.timeScale < 0.001f)
            {
                Time.timeScale = Mathf.Clamp(SimClock.Step / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.01f, 10f);
                stepping = true;
                Debug.Log($"[SimClock] ⏭ step 1 tick ({SimClock.Step:F2}s) from t={SimClock.Now:F2}");
            }
        }
    }
}
