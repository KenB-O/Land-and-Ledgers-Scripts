using System;
using System.Collections.Generic;

namespace LandLedgers.Skills
{
    /// <summary>
    /// Pass III foundation: capability data model contracts.
    /// Canon: XXXI Part I §§1.2-1.4, Part III §§3.1-3.5, Part IV §§4.4-4.6;
    /// Tech XIV Part II §§2.1-2.5; preserved Rev VII Part I §§1.1-1.5;
    /// Rev XVII integration §§3-4, §12; Rev XVI §2.2.
    ///
    /// Four authorities — do NOT collapse into one rating:
    ///   Aptitude   = tendency to learn/perform related work (learning-rate tendency).
    ///   Skill      = present capability at a real class of task (continuous 0..1).
    ///   Experience = what the person has actually done (task history w/ difficulty,
    ///                novelty, supervision, outcome). Calendar time alone adds nothing.
    ///   Knowledge  = domain familiarity (trade/industry/place/procedure context).
    ///
    /// Three catalogue layers (Rev XVII §4): CapabilityFamily (broad transferable)
    /// -> TaskCapability (what materially affects execution) -> DomainFamiliarity.
    /// Player-facing presentation is usually DESCRIPTIVE, not numeric RPG stats.
    ///
    /// FORBIDDEN (canon locks): XP-per-minute level ladders; flat SkillMultiplier
    /// ("-1 minute per level"); seven-band human display; hard skill gates on
    /// ordinary production; fixed "2 occupation + 5 general" skill counts;
    /// experience from calendar time / save restores / re-polled events.
    ///
    /// NOTE: LandLedgers.Population.CapabilityBand (7-band) still exists and is
    /// canon-violating. Worker D replaces it with this 4-band enum. The two live in
    /// different namespaces so there is no compile clash during migration.
    /// </summary>

    /// <summary>Broad transferable capability families (Rev XVII §4, layer 1).</summary>
    public enum CapabilityFamily
    {
        Unknown = 0,
        CommercialInteraction = 1,
        NumericalClerical = 2,
        OrganizationManagement = 3,
        Construction = 4,
        MechanicalRepair = 5,
        AnimalHandling = 6,
        Transport = 7,
        FoodProcessing = 8,
        ExtractionIndustrial = 9,
        Agricultural = 10,
    }

    /// <summary>Governing evidence bands: Ordinary -> Great -> Excellent -> Mastered.
    /// Canon XXXI §4.5 CANON LOCK. The old seven-band display is superseded.</summary>
    public enum CapabilityBand
    {
        Ordinary = 0,
        Great = 1,
        Excellent = 2,
        Mastered = 3,
    }

    /// <summary>Observer-side confidence rendering (XXXI §4.5).
    /// High = exactly one band; Moderate = two adjacent; Low = three adjacent;
    /// Insufficient = "Not enough information". A single band paired with
    /// Moderate/Low confidence is invalid and must be rejected.</summary>
    public enum CapabilityConfidence
    {
        Insufficient = 0,
        Low = 1,
        Moderate = 2,
        High = 3,
    }

    /// <summary>Learning-rate tendency per family. Set at person creation;
    /// aptitude accelerates development but never creates fictional prior training.</summary>
    [Serializable]
    public class AptitudeRecord
    {
        public string PersonId;
        public CapabilityFamily Family;
        /// <summary>Learning-rate multiplier, roughly 0.5 (slow) to 2.0 (quick).</summary>
        public double LearningRateMultiplier = 1.0;
        public int SetDay;
    }

    /// <summary>One real task experience: what the person actually did and encountered.</summary>
    [Serializable]
    public class ExperienceEntry
    {
        /// <summary>
        /// Deterministic idempotency key (assigned by ExperienceLedger when empty).
        /// Save/load restore keys on this — the same entry can NEVER double-credit.
        /// Canon: no experience from save restores or re-polled events (Rev VII §1.2).
        /// </summary>
        public string EntryId;
        public string PersonId;
        public string TaskCapabilityId;
        /// <summary>0..1 task difficulty relative to the worker.</summary>
        public double Difficulty01;
        /// <summary>0..1 novelty of this task for this worker.</summary>
        public double Novelty01;
        public bool Supervised;
        /// <summary>TaskOutcome name at completion (kept as string to avoid a
        /// Core/Skills -> task-authority type cycle in the foundation).</summary>
        public string Outcome;
        public int Day;
        public double MinutesSpent;
    }

    /// <summary>Contextual knowledge: trade, industry, market, place, organization,
    /// procedure, or customer base. A skilled negotiator does not automatically
    /// understand livestock or lumber markets.</summary>
    [Serializable]
    public class DomainFamiliarity
    {
        public string PersonId;
        public string Domain;
        public double Familiarity01;
    }

    /// <summary>Present continuous capability at a task-capability class. 0..1;
    /// Ordinary work lives in the middle — Mastered is exceptional and rare
    /// (Rev VII §1.3 anti-inflation lock: regression tests must catch systemic
    /// upper-band inflation).</summary>
    [Serializable]
    public class PersonCapabilityState
    {
        public string PersonId;
        public string TaskCapabilityId;
        public double Capability01;
        public int LastUpdatedDay;
    }

    /// <summary>
    /// Observer-side evidence reading (XXXI §4.4-4.5): what a recruiting employer
    /// legitimately knows. References, work history, observation, and demonstrations
    /// change evidence/confidence — never directly expose hidden truth.
    /// </summary>
    [Serializable]
    public class CapabilityEvidenceReading
    {
        public string PersonId;
        public string TaskCapabilityId;
        /// <summary>One to three ADJACENT bands, per confidence. Empty when Insufficient.</summary>
        public List<CapabilityBand> Bands = new List<CapabilityBand>();
        public CapabilityConfidence Confidence = CapabilityConfidence.Insufficient;
        public string Basis;
    }
}
