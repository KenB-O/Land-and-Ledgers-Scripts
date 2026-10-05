using System;

namespace LandLedgers.Population
{
    /// <summary>
    /// Player-facing capability presentation. The underlying capability remains the
    /// existing granular WorkerVisibleProfile value; these bands are a calibrated
    /// read model, not a second progression stat.
    /// </summary>
    public enum CapabilityBand
    {
        Abysmal = 0,
        Bad = 1,
        Poor = 2,
        Ordinary = 3,
        Great = 4,
        Excellent = 5,
        Mastered = 6
    }

    public enum CapabilityConfidence
    {
        InsufficientEvidence = 0,
        Low = 1,
        Moderate = 2,
        High = 3
    }

    public readonly struct CapabilityReading
    {
        public CapabilityReading(CapabilityBand lowerBand, CapabilityBand upperBand, CapabilityConfidence confidence)
        {
            LowerBand = lowerBand;
            UpperBand = upperBand;
            Confidence = confidence;
        }

        public CapabilityBand LowerBand { get; }
        public CapabilityBand UpperBand { get; }
        public CapabilityConfidence Confidence { get; }
        public bool IsKnown => Confidence != CapabilityConfidence.InsufficientEvidence;

        public string DisplayBand
        {
            get
            {
                if (!IsKnown) return "UNKNOWN";
                if (LowerBand == UpperBand) return CapabilityPresentation.Label(LowerBand);
                return $"{CapabilityPresentation.Label(LowerBand)}–{CapabilityPresentation.Label(UpperBand)}";
            }
        }

        public string DisplayConfidence => IsKnown
            ? CapabilityPresentation.ConfidenceLabel(Confidence)
            : "—";
    }

    public static class CapabilityPresentation
    {
        // Ordinary is deliberately the broad centre. Great and above are tails,
        // so routine employment does not make above-average capability normal.
        private const int BadThreshold = 20;
        private const int PoorThreshold = 35;
        private const int OrdinaryThreshold = 45;
        private const int GreatThreshold = 70;
        private const int ExcellentThreshold = 85;
        private const int MasteredThreshold = 95;

        public static CapabilityReading FromGeneralSkill(int generalSkill, CapabilityConfidence confidence)
        {
            if (confidence == CapabilityConfidence.InsufficientEvidence)
                return new CapabilityReading(CapabilityBand.Ordinary, CapabilityBand.Ordinary, confidence);

            CapabilityBand centre = FromValue(generalSkill);
            int span = confidence == CapabilityConfidence.High ? 0
                : confidence == CapabilityConfidence.Moderate ? 1 : 2;
            int lower = (int)centre - span / 2;
            lower = Math.Max((int)CapabilityBand.Abysmal, Math.Min((int)CapabilityBand.Mastered - span, lower));
            return new CapabilityReading(
                (CapabilityBand)lower,
                (CapabilityBand)(lower + span),
                confidence);
        }

        public static CapabilityReading FromPerson(PersonState person)
        {
            if (person == null || person.visibleWorkerProfile == null || !person.visibleWorkerProfile.IsInitialized)
                return FromGeneralSkill(0, CapabilityConfidence.InsufficientEvidence);

            string history = person.visibleWorkerProfile.roleHistorySummary ?? string.Empty;
            if (string.IsNullOrWhiteSpace(history) || history.Equals("No paid work history", StringComparison.OrdinalIgnoreCase))
                return FromGeneralSkill(person.visibleWorkerProfile.generalSkill, CapabilityConfidence.InsufficientEvidence);

            CapabilityConfidence confidence = person.apprenticeship != null
                && person.apprenticeship.TotalSupportedWeeksWorked >= 12
                ? CapabilityConfidence.High
                : person.apprenticeship != null && person.apprenticeship.TotalSupportedWeeksWorked >= 4
                    ? CapabilityConfidence.Moderate
                    : CapabilityConfidence.Low;
            return FromGeneralSkill(person.visibleWorkerProfile.generalSkill, confidence);
        }

        public static CapabilityBand FromValue(int generalSkill)
        {
            int value = Math.Max(0, Math.Min(100, generalSkill));
            if (value < BadThreshold) return CapabilityBand.Abysmal;
            if (value < PoorThreshold) return CapabilityBand.Bad;
            if (value < OrdinaryThreshold) return CapabilityBand.Poor;
            if (value < GreatThreshold) return CapabilityBand.Ordinary;
            if (value < ExcellentThreshold) return CapabilityBand.Great;
            if (value < MasteredThreshold) return CapabilityBand.Excellent;
            return CapabilityBand.Mastered;
        }

        public static string Label(CapabilityBand band)
        {
            return band switch
            {
                CapabilityBand.Abysmal => "Abysmal",
                CapabilityBand.Bad => "Bad",
                CapabilityBand.Poor => "Poor",
                CapabilityBand.Ordinary => "Ordinary",
                CapabilityBand.Great => "Great",
                CapabilityBand.Excellent => "Excellent",
                CapabilityBand.Mastered => "Mastered",
                _ => "Unknown"
            };
        }

        public static string ConfidenceLabel(CapabilityConfidence confidence)
        {
            return confidence switch
            {
                CapabilityConfidence.High => "High",
                CapabilityConfidence.Moderate => "Moderate",
                CapabilityConfidence.Low => "Low",
                _ => "—"
            };
        }

        public static string FormatReading(CapabilityReading reading)
        {
            return $"{reading.DisplayBand} ({reading.DisplayConfidence})";
        }
    }
}
