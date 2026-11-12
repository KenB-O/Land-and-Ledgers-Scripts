using UnityEngine;
using LandLedgers.Time;

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
        private float authoredAnimatorSpeed = 1f;
        private bool authoredSpeedCaptured;

        public TransportVisualState State => state;

        public void BindAnimator(Animator rigAnimator)
        {
            if (rigAnimator == null || rigAnimator == animator) return;
            if (animator != null && authoredSpeedCaptured) animator.speed = authoredAnimatorSpeed;
            animator = rigAnimator;
            authoredSpeedCaptured = false;
            CaptureAnimatorDefaults();
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            CaptureAnimatorDefaults();
        }

        private void Update()
        {
            if (animator == null || !authoredSpeedCaptured) return;
            TimeManager clock = TimeManager.Instance;
            animator.speed = SimulationAnimationScale.Resolve(
                authoredAnimatorSpeed,
                clock != null && clock.IsPaused,
                clock != null ? clock.CurrentGameSecondsPerRealSecond : 1f);
        }

        private void CaptureAnimatorDefaults()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false;
            if (!authoredSpeedCaptured)
            {
                authoredAnimatorSpeed = animator.speed;
                authoredSpeedCaptured = true;
            }
        }

        public void SetState(TransportVisualState next)
        {
            CaptureAnimatorDefaults();
            state = next;
            if (animator == null) return;
            animator.applyRootMotion = false;
            string stateName = next == TransportVisualState.DrivingWagon
                ? "DrivingWagon"
                : next == TransportVisualState.MountedHorse ? "MountedHorse" : "Idle";
            int hash = Animator.StringToHash(stateName);
            if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, 0.1f);
        }

        private void OnDisable()
        {
            if (animator != null && authoredSpeedCaptured) animator.speed = authoredAnimatorSpeed;
        }
    }
}
