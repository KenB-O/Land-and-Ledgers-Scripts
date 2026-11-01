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
    /// D1C: alterations on customer-owned garments — the "clothing service"
    /// from the Canon Part V tailor profile. No cloth is ever reserved for
    /// alterations; notions dispense with provenance, pinning fittings are
    /// genuine gated steps, and missed fittings park orders without ever
    /// simulating customer behavior.
    /// </summary>
    [TestFixture]
    public sealed class TailorAlterationTests
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
            var runtime = new TailorShopRuntime("tail-alt-1");
            var registry = new EntityIdRegistry();
            TailorClothBootstrap.ApplyBootstrapEndowment(runtime.ClothStock, registry, 290, diag);
            Assert.Null(runtime.AddCuttingTable("shop-floor", new List<string> { "table-asset-1" }, diag));
            return runtime;
        }

        private static int Work(TailorShopRuntime runtime, int day, List<string> diag, params string[] tableAssets)
        {
            return runtime.WorkDay(day, true,
                WorkstationCatalog.TailorCuttingTableStation, TableFinder(tableAssets), diag);
        }

        private static string PlaceAlteration(TailorShopRuntime runtime, int personSeq, string alterationId, List<string> diag)
        {
            string orderId = runtime.PlaceAlterationOrder(
                EntityId.For(EntityKind.Person, personSeq), alterationId, 300, diag);
            Assert.IsFalse(orderId.StartsWith("TailorShopRuntime:"),
                $"Alteration order should place cleanly, got rejection: {orderId}");
            return orderId;
        }

        private static TailorAlterationOrder FindAlteration(TailorShopRuntime runtime, string orderId)
        {
            foreach (var order in runtime.AlterationOrders)
            {
                if (order.OrderId == orderId) return order;
            }

            return null;
        }

        [Test]
        public void Catalog_KnowsFiveAlterations_UnknownRejected()
        {
            Assert.IsTrue(TailorAlterationCatalog.IsKnownAlteration(TailorAlterationCatalog.HemId));
            Assert.IsTrue(TailorAlterationCatalog.IsKnownAlteration(TailorAlterationCatalog.ResizeWaistId));
            Assert.IsTrue(TailorAlterationCatalog.IsKnownAlteration(TailorAlterationCatalog.ShortenSleevesId));
            Assert.IsTrue(TailorAlterationCatalog.IsKnownAlteration(TailorAlterationCatalog.ReplaceButtonsId));
            Assert.IsTrue(TailorAlterationCatalog.IsKnownAlteration(TailorAlterationCatalog.RelineId));
            Assert.IsFalse(TailorAlterationCatalog.IsKnownAlteration("tailor.alt.tailcoat"));
            Assert.IsFalse(TailorAlterationCatalog.IsKnownAlteration(""));

            foreach (string id in new[]
            {
                TailorAlterationCatalog.HemId, TailorAlterationCatalog.ResizeWaistId,
                TailorAlterationCatalog.ShortenSleevesId, TailorAlterationCatalog.ReplaceButtonsId,
                TailorAlterationCatalog.RelineId,
            })
            {
                TailorAlterationSpec spec = TailorAlterationCatalog.GetSpec(id);
                Assert.Greater(spec.LaborMinutes, 0, $"{id} needs labor.");
                Assert.GreaterOrEqual(spec.FeeCents, 0, $"{id} needs a fee.");
                Assert.GreaterOrEqual(spec.NotionsUnits, 0, $"{id} needs a notions count.");
            }
        }

        [Test]
        public void PlaceAlterationOrder_ValidatesPersonAndAlterationId()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);

            string badPerson = runtime.PlaceAlterationOrder(
                EntityId.Invalid, TailorAlterationCatalog.HemId, 300, diag);
            Assert.IsTrue(badPerson.StartsWith("TailorShopRuntime:"),
                "No anonymous alteration orders.");

            string badId = runtime.PlaceAlterationOrder(
                EntityId.For(EntityKind.Person, 101), "tailor.alt.tailcoat", 300, diag);
            Assert.IsTrue(badId.StartsWith("TailorShopRuntime:"),
                "Unknown alterations never schedule.");
        }

        [Test]
        public void Hem_CompletesWithoutFitting_NoClothMoved()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string orderId = PlaceAlteration(runtime, 101, TailorAlterationCatalog.HemId, diag);

            int advances = Work(runtime, 300, diag, "table-asset-1");

            Assert.AreEqual(2, advances, "Ordered → Altered → Delivered in one day.");
            TailorAlterationOrder order = FindAlteration(runtime, orderId);
            Assert.AreEqual(TailorAlterationStage.Delivered, order.Stage);
            Assert.AreEqual(1, runtime.AlterationDeliveries.Count);
            Assert.AreEqual(TailorAlterationCatalog.HemFeeCents, runtime.AlterationDeliveries[0].FeeCents);
            Assert.AreEqual(30, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId),
                "Alterations are customer-owned garments — the shop's cloth never moves.");
            Assert.AreEqual(20, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId),
                "Hemming consumes no notions.");
        }

        [Test]
        public void ResizeWaist_WaitsOnPinningFitting_ThenCompletes()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string orderId = PlaceAlteration(runtime, 101, TailorAlterationCatalog.ResizeWaistId, diag);

            int parked = Work(runtime, 300, diag, "table-asset-1");
            Assert.AreEqual(0, parked);
            Assert.AreEqual(TailorAlterationStage.Ordered, FindAlteration(runtime, orderId).Stage,
                "The pinning fitting is a genuine step — no fitting, no bench work.");

            Assert.Null(runtime.RecordPinningFitting(orderId, "take in 2in", 300, diag));
            int advances = Work(runtime, 301, diag, "table-asset-1");

            Assert.AreEqual(2, advances);
            Assert.AreEqual(TailorAlterationStage.Delivered, FindAlteration(runtime, orderId).Stage);
            TailorAlterationRecord delivery = runtime.AlterationDeliveries[0];
            Assert.AreEqual(TailorAlterationCatalog.ResizeWaistFeeCents, delivery.FeeCents);
            Assert.AreEqual(19, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId),
                "One notion unit consumed with provenance.");
            Assert.Greater(delivery.NotionsProvenance.Count, 0);
        }

        [Test]
        public void RecordPinningFitting_RejectsWhenNotNeededOrUnknown()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string hemId = PlaceAlteration(runtime, 101, TailorAlterationCatalog.HemId, diag);

            Assert.NotNull(runtime.RecordPinningFitting(hemId, "", 300, diag),
                "Hemming needs no pinning fitting — it goes straight to the bench.");
            Assert.NotNull(runtime.RecordPinningFitting("tail-alt-1-alt-999", "", 300, diag),
                "Unknown order ids are refused, never guessed.");

            string waistId = PlaceAlteration(runtime, 102, TailorAlterationCatalog.ResizeWaistId, diag);
            Assert.Null(runtime.RecordPinningFitting(waistId, "", 300, diag));
            Assert.NotNull(runtime.RecordPinningFitting(waistId, "", 300, diag),
                "The pinning fitting cannot be recorded twice.");
        }

        [Test]
        public void CancelAlterationOrder_ReturnsUnusedNotionsToShelf()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            var registry = new EntityIdRegistry();
            string orderId = PlaceAlteration(runtime, 101, TailorAlterationCatalog.ReplaceButtonsId, diag);

            // Simulate the mid-advance state: notions dispensed with real
            // provenance, sewing not yet done.
            var lines = runtime.ClothStock.TryDispenseUnits(
                TailorGarmentCatalog.NotionsItemId, 1, 300, diag);
            Assert.NotNull(lines);
            FindAlteration(runtime, orderId).NotionsUsed.AddRange(lines);
            Assert.AreEqual(19, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId));

            Assert.Null(runtime.CancelAlterationOrder(orderId, 300, registry, diag));

            Assert.AreEqual(TailorAlterationStage.Cancelled, FindAlteration(runtime, orderId).Stage);
            Assert.AreEqual(20, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId),
                "Unused notions return to the shelf — never silently absorbed.");
            TailorClothLot returned = null;
            foreach (var lot in runtime.ClothStock.Lots)
            {
                if (lot.ImportOrderId == $"return-{orderId}") returned = lot;
            }

            Assert.NotNull(returned, "The returned notions arrive as a named lot.");
            StringAssert.Contains("cancelled alteration", returned.OriginName);
            Assert.IsFalse(returned.IsBootstrapEndowment);
        }

        [Test]
        public void CancelAlterationOrder_FreshOrder_CancelsCleanly()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            var registry = new EntityIdRegistry();
            string orderId = PlaceAlteration(runtime, 101, TailorAlterationCatalog.HemId, diag);

            Assert.Null(runtime.CancelAlterationOrder(orderId, 300, registry, diag));
            Assert.AreEqual(TailorAlterationStage.Cancelled, FindAlteration(runtime, orderId).Stage);
            Assert.AreEqual(30, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
            Assert.AreEqual(20, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId));

            Assert.NotNull(runtime.CancelAlterationOrder(orderId, 300, registry, diag),
                "Double-cancel is refused, not silently absorbed.");
        }

        [Test]
        public void RecordMissedFitting_ParksGarmentAndAlterationOrders()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);

            // Bespoke shirt parks at Cut waiting on its basting fitting.
            string shirtId = runtime.PlaceOrder(
                EntityId.For(EntityKind.Person, 101), TailorGarmentCatalog.ShirtId, 300, diag);
            Work(runtime, 300, diag, "table-asset-1");

            // Alteration parks at Ordered waiting on its pinning fitting.
            string waistId = PlaceAlteration(runtime, 102, TailorAlterationCatalog.ResizeWaistId, diag);

            Assert.Null(runtime.RecordMissedFitting(shirtId, 301, diag));
            Assert.Null(runtime.RecordMissedFitting(waistId, 301, diag));

            TailorGarmentOrder shirt = null;
            foreach (var order in runtime.Orders)
            {
                if (order.OrderId == shirtId) shirt = order;
            }

            Assert.NotNull(shirt);
            Assert.AreEqual(1, shirt.MissedFittings);
            Assert.AreEqual(TailorOrderStage.Cut, shirt.Stage,
                "A missed fitting parks the order — it never skips the fitting.");
            Assert.AreEqual(1, FindAlteration(runtime, waistId).MissedFittings);
            Assert.AreEqual(TailorAlterationStage.Ordered, FindAlteration(runtime, waistId).Stage);

            Assert.NotNull(runtime.RecordMissedFitting("tail-alt-1-order-999", 301, diag),
                "Unknown order ids are refused.");
        }

        [Test]
        public void SaveLoad_PreservesAlterationOrdersAndDeliveries()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            string waistId = PlaceAlteration(runtime, 101, TailorAlterationCatalog.ResizeWaistId, diag);
            Assert.Null(runtime.RecordPinningFitting(waistId, "", 300, diag));
            Work(runtime, 300, diag, "table-asset-1");
            Assert.AreEqual(TailorAlterationStage.Delivered, FindAlteration(runtime, waistId).Stage);

            string hemId = PlaceAlteration(runtime, 102, TailorAlterationCatalog.HemId, diag);

            var restored = new TailorShopRuntime("tail-alt-1");
            restored.LoadFromSaveDto(runtime.CaptureSaveDto());

            Assert.AreEqual(1, restored.AlterationDeliveries.Count);
            Assert.AreEqual(TailorAlterationCatalog.ResizeWaistFeeCents, restored.AlterationDeliveries[0].FeeCents);
            TailorAlterationOrder live = null;
            foreach (var order in restored.AlterationOrders)
            {
                if (order.OrderId == hemId) live = order;
            }

            Assert.NotNull(live, "Only live alteration orders rehydrate.");
            Assert.IsTrue(live.IsActive);
        }
    }
}
