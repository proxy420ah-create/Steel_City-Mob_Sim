using System.Collections;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Gangsters-inspired NPC behavior: randomly stops to look around.
    /// Innocent pedestrians and criminal hoods share the same head-turn
    /// animation, creating emergent suspicion — you can't tell them apart
    /// until the hood actually commits a crime.
    ///
    /// Attach alongside VoxelCharacter + CharacterAnimation.
    /// </summary>
    public class PedestrianLookAround : MonoBehaviour
    {
        [Header("Look-Around Timing")]
        [Tooltip("Minimum seconds between look-around events.")]
        public float minLookInterval = 5f;
        [Tooltip("Maximum seconds between look-around events.")]
        public float maxLookInterval = 15f;
        [Tooltip("Minimum look duration (head turning).")]
        public float minLookDuration = 2f;
        [Tooltip("Maximum look duration (head turning).")]
        public float maxLookDuration = 4f;
        [Tooltip("If true, NPC will randomly look around. Disable for hoods using crime-AI triggered checks.")]
        public bool enableRandomLook = true;

        private CharacterAnimation anim;
        private float lookTimer;

        void Start()
        {
            anim = GetComponent<CharacterAnimation>();
            if (anim == null)
                anim = gameObject.AddComponent<CharacterAnimation>();
            lookTimer = Random.Range(minLookInterval, maxLookInterval);
        }

        void Update()
        {
            if (anim == null || !enableRandomLook) return;

            // Fidget only from an UNHELD base Idle. A pose override (debug pose,
            // aim, crouch) means another system owns the pose — never stomp it.
            // (Previously the LookAround coroutine could restore Idle over a
            // state set mid-look — gotcha G3 in CHARACTER_ASSET_LIFECYCLE.md.)
            if (anim.HasOverride || anim.currentState != CharacterAnimation.AnimState.Idle) return;

            lookTimer -= Time.deltaTime;
            if (lookTimer <= 0f)
            {
                // Timed override — auto-releases; nothing to restore afterwards.
                anim.RequestPose(CharacterAnimation.AnimState.Looking,
                    CharacterAnimation.PRIORITY_BEHAVIOR,
                    Random.Range(minLookDuration, maxLookDuration));
                lookTimer = Random.Range(minLookInterval, maxLookInterval);
            }
        }

        /// <summary>Trigger a coast-clear check (for hoods). Longer pause than civilian look-around.</summary>
        public IEnumerator CoastClearCheck(float duration = 3f)
        {
            anim.RequestPose(CharacterAnimation.AnimState.AimWalk,
                CharacterAnimation.PRIORITY_BEHAVIOR, duration);
            yield return new WaitForSeconds(duration);
        }
    }
}
