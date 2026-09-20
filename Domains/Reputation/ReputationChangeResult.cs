using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public readonly struct ReputationSubcategoryDelta
    {
        public ReputationSubcategoryDelta(ReputationSubcategory subcategory, float before01, float after01)
        {
            this.subcategory = subcategory;
            this.before01 = Mathf.Clamp01(before01);
            this.after01 = Mathf.Clamp01(after01);
        }

        public readonly ReputationSubcategory subcategory;
        public readonly float before01;
        public readonly float after01;
        public float Delta01 => after01 - before01;
        public int Delta100 => Mathf.RoundToInt(Delta01 * 100f);
        public bool IsImprovement => Delta01 > 0.0001f;
        public bool IsDamage => Delta01 < -0.0001f;
    }

    [Serializable]
    public sealed class ReputationChangeResult
    {
        public ReputationChangeResult(
            float headlineBefore01,
            float headlineAfter01,
            PlayerReputationState before,
            PlayerReputationState after,
            IReadOnlyList<ReputationSubcategoryDelta> deltas,
            IReadOnlyList<ReputationChangeReasonCode> reasonCodes)
        {
            HeadlineBefore01 = Mathf.Clamp01(headlineBefore01);
            HeadlineAfter01 = Mathf.Clamp01(headlineAfter01);
            Before = before ?? new PlayerReputationState();
            After = after ?? new PlayerReputationState();
            Deltas = deltas ?? Array.Empty<ReputationSubcategoryDelta>();
            ReasonCodes = reasonCodes ?? Array.Empty<ReputationChangeReasonCode>();
        }

        public float HeadlineBefore01 { get; }
        public float HeadlineAfter01 { get; }
        public float HeadlineDelta01 => HeadlineAfter01 - HeadlineBefore01;
        public int HeadlineBefore100 => Mathf.RoundToInt(HeadlineBefore01 * 100f);
        public int HeadlineAfter100 => Mathf.RoundToInt(HeadlineAfter01 * 100f);
        public int HeadlineDelta100 => Mathf.RoundToInt(HeadlineDelta01 * 100f);
        public bool HasChange => Mathf.Abs(HeadlineDelta01) > 0.0001f || Deltas.Count > 0;
        public bool IsImprovement => HeadlineDelta01 > 0.0001f;
        public bool IsDamage => HeadlineDelta01 < -0.0001f;
        public PlayerReputationState Before { get; }
        public PlayerReputationState After { get; }
        public IReadOnlyList<ReputationSubcategoryDelta> Deltas { get; }
        public IReadOnlyList<ReputationChangeReasonCode> ReasonCodes { get; }
        public ReputationChangeReasonCode PrimaryReasonCode => ReasonCodes.Count > 0 ? ReasonCodes[0] : ReputationChangeReasonCode.None;
        public ReputationSubcategoryDelta StrongestAbsoluteDelta => FindStrongestDelta(DeltaSelection.Absolute);
        public ReputationSubcategoryDelta StrongestPositiveDelta => FindStrongestDelta(DeltaSelection.Positive);
        public ReputationSubcategoryDelta StrongestNegativeDelta => FindStrongestDelta(DeltaSelection.Negative);

        public bool HasReason(ReputationChangeReasonCode reasonCode)
        {
            for (int i = 0; i < ReasonCodes.Count; i++)
            {
                if (ReasonCodes[i] == reasonCode)
                {
                    return true;
                }
            }

            return false;
        }

        public float GetDelta01(ReputationSubcategory subcategory)
        {
            return TryGetDelta(subcategory, out ReputationSubcategoryDelta delta) ? delta.Delta01 : 0f;
        }

        public bool TryGetDelta(ReputationSubcategory subcategory, out ReputationSubcategoryDelta delta)
        {
            for (int i = 0; i < Deltas.Count; i++)
            {
                if (Deltas[i].subcategory == subcategory)
                {
                    delta = Deltas[i];
                    return true;
                }
            }

            delta = default;
            return false;
        }

        private ReputationSubcategoryDelta FindStrongestDelta(DeltaSelection selection)
        {
            ReputationSubcategoryDelta strongest = default;
            float strongestMagnitude = 0f;

            for (int i = 0; i < Deltas.Count; i++)
            {
                ReputationSubcategoryDelta candidate = Deltas[i];
                float delta = candidate.Delta01;
                if (selection == DeltaSelection.Positive && delta <= 0f)
                {
                    continue;
                }

                if (selection == DeltaSelection.Negative && delta >= 0f)
                {
                    continue;
                }

                float magnitude = selection == DeltaSelection.Absolute ? Mathf.Abs(delta) : Mathf.Abs(delta);
                if (magnitude > strongestMagnitude)
                {
                    strongest = candidate;
                    strongestMagnitude = magnitude;
                }
            }

            return strongest;
        }

        private enum DeltaSelection
        {
            Absolute,
            Positive,
            Negative
        }
    }
}
