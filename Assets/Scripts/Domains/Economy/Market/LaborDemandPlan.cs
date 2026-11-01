using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Market
{
    /// <summary>
    /// T2E: one task's labor demand — worker-minutes per day in a season.
    /// Canon master doctrine: "Labor demand is worker-time by task and season,
    /// with peak simultaneous headcount distinct from annualized FTE."
    /// </summary>
    [Serializable]
    public sealed class TaskWorkload
    {
        public string TaskId = string.Empty;
        public int WorkerMinutesPerDay;
        /// <summary>Hands needed at once when this task runs (from the TTS task definition).</summary>
        public int SimultaneousWorkers = 1;
        public string Season = string.Empty; // e.g. "spring", "year-round"

        public TaskWorkload() { }
    }

    /// <summary>
    /// T2E: a business's labor demand as worker-time by task and season.
    /// FTE is derived (total minutes ÷ workday minutes); peak simultaneous
    /// headcount is the largest single crew — true simultaneity across tasks
    /// needs scheduling, which this plan honestly does not claim.
    /// </summary>
    [Serializable]
    public sealed class LaborDemandPlan
    {
        public string BusinessInstanceId = string.Empty;
        public List<TaskWorkload> Workloads = new List<TaskWorkload>();

        /// <summary>Minutes in a standard workday for FTE derivation (calibration).</summary>
        public int WorkdayMinutes = 600;

        public LaborDemandPlan() { }

        public int TotalWorkerMinutesPerDay(string season = null)
        {
            int total = 0;
            foreach (TaskWorkload w in Workloads)
            {
                if (w == null) continue;
                if (season != null && !string.IsNullOrEmpty(w.Season) &&
                    !string.Equals(w.Season, season, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(w.Season, "year-round", StringComparison.OrdinalIgnoreCase))
                    continue;
                total += Math.Max(0, w.WorkerMinutesPerDay);
            }
            return total;
        }

        public float FullTimeEquivalents(string season = null)
        {
            if (WorkdayMinutes <= 0) return 0f;
            return TotalWorkerMinutesPerDay(season) / (float)WorkdayMinutes;
        }

        public int PeakSimultaneousHeadcount(string season = null)
        {
            int peak = 0;
            foreach (TaskWorkload w in Workloads)
            {
                if (w == null) continue;
                if (season != null && !string.IsNullOrEmpty(w.Season) &&
                    !string.Equals(w.Season, season, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(w.Season, "year-round", StringComparison.OrdinalIgnoreCase))
                    continue;
                peak = Math.Max(peak, w.SimultaneousWorkers);
            }
            return peak;
        }
    }
}
