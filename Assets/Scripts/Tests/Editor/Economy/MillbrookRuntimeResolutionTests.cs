using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Orchestration.Bootstrap;
using NUnit.Framework;

/// <summary>
/// Plan 04 Slice 0 (P0-3): pure-contract tests pinning what the Millbrook
/// attach path depends on — the runtime is a faithful authority when held,
/// the reorder predicate finds the opening lot, save round-trips are
/// idempotent, and the single-instance claim guard refuses a second holder.
/// These tests do NOT prove the drivers ever hold the runtime — that is the
/// Unity-owed Slice 0(c)/(d) check (attached instance vs driving instance).
/// The definition is built in code because the harness has no Resources shim;
/// values are copied from MillbrookGrainCo.asset (230 lb starting, 48c/sack,
/// 10 lb per sack, 18 bakery units per sack), never re-derived.
/// </summary>
[TestFixture]
public sealed class MillbrookRuntimeResolutionTests
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

    private static OffMapAidBusinessRuntime NewSupplier()
    {
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
                    StartingUnits = 230,
                    OrderUnit = "10 lb sack",
                    InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18,
                    OrderPriceCents = 48,
                },
            },
        };
        return new OffMapAidBusinessRuntime(definition);
    }

    private static OffMapAidBusinessRuntime PlaceOpeningOrder(out FakeBusinessCashPort bakeryCash)
    {
        var supplier = NewSupplier();
        bakeryCash = new FakeBusinessCashPort(20000);
        Assert.IsNull(supplier.TryPlaceOrder(
            "opening-flour-b1", BuyerBusinessId, BuyerName, 1, bakeryCash, 0, out OffMapAidCargoLot lot));
        Assert.IsNotNull(lot);
        return supplier;
    }

    [Test]
    public void Definition_BuildsRuntimeWithStableIdentityAndAuthoredStock()
    {
        var supplier = NewSupplier();

        Assert.AreEqual("millbrook_grain_co", supplier.BusinessId);
        Assert.AreEqual("Millbrook Grain Co.", supplier.DisplayName);
        Assert.AreEqual(230, supplier.GetInventoryUnits("bakery_flour_sack"));
        Assert.AreEqual(0, supplier.CashCents);
        Assert.AreEqual(0, supplier.Lots.Count);
    }

    [Test]
    public void OpeningOrder_SucceedsOnce_BuyerAndSupplierMoveExactlyOnce()
    {
        var supplier = NewSupplier();
        var bakeryCash = new FakeBusinessCashPort(20000);

        string refusal = supplier.TryPlaceOrder(
            "opening-flour-b1", BuyerBusinessId, BuyerName, 1, bakeryCash, 0, out OffMapAidCargoLot lot);

        Assert.IsNull(refusal);
        Assert.IsNotNull(lot);
        Assert.AreEqual("aidlot-millbrook_grain_co:opening-flour-b1", lot.LotId);
        Assert.AreEqual("millbrook_grain_co:opening-flour-b1", lot.OrderId);
        Assert.AreEqual("bakery_flour_sack", lot.SkuId);
        Assert.AreEqual("millbrook_grain_co", lot.SupplierBusinessId);
        Assert.AreEqual(BuyerBusinessId, lot.BuyerBusinessId);
        Assert.AreEqual(1, lot.PackageUnits);
        Assert.AreEqual(10, lot.InventoryUnits, "1 sack = 10 lb");
        Assert.AreEqual(18, lot.BakeryUnits, "1 sack = 18 bakery units");
        Assert.AreEqual(48, lot.TotalCostCents, "1 sack x 48c");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "buyer debited exactly 48c");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "230 lb - 10 lb");
        Assert.AreEqual(48, supplier.CashCents, "supplier cash 0 -> 48c");
        Assert.AreEqual(1, supplier.Lots.Count);
        Assert.IsTrue(supplier.HasOrder("opening-flour-b1"));
    }

    [Test]
    public void OpeningOrder_DuplicateOrderIdRefused_NoSecondDebitNoSecondLot()
    {
        var supplier = PlaceOpeningOrder(out FakeBusinessCashPort bakeryCash);

        string refusal = supplier.TryPlaceOrder(
            "opening-flour-b1", BuyerBusinessId, BuyerName, 1, bakeryCash, 0, out OffMapAidCargoLot duplicateLot);

        Assert.IsNotNull(refusal, "a duplicate opening order id must refuse");
        Assert.IsNull(duplicateLot);
        Assert.AreEqual(1, supplier.Lots.Count, "no second lot placed");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "buyer debited once only");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "supplier stock debited once only");
        Assert.AreEqual(48, supplier.CashCents, "supplier cash credited once only");
    }

    [Test]
    public void FindShippableLot_ReturnsOpeningLotForBuyer_NullOtherwise()
    {
        var supplier = NewSupplier();

        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "no lots yet — nothing shippable");
        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(null, BuyerBusinessId),
            "a null supplier has no shippable lot");
        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, null),
            "a null buyer has no shippable lot");

        supplier = PlaceOpeningOrder(out FakeBusinessCashPort bakeryCash);
        OffMapAidCargoLot lot = MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId);
        Assert.IsNotNull(lot);
        Assert.AreEqual("aidlot-millbrook_grain_co:opening-flour-b1", lot.LotId);
        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, "some-other-buyer"),
            "lots for another buyer are never shippable for this buyer");

        Assert.IsNull(supplier.CommitFreightPickup(lot.LotId, "shipment-opening", 1),
            "commit of the opening lot should succeed");
        Assert.IsNull(MillbrookFlourReorder.FindShippableLot(supplier, BuyerBusinessId),
            "after the lot is committed, nothing is shippable");
        Assert.AreEqual(19952, bakeryCash.BalanceCents, "commit moves no cash");
    }

    [Test]
    public void SaveRoundTrip_PreservesStockCashAndLotIdentity_DoubleImportIsIdempotent()
    {
        var supplier = PlaceOpeningOrder(out FakeBusinessCashPort bakeryCash);
        OffMapAidBusinessSaveDto dto = supplier.ExportState();

        var restored = NewSupplier();
        Assert.IsNull(restored.ImportState(dto), "import of an exported state should succeed");
        Assert.AreEqual(220, restored.GetInventoryUnits("bakery_flour_sack"));
        Assert.AreEqual(48, restored.CashCents);
        Assert.AreEqual(1, restored.Lots.Count);
        Assert.AreEqual("aidlot-millbrook_grain_co:opening-flour-b1", restored.Lots[0].LotId);
        Assert.AreEqual("millbrook_grain_co:opening-flour-b1", restored.Lots[0].OrderId);
        Assert.AreEqual(BuyerBusinessId, restored.Lots[0].BuyerBusinessId);

        Assert.IsNull(restored.ImportState(dto), "a second import of the same state should succeed");
        Assert.AreEqual(220, restored.GetInventoryUnits("bakery_flour_sack"), "double import must not debit stock again");
        Assert.AreEqual(48, restored.CashCents, "double import must not credit cash again");
        Assert.AreEqual(1, restored.Lots.Count, "double import must not duplicate lots");
        Assert.AreEqual(1, restored.CashTransactions.Count, "double import must not duplicate transactions");
    }

    [Test]
    public void ImportState_MismatchedBusinessIdRefused_LiveStateUnchanged()
    {
        var supplier = PlaceOpeningOrder(out FakeBusinessCashPort bakeryCash);
        var foreignDto = new OffMapAidBusinessSaveDto
        {
            BusinessId = "other_supplier",
            CashCents = 999,
            InventoryItemIds = new List<string> { "bakery_flour_sack" },
            InventoryUnits = new List<int> { 5 },
        };

        string refusal = supplier.ImportState(foreignDto);

        Assert.IsNotNull(refusal, "an import for a different supplier must refuse");
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "live stock unchanged");
        Assert.AreEqual(48, supplier.CashCents, "live cash unchanged");
        Assert.AreEqual(1, supplier.Lots.Count, "live lots unchanged");
    }

    [Test]
    public void ImportState_NullDtoResetsToAuthoredStartingStockWithZeroLots()
    {
        var supplier = PlaceOpeningOrder(out FakeBusinessCashPort bakeryCash);
        Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"));
        Assert.AreEqual(1, supplier.Lots.Count);

        Assert.IsNull(supplier.ImportState(null), "a null DTO resets to the authored starting stock");

        Assert.AreEqual(230, supplier.GetInventoryUnits("bakery_flour_sack"));
        Assert.AreEqual(0, supplier.CashCents);
        Assert.AreEqual(0, supplier.Lots.Count);
        Assert.AreEqual(0, supplier.CashTransactions.Count);
    }

    [Test]
    public void Guard_SecondClaimRefused_NonHolderReleaseIgnored_ReclaimByHolderSucceeds()
    {
        const string authority = "MillbrookRuntimeResolutionGuardProbe";
        var firstHolder = new object();
        var secondHolder = new object();
        try
        {
            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim(authority, firstHolder, out string firstRefusal),
                "first claim should succeed; refusal: " + firstRefusal);
            Assert.IsTrue(SingleInstanceAuthorityGuard.IsClaimedBy(authority, firstHolder));
            Assert.IsTrue(!SingleInstanceAuthorityGuard.IsClaimedBy(authority, secondHolder));

            Assert.IsTrue(!SingleInstanceAuthorityGuard.TryClaim(authority, secondHolder, out string refusal),
                "a second holder must be refused while the first holds the claim");
            Assert.IsNotNull(refusal);

            Assert.IsTrue(!SingleInstanceAuthorityGuard.Release(authority, secondHolder),
                "a release by a non-holder is ignored, never a steal");
            Assert.IsTrue(SingleInstanceAuthorityGuard.IsClaimedBy(authority, firstHolder),
                "the claim still belongs to the first holder after the ignored release");

            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim(authority, firstHolder, out _),
                "a re-claim by the holder that already owns the claim succeeds");
            Assert.IsTrue(SingleInstanceAuthorityGuard.Release(authority, firstHolder));
            Assert.IsTrue(!SingleInstanceAuthorityGuard.IsClaimed(authority));
        }
        finally
        {
            SingleInstanceAuthorityGuard.Release(authority, firstHolder);
            SingleInstanceAuthorityGuard.Release(authority, secondHolder);
        }
    }

    [Test]
    public void Guard_ResetAllClearsClaims()
    {
        const string authority = "MillbrookRuntimeResolutionResetProbe";
        var holder = new object();
        try
        {
            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim(authority, holder, out _));
            Assert.IsTrue(SingleInstanceAuthorityGuard.IsClaimed(authority));

            SingleInstanceAuthorityGuard.ResetAll();

            Assert.IsTrue(!SingleInstanceAuthorityGuard.IsClaimed(authority), "ResetAll must clear the claim");
            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim(authority, holder, out string refusal),
                "the claim can be taken again after ResetAll; refusal: " + refusal);
        }
        finally
        {
            SingleInstanceAuthorityGuard.Release(authority, holder);
        }
    }

    [Test]
    public void Audit_TwoObjectCensusReportsOneErrorNamingBoth_OneObjectCensusReportsNone()
    {
        var twoObject = new BootstrapAuthorityAudit.Census
        {
            Name = "SimulationDrivers",
            ObjectNames = new List<string> { "Drivers (Scene)", "FirstLedgerSliceBootstrapper" },
        };
        var oneObject = new BootstrapAuthorityAudit.Census
        {
            Name = "SimulationDrivers",
            ObjectNames = new List<string> { "Drivers (Scene)" },
        };

        List<string> twoErrors = BootstrapAuthorityAudit.DescribeDuplicates(new List<BootstrapAuthorityAudit.Census> { twoObject });
        Assert.AreEqual(1, twoErrors.Count, "a two-object census must produce exactly one error");
        Assert.IsTrue(twoErrors[0].Contains("Drivers (Scene)"), "error names the first object; error: " + twoErrors[0]);
        Assert.IsTrue(twoErrors[0].Contains("FirstLedgerSliceBootstrapper"), "error names the second object; error: " + twoErrors[0]);

        List<string> oneErrors = BootstrapAuthorityAudit.DescribeDuplicates(new List<BootstrapAuthorityAudit.Census> { oneObject });
        Assert.AreEqual(0, oneErrors.Count, "a one-object census produces no error");
    }
}
