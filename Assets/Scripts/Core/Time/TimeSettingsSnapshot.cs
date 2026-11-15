using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Time
{
    /// <summary>
    /// Campaign C (DEF-045): the runtime-affecting time settings a save was
    /// made under, recorded so a Continue resumes with the saved settings
    /// instead of silently reinterpreting clock state under whatever the
    /// scene asset currently holds. This is the exact field list Check 09
    /// finding F-01 named: day length, short-tick interval, every speed
    /// multiplier, and both per-frame caps.
    ///
    /// Deliberately NOT recorded: the calendar anchor and scenario start
    /// date (the saved calendarDayOffset already carries the timeline; the
    /// anchor is scenario identity, not a session setting), startingSpeed
    /// (a fresh-start default; the saved currentSpeed restores separately),
    /// and the cosmetic name arrays (day/month/season labels).
    ///
    /// Additive save contract: old saves omit this snapshot and read
    /// hasSettings false, in which case ApplyTo changes nothing and the
    /// load keeps the current settings.
    /// </summary>
    [Serializable]
    public sealed class TimeSettingsSnapshot
    {
        public bool hasSettings;
        public float realSecondsPerGameDayAt1x;
        public float shortTickIntervalGameMinutes;
        public float speed1xMultiplier;
        public float speed2xMultiplier;
        public float speed4xMultiplier;
        public float speed5xMultiplier;
        public float speed6xMultiplier;
        public float speed10xMultiplier;
        public float speed25xMultiplier;
        public float speed50xMultiplier;
        public float speed100xMultiplier;
        public float speed250xMultiplier;
        public float speed500xMultiplier;
        public int maxShortTicksPerFrame;
        public int maxDayRolloversPerFrame;

        public static TimeSettingsSnapshot Capture(SimulationTimeSettings settings)
        {
            TimeSettingsSnapshot snapshot = new TimeSettingsSnapshot();
            if (settings == null)
            {
                return snapshot;
            }

            snapshot.hasSettings = true;
            snapshot.realSecondsPerGameDayAt1x = settings.realSecondsPerGameDayAt1x;
            snapshot.shortTickIntervalGameMinutes = settings.shortTickIntervalGameMinutes;
            snapshot.speed1xMultiplier = settings.speed1xMultiplier;
            snapshot.speed2xMultiplier = settings.speed2xMultiplier;
            snapshot.speed4xMultiplier = settings.speed4xMultiplier;
            snapshot.speed5xMultiplier = settings.speed5xMultiplier;
            snapshot.speed6xMultiplier = settings.speed6xMultiplier;
            snapshot.speed10xMultiplier = settings.speed10xMultiplier;
            snapshot.speed25xMultiplier = settings.speed25xMultiplier;
            snapshot.speed50xMultiplier = settings.speed50xMultiplier;
            snapshot.speed100xMultiplier = settings.speed100xMultiplier;
            snapshot.speed250xMultiplier = settings.speed250xMultiplier;
            snapshot.speed500xMultiplier = settings.speed500xMultiplier;
            snapshot.maxShortTicksPerFrame = settings.maxShortTicksPerFrame;
            snapshot.maxDayRolloversPerFrame = settings.maxDayRolloversPerFrame;
            return snapshot;
        }

        /// <summary>
        /// Applies the recorded settings to the live settings instance and
        /// sanitizes it. Returns the names of the fields that differed from
        /// the live values (empty when identical). A snapshot with
        /// hasSettings false (old save) or a null target changes nothing.
        /// </summary>
        public List<string> ApplyTo(SimulationTimeSettings settings)
        {
            List<string> differing = new List<string>();
            if (settings == null || !hasSettings)
            {
                return differing;
            }

            if (!Mathf.Approximately(settings.realSecondsPerGameDayAt1x, realSecondsPerGameDayAt1x))
            {
                differing.Add(nameof(realSecondsPerGameDayAt1x));
            }

            if (!Mathf.Approximately(settings.shortTickIntervalGameMinutes, shortTickIntervalGameMinutes))
            {
                differing.Add(nameof(shortTickIntervalGameMinutes));
            }

            if (!Mathf.Approximately(settings.speed1xMultiplier, speed1xMultiplier))
            {
                differing.Add(nameof(speed1xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed2xMultiplier, speed2xMultiplier))
            {
                differing.Add(nameof(speed2xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed4xMultiplier, speed4xMultiplier))
            {
                differing.Add(nameof(speed4xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed5xMultiplier, speed5xMultiplier))
            {
                differing.Add(nameof(speed5xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed6xMultiplier, speed6xMultiplier))
            {
                differing.Add(nameof(speed6xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed10xMultiplier, speed10xMultiplier))
            {
                differing.Add(nameof(speed10xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed25xMultiplier, speed25xMultiplier))
            {
                differing.Add(nameof(speed25xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed50xMultiplier, speed50xMultiplier))
            {
                differing.Add(nameof(speed50xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed100xMultiplier, speed100xMultiplier))
            {
                differing.Add(nameof(speed100xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed250xMultiplier, speed250xMultiplier))
            {
                differing.Add(nameof(speed250xMultiplier));
            }

            if (!Mathf.Approximately(settings.speed500xMultiplier, speed500xMultiplier))
            {
                differing.Add(nameof(speed500xMultiplier));
            }

            if (settings.maxShortTicksPerFrame != maxShortTicksPerFrame)
            {
                differing.Add(nameof(maxShortTicksPerFrame));
            }

            if (settings.maxDayRolloversPerFrame != maxDayRolloversPerFrame)
            {
                differing.Add(nameof(maxDayRolloversPerFrame));
            }

            settings.realSecondsPerGameDayAt1x = realSecondsPerGameDayAt1x;
            settings.shortTickIntervalGameMinutes = shortTickIntervalGameMinutes;
            settings.speed1xMultiplier = speed1xMultiplier;
            settings.speed2xMultiplier = speed2xMultiplier;
            settings.speed4xMultiplier = speed4xMultiplier;
            settings.speed5xMultiplier = speed5xMultiplier;
            settings.speed6xMultiplier = speed6xMultiplier;
            settings.speed10xMultiplier = speed10xMultiplier;
            settings.speed25xMultiplier = speed25xMultiplier;
            settings.speed50xMultiplier = speed50xMultiplier;
            settings.speed100xMultiplier = speed100xMultiplier;
            settings.speed250xMultiplier = speed250xMultiplier;
            settings.speed500xMultiplier = speed500xMultiplier;
            settings.maxShortTicksPerFrame = maxShortTicksPerFrame;
            settings.maxDayRolloversPerFrame = maxDayRolloversPerFrame;
            settings.Sanitize();
            return differing;
        }
    }
}
