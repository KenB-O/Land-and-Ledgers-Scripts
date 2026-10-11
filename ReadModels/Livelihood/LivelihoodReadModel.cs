using System;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass III foundation: compact, source-backed read model explaining one
    /// person's present livelihood situation. Every field derives from actual
    /// state and decisions — never fabricated as UI text.
    ///
    /// Privacy rule: only information the requesting observer legitimately knows
    /// may be populated (reuse the capability-evidence confidence rules for NPCs;
    /// the player sees their own household fully, others only through evidence).
    /// </summary>
    [Serializable]
    public class LivelihoodReadModel
    {
        public string PersonId;
        public string HouseholdId;
        public int AsOfDay;
        /// <summary>Descriptive, e.g. "General Store clerk". Occupation describes;
        /// it is not a permission gate.</summary>
        public string CurrentWorkDescription;
        /// <summary>e.g. "Accepted, active", "Seeking", "Casual", "Family work".</summary>
        public string EmploymentState;
        /// <summary>Why they sought work, from real pressure inputs; null if not seeking.</summary>
        public string SeekingReason;
        public string CurrentActivity;
        /// <summary>Descriptive capability summary, e.g. "Experienced at stocking;
        /// adequate bookkeeping; limited masonry." No numeric RPG stats.</summary>
        public string CapabilitySummary;
        public string ExpectedConsequence;
    }
}
