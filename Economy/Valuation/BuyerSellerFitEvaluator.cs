using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Valuation
{
    public sealed class BuyerSellerFitEvaluator
    {
        private const float UnknownBuyerReputationDefault01 = 0.45f;
        private const float UnknownBuyerLocalTrustDefault01 = 0.4f;
        private const float UnknownBuyerDealReliabilityDefault01 = 0.45f;
        private const float UnknownBuyerStrategicThreatDefault01 = 0.2f;
        private const float UnknownBuyerCommunityFitDefault01 = 0.5f;
        private const float UnknownBuyerCashCertaintyDefault01 = 0.45f;
        private const float UnknownBuyerRelationshipDefault01 = 0.35f;

        public BuyerSellerFitResult Evaluate(BuyerOfferProfile buyer, SellerProfile seller, SellerPressureResult sellerPressure)
        {
            buyer = buyer.Sanitized();
            seller = seller.Sanitized();

            float relationshipSensitivity = sellerPressure.RelationshipSensitivity01;
            float reputation = ResolveOptional01(buyer.reputation01, UnknownBuyerReputationDefault01);
            float localTrust = ResolveOptional01(buyer.localTrust01, UnknownBuyerLocalTrustDefault01);
            float priorDealReliability = ResolveOptional01(buyer.priorDealReliability01, UnknownBuyerDealReliabilityDefault01);
            float communityFit = ResolveOptional01(buyer.communityFit01, UnknownBuyerCommunityFitDefault01);
            float relationshipWithSeller = ResolveOptional01(buyer.relationshipWithSeller01, UnknownBuyerRelationshipDefault01);
            float cashCertainty = ResolveOptional01(buyer.cashCertainty01, UnknownBuyerCashCertaintyDefault01);
            float strategicThreat = ResolveOptional01(buyer.strategicThreat01, UnknownBuyerStrategicThreatDefault01);

            // Keep trust, continuity fit, and closing certainty distinct so sparse or mixed buyer
            // profiles do not collapse into one muddy signal. The seller's relationship sensitivity
            // decides how much interpersonal trust outweighs pure closing confidence.
            float trustScore = CalculateTrustScore(reputation, localTrust, priorDealReliability, communityFit, relationshipWithSeller);
            float certaintyScore = CalculateCertaintyScore(cashCertainty, priorDealReliability, buyer.buyerKind);
            float continuityScore = CalculateContinuityScore(localTrust, communityFit, relationshipWithSeller, buyer.buyerKind, seller.motive);
            float threatPenalty = CalculateThreatPenalty(strategicThreat, relationshipSensitivity, seller.motive, buyer.buyerKind);
            float kindModifier = GetBuyerKindModifier(buyer.buyerKind, seller.motive);

            float relationshipWeightedScore = Mathf.Lerp(certaintyScore, trustScore, relationshipSensitivity);
            float continuityWeight = GetContinuityWeight(seller.motive);
            float score = Mathf.Clamp01(
                relationshipWeightedScore * 0.82f
                + continuityScore * continuityWeight
                + kindModifier
                - threatPenalty);

            List<OfferEvaluationReasonCode> reasons = new List<OfferEvaluationReasonCode>();
            bool trustStrong = trustScore >= 0.72f;
            bool certaintyWeak = certaintyScore <= 0.3f;
            bool threatMaterial = threatPenalty >= 0.32f;
            bool overallWeak = score <= 0.32f;

            if (score >= 0.72f)
            {
                reasons.Add(OfferEvaluationReasonCode.StrongBuyerFit);
            }

            if (trustStrong && relationshipSensitivity >= 0.45f)
            {
                reasons.Add(OfferEvaluationReasonCode.TrustedBuyer);
            }

            // Do not mark a buyer as distrusted merely because liquidity/certainty looks thin.
            // Reserve this reason for actual trust/threat problems or clearly weak overall fit.
            if ((overallWeak && !certaintyWeak) || threatMaterial || trustScore <= 0.3f)
            {
                reasons.Add(OfferEvaluationReasonCode.BuyerDistrusted);
            }

            float inputCoverage = CalculateInputCoverage01(buyer);
            BuyerFitBand fitBand = DetermineFitBand(score, inputCoverage);
            BuyerFitLeadSignal primarySupportSignal = DeterminePrimarySupportSignal(
                fitBand,
                trustScore,
                certaintyScore,
                continuityScore,
                threatPenalty,
                relationshipSensitivity);
            BuyerFitPrimaryConcern primaryConcern = DeterminePrimaryConcern(
                score,
                trustScore,
                certaintyScore,
                continuityScore,
                threatPenalty,
                inputCoverage,
                relationshipSensitivity);
            AddPrimaryConcernReason(reasons, primaryConcern);

            return new BuyerSellerFitResult(
                score,
                trustScore,
                certaintyScore,
                continuityScore,
                threatPenalty,
                inputCoverage,
                fitBand,
                primarySupportSignal,
                primaryConcern,
                DescribeOverallFit(fitBand, primarySupportSignal, primaryConcern),
                DescribeTrust(trustScore, relationshipSensitivity),
                DescribeCertainty(certaintyScore),
                DescribeContinuity(continuityScore, seller.motive),
                DescribeThreat(threatPenalty, buyer.buyerKind),
                reasons);
        }

        private static void AddPrimaryConcernReason(List<OfferEvaluationReasonCode> reasons, BuyerFitPrimaryConcern primaryConcern)
        {
            switch (primaryConcern)
            {
                case BuyerFitPrimaryConcern.LowClosingCertainty:
                    reasons.Add(OfferEvaluationReasonCode.BuyerClosingCertaintyConcern);
                    break;
                case BuyerFitPrimaryConcern.WeakContinuity:
                    reasons.Add(OfferEvaluationReasonCode.BuyerContinuityConcern);
                    break;
                case BuyerFitPrimaryConcern.StrategicThreat:
                    reasons.Add(OfferEvaluationReasonCode.BuyerStrategicThreatConcern);
                    break;
                case BuyerFitPrimaryConcern.ThinProfile:
                    reasons.Add(OfferEvaluationReasonCode.BuyerProfileThinConcern);
                    break;
            }
        }

        private static BuyerFitBand DetermineFitBand(float score, float inputCoverage)
        {
            if (score >= 0.72f)
            {
                return BuyerFitBand.Strong;
            }

            if (score >= 0.5f)
            {
                return BuyerFitBand.Workable;
            }

            // Thin profiles around the middle should read as caution rather than as a hard weak fit.
            if (score >= 0.32f || inputCoverage <= 0.45f)
            {
                return BuyerFitBand.Cautious;
            }

            return BuyerFitBand.VeryWeak;
        }

        private static BuyerFitLeadSignal DeterminePrimarySupportSignal(
            BuyerFitBand fitBand,
            float trustScore,
            float certaintyScore,
            float continuityScore,
            float threatPenalty,
            float relationshipSensitivity)
        {
            if (fitBand == BuyerFitBand.VeryWeak)
            {
                return BuyerFitLeadSignal.None;
            }

            float weightedTrust = Mathf.Lerp(trustScore * 0.8f, trustScore, relationshipSensitivity);
            float weightedCertainty = Mathf.Lerp(certaintyScore, certaintyScore * 0.88f, relationshipSensitivity);
            float weightedContinuity = continuityScore * Mathf.Lerp(0.72f, 0.96f, relationshipSensitivity);

            float bestScore = weightedTrust;
            BuyerFitLeadSignal bestSignal = BuyerFitLeadSignal.Trust;

            if (weightedCertainty > bestScore)
            {
                bestScore = weightedCertainty;
                bestSignal = BuyerFitLeadSignal.ClosingCertainty;
            }

            if (weightedContinuity > bestScore)
            {
                bestScore = weightedContinuity;
                bestSignal = BuyerFitLeadSignal.Continuity;
            }

            if (bestScore < 0.52f || threatPenalty >= 0.38f)
            {
                return BuyerFitLeadSignal.None;
            }

            return bestSignal;
        }

        private static BuyerFitPrimaryConcern DeterminePrimaryConcern(
            float score,
            float trustScore,
            float certaintyScore,
            float continuityScore,
            float threatPenalty,
            float inputCoverage,
            float relationshipSensitivity)
        {
            if (threatPenalty >= 0.32f)
            {
                return BuyerFitPrimaryConcern.StrategicThreat;
            }

            if (trustScore <= Mathf.Lerp(0.26f, 0.38f, relationshipSensitivity))
            {
                return BuyerFitPrimaryConcern.LowTrust;
            }

            if (certaintyScore <= 0.3f)
            {
                return BuyerFitPrimaryConcern.LowClosingCertainty;
            }

            if (continuityScore <= 0.3f && relationshipSensitivity >= 0.45f)
            {
                return BuyerFitPrimaryConcern.WeakContinuity;
            }

            if (inputCoverage <= 0.45f && score <= 0.6f)
            {
                return BuyerFitPrimaryConcern.ThinProfile;
            }

            return BuyerFitPrimaryConcern.None;
        }

        private static string DescribeOverallFit(BuyerFitBand fitBand, BuyerFitLeadSignal primarySupportSignal, BuyerFitPrimaryConcern primaryConcern)
        {
            if (primaryConcern == BuyerFitPrimaryConcern.ThinProfile)
            {
                return fitBand switch
                {
                    BuyerFitBand.Strong => "strong fit, though the buyer profile is still thinly authored",
                    BuyerFitBand.Workable => "workable fit under thin buyer detail",
                    BuyerFitBand.Cautious => "cautious fit under thin buyer detail",
                    _ => "uncertain fit because the buyer profile is thin"
                };
            }

            if (primaryConcern != BuyerFitPrimaryConcern.None)
            {
                return primaryConcern switch
                {
                    BuyerFitPrimaryConcern.LowTrust => fitBand == BuyerFitBand.VeryWeak
                        ? "weak fit because seller trust is low"
                        : "cautious fit with trust concerns",
                    BuyerFitPrimaryConcern.LowClosingCertainty => fitBand == BuyerFitBand.VeryWeak
                        ? "weak fit because closing certainty looks thin"
                        : "workable fit with closing-certainty concerns",
                    BuyerFitPrimaryConcern.WeakContinuity => "cautious fit because continuity confidence is limited",
                    BuyerFitPrimaryConcern.StrategicThreat => "cautious fit because the buyer feels strategically threatening",
                    _ => string.Empty
                };
            }

            return primarySupportSignal switch
            {
                BuyerFitLeadSignal.Trust => fitBand == BuyerFitBand.Strong
                    ? "strong fit driven by seller trust"
                    : "workable fit driven by seller trust",
                BuyerFitLeadSignal.ClosingCertainty => fitBand == BuyerFitBand.Strong
                    ? "strong fit driven by closing certainty"
                    : "workable fit driven by closing certainty",
                BuyerFitLeadSignal.Continuity => fitBand == BuyerFitBand.Strong
                    ? "strong fit driven by continuity confidence"
                    : "workable fit driven by continuity confidence",
                _ => fitBand switch
                {
                    BuyerFitBand.Strong => "strong buyer fit",
                    BuyerFitBand.Workable => "workable buyer fit",
                    BuyerFitBand.Cautious => "cautious buyer fit",
                    _ => "weak buyer fit"
                }
            };
        }

        private static string DescribeTrust(float trustScore, float relationshipSensitivity)
        {
            if (trustScore >= 0.74f)
            {
                return relationshipSensitivity >= 0.45f ? "high and materially relevant to this seller" : "high";
            }

            if (trustScore >= 0.5f)
            {
                return "mixed but serviceable";
            }

            if (trustScore >= 0.32f)
            {
                return "thin";
            }

            return "low";
        }

        private static string DescribeCertainty(float certaintyScore)
        {
            if (certaintyScore >= 0.72f)
            {
                return "high";
            }

            if (certaintyScore >= 0.5f)
            {
                return "workable";
            }

            if (certaintyScore >= 0.32f)
            {
                return "uncertain";
            }

            return "weak";
        }

        private static string DescribeContinuity(float continuityScore, SellerMotive sellerMotive)
        {
            if (continuityScore >= 0.72f)
            {
                return sellerMotive == SellerMotive.Retirement || sellerMotive == SellerMotive.OwnerOperatorAttachment
                    ? "strong for this seller's continuity concerns"
                    : "strong";
            }

            if (continuityScore >= 0.5f)
            {
                return "workable";
            }

            if (continuityScore >= 0.32f)
            {
                return "limited";
            }

            return "weak";
        }

        private static string DescribeThreat(float threatPenalty, BuyerKind buyerKind)
        {
            if (threatPenalty >= 0.42f)
            {
                return buyerKind == BuyerKind.Rival ? "high rival threat" : "high";
            }

            if (threatPenalty >= 0.24f)
            {
                return buyerKind == BuyerKind.Rival ? "present rival threat" : "present";
            }

            return "low";
        }

        private static float CalculateTrustScore(
            float reputation,
            float localTrust,
            float priorDealReliability,
            float communityFit,
            float relationshipWithSeller)
        {
            return Mathf.Clamp01(
                reputation * 0.25f
                + localTrust * 0.24f
                + priorDealReliability * 0.2f
                + communityFit * 0.13f
                + relationshipWithSeller * 0.18f);
        }

        private static float CalculateCertaintyScore(float cashCertainty, float priorDealReliability, BuyerKind buyerKind)
        {
            float institutionalAdjustment = buyerKind == BuyerKind.Civic ? 0.03f : 0f;
            return Mathf.Clamp01(cashCertainty * 0.68f + priorDealReliability * 0.32f + institutionalAdjustment);
        }

        private static float CalculateContinuityScore(
            float localTrust,
            float communityFit,
            float relationshipWithSeller,
            BuyerKind buyerKind,
            SellerMotive sellerMotive)
        {
            float localContinuityBias = sellerMotive == SellerMotive.Retirement
                || sellerMotive == SellerMotive.OwnerOperatorAttachment
                || sellerMotive == SellerMotive.EstateSale
                ? 0.12f
                : 0f;
            float buyerContinuityBias = buyerKind == BuyerKind.LocalOperator || buyerKind == BuyerKind.Player
                ? 0.06f
                : buyerKind == BuyerKind.Civic ? 0.03f : 0f;

            return Mathf.Clamp01(
                localTrust * 0.32f
                + communityFit * 0.28f
                + relationshipWithSeller * 0.3f
                + localContinuityBias
                + buyerContinuityBias);
        }

        private static float CalculateThreatPenalty(float strategicThreat, float relationshipSensitivity, SellerMotive sellerMotive, BuyerKind buyerKind)
        {
            float motiveThreatWeight = sellerMotive switch
            {
                SellerMotive.OwnerOperatorAttachment => 0.72f,
                SellerMotive.StrategicHoldout => 0.68f,
                SellerMotive.Retirement => 0.44f,
                SellerMotive.Holding => 0.42f,
                _ => 0.28f
            };
            float buyerThreatBias = buyerKind == BuyerKind.Rival ? 0.08f : 0f;
            return Mathf.Clamp01(strategicThreat * Mathf.Lerp(0.24f, motiveThreatWeight, relationshipSensitivity) + buyerThreatBias);
        }

        private static float GetContinuityWeight(SellerMotive sellerMotive)
        {
            return sellerMotive switch
            {
                SellerMotive.OwnerOperatorAttachment => 0.14f,
                SellerMotive.Retirement => 0.12f,
                SellerMotive.EstateSale => 0.09f,
                SellerMotive.StrategicHoldout => 0.06f,
                _ => 0.04f
            };
        }

        private static float CalculateInputCoverage01(BuyerOfferProfile buyer)
        {
            // Coverage is an inspection signal only. It helps the rest of the lane tell the
            // difference between a weak buyer and a thinly-authored buyer profile.
            float coverage = 0.08f;
            coverage += buyer.HasExplicitReputation ? 0.16f : 0f;
            coverage += buyer.HasExplicitLocalTrust ? 0.14f : 0f;
            coverage += buyer.HasExplicitPriorDealReliability ? 0.16f : 0f;
            coverage += buyer.HasExplicitStrategicThreat ? 0.12f : 0f;
            coverage += buyer.HasExplicitCommunityFit ? 0.12f : 0f;
            coverage += buyer.HasExplicitCashCertainty ? 0.14f : 0f;
            coverage += buyer.HasExplicitRelationshipWithSeller ? 0.08f : 0f;
            return Mathf.Clamp01(coverage);
        }

        private static float ResolveOptional01(float value, float defaultValue)
        {
            return value < 0f ? Mathf.Clamp01(defaultValue) : Mathf.Clamp01(value);
        }

        private static float GetBuyerKindModifier(BuyerKind buyerKind, SellerMotive sellerMotive)
        {
            return buyerKind switch
            {
                BuyerKind.LocalOperator => sellerMotive == SellerMotive.Retirement || sellerMotive == SellerMotive.OwnerOperatorAttachment ? 0.1f : 0.05f,
                BuyerKind.Civic => sellerMotive == SellerMotive.StrategicHoldout || sellerMotive == SellerMotive.Holding ? 0.06f : 0.03f,
                BuyerKind.OutsideInvestor => sellerMotive == SellerMotive.OwnerOperatorAttachment || sellerMotive == SellerMotive.Retirement ? -0.08f : -0.03f,
                BuyerKind.Rival => -0.18f,
                BuyerKind.Player => 0.02f,
                _ => 0f
            };
        }
    }
}
