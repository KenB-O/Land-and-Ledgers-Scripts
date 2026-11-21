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

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class HouseholdShortageAllocationTests
    {
        private sealed class TestFoodSupplier : IItemResolvingSupplier, IItemCatalogSupplier,
            IOffersTradeCredit, IPackageUnitSupplier
        {
            public int Stock;
            public int UnitPriceCents = 1;
            public bool CreditAvailable;
            public int CreditLimitCents;
            public int PackageSize = 1;
            public string ItemId = HouseholdItemCatalog.BreadId;
            public string Id = "test-store";
            public string SupplierBusinessId => Id;
            public string SupplierName => "Test Store";
            public string LocationId => "test-store-location";
            public bool HasCategory(string categoryId) => categoryId == "staple_food";
            public int StockUnits(string categoryId) => Stock;
            public int PricePerUnitCents(string categoryId) => UnitPriceCents;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
            public bool OffersItem(string itemId) => RecognizesItem(itemId);
            public string CategoryForItem(string itemId) => RecognizesItem(itemId) ? "staple_food" : string.Empty;
            public int ItemStockUnits(string itemId) => RecognizesItem(itemId) ? Stock : 0;
            public int ItemPricePerUnitCents(string itemId) => UnitPriceCents;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public int SellItem(string itemId, int requestedUnits, int dayIndex,
                ShoppingSettlement settlement, List<string> diagnostics) => 0;
            public string LastReceiptId => string.Empty;
            public bool RecognizesItem(string itemId) => itemId == ItemId;
            public bool OffersTradeCredit => CreditAvailable;
            public int TradeCreditLimitCents => CreditLimitCents;
            public int HouseholdUnitsPerStoreUnit(string itemId) => PackageSize;
        }

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

        [Test]
        public void AttachRestoredNeed_MissingPersistedRowIsRestoredByStableIdentityOnce()
        {
            var registry = new HouseholdNeedRegistry();
            var persisted = new HouseholdPurchasingNeed
            {
                NeedSequence = 12,
                HouseholdId = 4,
                ItemId = HouseholdItemCatalog.BreadId,
                UnitsNeeded = 3,
                IsNecessity = true,
                Urgency01 = 0.75f,
                ReasonSummary = "mid-trip saved obligation",
                CreatedDayIndex = 8,
                LastEvaluatedDayIndex = 9,
                Status = PurchasingNeedStatus.Open,
            };

            HouseholdPurchasingNeed attached = registry.AttachRestoredNeed(persisted);
            HouseholdPurchasingNeed attachedAgain = registry.AttachRestoredNeed(persisted);

            Assert.NotNull(attached);
            Assert.AreSame(attached, attachedAgain);
            Assert.AreNotSame(persisted, attached);
            Assert.AreSame(attached, registry.GetOpenNeed(4, HouseholdItemCatalog.BreadId));
            Assert.AreEqual(3, attached.UnitsNeeded);
            Assert.AreEqual("mid-trip saved obligation", attached.ReasonSummary);
            Assert.AreEqual(13, registry.CreateNeed(5, HouseholdItemCatalog.BreadId, 10).NeedSequence,
                "sequence allocation must advance beyond the recovered identity");
        }

        [Test]
        public void AttachRestoredNeed_ExistingClosedIdentityIsNotReopenedOrReplaced()
        {
            var registry = new HouseholdNeedRegistry();
            var saved = new HouseholdPurchasingNeed
            {
                NeedSequence = 2,
                HouseholdId = 1,
                ItemId = HouseholdItemCatalog.BreadId,
                UnitsNeeded = 0,
                Status = PurchasingNeedStatus.Fulfilled,
            };
            registry.ImportState(new[] { saved });

            HouseholdPurchasingNeed attached = registry.AttachRestoredNeed(saved);

            Assert.NotNull(attached);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, attached.Status);
            Assert.IsNull(registry.GetOpenNeed(1, HouseholdItemCatalog.BreadId));
        }

        [Test]
        public void AttachRestoredNeed_ConflictingOpenIdentityFailsClosed()
        {
            var registry = new HouseholdNeedRegistry();
            HouseholdPurchasingNeed existing = registry.CreateNeed(1, HouseholdItemCatalog.BreadId, 2);
            var conflicting = new HouseholdPurchasingNeed
            {
                NeedSequence = existing.NeedSequence + 10,
                HouseholdId = 1,
                ItemId = HouseholdItemCatalog.BreadId,
                UnitsNeeded = 5,
                Status = PurchasingNeedStatus.Open,
            };

            Assert.IsNull(registry.AttachRestoredNeed(conflicting));
            Assert.AreSame(existing, registry.GetOpenNeed(1, HouseholdItemCatalog.BreadId));
            Assert.AreEqual(1, registry.GetOpenNeeds(1).Count);
            Assert.IsNotEmpty(registry.Diagnostics);
            Assert.AreEqual(11, registry.CreateNeed(2, HouseholdItemCatalog.PotatoesId, 3).NeedSequence,
                "a rejected but previously issued sequence must not be reused");
        }

        [Test]
        public void AttachRestoredNeed_SequenceReusedForDifferentIdentityFailsClosed()
        {
            var registry = new HouseholdNeedRegistry();
            HouseholdPurchasingNeed existing = registry.CreateNeed(1, HouseholdItemCatalog.BreadId, 2);
            var conflicting = new HouseholdPurchasingNeed
            {
                NeedSequence = existing.NeedSequence,
                HouseholdId = 2,
                ItemId = HouseholdItemCatalog.PotatoesId,
                UnitsNeeded = 1,
                Status = PurchasingNeedStatus.Open,
            };

            Assert.IsNull(registry.AttachRestoredNeed(conflicting));
            Assert.AreEqual(1, registry.GetOpenNeeds(1).Count);
            Assert.AreEqual(0, registry.GetOpenNeeds(2).Count);
            Assert.IsNotEmpty(registry.Diagnostics);
        }

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
        public void PartialBreadStock_DoesNotCountAsFullCoverForFallbackFood()
        {
            HouseholdState household = TwoMemberHousehold(9210);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9210);
            int requestedBreadUnits = 0;

            LivePolicyMonitor(needs).Evaluate(
                household, inventory, FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                isRecognizedByKnownSeller: null,
                mealPriorityOrder: new List<string> { HouseholdItemCatalog.BreadId, "beef" },
                isPurchasableNow: itemId => true,
                allocateSupplyUnitsNow: (itemId, units) =>
                {
                    if (itemId == HouseholdItemCatalog.BreadId)
                    {
                        requestedBreadUnits = units;
                        return Math.Min(12, units);
                    }

                    return 0;
                });

            Assert.AreEqual(42, requestedBreadUnits,
                "The supplier check must be against the complete actual need, not any positive stock.");
            Assert.IsTrue(needs.GetOpenNeed(9210, HouseholdItemCatalog.BreadId) != null,
                "A partial bread need remains real and observable.");
            Assert.AreEqual(30, needs.GetOpenNeed(9210, "beef").UnitsNeeded,
                "Twelve available bread units cover two days; alternative food fills the remaining five days.");
        }

        [Test]
        public void SharedKnownSellerStock_IsAllocatedAcrossHouseholdsOnlyOnce()
        {
            var supplier = new TestFoodSupplier { Stock = 51 };
            var directory = new SupplierDirectory();
            directory.Register(supplier);
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(101, supplier.SupplierBusinessId, "same town", 0);
            knowledge.LearnSupplier(102, supplier.SupplierBusinessId, "same town", 0);
            var budget = new HouseholdFoodSupplyBudget(directory, knowledge);

            Assert.AreEqual(42, budget.AllocateKnownStock(
                HouseholdItemCatalog.BreadId, 42, new[] { 101 }));
            Assert.AreEqual(9, budget.AllocateKnownStock(
                HouseholdItemCatalog.BreadId, 42, new[] { 102 }));
            Assert.AreEqual(0, budget.AllocateKnownStock(
                HouseholdItemCatalog.BreadId, 42, new[] { 101, 102 }));
            Assert.AreEqual(51, supplier.Stock,
                "Planning availability must not reserve or mutate the seller's actual stock.");
        }

        [Test]
        public void SharedKnownSellerStock_ExcludesLiveReservationsBeforeAllocatingAcrossHouseholds()
        {
            var supplier = new TestFoodSupplier { Stock = 51 };
            var directory = new SupplierDirectory();
            directory.Register(supplier);
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(101, supplier.SupplierBusinessId, "same town", 0);
            knowledge.LearnSupplier(102, supplier.SupplierBusinessId, "same town", 0);
            var budget = new HouseholdFoodSupplyBudget(directory, knowledge);
            var authority = new EquipmentReservationService();
            float at = RetailStockReservation.GameSeconds(10, 8 * 60);
            Assert.IsNotNull(RetailStockReservation.TryReserveUnits(
                authority, supplier.SupplierBusinessId, HouseholdItemCatalog.BreadId,
                50, supplier.Stock, "trip-household-1", "existing household trip",
                at, at + 2 * 3600f, null));
            int cash = int.MaxValue;

            Assert.AreEqual(1, budget.AllocateKnownStock(
                HouseholdItemCatalog.BreadId, 42, new[] { 101 },
                ref cash,
                availableStoreUnits: (seller, itemId, liveStock) =>
                    RetailStockReservation.AvailableUnits(
                        authority, seller.SupplierBusinessId, itemId, liveStock, at)));
            Assert.AreEqual(0, budget.AllocateKnownStock(
                HouseholdItemCatalog.BreadId, 42, new[] { 102 },
                ref cash,
                availableStoreUnits: (seller, itemId, liveStock) =>
                    RetailStockReservation.AvailableUnits(
                        authority, seller.SupplierBusinessId, itemId, liveStock, at)),
                "The one unreserved loaf may be forecast once across all households.");
            Assert.AreEqual(51, supplier.Stock,
                "Forecast netting never reserves or mutates store inventory.");
        }

        [Test]
        public void ExactCommittedPurchase_CoversItsNeedWithoutOpeningFallbackNeed()
        {
            HouseholdState household = TwoMemberHousehold(9215);
            var needs = new HouseholdNeedRegistry();
            HouseholdPurchasingNeed breadNeed = needs.CreateNeed(9215, HouseholdItemCatalog.BreadId, 0);
            var inventory = new HouseholdInventory(householdId: 9215);
            int committedCalls = 0;

            LivePolicyMonitor(needs).Evaluate(
                household, inventory,
                FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                mealPriorityOrder: new List<string> { HouseholdItemCatalog.BreadId, "beef" },
                allocateSupplyUnitsNow: (itemId, units) => 0,
                committedAcquisitionUnitsForNeed: (householdId, needSequence, itemId) =>
                {
                    committedCalls++;
                    return householdId == 9215 && needSequence == breadNeed.NeedSequence
                        && itemId == HouseholdItemCatalog.BreadId ? 42 : 0;
                });

            Assert.AreEqual(1, committedCalls,
                "Only the open bread need is considered for committed cargo.");
            Assert.IsNull(needs.GetOpenNeed(9215, "beef"),
                "A paid, exact-need incoming bread batch covers the week's forecast once.");
            Assert.AreEqual(42, breadNeed.UnitsNeeded,
                "Forecast cover is not a receipt and does not mutate the purchasing obligation.");
            Assert.AreEqual(0, inventory.GetTotalUnits(HouseholdItemCatalog.BreadId),
                "Forecast cover never manufactures pantry stock.");
        }

        [Test]
        public void UnaffordableTopPriorityStock_DoesNotHideAffordableFallbackFood()
        {
            var breadSupplier = new TestFoodSupplier
            {
                Id = "expensive-bread-shop",
                ItemId = HouseholdItemCatalog.BreadId,
                Stock = 42,
                UnitPriceCents = 50,
            };
            var beefSupplier = new TestFoodSupplier
            {
                Id = "affordable-beef-shop",
                ItemId = "beef",
                Stock = 42,
                UnitPriceCents = 2,
            };
            var directory = new SupplierDirectory();
            directory.Register(breadSupplier);
            directory.Register(beefSupplier);
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(101, breadSupplier.SupplierBusinessId, "town", 0);
            knowledge.LearnSupplier(101, beefSupplier.SupplierBusinessId, "town", 0);
            var budget = new HouseholdFoodSupplyBudget(directory, knowledge);
            int cash = 30;

            HouseholdState household = TwoMemberHousehold(9212);
            var needs = new HouseholdNeedRegistry();
            var inventory = new HouseholdInventory(householdId: 9212);
            LivePolicyMonitor(needs).Evaluate(
                household, inventory,
                FoodUse(household, HouseholdItemCatalog.BreadId, "beef"),
                isColdSeason: false, SupplyAccess(),
                upcomingObligationCents: 0, expectedIncomeCents: 0, dayIndex: 0,
                mealPriorityOrder: new List<string> { HouseholdItemCatalog.BreadId, "beef" },
                allocateSupplyUnitsNow: (itemId, units) => budget.AllocateKnownStock(
                    itemId, units, new[] { 101 }, ref cash,
                    allowTradeCredit: true, creditAuthorityAvailable: true));

            Assert.AreEqual(0, cash,
                "The affordable fallback allocation consumes only the cash the household actually has.");
            Assert.AreEqual(42, needs.GetOpenNeed(9212, HouseholdItemCatalog.BreadId).UnitsNeeded,
                "The full requested bread need remains open; planning is not a sale or reserve.");
            Assert.AreEqual(27, needs.GetOpenNeed(9212, "beef").UnitsNeeded,
                "No bread is affordable, so the fifteen affordable beef units cover two and a half of the seven planned days.");
            Assert.AreEqual(42, breadSupplier.Stock);
            Assert.AreEqual(42, beefSupplier.Stock,
                "Shortage planning cannot reserve or mutate physical seller inventory.");
        }

        [Test]
        public void AuthorizedTradeCredit_CoversOnlyAWholeAffordableSellerLot()
        {
            var supplier = new TestFoodSupplier
            {
                Id = "credit-bread-shop",
                ItemId = HouseholdItemCatalog.BreadId,
                Stock = 30,
                UnitPriceCents = 10,
                CreditAvailable = true,
                CreditLimitCents = 300,
            };
            var directory = new SupplierDirectory();
            directory.Register(supplier);
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(101, supplier.SupplierBusinessId, "town", 0);
            var budget = new HouseholdFoodSupplyBudget(directory, knowledge);
            int cash = 0;

            Assert.AreEqual(0, budget.AllocateKnownStock(HouseholdItemCatalog.BreadId,
                42, new[] { 101 }, ref cash,
                allowTradeCredit: false, creditAuthorityAvailable: true),
                "Credit is not counted when the actual trip options disable it.");
            Assert.AreEqual(0, budget.AllocateKnownStock(HouseholdItemCatalog.BreadId,
                42, new[] { 101 }, ref cash,
                allowTradeCredit: true, creditAuthorityAvailable: false),
                "A supplier's credit promise is not actionable without both purchasing credit authorities.");
            Assert.AreEqual(30, budget.AllocateKnownStock(HouseholdItemCatalog.BreadId,
                42, new[] { 101 }, ref cash,
                allowTradeCredit: true, creditAuthorityAvailable: true));
            Assert.AreEqual(30, supplier.Stock,
                "Credit-aware planning remains a non-binding stock estimate.");
        }

        [Test]
        public void UnreachableKnownSeller_DoesNotCountAsFoodCoverOrConsumePlanningBudget()
        {
            var supplier = new TestFoodSupplier
            {
                Id = "remote-bread-shop",
                ItemId = HouseholdItemCatalog.BreadId,
                Stock = 6,
                UnitPriceCents = 4,
            };
            var directory = new SupplierDirectory();
            directory.Register(supplier);
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(101, supplier.SupplierBusinessId, "town", 0);
            var budget = new HouseholdFoodSupplyBudget(directory, knowledge);
            int cash = 24;

            Assert.AreEqual(0, budget.AllocateKnownStock(HouseholdItemCatalog.BreadId,
                6, new[] { 101 }, ref cash, canServeSupplier: _ => false),
                "A seller excluded by the physical-access forecast cannot suppress a food need.");
            Assert.AreEqual(24, cash, "Rejected access must not consume forecast purchasing power.");
            Assert.AreEqual(6, supplier.Stock, "Forecasting must never reserve or deduct seller stock.");

            Assert.AreEqual(6, budget.AllocateKnownStock(HouseholdItemCatalog.BreadId,
                6, new[] { 101 }, ref cash, canServeSupplier: _ => true),
                "The same known, affordable stock counts when the production access predicate admits it.");
            Assert.AreEqual(0, cash, "Only the admitted affordable stock consumes the shared forecast budget.");
            Assert.AreEqual(6, supplier.Stock, "Even admitted forecast stock remains physically unchanged.");
        }

        [Test]
        public void PackagedFlourStock_AllocatesHouseholdUnitsAndChargesPackagePrice()
        {
            var supplier = new TestFoodSupplier
            {
                Id = "flour-sack-store",
                ItemId = HouseholdItemCatalog.FlourId,
                Stock = 5,
                UnitPriceCents = 100,
                PackageSize = 10,
            };
            var directory = new SupplierDirectory();
            directory.Register(supplier);
            var knowledge = new ShopperSellerKnowledge();
            knowledge.LearnSupplier(101, supplier.SupplierBusinessId, "town", 0);
            var budget = new HouseholdFoodSupplyBudget(directory, knowledge);
            int cash = 500;

            Assert.AreEqual(42, budget.AllocateKnownStock(HouseholdItemCatalog.FlourId,
                42, new[] { 101 }, ref cash),
                "Five store sacks cover the requested 42 household pounds, capped at the actual need.");
            Assert.AreEqual(0, cash,
                "Five packages are priced as five store units, not 42 household pounds.");
            Assert.AreEqual(5, supplier.Stock,
                "Planning counts packaged availability without reserving or consuming stock.");
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
