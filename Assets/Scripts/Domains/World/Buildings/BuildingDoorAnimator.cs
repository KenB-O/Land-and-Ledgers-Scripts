using UnityEngine;
using LandLedgers.Time;

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    public sealed class BuildingDoorAnimator : MonoBehaviour
    {
        private const string DoorActionTriggerName = "DoorAction";
        private static readonly int DoorActionTriggerId = Animator.StringToHash(DoorActionTriggerName);

        [SerializeField]
        private Animator doorAnimator;

        [SerializeField, Tooltip("Optional movable door leaf. Its colliders remain enabled and move with the leaf.")]
        private Transform physicalLeaf;

        [SerializeField, Tooltip("Local rotation offset from the closed pose when the leaf is open.")]
        private Vector3 openLocalEulerOffset = new(0f, 90f, 0f);

        private bool setupValidated;
        private bool hasDoorActionTrigger;
        private int lastTriggerFrame = -1;
        private float authoredAnimatorSpeed = 1f;
        private bool authoredSpeedCaptured;
        private Quaternion closedLeafLocalRotation;
        private bool closedLeafRotationCaptured;
        private bool isOpen;

        public bool HasPhysicalLeaf => physicalLeaf != null;
        public bool IsOpen => isOpen;

        /// <summary>Configures a real hinged leaf; framing and its colliders remain untouched.</summary>
        public bool ConfigurePhysicalLeaf(Transform leaf, Vector3 openedLocalEulerOffset)
        {
            if (leaf == null || leaf == transform || !leaf.IsChildOf(transform)) return false;
            physicalLeaf = leaf;
            openLocalEulerOffset = openedLocalEulerOffset;
            closedLeafLocalRotation = leaf.localRotation;
            closedLeafRotationCaptured = true;
            isOpen = false;
            return true;
        }

        public bool SetDoorOpen(bool open)
        {
            if (physicalLeaf == null) return false;
            if (!closedLeafRotationCaptured)
            {
                closedLeafLocalRotation = physicalLeaf.localRotation;
                closedLeafRotationCaptured = true;
            }
            isOpen = open;
            physicalLeaf.localRotation = open
                ? closedLeafLocalRotation * Quaternion.Euler(openLocalEulerOffset)
                : closedLeafLocalRotation;
            return true;
        }

        public void TriggerDoorAction()
        {
            if (this == null)
            {
                return;
            }

            EnsureAnimator();
            ValidateSetup();

            if (physicalLeaf != null)
                SetDoorOpen(!isOpen);

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

            if (doorAnimator != null && !authoredSpeedCaptured)
            {
                authoredAnimatorSpeed = doorAnimator.speed;
                authoredSpeedCaptured = true;
            }
        }

        private void Update()
        {
            EnsureAnimator();
            if (doorAnimator == null || !authoredSpeedCaptured)
            {
                return;
            }

            TimeManager clock = TimeManager.Instance;
            doorAnimator.speed = SimulationAnimationScale.Resolve(
                authoredAnimatorSpeed,
                clock != null && clock.IsPaused,
                clock != null ? clock.CurrentGameSecondsPerRealSecond : 1f);
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
            CaptureLeafPose();
        }

        private void CaptureLeafPose()
        {
            if (physicalLeaf == null) return;
            closedLeafLocalRotation = physicalLeaf.localRotation;
            closedLeafRotationCaptured = true;
            isOpen = false;
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
            if (physicalLeaf != null && !physicalLeaf.IsChildOf(transform)) physicalLeaf = null;
            if (physicalLeaf != null) CaptureLeafPose();
        }

        private void OnDisable()
        {
            if (doorAnimator != null && authoredSpeedCaptured)
            {
                doorAnimator.speed = authoredAnimatorSpeed;
            }
        }
    }
}
