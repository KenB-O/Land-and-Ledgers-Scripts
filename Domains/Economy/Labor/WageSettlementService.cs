// Worker D: Autonomous Labor, Phase F - wages and household income.
// Money rules (ABSOLUTE): business cash moves ONLY through
// BusinessRuntimeState's single-writer path (ResolveWeeklyPayroll /
// PayAgreedWage); household cash moves ONLY through HouseholdLedger
// (RecordWagePayment). No second wage pathway. No wage income from status
// alone - every payment names an EmploymentRelationship record.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// One line of a payroll settlement: who was paid what, and whether the
    /// cash actually moved. Read model only - compact enough for a later UI.
    /// </summary>
    [Serializable]
    public sealed class PayrollEntry
    {
        public string EmploymentId = string.Empty;
        public int PersonId = -1;
        public int AmountCents;
        public bool Paid;
        public string Note = string.Empty;
    }

    /// <summary>
    /// One completed (or attempted) payroll settlement for a business and week.
    /// </summary>
    [Serializable]
    public sealed class PayrollEvent
    {
        public string BusinessInstanceId = string.Empty;
        public int WeekIndex = -1;
        public int DayIndex = -1;
        public string PeriodLabel = string.Empty;
        public int TotalPaidCents;
        public List<PayrollEntry> Entries = new List<PayrollEntry>();
    }

    /// <summary>
    /// Save shape for WageSettlementService: settled-week markers (per business)
    /// plus the payroll event history. Clear-then-load on restore.
    /// </summary>
    [Serializable]
    public sealed class WageSettlementSaveDto
    {
        public List<SettledWeekMarker> SettledWeeks = new List<SettledWeekMarker>();
        public List<PayrollEvent> Events = new List<PayrollEvent>();
    }

    [Serializable]
    public sealed class SettledWeekMarker
    {
        public string BusinessInstanceId = string.Empty;
        public int WeekIndex = -1;
    }

    /// <summary>
    /// Idempotency-guarded wage settlement. Weekly payroll delegates to the
    /// EXISTING BusinessRuntimeState.ResolveWeeklyPayroll path; partial periods
    /// (mid-week start, departure final settlement) pay through the SAME cash
    /// path via BusinessRuntimeState.PayAgreedWage. This service never touches
    /// cash itself - it guards, records, and reports.
    /// </summary>
    public sealed class WageSettlementService
    {
        private readonly Dictionary<string, int> settledWeekByBusiness =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<PayrollEvent> events = new List<PayrollEvent>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<PayrollEvent> Events => events;

        /// <summary>
        /// Returns the settled-week marker for a business, or -1 when unset.
        /// </summary>
        public int SettledWeekIndex(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            {
                return -1;
            }

            return settledWeekByBusiness.TryGetValue(businessInstanceId, out int week) ? week : -1;
        }

        /// <summary>
        /// Settles one week's payroll for a business. Skips (returns
        /// "already-settled") when the business's settled-week marker already
        /// equals weekIndex - a week is never posted twice, including across
        /// save/load. Otherwise delegates to the existing
        /// business.ResolveWeeklyPayroll() path and records a PayrollEvent
        /// read model. Returns "settled" or "settled-arrears".
        /// </summary>
        public string SettleWeeklyPayroll(
            BusinessRuntimeState business,
            EmploymentRelationshipRegistry registry,
            int weekIndex,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (business == null)
            {
                diag.Add("WageSettlementService.SettleWeeklyPayroll: a business runtime is required.");
                return "refused";
            }

            string businessId = business.BusinessId;
            if (business.LastPayrollSettledWeekIndex == weekIndex
                || SettledWeekIndex(businessId) == weekIndex)
            {
                diag.Add($"WageSettlementService: week {weekIndex} for '{businessId}' is already settled - skipped (idempotency).");
                return "already-settled";
            }

            // Snapshot the payroll population BEFORE the single-writer path runs,
            // so the PayrollEvent can attribute per-employment outcomes. The
            // employment set is stabilized first (slot-projection is idempotent
            // and first-wins, so running it here is safe).
            business.EnsureEmploymentRecordsFromSlots();
            List<EmploymentRelationship> population = registry != null
                ? registry.GetActiveByEmployer(businessId)
                : new List<EmploymentRelationship>();
            var agreedWages = new List<int>(population.Count);
            for (int i = 0; i < population.Count; i++)
            {
                agreedWages.Add(Math.Max(0, population[i].Compensation.AgreedWeeklyWageCents));
            }

            int cashBefore = business.CurrentCashCents;
            int liabilityBefore = business.AccruedLiabilityCents;

            business.ResolveWeeklyPayroll();

            int cashAfter = business.CurrentCashCents;
            int liabilityAfter = business.AccruedLiabilityCents;

            // Attribute outcomes by replaying the same order the single-writer
            // path pays in (population order, pay-if-cash-covers, else accrue).
            // The replay is a READ MODEL: actual cash moved only inside
            // ResolveWeeklyPayroll. A mismatch between replay and the observed
            // cash delta is logged loudly - never silently corrected.
            var payrollEvent = new PayrollEvent
            {
                BusinessInstanceId = businessId,
                WeekIndex = weekIndex,
                DayIndex = dayIndex,
                PeriodLabel = "weekly payroll"
            };
            int replayCash = cashBefore;
            int replayPaid = 0;
            for (int i = 0; i < population.Count; i++)
            {
                EmploymentRelationship employment = population[i];
                int wage = agreedWages[i];
                bool paid = wage > 0 && replayCash >= wage;
                if (paid)
                {
                    replayCash -= wage;
                    replayPaid += wage;
                }

                payrollEvent.Entries.Add(new PayrollEntry
                {
                    EmploymentId = employment.Id,
                    PersonId = employment.EmployeePersonId,
                    AmountCents = wage,
                    Paid = paid,
                    Note = paid ? "paid via weekly payroll" : "unpaid - operating liability accrued, employment suspended"
                });
            }

            int observedPaid = Math.Max(0, cashBefore - cashAfter);
            if (observedPaid != replayPaid)
            {
                diag.Add($"WageSettlementService: payroll replay mismatch for '{businessId}' week {weekIndex} " +
                         $"(replay {replayPaid}c vs observed cash delta {observedPaid}c) - event records observed cash movement only.");
                payrollEvent.TotalPaidCents = observedPaid;
            }
            else
            {
                payrollEvent.TotalPaidCents = replayPaid;
            }

            bool arrears = liabilityAfter > liabilityBefore;
            settledWeekByBusiness[businessId] = weekIndex;
            business.LastPayrollSettledWeekIndex = weekIndex;
            events.Add(payrollEvent);

            diag.Add($"WageSettlementService: week {weekIndex} for '{businessId}' settled - " +
                     $"paid {payrollEvent.TotalPaidCents}c across {payrollEvent.Entries.Count} employments" +
                     (arrears ? " (ARREARS accrued)" : string.Empty) + ".");
            return arrears ? "settled-arrears" : "settled";
        }

        /// <summary>
        /// Pays a partial-period wage (mid-week start, departure final
        /// settlement) through the SAME existing cash path as weekly payroll:
        /// BusinessRuntimeState.PayAgreedWage. The employment must belong to
        /// this business. Family-labor/operator/non-wage kinds are refused via
        /// the read-only WorkRelationshipGuard taxonomy check. Returns "paid",
        /// "paid-arrears" (cash shortfall - liability accrued), or a refusal.
        /// </summary>
        public string SettlePartialPeriod(
            BusinessRuntimeState business,
            EmploymentRelationship employment,
            int amountCents,
            string periodLabel,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (business == null)
            {
                diag.Add("WageSettlementService.SettlePartialPeriod: a business runtime is required.");
                return "refused";
            }

            if (employment == null)
            {
                diag.Add("WageSettlementService.SettlePartialPeriod: an employment record is required - " +
                         "no wage income from status alone, and no payroll for family labor/operator work.");
                return "refused-no-employment";
            }

            if (!string.Equals(employment.EmployerBusinessId, business.BusinessId, StringComparison.Ordinal))
            {
                diag.Add($"WageSettlementService.SettlePartialPeriod: employment '{employment.Id}' belongs to " +
                         $"'{employment.EmployerBusinessId}', not '{business.BusinessId}' - refused.");
                return "refused-wrong-business";
            }

            // PL-04: read-only taxonomy refusal. Family labor, operators and
            // contract workers never carry payroll wage terms.
            string refusal = ValidatePayableKind(employment.ToWorkRelationshipKind(), amountCents);
            if (refusal != null)
            {
                diag.Add($"WageSettlementService.SettlePartialPeriod: {refusal}");
                return "refused-non-wage-kind";
            }

            string outcome = business.PayAgreedWage(employment.Id, amountCents, periodLabel, dayIndex, diag);
            if (outcome != null)
            {
                // PayAgreedWage returns a diagnostic on refusal or on shortfall
                // (shortfall suspends + accrues liability - that is an outcome,
                // not a silent skip).
                bool isShortfall = outcome.StartsWith("PayAgreedWage: insufficient cash", StringComparison.Ordinal);
                diag.Add($"WageSettlementService.SettlePartialPeriod: {outcome}");
                events.Add(new PayrollEvent
                {
                    BusinessInstanceId = business.BusinessId,
                    WeekIndex = -1,
                    DayIndex = dayIndex,
                    PeriodLabel = periodLabel,
                    TotalPaidCents = 0, // shortfall accrues liability - nothing moved
                    Entries = new List<PayrollEntry>
                    {
                        new PayrollEntry
                        {
                            EmploymentId = employment.Id,
                            PersonId = employment.EmployeePersonId,
                            AmountCents = Math.Max(0, amountCents),
                            Paid = false,
                            Note = outcome
                        }
                    }
                });
                return isShortfall ? "paid-arrears" : "refused";
            }

            diag.Add($"WageSettlementService.SettlePartialPeriod: paid {Math.Max(0, amountCents)}c for " +
                     $"'{periodLabel}' via employment '{employment.Id}'.");
            events.Add(new PayrollEvent
            {
                BusinessInstanceId = business.BusinessId,
                WeekIndex = -1,
                DayIndex = dayIndex,
                PeriodLabel = periodLabel,
                TotalPaidCents = Math.Max(0, amountCents),
                Entries = new List<PayrollEntry>
                {
                    new PayrollEntry
                    {
                        EmploymentId = employment.Id,
                        PersonId = employment.EmployeePersonId,
                        AmountCents = Math.Max(0, amountCents),
                        Paid = true,
                        Note = $"paid '{periodLabel}' via PayAgreedWage"
                    }
                }
            });
            return "paid";
        }

        /// <summary>
        /// Read-only PL-04 refusal: returns a diagnostic naming the violation
        /// when a non-wage work kind carries wage terms; null when payable.
        /// Family labor, operators and contract work never go through payroll.
        /// </summary>
        public static string ValidatePayableKind(WorkRelationshipKind kind, int amountCents)
        {
            return WorkRelationshipGuard.ValidateNoWageForNonWageKind(kind, amountCents);
        }

        /// <summary>
        /// Deterministic proration for partial periods: ceil(weeklyWage *
        /// daysWorked / daysInPeriod). No fractional cents ever escape.
        /// </summary>
        public static int ProrateWeeklyWage(int weeklyWageCents, int daysWorked, int daysInPeriod = 7)
        {
            int wage = Math.Max(0, weeklyWageCents);
            int worked = Math.Max(0, Math.Min(Math.Max(1, daysInPeriod), daysWorked));
            int period = Math.Max(1, daysInPeriod);
            return (wage * worked + period - 1) / period;
        }

        public WageSettlementSaveDto CaptureSaveDto()
        {
            var dto = new WageSettlementSaveDto();
            foreach (KeyValuePair<string, int> marker in settledWeekByBusiness)
            {
                dto.SettledWeeks.Add(new SettledWeekMarker
                {
                    BusinessInstanceId = marker.Key,
                    WeekIndex = marker.Value
                });
            }

            dto.Events.AddRange(events);
            return dto;
        }

        /// <summary>
        /// Clear-then-load: settled-week markers and payroll events are
        /// replaced, never merged. Callers re-stamp business runtimes'
        /// LastPayrollSettledWeekIndex from SettledWeeks during hub restore.
        /// </summary>
        public void LoadFromSaveDto(WageSettlementSaveDto dto)
        {
            settledWeekByBusiness.Clear();
            events.Clear();
            if (dto == null)
            {
                return;
            }

            if (dto.SettledWeeks != null)
            {
                foreach (SettledWeekMarker marker in dto.SettledWeeks)
                {
                    if (marker != null && !string.IsNullOrWhiteSpace(marker.BusinessInstanceId))
                    {
                        settledWeekByBusiness[marker.BusinessInstanceId] = marker.WeekIndex;
                    }
                }
            }

            if (dto.Events != null)
            {
                events.AddRange(dto.Events);
            }
        }
    }
}
