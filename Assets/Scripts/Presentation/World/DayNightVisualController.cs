using LandLedgers.Time;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LandLedgers.Presentation
{
    /// <summary>
    /// Presentation-only lighting blend driven by the authoritative TimeManager.
    /// Scene-authored presentation component driven by the authoritative TimeManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightVisualController : MonoBehaviour
    {
        private const float DawnStart = 0.22f;
        private const float DawnEnd = 0.30f;
        private const float DuskStart = 0.70f;
        private const float DuskEnd = 0.78f;
        [SerializeField, Min(0.1f)] private float transitionSeconds = 2.5f;
        [SerializeField, Range(0f, 0.8f)] private float maximumSunDim01 = 0.65f;
        [SerializeField, Range(0f, 0.6f)] private float maximumAmbientDim01 = 0.45f;
        [SerializeField, Range(0f, 4f)] private float maximumNightExposureStops = 3.5f;

        private TimeManager timeManager;
        private Light sun;
        private HDAdditionalLightData hdSun;
        private Volume globalVolume;
        private Exposure exposure;
        private float authoredSunIntensity;
        private Color authoredSunColor;
        private float authoredSunDimmer;
        private float authoredAmbientIntensity;
        private float authoredFixedExposure;
        private bool authoredFixedExposureOverride;
        private float currentNightLevel;
        private bool capturedLighting;

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
        /// Higher simulation rates progressively stabilize lighting for observation.
        /// </summary>
        public static float ResolveSpeedAttenuation(SimulationSpeed speed, float speedMultiplier)
        {
            if (speed == SimulationSpeed.RealTime || speed == SimulationSpeed.Paused)
            {
                return 1f;
            }
            switch (speed)
            {
                // Accelerated time keeps the astronomical clock authoritative,
                // while retaining a recognizable night presentation. This is
                // a lighting floor, not a second or randomized lighting clock.
                case SimulationSpeed.Speed1x: return 0.55f;
                case SimulationSpeed.Speed2x: return 0.53f;
                case SimulationSpeed.Speed4x: return 0.51f;
                case SimulationSpeed.Speed5x:
                case SimulationSpeed.Speed6x: return 0.49f;
                case SimulationSpeed.Observe: return 0.55f;
                case SimulationSpeed.Speed10x: return 0.47f;
                case SimulationSpeed.Speed25x: return 0.45f;
                case SimulationSpeed.Speed50x: return 0.43f;
                case SimulationSpeed.Speed100x:
                case SimulationSpeed.Speed250x:
                case SimulationSpeed.Speed500x: return 0.40f;
                default: return 0.40f;
            }
        }

        public static float ResolveNightLevel(float timeOfDay01, SimulationSpeed speed, float speedMultiplier)
        {
            return ResolveNightEnvelope(timeOfDay01) * ResolveSpeedAttenuation(speed, speedMultiplier);
        }

        public static float ResolveNightExposureStops(float timeOfDay01, SimulationSpeed speed, float speedMultiplier)
        {
            // HDRP Fixed Exposure is expressed as EV100: higher values reduce
            // sensor exposure. Subtracting here brightened midnight instead.
            return 3.5f * ResolveNightLevel(timeOfDay01, speed, speedMultiplier);
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

        private void CaptureAuthoredLighting()
        {
            if (capturedLighting)
            {
                // TimeManager awakens before ordinary scene Volumes register. Retry
                // the volume binding without capturing already-blended sun values.
                if (globalVolume == null)
                {
                    CaptureAuthoredVolumeLighting();
                }
                return;
            }

            sun = RenderSettings.sun;
            authoredSunIntensity = sun != null ? sun.intensity : 0f;
            authoredSunColor = sun != null ? sun.color : Color.white;
            hdSun = sun != null ? sun.GetComponent<HDAdditionalLightData>() : null;
            authoredSunDimmer = hdSun != null ? hdSun.lightDimmer : 1f;
            authoredAmbientIntensity = RenderSettings.ambientIntensity;

            CaptureAuthoredVolumeLighting();
            capturedLighting = true;
        }

        private void CaptureAuthoredVolumeLighting()
        {
            globalVolume = FindPrimaryGlobalVolume();
            if (globalVolume != null && globalVolume.sharedProfile != null)
            {
                VolumeProfile runtimeProfile = globalVolume.profile;
                runtimeProfile.TryGet(out exposure);
                if (exposure != null)
                {
                    authoredFixedExposure = exposure.fixedExposure.value;
                    authoredFixedExposureOverride = exposure.fixedExposure.overrideState;
                }
            }
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
            float blend = dt <= 0f ? 0f : 1f - Mathf.Exp(-dt / Mathf.Max(0.1f, transitionSeconds));
            currentNightLevel = Mathf.Lerp(currentNightLevel, targetNightLevel, blend);

            float sunDim = currentNightLevel * maximumSunDim01;
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
                * (1f - currentNightLevel * maximumAmbientDim01);

            if (exposure != null)
            {
                exposure.fixedExposure.overrideState = true;
                exposure.fixedExposure.value = authoredFixedExposure
                    + maximumNightExposureStops * currentNightLevel;
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
            if (exposure != null)
            {
                exposure.fixedExposure.value = authoredFixedExposure;
                exposure.fixedExposure.overrideState = authoredFixedExposureOverride;
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
