using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Market.PriceHistory;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Economy.Businesses.Newspaper.Tests
{
    /// <summary>
    /// D4J: the newspaper market-report column, compiled FROM the D4I price
    /// histories. Every figure traces to a recorded observation; the column
    /// never invents a number, never leaks future prices, and goes through
    /// the same dated-edition assembly as the rest of the issue.
    /// </summary>
    [TestFixture]
    public sealed class NewspaperMarketReportTests
    {
        private static PriceHistoryObservation MakeObs(
            string id, string settlementId, string settlementName, string commodity,
            int day, int priceCents, string txRef, string supersedes = "")
        {
            return new PriceHistoryObservation
            {
                ObservationId = id,
                SettlementId = settlementId,
                SettlementName = settlementName,
                CommodityKind = commodity,
                DayIndex = day,
                UnitPriceCents = priceCents,
                QuantityUnits = 10,
                UnitLabel = "bushel",
                QuoteSide = PriceHistoryQuoteSide.ObservedSellPrice,
                SourceTransactionReference = txRef,
                SourceBusinessId = "biz-mill",
                SupersedesObservationId = supersedes,
            };
        }

        private static MarketReportSpec MakeSpec(
            string[] settlements, string[] commodities, int lookbackDays = MarketReportSpec.DefaultLookbackDays)
        {
            return new MarketReportSpec
            {
                SettlementIds = new List<string>(settlements),
                CommodityKinds = new List<string>(commodities),
                LookbackDays = lookbackDays,
            };
        }

        private static NewspaperRuntime ReadyPaper(List<string> diag)
        {
            var paper = new NewspaperRuntime();
            paper.FindComponent = assetId =>
            {
                string kind;
                switch (assetId)
                {
                    case "press-1": kind = "press"; break;
                    case "cases-1": kind = "type-cases"; break;
                    case "stick-1": kind = "composing-stick"; break;
                    case "rollers-1": kind = "ink-rollers"; break;
                    default: return (WorkstationComponentView?)null;
                }
                return new WorkstationComponentView
                {
                    AssetId = assetId, Kind = kind, Condition01 = 1f, IsUsable = true,
                };
            };
            var ws = new BusinessWorkstations("paper-1");
            WorkstationInstance station = ws.GetOrCreate(WorkstationCatalog.PrintingPress.WorkstationId);
            station.SpaceId = "pressroom";
            station.InstallComponent("press-1");
            station.InstallComponent("cases-1");
            station.InstallComponent("stick-1");
            station.InstallComponent("rollers-1");
            paper.ReceivePaper(100, "import-1", diag);
            paper.ReceiveInk(2, "import-2", diag);
            return paper;
        }

        [Test]
        public void Compile_ReportsLatestPriceWithChangeAndDirection()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 30, 90, "mill-sale:A"), diag));
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-2", "ash", "Ash Grove", "wheat", 38, 100, "mill-sale:B"), diag));

            NewspaperMarketReportColumn column = MarketReportCompiler.Compile(
                store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 40, diag);
            Assert.IsNotNull(column);
            Assert.AreEqual(1, column.DataRowCount);
            Assert.AreEqual(0, column.GapCount);

            MarketReportLine line = column.Lines[0];
            Assert.AreEqual(100, line.LatestPriceCents);
            Assert.AreEqual(38, line.LatestDayIndex);
            Assert.AreEqual("mill-sale:B", line.SourceTransactionReference);
            Assert.AreEqual(90, line.EarlierPriceCents, "Day 33 (40-7) price in force is the day-30 observation.");
            Assert.AreEqual(30, line.EarlierDayIndex);
            Assert.AreEqual(10, line.ChangeCents);
            Assert.AreEqual(PriceHistoryTrendDirection.Up, line.Direction);
            Assert.AreEqual("Ash Grove", line.SettlementName, "Column carries the authored settlement name.");
        }

        [Test]
        public void Compile_ExcludesObservationsAfterTheEditionDay()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 30, 90, "mill-sale:A"), diag));
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-2", "ash", "Ash Grove", "wheat", 50, 100, "mill-sale:B"), diag));

            NewspaperMarketReportColumn column = MarketReportCompiler.Compile(
                store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 40, diag);
            Assert.IsNotNull(column);
            MarketReportLine line = column.Lines[0];
            Assert.AreEqual(90, line.LatestPriceCents, "The day-50 observation must not leak into the day-40 edition.");
            Assert.AreEqual(30, line.LatestDayIndex);
            Assert.AreEqual("mill-sale:A", line.SourceTransactionReference);
        }

        [Test]
        public void Compile_ReportsLatestWithoutChangeWhenNoEarlierQuotation()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 38, 100, "mill-sale:B"), diag));

            NewspaperMarketReportColumn column = MarketReportCompiler.Compile(
                store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 40, diag);
            MarketReportLine line = column.Lines[0];
            Assert.AreEqual(100, line.LatestPriceCents);
            Assert.IsNull(line.ChangeCents, "No observation on/before day 33 — no invented baseline.");
            Assert.AreEqual(PriceHistoryTrendDirection.Unknown, line.Direction);

            string text = MarketReportCompiler.FormatColumnText(column);
            StringAssert.Contains("no earlier quotation on record", text);
            StringAssert.Contains("mill-sale:B", text);
        }

        [Test]
        public void Compile_SteadyDirectionWhenPriceUnchanged()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 30, 100, "mill-sale:A"), diag));
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-2", "ash", "Ash Grove", "wheat", 38, 100, "mill-sale:B"), diag));

            NewspaperMarketReportColumn column = MarketReportCompiler.Compile(
                store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 40, diag);
            MarketReportLine line = column.Lines[0];
            Assert.AreEqual(0, line.ChangeCents);
            Assert.AreEqual(PriceHistoryTrendDirection.Steady, line.Direction);
            StringAssert.Contains("steady 0c since day 30", MarketReportCompiler.FormatColumnText(column));
        }

        [Test]
        public void Compile_RecordsHonestGapsForMissingData()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 38, 100, "mill-sale:B"), diag));

            NewspaperMarketReportColumn column = MarketReportCompiler.Compile(
                store, MakeSpec(new[] { "ash" }, new[] { "wheat", "oats" }), 40, diag);
            Assert.AreEqual(1, column.DataRowCount);
            Assert.AreEqual(1, column.GapCount);
            StringAssert.Contains("oats", column.Gaps[0]);
            StringAssert.Contains("ash", column.Gaps[0]);

            string text = MarketReportCompiler.FormatColumnText(column);
            StringAssert.Contains("No quotations recorded:", text);
            StringAssert.Contains("oats", text);
            Assert.IsFalse(text.Contains("oats:"), "No price line may be invented for a commodity with no observations.");
        }

        [Test]
        public void Compile_SupersededObservationsAreExcluded()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 30, 90, "mill-sale:A"), diag));
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-2", "ash", "Ash Grove", "wheat", 31, 95, "mill-sale:A-corr", supersedes: "obs-1"), diag));

            NewspaperMarketReportColumn column = MarketReportCompiler.Compile(
                store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 40, diag);
            MarketReportLine line = column.Lines[0];
            Assert.AreEqual(95, line.LatestPriceCents, "The superseded day-30 price must not appear.");
            Assert.AreEqual("mill-sale:A-corr", line.SourceTransactionReference);
        }

        [Test]
        public void Compile_RefusesInvalidInput()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();

            Assert.IsNull(MarketReportCompiler.Compile(null, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 40, diag));
            Assert.IsNull(MarketReportCompiler.Compile(store, null, 40, diag));
            Assert.IsNull(MarketReportCompiler.Compile(store, MakeSpec(new string[0], new[] { "wheat" }), 40, diag));
            Assert.IsNull(MarketReportCompiler.Compile(store, MakeSpec(new[] { "ash" }, new string[0]), 40, diag));
            Assert.IsNull(MarketReportCompiler.Compile(store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), -1, diag));
            var badLookback = MakeSpec(new[] { "ash" }, new[] { "wheat" }, lookbackDays: -3);
            Assert.IsNull(MarketReportCompiler.Compile(store, badLookback, 40, diag));
            Assert.Greater(diag.Count, 0, "Every refusal must explain itself.");
        }

        [Test]
        public void Compile_CoversHomeAndNeighborTowns()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 38, 100, "mill-sale:A"), diag));
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-2", "birch", "Birch Falls", "wheat", 39, 110, "mill-sale:C"), diag));

            NewspaperMarketReportColumn column = MarketReportCompiler.Compile(
                store, MakeSpec(new[] { "ash", "birch" }, new[] { "wheat" }), 40, diag);
            Assert.AreEqual(2, column.DataRowCount);
            Assert.AreEqual("ash", column.Lines[0].SettlementId);
            Assert.AreEqual("birch", column.Lines[1].SettlementId);

            string text = MarketReportCompiler.FormatColumnText(column);
            StringAssert.Contains("Ash Grove", text);
            StringAssert.Contains("Birch Falls", text);
            StringAssert.Contains("mill-sale:C", text);
        }

        [Test]
        public void AttachComponent_RefusesDuplicateComponent()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 38, 100, "mill-sale:B"), diag));

            NewspaperRuntime paper = ReadyPaper(diag);
            var ws = new BusinessWorkstations("paper-1");
            WorkstationInstance station = ws.GetOrCreate(WorkstationCatalog.PrintingPress.WorkstationId);
            station.SpaceId = "pressroom";
            station.InstallComponent("press-1");
            station.InstallComponent("cases-1");
            station.InstallComponent("stick-1");
            station.InstallComponent("rollers-1");
            NewspaperEdition edition = paper.PublishEdition(null, ws, 50, 40, diag);
            Assert.IsNotNull(edition);

            NewspaperEditionComponent component =
                MarketReportCompiler.CompileAsComponent(store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 40, null, diag);
            Assert.IsNotNull(component);
            Assert.IsNull(paper.AttachEditionComponent(edition.EditionId, component, diag));
            string problem = paper.AttachEditionComponent(edition.EditionId, component, diag);
            Assert.IsNotNull(problem, "The same component must not attach twice.");
            Assert.AreEqual(1, edition.Components.Count);
        }

        [Test]
        public void AttachComponent_FullColumnToEditionRoundTrip()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 38, 100, "mill-sale:B"), diag));

            NewspaperRuntime paper = ReadyPaper(diag);
            var ws = new BusinessWorkstations("paper-1");
            WorkstationInstance station = ws.GetOrCreate(WorkstationCatalog.PrintingPress.WorkstationId);
            station.SpaceId = "pressroom";
            station.InstallComponent("press-1");
            station.InstallComponent("cases-1");
            station.InstallComponent("stick-1");
            station.InstallComponent("rollers-1");

            NewspaperEdition edition = paper.PublishEdition(null, ws, 50, 40, diag);
            Assert.IsNotNull(edition, "Edition must publish day 40 with a ready press.");
            Assert.AreEqual(40, edition.PublicationDayIndex);

            NewspaperEditionComponent component =
                MarketReportCompiler.CompileAsComponent(store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 40, "Market Report", diag);
            Assert.IsNotNull(component);
            Assert.AreEqual(NewspaperEditionComponentKind.MarketReportColumn, component.Kind);
            Assert.AreEqual(40, component.CompiledDayIndex);

            string problem = paper.AttachEditionComponent(edition.EditionId, component, diag);
            Assert.IsNull(problem, "Same-day component must attach: " + problem);
            Assert.AreEqual(1, edition.Components.Count);
            StringAssert.Contains("MARKET REPORT", edition.Components[0].ContentText);
            StringAssert.Contains("mill-sale:B", edition.Components[0].ContentText);
        }

        [Test]
        public void AttachComponent_RefusesDateMismatch()
        {
            var diag = new List<string>();
            var store = new PriceHistoryStore();
            Assert.IsTrue(store.RecordObservation(MakeObs("obs-1", "ash", "Ash Grove", "wheat", 38, 100, "mill-sale:B"), diag));

            NewspaperRuntime paper = ReadyPaper(diag);
            var ws = new BusinessWorkstations("paper-1");
            WorkstationInstance station = ws.GetOrCreate(WorkstationCatalog.PrintingPress.WorkstationId);
            station.SpaceId = "pressroom";
            station.InstallComponent("press-1");
            station.InstallComponent("cases-1");
            station.InstallComponent("stick-1");
            station.InstallComponent("rollers-1");
            NewspaperEdition edition = paper.PublishEdition(null, ws, 50, 40, diag);
            Assert.IsNotNull(edition);

            NewspaperEditionComponent wrongDay =
                MarketReportCompiler.CompileAsComponent(store, MakeSpec(new[] { "ash" }, new[] { "wheat" }), 41, null, diag);
            Assert.IsNotNull(wrongDay);
            string problem = paper.AttachEditionComponent(edition.EditionId, wrongDay, diag);
            Assert.IsNotNull(problem, "A day-41 column must not attach to a day-40 edition.");
            Assert.AreEqual(0, edition.Components.Count);
        }

        [Test]
        public void AttachComponent_RefusesUnknownEditionAndBadComponents()
        {
            var diag = new List<string>();
            NewspaperRuntime paper = ReadyPaper(diag);
            var component = new NewspaperEditionComponent
            {
                ComponentId = "mrc-x",
                Kind = NewspaperEditionComponentKind.MarketReportColumn,
                Title = "Market Report",
                CompiledDayIndex = 40,
            };

            Assert.IsNotNull(paper.AttachEditionComponent("no-such-edition", component, diag),
                "Unknown editions are refused.");
            Assert.IsNotNull(paper.AttachEditionComponent("no-such-edition", null, diag),
                "Null components are refused.");
            var kindless = new NewspaperEditionComponent { ComponentId = "mrc-y", CompiledDayIndex = 40 };
            Assert.IsNotNull(paper.AttachEditionComponent("no-such-edition", kindless, diag),
                "Kindless components are refused.");
        }
    }
}
