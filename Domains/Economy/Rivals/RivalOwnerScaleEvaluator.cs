using UnityEngine;

namespace LandLedgers.Economy.Rivals
{
    public sealed class RivalOwnerScaleEvaluator
    {
        private const int MinimumImpliedTownBusinessCount = 2;
        private const int MinimumImpliedTownLandCount = 3;
        private const float CapitalScaleWeight01 = 0.32f;
        private const float HoldingsScaleWeight01 = 0.34f;
        private const float OperatingScaleWeight01 = 0.22f;
        private const float LandScaleWeight01 = 0.12f;

        public RivalOwnerScaleSnapshot Evaluate(string ownerId, RivalScaleInputs inputs)
        {
            int capitalDenominator = ResolveValueDenominator(inputs.playerCapitalCents, inputs.baselineTownCapitalCents);
            int holdingDenominator = ResolveValueDenominator(inputs.playerHoldingValueCents, inputs.baselineHoldingValueCents);
            int operatingDenominator = ResolveOperatingDenominator(inputs);
            int landDenominator = ResolveLandDenominator(inputs);

            float capitalScale = NormalizeRelativeScale(inputs.rivalCapitalCents, capitalDenominator);
            float holdingScale = NormalizeRelativeScale(inputs.rivalHoldingValueCents, holdingDenominator);
            float operatingScale = NormalizeRelativeScale(inputs.rivalBusinessCount, operatingDenominator);
            float landScale = NormalizeRelativeScale(inputs.rivalLandCount, landDenominator);
            // Capital and holdings carry the most scale weight; operations and land describe local anchoring.
            float totalScale = Mathf.Clamp01(
                capitalScale * CapitalScaleWeight01
                + holdingScale * HoldingsScaleWeight01
                + operatingScale * OperatingScaleWeight01
                + landScale * LandScaleWeight01);

            float overlapPressure = Mathf.Clamp01(inputs.opportunityOverlap01);
            // Overlap should sharpen an already-real rival, not turn a tiny early-town owner into a major threat by itself.
            float overlapWeight = Mathf.Lerp(0.1f, 0.22f, totalScale);
            float threat = Mathf.Clamp01(totalScale * (1f - overlapWeight) + overlapPressure * overlapWeight);

            return new RivalOwnerScaleSnapshot(
                ownerId,
                inputs.rivalCapitalCents,
                inputs.rivalHoldingValueCents,
                inputs.rivalBusinessCount,
                inputs.rivalLandCount,
                capitalScale,
                holdingScale,
                operatingScale,
                landScale,
                totalScale,
                threat,
                GetTier(threat));
        }

        public static RivalImportanceTier GetTier(float threatToPlayer01)
        {
            float clamped = Mathf.Clamp01(threatToPlayer01);
            if (clamped >= 0.75f)
            {
                return RivalImportanceTier.MajorRival;
            }

            if (clamped >= 0.5f)
            {
                return RivalImportanceTier.ActiveRival;
            }

            return clamped >= 0.25f ? RivalImportanceTier.LocalCompetitor : RivalImportanceTier.BackgroundOwner;
        }

        private static float NormalizeRelativeScale(int rivalValue, int referenceValue)
        {
            return Mathf.Clamp01(Mathf.Max(0, rivalValue) / (float)Mathf.Max(1, referenceValue));
        }

        private static int ResolveValueDenominator(int playerValue, int baselineTownValue)
        {
            int positivePlayerValue = Mathf.Max(0, playerValue);
            int positiveBaselineValue = Mathf.Max(0, baselineTownValue);
            if (positivePlayerValue <= 0 && positiveBaselineValue <= 0)
            {
                return 1;
            }

            return Mathf.Max(1, Mathf.Max(positivePlayerValue, positiveBaselineValue));
        }

        private static int ResolveOperatingDenominator(RivalScaleInputs inputs)
        {
            int playerBusinessCount = Mathf.Max(0, inputs.playerBusinessCount);
            int baselineTownBusinessCount = Mathf.Max(0, inputs.baselineTownBusinessCount);
            if (baselineTownBusinessCount <= 0 && (inputs.baselineHoldingValueCents > 0 || inputs.baselineTownCapitalCents > 0))
            {
                // Even the thinnest functioning town economy should not treat one rival shop as full-scale saturation by default.
                baselineTownBusinessCount = MinimumImpliedTownBusinessCount;
            }

            return Mathf.Max(1, Mathf.Max(playerBusinessCount, baselineTownBusinessCount));
        }

        private static int ResolveLandDenominator(RivalScaleInputs inputs)
        {
            int playerLandCount = Mathf.Max(0, inputs.playerLandCount);
            int baselineTownLandCount = Mathf.Max(0, inputs.baselineTownLandCount);
            if (baselineTownLandCount <= 0 && (inputs.baselineHoldingValueCents > 0 || inputs.baselineTownCapitalCents > 0))
            {
                // Land control should read against at least a small town-footprint reference when explicit parcel counts are unavailable.
                baselineTownLandCount = MinimumImpliedTownLandCount;
            }

            return Mathf.Max(1, Mathf.Max(playerLandCount, baselineTownLandCount));
        }
    }
}
