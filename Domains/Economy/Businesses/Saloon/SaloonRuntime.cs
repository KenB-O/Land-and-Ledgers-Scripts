using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Saloon
{
    /// <summary>
    /// T2G: one observed workload sample. Staffing scales from OBSERVED
    /// workload (GHOST-DES-050) — never a fixed employee recipe.
    /// </summary>
    [Serializable]
    public sealed class SaloonWorkloadSample
    {
        public int DayIndex;
        public int PatronsServed;
        public int DrinksServed;
        public int MealsServed;
        /// <summary>Average patrons lingering at once (the social hub — lingering is the product).</summary>
        public float AvgLingeringPatrons;

        public SaloonWorkloadSample() { }
    }

    /// <summary>
    /// T2G: the saloon runtime. The house earns ORDINARY food/drink
    /// transactions with provenance. Staffing is derived from observed
    /// workload; the runtime reports its staffing shortfall so hiring runs
    /// through the T2B recruitment workflow — never a spawned roster.
    /// </summary>
    public sealed class SaloonRuntime
    {
        /// <summary>Patrons one bartender can serve per day (calibration).</summary>
        public const int PatronsPerBartenderPerDay = 40;
        /// <summary>Lingering patrons one server can cover (calibration).</summary>
        public const float LingeringPatronsPerServer = 12f;

        private readonly string businessInstanceId;
        private readonly List<SaloonWorkloadSample> workloadHistory = new List<SaloonWorkloadSample>();
        private int currentBartenders;
        private int currentServers;
        private int drinksServedToday;
        private int mealsServedToday;
        private int patronsToday;
        private float lingeringSum;
        private int lingeringSamples;

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public SaloonRuntime(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        public void SetCurrentStaff(int bartenders, int servers)
        {
            currentBartenders = Math.Max(0, bartenders);
            currentServers = Math.Max(0, servers);
        }

        /// <summary>T2G: serves a drink — an ordinary transaction for the house.</summary>
        public void ServeDrink(int dayIndex, List<string> diag)
        {
            drinksServedToday++;
            patronsToday++;
            (diag ?? diagnostics).Add($"Saloon {businessInstanceId}: drink served (day {dayIndex}) — ordinary house revenue.");
        }

        /// <summary>T2G: serves a meal — an ordinary transaction for the house.</summary>
        public void ServeMeal(int dayIndex, List<string> diag)
        {
            mealsServedToday++;
            (diag ?? diagnostics).Add($"Saloon {businessInstanceId}: meal served (day {dayIndex}) — ordinary house revenue.");
        }

        /// <summary>
        /// T2G: patrons linger — the social hub. Lingering is observed, not
        /// rushed; it feeds the server-staffing calculation.
        /// </summary>
        public void ObserveLingering(float patronsPresentNow)
        {
            lingeringSum += Math.Max(0f, patronsPresentNow);
            lingeringSamples++;
        }

        public void CloseDay(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            workloadHistory.Add(new SaloonWorkloadSample
            {
                DayIndex = dayIndex,
                PatronsServed = patronsToday,
                DrinksServed = drinksServedToday,
                MealsServed = mealsServedToday,
                AvgLingeringPatrons = lingeringSamples > 0 ? lingeringSum / lingeringSamples : 0f,
            });
            diag.Add($"Saloon {businessInstanceId}: day {dayIndex} — {patronsToday} patrons, {drinksServedToday} drinks, {mealsServedToday} meals.");
            drinksServedToday = 0;
            mealsServedToday = 0;
            patronsToday = 0;
            lingeringSum = 0f;
            lingeringSamples = 0;
        }

        /// <summary>
        /// T2G: recommended staff from OBSERVED workload (GHOST-DES-050).
        /// Uses trailing average so one busy night doesn't hire a crowd.
        /// </summary>
        public (int bartenders, int servers) RecommendedStaff(int trailingDays = 7)
        {
            if (workloadHistory.Count == 0) return (1, 0); // a saloon opens with a bartender
            int days = Math.Min(trailingDays, workloadHistory.Count);
            float patronSum = 0f, lingerSum = 0f;
            for (int i = workloadHistory.Count - days; i < workloadHistory.Count; i++)
            {
                patronSum += workloadHistory[i].PatronsServed;
                lingerSum += workloadHistory[i].AvgLingeringPatrons;
            }
            float avgPatrons = patronSum / days;
            float avgLinger = lingerSum / days;
            int bartenders = Math.Max(1, (int)Math.Ceiling(avgPatrons / PatronsPerBartenderPerDay));
            int servers = (int)Math.Ceiling(avgLinger / LingeringPatronsPerServer);
            return (bartenders, servers);
        }

        /// <summary>
        /// T2G: staffing shortfall — roles to fill through the T2B recruitment
        /// workflow. Positive numbers only; overstaffing is the owner's call.
        /// </summary>
        public (int bartendersNeeded, int serversNeeded) StaffingShortfall()
        {
            var (bartenders, servers) = RecommendedStaff();
            return (Math.Max(0, bartenders - currentBartenders), Math.Max(0, servers - currentServers));
        }

        #region Save / Load
        [Serializable]
        public sealed class SaloonRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public List<SaloonWorkloadSample> WorkloadHistory = new List<SaloonWorkloadSample>();
            public int CurrentBartenders;
            public int CurrentServers;
        }

        public SaloonRuntimeSaveDto CaptureSaveDto()
        {
            return new SaloonRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                WorkloadHistory = new List<SaloonWorkloadSample>(workloadHistory),
                CurrentBartenders = currentBartenders,
                CurrentServers = currentServers,
            };
        }

        public void LoadFromSaveDto(SaloonRuntimeSaveDto dto)
        {
            workloadHistory.Clear();
            if (dto == null) return;
            workloadHistory.AddRange(dto.WorkloadHistory);
            currentBartenders = dto.CurrentBartenders;
            currentServers = dto.CurrentServers;
        }
        #endregion
    }
}
