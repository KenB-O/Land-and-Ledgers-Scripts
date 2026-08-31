using System;
using System.Collections.Generic;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Pathing
{
    [DisallowMultipleComponent]
    public sealed class AgentMover : MonoBehaviour
    {
        private enum AgentMoveState
        {
            Hidden = 0,
            Moving = 1,
            DoorPause = 2,
            ArrivedVisible = 3
        }

        [Header("References")]
        [SerializeField]
        private PathingManager pathingManager;

        [SerializeField]
        [Tooltip("Animator float parameter used by the imported character controllers. 0=idle, 1=walk.")]
        private string locomotionBlendParameter = "Blend";

        [SerializeField]
        [Tooltip("Optional state name to force when movement starts. The imported Man/Woman controllers use Locomotion.")]
        private string locomotionStateName = "Locomotion";

        [SerializeField]
        [Tooltip("Maximum multiplier applied to path movement when simulation time is fast-forwarded.")]
        private float maxTimeScaleMovementMultiplier = 12f;

        [SerializeField]
        [Tooltip("Maximum multiplier applied to walk animation when simulation time is fast-forwarded.")]
        private float maxTimeScaleAnimationMultiplier = 6f;

        [SerializeField]
        [Tooltip("Animation playback multiplier when walking at 1x simulation speed.")]
        private float baseWalkAnimationSpeed = 1.25f;

        [SerializeField]
        [Tooltip("Seconds used to blend from idle into walk. Keeps the simple Man/Woman locomotion tree from popping on route start.")]
        private float walkBlendInSeconds = 0.14f;

        [SerializeField]
        [Tooltip("Seconds used to blend from walk back into idle at doors and pauses.")]
        private float walkBlendOutSeconds = 0.1f;

        [SerializeField]
        [Tooltip("Rebinds the imported rig when an agent is made visible again. This clears stale retargeted bone poses before the walk state starts.")]
        private bool rebindAnimatorOnShow = true;

        [Header("Visual Walk Fallback")]
        [SerializeField]
        [Tooltip("Use the imported Animator blend tree for walking. Leave disabled for generated town agents; the procedural fallback is more reliable across mixed character rigs.")]
        private bool useAnimatorLocomotion;

        [SerializeField]
        [Tooltip("Applies a lightweight procedural bob and limb swing while the path root moves. This keeps street agents visibly walking even when imported clips retarget badly.")]
        private bool useProceduralWalkFallback = true;

        [SerializeField]
        [Tooltip("Optional visual child to animate independently from the path root. PopulationPathingDirector configures this for generated agents.")]
        private Transform visualMotionRoot;

        [SerializeField, Min(0.1f)]
        private float proceduralWalkFrequency = 1.85f;

        [SerializeField, Range(0f, 0.25f)]
        private float proceduralBobHeight = 0.035f;

        [SerializeField, Range(0f, 0.2f)]
        private float proceduralSideSway = 0.025f;

        [SerializeField, Range(0f, 12f)]
        private float proceduralBodyTiltDegrees = 2.5f;

        [SerializeField, Range(0f, 45f)]
        private float proceduralLimbSwingDegrees = 18f;

        [SerializeField, Min(0.1f)]
        private float proceduralIdleReturnSpeed = 10f;

        [Header("Runtime State")]
        [SerializeField]
        private AgentMoveState state = AgentMoveState.Hidden;

        [SerializeField]
        private int waypointIndex;

        [SerializeField]
        private float doorPauseTimer;

        [SerializeField]
        private bool hideOnArrival = true;

        private readonly List<GridCoord> currentPath = new();
        private BuildingAnchor currentTargetAnchor;
        private Renderer[] cachedRenderers;
        private Collider[] cachedColliders;
        private Animator cachedAnimator;
        private BuildingDoorAnimator currentStartDoorAnimator;
        private BuildingDoorAnimator currentTargetDoorAnimator;
        private int locomotionBlendParameterId;
        private int locomotionStateId;
        private bool lastAnimatorWalking;
        private bool animatorSetupValidated;
        private bool animatorCanDriveLocomotion;
        private bool animatorHasBlendParameter;
        private bool targetDoorActionTriggeredForRoute;
        private bool arrivalEventRaisedForRoute;
        private float currentLocomotionBlend;
        private float locomotionBlendVelocity;
        private Vector3 visualBaseLocalPosition;
        private Quaternion visualBaseLocalRotation = Quaternion.identity;
        private bool visualBasePoseCached;
        private bool proceduralBonesCached;
        private float proceduralWalkTime;
        private Transform leftUpperArm;
        private Transform rightUpperArm;
        private Transform leftUpperLeg;
        private Transform rightUpperLeg;
        private Transform leftLowerLeg;
        private Transform rightLowerLeg;
        private Quaternion leftUpperArmBaseRotation = Quaternion.identity;
        private Quaternion rightUpperArmBaseRotation = Quaternion.identity;
        private Quaternion leftUpperLegBaseRotation = Quaternion.identity;
        private Quaternion rightUpperLegBaseRotation = Quaternion.identity;
        private Quaternion leftLowerLegBaseRotation = Quaternion.identity;
        private Quaternion rightLowerLegBaseRotation = Quaternion.identity;

        public IReadOnlyList<GridCoord> CurrentPath => currentPath;
        public bool IsHidden => state == AgentMoveState.Hidden;
        public bool IsMoving => state == AgentMoveState.Moving;
        public bool IsRouteActive => state == AgentMoveState.Moving || state == AgentMoveState.DoorPause;
        public bool IsArrivedVisible => state == AgentMoveState.ArrivedVisible;
        public event Action<AgentMover> ArrivedAtDestination;

        public void Configure(PathingManager newPathingManager)
        {
            pathingManager = newPathingManager;
        }

        public void ConfigureVisualRoot(Transform newVisualMotionRoot)
        {
            visualMotionRoot = newVisualMotionRoot;
            visualBasePoseCached = false;
            proceduralBonesCached = false;
            cachedRenderers = null;
            cachedColliders = null;
        }

        public void HideImmediately()
        {
            state = AgentMoveState.Hidden;
            waypointIndex = 0;
            doorPauseTimer = 0f;
            hideOnArrival = true;
            currentPath.Clear();
            currentTargetAnchor = default;
            currentStartDoorAnimator = null;
            currentTargetDoorAnimator = null;
            targetDoorActionTriggeredForRoute = false;
            arrivalEventRaisedForRoute = false;
            currentLocomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            lastAnimatorWalking = false;

            SetVisible(false);
            UpdateAnimatorState(true);
            ResetProceduralVisualPose(true);
        }

        public bool BeginDoorToDoor(BuildingAnchor start, BuildingAnchor target, bool hideWhenArrived = true, BuildingDoorAnimator startDoorAnimator = null, BuildingDoorAnimator targetDoorAnimator = null)
        {
            EnsureReferences();
            if (pathingManager == null)
            {
                Debug.LogWarning("AgentMover cannot move without a PathingManager.", this);
                return false;
            }

            if (!pathingManager.TryFindPath(start, target, out PathingResult result, this, $"{name} door-to-door"))
            {
                return false;
            }

            StartRoute(result.Path, start.coord, target, hideWhenArrived, startDoorAnimator, targetDoorAnimator);
            return true;
        }

        public bool MoveFromCurrentCellTo(GridCoord target, bool hideWhenArrived = true)
        {
            EnsureReferences();
            if (pathingManager == null || pathingManager.Grid == null)
            {
                return false;
            }

            GridCoord start = pathingManager.Grid.WorldToCoord(transform.position);
            if (!pathingManager.TryFindPath(start, target, out PathingResult result, this, $"{name} cell-to-cell"))
            {
                return false;
            }

            BuildingAnchor targetAnchor = new BuildingAnchor { type = AnchorType.FrontDoor, coord = target };
            StartRoute(result.Path, start, targetAnchor, hideWhenArrived, null, null);
            return true;
        }

        private void StartRoute(IReadOnlyList<GridCoord> path, GridCoord startCoord, BuildingAnchor targetAnchor, bool hideWhenArrived, BuildingDoorAnimator startDoorAnimator, BuildingDoorAnimator targetDoorAnimator)
        {
            currentTargetAnchor = targetAnchor;
            currentStartDoorAnimator = startDoorAnimator;
            currentTargetDoorAnimator = targetDoorAnimator;
            targetDoorActionTriggeredForRoute = false;
            arrivalEventRaisedForRoute = false;
            hideOnArrival = hideWhenArrived;
            currentPath.Clear();

            if (path != null)
            {
                currentPath.AddRange(path);
            }

            waypointIndex = currentPath.Count > 1 ? 1 : 0;
            doorPauseTimer = 0f;
            currentLocomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            lastAnimatorWalking = false;
            transform.position = pathingManager != null ? pathingManager.CoordToPathWorld(startCoord) : transform.position;
            SetVisible(true);
            state = currentPath.Count <= 1 ? AgentMoveState.DoorPause : AgentMoveState.Moving;
            TriggerDoorActionIfAlive(ref currentStartDoorAnimator);
            if (state == AgentMoveState.DoorPause)
            {
                TriggerTargetDoorAction();
            }

            UpdateAnimatorState(true);
        }

        private void Update()
        {
            if (state == AgentMoveState.Moving)
            {
                TickMove(UnityEngine.Time.deltaTime);
            }
            else if (state == AgentMoveState.DoorPause)
            {
                TickDoorPause(UnityEngine.Time.deltaTime);
            }
        }

        private void LateUpdate()
        {
            UpdateProceduralVisualMotion(UnityEngine.Time.deltaTime);
        }

        private void TickMove(float deltaTime)
        {
            if (pathingManager == null || pathingManager.Settings == null || currentPath.Count == 0 || waypointIndex >= currentPath.Count)
            {
                CompleteRouteArrival();
                return;
            }

            PathingSettings settings = pathingManager.Settings;
            float timeScale = GetMovementTimeScale();
            Vector3 target = pathingManager.CoordToPathWorld(currentPath[waypointIndex]);
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;

            if (timeScale <= 0f)
            {
                UpdateAnimatorState();
                return;
            }

            if (toTarget.sqrMagnitude <= settings.waypointArrivalDistance * settings.waypointArrivalDistance)
            {
                waypointIndex++;
                if (waypointIndex >= currentPath.Count)
                {
                    transform.position = pathingManager.CoordToPathWorld(currentTargetAnchor.coord);
                    BeginDoorPause();
                }

                return;
            }

            float step = settings.pedestrianSpeedMetersPerSecond * timeScale * deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, target, step);

            if (toTarget.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    settings.turnSpeedDegreesPerSecond * timeScale * deltaTime);
            }

            UpdateAnimatorState();
        }

        private void TickDoorPause(float deltaTime)
        {
            PathingSettings settings = pathingManager != null ? pathingManager.Settings : null;
            float pauseDuration = settings != null ? settings.doorArrivalPauseSeconds : 1.1f;
            doorPauseTimer += deltaTime * Mathf.Max(1f, GetMovementTimeScale());
            if (doorPauseTimer < pauseDuration)
            {
                UpdateAnimatorState();
                return;
            }

            CompleteRouteArrival();
        }

        private void CompleteRouteArrival()
        {
            if (hideOnArrival)
            {
                SetVisible(false);
                state = AgentMoveState.Hidden;
            }
            else
            {
                SetVisible(true);
                state = AgentMoveState.ArrivedVisible;
            }

            waypointIndex = currentPath.Count;
            doorPauseTimer = 0f;
            currentStartDoorAnimator = null;
            currentTargetDoorAnimator = null;
            targetDoorActionTriggeredForRoute = false;
            UpdateAnimatorState(true);
            ResetProceduralVisualPose(true);

            if (!arrivalEventRaisedForRoute)
            {
                arrivalEventRaisedForRoute = true;
                ArrivedAtDestination?.Invoke(this);
            }
        }

        private void BeginDoorPause()
        {
            doorPauseTimer = 0f;
            state = AgentMoveState.DoorPause;
            TriggerTargetDoorAction();
            UpdateAnimatorState();
        }

        private void TriggerTargetDoorAction()
        {
            if (targetDoorActionTriggeredForRoute)
            {
                return;
            }

            targetDoorActionTriggeredForRoute = true;
            TriggerDoorActionIfAlive(ref currentTargetDoorAnimator);
        }

        private static void TriggerDoorActionIfAlive(ref BuildingDoorAnimator doorAnimator)
        {
            if (doorAnimator == null)
            {
                doorAnimator = null;
                return;
            }

            doorAnimator.TriggerDoorAction();
        }

        private void SetVisible(bool visible)
        {
            CacheRenderComponents();
            if (visible)
            {
                ResetAnimatorRigForShow();
            }
            else
            {
                ResetProceduralVisualPose(true);
            }

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                cachedRenderers[i].enabled = visible;
            }

            for (int i = 0; i < cachedColliders.Length; i++)
            {
                cachedColliders[i].enabled = visible;
            }
        }

        private void CacheRenderComponents()
        {
            cachedRenderers ??= GetComponentsInChildren<Renderer>(true);
            cachedColliders ??= GetComponentsInChildren<Collider>(true);
        }

        private void EnsureReferences()
        {
            if (pathingManager == null)
            {
                pathingManager = FindAnyObjectByType<PathingManager>();
            }

            if (cachedAnimator == null)
            {
                cachedAnimator = GetComponentInChildren<Animator>(true);
                locomotionBlendParameterId = !string.IsNullOrWhiteSpace(locomotionBlendParameter)
                    ? Animator.StringToHash(locomotionBlendParameter)
                    : 0;
                locomotionStateId = !string.IsNullOrWhiteSpace(locomotionStateName)
                    ? Animator.StringToHash(locomotionStateName)
                    : 0;

                if (cachedAnimator != null)
                {
                    cachedAnimator.applyRootMotion = false;
                    cachedAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    ValidateAnimatorSetup();
                }
            }

            EnsureVisualMotionRoot();
        }

        private void Reset()
        {
            EnsureReferences();
        }

        private float GetMovementTimeScale()
        {
            TimeManager timeManager = TimeManager.Instance;
            if (timeManager == null)
            {
                return 1f;
            }

            if (timeManager.IsPaused)
            {
                return 0f;
            }

            return Mathf.Clamp(timeManager.CurrentSpeedMultiplier, 0f, Mathf.Max(1f, maxTimeScaleMovementMultiplier));
        }

        private void UpdateAnimatorState(bool instant = false)
        {
            EnsureReferences();
            if (cachedAnimator == null || cachedAnimator.runtimeAnimatorController == null)
            {
                return;
            }

            float timeScale = GetMovementTimeScale();
            bool walking = state == AgentMoveState.Moving && timeScale > 0f;
            bool driveImportedWalk = useAnimatorLocomotion && animatorCanDriveLocomotion && walking;
            float targetBlend = driveImportedWalk ? 1f : 0f;

            if (animatorHasBlendParameter)
            {
                EnsureLocomotionState(driveImportedWalk ? 0f : cachedAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            }

            if (animatorHasBlendParameter && instant)
            {
                currentLocomotionBlend = targetBlend;
                locomotionBlendVelocity = 0f;
                cachedAnimator.SetFloat(locomotionBlendParameterId, currentLocomotionBlend);
                cachedAnimator.Update(0f);
            }
            else if (animatorHasBlendParameter)
            {
                float dampTime = driveImportedWalk ? walkBlendInSeconds : walkBlendOutSeconds;
                currentLocomotionBlend = Mathf.SmoothDamp(
                    currentLocomotionBlend,
                    targetBlend,
                    ref locomotionBlendVelocity,
                    Mathf.Max(0.001f, dampTime),
                    Mathf.Infinity,
                    UnityEngine.Time.deltaTime);

                if (Mathf.Abs(currentLocomotionBlend - targetBlend) <= 0.001f)
                {
                    currentLocomotionBlend = targetBlend;
                    locomotionBlendVelocity = 0f;
                }

                cachedAnimator.SetFloat(locomotionBlendParameterId, currentLocomotionBlend);
            }

            cachedAnimator.speed = driveImportedWalk
                ? Mathf.Max(0.01f, baseWalkAnimationSpeed * Mathf.Clamp(timeScale, 1f, Mathf.Max(1f, maxTimeScaleAnimationMultiplier)))
                : 1f;
            lastAnimatorWalking = driveImportedWalk;
        }

        private void EnsureLocomotionState(float normalizedTime)
        {
            if (locomotionStateId == 0)
            {
                return;
            }

            AnimatorStateInfo stateInfo = cachedAnimator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.shortNameHash == locomotionStateId)
            {
                return;
            }

            cachedAnimator.Play(locomotionStateId, 0, normalizedTime);
        }

        private void ResetAnimatorRigForShow()
        {
            EnsureReferences();
            ResetProceduralVisualPose(true);
            if (cachedAnimator == null || cachedAnimator.runtimeAnimatorController == null || !rebindAnimatorOnShow)
            {
                return;
            }

            cachedAnimator.Rebind();
            currentLocomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            if (animatorHasBlendParameter)
            {
                EnsureLocomotionState(0f);
                cachedAnimator.SetFloat(locomotionBlendParameterId, currentLocomotionBlend);
            }

            cachedAnimator.Update(0f);
            proceduralBonesCached = false;
            CacheProceduralBones();
            lastAnimatorWalking = false;
        }

        private void OnDisable()
        {
            currentLocomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            lastAnimatorWalking = false;
            if (cachedAnimator != null)
            {
                cachedAnimator.speed = 1f;
                if (CanWriteAnimatorBlendParameter())
                {
                    cachedAnimator.SetFloat(locomotionBlendParameterId, 0f);
                }
            }

            ResetProceduralVisualPose(true);
        }

        private bool CanWriteAnimatorBlendParameter()
        {
            return cachedAnimator != null
                && cachedAnimator.runtimeAnimatorController != null
                && animatorHasBlendParameter;
        }

        private void OnValidate()
        {
            maxTimeScaleMovementMultiplier = Mathf.Max(1f, maxTimeScaleMovementMultiplier);
            maxTimeScaleAnimationMultiplier = Mathf.Max(1f, maxTimeScaleAnimationMultiplier);
            baseWalkAnimationSpeed = Mathf.Max(0.01f, baseWalkAnimationSpeed);
            walkBlendInSeconds = Mathf.Max(0.001f, walkBlendInSeconds);
            walkBlendOutSeconds = Mathf.Max(0.001f, walkBlendOutSeconds);
            proceduralWalkFrequency = Mathf.Max(0.1f, proceduralWalkFrequency);
            proceduralIdleReturnSpeed = Mathf.Max(0.1f, proceduralIdleReturnSpeed);
        }

        private void ValidateAnimatorSetup()
        {
            if (animatorSetupValidated || cachedAnimator == null)
            {
                return;
            }

            animatorSetupValidated = true;
            animatorCanDriveLocomotion = false;
            animatorHasBlendParameter = false;

            if (cachedAnimator.runtimeAnimatorController == null)
            {
                if (useAnimatorLocomotion)
                {
                    Debug.LogWarning($"AgentMover on '{name}' found an Animator without a controller. Imported movement animation will be disabled.", this);
                }

                return;
            }

            Avatar avatar = cachedAnimator.avatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                if (useAnimatorLocomotion)
                {
                    Debug.LogWarning($"AgentMover on '{name}' found an invalid humanoid Avatar. Imported movement animation will be disabled to avoid broken deformation.", this);
                }

                return;
            }

            bool hasBlendParameter = false;
            AnimatorControllerParameter[] parameters = cachedAnimator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == locomotionBlendParameterId)
                {
                    hasBlendParameter = true;
                    break;
                }
            }

            animatorHasBlendParameter = hasBlendParameter;
            if (!hasBlendParameter)
            {
                if (useAnimatorLocomotion)
                {
                    Debug.LogWarning($"AgentMover on '{name}' could not find float Animator parameter '{locomotionBlendParameter}'. Imported movement animation will be disabled.", this);
                }

                return;
            }

            animatorCanDriveLocomotion = useAnimatorLocomotion;
        }

        private void EnsureVisualMotionRoot()
        {
            if (visualMotionRoot == null)
            {
                if (cachedAnimator != null && cachedAnimator.transform != transform)
                {
                    visualMotionRoot = cachedAnimator.transform;
                }
                else
                {
                    Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        if (renderers[i] != null && renderers[i].transform != transform)
                        {
                            visualMotionRoot = renderers[i].transform;
                            break;
                        }
                    }

                    if (visualMotionRoot == null)
                    {
                        visualMotionRoot = transform.childCount > 0 ? transform.GetChild(0) : transform;
                    }
                }
            }

            CacheVisualBasePose();
            CacheProceduralBones();
        }

        private void CacheVisualBasePose()
        {
            if (visualBasePoseCached || visualMotionRoot == null)
            {
                return;
            }

            visualBaseLocalPosition = visualMotionRoot.localPosition;
            visualBaseLocalRotation = visualMotionRoot.localRotation;
            visualBasePoseCached = true;
        }

        private void CacheProceduralBones()
        {
            if (proceduralBonesCached || visualMotionRoot == null)
            {
                return;
            }

            proceduralBonesCached = true;
            leftUpperArm = FindDescendantByNormalizedName(visualMotionRoot, "mixamorigleftarm", "leftarm", "leftupperarm", "upperarml", "lupperarm");
            rightUpperArm = FindDescendantByNormalizedName(visualMotionRoot, "mixamorigrightarm", "rightarm", "rightupperarm", "upperarmr", "rupperarm");
            leftUpperLeg = FindDescendantByNormalizedName(visualMotionRoot, "mixamorigleftupleg", "leftupleg", "leftthigh", "thighl", "lthigh");
            rightUpperLeg = FindDescendantByNormalizedName(visualMotionRoot, "mixamorigrightupleg", "rightupleg", "rightthigh", "thighr", "rthigh");
            leftLowerLeg = FindDescendantByNormalizedName(visualMotionRoot, "mixamorigleftleg", "leftleg", "leftcalf", "calfl", "lcalf");
            rightLowerLeg = FindDescendantByNormalizedName(visualMotionRoot, "mixamorigrightleg", "rightleg", "rightcalf", "calfr", "rcalf");

            if (leftUpperArm != null)
            {
                leftUpperArmBaseRotation = leftUpperArm.localRotation;
            }

            if (rightUpperArm != null)
            {
                rightUpperArmBaseRotation = rightUpperArm.localRotation;
            }

            if (leftUpperLeg != null)
            {
                leftUpperLegBaseRotation = leftUpperLeg.localRotation;
            }

            if (rightUpperLeg != null)
            {
                rightUpperLegBaseRotation = rightUpperLeg.localRotation;
            }

            if (leftLowerLeg != null)
            {
                leftLowerLegBaseRotation = leftLowerLeg.localRotation;
            }

            if (rightLowerLeg != null)
            {
                rightLowerLegBaseRotation = rightLowerLeg.localRotation;
            }
        }

        private void UpdateProceduralVisualMotion(float deltaTime)
        {
            if (!useProceduralWalkFallback)
            {
                return;
            }

            EnsureReferences();
            if (visualMotionRoot == null)
            {
                return;
            }

            float timeScale = GetMovementTimeScale();
            bool walking = state == AgentMoveState.Moving && timeScale > 0f && !lastAnimatorWalking;
            if (!walking)
            {
                ResetProceduralVisualPose(false, deltaTime);
                return;
            }

            float animationTimeScale = Mathf.Clamp(timeScale, 1f, Mathf.Max(1f, maxTimeScaleAnimationMultiplier));
            proceduralWalkTime += deltaTime * proceduralWalkFrequency * animationTimeScale;
            float cycle = proceduralWalkTime * Mathf.PI * 2f;
            float stride = Mathf.Sin(cycle);
            float oppositeStride = -stride;
            float footfall = Mathf.Abs(stride);

            if (visualMotionRoot != transform && visualBasePoseCached)
            {
                Vector3 localOffset = new Vector3(stride * proceduralSideSway, footfall * proceduralBobHeight, 0f);
                Quaternion localTilt = Quaternion.Euler(
                    footfall * proceduralBodyTiltDegrees,
                    0f,
                    -stride * proceduralBodyTiltDegrees);
                visualMotionRoot.localPosition = visualBaseLocalPosition + localOffset;
                visualMotionRoot.localRotation = visualBaseLocalRotation * localTilt;
            }

            ApplyProceduralBoneRotation(leftUpperArm, leftUpperArmBaseRotation, stride * proceduralLimbSwingDegrees);
            ApplyProceduralBoneRotation(rightUpperArm, rightUpperArmBaseRotation, oppositeStride * proceduralLimbSwingDegrees);
            ApplyProceduralBoneRotation(leftUpperLeg, leftUpperLegBaseRotation, oppositeStride * proceduralLimbSwingDegrees * 0.75f);
            ApplyProceduralBoneRotation(rightUpperLeg, rightUpperLegBaseRotation, stride * proceduralLimbSwingDegrees * 0.75f);
            ApplyProceduralBoneRotation(leftLowerLeg, leftLowerLegBaseRotation, Mathf.Max(0f, stride) * proceduralLimbSwingDegrees * 0.45f);
            ApplyProceduralBoneRotation(rightLowerLeg, rightLowerLegBaseRotation, Mathf.Max(0f, oppositeStride) * proceduralLimbSwingDegrees * 0.45f);
        }

        private void ResetProceduralVisualPose(bool instant)
        {
            ResetProceduralVisualPose(instant, UnityEngine.Time.deltaTime);
        }

        private void ResetProceduralVisualPose(bool instant, float deltaTime)
        {
            EnsureVisualMotionRoot();
            proceduralWalkTime = instant ? 0f : proceduralWalkTime;
            float t = instant ? 1f : Mathf.Clamp01(deltaTime * proceduralIdleReturnSpeed);

            if (visualMotionRoot != null && visualMotionRoot != transform && visualBasePoseCached)
            {
                visualMotionRoot.localPosition = Vector3.Lerp(visualMotionRoot.localPosition, visualBaseLocalPosition, t);
                visualMotionRoot.localRotation = Quaternion.Slerp(visualMotionRoot.localRotation, visualBaseLocalRotation, t);
            }

            ResetBoneRotation(leftUpperArm, leftUpperArmBaseRotation, t);
            ResetBoneRotation(rightUpperArm, rightUpperArmBaseRotation, t);
            ResetBoneRotation(leftUpperLeg, leftUpperLegBaseRotation, t);
            ResetBoneRotation(rightUpperLeg, rightUpperLegBaseRotation, t);
            ResetBoneRotation(leftLowerLeg, leftLowerLegBaseRotation, t);
            ResetBoneRotation(rightLowerLeg, rightLowerLegBaseRotation, t);
        }

        private static void ApplyProceduralBoneRotation(Transform bone, Quaternion baseRotation, float swingDegrees)
        {
            if (bone == null)
            {
                return;
            }

            bone.localRotation = baseRotation * Quaternion.AngleAxis(swingDegrees, Vector3.right);
        }

        private static void ResetBoneRotation(Transform bone, Quaternion baseRotation, float t)
        {
            if (bone == null)
            {
                return;
            }

            bone.localRotation = Quaternion.Slerp(bone.localRotation, baseRotation, t);
        }

        private static Transform FindDescendantByNormalizedName(Transform root, params string[] normalizedNames)
        {
            if (root == null)
            {
                return null;
            }

            string rootName = NormalizeTransformName(root.name);
            for (int i = 0; i < normalizedNames.Length; i++)
            {
                if (rootName == normalizedNames[i])
                {
                    return root;
                }
            }

            for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                Transform match = FindDescendantByNormalizedName(root.GetChild(childIndex), normalizedNames);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static string NormalizeTransformName(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }

            char[] buffer = new char[source.Length];
            int length = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsLetterOrDigit(c))
                {
                    buffer[length] = char.ToLowerInvariant(c);
                    length++;
                }
            }

            return new string(buffer, 0, length);
        }

        private void OnDrawGizmosSelected()
        {
            if (pathingManager == null || pathingManager.Settings == null || !pathingManager.Settings.drawSelectedAgentPath || currentPath.Count < 2)
            {
                return;
            }

            PathingSettings settings = pathingManager.Settings;
            Gizmos.color = settings.selectedAgentPathColor;
            for (int i = 1; i < currentPath.Count; i++)
            {
                Vector3 a = pathingManager.CoordToPathWorld(currentPath[i - 1]) + Vector3.up * settings.debugLineHeight;
                Vector3 b = pathingManager.CoordToPathWorld(currentPath[i]) + Vector3.up * settings.debugLineHeight;
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
