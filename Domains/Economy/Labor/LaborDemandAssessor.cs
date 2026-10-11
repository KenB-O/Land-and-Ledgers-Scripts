// Worker A — Phase A (real labor demand), Autonomous Labor mission.
// Phase A spec: labor demand emerges from actual work. One-person business can
// operate when one person can do the work; excess work shows up as queues,
// delays, missed sales, and backlogs, which feed legitimate hiring pressure.
//
// Dependency-free: System + System.Collections.Generic (+ System.Text) only.
// No domain types, no Unity types, no headcount authorities touched.
using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Assesses real workload-derived labor demand for one business instance.
    ///
    /// CAN-CE1-03.4 — workload, not slots: demand is computed from the caller's
    /// observed open task minutes per family, the service queue (waiting
    /// customers, recent balks), and pending production phases that require a
    /// person — never from prescribed job slots per business type.
    ///
    /// CAN-CE1-03.3 — exclusive person time: the coverage gap is measured in
    /// person-minutes against the summed available person-time budget of real
    /// people; the assessor never invents workers or budgets.
    ///
    /// CAN-CE1-03.2 — occupation is descriptive: task families are reported as
    /// observed; no family is gated or re-weighted by occupation.
    ///
    /// CAN-CE1-03.5 / CAN-CE1-13.1 — evidence-limited, delegation-bounded: the
    /// assessor is read-only over a caller-supplied snapshot and acts as no
    /// manager; it reports evidence (minutes, pressure signal) but never hires,
    /// assigns, or mandates.
    ///
    /// FORBIDDEN in this assessor (brief §4, audit): headcount targets, demand
    /// multipliers, labor quotas, "N workers required" outputs. HiringPressure
    /// is derived evidence (proposal 34), never a staffing order.
    /// </summary>
    public sealed class LaborDemandAssessor
    {
        /// <summary>
        /// Waiting-customer line length that counts as service pressure in a
        /// single-day snapshot. Mirrors CustomerQueue.MaxToleratedQueueLength:
        /// at this length arrivals begin to balk (Tech X §5.4 / Canon XV
        /// calibration), so the queue itself is excess-work evidence.
        /// A tunable, not a Canon rule.
        /// </summary>
        public const int WaitingPressureThreshold = 5;

        /// <summary>
        /// Balk count that counts as a recurring missed-sales pattern in a
        /// single-day snapshot. One balk can be a one-off rush; two or more
        /// mean customers are repeatedly leaving unserved (brief §4: missed
        /// sales -> legitimate hiring pressure). Cross-day sustained-pattern
        /// tracking is an integrator concern, not this assessor's.
        /// A tunable, not a Canon rule.
        /// </summary>
        public const int RecurringBalkThreshold = 2;

        /// <summary>
        /// Task family name used for the caller-reported pending production
        /// person-phase minutes (ProductionPhaseDefinition.RequiresPerson).
        /// </summary>
        public const string ProductionFamilyName = "Production";

        /// <summary>
        /// Pure, dependency-free assessment of workload-derived labor demand.
        ///
        /// Builds one <see cref="LaborDemandLine"/> per caller-reported task
        /// family (sorted by family name for determinism), adds the pending
        /// production person-phase minutes as a "Production" line, sums
        /// <see cref="LaborDemandReport.TotalUnmetMinutes"/>, and sets:
        /// <list type="bullet">
        /// <item><see cref="LaborDemandReport.CoverageGap"/> — true when
        /// unmet minutes exceed the available employee person-time budget
        /// (work exists that no available person can cover in time).</item>
        /// <item><see cref="LaborDemandReport.HiringPressure"/> — derived
        /// evidence only (proposal 34): true when the coverage gap holds, when
        /// missed sales recur (RecentBalkCount at or above
        /// <see cref="RecurringBalkThreshold"/>), or when the waiting line is
        /// at the balk tolerance (<see cref="WaitingPressureThreshold"/>).
        /// Never a headcount target, multiplier, or quota.</item>
        /// </list>
        ///
        /// Read-only: the inputs are never mutated. Negative minutes/counts are
        /// clamped to zero and noted in <paramref name="diag"/>.
        /// </summary>
        /// <param name="inputs">Caller-supplied read-only workload snapshot
        /// (see <see cref="LaborDemandInputs"/>).</param>
        /// <param name="diag">Optional diagnostics sink; may be null.</param>
        /// <returns>A fresh <see cref="LaborDemandReport"/>.</returns>
        public LaborDemandReport Assess(LaborDemandInputs inputs, List<string> diag)
        {
            var report = new LaborDemandReport
            {
                BusinessInstanceId = inputs != null ? inputs.BusinessInstanceId ?? string.Empty : string.Empty,
                DayIndex = inputs != null ? inputs.DayIndex : 0,
            };

            if (inputs == null)
            {
                AddDiag(diag, "LaborDemandAssessor: null inputs; reporting zero demand.");
                report.EvidenceSummary = "no inputs supplied; zero demand reported.";
                return report;
            }

            int availableMinutes = Math.Max(0, inputs.AvailableEmployeeMinutes);
            int waitingCustomers = Math.Max(0, inputs.WaitingCustomers);
            int recentBalks = Math.Max(0, inputs.RecentBalkCount);
            int productionMinutes = Math.Max(0, inputs.RequiresPersonPhaseMinutes);

            if (inputs.AvailableEmployeeMinutes < 0 || inputs.WaitingCustomers < 0
                || inputs.RecentBalkCount < 0 || inputs.RequiresPersonPhaseMinutes < 0)
            {
                AddDiag(diag, "LaborDemandAssessor: negative input clamped to zero.");
            }

            // Per-family open minutes, sorted by family name for determinism.
            var families = new List<string>();
            if (inputs.OpenTaskMinutesByFamily != null)
            {
                foreach (KeyValuePair<string, int> entry in inputs.OpenTaskMinutesByFamily)
                {
                    if (string.IsNullOrEmpty(entry.Key))
                    {
                        continue;
                    }

                    families.Add(entry.Key);
                }
            }

            families.Sort(StringComparer.Ordinal);

            int totalUnmetMinutes = 0;
            foreach (string family in families)
            {
                int minutes = Math.Max(0, inputs.OpenTaskMinutesByFamily[family]);
                totalUnmetMinutes += minutes;
                report.Lines.Add(new LaborDemandLine
                {
                    TaskFamily = family,
                    OpenMinutes = minutes,
                });
            }

            // Pending production person-phase minutes are a separate production
            // line: RequiresPerson phases (per ProductionPhaseDefinition) are
            // real demand; person-not-required phases are never reported here
            // because the caller only supplies person-phase minutes.
            if (productionMinutes > 0)
            {
                totalUnmetMinutes += productionMinutes;
                report.Lines.Add(new LaborDemandLine
                {
                    TaskFamily = ProductionFamilyName,
                    OpenMinutes = productionMinutes,
                });
            }

            report.TotalUnmetMinutes = totalUnmetMinutes;

            // CAN-CE1-03.3: coverage is measured against real available
            // person-minutes. Work beyond the budget is a gap, not a quota.
            report.CoverageGap = totalUnmetMinutes > availableMinutes;

            // Service-pressure evidence from the live queue (brief §4: excess
            // work -> queues, missed sales -> legitimate hiring pressure).
            bool queueAtBalkTolerance = waitingCustomers >= WaitingPressureThreshold;
            bool recurringBalks = recentBalks >= RecurringBalkThreshold;

            // Proposal 34: hiring pressure is DERIVED EVIDENCE, never a quota.
            // Fires on the coverage gap, on recurring missed sales, or on a
            // queue sitting at the balk tolerance.
            report.HiringPressure = report.CoverageGap || recurringBalks || queueAtBalkTolerance;

            report.EvidenceSummary = BuildEvidenceSummary(
                report, families.Count, productionMinutes, availableMinutes,
                waitingCustomers, recentBalks, queueAtBalkTolerance, recurringBalks,
                inputs.ActiveEmployeeCount);

            AddDiag(diag, string.Format(
                "LaborDemandAssessor: business={0} day={1} families={2} unmet={3} available={4} coverageGap={5} hiringPressure={6}",
                report.BusinessInstanceId, report.DayIndex, report.Lines.Count,
                report.TotalUnmetMinutes, availableMinutes,
                report.CoverageGap, report.HiringPressure));

            return report;
        }

        private static void AddDiag(List<string> diag, string message)
        {
            if (diag != null)
            {
                diag.Add(message);
            }
        }

        private static string BuildEvidenceSummary(
            LaborDemandReport report,
            int familyCount,
            int productionMinutes,
            int availableMinutes,
            int waitingCustomers,
            int recentBalks,
            bool queueAtBalkTolerance,
            bool recurringBalks,
            int activeEmployeeCount)
        {
            var builder = new StringBuilder();
            builder.Append(report.BusinessInstanceId);
            builder.Append(" day ");
            builder.Append(report.DayIndex);
            builder.Append(": ");
            builder.Append(report.Lines.Count);
            builder.Append(" task famil");
            builder.Append(report.Lines.Count == 1 ? "y" : "ies");
            builder.Append(", ");
            builder.Append(report.TotalUnmetMinutes);
            builder.Append(" unmet minutes vs ");
            builder.Append(availableMinutes);
            builder.Append(" available employee-minutes (");
            builder.Append(Math.Max(0, activeEmployeeCount));
            builder.Append(" active employees)");

            if (productionMinutes > 0)
            {
                builder.Append("; ");
                builder.Append(productionMinutes);
                builder.Append(" production person-phase minutes pending");
            }

            builder.Append("; coverage gap: ");
            builder.Append(report.CoverageGap ? "yes" : "no");

            if (waitingCustomers > 0 || recentBalks > 0)
            {
                builder.Append("; service pressure: ");
                builder.Append(waitingCustomers);
                builder.Append(" waiting customers, ");
                builder.Append(recentBalks);
                builder.Append(" recent balks");
                if (queueAtBalkTolerance)
                {
                    builder.Append(" (queue at balk tolerance)");
                }

                if (recurringBalks)
                {
                    builder.Append(" (recurring missed sales)");
                }
            }

            builder.Append("; hiring pressure: ");
            builder.Append(report.HiringPressure ? "TRUE" : "false");
            builder.Append(" (derived evidence per proposal 34; never a headcount target, multiplier, or quota)");

            return builder.ToString();
        }
    }
}
