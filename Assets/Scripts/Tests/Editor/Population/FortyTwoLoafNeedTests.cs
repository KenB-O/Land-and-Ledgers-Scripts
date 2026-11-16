// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F0 (expanded campaign 2026-11-16): the 42-loaf economics
// pin. No behaviour change — this test asserts the RULE, it does not
// change it. If it ever fails, the rule changed; that is a finding, not
// a tuning opportunity.
//
// The mechanism (plan section 3): H4 is the 2-member Ethan-Tia household.
// Expected daily bread use, computed by DriveHouseholdShortageMonitor
// exactly as this test computes it, is members x serving units x expected
// meals per day = 2 x 1 loaf x 3 meals = 6 loaves/day. The live serialized
// playtest policy is Low 3 days, Target 7 days, Fulfilled 7 days. With an
// empty larder (0 days covered), unitsNeeded = ceil((7 - 0) x 6) = 42 —
// exactly the observed day-1 purchase. The need reopens only when supply
// falls to the Low threshold (3 days), and a 7-day supply fulfills it.

using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class FortyTwoLoafNeedTests
    {
        // Live serialized playtest policy (SimulationDrivers
        // householdFoodReorderDays 3f / householdFoodTargetDays 7f /
        // householdFoodSatisfiedDays 7f), set explicitly so this pin does
        // not depend on the monitor's different defaults (7 / 7 / 14).
        private static HouseholdShortageMonitor LivePolicyMonitor(HouseholdNeedRegistry needs) =>
            new HouseholdShortageMonitor(needs)
            {
                LowDaysThreshold = 3f,
                TargetDaysThreshold = 7f,
                FulfilledDaysThreshold = 7f,
            };

        private static HouseholdState TwoMemberHousehold(int id)
        {
            var household = new HouseholdState { id = id };
            household.memberIds.Add(101);
            household.memberIds.Add(102);
            return household;
        }

        private static int DailyBreadUse(HouseholdState household)
        {
            // Exactly the driver's derivation (DriveHouseholdShortageMonitor):
            // members x serving units x expected meals per day.
            MealSchedulingPolicy policy = MealSchedulingPolicy.Default;
            return household.ConsumptionMemberCount
                * policy.GetServingUnits(HouseholdItemCatalog.BreadId)
                * policy.ExpectedMealsPerDay;
        }

        private static Dictionary<string, int> BreadUse(HouseholdState household) =>
            new Dictionary<string, int>
            {
                { HouseholdItemCatalog.BreadId, DailyBreadUse(household) },
            };

        private static HouseholdSupplyAccess SupplyAccess() =>
            new HouseholdSupplyAccess
            {
                HasGeneralStore = true,
                HasCash = true,
            };

        [Test]
        public void EmptyLarder_TwoMembers_BreadNeedIsExactlyFortyTwo()
        {
            HouseholdState household = TwoMemberHousehold(9101);
            Assert.AreEqual(2, household.ConsumptionMemberCount);
            // 2 members x 1 loaf per meal x 3 meals per day = 6 loaves/day.
            Assert.AreEqual(6, DailyBreadUse(household));

            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9101);
            HouseholdShortageMonitor monitor = LivePolicyMonitor(needs);
            monitor.Evaluate(
                household, inventory, BreadUse(household),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0);

            HouseholdPurchasingNeed need =
                needs.GetOpenNeed(9101, HouseholdItemCatalog.BreadId);
            Assert.IsTrue(need != null, "An empty larder must open a bread need.");
            Assert.AreEqual(42, need.UnitsNeeded);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
        }

        [Test]
        public void SevenDaySupply_FulfillsTheOpenNeed()
        {
            HouseholdState household = TwoMemberHousehold(9102);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9102);
            HouseholdShortageMonitor monitor = LivePolicyMonitor(needs);

            monitor.Evaluate(
                household, inventory, BreadUse(household),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0);
            HouseholdPurchasingNeed need =
                needs.GetOpenNeed(9102, HouseholdItemCatalog.BreadId);
            Assert.IsTrue(need != null, "An empty larder must open a bread need.");
            Assert.AreEqual(42, need.UnitsNeeded);

            // 42 loaves at 6/day is exactly 7 days: the Fulfilled threshold.
            Assert.IsNull(inventory.AddLot("bread", 42, 0, "f0-test-source"));
            monitor.Evaluate(
                household, inventory, BreadUse(household),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 1);
            Assert.IsTrue(
                needs.GetOpenNeed(9102, HouseholdItemCatalog.BreadId) == null,
                "A 7-day supply must close the bread need.");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void SupplyAboveLowThreshold_OpensNoNeed()
        {
            // 24 loaves at 6/day is 4 days: above the 3-day Low threshold,
            // below the 7-day Fulfilled threshold — adequate for now, and
            // no need opens.
            HouseholdState household = TwoMemberHousehold(9103);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9103);
            Assert.IsNull(inventory.AddLot("bread", 24, 0, "f0-test-source"));

            LivePolicyMonitor(needs).Evaluate(
                household, inventory, BreadUse(household),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0);
            Assert.IsTrue(
                needs.GetOpenNeed(9103, HouseholdItemCatalog.BreadId) == null,
                "4 days of bread must not open a need.");
        }

        [Test]
        public void SupplyAtLowThreshold_ReopensNeedAtTwentyFour()
        {
            // 18 loaves at 6/day is exactly 3 days: at the Low threshold,
            // so the need opens for the remaining gap to the 7-day target:
            // ceil((7 - 3) x 6) = 24.
            HouseholdState household = TwoMemberHousehold(9104);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9104);
            Assert.IsNull(inventory.AddLot("bread", 18, 0, "f0-test-source"));

            LivePolicyMonitor(needs).Evaluate(
                household, inventory, BreadUse(household),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0);
            HouseholdPurchasingNeed need =
                needs.GetOpenNeed(9104, HouseholdItemCatalog.BreadId);
            Assert.IsTrue(need != null, "3 days of bread must open a need.");
            Assert.AreEqual(24, need.UnitsNeeded);
        }
    }
}
