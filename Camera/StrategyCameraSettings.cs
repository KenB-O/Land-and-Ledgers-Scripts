using UnityEngine;
using UnityEngine.InputSystem;

namespace LandLedgers.CameraSystem
{
    public enum CameraRotateMouseButton
    {
        RightMouse = 0,
        MiddleMouse = 1,
        RightOrMiddleMouse = 2
    }

    [CreateAssetMenu(
        fileName = "StrategyCameraSettings",
        menuName = "Land & Ledgers/Camera/Strategy Camera Settings",
        order = 100)]
    public sealed class StrategyCameraSettings : ScriptableObject
    {
        [Header("Starting View")]
        [Tooltip("World-space focus point used when a rig is created or reset.")]
        public Vector3 defaultFocusPosition = new(0f, 0f, 0f);

        [Tooltip("Starting yaw in degrees. The pitch is derived from zoom plus the starting pitch offset.")]
        public float defaultYaw = 45f;

        [Tooltip("Starting zoom distance from the focus point.")]
        [Min(0.1f)]
        public float defaultZoomDistance = 48f;

        [Tooltip("Starting manual pitch offset layered on top of zoom-driven pitch.")]
        public float defaultManualPitchOffset = 0f;

        [Tooltip("Perspective field of view applied to the camera.")]
        [Range(20f, 80f)]
        public float fieldOfView = 42f;

        [Header("Pan")]
        [Tooltip("Pan speed at the closest zoom distance.")]
        [Range(1f, 80f)]
        public float panSpeedClose = 14f;

        [Tooltip("Pan speed at the farthest zoom distance.")]
        [Range(10f, 160f)]
        public float panSpeedFar = 58f;

        [Tooltip("Held multiplier for faster travel across a large map.")]
        [Range(1f, 4f)]
        public float fastPanMultiplier = 2f;

        [Tooltip("Seconds used to smooth focus movement. Lower values feel more direct.")]
        [Range(0.01f, 0.25f)]
        public float movementSmoothing = 0.08f;

        [Header("Mouse Rotation And Tilt")]
        [Tooltip("Mouse yaw sensitivity in degrees per screen pixel while dragging the rotate button.")]
        [Range(0.01f, 0.5f)]
        public float mouseYawSensitivity = 0.16f;

        [Tooltip("Mouse pitch sensitivity in degrees per screen pixel while dragging the rotate button vertically.")]
        [Range(0.01f, 0.5f)]
        public float mousePitchSensitivity = 0.18f;

        [Tooltip("Tiny per-frame vertical mouse movement ignored while rotating, which helps prevent accidental pitch drift during yaw-only drags.")]
        [Range(0f, 3f)]
        public float mousePitchDeadZonePixels = 0.35f;

        [Tooltip("When off, dragging up tilts toward the horizon and dragging down tilts more top-down.")]
        public bool invertMousePitch = false;

        [Tooltip("Mouse button used for drag rotation and tilt.")]
        public CameraRotateMouseButton rotateMouseButton = CameraRotateMouseButton.RightMouse;

        [Tooltip("Locks and hides the cursor while drag rotating so long drags do not hit the screen edge.")]
        public bool lockCursorWhileRotating = true;

        [Header("Keyboard Rotation And Tilt")]
        [Tooltip("Keyboard yaw speed in degrees per second.")]
        [Range(10f, 240f)]
        public float keyboardYawSpeed = 115f;

        [Tooltip("Keyboard pitch speed in degrees per second.")]
        [Range(5f, 160f)]
        public float keyboardPitchSpeed = 42f;

        [Tooltip("Seconds used to smooth yaw rotation. Lower values feel more direct.")]
        [Range(0.01f, 0.25f)]
        public float rotationSmoothing = 0.055f;

        [Header("Zoom")]
        [Tooltip("Closest camera distance from the focus point.")]
        [Min(0.1f)]
        public float minZoomDistance = 12f;

        [Tooltip("Farthest camera distance from the focus point.")]
        [Min(0.1f)]
        public float maxZoomDistance = 125f;

        [Tooltip("Zoom distance units per mouse wheel delta unit. Typical wheel notches report roughly 120 units. Raise this for faster wheel zoom.")]
        [Min(0.1f)]
        public float wheelZoomSensitivity = 0.42f;

        [Tooltip("Wheel zoom multiplier at close zoom, where smaller steps preserve readability around town details.")]
        [Min(0.1f)]
        public float closeZoomWheelMultiplier = 0.75f;

        [Tooltip("Additional wheel zoom multiplier at far zoom so strategic overview does not feel sluggish.")]
        [Min(0.1f)]
        public float farZoomWheelMultiplier = 1.2f;

        [Tooltip("When enabled, wheel zoom preserves the player's current final pitch instead of re-deriving a new one from zoom.")]
        public bool preserveAbsolutePitchWhileZooming = true;

        [Tooltip("Seconds used to smooth camera distance. Lower values feel more direct.")]
        [Range(0.01f, 0.3f)]
        public float zoomSmoothing = 0.075f;

        [Header("Pitch")]
        [Tooltip("Pitch angle at closest zoom. Positive values look downward from above the focus point.")]
        [Range(5f, 85f)]
        public float closePitch = 42f;

        [Tooltip("Pitch angle at farthest zoom. Higher values become more top-down.")]
        [Range(5f, 85f)]
        public float farPitch = 72f;

        [Tooltip("Curve from close zoom (0) to far zoom (1).")]
        public AnimationCurve pitchByZoom = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Seconds used to smooth pitch changes.")]
        [Range(0.01f, 0.3f)]
        public float pitchSmoothing = 0.08f;

        [Tooltip("Minimum final readable pitch after manual pitch offset is applied.")]
        [Range(5f, 85f)]
        public float minReadablePitch = 5f;

        [Tooltip("Maximum final readable pitch after manual pitch offset is applied.")]
        [Range(5f, 89f)]
        public float maxReadablePitch = 80f;

        [Tooltip("When enabled, the manual pitch offset is additionally clamped to the min/max offset values below. Leave this off if you want the full final readable pitch range to always be reachable.")]
        public bool limitManualPitchOffsetRange = false;

        [Tooltip("Lowest manual pitch offset the player can apply relative to the zoom-driven pitch when manual offset range limiting is enabled.")]
        [Range(-85f, 0f)]
        public float minManualPitchOffset = -40f;

        [Tooltip("Highest manual pitch offset the player can apply relative to the zoom-driven pitch when manual offset range limiting is enabled.")]
        [Range(0f, 85f)]
        public float maxManualPitchOffset = 40f;

        [Header("Edge Pan")]
        [Tooltip("Optional mouse-at-screen-edge panning. Disabled by default for editor friendliness.")]
        public bool edgePanEnabled = false;

        [Tooltip("Screen edge thickness in pixels.")]
        [Range(1f, 80f)]
        public float edgePanBorderPixels = 18f;

        [Tooltip("Edge pan multiplier relative to normal keyboard panning.")]
        [Range(0f, 2f)]
        public float edgePanSpeedMultiplier = 0.6f;

        [Tooltip("Suppress edge pan while the mouse rotate button is rotating the camera.")]
        public bool suppressEdgePanWhileRotating = true;

        [Header("Input Keys")]
        [Tooltip("Keyboard key for rotating the camera left.")]
        public Key rotateLeftKey = Key.Q;

        [Tooltip("Keyboard key for rotating the camera right.")]
        public Key rotateRightKey = Key.E;

        [Tooltip("Keyboard key for tilting the camera toward the horizon.")]
        public Key pitchUpKey = Key.R;

        [Tooltip("Keyboard key for tilting the camera more top-down.")]
        public Key pitchDownKey = Key.F;

        [Tooltip("Keyboard key held to use the fast pan multiplier.")]
        public Key fastPanKey = Key.LeftShift;

        [Header("Bounds")]
        [Tooltip("Used if the controller has no StrategyCameraBounds reference.")]
        public bool useFallbackBounds = true;

        public Vector2 fallbackBoundsCenter = Vector2.zero;

        [Min(1f)]
        public Vector2 fallbackBoundsSize = new(320f, 320f);

        [Tooltip("How far the focus may drift past the playable bounds before hard limiting.")]
        [Min(0f)]
        public float fallbackSoftMargin = 36f;

        [Tooltip("How aggressively the target focus is eased back inside the playable bounds.")]
        [Min(0f)]
        public float fallbackReturnStrength = 10f;

        [Header("Terrain Follow")]
        [Tooltip("When enabled, the focus height follows raycast terrain below the camera focus.")]
        public bool terrainFollowEnabled = false;

        [Tooltip("Raycast starts this far above the current focus point.")]
        [Min(1f)]
        public float terrainRaycastHeight = 250f;

        [Tooltip("Maximum raycast distance used for terrain follow.")]
        [Min(1f)]
        public float terrainRaycastDistance = 600f;

        [Tooltip("World-space height offset added above the hit point.")]
        public float terrainHeightOffset = 0f;

        [Tooltip("Seconds used to smooth vertical terrain following.")]
        [Min(0.001f)]
        public float terrainVerticalSmoothing = 0.18f;

        public LayerMask terrainLayers = ~0;

        [Header("Debug")]
        public bool drawFocusGizmo = true;
        public Color focusGizmoColor = new(0.15f, 0.8f, 1f, 0.75f);
        public Color boundsGizmoColor = new(1f, 0.75f, 0.2f, 0.75f);

        public float Zoom01(float zoomDistance)
        {
            float min = Mathf.Min(minZoomDistance, maxZoomDistance);
            float max = Mathf.Max(minZoomDistance, maxZoomDistance);
            return Mathf.InverseLerp(min, max, Mathf.Clamp(zoomDistance, min, max));
        }

        public float EvaluateBasePitch(float zoomDistance)
        {
            float zoom01 = Zoom01(zoomDistance);
            float curveValue = pitchByZoom != null ? pitchByZoom.Evaluate(zoom01) : zoom01;
            return Mathf.Lerp(closePitch, farPitch, Mathf.Clamp01(curveValue));
        }

        public float ClampPitch(float pitchDegrees)
        {
            return Mathf.Clamp(pitchDegrees, minReadablePitch, maxReadablePitch);
        }

        public float EvaluatePitch(float zoomDistance, float manualPitchOffset = 0f)
        {
            return ClampPitch(EvaluateBasePitch(zoomDistance) + manualPitchOffset);
        }

        public float ManualOffsetFromFinalPitch(float finalPitchDegrees, float zoomDistance)
        {
            float clampedPitch = ClampPitch(finalPitchDegrees);
            float rawOffset = clampedPitch - EvaluateBasePitch(zoomDistance);
            return ClampManualPitchOffset(rawOffset, zoomDistance);
        }

        public float EvaluatePanSpeed(float zoomDistance)
        {
            return Mathf.Lerp(panSpeedClose, panSpeedFar, Zoom01(zoomDistance));
        }

        public float EvaluateWheelZoomMultiplier(float zoomDistance)
        {
            return Mathf.Lerp(closeZoomWheelMultiplier, farZoomWheelMultiplier, Zoom01(zoomDistance));
        }

        public float ClampManualPitchOffset(float manualPitchOffset, float zoomDistance)
        {
            float basePitch = EvaluateBasePitch(zoomDistance);
            float minOffset = minReadablePitch - basePitch;
            float maxOffset = maxReadablePitch - basePitch;

            if (limitManualPitchOffsetRange)
            {
                minOffset = Mathf.Max(minOffset, minManualPitchOffset);
                maxOffset = Mathf.Min(maxOffset, maxManualPitchOffset);
            }

            if (minOffset > maxOffset)
            {
                float midpoint = (minOffset + maxOffset) * 0.5f;
                minOffset = midpoint;
                maxOffset = midpoint;
            }

            return Mathf.Clamp(manualPitchOffset, minOffset, maxOffset);
        }

        public void Sanitize()
        {
            panSpeedClose = Mathf.Max(0f, panSpeedClose);
            panSpeedFar = Mathf.Max(0f, panSpeedFar);
            fastPanMultiplier = Mathf.Max(1f, fastPanMultiplier);
            movementSmoothing = Mathf.Max(0.001f, movementSmoothing);
            mouseYawSensitivity = Mathf.Max(0f, mouseYawSensitivity);
            mousePitchSensitivity = Mathf.Max(0f, mousePitchSensitivity);
            mousePitchDeadZonePixels = Mathf.Max(0f, mousePitchDeadZonePixels);
            keyboardYawSpeed = Mathf.Max(0f, keyboardYawSpeed);
            keyboardPitchSpeed = Mathf.Max(0f, keyboardPitchSpeed);
            rotationSmoothing = Mathf.Max(0.001f, rotationSmoothing);
            minZoomDistance = Mathf.Max(0.1f, minZoomDistance);
            maxZoomDistance = Mathf.Max(minZoomDistance + 0.1f, maxZoomDistance);
            defaultZoomDistance = Mathf.Clamp(defaultZoomDistance, minZoomDistance, maxZoomDistance);
            wheelZoomSensitivity = Mathf.Max(0f, wheelZoomSensitivity);
            closeZoomWheelMultiplier = Mathf.Max(0.1f, closeZoomWheelMultiplier);
            farZoomWheelMultiplier = Mathf.Max(0.1f, farZoomWheelMultiplier);
            zoomSmoothing = Mathf.Max(0.001f, zoomSmoothing);
            closePitch = Mathf.Clamp(closePitch, 5f, 85f);
            farPitch = Mathf.Clamp(farPitch, 5f, 85f);
            minReadablePitch = Mathf.Clamp(minReadablePitch, 5f, 85f);
            maxReadablePitch = Mathf.Clamp(maxReadablePitch, minReadablePitch + 0.1f, 89f);
            minManualPitchOffset = Mathf.Clamp(minManualPitchOffset, -85f, 0f);
            maxManualPitchOffset = Mathf.Clamp(maxManualPitchOffset, 0f, 85f);
            if (minManualPitchOffset > maxManualPitchOffset)
            {
                (minManualPitchOffset, maxManualPitchOffset) = (maxManualPitchOffset, minManualPitchOffset);
            }

            defaultManualPitchOffset = ClampManualPitchOffset(defaultManualPitchOffset, defaultZoomDistance);
            fallbackBoundsSize.x = Mathf.Max(1f, fallbackBoundsSize.x);
            fallbackBoundsSize.y = Mathf.Max(1f, fallbackBoundsSize.y);
            edgePanBorderPixels = Mathf.Max(1f, edgePanBorderPixels);
            edgePanSpeedMultiplier = Mathf.Max(0f, edgePanSpeedMultiplier);
            pitchSmoothing = Mathf.Max(0.001f, pitchSmoothing);
            terrainVerticalSmoothing = Mathf.Max(0.001f, terrainVerticalSmoothing);
        }

        private void OnValidate()
        {
            Sanitize();
        }
    }
}
