using System;
using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Valuation
{
    public sealed class ParcelValuationEvaluator
    {
        private const float UnknownTrafficExposureDefault01 = 0.45f;
        private const float UnknownCommercialFitDefault01 = 0.5f;
        private const float UnknownConstraintBurdenDefault01 = 0.45f;
        private const float UnknownBuildableRatioDefault01 = 0.75f;

        public ParcelValuationResult Evaluate(ParcelValuationInputs inputs)
        {
            return Evaluate(inputs, ParcelValuationWeights.Default);
        }

        public ParcelValuationResult Evaluate(ParcelValuationInputs inputs, ParcelValuationWeights weights)
        {
            ParcelValuationInputs sanitizedInputs = inputs.Sanitized();
            ParcelValuationWeights sanitizedWeights = weights.Sanitized();

            float locationScore = CalculateLocationScore(sanitizedInputs);
            float trafficScore = CalculateTrafficScore(sanitizedInputs);
            float commercialScore = CalculateCommercialPotentialScore(sanitizedInputs);
            float readinessScore = CalculateDevelopmentReadinessScore(sanitizedInputs);
            float conditionScore = sanitizedInputs.assetKind == ParcelAssetKind.Land
                ? 1f
                : Clamp01OrUnknown(sanitizedInputs.improvementCondition01, 0.5f);
            float constraintScore = 1f - Clamp01OrUnknown(sanitizedInputs.knownConstraints01, UnknownConstraintBurdenDefault01);

            float weightedScore = WeightedAverage(
                locationScore,
                sanitizedWeights.locationWeight,
                trafficScore,
                sanitizedWeights.trafficWeight,
                commercialScore,
                sanitizedWeights.commercialPotentialWeight,
                readinessScore,
                sanitizedWeights.developmentReadinessWeight);

            int siteArea = Mathf.Max(1, sanitizedInputs.SiteAreaCells);
            int buildableArea = ResolveBuildableAreaCells(sanitizedInputs, siteArea);
            int baseValue = siteArea * sanitizedWeights.baseLandPricePerCellCents;
            int frontageValue = Mathf.Max(0, sanitizedInputs.frontageCells) * sanitizedWeights.frontagePremiumPerCellCents;
            float qualityMultiplier = 0.75f + weightedScore * 0.65f;
            qualityMultiplier += conditionScore * sanitizedWeights.conditionWeight;
            qualityMultiplier += constraintScore * sanitizedWeights.constraintPenaltyWeight;
            qualityMultiplier -= Mathf.Clamp01(sanitizedInputs.competitionPressure01) * 0.12f;

            float buildableRatio = Mathf.Clamp01(buildableArea / (float)siteArea);
            qualityMultiplier *= Mathf.Lerp(0.7f, 1.08f, buildableRatio);
            qualityMultiplier *= GetSoftenedZoneMultiplier(sanitizedInputs.zone, sanitizedWeights);
            qualityMultiplier *= GetAssetKindMultiplier(sanitizedInputs.assetKind, sanitizedWeights);

            int estimatedValue = Mathf.Max(1, Mathf.RoundToInt((baseValue + frontageValue) * Mathf.Max(0.1f, qualityMultiplier)));
            float confidence = CalculateConfidence(sanitizedInputs);
            float bandPercent = sanitizedWeights.valuationBandPercent * Mathf.Lerp(1.35f, 0.75f, confidence);
            int low = Mathf.Max(1, Mathf.RoundToInt(estimatedValue * (1f - bandPercent)));
            int high = Mathf.Max(low, Mathf.RoundToInt(estimatedValue * (1f + bandPercent)));
            // Condition and site constraints are part of the parcel quality read, while asking
            // price remains negotiation context rather than intrinsic parcel quality.
            string conditionSummary = DescribeCondition(conditionScore, sanitizedInputs.assetKind, sanitizedInputs.HasExplicitImprovementCondition);
            string constraintSummary = DescribeConstraints(constraintScore, sanitizedInputs.HasExplicitKnownConstraints);
            string askingPriceSummary = DescribeAskingPrice(sanitizedInputs.currentAskingPriceCents, estimatedValue, low, high, confidence);

            List<ScorecardRow> rows = new List<ScorecardRow>
            {
                new ScorecardRow(ValuationScorecardCategory.Location, locationScore, DescribeLocation(locationScore), Driver("location", locationScore)),
                new ScorecardRow(ValuationScorecardCategory.Traffic, trafficScore, DescribeTraffic(trafficScore, sanitizedInputs.HasExplicitTrafficExposure), Driver("traffic", trafficScore)),
                new ScorecardRow(ValuationScorecardCategory.CommercialPotential, commercialScore, DescribeCommercial(commercialScore, sanitizedInputs.HasExplicitCommercialFit), Driver("commercial", commercialScore)),
                new ScorecardRow(ValuationScorecardCategory.DevelopmentReadiness, readinessScore, DescribeReadiness(readinessScore, sanitizedInputs.HasExplicitBuildableArea, sanitizedInputs.HasExplicitDevelopmentReadiness, sanitizedInputs.HasExplicitKnownConstraints), Driver("readiness", readinessScore)),
                new ScorecardRow(ValuationScorecardCategory.Condition, conditionScore, conditionSummary, Driver("condition", conditionScore)),
                new ScorecardRow(ValuationScorecardCategory.SiteConstraints, constraintScore, constraintSummary, Driver("constraints", constraintScore))
            };

            List<string> positiveDrivers = new List<string>();
            List<string> negativeDrivers = new List<string>();
            AddDriver(positiveDrivers, negativeDrivers, "location", locationScore);
            AddDriver(positiveDrivers, negativeDrivers, "traffic", trafficScore);
            AddDriver(positiveDrivers, negativeDrivers, "commercial_potential", commercialScore);
            AddDriver(positiveDrivers, negativeDrivers, "development_readiness", readinessScore);
            AddDriver(positiveDrivers, negativeDrivers, "condition", conditionScore);
            AddDriver(positiveDrivers, negativeDrivers, "constraints", constraintScore);

            return new ParcelValuationResult(
                estimatedValue,
                low,
                high,
                confidence,
                locationScore,
                trafficScore,
                commercialScore,
                readinessScore,
                conditionScore,
                constraintScore,
                conditionSummary,
                constraintSummary,
                sanitizedInputs.currentAskingPriceCents,
                askingPriceSummary,
                new ValuationScorecard(weightedScore, rows),
                positiveDrivers,
                negativeDrivers);
        }

        private static float CalculateLocationScore(ParcelValuationInputs inputs)
        {
            float zoneFit = GetZoneSuitability01(inputs.zone);
            float mainStreet = 1f - Mathf.Clamp01(inputs.distanceToMainStreet01);
            float townCenter = 1f - Mathf.Clamp01(inputs.distanceToTownCenter01);
            float frontageScale = Mathf.Clamp01(inputs.frontageCells / 8f);
            return Mathf.Clamp01(zoneFit * 0.2f + mainStreet * 0.4f + townCenter * 0.25f + frontageScale * 0.15f);
        }

        private static float CalculateTrafficScore(ParcelValuationInputs inputs)
        {
            float frontageScore = Mathf.Clamp01(inputs.frontageCells / 8f);
            float trafficExposure = Clamp01OrUnknown(inputs.trafficExposure01, UnknownTrafficExposureDefault01);
            return Mathf.Clamp01(
                trafficExposure * 0.62f
                + frontageScore * 0.23f
                + Mathf.Clamp01(inputs.nearbyBusinesses01) * 0.1f
                + Mathf.Clamp01(inputs.nearbyHouseholds01) * 0.05f);
        }

        private static float CalculateCommercialPotentialScore(ParcelValuationInputs inputs)
        {
            float zoneFit = GetZoneSuitability01(inputs.zone);
            float siteScale = Mathf.Clamp01(inputs.SiteAreaCells / 64f);
            float competitionRelief = 1f - Mathf.Clamp01(inputs.competitionPressure01);
            float commercialFit = Clamp01OrUnknown(inputs.commercialFit01, UnknownCommercialFitDefault01);
            float trafficExposure = Clamp01OrUnknown(inputs.trafficExposure01, UnknownTrafficExposureDefault01);
            return Mathf.Clamp01(
                zoneFit * 0.12f
                + commercialFit * 0.4f
                + siteScale * 0.16f
                + trafficExposure * 0.06f
                + Mathf.Clamp01(inputs.nearbyHouseholds01) * 0.11f
                + Mathf.Clamp01(inputs.nearbyBusinesses01) * 0.08f
                + competitionRelief * 0.07f);
        }

        private static float CalculateDevelopmentReadinessScore(ParcelValuationInputs inputs)
        {
            int area = Mathf.Max(1, inputs.SiteAreaCells);
            int buildableArea = ResolveBuildableAreaCells(inputs, area);
            float buildableRatio = Mathf.Clamp01(buildableArea / (float)area);
            float depthScore = inputs.depthCells <= 0 ? 0.55f : Mathf.Clamp01(inputs.depthCells / 8f);
            float constraints = 1f - Clamp01OrUnknown(inputs.knownConstraints01, UnknownConstraintBurdenDefault01);
            return Mathf.Clamp01(
                Clamp01OrUnknown(inputs.developmentReadiness01, 0.5f) * 0.52f
                + buildableRatio * 0.26f
                + depthScore * 0.08f
                + constraints * 0.14f);
        }

        private static float CalculateConfidence(ParcelValuationInputs inputs)
        {
            float explicitInputs = 0.18f;
            explicitInputs += inputs.SiteAreaCells > 0 ? 0.08f : 0f;
            explicitInputs += inputs.frontageCells > 0 ? 0.08f : 0f;
            explicitInputs += inputs.HasExplicitBuildableArea ? 0.14f : 0f;
            explicitInputs += inputs.HasExplicitTrafficExposure ? 0.16f : 0f;
            explicitInputs += inputs.HasExplicitCommercialFit ? 0.16f : 0f;
            explicitInputs += inputs.HasExplicitDevelopmentReadiness ? 0.12f : 0f;
            explicitInputs += inputs.HasExplicitKnownConstraints ? 0.08f : 0f;

            if (inputs.assetKind == ParcelAssetKind.Land)
            {
                explicitInputs += 0.08f;
            }
            else if (inputs.HasExplicitImprovementCondition)
            {
                explicitInputs += 0.08f;
            }

            return Mathf.Clamp01(explicitInputs);
        }

        private static int ResolveBuildableAreaCells(ParcelValuationInputs inputs, int siteArea)
        {
            if (inputs.HasExplicitBuildableArea)
            {
                return Mathf.Clamp(inputs.buildableAreaCells, 0, siteArea);
            }

            return Mathf.Clamp(Mathf.RoundToInt(siteArea * UnknownBuildableRatioDefault01), 0, siteArea);
        }

        private static float GetZoneMultiplier(PlotZone zone, ParcelValuationWeights weights)
        {
            return zone switch
            {
                PlotZone.Business => weights.businessZoneMultiplier,
                PlotZone.MixedUse => weights.mixedUseZoneMultiplier,
                PlotZone.Residential => weights.residentialZoneMultiplier,
                _ => 1f
            };
        }

        private static float GetZoneSuitability01(PlotZone zone)
        {
            return zone switch
            {
                PlotZone.Business => 0.82f,
                PlotZone.MixedUse => 0.72f,
                PlotZone.Residential => 0.56f,
                _ => 0.5f
            };
        }

        private static float GetSoftenedZoneMultiplier(PlotZone zone, ParcelValuationWeights weights)
        {
            // Keep zone as a context hint, not a hard lock. Suitability should still come
            // mainly from frontage, access, scale, traffic, nearby demand, and constraints.
            return Mathf.Lerp(1f, GetZoneMultiplier(zone, weights), 0.45f);
        }

        private static float GetAssetKindMultiplier(ParcelAssetKind kind, ParcelValuationWeights weights)
        {
            return kind switch
            {
                ParcelAssetKind.ImprovedLand => weights.improvedLandMultiplier,
                ParcelAssetKind.OperatingBusiness => weights.operatingBusinessMultiplier,
                _ => 1f
            };
        }

        private static float WeightedAverage(float first, float firstWeight, float second, float secondWeight, float third, float thirdWeight, float fourth, float fourthWeight)
        {
            float totalWeight = firstWeight + secondWeight + thirdWeight + fourthWeight;
            if (totalWeight <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01((first * firstWeight + second * secondWeight + third * thirdWeight + fourth * fourthWeight) / totalWeight);
        }

        private static float Clamp01OrUnknown(float value, float defaultValue)
        {
            // Negative values mean "unknown / not authored yet". Zero stays a real low score.
            return value < 0f ? Mathf.Clamp01(defaultValue) : Mathf.Clamp01(value);
        }

        private static void AddDriver(List<string> positiveDrivers, List<string> negativeDrivers, string code, float score01)
        {
            if (score01 >= 0.7f)
            {
                positiveDrivers.Add(code);
            }
            else if (score01 <= 0.35f)
            {
                negativeDrivers.Add(code);
            }
        }

        private static string Driver(string prefix, float score01)
        {
            return score01 >= 0.7f ? $"{prefix}_strong" : score01 <= 0.35f ? $"{prefix}_weak" : $"{prefix}_moderate";
        }

        private static string DescribeCondition(float conditionScore, ParcelAssetKind assetKind, bool hasExplicitCondition)
        {
            if (assetKind == ParcelAssetKind.Land)
            {
                return "Unimproved land; condition drag is not currently in play.";
            }

            if (!hasExplicitCondition)
            {
                return conditionScore >= 0.7f
                    ? "Condition read looks favorable, but it is still partly inferred rather than surveyed."
                    : conditionScore <= 0.35f
                        ? "Condition read looks weak, though no explicit survey-grade condition input is authored yet."
                        : "Condition read is provisional because explicit improvement-condition data is missing.";
            }

            return conditionScore >= 0.7f
                ? "Improvements appear sound enough to support value without a major near-term drag."
                : conditionScore <= 0.35f
                    ? "Condition looks poor enough to drag value until repairs or refurbishment are addressed."
                    : "Condition is workable but likely carries some repair or upkeep drag.";
        }

        private static string DescribeConstraints(float constraintScore, bool hasExplicitConstraints)
        {
            if (!hasExplicitConstraints)
            {
                return constraintScore >= 0.7f
                    ? "Constraint read looks light, but some site burden is still inferred rather than explicitly surveyed."
                    : constraintScore <= 0.35f
                        ? "Constraint burden looks material, though one or more limiting factors are still provisional."
                        : "Constraint read is provisional because explicit site-burden data is incomplete.";
            }

            return constraintScore >= 0.7f
                ? "Constraints look manageable for present use or sensible conversion."
                : constraintScore <= 0.35f
                    ? "Constraints look heavy enough to materially narrow reuse or development options."
                    : "Constraints are real but not necessarily deal-breaking if the intended use fits the site.";
        }

        private static string DescribeAskingPrice(int askingPriceCents, int estimatedValueCents, int bandLowCents, int bandHighCents, float confidence01)
        {
            if (askingPriceCents <= 0)
            {
                return string.Empty;
            }

            if (askingPriceCents < bandLowCents)
            {
                return confidence01 >= 0.75f
                    ? "Asking price sits below the current valuation band; this may reflect urgency, noise, or hidden baggage."
                    : "Asking price sits below the current band, though the valuation read is still confidence-limited.";
            }

            if (askingPriceCents <= bandHighCents)
            {
                return "Asking price sits inside the current valuation band and reads broadly plausible.";
            }

            float ratioToEstimate = estimatedValueCents <= 0 ? 0f : askingPriceCents / (float)estimatedValueCents;
            if (ratioToEstimate <= 1.1f)
            {
                return "Asking price is above the estimate, but still close enough to read like a normal seller stretch.";
            }

            return confidence01 >= 0.75f
                ? "Asking price sits materially above the current valuation read and likely needs stronger justification."
                : "Asking price sits materially above the current valuation read, though confidence is not high enough to treat that gap as conclusive.";
        }

        private static string DescribeLocation(float score01)
        {
            return score01 >= 0.7f ? "Strong access and district position." : score01 <= 0.35f ? "Weak access or peripheral position." : "Serviceable location with mixed tradeoffs.";
        }

        private static string DescribeTraffic(float score01, bool hasExplicitTrafficExposure)
        {
            if (!hasExplicitTrafficExposure)
            {
                return score01 >= 0.7f
                    ? "Traffic read looks strong, but it is still partly inferred from frontage and nearby activity."
                    : score01 <= 0.35f
                        ? "Traffic read looks weak, but direct exposure data is still missing."
                        : "Traffic read is provisional because direct exposure data is missing.";
            }

            return score01 >= 0.7f ? "High exposure for customer flow." : score01 <= 0.35f ? "Limited traffic exposure." : "Moderate traffic exposure.";
        }

        private static string DescribeCommercial(float score01, bool hasExplicitCommercialFit)
        {
            if (!hasExplicitCommercialFit)
            {
                return score01 >= 0.7f
                    ? "Commercial upside looks strong, but some fit is still inferred rather than explicitly authored."
                    : score01 <= 0.35f
                        ? "Commercial upside looks weak, though the fit read is still provisional."
                        : "Commercial fit is only partly authored, so this upside read is provisional.";
            }

            return score01 >= 0.7f ? "Strong commercial reuse potential." : score01 <= 0.35f ? "Limited commercial upside." : "Workable commercial potential.";
        }

        private static string DescribeReadiness(float score01, bool hasExplicitBuildableArea, bool hasExplicitReadiness, bool hasExplicitConstraints)
        {
            bool provisional = !hasExplicitBuildableArea || !hasExplicitReadiness || !hasExplicitConstraints;
            if (provisional)
            {
                return score01 >= 0.7f
                    ? "Readiness looks favorable, but part of the site-read is still provisional."
                    : score01 <= 0.35f
                        ? "Readiness looks constrained, though one or more site inputs are still provisional."
                        : "Readiness is only partly authored, so this development read is provisional.";
            }

            return score01 >= 0.7f ? "Ready for development with few obvious constraints." : score01 <= 0.35f ? "Development would face material constraints." : "Developable with some constraints.";
        }
    }
}
