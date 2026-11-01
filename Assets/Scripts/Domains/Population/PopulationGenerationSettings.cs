using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Population
{
    [CreateAssetMenu(
        fileName = "PopulationGenerationSettings",
        menuName = "Land & Ledgers/Population/Population Generation Settings",
        order = 210)]
    public sealed class PopulationGenerationSettings : ScriptableObject
    {
        [Header("Generation")]
        [Min(0)]
        public int seedOffset = 511;

        [Range(0.1f, 1f)]
        public float householdOccupancyRate = 0.72f;

        [Min(1)]
        public int minHouseholdSize = 1;

        [Min(1)]
        public int maxHouseholdSize = 6;

        [Header("Starter Town Targets")]
        [Min(1)]
        [Tooltip("Soft floor for occupied households at world start when enough housing exists.")]
        public int minimumStartingHouseholds = 14;

        [Tooltip("Upper bound for filled starter households; extra homes should become settlement opportunity instead of automatic occupancy.")]
        public int maximumStartingHouseholds = 22;

        [Min(1)]
        [Tooltip("Lower bound for total settled residents created before newcomer settlement adds extra arrivals.")]
        public int minimumStartingResidents = 55;

        [Min(1)]
        [Tooltip("Preferred settled-resident target before newcomer settlement is resolved.")]
        public int targetStartingResidents = 78;

        [Min(1)]
        [Tooltip("Upper bound for settled residents created before newcomer settlement is resolved.")]
        public int maximumStartingResidents = 95;

        [Range(0f, 1f)]
        public float adultWorkerChance = 0.82f;

        [Range(0f, 1f)]
        public float juniorWorkerChance = 0.35f;

        [Range(0f, 1f)]
        public float youngWorkerChance = 0.65f;

        [Header("Newcomers / Settlement")]
        [Range(0f, 1f)]
        public float newcomerArrivalRate = 0.28f;

        [Min(0)]
        public int minimumNewcomerArrivalHouseholds = 1;

        [Min(0)]
        public int residentialBoardingBaseCapacity = 1;

        [Min(0)]
        public int mixedUseBoardingBaseCapacity = 2;

        [Min(1)]
        public int boarderStableMoveMinWeeks = 4;

        [Range(0f, 1f)]
        public float departureHousingPressureThreshold = 0.78f;

        [Range(0f, 1f)]
        public float pressuredArrivalDepartureChance = 0.22f;

        [Header("Newcomer Profile Weights")]
        [Min(0)]
        public int loneLaborerArrivalWeight = 18;

        [Min(0)]
        public int kinLinkedArrivalWeight = 14;

        [Min(0)]
        public int boarderSeekingWorkerArrivalWeight = 24;

        [Min(0)]
        public int renterReadyHouseholdArrivalWeight = 14;

        [Min(0)]
        public int skilledCapitalizedArrivalWeight = 10;

        [Min(0)]
        public int distressedRelocationArrivalWeight = 14;

        [Min(0)]
        public int widowElderRelocationArrivalWeight = 6;

        [Header("Professions")]
        public ProfessionDefinition[] professionDefinitions;

        [Header("Names")]
        public string[] maleFirstNames =
        {
            "Thomas", "Samuel", "Edwin", "Caleb", "Silas", "Jonah", "Nathaniel", "Isaac", "Walter", "Henry"
        };

        public string[] femaleFirstNames =
        {
            "Clara", "Martha", "Ada", "Eleanor", "Josephine", "Rose", "Lydia", "Nora", "Abigail", "Mae"
        };

        public string[] surnames =
        {
            "Bennett", "Whitaker", "Hale", "Carver", "Mercer", "Dawson", "Reed", "Bell", "Sutter", "Cole",
            "Fletcher", "Hobbs", "Wainwright", "Marsh", "Beckett", "Quinn"
        };

        public void Sanitize()
        {
            householdOccupancyRate = Mathf.Clamp(householdOccupancyRate, 0.1f, 1f);
            minHouseholdSize = Mathf.Max(1, minHouseholdSize);
            maxHouseholdSize = Mathf.Max(minHouseholdSize, maxHouseholdSize);
            minimumStartingHouseholds = Mathf.Max(1, minimumStartingHouseholds);
            maximumStartingHouseholds = Mathf.Max(minimumStartingHouseholds, maximumStartingHouseholds);
            minimumStartingResidents = Mathf.Max(minimumStartingHouseholds * minHouseholdSize, minimumStartingResidents);
            targetStartingResidents = Mathf.Max(minimumStartingResidents, targetStartingResidents);
            maximumStartingResidents = Mathf.Max(targetStartingResidents, maximumStartingResidents);
            adultWorkerChance = Mathf.Clamp01(adultWorkerChance);
            juniorWorkerChance = Mathf.Clamp01(juniorWorkerChance);
            youngWorkerChance = Mathf.Clamp01(youngWorkerChance);
            newcomerArrivalRate = Mathf.Clamp01(newcomerArrivalRate);
            minimumNewcomerArrivalHouseholds = Mathf.Max(0, minimumNewcomerArrivalHouseholds);
            residentialBoardingBaseCapacity = Mathf.Max(0, residentialBoardingBaseCapacity);
            mixedUseBoardingBaseCapacity = Mathf.Max(0, mixedUseBoardingBaseCapacity);
            boarderStableMoveMinWeeks = Mathf.Max(1, boarderStableMoveMinWeeks);
            departureHousingPressureThreshold = Mathf.Clamp01(departureHousingPressureThreshold);
            pressuredArrivalDepartureChance = Mathf.Clamp01(pressuredArrivalDepartureChance);
            loneLaborerArrivalWeight = Mathf.Max(0, loneLaborerArrivalWeight);
            kinLinkedArrivalWeight = Mathf.Max(0, kinLinkedArrivalWeight);
            boarderSeekingWorkerArrivalWeight = Mathf.Max(0, boarderSeekingWorkerArrivalWeight);
            renterReadyHouseholdArrivalWeight = Mathf.Max(0, renterReadyHouseholdArrivalWeight);
            skilledCapitalizedArrivalWeight = Mathf.Max(0, skilledCapitalizedArrivalWeight);
            distressedRelocationArrivalWeight = Mathf.Max(0, distressedRelocationArrivalWeight);
            widowElderRelocationArrivalWeight = Mathf.Max(0, widowElderRelocationArrivalWeight);
        }

        public IReadOnlyList<ProfessionDefinition> GetProfessionDefinitions()
        {
            if (professionDefinitions != null && professionDefinitions.Length > 0)
            {
                return professionDefinitions;
            }

            return DefaultProfessions;
        }

        private static readonly ProfessionDefinition[] DefaultProfessions =
        {
            ProfessionDefinition.CreateRuntime(
                "general_store_clerk",
                "General Store Clerk",
                16,
                LaborAccessLevel.YoungWorker,
                14,
                20,
                2,
                new[] { PlotZone.Business, PlotZone.MixedUse },
                new[] { "single_story_business_natural_wood" }),

            ProfessionDefinition.CreateRuntime(
                "stock_hand",
                "Stock Hand",
                13,
                LaborAccessLevel.JuniorLowTrust,
                7,
                11,
                3,
                new[] { PlotZone.Business, PlotZone.MixedUse },
                new[] { "single_story_business_natural_wood", "single_story_business_green_wood" }),

            ProfessionDefinition.CreateRuntime(
                "porter",
                "Porter",
                16,
                LaborAccessLevel.YoungWorker,
                10,
                15,
                2,
                new[] { PlotZone.Business, PlotZone.MixedUse },
                new[] { "single_story_business_green_wood", "double_story_saloon_corner_natural_wood" }),

            ProfessionDefinition.CreateRuntime(
                "saloon_hand",
                "Saloon Hand",
                18,
                LaborAccessLevel.FullLaborMarket,
                12,
                18,
                3,
                new[] { PlotZone.Business, PlotZone.MixedUse },
                new[] { "double_story_saloon_corner_natural_wood" }),

            ProfessionDefinition.CreateRuntime(
                "carpenter",
                "Carpenter",
                18,
                LaborAccessLevel.FullLaborMarket,
                20,
                30,
                1,
                new[] { PlotZone.Business, PlotZone.MixedUse },
                new[] { "single_story_business_green_wood" }),

            ProfessionDefinition.CreateRuntime(
                "teamster",
                "Teamster",
                18,
                LaborAccessLevel.FullLaborMarket,
                18,
                26,
                1,
                new[] { PlotZone.Business, PlotZone.MixedUse },
                new[] { "single_story_business_green_wood", "single_story_business_natural_wood" }),

            ProfessionDefinition.CreateRuntime(
                "shopkeeper",
                "Shopkeeper",
                18,
                LaborAccessLevel.FullLaborMarket,
                26,
                38,
                1,
                new[] { PlotZone.Business, PlotZone.MixedUse },
                new[] { "single_story_business_natural_wood", "single_story_business_green_wood" })
        };
    }
}
