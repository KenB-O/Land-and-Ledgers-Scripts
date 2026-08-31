using System;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Population
{
    [Serializable]
    public sealed class ProfessionDefinition
    {
        [SerializeField]
        private string professionId = "profession";

        [SerializeField]
        private string displayName = "Profession";

        [SerializeField, Min(0)]
        private int minimumAge = 18;

        [SerializeField]
        private LaborAccessLevel minimumLaborAccess = LaborAccessLevel.FullLaborMarket;

        [SerializeField, Min(0)]
        private int minWeeklyWage = 12;

        [SerializeField, Min(0)]
        private int maxWeeklyWage = 18;

        [SerializeField, Min(1)]
        private int maxWorkersPerWorkplace = 2;

        [SerializeField]
        private PlotZone[] compatiblePlotZones = { PlotZone.Business };

        [SerializeField]
        private string[] compatibleBuildingIds = Array.Empty<string>();

        [SerializeField]
        private bool assignableAtGeneration = true;

        public string ProfessionId => string.IsNullOrWhiteSpace(professionId) ? displayName : professionId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? ProfessionId : displayName;
        public int MinimumAge => Mathf.Max(0, minimumAge);
        public LaborAccessLevel MinimumLaborAccess => minimumLaborAccess;
        public int MinWeeklyWage => Mathf.Max(0, minWeeklyWage);
        public int MaxWeeklyWage => Mathf.Max(MinWeeklyWage, maxWeeklyWage);
        public int MaxWorkersPerWorkplace => Mathf.Max(1, maxWorkersPerWorkplace);
        public bool AssignableAtGeneration => assignableAtGeneration;

        public bool IsCompatibleWith(string buildingId, PlotZone plotZone)
        {
            if (compatibleBuildingIds != null && compatibleBuildingIds.Length > 0)
            {
                for (int i = 0; i < compatibleBuildingIds.Length; i++)
                {
                    if (string.Equals(compatibleBuildingIds[i], buildingId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (compatiblePlotZones == null || compatiblePlotZones.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < compatiblePlotZones.Length; i++)
            {
                if (compatiblePlotZones[i] == plotZone || compatiblePlotZones[i] == PlotZone.MixedUse || plotZone == PlotZone.MixedUse)
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanAssignTo(PersonState person)
        {
            return assignableAtGeneration
                && person != null
                && person.age >= MinimumAge
                && person.laborAccessLevel >= minimumLaborAccess;
        }

        public WageSnapshot RollWage(System.Random random)
        {
            int wage = random.Next(MinWeeklyWage, MaxWeeklyWage + 1);
            return new WageSnapshot
            {
                weeklyWage = wage,
                stabilityPercent = 70 + random.Next(0, 21),
                currencyId = "dollars"
            };
        }

        public static ProfessionDefinition CreateRuntime(
            string id,
            string label,
            int minimumAge,
            LaborAccessLevel minimumAccess,
            int minWage,
            int maxWage,
            int maxWorkers,
            PlotZone[] zones,
            string[] buildingIds)
        {
            return new ProfessionDefinition
            {
                professionId = id,
                displayName = label,
                minimumAge = Mathf.Max(0, minimumAge),
                minimumLaborAccess = minimumAccess,
                minWeeklyWage = Mathf.Max(0, minWage),
                maxWeeklyWage = Mathf.Max(minWage, maxWage),
                maxWorkersPerWorkplace = Mathf.Max(1, maxWorkers),
                compatiblePlotZones = zones ?? Array.Empty<PlotZone>(),
                compatibleBuildingIds = buildingIds ?? Array.Empty<string>(),
                assignableAtGeneration = true
            };
        }
    }
}
