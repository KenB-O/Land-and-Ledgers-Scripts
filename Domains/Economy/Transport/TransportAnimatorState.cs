using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    /// <summary>
    /// Visual-only animation bridge. Movement and economic state never use root motion.
    /// The controller exposes stable gameplay state names so clips can be replaced later.
    /// </summary>
    public sealed class TransportAnimatorState : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private TransportVisualState state;

        public TransportVisualState State => state;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator != null) animator.applyRootMotion = false;
        }

        public void SetState(TransportVisualState next)
        {
            state = next;
            if (animator == null) return;
            animator.applyRootMotion = false;
            string stateName = next == TransportVisualState.DrivingWagon
                ? "DrivingWagon"
                : next == TransportVisualState.MountedHorse ? "MountedHorse" : "Idle";
            int hash = Animator.StringToHash(stateName);
            if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, 0.1f);
        }
    }
}
