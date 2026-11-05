// Slice 29 (2026-11-05): Millbrook flour-freight custody refusal tests.
// The MillbrookFlourFreightCustodyAdapter is wired into the daily freight
// tick (SimulationDrivers binds CommitSourceLot / ReceiveCargoLot into
// FreightShipmentHooks), but its only test file
// (OffMapAidBusinessRuntimeTests.cs) is !-excluded from the Roslyn harness
// because it Resources.Loads the Millbrook asset, so the harness had never
// executed a single custody assertion. These nine tests pin the refusal
// branches against flour duplication / minting, plus a happy-path control.
// The fixture builds the supplier definition in code with row values copied
// verbatim from the authored Millbrook asset (10 lb / 18 bakery units /
// 48c / 230 starting); no production file is touched by this slice.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Primitives;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class MillbrookFlourFreightCustodyTests
    {
        private sealed class FakeBusinessCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }

            public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents) return "test cash port refused debit";
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

        private sealed class Setup
        {
            public OffMapAidBusinessRuntime Supplier;
            public BakeryShopRuntime Bakery;
            public MillbrookFlourFreightCustodyAdapter Adapter;
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public OffMapAidCargoLot Lot;
            public LogisticsShipmentState Shipment;
            public FakeBusinessCashPort BakeryCash;
        }

        private static OffMapAidBusinessDefinition MillbrookDefinition(string flourInventoryUnit)
        {
            var definition = ScriptableObject.CreateInstance<OffMapAidBusinessDefinition>();
            definition.BusinessId = "millbrook_grain_co";
            definition.DisplayName = "Millbrook Grain Co.";
            definition.PickupRouteMilesFromTown = 50f;
            definition.Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack", DisplayName = "Flour",
                    InventoryUnit = flourInventoryUnit, StartingUnits = 230,
                    OrderUnit = "10 lb sack", InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18, OrderPriceCents = 48,
                },
            };
            return definition;
        }

        private static Setup NewSetup(string flourInventoryUnit = "lb")
        {
            var s = new Setup();
            s.Supplier = new OffMapAidBusinessRuntime(MillbrookDefinition(flourInventoryUnit));
            s.BakeryCash = new FakeBusinessCashPort(20000);
            Assert.IsNull(s.Supplier.TryPlaceOrder(
                "custody-test", "bakery_001", "Glen Pierce", 1, s.BakeryCash, 0, out OffMapAidCargoLot lot),
                "fixture order must place");
            s.Lot = lot;
            s.Bakery = new BakeryShopRuntime("bakery_001");
            s.Adapter = new MillbrookFlourFreightCustodyAdapter(s.Supplier, s.Bakery, s.Ids);
            s.Shipment = new LogisticsShipmentState
            {
                shipmentId = "test-millbrook-to-bakery",
                sourceBusinessInstanceId = s.Supplier.BusinessId,
                destinationBusinessInstanceId = s.Bakery.BusinessInstanceId,
                cargoLotId = s.Lot.LotId,
                cargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId,
                plannedQuantityUnits = s.Lot.PackageUnits,
                requiresSourceLotCustody = true,
            };
            return s;
        }

        private static LogisticsShipmentState ShipmentLike(Setup s,
            string shipmentId = null, string sourceBusinessInstanceId = null, string cargoLotId = null)
        {
            return new LogisticsShipmentState
            {
                shipmentId = shipmentId ?? s.Shipment.shipmentId,
                sourceBusinessInstanceId = sourceBusinessInstanceId ?? s.Shipment.sourceBusinessInstanceId,
                destinationBusinessInstanceId = s.Shipment.destinationBusinessInstanceId,
                cargoLotId = cargoLotId ?? s.Shipment.cargoLotId,
                cargoSkuId = s.Shipment.cargoSkuId,
                plannedQuantityUnits = s.Shipment.plannedQuantityUnits,
                requiresSourceLotCustody = true,
            };
        }

        private static void AssertReasonContains(string reason, string fragment)
        {
            Assert.IsTrue(!string.IsNullOrEmpty(reason) && reason.Contains(fragment),
                "expected refusal reason to contain '" + fragment + "' but got '" + reason + "'");
        }

        [Test]
        public void CommitThenReceive_DeliversTenPoundsOnce_DuplicateReceiptIsIdempotent()
        {
            Setup s = NewSetup();

            // Fixture facts, mirroring the authored Millbrook asset and the
            // Unity-only test: 1 sack = 10 lb = 18 bakery units = 48c.
            Assert.AreEqual("aidlot-millbrook_grain_co:custody-test", s.Lot.LotId);
            Assert.AreEqual(1, s.Lot.PackageUnits);
            Assert.AreEqual(10, s.Lot.InventoryUnits);
            Assert.AreEqual("lb", s.Lot.InventoryUnit);
            Assert.AreEqual(18, s.Lot.BakeryUnits);
            Assert.AreEqual(48, s.Lot.TotalCostCents);
            Assert.AreEqual(220, s.Supplier.GetInventoryUnits("bakery_flour_sack"));
            Assert.AreEqual(19952, s.BakeryCash.BalanceCents);
            Assert.AreEqual(48, s.Supplier.CashCents);

            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, 1, out string pickupReason), pickupReason);
            Assert.IsTrue(s.Adapter.ReceiveCargoLot(s.Shipment, 1, 1, out string receiptReason), receiptReason);
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count);
            Assert.AreEqual(10, s.Bakery.FlourStock.Lots[0].Units,
                "The inventory transfer is pounds, not the separate 18 bakery-unit quotation.");
            Assert.AreEqual(BakeryBreadCatalog.FlourItemId, s.Bakery.FlourStock.Lots[0].FlourName);
            Assert.AreEqual(s.Lot.LotId, s.Bakery.FlourStock.Lots[0].ImportOrderId);
            Assert.IsTrue(s.Lot.SourceCommitted);
            Assert.IsTrue(s.Lot.Received);

            Assert.IsTrue(s.Adapter.ReceiveCargoLot(s.Shipment, 1, 1, out string duplicateReason), duplicateReason);
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count,
                "A repeated shipment receipt must not duplicate the flour lot.");
            Assert.AreEqual(220, s.Supplier.GetInventoryUnits("bakery_flour_sack"));
        }

        [Test]
        public void ReceiveWithoutCommit_RefusesAndMintsNothing()
        {
            Setup s = NewSetup();

            Assert.IsTrue(!s.Adapter.ReceiveCargoLot(s.Shipment, 1, 1, out string reason), "receive must refuse");
            AssertReasonContains(reason, "not in this shipment's wagon custody");
            Assert.AreEqual(0, s.Bakery.FlourStock.Lots.Count);
            Assert.IsTrue(!s.Lot.Received);
            Assert.AreEqual(220, s.Supplier.GetInventoryUnits("bakery_flour_sack"));
        }

        [Test]
        public void ReceiveViaWrongShipment_RefusesAndMintsNothing()
        {
            Setup s = NewSetup();
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, 1, out string pickupReason), pickupReason);

            LogisticsShipmentState otherShipment = ShipmentLike(s, shipmentId: "test-millbrook-to-bakery-2");
            Assert.IsTrue(!s.Adapter.ReceiveCargoLot(otherShipment, 1, 1, out string reason), "receive must refuse");
            AssertReasonContains(reason, "not in this shipment's wagon custody");
            Assert.AreEqual(0, s.Bakery.FlourStock.Lots.Count);
            // The lot stays committed to the first shipment only.
            Assert.IsTrue(s.Lot.SourceCommitted);
            Assert.AreEqual("test-millbrook-to-bakery", s.Lot.ShipmentId);
            Assert.IsTrue(!s.Lot.Received);
        }

        [Test]
        public void CommitWrongPackageQuantity_RefusesAndLeavesLotUncommitted()
        {
            Setup s = NewSetup();

            Assert.IsTrue(!s.Adapter.CommitSourceLot(s.Shipment, 2, out string reason), "commit must refuse");
            AssertReasonContains(reason, "does not match");
            Assert.IsTrue(!s.Lot.SourceCommitted);
            Assert.AreEqual(0, s.Bakery.FlourStock.Lots.Count);

            // A quantity mismatch refuses cleanly with no partial commit
            // state: the correct commit still succeeds afterwards.
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, 1, out string retryReason), retryReason);
            Assert.IsTrue(s.Lot.SourceCommitted);
        }

        [Test]
        public void CommitWrongSource_Refuses()
        {
            Setup s = NewSetup();
            LogisticsShipmentState wrongSource = ShipmentLike(s, sourceBusinessInstanceId: "ashford_mill");

            Assert.IsTrue(!s.Adapter.CommitSourceLot(wrongSource, 1, out string reason), "commit must refuse");
            AssertReasonContains(reason, "does not match the Millbrook-to-Bakery order");
            Assert.IsTrue(!s.Lot.SourceCommitted);
            Assert.AreEqual(0, s.Bakery.FlourStock.Lots.Count);
        }

        [Test]
        public void CommitMissingLot_Refuses()
        {
            Setup s = NewSetup();
            LogisticsShipmentState missingLot = ShipmentLike(s, cargoLotId: "aidlot-millbrook_grain_co:no-such-order");

            Assert.IsTrue(!s.Adapter.CommitSourceLot(missingLot, 1, out string reason), "commit must refuse");
            AssertReasonContains(reason, "does not exist");
            Assert.IsTrue(!s.Lot.SourceCommitted);
        }

        [Test]
        public void CommitAfterReceipt_Refuses()
        {
            Setup s = NewSetup();
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, 1, out string pickupReason), pickupReason);
            Assert.IsTrue(s.Adapter.ReceiveCargoLot(s.Shipment, 1, 1, out string receiptReason), receiptReason);

            Assert.IsTrue(!s.Adapter.CommitSourceLot(s.Shipment, 1, out string reason), "re-commit must refuse");
            AssertReasonContains(reason, "already been received");
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count);
            Assert.AreEqual(10, s.Bakery.FlourStock.Lots[0].Units);
        }

        [Test]
        public void ConflictingReceipt_RefusesAndAddsNothing()
        {
            Setup s = NewSetup();
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, 1, out string pickupReason), pickupReason);

            // Seed the bin directly with a prior receipt for the same source
            // lot but a different pound count (999 is a test-only corrupt
            // value fed to the guard, not a production rate).
            var seed = new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 999,
                AcquiredDayIndex = 0,
                ImportOrderId = s.Lot.LotId,
                OriginName = "Millbrook test seed",
            };
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(seed, new List<string>()),
                "fixture seed receipt must be accepted by the bin");

            Assert.IsTrue(!s.Adapter.ReceiveCargoLot(s.Shipment, 1, 1, out string reason), "receive must refuse");
            AssertReasonContains(reason, "conflicting receipt");
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count);
            Assert.AreEqual(999, s.Bakery.FlourStock.Lots[0].Units);
        }

        [Test]
        public void NonPoundLot_RefusesAtReceipt()
        {
            // Misconfigured row: the unit label is "sacks" instead of "lb".
            // ValidateDefinition does not constrain the unit label and
            // TryPlaceOrder still places, so the pound guard at receipt is
            // the custody boundary.
            Setup s = NewSetup("sacks");
            Assert.AreEqual("sacks", s.Lot.InventoryUnit);
            Assert.AreEqual(10, s.Lot.InventoryUnits);

            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, 1, out string pickupReason),
                "commit does not check the unit: " + pickupReason);
            Assert.IsTrue(!s.Adapter.ReceiveCargoLot(s.Shipment, 1, 1, out string reason), "receive must refuse");
            AssertReasonContains(reason, "no valid pound quantity");
            Assert.AreEqual(0, s.Bakery.FlourStock.Lots.Count);
            Assert.IsTrue(!s.Lot.Received);
        }
    }
}
