using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass XI WS3 (PASS9-002): tax fee waterfall and player-leg ordering.
    /// Fees are tracked separately from principal on the assessment, so
    /// fees accruing after a partial principal payment route to the
    /// County Tax Collector instead of being swallowed by AmountPaidCents.
    /// The player leg refuses a satisfied assessment before spending, so
    /// no debit is stranded by a refusal.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws3TaxFeeWaterfallTests
    {
        private sealed class NpcFixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public PropertyTaxService Taxes = new PropertyTaxService();
            public NpcPropertyLifeService Life = new NpcPropertyLifeService();
            public List<string> Diag = new List<string>();

            public HouseholdLedger RegisterHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId, new HouseholdCashStore(householdId, ledger)));
                return ledger;
            }

            public PurseCashStore RegisterPurse(string owner, int balanceCents)
            {
                var purse = new PurseCashStore(owner, balanceCents, "test seed capital");
                Assert.IsNull(Cash.Register(owner, purse));
                return purse;
            }
        }

        private static NpcPropertyHolding Holding(NpcFixture f, int householdId)
        {
            return f.Life.RegisterHolding("parcel-1", "bld-1", householdId, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
        }

        [Test]
        public void NpcLeg_FeesAccruingAfterPartialPrincipalPayment_RouteToCollector()
        {
            var f = new NpcFixture();
            HouseholdLedger ledger = f.RegisterHousehold(7, 500);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = Holding(f, 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Taxes, f.Ids, holding, 1880, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();

            // Partial principal payment while no fees exist yet.
            int credited = NpcPropertyLifeService.AuditTaxes(f.Taxes, f.Cash,
                new[] { holding }, f.Authority, treasury, 150, f.Diag, out int paid);
            Assert.AreEqual(500, paid);
            Assert.AreEqual(500, credited);
            Assert.AreEqual(TaxAssessmentStatus.Assessed, assessment.Status, "still open: 700 principal remains");

            // Fees accrue AFTER that partial principal payment. Under the
            // old AmountPaidCents formula these 200c would have routed to
            // the treasury (200 minus 500, floored at 0 fee portion).
            assessment.PenaltiesAccruedCents = 200;
            Assert.IsNull(ledger.RecordInflow(160, 1000, HouseholdIncomeSource.OwnerContribution,
                "test-seed", "top-up", "test"));
            int credited2 = NpcPropertyLifeService.AuditTaxes(f.Taxes, f.Cash,
                new[] { holding }, f.Authority, treasury, 170, f.Diag, out int paid2);
            Assert.AreEqual(900, paid2, "700 principal remaining plus 200 fees");
            Assert.AreEqual(700, credited2, "only the principal reaches the treasury");
            Assert.AreEqual(200, collector.ReadBalanceCents(), "the late fees are the collector's income");
            Assert.AreEqual(1200, treasury.BalanceCents);
            Assert.AreEqual(200, assessment.FeesPaidCents, "the fee split is booked on the assessment");
            Assert.AreEqual(100, ledger.GetBalanceCents(), "500 spent, 1000 topped up, 900 paid");
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);
        }

        [Test]
        public void NpcLeg_PartialPrincipalThenFees_FeesFirstOnNextPayment()
        {
            var f = new NpcFixture();
            f.RegisterHousehold(7, 10000);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = Holding(f, 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Taxes, f.Ids, holding, 1880, 1200, 100, 100, 600, 10, f.Diag);
            var treasury = new CivicTreasuryService();

            // Pay part of the principal directly through the single-payment
            // leg (all to the collector store), then fees accrue.
            Assert.IsNull(f.Life.PayPropertyTax(f.Taxes, f.Cash, holding, assessment, 500, 50, f.Diag));
            Assert.AreEqual(500, assessment.AmountPaidCents);
            assessment.PenaltiesAccruedCents = 100;

            int credited = NpcPropertyLifeService.AuditTaxes(f.Taxes, f.Cash,
                new[] { holding }, f.Authority, treasury, 150, f.Diag, out int paid);
            Assert.AreEqual(800, paid, "700 principal remaining plus 100 fees");
            Assert.AreEqual(700, credited, "only the principal reaches the treasury");
            Assert.AreEqual(600, collector.ReadBalanceCents(), "500 from the single-payment leg plus the 100 fee portion from the audit");
            Assert.AreEqual(100, assessment.FeesPaidCents);
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);

            int creditedAgain = NpcPropertyLifeService.AuditTaxes(f.Taxes, f.Cash,
                new[] { holding }, f.Authority, treasury, 160, f.Diag, out int paidAgain);
            Assert.AreEqual(0, creditedAgain);
            Assert.AreEqual(0, paidAgain, "fees are never collected twice");
        }

        [Test]
        public void OldSave_UntrackedAssessment_ReconstructionPreservesSettledFees()
        {
            var taxes = new PropertyTaxService();
            var assessment = new TaxAssessment
            {
                AssessmentId = "old-1",
                ParcelId = "parcel-1",
                OwnerName = "household:7",
                TaxYear = 1880,
                AmountCents = 1200,
                DueDayIndex = 100,
                PenaltyBpsMonthly = 100,
                InterestBpsAnnual = 600,
                PenaltiesAccruedCents = 200,
                AmountPaidCents = 1200,
                Status = TaxAssessmentStatus.Assessed,
                FeesPaidTracked = false,
            };
            taxes.LoadFromSaveDto(new PropertyTaxService.PropertyTaxSaveDto
            {
                Assessments = new List<TaxAssessment> { assessment },
            });
            // Pass XII Worker F (copy-on-load): load clones the DTO, so the
            // reconstruction is asserted on the LIVE assessment, not on the
            // snapshot object — and the snapshot itself must be untouched,
            // so a second load of the same DTO reconstructs identically.
            Assert.IsFalse(assessment.FeesPaidTracked, "load must not mutate its snapshot");
            Assert.AreEqual(0, assessment.FeesPaidCents, "load must not mutate its snapshot");
            TaxAssessment live = taxes.GetAssessment("old-1");
            Assert.IsNotNull(live);
            Assert.IsTrue(live.FeesPaidTracked);
            Assert.AreEqual(200, live.FeesPaidCents,
                "reconstruction: fees already settled under the old book stay settled; only later accruals route anew");
            Assert.AreEqual(0, live.UnpaidFeesCents());
        }

        [Test]
        public void PlayerLeg_SatisfiedAssessment_RefusedBeforeSpending()
        {
            int cash = 10000;
            var taxes = new PropertyTaxService();
            TaxAssessment assessment = taxes.LevyAssessment(new EntityIdRegistry(), "plot-7", "Player",
                1880, 1200, 100, 100, 600, new List<string>());
            var driver = new PlayerPropertyTaxDriver(taxes, () => new List<int>(), _ => 0,
                (int cents, string label, out string message) =>
                {
                    if (cents > cash) { message = "insufficient"; return false; }
                    cash -= cents; message = "ok"; return true;
                });
            var treasury = new CivicTreasuryService();
            Assert.IsTrue(driver.TryPayAssessment(assessment.AssessmentId, 1200, 50, out string message, treasury), message);
            Assert.AreEqual(8800, cash);
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);

            bool ok = driver.TryPayAssessment(assessment.AssessmentId, 100, 60, out message, treasury);
            Assert.IsFalse(ok, "a satisfied assessment refuses");
            Assert.AreEqual(8800, cash, "no debit is stranded by the refusal");
            Assert.AreEqual(1200, treasury.BalanceCents, "no second credit either");
        }

        [Test]
        public void PlayerLeg_PartialPaymentThenFees_BooksFeeSplit()
        {
            int cash = 10000;
            var taxes = new PropertyTaxService();
            TaxAssessment assessment = taxes.LevyAssessment(new EntityIdRegistry(), "plot-7", "Player",
                1880, 1200, 100, 100, 600, new List<string>());
            var driver = new PlayerPropertyTaxDriver(taxes, () => new List<int>(), _ => 0,
                (int cents, string label, out string message) =>
                {
                    if (cents > cash) { message = "insufficient"; return false; }
                    cash -= cents; message = "ok"; return true;
                });
            var treasury = new CivicTreasuryService();
            Assert.IsTrue(driver.TryPayAssessment(assessment.AssessmentId, 500, 50, out string message, treasury), message);
            assessment.PenaltiesAccruedCents = 100;
            Assert.IsTrue(driver.TryPayAssessment(assessment.AssessmentId, 100, 60, out message, treasury), message);
            Assert.AreEqual(100, assessment.FeesPaidCents, "the fee portion of the second payment is booked");
            Assert.AreEqual(700, assessment.TotalOwedCents(), "1200 plus 100 fees minus 600 paid");
        }
    }
}
