// Muse Pass — Lane 1/2 seam wiring (coordinator, Nov 20, 2026).
// Lane 2's Investigator hands classified findings to Lane 1's incident /
// warning core through IIncidentCore; this file is the only translation
// between the two models. Diagnostics OBSERVE authoritative state: the
// adapter records findings in the core and never assigns work, changes
// routes, or moves goods, money, food, transport or labour.
using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    /// <summary>
    /// Translates a Lane 2 <see cref="IncidentFinding"/> into a Lane 1
    /// <see cref="IncidentReport"/> and emits it through an
    /// <see cref="IncidentWarningAuthority"/>. The core owns dedup,
    /// retention, surfacing and export; a repeated IncidentId is
    /// idempotent (occurrence count increments, no second incident).
    /// Emitting never throws: a core failure is a lost emission, never a
    /// reason to break the simulation or reclassify the finding.
    /// </summary>
    public sealed class IncidentCoreAdapter : IIncidentCore
    {
        private readonly IncidentWarningAuthority authority;

        public IncidentCoreAdapter(IncidentWarningAuthority authority)
        {
            this.authority = authority ?? throw new ArgumentNullException("authority");
        }

        /// <summary>The authority this adapter forwards to (tests).</summary>
        public IncidentWarningAuthority Authority
        {
            get { return authority; }
        }

        public void EmitClassifiedIncident(IncidentFinding finding)
        {
            if (finding == null || authority == null) return;
            try
            {
                authority.Emit(ToReport(finding));
            }
            catch
            {
                // Diagnostics must never break the simulation. A failed
                // emission is lost, not retried here; the finding itself is
                // still returned to the Investigator's caller.
            }
        }

        /// <summary>Maps one classified finding onto the core's report shape.</summary>
        public static IncidentReport ToReport(IncidentFinding finding)
        {
            if (finding == null) throw new ArgumentNullException("finding");
            IncidentReport report = new IncidentReport();
            report.SignatureId = finding.IncidentId ?? string.Empty;
            report.Severity = finding.Severity;
            report.Summary = !string.IsNullOrEmpty(finding.Title)
                ? finding.Title
                : (finding.Explanation ?? string.Empty);
            // The finding carries sim seconds but no day index; the core
            // falls back to session-once dedup when DayIndex is -1.
            report.DayIndex = -1;
            report.SimSeconds = finding.EarliestBlockerSeconds;
            report.OwningSystem = finding.ResponsibleAuthority ?? string.Empty;
            // The finding names one responsible authority; that authority
            // is the best-known recovery authority (no separate field).
            report.RecoveryAuthority = finding.ResponsibleAuthority ?? string.Empty;
            report.CauseClass = MapBlockerKind(finding.BlockerKind);
            report.CauseEvidence = finding.EarliestBlocker ?? string.Empty;
            report.TaskId = finding.TaskId ?? string.Empty;
            report.PersonId = finding.PersonNumericId >= 0
                ? "P" + finding.PersonNumericId : string.Empty;
            report.HouseholdId = finding.HouseholdId >= 0
                ? "H" + finding.HouseholdId : string.Empty;
            if (!string.IsNullOrEmpty(finding.AuthoritySourcePath))
            {
                report.EvidenceReference = finding.AuthoritySourceLine >= 0
                    ? finding.AuthoritySourcePath + ":" + finding.AuthoritySourceLine
                    : finding.AuthoritySourcePath;
            }
            if (finding.BlockerKind != DestinationBlockerKind.None)
            {
                report.PhysicalCapabilityBlocker = finding.EarliestBlocker ?? string.Empty;
            }
            return report;
        }

        /// <summary>
        /// Maps the Investigator's causal class onto the core's M-R14
        /// taxonomy. The two enums share the M-R14 classes 1-6 by design
        /// (None/Unclassified = 0); the switch keeps the mapping explicit
        /// rather than relying on the numeric alignment.
        /// </summary>
        public static RefusalCauseClass MapBlockerKind(DestinationBlockerKind kind)
        {
            switch (kind)
            {
                case DestinationBlockerKind.DestinationMissing: return RefusalCauseClass.DestinationMissing;
                case DestinationBlockerKind.DestinationInaccessible: return RefusalCauseClass.DestinationInaccessible;
                case DestinationBlockerKind.DestinationOccupiedOrReserved: return RefusalCauseClass.DestinationOccupiedOrReserved;
                case DestinationBlockerKind.WorkerCannotPerform: return RefusalCauseClass.WorkerCannotPerform;
                case DestinationBlockerKind.OtherObligationTakesPriority: return RefusalCauseClass.OtherObligationTakesPriority;
                case DestinationBlockerKind.OriginInvalid: return RefusalCauseClass.OriginInvalid;
                default: return RefusalCauseClass.Unclassified;
            }
        }
    }

    /// <summary>
    /// The shared diagnostic core instance for the Editor tools. One
    /// authority, created lazily; the Investigator window wires it to the
    /// Investigator the first time a tool investigates anything. Lane 6
    /// (Health Control Center) owns the runtime surfacing of what lands
    /// here; until then the authority is the retention/dedup/export store
    /// behind the Investigator window.
    /// </summary>
    public static class IncidentDiagnostics
    {
        private static IncidentWarningAuthority sharedAuthority;

        /// <summary>The single shared authority (created on first use).</summary>
        public static IncidentWarningAuthority SharedAuthority
        {
            get
            {
                if (sharedAuthority == null)
                {
                    sharedAuthority = new IncidentWarningAuthority();
                }
                return sharedAuthority;
            }
        }

        /// <summary>
        /// Attaches the shared core to the Investigator, once. Never
        /// overrides a core that is already attached (a test double, or a
        /// Lane 6 wiring, wins over the default).
        /// </summary>
        public static void WireInvestigator()
        {
            if (IncidentInvestigator.IncidentCore == null)
            {
                IncidentInvestigator.IncidentCore = new IncidentCoreAdapter(SharedAuthority);
            }
        }

        /// <summary>
        /// Lane 6 (Tool 12) — the day-drive hook. Runs the 7-day
        /// escalation sweep on the shared authority for the day that just
        /// changed (reuses the daily drive's day index; call from
        /// SimulationDrivers.OnDayChanged at the end of the day drive,
        /// after the daily drivers have run). Returns the signature ids
        /// escalated. Never throws: a diagnostic failure must never break
        /// the simulation. The daily summary line stays on demand from the
        /// Control Center window; the hook emits only genuine escalation
        /// lines, so a quiet day adds no Console output.
        /// </summary>
        public static IReadOnlyList<string> DriveDailyHealth(int dayIndex, double simSeconds)
        {
            try
            {
                return SharedAuthority.EvaluateEscalationSweep(dayIndex, simSeconds);
            }
            catch
            {
                return new string[0];
            }
        }
    }
}
