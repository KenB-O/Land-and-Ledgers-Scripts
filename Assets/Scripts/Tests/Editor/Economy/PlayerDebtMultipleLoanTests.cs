using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Persistence;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class PlayerDebtMultipleLoanTests
    {
        [Test]
        public void ActiveDebtAggregatesAcrossUnresolvedLoans()
        {
            GameObject target = new("Player Debt Multi Loan Aggregate Test");
            try
            {
                PlayerDebtManager manager = target.AddComponent<PlayerDebtManager>();
                manager.LoadFromSaveDto(new PlayerDebtSaveDto
                {
                    activeLoans = new List<LoanContract>
                    {
                        CreateLoan("loan_a", 120000, 11000),
                        CreateLoan("loan_b", 80000, 7000),
                        CreateLoan("loan_paid", 50000, 4000, LoanStatus.PaidOff)
                    }
                });

                Assert.IsTrue(manager.HasActiveLoan);
                Assert.AreEqual(200000, manager.ActiveDebtPrincipalCents);
                Assert.AreEqual(18000, manager.ActiveDebtPaymentCents);
                StringAssert.Contains("Active loan:", manager.BuildActiveLoanText());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ExistingActiveLoanDoesNotBlockNewApplicationSubmission()
        {
            GameObject target = new("Player Debt Multi Loan Submit Test");
            try
            {
                PlayerDebtManager manager = target.AddComponent<PlayerDebtManager>();
                manager.LoadFromSaveDto(new PlayerDebtSaveDto
                {
                    requestedAmountCents = 10000,
                    activeLoans = new List<LoanContract>
                    {
                        CreateLoan("loan_a", 120000, 11000)
                    }
                });

                Assert.IsTrue(manager.CanEditRequestedAmount);
                Assert.IsTrue(manager.CanSubmitApplication);
                Assert.IsTrue(manager.SubmitApplication(out string message), message);
                StringAssert.Contains("Loan Application Submitted", message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void RecoveredAndPaidOffLoansDoNotCountTowardActiveDebtSummary()
        {
            GameObject target = new("Player Debt Multi Loan Status Filter Test");
            try
            {
                PlayerDebtManager manager = target.AddComponent<PlayerDebtManager>();
                manager.LoadFromSaveDto(new PlayerDebtSaveDto
                {
                    activeLoans = new List<LoanContract>
                    {
                        CreateLoan("loan_active", 120000, 11000),
                        CreateLoan("loan_recovered", 70000, 6000, LoanStatus.Recovered),
                        CreateLoan("loan_paid", 50000, 4000, LoanStatus.PaidOff)
                    }
                });

                Assert.IsTrue(manager.HasActiveLoan);
                Assert.AreEqual(120000, manager.ActiveDebtPrincipalCents);
                Assert.AreEqual(11000, manager.ActiveDebtPaymentCents);
                StringAssert.Contains("Active loan:", manager.BuildActiveLoanText());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void SubmittingNewApplicationDoesNotChangeExistingActiveDebtTotals()
        {
            GameObject target = new("Player Debt Multi Loan Totals Stability Test");
            try
            {
                PlayerDebtManager manager = target.AddComponent<PlayerDebtManager>();
                manager.LoadFromSaveDto(new PlayerDebtSaveDto
                {
                    requestedAmountCents = 10000,
                    activeLoans = new List<LoanContract>
                    {
                        CreateLoan("loan_a", 120000, 11000)
                    }
                });

                int principalBefore = manager.ActiveDebtPrincipalCents;
                int paymentBefore = manager.ActiveDebtPaymentCents;

                Assert.IsTrue(manager.SubmitApplication(out string message), message);
                StringAssert.Contains("Loan Application Submitted", message);
                Assert.AreEqual(principalBefore, manager.ActiveDebtPrincipalCents);
                Assert.AreEqual(paymentBefore, manager.ActiveDebtPaymentCents);
                StringAssert.Contains("Active loan:", manager.BuildActiveLoanText());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static LoanContract CreateLoan(string id, int principalCents, int nextPaymentCents, LoanStatus status = LoanStatus.Active)
        {
            return new LoanContract
            {
                loanId = id,
                lenderId = "frontier_local_bank",
                purpose = LoanPurpose.WorkingCapital,
                status = status,
                remainingPrincipalCents = principalCents,
                nextPaymentDueDate = new SimulationDate(7, 0, 8, 2, 2, 4, 1892),
                termStructure = new LoanTermStructure
                {
                    principalCents = principalCents,
                    annualInterestRateBps = 900,
                    termMonths = 12,
                    amortizationMonths = 12,
                    repaymentFrequency = RepaymentFrequency.Weekly
                },
                schedule = new LoanPaymentSchedule
                {
                    loanId = id,
                    payments = new List<LoanPaymentDue>
                    {
                        new()
                        {
                            paymentNumber = 1,
                            dueDate = new SimulationDate(7, 0, 8, 2, 2, 4, 1892),
                            dueDayIndex = 7,
                            principalCents = Mathf.Min(principalCents, nextPaymentCents),
                            totalDueCents = nextPaymentCents,
                            status = LoanPaymentStatus.Scheduled
                        }
                    }
                }
            };
        }
    }
}
