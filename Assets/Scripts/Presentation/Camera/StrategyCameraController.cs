using UnityEngine;
using UnityEngine.InputSystem;

namespace LandLedgers.CameraSystem
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class StrategyCameraController : MonoBehaviour
    {
        private const float StandardWheelDeltaPerNotch = 120f;

        [Header("Settings")]
        [SerializeField]
        private StrategyCameraSettings settings;

        [Header("Rig References")]
        [SerializeField]
        private Transform yawPivot;

        [SerializeField]
        private Transform pitchPivot;

        [SerializeField]
        private Camera controlledCamera;

        [SerializeField]
        private Transform cameraTransform;

        [SerializeField]
        private StrategyCameraBounds boundsSource;

        [Header("Editor Preview")]
        [SerializeField]
        private bool applySettingsInEditMode = true;

        [Header("Runtime State")]
        [SerializeField]
        private Vector3 targetFocusPosition;

        [SerializeField]
        private float targetYaw;

        [SerializeField]
        private float targetZoomDistance;

        [SerializeField]
        private float targetPitchDegrees;

        [SerializeField]
        private float targetManualPitchOffset;

        [SerializeField]
        private float currentPitch;

        private Vector3 movementVelocity;
        private float yawVelocity;
        private float zoomVelocity;
        private float pitchVelocity;
        private float terrainHeightVelocity;
        private bool cursorLockedByCamera;
        private bool stateInitialized;
        private bool wasRotatingWithMouse;
        private bool closeViewPivotActive;
        private Transform followTarget;

        public StrategyCameraSettings Settings => settings;
        public StrategyCameraBounds BoundsSource => boundsSource;
        public Camera ControlledCamera => controlledCamera;
        public Vector3 FocusPosition => targetFocusPosition;
        public float YawDegrees => targetYaw;
        public float ZoomDistance => targetZoomDistance;
        public float PitchDegrees => currentPitch;
        public float TargetPitchDegrees => targetPitchDegrees;
        public float ManualPitchOffset => targetManualPitchOffset;
        public bool SuppressArrowKeyPan { get; set; }
        public bool SuppressWheelZoomUnlessRotating { get; set; }

        public Transform FollowTarget => followTarget;

        public bool Follow(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            closeViewPivotActive = false;
            followTarget = target;
            targetFocusPosition = target.position;
            return true;
        }

        public void StopFollowing()
        {
            followTarget = null;
            closeViewPivotActive = false;
        }

        public void ConfigureRigReferences(
            StrategyCameraSettings newSettings,
            Transform newYawPivot,
            Transform newPitchPivot,
            Camera newControlledCamera,
            StrategyCameraBounds newBoundsSource)
        {
            settings = newSettings;
            yawPivot = newYawPivot;
            pitchPivot = newPitchPivot;
            controlledCamera = newControlledCamera;
            cameraTransform = newControlledCamera != null ? newControlledCamera.transform : null;
            boundsSource = newBoundsSource;
            InitializeFromSettings(true);
        }

        public void SetFocus(Vector3 worldPosition)
        {
            closeViewPivotActive = false;
            targetFocusPosition = worldPosition;
        }

        public void JumpTo(Vector3 worldFocusPosition, float yawDegrees, float zoomDistance)
        {
            closeViewPivotActive = false;
            targetFocusPosition = worldFocusPosition;
            targetYaw = yawDegrees;
            targetZoomDistance = ClampZoom(zoomDistance);
            targetPitchDegrees = settings != null ? settings.ClampPitch(targetPitchDegrees) : targetPitchDegrees;
            RefreshManualPitchOffset();
            ApplyRigImmediate();
        }

        public void JumpTo(Vector3 worldFocusPosition, float yawDegrees, float zoomDistance, float pitchDegrees)
        {
            closeViewPivotActive = false;
            targetFocusPosition = worldFocusPosition;
            targetYaw = yawDegrees;
            targetZoomDistance = ClampZoom(zoomDistance);
            targetPitchDegrees = settings != null ? settings.ClampPitch(pitchDegrees) : pitchDegrees;
            RefreshManualPitchOffset();
            ApplyRigImmediate();
        }

        [ContextMenu("Find Rig References")]
        public void FindRigReferences()
        {
            yawPivot = yawPivot != null ? yawPivot : transform.Find("YawPivot");
            pitchPivot = pitchPivot != null && pitchPivot.parent == yawPivot
                ? pitchPivot
                : yawPivot != null
                    ? yawPivot.Find("PitchPivot")
                    : transform.Find("PitchPivot");

            if (controlledCamera == null)
            {
                controlledCamera = GetComponentInChildren<Camera>(true);
            }

            cameraTransform = controlledCamera != null ? controlledCamera.transform : cameraTransform;

            if (boundsSource == null)
            {
                boundsSource = GetComponent<StrategyCameraBounds>();
            }
        }

        [ContextMenu("Reset To Settings Defaults")]
        public void ResetToSettingsDefaults()
        {
            InitializeFromSettings(true);
        }

        private void Reset()
        {
            FindRigReferences();
        }

        private void Awake()
        {
            FindRigReferences();
            InitializeFromCurrentRig();
        }

        private void OnEnable()
        {
            FindRigReferences();
            InitializeFromCurrentRig();
        }

        private void OnDisable()
        {
            ReleaseCursorIfNeeded();
            wasRotatingWithMouse = false;
            closeViewPivotActive = false;
        }

        private void OnValidate()
        {
            FindRigReferences();
            settings?.Sanitize();

            if (!Application.isPlaying && applySettingsInEditMode)
            {
                InitializeFromSettings(false);
            }
        }

        private void LateUpdate()
        {
            if (settings == null)
            {
                return;
            }

            settings.Sanitize();

            if (!stateInitialized)
            {
                InitializeFromCurrentRig();
            }

            if (!Application.isPlaying)
            {
                if (applySettingsInEditMode)
                {
                    ApplyRigImmediate();
                }

                return;
            }

            float dt = UnityEngine.Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                return;
            }

            ReadInput(dt);
            if (followTarget != null)
            {
                if (!followTarget.gameObject.activeInHierarchy) followTarget = null;
                else targetFocusPosition = followTarget.position;
            }
            if (targetZoomDistance > settings.closeControlStartDistance)
            {
                closeViewPivotActive = false;
            }
            ApplyBounds(dt);
            if (!closeViewPivotActive && targetZoomDistance > settings.closeControlStartDistance)
            {
                ApplyTerrainTargetHeight(dt);
            }
            SmoothAndApplyRig(dt);
        }

        private void InitializeFromSettings(bool snapTransform)
        {
            if (settings == null)
            {
                return;
            }

            closeViewPivotActive = false;
            settings.Sanitize();
            targetFocusPosition = settings.defaultFocusPosition;
            targetYaw = settings.defaultYaw;
            targetZoomDistance = ClampZoom(settings.defaultZoomDistance);
            targetPitchDegrees = settings.EvaluatePitch(targetZoomDistance, settings.defaultManualPitchOffset);
            RefreshManualPitchOffset();
            currentPitch = targetPitchDegrees;
            stateInitialized = true;

            if (snapTransform || !Application.isPlaying)
            {
                ApplyRigImmediate();
            }
        }

        private void InitializeFromCurrentRig()
        {
            if (settings == null)
            {
                stateInitialized = false;
                return;
            }

            closeViewPivotActive = false;
            settings.Sanitize();
            targetFocusPosition = transform.position;
            targetYaw = yawPivot != null ? yawPivot.localEulerAngles.y : settings.defaultYaw;
            targetZoomDistance = cameraTransform != null
                ? ClampZoom(Mathf.Abs(cameraTransform.localPosition.z))
                : ClampZoom(settings.defaultZoomDistance);

            float rigPitch = pitchPivot != null
                ? NormalizeSignedAngle(pitchPivot.localEulerAngles.x)
                : settings.EvaluatePitch(targetZoomDistance, settings.defaultManualPitchOffset);

            targetPitchDegrees = settings.ClampPitch(rigPitch);
            RefreshManualPitchOffset();
            currentPitch = targetPitchDegrees;
            stateInitialized = true;
        }

        private void ReadInput(float dt)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                UpdateCursorLock(false);
                wasRotatingWithMouse = false;
                return;
            }
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            Vector2 panInput = keyboard != null ? ReadKeyboardPan(keyboard, SuppressArrowKeyPan) : Vector2.zero;

            bool isRotatingWithMouse = mouse != null && IsRotateMouseButtonPressed(mouse);
            if (isRotatingWithMouse && !wasRotatingWithMouse)
            {
                TryAdoptCloseViewPivot();
            }
            wasRotatingWithMouse = isRotatingWithMouse;
            UpdateCursorLock(isRotatingWithMouse);

            if (settings.edgePanEnabled && mouse != null && (!settings.suppressEdgePanWhileRotating || !isRotatingWithMouse))
            {
                panInput += ReadEdgePan(mouse);
            }

            if (panInput.sqrMagnitude > 1f)
            {
                panInput.Normalize();
            }

            if (panInput.sqrMagnitude > 0f)
            {
                closeViewPivotActive = false;
                float speed = settings.EvaluatePanSpeed(targetZoomDistance)
                    * settings.EvaluateClosePanSensitivity(targetZoomDistance);
                if (keyboard != null && IsKeyPressed(keyboard, settings.fastPanKey))
                {
                    speed *= settings.fastPanMultiplier;
                }

                Vector3 panWorld = ToYawSpace(panInput);
                targetFocusPosition += panWorld * speed * dt;
            }

            float yawInput = keyboard != null ? ReadKeyboardYaw(keyboard) * settings.keyboardYawSpeed * dt : 0f;
            float pitchInput = keyboard != null ? ReadKeyboardPitch(keyboard) * settings.keyboardPitchSpeed * dt : 0f;

            if (isRotatingWithMouse)
            {
                Vector2 mouseDelta = mouse.delta.ReadValue();
                yawInput += mouseDelta.x * settings.mouseYawSensitivity;

                float verticalDrag = ApplyAxisDeadZone(mouseDelta.y, settings.mousePitchDeadZonePixels);
                float pitchSign = settings.invertMousePitch ? 1f : -1f;
                pitchInput += verticalDrag * settings.mousePitchSensitivity * pitchSign;
            }

            float rotationSensitivity = settings.EvaluateCloseRotationSensitivity(targetZoomDistance);
            yawInput *= rotationSensitivity;
            pitchInput *= rotationSensitivity;

            targetYaw += yawInput;

            if (Mathf.Abs(pitchInput) > 0.0001f)
            {
                targetPitchDegrees = settings.ClampPitch(targetPitchDegrees + pitchInput);
                RefreshManualPitchOffset();
            }

            if (mouse != null && (!SuppressWheelZoomUnlessRotating || isRotatingWithMouse))
            {
                float scrollY = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scrollY) > 0.01f)
                {
                    float zoomInput = CalculateWheelZoomInput(
                        scrollY,
                        settings.wheelZoomSensitivity,
                        settings.EvaluateWheelZoomMultiplier(targetZoomDistance),
                        settings.EvaluateDistanceWheelZoomScale(targetZoomDistance));
                    float newZoom = ClampZoom(targetZoomDistance - zoomInput);

                    if (settings.focusViewCenterWhenZoomingClose
                        && !closeViewPivotActive
                        && newZoom <= settings.closeControlStartDistance
                        && TryAdoptCloseViewPivot(true))
                    {
                        zoomInput = CalculateWheelZoomInput(
                            scrollY,
                            settings.wheelZoomSensitivity,
                            settings.EvaluateWheelZoomMultiplier(targetZoomDistance),
                            settings.EvaluateDistanceWheelZoomScale(targetZoomDistance));
                        newZoom = ClampZoom(targetZoomDistance - zoomInput);
                    }

                    if (!Mathf.Approximately(newZoom, targetZoomDistance))
                    {
                        float previousManualOffset = targetManualPitchOffset;
                        targetZoomDistance = newZoom;

                        if (settings.preserveAbsolutePitchWhileZooming)
                        {
                            RefreshManualPitchOffset();
                        }
                        else
                        {
                            targetManualPitchOffset = settings.ClampManualPitchOffset(previousManualOffset, targetZoomDistance);
                            targetPitchDegrees = settings.EvaluatePitch(targetZoomDistance, targetManualPitchOffset);
                        }
                    }
                }
            }
        }

        private bool IsRotateMouseButtonPressed(Mouse mouse)
        {
            return settings.rotateMouseButton switch
            {
                CameraRotateMouseButton.RightMouse => mouse.rightButton.isPressed,
                CameraRotateMouseButton.MiddleMouse => mouse.middleButton.isPressed,
                CameraRotateMouseButton.RightOrMiddleMouse => mouse.rightButton.isPressed || mouse.middleButton.isPressed,
                _ => mouse.rightButton.isPressed
            };
        }

        private void UpdateCursorLock(bool isRotatingWithMouse)
        {
            if (!settings.lockCursorWhileRotating)
            {
                ReleaseCursorIfNeeded();
                return;
            }

            if (isRotatingWithMouse && !cursorLockedByCamera)
            {
                cursorLockedByCamera = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (!isRotatingWithMouse)
            {
                ReleaseCursorIfNeeded();
            }
        }

        private void ReleaseCursorIfNeeded()
        {
            if (!cursorLockedByCamera)
            {
                return;
            }

            cursorLockedByCamera = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static Vector2 ReadKeyboardPan(Keyboard keyboard, bool suppressArrowKeys)
        {
            Vector2 input = Vector2.zero;

            if (keyboard.wKey.isPressed || (!suppressArrowKeys && keyboard.upArrowKey.isPressed))
            {
                input.y += 1f;
            }

            if (keyboard.sKey.isPressed || (!suppressArrowKeys && keyboard.downArrowKey.isPressed))
            {
                input.y -= 1f;
            }

            if (keyboard.dKey.isPressed || (!suppressArrowKeys && keyboard.rightArrowKey.isPressed))
            {
                input.x += 1f;
            }

            if (keyboard.aKey.isPressed || (!suppressArrowKeys && keyboard.leftArrowKey.isPressed))
            {
                input.x -= 1f;
            }

            return input;
        }

        private Vector2 ReadEdgePan(Mouse mouse)
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return Vector2.zero;
            }

            Vector2 position = mouse.position.ReadValue();
            float border = Mathf.Max(1f, settings.edgePanBorderPixels);
            return new Vector2(
                EvaluateEdgeAxis(position.x, Screen.width, border),
                EvaluateEdgeAxis(position.y, Screen.height, border)) * settings.edgePanSpeedMultiplier;
        }

        private static float EvaluateEdgeAxis(float position, float screenSize, float border)
        {
            if (position <= border)
            {
                return -Mathf.Clamp01((border - position) / border);
            }

            if (position >= screenSize - border)
            {
                return Mathf.Clamp01((position - (screenSize - border)) / border);
            }

            return 0f;
        }

        private float ReadKeyboardYaw(Keyboard keyboard)
        {
            float input = 0f;

            if (IsKeyPressed(keyboard, settings.rotateLeftKey))
            {
                input -= 1f;
            }

            if (IsKeyPressed(keyboard, settings.rotateRightKey))
            {
                input += 1f;
            }

            return input;
        }

        private float ReadKeyboardPitch(Keyboard keyboard)
        {
            bool raisePressed = IsKeyPressed(keyboard, settings.pitchUpKey);
            bool lowerPressed = IsKeyPressed(keyboard, settings.pitchDownKey);
            return EvaluateRaiseLowerPitchInput(raisePressed, lowerPressed);
        }

        /// <summary>
        /// Resolves camera elevation controls into pitch input. Raising the camera
        /// rig increases its downward viewing angle (positive rig pitch); lowering
        /// it decreases that angle. Opposing inputs cancel.
        /// </summary>
        public static float EvaluateRaiseLowerPitchInput(bool raisePressed, bool lowerPressed)
        {
            return (raisePressed ? 1f : 0f) - (lowerPressed ? 1f : 0f);
        }

        private static bool IsKeyPressed(Keyboard keyboard, Key key)
        {
            return key != Key.None && keyboard[key].isPressed;
        }

        private static float ApplyAxisDeadZone(float value, float deadZone)
        {
            float absValue = Mathf.Abs(value);
            if (absValue <= deadZone)
            {
                return 0f;
            }

            return Mathf.Sign(value) * (absValue - deadZone);
        }

        private static float NormalizeSignedAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }

            return degrees;
        }

        private Vector3 ToYawSpace(Vector2 input)
        {
            Quaternion yawRotation = Quaternion.Euler(0f, targetYaw, 0f);
            Vector3 right = yawRotation * Vector3.right;
            Vector3 forward = yawRotation * Vector3.forward;
            return right * input.x + forward * input.y;
        }

        private void ApplyBounds(float dt)
        {
            if (!TryGetBounds(out Rect inner, out Rect soft, out float returnStrength))
            {
                return;
            }

            Vector2 focus = new(targetFocusPosition.x, targetFocusPosition.z);
            focus.x = Mathf.Clamp(focus.x, soft.xMin, soft.xMax);
            focus.y = Mathf.Clamp(focus.y, soft.yMin, soft.yMax);

            Vector2 innerClamped = new(
                Mathf.Clamp(focus.x, inner.xMin, inner.xMax),
                Mathf.Clamp(focus.y, inner.yMin, inner.yMax));

            Vector2 outsideInner = focus - innerClamped;
            if (outsideInner.sqrMagnitude > 0.0001f && returnStrength > 0f)
            {
                float ease = 1f - Mathf.Exp(-returnStrength * dt);
                focus = Vector2.Lerp(focus, innerClamped, ease);
            }

            targetFocusPosition.x = focus.x;
            targetFocusPosition.z = focus.y;
        }

        private bool TryGetBounds(out Rect inner, out Rect soft, out float returnStrength)
        {
            if (boundsSource != null && boundsSource.BoundsEnabled)
            {
                inner = boundsSource.InnerRect;
                soft = boundsSource.SoftRect;
                returnStrength = boundsSource.ReturnStrength;
                return true;
            }

            if (settings != null && settings.useFallbackBounds)
            {
                Vector2 size = new(Mathf.Max(1f, settings.fallbackBoundsSize.x), Mathf.Max(1f, settings.fallbackBoundsSize.y));
                inner = new Rect(settings.fallbackBoundsCenter - size * 0.5f, size);
                soft = inner;
                float margin = Mathf.Max(0f, settings.fallbackSoftMargin);
                soft.xMin -= margin;
                soft.xMax += margin;
                soft.yMin -= margin;
                soft.yMax += margin;
                returnStrength = settings.fallbackReturnStrength;
                return true;
            }

            inner = default;
            soft = default;
            returnStrength = 0f;
            return false;
        }

        private void ApplyTerrainTargetHeight(float dt)
        {
            if (!settings.terrainFollowEnabled)
            {
                return;
            }

            Vector3 rayOrigin = new(
                targetFocusPosition.x,
                targetFocusPosition.y + settings.terrainRaycastHeight,
                targetFocusPosition.z);

            if (Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    settings.terrainRaycastDistance,
                    settings.terrainLayers,
                    QueryTriggerInteraction.Ignore))
            {
                float targetHeight = hit.point.y + settings.terrainHeightOffset;
                targetFocusPosition.y = Mathf.SmoothDamp(
                    targetFocusPosition.y,
                    targetHeight,
                    ref terrainHeightVelocity,
                    settings.terrainVerticalSmoothing,
                    Mathf.Infinity,
                    dt);
            }
        }

        private bool TryAdoptCloseViewPivot(bool enteringCloseRange = false)
        {
            bool focusView = enteringCloseRange
                ? settings.focusViewCenterWhenZoomingClose
                : settings.focusViewCenterWhenRotatingClose;
            if (!focusView
                || followTarget != null
                || controlledCamera == null
                || cameraTransform == null
                || (!enteringCloseRange && targetZoomDistance > settings.closeControlStartDistance))
            {
                return false;
            }

            float localZoom = Mathf.Abs(cameraTransform.localPosition.z);
            if (localZoom <= 0.001f)
            {
                return false;
            }

            float actualWorldDistance = Vector3.Distance(controlledCamera.transform.position, transform.position);
            float worldPerLocalUnit = actualWorldDistance / localZoom;
            if (worldPerLocalUnit <= 0.001f)
            {
                return false;
            }

            Vector3 cameraPosition = controlledCamera.transform.position;
            Vector3 cameraForward = controlledCamera.transform.forward;
            float maximumPivotDistance = Mathf.Min(actualWorldDistance, targetZoomDistance * worldPerLocalUnit);
            if (!Physics.Raycast(
                    cameraPosition,
                    cameraForward,
                    out RaycastHit hit,
                    maximumPivotDistance,
                    settings.closePivotLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            float minimumWorldDistance = settings.minZoomDistance * worldPerLocalUnit;
            if (hit.distance < minimumWorldDistance || hit.distance >= maximumPivotDistance - 0.05f)
            {
                return false;
            }

            float rebasedZoom = hit.distance / worldPerLocalUnit;
            if (enteringCloseRange && rebasedZoom > settings.closeControlStartDistance)
                return false;

            Vector2 focusXZ = new(hit.point.x, hit.point.z);
            if (TryGetBounds(out _, out Rect softBounds, out _) && !softBounds.Contains(focusXZ))
            {
                return false;
            }

            // The hit lies on the view ray, so rebasing focus and zoom together
            // preserves the camera's world position and viewing direction.
            targetFocusPosition = hit.point;
            targetZoomDistance = ClampZoom(rebasedZoom);
            transform.position = targetFocusPosition;
            movementVelocity = Vector3.zero;
            zoomVelocity = 0f;
            terrainHeightVelocity = 0f;

            if (yawPivot != null)
            {
                targetYaw = yawPivot.localEulerAngles.y;
            }
            if (pitchPivot != null)
            {
                targetPitchDegrees = settings.ClampPitch(NormalizeSignedAngle(pitchPivot.localEulerAngles.x));
                currentPitch = targetPitchDegrees;
            }

            yawVelocity = 0f;
            pitchVelocity = 0f;
            RefreshManualPitchOffset();
            closeViewPivotActive = true;
            ApplyRigImmediate();
            return true;
        }

        private void SmoothAndApplyRig(float dt)
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetFocusPosition,
                ref movementVelocity,
                settings.movementSmoothing,
                Mathf.Infinity,
                dt);

            float currentYaw = yawPivot != null ? yawPivot.localEulerAngles.y : transform.localEulerAngles.y;
            float smoothedYaw = Mathf.SmoothDampAngle(
                currentYaw,
                targetYaw,
                ref yawVelocity,
                settings.rotationSmoothing,
                Mathf.Infinity,
                dt);

            float currentZoom = cameraTransform != null ? Mathf.Abs(cameraTransform.localPosition.z) : targetZoomDistance;
            float zoom01 = settings.Zoom01(currentZoom);
            float adaptiveZoomSmoothing = settings.zoomSmoothing * Mathf.Lerp(0.8f, 1.25f, zoom01);
            float smoothedZoom = Mathf.SmoothDamp(
                currentZoom,
                targetZoomDistance,
                ref zoomVelocity,
                adaptiveZoomSmoothing,
                Mathf.Infinity,
                dt);

            float clampedTargetPitch = settings.ClampPitch(targetPitchDegrees);
            currentPitch = Mathf.SmoothDamp(
                currentPitch,
                clampedTargetPitch,
                ref pitchVelocity,
                settings.pitchSmoothing,
                Mathf.Infinity,
                dt);

            targetManualPitchOffset = settings.ManualOffsetFromFinalPitch(currentPitch, smoothedZoom);
            ApplyRig(smoothedYaw, currentPitch, smoothedZoom);
        }

        private static float CalculateWheelZoomInput(
            float rawScrollDelta,
            float wheelSensitivity,
            float distanceMultiplier,
            float distanceScale)
        {
            // Input System devices vary: some report one unit per wheel notch,
            // while others report the traditional 120-unit detent.
            float notches = Mathf.Abs(rawScrollDelta) > 10f
                ? rawScrollDelta / StandardWheelDeltaPerNotch
                : rawScrollDelta;
            return notches * wheelSensitivity * distanceMultiplier * distanceScale;
        }

        private void ApplyRigImmediate()
        {
            if (settings == null)
            {
                return;
            }

            targetZoomDistance = ClampZoom(targetZoomDistance <= 0f ? settings.defaultZoomDistance : targetZoomDistance);
            targetPitchDegrees = settings.ClampPitch(targetPitchDegrees <= 0f
                ? settings.EvaluatePitch(targetZoomDistance, settings.defaultManualPitchOffset)
                : targetPitchDegrees);
            currentPitch = targetPitchDegrees;
            RefreshManualPitchOffset();
            transform.position = targetFocusPosition;
            ApplyRig(targetYaw, currentPitch, targetZoomDistance);
        }

        private float ResolveCameraObstruction(float yawDegrees, float pitchDegrees, float desiredDistance)
        {
            if (settings == null || !settings.preventCameraObstruction || desiredDistance <= 0f)
                return desiredDistance;

            float startOffset = Mathf.Min(settings.cameraObstructionStartOffset, desiredDistance * 0.5f);
            float castDistance = desiredDistance - startOffset;
            if (castDistance <= 0f)
                return desiredDistance;

            Quaternion armRotation = Quaternion.Euler(0f, yawDegrees, 0f)
                * Quaternion.Euler(pitchDegrees, 0f, 0f);
            Vector3 direction = (transform.rotation * armRotation * Vector3.back).normalized;
            Vector3 origin = transform.position + direction * startOffset;
            if (!Physics.SphereCast(origin, settings.cameraObstructionRadius, direction,
                    out RaycastHit hit, castDistance, settings.cameraObstructionLayers,
                    QueryTriggerInteraction.Ignore))
                return desiredDistance;

            float safeDistance = startOffset + hit.distance - settings.cameraObstructionPadding;
            // The target zoom stays intact; only the rendered camera arm shortens
            // until the wall or furnishing is no longer between it and the focus.
            return Mathf.Clamp(safeDistance, 0.2f, desiredDistance);
        }

        private void ApplyRig(float yawDegrees, float pitchDegrees, float zoomDistance)
        {
            if (yawPivot != null)
            {
                yawPivot.localPosition = Vector3.zero;
                yawPivot.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            }

            if (pitchPivot != null)
            {
                pitchPivot.localPosition = Vector3.zero;
                pitchPivot.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            }

            if (cameraTransform != null)
            {
                float safeDistance = ResolveCameraObstruction(yawDegrees, pitchDegrees, zoomDistance);
                cameraTransform.localPosition = new Vector3(0f, 0f, -Mathf.Max(0.2f, safeDistance));
                cameraTransform.localRotation = Quaternion.identity;
            }
        }

        private void RefreshManualPitchOffset()
        {
            if (settings == null)
            {
                return;
            }

            targetPitchDegrees = settings.ClampPitch(targetPitchDegrees);
            targetManualPitchOffset = settings.ManualOffsetFromFinalPitch(targetPitchDegrees, targetZoomDistance);
        }

        private float ClampZoom(float zoomDistance)
        {
            if (settings == null)
            {
                return Mathf.Max(0.1f, zoomDistance);
            }

            float min = Mathf.Min(settings.minZoomDistance, settings.maxZoomDistance);
            float max = Mathf.Max(settings.minZoomDistance, settings.maxZoomDistance);
            return Mathf.Clamp(zoomDistance, min, max);
        }

        private void OnDrawGizmosSelected()
        {
            if (settings == null || !settings.drawFocusGizmo)
            {
                return;
            }

            Gizmos.color = settings.focusGizmoColor;
            Vector3 focus = Application.isPlaying ? targetFocusPosition : transform.position;
            Gizmos.DrawWireSphere(focus, 1.2f);

            if (cameraTransform != null)
            {
                Gizmos.DrawLine(focus, cameraTransform.position);
            }

            if (boundsSource == null && settings.useFallbackBounds)
            {
                Gizmos.color = settings.boundsGizmoColor;
                Rect rect = new(settings.fallbackBoundsCenter - settings.fallbackBoundsSize * 0.5f, settings.fallbackBoundsSize);
                DrawRect(rect);
            }
        }

        private static void DrawRect(Rect rect)
        {
            Vector3 a = new(rect.xMin, 0f, rect.yMin);
            Vector3 b = new(rect.xMax, 0f, rect.yMin);
            Vector3 c = new(rect.xMax, 0f, rect.yMax);
            Vector3 d = new(rect.xMin, 0f, rect.yMax);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
    }
}
