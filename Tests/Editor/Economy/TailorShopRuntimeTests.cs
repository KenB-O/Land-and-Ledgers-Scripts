using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Tailor;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1C: the tailor shop runtime — cutting-table throughput, the garment
    /// order pipeline, cloth gating with loud refusal, fittings as genuine
    /// steps, piece-rate delivery, and save/load.
    /// </summary>
    [TestFixture]
    public sealed class TailorShopRuntimeTests
    {
        private static Func<string, WorkstationComponentView?> TableFinder(params string[] goodAssetIds)
        {
            var good = new HashSet<string>(goodAssetIds);
            return assetId => good.Contains(assetId)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = "cutting-table",
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static TailorShopRuntime OneTableShop(List<string> diag)
        {
            var runtime = new TailorShopRuntime("tail-biz-1");
            var registry = new EntityIdRegistry();
            TailorClothBootstrap.ApplyBootstrapEndowment(runtime.ClothStock, registry, 290, diag);
            Assert.Null(runtime.AddCuttingTable("shop-floor", new List<string> { "table-asset-1" }, diag));
            return runtime;
        }

        private static string PlaceGarment(TailorShopRuntime runtime, int personSeq, string garmentId, List<string> diag)
        {
            string orderId = runtime.PlaceOrder(
                EntityId.For(EntityKind.Person, personSeq), garmentId, 300, diag);
            Assert.IsFalse(orderId.StartsWith("TailorShopRuntime:"),
                $"Order should place cleanly, got rejection: {orderId}");
            return orderId;
        }

        private static string PlaceShirt(TailorShopRuntime runtime, int personSeq, List<string> diag)
        {
            return PlaceGarment(runtime, personSeq, TailorGarmentCatalog.ShirtId, diag);
        }

        private static TailorGarmentOrder FindOrder(TailorShopRuntime runtime, string orderId)
        {
            foreach (var order in runtime.Orders)
            {
                if (order.OrderId == orderId) return order;
            }

            return null;
        }

        private static int Work(TailorShopRuntime runtime, int day, List<string> diag, params string[] tableAssets)
        {
            return runtime.WorkDay(day, true,
                WorkstationCatalog.TailorCuttingTableStation, TableFinder(tableAssets), diag);
        }

        [Test]
        public void CuttingTableStationDefinition_ExistsInCatalog_WithTailoringCapability()
        {
            WorkstationDefinition def = null;
            foreach (var candidate in WorkstationCatalog.All)
            {
                if (candidate.WorkstationId == TailorGarmentCatalog.TailorCuttingTableStationId) def = candidate;
            }

            Assert.NotNull(def, "The tailor cutting table must be a catalogued workstation.");
            Assert.Contains("tailoring", def.CapabilitiesGranted);
            Assert.AreEqual(1, def.Components.Count);
            Assert.AreEqual("cutting-table", def.Components[0].EquipmentKind);
        }

        [Test]
        public void PlaceOrder_ValidatesPersonAndGarment()
        {
            var runtime = new TailorShopRuntime("tail-biz-1");
            var diag = new List<string>();

            string nullPerson = runtime.PlaceOrder(EntityId.Invalid, TailorGarmentCatalog.ShirtId, 300, diag);
            Assert.IsTrue(nullPerson.StartsWith("TailorShopRuntime:"), "Anonymous order refused.");

            string unknown = runtime.PlaceOrder(
                EntityId.For(EntityKind.Person, 101), "tail.ballgown", 300, diag);
            Assert.IsTrue(unknown.StartsWith("TailorShopRuntime:"), "Unknown garment refused.");

            string ok = PlaceShirt(runtime, 101, diag);
            Assert.AreEqual(1, runtime.Orders.Count);
            Assert.AreEqual(TailorOrderStage.Ordered, FindOrder(runtime, ok).Stage);
        }

        [Test]
        public void AddCuttingTable_RequiresFunctionalSpace()
        {
            var runtime = new TailorShopRuntime("tail-biz-1");
            var diag = new List<string>();

            Assert.NotNull(runtime.AddCuttingTable("", new List<string> { "table-asset-1" }, diag),
                "A table with no space is refused — a room alone never grants the workstation.");
            Assert.AreEqual(0, runtime.Tables.Count);
        }

        [Test]
        public void WorkDay_NoKit_NoWork_OrdersStayOpen()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            PlaceShirt(runtime, 101, diag);

            int advances = runtime.WorkDay(300, tailorKitUsable: false,
                WorkstationCatalog.TailorCuttingTableStation, TableFinder("table-asset-1"), diag);

            Assert.AreEqual(0, advances, "No usable kit — the NX-1 teeth gate stops the whole day.");
            Assert.AreEqual(TailorOrderStage.Ordered, runtime.Orders[0].Stage,
                "The order stays open and visible, never silently dropped.");
        }

        [Test]
        public void WorkDay_NoReadyTable_BenchStagesPark()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string orderId = PlaceShirt(runtime, 101, diag);

            // Finder knows no table assets — the table is never ready.
            int advances = runtime.WorkDay(300, true,
                WorkstationCatalog.TailorCuttingTableStation, TableFinder(), diag);

            // Measuring needs no table; reservation needs no table; cutting does.
            Assert.AreEqual(2, advances, "Measure + cloth reserve run; cutting parks on the table.");
            Assert.AreEqual(TailorOrderStage.ClothReserved, FindOrder(runtime, orderId).Stage);
            Assert.AreEqual(0, runtime.CountReadyTables(
                WorkstationCatalog.TailorCuttingTableStation, TableFinder(), diag));
        }

        [Test]
        public void WorkDay_FullShirtLifecycle_FittingsBlockUntilRecorded()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string orderId = PlaceShirt(runtime, 101, diag);

            // Day 1: measure → reserve cloth → cut. Parks at Cut — no basting fitting yet.
            int advances = Work(runtime, 300, diag, "table-asset-1");
            TailorGarmentOrder order = FindOrder(runtime, orderId);
            Assert.AreEqual(3, advances);
            Assert.AreEqual(TailorOrderStage.Cut, order.Stage);
            Assert.AreEqual(TailorGarmentCatalog.ShirtClothYards, order.ClothInCustody[0].UnitsTaken,
                "The shirt's cloth yards move into the order's custody at reservation.");
            StringAssert.Contains("BOOTSTRAP", order.ClothInCustody[0].ProvenanceChain);

            // The order cannot pass Cut without its basting fitting — record it now.
            Assert.Null(runtime.RecordFitting(orderId, true, "take in waist", 300, diag));
            int parked = Work(runtime, 301, diag, "table-asset-1");
            Assert.AreEqual(TailorOrderStage.BasteFitted, FindOrder(runtime, orderId).Stage,
                "With the basting fitting recorded, the order advances through fitting and sewing.");
            Assert.GreaterOrEqual(parked, 2);
            Assert.AreEqual(TailorOrderStage.Sewn, FindOrder(runtime, orderId).Stage,
                "Sewing completes but the order parks — no final fitting yet.");

            // Final fitting, then press → finish → deliver.
            Assert.Null(runtime.RecordFitting(orderId, false, "hem 1in", 302, diag));
            Work(runtime, 302, diag, "table-asset-1");

            order = FindOrder(runtime, orderId);
            Assert.AreEqual(TailorOrderStage.Delivered, order.Stage);
            Assert.AreEqual(0, order.ClothInCustody.Count,
                "Delivered cloth lives on the delivery record, never double-counted.");

            Assert.AreEqual(1, runtime.Deliveries.Count);
            TailorDeliveryRecord delivery = runtime.Deliveries[0];
            Assert.AreEqual(150, delivery.PieceRateCents, "Piece rate comes from the schedule as data.");
            Assert.AreEqual(50, delivery.FittingFeesCents, "Two fittings at the schedule's fitting fee.");
            Assert.AreEqual(200, delivery.TotalFeeCents);
            Assert.Greater(delivery.ClothProvenance.Count, 0, "The delivery carries cloth provenance.");
            Assert.Greater(delivery.NotionsProvenance.Count, 0, "The delivery carries notions provenance.");
            foreach (var line in delivery.ClothProvenance)
                StringAssert.Contains("BOOTSTRAP", line.ProvenanceChain,
                    "Every consumed yard traces to its lot.");
        }

        [Test]
        public void RecordFitting_WrongStageOrClosedOrder_Refused()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string orderId = PlaceShirt(runtime, 101, diag);

            Assert.NotNull(runtime.RecordFitting(orderId, true, "", 300, diag),
                "No basting fitting before the garment is cut.");
            Assert.NotNull(runtime.RecordFitting("tail-biz-1-order-999", true, "", 300, diag),
                "No fitting on an unknown order.");
        }

        [Test]
        public void WorkDay_ClothShortfall_OrderParksLoudly_NeverConjured()
        {
            var diag = new List<string>();
            // No bootstrap: the shelf starts empty.
            var runtime = new TailorShopRuntime("tail-biz-1");
            Assert.Null(runtime.AddCuttingTable("shop-floor", new List<string> { "table-asset-1" }, diag));
            string orderId = PlaceShirt(runtime, 101, diag);

            int advances = Work(runtime, 300, diag, "table-asset-1");

            Assert.AreEqual(1, advances, "Only measuring runs — reservation refuses on shortfall.");
            Assert.AreEqual(TailorOrderStage.Measured, FindOrder(runtime, orderId).Stage,
                "The cloth-starved order stays parked — never worked on conjured stock, never dropped.");
            Assert.AreEqual(0, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
            Assert.IsTrue(diag.Count > 0, "The refusal is loud.");
        }

        [Test]
        public void WorkDay_NotionsShortfall_OrderParksAtSewStage()
        {
            var diag = new List<string>();
            var runtime = new TailorShopRuntime("tail-biz-1");
            var registry = new EntityIdRegistry();
            Assert.Null(runtime.AddCuttingTable("shop-floor", new List<string> { "table-asset-1" }, diag));
            // Cloth only — no notions on the shelf.
            Assert.Null(runtime.ClothStock.ReceiveLot(new TailorClothLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ClothName = TailorGarmentCatalog.ClothItemId,
                Units = 20,
                AcquiredDayIndex = 300,
                ImportOrderId = "IMP-1870-077",
                OriginName = "Off-map dry-goods wholesaler, via railhead",
            }, diag));

            string orderId = PlaceShirt(runtime, 101, diag);
            Work(runtime, 300, diag, "table-asset-1"); // → Cut
            Assert.Null(runtime.RecordFitting(orderId, true, "", 301, diag));
            Work(runtime, 301, diag, "table-asset-1"); // fitting, then sew refuses on notions

            Assert.AreEqual(TailorOrderStage.BasteFitted, FindOrder(runtime, orderId).Stage,
                "The notions-starved order parks before sewing — never sewn on conjured thread.");
        }

        [Test]
        public void WorkDay_OneTable_BoundsConcurrentBenchWork()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string first = PlaceShirt(runtime, 101, diag);
            string second = PlaceShirt(runtime, 102, diag);

            int advances = Work(runtime, 300, diag, "table-asset-1");

            Assert.AreEqual(5, advances,
                "First order: measure/reserve/cut (3). Second: measure/reserve (2) — the table is busy.");
            Assert.AreEqual(TailorOrderStage.Cut, FindOrder(runtime, first).Stage);
            Assert.AreEqual(TailorOrderStage.ClothReserved, FindOrder(runtime, second).Stage,
                "The second shirt waits on a free cutting table — throughput is visible, never assumed.");
        }

        [Test]
        public void WorkDay_MendingLifecycle_NoCloth_NoFittings()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string orderId = runtime.PlaceOrder(
                EntityId.For(EntityKind.Person, 101), TailorGarmentCatalog.MendId, 300, diag);
            Assert.IsFalse(orderId.StartsWith("TailorShopRuntime:"));

            Work(runtime, 300, diag, "table-asset-1");

            TailorGarmentOrder order = FindOrder(runtime, orderId);
            Assert.AreEqual(TailorOrderStage.Delivered, order.Stage,
                "Mending flows assess → mend → deliver in one day.");
            Assert.AreEqual(0, order.ClothInCustody.Count, "Mending consumes no cloth.");

            Assert.AreEqual(1, runtime.Deliveries.Count);
            TailorDeliveryRecord delivery = runtime.Deliveries[0];
            Assert.AreEqual(50, delivery.PieceRateCents);
            Assert.AreEqual(0, delivery.FittingFeesCents, "Mending has no fittings.");
            Assert.AreEqual(50, delivery.TotalFeeCents);
            Assert.Greater(delivery.NotionsProvenance.Count, 0);

            Assert.NotNull(runtime.RecordFitting(orderId, true, "", 300, diag),
                "Fittings are refused on mending orders.");
        }

        [Test]
        public void WorkDay_SewLaborShortfall_NotionsNotDoubleCharged()
        {
            var diag = new List<string>();
            var runtime = new TailorShopRuntime("tail-biz-1");
            var registry = new EntityIdRegistry();
            TailorClothBootstrap.ApplyBootstrapEndowment(runtime.ClothStock, registry, 290, diag);
            Assert.Null(runtime.AddCuttingTable("shop-floor", new List<string> { "table-asset-1" }, diag));
            Assert.Null(runtime.AddCuttingTable("shop-floor", new List<string> { "table-asset-2" }, diag));

            string first = PlaceGarment(runtime, 101, TailorGarmentCatalog.CoatId, diag);
            string second = PlaceGarment(runtime, 102, TailorGarmentCatalog.CoatId, diag);

            // Day 1: both coats measured, reserved, cut (two tables). Park at Cut.
            Work(runtime, 300, diag, "table-asset-1", "table-asset-2");
            Assert.AreEqual(TailorOrderStage.Cut, FindOrder(runtime, first).Stage);
            Assert.AreEqual(TailorOrderStage.Cut, FindOrder(runtime, second).Stage);

            Assert.Null(runtime.RecordFitting(first, true, "", 301, diag));
            Assert.Null(runtime.RecordFitting(second, true, "", 301, diag));

            // Day 2: the first coat sews (480m); the second coat's sew attempt
            // fails on labor AFTER its notions dispensed — they stay with the order.
            Work(runtime, 301, diag, "table-asset-1", "table-asset-2");
            Assert.AreEqual(TailorOrderStage.Sewn, FindOrder(runtime, first).Stage);
            TailorGarmentOrder parked = FindOrder(runtime, second);
            Assert.AreEqual(TailorOrderStage.BasteFitted, parked.Stage);
            int held = 0;
            foreach (var line in parked.NotionsUsed) held += line.UnitsTaken;
            Assert.AreEqual(TailorGarmentCatalog.HeavyNotionsUnits, held,
                "The failed sew attempt's notions stay with the order.");

            // Day 3: the retry must NOT dispense notions again.
            int notionsBefore = runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId);
            Work(runtime, 302, diag, "table-asset-1", "table-asset-2");

            Assert.AreEqual(TailorOrderStage.Sewn, FindOrder(runtime, second).Stage);
            held = 0;
            foreach (var line in FindOrder(runtime, second).NotionsUsed) held += line.UnitsTaken;
            Assert.AreEqual(TailorGarmentCatalog.HeavyNotionsUnits, held,
                "The retry reuses the held notions — never double-charged.");
            Assert.AreEqual(notionsBefore,
                runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId),
                "No second dispense touched the shelf.");
        }

        [Test]
        public void CancelOrder_ReturnsClothToShelf_WithReturnProvenance()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            var registry = new EntityIdRegistry();
            string orderId = PlaceShirt(runtime, 101, diag);

            Work(runtime, 300, diag, "table-asset-1"); // → Cut, cloth in custody
            int onHandBefore = runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId);

            Assert.Null(runtime.CancelOrder(orderId, 301, registry, diag));

            TailorGarmentOrder order = FindOrder(runtime, orderId);
            Assert.AreEqual(TailorOrderStage.Cancelled, order.Stage);
            Assert.AreEqual(onHandBefore + TailorGarmentCatalog.ShirtClothYards,
                runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId),
                "The shirt's cloth returns to the shelf.");
            TailorClothLot returned = runtime.ClothStock.Lots[runtime.ClothStock.Lots.Count - 1];
            StringAssert.Contains(orderId, returned.SupplierNote,
                "The return lot names the cancelled order and its original chains.");
            Assert.IsFalse(returned.IsBootstrapEndowment, "Returns are real lots, never re-marked bootstrap.");

            Assert.NotNull(runtime.CancelOrder(orderId, 302, registry, diag),
                "Cancelling twice is refused.");
            Assert.NotNull(runtime.CancelOrder("tail-biz-1-order-999", 302, registry, diag),
                "Cancelling an unknown order is refused.");
        }

        [Test]
        public void PopulateWorkstations_ExposesTablesToEquipmentGate()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            var registry = new BusinessWorkstations("tail-biz-1");

            runtime.PopulateWorkstations(registry,
                WorkstationCatalog.TailorCuttingTableStation, TableFinder("table-asset-1"), diag);

            Assert.NotNull(registry.Get("tailor-cutting-table-0"), "Each table is individually addressable.");
            WorkstationInstance alias = registry.Get(TailorGarmentCatalog.TailorCuttingTableStationId);
            Assert.NotNull(alias, "The declared workstation id resolves while a table is ready.");
            StringAssert.Contains("tailor-cutting-table-0", alias.InstanceId,
                "The alias points at the first READY table.");
        }

        [Test]
        public void PopulateWorkstations_NoReadyTable_AliasRefusedHonestly()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            var registry = new BusinessWorkstations("tail-biz-1");

            runtime.PopulateWorkstations(registry,
                WorkstationCatalog.TailorCuttingTableStation, TableFinder(), diag);

            Assert.Null(registry.Get(TailorGarmentCatalog.TailorCuttingTableStationId),
                "No ready table — the alias stays unregistered so the gate refuses honestly.");
        }

        [Test]
        public void SaveLoad_RoundTripsTablesStockRatesOrdersAndDeliveries()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            runtime.SetPieceRateSchedule(new TailorPieceRateSchedule(111, 222, 333, 444, 55, 11));
            string orderId = PlaceShirt(runtime, 101, diag);
            Work(runtime, 300, diag, "table-asset-1"); // → Cut

            var restored = new TailorShopRuntime("tail-biz-1");
            restored.LoadFromSaveDto(runtime.CaptureSaveDto());

            Assert.AreEqual(111, restored.PieceRates.ShirtRateCents, "Custom piece rates must survive.");
            Assert.AreEqual(TailorClothBootstrap.BootstrapClothYards - TailorGarmentCatalog.ShirtClothYards,
                restored.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId),
                "Shelf stock must survive (minus the shirt's reserved yards).");
            Assert.AreEqual(1, restored.Tables.Count, "Tables must survive.");
            Assert.AreEqual("shop-floor", restored.Tables[0].Station.SpaceId);
            Assert.AreEqual(1, restored.Orders.Count, "Open orders must survive.");
            TailorGarmentOrder restoredOrder = restored.Orders[0];
            Assert.AreEqual(TailorOrderStage.Cut, restoredOrder.Stage);
            Assert.AreEqual(TailorGarmentCatalog.ShirtClothYards,
                restoredOrder.ClothInCustody[0].UnitsTaken,
                "Cloth in the order's custody must survive.");

            // The restored shop still works: deliver the shirt end to end.
            var rdiag = new List<string>();
            Assert.Null(restored.RecordFitting(orderId, true, "", 301, rdiag));
            restored.WorkDay(301, true, WorkstationCatalog.TailorCuttingTableStation,
                TableFinder("table-asset-1"), rdiag);
            Assert.Null(restored.RecordFitting(orderId, false, "", 302, rdiag));
            restored.WorkDay(302, true, WorkstationCatalog.TailorCuttingTableStation,
                TableFinder("table-asset-1"), rdiag);
            Assert.AreEqual(TailorOrderStage.Delivered, restored.Orders[0].Stage);
            Assert.AreEqual(111 + 22, restored.Deliveries[0].TotalFeeCents,
                "Delivery settles at the restored schedule's rates.");
        }

        [Test]
        public void SaveLoad_DeliveredOrdersPersistAsDeliveryRecords_NotLiveOrders()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string orderId = PlaceShirt(runtime, 101, diag);
            Work(runtime, 300, diag, "table-asset-1");
            Assert.Null(runtime.RecordFitting(orderId, true, "", 301, diag));
            Work(runtime, 301, diag, "table-asset-1");
            Assert.AreEqual(TailorOrderStage.Sewn, FindOrder(runtime, orderId).Stage);
            Assert.Null(runtime.RecordFitting(orderId, false, "", 302, diag));
            Work(runtime, 302, diag, "table-asset-1");
            Assert.AreEqual(TailorOrderStage.Delivered, FindOrder(runtime, orderId).Stage);

            var restored = new TailorShopRuntime("tail-biz-1");
            restored.LoadFromSaveDto(runtime.CaptureSaveDto());

            Assert.AreEqual(0, restored.Orders.Count, "Delivered orders do not rehydrate as live orders.");
            Assert.AreEqual(1, restored.Deliveries.Count, "Their delivery records are the persisted audit trail.");
            Assert.AreEqual(200, restored.Deliveries[0].TotalFeeCents);
        }
    }
}
