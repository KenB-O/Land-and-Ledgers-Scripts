using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Orchestration.Player;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;

/// <summary>
/// Pass XL Phase 2: persistence for Phase 1 slice state, bootstrapper
/// forwarding (OR logic), and atomic household receipt delivery.
///
/// Freight surcharge in these tests (250c on 1000c of goods) is an
/// EXPLICIT TEST PARAMETER, not canon, carried over from Phase 1.
/// The supplier ("test-grain") is a test fixture, not a canon supplier.
/// No production freight rate or supplier is invented here.
/// </summary>
[TestFixture]
public sealed class PassXlPhase2PersistenceAndDeliveryTests
{
    private sealed class FakeBusinessCash : IBusinessCashPort
    {
        public int Balance;

        public FakeBusinessCash(int startingBalance)
        {
            Balance = startingBalance;
        }

        public int BalanceCents => Balance;

        public string TryDebit(int amountCents, string reason)
        {
            if (amountCents < 0 || amountCents > Balance)
            {
                return "test: insufficient business cash";
            }

            Balance -= amountCents;
            return null;
        }

        public string Credit(int amountCents, string reason)
        {
            if (amountCents < 0)
            {
                return "test: negative credit";
            }

            Balance += amountCents;
            return null;
        }
    }

    private sealed class FakeMigrationHolder : IPassXlMigrationFlagHolder
    {
        public bool SuppressMigrantArrivals { get; set; }
    }

    private sealed class FakePlayerHolder : IPassXlPlayerFlagHolder
    {
        public bool TreatPlayerAsNpc { get; set; }
    }

    private static ExternalMarketAuthority MarketWithSupply(string skuId, int units, int transitDays)
    {
        var market = new ExternalMarketAuthority();
        var supplier = new ExternalCounterparty
        {
            CounterpartyId = "test-grain",
            DisplayName = "Test Grain Co.",
            InitialBudgetCents = 50000,
            TransitDays = transitDays,
        };
        supplier.SupplyUnits[skuId] = units;
        Assert.IsNull(market.RegisterCounterparty(supplier));
        return market;
    }

    private static HouseholdLedger FundedHousehold(int householdId, int balanceCents, int dayIndex)
    {
        var ledger = new HouseholdLedger(householdId);
        Assert.IsNull(ledger.RecordInflow(
            dayIndex, balanceCents, HouseholdIncomeSource.OtherDocumented,
            "test-funding", "test funding for phase 2 tests", "test"));
        return ledger;
    }

    private static OffMapNamedSkuProcurementService ServiceWithFlourLot(
        FakeBusinessCash cash, out ExternalMarketAuthority market)
    {
        market = MarketWithSupply("flour", 500, 1);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        service.PlaceOrder("flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);
        Assert.AreEqual(1, service.ReceiveArrivals(11, null).Count);
        return service;
    }

    // ---- Persistence: coordinator ----

    [Test]
    public void OldSaveLoadDefaults_FlagsFalseAndProcurementEmpty()
    {
        var migration = new FakeMigrationHolder { SuppressMigrantArrivals = true };
        var player = new FakePlayerHolder { TreatPlayerAsNpc = true };
        var cash = new FakeBusinessCash(10000);
        var service = ServiceWithFlourLot(cash, out _);
        Assert.AreEqual(1, service.Lots.Count);

        // A null dto is an old save with no Pass XL section.
        List<string> diagnostics = PassXlSliceSaveCoordinator.Restore(
            null, migration, player, service);

        Assert.IsFalse(migration.SuppressMigrantArrivals);
        Assert.IsFalse(player.TreatPlayerAsNpc);
        Assert.AreEqual(0, service.Orders.Count);
        Assert.AreEqual(0, service.Lots.Count);
        Assert.AreEqual(0, service.Sales.Count);
        Assert.AreEqual(0, service.DeliveredSaleIds.Count);
        Assert.IsTrue(diagnostics.Count > 0);
    }

    [Test]
    public void CaptureWithNullHoldersAndService_ReadsAsDefaults()
    {
        PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(null, null, null);
        Assert.IsNotNull(dto);
        Assert.IsFalse(dto.treatPlayerAsNpc);
        Assert.IsFalse(dto.suppressMigrantArrivals);
        Assert.IsNull(dto.procurement);
    }

    [Test]
    public void RoundTrip_PreservesFlagsOrdersLotsSalesAndDeliveredMarks()
    {
        var cash = new FakeBusinessCash(10000);
        var service = ServiceWithFlourLot(cash, out _);
        var household = FundedHousehold(7, 10000, 11);
        var inventory = new HouseholdInventory(7);
        OffMapNamedSkuProcurementService.NamedSkuDeliveryResult delivery =
            service.SellAndDeliverToInventory(household, inventory, 7, 3, "buyer", "flour", 40, 20, 11);
        Assert.IsTrue(delivery.Success);

        var migration = new FakeMigrationHolder { SuppressMigrantArrivals = true };
        var player = new FakePlayerHolder { TreatPlayerAsNpc = true };
        PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(migration, player, service);
        Assert.IsTrue(dto.treatPlayerAsNpc);
        Assert.IsTrue(dto.suppressMigrantArrivals);
        Assert.IsNotNull(dto.procurement);

        var restoredMigration = new FakeMigrationHolder();
        var restoredPlayer = new FakePlayerHolder();
        var restoredCash = new FakeBusinessCash(0);
        var restoredService = new OffMapNamedSkuProcurementService(
            "general-store-1", MarketWithSupply("flour", 500, 1), restoredCash);
        PassXlSliceSaveCoordinator.Restore(dto, restoredMigration, restoredPlayer, restoredService);

        Assert.IsTrue(restoredMigration.SuppressMigrantArrivals);
        Assert.IsTrue(restoredPlayer.TreatPlayerAsNpc);
        Assert.AreEqual(1, restoredService.Orders.Count);
        Assert.AreEqual(1, restoredService.Lots.Count);
        Assert.AreEqual(60, restoredService.SellableUnits("flour"));
        Assert.AreEqual(1, restoredService.Sales.Count);
        Assert.AreEqual(1, restoredService.DeliveredSaleIds.Count);
        Assert.IsTrue(restoredService.IsSaleDelivered(restoredService.Sales[0].SaleId));
        Assert.AreEqual(service.Sales[0].SaleId, restoredService.Sales[0].SaleId);
    }

    [Test]
    public void DuplicateImport_AndDuplicateRestore_CreateNothingTwice()
    {
        var cash = new FakeBusinessCash(10000);
        var service = ServiceWithFlourLot(cash, out _);
        var household = FundedHousehold(7, 10000, 11);
        var inventory = new HouseholdInventory(7);
        Assert.IsTrue(service.SellAndDeliverToInventory(household, inventory, 7, 3, "buyer", "flour", 10, 20, 11).Success);

        NamedSkuProcurementSaveDto dto = service.ExportState();
        service.ImportState(dto);
        service.ImportState(dto);
        Assert.AreEqual(1, service.Orders.Count);
        Assert.AreEqual(1, service.Lots.Count);
        Assert.AreEqual(1, service.Sales.Count);
        Assert.AreEqual(1, service.DeliveredSaleIds.Count);

        var sliceDto = PassXlSliceSaveCoordinator.Capture(
            new FakeMigrationHolder { SuppressMigrantArrivals = true },
            new FakePlayerHolder { TreatPlayerAsNpc = true }, service);
        var migration = new FakeMigrationHolder();
        var player = new FakePlayerHolder();
        var freshCash = new FakeBusinessCash(0);
        var fresh = new OffMapNamedSkuProcurementService(
            "general-store-1", MarketWithSupply("flour", 500, 1), freshCash);
        PassXlSliceSaveCoordinator.Restore(sliceDto, migration, player, fresh);
        PassXlSliceSaveCoordinator.Restore(sliceDto, migration, player, fresh);
        Assert.AreEqual(1, fresh.Orders.Count);
        Assert.AreEqual(1, fresh.Lots.Count);
        Assert.AreEqual(1, fresh.Sales.Count);
        Assert.AreEqual(1, fresh.DeliveredSaleIds.Count);
        Assert.IsTrue(migration.SuppressMigrantArrivals);
        Assert.IsTrue(player.TreatPlayerAsNpc);
    }

    [Test]
    public void LoadDifferentSave_DoesNotLeakPreviousSaveState()
    {
        var cash = new FakeBusinessCash(10000);
        var service = ServiceWithFlourLot(cash, out _);
        PassXlSliceSaveDto fullDto = PassXlSliceSaveCoordinator.Capture(
            new FakeMigrationHolder { SuppressMigrantArrivals = true },
            new FakePlayerHolder { TreatPlayerAsNpc = true }, service);

        var migration = new FakeMigrationHolder();
        var player = new FakePlayerHolder();
        var freshCash = new FakeBusinessCash(0);
        var fresh = new OffMapNamedSkuProcurementService(
            "general-store-1", MarketWithSupply("flour", 500, 1), freshCash);
        PassXlSliceSaveCoordinator.Restore(fullDto, migration, player, fresh);
        Assert.AreEqual(1, fresh.Lots.Count);

        // A second, empty save (old-save shape, flags false, no procurement).
        var emptyDto = new PassXlSliceSaveDto { treatPlayerAsNpc = false, suppressMigrantArrivals = false, procurement = null };
        PassXlSliceSaveCoordinator.Restore(emptyDto, migration, player, fresh);
        Assert.IsFalse(migration.SuppressMigrantArrivals);
        Assert.IsFalse(player.TreatPlayerAsNpc);
        Assert.AreEqual(0, fresh.Orders.Count);
        Assert.AreEqual(0, fresh.Lots.Count);
        Assert.AreEqual(0, fresh.Sales.Count);
        Assert.AreEqual(0, fresh.DeliveredSaleIds.Count);
    }

    [Test]
    public void Restore_WithNullHolders_DoesNotCrashAndStillRestoresService()
    {
        var cash = new FakeBusinessCash(10000);
        var service = ServiceWithFlourLot(cash, out _);
        PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(
            new FakeMigrationHolder { SuppressMigrantArrivals = true },
            new FakePlayerHolder { TreatPlayerAsNpc = true }, service);

        var freshCash = new FakeBusinessCash(0);
        var fresh = new OffMapNamedSkuProcurementService(
            "general-store-1", MarketWithSupply("flour", 500, 1), freshCash);
        List<string> diagnostics = PassXlSliceSaveCoordinator.Restore(dto, null, null, fresh);
        Assert.AreEqual(1, fresh.Lots.Count);
        Assert.IsTrue(diagnostics.Count > 0);
    }

    // ---- Bootstrapper forwarding: OR logic ----

    [Test]
    public void Forwarding_TrueForcesNpcModeOn()
    {
        var player = new FakePlayerHolder { TreatPlayerAsNpc = false };
        bool result = PlayerNpcPlaytestForwarding.ApplyBootstrapperFlag(player, true);
        Assert.IsTrue(result);
        Assert.IsTrue(player.TreatPlayerAsNpc);
    }

    [Test]
    public void Forwarding_FalseLeavesSavedTrueUntouched()
    {
        var player = new FakePlayerHolder { TreatPlayerAsNpc = true };
        bool result = PlayerNpcPlaytestForwarding.ApplyBootstrapperFlag(player, false);
        Assert.IsTrue(result);
        Assert.IsTrue(player.TreatPlayerAsNpc);
    }

    [Test]
    public void Forwarding_FalseLeavesFalseUnchanged_AndNullDriversIsNoOp()
    {
        var player = new FakePlayerHolder { TreatPlayerAsNpc = false };
        bool result = PlayerNpcPlaytestForwarding.ApplyBootstrapperFlag(player, false);
        Assert.IsFalse(result);
        Assert.IsFalse(player.TreatPlayerAsNpc);
        Assert.IsFalse(PlayerNpcPlaytestForwarding.ApplyBootstrapperFlag(null, true));
    }

    // ---- Household receipt: SellAndDeliver ----

    [Test]
    public void SellAndDeliver_Success_InventoryCashAndStoreLotAllSettleOnce()
    {
        var cash = new FakeBusinessCash(10000);
        var service = ServiceWithFlourLot(cash, out _);
        // Procurement paid 1250c (100u at 10c + 250c freight): 10000 -> 8750.
        Assert.AreEqual(8750, cash.Balance);
        var household = FundedHousehold(7, 10000, 11);
        var inventory = new HouseholdInventory(7);

        OffMapNamedSkuProcurementService.NamedSkuDeliveryResult result =
            service.SellAndDeliverToInventory(household, inventory, 7, 3, "buyer", "flour", 40, 20, 11);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.AlreadyDelivered);
        Assert.IsNotNull(result.Sale);
        Assert.AreEqual(1, inventory.Lots.Count);
        Assert.AreEqual("flour", inventory.Lots[0].ItemId);
        Assert.AreEqual(40, inventory.Lots[0].QuantityUnits);
        Assert.AreEqual("off-map-procurement-sale", inventory.Lots[0].SourceKind);
        Assert.AreEqual(3, inventory.Lots[0].OwnerPersonId);
        Assert.IsTrue(inventory.Lots[0].SourceLabel.Contains(result.Sale.SaleId));
        // Single charge only: household 10000 - 800 = 9200; business 8750 + 800 = 9550.
        Assert.AreEqual(9200, household.GetBalanceCents());
        Assert.AreEqual(9550, cash.Balance);
        Assert.AreEqual(60, service.SellableUnits("flour"));
        Assert.AreEqual(1, service.Sales.Count);
        Assert.AreEqual(1, service.DeliveredSaleIds.Count);
        Assert.IsTrue(service.IsSaleDelivered(result.Sale.SaleId));
    }

    [Test]
    public void SellAndDeliver_AddLotRejection_RollsBackEverything()
    {
        // "no_such_item" is orderable off-map (market checks supply only),
        // but HouseholdInventory rejects unknown items. That is the
        // AddLot-rejection path, with no synthetic stock created.
        var market = MarketWithSupply("no_such_item", 100, 1);
        var cash = new FakeBusinessCash(10000);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        service.PlaceOrder("no_such_item", 10, 10, 25, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);
        Assert.AreEqual(1, service.ReceiveArrivals(11, null).Count);
        Assert.AreEqual(10000 - 125, cash.Balance);

        var household = FundedHousehold(7, 10000, 11);
        var inventory = new HouseholdInventory(7);
        OffMapNamedSkuProcurementService.NamedSkuDeliveryResult result =
            service.SellAndDeliverToInventory(household, inventory, 7, 3, "buyer", "no_such_item", 5, 20, 11);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostic.Contains("rolled back"));
        // The attempted sale object is returned for diagnostics, but the
        // service record itself must be gone after rollback.
        Assert.AreEqual(0, inventory.Lots.Count);
        Assert.AreEqual(10, service.SellableUnits("no_such_item"));
        Assert.AreEqual(10000, household.GetBalanceCents());
        Assert.AreEqual(10000 - 125, cash.Balance);
        Assert.AreEqual(0, service.Sales.Count);
        Assert.AreEqual(0, service.DeliveredSaleIds.Count);
    }

    [Test]
    public void DeliverSaleTwice_DoesNotDoubleAddInventory()
    {
        var cash = new FakeBusinessCash(10000);
        var service = ServiceWithFlourLot(cash, out _);
        var household = FundedHousehold(7, 10000, 11);
        var inventory = new HouseholdInventory(7);
        OffMapNamedSkuProcurementService.NamedSkuDeliveryResult first =
            service.SellAndDeliverToInventory(household, inventory, 7, 3, "buyer", "flour", 40, 20, 11);
        Assert.IsTrue(first.Success);

        OffMapNamedSkuProcurementService.NamedSkuDeliveryResult second =
            service.DeliverSaleToInventory(first.Sale, inventory);
        Assert.IsTrue(second.Success);
        Assert.IsTrue(second.AlreadyDelivered);
        Assert.AreEqual(1, inventory.Lots.Count);
        Assert.AreEqual(40, inventory.Lots[0].QuantityUnits);
        Assert.AreEqual(9200, household.GetBalanceCents());
        Assert.AreEqual(60, service.SellableUnits("flour"));
    }

    [Test]
    public void SaveRestoreAfterDelivery_DoesNotRedeliver()
    {
        var cash = new FakeBusinessCash(10000);
        var service = ServiceWithFlourLot(cash, out _);
        var household = FundedHousehold(7, 10000, 11);
        var inventory = new HouseholdInventory(7);
        OffMapNamedSkuProcurementService.NamedSkuDeliveryResult delivery =
            service.SellAndDeliverToInventory(household, inventory, 7, 3, "buyer", "flour", 40, 20, 11);
        Assert.IsTrue(delivery.Success);

        PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(
            new FakeMigrationHolder(), new FakePlayerHolder(), service);

        // Simulate Continue: a fresh service restored from the save,
        // and a fresh inventory restored from the household's saved lots.
        var restoredCash = new FakeBusinessCash(0);
        var restoredService = new OffMapNamedSkuProcurementService(
            "general-store-1", MarketWithSupply("flour", 500, 1), restoredCash);
        PassXlSliceSaveCoordinator.Restore(dto, new FakeMigrationHolder(), new FakePlayerHolder(), restoredService);
        Assert.AreEqual(1, restoredService.Sales.Count);
        NamedSkuSale restoredSale = restoredService.Sales[0];

        var restoredState = new HouseholdInventoryState
        {
            HouseholdId = 7,
            NextLotSequence = inventory.State.NextLotSequence,
            Lots = new List<HouseholdItemLot>(),
        };
        for (int i = 0; i < inventory.Lots.Count; i++)
        {
            HouseholdItemLot lot = inventory.Lots[i];
            restoredState.Lots.Add(new HouseholdItemLot
            {
                LotId = lot.LotId,
                ItemId = lot.ItemId,
                QuantityUnits = lot.QuantityUnits,
                UnitLabel = lot.UnitLabel,
                AcquiredDayIndex = lot.AcquiredDayIndex,
                SourceLabel = lot.SourceLabel,
                QualityBand = lot.QualityBand,
                SourceKind = lot.SourceKind,
                OwnerPersonId = lot.OwnerPersonId,
            });
        }

        var restoredInventory = new HouseholdInventory(restoredState);
        OffMapNamedSkuProcurementService.NamedSkuDeliveryResult again =
            restoredService.DeliverSaleToInventory(restoredSale, restoredInventory);
        Assert.IsTrue(again.Success);
        Assert.IsTrue(again.AlreadyDelivered);
        Assert.AreEqual(1, restoredInventory.Lots.Count);
        Assert.AreEqual(40, restoredInventory.Lots[0].QuantityUnits);
    }
}
