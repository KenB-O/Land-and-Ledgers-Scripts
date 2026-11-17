// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent I1 (UBC campaign 2026-11-17): regression tests for finding I1-M1.
//
// The mechanism: HouseholdShortageMonitor.EvaluateItem sized every
// meal-priority item's need for the full target independently, while meals
// consume one item per serving, first available in priority order — so one
// household demanded one full diet per food item (about six times the food
// it eats) and stripped the store for every other household. With a
// meal-priority order supplied, needs are allocated in that order against
// one shared cover total: the top food covers the shortfall first, lower
// foods open only for what remains, and an unpurchasable top food does not
// block the fallback. Without a priority order, today's per-item behaviour
// is preserved exactly (last test pins it).

using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class HouseholdShortageAllocationTests
    {
        // Live serialized playtest policy (SimulationDrivers
        // householdFoodReorderDays 3f / householdFoodTargetDays 7f /
        // householdFoodSatisfiedDays 7f), as in FortyTwoLoafNeedTests.
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

        private static int DailyUse(HouseholdState household, string itemId)
        {
            // Exactly the driver's derivation (DriveHouseholdShortageMonitor):
            // members x serving units x expected meals per day.
            MealSchedulingPolicy policy = MealSchedulingPolicy.Default;
            return household.ConsumptionMemberCount
                * policy.GetServingUnits(itemId)
                * policy.ExpectedMealsPerDay;
        }

        private static Dictionary<string, int> FoodUse(HouseholdState household, params string[] itemIds)
        {
            var use = new Dictionary<string, int>();
            foreach (string itemId in itemIds)
                use[itemId] = DailyUse(household, itemId);
            return use;
        }

        private static HouseholdSupplyAccess SupplyAccess() =>
            new HouseholdSupplyAccess
            {
                HasGeneralStore = true,
                HasCash = true,
            };

        [Test]
        public void EmptyLarder_BreadFirst_BeefNeedNeverOpens()
        {
            HouseholdState household = TwoMemberHousehold(9201);
            // 2 members x 1 unit per meal x 3 meals = 6/day for bread and beef.
            Assert.AreEqual(6, DailyUse(household, HouseholdItemCatalog.BreadId));
            Assert.AreEqual(6, DailyUse(household, "beef"));

            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9201);
            LivePolicyMonitor(needs).Evaluate(
                household, inventory, FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                isRecognizedByKnownSeller: null,
                mealPriorityOrder: new List<string> { HouseholdItemCatalog.BreadId, "beef" },
                isPurchasableNow: itemId => true);

            HouseholdPurchasingNeed breadNeed =
                needs.GetOpenNeed(9201, HouseholdItemCatalog.BreadId);
            Assert.IsTrue(breadNeed != null, "An empty larder must open a bread need.");
            Assert.AreEqual(42, breadNeed.UnitsNeeded);
            Assert.IsTrue(
                needs.GetOpenNeed(9201, "beef") == null,
                "Bread alone covers the 7-day target, so no beef need may open.");
        }

        [Test]
        public void BreadUnpurchasable_BeefNeedOpensAsFallback()
        {
            HouseholdState household = TwoMemberHousehold(9202);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9202);
            LivePolicyMonitor(needs).Evaluate(
                household, inventory, FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                isRecognizedByKnownSeller: null,
                mealPriorityOrder: new List<string> { HouseholdItemCatalog.BreadId, "beef" },
                isPurchasableNow: itemId => !string.Equals(itemId, HouseholdItemCatalog.BreadId));

            HouseholdPurchasingNeed breadNeed =
                needs.GetOpenNeed(9202, HouseholdItemCatalog.BreadId);
            Assert.IsTrue(breadNeed != null, "Bread is still short, so its need stays open and observable.");
            Assert.AreEqual(42, breadNeed.UnitsNeeded);
            HouseholdPurchasingNeed beefNeed = needs.GetOpenNeed(9202, "beef");
            Assert.IsTrue(beefNeed != null, "Unpurchasable bread must not block the beef fallback.");
            Assert.AreEqual(42, beefNeed.UnitsNeeded);
        }

        [Test]
        public void ThreeDaysBreadHeld_BreadNeedTwentyFour_BeefNeedNeverOpens()
        {
            // 18 loaves at 6/day is exactly 3 days: at the Low threshold.
            HouseholdState household = TwoMemberHousehold(9203);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9203);
            Assert.IsNull(inventory.AddLot("bread", 18, 0, "i1-test-source"));

            LivePolicyMonitor(needs).Evaluate(
                household, inventory, FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                isRecognizedByKnownSeller: null,
                mealPriorityOrder: new List<string> { HouseholdItemCatalog.BreadId, "beef" },
                isPurchasableNow: itemId => true);

            HouseholdPurchasingNeed breadNeed =
                needs.GetOpenNeed(9203, HouseholdItemCatalog.BreadId);
            Assert.IsTrue(breadNeed != null, "3 days of bread must open a need.");
            Assert.AreEqual(24, breadNeed.UnitsNeeded);
            Assert.IsTrue(
                needs.GetOpenNeed(9203, "beef") == null,
                "3 held days plus the 4 planned bread days cover the target, so no beef need may open.");
        }

        [Test]
        public void SevenDaysBreadHeld_BothNeedsClosed()
        {
            HouseholdState household = TwoMemberHousehold(9204);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9204);
            Assert.IsNull(inventory.AddLot("bread", 42, 0, "i1-test-source"));

            LivePolicyMonitor(needs).Evaluate(
                household, inventory, FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                isRecognizedByKnownSeller: null,
                mealPriorityOrder: new List<string> { HouseholdItemCatalog.BreadId, "beef" },
                isPurchasableNow: itemId => true);

            Assert.IsTrue(
                needs.GetOpenNeed(9204, HouseholdItemCatalog.BreadId) == null,
                "A 7-day bread supply must close the bread need.");
            Assert.IsTrue(
                needs.GetOpenNeed(9204, "beef") == null,
                "A 7-day bread supply is 7 days of food, so no beef need may open.");
        }

        [Test]
        public void SevenDaysBeefHeld_NoBreadNeedOpens()
        {
            // The shared cover counts held stock of every priority food,
            // not just higher-priority ones: a larder full of beef is 7
            // days of food, so no bread need may open on top of it.
            HouseholdState household = TwoMemberHousehold(9208);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9208);
            Assert.IsNull(inventory.AddLot("beef", 42, 0, "i1-test-source"));

            LivePolicyMonitor(needs).Evaluate(
                household, inventory, FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                isRecognizedByKnownSeller: null,
                mealPriorityOrder: new List<string> { HouseholdItemCatalog.BreadId, "beef" },
                isPurchasableNow: itemId => true);

            Assert.IsTrue(
                needs.GetOpenNeed(9208, HouseholdItemCatalog.BreadId) == null,
                "7 days of beef is 7 days of food; the household must not demand 42 loaves on top of it.");
            Assert.IsTrue(
                needs.GetOpenNeed(9208, "beef") == null,
                "A 7-day beef supply must close the beef need.");
        }

        [Test]
        public void PriorityOrderRespected_BeefFirst()
        {
            HouseholdState household = TwoMemberHousehold(9205);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9205);
            LivePolicyMonitor(needs).Evaluate(
                household, inventory, FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                isRecognizedByKnownSeller: null,
                mealPriorityOrder: new List<string> { "beef", HouseholdItemCatalog.BreadId },
                isPurchasableNow: itemId => true);

            HouseholdPurchasingNeed beefNeed = needs.GetOpenNeed(9205, "beef");
            Assert.IsTrue(beefNeed != null, "The first-priority food must open a need.");
            Assert.AreEqual(42, beefNeed.UnitsNeeded);
            Assert.IsTrue(
                needs.GetOpenNeed(9205, HouseholdItemCatalog.BreadId) == null,
                "Beef first covers the target, so no bread need may open.");
        }

        [Test]
        public void BreadUnpurchasable_BeefFillsCover_FlourNeedNeverOpens()
        {
            // Chained fallback: bread cannot be bought, beef fills the
            // whole shortfall, flour (2 units per serving, 12/day) opens
            // nothing — the household demands one week's food, not three.
            HouseholdState household = TwoMemberHousehold(9206);
            Assert.AreEqual(12, DailyUse(household, HouseholdItemCatalog.FlourId));
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9206);
            LivePolicyMonitor(needs).Evaluate(
                household, inventory,
                FoodUse(household, HouseholdItemCatalog.BreadId, "beef", HouseholdItemCatalog.FlourId),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                isRecognizedByKnownSeller: null,
                mealPriorityOrder: new List<string>
                    { HouseholdItemCatalog.BreadId, "beef", HouseholdItemCatalog.FlourId },
                isPurchasableNow: itemId => !string.Equals(itemId, HouseholdItemCatalog.BreadId));

            Assert.AreEqual(42, needs.GetOpenNeed(9206, HouseholdItemCatalog.BreadId).UnitsNeeded);
            Assert.AreEqual(42, needs.GetOpenNeed(9206, "beef").UnitsNeeded);
            Assert.IsTrue(
                needs.GetOpenNeed(9206, HouseholdItemCatalog.FlourId) == null,
                "Beef already covers the target, so no flour need may open.");
        }

        [Test]
        public void NoPriorityOrder_TodayBehaviourPreserved()
        {
            // Without a priority order the monitor keeps today's per-item
            // sizing exactly: an empty larder opens a full-target need for
            // every food. This pins the opt-in boundary of the repair.
            HouseholdState household = TwoMemberHousehold(9207);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9207);
            LivePolicyMonitor(needs).Evaluate(
                household, inventory, FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0);

            Assert.AreEqual(42, needs.GetOpenNeed(9207, HouseholdItemCatalog.BreadId).UnitsNeeded);
            Assert.AreEqual(42, needs.GetOpenNeed(9207, "beef").UnitsNeeded);
        }
    }
}
