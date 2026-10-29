using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Reload tap driver — feeds the authored reload states (13 Reloading /
    /// 14 ReloadCrouch) their per-seat tap pulse. The ARMS are owned by the
    /// animation states themselves (authored FK, same conventions as AimSweep:
    /// RotationY reach outermost + authored axis swing + rest roll); this
    /// component only supplies the timing channel.
    ///
    /// Each CombatController.RoundSeated event stamps SimClock.Now; the pulse
    /// is a fast-attack/exponential-release envelope the states add onto the
    /// support arm's authored reach/bend (tap* deltas) — press lands on the
    /// same tick as reload_click. SimClock drives it, so the tap freezes and
    /// steps deterministically under F8/'.'.
    ///
    /// Latched test poses (Alt+R / Alt+E) never see RoundSeated — previewPulse
    /// gives them a gentle periodic tap so the hold doesn't read as a statue.
    /// </summary>
    public class ReloadGestureSolver : MonoBehaviour
    {
        [Header("Tap envelope (seconds)")]
        public float tapAttackSec = 0.07f;
        public float tapReleaseSec = 0.45f;

        [Header("Preview")]
        [Tooltip("Latched test poses get a periodic tap so the state previews.")]
        public bool previewPulse = true;
        [Tooltip("Preview tap cadence (Hz) — matches the 1.5s chamber pace ~0.67")]
        public float previewHz = 0.67f;

        public bool Active { get; private set; }
        public float Pulse { get; private set; }

        CombatController cc;
        CharacterAnimation animRef;
        VoxelCharacter character;
        WeaponMount mount;
        VoxelChunkManager.InstancedCharacter instHandle;
        float seatTime = -999f;

        void OnEnable()  { CombatController.RoundSeated += OnRoundSeated; }
        void OnDisable()
        {
            CombatController.RoundSeated -= OnRoundSeated;
            if (animRef != null) animRef.tapPulse = 0f;
        }

        void OnRoundSeated(CombatController who)
        {
            if (who == cc) seatTime = SimClock.Now;
        }

        void Update()
        {
            if (cc == null)        cc = GetComponent<CombatController>();
            if (animRef == null)   animRef = GetComponent<CharacterAnimation>();
            if (character == null) character = GetComponent<VoxelCharacter>();
            if (mount == null)     mount = GetComponent<WeaponMount>();
            if (instHandle == null && character != null) instHandle = character.GetInstancedHandle();
            if (animRef == null) return;

            // Gun side — the reload states assign arm params by ROLE (support
            // arm = authored L column, gun arm = R column), so the Unity L/R
            // flip can't put the tap on the gun hand. +1 = gun on gid-3 arm,
            // -1 = gun on gid-2 arm; resolves live from the mount's hand gid.
            float gunSide = 1f;
            if (mount != null && mount.Animator != null && mount.IsEquipped)
            {
                int armGid = mount.Animator.GetParentGroup(mount.HandGid);
                gunSide = armGid == 2 ? -1f : 1f;
                mount.Animator.reloadGunSide = gunSide;
            }
            if (instHandle != null) instHandle.reloadGunSide = gunSide;

            float state = (float)animRef.EffectiveState;
            bool inReloadState = state > 12.5f && state < 14.5f;
            Active = (cc != null && cc.Reloading) || inReloadState;

            float pulse = 0f;
            if (Active)
            {
                float dt = SimClock.Now - seatTime;
                bool recentSeat = dt >= 0f && dt < 1.5f;
                if (recentSeat)
                {
                    pulse = dt < tapAttackSec ? dt / Mathf.Max(0.001f, tapAttackSec)
                        : Mathf.Exp(-(dt - tapAttackSec) / Mathf.Max(0.001f, tapReleaseSec));
                }
                else if (previewPulse && inReloadState && (cc == null || !cc.Reloading))
                {
                    // Test-pose lane: soft periodic press, same envelope shape.
                    float ph = Mathf.Repeat(SimClock.Now * previewHz, 1f) / Mathf.Max(0.001f, previewHz);
                    pulse = ph < tapAttackSec ? ph / Mathf.Max(0.001f, tapAttackSec)
                        : Mathf.Exp(-(ph - tapAttackSec) / Mathf.Max(0.001f, tapReleaseSec));
                }
            }

            Pulse = pulse;
            animRef.tapPulse = pulse;
        }
    }
}
