using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.FirstLedger;
using NUnit.Framework;
using UnityEditor;

public sealed class PurchasedRetailLotReceiptTests
{
    private const string StoreDefinitionPath = "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset";

    [TestCase(0, 2, 7, 2, 7)]
    [TestCase(1, 2, 7, 2, 7)]
    [TestCase(10, 2, 7, 2, 2)]
    [TestCase(14, 2, 7, 2, 0)]
    public void BusinessFeedRestock_UsesWholePackagesAndExistingCarryUnits(
        int availableFeedUnits, int dailyRequired, int targetDays, int unitsPerSack, int expectedSacks)
    {
        Assert.AreEqual(expectedSacks, BusinessFeedPurchase.PackagesToTarget(
            availableFeedUnits, dailyRequired, targetDays, unitsPerSack));
    }

    [Test]
    public void BusinessFeedReceipt_PreservesNamedSourceAndRejectsDuplicateReceipt()
    {
        GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(StoreDefinitionPath);
        Assert.IsNotNull(definition);
        BusinessInstanceState business = BusinessInstanceState.Create(
            "livery_feed_receipt_idempotency_test", definition, 0,
            BusinessOwnerIdentity.Npc(7, "Ethan", "Boniecki"));
        business.RuntimeState.EnsureCategoryStock("animal_feed", 0, 0);
        var batch = new BusinessFeedPurchase.Batch
        {
            TransactionId = "b3-feed-test-day-1",
            SellerBusinessId = "general_store_test",
            BuyerBusinessId = business.InstanceId,
            ItemId = "store_horse_feed_sack",
            CategoryId = "animal_feed",
            UnitLabel = "sack",
            Units = 2,
            DayIndex = 1,
            SourceLots = new System.Collections.Generic.List<BusinessFeedPurchase.SourceLotPart>
            {
                new BusinessFeedPurchase.SourceLotPart
                {
                    LotId = "b0-feed-opening-lot",
                    Provenance = "test supplier opening feed order",
                    UnitLabel = "sack",
                    Units = 2,
                }
            }
        };

        BusinessFeedPurchase.Result first = BusinessFeedPurchase.ReceiveAtBusiness(batch, business.RuntimeState);
        Assert.IsTrue(first.Succeeded, first.Refusal);
        Assert.IsFalse(first.Duplicate);
        Assert.AreEqual(2, business.RuntimeState.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        BusinessRetailItemLotState received = business.RuntimeState.FindRetailItemLot(
            "feed-purchase:b3-feed-test-day-1:b0-feed-opening-lot");
        Assert.IsNotNull(received);
        Assert.IsTrue(received.Provenance.Contains("b0-feed-opening-lot"));

        BusinessFeedPurchase.Result retry = BusinessFeedPurchase.ReceiveAtBusiness(batch, business.RuntimeState);
        Assert.IsTrue(retry.Succeeded, retry.Refusal);
        Assert.IsTrue(retry.Duplicate);
        Assert.AreEqual(2, business.RuntimeState.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(1, business.RuntimeState.RetailItemLots.Count);
    }

    [Test]
    public void BusinessFeedStorage_ExpandsOnlyToFiniteRestockNeedWithoutCreatingStock()
    {
        GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(StoreDefinitionPath);
        Assert.IsNotNull(definition);
        BusinessInstanceState business = BusinessInstanceState.Create(
            "livery_feed_capacity_test", definition, 0,
            BusinessOwnerIdentity.Npc(7, "Ethan", "Boniecki"));
        business.RuntimeState.EnsureCategoryStock("animal_feed", 1, 6);

        int capacity = business.RuntimeState.EnsureCategoryStorageCapacity("animal_feed", 8);

        Assert.AreEqual(8, capacity);
        Assert.AreEqual(1, business.RuntimeState.GetCategoryStock("animal_feed").CurrentStockUnits);
        Assert.AreEqual(0, business.RuntimeState.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
    }

    [Test]
    public void OpeningFeedOrder_ReceivesTwentySacksOnceAndSettlesBusinessCash()
    {
        GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(StoreDefinitionPath);
        Assert.IsNotNull(definition);
        BusinessInstanceState store = BusinessInstanceState.Create(
            "general_store_test_feed_receipt", definition, 0,
            BusinessOwnerIdentity.Npc(1, "Test Storekeeper", "Test"));
        store.RuntimeState.SetCurrentCashCents(20000);

        var supplier = new ExternalCounterparty
        {
            CounterpartyId = "test-feed-supplier",
            DisplayName = "Test Feed Supplier",
            InitialBudgetCents = 50000,
            TransitDays = 0,
        };
        supplier.SupplyUnits["store_horse_feed_sack"] = 20;
        var market = new ExternalMarketAuthority();
        Assert.IsNull(market.RegisterCounterparty(supplier));
        var service = new OffMapNamedSkuProcurementService(
            store.InstanceId, market, new Series2BusinessRuntimeCashPort(store.RuntimeState));

        NamedSkuOrder order = service.PlaceOrder(
            "store_horse_feed_sack", 20, 15, 0, supplier.CounterpartyId, 0,
            NamedSkuPaymentMode.CashOnArrival, null);
        Assert.IsNotNull(order);
        Assert.AreEqual(1, service.ReceiveArrivals(order.ExpectedArrivalDayIndex, null).Count);
        Assert.AreEqual(19700, store.RuntimeState.CurrentCashCents);
        Assert.AreEqual(0, market.GetCounterparty(supplier.CounterpartyId).SupplyUnits["store_horse_feed_sack"]);

        store.RuntimeState.EnsureCategoryStock("animal_feed", 0, 0);
        NamedSkuLot sourceLot = service.Lots[0];
        PurchasedRetailLotReceipt.Result result = PurchasedRetailLotReceipt.Transfer(
            service, store.RuntimeState, sourceLot, "animal_feed", "sack", 0);

        Assert.IsTrue(result.Received, result.Refusal);
        Assert.AreEqual(20, result.Units);
        Assert.AreEqual(0, sourceLot.RemainingUnits);
        Assert.AreEqual(20, store.RuntimeState.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(1, store.RuntimeState.RetailItemLots.Count);
        Assert.IsTrue(store.RuntimeState.RetailItemLots[0].Provenance.Contains(supplier.CounterpartyId));
        Assert.AreEqual(19700, store.RuntimeState.CurrentCashCents);

        PurchasedRetailLotReceipt.Result duplicate = PurchasedRetailLotReceipt.Transfer(
            service, store.RuntimeState, sourceLot, "animal_feed", "sack", 0);
        Assert.IsFalse(duplicate.Received);
        Assert.IsTrue(duplicate.Duplicate);
        Assert.AreEqual(20, store.RuntimeState.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(19700, store.RuntimeState.CurrentCashCents);
    }

    [Test]
    public void RefusedDestinationReceiptRestoresPurchasedSourceCustody()
    {
        GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(StoreDefinitionPath);
        Assert.IsNotNull(definition);
        BusinessInstanceState store = BusinessInstanceState.Create(
            "general_store_test_feed_receipt_rollback", definition, 0,
            BusinessOwnerIdentity.Npc(1, "Test Storekeeper", "Test"));
        store.RuntimeState.SetCurrentCashCents(20000);

        var supplier = new ExternalCounterparty
        {
            CounterpartyId = "test-feed-supplier-rollback",
            DisplayName = "Test Feed Supplier",
            InitialBudgetCents = 50000,
            TransitDays = 0,
        };
        supplier.SupplyUnits["store_horse_feed_sack"] = 20;
        var market = new ExternalMarketAuthority();
        Assert.IsNull(market.RegisterCounterparty(supplier));
        var service = new OffMapNamedSkuProcurementService(
            store.InstanceId, market, new Series2BusinessRuntimeCashPort(store.RuntimeState));
        NamedSkuOrder order = service.PlaceOrder("store_horse_feed_sack", 20, 15, 0,
            supplier.CounterpartyId, 0, NamedSkuPaymentMode.CashOnArrival, null);
        Assert.IsNotNull(order);
        service.ReceiveArrivals(order.ExpectedArrivalDayIndex, null);
        NamedSkuLot sourceLot = service.Lots[0];

        PurchasedRetailLotReceipt.Result result = PurchasedRetailLotReceipt.Transfer(
            service, store.RuntimeState, sourceLot, "missing_feed_category", "sack", order.ExpectedArrivalDayIndex);

        Assert.IsFalse(result.Received);
        Assert.IsTrue(result.SourceRestored);
        Assert.AreEqual(20, sourceLot.RemainingUnits);
        Assert.AreEqual(0, service.LotConsumptions.Count);
        Assert.AreEqual(0, store.RuntimeState.RetailItemLots.Count);
        Assert.AreEqual(19700, store.RuntimeState.CurrentCashCents,
            "The already-settled supplier purchase remains paid while its purchased source lot remains in custody for retry.");
    }

    [Test]
    public void BusinessHorseFeedAdapter_UsesTwoFeedUnitsPerSackAndCarriesRemainder()
    {
        GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(StoreDefinitionPath);
        Assert.IsNotNull(definition);
        BusinessInstanceState business = BusinessInstanceState.Create(
            "livery_feed_adapter_test", definition, 0,
            BusinessOwnerIdentity.Npc(7, "Ethan", "Boniecki"));
        BusinessRuntimeState runtime = business.RuntimeState;
        runtime.EnsureCategoryStock("animal_feed", 0, 0);
        Assert.IsNull(runtime.TryAddRetailItemLot(
            "feed-adapter-test-lot", "store_horse_feed_sack", "animal_feed", "sack", 2,
            business.InstanceId, business.InstanceId, "test purchase receipt", 0));

        int carry = 0;
        var adapter = new BusinessHorseFeedStockAdapter(
            runtime, "store_horse_feed_sack", "animal_feed", "sack", 2, 1,
            () => carry, amount => carry = amount);

        Assert.AreEqual(4, adapter.FeedStockUnits);
        Assert.AreEqual(3, adapter.TakeFeedUnits(3));
        Assert.AreEqual(1, carry);
        Assert.AreEqual(1, adapter.FeedStockUnits);
        Assert.AreEqual(1, adapter.TakeFeedUnits(1));
        Assert.AreEqual(0, carry);
        Assert.AreEqual(0, adapter.FeedStockUnits);
        Assert.AreEqual(0, runtime.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
    }
}
