using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using NUnit.Framework;

/// <summary>
/// Plan 02 §7 tests 13, 15–18 (Plan 10 slice E1): the repeatable
/// Millbrook flour reorder. The opening order is placed once by
/// DriveMillbrookAidTable; these tests pin the second-and-later order
/// path (PlaceReorder) and the negotiation predicate (FindShippableLot).
/// </summary>
[TestFixture]
public sealed class MillbrookFlourReorderTests
{
    private const string BuyerBusinessId = "bakery_001";
    private const string BuyerName = "Glen Pierce";

    private sealed class FakeBusinessCashPort : IBusinessCashPort
    {
        public int BalanceCents { get; private set; }

        public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;

        public string TryDebit(int amountCents, string reason)
        {
            if (amountCents <= 0 || amountCents > BalanceCents)
                return "test cash port refused debit";
            BalanceCents -= amountCents;
            return null;
        }

        public string Credit(int amountCents, string reason)
        {
            if (amountCents <= 0) return "test cash port refused credit";
            BalanceCents += amountCents;
            return null;
        }
    }

    private static OffMapAidBusinessRuntime NewSupplier(int startingUnits = 230, int weeklyReplenishUnits = 0)
    {
        // The production Millbrook definition is asset-backed
        // (Resources.Load) and the harness has no Resources shim, so the
        // test builds the same table in code: values copied from
        // MillbrookGrainCo.asset (230 lb starting, 48c/sack, 10 lb per
        // sack, 18 bakery units per sack). Production still loads the
        // asset; this mirrors it, it does not replace it.
        var definition = new OffMapAidBusinessDefinition
        {
            BusinessId = "millbrook_grain_co",
            DisplayName = "Millbrook Grain Co.",
            PickupRouteMilesFromTown = 50f,
            Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack",
                    DisplayName = "Flour",
                    InventoryUnit = "lb",
                    StartingUnits = startingUnits,
                    OrderUnit = "10 lb sack",
                    InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18,
                    OrderPriceCents = 48,
                },
            },
            // Optional authored weekly replenishment (same shape as the
            // production row the flour drive adds): min == max makes
            // the weekly amount exact, so ApplyWeeklyReplenishment(7)
            // adds exactly weeklyReplenishUnits lb for week 1.
            Production = weeklyReplenishUnits > 0
                ? new List<OffMapAidProductionRow>
                {
                    new OffMapAidProductionRow
                    {
                        ItemId = "bakery_flour_sack",
                        WeeklyMinimumUnits = weeklyReplenishUnits,
                        WeeklyMaximumUnits = weeklyReplenishUnits,
                        TargetReserveUnits = 460,
                    },
                }
                : new List<OffMapAidProductionRow>(),
        };
        return new OffMapAidBusinessRuntime(definition);
    }

    [Test]
    public void RefusedRequestLotBlocksAllLaterReorders()
    {
        // Plan 07 Slice 0 discriminator, reorder half (AUD-334): the
        // bricked lot is uncommitted and unreceived forever, so the
        // single-outstanding rule refuses every later reorder naming it.
        // The lot itself is fine; the request for it is the bricked part.
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 0, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNotNull(firstLot);
        Assert.AreEqual("aidlot-millbrook_grain_co:flour-reorder-d0-n1", firstLot.LotId);
        Assert.AreEqual(48, firstLot.TotalCostCents, "1 sack x 48c paid");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "buyer debited exactly 48c");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "230 lb - 10 lb");
        Assert.AreSame(firstLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "the lot is shippable; only its freight request is bricked");

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 0, diag, out OffMapAidCargoLot duplicateLot);

        Assert.IsNotNull(refusal, "a second reorder for a covered need must refuse");
        Assert.IsTrue(refusal.Contains(firstLot.LotId),
            "refusal names the outstanding lot; refusal: " + refusal);
        Assert.IsNull(duplicateLot);
        Assert.AreEqual(1, supplier.Lots.Count, "no second lot placed");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "refused attempt moves no buyer cash");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "refused attempt moves no stock");
    }

    [Test]
    public void ReopenedLotUnblocksThePipeline()
    {
        // Plan 07 Slice 1b (Q02, AUD-334): after the book-level reopen,
        // the same lot is still the shippable lot and the
        // single-outstanding rule still correctly refuses a duplicate
        // reorder while it is outstanding; once the lot commits,
        // FindShippableLot moves to the next lot.
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 0, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 0, diag, out OffMapAidCargoLot secondLot));
        Assert.AreSame(firstLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId));

        var book = new FreightServiceAgreementBook();
        string requestId = "freight-request:" + firstLot.OrderId;
        var request = new FreightServiceRequest
        {
            RequestId = requestId, RequestingBusinessId = BuyerBusinessId,
            CarrierBusinessId = "livery_freight_003", PickupBusinessId = "millbrook_grain_co",
            DestinationBusinessId = BuyerBusinessId, CargoLotId = firstLot.LotId, Quantity = 1,
            RequestingPersonId = 4, CarrierOperatorPersonId = 7
        };
        Assert.IsNull(book.CreateRequest(request, out _));
        Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.MeetingScheduled));
        Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.Refused,
            "Bakery B1 has 40c available for a 125c agreed charge."));
        Assert.IsNull(book.ReopenRefusedRequest(requestId,
            "Lot '" + firstLot.LotId + "' is still shippable; reopening the refused request for a retried meeting."));
        Assert.AreEqual(FreightServiceRequestStatus.Open, book.FindRequest(requestId).Status);

        Assert.AreSame(firstLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "the reopened request still targets the same lot");
        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 0, diag, out OffMapAidCargoLot duplicateLot);
        Assert.IsNotNull(refusal, "the outstanding lot still correctly blocks a duplicate reorder");
        Assert.IsTrue(refusal.Contains(firstLot.LotId), "refusal: " + refusal);
        Assert.IsNull(duplicateLot);

        Assert.IsNull(supplier.CommitFreightPickup(firstLot.LotId, "shipment-1", 1),
            "commit of the first lot should succeed");
        Assert.AreSame(secondLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "after the first lot commits, the next lot is shippable");
    }

    [Test]
    public void PlaceReorder_PlacesUniqueLot_BuyerDebitedOnce_SupplierStockDebited()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNull(refusal, "reorder should succeed; diag: " + string.Join(" | ", diag));
        Assert.IsNotNull(lot);
        Assert.AreEqual("aidlot-millbrook_grain_co:flour-reorder-d3-n1", lot.LotId);
        Assert.AreEqual("millbrook_grain_co:flour-reorder-d3-n1", lot.OrderId);
        Assert.AreEqual(2, lot.PackageUnits);
        Assert.AreEqual(20, lot.InventoryUnits, "2 sacks = 20 lb");
        Assert.AreEqual(96, lot.TotalCostCents, "2 sacks x 48c");
        Assert.AreEqual(19904, bakeryCash.BalanceCents, "buyer debited exactly once");
        Assert.AreEqual(96, supplier.CashCents);
        Assert.AreEqual(210, supplier.GetInventoryUnits("bakery_flour_sack"), "230 lb - 20 lb");
        Assert.AreEqual(1, supplier.Lots.Count);
    }

    [Test]
    public void PlaceReorder_OutstandingLotBlocksDuplicate_RefusalNamesTheLot()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNotNull(firstLot);

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot duplicateLot);

        Assert.IsNotNull(refusal, "a duplicate reorder for a covered need must refuse");
        Assert.IsTrue(refusal.Contains(firstLot.LotId),
            "refusal names the outstanding lot; refusal: " + refusal);
        Assert.IsNull(duplicateLot);
        Assert.AreEqual(1, supplier.Lots.Count, "no second lot placed");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "buyer debited once only (48c for the first lot)");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "supplier stock debited once only");
    }

    [Test]
    public void PlaceReorder_OutstandingLotSmallerThanNeed_PlacesSecondLot()
    {
        // The guard is "covers the need", not "any outstanding lot": a
        // 1-sack outstanding lot does not cover a 2-sack need, so the
        // second order is placed with a distinct request id.
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot firstLot));
        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 3, diag, out OffMapAidCargoLot secondLot);

        Assert.IsNull(refusal, "refusal: " + refusal);
        Assert.IsNotNull(secondLot);
        Assert.AreNotEqual(firstLot.LotId, secondLot.LotId);
        Assert.AreEqual("aidlot-millbrook_grain_co:flour-reorder-d3-n2", secondLot.LotId);
        Assert.AreEqual(2, supplier.Lots.Count);
    }

    [Test]
    public void PlaceReorder_ZeroSacksRefuses_NothingMoved()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 0, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNotNull(refusal);
        Assert.IsNull(lot);
        Assert.AreEqual(0, supplier.Lots.Count);
        Assert.AreEqual(20000, bakeryCash.BalanceCents);
        Assert.AreEqual(230, supplier.GetInventoryUnits("bakery_flour_sack"));
    }

    [Test]
    public void PlaceReorder_InsufficientSupplierStockClampsToAvailable()
    {
        // Priority 3 Slice 4b DELIBERATE TEST UPDATE (not a regression):
        // this test formerly pinned all-or-nothing refusal
        // (PlaceReorder_InsufficientSupplierStockRefuses_NothingMoved:
        // request 24 sacks vs 23 available -> refusal, nothing moved).
        // The Slice 4b repair changes the rule to partial fill: the
        // order is clamped to the stock Millbrook actually holds and
        // refused only when none is available (0 sacks is still a
        // refusal — pinned by
        // PlaceReorder_PartialFill_ZeroAvailableStillRefuses_NothingMoved).
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        // Fresh Millbrook stock is 230 lb = 23 sacks; 24 sacks are requested.
        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 24, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNull(refusal, "the request clamps to available stock instead of refusing; diag: " + string.Join(" | ", diag));
        Assert.IsNotNull(lot);
        Assert.AreEqual(23, lot.PackageUnits, "clamped to the 23 sacks Millbrook holds");
        Assert.AreEqual(230, lot.InventoryUnits, "23 sacks = 230 lb");
        Assert.AreEqual(23 * 48, lot.TotalCostCents, "23 sacks x 48c");
        Assert.AreEqual(1, supplier.Lots.Count, "exactly one clamped lot");
        Assert.AreEqual(1, supplier.CashTransactions.Count, "exactly one cash transaction");
        Assert.AreEqual(20000 - 23 * 48, bakeryCash.BalanceCents, "buyer debited exactly once for the clamped lot");
        Assert.AreEqual(23 * 48, supplier.CashCents, "Millbrook paid exactly 48c per clamped sack");
        Assert.AreEqual(0, supplier.GetInventoryUnits("bakery_flour_sack"), "supplier stock fully drawn");
        Assert.IsTrue(string.Join(" | ", diag).Contains("clamped"),
            "the diag names the clamp; diag: " + string.Join(" | ", diag));
    }

    [Test]
    public void PlaceReorder_PartialFill_OrdersAvailableWhenRequestExceedsStock()
    {
        // Priority 3 Slice 4b: the all-or-nothing stock guard refused
        // a fulfillable majority (18 requested vs 16 held) and ordered
        // 0; partial fill orders the 16 available sacks instead.
        var supplier = NewSupplier(160);
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 18, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNull(refusal, "the request clamps to available stock instead of refusing; diag: " + string.Join(" | ", diag));
        Assert.IsNotNull(lot);
        Assert.AreEqual(16, lot.PackageUnits, "clamped to the 16 sacks Millbrook holds");
        Assert.AreEqual(160, lot.InventoryUnits, "16 sacks = 160 lb");
        Assert.AreEqual(768, lot.TotalCostCents, "16 sacks x 48c");
        Assert.AreEqual(1, supplier.Lots.Count, "exactly one lot");
        Assert.AreEqual(1, supplier.CashTransactions.Count, "exactly one cash transaction");
        Assert.AreEqual(20000 - 768, bakeryCash.BalanceCents, "buyer debited exactly 768c once");
        Assert.AreEqual(768, supplier.CashCents, "Millbrook paid exactly 48c per sack");
        Assert.AreEqual(0, supplier.GetInventoryUnits("bakery_flour_sack"), "supplier stock fully drawn");
        Assert.IsTrue(string.Join(" | ", diag).Contains("clamped: requested 18 sack(s)"),
            "the diag names the clamp (requested vs holds vs ordering); diag: " + string.Join(" | ", diag));
    }

    [Test]
    public void PlaceReorder_PartialFill_ZeroAvailableStillRefuses_NothingMoved()
    {
        // Priority 3 Slice 4b: the clamp never orders 0 sacks — when
        // Millbrook holds nothing the existing refusal stands verbatim.
        // Passes before and after the fix; it guards the repair, not
        // the defect.
        var supplier = NewSupplier(0);
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 5, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNotNull(refusal, "zero available stock still refuses");
        Assert.IsTrue(refusal.Contains("0 sack(s)"), "refusal names 0 sacks available; refusal: " + refusal);
        Assert.IsNull(lot);
        Assert.AreEqual(0, supplier.Lots.Count);
        Assert.AreEqual(20000, bakeryCash.BalanceCents, "refused attempt moves no buyer cash");
        Assert.AreEqual(0, supplier.GetInventoryUnits("bakery_flour_sack"), "supplier stock unchanged");
    }

    [Test]
    public void PlaceReorder_PartialFill_ExactStock_Unchanged()
    {
        // Priority 3 Slice 4b: request == available orders the full
        // lot, identical to today's behaviour. Passes before and after
        // the fix; guards against over-clamping.
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 23, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNull(refusal, "refusal: " + refusal);
        Assert.IsNotNull(lot);
        Assert.AreEqual(23, lot.PackageUnits);
        Assert.AreEqual(230, lot.InventoryUnits, "23 sacks = 230 lb");
        Assert.AreEqual(23 * 48, lot.TotalCostCents, "23 sacks x 48c");
        Assert.AreEqual(1, supplier.Lots.Count);
        Assert.AreEqual(20000 - 23 * 48, bakeryCash.BalanceCents, "buyer debited exactly once");
        Assert.AreEqual(0, supplier.GetInventoryUnits("bakery_flour_sack"));
    }

    [Test]
    public void PlaceReorder_PartialFill_ClampedLot_StillGuardsDuplicate()
    {
        // Priority 3 Slice 4b: guard (a) is untouched, so it still
        // works on a clamped lot — a second request covered by the
        // clamped outstanding lot refuses naming that lot.
        //
        // NOTE (test-authoring correction): a clamp by definition
        // orders every sack Millbrook holds, so the supplier sits at
        // 0 lb after the clamped order and a second request would
        // refuse on stock (guard (b), layer 1) before guard (a) is
        // ever evaluated. The second request can only reach guard (a)
        // once stock returns — here via the authored weekly
        // replenishment, the same mechanism the 60-day flour drive
        // uses: the supplier carries a 50 lb/week production row,
        // ApplyWeeklyReplenishment(7) lifts it to 5 sacks, and the
        // 2-sack request then passes the stock guard and meets
        // guard (a) (outstanding 16 >= 2).
        var supplier = NewSupplier(160, weeklyReplenishUnits: 50);
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 18, 3, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNotNull(firstLot);
        Assert.AreEqual(16, firstLot.PackageUnits, "the first order clamped to 16 sacks");
        Assert.AreEqual(0, supplier.GetInventoryUnits("bakery_flour_sack"), "the clamp draws all available stock");

        supplier.ApplyWeeklyReplenishment(7);
        Assert.AreEqual(50, supplier.GetInventoryUnits("bakery_flour_sack"),
            "week-1 replenishment returns 50 lb (5 sacks) to Millbrook");

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 3, diag, out OffMapAidCargoLot duplicateLot);

        Assert.IsNotNull(refusal, "a second reorder covered by the clamped outstanding lot must refuse");
        Assert.IsTrue(refusal.Contains(firstLot.LotId),
            "refusal names the clamped outstanding lot; refusal: " + refusal);
        Assert.IsNull(duplicateLot);
        Assert.AreEqual(1, supplier.Lots.Count, "no second lot placed");
        Assert.AreEqual(20000 - 768, bakeryCash.BalanceCents, "buyer debited once only (768c for the clamped lot)");
        Assert.AreEqual(50, supplier.GetInventoryUnits("bakery_flour_sack"), "the refused attempt moves no stock");
    }

    [Test]
    public void PlaceReorder_InsufficientBuyerCashRefuses_NothingMoved()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(10);
        var diag = new List<string>();

        string refusal = MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot lot);

        Assert.IsNotNull(refusal);
        Assert.IsNull(lot);
        Assert.AreEqual(0, supplier.Lots.Count);
        Assert.AreEqual(10, bakeryCash.BalanceCents);
        Assert.AreEqual(230, supplier.GetInventoryUnits("bakery_flour_sack"));
    }

    [Test]
    public void FindShippableLot_ReturnsOldestThenNextThenNull()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);
        var diag = new List<string>();

        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "no lots yet — nothing shippable");
        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 1, 3, diag, out OffMapAidCargoLot firstLot));
        Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
            supplier, bakeryCash, BuyerBusinessId, BuyerName, 2, 3, diag, out OffMapAidCargoLot secondLot));

        Assert.AreSame(firstLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "oldest uncommitted lot first");
        Assert.IsNull(supplier.CommitFreightPickup(firstLot.LotId, "shipment-1", 1),
            "commit of the first lot should succeed");
        Assert.AreSame(secondLot, MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "after the first lot commits, the next uncommitted lot is shippable");
        Assert.IsNull(supplier.CommitFreightPickup(secondLot.LotId, "shipment-2", 2));
        Assert.IsNull(supplier.ReceiveAtBakery(secondLot.LotId, "shipment-2", BuyerBusinessId, out int bakeryUnits));
        Assert.AreEqual(36, bakeryUnits, "2 sacks x 18 bakery units (provenance only)");
        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "both lots committed/received — nothing shippable");
        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, "some-other-buyer"),
            "lots for another buyer are never shippable for this buyer");
    }
}
