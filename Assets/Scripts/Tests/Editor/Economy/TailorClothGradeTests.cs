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
    /// D1C: cloth grading/quality. The canon defines no grade table, so the
    /// default policy is Unrated (W1C behavior preserved): grades are recorded
    /// on lots for provenance, and only an explicit Rated policy with a
    /// caller-supplied multiplier table changes pricing. Grade preference
    /// orders dispense but never conjures units.
    /// </summary>
    [TestFixture]
    public sealed class TailorClothGradeTests
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
            var runtime = new TailorShopRuntime("tail-grade-1");
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

        private static string PlaceShirt(TailorShopRuntime runtime, int personSeq, List<string> diag)
        {
            string orderId = runtime.PlaceOrder(
                EntityId.For(EntityKind.Person, personSeq), TailorGarmentCatalog.ShirtId, 300, diag);
            Assert.IsFalse(orderId.StartsWith("TailorShopRuntime:"),
                $"Order should place cleanly, got rejection: {orderId}");
            return orderId;
        }

        private static void DriveShirtToDelivery(TailorShopRuntime runtime, string orderId, List<string> diag)
        {
            Work(runtime, 300, diag, "table-asset-1"); // measure → reserve → cut
            Assert.Null(runtime.RecordFitting(orderId, true, "", 300, diag));
            Work(runtime, 301, diag, "table-asset-1"); // baste-fit → sew
            Assert.Null(runtime.RecordFitting(orderId, false, "", 302, diag));
            Work(runtime, 302, diag, "table-asset-1"); // final-fit → press → deliver
        }

        private static TailorGarmentOrder FindOrder(TailorShopRuntime runtime, string orderId)
        {
            foreach (var order in runtime.Orders)
            {
                if (order.OrderId == orderId) return order;
            }

            return null;
        }

        [Test]
        public void DefaultGradePolicy_IsUnrated()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            Assert.AreEqual(TailorClothGradeMode.Unrated, runtime.ClothGradePolicy.Mode,
                "The canon defines no grade table — grading is opt-in, never assumed.");
        }

        [Test]
        public void HistoricalFrontierDefaults_AreRatedAndMarkedNotCanon()
        {
            TailorClothGradePolicy policy = TailorClothGrades.HistoricalFrontierDefaults();
            Assert.AreEqual(TailorClothGradeMode.Rated, policy.Mode);
            Assert.AreEqual(2, policy.KnownGrades.Count);
            foreach (var grade in policy.KnownGrades)
            {
                StringAssert.Contains("NOT canon", grade.SourceNote,
                    "Historical-practice grades must never be presented as canon.");
            }
        }

        [Test]
        public void GetPriceMultiplier_UnratedOrUnknownGrade_ReturnsOne()
        {
            var unrated = new TailorClothGradePolicy();
            Assert.AreEqual(1f, TailorClothGrades.GetPriceMultiplier(unrated, "fine"));
            Assert.AreEqual(1f, TailorClothGrades.GetPriceMultiplier(null, "fine"));

            var rated = TailorClothGrades.HistoricalFrontierDefaults();
            Assert.AreEqual(1f, TailorClothGrades.GetPriceMultiplier(rated, "mystery-grade"),
                "Unknown grades price at 1.0 — never guessed.");
            Assert.AreEqual(1f, TailorClothGrades.GetPriceMultiplier(rated, ""));
        }

        [Test]
        public void Dispense_PrefersMatchingGrade_FallsBackToFifo()
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            var stock = new TailorClothStock();
            // Oldest lot is utility; a newer lot is fine.
            Assert.Null(stock.ReceiveLot(new TailorClothLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ClothName = TailorGarmentCatalog.ClothItemId, Units = 10, AcquiredDayIndex = 290,
                ImportOrderId = "imp-1", OriginName = "Railhead wholesaler",
                GradeLabel = TailorClothGrades.UtilityGradeId,
            }, diag));
            Assert.Null(stock.ReceiveLot(new TailorClothLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ClothName = TailorGarmentCatalog.ClothItemId, Units = 10, AcquiredDayIndex = 295,
                ImportOrderId = "imp-2", OriginName = "Railhead wholesaler",
                GradeLabel = TailorClothGrades.FineGradeId,
            }, diag));

            var preferred = stock.TryDispenseUnits(
                TailorGarmentCatalog.ClothItemId, 10, 300, diag, TailorClothGrades.FineGradeId);
            Assert.NotNull(preferred);
            Assert.AreEqual(TailorClothGrades.FineGradeId, preferred[0].GradeLabel,
                "The grade preference orders which real lots give up units — the older utility lot stays.");

            var plain = stock.TryDispenseUnits(
                TailorGarmentCatalog.ClothItemId, 5, 300, diag);
            Assert.NotNull(plain);
            Assert.AreEqual(TailorClothGrades.UtilityGradeId, plain[0].GradeLabel,
                "Without a preference the dispense is plain FIFO (the older utility lot).");
        }

        [Test]
        public void Dispense_GradePreference_NeverConjuresUnits()
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            var stock = new TailorClothStock();
            Assert.Null(stock.ReceiveLot(new TailorClothLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ClothName = TailorGarmentCatalog.ClothItemId, Units = 4, AcquiredDayIndex = 290,
                ImportOrderId = "imp-1", OriginName = "Railhead wholesaler",
                GradeLabel = TailorClothGrades.UtilityGradeId,
            }, diag));

            var lines = stock.TryDispenseUnits(
                TailorGarmentCatalog.ClothItemId, 5, 300, diag, TailorClothGrades.FineGradeId);
            Assert.IsNull(lines, "No fine cloth on hand — the shortfall refuses loudly, never conjures.");
            Assert.AreEqual(4, stock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
        }

        [Test]
        public void Deliver_UnratedPolicy_PieceRateUnchanged()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            var registry = new EntityIdRegistry();
            // A graded lot arrives by import, but the policy is Unrated.
            Assert.Null(TailorClothSupply.ReceiveImportArrival(runtime.ClothStock,
                TailorGarmentCatalog.ClothItemId, 10, "imp-fine-1", "Railhead wholesaler",
                295, registry, diag, TailorClothGrades.FineGradeId));
            // The shop prefers fine cloth for shirts — the reservation draws it first.
            runtime.SetGarmentGradePreference(TailorGarmentCatalog.ShirtId, TailorClothGrades.FineGradeId, diag);

            string orderId = PlaceShirt(runtime, 101, diag);
            DriveShirtToDelivery(runtime, orderId, diag);

            Assert.AreEqual(TailorOrderStage.Delivered, FindOrder(runtime, orderId).Stage);
            Assert.AreEqual(1, runtime.Deliveries.Count);
            Assert.AreEqual(150, runtime.Deliveries[0].PieceRateCents,
                "Unrated policy: the fine cloth changes nothing about the price.");
            StringAssert.Contains("fine", runtime.Deliveries[0].ClothProvenance[0].GradeLabel,
                "The grade is still recorded on the provenance line.");
        }

        [Test]
        public void Deliver_RatedPolicy_AppliesDominantGradeMultiplierToPieceRateOnly()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            var registry = new EntityIdRegistry();
            Assert.Null(TailorClothSupply.ReceiveImportArrival(runtime.ClothStock,
                TailorGarmentCatalog.ClothItemId, 10, "imp-fine-1", "Railhead wholesaler",
                295, registry, diag, TailorClothGrades.FineGradeId));

            var policy = TailorClothGrades.HistoricalFrontierDefaults();
            // Pin the multiplier for a deterministic assertion.
            policy.PriceMultipliers.Clear();
            policy.PriceMultipliers.Add(new TailorGradePriceMultiplier
            {
                GradeId = TailorClothGrades.FineGradeId, PriceMultiplier = 2f,
            });
            runtime.SetClothGradePolicy(policy);
            runtime.SetGarmentGradePreference(TailorGarmentCatalog.ShirtId, TailorClothGrades.FineGradeId, diag);

            string orderId = PlaceShirt(runtime, 101, diag);
            DriveShirtToDelivery(runtime, orderId, diag);

            Assert.AreEqual(1, runtime.Deliveries.Count);
            TailorDeliveryRecord delivery = runtime.Deliveries[0];
            Assert.AreEqual(300, delivery.PieceRateCents,
                "Dominant fine cloth × 2.0 on the 150c shirt piece rate.");
            Assert.AreEqual(50, delivery.FittingFeesCents,
                "Fitting fees are labor, not cloth — the grade never scales them.");
            Assert.AreEqual(350, delivery.TotalFeeCents);
        }

        [Test]
        public void SaveLoad_PreservesGradePolicy()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            runtime.SetClothGradePolicy(TailorClothGrades.HistoricalFrontierDefaults());
            runtime.SetGarmentGradePreference(TailorGarmentCatalog.ShirtId, TailorClothGrades.FineGradeId, diag);

            var restored = new TailorShopRuntime("tail-grade-1");
            restored.LoadFromSaveDto(runtime.CaptureSaveDto());

            Assert.AreEqual(TailorClothGradeMode.Rated, restored.ClothGradePolicy.Mode);
            Assert.AreEqual(2, restored.ClothGradePolicy.KnownGrades.Count);
            Assert.AreEqual(2, restored.ClothGradePolicy.PriceMultipliers.Count);
            Assert.AreEqual(TailorClothGrades.FineGradeId,
                restored.GetGarmentGradePreference(TailorGarmentCatalog.ShirtId, TailorGarmentCatalog.GetSpec(TailorGarmentCatalog.ShirtId)),
                "Per-garment grade preferences survive the save round-trip.");

            // Clearing the preference falls back to FIFO (empty).
            restored.SetGarmentGradePreference(TailorGarmentCatalog.ShirtId, "", diag);
            Assert.AreEqual(string.Empty,
                restored.GetGarmentGradePreference(TailorGarmentCatalog.ShirtId, TailorGarmentCatalog.GetSpec(TailorGarmentCatalog.ShirtId)));
        }
    }
}
