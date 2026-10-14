// Pass VIII (6.3): production-authority proofs for the live business lender.
// Every test drives the real FinancialObligationAuthority and the bank's real
// cash ledger — no invented money, no second debt registry.
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;

namespace LandLedgers.Editor.Financing
{
    [TestFixture]
    public sealed class BusinessLenderServiceTests
    {
        private const int Day = 100;
        private const string BankName = "Test Town Bank";

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditEventLog Events = new CreditEventLog();
            public EnterpriseValuationReadModel Valuation = new EnterpriseValuationReadModel();
            public Dictionary<string, BusinessInstanceState> Businesses =
                new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            public BusinessLenderService Lender;
            public List<string> Diagnostics = new List<string>();

            public Fixture(int charterCapitalCents)
            {
                Lender = new BusinessLenderService(BankName);
                Lender.BindHubServices(Ids, Obligations, Workflow, Events, Valuation);
                Lender.BindBusinessLookup(id =>
                {
                    BusinessInstanceState found;
                    return Businesses.TryGetValue(id, out found) ? found : null;
                });
                string problem = Lender.EnsureCharteredCapital(
                    charterCapitalCents, "test charter capital", Day, Diagnostics);
                Assert.IsNull(problem, "charter refused: " + problem);
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "field " + name);
            field.SetValue(target, value);
        }

        private static BusinessInstanceState MakeBusiness(string id, int cashCents)
        {
            var business = new BusinessInstanceState();
            SetField(business, "instanceId", id);
            Assert.IsTrue(business.TryAssignPremises(42), "premises assigned");
            var runtime = new BusinessRuntimeState();
            runtime.AddCashCents(cashCents);
            SetField(business, "runtimeState", runtime);
            return business;
        }

        private static void MakeBorrower(Fixture fixture, string id, int cashCents,
            int weeklyProfitCents, int profitWeeks, int transferableAssetsCents, float reputation01)
        {
            BusinessInstanceState business = MakeBusiness(id, cashCents);
            business.BusinessReputation.stockReliability01 = reputation01;
            business.BusinessReputation.valueFairness01 = reputation01;
            business.BusinessReputation.serviceExperience01 = reputation01;
            business.BusinessReputation.conditionPresentationTrust01 = reputation01;
            business.BusinessReputation.productTradeConfidence01 = reputation01;
            fixture.Businesses[id] = business;
            fixture.Valuation.RegisterBusiness(id, "owner:" + id, false);
            for (int i = 0; i < profitWeeks; i++)
                fixture.Valuation.RecordWeeklyProfit(id, weeklyProfitCents);
            fixture.Valuation.RecordTransferableAssets(id, transferableAssetsCents);
        }

        private static FinancialObligation ArrangeHealthyLoan(Fixture fixture, string borrowerId,
            int amountCents, int day)
        {
            FinancialObligation obligation = fixture.Lender.TryArrangeLoan(
                fixture.Ids, fixture.Obligations, borrowerId, amountCents, day,
                fixture.Diagnostics, out string refusalReason);
            Assert.IsNotNull(obligation, "healthy loan refused: " + refusalReason);
            // The delegation path's borrower leg: the executor credits the
            // business till after the lender returns the obligation.
            fixture.Businesses[borrowerId].RuntimeState.AdjustCashCents(amountCents, recordNetDelta: true);
            return obligation;
        }

        // ---------- lender availability: no unfunded proceeds ----------

        [Test]
        public void InsufficientLenderCash_LoanRefused_NoUnfundedProceeds()
        {
            var fixture = new Fixture(100000);
            MakeBorrower(fixture, "store-1", 100000, 20000, 6, 200000, 0.9f);

            FinancialObligation obligation = fixture.Lender.TryArrangeLoan(
                fixture.Ids, fixture.Obligations, "store-1", 500000, Day,
                fixture.Diagnostics, out string refusalReason);

            Assert.IsNull(obligation, "a loan the bank cannot fund must not exist");
            Assert.IsFalse(string.IsNullOrWhiteSpace(refusalReason), "refusal must say why");
            Assert.AreEqual(0, fixture.Obligations.Obligations.Count, "no debt record without funding");
            Assert.AreEqual(100000, fixture.Lender.CashOnHandCents, "lender cash untouched");
            Assert.AreEqual(0, fixture.Events.FindByKind(CreditEventKind.Settlement).Count,
                "no settlement event without disbursement");
        }

        // ---------- underwriting: no automatic approval ----------

        [Test]
        public void Underwriting_RefusesUncreditworthyBorrower()
        {
            var fixture = new Fixture(1000000);
            // Broke, loss-making, no assets, poor standing.
            MakeBorrower(fixture, "store-1", 1000, -5000, 6, 0, 0.1f);

            FinancialObligation obligation = fixture.Lender.TryArrangeLoan(
                fixture.Ids, fixture.Obligations, "store-1", 40000, Day,
                fixture.Diagnostics, out string refusalReason);

            Assert.IsNull(obligation, "an uncreditworthy borrower must be refused");
            Assert.IsFalse(string.IsNullOrWhiteSpace(refusalReason), "refusal must say why");
            Assert.AreEqual(0, fixture.Obligations.Obligations.Count, "no debt record on refusal");
            Assert.AreEqual(1000000, fixture.Lender.CashOnHandCents, "lender cash untouched");
            Assert.AreEqual(1, fixture.Events.FindByKind(CreditEventKind.LenderRefused).Count,
                "refusal is on the record");
        }

        // ---------- approval: real cash, exactly one debt record ----------

        [Test]
        public void ApprovedLoan_DisbursesRealCash_CreatesExactlyOneDebtRecord()
        {
            var fixture = new Fixture(1000000);
            MakeBorrower(fixture, "store-1", 100000, 20000, 6, 200000, 0.9f);

            FinancialObligation obligation = ArrangeHealthyLoan(fixture, "store-1", 50000, Day);

            Assert.AreEqual(1, fixture.Obligations.Obligations.Count, "exactly one debt record");
            Assert.AreEqual("store-1", obligation.Debtor);
            Assert.AreEqual(BankName, obligation.Creditor);
            Assert.AreEqual(FinancialObligationKind.Loan, obligation.Kind);
            Assert.AreEqual(50000, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(950000, fixture.Lender.CashOnHandCents, "the bank really held and moved the funds");
            Assert.AreEqual(150000, fixture.Businesses["store-1"].RuntimeState.CurrentCashCents,
                "proceeds landed in the borrower's till");

            // Senior security: the bank's institutional power, filed at priority 1.
            Assert.AreEqual(1, fixture.Obligations.Securities.Count, "one senior lien");
            SecurityInterestRecord security = new List<SecurityInterestRecord>(fixture.Obligations.Securities)[0];
            Assert.AreEqual(1, security.Priority);
            Assert.IsFalse(security.PermitsJuniorInterest, "senior lien bars junior interests");
            Assert.AreEqual(BankName, security.Creditor);

            // Guaranty established for a significant loan (owner is the player).
            Assert.AreEqual(1, fixture.Obligations.Guaranties.Count, "one owner guaranty");
            GuarantyRecord guaranty = new List<GuarantyRecord>(fixture.Obligations.Guaranties)[0];
            Assert.AreEqual("Player", guaranty.Guarantor);

            Assert.AreEqual(1, fixture.Events.FindByKind(CreditEventKind.Settlement).Count);
        }

        // ---------- interest accrues; repayment reduces the obligation ----------

        [Test]
        public void InterestAccrues_And_RepaymentReducesObligation()
        {
            var fixture = new Fixture(1000000);
            MakeBorrower(fixture, "store-1", 100000, 20000, 6, 200000, 0.9f);
            FinancialObligation obligation = ArrangeHealthyLoan(fixture, "store-1", 50000, Day);

            for (int day = Day + 1; day < Day + 30; day++)
                fixture.Lender.ProcessDay(day, fixture.Diagnostics);

            Assert.Greater(obligation.AccruedInterestCents, 0, "interest accrues as a real obligation");
            int lenderCashBefore = fixture.Lender.CashOnHandCents;
            int borrowerCashBefore = fixture.Businesses["store-1"].RuntimeState.CurrentCashCents;

            // First scheduled payment day: the borrower can pay.
            fixture.Lender.ProcessDay(Day + 30, fixture.Diagnostics);

            Assert.AreEqual(0, obligation.AccruedInterestCents, "collected interest clears the accrual");
            Assert.Less(obligation.OutstandingPrincipalCents, 50000, "repayment reduces principal");
            Assert.Greater(fixture.Lender.CashOnHandCents, lenderCashBefore, "repaid cash returns to the bank");
            List<CreditEvent> collections = fixture.Events.FindByKind(CreditEventKind.PaymentCollected);
            Assert.AreEqual(1, collections.Count);
            Assert.AreEqual(borrowerCashBefore - collections[0].AmountCents,
                fixture.Businesses["store-1"].RuntimeState.CurrentCashCents,
                "borrower cash conserved: exactly the collected payment left the till");
            Assert.AreEqual(lenderCashBefore + collections[0].AmountCents, fixture.Lender.CashOnHandCents,
                "the same cents arrived at the bank — conservation, not invention");
        }

        // ---------- default: guaranty called, collateral enforced ----------

        [Test]
        public void Default_CallsGuaranty_And_EnforcesCollateral()
        {
            var fixture = new Fixture(1000000);
            // Smaller collateral so a deficiency remains after enforcement.
            MakeBorrower(fixture, "store-1", 100000, 20000, 6, 20000, 0.9f);
            FinancialObligation obligation = ArrangeHealthyLoan(fixture, "store-1", 50000, Day);

            // The borrower spends everything: scheduled payments cannot be met.
            BusinessInstanceState business = fixture.Businesses["store-1"];
            business.RuntimeState.AdjustCashCents(-business.RuntimeState.CurrentCashCents, recordNetDelta: true);
            Assert.AreEqual(0, business.RuntimeState.CurrentCashCents);

            for (int i = 1; i <= 4; i++)
                fixture.Lender.ProcessDay(Day + 30 * i, fixture.Diagnostics);

            Assert.AreEqual(FinancialObligationStatus.Defaulted, obligation.Status, "four misses default the loan");

            // Guaranty called: a real new obligation on the guarantor, not a
            // clone of the covered principal.
            GuarantyRecord guaranty = new List<GuarantyRecord>(fixture.Obligations.Guaranties)[0];
            Assert.IsTrue(guaranty.Called, "the established guaranty is called");
            FinancialObligation call = fixture.Obligations.Find(guaranty.CalledObligationId);
            Assert.IsNotNull(call, "the call is a real obligation");
            Assert.AreEqual(FinancialObligationKind.GuarantyCall, call.Kind);
            Assert.AreEqual("Player", call.Debtor);
            Assert.AreEqual(BankName, call.Creditor);
            Assert.AreEqual(1, fixture.Events.FindByKind(CreditEventKind.GuarantyCalled).Count);

            // Collateral enforced through the senior lien: the bank takes the
            // asset at its appraised value; the shortfall stays live.
            SecurityInterestRecord security = new List<SecurityInterestRecord>(fixture.Obligations.Securities)[0];
            Assert.AreEqual("Enforced:" + (Day + 120), security.EnforcementState);
            Assert.Less(obligation.TotalOutstandingCents, 50000 + obligation.AccruedInterestCents + 20000,
                "the recovery reduced the debt");
            Assert.Greater(obligation.TotalOutstandingCents, 0, "the deficiency stays live — not wished away");
            Assert.AreEqual(FinancialObligationStatus.Defaulted, obligation.Status,
                "a deficiency keeps the defaulted obligation open");
        }

        // ---------- exactly-once: no duplicate debt ----------

        [Test]
        public void SecondIdenticalApproval_CannotDuplicateDebt()
        {
            var fixture = new Fixture(1000000);
            MakeBorrower(fixture, "store-1", 100000, 20000, 6, 200000, 0.9f);

            FinancialObligation first = ArrangeHealthyLoan(fixture, "store-1", 50000, Day);
            FinancialObligation second = fixture.Lender.TryArrangeLoan(
                fixture.Ids, fixture.Obligations, "store-1", 50000, Day,
                fixture.Diagnostics, out string refusalReason);

            Assert.IsNull(second, "the duplicate approval must not create debt");
            StringAssert.Contains("already arranged", refusalReason);
            StringAssert.Contains(first.ObligationId, refusalReason);
            Assert.AreEqual(1, fixture.Obligations.Obligations.Count, "still exactly one debt record");
            Assert.AreEqual(950000, fixture.Lender.CashOnHandCents, "no second disbursement");
        }

        // ---------- save/load: state preserved exactly once ----------

        [Test]
        public void SaveLoad_MidLoan_PreservesStateExactlyOnce()
        {
            var fixture = new Fixture(1000000);
            MakeBorrower(fixture, "store-1", 100000, 20000, 6, 200000, 0.9f);
            FinancialObligation obligation = ArrangeHealthyLoan(fixture, "store-1", 50000, Day);
            fixture.Lender.ProcessDay(Day + 30, fixture.Diagnostics); // first collection
            int outstandingAfterFirstPayment = obligation.TotalOutstandingCents;
            int lenderCashAfterFirstPayment = fixture.Lender.CashOnHandCents;

            BusinessLenderService.BusinessLenderServiceSaveDto lenderDto = fixture.Lender.CaptureSaveDto();
            FinancialObligationSaveDto authorityDto =
                fixture.Obligations.CaptureSaveDto();

            // Restore into fresh instances: clear-then-load, no duplication.
            var restored = new Fixture(1); // charter replaced by the DTO
            restored.Obligations.LoadFromSaveDto(authorityDto);
            restored.Lender.LoadFromSaveDto(lenderDto);

            Assert.AreEqual(1, restored.Obligations.Obligations.Count, "the debt is not duplicated on load");
            FinancialObligation restoredObligation = restored.Obligations.Find(obligation.ObligationId);
            Assert.IsNotNull(restoredObligation);
            Assert.AreEqual(outstandingAfterFirstPayment, restoredObligation.TotalOutstandingCents,
                "mid-loan balances survive the round trip");
            Assert.AreEqual(lenderCashAfterFirstPayment, restored.Lender.CashOnHandCents,
                "the bank's cash position survives the round trip");

            // Servicing continues on the restored lender: no duplicate debt,
            // the next scheduled payment still collects.
            restored.Lender.BindBusinessLookup(id =>
            {
                BusinessInstanceState found;
                return fixture.Businesses.TryGetValue(id, out found) ? found : null;
            });
            restored.Lender.ProcessDay(Day + 60, fixture.Diagnostics);
            Assert.AreEqual(1, restored.Obligations.Obligations.Count, "still exactly one debt record");
            Assert.Less(restoredObligation.TotalOutstandingCents, outstandingAfterFirstPayment,
                "repayment continues after restore");

            // The idempotency key survived: the same approval still refuses.
            FinancialObligation dup = restored.Lender.TryArrangeLoan(
                restored.Ids, restored.Obligations, "store-1", 50000, Day,
                restored.Diagnostics, out string refusalReason);
            Assert.IsNull(dup, "duplicate still refused after restore");
            Assert.AreEqual(1, restored.Obligations.Obligations.Count);
        }

        // ---------- the delegation path: manager Borrow reaches the lender ----------

        [Test]
        public void ManagerBorrow_ReachesProductionLender_ThroughDelegationPath()
        {
            var fixture = new Fixture(1000000);
            MakeBorrower(fixture, "store-1", 100000, 20000, 6, 200000, 0.9f);

            var services = new ManagerExecutionServices
            {
                FindBusiness = id =>
                {
                    BusinessInstanceState found;
                    return fixture.Businesses.TryGetValue(id, out found) ? found : null;
                },
                FinancialObligations = fixture.Obligations,
                Ids = fixture.Ids,
                BusinessLender = fixture.Lender,
            };
            var executor = new ManagerBorrowExecutor(services, new ManagerDecisionExecutionLedger());
            var context = new ManagerExecutionContext
            {
                BusinessInstanceId = "store-1",
                ManagerPersonId = 7,
                DayIndex = Day,
                PurchaseLimitCents = 30000,
                MaxBorrowingCents = 30000,
                DelegatedScopes = new List<string> { ManagerScopeNames.CashHandling },
            };
            var decision = new ManagerDecisionResult
            {
                Acted = true,
                ActionKind = ManagerActKinds.Borrow,
                ResultingReference = "intent:borrow-request:store-1:d100:20000c",
                DayIndex = Day,
            };

            ManagerExecutionOutcome outcome = executor.Execute(decision, context, fixture.Diagnostics);

            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);
            Assert.AreEqual(1, fixture.Obligations.Obligations.Count, "one real obligation");
            FinancialObligation obligation = new List<FinancialObligation>(fixture.Obligations.Obligations)[0];
            Assert.AreEqual("store-1", obligation.Debtor);
            Assert.AreEqual(BankName, obligation.Creditor);
            Assert.AreEqual(20000, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(120000, fixture.Businesses["store-1"].RuntimeState.CurrentCashCents,
                "loan proceeds funded the till through the delegation path");
            Assert.AreEqual(980000, fixture.Lender.CashOnHandCents,
                "the bank's real cash funded it — nothing invented");
        }

        [Test]
        public void ManagerBorrow_UncharteredLender_FailsHonestly()
        {
            var fixture = new Fixture(1000000);
            var unchartered = new BusinessLenderService("Unchartered Bank");
            unchartered.BindHubServices(fixture.Ids, fixture.Obligations, fixture.Workflow,
                fixture.Events, fixture.Valuation);
            unchartered.BindBusinessLookup(id =>
            {
                BusinessInstanceState found;
                return fixture.Businesses.TryGetValue(id, out found) ? found : null;
            });
            MakeBorrower(fixture, "store-1", 100000, 20000, 6, 200000, 0.9f);

            FinancialObligation obligation = unchartered.TryArrangeLoan(
                fixture.Ids, fixture.Obligations, "store-1", 20000, Day,
                fixture.Diagnostics, out string refusalReason);

            Assert.IsNull(obligation, "an uncapitalized bank cannot lend");
            StringAssert.Contains("not chartered", refusalReason);
            Assert.AreEqual(0, fixture.Obligations.Obligations.Count);
        }
    }
}
