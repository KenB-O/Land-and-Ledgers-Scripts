// CAMPAIGN-B (DEF-053), package 12: reserve-to-lot seed duplicates food —
// verify-then-fix. VERDICT: REFUTED. The seed does NOT duplicate food, so NO
// production file is changed by this package (except the E-20 comment merge
// in GeneralStoreRuntimeManager.cs, a documentation-only change on a file
// this lane already owns — see the checkpoint report).
//
// The defect's framing treats the reserve as a separate stockpile that the
// seed copies out of: SeedCategory reads reserve.currentUnits, calls AddLot,
// never decrements the reserve. That reading is wrong. The reserve is a
// LEGACY PLANNING MIRROR of the lots, not a second stockpile:
//   - HouseholdInventoryReserveBridge.cs (class doc): "LOTS are the
//     consumption truth. Reserve category units are a legacy planning
//     mirror until Phase C replaces the category-based shopping loop."
//   - Every mirror operation moves reserve and lots TOGETHER, by the same
//     amount: credit (ApplyReservePurchase + MirrorReserveCreditToLots,
//     loop receipt + MirrorLotReceiptToReserve) adds to both; consumption
//     (meal + MirrorLotConsumptionToReserve) subtracts from both.
//   - The AUD-335 repair (HouseholdShoppingLoop.cs:973) credits the reserve
//     on a loop receipt "so the reserve deficit closes and the GSRM path
//     does not re-buy the same food."
//   - Existing test Bridge_CategoryCreditsDoNotInventSpecificPooledSkuIdentity
//     pins: "Lot consumption updates the corresponding legacy reserve
//     mirror exactly once."
//
// The seed ESTABLISHES the mirror: before it, the household's food is R=N
// with no lots; after it, R=N and L=N — two consistent views of the same N
// units. No live reader sums reserve + lots (verified repo-wide: no
// GetAvailableUnits/GetTotalUnits call in HouseholdReserves.cs or
// HouseholdState.cs, and no lot reader in ReadModels/ or Domains/Economy/),
// so the "duplication" has no observable double-count. Meals read lots
// only; shopping needs and heating read the reserve only.
//
// The briefed fix (decrement the reserve at seed) would BREAK the mirror:
// R=0 while L=N. The next ResolveDailySales would see a full-target
// staple/meat deficit at high urgency and buy food the household already
// holds in lots — the exact AUD-335 rebuying defect, reintroduced at seed
// time. Test ZeroedReserveAfterSeedWouldRebuyFoodAlreadyHeld pins that
// mechanism on the unchanged tree. The fuel half is correctly excluded
// from the decrement for a second, independent reason: heating reads the
// fuel reserve directly (HouseholdHeatingEvaluator.ResolveDailyHeatingUse)
// and nothing consumes fuel lots, so decrementing the fuel reserve would
// strand seeded fuel in lots and starve heating immediately.
//
// Residuals (not fixed here, not this defect):
//   - ResolveDailyUse drains the food mirror without touching lots, so the
//     mirror understates lots over time and the GSRM loop over-buys. That
//     is the Check 6 legacy-loop / Phase C-loop overlap, not the seed.
//   - The seed's idempotency rests entirely on SeededFromReserves, which
//     IS persisted in the inventory save DTO (HouseholdInventory.cs:844),
//     so a re-seed on load cannot duplicate. If that flag were ever lost,
//     a second seed would add lots again — the flag is the only guard.
//   - DEF-010 domestic-labour triggers are hard-coded off in production
//     (SimulationDrivers.cs:3481/3485/3489), confirmed, untouched.

using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    public sealed class CampaignBReserveSeedMirrorTests
    {
        [Test]
        public void SeedEstablishesReserveMirror()
        {
            HouseholdState household = new HouseholdState { id = 71 };
            household.EnsureHouseholdReservesInitialized();
            household.GetReserve("staple_food").currentUnits = 8;
            household.GetReserve("meat").currentUnits = 3;
            household.GetReserve("fuel_wood").currentUnits = 5;
            HouseholdInventory inventory = new HouseholdInventory(71);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 9, new List<string>(), fromLegacySave: true);

            Assert.AreEqual(8, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedStapleFoodId),
                "Seeded staple lots must hold the reserve units.");
            Assert.AreEqual(3, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedMeatId),
                "Seeded meat lots must hold the reserve units.");
            Assert.AreEqual(5, inventory.GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "Seeded fuel lots must hold the reserve units.");
            Assert.AreEqual(8, household.GetReserve("staple_food").currentUnits,
                "The seed must NOT decrement the staple reserve: the reserve is the legacy mirror of the lots, not a second stockpile.");
            Assert.AreEqual(3, household.GetReserve("meat").currentUnits,
                "The seed must NOT decrement the meat reserve.");
            Assert.AreEqual(5, household.GetReserve("fuel_wood").currentUnits,
                "The seed must NOT decrement the fuel reserve.");
            Assert.IsTrue(inventory.State.SeededFromReserves);
        }

        [Test]
        public void SeededHouseholdAtTargetHasNoStapleOrMeatNeed()
        {
            HouseholdState household = new HouseholdState { id = 72 };
            household.EnsureHouseholdReservesInitialized();
            HouseholdReserveState staple = household.GetReserve("staple_food");
            HouseholdReserveState meat = household.GetReserve("meat");
            staple.currentUnits = staple.targetUnits;
            meat.currentUnits = meat.targetUnits;
            HouseholdInventory inventory = new HouseholdInventory(72);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 9, new List<string>(), fromLegacySave: true);

            List<HouseholdReserveNeed> needs = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            Assert.IsFalse(FindNeed(needs, "staple_food").HasValue,
                "A household holding a full target of staple lots must not need to buy staple_food.");
            Assert.IsFalse(FindNeed(needs, "meat").HasValue,
                "A household holding a full target of meat lots must not need to buy meat.");
        }

        [Test]
        public void ZeroedReserveAfterSeedWouldRebuyFoodAlreadyHeld()
        {
            // Counterfactual pin for the refutation: this is exactly what the
            // briefed seed-decrement would do to the mirror — and the GSRM
            // loop answers by buying food the household already holds.
            HouseholdState household = new HouseholdState { id = 73 };
            household.EnsureHouseholdReservesInitialized();
            HouseholdReserveState staple = household.GetReserve("staple_food");
            int target = staple.targetUnits;
            Assert.Greater(target, 0, "The fixture household must have a staple target.");
            staple.currentUnits = target;
            HouseholdInventory inventory = new HouseholdInventory(73);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 9, new List<string>(), fromLegacySave: true);
            Assert.AreEqual(target, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedStapleFoodId),
                "The household holds a full target of staple lots from the seed.");

            staple.currentUnits = 0;

            List<HouseholdReserveNeed> needs = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            HouseholdReserveNeed? stapleNeed = FindNeed(needs, "staple_food");
            Assert.IsTrue(stapleNeed.HasValue,
                "Zeroing the reserve after seeding opens a staple_food need — the decrement would trigger rebuying.");
            Assert.Greater(stapleNeed.Value.PurchaseUnits, 0);
            Assert.GreaterOrEqual(stapleNeed.Value.Urgency01, 0.72f,
                "At an empty reserve the need is urgent enough to shop today, so the rebuy would fire on the next ResolveDailySales.");
            Assert.AreEqual(target, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedStapleFoodId),
                "The lots still hold the full target while the need asks to buy more: the same food, bought twice.");
        }

        [Test]
        public void MealConsumptionAfterSeedMirrorsReserveExactlyOnce()
        {
            HouseholdState household = new HouseholdState { id = 74 };
            household.EnsureHouseholdReservesInitialized();
            household.GetReserve("staple_food").currentUnits = 8;
            // Meat is zeroed on purpose: the meal priority list serves
            // unspecified meat before unspecified staple, so a meat lot
            // would be eaten first and the staple mirror would not move.
            household.GetReserve("meat").currentUnits = 0;
            HouseholdInventory inventory = new HouseholdInventory(74);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 9, new List<string>(), fromLegacySave: true);

            HouseholdMealService mealService = new HouseholdMealService(new HouseholdMealLogRegistry());
            MealDayResult meal = mealService.ServeHouseholdDay(
                household,
                new List<int> { 1 },
                inventory,
                new MealSchedulingPolicy
                {
                    SubstantialMealSlots = new List<MealSlot> { MealSlot.Morning },
                    MiddayMealEnabled = false,
                },
                10,
                null);

            Assert.AreEqual(1, meal.MealsServed, "One member x one substantial slot serves one meal.");
            Assert.AreEqual(6, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedStapleFoodId),
                "The meal consumes one staple serving (2 units) from the staple lots.");
            Assert.AreEqual(6, household.GetReserve("staple_food").currentUnits,
                "Lot consumption updates the corresponding legacy reserve mirror exactly once — the mirror stays in step after seeding.");
        }

        [Test]
        public void SecondSeedAfterPartialConsumptionAddsNothing()
        {
            HouseholdState household = new HouseholdState { id = 75 };
            household.EnsureHouseholdReservesInitialized();
            household.GetReserve("staple_food").currentUnits = 8;
            // Meat is zeroed on purpose (see the meal-mirror test above):
            // the meal must eat the unspecified staple, not the meat.
            household.GetReserve("meat").currentUnits = 0;
            HouseholdInventory inventory = new HouseholdInventory(75);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 9, new List<string>(), fromLegacySave: true);

            HouseholdMealService mealService = new HouseholdMealService(new HouseholdMealLogRegistry());
            mealService.ServeHouseholdDay(
                household,
                new List<int> { 1 },
                inventory,
                new MealSchedulingPolicy
                {
                    SubstantialMealSlots = new List<MealSlot> { MealSlot.Morning },
                    MiddayMealEnabled = false,
                },
                10,
                null);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 11, new List<string>(), fromLegacySave: true);

            Assert.AreEqual(6, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedStapleFoodId),
                "A second seed after consumption adds nothing: idempotency holds through the flag, not through the reserve balance.");
            Assert.AreEqual(6, household.GetReserve("staple_food").currentUnits);
            Assert.AreEqual(1, inventory.Lots.Count, "No duplicate lots: one staple lot only (meat and fuel were zero).");
        }

        [Test]
        public void FuelSeedLeavesHeatingReserveIntactAndHeatingReadsReserve()
        {
            HouseholdState household = new HouseholdState { id = 76 };
            household.heatRetentionTier = HouseholdHeatRetentionTier.Drafty;
            household.EnsureHouseholdReservesInitialized();
            household.GetReserve("fuel_wood").currentUnits = 5;
            HouseholdInventory inventory = new HouseholdInventory(76);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 9, new List<string>(), fromLegacySave: true);

            Assert.AreEqual(5, household.GetReserve("fuel_wood").currentUnits,
                "The seed must leave the fuel reserve intact: heating reads the reserve, not the lots.");
            Assert.AreEqual(5, inventory.GetAvailableUnits(HouseholdItemCatalog.FuelWoodId));

            int winterDay = CalendarAnchor.Default.ToAbsoluteDay(1880, 12, 15);
            int used = HouseholdReserveEvaluator.ResolveDailyUse(household, winterDay);

            Assert.Greater(used, 0, "Winter daily use must consume something on this fixture.");
            Assert.Less(household.GetReserve("fuel_wood").currentUnits, 5,
                "Heating consumes from the fuel reserve.");
            Assert.AreEqual(5, inventory.GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "Nothing consumes fuel lots: a fuel-reserve decrement at seed would strand seeded fuel in lots and starve heating.");
        }

        [Test]
        public void ConsumptionMirrorClampsAtZero()
        {
            // The brief's safety premise, verified: the mirror clamps at 0,
            // so post-seed consumption can never drive the reserve negative.
            // It holds — but it does not license the decrement, which would
            // break the mirror the other way (reserve 0, lots full).
            HouseholdState household = new HouseholdState { id = 77 };
            household.EnsureHouseholdReservesInitialized();
            household.GetReserve("staple_food").currentUnits = 8;
            HouseholdInventory inventory = new HouseholdInventory(77);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 9, new List<string>(), fromLegacySave: true);
            household.GetReserve("staple_food").currentUnits = 0;

            HouseholdInventoryReserveBridge.MirrorLotConsumptionToReserve(
                household, HouseholdItemCatalog.UnspecifiedStapleFoodId, 3);

            Assert.AreEqual(0, household.GetReserve("staple_food").currentUnits,
                "The consumption mirror clamps at zero; it never drives the reserve negative.");
        }

        private static HouseholdReserveNeed? FindNeed(List<HouseholdReserveNeed> needs, string categoryId)
        {
            if (needs == null)
            {
                return null;
            }

            for (int i = 0; i < needs.Count; i++)
            {
                if (string.Equals(needs[i].CategoryId, categoryId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return needs[i];
                }
            }

            return null;
        }
    }
}
