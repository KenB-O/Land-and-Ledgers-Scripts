using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using UnityEngine;

namespace LandLedgers.Population
{
    [Serializable]
    public sealed class HouseholdReserveState
    {
        public string categoryId = string.Empty;
        public string displayName = string.Empty;
        public int currentUnits;
        public int targetUnits;
        public int lowThresholdUnits;
        public int lastDailyUseUnits;
        public int lastRequestedPurchaseUnits;
        public int lastLocalPurchaseUnits;
        public int lastOffMapPurchaseUnits;
        public float lastUrgency01;
        public int lastShoppingAttemptDayIndex = -1;
        public int consecutiveLocalShortfallDays;
        public int rememberedStockoutUnits;
        public float stockoutFrustration01;

        public void ApplyDefinition(HouseholdReserveDefinition definition, HouseholdState household)
        {
            if (definition == null)
            {
                currentUnits = Mathf.Max(0, currentUnits);
                targetUnits = Mathf.Max(0, targetUnits);
                lowThresholdUnits = Mathf.Clamp(lowThresholdUnits, 0, targetUnits);
                SanitizeMemory();
                return;
            }

            categoryId = definition.CategoryId;
            displayName = definition.DisplayName;
            targetUnits = HouseholdReserveCatalog.GetTargetUnits(definition, household);
            lowThresholdUnits = HouseholdReserveCatalog.GetLowThresholdUnits(definition, household);
            currentUnits = Mathf.Clamp(currentUnits, 0, targetUnits);
            SanitizeMemory();
        }

        public void ResetDailyPurchaseTracking()
        {
            lastDailyUseUnits = 0;
            lastRequestedPurchaseUnits = 0;
            lastLocalPurchaseUnits = 0;
            lastOffMapPurchaseUnits = 0;
            lastUrgency01 = 0f;
            rememberedStockoutUnits = Mathf.Max(0, rememberedStockoutUnits);
            stockoutFrustration01 = Mathf.Clamp01(stockoutFrustration01);
        }

        public void RecordShoppingOutcome(int absoluteDayIndex, int requestedUnits, int localUnits, int shortfallUnits)
        {
            int requested = Mathf.Max(0, requestedUnits);
            int local = Mathf.Max(0, localUnits);
            int shortfall = Mathf.Max(0, shortfallUnits);
            if (requested <= 0)
            {
                return;
            }

            lastShoppingAttemptDayIndex = absoluteDayIndex;
            if (shortfall > 0)
            {
                consecutiveLocalShortfallDays = Mathf.Min(30, consecutiveLocalShortfallDays + 1);
                rememberedStockoutUnits = Mathf.Min(targetUnits * 3 + 12, rememberedStockoutUnits + shortfall);
                float shortfallShare01 = Mathf.Clamp01(shortfall / (float)requested);
                stockoutFrustration01 = Mathf.Clamp01(stockoutFrustration01 + 0.16f + shortfallShare01 * 0.34f);
                return;
            }

            if (local > 0)
            {
                consecutiveLocalShortfallDays = Mathf.Max(0, consecutiveLocalShortfallDays - 1);
                rememberedStockoutUnits = Mathf.Max(0, rememberedStockoutUnits - local * 2);
                stockoutFrustration01 = Mathf.MoveTowards(stockoutFrustration01, 0f, 0.18f + Mathf.Clamp01(local / (float)requested) * 0.22f);
            }
        }

        public void DecayShoppingMemory()
        {
            rememberedStockoutUnits = Mathf.Max(0, rememberedStockoutUnits - 1);
            stockoutFrustration01 = Mathf.MoveTowards(Mathf.Clamp01(stockoutFrustration01), 0f, 0.015f);
            if (rememberedStockoutUnits == 0 && stockoutFrustration01 <= 0.01f)
            {
                consecutiveLocalShortfallDays = 0;
            }
        }

        private void SanitizeMemory()
        {
            lastShoppingAttemptDayIndex = Mathf.Max(-1, lastShoppingAttemptDayIndex);
            consecutiveLocalShortfallDays = Mathf.Max(0, consecutiveLocalShortfallDays);
            rememberedStockoutUnits = Mathf.Max(0, rememberedStockoutUnits);
            stockoutFrustration01 = Mathf.Clamp01(stockoutFrustration01);
        }
    }

    public sealed class HouseholdReserveDefinition
    {
        public HouseholdReserveDefinition(
            string categoryId,
            string displayName,
            RetailDemandGroup demandGroup,
            int baseTargetUnits,
            int lowThresholdUnits,
            int baseUseUnits,
            int householdMemberDivisor,
            int useEveryDays,
            int useCadenceSalt,
            params string[] localSellerCategoryIds)
        {
            CategoryId = string.IsNullOrWhiteSpace(categoryId) ? "reserve" : categoryId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? CategoryId : displayName;
            DemandGroup = demandGroup;
            BaseTargetUnits = Mathf.Max(0, baseTargetUnits);
            LowThresholdUnits = Mathf.Max(0, lowThresholdUnits);
            BaseUseUnits = Mathf.Max(0, baseUseUnits);
            HouseholdMemberDivisor = Mathf.Max(0, householdMemberDivisor);
            UseEveryDays = Mathf.Max(1, useEveryDays);
            UseCadenceSalt = useCadenceSalt;
            LocalSellerCategoryIds = localSellerCategoryIds ?? Array.Empty<string>();
        }

        public string CategoryId { get; }
        public string DisplayName { get; }
        public RetailDemandGroup DemandGroup { get; }
        public int BaseTargetUnits { get; }
        public int LowThresholdUnits { get; }
        public int BaseUseUnits { get; }
        public int HouseholdMemberDivisor { get; }
        public int UseEveryDays { get; }
        public int UseCadenceSalt { get; }
        public IReadOnlyList<string> LocalSellerCategoryIds { get; }
    }

    public readonly struct HouseholdReserveNeed
    {
        public HouseholdReserveNeed(
            HouseholdReserveState reserve,
            HouseholdReserveDefinition definition,
            int purchaseUnits,
            float urgency01)
        {
            Reserve = reserve;
            Definition = definition;
            PurchaseUnits = Mathf.Max(0, purchaseUnits);
            Urgency01 = Mathf.Clamp01(urgency01);
        }

        public HouseholdReserveState Reserve { get; }
        public HouseholdReserveDefinition Definition { get; }
        public int PurchaseUnits { get; }
        public float Urgency01 { get; }
        public string CategoryId => Definition != null ? Definition.CategoryId : Reserve != null ? Reserve.categoryId : string.Empty;
    }

    public static class HouseholdReserveCatalog
    {
        public const string StapleFoodCategoryId = "staple_food";
        public const string FuelWoodCategoryId = "fuel_wood";

        private static readonly HouseholdReserveDefinition[] Definitions =
        {
            new(
                StapleFoodCategoryId,
                "Staple Food",
                RetailDemandGroup.DailyStaple,
                HouseholdUpgradeCatalog.BaseFoodReserveCapacityUnits,
                3,
                1,
                3,
                1,
                11,
                "staple_food",
                "crop_food"),
            new(
                "meat",
                "Meat",
                RetailDemandGroup.FoodProtein,
                4,
                1,
                1,
                4,
                2,
                17,
                "meat"),
            new(
                "household_goods",
                "Household Goods",
                RetailDemandGroup.Household,
                5,
                2,
                1,
                4,
                2,
                23,
                "household_goods"),
            new(
                FuelWoodCategoryId,
                "Fuel Wood",
                RetailDemandGroup.Household,
                10,
                4,
                0,
                0,
                1,
                29,
                "fuel_wood",
                "slabs_offcuts",
                "lumber"),
            new(
                "clothing",
                "Clothing",
                RetailDemandGroup.Clothing,
                3,
                1,
                1,
                0,
                5,
                31,
                "clothing"),
            new(
                "tools_hardware",
                "Tools / Hardware",
                RetailDemandGroup.Hardware,
                3,
                1,
                1,
                0,
                4,
                37,
                "tools_hardware"),
            new(
                "medicine_remedies",
                "Medicine / Remedies",
                RetailDemandGroup.Medicine,
                2,
                1,
                1,
                5,
                7,
                41,
                "medicine_remedies")
        };

        public static IReadOnlyList<HouseholdReserveDefinition> All => Definitions;
        public static int Count => Definitions.Length;

        public static HouseholdReserveDefinition Get(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return null;
            }

            for (int i = 0; i < Definitions.Length; i++)
            {
                if (string.Equals(Definitions[i].CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return Definitions[i];
                }
            }

            return null;
        }

        public static int GetTargetUnits(HouseholdReserveDefinition definition, HouseholdState household)
        {
            if (definition == null)
            {
                return 0;
            }

            if (string.Equals(definition.CategoryId, StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return HouseholdUpgradeCatalog.GetFoodReserveCapacityUnits(household);
            }

            int consumers = GetHouseholdConsumerCount(household);
            int sizeBonus = GetCategoryTargetSizeBonus(definition.CategoryId, consumers);
            return Mathf.Max(0, definition.BaseTargetUnits + sizeBonus);
        }

        private static int GetHouseholdConsumerCount(HouseholdState household)
        {
            return household != null ? household.ConsumptionMemberCount : 1;
        }

        private static int GetCategoryTargetSizeBonus(string categoryId, int consumers)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return 0;
            }

            int broadHouseholdBonus = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0, consumers - 2) / 4f), 0, 3);
            if (string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase))
            {
                return broadHouseholdBonus;
            }

            if (string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return consumers >= 4 ? 1 : 0;
            }

            if (string.Equals(categoryId, "clothing", StringComparison.OrdinalIgnoreCase))
            {
                return consumers >= 6 ? 1 : 0;
            }

            return 0;
        }

        public static int GetLowThresholdUnits(HouseholdReserveDefinition definition, HouseholdState household)
        {
            if (definition == null)
            {
                return 0;
            }

            int target = GetTargetUnits(definition, household);
            float dwellingPressure = household != null && household.dwellingKind == HouseholdDwellingKind.RoughTemporaryLodging
                ? 0.25f
                : household != null && household.dwellingKind == HouseholdDwellingKind.KinSharedHousehold
                    ? 0.12f
                    : 0f;
            if (string.Equals(definition.CategoryId, StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp(Mathf.Max(definition.LowThresholdUnits, Mathf.CeilToInt(target * (0.35f + dwellingPressure))), 0, target);
            }

            return Mathf.Clamp(Mathf.Max(definition.LowThresholdUnits, Mathf.CeilToInt(target * dwellingPressure)), 0, target);
        }
    }

    public static class HouseholdHeatingEvaluator
    {
        private const int DaysPerMonth = 28;
        private const float ShoulderSeasonStrain01 = 0.45f;

        public static bool IsFuelReserve(string categoryId)
        {
            return string.Equals(categoryId, HouseholdReserveCatalog.FuelWoodCategoryId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFuelReserve(HouseholdReserveDefinition definition)
        {
            return definition != null && IsFuelReserve(definition.CategoryId);
        }

        public static int ResolveDailyHeatingUse(HouseholdState household, HouseholdReserveState reserve, int absoluteDayIndex)
        {
            if (household == null)
            {
                return 0;
            }

            household.EnsureHouseholdReservesInitialized();
            reserve ??= household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            if (reserve == null)
            {
                ResetHeatingTracking(household);
                return 0;
            }

            reserve.ApplyDefinition(HouseholdReserveCatalog.Get(HouseholdReserveCatalog.FuelWoodCategoryId), household);

            int requiredFuelUnits = CalculateDailyFuelUseUnits(household, absoluteDayIndex);
            int consumedUnits = Mathf.Min(requiredFuelUnits, Mathf.Max(0, reserve.currentUnits));
            reserve.currentUnits = Mathf.Max(0, reserve.currentUnits - consumedUnits);

            int shortfallUnits = Mathf.Max(0, requiredFuelUnits - consumedUnits);
            float winterStrain01 = CalculateEffectiveWinterStrain01(household, GetMonthFromAbsoluteDay(absoluteDayIndex));
            float pressure01 = CalculateHeatingPressure01(household, winterStrain01, requiredFuelUnits, shortfallUnits);

            reserve.lastDailyUseUnits = consumedUnits;
            household.lastWinterStrain01 = winterStrain01;
            household.lastHeatingPressure01 = pressure01;
            household.lastHeatingFuelUseUnits = consumedUnits;
            household.lastHeatingFuelShortfallUnits = shortfallUnits;
            return consumedUnits;
        }

        public static int CalculateDailyFuelUseUnits(HouseholdState household, int absoluteDayIndex)
        {
            if (household == null)
            {
                return 0;
            }

            float seasonStrain01 = GetSeasonStrain01(GetMonthFromAbsoluteDay(absoluteDayIndex));
            if (seasonStrain01 <= 0f)
            {
                return 0;
            }

            int members = household.ConsumptionMemberCount;
            float memberLoad = 1f + Mathf.Max(0, members - 1) * 0.35f;
            float useUnits = seasonStrain01
                * memberLoad
                * GetHeatRetentionFuelMultiplier(household.heatRetentionTier)
                * GetStoveFuelMultiplier(household.stoveTier);
            return Mathf.Max(1, Mathf.CeilToInt(useUnits));
        }

        public static float CalculateEffectiveWinterStrain01(HouseholdState household, int month)
        {
            if (household == null)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                GetSeasonStrain01(month)
                * GetHeatRetentionStrainMultiplier(household.heatRetentionTier)
                * GetStoveStrainMultiplier(household.stoveTier));
        }

        public static float GetSeasonStrain01(int month)
        {
            int normalizedMonth = Mathf.Clamp(month, 1, 12);
            return normalizedMonth switch
            {
                12 or 1 or 2 => 1f,
                11 or 3 => ShoulderSeasonStrain01,
                _ => 0f
            };
        }

        public static int GetMonthFromAbsoluteDay(int absoluteDayIndex)
        {
            return Mathf.Max(0, absoluteDayIndex) / DaysPerMonth % 12 + 1;
        }

        private static float CalculateHeatingPressure01(
            HouseholdState household,
            float winterStrain01,
            int requiredFuelUnits,
            int shortfallUnits)
        {
            if (household == null || winterStrain01 <= 0f)
            {
                return 0f;
            }

            float shortfallPressure01 = requiredFuelUnits <= 0
                ? 0f
                : Mathf.Clamp01((float)Mathf.Max(0, shortfallUnits) / requiredFuelUnits);
            float housingPressure01 = winterStrain01 * GetHousingPressureMultiplier(household);
            return Mathf.Clamp01(shortfallPressure01 * 0.85f + housingPressure01);
        }

        private static void ResetHeatingTracking(HouseholdState household)
        {
            household.lastWinterStrain01 = 0f;
            household.lastHeatingPressure01 = 0f;
            household.lastHeatingFuelUseUnits = 0;
            household.lastHeatingFuelShortfallUnits = 0;
        }

        private static float GetHeatRetentionFuelMultiplier(HouseholdHeatRetentionTier tier)
        {
            return tier switch
            {
                HouseholdHeatRetentionTier.Drafty => 1.25f,
                HouseholdHeatRetentionTier.Tight => 0.75f,
                _ => 1f
            };
        }

        private static float GetStoveFuelMultiplier(HouseholdStoveTier tier)
        {
            return tier == HouseholdStoveTier.Efficient ? 0.7f : 1f;
        }

        private static float GetHeatRetentionStrainMultiplier(HouseholdHeatRetentionTier tier)
        {
            return tier switch
            {
                HouseholdHeatRetentionTier.Drafty => 1.15f,
                HouseholdHeatRetentionTier.Tight => 0.75f,
                _ => 1f
            };
        }

        private static float GetStoveStrainMultiplier(HouseholdStoveTier tier)
        {
            return tier == HouseholdStoveTier.Efficient ? 0.85f : 1f;
        }

        private static float GetHousingPressureMultiplier(HouseholdState household)
        {
            float retentionPressure = household.heatRetentionTier switch
            {
                HouseholdHeatRetentionTier.Drafty => 0.22f,
                HouseholdHeatRetentionTier.Tight => 0.01f,
                _ => 0.05f
            };
            float stovePressure = household.stoveTier == HouseholdStoveTier.Efficient ? 0.01f : 0.05f;
            return Mathf.Clamp01(retentionPressure + stovePressure);
        }
    }

    public static class HouseholdReserveEvaluator
    {
        public static int ResolveDailyUse(HouseholdState household, int absoluteDayIndex)
        {
            if (household == null)
            {
                return 0;
            }

            household.EnsureHouseholdReservesInitialized();
            if (household.lastReserveDepletionDayIndex == absoluteDayIndex)
            {
                return 0;
            }

            int totalUsed = 0;
            IReadOnlyList<HouseholdReserveDefinition> definitions = HouseholdReserveCatalog.All;
            for (int i = 0; i < definitions.Count; i++)
            {
                HouseholdReserveDefinition definition = definitions[i];
                HouseholdReserveState reserve = household.GetReserve(definition.CategoryId);
                if (reserve == null)
                {
                    continue;
                }

                reserve.ApplyDefinition(definition, household);
                reserve.ResetDailyPurchaseTracking();
                reserve.DecayShoppingMemory();

                if (HouseholdHeatingEvaluator.IsFuelReserve(definition))
                {
                    totalUsed += HouseholdHeatingEvaluator.ResolveDailyHeatingUse(household, reserve, absoluteDayIndex);
                    continue;
                }

                int use = CalculateDailyUseUnits(household, definition, absoluteDayIndex);
                int consumed = Mathf.Min(use, Mathf.Max(0, reserve.currentUnits));
                reserve.currentUnits = Mathf.Max(0, reserve.currentUnits - consumed);
                reserve.lastDailyUseUnits = consumed;
                totalUsed += consumed;
            }

            household.lastReserveDepletionDayIndex = absoluteDayIndex;
            household.lastDailyReserveUseUnits = totalUsed;
            household.SyncLegacyFoodReserveFromReserves();
            return totalUsed;
        }

        public static List<HouseholdReserveNeed> BuildShoppingNeeds(HouseholdState household)
        {
            List<HouseholdReserveNeed> needs = new();
            if (household == null)
            {
                return needs;
            }

            household.EnsureHouseholdReservesInitialized();
            IReadOnlyList<HouseholdReserveDefinition> definitions = HouseholdReserveCatalog.All;
            for (int i = 0; i < definitions.Count; i++)
            {
                HouseholdReserveDefinition definition = definitions[i];
                HouseholdReserveState reserve = household.GetReserve(definition.CategoryId);
                if (reserve == null)
                {
                    continue;
                }

                reserve.ApplyDefinition(definition, household);
                int reserveThreshold = GetReserveThresholdUnits(reserve);
                int criticalThreshold = GetCriticalThresholdUnits(reserve);
                int deficit = Mathf.Max(0, reserve.targetUnits - reserve.currentUnits);
                bool belowReserve = reserve.currentUnits < reserveThreshold;
                bool critical = reserve.currentUnits <= criticalThreshold;
                bool rememberedUnreliable = reserve.stockoutFrustration01 >= GetUnreliableMemorySensitivity(definition.CategoryId) && reserve.rememberedStockoutUnits > 0;
                if (!belowReserve && !critical && !rememberedUnreliable || deficit <= 0)
                {
                    reserve.lastRequestedPurchaseUnits = 0;
                    reserve.lastUrgency01 = 0f;
                    continue;
                }

                int reserveGap = Mathf.Max(0, reserveThreshold - reserve.currentUnits);
                float reservePressure01 = reserveThreshold <= 0 ? 0f : Mathf.Clamp01(reserveGap / (float)reserveThreshold);
                float criticalPressure01 = criticalThreshold <= 0
                    ? reserve.currentUnits <= 0 ? 1f : 0f
                    : Mathf.Clamp01((criticalThreshold - reserve.currentUnits + 1f) / (criticalThreshold + 1f));
                float memoryPressure01 = Mathf.Clamp01(reserve.stockoutFrustration01 + Mathf.Clamp01(reserve.rememberedStockoutUnits / (float)Mathf.Max(1, reserve.targetUnits)) * 0.35f);
                float urgency = Mathf.Clamp01(
                    reservePressure01 * 0.55f
                    + criticalPressure01 * 0.35f
                    + memoryPressure01 * 0.25f
                    + GetNeedUrgencyBonus(definition.CategoryId));
                float purchaseShare01 = Mathf.Max(
                    GetPurchaseCoverageFloor01(definition.CategoryId),
                    Mathf.Lerp(0.55f, 1f, urgency));
                int purchaseUnits = Mathf.Clamp(
                    Mathf.CeilToInt(deficit * purchaseShare01),
                    1,
                    deficit);
                reserve.lastRequestedPurchaseUnits = purchaseUnits;
                reserve.lastUrgency01 = urgency;
                needs.Add(new HouseholdReserveNeed(reserve, definition, purchaseUnits, urgency));
            }

            needs.Sort(CompareNeedsByUrgencyThenCatalogOrder);
            return needs;
        }

        public static bool HasShoppingNeed(HouseholdState household)
        {
            return BuildShoppingNeeds(household).Count > 0;
        }

        public static HouseholdDemandSnapshot BuildDemandSnapshot(HouseholdState household)
        {
            List<HouseholdReserveNeed> needs = BuildShoppingNeeds(household);
            HouseholdDemandSnapshot demand = HouseholdDemandSnapshot.Empty();
            float maxUrgency = 0f;
            for (int i = 0; i < needs.Count; i++)
            {
                HouseholdReserveNeed need = needs[i];
                maxUrgency = Mathf.Max(maxUrgency, need.Urgency01);
                if (HouseholdHeatingEvaluator.IsFuelReserve(need.CategoryId))
                {
                    demand.heatingNeed += need.PurchaseUnits;
                    continue;
                }

                switch (need.Definition.DemandGroup)
                {
                    case RetailDemandGroup.DailyStaple:
                    case RetailDemandGroup.FoodProtein:
                        demand.foodNeed += need.PurchaseUnits;
                        break;
                    case RetailDemandGroup.Medicine:
                        demand.medicineNeed += need.PurchaseUnits;
                        break;
                    default:
                        demand.generalGoodsNeed += need.PurchaseUnits;
                        break;
                }
            }

            float heatingPressure01 = household != null ? Mathf.Clamp01(household.lastHeatingPressure01) : 0f;
            maxUrgency = Mathf.Max(maxUrgency, heatingPressure01);
            demand.heatingNeed = Mathf.Max(demand.heatingNeed, Mathf.RoundToInt(heatingPressure01 * 5f));
            demand.reservedFutureScore = Mathf.RoundToInt(maxUrgency * 5f);
            return demand;
        }

        public static HouseholdReservePressureSnapshot BuildPressureSnapshot(HouseholdState household)
        {
            HouseholdReservePressureSnapshot snapshot = HouseholdReservePressureSnapshot.Empty();
            if (household == null || household.reserves == null || household.reserves.Count == 0)
            {
                return snapshot;
            }

            float strongestPressure = 0f;
            int totalRequested = 0;
            int totalRemembered = 0;
            int maxShortfallDays = 0;
            int lowCount = 0;
            int criticalCount = 0;
            int stockoutCount = 0;
            int tracked = 0;

            string topCategoryId = string.Empty;
            string topCategoryName = string.Empty;
            int topCurrent = 0;
            int topTarget = 0;
            int topLowThreshold = 0;
            int topCriticalThreshold = 0;
            bool topIsFood = false;
            bool topIsFuel = false;
            bool topIsMedicine = false;

            for (int i = 0; i < household.reserves.Count; i++)
            {
                HouseholdReserveState reserve = household.reserves[i];
                if (reserve == null || string.IsNullOrWhiteSpace(reserve.categoryId))
                {
                    continue;
                }

                tracked++;
                int reserveThreshold = GetReserveThresholdUnits(reserve);
                int criticalThreshold = GetCriticalThresholdUnits(reserve);
                bool low = reserveThreshold > 0 && reserve.currentUnits <= reserveThreshold;
                bool critical = criticalThreshold > 0 && reserve.currentUnits <= criticalThreshold;
                bool stockoutMemory = reserve.rememberedStockoutUnits > 0
                    || reserve.consecutiveLocalShortfallDays > 0
                    || reserve.stockoutFrustration01 > 0.12f;
                bool isFood = IsFoodReserve(reserve.categoryId);
                bool isFuel = HouseholdHeatingEvaluator.IsFuelReserve(reserve.categoryId);
                bool isMedicine = IsMedicineReserve(reserve.categoryId);

                if (low)
                {
                    lowCount++;
                }

                if (critical)
                {
                    criticalCount++;
                }

                if (stockoutMemory)
                {
                    stockoutCount++;
                }

                if ((low || critical || stockoutMemory) && isFood)
                {
                    snapshot.foodPressure = true;
                }

                if ((low || critical || stockoutMemory || household.lastHeatingFuelShortfallUnits > 0) && isFuel)
                {
                    snapshot.fuelPressure = true;
                }

                if ((low || critical || stockoutMemory) && isMedicine)
                {
                    snapshot.medicinePressure = true;
                }

                totalRequested += Mathf.Max(0, reserve.lastRequestedPurchaseUnits);
                totalRemembered += Mathf.Max(0, reserve.rememberedStockoutUnits);
                maxShortfallDays = Mathf.Max(maxShortfallDays, Mathf.Max(0, reserve.consecutiveLocalShortfallDays));

                float fillPressure01 = 1f - GetReserveFill01(reserve);
                float thresholdPressure01 = reserveThreshold <= 0
                    ? 0f
                    : Mathf.Clamp01((reserveThreshold - reserve.currentUnits + 1f) / (reserveThreshold + 1f));
                float criticalPressure01 = criticalThreshold <= 0
                    ? reserve.currentUnits <= 0 ? 1f : 0f
                    : Mathf.Clamp01((criticalThreshold - reserve.currentUnits + 1f) / (criticalThreshold + 1f));
                float memoryPressure01 = Mathf.Clamp01(
                    Mathf.Max(0f, reserve.stockoutFrustration01)
                    + Mathf.Clamp01(reserve.rememberedStockoutUnits / (float)Mathf.Max(1, reserve.targetUnits + reserve.lowThresholdUnits + 2)) * 0.4f
                    + Mathf.Clamp01(reserve.consecutiveLocalShortfallDays / 4f) * 0.25f);
                float heatingPressure01 = isFuel
                    ? Mathf.Clamp01(household.lastHeatingPressure01 + Mathf.Clamp01(household.lastHeatingFuelShortfallUnits / 4f) * 0.45f)
                    : 0f;
                float categoryPressure01 = Mathf.Clamp01(
                    fillPressure01 * 0.32f
                    + thresholdPressure01 * 0.23f
                    + criticalPressure01 * 0.24f
                    + Mathf.Clamp01(reserve.lastUrgency01) * 0.17f
                    + memoryPressure01 * 0.24f
                    + heatingPressure01 * 0.34f
                    + GetPressurePriorityBonus01(reserve.categoryId));

                if (categoryPressure01 > strongestPressure)
                {
                    strongestPressure = categoryPressure01;
                    topCategoryId = reserve.categoryId;
                    topCategoryName = string.IsNullOrWhiteSpace(reserve.displayName) ? reserve.categoryId : reserve.displayName;
                    topCurrent = Mathf.Max(0, reserve.currentUnits);
                    topTarget = Mathf.Max(0, reserve.targetUnits);
                    topLowThreshold = reserveThreshold;
                    topCriticalThreshold = criticalThreshold;
                    topIsFood = isFood;
                    topIsFuel = isFuel;
                    topIsMedicine = isMedicine;
                }
            }

            HouseholdReservePressureBand band = HouseholdReservePressureBand.Stable;
            if (stockoutCount > 0 && (criticalCount > 0 || totalRemembered > 0 || maxShortfallDays >= 2))
            {
                band = HouseholdReservePressureBand.Stockout;
            }
            else if (criticalCount > 0 || strongestPressure >= 0.7f)
            {
                band = HouseholdReservePressureBand.Critical;
            }
            else if (lowCount > 0 || strongestPressure >= 0.38f || totalRequested > 0)
            {
                band = HouseholdReservePressureBand.Low;
            }

            snapshot.pressureBand = band;
            snapshot.pressureScore = Mathf.Clamp(Mathf.RoundToInt(strongestPressure * 100f), 0, 100);
            snapshot.trackedCategoryCount = tracked;
            snapshot.lowCategoryCount = lowCount;
            snapshot.criticalCategoryCount = criticalCount;
            snapshot.stockoutMemoryCategoryCount = stockoutCount;
            snapshot.requestedPurchaseUnits = totalRequested;
            snapshot.rememberedStockoutUnits = totalRemembered;
            snapshot.maxConsecutiveLocalShortfallDays = maxShortfallDays;
            snapshot.topPressureCategoryId = topCategoryId ?? string.Empty;
            snapshot.topPressureCategoryName = topCategoryName ?? string.Empty;
            snapshot.topCurrentUnits = topCurrent;
            snapshot.topTargetUnits = topTarget;
            snapshot.topLowThresholdUnits = topLowThreshold;
            snapshot.topCriticalThresholdUnits = topCriticalThreshold;
            snapshot.topIsFood = topIsFood;
            snapshot.topIsFuel = topIsFuel;
            snapshot.topIsMedicine = topIsMedicine;
            BuildReservePressureCopy(ref snapshot);
            return snapshot;
        }

        private static void BuildReservePressureCopy(ref HouseholdReservePressureSnapshot snapshot)
        {
            string categoryName = string.IsNullOrWhiteSpace(snapshot.topPressureCategoryName)
                ? "reserves"
                : snapshot.topPressureCategoryName;

            switch (snapshot.pressureBand)
            {
                case HouseholdReservePressureBand.Stockout:
                    snapshot.headline = $"Reserve stockout memory: {categoryName}.";
                    snapshot.primaryCause = "Repeated local shortfalls or remembered stockouts are affecting household shopping confidence.";
                    snapshot.recommendedAction = BuildReserveAction(snapshot, "Improve local supply reliability before frustration becomes a durable preference penalty.");
                    break;
                case HouseholdReservePressureBand.Critical:
                    snapshot.headline = $"Critical household reserve pressure: {categoryName}.";
                    snapshot.primaryCause = "At least one reserve category is at or below its critical threshold.";
                    snapshot.recommendedAction = BuildReserveAction(snapshot, "Prioritize restock before household work readiness, health, or store preference suffers.");
                    break;
                case HouseholdReservePressureBand.Low:
                    snapshot.headline = $"Household reserves running low: {categoryName}.";
                    snapshot.primaryCause = "One or more reserve categories are below their preferred buffer.";
                    snapshot.recommendedAction = BuildReserveAction(snapshot, "Top up reserves before the next shopping cycle becomes urgent.");
                    break;
                default:
                    snapshot.headline = "Household reserves are stable.";
                    snapshot.primaryCause = string.Empty;
                    snapshot.recommendedAction = string.Empty;
                    break;
            }
        }

        private static string BuildReserveAction(HouseholdReservePressureSnapshot snapshot, string fallback)
        {
            if (snapshot.topIsFuel || snapshot.fuelPressure)
            {
                return "Prioritize fuel supply before heating strain turns into illness risk, wage loss, or emergency purchases.";
            }

            if (snapshot.topIsMedicine || snapshot.medicinePressure)
            {
                return "Prioritize remedies and doctor access before household health burden compounds.";
            }

            if (snapshot.topIsFood || snapshot.foodPressure)
            {
                return "Prioritize food availability before hunger pressure and store preference penalties build.";
            }

            return fallback;
        }

        private static bool IsFoodReserve(string categoryId)
        {
            return string.Equals(categoryId, HouseholdReserveCatalog.StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMedicineReserve(string categoryId)
        {
            return string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase);
        }

        private static float GetReserveFill01(HouseholdReserveState reserve)
        {
            if (reserve == null)
            {
                return 0f;
            }

            int target = Mathf.Max(1, reserve.targetUnits);
            return Mathf.Clamp01(reserve.currentUnits / (float)target);
        }

        private static float GetPressurePriorityBonus01(string categoryId)
        {
            if (IsMedicineReserve(categoryId))
            {
                return 0.06f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, HouseholdReserveCatalog.FuelWoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.045f;
            }

            if (string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase))
            {
                return 0.025f;
            }

            return 0f;
        }

        private static int CalculateDailyUseUnits(HouseholdState household, HouseholdReserveDefinition definition, int absoluteDayIndex)
        {
            if (household == null || definition == null)
            {
                return 0;
            }

            int cadence = Mathf.Max(1, definition.UseEveryDays);
            int cadenceValue = Mathf.Abs(absoluteDayIndex + household.id + definition.UseCadenceSalt);
            if (cadenceValue % cadence != 0)
            {
                return 0;
            }

            int members = household.ConsumptionMemberCount;
            int use = definition.BaseUseUnits;
            if (definition.HouseholdMemberDivisor > 0)
            {
                use += members / definition.HouseholdMemberDivisor;
            }

            return Mathf.Max(0, use);
        }

        public static int GetReserveThresholdUnits(HouseholdReserveState reserve)
        {
            if (reserve == null)
            {
                return 0;
            }

            return Mathf.Clamp(
                Mathf.Max(
                    reserve.lowThresholdUnits,
                    Mathf.CeilToInt(Mathf.Max(0, reserve.targetUnits) * GetReserveThresholdShare01(reserve.categoryId))),
                0,
                Mathf.Max(0, reserve.targetUnits));
        }

        public static int GetCriticalThresholdUnits(HouseholdReserveState reserve)
        {
            if (reserve == null)
            {
                return 0;
            }

            return Mathf.Clamp(
                Mathf.Max(
                    1,
                    Mathf.FloorToInt(Mathf.Max(0, reserve.lowThresholdUnits) * 0.5f),
                    Mathf.CeilToInt(Mathf.Max(0, reserve.targetUnits) * GetCriticalThresholdShare01(reserve.categoryId))),
                0,
                Mathf.Max(0, reserve.targetUnits));
        }

        private static float GetReserveThresholdShare01(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return 0.5f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.6f;
            }

            if (string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase))
            {
                return 0.55f;
            }

            if (string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase))
            {
                return 0.6f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.FuelWoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.55f;
            }

            if (string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return 0.67f;
            }

            if (string.Equals(categoryId, "clothing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "tools_hardware", StringComparison.OrdinalIgnoreCase))
            {
                return 0.45f;
            }

            return 0.5f;
        }

        private static float GetCriticalThresholdShare01(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return 0.2f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.3f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.FuelWoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.28f;
            }

            if (string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return 0.34f;
            }

            return 0.2f;
        }

        private static float GetNeedUrgencyBonus(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return 0f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.06f;
            }

            if (string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase))
            {
                return 0.03f;
            }

            if (string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase))
            {
                return 0.04f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.FuelWoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.06f;
            }

            if (string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return 0.08f;
            }

            return 0f;
        }

        private static float GetPurchaseCoverageFloor01(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return 0.55f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.72f;
            }

            if (string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase))
            {
                return 0.68f;
            }

            if (string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase))
            {
                return 0.7f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.FuelWoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.75f;
            }

            if (string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return 0.85f;
            }

            return 0.55f;
        }

        private static float GetUnreliableMemorySensitivity(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return 0.25f;
            }

            if (string.Equals(categoryId, HouseholdReserveCatalog.StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return 0.18f;
            }

            if (string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, HouseholdReserveCatalog.FuelWoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.2f;
            }

            return 0.25f;
        }

        private static int CompareNeedsByUrgencyThenCatalogOrder(HouseholdReserveNeed left, HouseholdReserveNeed right)
        {
            int urgency = right.Urgency01.CompareTo(left.Urgency01);
            if (urgency != 0)
            {
                return urgency;
            }

            return GetCatalogIndex(left.CategoryId).CompareTo(GetCatalogIndex(right.CategoryId));
        }

        private static int GetCatalogIndex(string categoryId)
        {
            IReadOnlyList<HouseholdReserveDefinition> definitions = HouseholdReserveCatalog.All;
            for (int i = 0; i < definitions.Count; i++)
            {
                if (string.Equals(definitions[i].CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }
    }
}
