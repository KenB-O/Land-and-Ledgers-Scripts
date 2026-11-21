using System;

namespace LandLedgers.Diagnostics
{
    /// <summary>
    /// Lane 1 (§4/§17) — the single severity scale for the shared incident
    /// infrastructure. Every warning the pass emits, and every incident the
    /// Investigator classifies, uses one of these four levels; no emitter
    /// invents its own.
    ///
    /// Severity depends on circumstances, never on elapsed time or frequency
    /// alone: a worker waiting 15 minutes for an occupied workstation is
    /// INFORMATION; a worker idle a whole day without valid cause is WARNING;
    /// an integrity violation (custody moved without payment, goods lost) is
    /// CRITICAL; insufficient evidence is UNKNOWN, never upgraded on
    /// frequency alone.
    /// </summary>
    public enum IncidentSeverity
    {
        /// <summary>
        /// Insufficient evidence to classify. The incident carries the
        /// preserved context and names what evidence is missing; the
        /// Investigator must show that, not guess a level.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The game is working as designed and the event is worth knowing
        /// about: a legitimate hardship, a routine success, a working
        /// fallback, a known-by-design gap stated once.
        /// </summary>
        Information = 1,

        /// <summary>
        /// Something is broken, missing, or stuck, with a named cause and a
        /// named owning system. The default for a real defect signal.
        /// </summary>
        Warning = 2,

        /// <summary>
        /// An integrity violation or a failure with no recovery path:
        /// custody without payment, a paid lot orphaned, a freight request
        /// stranded terminal with no reopen path, a save that loses state.
        /// </summary>
        Critical = 3,
    }

    /// <summary>Stable text labels for <see cref="IncidentSeverity"/> (log lines, export).</summary>
    public static class IncidentSeverityText
    {
        /// <summary>The canonical label for a severity (UNKNOWN / INFORMATION / WARNING / CRITICAL).</summary>
        public static string Label(IncidentSeverity severity)
        {
            switch (severity)
            {
                case IncidentSeverity.Information: return "INFORMATION";
                case IncidentSeverity.Warning: return "WARNING";
                case IncidentSeverity.Critical: return "CRITICAL";
                default: return "UNKNOWN";
            }
        }

        /// <summary>The higher of two severities (severity only ever escalates upward on an incident).</summary>
        public static IncidentSeverity Max(IncidentSeverity left, IncidentSeverity right)
        {
            return (int)left >= (int)right ? left : right;
        }
    }
}
