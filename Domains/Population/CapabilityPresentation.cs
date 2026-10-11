using System;
using LandLedgers.Skills;

namespace LandLedgers.Population
{
    /// <summary>
    /// Player-facing capability presentation — Pass III Worker D rewrite.
    ///
    /// CANON CORRECTION (XXXI §4.5 CANON LOCK): the old seven-band display
    /// (Abysmal/Bad/Poor/Ordinary/Great/Excellent/Mastered) is SUPERSEDED and
    /// removed. The governing bands are Ordinary → Great → Excellent →
    /// Mastered (LandLedgers.Skills.CapabilityBand). Confidence rendering:
    /// High = exactly one band; Moderate = two adjacent bands;
    /// Low = three adjacent bands; Insufficient = "Not enough information".
    /// A single band paired with Moderate/Low confidence is INVALID and is
    /// rejected (CapabilityEvidenceService.ValidateReading).
    ///
    /// Why the old bands were wrong: the superseded display assigned "Abysmal"
    /// to mere low generalSkill (&lt;20), while canon reserves Abysmal for
    /// exceptional inability — not normal inexperience (Rev XVII §1.4).
    /// Ordinary is the broad centre (Rev VII §1.3 anti-inflation lock).
    ///
    /// This renders the foundation's CapabilityEvidenceReading (observer-side
    /// knowledge). It never exposes hidden capability truth.
    /// </summary>
    public static class CapabilityPresentation
    {
        // Band thresholds on continuous capability 0..1. Ordinary is the broad
        // centre; Great and above are tails, so routine employment does not
        // make above-average capability normal (Rev VII §1.3).
        public const double GreatThreshold = 0.70;
        public const double ExcellentThreshold = 0.85;
        public const double MasteredThreshold = 0.95;

        /// <summary>Centre band for a continuous capability value.</summary>
        public static CapabilityBand BandFromCapability(double capability01)
        {
            double value = Math.Max(0.0, Math.Min(1.0, capability01));
            if (value >= MasteredThreshold) return CapabilityBand.Mastered;
            if (value >= ExcellentThreshold) return CapabilityBand.Excellent;
            if (value >= GreatThreshold) return CapabilityBand.Great;
            return CapabilityBand.Ordinary;
        }

        /// <summary>
        /// Builds an observer-side reading from a centre band and confidence,
        /// expanding to the §4.5 span (High 1, Moderate 2, Low 3 adjacent bands).
        /// Throws on invalid combinations (single band + Moderate/Low).
        /// </summary>
        public static CapabilityEvidenceReading ReadingFromCentre(
            string personId, string taskCapabilityId,
            CapabilityBand centre, CapabilityConfidence confidence, string basis)
        {
            var reading = new CapabilityEvidenceReading
            {
                PersonId = personId,
                TaskCapabilityId = taskCapabilityId,
                Confidence = confidence,
                Basis = basis ?? string.Empty,
            };

            if (confidence == CapabilityConfidence.Insufficient)
            {
                reading.Bands = new System.Collections.Generic.List<CapabilityBand>();
                return reading;
            }

            int span = confidence == CapabilityConfidence.High ? 0
                : confidence == CapabilityConfidence.Moderate ? 1 : 2;
            int lower = (int)centre - span / 2;
            lower = Math.Max((int)CapabilityBand.Ordinary,
                Math.Min((int)CapabilityBand.Mastered - span, lower));

            var bands = new System.Collections.Generic.List<CapabilityBand>(span + 1);
            for (int b = lower; b <= lower + span; b++)
            {
                bands.Add((CapabilityBand)b);
            }

            reading.Bands = bands;

            string invalid = CapabilityEvidenceService.ValidateReading(reading);
            if (invalid != null)
            {
                throw new ArgumentException(
                    "CapabilityPresentation.ReadingFromCentre: " + invalid);
            }

            return reading;
        }

        /// <summary>
        /// Legacy bridge: maps WorkerVisibleProfile.generalSkill (0-100) to a
        /// continuous capability, then to a 4-band reading. Confidence follows
        /// the apprenticeship evidence rule (unchanged): ≥12 supported weeks =
        /// High, ≥4 = Moderate, otherwise Low; no usable history = Insufficient.
        /// </summary>
        public static CapabilityEvidenceReading FromPerson(PersonState person)
        {
            if (person == null || person.visibleWorkerProfile == null ||
                !person.visibleWorkerProfile.IsInitialized)
            {
                return ReadingFromCentre(null, null,
                    CapabilityBand.Ordinary, CapabilityConfidence.Insufficient,
                    "Not enough information");
            }

            string history = person.visibleWorkerProfile.roleHistorySummary ?? string.Empty;
            if (string.IsNullOrWhiteSpace(history) ||
                history.Equals("No paid work history", StringComparison.OrdinalIgnoreCase))
            {
                return ReadingFromCentre(null, null,
                    CapabilityBand.Ordinary, CapabilityConfidence.Insufficient,
                    "Not enough information");
            }

            double capability01 = Math.Max(0, Math.Min(100,
                person.visibleWorkerProfile.generalSkill)) / 100.0;
            CapabilityBand centre = BandFromCapability(capability01);

            CapabilityConfidence confidence = person.apprenticeship != null &&
                person.apprenticeship.TotalSupportedWeeksWorked >= 12
                    ? CapabilityConfidence.High
                    : person.apprenticeship != null &&
                      person.apprenticeship.TotalSupportedWeeksWorked >= 4
                        ? CapabilityConfidence.Moderate
                        : CapabilityConfidence.Low;

            return ReadingFromCentre(null, null, centre, confidence,
                "legacy visible profile");
        }

        public static string Label(CapabilityBand band)
        {
            switch (band)
            {
                case CapabilityBand.Ordinary: return "Ordinary";
                case CapabilityBand.Great: return "Great";
                case CapabilityBand.Excellent: return "Excellent";
                case CapabilityBand.Mastered: return "Mastered";
                default: return "Unknown";
            }
        }

        public static string ConfidenceLabel(CapabilityConfidence confidence)
        {
            switch (confidence)
            {
                case CapabilityConfidence.High: return "High";
                case CapabilityConfidence.Moderate: return "Moderate";
                case CapabilityConfidence.Low: return "Low";
                default: return "Not enough information";
            }
        }

        /// <summary>
        /// Renders a reading. Insufficient evidence renders as
        /// "Not enough information" (the old "UNKNOWN (—)" is removed).
        /// Invalid readings render their validation error — never a false band.
        /// </summary>
        public static string FormatReading(CapabilityEvidenceReading reading)
        {
            if (reading == null || reading.Confidence == CapabilityConfidence.Insufficient)
            {
                return "Not enough information";
            }

            string invalid = CapabilityEvidenceService.ValidateReading(reading);
            if (invalid != null)
            {
                return "Invalid reading (" + invalid + ")";
            }

            string bandText = reading.Bands.Count == 1
                ? Label(reading.Bands[0])
                : Label(reading.Bands[0]) + "–" + Label(reading.Bands[reading.Bands.Count - 1];
            return bandText + " (" + ConfidenceLabel(reading.Confidence) + ")";
        }
    }
}
