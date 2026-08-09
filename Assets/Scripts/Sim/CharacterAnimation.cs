using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Drives voxel group animation state for a VoxelCharacter.
    /// Updates animState/animTime/animSpeed on the InstancedCharacter handle
    /// so the raymarch shader can apply per-group limb transforms.
    ///
    /// Animation states (must match shader GroupTransformOffset logic):
    ///   0 = Idle, 1 = Walking, 2 = Looking, 3 = Checking,
    ///   4 = Aiming, 5 = Crouching, 6 = Flinching, 7 = Falling, 8 = Down
    /// </summary>
    public class CharacterAnimation : MonoBehaviour
    {
        public enum AnimState : int
        {
            Idle = 0,
            Walking = 1,
            Looking = 2,
            Checking = 3,
            Aiming = 4,
            Crouching = 5,
            Flinching = 6,
            Falling = 7,
            Down = 8
        }

        [Header("Animation")]
        [Tooltip("Current animation state. Drives shader per-group transforms.")]
        public AnimState currentState = AnimState.Idle;
        [Tooltip("Walk speed multiplier. 1.0 = normal, 1.5 = jogging.")]
        public float walkSpeed = 1.0f;
        [Tooltip("If true, auto-detect walking state from velocity.")]
        public bool autoDetectWalking = true;
        [Tooltip("Minimum velocity magnitude to be considered walking.")]
        public float walkVelocityThreshold = 0.1f;

        private VoxelCharacter voxelChar;
        private VoxelChunkManager.InstancedCharacter instancedHandle;
        private float animTime = 0f;
        private AnimState prevState;
        private Vector3 lastPos;

        void Start()
        {
            voxelChar = GetComponent<VoxelCharacter>();
            prevState = currentState;
            lastPos = transform.position;
        }

        void Update()
        {
            if (instancedHandle == null && voxelChar != null)
            {
                // Try to get the handle from VoxelCharacter (it creates it in Start)
                instancedHandle = voxelChar.GetInstancedHandle();
            }

            if (instancedHandle == null) return;

            // Auto-detect walking from movement
            if (autoDetectWalking)
            {
                Vector3 velocity = (transform.position - lastPos) / Time.deltaTime;
                lastPos = transform.position;
                float horSpeed = new Vector2(velocity.x, velocity.z).magnitude;

                if (currentState != AnimState.Looking && currentState != AnimState.Checking)
                {
                    if (horSpeed > walkVelocityThreshold)
                    {
                        if (currentState != AnimState.Walking)
                            SetState(AnimState.Walking);
                        walkSpeed = Mathf.Clamp(horSpeed * 0.5f, 0.5f, 2.0f);
                    }
                    else
                    {
                        if (currentState == AnimState.Walking)
                            SetState(AnimState.Idle);
                    }
                }
            }

            // Reset animTime on state change for clean transitions
            if (currentState != prevState)
            {
                animTime = 0f;
                prevState = currentState;
            }

            animTime += Time.deltaTime;

            // Push to GPU via instance buffer
            instancedHandle.animState = (float)currentState;
            instancedHandle.animTime = animTime;
            instancedHandle.animSpeed = walkSpeed;
        }

        /// <summary>Set animation state. Resets animTime for clean transitions.</summary>
        public void SetState(AnimState newState)
        {
            if (currentState != newState)
            {
                currentState = newState;
                animTime = 0f;
                prevState = newState;
            }
        }

        /// <summary>Current animation time (seconds since state change).</summary>
        public float AnimTime => animTime;
    }
}
