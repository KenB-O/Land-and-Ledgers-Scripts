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
    /// Pass IX WS6 (A1-11/A1-07): civic treasury conservation — the player
    /// tax leg and the NPC collector leg both credit the ONE civic store,
    /// and the store's own ledger always reconciles with its balance.
    /// Hub-level conservation is exercised through the exact DTO type the
    /// hub persists (SimulationSystemsHub itself is excluded from the
    /// Roslyn harness by sources.txt; its Capture/Load wiring is
    /// source-verified).
    /// </summary>
    [TestFixture]
    public sealed class Pass9CivicTreasuryTests
    {
        [Test]
        public void Treasury_CreditDebitNeverNegative_LedgerConserves()
        {
            var treasury = new CivicTreasuryService();
            var diag = new List<string>();
            Assert.IsTrue(treasury.TryCredit(5000, CivicLedgerEntryKind.TaxReceived, 10, "tax", diag));
            Assert.IsTrue(treasury.TryDebit(2000, CivicLedgerEntryKind.TeacherPayroll, 20, "teacher", diag));
            Assert.IsFalse(treasury.TryDebit(4000, CivicLedgerEntryKind.CivicPayroll, 21, "too much", diag),
                "a debit beyond the balance must refuse, never go negative");
            Assert.IsFalse(treasury.TryCredit(0, CivicLedgerEntryKind.OtherCredit, 22, "zero", diag));
            Assert.AreEqual(3000, treasury.BalanceCents);

            int ledgerSum = 0;
            foreach (CivicTreasuryLedgerEntry entry in treasury.Ledger) ledgerSum += entry.AmountCents;
            Assert.AreEqual(treasury.BalanceCents, ledgerSum, "ledger entries must sum to the balance");
            Assert.AreEqual(5000, treasury.TotalForKind(CivicLedgerEntryKind.TaxReceived));
            Assert.AreEqual(-2000, treasury.TotalForKind(CivicLedgerEntryKind.TeacherPayroll));
        }

        [Test]
        public void Treasury_SaveDto_RoundTripsClearThenLoad()
        {
            var treasury = new CivicTreasuryService();
            treasury.TryCredit(7500, CivicLedgerEntryKind.TaxReceived, 5, "tax");
            treasury.TryDebit(1500, CivicLedgerEntryKind.OtherDebit, 6, "works");
            CivicTreasuryService.CivicTreasurySaveDto dto = treasury.CaptureSaveDto();

            var restored = new CivicTreasuryService();
            restored.TryCredit(999, CivicLedgerEntryKind.OtherCredit, 1, "stale — must be cleared");
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(6000, restored.BalanceCents);
            Assert.AreEqual(2, restored.Ledger.Count, "clear-then-load: the stale entry must not survive");
            Assert.AreEqual(CivicLedgerEntryKind.TaxReceived, restored.Ledger[0].Kind);
        }

        [Test]
        public void PlayerLeg_TryPayAssessment_CreditsTreasuryExactly()
        {
            int cash = 10000;
            var taxes = new PropertyTaxService();
            TaxAssessment assessment = taxes.LevyAssessment(new EntityIdRegistry(), "plot-7", "Player",
                1880, 1200, 100, 100, 600, new List<string>());
            Assert.IsNotNull(assessment);
            var driver = new PlayerPropertyTaxDriver(taxes, () => new List<int>(), _ => 0,
                (int cents, string label, out string message) =>
                {
                    if (cents > cash) { message = "insufficient"; return false; }
                    cash -= cents; message = "ok"; return true;
                });
            var treasury = new CivicTreasuryService();

            Assert.IsTrue(driver.TryPayAssessment(assessment.AssessmentId, 500, 50, out string message, treasury), message);
            Assert.AreEqual(9500, cash);
            Assert.AreEqual(500, treasury.BalanceCents, "player debit == treasury credit for a partial payment");
            Assert.AreEqual(700, assessment.TotalOwedCents());

            Assert.IsTrue(driver.TryPayAssessment(assessment.AssessmentId, 700, 60, out message, treasury), message);
            Assert.AreEqual(8800, cash);
            Assert.AreEqual(1200, treasury.BalanceCents, "total credited == total paid, exactly once per cent");
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);
            Assert.AreEqual(1200, treasury.TotalForKind(CivicLedgerEntryKind.TaxReceived));
        }

        [Test]
        public void PlayerLeg_WithoutTreasury_BehaviorUnchanged()
        {
            int cash = 5000;
            var taxes = new PropertyTaxService();
            TaxAssessment assessment = taxes.LevyAssessment(new EntityIdRegistry(), "plot-9", "Player",
                1880, 900, 100, 100, 600, new List<string>());
            var driver = new PlayerPropertyTaxDriver(taxes, () => new List<int>(), _ => 0,
                (int cents, string label, out string message) =>
                {
                    if (cents > cash) { message = "insufficient"; return false; }
                    cash -= cents; message = "ok"; return true;
                });
            Assert.IsTrue(driver.TryPayAssessment(assessment.AssessmentId, 900, 50, out string message), message);
            Assert.AreEqual(4100, cash);
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);
        }

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

        [Test]
        public void NpcLeg_AuditTaxes_PrincipalToTreasury_FeesToCollector_ExactlyOnce()
        {
            var f = new NpcFixture();
            HouseholdLedger ledger = f.RegisterHousehold(7, 10000);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.Life.RegisterHolding("parcel-1", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Taxes, f.Ids, holding, 1880, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            assessment.PenaltiesAccruedCents = 100; // the collector's arrears-fee income
            var treasury = new CivicTreasuryService();

            int credited = NpcPropertyLifeService.AuditTaxes(f.Taxes, f.Cash,
                new[] { holding }, f.Authority, treasury, 150, f.Diag, out int paid);

            Assert.AreEqual(1300, paid, "owner paid principal + fees in full");
            Assert.AreEqual(1200, credited, "principal — and only principal — reaches the treasury");
            Assert.AreEqual(1200, treasury.BalanceCents);
            Assert.AreEqual(100, collector.ReadBalanceCents(), "the collector keeps the arrears-fee income");
            Assert.AreEqual(10000 - 1300, ledger.GetBalanceCents(), "owner debit == treasury + collector credits");
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);

            // A second audit must not re-credit the same cents.
            int creditedAgain = NpcPropertyLifeService.AuditTaxes(f.Taxes, f.Cash,
                new[] { holding }, f.Authority, treasury, 160, f.Diag, out int paidAgain);
            Assert.AreEqual(0, creditedAgain);
            Assert.AreEqual(0, paidAgain);
            Assert.AreEqual(1200, treasury.BalanceCents);
        }

        [Test]
        public void NpcLeg_AuditTaxes_ShortOwner_PaysWhatExists_RestStandsAsArrears()
        {
            var f = new NpcFixture();
            HouseholdLedger ledger = f.RegisterHousehold(7, 500);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.Life.RegisterHolding("parcel-1", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Taxes, f.Ids, holding, 1880, 1200, 100, 100, 600, 10, f.Diag);
            assessment.PenaltiesAccruedCents = 100;
            var treasury = new CivicTreasuryService();

            int credited = NpcPropertyLifeService.AuditTaxes(f.Taxes, f.Cash,
                new[] { holding }, f.Authority, treasury, 150, f.Diag, out int paid);

            Assert.AreEqual(500, paid);
            Assert.AreEqual(100, collector.ReadBalanceCents(), "fees are paid first");
            Assert.AreEqual(400, credited, "the remaining 400c of principal reaches the treasury");
            Assert.AreEqual(0, ledger.GetBalanceCents());
            Assert.AreEqual(800, assessment.TotalOwedCents(), "the unpaid balance stands as arrears — never forgiven, never double-credited");
        }

        [Test]
        public void DriveNpcPropertyLife_TaxMaintenanceAndOneDistressSale()
        {
            var f = new NpcFixture();
            HouseholdLedger sellerLedger = f.RegisterHousehold(7, 20000);
            HouseholdLedger buyerLedger = f.RegisterHousehold(8, 120000);
            f.RegisterPurse("County Tax Collector", 0);
            f.RegisterPurse("maintenance:parcel-1", 0);
            var titles = new TitleAuthority();
            var housing = new HousingAuthority();
            Assert.IsNotNull(titles.RegisterParcel("parcel-1", "house parcel", 5f,
                "household:7", TitleBasis.Purchase, 0, f.Diag));
            Building building = housing.RegisterBuilding("bld-parcel-1", "parcel-1", "house", 0, "test house", f.Diag);
            Assert.IsNotNull(building);
            Assert.IsNotNull(housing.DefineSpace(building.BuildingId, 4, f.Diag));
            NpcPropertyHolding holding = f.Life.RegisterHolding("parcel-1", building.BuildingId, 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);

            // Tax due on the service's own book (the drive's audit book).
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding, 1880, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            // Maintenance need the owner can afford.
            NpcPropertyMaintenanceNeed need = f.Life.ReportMaintenanceNeed(holding, "fix gate", 800, 10, f.Diag);
            Assert.IsNotNull(need);
            // Distress: a delinquent obligation in the one debt authority.
            FinancialObligation debt = f.Authority.Create(f.Ids, FinancialObligationKind.Payable,
                "household:7", "creditor", 5000, 10, "terms", "distress debt");
            Assert.IsNotNull(debt);
            f.Authority.MarkDelinquent(debt.ObligationId);

            var treasury = new CivicTreasuryService();
            var saleContext = new NpcDistressSaleContext
            {
                Ids = f.Ids, Authority = f.Authority, Cash = f.Cash, Titles = titles,
                Housing = housing, Workflow = new CreditOfferWorkflow(), Events = new CreditEventLog(),
                TaxService = f.Life.Taxes, BuyerHouseholdId = 8, SalePriceCents = 90000,
            };
            NpcPropertyLifeReport report = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 50, f.Diag, saleContext);

            Assert.AreEqual(1, report.HoldingsAudited);
            Assert.AreEqual(1200, report.TaxesPaidCents);
            Assert.AreEqual(1200, report.TreasuryCreditedCents);
            Assert.AreEqual(1200, treasury.BalanceCents);
            Assert.AreEqual(1, report.MaintenanceNeedsFunded);
            Assert.IsTrue(need.Funded);
            Assert.AreEqual(1, report.SalesAttempted, "at most one distress sale per drive");
            Assert.IsTrue(report.SaleCompleted, report.SaleNote);
            Assert.AreEqual("household:8", titles.CurrentHolder("parcel-1"));
            Assert.AreEqual(20000 - 1200 - 800 + 90000, sellerLedger.GetBalanceCents(),
                "seller: seed - tax - maintenance + sale price");
            Assert.AreEqual(120000 - 90000, buyerLedger.GetBalanceCents());
        }

        [Test]
        public void DriveNpcPropertyLife_NoHoldings_NoOpsCleanly()
        {
            var f = new NpcFixture();
            var treasury = new CivicTreasuryService();
            NpcPropertyLifeReport report = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 50, f.Diag);
            Assert.AreEqual(0, report.HoldingsAudited);
            Assert.AreEqual(0, report.TaxesPaidCents);
            Assert.AreEqual(0, report.SalesAttempted);
            Assert.AreEqual(0, treasury.BalanceCents);
        }
    }
}
