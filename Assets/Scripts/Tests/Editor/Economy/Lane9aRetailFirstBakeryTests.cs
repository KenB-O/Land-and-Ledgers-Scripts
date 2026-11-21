using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Lane 9a (§19 retail-first bakery + staple_food SKU identity):
    /// the focused tests for the lane — the retail-first policy
    /// default, staged-batch gating on retail demand net of due
    /// wholesale, item-aware capacity refusals, SKU-identity
    /// consumption, and missed-meal classification. The (1)+(2)
    /// no-account-when-empty test lives in
    /// BakeryWholesaleAccountConfigTests (its closure needs
    /// BusinessInstanceState, which explodes the harness closure
    /// through the GenericBusiness subsystem and the SaveDtos hub,
    /// so it is ParseCheck-verified, not harness-run). Unity
    /// compilation, Test Runner, PlayMode, and live behaviour are
    /// NOT claimed; live behaviour is unverified until Codex runs
    /// the 5–10 day 10x run.
    /// </summary>
    [TestFixture]
    public sealed class Lane9aRetailFirstBakeryTests
    {
        private const string BakeryInstanceId = "test-bakery-1";

        // ---- (3): the retail-first policy default ----

        [Test]
        public void SupplyProtection_DefaultIsProtectRetailCounter_Everywhere()
        {
            // Lane 9a owner decision (Nov 20, 2026): Canon §7.3E gives no
            // default, so the code default IS the decision. The field
            // initializer, the setter coercion of Unspecified, and the
            // save DTO default all agree — retail first.
            var runtime = new BakeryShopRuntime(BakeryInstanceId);
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, runtime.SupplyProtection,
                "a new runtime defaults to ProtectRetailCounter");

            runtime.SupplyProtection = BakerySupplyProtectionPolicy.Unspecified;
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, runtime.SupplyProtection,
                "the setter coerces Unspecified to the retail-first default");

            var dto = new BakeryShopRuntime.BakeryShopRuntimeSaveDto();
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, dto.SupplyProtection,
                "the DTO default is only a deserialization fallback, and it agrees");

            // A recorded policy survives a save/load round trip — the
            // default never overwrites a decision.
            runtime.SupplyProtection = BakerySupplyProtectionPolicy.ProtectWholesaleContracts;
            BakeryShopRuntime.BakeryShopRuntimeSaveDto saved = runtime.CaptureSaveDto();
            var restored = new BakeryShopRuntime(BakeryInstanceId);
            restored.LoadFromSaveDto(saved);
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectWholesaleContracts, restored.SupplyProtection,
                "a persisted policy is restored, not reset to the default");
        }

        // ---- (4): staged-batch gating nets due-today wholesale ----

        [Test]
        public void RetailUnitsNetOfDueWholesale_NetsDueTodayOnly()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            // 24 fresh loaves on the shelf; no account, nothing due.
            Assert.AreEqual(24, runtime.RetailUnitsNetOfDueWholesale(BakeryBreadCatalog.BreadLoafId, 300, diag),
                "with nothing due, the whole shelf is retail-available");

            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(units: 24, dueDay: 300), diag));
            Assert.AreEqual(24, runtime.WholesaleUnitsDueToday(BakeryBreadCatalog.BreadLoafId, 300),
                "the voluntary account's 24 loaves are due today");
            Assert.AreEqual(0, runtime.RetailUnitsNetOfDueWholesale(BakeryBreadCatalog.BreadLoafId, 300, diag),
                "due-today wholesale is netted out of the retail figure the staged gate compares against open need");

            // The same account, due tomorrow, nets nothing today.
            Assert.AreEqual(24, runtime.RetailUnitsNetOfDueWholesale(BakeryBreadCatalog.BreadLoafId, 299, diag),
                "a delivery due tomorrow does not shrink today's retail figure");
        }

        [Test]
        public void RetailUnitsNetOfDueWholesale_DoesNotDoubleSubtractUnderContractsPolicy()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(units: 24, dueDay: 300), diag));
            runtime.SupplyProtection = BakerySupplyProtectionPolicy.ProtectWholesaleContracts;

            Assert.AreEqual(0, runtime.RetailUnitsAvailableForSale(BakeryBreadCatalog.BreadLoafId, 300, diag),
                "under ProtectWholesaleContracts the allowance already nets due commitments");
            Assert.AreEqual(0, runtime.RetailUnitsNetOfDueWholesale(BakeryBreadCatalog.BreadLoafId, 300, diag),
                "netting again would double-subtract; the allowance stands as-is");
        }

        // ---- (6): item-aware capacity refusal ----

        [Test]
        public void StockShelfToLive_ZeroCapacityRefusalNamesOccupyingItems()
        {
            // A10: a zero-capacity refusal names the category target,
            // current stock and the occupying items, so a refused
            // stocking says whether bread is blocked by flour or by
            // bread itself.
            var diag = new List<string>();
            var catalog = new Pass3StoreCatalog();
            Assert.IsTrue(catalog.AddProduct(new Pass3ProductLine
            {
                ProductId = "bread", DisplayName = "Bread", PriceCents = 5,
            }, diag));
            catalog.Find("bread").ShelfUnits = 24;

            var port = new FullCategoryPort();
            Review2StockingResult result = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                catalog, "bread", 24, "staple_food", port, diag, "loaf", "lot-1", 2, 48);

            Assert.IsFalse(result.Stocked, "nothing stocks into a full category");
            Assert.AreEqual(24, catalog.Find("bread").ShelfUnits, "a refused stocking loses nothing");
            StringAssert.Contains("target 100u", result.Refusal, "refusal: " + result.Refusal);
            StringAssert.Contains("current 100u", result.Refusal, "refusal: " + result.Refusal);
            StringAssert.Contains("flour 60u", result.Refusal, "refusal: " + result.Refusal);
            StringAssert.Contains("bread 40u", result.Refusal, "refusal: " + result.Refusal);
            StringAssert.Contains("24u 'bread' remain on the catalog shelf", result.Refusal, "refusal: " + result.Refusal);
        }

        // ---- (7)+(8): SKU identity and missed-meal classification ----

        [Test]
        public void GenericOnlyLarder_ServesNoMeals_AndMissedMealsAreClassified()
        {
            // A8: generic unspecified staple is never a servable meal
            // item — a loaf is a loaf. A5: a missed meal with an empty
            // larder is classified NoFoodInLarder, not generic.
            var household = new HouseholdState { id = 901 };
            household.memberIds.Add(1);
            household.EnsureHouseholdReservesInitialized(fillMissingToTarget: true);
            var inventory = new HouseholdInventory(household.id);
            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 0, new List<string>());

            var mealLog = new HouseholdMealLogRegistry();
            var meals = new HouseholdMealService(mealLog);
            MealDayResult result = meals.ServeHouseholdDay(
                household, new List<int>(household.memberIds), inventory, MealSchedulingPolicy.Default, 0, null);

            Assert.AreEqual(0, result.MealsServed, "generic stock serves no meal");
            Assert.AreEqual(3, result.MealsMissed);
            foreach (MissedMealRecord missed in mealLog.GetMissedMeals(household.id))
            {
                Assert.AreEqual(MissedMealReasonKind.NoFoodInLarder, missed.ReasonKind,
                    "a missed meal with no servable food is classified, not generic");
            }
        }

        [Test]
        public void LegacyFlaggedLot_CannotBeServedUntilIdentified()
        {
            // A8: a legacy save's historical staple reserve seeds as
            // flagged unidentified stock — the quantity is preserved,
            // but it cannot be served as a meal until identified, never
            // silently converted to an arbitrary product. (Unspecified
            // meat stays edible by design — a pinned PhaseB behaviour —
            // so the meat reserve is zeroed here to isolate the staple.)
            var household = new HouseholdState { id = 902 };
            household.memberIds.Add(1);
            household.EnsureHouseholdReservesInitialized(fillMissingToTarget: true);
            household.GetReserve("meat").currentUnits = 0;
            var inventory = new HouseholdInventory(household.id);
            HouseholdInventoryReserveBridge.SeedLotsFromReserves(
                household, inventory, 0, new List<string>(), fromLegacySave: true);

            Assert.Greater(inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedStapleFoodId), 0,
                "the legacy quantity is preserved as a lot");
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedMeatId));
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (string.Equals(lot.ItemId, HouseholdItemCatalog.UnspecifiedStapleFoodId, StringComparison.Ordinal))
                    Assert.IsTrue(lot.IsUnidentifiedHistoricalStock, "the lot is flagged unidentified");
            }

            var mealLog = new HouseholdMealLogRegistry();
            var meals = new HouseholdMealService(mealLog);
            MealDayResult result = meals.ServeHouseholdDay(
                household, new List<int>(household.memberIds), inventory, MealSchedulingPolicy.Default, 0, null);
            Assert.AreEqual(0, result.MealsServed, "unidentified historical stock cannot be served");
            Assert.AreEqual(3, result.MealsMissed);
            foreach (MissedMealRecord missed in mealLog.GetMissedMeals(household.id))
                Assert.AreEqual(MissedMealReasonKind.NoFoodInLarder, missed.ReasonKind);
        }

        [Test]
        public void IdentifiedBread_ServesMeals_AndMissedMealClassifierAgreesWithInventory()
        {
            var household = new HouseholdState { id = 903 };
            household.memberIds.Add(1);
            var inventory = new HouseholdInventory(household.id);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 3, 0, "actual household purchase"));

            Assert.AreEqual(MissedMealReasonKind.Unknown,
                HouseholdMealService.ClassifyMissedMealReason(inventory, MealSchedulingPolicy.Default),
                "inventory evidence alone cannot classify a miss when servable food is present");
            Assert.AreEqual(MissedMealReasonKind.NoFoodInLarder,
                HouseholdMealService.ClassifyMissedMealReason(new HouseholdInventory(household.id), MealSchedulingPolicy.Default),
                "an empty larder classifies as NoFoodInLarder");

            var mealLog = new HouseholdMealLogRegistry();
            var meals = new HouseholdMealService(mealLog);
            MealDayResult result = meals.ServeHouseholdDay(
                household, new List<int>(household.memberIds), inventory, MealSchedulingPolicy.Default, 0, null);
            Assert.AreEqual(3, result.MealsServed, "three loaves serve breakfast, lunch and supper");
            Assert.AreEqual(0, result.MealsMissed);
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));
        }

        [Test]
        public void MissedMealSlot_ExplicitReasonWins_AndGenericOverrideKeepsItsTextWithoutFood()
        {
            // A5: a caller holding stronger evidence stamps its kind
            // explicitly; a generic close-time reason without food keeps
            // its text and classifies NoFoodInLarder. A missedReasonOverride
            // IS the late close — serving is not attempted once the
            // caller closes the slot — and the inventory check then
            // classifies it: food in the larder means the window passed
            // (SchedulingWindowMissed), never a retroactive serving.
            var household = new HouseholdState { id = 904 };
            household.memberIds.Add(1);

            var emptyInventory = new HouseholdInventory(household.id);
            var emptyLog = new HouseholdMealLogRegistry();
            var emptyMeals = new HouseholdMealService(emptyLog);
            Assert.IsTrue(emptyMeals.ServeHouseholdMealSlot(
                household, 1, emptyInventory, MealSchedulingPolicy.Default, 0, MealSlot.Morning,
                missedReasonOverride: "No meal was served.",
                missedReasonKind: MissedMealReasonKind.NoShopperAvailable));
            MissedMealRecord stamped = emptyLog.GetMissedMeals(household.id)[0];
            Assert.AreEqual(MissedMealReasonKind.NoShopperAvailable, stamped.ReasonKind,
                "the caller's explicit evidence wins over the inventory-only classification");

            var overrideLog = new HouseholdMealLogRegistry();
            var overrideMeals = new HouseholdMealService(overrideLog);
            Assert.IsTrue(overrideMeals.ServeHouseholdMealSlot(
                household, 1, new HouseholdInventory(household.id), MealSchedulingPolicy.Default, 0, MealSlot.Midday,
                missedReasonOverride: "No meal was served."));
            MissedMealRecord generic = overrideLog.GetMissedMeals(household.id)[0];
            Assert.AreEqual(MissedMealReasonKind.NoFoodInLarder, generic.ReasonKind);
            Assert.AreEqual("No meal was served.", generic.Reason,
                "without food the generic close-time reason is kept, not replaced");

            var stockedInventory = new HouseholdInventory(household.id);
            Assert.IsNull(stockedInventory.AddLot(HouseholdItemCatalog.BreadId, 1, 0, "actual household purchase"));
            var stockedLog = new HouseholdMealLogRegistry();
            var stockedMeals = new HouseholdMealService(stockedLog);
            Assert.IsTrue(stockedMeals.ServeHouseholdMealSlot(
                household, 1, stockedInventory, MealSchedulingPolicy.Default, 0, MealSlot.Evening,
                missedReasonOverride: "No meal was served."));
            Assert.AreEqual(0, stockedLog.GetMeals(household.id).Count,
                "a late close never serves retroactively, even with food in the larder");
            Assert.AreEqual(1, stockedLog.GetMissedMeals(household.id).Count);
            MissedMealRecord lateClose = stockedLog.GetMissedMeals(household.id)[0];
            Assert.AreEqual(MissedMealReasonKind.SchedulingWindowMissed, lateClose.ReasonKind,
                "food in the larder at a late close means the window passed, not the larder");
            Assert.AreEqual(1, stockedInventory.GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "the loaf is untouched by the missed meal");
        }

        // ---- fixtures ----

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BakeryShopRuntime StockedShop(List<string> diag, int day = 290)
        {
            var runtime = new BakeryShopRuntime(BakeryInstanceId);
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, day, diag);
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            Assert.IsNull(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            return runtime;
        }

        private static int WorkDay(BakeryShopRuntime runtime, int day, List<string> diag)
        {
            return runtime.WorkDay(day, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, new EntityIdRegistry());
        }

        private static BakeryWholesaleAccount BoardingHouseAccount(int units = 24, int dueDay = 300)
        {
            return new BakeryWholesaleAccount
            {
                BuyerBusinessId = "boardinghouse-biz-1",
                BuyerType = BusinessType.BoardingHouse,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = units,
                DeliveryCadenceDays = 1,
                NextDeliveryDayIndex = dueDay,
                PricePerUnitCents = 6, // wholesale under the 8c retail price
            };
        }

        /// <summary>Minimal item-level port whose staple_food category is
        /// full: target 100, current 100, flour 60u + bread 40u.</summary>
        private sealed class FullCategoryPort : IItemLevelGeneralStoreTradingPort
        {
            public string StoreBusinessId => "test-store-1";
            public string StoreName => "General Store";
            public string LocationId => "store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => new[]
            {
                new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { "flour", "bread" },
                    UsesItemLevelInventory = true,
                },
            };
            public int CategoryStockUnits(string categoryId) => 100;
            public int CategoryPricePerUnitCents(string categoryId) => 5;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => 5000;
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose) => "test port spends nothing";
            public string ReceiveStock(string categoryId, int units, string provenanceLabel) => "test port receives nothing";
            public int CategoryReceivableUnits(string categoryId) => 0;
            public int CategoryTargetStockUnits(string categoryId) => 100;
            public bool OffersDelivery => false;
            public int DeliveryFeeCents(string itemId, int units) => 0;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;
            public bool UsesItemLevelInventory(string itemId) => true;
            public int ItemStockUnits(string itemId) => string.Equals(itemId, "flour", StringComparison.Ordinal) ? 60 : 40;
            public int ItemPricePerUnitCents(string itemId) => 5;
            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }
            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
        }
    }
}
