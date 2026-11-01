using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Persistence;
using LandLedgers.Reputation;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Financing
{
    public sealed class PlayerDebtManagerTests
    {
        [Test]
        public void StrongApplicantAndReasonableAmountApproves()
        {
            BankLoanUnderwritingResult result = PlayerDebtManager.EvaluateWorkingCapitalApplication(
                10000,
                150000,
                250000,
                40000,
                0,
                0.72f,
                0,
                0,
                false,
                LenderProfile.DefaultLocalBank,
                0,
                -1,
                0f);

            Assert.AreEqual(FinancingDecision.Approved, result.decision);
            Assert.Contains(FinancingReasonCode.WithinLenderPolicy, result.reasonCodes);
        }

        [Test]
        public void TooLargeRequestDeclinesWithRequestedAmountReason()
        {
            BankLoanUnderwritingResult result = PlayerDebtManager.EvaluateWorkingCapitalApplication(
                5000000,
                10000,
                0,
                5000,
                0,
                0.72f,
                0,
                0,
                false,
                LenderProfile.DefaultLocalBank,
                0,
                -1,
                0f);

            Assert.AreEqual(FinancingDecision.Declined, result.decision);
            Assert.Contains(FinancingReasonCode.ExceedsLoanToValue, result.reasonCodes);
            Assert.AreEqual("requested amount too high", result.declineReason);
        }

        [Test]
        public void HeavyActiveDebtDeclinesWithDebtBurdenReason()
        {
            BankLoanUnderwritingResult result = PlayerDebtManager.EvaluateWorkingCapitalApplication(
                10000,
                150000,
                250000,
                40000,
                30000,
                0.72f,
                0,
                0,
                true,
                LenderProfile.DefaultLocalBank,
                0,
                -1,
                0f);

            Assert.AreEqual(FinancingDecision.Declined, result.decision);
            Assert.Contains(FinancingReasonCode.HeavyDebtBurden, result.reasonCodes);
            Assert.AreEqual("existing debt burden too high", result.declineReason);
        }

        [Test]
        public void LowLenderTrustDeclinesWithConfidenceReason()
        {
            BankLoanUnderwritingResult result = PlayerDebtManager.EvaluateWorkingCapitalApplication(
                10000,
                150000,
                250000,
                40000,
                0,
                0.12f,
                0,
                0,
                false,
                LenderProfile.DefaultLocalBank,
                0,
                -1,
                0f);

            Assert.AreEqual(FinancingDecision.Declined, result.decision);
            Assert.Contains(FinancingReasonCode.LowLenderTrust, result.reasonCodes);
            Assert.AreEqual("lender confidence too low", result.declineReason);
        }

        [Test]
        public void PaymentEstimateAndTotalOwedMatchScheduleBuilder()
        {
            BankLoanUnderwritingResult result = PlayerDebtManager.EvaluateWorkingCapitalApplication(
                25000,
                150000,
                250000,
                40000,
                0,
                0.72f,
                0,
                0,
                false,
                LenderProfile.DefaultLocalBank,
                0,
                -1,
                0f);
            PaymentScheduleBuilder builder = new();
            LoanPaymentSchedule schedule = builder.Build(
                "test_bank_loan",
                result.term,
                new SimulationDate(0, 0, 1, 1, 1, 1, 1));

            Assert.AreEqual(PaymentScheduleBuilder.EstimatePaymentCents(result.term), result.estimatedPaymentCents);
            Assert.AreEqual(schedule.totalPaymentCents, result.estimatedTotalOwedCents);
        }

        [Test]
        public void SaveDtoPreservesPendingApplicationActiveLoanAndReputation()
        {
            PlayerDebtSaveDto dto = new()
            {
                requestedAmountCents = 12500,
                application = new BankLoanApplicationState
                {
                    status = BankLoanApplicationStatus.PendingReview,
                    requestedAmountCents = 12500,
                    submittedDayIndex = 4,
                    decisionDayIndex = 5
                },
                activeLoan = new LoanContract
                {
                    loanId = "loan_test",
                    status = LoanStatus.Active,
                    remainingPrincipalCents = 12500
                },
                reputation = new PlayerReputationState(0.5f, 0.42f, 0.5f, 0.5f, 0.5f),
                consecutiveMissedPayments = 2,
                defaultCount = 1
            };

            Assert.AreEqual(BankLoanApplicationStatus.PendingReview, dto.application.status);
            Assert.AreEqual(LoanStatus.Active, dto.activeLoan.status);
            Assert.AreEqual(0.42f, dto.reputation.lenderTrust01, 0.0001f);
            Assert.AreEqual(2, dto.consecutiveMissedPayments);
            Assert.AreEqual(1, dto.defaultCount);
        }

        [Test]
        public void LoanApprovalAddsPrincipalToOwnerCash()
        {
            using DebtHarness harness = DebtHarness.Create(150000);
            harness.DebtManager.LoadFromSaveDto(new PlayerDebtSaveDto
            {
                requestedAmountCents = 10000
            });
            int ownerCashBefore = harness.Portfolio.OwnerCashCents;

            Assert.IsTrue(harness.DebtManager.SubmitApplication(out string submitMessage), submitMessage);
            harness.DebtManager.ProcessDay(1);

            Assert.NotNull(harness.DebtManager.ActiveLoan);
            Assert.AreEqual(LoanStatus.Active, harness.DebtManager.ActiveLoan.status);
            Assert.AreEqual(ownerCashBefore + 10000, harness.Portfolio.OwnerCashCents);
            StringAssert.Contains("owner cash", harness.DebtManager.BuildPanelStatusText());
        }

        [Test]
        public void AcquisitionLoanActivationCreatesAcquisitionDebtAndAddsOwnerCash()
        {
            using DebtHarness harness = DebtHarness.Create(30000, 20000);
            int ownerCashBefore = harness.Portfolio.OwnerCashCents;

            Assert.IsTrue(harness.DebtManager.TryActivateAcquisitionLoan(
                "agreement_001",
                100000,
                30000,
                140000,
                LoanCollateralKind.Land,
                "plot_001",
                out int fundedPrincipalCents,
                out string message), message);

            Assert.Greater(fundedPrincipalCents, 0);
            Assert.NotNull(harness.DebtManager.ActiveLoan);
            Assert.AreEqual(LoanPurpose.Acquisition, harness.DebtManager.ActiveLoan.purpose);
            Assert.AreEqual(LoanStatus.Active, harness.DebtManager.ActiveLoan.status);
            Assert.AreEqual(ownerCashBefore + fundedPrincipalCents, harness.Portfolio.OwnerCashCents);
        }

        [Test]
        public void DueLoanPaymentSpendsOwnerCash()
        {
            using DebtHarness harness = DebtHarness.Create(50000);
            harness.DebtManager.LoadFromSaveDto(new PlayerDebtSaveDto
            {
                requestedAmountCents = 10000,
                activeLoan = CreateActiveLoanDueToday(10000, 1000)
            });

            harness.DebtManager.ProcessDay(0);

            Assert.AreEqual(49000, harness.Portfolio.OwnerCashCents);
            Assert.AreEqual(LoanPaymentStatus.Paid, harness.DebtManager.ActiveLoan.schedule.payments[0].status);
            Assert.AreEqual(9100, harness.DebtManager.ActiveLoan.remainingPrincipalCents);
        }

        [Test]
        public void MissedPaymentsUseOwnerCashShortageAndEventuallyDefault()
        {
            using DebtHarness harness = DebtHarness.Create(0);
            harness.DebtManager.LoadFromSaveDto(new PlayerDebtSaveDto
            {
                requestedAmountCents = 10000,
                activeLoan = CreateActiveLoanDueToday(10000, 1000)
            });

            harness.DebtManager.ProcessDay(0);
            harness.DebtManager.ProcessDay(7);
            harness.DebtManager.ProcessDay(14);
            harness.DebtManager.ProcessDay(21);

            Assert.AreEqual(LoanStatus.Defaulted, harness.DebtManager.ActiveLoan.status);
            Assert.AreEqual(4, harness.DebtManager.ConsecutiveMissedPayments);
            Assert.AreEqual(1, harness.DebtManager.DefaultCount);
        }

        [Test]
        public void DefaultedDebtBlocksNewBorrowing()
        {
            using DebtHarness harness = DebtHarness.Create(150000);
            harness.DebtManager.LoadFromSaveDto(new PlayerDebtSaveDto
            {
                requestedAmountCents = 10000,
                activeLoan = CreateActiveLoanDueToday(10000, 1000, LoanPurpose.WorkingCapital, LoanStatus.Defaulted)
            });

            Assert.IsTrue(harness.DebtManager.HasActiveLoan);
            Assert.IsFalse(harness.DebtManager.CanSubmitApplication);
            Assert.IsFalse(harness.DebtManager.SubmitApplication(out string message));
            StringAssert.Contains("Defaulted bank debt", message);
        }

        [Test]
        public void PendingApprovalDoesNotOverwriteUnresolvedDefault()
        {
            using DebtHarness harness = DebtHarness.Create(150000);
            int ownerCashBefore = harness.Portfolio.OwnerCashCents;
            harness.DebtManager.LoadFromSaveDto(new PlayerDebtSaveDto
            {
                requestedAmountCents = 10000,
                application = new BankLoanApplicationState
                {
                    status = BankLoanApplicationStatus.PendingReview,
                    requestedAmountCents = 10000,
                    submittedDayIndex = 0,
                    decisionDayIndex = 1
                },
                activeLoan = CreateActiveLoanDueToday(10000, 1000, LoanPurpose.WorkingCapital, LoanStatus.Defaulted)
            });

            harness.DebtManager.ProcessDay(1);

            Assert.NotNull(harness.DebtManager.ActiveLoan);
            Assert.AreEqual(LoanStatus.Defaulted, harness.DebtManager.ActiveLoan.status);
            Assert.AreEqual(BankLoanApplicationStatus.Declined, harness.DebtManager.Application.status);
            Assert.AreEqual(ownerCashBefore, harness.Portfolio.OwnerCashCents);
            StringAssert.Contains("unresolved bank debt", harness.DebtManager.Application.declineReason);
        }

        private static LoanContract CreateActiveLoanDueToday(
            int remainingPrincipalCents,
            int totalDueCents,
            LoanPurpose purpose = LoanPurpose.WorkingCapital,
            LoanStatus status = LoanStatus.Active,
            List<string> collateralIds = null)
        {
            return new LoanContract
            {
                loanId = "test_active_loan",
                lenderId = "frontier_local_bank",
                purpose = purpose,
                collateralIds = collateralIds ?? new List<string>(),
                status = status,
                remainingPrincipalCents = remainingPrincipalCents,
                nextPaymentDueDate = CreateStartDate(),
                schedule = new LoanPaymentSchedule
                {
                    loanId = "test_active_loan",
                    totalPrincipalCents = remainingPrincipalCents,
                    totalPaymentCents = totalDueCents,
                    payments = new List<LoanPaymentDue>
                    {
                        new()
                        {
                            paymentNumber = 1,
                            dueDate = CreateStartDate(),
                            dueDayIndex = 0,
                            principalCents = Mathf.Max(0, totalDueCents - 100),
                            interestCents = 100,
                            totalDueCents = totalDueCents,
                            status = LoanPaymentStatus.Scheduled
                        }
                    }
                }
            };
        }

        private static SimulationDate CreateStartDate()
        {
            return new SimulationDate(0, 0, 1, 1, 1, 1, 1);
        }

        private sealed class DebtHarness : IDisposable
        {
            private readonly GameObject root;

            public PlayerPortfolioManager Portfolio { get; }
            public PlayerDebtManager DebtManager { get; }

            private DebtHarness(GameObject root)
            {
                this.root = root;
                Portfolio = root.AddComponent<PlayerPortfolioManager>();
                DebtManager = root.AddComponent<PlayerDebtManager>();
            }

            public static DebtHarness Create(int ownerCashCents)
            {
                return Create(ownerCashCents, 0);
            }

            public static DebtHarness Create(int ownerCashCents, int lastWeeklyDistributionCents)
            {
                GameObject root = new("Player Debt Manager Test Harness");
                DebtHarness harness = new(root);
                harness.Portfolio.Configure(null, null);
                harness.Portfolio.LoadFromSaveDto(new PlayerPortfolioSaveDto
                {
                    initialized = true,
                    ownerCashCents = ownerCashCents,
                    lastWeeklyDistributionCents = lastWeeklyDistributionCents
                });
                harness.DebtManager.Configure(null, null, null, null, harness.Portfolio);
                return harness;
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }
    }
}
