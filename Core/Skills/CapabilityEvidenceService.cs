using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Skills
{
    /// <summary>
    /// How an observer came to know something about a worker's capability.
    /// Canon XXXI §4.4: references, work history, observation, and demonstrations
    /// change evidence/confidence — never directly expose hidden truth.
    /// </summary>
    public enum EvidenceKind
    {
        Unknown = 0,
        /// <summary>Someone vouched for the worker (referral, letter of reference).</summary>
        Reference = 1,
        /// <summary>Known employment or work history with the worker.</summary>
        WorkHistory = 2,
        /// <summary>The observer directly watched the worker perform.</summary>
        Observation = 3,
        /// <summary>The worker demonstrated the capability (trial, sample work).</summary>
        Demonstration = 4,
    }

    /// <summary>
    /// Observer-side capability evidence reader. Implementations answer what a
    /// given observer legitimately knows — never the hidden capability truth.
    /// Canon: XXXI §§4.4-4.5; Tech XIV Part III §§3.4-3.5.
    /// </summary>
    public interface ICapabilityEvidenceReader
    {
        CapabilityEvidenceReading ReadCapability(
            string observerId, string subjectId, string taskCapabilityId);
    }

    /// <summary>
    /// One observer-side evidence item: somebody saw, heard, or can vouch for
    /// a worker's capability in a task class.
    /// </summary>
    [Serializable]
    public sealed class CapabilityEvidenceItem
    {
        public string ObserverId;
        public string SubjectId;
        public string TaskCapabilityId;
        public EvidenceKind Kind;
        public CapabilityBand BandHint;
        public string Basis;
        public int Day;
    }

    /// <summary>
    /// Pass III Worker D: observer-side capability evidence.
    ///
    /// Canon: XXXI §4.4 (known skills are evidence, not full stats),
    /// §4.5 CANON LOCK (bands Ordinary/Great/Excellent/Mastered; High = 1 band,
    /// Moderate = 2 adjacent, Low = 3 adjacent, Insufficient = "Not enough
    /// information"; single band + Moderate/Low is INVALID),
    /// Tech XIV Part III §§3.4-3.5; preserved Rev VII §1.5 (confidence invariant).
    ///
    /// Actual capability (PersonCapabilityStore) and observer knowledge stay
    /// separate: this service only aggregates legitimately recorded evidence.
    /// It NEVER reads the hidden truth.
    /// </summary>
    [Serializable]
    public sealed class CapabilityEvidenceService : ICapabilityEvidenceReader
    {
        [SerializeField]
        private List<CapabilityEvidenceItem> items = new List<CapabilityEvidenceItem>();

        /// <summary>
        /// Records one evidence item. The band hint is the recorder's honest
        /// assessment — not a peek at hidden truth (callers must not pass
        /// through the actual capability value).
        /// </summary>
        public void RecordEvidence(
            string observerId, string subjectId, string taskCapabilityId,
            EvidenceKind kind, CapabilityBand bandHint, string basis, int day)
        {
            if (string.IsNullOrEmpty(observerId) || string.IsNullOrEmpty(subjectId) ||
                string.IsNullOrEmpty(taskCapabilityId) || kind == EvidenceKind.Unknown)
            {
                return;
            }

            items.Add(new CapabilityEvidenceItem
            {
                ObserverId = observerId,
                SubjectId = subjectId,
                TaskCapabilityId = taskCapabilityId,
                Kind = kind,
                BandHint = bandHint,
                Basis = basis ?? string.Empty,
                Day = day,
            });
        }

        /// <summary>
        /// Builds the observer-side reading. Confidence follows evidence weight:
        /// none → Insufficient; 1-2 → Low; 3-4 → Moderate; 5+ → High.
        /// Bands are the median hint expanded to the confidence span, clamped
        /// to adjacent bands within Ordinary..Mastered.
        /// </summary>
        public CapabilityEvidenceReading ReadCapability(
            string observerId, string subjectId, string taskCapabilityId)
        {
            var reading = new CapabilityEvidenceReading
            {
                PersonId = subjectId,
                TaskCapabilityId = taskCapabilityId,
                Confidence = CapabilityConfidence.Insufficient,
                Bands = new List<CapabilityBand>(),
                Basis = string.Empty,
            };

            var matched = new List<CapabilityEvidenceItem>();
            foreach (CapabilityEvidenceItem item in items)
            {
                if (string.Equals(item.ObserverId, observerId, StringComparison.Ordinal) &&
                    string.Equals(item.SubjectId, subjectId, StringComparison.Ordinal) &&
                    string.Equals(item.TaskCapabilityId, taskCapabilityId, StringComparison.Ordinal))
                {
                    matched.Add(item);
                }
            }

            if (matched.Count == 0)
            {
                reading.Basis = "Not enough information";
                return reading;
            }

            int weight = 0;
            var hints = new List<int>(matched.Count);
            var kinds = new HashSet<string>();
            foreach (CapabilityEvidenceItem item in matched)
            {
                weight += EvidenceWeight(item.Kind);
                hints.Add((int)item.BandHint);
                kinds.Add(item.Kind.ToString());
            }

            hints.Sort();
            int centre = hints[hints.Count / 2]; // median hint

            CapabilityConfidence confidence =
                weight >= 5 ? CapabilityConfidence.High :
                weight >= 3 ? CapabilityConfidence.Moderate :
                CapabilityConfidence.Low;

            int span = confidence == CapabilityConfidence.High ? 0
                : confidence == CapabilityConfidence.Moderate ? 1 : 2;
            int lower = centre - span / 2;
            lower = Math.Max((int)CapabilityBand.Ordinary,
                Math.Min((int)CapabilityBand.Mastered - span, lower));

            var bands = new List<CapabilityBand>(span + 1);
            for (int b = lower; b <= lower + span; b++)
            {
                bands.Add((CapabilityBand)b);
            }

            reading.Confidence = confidence;
            reading.Bands = bands;
            reading.Basis = string.Join("+", kinds) + " (" + matched.Count + " records)";
            return reading;
        }

        private static int EvidenceWeight(EvidenceKind kind)
        {
            switch (kind)
            {
                case EvidenceKind.Demonstration: return 3;
                case EvidenceKind.Observation: return 2;
                case EvidenceKind.WorkHistory: return 2;
                case EvidenceKind.Reference: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// Validates a reading against the §4.5 confidence invariant.
        /// Returns an error string, or null when valid.
        /// A single band paired with Moderate/Low confidence is INVALID.
        /// </summary>
        public static string ValidateReading(CapabilityEvidenceReading reading)
        {
            if (reading == null)
            {
                return "Reading is null.";
            }

            int bandCount = reading.Bands != null ? reading.Bands.Count : 0;

            if (reading.Confidence == CapabilityConfidence.Insufficient)
            {
                return bandCount == 0
                    ? null
                    : "Insufficient confidence must carry no bands.";
            }

            int expected = reading.Confidence == CapabilityConfidence.High ? 1
                : reading.Confidence == CapabilityConfidence.Moderate ? 2 : 3;
            if (bandCount != expected)
            {
                return "Confidence " + reading.Confidence + " requires exactly " +
                       expected + " band(s); got " + bandCount + ".";
            }

            if (bandCount == 1 && reading.Confidence != CapabilityConfidence.High)
            {
                return "A single band paired with " + reading.Confidence +
                       " confidence is invalid (XXXI §4.5).";
            }

            for (int i = 1; i < bandCount; i++)
            {
                if ((int)reading.Bands[i] != (int)reading.Bands[i - 1] + 1)
                {
                    return "Bands must be adjacent.";
                }
            }

            return null;
        }

        public int ItemCount => items.Count;

        public List<CapabilityEvidenceItem> CaptureItems()
        {
            return new List<CapabilityEvidenceItem>(items);
        }

        /// <summary>Clear-then-load.</summary>
        public void LoadItems(List<CapabilityEvidenceItem> loaded)
        {
            items.Clear();
            if (loaded == null)
            {
                return;
            }

            foreach (CapabilityEvidenceItem item in loaded)
            {
                if (item == null || string.IsNullOrEmpty(item.ObserverId) ||
                    string.IsNullOrEmpty(item.SubjectId) ||
                    string.IsNullOrEmpty(item.TaskCapabilityId))
                {
                    continue;
                }

                items.Add(item);
            }
        }
    }
}
