using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Lane 9b (Agent L / Agent D): daily drivers re-homed off the
    /// player-store gate. These tests cover the pieces the Roslyn
    /// harness can compile: the household reserve/heating resolution
    /// the new SimulationDrivers.DriveHouseholdReserveDay calls per
    /// household (F-L1), the fresh demand snapshot it builds (F-L1),
    /// the NPC daily sales cadence primitive DriveNpcDailySalesCadence
    /// begins on the NPC runtime (D-5), and the weekly upgrade
    /// settlement effects the shared weekly pass now owns (F-L3).
    /// The drives themselves live in SimulationDrivers /
    /// GeneralStoreRuntimeManager / SharedBusinessRuntimeManager, which
    /// are harness-excluded per campaign precedent — those files are
    /// ParseCheck-verified only, with Unity compilation proof owed
    /// from Codex. No test claims Unity compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class Lane9bDailyDriversTests
    {
        private static HouseholdState MakeHousehold(int id, int fuelUnits)
        {
            var household = new HouseholdState { id = id };
            household.EnsureHouseholdReservesInitialized();
            household.GetReserve("fuel_wood").currentUnits = fuelUnits;
            return household;
        }

        [Test]
        public void FuelBurnsOnConsecutiveDays_AndIsDayKeyed()
        {
            // F-L1: the heating burn the new daily drive runs. Winter
            // day from the same anchor the B3 tests use (Jan 306 /
            // Dec 15 1880); fuel must fall on day N and again on day
            // N+1, never twice on the same day — the day key is what
            // makes the new drive safe alongside a player-store
            // ResolveDailySales later the same day.
            HouseholdState household = MakeHousehold(91, 10);
            int winterDay = CalendarAnchor.Default.ToAbsoluteDay(1880, 12, 15);

            int usedDayOne = HouseholdReserveEvaluator.ResolveDailyUse(household, winterDay);

            Assert.Greater(usedDayOne, 0, "Winter daily use must burn fuel on day one.");
            Assert.AreEqual(winterDay, household.lastReserveDepletionDayIndex,
                "The drive must stamp the day it resolved.");
            int fuelAfterDayOne = household.GetReserve("fuel_wood").currentUnits;
            Assert.Less(fuelAfterDayOne, 10, "Heating consumes from the fuel reserve.");

            int usedSameDayAgain = HouseholdReserveEvaluator.ResolveDailyUse(household, winterDay);

            Assert.AreEqual(0, usedSameDayAgain, "A second resolution the same day must burn nothing.");
            Assert.AreEqual(fuelAfterDayOne, household.GetReserve("fuel_wood").currentUnits,
                "A second resolution the same day must not move the reserve.");

            int usedDayTwo = HouseholdReserveEvaluator.ResolveDailyUse(household, winterDay + 1);

            Assert.Greater(usedDayTwo, 0, "Fuel must burn again on the next day.");
            Assert.AreEqual(winterDay + 1, household.lastReserveDepletionDayIndex);
            Assert.Less(household.GetReserve("fuel_wood").currentUnits, fuelAfterDayOne,
                "The next day's burn must deplete the reserve further.");
        }

        [Test]
        public void DemandSnapshotIsFreshAfterTheDailyBurn()
        {
            // F-L1: the snapshot the new drive builds into
            // household.demandSnapshot must reflect the post-burn
            // reserves, not yesterday's. One unit of fuel burns away
            // entirely on the winter day, leaving an empty fuel reserve
            // the snapshot must report as heating need.
            HouseholdState household = MakeHousehold(92, 1);
            int winterDay = CalendarAnchor.Default.ToAbsoluteDay(1880, 12, 15);

            HouseholdReserveEvaluator.ResolveDailyUse(household, winterDay);
            Assert.AreEqual(0, household.GetReserve("fuel_wood").currentUnits,
                "The single fuel unit must burn away on the winter day.");

            HouseholdDemandSnapshot snapshot = HouseholdReserveEvaluator.BuildDemandSnapshot(household);

            Assert.Greater(snapshot.heatingNeed, 0,
                "An empty fuel reserve after the daily burn must read as heating need in the fresh snapshot.");
        }

        [Test]
        public void NpcDailySalesCadence_ResetsDailyCountersOnly()
        {
            // D-5: the cadence primitive DriveNpcDailySalesCadence begins
            // on the NPC runtime. A day's embodied sales accumulate
            // against the runtime's daily counters; beginning the next
            // day's cadence must reset the daily counters and leave the
            // week-to-date totals alone, and a second begin must be
            // harmless.
            var runtime = new BusinessRuntimeState();
            runtime.RecordDailyServiceRevenue("staple_food", 2, 500);

            Assert.AreEqual(500, runtime.LastDailyRevenueCents, "The recorded sale must sit on the daily counter.");
            Assert.AreEqual(500, runtime.WeekToDateRevenueCents, "The recorded sale must sit on the weekly counter.");

            runtime.BeginDailySalesCadence();

            Assert.AreEqual(0, runtime.LastDailyRevenueCents, "The cadence must reset the daily revenue counter.");
            Assert.AreEqual(500, runtime.WeekToDateRevenueCents,
                "The cadence must not touch the week-to-date totals.");

            runtime.BeginDailySalesCadence();
            Assert.AreEqual(0, runtime.LastDailyRevenueCents, "Beginning the cadence twice must stay at zero.");
            Assert.AreEqual(500, runtime.WeekToDateRevenueCents);
        }

        [Test]
        public void WeeklyUpgradeEffects_FlowThroughLedgerWithoutAStore()
        {
            // F-L3: the weekly upgrade settlement the shared weekly pass
            // now owns. Same fixture values as the existing evaluator
            // test (ChickenCoop + KitchenGarden: 400c income, 115c
            // upkeep, 6u reserve production minus 1u spoilage), but
            // through the ledger the SBRM home passes — the wallet must
            // stay untouched when a ledger is authoritative.
            var household = new HouseholdState
            {
                id = 93,
                householdName = "Ward Household",
                spendingMoneyCents = 100,
                foodReserveUnits = 0,
            };
            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.BoarderRoom, 1));
            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.ChickenCoop, 2));
            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.KitchenGarden, 3));
            HouseholdReserveState stapleReserve = household.GetReserve(HouseholdReserveCatalog.StapleFoodCategoryId);
            Assert.NotNull(stapleReserve);
            stapleReserve.currentUnits = 0;
            household.SyncLegacyFoodReserveFromReserves();
            var ledger = new HouseholdLedger(93);

            HouseholdUpgradeEconomyEvaluator.ApplyWeeklySettlementEffects(household, ledger, 7);

            Assert.AreEqual(400, household.lastWeeklyUpgradeIncomeCents);
            Assert.AreEqual(115, household.lastWeeklyUpgradeUpkeepCents);
            Assert.AreEqual(285, ledger.GetBalanceCents(), "Income minus upkeep must land in the ledger.");
            Assert.AreEqual(100, household.spendingMoneyCents,
                "The legacy wallet must stay untouched when a ledger is passed.");
            Assert.AreEqual(5, household.foodReserveUnits, "6u production minus 1u spoilage.");
            Assert.AreEqual(5, household.lastWeeklyReserveDeltaUnits);
        }

        [Test]
        public void WeeklyUpgradeEffects_ApplySpoilageWithoutAStore()
        {
            // F-L3: spoilage is part of the moved weekly effect. With no
            // upgrades at all, a 3-unit staple reserve still loses 1 unit
            // a week — and a built root cellar prevents it.
            var household = new HouseholdState { id = 94, foodReserveUnits = 3 };
            HouseholdUpgradeEconomyEvaluator.ApplyWeeklySettlementEffects(household);

            Assert.AreEqual(2, household.foodReserveUnits, "One unit spoils per week without a root cellar.");
            Assert.AreEqual(-1, household.lastWeeklyReserveDeltaUnits);
            Assert.AreEqual(0, household.lastWeeklyUpgradeIncomeCents);
            Assert.AreEqual(0, household.lastWeeklyUpgradeUpkeepCents);

            var cellarHousehold = new HouseholdState { id = 95, foodReserveUnits = 3 };
            cellarHousehold.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.RootCellar, 1));
            HouseholdUpgradeEconomyEvaluator.ApplyWeeklySettlementEffects(cellarHousehold);

            Assert.AreEqual(3, cellarHousehold.foodReserveUnits, "A built root cellar must prevent spoilage.");
            Assert.AreEqual(0, cellarHousehold.lastWeeklyReserveDeltaUnits);
        }
    }
}
