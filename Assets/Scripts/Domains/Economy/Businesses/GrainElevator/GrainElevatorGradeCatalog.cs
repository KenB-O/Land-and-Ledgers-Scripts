using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainElevator
{
    /// <summary>
    /// W5B: grain grades as DATA, not canon — the W4B lumberyard
    /// grade-as-data precedent. Historical calibration: Chicago Board of
    /// Trade grain inspection grades (No. 1 / No. 2 / No. 3) date to the
    /// 1850s-60s, and Dakota country elevators graded to board-of-trade
    /// inspection standards (cf. canon R6's "The American Elevator and
    /// Grain Trade" references). No. 2 was the standard contract grade —
    /// the honest default sort for undeclared grain. Exact grade discounts
    /// are an explicit Canon R6 §6 calibration hold, so they live in price
    /// policy, never in this catalog.
    ///
    /// Grade is assigned by the ELEVATOR at intake (the elevator is the
    /// grading authority for what it holds); the upstream CropLot stays
    /// grade-free — that authority is untouched, as with W4A sawmill lots.
    /// </summary>
    public static class GrainElevatorGradeCatalog
    {
        public const string No1GradeId = "no1";        // No. 1 — top inspection grade
        public const string No2GradeId = "no2";        // No. 2 — the standard contract grade; honest default
        public const string No3GradeId = "no3";        // No. 3 — below standard, still millable
        public const string FeedGradeId = "feed";      // feed grade — below milling standard, fit for feed
        public const string RejectedGradeId = "rejected"; // refused by buyers, not by the elevator (stored for feed market)

        private static readonly Dictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { No1GradeId, "No. 1 (top inspection grade)" },
                { No2GradeId, "No. 2 (standard contract grade)" },
                { No3GradeId, "No. 3 (below standard)" },
                { FeedGradeId, "Feed grade (below milling standard)" },
                { RejectedGradeId, "Rejected (feed market only)" },
            };

        /// <summary>The honest default sort for undeclared grain: the standard contract grade.</summary>
        public static string DefaultGradeId => No2GradeId;

        public static bool IsKnownGrade(string gradeId)
        {
            return !string.IsNullOrWhiteSpace(gradeId) && DisplayNames.ContainsKey(gradeId.Trim());
        }

        public static string DisplayName(string gradeId)
        {
            if (string.IsNullOrWhiteSpace(gradeId)) return DefaultGradeId;
            string key = gradeId.Trim();
            return DisplayNames.TryGetValue(key, out string name) ? name : key;
        }
    }
}
