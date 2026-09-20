using UnityEngine;

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    public sealed class BuildingDoorAnimator : MonoBehaviour
    {
        private const string DoorActionTriggerName = "DoorAction";
        private static readonly int DoorActionTriggerId = Animator.StringToHash(DoorActionTriggerName);

        [SerializeField]
        private Animator doorAnimator;

        private bool setupValidated;
        private bool hasDoorActionTrigger;
        private int lastTriggerFrame = -1;

        public void TriggerDoorAction()
        {
            if (this == null)
            {
                return;
            }

            EnsureAnimator();
            ValidateSetup();

            if (doorAnimator == null || !hasDoorActionTrigger || !doorAnimator.isInitialized || lastTriggerFrame == UnityEngine.Time.frameCount)
            {
                return;
            }

            lastTriggerFrame = UnityEngine.Time.frameCount;
            try
            {
                doorAnimator.SetTrigger(DoorActionTriggerId);
            }
            catch (System.Exception)
            {
                hasDoorActionTrigger = false;
            }
        }

        private void EnsureAnimator()
        {
            if (doorAnimator == null)
            {
                doorAnimator = GetComponentInChildren<Animator>(true);
            }
        }

        private void ValidateSetup()
        {
            if (setupValidated)
            {
                return;
            }

            setupValidated = true;
            hasDoorActionTrigger = false;

            if (doorAnimator == null || doorAnimator.runtimeAnimatorController == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters;
            try
            {
                parameters = doorAnimator.parameters;
            }
            catch (System.Exception)
            {
                return;
            }
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.nameHash == DoorActionTriggerId)
                {
                    hasDoorActionTrigger = true;
                    return;
                }
            }
        }

        private void Awake()
        {
            EnsureAnimator();
        }

        private void Reset()
        {
            EnsureAnimator();
            setupValidated = false;
        }

        private void OnValidate()
        {
            setupValidated = false;
            EnsureAnimator();
        }
    }
}
