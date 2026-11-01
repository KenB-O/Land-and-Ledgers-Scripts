using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LandLedgers.Economy
{
    public sealed partial class SharedBusinessRuntimeManager
    {
        private static readonly BusinessType[] GoodsTransformerRuntimeTypes =
        {
            BusinessType.CropFarm,
            BusinessType.Ranch,
            BusinessType.Butcher,
            BusinessType.Blacksmith,
            BusinessType.FuelDealer,
            BusinessType.GrainMill,
            BusinessType.Bakery,
            BusinessType.Tailor,
            BusinessType.Wheelwright
        };

        private static readonly BusinessType[] ServiceRuntimeTypes =
        {
            BusinessType.Builder,
            BusinessType.Saloon,
            BusinessType.Barber
        };

        private static readonly BusinessType[] StorefrontLocalOutletTypes =
        {
            BusinessType.FuelDealer,
            BusinessType.Bakery,
            BusinessType.Tailor,
            BusinessType.LumberYard
        };

        [Header("Archetype Runtime Diagnostics")]
        [SerializeField, TextArea(2, 7)]
        private string lastWeeklyArchetypeRuntimeSummary = "No archetype runtime pass has resolved yet.";

        [SerializeField, TextArea(2, 7)]
        private string lastWeeklyArchetypeSalesSummary = "No archetype local-market pass has resolved yet.";

        public string LastWeeklyArchetypeRuntimeSummary => string.IsNullOrWhiteSpace(lastWeeklyArchetypeRuntimeSummary)
            ? "No archetype runtime pass has resolved yet."
            : lastWeeklyArchetypeRuntimeSummary;

        public string LastWeeklyArchetypeSalesSummary => string.IsNullOrWhiteSpace(lastWeeklyArchetypeSalesSummary)
            ? "No archetype local-market pass has resolved yet."
            : lastWeeklyArchetypeSalesSummary;

        private int ResolveArchetypeRoutedWeeklyOperations(StringBuilder summary)
        {
            BusinessArchetypeRuntimeTally tally = new();
            int operationCount = 0;

            int goodsCount = ResolveOperationsForArchetypeGroup(
                "goods/workshop",
                BusinessArchetype.GoodsTransformer,
                GoodsTransformerRuntimeTypes,
                summary);
            tally.goodsTransformerOperations += goodsCount;
            operationCount += goodsCount;

            int serviceCount = ResolveOperationsForArchetypeGroup(
                "service",
                BusinessArchetype.ServiceThroughput,
                ServiceRuntimeTypes,
                summary);
            tally.serviceOperations += serviceCount;
            operationCount += serviceCount;

            int sawmillCount = ResolveSawmillOperationsForLinkedSupport(GetCurrentWeekKey(), summary);
            tally.specialProductionOperations += sawmillCount;
            operationCount += sawmillCount;

            int lodgingCount = ResolveBoardingHouseOperations(summary);
            tally.lodgingOperations += lodgingCount;
            operationCount += lodgingCount;

            int logisticsCount = ResolveLiveryFreightOperations(summary);
            tally.logisticsOperations += logisticsCount;
            operationCount += logisticsCount;

            int mineCount = ResolveMineOperations(summary);
            tally.specialProductionOperations += mineCount;
            operationCount += mineCount;

            tally.businessesReviewed = CountActiveSharedBusinessesForArchetypePass();
            tally.operationsResolved = operationCount;
            lastWeeklyArchetypeRuntimeSummary = tally.BuildOperationSummary();

            if (operationCount > 0)
            {
                AppendSummarySegment(summary, lastWeeklyArchetypeRuntimeSummary);
            }

            return operationCount;
        }

        private int ResolveOperationsForArchetypeGroup(
            string label,
            BusinessArchetype expectedArchetype,
            IReadOnlyList<BusinessType> businessTypes,
            StringBuilder summary)
        {
            if (businessTypes == null || businessTypes.Count <= 0)
            {
                return 0;
            }

            int resolved = 0;
            int present = 0;
            for (int i = 0; i < businessTypes.Count; i++)
            {
                BusinessType type = businessTypes[i];
                int typeCount = CountBusinessesOfType(type);
                if (typeCount <= 0)
                {
                    continue;
                }

                present += typeCount;
                BusinessProfileDefinition profile = FindProfile(type);
                BusinessArchetype archetype = profile != null ? profile.Archetype : expectedArchetype;
                if (!IsCompatibleRuntimeArchetype(expectedArchetype, archetype))
                {
                    AppendSummarySegment(summary, $"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(type)} profile routed as {archetype}, expected {expectedArchetype}");
                }

                resolved += ResolveOperationsForType(type, summary);
            }

            if (present > 0)
            {
                AppendSummarySegment(summary, $"{label} runtime {resolved}/{present}");
            }

            return resolved;
        }

        private int ResolveArchetypeRoutedLocalMarketSales(StringBuilder summary)
        {
            BusinessArchetypeRuntimeTally tally = new();
            int salesCount = 0;

            salesCount += SellTrackedLocalMarketOutput(BusinessType.CropFarm, CategoryCropFood, CropFarmLocalOutletMaxUnits, "staple crops", summary, ref tally);
            salesCount += SellTrackedLocalMarketOutput(BusinessType.Ranch, CategoryLivestock, RanchLocalOutletMaxUnits, "livestock", summary, ref tally);
            salesCount += SellTrackedLocalMarketOutput(BusinessType.Butcher, CategoryMeat, ButcherLocalOutletMaxUnits, "meat", summary, ref tally);
            salesCount += SellTrackedLocalMarketOutput(BusinessType.Blacksmith, CategoryHardware, BlacksmithLocalOutletMaxUnits, "hardware", summary, ref tally);
            salesCount += SellTrackedLocalMarketOutput(BusinessType.FuelDealer, CategoryFuelWood, FuelDealerLocalOutletMaxUnits, "fuel", summary, ref tally);
            salesCount += SellTrackedLocalMarketOutput(BusinessType.GrainMill, CategoryFlour, GrainMillLocalOutletMaxUnits, "flour", summary, ref tally);
            salesCount += SellTrackedLocalMarketOutput(BusinessType.Bakery, CategoryBread, BakeryLocalOutletMaxUnits, "bread", summary, ref tally);
            salesCount += SellTrackedLocalMarketOutput(BusinessType.Tailor, CategoryClothing, TailorLocalOutletMaxUnits, "clothing", summary, ref tally);
            salesCount += SellTrackedLocalMarketOutput(BusinessType.Wheelwright, CategoryWheelwrightRepairs, WheelwrightLocalRepairDemandMaxUnits, "wagon repairs", summary, ref tally);

            tally.operationsResolved = salesCount;
            lastWeeklyArchetypeSalesSummary = tally.BuildSalesSummary();

            if (salesCount > 0)
            {
                AppendSummarySegment(summary, lastWeeklyArchetypeSalesSummary);
            }

            return salesCount;
        }

        private int SellTrackedLocalMarketOutput(
            BusinessType businessType,
            string categoryId,
            int maxUnits,
            string outletLabel,
            StringBuilder summary,
            ref BusinessArchetypeRuntimeTally tally)
        {
            int before = CountBusinessesWithCategoryStock(businessType, categoryId);
            int soldCount = SellLocalMarketOutput(businessType, categoryId, maxUnits, summary);
            if (soldCount > 0)
            {
                tally.localMarketSales += soldCount;
                tally.salesLabels ??= new List<string>();
                tally.salesLabels.Add($"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType)} {outletLabel}");
            }
            else if (before > 0)
            {
                tally.localMarketNoSaleCandidates += before;
            }

            return soldCount;
        }

        private int CountActiveSharedBusinessesForArchetypePass()
        {
            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType == BusinessType.GeneralStore)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private int CountBusinessesOfType(BusinessType businessType)
        {
            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business != null && business.BusinessType == businessType)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountBusinessesWithCategoryStock(BusinessType businessType, string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != businessType || business.RuntimeState == null)
                {
                    continue;
                }

                CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
                if (stock != null && stock.CurrentStockUnits > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsCompatibleRuntimeArchetype(BusinessArchetype expected, BusinessArchetype actual)
        {
            if (expected == actual)
            {
                return true;
            }

            if (expected == BusinessArchetype.GoodsTransformer
                && (actual == BusinessArchetype.SpecialProduction || actual == BusinessArchetype.LogisticsMovement))
            {
                return false;
            }

            if (expected == BusinessArchetype.ServiceThroughput
                && (actual == BusinessArchetype.ConstructionProject || actual == BusinessArchetype.Lodging))
            {
                return true;
            }

            return actual == BusinessArchetype.Unspecified;
        }

        private struct BusinessArchetypeRuntimeTally
        {
            public int businessesReviewed;
            public int operationsResolved;
            public int goodsTransformerOperations;
            public int serviceOperations;
            public int lodgingOperations;
            public int logisticsOperations;
            public int specialProductionOperations;
            public int localMarketSales;
            public int localMarketNoSaleCandidates;
            public List<string> salesLabels;

            public string BuildOperationSummary()
            {
                return $"Archetype runtime: reviewed {businessesReviewed} | ops {operationsResolved} | goods {goodsTransformerOperations} | service {serviceOperations} | lodging {lodgingOperations} | logistics {logisticsOperations} | special {specialProductionOperations}.";
            }

            public string BuildSalesSummary()
            {
                string labels = salesLabels != null && salesLabels.Count > 0
                    ? string.Join(", ", salesLabels)
                    : "none";
                return $"Archetype sales: local outlets {localMarketSales} | stocked-but-unsold candidates {localMarketNoSaleCandidates} | outlets {labels}.";
            }
        }
    }
}
