using System;
using System.Reflection;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    public sealed class PaymentScheduleBuilder
    {
        private const int DefaultDaysPerWeek = 7;
        private const int DefaultWeeksPerMonth = 4;
        private const int DefaultMonthsPerYear = 12;

        public LoanPaymentSchedule Build(
            string loanId,
            LoanTermStructure term,
            SimulationDate startDate,
            int firstDueOffsetDays = 7,
            int daysPerMonth = 28)
        {
            LoanTermStructure sanitized = term.Sanitized();
            int paymentCount = GetPaymentCount(sanitized);
            int intervalDays = sanitized.repaymentFrequency == RepaymentFrequency.Weekly
                ? DefaultDaysPerWeek
                : Mathf.Max(DefaultDaysPerWeek, daysPerMonth);
            int firstDue = Mathf.Max(1, firstDueOffsetDays);

            LoanPaymentSchedule schedule = new LoanPaymentSchedule
            {
                loanId = loanId ?? string.Empty,
                principalCents = sanitized.principalCents,
                originationFeeCents = sanitized.originationFeeCents
            };

            if (sanitized.principalCents <= 0 || paymentCount <= 0)
            {
                return schedule;
            }

            int amortizedPrincipalCents = Mathf.Max(0, sanitized.principalCents - sanitized.balloonPaymentCents);
            int standardPaymentCents = CalculateAmortizedPaymentCents(
                amortizedPrincipalCents,
                sanitized.annualInterestRateBps,
                paymentCount,
                sanitized.repaymentFrequency);
            int remainingPrincipalCents = sanitized.principalCents;
            int totalPrincipalCents = 0;
            int totalInterestCents = 0;

            for (int i = 1; i <= paymentCount; i++)
            {
                int interestCents = CalculatePeriodicInterestCents(
                    remainingPrincipalCents,
                    sanitized.annualInterestRateBps,
                    sanitized.repaymentFrequency);
                int principalCents = Mathf.Clamp(standardPaymentCents - interestCents, 0, remainingPrincipalCents);

                if (i == paymentCount)
                {
                    principalCents = remainingPrincipalCents;
                }

                remainingPrincipalCents = Mathf.Max(0, remainingPrincipalCents - principalCents);
                int dueDayIndex = GetAbsoluteDayIndex(startDate) + firstDue + (i - 1) * intervalDays;
                LoanPaymentDue payment = new LoanPaymentDue
                {
                    paymentNumber = i,
                    dueDate = BuildDateFromAbsoluteDay(dueDayIndex, daysPerMonth),
                    dueDayIndex = dueDayIndex,
                    principalCents = principalCents,
                    interestCents = interestCents,
                    totalDueCents = principalCents + interestCents,
                    paidCents = 0,
                    status = LoanPaymentStatus.Scheduled
                };

                schedule.payments.Add(payment);
                totalPrincipalCents += principalCents;
                totalInterestCents += interestCents;
            }

            if (schedule.payments.Count > 0 && sanitized.originationFeeCents > 0)
            {
                // The bank fee is collected as part of the first scheduled note payment so the ledger never shows a cost the repayment path cannot collect.
                schedule.payments[0].totalDueCents = Mathf.Max(0, schedule.payments[0].totalDueCents) + sanitized.originationFeeCents;
            }

            schedule.totalPrincipalCents = totalPrincipalCents;
            schedule.totalInterestCents = totalInterestCents;
            schedule.totalPaymentCents = CalculateScheduledPaymentTotalCents(schedule);
            return schedule;
        }

        public static int EstimatePaymentCents(LoanTermStructure term)
        {
            LoanTermStructure sanitized = term.Sanitized();
            int amortizedPrincipalCents = Mathf.Max(0, sanitized.principalCents - sanitized.balloonPaymentCents);
            return CalculateAmortizedPaymentCents(
                amortizedPrincipalCents,
                sanitized.annualInterestRateBps,
                GetPaymentCount(sanitized),
                sanitized.repaymentFrequency);
        }

        public static int ToWeeklyEquivalentCents(int periodicPaymentCents, RepaymentFrequency frequency)
        {
            int payment = Mathf.Max(0, periodicPaymentCents);
            return frequency == RepaymentFrequency.Monthly
                ? Mathf.CeilToInt(payment * 12f / 52f)
                : payment;
        }

        public static int ToWeeklyEquivalentCents(LoanTermStructure term, int periodicPaymentCents)
        {
            return ToWeeklyEquivalentCents(periodicPaymentCents, term.Sanitized().repaymentFrequency);
        }

        public static int CalculateScheduledPaymentTotalCents(LoanPaymentSchedule schedule)
        {
            if (schedule == null)
            {
                return 0;
            }

            int rowTotal = 0;
            if (schedule.payments != null && schedule.payments.Count > 0)
            {
                for (int i = 0; i < schedule.payments.Count; i++)
                {
                    LoanPaymentDue payment = schedule.payments[i];
                    if (payment != null)
                    {
                        rowTotal += Mathf.Max(0, payment.totalDueCents);
                    }
                }
            }

            return rowTotal > 0 ? rowTotal : Mathf.Max(0, schedule.totalPaymentCents);
        }

        public static int CalculateTotalBorrowerCostCents(LoanTermStructure term, LoanPaymentSchedule schedule)
        {
            LoanTermStructure sanitizedTerm = term.Sanitized();
            if (schedule != null)
            {
                return CalculateScheduledPaymentTotalCents(schedule) + GetUnembeddedOriginationFeeCents(sanitizedTerm, schedule);
            }

            if (sanitizedTerm.principalCents <= 0)
            {
                return sanitizedTerm.originationFeeCents;
            }

            LoanPaymentSchedule estimatedSchedule = BuildEstimateSchedule(sanitizedTerm);
            return CalculateScheduledPaymentTotalCents(estimatedSchedule);
        }

        public static int GetEmbeddedOriginationFeeCents(LoanTermStructure term, LoanPaymentSchedule schedule)
        {
            if (schedule == null || schedule.payments == null || schedule.payments.Count == 0)
            {
                return 0;
            }

            int expectedFeeCents = Mathf.Max(term.Sanitized().originationFeeCents, schedule.originationFeeCents);
            if (expectedFeeCents <= 0)
            {
                return 0;
            }

            int detectedFeeCents = 0;
            for (int i = 0; i < schedule.payments.Count; i++)
            {
                LoanPaymentDue payment = schedule.payments[i];
                if (payment == null)
                {
                    continue;
                }

                int baseDue = Mathf.Max(0, payment.principalCents) + Mathf.Max(0, payment.interestCents);
                detectedFeeCents += Mathf.Max(0, payment.totalDueCents - baseDue);
            }

            return Mathf.Clamp(detectedFeeCents, 0, expectedFeeCents);
        }

        public static int GetUnembeddedOriginationFeeCents(LoanTermStructure term, LoanPaymentSchedule schedule)
        {
            int expectedFeeCents = Mathf.Max(term.Sanitized().originationFeeCents, schedule != null ? schedule.originationFeeCents : 0);
            return Mathf.Max(0, expectedFeeCents - GetEmbeddedOriginationFeeCents(term, schedule));
        }

        public static bool ScheduleAlreadyIncludesOriginationFee(LoanTermStructure term, LoanPaymentSchedule schedule)
        {
            return GetUnembeddedOriginationFeeCents(term, schedule) <= 0;
        }

        private static int GetPaymentCount(LoanTermStructure term)
        {
            return term.repaymentFrequency == RepaymentFrequency.Weekly
                ? Mathf.Max(1, Mathf.RoundToInt(term.termMonths * 52f / 12f))
                : Mathf.Max(1, term.termMonths);
        }

        private static int CalculateAmortizedPaymentCents(
            int principalCents,
            int annualInterestRateBps,
            int paymentCount,
            RepaymentFrequency frequency)
        {
            if (principalCents <= 0 || paymentCount <= 0)
            {
                return 0;
            }

            float periodicRate = GetPeriodicRate(annualInterestRateBps, frequency);
            if (periodicRate <= 0f)
            {
                return Mathf.CeilToInt(principalCents / (float)paymentCount);
            }

            double rate = periodicRate;
            double discount = Math.Pow(1d + rate, -paymentCount);
            double payment = principalCents * rate / (1d - discount);
            return Mathf.Max(1, Mathf.RoundToInt((float)payment));
        }

        private static int CalculatePeriodicInterestCents(
            int remainingPrincipalCents,
            int annualInterestRateBps,
            RepaymentFrequency frequency)
        {
            if (remainingPrincipalCents <= 0 || annualInterestRateBps <= 0)
            {
                return 0;
            }

            return Mathf.RoundToInt(remainingPrincipalCents * GetPeriodicRate(annualInterestRateBps, frequency));
        }

        private static float GetPeriodicRate(int annualInterestRateBps, RepaymentFrequency frequency)
        {
            float annualRate = Mathf.Max(0, annualInterestRateBps) / 10000f;
            float periodsPerYear = frequency == RepaymentFrequency.Monthly ? 12f : 52f;
            return annualRate / periodsPerYear;
        }

        private static LoanPaymentSchedule BuildEstimateSchedule(LoanTermStructure term)
        {
            return new PaymentScheduleBuilder().Build(
                "estimated_total_cost",
                term,
                new SimulationDate(0, 0, 1, 1, 1, 1, 1),
                DefaultDaysPerWeek,
                DefaultDaysPerWeek * DefaultWeeksPerMonth);
        }

        private static int GetAbsoluteDayIndex(SimulationDate date)
        {
            Type type = date.GetType();
            return GetIntMemberValue(type.GetProperty("AbsoluteDayIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? GetIntMemberValue(type.GetProperty("absoluteDayIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? GetIntMemberValue(type.GetProperty("AbsoluteDay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? GetIntMemberValue(type.GetField("absoluteDayIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? GetIntMemberValue(type.GetField("absoluteDay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? 0;
        }

        private static int? GetIntMemberValue(MemberInfo member, object target)
        {
            if (member is PropertyInfo property && property.CanRead)
            {
                object value = property.GetValue(target, null);
                return value is int intValue ? intValue : null;
            }

            if (member is FieldInfo field)
            {
                object value = field.GetValue(target);
                return value is int intValue ? intValue : null;
            }

            return null;
        }

        private static SimulationDate BuildDateFromAbsoluteDay(int absoluteDayIndex, int daysPerMonth)
        {
            int clampedDay = Mathf.Max(0, absoluteDayIndex);
            int normalizedDaysPerMonth = Mathf.Max(DefaultDaysPerWeek, daysPerMonth <= 0 ? DefaultDaysPerWeek * DefaultWeeksPerMonth : daysPerMonth);
            int dayOfWeekIndex = clampedDay % DefaultDaysPerWeek;
            int monthIndex = clampedDay / normalizedDaysPerMonth;
            int month = monthIndex % DefaultMonthsPerYear + 1;
            int year = monthIndex / DefaultMonthsPerYear + 1;
            int dayOfMonth = clampedDay % normalizedDaysPerMonth + 1;
            int week = clampedDay / DefaultDaysPerWeek + 1;
            int weekOfMonth = (dayOfMonth - 1) / DefaultDaysPerWeek + 1;
            return new SimulationDate(clampedDay, dayOfWeekIndex, dayOfMonth, week, weekOfMonth, month, year);
        }
    }
}
