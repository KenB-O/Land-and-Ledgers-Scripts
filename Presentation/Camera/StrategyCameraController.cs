using UnityEngine;
using UnityEngine.InputSystem;

namespace LandLedgers.CameraSystem
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class StrategyCameraController : MonoBehaviour
    {
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

        public void SetBoundsSource(StrategyCameraBounds newBoundsSource)
        {
            boundsSource = newBoundsSource;
        }

        public void SetFocus(Vector3 worldPosition)
        {
            targetFocusPosition = worldPosition;
        }

        public void JumpTo(Vector3 worldFocusPosition, float yawDegrees, float zoomDistance)
        {
            targetFocusPosition = worldFocusPosition;
            targetYaw = yawDegrees;
            targetZoomDistance = ClampZoom(zoomDistance);
            targetPitchDegrees = settings != null ? settings.ClampPitch(targetPitchDegrees) : targetPitchDegrees;
            RefreshManualPitchOffset();
            ApplyRigImmediate();
        }

        public void JumpTo(Vector3 worldFocusPosition, float yawDegrees, float zoomDistance, float pitchDegrees)
        {
            targetFocusPosition = worldFocusPosition;
            targetYaw = yawDegrees;
            targetZoomDistance = ClampZoom(zoomDistance);
            targetPitchDegrees = settings != null ? settings.ClampPitch(pitchDegrees) : pitchDegrees;
            RefreshManualPitchOffset();
            ApplyRigImmediate();
        }

        public void SetManualPitchOffset(float manualPitchOffset)
        {
            if (settings == null)
            {
                targetManualPitchOffset = manualPitchOffset;
                return;
            }

            targetManualPitchOffset = settings.ClampManualPitchOffset(manualPitchOffset, targetZoomDistance);
            targetPitchDegrees = settings.EvaluatePitch(targetZoomDistance, targetManualPitchOffset);
        }

        public void SetPitchDegrees(float pitchDegrees)
        {
            if (settings == null)
            {
                targetPitchDegrees = pitchDegrees;
                return;
            }

            targetPitchDegrees = settings.ClampPitch(pitchDegrees);
            RefreshManualPitchOffset();
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
            ApplyBounds(dt);
            ApplyTerrainTargetHeight(dt);
            SmoothAndApplyRig(dt);
        }

        private void InitializeFromSettings(bool snapTransform)
        {
            if (settings == null)
            {
                return;
            }

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
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            Vector2 panInput = keyboard != null ? ReadKeyboardPan(keyboard, SuppressArrowKeyPan) : Vector2.zero;

            bool isRotatingWithMouse = mouse != null && IsRotateMouseButtonPressed(mouse);
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
                float speed = settings.EvaluatePanSpeed(targetZoomDistance);
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
                    float zoomInput = scrollY * settings.wheelZoomSensitivity * settings.EvaluateWheelZoomMultiplier(targetZoomDistance);
                    float newZoom = ClampZoom(targetZoomDistance - zoomInput);

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
            float input = 0f;

            if (IsKeyPressed(keyboard, settings.pitchUpKey))
            {
                input -= 1f;
            }

            if (IsKeyPressed(keyboard, settings.pitchDownKey))
            {
                input += 1f;
            }

            return input;
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
            float smoothedZoom = Mathf.SmoothDamp(
                currentZoom,
                targetZoomDistance,
                ref zoomVelocity,
                settings.zoomSmoothing,
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

        private void ApplyRig(float yawDegrees, float pitchDegrees, float zoomDistance)
        {
            if (controlledCamera != null)
            {
                controlledCamera.fieldOfView = settings.fieldOfView;
            }

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
                cameraTransform.localPosition = new Vector3(0f, 0f, -ClampZoom(zoomDistance));
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
