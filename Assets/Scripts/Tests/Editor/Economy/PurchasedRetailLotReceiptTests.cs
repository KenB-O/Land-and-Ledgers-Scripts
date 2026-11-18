using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.FirstLedger;
using NUnit.Framework;

public sealed class PurchasedRetailLotReceiptTests
{

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

    [TestCase(5, 3, 3)]
    [TestCase(2, 8, 2)]
    [TestCase(5, 0, 0)]
    [TestCase(-2, 4, 0)]
    public void BusinessFeedRestock_UsesOnlyPackagesActuallyAvailableAtSeller(
        int requestedPackages, int sellerPackages, int expectedPurchase)
    {
        Assert.AreEqual(expectedPurchase, BusinessFeedPurchase.PackagesAvailableToPurchase(
            requestedPackages, sellerPackages));
    }

    [Test]
    public void BusinessFeedStockout_ReportsFiniteShelfScarcityWithoutLosingConfiguredPrice()
    {
        string refusal = BusinessFeedPurchase.DescribeStoreAvailabilityRefusal(
            "store_horse_feed_sack", 7, 0, 15, false);

        StringAssert.Contains("no identified 'store_horse_feed_sack' stock", refusal);
        StringAssert.Contains("price remains 15c/package", refusal);
        StringAssert.DoesNotContain("0c", refusal);
        StringAssert.DoesNotContain("7", refusal,
            "The same zero-stock cause should not vary each day as reserve demand grows.");
    }

    [Test]
    public void BusinessFeedAvailability_SeparatesUnpricedAndInvalidLiveOffers()
    {
        string unpriced = BusinessFeedPurchase.DescribeStoreAvailabilityRefusal(
            "store_horse_feed_sack", 3, 4, 0, true);
        string invalidOffer = BusinessFeedPurchase.DescribeStoreAvailabilityRefusal(
            "store_horse_feed_sack", 3, 4, 15, false);

        StringAssert.Contains("no positive owner-set price", unpriced);
        StringAssert.Contains("no valid live sale offer", invalidOffer);
        StringAssert.Contains("4 identified", invalidOffer);
    }

    [TestCase("store_horse_feed_sack", true)]
    [TestCase("bakery-notions", false)]
    [TestCase("", false)]
    public void StoreFeedReceipt_OnlyRoutesConfiguredHorseFeedSkuToAnimalFeed(string lotSkuId, bool expected)
    {
        Assert.AreEqual(expected, BusinessFeedPurchase.IsHorseFeedSku(lotSkuId, "store_horse_feed_sack"));
    }

    [Test]
    public void BusinessFeedReceipt_PreservesNamedSourceAndRejectsDuplicateReceipt()
    {
        // Harness note: the destination used to come from
        // AssetDatabase.LoadAssetAtPath, which returns null in the Roslyn
        // harness (the .asset is Unity-only). The receipt behaviour under
        // test needs only a bound runtime identity, so build it the W4 way
        // (FeedDestination below); every assertion is unchanged.
        BusinessRuntimeState business = FeedDestination("livery_feed_receipt_idempotency_test");
        var batch = new BusinessFeedPurchase.Batch
        {
            TransactionId = "b3-feed-test-day-1",
            SellerBusinessId = "general_store_test",
            BuyerBusinessId = business.BusinessId,
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

        BusinessFeedPurchase.Result first = BusinessFeedPurchase.ReceiveAtBusiness(batch, business);
        Assert.IsTrue(first.Succeeded, first.Refusal);
        Assert.IsFalse(first.Duplicate);
        Assert.AreEqual(2, business.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        BusinessRetailItemLotState received = business.FindRetailItemLot(
            "feed-purchase:b3-feed-test-day-1:b0-feed-opening-lot");
        Assert.IsNotNull(received);
        Assert.IsTrue(received.Provenance.Contains("b0-feed-opening-lot"));

        BusinessFeedPurchase.Result retry = BusinessFeedPurchase.ReceiveAtBusiness(batch, business);
        Assert.IsTrue(retry.Succeeded, retry.Refusal);
        Assert.IsTrue(retry.Duplicate);
        Assert.AreEqual(2, business.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(1, business.RetailItemLots.Count);
    }

    [Test]
    public void BusinessFeedStorage_ExpandsOnlyToFiniteRestockNeedWithoutCreatingStock()
    {
        // Harness note: see test 1 — no AssetDatabase in the harness.
        // Built directly (not via the shared FeedDestination helper, which
        // pre-creates animal_feed at (0, 0)): EnsureCategoryStock never
        // updates an existing category, so the (1, 6) creation must happen
        // on a fresh runtime for the assertions below to mean anything.
        var business = new BusinessRuntimeState();
        business.BindBusinessIdentity("livery_feed_capacity_test");
        business.EnsureCategoryStock("animal_feed", 1, 6);

        int capacity = business.EnsureCategoryStorageCapacity("animal_feed", 8);

        Assert.AreEqual(8, capacity);
        Assert.AreEqual(1, business.GetCategoryStock("animal_feed").CurrentStockUnits);
        Assert.AreEqual(0, business.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
    }

    [Test]
    public void OpeningFeedOrder_ReceivesTwentySacksOnceAndSettlesBusinessCash()
    {
        // Harness note: see test 1 — no AssetDatabase in the harness.
        BusinessRuntimeState store = FeedDestination("general_store_test_feed_receipt");
        store.SetCurrentCashCents(20000);

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
            store.BusinessId, market, new Series2BusinessRuntimeCashPort(store));

        NamedSkuOrder order = service.PlaceOrder(
            "store_horse_feed_sack", 20, 15, 0, supplier.CounterpartyId, 0,
            NamedSkuPaymentMode.CashOnArrival, null);
        Assert.IsNotNull(order);
        Assert.AreEqual(1, service.ReceiveArrivals(order.ExpectedArrivalDayIndex, null).Count);
        Assert.AreEqual(19700, store.CurrentCashCents);
        Assert.AreEqual(0, market.GetCounterparty(supplier.CounterpartyId).SupplyUnits["store_horse_feed_sack"]);

        store.EnsureCategoryStock("animal_feed", 0, 0);
        NamedSkuLot sourceLot = service.Lots[0];
        PurchasedRetailLotReceipt.Result result = PurchasedRetailLotReceipt.Transfer(
            service, store, sourceLot, "animal_feed", "sack", 0);

        Assert.IsTrue(result.Received, result.Refusal);
        Assert.AreEqual(20, result.Units);
        Assert.AreEqual(0, sourceLot.RemainingUnits);
        Assert.AreEqual(20, store.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(1, store.RetailItemLots.Count);
        Assert.IsTrue(store.RetailItemLots[0].Provenance.Contains(supplier.CounterpartyId));
        Assert.IsTrue(store.RetailItemLots[0].AcquisitionCostKnown);
        Assert.AreEqual(300, store.RetailItemLots[0].RemainingAcquisitionCostCents);
        Assert.AreEqual(19700, store.CurrentCashCents);

        PurchasedRetailLotReceipt.Result duplicate = PurchasedRetailLotReceipt.Transfer(
            service, store, sourceLot, "animal_feed", "sack", 0);
        Assert.IsFalse(duplicate.Received);
        Assert.IsTrue(duplicate.Duplicate);
        Assert.AreEqual(20, store.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(19700, store.CurrentCashCents);
    }

    [Test]
    public void RefusedDestinationReceiptRestoresPurchasedSourceCustody()
    {
        // Harness note: see test 1 — no AssetDatabase in the harness.
        BusinessRuntimeState store = FeedDestination("general_store_test_feed_receipt_rollback");
        store.SetCurrentCashCents(20000);

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
            store.BusinessId, market, new Series2BusinessRuntimeCashPort(store));
        NamedSkuOrder order = service.PlaceOrder("store_horse_feed_sack", 20, 15, 0,
            supplier.CounterpartyId, 0, NamedSkuPaymentMode.CashOnArrival, null);
        Assert.IsNotNull(order);
        service.ReceiveArrivals(order.ExpectedArrivalDayIndex, null);
        NamedSkuLot sourceLot = service.Lots[0];

        PurchasedRetailLotReceipt.Result result = PurchasedRetailLotReceipt.Transfer(
            service, store, sourceLot, "missing_feed_category", "sack", order.ExpectedArrivalDayIndex);

        Assert.IsFalse(result.Received);
        Assert.IsTrue(result.SourceRestored);
        Assert.AreEqual(20, sourceLot.RemainingUnits);
        Assert.AreEqual(0, service.LotConsumptions.Count);
        Assert.AreEqual(0, store.RetailItemLots.Count);
        Assert.AreEqual(19700, store.CurrentCashCents,
            "The already-settled supplier purchase remains paid while its purchased source lot remains in custody for retry.");
    }

    [Test]
    public void PartialOffMapFeedReceipt_RestoresSourceWhenCapacityIsShort_ThenRetriesSameLot()
    {
        BusinessRuntimeState store = FeedDestination("general_store_partial_feed_capacity");
        store.SetCurrentCashCents(5000);
        store.EnsureCategoryStorageCapacity("animal_feed", 19);
        Assert.AreEqual(13, store.AddCategoryStockUnits("animal_feed", 13),
            "the store starts with the 13 sacks already counted by the partial-order planner");

        var supplier = new ExternalCounterparty
        {
            CounterpartyId = "millbrook-partial-feed-capacity",
            DisplayName = "Millbrook Grain Co.",
            InitialBudgetCents = 0,
            TransitDays = 0,
        };
        supplier.SupplyUnits["store_horse_feed_sack"] = 7;
        var market = new ExternalMarketAuthority();
        Assert.IsNull(market.RegisterCounterparty(supplier));
        var service = new OffMapNamedSkuProcurementService(
            store.BusinessId, market, new Series2BusinessRuntimeCashPort(store));
        NamedSkuOrder order = service.PlaceOrder(
            "store_horse_feed_sack", 7, 15, 0, supplier.CounterpartyId, 0,
            NamedSkuPaymentMode.CashOnArrival, null);
        Assert.IsNotNull(order);
        Assert.AreEqual(1, service.ReceiveArrivals(0, null).Count);
        NamedSkuLot sourceLot = service.Lots[0];
        Assert.AreEqual(4895, store.CurrentCashCents,
            "the 105c supplier payment occurs once before retail-shelf receiving");

        PurchasedRetailLotReceipt.Result refused = PurchasedRetailLotReceipt.Transfer(
            service, store, sourceLot, "animal_feed", "sack", 0);

        Assert.IsFalse(refused.Received);
        Assert.IsTrue(refused.SourceRestored, refused.Refusal);
        Assert.AreEqual(7, sourceLot.RemainingUnits,
            "a seven-sack deficit lot remains in paid source custody when only five shelf spaces remain");
        Assert.AreEqual(13, store.GetCategoryStock("animal_feed").CurrentStockUnits,
            "refusal leaves the original 13 store units untouched");
        Assert.AreEqual(0, store.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(0, store.RetailItemLots.Count);
        Assert.AreEqual(4895, store.CurrentCashCents,
            "a custody retry never charges the supplier a second time");

        store.EnsureCategoryStorageCapacity("animal_feed", 20);
        PurchasedRetailLotReceipt.Result retry = PurchasedRetailLotReceipt.Transfer(
            service, store, sourceLot, "animal_feed", "sack", 1);

        Assert.IsTrue(retry.Received, retry.Refusal);
        Assert.AreEqual(7, retry.Units);
        Assert.AreEqual(0, sourceLot.RemainingUnits);
        Assert.AreEqual(20, store.GetCategoryStock("animal_feed").CurrentStockUnits,
            "the seven delivered units fill the 13-to-20 coverage target exactly");
        Assert.AreEqual(7, store.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
        Assert.AreEqual(1, store.RetailItemLots.Count);
        Assert.AreEqual(4895, store.CurrentCashCents,
            "receiving transfers custody only; no second payment occurs");
    }

    [Test]
    public void BusinessHorseFeedAdapter_UsesTwoFeedUnitsPerSackAndCarriesRemainder()
    {
        // Harness note: see test 1 — no AssetDatabase in the harness.
        // FeedDestination already creates animal_feed at (0, 0).
        BusinessRuntimeState runtime = FeedDestination("livery_feed_adapter_test");
        Assert.IsNull(runtime.TryAddRetailItemLot(
            "feed-adapter-test-lot", "store_horse_feed_sack", "animal_feed", "sack", 2,
            runtime.BusinessId, runtime.BusinessId, "test purchase receipt", 0));

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

    // Fixture note: the pre-existing tests in this file used to build
    // their destination through AssetDatabase.LoadAssetAtPath, which
    // returns null in the Roslyn harness (the .asset is Unity-only), so
    // they only passed in the Unity Test Runner. S3 (universal business
    // campaign 2026-11-17) rebuilt them on the bare BusinessRuntimeState
    // fixture below (BindBusinessIdentity + EnsureCategoryStock) — the
    // same fixture the W4 tests use — with every behavioural assertion
    // unchanged, so the receipt/ledger behaviour is now pinned in the
    // harness too. The definition was only ever a construction vehicle:
    // CreateRuntimeState seeds no cash and no physical stock, only
    // category targets, and the tests set cash and stock explicitly.
    // (The storage test builds its runtime directly instead of via
    // FeedDestination, because EnsureCategoryStock never updates an
    // existing category and that test needs animal_feed at (1, 6).)
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
