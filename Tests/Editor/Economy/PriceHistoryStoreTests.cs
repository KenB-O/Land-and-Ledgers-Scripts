using System.Collections.Generic;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D4I: price histories — append-only observed price records. Pure logic,
    /// no Unity here. Canon locks honored: no invented prices (every
    /// observation cites a real transaction), corrections supersede rather
    /// than edit, nothing predicts.
    /// </summary>
    [TestFixture]
    public sealed class PriceHistoryStoreTests
    {
        private static LandLedgers.Economy.Market.PriceHistory.PriceHistoryObservation MakeObs(
            string id, string settlement, string commodity, int day, int priceCents,
            string txRef = "tx-default", string supersedes = "")
        {
            return new LandLedgers.Economy.Market.PriceHistory.PriceHistoryObservation
            {
                ObservationId = id,
                SettlementId = settlement,
                SettlementName = "Test Town",
                CommodityKind = commodity,
                DayIndex = day,
                UnitPriceCents = priceCents,
                QuantityUnits = 10,
                UnitLabel = "bushel",
                QuoteSide = LandLedgers.Economy.Market.PriceHistory.PriceHistoryQuoteSide.ObservedSellPrice,
                SourceTransactionReference = txRef,
                SourceBusinessId = "biz-1",
                SupersedesObservationId = supersedes,
            };
        }

        private static LandLedgers.Economy.Market.PriceHistory.PriceHistoryStore FreshStore(
            out List<string> diag)
        {
            diag = new List<string>();
            return new LandLedgers.Economy.Market.PriceHistory.PriceHistoryStore();
        }

        [Test]
        public void RecordObservation_AcceptsWellFormedObservation()
        {
            var store = FreshStore(out var diag);
            bool ok = store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240), diag);
            Assert.IsTrue(ok);
            Assert.AreEqual(1, store.EffectiveEntryCount);
        }

        [Test]
        public void RecordObservation_RefusesMissingSourceTransaction()
        {
            var store = FreshStore(out var diag);
            bool ok = store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240, txRef: ""), diag);
            Assert.IsFalse(ok, "sourceless observation must be refused — no invented prices");
            Assert.AreEqual(0, store.EffectiveEntryCount);
        }

        [Test]
        public void RecordObservation_RefusesNonPositivePrice()
        {
            var store = FreshStore(out var diag);
            Assert.IsFalse(store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 0), diag));
            Assert.IsFalse(store.RecordObservation(MakeObs("obs-2", "ash", "flour", 10, -5), diag));
            Assert.AreEqual(0, store.EffectiveEntryCount);
        }

        [Test]
        public void RecordObservation_RefusesDuplicateObservationId()
        {
            var store = FreshStore(out var diag);
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240), diag));
            Assert.IsFalse(store.RecordObservation(MakeObs("obs-1", "ash", "flour", 11, 250), diag),
                "append-only log never re-records an id");
            Assert.AreEqual(1, store.EffectiveEntryCount);
        }

        [Test]
        public void RecordObservation_RefusesCorrectionOfUnknownObservation()
        {
            var store = FreshStore(out var diag);
            Assert.IsFalse(store.RecordObservation(
                MakeObs("obs-c", "ash", "flour", 10, 250, txRef: "tx-c", supersedes: "no-such-id"), diag));
            Assert.AreEqual(0, store.EffectiveEntryCount);
        }

        [Test]
        public void GetLatest_ReturnsNewestDayIndex()
        {
            var store = FreshStore(out var diag);
            store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240), diag);
            store.RecordObservation(MakeObs("obs-2", "ash", "flour", 12, 260), diag);
            store.RecordObservation(MakeObs("obs-3", "ash", "wheat", 12, 90), diag);
            var latest = store.GetLatest("ash", "flour");
            Assert.IsNotNull(latest);
            Assert.AreEqual(260, latest.UnitPriceCents);
            Assert.AreEqual(12, latest.DayIndex);
            Assert.IsNull(store.GetLatest("ash", "corn"));
        }

        [Test]
        public void GetPriceOnDate_ReturnsPriceInForceThatDay()
        {
            var store = FreshStore(out var diag);
            store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240), diag);
            store.RecordObservation(MakeObs("obs-2", "ash", "flour", 15, 260), diag);
            Assert.AreEqual(240, store.GetPriceOnDate("ash", "flour", 14).UnitPriceCents);
            Assert.AreEqual(260, store.GetPriceOnDate("ash", "flour", 15).UnitPriceCents);
            Assert.IsNull(store.GetPriceOnDate("ash", "flour", 9), "no price before first observation");
        }

        [Test]
        public void GetPriceSeries_ReturnsOrderedRange()
        {
            var store = FreshStore(out var diag);
            store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240), diag);
            store.RecordObservation(MakeObs("obs-2", "ash", "flour", 12, 250), diag);
            store.RecordObservation(MakeObs("obs-3", "ash", "flour", 20, 260), diag);
            var series = store.GetPriceSeries("ash", "flour", 10, 15);
            Assert.AreEqual(2, series.Count);
            Assert.AreEqual(10, series[0].DayIndex);
            Assert.AreEqual(12, series[1].DayIndex);
            Assert.AreEqual(0, store.GetPriceSeries("gj", "flour", 10, 15).Count);
        }

        [Test]
        public void Correction_SupersedesOriginalInQueriesButKeepsAuditTrail()
        {
            var store = FreshStore(out var diag);
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240, txRef: "tx-1"), diag));
            Assert.IsTrue(LandLedgers.Economy.Market.PriceHistory.PriceHistoryCaptureSeams.TryCaptureCorrection(
                store, "obs-1", "flour", 244, "bushel", 10, "clerk misread the ticket", diag));

            var latest = store.GetLatest("ash", "flour");
            Assert.IsNotNull(latest);
            Assert.AreEqual(244, latest.UnitPriceCents, "queries must see the correction");
            Assert.AreEqual("tx-1", latest.SourceTransactionReference, "correction re-states the same transaction");
            Assert.AreEqual(2, store.AllEntries.Count, "original stays in the log for audit");
            Assert.AreEqual(1, store.EffectiveEntryCount, "superseded original excluded from effective counts");
        }

        [Test]
        public void CaptureSeams_RefusesSourcelessCapture()
        {
            var store = FreshStore(out var diag);
            bool ok = LandLedgers.Economy.Market.PriceHistory.PriceHistoryCaptureSeams.TryCaptureSalePrice(
                store, "ash", "Ash Creek", "flour", 10, 240, 10, "bushel",
                LandLedgers.Economy.Market.PriceHistory.PriceHistoryQuoteSide.ObservedSellPrice,
                "", "biz-1", diag);
            Assert.IsFalse(ok);
            Assert.AreEqual(0, store.EffectiveEntryCount);
        }

        [Test]
        public void CaptureSeams_DuplicateTransactionCaptureIsRefused()
        {
            var store = FreshStore(out var diag);
            bool first = LandLedgers.Economy.Market.PriceHistory.PriceHistoryCaptureSeams.TryCaptureSalePrice(
                store, "ash", "Ash Creek", "flour", 10, 240, 10, "bushel",
                LandLedgers.Economy.Market.PriceHistory.PriceHistoryQuoteSide.ObservedSellPrice,
                "gristmill-sale:GS-0042", "mill-1", diag);
            bool second = LandLedgers.Economy.Market.PriceHistory.PriceHistoryCaptureSeams.TryCaptureSalePrice(
                store, "ash", "Ash Creek", "flour", 10, 240, 10, "bushel",
                LandLedgers.Economy.Market.PriceHistory.PriceHistoryQuoteSide.ObservedSellPrice,
                "gristmill-sale:GS-0042", "mill-1", diag);
            Assert.IsTrue(first);
            Assert.IsFalse(second, "re-processing the same transaction must not double-record");
            Assert.AreEqual(1, store.EffectiveEntryCount);
        }

        [Test]
        public void SaveLoad_RoundTripsLogAndSupersedeLinks()
        {
            var store = FreshStore(out var diag);
            store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240, txRef: "tx-1"), diag);
            store.RecordObservation(MakeObs("obs-2", "gj", "nails", 11, 85, txRef: "tx-2"), diag);
            LandLedgers.Economy.Market.PriceHistory.PriceHistoryCaptureSeams.TryCaptureCorrection(
                store, "obs-1", "flour", 244, "bushel", 10, "misread ticket", diag);

            var dto = store.CaptureSaveDto();
            var restored = FreshStore(out var diag2);
            restored.RestoreFromSaveDto(dto, diag2);

            Assert.AreEqual(3, restored.AllEntries.Count);
            Assert.AreEqual(2, restored.EffectiveEntryCount);
            Assert.AreEqual(244, restored.GetLatest("ash", "flour").UnitPriceCents);
            Assert.AreEqual(85, restored.GetLatest("gj", "nails").UnitPriceCents);
            Assert.AreEqual("tx-1", restored.GetLatest("ash", "flour").SourceTransactionReference);
        }

        [Test]
        public void RestoreFromSaveDto_HandlesNullGracefully()
        {
            var store = FreshStore(out var diag);
            store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240), diag);
            store.RestoreFromSaveDto(null, diag);
            Assert.AreEqual(0, store.EffectiveEntryCount, "null restore yields empty store, not a crash");
        }

        [Test]
        public void MarketRow_ReportsLatestVsLookback()
        {
            var store = FreshStore(out var diag);
            store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240), diag);
            store.RecordObservation(MakeObs("obs-2", "ash", "flour", 20, 260), diag);
            var row = store.BuildMarketRow("ash", "flour", 20, 10);
            Assert.IsTrue(row.HasData);
            Assert.AreEqual(260, row.LatestPriceCents);
            Assert.AreEqual(240, row.EarlierPriceCents);
            Assert.AreEqual(20, row.ChangeCents);
            Assert.AreEqual(LandLedgers.Economy.Market.PriceHistory.PriceHistoryTrendDirection.Up, row.Direction);
            Assert.AreEqual("tx-default", row.SourceTransactionReference);
        }

        [Test]
        public void MarketRow_NoEarlierPriceLeavesChangeUnknown()
        {
            var store = FreshStore(out var diag);
            store.RecordObservation(MakeObs("obs-1", "ash", "flour", 20, 260), diag);
            var row = store.BuildMarketRow("ash", "flour", 20, 10);
            Assert.IsTrue(row.HasData);
            Assert.IsNull(row.EarlierPriceCents);
            Assert.IsNull(row.ChangeCents);
            Assert.AreEqual(LandLedgers.Economy.Market.PriceHistory.PriceHistoryTrendDirection.Unknown, row.Direction);
        }

        [Test]
        public void MarketRow_NoHistoryReturnsEmptyRow()
        {
            var store = FreshStore(out var diag);
            var row = store.BuildMarketRow("ash", "corn", 20, 10);
            Assert.IsFalse(row.HasData);
        }

        [Test]
        public void MarketRowsForNewspaper_SkipsCommoditiesWithoutHistory()
        {
            var store = FreshStore(out var diag);
            store.RecordObservation(MakeObs("obs-1", "ash", "flour", 10, 240), diag);
            store.RecordObservation(MakeObs("obs-2", "ash", "nails", 10, 85), diag);
            var rows = store.BuildMarketRowsForNewspaper("ash",
                new List<string> { "flour", "corn", "nails" }, 10, 7);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("flour", rows[0].CommodityKind);
            Assert.AreEqual("nails", rows[1].CommodityKind);
        }

        [Test]
        public void CommodityCatalog_UsesExistingRepoKinds()
        {
            var catalog = typeof(LandLedgers.Economy.Market.PriceHistory.TradeCommodityKindCatalog);
            Assert.IsNotNull(catalog);
            Assert.IsTrue(LandLedgers.Economy.Market.PriceHistory.TradeCommodityKindCatalog.IsKnownCommodityKind("flour"));
            Assert.IsTrue(LandLedgers.Economy.Market.PriceHistory.TradeCommodityKindCatalog.IsKnownCommodityKind("wheat"));
            Assert.IsTrue(LandLedgers.Economy.Market.PriceHistory.TradeCommodityKindCatalog.IsKnownCommodityKind("nails"));
            Assert.IsFalse(LandLedgers.Economy.Market.PriceHistory.TradeCommodityKindCatalog.IsKnownCommodityKind("unobtainium"));
            Assert.AreEqual(9, LandLedgers.Economy.Market.PriceHistory.TradeCommodityKindCatalog.KnownCommodityKinds().Count);
        }
    }
}
