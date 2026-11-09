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
        [Tooltip("Use imported locomotion when the Avatar, state and blend parameter validate. Invalid rigs retain the procedural fallback.")]
        private bool useAnimatorLocomotion = true;

        [SerializeField, Min(0.01f)]
        [Tooltip("Distance travelled per second at clip speed 1, per unit of Humanoid scale. Calibrated from the imported walk's source stride.")]
        private float walkMetresPerSecondPerHumanScale = 1.46f;

        [SerializeField, Min(0f)]
        [Tooltip("Pelvis clearance above the DriverSeat point in metres.")]
        private float seatedPelvisClearance = 0.04f;
        private Transform presentationSeat;
        private Transform presentationHips;
        [SerializeField]
        [Tooltip("Ankle target relative to the pelvis seat, in metres: lateral spacing, step height, forward reach.")]
        private Vector3 seatedFootOffsetMetres = new Vector3(0.08f, -0.23f, 0.33f);
        private readonly Transform[] seatedThighs = new Transform[2];
        private readonly Transform[] seatedKnees = new Transform[2];
        private readonly Transform[] seatedFeet = new Transform[2];
        private readonly Transform[] seatedToes = new Transform[2];
        private int presentationAnimationStateId;
        private bool proceduralPoseApplied;
        private float measuredMovementSpeed;

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
        public int RemainingPathWaypointCount => Math.Max(0, currentPath.Count - waypointIndex);
        public event Action<AgentMover> ArrivedAtDestination;

        public bool HasPresentationAnimation => presentationAnimationStateId != 0;

        /// <summary>Claims animation only; person identity, routes and vehicle movement remain externally owned.</summary>
        public bool TrySetPresentationAnimation(string stateName, Transform seat = null)
        {
            EnsureReferences();
            int hash = Animator.StringToHash(stateName);
            if (cachedAnimator == null || cachedAnimator.runtimeAnimatorController == null
                || !cachedAnimator.HasState(0, hash)) return false;
            presentationSeat = seat;
            presentationHips = seat != null && cachedAnimator.isHuman
                ? cachedAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (presentationHips != null)
            {
                seatedThighs[0] = cachedAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                seatedThighs[1] = cachedAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                seatedKnees[0] = cachedAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                seatedKnees[1] = cachedAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                seatedFeet[0] = cachedAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
                seatedFeet[1] = cachedAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
                seatedToes[0] = cachedAnimator.GetBoneTransform(HumanBodyBones.LeftToes);
                seatedToes[1] = cachedAnimator.GetBoneTransform(HumanBodyBones.RightToes);
            }
            if (presentationAnimationStateId == hash) return true;
            ResetProceduralVisualPose(true);
            presentationAnimationStateId = hash;
            currentLocomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            lastAnimatorWalking = false;
            cachedAnimator.applyRootMotion = false;
            cachedAnimator.speed = 1f;
            cachedAnimator.CrossFadeInFixedTime(hash, walkBlendInSeconds);
            return true;
        }

        public void ClearPresentationAnimation()
        {
            if (presentationAnimationStateId == 0) return;
            presentationAnimationStateId = 0;
            presentationSeat = null;
            presentationHips = null;
            if (visualMotionRoot != null && visualMotionRoot != transform && visualBasePoseCached)
                visualMotionRoot.localPosition = visualBaseLocalPosition;
            measuredMovementSpeed = 0f;
            currentLocomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            if (cachedAnimator != null && cachedAnimator.runtimeAnimatorController != null)
                cachedAnimator.CrossFadeInFixedTime(locomotionStateId, walkBlendOutSeconds);
        }

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

        /// <summary>
        /// Keeps this person's visual actor present at its current authoritative
        /// location without starting a route. Population actors currently have no
        /// authored interior representation; hiding the actor at every doorway
        /// made the whole town appear empty even though the Person remained live.
        /// </summary>
        public void ShowAtCurrentLocation()
        {
            state = AgentMoveState.ArrivedVisible;
            doorPauseTimer = 0f;
            hideOnArrival = false;
            currentPath.Clear();
            waypointIndex = 0;
            currentTargetAnchor = default;
            currentStartDoorAnimator = null;
            currentTargetDoorAnimator = null;
            targetDoorActionTriggeredForRoute = false;
            arrivalEventRaisedForRoute = true;
            currentLocomotionBlend = 0f;
            locomotionBlendVelocity = 0f;
            lastAnimatorWalking = false;

            SetVisible(true);
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
            float realDeltaSeconds = UnityEngine.Time.unscaledDeltaTime;
            float simulationDeltaSeconds = realDeltaSeconds * GetMovementTimeScale();
            if (state == AgentMoveState.Moving)
            {
                TickMove(simulationDeltaSeconds);
            }
            else if (state == AgentMoveState.DoorPause)
            {
                TickDoorPause(simulationDeltaSeconds);
            }
        }

        private void LateUpdate()
        {
            if (state == AgentMoveState.ArrivedVisible || HasPresentationAnimation)
                UpdateAnimatorState();
            UpdateProceduralVisualMotion(UnityEngine.Time.unscaledDeltaTime);
            if (HasPresentationAnimation && presentationSeat != null && presentationHips != null
                && visualMotionRoot != null && visualMotionRoot != transform)
            {
                Vector3 correction = presentationSeat.position + presentationSeat.up * seatedPelvisClearance
                    - presentationHips.position;
                float weight = cachedAnimator.IsInTransition(0)
                    ? 1f - Mathf.Exp(-UnityEngine.Time.unscaledDeltaTime / walkBlendInSeconds) : 1f;
                visualMotionRoot.position += correction * weight;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 footTarget = presentationSeat.position
                        + transform.right * (i == 0 ? -seatedFootOffsetMetres.x : seatedFootOffsetMetres.x)
                        + presentationSeat.up * seatedFootOffsetMetres.y
                        + transform.forward * seatedFootOffsetMetres.z;
                    AlignSeatedLeg(seatedThighs[i], seatedKnees[i], seatedFeet[i], seatedToes[i], footTarget, weight);
                }
            }
        }

        private void AlignSeatedLeg(Transform thigh, Transform knee, Transform foot, Transform toes, Vector3 target, float weight)
        {
            if (thigh == null || knee == null || foot == null) return;
            Vector3 hip = thigh.position;
            float upperLength = Vector3.Distance(hip, knee.position);
            float lowerLength = Vector3.Distance(knee.position, foot.position);
            Vector3 direction = target - hip;
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.001f,
                upperLength + lowerLength - 0.001f);
            direction.Normalize();
            Vector3 pole = Vector3.ProjectOnPlane(transform.forward, direction).normalized;
            float along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 desiredKnee = hip + direction * along + pole * height;
            Quaternion ankleRotation = foot.rotation;
            Quaternion thighTarget = Quaternion.FromToRotation(knee.position - hip, desiredKnee - hip) * thigh.rotation;
            thigh.rotation = Quaternion.Slerp(thigh.rotation, thighTarget, weight);
            Quaternion kneeTarget = Quaternion.FromToRotation(foot.position - knee.position,
                hip + direction * distance - knee.position) * knee.rotation;
            knee.rotation = Quaternion.Slerp(knee.rotation, kneeTarget, weight);
            foot.rotation = ankleRotation;
            if (toes != null)
            {
                Vector3 toeDirection = toes.position - foot.position;
                Vector3 levelDirection = Vector3.ProjectOnPlane(toeDirection, presentationSeat.up);
                if (levelDirection.sqrMagnitude > 0.0001f)
                    foot.rotation = Quaternion.Slerp(foot.rotation,
                        Quaternion.FromToRotation(toeDirection, levelDirection) * foot.rotation, weight);
            }
        }

        private void TickMove(float simulationDeltaSeconds)
        {
            Vector3 movementStart = transform.position;
            if (pathingManager == null || pathingManager.Settings == null || currentPath.Count == 0 || waypointIndex >= currentPath.Count)
            {
                CompleteRouteArrival();
                return;
            }

            PathingSettings settings = pathingManager.Settings;
            if (simulationDeltaSeconds <= 0f)
            {
                UpdateAnimatorState();
                return;
            }

            float remainingDistance = settings.pedestrianSpeedMetersPerSecond * simulationDeltaSeconds;
            float arrivalDistance = settings.waypointArrivalDistance;
            while (waypointIndex < currentPath.Count)
            {
                Vector3 target = pathingManager.CoordToPathWorld(currentPath[waypointIndex]);
                Vector3 toTarget = target - transform.position;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;

                if (distance <= arrivalDistance)
                {
                    transform.position = target;
                    waypointIndex++;
                    if (waypointIndex >= currentPath.Count)
                    {
                        transform.position = pathingManager.CoordToPathWorld(currentTargetAnchor.coord);
                        BeginDoorPause();
                        break;
                    }
                    continue;
                }

                Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                float turnDegrees = settings.turnSpeedDegreesPerSecond * simulationDeltaSeconds;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnDegrees);
                if (remainingDistance >= distance)
                {
                    transform.position = target;
                    remainingDistance -= distance;
                    waypointIndex++;
                    if (waypointIndex >= currentPath.Count)
                    {
                        transform.position = pathingManager.CoordToPathWorld(currentTargetAnchor.coord);
                        BeginDoorPause();
                        break;
                    }
                    continue;
                }

                transform.position = Vector3.MoveTowards(transform.position, target, remainingDistance);
                remainingDistance = 0f;
                break;
            }

            Vector3 movementDelta = transform.position - movementStart;
            movementDelta.y = 0f;
            measuredMovementSpeed = simulationDeltaSeconds > 0f
                ? movementDelta.magnitude / simulationDeltaSeconds : 0f;
            UpdateAnimatorState();
        }

        private void TickDoorPause(float simulationDeltaSeconds)
        {
            PathingSettings settings = pathingManager != null ? pathingManager.Settings : null;
            if (simulationDeltaSeconds <= 0f)
            {
                UpdateAnimatorState();
                return;
            }

            float pauseDuration = settings != null ? settings.doorArrivalPauseSeconds : 1.1f;
            doorPauseTimer += simulationDeltaSeconds;
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

            return Mathf.Max(0f, timeManager.CurrentGameSecondsPerRealSecond);
        }

        private void UpdateAnimatorState(bool instant = false)
        {
            EnsureReferences();
            if (cachedAnimator == null || cachedAnimator.runtimeAnimatorController == null)
            {
                return;
            }

            float timeScale = GetMovementTimeScale();
            if (HasPresentationAnimation)
            {
                cachedAnimator.speed = timeScale <= 0f ? 0f : 1f;
                return;
            }
            // Other activities may claim a different state directly. Only an explicit
            // route/show reset may reclaim it, never an ordinary locomotion tick.
            if (!instant && animatorHasBlendParameter
                && cachedAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash != locomotionStateId)
                return;

            bool walking = state == AgentMoveState.Moving && timeScale > 0f;
            bool driveImportedWalk = useAnimatorLocomotion && animatorCanDriveLocomotion && walking;
            float targetBlend = driveImportedWalk ? 1f : 0f;

            if (animatorHasBlendParameter && instant && !HasPresentationAnimation)
            {
                EnsureLocomotionState(0f);
            }

            if (animatorHasBlendParameter && instant)
            {
                currentLocomotionBlend = driveImportedWalk ? 0f : targetBlend;
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
                    UnityEngine.Time.unscaledDeltaTime * timeScale);

                if (Mathf.Abs(currentLocomotionBlend - targetBlend) <= 0.001f)
                {
                    currentLocomotionBlend = targetBlend;
                    locomotionBlendVelocity = 0f;
                }

                cachedAnimator.SetFloat(locomotionBlendParameterId, currentLocomotionBlend);
            }

            // Animator time is part of the simulated movement clock. Procedural
            // locomotion and imported locomotion both need to pause and scale
            // with the same simulation rate as the actor's route.
            float walkPlayback = baseWalkAnimationSpeed;
            if (driveImportedWalk)
            {
                float strideSpeed = walkMetresPerSecondPerHumanScale * cachedAnimator.humanScale
                    * Mathf.Abs(cachedAnimator.transform.lossyScale.y);
                walkPlayback = measuredMovementSpeed / Mathf.Max(0.01f, strideSpeed);
            }
            cachedAnimator.speed = timeScale <= 0f ? 0f
                : LandLedgers.Time.SimulationAnimationScale.ResolveMovingActorSpeed(
                    walkPlayback, state == AgentMoveState.Moving, timeScale);
            lastAnimatorWalking = driveImportedWalk;
        }

        private void EnsureLocomotionState(float normalizedTime)
        {
            if (locomotionStateId == 0)
            {
                return;
            }

            AnimatorStateInfo stateInfo = cachedAnimator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.shortNameHash == locomotionStateId
                || (cachedAnimator.IsInTransition(0)
                    && cachedAnimator.GetNextAnimatorStateInfo(0).shortNameHash == locomotionStateId))
            {
                return;
            }

            cachedAnimator.Play(locomotionStateId, 0, normalizedTime);
        }

        private void ResetAnimatorRigForShow()
        {
            EnsureReferences();
            ResetProceduralVisualPose(true);
            if (cachedAnimator == null || cachedAnimator.runtimeAnimatorController == null || !rebindAnimatorOnShow
                || HasPresentationAnimation || cachedAnimator.IsInTransition(0))
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

            animatorCanDriveLocomotion = cachedAnimator.HasState(0, locomotionStateId);
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
            if (HasPresentationAnimation || (useAnimatorLocomotion && animatorCanDriveLocomotion))
            {
                // Do not reset bones here: Animator has already evaluated this frame.
                return;
            }
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

            proceduralPoseApplied = true;
            proceduralWalkTime += deltaTime * proceduralWalkFrequency * timeScale;
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
            if (!proceduralPoseApplied) return;
            EnsureVisualMotionRoot();
            if (instant) proceduralPoseApplied = false;
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
