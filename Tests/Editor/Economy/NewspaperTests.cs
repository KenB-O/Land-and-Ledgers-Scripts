using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Economy.Businesses.Newspaper.Tests
{
    /// <summary>
    /// NX-3A: the newspaper as a real business. Canon §5.8 / GHOST-DES-076:
    /// advertising is a real media market.
    /// </summary>
    public sealed class NewspaperTests
    {
        private List<string> diag;
        private NewspaperRuntime paper;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            paper = new NewspaperRuntime();
        }

        private BusinessWorkstations ReadyPress()
        {
            var ws = new BusinessWorkstations("paper-1");
            WorkstationInstance station = ws.GetOrCreate(WorkstationCatalog.PrintingPress.WorkstationId);
            station.SpaceId = "pressroom";
            station.InstallComponent("press-1");
            station.InstallComponent("cases-1");
            station.InstallComponent("stick-1");
            station.InstallComponent("rollers-1");
            return ws;
        }

        private void WireComponents()
        {
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
        }

        [Test]
        public void PublishEditionRefusedWithoutPress()
        {
            paper.ReceivePaper(100, "import-1", diag);
            paper.ReceiveInk(2, "import-2", diag);
            var edition = paper.PublishEdition(null, new BusinessWorkstations("paper-1"), 50, 10, diag);
            Assert.IsNull(edition, "A pressroom alone does not print (Tech X §3.5).");
        }

        [Test]
        public void PublishEditionRefusedWithoutPaper()
        {
            WireComponents();
            var edition = paper.PublishEdition(null, ReadyPress(), 50, 10, diag);
            Assert.IsNull(edition, "No paper, no edition — the press does not print on wishes.");
        }

        [Test]
        public void PublishEditionConsumesPaperAndDatesTheLot()
        {
            WireComponents();
            paper.ReceivePaper(100, "import-1", diag);
            paper.ReceiveInk(2, "import-2", diag);
            var edition = paper.PublishEdition(null, ReadyPress(), 50, 10, diag);
            Assert.IsNotNull(edition);
            Assert.AreEqual(1, edition.IssueNumber);
            Assert.AreEqual(10, edition.PublicationDayIndex);
            Assert.AreEqual(50, edition.CopiesPrinted);
            Assert.AreEqual(50, paper.PaperUnits, "50 copies consumed 50 paper units.");
        }

        [Test]
        public void PlaceAdRecordsRealPlacementWithRepeatTerms()
        {
            var single = paper.PlaceAd("biz-smith", "Blacksmith", "Horses shod.", NewspaperAdSize.Notice, 1, 10, diag);
            Assert.IsNotNull(single);
            Assert.AreEqual(NewspaperRuntime.NoticeRateCents, single.RateCents);

            var repeat = paper.PlaceAd("biz-smith", "Blacksmith", "Horses shod.", NewspaperAdSize.Notice, 4, 10, diag);
            Assert.IsNotNull(repeat);
            Assert.Less(repeat.RateCents, single.RateCents, "Repeat insertions earn the repeat-terms discount (Canon §5.8).");

            var anonymous = paper.PlaceAd("", "", "Buy now.", NewspaperAdSize.Notice, 1, 10, diag);
            Assert.IsNull(anonymous, "No anonymous advertisers.");
        }

        [Test]
        public void AdMarketAdapterPlacesRealAds()
        {
            INewspaperAdMarket market = new NewspaperAdMarket(paper, "The Chronicle");
            Assert.AreEqual(NewspaperRuntime.NoticeRateCents, market.RateForSizeCents("notice"));
            Assert.AreEqual(-1, market.RateForSizeCents("skywriting"), "Unoffered sizes are refused, not priced.");
            string placementId = market.PlaceAd("biz-1", "Help wanted: clerk.", "notice", 1, 10, diag);
            Assert.IsNotNull(placementId);
            Assert.AreEqual(1, paper.Placements.Count);
        }

        [Test]
        public void SubscriptionNamesSubscriber()
        {
            var anonymous = paper.SellSubscription("", 365, false, 10, diag);
            Assert.IsNull(anonymous, "Subscribers are named — no anonymous circulation.");
            var sub = paper.SellSubscription("Samuel", 365, true, 10, diag);
            Assert.IsNotNull(sub);
            Assert.AreEqual(NewspaperRuntime.YearlySubscriptionCents, sub.PricePaidCents);
            Assert.IsTrue(sub.DeliveredByPost);
        }

        [Test]
        public void JobPrintRequiresPressAndPaper()
        {
            var order = paper.AcceptJobPrint("biz-store", "General Store", "100 handbills", 100, 150, 10, diag);
            Assert.IsNotNull(order);
            string noPress = paper.CompleteJobPrint(order.OrderId, new BusinessWorkstations("paper-1"), 11, diag);
            Assert.IsNotNull(noPress, "Job printing needs the press too.");

            WireComponents();
            paper.ReceivePaper(200, "import-1", diag);
            string done = paper.CompleteJobPrint(order.OrderId, ReadyPress(), 11, diag);
            Assert.IsNull(done);
            Assert.IsTrue(order.Completed);
        }

        [Test]
        public void EditionsGoStale()
        {
            WireComponents();
            paper.ReceivePaper(100, "import-1", diag);
            paper.ReceiveInk(2, "import-2", diag);
            var edition = paper.PublishEdition(null, ReadyPress(), 50, 10, diag);
            paper.AgeEditions(18, diag);
            Assert.IsTrue(edition.Stale, "Dated lots — last week's news is wrapping paper.");
            string refused = paper.SellCopies(0, 5, 18, diag);
            Assert.IsNotNull(refused, "Stale editions don't sell.");
        }

        [Test]
        public void PaperNeedsANamedSource()
        {
            string problem = paper.ReceivePaper(10, "", diag);
            Assert.IsNotNull(problem, "Paper provenance is required — no anonymous stock.");
        }
    }
}
