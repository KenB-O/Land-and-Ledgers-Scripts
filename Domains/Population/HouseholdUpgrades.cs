using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Population
{
    public enum HouseholdUpgradeKind
    {
        ChickenCoop = 0,
        KitchenGarden = 1,
        RootCellar = 2,
        BoarderRoom = 3
    }

    [Serializable]
    public sealed class HouseholdUpgradeState
    {
        public HouseholdUpgradeKind kind;
        public bool built;
        public int builtDayIndex;

        public static HouseholdUpgradeState Built(HouseholdUpgradeKind kind, int builtDayIndex)
        {
            return new HouseholdUpgradeState
            {
                kind = kind,
                built = true,
                builtDayIndex = Mathf.Max(0, builtDayIndex)
            };
        }
    }

    public sealed class HouseholdUpgradeDefinition
    {
        public HouseholdUpgradeDefinition(
            HouseholdUpgradeKind kind,
            string displayName,
            string resilienceSummary,
            int weeklyReserveProductionUnits,
            int weeklyIncomeCents,
            int weeklyUpkeepCents,
            int foodReserveCapacityBonus,
            int dailyFoodNeedBonus,
            int dailyGeneralGoodsNeedBonus,
            int housingSlotBonus,
            int lumberUnits,
            int nailsUnits,
            int laborUnits)
        {
            Kind = kind;
            DisplayName = displayName ?? kind.ToString();
            ResilienceSummary = resilienceSummary ?? string.Empty;
            WeeklyReserveProductionUnits = Mathf.Max(0, weeklyReserveProductionUnits);
            WeeklyIncomeCents = Mathf.Max(0, weeklyIncomeCents);
            WeeklyUpkeepCents = Mathf.Max(0, weeklyUpkeepCents);
            FoodReserveCapacityBonus = Mathf.Max(0, foodReserveCapacityBonus);
            DailyFoodNeedBonus = Mathf.Max(0, dailyFoodNeedBonus);
            DailyGeneralGoodsNeedBonus = Mathf.Max(0, dailyGeneralGoodsNeedBonus);
            HousingSlotBonus = Mathf.Max(0, housingSlotBonus);
            LumberUnits = Mathf.Max(0, lumberUnits);
            NailsUnits = Mathf.Max(0, nailsUnits);
            LaborUnits = Mathf.Max(0, laborUnits);
        }

        public HouseholdUpgradeKind Kind { get; }
        public string DisplayName { get; }
        public string ResilienceSummary { get; }
        public int WeeklyReserveProductionUnits { get; }
        public int WeeklyIncomeCents { get; }
        public int WeeklyUpkeepCents { get; }
        public int FoodReserveCapacityBonus { get; }
        public int DailyFoodNeedBonus { get; }
        public int DailyGeneralGoodsNeedBonus { get; }
        public int HousingSlotBonus { get; }
        public int LumberUnits { get; }
        public int NailsUnits { get; }
        public int LaborUnits { get; }
    }

    public static class HouseholdUpgradeCatalog
    {
        public const int BaseFoodReserveCapacityUnits = 8;

        private static readonly HouseholdUpgradeDefinition[] Definitions =
        {
            new(
                HouseholdUpgradeKind.ChickenCoop,
                "Chicken Coop",
                "Adds steady egg and small livestock resilience.",
                2,
                50,
                25,
                0,
                0,
                0,
                0,
                6,
                2,
                4),
            new(
                HouseholdUpgradeKind.KitchenGarden,
                "Kitchen Garden",
                "Builds a low-cost buffer against staple food shortages.",
                4,
                0,
                15,
                0,
                0,
                0,
                0,
                3,
                1,
                5),
            new(
                HouseholdUpgradeKind.RootCellar,
                "Root Cellar",
                "Keeps stored food usable longer and expands reserve space.",
                0,
                0,
                10,
                16,
                0,
                0,
                0,
                10,
                3,
                8),
            new(
                HouseholdUpgradeKind.BoarderRoom,
                "Boarder Room",
                "Turns spare home space into lodging income.",
                0,
                350,
                75,
                0,
                1,
                1,
                1,
                14,
                4,
                10)
        };

        public static int Count => Definitions.Length;
        public static IReadOnlyList<HouseholdUpgradeDefinition> All => Definitions;

        public static HouseholdUpgradeDefinition GetByIndex(int index)
        {
            return Definitions.Length == 0 ? null : Definitions[Mathf.Clamp(index, 0, Definitions.Length - 1)];
        }

        public static HouseholdUpgradeDefinition Get(HouseholdUpgradeKind kind)
        {
            for (int i = 0; i < Definitions.Length; i++)
            {
                if (Definitions[i].Kind == kind)
                {
                    return Definitions[i];
                }
            }

            return null;
        }

        public static bool HasBuiltUpgrade(HouseholdState household, HouseholdUpgradeKind kind)
        {
            if (household == null)
            {
                return false;
            }

            household.EnsureHouseholdUpgradesInitialized();
            for (int i = 0; i < household.upgrades.Count; i++)
            {
                HouseholdUpgradeState upgrade = household.upgrades[i];
                if (upgrade != null && upgrade.built && upgrade.kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryAddBuiltUpgrade(HouseholdState household, HouseholdUpgradeKind kind, int builtDayIndex)
        {
            if (household == null || HasBuiltUpgrade(household, kind))
            {
                return false;
            }

            household.upgrades.Add(HouseholdUpgradeState.Built(kind, builtDayIndex));
            ClampReserveToCapacity(household);
            return true;
        }

        public static int CountBuiltUpgrades(HouseholdState household)
        {
            if (household == null)
            {
                return 0;
            }

            household.EnsureHouseholdUpgradesInitialized();
            int count = 0;
            for (int i = 0; i < household.upgrades.Count; i++)
            {
                if (household.upgrades[i] != null && household.upgrades[i].built)
                {
                    count++;
                }
            }

            return count;
        }

        public static int GetFoodReserveCapacityUnits(HouseholdState household)
        {
            int capacity = BaseFoodReserveCapacityUnits;
            if (household == null)
            {
                return capacity;
            }

            household.EnsureHouseholdUpgradesInitialized();
            int consumerCount = household.ConsumptionMemberCount;
            capacity += Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0, consumerCount - 2) / 4f), 0, 3);

            for (int i = 0; i < household.upgrades.Count; i++)
            {
                HouseholdUpgradeState upgrade = household.upgrades[i];
                if (upgrade == null || !upgrade.built)
                {
                    continue;
                }

                HouseholdUpgradeDefinition definition = Get(upgrade.kind);
                if (definition != null)
                {
                    capacity += definition.FoodReserveCapacityBonus;
                }
            }

            return Mathf.Max(0, capacity);
        }

        public static void ClampReserveToCapacity(HouseholdState household)
        {
            if (household == null)
            {
                return;
            }

            household.foodReserveUnits = Mathf.Clamp(household.foodReserveUnits, 0, GetFoodReserveCapacityUnits(household));
            bool migrateLegacyReserve = household.reserves == null || household.reserves.Count == 0;
            household.EnsureHouseholdReservesInitialized(false, migrateLegacyReserve);
            HouseholdReserveState stapleReserve = household.GetReserve("staple_food");
            if (stapleReserve != null)
            {
                HouseholdReserveDefinition definition = HouseholdReserveCatalog.Get("staple_food");
                stapleReserve.ApplyDefinition(definition, household);
                household.foodReserveUnits = stapleReserve.currentUnits;
            }
        }
    }

    public static class HouseholdUpgradeEconomyEvaluator
    {
        public static HouseholdDemandSnapshot ApplyDailyDemandEffects(HouseholdState household, HouseholdDemandSnapshot demand)
        {
            if (household == null)
            {
                return demand;
            }

            household.EnsureHouseholdUpgradesInitialized();
            ApplyDailyNeedBonuses(household, ref demand);

            household.EnsureHouseholdReservesInitialized();
            HouseholdReserveState stapleReserve = household.GetReserve("staple_food");
            int availableReserve = stapleReserve != null ? stapleReserve.currentUnits : household.foodReserveUnits;
            int maxReserveUse = HouseholdUpgradeCatalog.HasBuiltUpgrade(household, HouseholdUpgradeKind.RootCellar) ? 2 : 1;
            int reserveUsed = Mathf.Min(Mathf.Max(0, demand.foodNeed), Mathf.Max(0, availableReserve), maxReserveUse);
            if (reserveUsed > 0)
            {
                if (stapleReserve != null)
                {
                    stapleReserve.currentUnits = Mathf.Max(0, stapleReserve.currentUnits - reserveUsed);
                    household.SyncLegacyFoodReserveFromReserves();
                }
                else
                {
                    household.foodReserveUnits -= reserveUsed;
                }

                demand.foodNeed = Mathf.Max(0, demand.foodNeed - reserveUsed);
            }

            return demand;
        }

        /// <summary>
        /// Phase B (Real People): household-upgrade production income and upkeep
        /// now flow through the household ledger with explicit provenance (Canon
        /// 13.2) instead of the retired spendingMoneyCents wallet. Pass the
        /// household's ledger (null keeps the legacy wallet write for unwired
        /// contexts). Income and upkeep are recorded as separate entries so the
        /// household's production economics stay auditable.
        /// </summary>
        public static void ApplyWeeklySettlementEffects(HouseholdState household, HouseholdLedger ledger = null, int dayIndex = -1)
        {
            if (household == null)
            {
                return;
            }

            household.EnsureHouseholdUpgradesInitialized();
            household.EnsureHouseholdReservesInitialized();
            HouseholdReserveState stapleReserve = household.GetReserve("staple_food");
            int reserveBefore = stapleReserve != null ? Mathf.Max(0, stapleReserve.currentUnits) : Mathf.Max(0, household.foodReserveUnits);
            int reserveProduction = 0;
            int income = 0;
            int upkeep = 0;

            for (int i = 0; i < household.upgrades.Count; i++)
            {
                HouseholdUpgradeState upgrade = household.upgrades[i];
                if (upgrade == null || !upgrade.built)
                {
                    continue;
                }

                HouseholdUpgradeDefinition definition = HouseholdUpgradeCatalog.Get(upgrade.kind);
                if (definition == null)
                {
                    continue;
                }

                reserveProduction += definition.WeeklyReserveProductionUnits;
                income += definition.WeeklyIncomeCents;
                upkeep += definition.WeeklyUpkeepCents;
            }

            int capacity = HouseholdUpgradeCatalog.GetFoodReserveCapacityUnits(household);
            bool preventsSpoilage = HouseholdUpgradeCatalog.HasBuiltUpgrade(household, HouseholdUpgradeKind.RootCellar);
            int preSpoilageReserve = Mathf.Clamp(reserveBefore + reserveProduction, 0, capacity);
            int spoilage = preventsSpoilage ? 0 : Mathf.Min(1, preSpoilageReserve);
            int resolvedReserve = Mathf.Clamp(preSpoilageReserve - spoilage, 0, capacity);
            if (stapleReserve != null)
            {
                stapleReserve.currentUnits = resolvedReserve;
                stapleReserve.ApplyDefinition(HouseholdReserveCatalog.Get("staple_food"), household);
                household.SyncLegacyFoodReserveFromReserves();
            }
            else
            {
                household.foodReserveUnits = resolvedReserve;
            }

            if (ledger != null)
            {
                int day = Mathf.Max(0, dayIndex);
                int incomeRecorded = 0;
                int upkeepPaid = 0;
                if (income > 0)
                {
                    string incomeRejection = ledger.RecordInflow(
                        day,
                        income,
                        HouseholdIncomeSource.OtherDocumented,
                        "household-upgrades",
                        "household upgrade production income (garden, cellar, outbuildings)",
                        "household production");
                    if (incomeRejection == null)
                    {
                        incomeRecorded = income;
                    }
                }

                if (upkeep > 0)
                {
                    string upkeepRejection = ledger.RecordOutflow(
                        day,
                        upkeep,
                        "household upgrade upkeep",
                        "household production");
                    if (upkeepRejection == null)
                    {
                        upkeepPaid = upkeep;
                    }
                    // A rejected upkeep (insufficient funds) leaves the upkeep
                    // unpaid and logged — never a negative balance, never fiat.
                }

                household.lastWeeklyUpgradeIncomeCents = incomeRecorded;
                household.lastWeeklyUpgradeUpkeepCents = upkeepPaid;
            }
            else
            {
                int netWallet = income - upkeep;
                household.spendingMoneyCents = Mathf.Max(0, household.spendingMoneyCents + netWallet);
                household.lastWeeklyUpgradeIncomeCents = income;
                household.lastWeeklyUpgradeUpkeepCents = upkeep;
            }

            household.lastWeeklyReserveDeltaUnits = resolvedReserve - reserveBefore;
        }

        private static void ApplyDailyNeedBonuses(HouseholdState household, ref HouseholdDemandSnapshot demand)
        {
            for (int i = 0; i < household.upgrades.Count; i++)
            {
                HouseholdUpgradeState upgrade = household.upgrades[i];
                if (upgrade == null || !upgrade.built)
                {
                    continue;
                }

                HouseholdUpgradeDefinition definition = HouseholdUpgradeCatalog.Get(upgrade.kind);
                if (definition == null)
                {
                    continue;
                }

                demand.foodNeed += definition.DailyFoodNeedBonus;
                demand.generalGoodsNeed += definition.DailyGeneralGoodsNeedBonus;
            }
        }
    }
}
