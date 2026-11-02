using LandLedgers.Time;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LandLedgers.Presentation
{
    /// <summary>
    /// Presentation-only lighting blend driven by the authoritative TimeManager.
    /// This component is attached to the existing TimeManager GameObject at runtime;
    /// it does not create or persist a separate scene authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightVisualController : MonoBehaviour
    {
        private const float DawnStart = 0.22f;
        private const float DawnEnd = 0.30f;
        private const float DuskStart = 0.70f;
        private const float DuskEnd = 0.78f;
        private const float TransitionSeconds = 0.8f;
        private const float MaximumSunDim01 = 0.78f;
        private const float MaximumAmbientDim01 = 0.55f;
        private const float MaximumNightExposureStops = 4f;

        private TimeManager timeManager;
        private Light sun;
        private HDAdditionalLightData hdSun;
        private Volume globalVolume;
        private ColorAdjustments colorAdjustments;
        private float authoredSunIntensity;
        private Color authoredSunColor;
        private float authoredSunDimmer;
        private float authoredAmbientIntensity;
        private float authoredPostExposure;
        private bool authoredPostExposureOverride;
        private bool authoredColorAdjustmentsActive;
        private bool createdColorAdjustments;
        private float currentNightLevel;
        private bool capturedLighting;

        public static void EnsureAttached(TimeManager manager)
        {
            if (!Application.isPlaying || manager == null)
            {
                return;
            }

            DayNightVisualController controller = manager.GetComponent<DayNightVisualController>();
            if (controller == null)
            {
                controller = manager.gameObject.AddComponent<DayNightVisualController>();
            }

            controller.Bind(manager);
        }

        /// <summary>Returns the night envelope for a normalized authoritative time of day.</summary>
        public static float ResolveNightEnvelope(float timeOfDay01)
        {
            float time = Mathf.Repeat(timeOfDay01, 1f);
            float daylight;

            if (time < DawnStart || time >= DuskEnd)
            {
                daylight = 0f;
            }
            else if (time < DawnEnd)
            {
                daylight = SmoothStep01((time - DawnStart) / (DawnEnd - DawnStart));
            }
            else if (time < DuskStart)
            {
                daylight = 1f;
            }
            else
            {
                daylight = 1f - SmoothStep01((time - DuskStart) / (DuskEnd - DuskStart));
            }

            return 1f - daylight;
        }

        /// <summary>
        /// Real Time and pause retain the full visual night. Accelerated speeds
        /// monotonically soften it so rapid simulation does not cause harsh flashes.
        /// </summary>
        public static float ResolveSpeedAttenuation(SimulationSpeed speed, float speedMultiplier)
        {
            if (speed == SimulationSpeed.RealTime || speed == SimulationSpeed.Paused)
            {
                return 1f;
            }

            float normalizedSpeed = Mathf.Max(1f, speedMultiplier);
            return 1f / (1f + Mathf.Log(normalizedSpeed, 2f));
        }

        public static float ResolveNightLevel(float timeOfDay01, SimulationSpeed speed, float speedMultiplier)
        {
            return ResolveNightEnvelope(timeOfDay01) * ResolveSpeedAttenuation(speed, speedMultiplier);
        }

        public static float ResolveNightExposureStops(float timeOfDay01, SimulationSpeed speed, float speedMultiplier)
        {
            return -MaximumNightExposureStops * ResolveNightLevel(timeOfDay01, speed, speedMultiplier);
        }

        private void Awake()
        {
            CaptureAuthoredLighting();
        }

        private void OnEnable()
        {
            if (timeManager == null)
            {
                timeManager = GetComponent<TimeManager>();
            }

            if (timeManager != null)
            {
                timeManager.VisualFrameUpdated -= OnVisualFrameUpdated;
                timeManager.VisualFrameUpdated += OnVisualFrameUpdated;
            }

            CaptureAuthoredLighting();
        }

        private void OnDisable()
        {
            if (timeManager != null)
            {
                timeManager.VisualFrameUpdated -= OnVisualFrameUpdated;
            }

            RestoreAuthoredLighting();
        }

        private void OnDestroy()
        {
            RestoreAuthoredLighting();
        }

        private void Bind(TimeManager manager)
        {
            if (timeManager == manager)
            {
                return;
            }

            if (timeManager != null)
            {
                timeManager.VisualFrameUpdated -= OnVisualFrameUpdated;
            }

            timeManager = manager;
            CaptureAuthoredLighting();
            if (isActiveAndEnabled)
            {
                timeManager.VisualFrameUpdated -= OnVisualFrameUpdated;
                timeManager.VisualFrameUpdated += OnVisualFrameUpdated;
            }
        }

        private void CaptureAuthoredLighting()
        {
            if (capturedLighting)
            {
                return;
            }

            sun = RenderSettings.sun;
            authoredSunIntensity = sun != null ? sun.intensity : 0f;
            authoredSunColor = sun != null ? sun.color : Color.white;
            hdSun = sun != null ? sun.GetComponent<HDAdditionalLightData>() : null;
            authoredSunDimmer = hdSun != null ? hdSun.lightDimmer : 1f;
            authoredAmbientIntensity = RenderSettings.ambientIntensity;

            globalVolume = FindPrimaryGlobalVolume();
            if (globalVolume != null && globalVolume.sharedProfile != null)
            {
                VolumeProfile runtimeProfile = globalVolume.profile;
                if (!runtimeProfile.TryGet(out colorAdjustments))
                {
                    colorAdjustments = runtimeProfile.Add<ColorAdjustments>(false);
                    createdColorAdjustments = true;
                }

                authoredColorAdjustmentsActive = colorAdjustments.active;
                authoredPostExposure = colorAdjustments.postExposure.value;
                authoredPostExposureOverride = colorAdjustments.postExposure.overrideState;
            }

            capturedLighting = true;
        }

        private static Volume FindPrimaryGlobalVolume()
        {
            Volume[] volumes = FindObjectsByType<Volume>(FindObjectsInactive.Exclude);
            Volume selected = null;
            for (int i = 0; i < volumes.Length; i++)
            {
                Volume candidate = volumes[i];
                if (candidate == null || !candidate.isActiveAndEnabled || !candidate.isGlobal || candidate.weight <= 0f)
                {
                    continue;
                }

                if (selected == null || candidate.priority > selected.priority)
                {
                    selected = candidate;
                }
            }

            return selected;
        }

        private void OnVisualFrameUpdated(SimulationFrameContext context)
        {
            CaptureAuthoredLighting();
            float targetNightLevel = ResolveNightLevel(context.TimeOfDay01, context.Speed, context.SpeedMultiplier);
            float dt = Mathf.Max(0f, global::UnityEngine.Time.unscaledDeltaTime);
            float blend = dt <= 0f ? 0f : 1f - Mathf.Exp(-dt / TransitionSeconds);
            currentNightLevel = Mathf.Lerp(currentNightLevel, targetNightLevel, blend);

            float sunDim = currentNightLevel * MaximumSunDim01;
            if (sun != null)
            {
                if (hdSun != null)
                {
                    hdSun.lightDimmer = authoredSunDimmer * (1f - sunDim);
                }
                else
                {
                    sun.intensity = authoredSunIntensity * (1f - sunDim);
                }

                Color nightTint = new Color(0.66f, 0.74f, 0.90f, authoredSunColor.a);
                sun.color = Color.Lerp(authoredSunColor, nightTint, currentNightLevel * 0.28f);
            }

            RenderSettings.ambientIntensity = authoredAmbientIntensity
                * (1f - currentNightLevel * MaximumAmbientDim01);

            if (colorAdjustments != null)
            {
                colorAdjustments.active = true;
                colorAdjustments.postExposure.overrideState = true;
                // Use the same smoothed envelope as the sun and ambient light.
                // Applying the raw target here made exposure jump instantly
                // when the player changed simulation speed at night.
                colorAdjustments.postExposure.value = authoredPostExposure
                    - MaximumNightExposureStops * currentNightLevel;
            }
        }

        private void RestoreAuthoredLighting()
        {
            if (!capturedLighting)
            {
                return;
            }

            if (sun != null)
            {
                if (hdSun != null)
                {
                    hdSun.lightDimmer = authoredSunDimmer;
                }
                else
                {
                    sun.intensity = authoredSunIntensity;
                }

                sun.color = authoredSunColor;
            }

            RenderSettings.ambientIntensity = authoredAmbientIntensity;
            if (colorAdjustments != null)
            {
                colorAdjustments.postExposure.value = authoredPostExposure;
                colorAdjustments.postExposure.overrideState = createdColorAdjustments
                    ? false
                    : authoredPostExposureOverride;
                colorAdjustments.active = createdColorAdjustments
                    ? false
                    : authoredColorAdjustmentsActive;
            }

            currentNightLevel = 0f;
        }

        private static float SmoothStep01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}
