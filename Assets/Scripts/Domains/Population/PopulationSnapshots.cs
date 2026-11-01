using System;

namespace LandLedgers.Population
{
    [Serializable]
    public struct WageSnapshot
    {
        public int weeklyWage;
        public int stabilityPercent;
        public string currencyId;

        public static WageSnapshot None()
        {
            return new WageSnapshot
            {
                weeklyWage = 0,
                stabilityPercent = 0,
                currencyId = "dollars"
            };
        }
    }

    [Serializable]
    public struct HouseholdDemandSnapshot
    {
        public int foodNeed;
        public int generalGoodsNeed;
        public int medicineNeed;
        public int heatingNeed;
        public int reservedFutureScore;

        public static HouseholdDemandSnapshot Empty()
        {
            return new HouseholdDemandSnapshot();
        }
    }

    public enum HouseholdReservePressureBand
    {
        Stable = 0,
        Low = 1,
        Critical = 2,
        Stockout = 3
    }

    [Serializable]
    public struct HouseholdReservePressureSnapshot
    {
        public HouseholdReservePressureBand pressureBand;
        public int pressureScore;
        public int trackedCategoryCount;
        public int lowCategoryCount;
        public int criticalCategoryCount;
        public int stockoutMemoryCategoryCount;
        public int requestedPurchaseUnits;
        public int rememberedStockoutUnits;
        public int maxConsecutiveLocalShortfallDays;
        public string topPressureCategoryId;
        public string topPressureCategoryName;
        public int topCurrentUnits;
        public int topTargetUnits;
        public int topLowThresholdUnits;
        public int topCriticalThresholdUnits;
        public bool foodPressure;
        public bool fuelPressure;
        public bool medicinePressure;
        public bool topIsFood;
        public bool topIsFuel;
        public bool topIsMedicine;
        public string headline;
        public string primaryCause;
        public string recommendedAction;

        public static HouseholdReservePressureSnapshot Empty()
        {
            return new HouseholdReservePressureSnapshot
            {
                pressureBand = HouseholdReservePressureBand.Stable,
                pressureScore = 0,
                topPressureCategoryId = string.Empty,
                topPressureCategoryName = string.Empty,
                headline = "Household reserves are stable.",
                primaryCause = string.Empty,
                recommendedAction = string.Empty
            };
        }
    }

    public enum TownReservePressureBand
    {
        Stable = 0,
        Watch = 1,
        Pressure = 2,
        Urgent = 3
    }

    [Serializable]
    public struct TownReservePressureSnapshot
    {
        public bool initialized;
        public TownReservePressureBand pressureBand;
        public float pressure01;
        public int pressureScore;
        public int trackedHouseholds;
        public int trackedHouseholdCount;
        public int lowReserveHouseholds;
        public int lowReserveHouseholdCount;
        public int criticalReserveHouseholds;
        public int criticalReserveHouseholdCount;
        public int stockoutMemoryHouseholds;
        public int stockoutMemoryHouseholdCount;
        public int foodPressureHouseholds;
        public int foodPressureHouseholdCount;
        public int fuelPressureHouseholds;
        public int fuelPressureHouseholdCount;
        public int medicinePressureHouseholds;
        public int medicinePressureHouseholdCount;
        public int totalRequestedPurchaseUnits;
        public int requestedPurchaseUnits;
        public int totalRememberedStockoutUnits;
        public int rememberedStockoutUnits;
        public int maxConsecutiveShortfallDays;
        public int maxConsecutiveLocalShortfallDays;
        public string topCategoryId;
        public string topPressureCategoryId;
        public string topCategoryName;
        public string topPressureCategoryName;
        public int topCategoryHouseholds;
        public float topCategoryPressure01;
        public string headline;
        public string primaryCause;
        public string recommendedAction;

        public static TownReservePressureSnapshot Empty()
        {
            return new TownReservePressureSnapshot
            {
                initialized = false,
                pressureBand = TownReservePressureBand.Stable,
                pressure01 = 0f,
                pressureScore = 0,
                topCategoryId = string.Empty,
                topPressureCategoryId = string.Empty,
                topCategoryName = string.Empty,
                topPressureCategoryName = string.Empty,
                headline = "Town reserve pressure unavailable.",
                primaryCause = string.Empty,
                recommendedAction = string.Empty
            };
        }

        public void Sanitize()
        {
            if (!Enum.IsDefined(typeof(TownReservePressureBand), pressureBand))
            {
                pressureBand = TownReservePressureBand.Stable;
            }

            pressure01 = Clamp01(pressure01);
            pressureScore = Math.Max(0, pressureScore);
            trackedHouseholds = Math.Max(0, Math.Max(trackedHouseholds, trackedHouseholdCount));
            trackedHouseholdCount = trackedHouseholds;
            lowReserveHouseholds = Math.Max(0, Math.Max(lowReserveHouseholds, lowReserveHouseholdCount));
            lowReserveHouseholdCount = lowReserveHouseholds;
            criticalReserveHouseholds = Math.Max(0, Math.Max(criticalReserveHouseholds, criticalReserveHouseholdCount));
            criticalReserveHouseholdCount = criticalReserveHouseholds;
            stockoutMemoryHouseholds = Math.Max(0, Math.Max(stockoutMemoryHouseholds, stockoutMemoryHouseholdCount));
            stockoutMemoryHouseholdCount = stockoutMemoryHouseholds;
            foodPressureHouseholds = Math.Max(0, Math.Max(foodPressureHouseholds, foodPressureHouseholdCount));
            foodPressureHouseholdCount = foodPressureHouseholds;
            fuelPressureHouseholds = Math.Max(0, Math.Max(fuelPressureHouseholds, fuelPressureHouseholdCount));
            fuelPressureHouseholdCount = fuelPressureHouseholds;
            medicinePressureHouseholds = Math.Max(0, Math.Max(medicinePressureHouseholds, medicinePressureHouseholdCount));
            medicinePressureHouseholdCount = medicinePressureHouseholds;
            totalRequestedPurchaseUnits = Math.Max(0, Math.Max(totalRequestedPurchaseUnits, requestedPurchaseUnits));
            requestedPurchaseUnits = totalRequestedPurchaseUnits;
            totalRememberedStockoutUnits = Math.Max(0, Math.Max(totalRememberedStockoutUnits, rememberedStockoutUnits));
            rememberedStockoutUnits = totalRememberedStockoutUnits;
            maxConsecutiveShortfallDays = Math.Max(0, Math.Max(maxConsecutiveShortfallDays, maxConsecutiveLocalShortfallDays));
            maxConsecutiveLocalShortfallDays = maxConsecutiveShortfallDays;
            topCategoryId ??= topPressureCategoryId ?? string.Empty;
            topPressureCategoryId ??= topCategoryId;
            topCategoryName ??= topPressureCategoryName ?? string.Empty;
            topPressureCategoryName ??= topCategoryName;
            topCategoryHouseholds = Math.Max(0, topCategoryHouseholds);
            topCategoryPressure01 = Clamp01(topCategoryPressure01);
            headline ??= string.Empty;
            primaryCause ??= string.Empty;
            recommendedAction ??= string.Empty;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Math.Min(1f, Math.Max(0f, value));
        }
    }

    public enum SettlementPressureBand
    {
        Calm = 0,
        Opportunity = 1,
        Pressure = 2,
        Urgent = 3
    }

    [Serializable]
    public struct SettlementPressureSnapshot
    {
        public SettlementPressureBand pressureBand;
        public int pressureScore;
        public float boardingSaturation01;
        public float boardingHouseSaturation01;
        public float rentalShortage01;
        public float transientPressure01;
        public float laborAbsorption01;
        public int boardingCapacity;
        public int boardingUsed;
        public int boardingHouseCapacity;
        public int boardingHouseUsed;
        public int rentalVacancyCount;
        public int playerOwnedRentalVacancyCount;
        public int rentalApplicantCount;
        public int strongerHousingNeedCount;
        public int transientPersonCount;
        public int uncoveredHousingDemand;
        public bool playerOwnedVacancyReliefAvailable;
        public string headline;
        public string primaryCause;
        public string recommendedAction;

        public static SettlementPressureSnapshot Empty()
        {
            return new SettlementPressureSnapshot
            {
                pressureBand = SettlementPressureBand.Calm,
                pressureScore = 0,
                laborAbsorption01 = 1f,
                headline = "Settlement pressure unavailable.",
                primaryCause = string.Empty,
                recommendedAction = string.Empty
            };
        }
    }

    public enum PopulationValidationSeverity
    {
        Clean = 0,
        Notice = 1,
        Warning = 2,
        Error = 3
    }

    [Serializable]
    public struct PopulationValidationSnapshot
    {
        public PopulationValidationSeverity severity;
        public int personCount;
        public int householdCount;
        public int activePersonCount;
        public int activeHouseholdCount;
        public int nullPersonEntries;
        public int nullHouseholdEntries;
        public int duplicatePersonIds;
        public int duplicateHouseholdIds;
        public int negativePersonIds;
        public int negativeHouseholdIds;
        public int orphanedPeople;
        public int missingMemberReferences;
        public int missingBoarderReferences;
        public int duplicateMemberLinks;
        public int duplicateBoarderLinks;
        public int householdMemberMismatches;
        public int boarderArrangementMismatches;
        public int invalidHostHouseholdLinks;
        public int emptyNonGuestHouseholds;
        public int departedActiveMismatches;
        public int boardingOverCapacityHouseholds;
        public int staleRentalApplicants;
        public int invalidRentalApplicants;
        public int invalidRentalProperties;
        public int overfilledRentalProperties;
        public int unassignedLaborEligibleWorkers;
        public int negativeMoneyRecords;
        public int errorCount;
        public int warningCount;
        public int noticeCount;
        public string headline;
        public string detail;
        public string recommendedAction;

        public static PopulationValidationSnapshot Clean()
        {
            return new PopulationValidationSnapshot
            {
                severity = PopulationValidationSeverity.Clean,
                headline = "Population records are clean.",
                detail = string.Empty,
                recommendedAction = string.Empty
            };
        }
    }
}
