using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D-4: construction-loan path. A household whose cash cannot fund a
    /// build may fund it with an approved construction loan instead: the
    /// loan is underwritten through the real financing evaluator (the
    /// construction purpose policy), the principal is drawn from the
    /// lender's real cash store through the real cash mover, the debt is
    /// created through the single debt authority with the offer's terms
    /// and a repayment schedule, a first lien is filed on the parcel, and
    /// the driver services one installment per due day. Only a full
    /// approval with no blocking readiness gap ever funds — a conditional
    /// approval, a declined application, or a lender who cannot fund are
    /// refused loudly, with nothing moved and nothing faked as paid. A
    /// household that cannot cover an installment misses it loudly (the
    /// obligation goes delinquent, the payment stays due for retry).
    /// The fixture binds no labour port problem: the build itself waits
    /// for labour only through the own-labour mirror port (copied from
    /// the D-3 fixture, hired fallback removed), so the house completes
    /// and the household's cash is spent on materials before the first
    /// installment falls due — the loan is serviced on its own schedule,
    /// not the build's.
    /// </summary>
    [TestFixture]
    public sealed class NpcConstructionLoanDef011Tests
    {
        private sealed class FakeParcelAuthority : IConstructionParcelAuthority
        {
            public readonly Dictionary<string, string> HolderByParcel = new Dictionary<string, string>();
            public string AuthorizeBuild(int householdId, string parcelId, int dayIndex, List<string> diagnostics)
            {
                if (string.IsNullOrWhiteSpace(parcelId)) return "no parcel id.";
                if (!HolderByParcel.TryGetValue(parcelId, out string holder))
                    return $"parcel '{parcelId}' is not a registered parcel.";
                if (!string.Equals(holder, "household:" + householdId, StringComparison.Ordinal))
                    return $"parcel '{parcelId}' is held by '{holder}', not 'household:{householdId}'.";
                return null;
            }
        }

        private sealed class FakePricePort : IConstructionMaterialPricePort
        {
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int UnitPriceCents(ConstructionMaterialRequirement requirement)
            {
                if (requirement == null) return -1;
                return Prices.TryGetValue(requirement.MaterialDisplayName, out int price) ? price : -1;
            }
        }

        private sealed class FakeMaterialSource : IConstructionMaterialSource
        {
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public int AvailableUnits(ConstructionMaterialRequirement requirement)
            {
                return requirement == null ? 0
                    : Stock.TryGetValue(requirement.MaterialDisplayName, out int units) ? units : 0;
            }
            public string TryConsume(ConstructionMaterialRequirement requirement, int units,
                string projectLabel, int dayIndex, List<string> diagnostics, out string provenanceLabel)
            {
                provenanceLabel = string.Empty;
                int have = AvailableUnits(requirement);
                if (have < units) return $"FakeMaterialSource: only {have} of '{requirement.MaterialDisplayName}'.";
                Stock[requirement.MaterialDisplayName] = have - units;
                provenanceLabel = $"lot '{requirement.MaterialDisplayName}' x{units} ({projectLabel}, day {dayIndex})";
                return null;
            }
        }

        private sealed class FakeToolCustody : IHouseholdToolCustody
        {
            private readonly HashSet<string> held = new HashSet<string>();
            public void Grant(int householdId, string toolId) => held.Add(householdId + ":" + toolId);
            public bool HouseholdHoldsTool(int householdId, string toolItemId) =>
                held.Contains(householdId + ":" + toolItemId);
        }

        /// <summary>Own labour only (the D-3 mirror port with the hired fallback removed).</summary>
        private sealed class OwnLaborPort : INpcConstructionLaborPort
        {
            private readonly LoanChain chain;
            public OwnLaborPort(LoanChain chain) { this.chain = chain; }
            public string WorkPackage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag)
            {
                NpcConstructionRecord record = chain.Commissioner.FindRecordForProject(project.ProjectId);
                string commit = chain.Commissioner.CommitOwnLabor(chain.Executor, project, record, package,
                    101, chain.Tracker, chain.Custody, chain.Tasks, chain.Budgets,
                    dayIndex, 360, package.LaborMinutesRequired, diag);
                if (commit != null) return commit;
                string taskId = null;
                for (int i = project.LaborTaskBindings.Count - 1; i >= 0; i--)
                {
                    LaborTaskBinding binding = project.LaborTaskBindings[i];
                    if (binding != null && string.Equals(binding.PackageId, package.PackageId, StringComparison.Ordinal))
                    {
                        taskId = binding.TaskId;
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(taskId)) return "no labor task bound to the package.";
                return chain.Executor.WorkCommittedLabor(project, package, taskId,
                    package.LaborMinutesRequired, chain.Tasks, chain.Budgets, dayIndex, diag);
            }
        }

        private sealed class LoanChain
        {
            public List<string> Diag = new List<string>();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public ConstructionProjectExecutor Executor = new ConstructionProjectExecutor();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public FakeParcelAuthority Parcels = new FakeParcelAuthority();
            public FakePricePort Prices = new FakePricePort();
            public FakeMaterialSource Source = new FakeMaterialSource();
            public FakeToolCustody Custody = new FakeToolCustody();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public HousingAuthority Housing = new HousingAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<ConstructionProject> Projects = new List<ConstructionProject>();
            public readonly Dictionary<int, HouseholdLedger> Ledgers = new Dictionary<int, HouseholdLedger>();
            /// <summary>When false the loan leg stays unbound (no applicant provider, no lender store name).</summary>
            public bool BindLoan = true;
            /// <summary>Applicant-profile knobs (the strong defaults approve).</summary>
            public float ApplicantReputation = 0.82f;
            public float ApplicantTrust = 0.76f;
            public float ApplicantReliability = 0.78f;
            public int ApplicantWeeklyNet = 18000;
            public int ApplicantDebtPayment = 2000;
            public int ApplicantAssets = 320000;
            public int ApplicantFailures = 0;

            public LoanChain()
            {
                Prices.Prices["fieldstone"] = 50;
                Prices.Prices["lumber"] = 40;
                Source.Stock["fieldstone"] = 1000;
                Source.Stock["lumber"] = 10000;
                Custody.Grant(7, "shovel");
                Custody.Grant(7, "saw");
                Parcels.HolderByParcel["parcel-9"] = "household:7";
            }

            public void FundHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
                Ledgers[householdId] = ledger;
                if (householdId == 7)
                {
                    var yard = new PurseCashStore("business:lumber-yard", 0, "test seed capital");
                    Assert.IsNull(Cash.Register("business:lumber-yard", yard));
                }
            }

            public void FundBank(int cashCents)
            {
                var bank = new PurseCashStore("business:frontier-bank", cashCents, "test bank capital");
                Assert.IsNull(Cash.Register("business:frontier-bank", bank));
            }

            public void PayWages(int householdId, int dayIndex, int cashCents)
            {
                Assert.IsNull(Ledgers[householdId].RecordInflow(dayIndex, cashCents,
                    HouseholdIncomeSource.WageEmployment, "test-wages",
                    "test weekly wages", "test employer"));
            }

            public FinancingApplicantProfile ProfileFor(int householdId)
            {
                return new FinancingApplicantProfile
                {
                    applicantId = "household:" + householdId,
                    reputation01 = ApplicantReputation,
                    lenderTrust01 = ApplicantTrust,
                    operationalReliability01 = ApplicantReliability,
                    availableCashCents = Ledgers.TryGetValue(householdId, out HouseholdLedger ledger)
                        ? ledger.GetBalanceCents() : 0,
                    existingDebtPaymentCents = ApplicantDebtPayment,
                    weeklyNetCashFlowCents = ApplicantWeeklyNet,
                    ownedAssetValueCents = ApplicantAssets,
                    priorFailedFinancingCount = ApplicantFailures,
                };
            }

            public NpcConstructionDriver NewDriver()
            {
                var driver = new NpcConstructionDriver(Commissioner, Executor, Catalog, Projects,
                    Cash, Obligations, Ids, Housing, Prices, Source, "business:lumber-yard",
                    null, new OwnLaborPort(this),
                    householdId => householdId == 7 ? new List<int> { 101 } : new List<int>());
                if (BindLoan)
                {
                    driver.LoanApplicantByHousehold = ProfileFor;
                    driver.LoanLenderOwnerName = "business:frontier-bank";
                }
                return driver;
            }

            public static ConstructionMaterialRequirement Material(string name, int units, string unitLabel,
                ConstructionMaterialKind kind)
            {
                return new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-" + name,
                    MaterialKind = kind,
                    MaterialDisplayName = name,
                    RequiredUnits = units,
                    UnitLabel = unitLabel,
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                };
            }

            public void RegisterCabin()
            {
                var design = new BuildingDesign
                {
                    DesignId = "cabin-a",
                    DisplayName = "Settler cabin",
                    WorksKind = ConstructionWorksKind.Unspecified,
                    KindLabel = "cabin",
                };
                var foundation = new BuildingDesignPhase
                {
                    PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240, Notes = "stone footings",
                };
                foundation.Materials.Add(Material("fieldstone", 40, "perch", ConstructionMaterialKind.Masonry));
                foundation.RequiredToolItemIds.Add("shovel");
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 1, LaborMinutes = 480, Notes = "log walls and roof",
                };
                framing.Materials.Add(Material("lumber", 600, "board feet", ConstructionMaterialKind.Lumber));
                framing.RequiredToolItemIds.Add("saw");
                design.Phases.Add(foundation);
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(design, Diag), string.Join("; ", Diag));
            }

            public NpcConstructionRequest CabinRequest()
            {
                return new NpcConstructionRequest
                {
                    HouseholdId = 7,
                    DesignId = "cabin-a",
                    ParcelId = "parcel-9",
                    MemberPersonIds = new List<int> { 101 },
                    Arrangement = AccommodationArrangement.OwnerOccupied,
                    Reason = NpcConstructionReason.Overcrowding,
                    ReasonNotes = "test fixture: household of 1 in a cabin sleeping 4",
                };
            }

            /// <summary>Commissions and asserts success (use CommissionOrNull for refusal tests).</summary>
            public ConstructionProject Commission(NpcConstructionDriver driver, int day)
            {
                Budgets.EnsureDay(day);
                ConstructionProject project = driver.Commission(CabinRequest(), Parcels, day, Diag);
                Assert.IsNotNull(project, string.Join("; ", Diag));
                return project;
            }

            public ConstructionProject CommissionOrNull(NpcConstructionDriver driver, int day)
            {
                Budgets.EnsureDay(day);
                return driver.Commission(CabinRequest(), Parcels, day, Diag);
            }

            public void DriveDay(NpcConstructionDriver driver, int day)
            {
                Budgets.EnsureDay(day);
                driver.Drive(day, Diag);
            }

            public int HouseholdBalance(int householdId) =>
                Cash.FindStore("household:" + householdId).ReadBalanceCents();

            public int BankBalance() =>
                Cash.FindStore("business:frontier-bank").ReadBalanceCents();
        }

        private static bool DiagMentions(IReadOnlyList<string> diag, string fragment)
        {
            foreach (string line in diag)
                if (line != null && line.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Cabin estimate: 40 fieldstone x 50c + 600 lumber x 40c = 26000c.</summary>
        private const int CabinEstimateCents = 26000;

        [Test]
        public void Loan_FundsCommissionCashCannotFund()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.FundHousehold(7, 15000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record);
            Assert.IsNotNull(record.LoanTerms, "the commission was loan-funded — the terms belong on the record.");
            NpcConstructionLoanTerms terms = record.LoanTerms;
            Assert.AreEqual(FinancingDecision.Approved, terms.Decision);
            Assert.AreEqual(CabinEstimateCents, terms.EvaluatedPriceCents);
            Assert.AreEqual(11000, terms.PrincipalCents, "exact-shortfall borrowing: 26000c price minus 15000c cash.");
            Assert.AreEqual(15000, terms.DownPaymentCents);
            Assert.IsTrue(terms.IsDrawn, "the loan is drawn at commission time.");
            Assert.AreEqual(10, terms.DrawnDayIndex);
            Assert.IsFalse(string.IsNullOrWhiteSpace(terms.ObligationId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(terms.SecurityInterestId),
                "a first lien on the parcel is filed at draw time.");
            Assert.IsNotNull(terms.Schedule);
            Assert.AreEqual(11000, terms.Schedule.totalPrincipalCents, "the schedule balances the principal.");
            Assert.Greater(terms.Schedule.payments.Count, 0);
            Assert.AreEqual(17, terms.Schedule.payments[0].dueDayIndex,
                "weekly installments, first due 7 days after the draw.");
            Assert.Greater(terms.OriginationFeeCents, 0);

            // Cash is conserved: the principal moved bank -> household, nothing conjured.
            Assert.AreEqual(15000 + 11000, c.HouseholdBalance(7));
            Assert.AreEqual(100000 - 11000, c.BankBalance());

            FinancialObligation obligation = c.Obligations.Find(terms.ObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual("household:7", obligation.Debtor);
            Assert.AreEqual("business:frontier-bank", obligation.Creditor);
            Assert.AreEqual(11000, obligation.OriginalPrincipalCents);
            Assert.AreEqual(terms.AnnualInterestRateBps, obligation.PaymentTerms.AnnualInterestRateBps);
            Assert.AreEqual(terms.OriginationFeeCents, obligation.FeesCents,
                "the origination fee is carried on the obligation, collected with the first payment.");
            Assert.AreEqual(terms.Schedule.payments[terms.Schedule.payments.Count - 1].dueDayIndex,
                obligation.MaturityDayIndex);
            Assert.IsTrue(DiagMentions(record.Decisions, "construction loan drawn"),
                "the draw is on the record, not just in diagnostics.");
            Assert.IsTrue(DiagMentions(record.Decisions, "funded by cash 15000c + construction loan 11000c"),
                "the funding stack is on the record.");
        }

        [Test]
        public void Loan_CashOnlyWhenCashCovers_NoLoanSought()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record);
            Assert.IsNull(record.LoanTerms, "cash covers the build — no loan is sought.");
            Assert.AreEqual(100000, c.HouseholdBalance(7), "no principal was drawn.");
            Assert.AreEqual(100000, c.BankBalance());
            Assert.AreEqual(0, c.Obligations.TotalOutstandingFor("household:7"));
        }

        [Test]
        public void Loan_WeakProfile_DeclinedLoudly()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.ApplicantReputation = 0.18f;
            c.ApplicantTrust = 0.12f;
            c.ApplicantReliability = 0.16f;
            c.ApplicantWeeklyNet = 12000;
            c.ApplicantDebtPayment = 8000;
            c.ApplicantAssets = 25000;
            c.ApplicantFailures = 3;
            c.FundHousehold(7, 15000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.CommissionOrNull(driver, 10);

            Assert.IsNull(project, "a household that cannot carry the loan is refused, never silently funded.");
            Assert.AreEqual(0, c.Projects.Count);
            Assert.AreEqual(15000, c.HouseholdBalance(7));
            Assert.AreEqual(100000, c.BankBalance());
            Assert.AreEqual(0, c.Obligations.TotalOutstandingFor("household:7"));
            Assert.IsTrue(DiagMentions(c.Diag, "Declined"),
                "the underwriting decline is named in diagnostics.");
        }

        [Test]
        public void Loan_CollateralCapacity_DeclinedLoudly()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            // Cash 2000c: principal 24000c against ~14639c of collateral
            // capacity (the structure valued at its 26000c estimate) —
            // the security cannot carry the loan.
            c.FundHousehold(7, 2000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.CommissionOrNull(driver, 10);

            Assert.IsNull(project);
            Assert.AreEqual(0, c.Projects.Count);
            Assert.AreEqual(2000, c.HouseholdBalance(7));
            Assert.AreEqual(100000, c.BankBalance());
            Assert.AreEqual(0, c.Obligations.TotalOutstandingFor("household:7"));
            Assert.IsTrue(DiagMentions(c.Diag, "ExceedsLoanToValue"),
                "the capacity refusal names its reason code.");
        }

        [Test]
        public void Loan_ConditionalApproval_NotFunded()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            // Weekly net 2050c against 2000c of existing debt: the
            // cash-flow shortfall is ~29c, inside the conditional grace —
            // so the bank conditions the loan instead of approving it.
            // A conditional approval never funds a commission.
            c.ApplicantWeeklyNet = 2050;
            c.FundHousehold(7, 15000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.CommissionOrNull(driver, 10);

            Assert.IsNull(project);
            Assert.AreEqual(0, c.Projects.Count);
            Assert.AreEqual(15000, c.HouseholdBalance(7));
            Assert.AreEqual(100000, c.BankBalance());
            Assert.AreEqual(0, c.Obligations.TotalOutstandingFor("household:7"));
            Assert.IsTrue(DiagMentions(c.Diag, "ConditionallyApproved"),
                "the conditional decision is named, not hidden behind the cash shortfall.");
        }

        [Test]
        public void Loan_LenderCannotFund_CommissionRefused()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.FundHousehold(7, 15000);
            var driver = c.NewDriver();

            // No lender cash store at all: the approved loan has nowhere
            // to draw from.
            ConstructionProject noStore = c.CommissionOrNull(driver, 10);
            Assert.IsNull(noStore);
            Assert.AreEqual(0, c.Projects.Count);
            Assert.AreEqual(0, c.Obligations.TotalOutstandingFor("household:7"));
            Assert.IsTrue(DiagMentions(c.Diag, "no real cash store"),
                "the missing lender store is named.");

            // A short lender: 5000c against the approved 11000c principal.
            c.Diag.Clear();
            c.FundBank(5000);
            ConstructionProject shortBank = c.CommissionOrNull(driver, 10);
            Assert.IsNull(shortBank);
            Assert.AreEqual(0, c.Projects.Count);
            Assert.AreEqual(15000, c.HouseholdBalance(7));
            Assert.AreEqual(5000, c.BankBalance());
            Assert.AreEqual(0, c.Obligations.TotalOutstandingFor("household:7"));
            Assert.IsTrue(DiagMentions(c.Diag, "short of the approved 11000c"),
                "the lender shortfall is named with the numbers.");
        }

        [Test]
        public void Loan_Unbound_StaysCashOnly()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.BindLoan = false;
            c.FundHousehold(7, 15000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.CommissionOrNull(driver, 10);

            Assert.IsNull(project, "with no underwriting bound, a cash-short commission stays refused.");
            Assert.AreEqual(0, c.Projects.Count);
            Assert.AreEqual(0, c.Obligations.TotalOutstandingFor("household:7"));
            Assert.IsTrue(DiagMentions(c.Diag, "no construction-loan underwriting bound"),
                "the skip is recorded, not silent.");
        }

        [Test]
        public void Loan_Repayment_FirstInstallmentPaid()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.FundHousehold(7, 15000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11); // stage foundation: 2000c
            c.DriveDay(driver, 12); // own labour works the foundation
            c.DriveDay(driver, 13); // stage framing: 24000c — household at 0c
            c.DriveDay(driver, 14); // own labour works the framing
            c.DriveDay(driver, 15); // complete and occupy
            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status);
            Assert.AreEqual(0, c.HouseholdBalance(7), "every cent went into materials.");

            c.PayWages(7, 16, 10000);
            c.DriveDay(driver, 16); // nothing due yet
            Assert.AreEqual(10000, c.HouseholdBalance(7));

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            int firstDue = record.LoanTerms.Schedule.payments[0].totalDueCents;
            c.DriveDay(driver, 17); // first installment falls due

            Assert.AreEqual(10000 - firstDue, c.HouseholdBalance(7));
            Assert.AreEqual(100000 - 11000 + firstDue, c.BankBalance());
            LoanPaymentDue first = record.LoanTerms.Schedule.payments[0];
            Assert.AreEqual(LoanPaymentStatus.Paid, first.status);
            Assert.AreEqual(first.totalDueCents, first.paidCents);
            Assert.AreEqual(17, record.LoanTerms.LastServicedDayIndex);
            FinancialObligation obligation = c.Obligations.Find(record.LoanTerms.ObligationId);
            Assert.AreEqual(1, obligation.Payments.Count);
            Assert.AreEqual(firstDue, obligation.Payments[0].AmountCents);
            Assert.Less(obligation.TotalOutstandingCents, 11000,
                "the payment covered interest and fee and reduced the principal.");
            Assert.AreEqual(0, obligation.FeesCents,
                "the origination fee rode with the first payment and is now collected.");
        }

        [Test]
        public void Loan_MissedPayment_RecordedThenRetried()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.FundHousehold(7, 15000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11);
            c.DriveDay(driver, 12);
            c.DriveDay(driver, 13); // household at 0c after staging
            c.DriveDay(driver, 14);
            c.DriveDay(driver, 15);
            Assert.AreEqual(0, c.HouseholdBalance(7));

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            int firstDue = record.LoanTerms.Schedule.payments[0].totalDueCents;
            c.DriveDay(driver, 17); // first installment falls due — no cash to pay it

            Assert.AreEqual(1, record.LoanTerms.MissedPayments);
            Assert.AreEqual(17, record.LoanTerms.LastMissedDayIndex);
            LoanPaymentDue first = record.LoanTerms.Schedule.payments[0];
            Assert.AreEqual(LoanPaymentStatus.Late, first.status);
            Assert.AreEqual(0, first.paidCents, "nothing is faked as paid.");
            FinancialObligation obligation = c.Obligations.Find(record.LoanTerms.ObligationId);
            Assert.AreEqual(FinancialObligationStatus.Delinquent, obligation.Status);
            Assert.AreEqual(0, obligation.Payments.Count);
            Assert.AreEqual(0, c.HouseholdBalance(7));
            Assert.AreEqual(100000 - 11000, c.BankBalance());
            Assert.IsTrue(DiagMentions(record.Decisions, "MISSED construction loan installment"),
                "the miss is on the record.");

            // Wages arrive; the next drive retries the same payment.
            c.PayWages(7, 17, 10000);
            c.DriveDay(driver, 18);
            Assert.AreEqual(1, record.LoanTerms.MissedPayments, "the miss stays recorded; it is not erased by paying.");
            Assert.AreEqual(LoanPaymentStatus.Paid, first.status);
            Assert.AreEqual(first.totalDueCents, first.paidCents);
            Assert.AreEqual(10000 - firstDue, c.HouseholdBalance(7));
            Assert.AreEqual(100000 - 11000 + firstDue, c.BankBalance());
            Assert.AreEqual(1, obligation.Payments.Count);
        }

        [Test]
        public void Loan_NoDuplicateDraw()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.FundHousehold(7, 15000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);

            string problem = c.Commissioner.DrawConstructionLoan(record, record.LoanTerms,
                c.Cash, c.Obligations, c.Ids, 11, c.Diag);
            Assert.IsNotNull(problem, "a loan is drawn exactly once.");
            Assert.IsTrue(problem.IndexOf("already drawn", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.AreEqual(15000 + 11000, c.HouseholdBalance(7), "no second principal moved.");
            Assert.AreEqual(100000 - 11000, c.BankBalance());
            Assert.AreEqual(11000 + record.LoanTerms.OriginationFeeCents,
                c.Obligations.TotalOutstandingFor("household:7"),
                "the outstanding is principal plus the origination fee — no second principal was drawn.");
        }

        [Test]
        public void Loan_PersistsThroughSaveLoad()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.FundHousehold(7, 15000);
            c.FundBank(100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            int paymentCount = record.LoanTerms.Schedule.payments.Count;

            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto dto = c.Commissioner.CaptureSaveDto();
            var restored = new NpcConstructionCommissioner();
            restored.LoadFromSaveDto(dto, c.Diag);
            NpcConstructionRecord loaded = restored.FindRecordForProject(project.ProjectId);

            Assert.IsNotNull(loaded);
            Assert.IsNotNull(loaded.LoanTerms);
            Assert.AreNotSame(record.LoanTerms, loaded.LoanTerms, "the load deep-copies the loan, never shares it.");
            Assert.AreEqual(record.LoanTerms.ObligationId, loaded.LoanTerms.ObligationId);
            Assert.AreEqual(11000, loaded.LoanTerms.PrincipalCents);
            Assert.AreEqual(10, loaded.LoanTerms.DrawnDayIndex);
            Assert.AreEqual(0, loaded.LoanTerms.MissedPayments);
            Assert.IsNotNull(loaded.LoanTerms.Schedule);
            Assert.AreEqual(paymentCount, loaded.LoanTerms.Schedule.payments.Count);
            Assert.AreEqual(17, loaded.LoanTerms.Schedule.payments[0].dueDayIndex);
        }

        [Test]
        public void Loan_StaleApprovalNeverFunds()
        {
            var c = new LoanChain();
            c.RegisterCabin();
            c.FundHousehold(7, 15000);
            c.FundBank(100000);

            // Underwrite for the real 26000c estimate, then corrupt the
            // price on the terms: the loan must not fund the design.
            var collateral = new CollateralProfile
            {
                collateralId = "parcel:parcel-9+structure:cabin-a",
                kind = LoanCollateralKind.ImprovedProperty,
                estimatedValueCents = CabinEstimateCents,
                lienPriority = 1,
                liquidity01 = 0.5f,
                condition01 = 1.0f,
                encumberedValueCents = 0,
            };
            FinancingApprovalResult result = c.Commissioner.EvaluateConstructionLoan(
                CabinEstimateCents, 15000, c.ProfileFor(7), collateral,
                default(LenderProfile),
                new LoanTermStructure
                {
                    termMonths = 60,
                    amortizationMonths = 60,
                    repaymentFrequency = RepaymentFrequency.Weekly,
                },
                "stale-test", 10, c.Diag);
            Assert.AreEqual(FinancingDecision.Approved, result.Decision);
            NpcConstructionLoanTerms terms = NpcConstructionCommissioner.BuildLoanTerms(
                result, collateral, "business:frontier-bank", CabinEstimateCents);
            terms.EvaluatedPriceCents = 99999;

            NpcConstructionEvaluation evaluation = c.Commissioner.EvaluateCommissionWithLoan(
                c.Catalog, "cabin-a", 1, c.Prices, 15000, terms, null,
                NpcConstructionReason.Overcrowding, "stale approval test", 10, c.Diag);
            Assert.IsFalse(evaluation.IsViable, "a loan evaluated for one price never funds another.");
            Assert.IsTrue(evaluation.RefusalReason.IndexOf("stale", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
