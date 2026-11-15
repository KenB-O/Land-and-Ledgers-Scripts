using LandLedgers.Time;
using System;
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
        [SerializeField, HideInInspector, Range(0f, 4f)] private float maximumNightExposureStops = 3.5f;

        [Header("Night Lighting By Speed")]
        [Tooltip("Set the nighttime HDRP exposure change for each named speed. 1.5–2 stops is clearly darker; lower values soften the cycle. Edit these rows instead of the simulation clock.")]
        [SerializeField] private SpeedNightExposurePreset[] speedNightExposure = CreateDefaultSpeedProfile();

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

        [Serializable]
        public struct SpeedNightExposurePreset
        {
            public SimulationSpeed speed;
            [Tooltip("HDRP exposure increase at midnight. 0 keeps the daytime exposure; higher values make night darker.")]
            [Range(0f, 4f)] public float nightExposureStops;

            public SpeedNightExposurePreset(SimulationSpeed speed, float nightExposureStops)
            {
                this.speed = speed;
                this.nightExposureStops = nightExposureStops;
            }
        }

        private static readonly SpeedNightExposurePreset[] DefaultSpeedProfile = CreateDefaultSpeedProfile();

        private static SpeedNightExposurePreset[] CreateDefaultSpeedProfile()
        {
            // Values are exposure stops applied at full midnight. Higher EV
            // values make the HDRP image darker. Preset identity comes from
            // SimulationSpeed; no simulation timing or multipliers are changed.
            return new[]
            {
                new SpeedNightExposurePreset(SimulationSpeed.RealTime, 1.20f),
                new SpeedNightExposurePreset(SimulationSpeed.Observe, 0.50f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed1x, 0.50f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed2x, 0.46f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed4x, 0.40f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed5x, 0.38f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed6x, 0.36f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed10x, 0.29f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed25x, 0.17f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed50x, 0.11f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed100x, 0.07f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed250x, 0.04f),
                new SpeedNightExposurePreset(SimulationSpeed.Speed500x, 0.025f),
                new SpeedNightExposurePreset(SimulationSpeed.Paused, 0.50f)
            };
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
        /// Higher simulation rates progressively stabilize lighting for observation.
        /// </summary>
        public static float ResolveSpeedAttenuation(SimulationSpeed speed, float speedMultiplier)
        {
            return ResolveSpeedAttenuation(speed, speedMultiplier, DefaultSpeedProfile);
        }

        /// <summary>
        /// Ratio between the selected preset's night exposure and the 1x
        /// gameplay reference. The multiplier is retained for call-site
        /// compatibility; named presets are intentionally tuned independently.
        /// </summary>
        public static float ResolveSpeedAttenuation(
            SimulationSpeed speed,
            float speedMultiplier,
            SpeedNightExposurePreset[] presetExposureStops)
        {
            SpeedNightExposurePreset[] profile = presetExposureStops ?? DefaultSpeedProfile;
            float normalStops = ResolvePresetExposureStops(SimulationSpeed.Speed1x, profile, 1.6f);
            float selectedStops = ResolvePresetExposureStops(speed, profile, normalStops);
            return normalStops <= 0.0001f ? 0f : selectedStops / normalStops;
        }

        public static float ResolvePresetExposureStops(
            SimulationSpeed speed,
            SpeedNightExposurePreset[] presetExposureStops,
            float fallbackStops)
        {
            if (presetExposureStops != null)
            {
                for (int i = 0; i < presetExposureStops.Length; i++)
                {
                    if (presetExposureStops[i].speed == speed)
                    {
                        return Mathf.Max(0f, presetExposureStops[i].nightExposureStops);
                    }
                }
            }
            return Mathf.Max(0f, fallbackStops);
        }

        private float ResolvePresetExposureStops(SimulationSpeed speed)
        {
            if (speedNightExposure != null)
            {
                for (int i = 0; i < speedNightExposure.Length; i++)
                {
                    if (speedNightExposure[i].speed == speed)
                    {
                        return Mathf.Max(0f, speedNightExposure[i].nightExposureStops);
                    }
                }
            }

            // Keep old scene/prefab instances useful if their new profile is
            // empty or a row was removed in the Inspector.
            return Mathf.Max(0f, maximumNightExposureStops);
        }

        public static float ResolveNightLevel(float timeOfDay01, SimulationSpeed speed, float speedMultiplier)
        {
            return ResolveNightEnvelope(timeOfDay01) * ResolveSpeedAttenuation(speed, speedMultiplier);
        }

        public static float ResolveNightLevel(
            float timeOfDay01,
            SimulationSpeed speed,
            float speedMultiplier,
            SpeedNightExposurePreset[] profile)
        {
            return ResolveNightEnvelope(timeOfDay01)
                * ResolveSpeedAttenuation(speed, speedMultiplier, profile);
        }

        public static float ResolveNightExposureStops(float timeOfDay01, SimulationSpeed speed, float speedMultiplier)
        {
            float stops = ResolvePresetExposureStops(speed, DefaultSpeedProfile, 1.6f);
            return ResolveNightEnvelope(timeOfDay01) * stops;
        }

        public static float ResolveNightExposureStops(
            float timeOfDay01,
            SimulationSpeed speed,
            float speedMultiplier,
            SpeedNightExposurePreset[] profile)
        {
            float stops = ResolvePresetExposureStops(
                speed,
                profile ?? DefaultSpeedProfile,
                ResolvePresetExposureStops(SimulationSpeed.Speed1x, profile ?? DefaultSpeedProfile, 1.6f));
            return ResolveNightEnvelope(timeOfDay01) * stops;
        }

        public static float SmoothNightLevel(float current, float target, float unscaledDeltaSeconds, float transitionDurationSeconds)
        {
            float dt = Mathf.Max(0f, unscaledDeltaSeconds);
            float duration = Mathf.Max(0.1f, transitionDurationSeconds);
            float blend = dt <= 0f ? 0f : 1f - Mathf.Exp(-dt / duration);
            return Mathf.Lerp(current, target, blend);
        }

        private void Awake()
        {
            EnsureSpeedNightProfile();
            CaptureAuthoredLighting();
        }

        private void OnEnable()
        {
            EnsureSpeedNightProfile();
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

        private void EnsureSpeedNightProfile()
        {
            if (speedNightExposure == null || speedNightExposure.Length == 0)
            {
                speedNightExposure = CreateDefaultSpeedProfile();
            }
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
            float targetNightLevel = ResolveNightLevel(
                context.TimeOfDay01,
                context.Speed,
                context.SpeedMultiplier,
                speedNightExposure);
            currentNightLevel = SmoothNightLevel(
                currentNightLevel,
                targetNightLevel,
                global::UnityEngine.Time.unscaledDeltaTime,
                transitionSeconds);

            // Keep direct/ambient light dimming at its authored night maximum.
            // Real Time can use a deeper exposure profile than 1x, but should
            // not drive the sun or ambient contribution below zero.
            float lightingNightLevel = Mathf.Clamp01(currentNightLevel);
            float sunDim = lightingNightLevel * maximumSunDim01;
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
                sun.color = Color.Lerp(authoredSunColor, nightTint, lightingNightLevel * 0.28f);
            }

            RenderSettings.ambientIntensity = authoredAmbientIntensity
                * (1f - lightingNightLevel * maximumAmbientDim01);

            if (exposure != null)
            {
                exposure.fixedExposure.overrideState = true;
                exposure.fixedExposure.value = authoredFixedExposure
                    + ResolvePresetExposureStops(SimulationSpeed.Speed1x) * currentNightLevel;
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
