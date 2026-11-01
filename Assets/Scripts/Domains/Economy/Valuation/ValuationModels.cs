using System;
using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Valuation
{
    public enum ParcelAssetKind
    {
        Land = 0,
        ImprovedLand = 1,
        OperatingBusiness = 2
    }

    public enum LandAppreciationHoldingKind
    {
        UrbanParcel = 0,
        AgriculturalHolding = 1
    }

    public enum LandAppreciationImprovementState
    {
        Empty = 0,
        ImprovedShell = 1,
        OperatingBusiness = 2
    }

    public enum ValuationScorecardCategory
    {
        Location = 0,
        Traffic = 1,
        CommercialPotential = 2,
        DevelopmentReadiness = 3,
        SellerPressure = 4,
        Condition = 5,
        SiteConstraints = 6
    }

    public enum ScorecardGrade
    {
        Poor = 0,
        Fair = 1,
        Solid = 2,
        Strong = 3,
        Exceptional = 4
    }

    [Serializable]
    public readonly struct ScorecardRow
    {
        public readonly ValuationScorecardCategory category;
        public readonly float score01;
        public readonly ScorecardGrade grade;
        public readonly string summary;
        public readonly string[] driverCodes;

        public ScorecardRow(ValuationScorecardCategory category, float score01, string summary, params string[] driverCodes)
        {
            this.category = category;
            this.score01 = Mathf.Clamp01(score01);
            grade = ToGrade(this.score01);
            this.summary = summary ?? string.Empty;
            this.driverCodes = driverCodes ?? Array.Empty<string>();
        }

        public int Score100 => Mathf.RoundToInt(score01 * 100f);

        public static ScorecardGrade ToGrade(float score01)
        {
            float clamped = Mathf.Clamp01(score01);
            if (clamped >= 0.85f)
            {
                return ScorecardGrade.Exceptional;
            }

            if (clamped >= 0.68f)
            {
                return ScorecardGrade.Strong;
            }

            if (clamped >= 0.48f)
            {
                return ScorecardGrade.Solid;
            }

            return clamped >= 0.28f ? ScorecardGrade.Fair : ScorecardGrade.Poor;
        }
    }

    [Serializable]
    public sealed class ValuationScorecard
    {
        private readonly List<ScorecardRow> rows = new List<ScorecardRow>();

        public ValuationScorecard(float overallScore01, IEnumerable<ScorecardRow> rows)
        {
            OverallScore01 = Mathf.Clamp01(overallScore01);
            if (rows == null)
            {
                return;
            }

            this.rows.AddRange(rows);
        }

        public float OverallScore01 { get; }
        public int OverallScore100 => Mathf.RoundToInt(OverallScore01 * 100f);
        public IReadOnlyList<ScorecardRow> Rows => rows;

        public bool TryGetRow(ValuationScorecardCategory category, out ScorecardRow row)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].category == category)
                {
                    row = rows[i];
                    return true;
                }
            }

            row = default;
            return false;
        }
    }

    [Serializable]
    public struct ParcelValuationInputs
    {
        public string assetId;
        public int plotId;
        public int buildingId;
        public ParcelAssetKind assetKind;
        public PlotZone zone;
        public Vector2Int siteSizeCells;
        public int buildableAreaCells;
        public int frontageCells;
        public int depthCells;
        public GridDirection roadFrontageDirection;
        public float distanceToMainStreet01;
        public float distanceToTownCenter01;
        public float trafficExposure01;
        public float nearbyHouseholds01;
        public float nearbyBusinesses01;
        public float competitionPressure01;
        public float commercialFit01;
        public float developmentReadiness01;
        public float improvementCondition01;
        public float knownConstraints01;
        public bool listedForSale;
        public int currentAskingPriceCents;

        public int SiteAreaCells => Mathf.Max(0, siteSizeCells.x) * Mathf.Max(0, siteSizeCells.y);
        public bool HasExplicitBuildableArea => buildableAreaCells >= 0;
        public bool HasExplicitTrafficExposure => trafficExposure01 >= 0f;
        public bool HasExplicitCommercialFit => commercialFit01 >= 0f;
        public bool HasExplicitDevelopmentReadiness => developmentReadiness01 >= 0f;
        public bool HasExplicitImprovementCondition => improvementCondition01 >= 0f;
        public bool HasExplicitKnownConstraints => knownConstraints01 >= 0f;
        public bool HasExplicitAskingPrice => currentAskingPriceCents > 0;

        public ParcelValuationInputs Sanitized()
        {
            ParcelValuationInputs sanitized = this;
            sanitized.assetId = assetId ?? string.Empty;
            sanitized.siteSizeCells = new Vector2Int(Mathf.Max(0, siteSizeCells.x), Mathf.Max(0, siteSizeCells.y));
            sanitized.buildableAreaCells = buildableAreaCells < 0 ? -1 : Mathf.Max(0, buildableAreaCells);
            sanitized.frontageCells = Mathf.Max(0, frontageCells);
            sanitized.depthCells = Mathf.Max(0, depthCells);
            sanitized.distanceToMainStreet01 = Mathf.Clamp01(distanceToMainStreet01);
            sanitized.distanceToTownCenter01 = Mathf.Clamp01(distanceToTownCenter01);
            sanitized.trafficExposure01 = ClampOptional01(trafficExposure01);
            sanitized.nearbyHouseholds01 = Mathf.Clamp01(nearbyHouseholds01);
            sanitized.nearbyBusinesses01 = Mathf.Clamp01(nearbyBusinesses01);
            sanitized.competitionPressure01 = Mathf.Clamp01(competitionPressure01);
            sanitized.commercialFit01 = ClampOptional01(commercialFit01);
            sanitized.developmentReadiness01 = ClampOptional01(developmentReadiness01);
            sanitized.improvementCondition01 = ClampOptional01(improvementCondition01);
            sanitized.knownConstraints01 = ClampOptional01(knownConstraints01);
            sanitized.currentAskingPriceCents = Mathf.Max(0, currentAskingPriceCents);
            return sanitized;
        }

        private static float ClampOptional01(float value)
        {
            return value < 0f ? -1f : Mathf.Clamp01(value);
        }
    }

    [Serializable]
    public struct ParcelValuationWeights
    {
        public int baseLandPricePerCellCents;
        public int frontagePremiumPerCellCents;
        public float businessZoneMultiplier;
        public float mixedUseZoneMultiplier;
        public float residentialZoneMultiplier;
        public float improvedLandMultiplier;
        public float operatingBusinessMultiplier;
        public float locationWeight;
        public float trafficWeight;
        public float commercialPotentialWeight;
        public float developmentReadinessWeight;
        public float conditionWeight;
        public float constraintPenaltyWeight;
        public float valuationBandPercent;

        public static ParcelValuationWeights Default => new ParcelValuationWeights
        {
            baseLandPricePerCellCents = 55,
            frontagePremiumPerCellCents = 22,
            businessZoneMultiplier = 1.35f,
            mixedUseZoneMultiplier = 1.18f,
            residentialZoneMultiplier = 1f,
            improvedLandMultiplier = 1.12f,
            operatingBusinessMultiplier = 1.35f,
            locationWeight = 0.28f,
            trafficWeight = 0.22f,
            commercialPotentialWeight = 0.28f,
            developmentReadinessWeight = 0.22f,
            conditionWeight = 0.1f,
            constraintPenaltyWeight = 0.18f,
            valuationBandPercent = 0.12f
        };

        public ParcelValuationWeights Sanitized()
        {
            ParcelValuationWeights sanitized = this;
            sanitized.baseLandPricePerCellCents = Mathf.Max(1, sanitized.baseLandPricePerCellCents);
            sanitized.frontagePremiumPerCellCents = Mathf.Max(0, sanitized.frontagePremiumPerCellCents);
            sanitized.businessZoneMultiplier = Mathf.Max(0.1f, sanitized.businessZoneMultiplier);
            sanitized.mixedUseZoneMultiplier = Mathf.Max(0.1f, sanitized.mixedUseZoneMultiplier);
            sanitized.residentialZoneMultiplier = Mathf.Max(0.1f, sanitized.residentialZoneMultiplier);
            sanitized.improvedLandMultiplier = Mathf.Max(0.1f, sanitized.improvedLandMultiplier);
            sanitized.operatingBusinessMultiplier = Mathf.Max(0.1f, sanitized.operatingBusinessMultiplier);
            sanitized.locationWeight = Mathf.Max(0f, sanitized.locationWeight);
            sanitized.trafficWeight = Mathf.Max(0f, sanitized.trafficWeight);
            sanitized.commercialPotentialWeight = Mathf.Max(0f, sanitized.commercialPotentialWeight);
            sanitized.developmentReadinessWeight = Mathf.Max(0f, sanitized.developmentReadinessWeight);
            sanitized.conditionWeight = Mathf.Max(0f, sanitized.conditionWeight);
            sanitized.constraintPenaltyWeight = Mathf.Max(0f, sanitized.constraintPenaltyWeight);
            sanitized.valuationBandPercent = Mathf.Clamp(sanitized.valuationBandPercent, 0f, 0.5f);
            return sanitized;
        }
    }

    [Serializable]
    public sealed class ParcelValuationResult
    {

        private const string LegacyConditionSummary = "Condition detail is not explicitly surfaced in this valuation path.";
        private const string LegacyConstraintSummary = "Constraint detail is not explicitly surfaced in this valuation path.";

        public ParcelValuationResult(
            int estimatedValueCents,
            int valuationBandLowCents,
            int valuationBandHighCents,
            float confidence01,
            float locationScore01,
            float trafficScore01,
            float commercialPotentialScore01,
            float developmentReadinessScore01,
            ValuationScorecard scorecard,
            IReadOnlyList<string> positiveDriverCodes,
            IReadOnlyList<string> negativeDriverCodes)
            : this(
                estimatedValueCents,
                valuationBandLowCents,
                valuationBandHighCents,
                confidence01,
                locationScore01,
                trafficScore01,
                commercialPotentialScore01,
                developmentReadinessScore01,
                0.5f,
                0.5f,
                LegacyConditionSummary,
                LegacyConstraintSummary,
                0,
                string.Empty,
                scorecard,
                positiveDriverCodes,
                negativeDriverCodes)
        {
        }

        public ParcelValuationResult(
            int estimatedValueCents,
            int valuationBandLowCents,
            int valuationBandHighCents,
            float confidence01,
            float locationScore01,
            float trafficScore01,
            float commercialPotentialScore01,
            float developmentReadinessScore01,
            float conditionScore01,
            float constraintScore01,
            string conditionSummary,
            string constraintSummary,
            int currentAskingPriceCents,
            string askingPriceSummary,
            ValuationScorecard scorecard,
            IReadOnlyList<string> positiveDriverCodes,
            IReadOnlyList<string> negativeDriverCodes)
        {
            EstimatedValueCents = Mathf.Max(1, estimatedValueCents);
            ValuationBandLowCents = Mathf.Max(1, valuationBandLowCents);
            ValuationBandHighCents = Mathf.Max(ValuationBandLowCents, valuationBandHighCents);
            Confidence01 = Mathf.Clamp01(confidence01);
            LocationScore01 = Mathf.Clamp01(locationScore01);
            TrafficScore01 = Mathf.Clamp01(trafficScore01);
            CommercialPotentialScore01 = Mathf.Clamp01(commercialPotentialScore01);
            DevelopmentReadinessScore01 = Mathf.Clamp01(developmentReadinessScore01);
            ConditionScore01 = Mathf.Clamp01(conditionScore01);
            ConstraintScore01 = Mathf.Clamp01(constraintScore01);
            ConditionSummary = conditionSummary ?? string.Empty;
            ConstraintSummary = constraintSummary ?? string.Empty;
            CurrentAskingPriceCents = Mathf.Max(0, currentAskingPriceCents);
            AskingPriceSummary = askingPriceSummary ?? string.Empty;
            Scorecard = scorecard ?? new ValuationScorecard(0f, Array.Empty<ScorecardRow>());
            PositiveDriverCodes = positiveDriverCodes ?? Array.Empty<string>();
            NegativeDriverCodes = negativeDriverCodes ?? Array.Empty<string>();
        }

        public int EstimatedValueCents { get; }
        public int ValuationBandLowCents { get; }
        public int ValuationBandHighCents { get; }
        public float Confidence01 { get; }
        public float LocationScore01 { get; }
        public float TrafficScore01 { get; }
        public float CommercialPotentialScore01 { get; }
        public float DevelopmentReadinessScore01 { get; }
        public float ConditionScore01 { get; }
        public float ConstraintScore01 { get; }
        public string ConditionSummary { get; }
        public string ConstraintSummary { get; }
        public int CurrentAskingPriceCents { get; }
        public bool HasAskingPrice => CurrentAskingPriceCents > 0;
        public int AskingPriceDeltaCents => CurrentAskingPriceCents - EstimatedValueCents;
        public float AskingPriceToEstimatedValueRatio => EstimatedValueCents <= 0 ? 0f : CurrentAskingPriceCents / (float)EstimatedValueCents;
        public bool AskingPriceWithinValuationBand => HasAskingPrice && CurrentAskingPriceCents >= ValuationBandLowCents && CurrentAskingPriceCents <= ValuationBandHighCents;
        public string AskingPriceSummary { get; }
        public ValuationScorecard Scorecard { get; }
        public IReadOnlyList<string> PositiveDriverCodes { get; }
        public IReadOnlyList<string> NegativeDriverCodes { get; }
    }

    [Serializable]
    public sealed class LandAppreciationState
    {
        public int plotId = -1;
        public int buildingId = -1;
        public LandAppreciationHoldingKind holdingKind;
        public LandAppreciationImprovementState improvementState;
        public int purchaseBasisCents;
        public int capitalizedImprovementCents;
        public int currentEstimatedValueCents;
        public int appreciationDeltaCents;
        public int purchaseDayIndex;
        public int lastValuationDayIndex;
        public float townGrowthPressure01;
        public float developmentReadiness01;

        public int TotalBasisCents => Mathf.Max(1, Mathf.Max(0, purchaseBasisCents) + Mathf.Max(0, capitalizedImprovementCents));
        public int DaysHeld => Mathf.Max(0, lastValuationDayIndex - purchaseDayIndex);
    }

    [Serializable]
    public struct LandAppreciationInputs
    {
        public LandAppreciationHoldingKind holdingKind;
        public LandAppreciationImprovementState improvementState;
        public int purchaseBasisCents;
        public int capitalizedImprovementCents;
        public int purchaseDayIndex;
        public int currentDayIndex;
        public float townGrowthPressure01;
        public float developmentReadiness01;

        public int TotalBasisCents => Mathf.Max(1, Mathf.Max(0, purchaseBasisCents) + Mathf.Max(0, capitalizedImprovementCents));
        public int DaysHeld => Mathf.Max(0, currentDayIndex - purchaseDayIndex);
    }

    [Serializable]
    public struct LandAppreciationWeights
    {
        public float baseAnnualAppreciationRate;
        public float townGrowthAnnualRate;
        public float readinessAnnualRate;
        public float improvedShellAnnualBonus;
        public float operatingBusinessAnnualBonus;
        public float urbanAnnualRateMultiplier;
        public float agriculturalAnnualRateMultiplier;
        public float maxAnnualAppreciationRate;
        public float minEstimatedValueRatio;
        public float maxEstimatedValueRatio;
        public float daysPerYear;

        public static LandAppreciationWeights Default => new LandAppreciationWeights
        {
            baseAnnualAppreciationRate = 0.0125f,
            townGrowthAnnualRate = 0.045f,
            readinessAnnualRate = 0.02f,
            improvedShellAnnualBonus = 0.01f,
            operatingBusinessAnnualBonus = 0.018f,
            urbanAnnualRateMultiplier = 1f,
            agriculturalAnnualRateMultiplier = 0.65f,
            maxAnnualAppreciationRate = 0.11f,
            minEstimatedValueRatio = 0.75f,
            maxEstimatedValueRatio = 1.75f,
            daysPerYear = 365f
        };

        public LandAppreciationWeights Sanitized()
        {
            LandAppreciationWeights sanitized = this;
            sanitized.baseAnnualAppreciationRate = Mathf.Max(0f, sanitized.baseAnnualAppreciationRate);
            sanitized.townGrowthAnnualRate = Mathf.Max(0f, sanitized.townGrowthAnnualRate);
            sanitized.readinessAnnualRate = Mathf.Max(0f, sanitized.readinessAnnualRate);
            sanitized.improvedShellAnnualBonus = Mathf.Max(0f, sanitized.improvedShellAnnualBonus);
            sanitized.operatingBusinessAnnualBonus = Mathf.Max(0f, sanitized.operatingBusinessAnnualBonus);
            sanitized.urbanAnnualRateMultiplier = Mathf.Max(0.1f, sanitized.urbanAnnualRateMultiplier);
            sanitized.agriculturalAnnualRateMultiplier = Mathf.Max(0.1f, sanitized.agriculturalAnnualRateMultiplier);
            sanitized.maxAnnualAppreciationRate = Mathf.Clamp(sanitized.maxAnnualAppreciationRate, 0f, 0.5f);
            sanitized.minEstimatedValueRatio = Mathf.Clamp(sanitized.minEstimatedValueRatio, 0.1f, 1f);
            sanitized.maxEstimatedValueRatio = Mathf.Max(sanitized.minEstimatedValueRatio, sanitized.maxEstimatedValueRatio);
            sanitized.daysPerYear = Mathf.Max(1f, sanitized.daysPerYear);
            return sanitized;
        }
    }

    [Serializable]
    public sealed class LandAppreciationResult
    {
        public LandAppreciationResult(
            int totalBasisCents,
            int estimatedValueCents,
            int appreciationDeltaCents,
            int daysHeld,
            float annualRate01,
            float timeMultiplier)
        {
            TotalBasisCents = Mathf.Max(1, totalBasisCents);
            EstimatedValueCents = Mathf.Max(1, estimatedValueCents);
            AppreciationDeltaCents = appreciationDeltaCents;
            DaysHeld = Mathf.Max(0, daysHeld);
            AnnualRate01 = Mathf.Max(0f, annualRate01);
            TimeMultiplier = Mathf.Max(0f, timeMultiplier);
        }

        public int TotalBasisCents { get; }
        public int EstimatedValueCents { get; }
        public int AppreciationDeltaCents { get; }
        public int DaysHeld { get; }
        public float AnnualRate01 { get; }
        public float TimeMultiplier { get; }
    }

    public sealed class LandAppreciationEvaluator
    {
        public LandAppreciationResult Evaluate(LandAppreciationInputs inputs)
        {
            return Evaluate(inputs, LandAppreciationWeights.Default);
        }

        public LandAppreciationResult Evaluate(LandAppreciationInputs inputs, LandAppreciationWeights weights)
        {
            LandAppreciationWeights sanitized = weights.Sanitized();
            int totalBasis = inputs.TotalBasisCents;
            int daysHeld = inputs.DaysHeld;
            float pressure = Mathf.Clamp01(inputs.townGrowthPressure01);
            float readiness = Mathf.Clamp01(inputs.developmentReadiness01);
            float holdingMultiplier = inputs.holdingKind == LandAppreciationHoldingKind.AgriculturalHolding
                ? sanitized.agriculturalAnnualRateMultiplier
                : sanitized.urbanAnnualRateMultiplier;

            float annualRate =
                sanitized.baseAnnualAppreciationRate
                + pressure * sanitized.townGrowthAnnualRate
                + readiness * sanitized.readinessAnnualRate
                + GetImprovementAnnualBonus(inputs.improvementState, sanitized);
            annualRate = Mathf.Clamp(annualRate * holdingMultiplier, 0f, sanitized.maxAnnualAppreciationRate);

            float yearsHeld = daysHeld / sanitized.daysPerYear;
            float timeMultiplier = 1f + annualRate * yearsHeld;
            int rawEstimate = Mathf.Max(1, Mathf.RoundToInt(totalBasis * timeMultiplier));
            int low = Mathf.Max(1, Mathf.RoundToInt(totalBasis * sanitized.minEstimatedValueRatio));
            int high = Mathf.Max(low, Mathf.RoundToInt(totalBasis * sanitized.maxEstimatedValueRatio));
            int estimated = Mathf.Clamp(rawEstimate, low, high);

            return new LandAppreciationResult(
                totalBasis,
                estimated,
                estimated - totalBasis,
                daysHeld,
                annualRate,
                timeMultiplier);
        }

        private static float GetImprovementAnnualBonus(LandAppreciationImprovementState state, LandAppreciationWeights weights)
        {
            return state switch
            {
                LandAppreciationImprovementState.OperatingBusiness => weights.operatingBusinessAnnualBonus,
                LandAppreciationImprovementState.ImprovedShell => weights.improvedShellAnnualBonus,
                _ => 0f
            };
        }
    }
}
