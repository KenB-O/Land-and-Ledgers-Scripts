using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Legal;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Tests.Editor.Financing
{
    public sealed class FinancialObligationAuthorityTests
    {
        [Test]
        public void OneObligationOwnsBalanceAndPaymentHistory()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation obligation = authority.Create(
                ids, FinancialObligationKind.SellerFinance, "buyer", "seller",
                30000, 10, "6% simple; due in 90 days", "business purchase");

            Assert.IsNotNull(obligation);
            Assert.AreEqual(30000, obligation.OriginalPrincipalCents);
            Assert.AreEqual(30000, obligation.OutstandingPrincipalCents);
            Assert.IsNull(authority.AccrueInterest(obligation.ObligationId, 1500));
            FinancialPaymentRecord payment = authority.ApplyPayment(
                obligation.ObligationId, 5000, 20, "buyer", "seller", "event-1");

            Assert.IsNotNull(payment);
            Assert.AreEqual(5000, payment.AmountCents);
            Assert.AreEqual(1500, payment.InterestCents);
            Assert.AreEqual(3500, payment.PrincipalCents);
            Assert.AreEqual(26500, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(0, obligation.AccruedInterestCents);
            Assert.AreEqual(1, obligation.Payments.Count);
        }

        [Test]
        public void InterestAccrualThroughDayIsDeterministicAndIdempotent()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation obligation = authority.CreateWithTerms(
                ids, FinancialObligationKind.Loan, "borrower", "lender", 365000,
                0, "annual", "working capital", new FinancialPaymentTerms
                {
                    AnnualInterestRateBps = 1000,
                    Structure = FinancialPaymentStructure.MaturityPrincipal
                });

            Assert.IsNull(authority.AccrueInterestThroughDay(obligation.ObligationId, 365));
            Assert.AreEqual(36500, obligation.AccruedInterestCents);
            Assert.IsNull(authority.AccrueInterestThroughDay(obligation.ObligationId, 365));
            Assert.AreEqual(36500, obligation.AccruedInterestCents);
        }

        [Test]
        public void PlayerFinanceReadModelShowsOwedClaimsSecurityFacilitiesAndGuaranties()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation owed = authority.CreateWithTerms(
                ids, FinancialObligationKind.SellerFinance, "player", "seller", 30000, 1,
                "seller note", "equipment", new FinancialPaymentTerms
                {
                    AnnualInterestRateBps = 600,
                    Structure = FinancialPaymentStructure.InterestOnlyBalloon,
                    PaymentIntervalDays = 30,
                    InstallmentPrincipalCents = 0,
                    BalloonPrincipalCents = 30000
                });
            FinancialObligation claim = authority.Create(ids, FinancialObligationKind.Payable,
                "merchant", "player", 12500, 2, "account", "supplier advance");
            authority.AttachSecurity(ids, owed.ObligationId, "seller", "player", new[] { "oven-asset-1" }, 1, 1);
            authority.AddGuaranty(ids, owed.ObligationId, "seller", "player", "surety-1", 30000);
            authority.CreateFacility(ids, "supplier", "player", 50000, 30, 365, true, "open account");

            string text = FinancialObligationReadModel.BuildPlayerSummary(authority, "player", 10);

            StringAssert.Contains("Obligations owed: $300.00", text);
            StringAssert.Contains("Claims / notes held:", text);
            StringAssert.Contains("$125.00", text);
            StringAssert.Contains("Security:", text);
            StringAssert.Contains("oven-asset-1", text);
            StringAssert.Contains("Guaranty:", text);
            StringAssert.Contains("Credit facilities / open accounts:", text);
            StringAssert.Contains("undrawn $500.00", text);
            Assert.IsNotNull(claim);
        }

        [Test]
        public void PaymentStructuresResolveDueAmountsWithoutCreatingSecondBalance()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation installment = authority.CreateWithTerms(ids, FinancialObligationKind.Loan,
                "borrower", "lender", 10000, 0, "installments", "working capital",
                new FinancialPaymentTerms { Structure = FinancialPaymentStructure.PrincipalInstallments, InstallmentPrincipalCents = 2500, PaymentIntervalDays = 30 });
            FinancialObligation balloon = authority.CreateWithTerms(ids, FinancialObligationKind.Loan,
                "borrower", "lender", 10000, 0, "balloon", "equipment",
                new FinancialPaymentTerms { Structure = FinancialPaymentStructure.InterestOnlyBalloon, PaymentIntervalDays = 30 });
            balloon.MaturityDayIndex = 60;
            Assert.AreEqual(2500, authority.CalculateDueAmountCents(installment.ObligationId, 30));
            Assert.AreEqual(0, authority.CalculateDueAmountCents(balloon.ObligationId, 30));
            Assert.AreEqual(10000, authority.CalculateDueAmountCents(balloon.ObligationId, 60));
            FinancialPaymentRecord payment = authority.ApplyScheduledPayment(installment.ObligationId, 30, "borrower", "lender", "scheduled-1");
            Assert.IsNotNull(payment);
            Assert.AreEqual(7500, installment.OutstandingPrincipalCents);
            Assert.AreEqual(17500, authority.TotalOutstandingFor("borrower"));
        }

        [Test]
        public void EarlyPayoffRespectsContractPermissionAndFee()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation allowed = authority.CreateWithTerms(ids, FinancialObligationKind.Loan,
                "borrower", "lender", 10000, 0, "payoff", "equipment",
                new FinancialPaymentTerms { AllowsEarlyPayoff = true, PrepaymentFeeCents = 125 });
            FinancialObligation restricted = authority.CreateWithTerms(ids, FinancialObligationKind.Loan,
                "borrower", "lender", 5000, 0, "locked", "property",
                new FinancialPaymentTerms { AllowsEarlyPayoff = false });
            Assert.AreEqual(10125, authority.CalculateEarlyPayoffAmountCents(allowed.ObligationId));
            Assert.AreEqual(-1, authority.CalculateEarlyPayoffAmountCents(restricted.ObligationId));
        }

        [Test]
        public void LegalRegimeCanRejectAnUnlawfulWrittenRateAtOrigination()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            string rejection;
            FinancialObligation rejected = authority.CreateWithLegalTerms(ids, FinancialObligationKind.Loan,
                "borrower", "lender", 10000, 0, "written", "working capital",
                new FinancialPaymentTerms { AnnualInterestRateBps = 1500 },
                new LegalRegime(), false, out rejection);
            Assert.IsNull(rejected);
            StringAssert.Contains("12%", rejection);

            FinancialObligation accepted = authority.CreateWithLegalTerms(ids, FinancialObligationKind.Loan,
                "borrower", "lender", 10000, 0, "written", "working capital",
                new FinancialPaymentTerms { AnnualInterestRateBps = 1200 },
                new LegalRegime(), false, out rejection);
            Assert.IsNotNull(accepted);
            Assert.AreEqual("DakotaTerritory", accepted.LegalRegimeId);
        }

        [Test]
        public void LegalRegimeRequiresSpousalParticipationForHomesteadSecurity()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation obligation = authority.Create(ids, FinancialObligationKind.Loan,
                "owner", "lender", 10000, 1, "secured note", "working capital");
            var regime = new LegalRegime();
            SecurityInterestRecord rejected = authority.AttachSecurityWithLegalRegime(ids,
                obligation.ObligationId, "lender", "owner", new[] { "homestead-1" }, 1,
                regime, true, false, 1, true, out string rejection);
            Assert.IsNull(rejected);
            StringAssert.Contains("spousal", rejection);
            SecurityInterestRecord accepted = authority.AttachSecurityWithLegalRegime(ids,
                obligation.ObligationId, "lender", "owner", new[] { "homestead-1" }, 1,
                regime, true, true, 1, true, out rejection);
            Assert.IsNotNull(accepted, rejection);
            Assert.AreEqual(LegalJurisdiction.DakotaTerritory.ToString(), accepted.LegalRegimeId);
        }

        [Test]
        public void CreditOfferWorkflowUsesFiniteLiquidityAndCreatesSharedObligation()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "npc-borrower", "private-lender", 20000,
                "equipment", 90, 5, "business receipts", "wagon-1");
            CreditOffer offer = workflow.Evaluate(ids, request, authority, 12000, 5);
            Assert.AreEqual(12000, offer.OfferedAmountCents);
            var lenderCash = new CreditCashAccount("private-lender", 12000);
            var borrowerCash = new CreditCashAccount("npc-borrower", 500);
            FinancialObligation obligation = workflow.Accept(ids, offer.OfferId, authority, lenderCash, borrowerCash, 5);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(0, lenderCash.BalanceCents);
            Assert.AreEqual(12500, borrowerCash.BalanceCents);
            Assert.AreEqual(12000, authority.TotalOutstandingFor("npc-borrower"));
            Assert.AreEqual(1, obligation.SecurityInterestIds.Count);
        }

        [Test]
        public void DisclosedEvidenceChangesOfferWithoutUsingHiddenGlobalDebt()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var workflow = new CreditOfferWorkflow();
            FinancialObligation hidden = authority.Create(ids, FinancialObligationKind.Loan,
                "borrower", "old-lender", 15000, 1, "terms", "working capital");
            CreditRequest request = workflow.SubmitRequest(ids, "borrower", "new-lender", 20000,
                "working capital", 90, 5, "sales");
            CreditOffer undisclosed = workflow.Evaluate(ids, request, authority, 30000, 5);
            Assert.AreEqual(20000, undisclosed.OfferedAmountCents);
            workflow.DiscloseEvidence(ids, request, "supplier reference", "old account disclosed", hidden.ObligationId, 6);
            CreditOffer informed = workflow.Evaluate(ids, request, authority, 30000, 6);
            Assert.AreEqual(10000, informed.OfferedAmountCents);
            Assert.IsTrue(informed.EvidenceConsidered.Count > 0);
        }

        [Test]
        public void CreditOfferWorkflowSaveLoadPreservesNegotiationAndEvidence()
        {
            var ids = new EntityIdRegistry();
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "borrower", "seller", 10000,
                "property", 180, 4, "rent", "plot-1", "surety", "deal-1");
            CreditOffer offer = workflow.Evaluate(ids, request, new FinancialObligationAuthority(), 20000, 4);
            workflow.DiscloseEvidence(ids, request, "title search", "plot title verified", string.Empty, 4);
            CreditOfferWorkflow restored = new CreditOfferWorkflow();
            restored.LoadFromSaveDto(workflow.CaptureSaveDto());
            Assert.AreEqual(1, restored.Requests.Count);
            Assert.AreEqual(1, restored.Offers.Count);
            Assert.AreEqual(1, restored.Evidence.Count);
            Assert.AreEqual(offer.OfferId, ((List<CreditOffer>)new List<CreditOffer>(restored.Offers))[0].OfferId);
        }

        [Test]
        public void PlayerCreditorAndNpcBorrowerUseTheSameCollectionAuthority()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "npc-business", "player", 8000,
                "seasonal stock", 30, 1, "harvest receipts");
            CreditOffer offer = workflow.Evaluate(ids, request, authority, 8000, 1);
            FinancialObligation obligation = workflow.Accept(ids, offer.OfferId, authority,
                new CreditCashAccount("player", 8000), new CreditCashAccount("npc-business", 0), 1);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(8000, authority.TotalOutstandingFor("npc-business"));
            authority.MarkDue(obligation.ObligationId);
            Assert.AreEqual(8000, authority.CalculateDueAmountCents(obligation.ObligationId, 31));
            FinancialPaymentRecord payment = authority.ApplyScheduledPayment(obligation.ObligationId, 31,
                "npc-business", "player", "npc-payment-1");
            Assert.IsNotNull(payment);
            Assert.AreEqual(0, authority.TotalOutstandingFor("npc-business"));
        }

        [Test]
        public void SellerFinanceCanBeCounteredOrRefusedBeforeClosing()
        {
            var ids = new EntityIdRegistry();
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "buyer", "seller", 30000,
                "business acquisition", 180, 2, "business earnings", "business-7", "buyer-surety", "deal-7");
            CreditOffer offer = workflow.Evaluate(ids, request, new FinancialObligationAuthority(), 30000, 2);
            string message;
            Assert.IsTrue(workflow.CounterOffer(offer.OfferId, 15000, 850, 270, "First lien; quarterly review", out message));
            Assert.AreEqual(15000, offer.OfferedAmountCents);
            Assert.AreEqual(CreditOfferStatus.Countered, offer.Status);
            Assert.IsTrue(workflow.Decline(offer.OfferId, "Seller needs more cash at closing."));
            Assert.AreEqual(CreditOfferStatus.Declined, offer.Status);
        }

        [Test]
        public void CounterofferRequiresLenderDecisionBeforeClosing()
        {
            var ids = new EntityIdRegistry();
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "buyer", "private-lender", 20000,
                "equipment", 120, 2, "business receipts", "forge-1", "surety-1");
            CreditOffer offer = workflow.Evaluate(ids, request, new FinancialObligationAuthority(), 20000, 2);
            Assert.IsTrue(workflow.CounterOffer(offer.OfferId, 15000, 600, 180,
                "Junior security accepted", out string message, 3), message);
            var authority = new FinancialObligationAuthority();
            FinancialObligation blocked = workflow.Accept(ids, offer.OfferId, authority,
                new CreditCashAccount("private-lender", 20000), new CreditCashAccount("buyer", 0), 3);
            Assert.IsNull(blocked);
            Assert.IsTrue(workflow.AcceptCounterOffer(offer.OfferId, "private-lender", 4, out message), message);
            FinancialObligation accepted = workflow.Accept(ids, offer.OfferId, authority,
                new CreditCashAccount("private-lender", 20000), new CreditCashAccount("buyer", 0), 4);
            Assert.IsNotNull(accepted);
            Assert.AreEqual(CreditOfferStatus.Accepted, offer.Status);
            Assert.AreEqual(4, offer.NegotiationHistory.Count);
        }

        [Test]
        public void ExpiredOfferCannotBeCounteredAndBorrowerDeclineIsRecorded()
        {
            var ids = new EntityIdRegistry();
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "buyer", "seller", 10000,
                "property", 30, 1, "farm receipts");
            CreditOffer offer = workflow.Evaluate(ids, request, new FinancialObligationAuthority(), 10000, 1);
            Assert.IsFalse(workflow.CounterOffer(offer.OfferId, 8000, 700, 20,
                "more cash down", out string message, offer.ExpiryDayIndex + 1));
            Assert.AreEqual(CreditOfferStatus.Expired, offer.Status);

            CreditOffer second = workflow.Evaluate(ids, request, new FinancialObligationAuthority(), 10000, 1);
            Assert.IsTrue(workflow.Decline(second.OfferId, "Buyer declined."));
            Assert.AreEqual(CreditOfferNegotiationAction.BorrowerDeclined,
                second.NegotiationHistory[second.NegotiationHistory.Count - 1].Action);
        }

        [Test]
        public void LivestockAndStagedProjectFinanceUseRealObligationsAndIndividualCollateral()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "farm-business", "rancher", 45000,
                "one identifiable steer", 120, 1, "harvest receipts");
            CreditOffer offer = workflow.Evaluate(ids, request, authority, 45000, 1);
            FinancialObligation livestock = workflow.AcceptDeferredConsideration(ids, offer.OfferId,
                authority, 1, FinancialObligationKind.SellerFinance, "livestock purchase", "livestock-sale-1");
            SecurityInterestRecord security = authority.AttachSecurity(ids, livestock.ObligationId,
                "rancher", "farm-business", new[] { "animal-steer-001" }, 1, 1);
            Assert.IsNotNull(security);
            Assert.AreEqual("animal-steer-001", security.CollateralIds[0]);
            Assert.IsNotNull(authority.ApplyPayment(livestock.ObligationId, 45000, 30,
                "farm-business", "rancher", "harvest-payment"));
            Assert.IsTrue(authority.ReleaseSecurity(security.SecurityInterestId, "paid in full"));
            Assert.IsTrue(security.Released);

            CreditFacilityRecord projectFacility = authority.CreateFacility(ids, "project-lender",
                "construction-project-1", 100000, 1, 365, false, "staged progress advances");
            FinancialObligation firstAdvance = authority.DrawFacility(ids, projectFacility.FacilityId, 25000, 10);
            FinancialObligation secondAdvance = authority.DrawFacility(ids, projectFacility.FacilityId, 75000, 40);
            Assert.IsNotNull(firstAdvance);
            Assert.IsNotNull(secondAdvance);
            Assert.AreEqual(100000, authority.TotalOutstandingFor("construction-project-1"));
            Assert.IsNull(authority.DrawFacility(ids, projectFacility.FacilityId, 1, 41));
        }

        [Test]
        public void SecurityAllowsValidJuniorPriorityAndPreservesOwnershipParties()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation first = authority.Create(ids, FinancialObligationKind.Loan, "owner", "bank", 10000, 1, "terms", "equipment");
            FinancialObligation second = authority.Create(ids, FinancialObligationKind.Loan, "owner", "supplier", 2000, 2, "terms", "working capital");

            SecurityInterestRecord senior = authority.AttachSecurity(ids, first.ObligationId, "bank", "owner", new[] { "asset-1" }, 1, 1);
            SecurityInterestRecord junior = authority.AttachSecurity(ids, second.ObligationId, "supplier", "owner", new[] { "asset-1" }, 2, 2);

            Assert.IsNotNull(senior);
            Assert.IsNotNull(junior);
            Assert.AreEqual(1, senior.Priority);
            Assert.AreEqual(2, junior.Priority);
            Assert.AreEqual("owner", second.Debtor);
        }

        [Test]
        public void FacilityAuthorizationDoesNotBecomeDebtUntilDrawn()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            CreditFacilityRecord facility = authority.CreateFacility(ids, "supplier", "merchant", 50000, 30, 365, true, "seasonal account");

            Assert.IsNotNull(facility);
            Assert.AreEqual(0, authority.TotalOutstandingFor("merchant"));
            FinancialObligation draw = authority.DrawFacility(ids, facility.FacilityId, 12000, 40);

            Assert.IsNotNull(draw);
            Assert.AreEqual(12000, authority.TotalOutstandingFor("merchant"));
            Assert.AreEqual(12000, facility.DrawnPrincipalCents);
            Assert.AreEqual(38000, facility.AuthorizedLimitCents - facility.DrawnPrincipalCents);
        }

        [Test]
        public void SaveLoadPreservesObligationPaymentSecurityAndFacility()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation obligation = authority.Create(ids, FinancialObligationKind.Payable, "merchant", "jobber", 8000, 1, "net 30", "coffee");
            authority.AttachSecurity(ids, obligation.ObligationId, "jobber", "merchant", new List<string> { "inventory-1" }, 1, 1);
            authority.ApplyPayment(obligation.ObligationId, 1000, 4, "merchant", "jobber");
            authority.CreateFacility(ids, "jobber", "merchant", 20000, 10, 100, false, "open account");

            var restored = new FinancialObligationAuthority();
            restored.LoadFromSaveDto(authority.CaptureSaveDto());
            FinancialObligation copy = restored.Find(obligation.ObligationId);

            Assert.IsNotNull(copy);
            Assert.AreEqual(7000, copy.OutstandingPrincipalCents);
            Assert.AreEqual(1, copy.Payments.Count);
            Assert.AreEqual(1, restored.Securities.Count);
            Assert.AreEqual(1, restored.Facilities.Count);
            Assert.IsEmpty(restored.Reconcile());
        }

        [Test]
        public void CompatibilityLiabilityLedgerProjectsSharedBalance()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var ledger = new BusinessLiabilityLedger();
            ledger.AttachFinancialAuthority(authority);
            ledger.AttachValuation(new EnterpriseValuationReadModel());
            BusinessLiability liability = ledger.Borrow(ids, "business-1", "private lender", 9000,
                "demand note", "working capital", 1, null, new List<string>());

            Assert.IsNotNull(liability);
            Assert.AreEqual(9000, authority.TotalOutstandingFor("business-1"));
            Assert.IsNull(ledger.AccrueInterest(liability.LiabilityId.ToString(), 500, 2, null));
            Assert.AreEqual(9500, authority.TotalOutstandingFor("business-1"));
            var payer = new LandLedgers.Population.HouseholdLedger(1);
            payer.RecordInflow(0, 9500, LandLedgers.Population.HouseholdIncomeSource.OwnerContribution,
                "test", "repayment funding", "owner");
            Assert.IsNull(ledger.Repay(liability.LiabilityId.ToString(), 9500, 3, payer, new List<string>()));
            Assert.AreEqual(0, authority.TotalOutstandingFor("business-1"));
        }

        [Test]
        public void AssumptionCanAddDebtorWithoutImplyingReleaseOrNovation()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation obligation = authority.Create(ids, FinancialObligationKind.Loan,
                "seller", "bank", 10000, 1, "secured", "business purchase");

            ObligationAssumptionRecord assumption = authority.RecordAssumption(
                ids, obligation.ObligationId, "buyer", false, false, false, 2);

            Assert.IsNotNull(assumption);
            Assert.AreEqual("seller", obligation.Debtor);
            Assert.IsFalse(obligation.OriginalDebtorReleased);
            Assert.Contains("buyer", obligation.LiableParties);
        }

        [Test]
        public void RefinanceSatisfiesPredecessorAndPreservesLink()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation first = authority.Create(ids, FinancialObligationKind.Payable,
                "merchant", "jobber", 10000, 1, "net 30", "inventory");
            FinancialObligation second = authority.Create(ids, FinancialObligationKind.Loan,
                "merchant", "bank", 10000, 30, "90-day note", "refinance");

            // The public operation is intentionally exercised with a fresh predecessor below.
            var authority2 = new FinancialObligationAuthority();
            FinancialObligation predecessor = authority2.Create(ids, FinancialObligationKind.Payable,
                "merchant", "jobber", 10000, 1, "net 30", "inventory");
            FinancialObligation successor = authority2.Refinance(ids,
                new[] { predecessor.ObligationId }, "merchant", "bank", 10000, 30, "90-day note", "refinance");

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.IsNotNull(successor);
            Assert.IsTrue(predecessor.Settled);
            Assert.AreEqual(successor.ObligationId, predecessor.SuccessorObligationId);
            Assert.AreEqual(predecessor.ObligationId, successor.PredecessorObligationId);
        }

        [Test]
        public void InstrumentRegistryProjectsNoteAndGuarantyIntoSharedAuthority()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var registry = new CreditRegistry();
            registry.AttachFinancialAuthority(authority);
            var diagnostics = new List<string>();
            PromissoryNote note = registry.IssuePromissoryNote(ids, "buyer", "seller", 5000,
                "90-day note", 1, new List<string> { "wagon-1" }, diagnostics);
            GuarantyAgreement guaranty = registry.IssueGuaranty(ids, "surety", "seller", "buyer",
                note.InstrumentId.ToString(), 3000, "limited surety", 1, diagnostics);

            Assert.IsNotNull(note);
            Assert.IsNotNull(guaranty);
            Assert.AreEqual(5000, authority.TotalOutstandingFor("buyer"));
            Assert.AreEqual(3000, registry.ContingentExposureFor("surety"));
            Assert.IsNotNull(registry.ApplyInstrumentPayment(note.InstrumentId.ToString(), 5000,
                10, "buyer", "seller", diagnostics));
            Assert.AreEqual(0, authority.TotalOutstandingFor("buyer"));
        }

        [Test]
        public void GuarantyCallCreatesGuarantorExposureWithoutCloningCreditorPrincipal()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var registry = new CreditRegistry();
            registry.AttachFinancialAuthority(authority);
            var diagnostics = new List<string>();
            PromissoryNote note = registry.IssuePromissoryNote(ids, "buyer", "bank", 5000,
                "demand", 1, null, diagnostics);
            GuarantyAgreement guaranty = registry.IssueGuaranty(ids, "surety", "bank", "buyer",
                note.InstrumentId.ToString(), 3000, "surety", 1, diagnostics);

            registry.RecordDefault(note.InstrumentId.ToString(), 3000, 10, null, ids, "", diagnostics);

            Assert.AreEqual(5000, authority.TotalOutstandingFor("buyer"));
            Assert.AreEqual(3000, authority.TotalOutstandingFor("surety"));
            Assert.AreEqual(CreditInstrumentStatus.Called, guaranty.Status);
            Assert.AreEqual(2, authority.Obligations.Count);
            FinancialObligation recovery = authority.SettleGuarantyPayment(ids,
                guaranty.FinancialGuarantyId, 3000, 11, "guaranty-payment");
            Assert.IsNotNull(recovery);
            Assert.AreEqual(2000, authority.Find(note.ObligationId).TotalOutstandingCents);
            Assert.AreEqual(0, authority.TotalOutstandingFor("surety"));
            Assert.AreEqual("surety", recovery.Creditor);
        }

        [Test]
        public void SupplierPayableCanBecomeOneSuccessorNote()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var ledger = new BusinessLiabilityLedger();
            ledger.AttachFinancialAuthority(authority);
            ledger.AttachValuation(new EnterpriseValuationReadModel());
            BusinessLiability payable = ledger.BuyOnCredit(ids, "merchant", "jobber", 12000,
                "coffee invoice", 1, new List<string>());

            BusinessLiability note = ledger.ConvertPayableToNote(payable.LiabilityId.ToString(),
                ids, "90-day promissory note", 30, new List<string>());

            Assert.IsNotNull(note);
            Assert.AreEqual(0, payable.BalanceCents);
            Assert.AreEqual(12000, note.BalanceCents);
            Assert.AreEqual(12000, authority.TotalOutstandingFor("merchant"));
            Assert.AreEqual(2, ledger.All.Count);
        }

        [Test]
        public void CreditOfferPaymentMovesCashAndSettlesOneSharedBalance()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var workflow = new CreditOfferWorkflow();
            var request = workflow.SubmitRequest(ids, "npc-borrower", "player-lender", 10000,
                "working capital", 30, 1, "retail receipts");
            CreditOffer offer = workflow.Evaluate(ids, request, authority, 10000, 1);
            var lender = new CreditCashAccount("player-lender", 20000);
            var borrower = new CreditCashAccount("npc-borrower", 0);
            FinancialObligation obligation = workflow.Accept(ids, offer.OfferId, authority, lender, borrower, 1);

            Assert.IsNotNull(obligation);
            Assert.AreEqual(10000, borrower.BalanceCents);
            Assert.AreEqual(10000, lender.BalanceCents);
            Assert.IsNull(authority.AccrueInterest(obligation.ObligationId, 500));
            FinancialPaymentRecord payment = workflow.CollectPayment(obligation.ObligationId, 2500,
                10, authority, borrower, lender, out string message);

            Assert.IsNotNull(payment, message);
            Assert.AreEqual(2500, payment.AmountCents);
            Assert.AreEqual(7500, borrower.BalanceCents);
            Assert.AreEqual(12500, lender.BalanceCents);
            Assert.AreEqual(8000, obligation.TotalOutstandingCents);
        }

        [Test]
        public void FacilityDrawMovesOnlyDrawnCashAndRespectsRemainingAuthority()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            CreditFacilityRecord facility = authority.CreateFacility(ids, "npc-bank", "farm", 15000,
                1, 90, true, "seasonal advance");
            var lender = new CreditCashAccount("npc-bank", 12000);
            var borrower = new CreditCashAccount("farm", 0);

            FinancialObligation first = authority.DrawFacility(ids, facility.FacilityId, 7000, 2,
                lender, borrower, out string message);
            Assert.IsNotNull(first, message);
            Assert.AreEqual(5000, lender.BalanceCents);
            Assert.AreEqual(7000, borrower.BalanceCents);
            Assert.AreEqual(7000, facility.DrawnPrincipalCents);

            FinancialObligation staged = authority.DrawFacility(ids, facility.FacilityId, 4000, 3,
                lender, borrower, out message);
            Assert.IsNotNull(staged, message);
            Assert.AreEqual(1000, lender.BalanceCents);
            Assert.AreEqual(11000, borrower.BalanceCents);
            Assert.AreEqual(11000, facility.DrawnPrincipalCents);

            FinancialObligation second = authority.DrawFacility(ids, facility.FacilityId, 9000, 3,
                lender, borrower, out message);
            Assert.IsNull(second);
            Assert.AreEqual(1000, lender.BalanceCents);
            Assert.AreEqual(11000, borrower.BalanceCents);
        }

        [Test]
        public void PrivateOrNpcParticipantLendsFromFiniteCashAndCollectsPayment()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            var workflow = new CreditOfferWorkflow();
            var lender = new CreditParticipantService("npc-lender", CreditParticipantRole.PrivatePerson, 12000);
            var borrower = new CreditCashAccount("npc-borrower", 1000);
            CreditRequest request = workflow.SubmitRequest(ids, "npc-borrower", "npc-lender", 10000,
                "equipment", 30, 1, "shop receipts");

            CreditOffer offer = lender.ConsiderRequest(ids, workflow, authority, request, 1);
            Assert.IsNotNull(offer);
            FinancialObligation obligation = lender.AcceptOffer(ids, workflow, authority,
                offer.OfferId, borrower, 1);

            Assert.IsNotNull(obligation);
            Assert.AreEqual(2000, lender.Cash.BalanceCents);
            Assert.AreEqual(11000, borrower.BalanceCents);

            FinancialPaymentRecord payment = lender.CollectPayment(workflow, authority,
                obligation.ObligationId, borrower, 2000, 8, out string message);
            Assert.IsNotNull(payment, message);
            Assert.AreEqual(4000, lender.Cash.BalanceCents);
            Assert.AreEqual(9000, borrower.BalanceCents);
            Assert.AreEqual(8000, obligation.TotalOutstandingCents);
        }

        [Test]
        public void ParticipantDelinquencyResponseUsesStateAndSecurityWithoutAutomaticSeizure()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation obligation = authority.Create(ids, FinancialObligationKind.Loan,
                "borrower", "creditor", 5000, 1, "note", "working capital");
            var participant = new CreditParticipantService("creditor", CreditParticipantRole.Business, 10000);

            obligation.Status = FinancialObligationStatus.Delinquent;
            Assert.AreEqual(CreditDelinquencyResponse.OfferWorkout,
                participant.ChooseDelinquencyResponse(obligation, 0, false, false));
            obligation.Status = FinancialObligationStatus.Defaulted;
            Assert.AreEqual(CreditDelinquencyResponse.EnforceSecurity,
                participant.ChooseDelinquencyResponse(obligation, 0, true, false));
            Assert.AreEqual(FinancialObligationStatus.Defaulted, obligation.Status);
        }
    }
}
