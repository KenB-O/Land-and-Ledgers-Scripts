// Copyright (c) 2026 Land & Ledgers. All rights reserved.
// Pass V P0-1: the payment schedule is built on the true Gregorian calendar.
// Monthly payments fall on the calendar-month anniversary of the first due
// date (clamped to the month length); weekly payments fall every 7 days.
using LandLedgers.Economy.Financing;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class PaymentScheduleCalendarTests
    {
        private CalendarAnchor anchor;
        private SimulationTimeSettings timeSettings;

        [SetUp]
        public void SetUp()
        {
            anchor = CalendarAnchor.Default;
            timeSettings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
        }

        private LoanTermStructure MonthlyTerm(int termMonths) => new LoanTermStructure
        {
            principalCents = 60000,
            annualInterestRateBps = 600,
            termMonths = termMonths,
            amortizationMonths = termMonths,
            repaymentFrequency = RepaymentFrequency.Monthly,
        };

        [Test]
        public void MonthlyPayments_FallOnMonthAnniversaries_ClampedToMonthLength()
        {
            // First due date lands on 1892-01-31 (a real Gregorian month end).
            int startAbs = anchor.ToAbsoluteDay(1892, 1, 1);
            SimulationDate startDate = timeSettings.BuildDateFromAbsoluteDay(startAbs);

            LoanPaymentSchedule schedule = new PaymentScheduleBuilder().Build(
                "loan-1", MonthlyTerm(6), startDate, firstDueOffsetDays: 30, anchor);

            Assert.AreEqual(6, schedule.payments.Count);

            int[][] expected = new int[][]
            {
                new[] { 1892, 1, 31 },
                new[] { 1892, 2, 29 }, // leap-year February clamps 31 -> 29
                new[] { 1892, 3, 31 },
                new[] { 1892, 4, 30 },
                new[] { 1892, 5, 31 },
                new[] { 1892, 6, 30 },
            };

            for (int i = 0; i < expected.Length; i++)
            {
                LoanPaymentDue due = schedule.payments[i];
                Assert.AreEqual(expected[i][0], due.dueDate.Year, $"Payment {i + 1} year.");
                Assert.AreEqual(expected[i][1], due.dueDate.Month, $"Payment {i + 1} month.");
                Assert.AreEqual(expected[i][2], due.dueDate.DayOfMonth, $"Payment {i + 1} day.");
            }
        }

        [Test]
        public void MonthlyPayments_AnchorToFirstDueDate_NotToMonthStart()
        {
            // First due on the 15th: anniversaries stay on the 15th.
            int startAbs = anchor.ToAbsoluteDay(1892, 3, 1);
            SimulationDate startDate = timeSettings.BuildDateFromAbsoluteDay(startAbs);

            LoanPaymentSchedule schedule = new PaymentScheduleBuilder().Build(
                "loan-2", MonthlyTerm(3), startDate, firstDueOffsetDays: 14, anchor);

            Assert.AreEqual(3, schedule.payments.Count);
            foreach (LoanPaymentDue due in schedule.payments)
            {
                Assert.AreEqual(15, due.dueDate.DayOfMonth);
            }

            Assert.AreEqual(3, schedule.payments[0].dueDate.Month);
            Assert.AreEqual(4, schedule.payments[1].dueDate.Month);
            Assert.AreEqual(5, schedule.payments[2].dueDate.Month);
        }

        [Test]
        public void WeeklyPayments_FallEverySevenDays()
        {
            var term = new LoanTermStructure
            {
                principalCents = 40000,
                annualInterestRateBps = 600,
                termMonths = 3,
                amortizationMonths = 3,
                repaymentFrequency = RepaymentFrequency.Weekly,
            };
            int startAbs = anchor.ToAbsoluteDay(1892, 3, 1);
            SimulationDate startDate = timeSettings.BuildDateFromAbsoluteDay(startAbs);

            LoanPaymentSchedule schedule = new PaymentScheduleBuilder().Build(
                "loan-3", term, startDate, firstDueOffsetDays: 7, anchor);

            Assert.Greater(schedule.payments.Count, 2);
            for (int i = 1; i < schedule.payments.Count; i++)
            {
                int gap = schedule.payments[i].dueDayIndex - schedule.payments[i - 1].dueDayIndex;
                Assert.AreEqual(7, gap, $"Payment {i + 1} is not 7 days after the previous.");
            }
        }

        [Test]
        public void DueDayIndex_MatchesDueDate()
        {
            int startAbs = anchor.ToAbsoluteDay(1892, 3, 1);
            SimulationDate startDate = timeSettings.BuildDateFromAbsoluteDay(startAbs);

            LoanPaymentSchedule schedule = new PaymentScheduleBuilder().Build(
                "loan-4", MonthlyTerm(4), startDate, firstDueOffsetDays: 14, anchor);

            foreach (LoanPaymentDue due in schedule.payments)
            {
                SimulationDate rebuilt = timeSettings.BuildDateFromAbsoluteDay(due.dueDayIndex);
                Assert.AreEqual(due.dueDate.Year, rebuilt.Year);
                Assert.AreEqual(due.dueDate.Month, rebuilt.Month);
                Assert.AreEqual(due.dueDate.DayOfMonth, rebuilt.DayOfMonth);
            }
        }
    }
}
