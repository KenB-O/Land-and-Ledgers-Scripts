using System.Collections.Generic;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// P5 — property & legal journey (First Ledger). Dead ends closed:
    ///   P5-TITLE: player purchases/sales never touched the T2F title chain
    ///             (TitleAuthority was never even instantiated in production).
    ///             Now bridged via PlayerTitleBridge on all five live paths.
    ///   P5-TAX:   PropertyTaxService was never instantiated or driven — taxes
    ///             were never levied. Now driven weekly via PlayerPropertyTaxDriver.
    /// Recorded (not faked): lien filing needs a production T2A CreditRegistry
    /// (P5-LIEN); tax sales need a real named bidder source; dispute triggers and
    /// the lawyer-hire UI are scene/design work; player-death succession is a
    /// design fork (P5-SUCCESSION).
    /// </summary>
    [TestFixture]
    public sealed class P5PropertyLegalJourneyTests
    {
        private TitleAuthority titles;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            titles = new TitleAuthority();
            diag = new List<string>();
        }

        // ------------------------------------------------------------------
        // Title bridge: acquisition
        // ------------------------------------------------------------------

        [Test]
        public void RecordPlayerAcquisition_AppendsSellerRootThenPlayerPurchase()
        {
            string problem = PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 7, "Ada Hawkins", "deal-123", 50000, 42, diag);

            Assert.IsNull(problem, $"unexpected recording problem: {problem}");
            Assert.AreEqual("Player", titles.CurrentHolder("plot-7"));

            IReadOnlyList<TitleRecord> chain = titles.ChainOf("plot-7");
            Assert.AreEqual(2, chain.Count, "expected seller root + player purchase");
            Assert.AreEqual("Ada Hawkins", chain[0].HolderName);
            Assert.AreEqual(TitleBasis.Purchase, chain[1].Basis);
            Assert.AreEqual("Player", chain[1].HolderName);
            Assert.AreEqual("deal-123", chain[1].InstrumentId,
                "the live transaction id is the conveyance instrument");
            Assert.AreEqual(42, chain[1].DayIndex);
        }

        [Test]
        public void RecordPlayerAcquisition_UnnamedSeller_RegistersHonestly()
        {
            string problem = PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 9, null, "deal-9", 10000, 5, diag);

            Assert.IsNull(problem);
            IReadOnlyList<TitleRecord> chain = titles.ChainOf("plot-9");
            Assert.AreEqual("Unrecorded seller", chain[0].HolderName,
                "no invented grantor — the gap is stated, not filled");
            Assert.AreEqual(TitleBasis.Unspecified, chain[0].Basis);
            Assert.AreEqual("Player", titles.CurrentHolder("plot-9"));
        }

        [Test]
        public void RecordPlayerAcquisition_RepeatPurchase_DoesNotDuplicateRoot()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(titles, 3, "Ada Hawkins", "deal-1", 10000, 5, diag));
            Assert.IsNull(PlayerTitleBridge.RecordPlayerDisposal(titles, 3, "Bob Stone", "sale-1", 12000, 50, diag));
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(titles, 3, "Bob Stone", "deal-2", 12000, 60, diag));

            IReadOnlyList<TitleRecord> chain = titles.ChainOf("plot-3");
            Assert.AreEqual(4, chain.Count, "root + purchase + sale + re-purchase; root registered once");
            Assert.AreEqual("Ada Hawkins", chain[0].HolderName);
            Assert.AreEqual("Player", titles.CurrentHolder("plot-3"));
        }

        [Test]
        public void RecordPlayerAcquisition_NullAuthority_ReturnsError()
        {
            string problem = PlayerTitleBridge.RecordPlayerAcquisition(null, 1, "X", "d", 1, 1, diag);
            Assert.IsNotNull(problem);
        }

        // ------------------------------------------------------------------
        // Title bridge: disposal
        // ------------------------------------------------------------------

        [Test]
        public void RecordPlayerDisposal_TransfersToNamedBuyer()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(titles, 11, "Ada Hawkins", "deal-11", 20000, 5, diag));
            string problem = PlayerTitleBridge.RecordPlayerDisposal(
                titles, 11, "Capital Buyer - General Store", "sale-11", 25000, 90, diag);

            Assert.IsNull(problem);
            Assert.AreEqual("Capital Buyer - General Store", titles.CurrentHolder("plot-11"));
            IReadOnlyList<TitleRecord> chain = titles.ChainOf("plot-11");
            Assert.AreEqual(3, chain.Count);
            Assert.AreEqual(TitleBasis.Purchase, chain[2].Basis);
            Assert.AreEqual("sale-11", chain[2].InstrumentId);
        }

        [Test]
        public void RecordPlayerDisposal_AnonymousBuyer_RefusedChainUnchanged()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(titles, 12, "Ada Hawkins", "deal-12", 20000, 5, diag));
            string problem = PlayerTitleBridge.RecordPlayerDisposal(titles, 12, "  ", "sale-12", 1, 90, diag);

            Assert.IsNotNull(problem, "anonymous grantees must be refused");
            Assert.AreEqual("Player", titles.CurrentHolder("plot-12"),
                "a refused disposal moves nothing");
            Assert.AreEqual(2, titles.ChainOf("plot-12").Count);
        }

        [Test]
        public void TitleChain_SaveLoadRoundTrip_PreservesChain()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(titles, 7, "Ada Hawkins", "deal-123", 50000, 42, diag));

            TitleAuthority.TitleSaveDto dto = titles.CaptureSaveDto();
            var restored = new TitleAuthority();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual("Player", restored.CurrentHolder("plot-7"));
            IReadOnlyList<TitleRecord> chain = restored.ChainOf("plot-7");
            Assert.AreEqual(2, chain.Count);
            Assert.AreEqual("deal-123", chain[1].InstrumentId);
        }

        [Test]
        public void ParcelKeyForPlot_IsStable()
        {
            Assert.AreEqual("plot-7", PlayerTitleBridge.ParcelKeyForPlot(7));
            Assert.AreEqual(PlayerTitleBridge.ParcelKeyForPlot(7), PlayerTitleBridge.ParcelKeyForPlot(7));
        }

        // ------------------------------------------------------------------
        // Property-tax driver
        // ------------------------------------------------------------------

        private sealed class TaxHarness
        {
            public readonly List<int> PlotIds = new List<int>();
            public readonly Dictionary<int, int> AssessedByPlot = new Dictionary<int, int>();
            public int CashCents;
            public readonly PlayerPropertyTaxDriver Driver;

            public TaxHarness(int startingCashCents)
            {
                CashCents = startingCashCents;
                var service = new PropertyTaxService();
                Driver = new PlayerPropertyTaxDriver(
                    service,
                    () => PlotIds,
                    pid => AssessedByPlot.TryGetValue(pid, out int v) ? v : 0,
                    (int cents, string label, out string message) =>
                    {
                        if (cents > CashCents) { message = "insufficient owner cash"; return false; }
                        CashCents -= cents;
                        message = "ok";
                        return true;
                    });
            }
        }

        [Test]
        public void Driver_LeviesAnnualAssessmentOncePerTaxYear()
        {
            var harness = new TaxHarness(1000000);
            harness.PlotIds.Add(7);
            harness.AssessedByPlot[7] = 100000; // $1,000 assessed

            harness.Driver.ResolveWeek(0);
            harness.Driver.ResolveWeek(100); // same tax year: no second levy

            IReadOnlyList<TaxAssessment> assessments = harness.Driver.Taxes.AllAssessments;
            Assert.AreEqual(1, assessments.Count);
            Assert.AreEqual("plot-7", assessments[0].ParcelId);
            Assert.AreEqual("Player", assessments[0].OwnerName);
            Assert.AreEqual(1500, assessments[0].AmountCents, "1.5% calibration on 100000c");
            Assert.AreEqual(TaxAssessmentStatus.Assessed, assessments[0].Status);

            harness.Driver.ResolveWeek(400); // next tax year
            Assert.AreEqual(2, harness.Driver.Taxes.AllAssessments.Count);
        }

        [Test]
        public void Driver_NoLevyWhenAssessedValueZero()
        {
            var harness = new TaxHarness(1000000);
            harness.PlotIds.Add(8);
            harness.AssessedByPlot[8] = 0;

            harness.Driver.ResolveWeek(0);

            Assert.AreEqual(0, harness.Driver.Taxes.AllAssessments.Count,
                "a zero-value parcel is skipped, not zero-filed");
        }

        [Test]
        public void Driver_AdvancesDelinquencyAndAccruesPenalties()
        {
            var harness = new TaxHarness(1000000);
            harness.PlotIds.Add(7);
            harness.AssessedByPlot[7] = 100000;

            harness.Driver.ResolveWeek(0);
            TaxAssessment assessment = harness.Driver.Taxes.AllAssessments[0];
            int dueDay = assessment.DueDayIndex;

            harness.Driver.ResolveWeek(dueDay + 45); // 45 days past due

            Assert.AreEqual(TaxAssessmentStatus.Delinquent, assessment.Status);
            Assert.Greater(assessment.PenaltiesAccruedCents, 0, "monthly penalty accrues once delinquent");
            Assert.Greater(assessment.TotalOwedCents(), assessment.AmountCents,
                "penalties/interest grow the balance — staged collection, not instant confiscation");
        }

        [Test]
        public void Driver_TryPayAssessment_SpendsCashAndSatisfies()
        {
            var harness = new TaxHarness(100000);
            harness.PlotIds.Add(7);
            harness.AssessedByPlot[7] = 100000;

            harness.Driver.ResolveWeek(0);
            TaxAssessment assessment = harness.Driver.Taxes.AllAssessments[0];
            int cashBefore = harness.CashCents;

            bool ok = harness.Driver.TryPayAssessment(assessment.AssessmentId, 1500, 10, out string message);

            Assert.IsTrue(ok, message);
            Assert.AreEqual(cashBefore - 1500, harness.CashCents, "owner cash actually moves");
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);
            Assert.AreEqual(0, assessment.TotalOwedCents());
        }

        [Test]
        public void Driver_TryPayAssessment_UnknownAssessment_RefusedWithoutSpending()
        {
            var harness = new TaxHarness(100000);
            int cashBefore = harness.CashCents;

            bool ok = harness.Driver.TryPayAssessment("no-such-assessment", 500, 10, out string message);

            Assert.IsFalse(ok);
            Assert.IsNotNull(message);
            Assert.AreEqual(cashBefore, harness.CashCents, "no cash moves on a refused payment");
        }

        [Test]
        public void PropertyTax_SaveLoadRoundTrip_PreservesAssessments()
        {
            var harness = new TaxHarness(1000000);
            harness.PlotIds.Add(7);
            harness.AssessedByPlot[7] = 100000;
            harness.Driver.ResolveWeek(0);
            harness.Driver.ResolveWeek(200); // push one assessment toward delinquency

            PropertyTaxService.PropertyTaxSaveDto dto = harness.Driver.Taxes.CaptureSaveDto();
            var restored = new PropertyTaxService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.AllAssessments.Count);
            TaxAssessment assessment = restored.AllAssessments[0];
            Assert.AreEqual("plot-7", assessment.ParcelId);
            Assert.AreEqual(1500, assessment.AmountCents);
            Assert.AreEqual(TaxAssessmentStatus.Delinquent, assessment.Status,
                "delinquency state survives the round trip");
        }

        [Test]
        public void Calibration_LevyMathAndTaxYear()
        {
            Assert.AreEqual(1500, PropertyTaxCalibration.ComputeLevyCents(100000));
            Assert.AreEqual(0, PropertyTaxCalibration.ComputeLevyCents(0));
            Assert.AreEqual(0, PropertyTaxCalibration.ComputeLevyCents(-50));
            Assert.AreEqual(0, PropertyTaxCalibration.TaxYearForDay(0));
            Assert.AreEqual(0, PropertyTaxCalibration.TaxYearForDay(364));
            Assert.AreEqual(1, PropertyTaxCalibration.TaxYearForDay(365));
        }
    }
}
