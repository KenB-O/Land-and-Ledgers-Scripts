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

    // ---- W4 (F-1): feed receipt custody ----

    // W4 fixture note: the pre-existing tests in this file build their
    // destination through AssetDatabase.LoadAssetAtPath, which returns null
    // in the Roslyn harness — that is why those five tests are in the known
    // 18-failure set and only pass in the Unity Test Runner. The W4 tests
    // build a bare BusinessRuntimeState instead (BindBusinessIdentity +
    // EnsureCategoryStock), so they pin the receipt/ledger behaviour in
    // the harness too.
    private static BusinessRuntimeState FeedDestination(string instanceId)
    {
        var runtime = new BusinessRuntimeState();
        runtime.BindBusinessIdentity(instanceId);
        runtime.EnsureCategoryStock("animal_feed", 0, 0);
        return runtime;
    }

    private static BusinessFeedPurchase.Batch FeedBatch(string transactionId, string buyerBusinessId, int units)
    {
        return new BusinessFeedPurchase.Batch
        {
            TransactionId = transactionId,
            SellerBusinessId = "general_store_test",
            BuyerBusinessId = buyerBusinessId,
            ItemId = "store_horse_feed_sack",
            CategoryId = "animal_feed",
            UnitLabel = "sack",
            Units = units,
            DayIndex = 1,
            SourceLots = new System.Collections.Generic.List<BusinessFeedPurchase.SourceLotPart>
            {
                new BusinessFeedPurchase.SourceLotPart
                {
                    LotId = "b0-feed-opening-lot",
                    Provenance = "test supplier opening feed order",
                    UnitLabel = "sack",
                    Units = units,
                }
            }
        };
    }

    [Test]
    public void FeedReceipt_NullBatch_RefusalNamesMissingBatch()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_null_batch");
        BusinessFeedPurchase.Result result = BusinessFeedPurchase.ReceiveAtBusiness(null, destination);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Refusal.Contains("no custody batch"), result.Refusal);
    }

    [Test]
    public void FeedReceipt_NullDestination_RefusalNamesMissingDestination()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_null_dest");
        BusinessFeedPurchase.Result result = BusinessFeedPurchase.ReceiveAtBusiness(
            FeedBatch("w4-null-dest", destination.BusinessId, 2), null);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Refusal.Contains("no destination business"), result.Refusal);
    }

    [Test]
    public void FeedReceipt_EmptyTransactionId_RefusalNamesMissingTransactionId()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_empty_tx");
        BusinessFeedPurchase.Batch batch = FeedBatch(string.Empty, destination.BusinessId, 2);
        BusinessFeedPurchase.Result result = BusinessFeedPurchase.ReceiveAtBusiness(batch, destination);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Refusal.Contains("no transaction id"), result.Refusal);
    }

    [Test]
    public void FeedReceipt_BusinessIdMismatch_RefusalNamesMismatch()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_mismatch");
        BusinessFeedPurchase.Batch batch = FeedBatch("w4-mismatch", "another-business", 2);
        BusinessFeedPurchase.Result result = BusinessFeedPurchase.ReceiveAtBusiness(batch, destination);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Refusal.Contains("does not match buyer"), result.Refusal);
        Assert.IsTrue(result.Refusal.Contains(destination.BusinessId), result.Refusal);
    }

    [Test]
    public void FeedReceipt_ZeroUnits_RefusalNamesMissingUnits()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_zero_units");
        BusinessFeedPurchase.Batch batch = FeedBatch("w4-zero-units", destination.BusinessId, 0);
        BusinessFeedPurchase.Result result = BusinessFeedPurchase.ReceiveAtBusiness(batch, destination);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Refusal.Contains("has no units"), result.Refusal);
    }

    [Test]
    public void FeedReceipt_EmptySourceLots_RefusalNamesMissingSourceLots()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_empty_lots");
        BusinessFeedPurchase.Batch batch = FeedBatch("w4-empty-lots", destination.BusinessId, 2);
        batch.SourceLots = new System.Collections.Generic.List<BusinessFeedPurchase.SourceLotPart>();
        BusinessFeedPurchase.Result result = BusinessFeedPurchase.ReceiveAtBusiness(batch, destination);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Refusal.Contains("names no source lots"), result.Refusal);
    }

    /// <summary>
    /// W4 retry arc: the direct receipt is refused on capacity (target 1
    /// sack, batch 2), the batch is held in the custody ledger, and the
    /// next-day retry succeeds — reserving the capacity the drive would
    /// have reserved — with the SAME batch, so no second purchase is
    /// placed and cash moves once (the retry never debits).
    /// </summary>
    [Test]
    public void CustodyLedger_RetryAfterCapacityRefusal_SucceedsWithoutSecondPurchase()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_retry");
        // EnsureCategoryStock does not update an existing category's target
        // (the fixture already created it at 0), so raise the target with
        // the storage-capacity seam: target 1 sack, batch 2.
        destination.EnsureCategoryStorageCapacity("animal_feed", 1);
        destination.SetCurrentCashCents(5000);
        BusinessFeedPurchase.Batch batch = FeedBatch("w4-retry-capacity", destination.BusinessId, 2);

        BusinessFeedPurchase.Result refused = BusinessFeedPurchase.ReceiveAtBusiness(batch, destination);
        Assert.IsFalse(refused.Succeeded);
        Assert.IsTrue(refused.Refusal.Contains("capacity cannot accept"), refused.Refusal);
        Assert.AreEqual(0, destination.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));

        var ledger = new BusinessFeedCustodyLedger();
        Assert.IsTrue(ledger.TryHold(batch));
        Assert.AreEqual(1, ledger.PendingCount);
        Assert.IsTrue(ledger.HasPendingForBuyer(destination.BusinessId));

        bool attempted = ledger.TryRetryForBuyer(destination.BusinessId, destination, out BusinessFeedPurchase.Result retry);
        Assert.IsTrue(attempted);
        Assert.IsTrue(retry.Succeeded, retry.Refusal);
        Assert.IsFalse(retry.Duplicate);
        Assert.AreEqual(0, ledger.PendingCount);
        Assert.AreEqual(2, destination.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(1, destination.RetailItemLots.Count);
        Assert.AreEqual(5000, destination.CurrentCashCents,
            "The retry reuses the already-paid batch; cash moves once, at purchase.");
    }

    /// <summary>
    /// W4: a refusal the retry cannot repair (an existing receipt lot
    /// that does not match the batch) stays held — the ledger never drops
    /// a batch it could not receive, and never silently buys a replacement.
    /// </summary>
    [Test]
    public void CustodyLedger_RetryKeepsBatchOnLotMismatchRefusal()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_mismatch_lot");
        BusinessFeedPurchase.Batch batch = FeedBatch("w4-mismatch-lot", destination.BusinessId, 2);
        Assert.IsNull(destination.TryAddRetailItemLot(
            "feed-purchase:w4-mismatch-lot:b0-feed-opening-lot", "store_horse_feed_sack", "animal_feed", "sack", 1,
            destination.BusinessId, destination.BusinessId, "pre-existing mismatched lot", 0));

        BusinessFeedPurchase.Result refused = BusinessFeedPurchase.ReceiveAtBusiness(batch, destination);
        Assert.IsFalse(refused.Succeeded);
        Assert.IsTrue(refused.Refusal.Contains("does not match"), refused.Refusal);

        var ledger = new BusinessFeedCustodyLedger();
        Assert.IsTrue(ledger.TryHold(batch));
        bool attempted = ledger.TryRetryForBuyer(destination.BusinessId, destination, out BusinessFeedPurchase.Result retry);
        Assert.IsTrue(attempted);
        Assert.IsFalse(retry.Succeeded);
        Assert.AreEqual(1, ledger.PendingCount);
        Assert.IsTrue(ledger.TryPeekForBuyer(destination.BusinessId, out BusinessFeedPurchase.Batch held));
        Assert.AreEqual("w4-mismatch-lot", held.TransactionId);
    }

    [Test]
    public void CustodyLedger_HoldRefusesDuplicateTransaction()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_dup_hold");
        BusinessFeedPurchase.Batch batch = FeedBatch("w4-dup-hold", destination.BusinessId, 2);

        var ledger = new BusinessFeedCustodyLedger();
        Assert.IsTrue(ledger.TryHold(batch));
        Assert.IsFalse(ledger.TryHold(batch), "The same transaction is held once; a second copy must never be retried.");
        Assert.AreEqual(1, ledger.PendingCount);
        Assert.IsFalse(ledger.TryHold(null));
        Assert.IsFalse(ledger.TryHold(FeedBatch(string.Empty, destination.BusinessId, 2)));
    }

    [Test]
    public void CustodyLedger_RefusesEmptySerializedPlaceholderBatch()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_empty_placeholder");
        var ledger = new BusinessFeedCustodyLedger();
        var placeholder = new BusinessFeedPurchase.Batch();

        Assert.IsFalse(placeholder.HasValidCustodyIdentity);
        Assert.IsFalse(ledger.TryHold(placeholder),
            "A refused checkout may deserialize null as an empty Batch; it is not purchased custody.");
        Assert.AreEqual(0, ledger.PendingCount);
    }

    /// <summary>
    /// W4: a batch already received by another path retries as a duplicate
    /// success — the ledger clears without stocking the goods twice.
    /// </summary>
    [Test]
    public void CustodyLedger_RetryIsIdempotentWhenAlreadyReceived()
    {
        BusinessRuntimeState destination = FeedDestination("livery_feed_w4_dup_receipt");
        BusinessFeedPurchase.Batch batch = FeedBatch("w4-dup-receipt", destination.BusinessId, 2);

        BusinessFeedPurchase.Result first = BusinessFeedPurchase.ReceiveAtBusiness(batch, destination);
        Assert.IsTrue(first.Succeeded, first.Refusal);

        var ledger = new BusinessFeedCustodyLedger();
        Assert.IsTrue(ledger.TryHold(batch));
        bool attempted = ledger.TryRetryForBuyer(destination.BusinessId, destination, out BusinessFeedPurchase.Result retry);
        Assert.IsTrue(attempted);
        Assert.IsTrue(retry.Succeeded, retry.Refusal);
        Assert.IsTrue(retry.Duplicate);
        Assert.AreEqual(0, ledger.PendingCount);
        Assert.AreEqual(2, destination.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(1, destination.RetailItemLots.Count);
    }
}
