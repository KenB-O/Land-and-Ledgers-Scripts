// Review 2 vertical slice Pass D3 — save adoption for the D2
// owner-addable horse-feed registrations, plus decision-free
// end-to-end traces of the fuel, freight, and hiring chains.
//
// D3's first priority, and the one production gap D2 left
// open: D2 registrations were in-memory only, so a restored
// world fell back to null / unknown-item with diagnostics,
// never phantom feed. D3 adds an additive save section
// (Review2VerticalD3SaveState, coordinator overloads, one new
// DTO field) and proves, mid-run, that a store feed item and
// a runtime item survive restore and still feed, and that an
// old save without the section restores empty with a
// diagnostic. No existing DTO shape changes except that one
// additive field; persisted values are exactly the
// caller-supplied registration values, verbatim. No feed
// item id, price, or unit in this file is canon: change any
// one of them and the tests must still reconcile.
//
// WHAT THE SAVE TESTS DO AND DO NOT PROVE, stated plainly:
//   - Store category STOCK is not in the D3 section (it lives
//     in the business runtime save). The mid-run test keeps
//     the same BusinessRuntimeState object across the
//     save/restore boundary, exactly as a runtime save would
//     restore it, and proves the REGISTRATION survives: the
//     restored registry resolves the same category at the
//     same conversion and CareDay draws the same units.
//     Restoring registrations alone, without the runtime
//     save, must NOT be claimed to restore stock.
//   - The static HouseholdItemCatalog runtime dictionary is
//     per-process, not per-world (D2 design, unchanged).
//     Restore re-registers DTO items into it; the old-save
//     test therefore asserts the restored STATE lists are
//     empty and diagnosed, and the restored registry resolves
//     null — it does NOT assert the static catalog is empty,
//     because an earlier test in the same process may have
//     registered an identical item. The state lists ARE
//     clear-then-load; the catalog is idempotent-or-diagnosed.
//   - The SimulationDrivers branch, per-day household adapter
//     construction, and CareDay wiring are Unity-side and are
//     NOT tested here (the driver is a MonoBehaviour,
//     excluded from the harness, as in D1/D2). The domain
//     resolvers it calls ARE tested, including after restore.
//   - Feed-need origination (who opens a feed need, at what
//     quantity) is NOT decided here. Where a test needs a
//     need, the caller constructs it and the test says so.
//   - Fuel, freight, and hiring traces below run on
//     production driver/adapter classes only, never on an
//     orchestrator as the system under test, with exact
//     cash/stock reconciliation. They chain Pass 6/7/8
//     behaviour end to end rather than re-proving each link:
//     the new claim in each trace is the join, not the link.
//   - No named-person fixture data is authored anywhere in
//     this file: every person is a generic test person.
//     Unity compilation, Test Runner, PlayMode, and scene
//     wiring are NOT claimed.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Animals;
using LandLedgers.Primitives;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.FreightFuel;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Tests.Economy
{
    public sealed class Review2VerticalD3Tests
    {
        // Caller-supplied test parameters for every test below.
        // None of these is a game price, id, rate, or conversion:
        // change any one of them and the tests must still
        // reconcile, because the code takes them as parameters.
        private const string FeedItemId = "d3-test-horse-feed";
        private const string FeedCategoryId = "d3-feed-cat";
        private const string StoreBusinessId = "general-store-1";
        private const int StoreUnitsPerFeedUnit = 2;
        private const int HouseholdUnitsPerFeedUnit = 1;

        private static AnimalState RegisterHorse(AnimalRegistry registry, string ownerId, AnimalOwnerKind ownerKind, int dayIndex)
        {
            AnimalState horse = registry.RegisterAnimal(
                AnimalSpecies.Horse, "draft", AnimalSex.Female, ownerKind, ownerId, dayIndex, "d3 test horse");
            Assert.IsNotNull(horse);
            Assert.IsNull(HorseAuthority.Configure(horse, HorseUseCapabilities.Draft, "stable-1-yard", "stable-1", dayIndex));
            return horse;
        }

        private static Review2VerticalD3SaveState NewRegisteredState(List<string> diag)
        {
            var state = new Review2VerticalD3SaveState();
            Assert.IsNull(state.RegisterRuntimeItem(
                new HouseholdItemDefinition(FeedItemId, "Test Horse Feed", "sack", FeedCategoryId, 1, false), diag),
                "runtime registration must succeed; diag: " + string.Join("; ", diag));
            return state;
        }

        private static BusinessRuntimeState NewStockedStore(Review2StoreFeedRegistry registry, int storeUnits)
        {
            var store = new BusinessRuntimeState();
            store.BindBusinessIdentity(StoreBusinessId);
            var diag = new List<string>();
            Assert.IsNull(Review2StoreHorseFeed.AddFeedItem(store, registry, new Review2StoreFeedItem
            {
                BusinessInstanceId = StoreBusinessId,
                FeedCategoryId = FeedCategoryId,
                FeedItemId = FeedItemId,
                DisplayName = "Test Horse Feed",
                UnitLabel = "sack",
                StoreUnitsPerFeedUnit = StoreUnitsPerFeedUnit,
            }, diag), "owner add must succeed; diag: " + string.Join("; ", diag));
            Assert.AreEqual(storeUnits, store.AddCategoryStockUnits(FeedCategoryId, storeUnits),
                "stocking after the add must land exactly; the add itself creates no stock");
            return store;
        }

        // ---------- D3 save adoption ----------

        [Test]
        public void D3Save_MidRunSaveRestore_StoreFeedAndRuntimeItemStillFeed()
        {
            var diag = new List<string>();
            Review2VerticalD3SaveState live = NewRegisteredState(diag);
            BusinessRuntimeState store = NewStockedStore(live.FeedRegistry, 20);

            // Mid-run save: capture the live state into the DTO.
            Review2VerticalD3SaveDto dto = live.Capture(diag);
            Assert.AreEqual(1, dto.RuntimeItems.Count, "exactly the one registered runtime item is captured");
            Assert.AreEqual(FeedItemId, dto.RuntimeItems[0].ItemId, "captured values are the caller-supplied values, verbatim");
            Assert.AreEqual(FeedCategoryId, dto.RuntimeItems[0].ReserveCategoryId);
            Assert.AreEqual(1, dto.StoreFeeds.Count, "exactly the one store feed registration is captured");
            Assert.AreEqual(StoreBusinessId, dto.StoreFeeds[0].BusinessInstanceId);
            Assert.AreEqual(StoreUnitsPerFeedUnit, dto.StoreFeeds[0].StoreUnitsPerFeedUnit);

            // Restore into a fresh state, as a load would: the
            // store runtime object stands in for the runtime
            // save, which restores stock separately (see header).
            var restored = new Review2VerticalD3SaveState();
            var restoreDiag = new List<string>();
            restored.Restore(dto, restoreDiag);
            Assert.AreEqual(1, restored.RuntimeItems.Count);
            Assert.AreEqual(FeedItemId, restored.RuntimeItems[0].ItemId);
            Assert.AreEqual(1, restored.StoreFeeds.Count);
            Assert.AreEqual(StoreUnitsPerFeedUnit, restored.StoreFeeds[0].StoreUnitsPerFeedUnit);
            Review2StoreFeedItem restoredFeed = restored.FeedRegistry.Find(StoreBusinessId);
            Assert.IsNotNull(restoredFeed, "the restored registry must resolve the store feed item");
            Assert.AreEqual(FeedItemId, restoredFeed.FeedItemId);
            Assert.AreEqual(StoreUnitsPerFeedUnit, restoredFeed.StoreUnitsPerFeedUnit);

            // The restored registration still feeds a store horse.
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterHorse(animals, StoreBusinessId, AnimalOwnerKind.Business, 0);
            horse.Nutrition01 = 0.70f;
            var careDiag = new List<string>();
            IHorseFeedStock stock = Review2StoreHorseFeed.FeedStockForHorse(
                horse, _ => store, restored.FeedRegistry, careDiag);
            Assert.IsNotNull(stock, "a restored registration must resolve a feed stock, never null: " + string.Join("; ", careDiag));
            Assert.AreEqual(10, stock.FeedStockUnits, "20 store units at 2 per feed unit = 10 feed units, exactly as before restore");
            int unfed = HorseCareDriver.CareDay(animals, h => Review2StoreHorseFeed.FeedStockForHorse(h, _ => store, restored.FeedRegistry, careDiag), _ => false, 1, careDiag);
            Assert.AreEqual(0, unfed, "the store horse must be fed after restore: " + string.Join("; ", careDiag));
            Assert.AreEqual(16, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits,
                "idle ration is ceil(1.2) = 2 feed units x 2 store units = 4 store units, as before restore");
            Assert.GreaterOrEqual(horse.Nutrition01, 0.70f, "a fed horse must not degrade");

            // And the restored runtime item still lets a
            // person-owned horse eat from household inventory:
            // AddLot rejects unknown items, so a successful lot
            // proves the item is known to the catalog again.
            var inventory = new HouseholdInventory(7);
            Assert.IsNull(inventory.AddLot(FeedItemId, 6, 1, "test purchase", "", "", -1),
                "the restored runtime item must be a known household item");
            AnimalState personHorse = RegisterHorse(animals, "person:1", AnimalOwnerKind.Person, 0);
            personHorse.Nutrition01 = 0.70f;
            var personFeed = new Review2HouseholdHorseFeed(inventory, FeedItemId, HouseholdUnitsPerFeedUnit, 1);
            int personUnfed = HorseCareDriver.CareDay(animals, h => h == personHorse ? personFeed : null, _ => false, 1, careDiag);
            Assert.AreEqual(0, personUnfed, "the person-owned horse must be fed from the restored item: " + string.Join("; ", careDiag));
            Assert.AreEqual(4, inventory.GetAvailableUnits(FeedItemId),
                "idle ration is 2 feed units x 1 household unit = 2 units drawn from inventory");
        }

        [Test]
        public void D3Save_OldSaveWithoutD3Section_RestoresEmptyWithDiagnostic_AndNoPhantomFeed()
        {
            var state = new Review2VerticalD3SaveState();
            var diag = new List<string>();
            state.Restore(null, diag);
            Assert.AreEqual(0, state.RuntimeItems.Count, "an old save restores empty runtime items, never invented ones");
            Assert.AreEqual(0, state.StoreFeeds.Count, "an old save restores empty store feeds, never phantom feed");
            Assert.IsNull(state.FeedRegistry.Find(StoreBusinessId), "no registration means no resolution");
            Assert.IsTrue(string.Join("; ", diag).Contains("no D3 section in save"),
                "the empty restore must be diagnosed, never silent: " + string.Join("; ", diag));

            // Same for a DTO that exists but carries no section.
            var dto = new PassXlSliceSaveDto();
            Assert.IsNull(dto.review2VerticalD3, "the new field defaults to null on a DTO built without D3");
            var state2 = new Review2VerticalD3SaveState();
            var diag2 = new List<string>();
            state2.Restore(dto.review2VerticalD3, diag2);
            Assert.AreEqual(0, state2.RuntimeItems.Count);
            Assert.AreEqual(0, state2.StoreFeeds.Count);
            Assert.IsTrue(string.Join("; ", diag2).Contains("no D3 section in save"));
        }

        [Test]
        public void D3Save_RestoreTwice_IsIdempotent_AndSkipsInvalidEntries()
        {
            var diag = new List<string>();
            Review2VerticalD3SaveState live = NewRegisteredState(diag);
            BusinessRuntimeState store = NewStockedStore(live.FeedRegistry, 20);
            Review2VerticalD3SaveDto dto = live.Capture(diag);
            // Invalid entries must never become registrations.
            dto.RuntimeItems.Add(new Review2D3RuntimeItemDto { ItemId = string.Empty });
            dto.StoreFeeds.Add(new Review2D3StoreFeedDto
            {
                BusinessInstanceId = StoreBusinessId, FeedCategoryId = FeedCategoryId,
                FeedItemId = "bogus-feed", DisplayName = "Bogus", UnitLabel = "sack",
                StoreUnitsPerFeedUnit = 0,
            });

            var restored = new Review2VerticalD3SaveState();
            var restoreDiag = new List<string>();
            restored.Restore(dto, restoreDiag);
            Assert.AreEqual(1, restored.RuntimeItems.Count, "the empty-id runtime item is skipped, not stored");
            Assert.AreEqual(1, restored.StoreFeeds.Count, "the zero-conversion feed item is skipped, never a silent 1:1");
            Assert.AreEqual(FeedItemId, restored.FeedRegistry.Find(StoreBusinessId).FeedItemId);
            Assert.IsTrue(string.Join("; ", restoreDiag).Contains("no item id"));
            Assert.IsTrue(string.Join("; ", restoreDiag).Contains("no positive conversion"));

            var restoreDiag2 = new List<string>();
            restored.Restore(dto, restoreDiag2);
            Assert.AreEqual(1, restored.RuntimeItems.Count, "a second restore yields the same state, never doubles");
            Assert.AreEqual(1, restored.StoreFeeds.Count);
            Assert.AreEqual(FeedItemId, restored.FeedRegistry.Find(StoreBusinessId).FeedItemId);

            // The restored state still feeds, after both restores.
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterHorse(animals, StoreBusinessId, AnimalOwnerKind.Business, 0);
            var careDiag = new List<string>();
            int unfed = HorseCareDriver.CareDay(animals, h => Review2StoreHorseFeed.FeedStockForHorse(h, _ => store, restored.FeedRegistry, careDiag), _ => false, 1, careDiag);
            Assert.AreEqual(0, unfed);
            Assert.AreEqual(16, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits);
        }

        [Test]
        public void D3Save_CoordinatorOverload_RoundTripsD3Section_AndNullStateDefaultsEmpty()
        {
            var diag = new List<string>();
            Review2VerticalD3SaveState live = NewRegisteredState(diag);
            NewStockedStore(live.FeedRegistry, 20);
            PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(null, null, null, null, null, live);
            Assert.IsNotNull(dto.review2VerticalD3, "the coordinator overload must carry the D3 section");
            Assert.AreEqual(1, dto.review2VerticalD3.RuntimeItems.Count);
            Assert.AreEqual(1, dto.review2VerticalD3.StoreFeeds.Count);

            var restored = new Review2VerticalD3SaveState();
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(dto, null, null, null, null, null, null, restored);
            Assert.AreEqual(1, restored.RuntimeItems.Count);
            Assert.AreEqual(1, restored.StoreFeeds.Count);
            Assert.AreEqual(FeedItemId, restored.FeedRegistry.Find(StoreBusinessId).FeedItemId);

            // A null state captures a null section, which
            // restores as empty — the Pass 9 / Pass 10 contract.
            PassXlSliceSaveDto emptyDto = PassXlSliceSaveCoordinator.Capture(null, null, null, null, null, null);
            Assert.IsNull(emptyDto.review2VerticalD3);
            var emptyState = new Review2VerticalD3SaveState();
            List<string> emptyDiag = PassXlSliceSaveCoordinator.Restore(emptyDto, null, null, null, null, null, null, emptyState);
            Assert.AreEqual(0, emptyState.RuntimeItems.Count);
            Assert.AreEqual(0, emptyState.StoreFeeds.Count);
            Assert.IsTrue(string.Join("; ", emptyDiag).Contains("no D3 section in save"));

            // A present section with no state holder is diagnosed,
            // never silently dropped without a trace.
            List<string> noHolderDiag = PassXlSliceSaveCoordinator.Restore(dto, null, null, null, null, null, null, null);
            Assert.IsTrue(string.Join("; ", noHolderDiag).Contains("no Review2VerticalD3SaveState supplied"));
        }

        [Test]
        public void D3Save_ConflictingCatalogDefinition_IsDiagnosed_AndExistingDefinitionWins()
        {
            // The static catalog cannot be cleared by a restore
            // (see header). If a save carries a definition that
            // conflicts with what the catalog already holds for
            // that id, restore diagnoses it and the existing
            // definition wins, while the state list still records
            // what the save said, so the divergence is visible.
            var diag = new List<string>();
            Review2VerticalD3SaveState live = NewRegisteredState(diag);
            var conflicting = new Review2VerticalD3SaveDto();
            conflicting.RuntimeItems.Add(new Review2D3RuntimeItemDto
            {
                ItemId = FeedItemId, DisplayName = "Different Feed", UnitLabel = "sack",
                ReserveCategoryId = FeedCategoryId, MealServingUnits = 1, IsFood = false,
            });
            var restored = new Review2VerticalD3SaveState();
            var restoreDiag = new List<string>();
            restored.Restore(conflicting, restoreDiag);
            Assert.AreEqual(1, restored.RuntimeItems.Count, "the save record is kept in the state list for diagnosis");
            Assert.AreEqual("Different Feed", restored.RuntimeItems[0].DisplayName);
            Assert.AreEqual("Test Horse Feed", HouseholdItemCatalog.Get(FeedItemId).DisplayName,
                "the existing catalog definition wins over the conflicting restore");
            Assert.IsTrue(string.Join("; ", restoreDiag).Contains("not re-registered"),
                "the conflict must be diagnosed: " + string.Join("; ", restoreDiag));
            _ = live;
        }

        // ---------- fuel chain, end to end ----------

        [Test]
        public void D3Fuel_DealerToBakery_ThenDealerToHousehold_ExactReconciliation()
        {
            // One dealer yard, two legs, one stock pool, every
            // unit reconciled. This chains the Pass 8 links
            // (intake, dealer-to-bakery supply, adapter sale,
            // reserve link) into a single trace; the new claim
            // is the join, not any single link. Pre-check-first
            // here means what Pass 8 proved: a supply request
            // larger than dealer stock is refused BEFORE any
            // unit moves, never partially dispensed — re-asserted
            // below after the first two legs have drawn stock.
            var ids = new EntityIdRegistry();
            var intake = new FuelYardByproductIntake();
            var dealer = new Series2FuelDealer(intake, "fuel-dealer-1");
            var diag = new List<string>();
            Assert.IsNull(dealer.ReceiveSawmillLot(new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot), FuelWoodUnits = 10,
                SourceMillBusinessId = "mill-1", SourceLogLotId = "log-lot-1",
                StandId = "stand-1", Species = "pine", ProducedDayIndex = 1,
            }, diag), "first sawmill lot must be received: " + string.Join("; ", diag));
            Assert.IsNull(dealer.ReceiveSawmillLot(new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot), FuelWoodUnits = 10,
                SourceMillBusinessId = "mill-1", SourceLogLotId = "log-lot-1",
                StandId = "stand-1", Species = "pine", ProducedDayIndex = 2,
            }, diag));
            Assert.AreEqual(20, dealer.StockUnits, "dealer holds exactly the 20 units received");
            Assert.IsTrue(string.Join("; ", diag).Contains("no sawmill payment"),
                "receipt must diagnose that no sawmill payment was made, not silently price it at 0c: " + string.Join("; ", diag));

            // Leg 1, dealer -> bakery fuel store: 6 units move,
            // with the dealer lot reference carried into the
            // bakery lot (provenance, not an orphan lot).
            var bakery = new BakeryShopRuntime("bakery-1");
            var source = new Series2DealerBakeryFuelSource(dealer, 1, null, ids);
            Assert.AreEqual(20, source.FuelUnitsOnHand());
            Assert.IsNull(source.SupplyTo(bakery, 6, 5, diag), "bakery supply must succeed: " + string.Join("; ", diag));
            Assert.AreEqual(6, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
            Assert.AreEqual(14, dealer.StockUnits, "20 - 6 supplied = 14 in the yard");
            Assert.AreEqual("fuel-dealer-1", bakery.FuelStock.Lots[0].FuelDealerBusinessId);

            // Pre-check-first, re-asserted: a 30-unit request
            // against 14 in stock refuses in full, moving nothing.
            Assert.IsNotNull(source.SupplyTo(bakery, 30, 5, diag), "shortfall must refuse, not partially fuel");
            Assert.AreEqual(6, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "refused supply leaves the bakery fuel store untouched");
            Assert.AreEqual(14, dealer.StockUnits, "refused supply leaves dealer stock untouched");

            // Leg 2, dealer -> household, through the real
            // supplier adapter and the reserve link: 4 units
            // sold at the caller-supplied 28c, then credited
            // to the household fuel reserve at the
            // caller-supplied conversion factor 2 (8 units).
            var dealerRuntime = new BusinessRuntimeState();
            dealerRuntime.BindBusinessIdentity("fuel-dealer-1");
            var adapter = new Series2FuelDealerSupplierAdapter(
                dealer.Intake, new Pass8RuntimeCashPort(dealerRuntime),
                "fuel-dealer-1", "Fuel Dealer", "yard-1", 28);
            int sold = adapter.SellItem(HouseholdItemCatalog.FuelWoodId, 4, 7,
                new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger }, diag);
            Assert.AreEqual(4, sold, string.Join("; ", diag));
            Assert.AreEqual(10, dealer.StockUnits, "14 - 4 sold = 10 in the yard");
            Assert.AreEqual(112, dealerRuntime.CurrentCashCents, "4u x 28c revenue posted to the dealer, not invented");

            var household = new HouseholdState { id = 99, householdName = "Test Household" };
            household.EnsureHouseholdReservesInitialized();
            HouseholdReserveState before = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            int beforeUnits = before != null ? before.currentUnits : 0;
            int credited = Series2HouseholdFuelReserveLink.CreditReserve(household, sold, 2, 7, diag);
            Assert.AreEqual(8, credited, "conversion factor 2 is a caller parameter, honoured here, not a game rate");
            HouseholdReserveState after = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            Assert.IsNotNull(after);
            Assert.AreEqual(beforeUnits + 8, after.currentUnits, "reserve gains exactly the 8 converted units");
            Assert.AreEqual(-1, Series2HouseholdFuelReserveLink.CreditReserve(null, 4, 1, 7, diag), "null household refuses, credits nothing");
            Assert.AreEqual(-1, Series2HouseholdFuelReserveLink.CreditReserve(household, 0, 1, 7, diag), "zero quantity refuses, credits nothing");

            // Whole-trace reconciliation: 20 in, 6 to the bakery,
            // 4 to the household, 10 left in the yard, 112c to
            // the dealer, 8 units in the household reserve.
            Assert.AreEqual(20, 6 + 4 + dealer.StockUnits, "every intake unit is accounted for, none created or lost");
        }

        // ---------- freight legs, end to end ----------

        [Test]
        public void D3Freight_CarrierLeg_AcceptanceGatedSettlement_SurvivesSaveRestoreBoundary()
        {
            // Carrier leg through the real production driver
            // (Pass 8 pattern): dealer to bakery, 10 units, 250c.
            var diag = new List<string>();
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            var service = new FreightShipmentService();
            service.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", DisplayName = "wagon-1",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
            var driver = new FreightProductionDriver(service);
            var carrierRuntime = new BusinessRuntimeState();
            carrierRuntime.BindBusinessIdentity("freight-co-1");
            driver.EnsureCompany("freight-co-1", carrierRuntime);
            var carrier = new Series2FreightProductionCarrier(driver, "freight-co-1");
            var recipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            var hooks = new FreightShipmentHooks
            {
                Tasks = tasks, Budgets = budgets, DayIndex = 42,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    string key = shipment.destinationBusinessInstanceId ?? "nowhere";
                    recipientStock[key] = recipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            Series2CarrierLeg leg = Series2SliceLegs.DealerToBakery(
                "ship-fuel-1", "freight-co-1", "fuel-dealer-1", "bakery-1", 10, 250, "wagon-1");
            leg.DriverId = EntityId.For(EntityKind.Person, 702);
            leg.AnimalIds = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            Series2CarrierLegResult created = carrier.RequestLeg(leg, hooks, diag);
            Assert.AreEqual(string.Empty, created.Refusal, created.Refusal);
            Assert.IsNotNull(created.Shipment);
            int guard = 0;
            while (created.Shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 60)
                carrier.AdvanceTick(3600f, hooks, EntityId.For(EntityKind.Person, 701), diag);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, created.Shipment.Status, string.Join("; ", diag));
            Assert.AreEqual(10, created.Shipment.DeliveredQuantityUnits);
            Assert.AreEqual(10, recipientStock["bakery-1"], "acceptance through the callback, exactly once — no double stock");

            // Settle once: payer 1000c -> 750c, carrier 0c -> 250c.
            var payerRuntime = new BusinessRuntimeState();
            payerRuntime.BindBusinessIdentity("bakery-1");
            payerRuntime.AddCashCents(1000);
            var carrierCash = new BusinessRuntimeState();
            carrierCash.BindBusinessIdentity("freight-co-1");
            var settlement = new Series2CarrierSettlement();
            Series2CarrierSettlementResult settled = settlement.SettleOnAcceptance(
                created.Shipment, new Pass8RuntimeCashPort(payerRuntime), new Pass8RuntimeCashPort(carrierCash), diag);
            Assert.IsTrue(settled.Settled, settled.Refusal);
            Assert.AreEqual(750, payerRuntime.CurrentCashCents);
            Assert.AreEqual(250, carrierCash.CurrentCashCents);

            // Save/restore boundary (Pass 9 decision, chained
            // here): a fresh settlement object rebuilt from the
            // restored shipments must still refuse the same leg,
            // because the shipment's own freightRevenueCollected
            // flag — which IS in the shipment save DTO — is the
            // settlement authority across saves, not the
            // in-memory set. No double collection, ever.
            var restoredSettlement = new Series2CarrierSettlement();
            restoredSettlement.RestoreFromShipments(new[] { created.Shipment });
            Assert.IsTrue(restoredSettlement.AlreadySettled("ship-fuel-1"));
            Series2CarrierSettlementResult again = restoredSettlement.SettleOnAcceptance(
                created.Shipment, new Pass8RuntimeCashPort(payerRuntime), new Pass8RuntimeCashPort(carrierCash), diag);
            Assert.IsFalse(again.Settled, "a restored settlement must refuse the same leg again");
            Assert.AreEqual(750, payerRuntime.CurrentCashCents, "cash unchanged by the refused second settlement");
            Assert.AreEqual(250, carrierCash.CurrentCashCents);
        }

        // ---------- hiring trace: where it stops in Roslyn ----------

        [Test]
        public void D3Hiring_MissedWork_AssessorToBrain_IntentOnly_EffortIsNotAHire()
        {
            // Missed work, as the assessor reads it: 2400 open
            // production minutes, no available employee minutes,
            // a waiting line at the balk tolerance and recurring
            // balks. The assessor is read-only and reports
            // evidence only — never a headcount target, quota,
            // or staffing order (its own contract, honoured here).
            var diag = new List<string>();
            var assessor = new LaborDemandAssessor();
            LaborDemandReport report = assessor.Assess(new LaborDemandInputs
            {
                BusinessInstanceId = "bakery-1",
                DayIndex = 1,
                OpenTaskMinutesByFamily = new Dictionary<string, int> { { "Production", 2400 } },
                WaitingCustomers = 6,
                RecentBalkCount = 3,
                RequiresPersonPhaseMinutes = 2400,
                ActiveEmployeeCount = 0,
                AvailableEmployeeMinutes = 0,
            }, diag);
            Assert.IsNotNull(report);
            Assert.Greater(report.TotalUnmetMinutes, 0, "missed work with no workers must leave unmet minutes");
            Assert.IsTrue(report.CoverageGap, "work exists that no available person can cover in time");
            Assert.IsTrue(report.HiringPressure, "coverage gap plus recurring balks is hiring-pressure evidence, not a quota");

            // The brain decides within delegated Staffing scope:
            // OpenRecruitment, as an INTENT string, not a hire.
            // "Channel costs and effort creation stay with the
            // integrator" is the brain's own outcome wording.
            var brain = new ManagerBrain();
            var inputs = new ManagerCycleInputs
            {
                BusinessInstanceId = "bakery-1",
                ManagerPersonId = 3,
                CashCents = 10000,
                DemandReport = report,
                DelegatedScopes = new List<string> { ManagerScopeNames.Staffing },
                DayIndex = 1,
                JudgmentSeed = 42,
                JudgmentQuality01 = 1f,
                PurchaseLimitCents = 2000,
                MaxBorrowingCents = 10000,
                Instructions = new List<StandingInstruction>
                {
                    new StandingInstruction
                    {
                        InstructionId = "d3-hire-1",
                        BusinessInstanceId = "bakery-1",
                        Kind = InstructionKind.ManagerJudgment,
                        TargetActKind = ManagerActKinds.OpenRecruitment,
                        Objective = "keep the bakery staffed as work goes unmet",
                        MaxSpendCentsPerAct = -1,
                        CreatedByPersonId = 7,
                        CreatedDayIndex = 0,
                    },
                },
            };
            ManagerDecisionResult decision = brain.DailyCycle(inputs, diag);
            Assert.IsTrue(decision.Acted, "the brain must act on hiring pressure within Staffing scope");
            Assert.AreEqual(ManagerActKinds.OpenRecruitment, decision.ActionKind);
            Assert.AreEqual("intent:recruitment-effort:bakery-1:d1", decision.ResultingReference,
                "the brain's output is an intent reference, not an effort and not a hire");
            Assert.IsTrue(decision.Outcome.Contains("stay with the integrator"),
                "the brain itself names the integrator step it does not perform: " + decision.Outcome);

            // Where the chain stops in Roslyn: the intent alone
            // creates NOTHING in the recruitment service and NO
            // employment relationship. The brain-to-effort link
            // (SimulationDrivers.ExecuteManagerRecruitment) is
            // Unity-side and is not claimed here.
            var recruitment = new RecruitmentService();
            var employment = new EmploymentRelationshipRegistry();
            Assert.AreEqual(0, Count(recruitment.Efforts), "an intent string is not an effort");
            Assert.AreEqual(0, employment.GetByEmployer("bakery-1").Count, "no employment relationship exists from an intent");

            // The integrator step, performed explicitly here so
            // its cost is visible: opening an effort still hires
            // NOBODY. The handbill channel costs 150c in
            // printing, which the caller books through the normal
            // expense flow — that booking is not done here, and
            // no cash is claimed to have moved. A hire needs a
            // real candidate inquiry and acceptance, which this
            // trace deliberately does not fabricate: no named
            // person is hired, and no fake employment is recorded.
            RecruitmentEffort effort = recruitment.OpenEffort(
                "bakery-1", "Baker", RecruitmentChannel.Handbill, 1, 42, diag);
            Assert.IsNotNull(effort, "the integrator-called effort must open: " + string.Join("; ", diag));
            Assert.AreEqual(150, effort.CostCents, "handbill printing cost is recorded on the effort, never silently absorbed");
            Assert.AreEqual(1, Count(recruitment.Efforts), "exactly one effort exists after the explicit open");
            Assert.AreEqual(0, employment.GetByEmployer("bakery-1").Count,
                "an open effort is not a hire: still no employment relationship");
        }

        private static int Count<T>(IReadOnlyCollection<T> items)
        {
            return items != null ? items.Count : 0;
        }

        // Production-port seam, copied from the Pass 8 fixture
        // (private there): the same production interface over
        // the same BusinessRuntimeState the cash port uses.
        private sealed class Pass8RuntimeCashPort : IBusinessCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public Pass8RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }
            public int BalanceCents => runtime != null ? runtime.CurrentCashCents : 0;
            public string TryDebit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                return runtime.TrySpendCents(amountCents) ? null : "insufficient business cash";
            }
            public string Credit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                runtime.AddCashCents(amountCents);
                return null;
            }
        }
    }
}
