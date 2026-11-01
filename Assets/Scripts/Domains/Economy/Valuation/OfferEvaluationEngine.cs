using System;
using System.Collections.Generic;
using LandLedgers.Economy.DealTerms;
using UnityEngine;

namespace LandLedgers.Economy.Valuation
{
    public sealed class OfferEvaluationEngine
    {
        private const float UnknownClosingSpeedDefault01 = 0.45f;
        private const float UnknownContingencyBurdenDefault01 = 0.4f;
        private const float UnknownInspectionStrictnessDefault01 = 0.5f;
        private const float UnknownNonPriceConcessionsDefault01 = 0.2f;
        private const float UnknownBuyerReputationDefault01 = 0.45f;
        private const float UnknownBuyerLocalTrustDefault01 = 0.4f;
        private const float UnknownBuyerDealReliabilityDefault01 = 0.45f;
        private const float UnknownBuyerCommunityFitDefault01 = 0.5f;
        private const float UnknownBuyerCashCertaintyDefault01 = 0.45f;
        private const int MaxReportedReasons = 5;

        private readonly SellerPressureEvaluator sellerPressureEvaluator;
        private readonly BuyerSellerFitEvaluator buyerSellerFitEvaluator;

        public OfferEvaluationEngine()
            : this(new SellerPressureEvaluator(), new BuyerSellerFitEvaluator())
        {
        }

        public OfferEvaluationEngine(SellerPressureEvaluator sellerPressureEvaluator, BuyerSellerFitEvaluator buyerSellerFitEvaluator)
        {
            this.sellerPressureEvaluator = sellerPressureEvaluator ?? throw new ArgumentNullException(nameof(sellerPressureEvaluator));
            this.buyerSellerFitEvaluator = buyerSellerFitEvaluator ?? throw new ArgumentNullException(nameof(buyerSellerFitEvaluator));
        }

        public OfferEvaluationResult Evaluate(ParcelValuationResult valuation, SellerProfile seller, BuyerOfferProfile buyer, AcquisitionOfferTerms terms)
        {
            if (valuation == null)
            {
                throw new ArgumentNullException(nameof(valuation));
            }

            seller = seller.Sanitized();
            buyer = buyer.Sanitized();
            terms = terms.Sanitized();

            SellerPressureResult sellerPressure = sellerPressureEvaluator.Evaluate(seller);
            BuyerSellerFitResult buyerFit = buyerSellerFitEvaluator.Evaluate(buyer, seller, sellerPressure);
            return EvaluateInternal(valuation, seller, sellerPressure, buyerFit, terms, buyer);
        }

        public OfferEvaluationResult Evaluate(
            ParcelValuationResult valuation,
            SellerProfile seller,
            SellerPressureResult sellerPressure,
            BuyerSellerFitResult buyerFit,
            AcquisitionOfferTerms terms)
        {
            seller = seller.Sanitized();
            terms = terms.Sanitized();
            return EvaluateInternal(valuation, seller, sellerPressure, buyerFit, terms, null);
        }

        private OfferEvaluationResult EvaluateInternal(
            ParcelValuationResult valuation,
            SellerProfile seller,
            SellerPressureResult sellerPressure,
            BuyerSellerFitResult buyerFit,
            AcquisitionOfferTerms terms,
            BuyerOfferProfile? buyerProfile)
        {
            if (valuation == null)
            {
                throw new ArgumentNullException(nameof(valuation));
            }

            seller = seller.Sanitized();
            terms = terms.Sanitized();
            BuyerOfferProfile? sanitizedBuyer = buyerProfile.HasValue ? buyerProfile.Value.Sanitized() : (BuyerOfferProfile?)null;

            float minimumRatio = seller.HasExplicitMinimumAcceptableRatio
                ? Mathf.Clamp(seller.minimumAcceptableRatio, 0.4f, 1.8f)
                : 1f;
            int threshold = Mathf.Max(1, Mathf.RoundToInt(valuation.EstimatedValueCents * minimumRatio * sellerPressure.ReservationValueMultiplier));
            float priceStrength = terms.offerPriceCents / (float)threshold;

            bool hasExpandedTermEvaluation = TryEvaluateExplicitExpandedTerms(
                terms,
                seller,
                sellerPressure,
                buyerFit,
                valuation.EstimatedValueCents,
                sanitizedBuyer,
                out ExpandedDealTermEvaluationResult expandedTermEvaluation);

            float termsFit = CalculateTermsFit(terms, sellerPressure, seller, hasExpandedTermEvaluation, expandedTermEvaluation);
            float sellerValueScore = Mathf.Clamp01(
                Mathf.Clamp01(priceStrength) * 0.55f
                + buyerFit.Score01 * 0.25f
                + termsFit * 0.2f);

            List<OfferEvaluationReasonCode> reasons = new List<OfferEvaluationReasonCode>();
            AddReasons(reasons, sellerPressure.ReasonCodes);
            AddReasons(reasons, buyerFit.ReasonCodes);
            AddTermReasons(reasons, terms, termsFit, hasExpandedTermEvaluation, expandedTermEvaluation);

            bool willingToSell = seller.listedForSale || sellerPressure.WillingnessToSell01 >= 0.38f || terms.offerPriceCents >= Mathf.RoundToInt(threshold * 1.18f);
            float buyerAcceptabilityScore = CalculateBuyerAcceptabilityScore(buyerFit, sellerPressure);
            bool buyerAcceptable = buyerAcceptabilityScore >= Mathf.Lerp(0.22f, 0.48f, sellerPressure.RelationshipSensitivity01);
            bool buyerDistrustConcern = ShouldFlagBuyerDistrust(buyerFit, buyerAcceptable);
            bool termsAcceptable = termsFit >= 0.46f;
            bool priceMeetsThreshold = terms.offerPriceCents >= threshold;
            bool betterUsePotential = valuation.CommercialPotentialScore01 >= 0.78f
                && priceStrength < 1.08f
                && (seller.motive == SellerMotive.Holding || seller.motive == SellerMotive.StrategicHoldout);

            if (priceMeetsThreshold)
            {
                reasons.Add(OfferEvaluationReasonCode.PriceMeetsThreshold);
            }
            else
            {
                reasons.Add(OfferEvaluationReasonCode.TooLow);
            }

            if (!willingToSell)
            {
                reasons.Add(OfferEvaluationReasonCode.NotReadyToSell);
            }

            // Keep distrust surfaces aligned with buyer-fit detail. A thinly-authored buyer with
            // shaky certainty should not be reported the same way as a buyer the seller actively
            // distrusts or sees as strategically threatening.
            if (buyerDistrustConcern)
            {
                reasons.Add(OfferEvaluationReasonCode.BuyerDistrusted);
            }

            if (!termsAcceptable)
            {
                reasons.Add(OfferEvaluationReasonCode.TermsTooRisky);
            }

            if (betterUsePotential)
            {
                reasons.Add(OfferEvaluationReasonCode.BetterUsePotential);
            }

            if (priceMeetsThreshold && willingToSell && buyerAcceptable && termsAcceptable)
            {
                return new OfferEvaluationResult(
                    OfferDecision.Accept,
                    threshold,
                    0,
                    sellerValueScore,
                    priceStrength,
                    buyerFit.Score01,
                    termsFit,
                    buyerFit.FitBand,
                    buyerFit.PrimarySupportSignal,
                    buyerFit.PrimaryConcern,
                    ResolveBuyerSummary(buyerFit),
                    DescribePricing(valuation, terms.offerPriceCents, threshold, priceMeetsThreshold),
                    DescribeTerms(termsFit, hasExpandedTermEvaluation, expandedTermEvaluation),
                    DescribeDecision(OfferDecision.Accept, willingToSell, buyerFit, buyerDistrustConcern, termsAcceptable, priceMeetsThreshold, betterUsePotential),
                    FinalizeReasons(
                        OfferDecision.Accept,
                        priceMeetsThreshold,
                        willingToSell,
                        buyerDistrustConcern,
                        termsAcceptable,
                        reasons));
            }

            // This lane can only return a counter price, not a fully revised term sheet.
            // Keep the counter path reserved for price-only negotiations that are otherwise
            // workable. When trust or terms are the main blocker, reject clearly instead of
            // pretending a price bump alone would solve the deal.
            float counterFloorRatio = CalculateCounterFloorRatio(seller, sellerPressure, betterUsePotential);
            bool counterViable = willingToSell
                && !priceMeetsThreshold
                && buyerAcceptable
                && termsAcceptable
                && terms.offerPriceCents >= Mathf.RoundToInt(threshold * counterFloorRatio);

            if (counterViable)
            {
                int counterPrice = CalculateCounterPrice(terms.offerPriceCents, threshold, valuation, seller, sellerPressure, betterUsePotential);
                reasons.Add(OfferEvaluationReasonCode.CounterPriceRecommended);
                return new OfferEvaluationResult(
                    OfferDecision.Counter,
                    threshold,
                    counterPrice,
                    sellerValueScore,
                    priceStrength,
                    buyerFit.Score01,
                    termsFit,
                    buyerFit.FitBand,
                    buyerFit.PrimarySupportSignal,
                    buyerFit.PrimaryConcern,
                    ResolveBuyerSummary(buyerFit),
                    DescribePricing(valuation, terms.offerPriceCents, threshold, priceMeetsThreshold),
                    DescribeTerms(termsFit, hasExpandedTermEvaluation, expandedTermEvaluation),
                    DescribeDecision(OfferDecision.Counter, willingToSell, buyerFit, buyerDistrustConcern, termsAcceptable, priceMeetsThreshold, betterUsePotential),
                    FinalizeReasons(
                        OfferDecision.Counter,
                        priceMeetsThreshold,
                        willingToSell,
                        buyerDistrustConcern,
                        termsAcceptable,
                        reasons));
            }

            return new OfferEvaluationResult(
                OfferDecision.Reject,
                threshold,
                0,
                sellerValueScore,
                priceStrength,
                buyerFit.Score01,
                termsFit,
                buyerFit.FitBand,
                buyerFit.PrimarySupportSignal,
                buyerFit.PrimaryConcern,
                ResolveBuyerSummary(buyerFit),
                DescribePricing(valuation, terms.offerPriceCents, threshold, priceMeetsThreshold),
                DescribeTerms(termsFit, hasExpandedTermEvaluation, expandedTermEvaluation),
                DescribeDecision(OfferDecision.Reject, willingToSell, buyerFit, buyerDistrustConcern, termsAcceptable, priceMeetsThreshold, betterUsePotential),
                FinalizeReasons(
                    OfferDecision.Reject,
                    priceMeetsThreshold,
                    willingToSell,
                    buyerDistrustConcern,
                    termsAcceptable,
                    reasons));
        }

        private static bool TryEvaluateExplicitExpandedTerms(
            AcquisitionOfferTerms terms,
            SellerProfile seller,
            SellerPressureResult sellerPressure,
            BuyerSellerFitResult buyerFit,
            int estimatedAssetValueCents,
            BuyerOfferProfile? buyerProfile,
            out ExpandedDealTermEvaluationResult result)
        {
            if (!ExpandedDealTermAdapters.HasExplicitExpandedTerms(terms))
            {
                result = default;
                return false;
            }

            int offerPriceCents = Mathf.Max(0, terms.offerPriceCents);
            float buyerReputation01 = ResolveBuyerReputation01(buyerProfile, buyerFit);
            float buyerCashCertainty01 = ResolveBuyerCashCertainty01(buyerProfile, buyerFit);
            ExpandedDealTermEvaluationContext context = new ExpandedDealTermEvaluationContext
            {
                purchasePriceCents = offerPriceCents,
                estimatedAssetValueCents = estimatedAssetValueCents,
                buyerAvailableCashCents = ResolveBuyerAvailableCashCents(terms, buyerProfile, buyerCashCertainty01),
                sellerPressure01 = sellerPressure.Pressure01,
                sellerCashNeed01 = ResolveOptional01(seller.cashNeed01, 0.3f),
                sellerAttachment01 = ResolveOptional01(seller.attachment01, 0.45f),
                sellerRelationshipSensitivity01 = sellerPressure.RelationshipSensitivity01,
                buyerReputation01 = buyerReputation01,
                buyerCashCertainty01 = buyerCashCertainty01
            };

            result = new ExpandedDealTermEvaluator().Evaluate(terms.expandedDealTerms, context);
            return true;
        }

        private static float CalculateCounterFloorRatio(
            SellerProfile seller,
            SellerPressureResult sellerPressure,
            bool betterUsePotential)
        {
            float flexibilityDiscount = sellerPressure.CounterofferFlexibility01 * 0.16f;
            float pressureDiscount = sellerPressure.Pressure01 * 0.1f;
            float motivePremium = seller.motive switch
            {
                SellerMotive.StrategicHoldout => 0.07f,
                SellerMotive.OwnerOperatorAttachment => 0.06f,
                SellerMotive.Holding => 0.05f,
                SellerMotive.Retirement => 0.03f,
                _ => 0f
            };
            float betterUsePremium = betterUsePotential ? 0.02f : 0f;

            return Mathf.Clamp(0.9f - flexibilityDiscount - pressureDiscount + motivePremium + betterUsePremium, 0.72f, 0.94f);
        }

        private static int CalculateCounterPrice(
            int offeredPriceCents,
            int thresholdCents,
            ParcelValuationResult valuation,
            SellerProfile seller,
            SellerPressureResult sellerPressure,
            bool betterUsePotential)
        {
            float motivePremium = seller.motive switch
            {
                SellerMotive.StrategicHoldout => 0.07f,
                SellerMotive.OwnerOperatorAttachment => 0.06f,
                SellerMotive.Holding => 0.05f,
                SellerMotive.Retirement => 0.03f,
                SellerMotive.EstateSale => 0.01f,
                _ => 0f
            };
            float attachmentPremium = ResolveOptional01(seller.attachment01, 0.45f) * 0.02f;
            float betterUsePremium = betterUsePotential ? 0.02f : 0f;
            float aspirationalRatio = 1f + motivePremium + attachmentPremium + betterUsePremium;

            int counterCeiling = Mathf.Max(thresholdCents, valuation.ValuationBandHighCents);
            int aspirationalAnchor = Mathf.Max(thresholdCents, Mathf.RoundToInt(thresholdCents * aspirationalRatio));
            aspirationalAnchor = Mathf.Min(counterCeiling, aspirationalAnchor);

            float concession01 = sellerPressure.Pressure01 * 0.35f + sellerPressure.CounterofferFlexibility01 * 0.4f;
            int softenedAnchor = Mathf.RoundToInt(Mathf.Lerp(aspirationalAnchor, thresholdCents, Mathf.Clamp01(concession01)));

            int minimumCounter = Mathf.Max(offeredPriceCents + 1, thresholdCents);
            return Mathf.Clamp(softenedAnchor, minimumCounter, counterCeiling);
        }

        private static float ResolveBuyerReputation01(BuyerOfferProfile? buyerProfile, BuyerSellerFitResult buyerFit)
        {
            if (!buyerProfile.HasValue)
            {
                return Mathf.Clamp01(buyerFit.HasDetailedSignals ? buyerFit.TrustScore01 : buyerFit.Score01);
            }

            BuyerOfferProfile buyer = buyerProfile.Value.Sanitized();
            return Mathf.Clamp01(
                ResolveOptional01(buyer.reputation01, UnknownBuyerReputationDefault01) * 0.45f
                + ResolveOptional01(buyer.localTrust01, UnknownBuyerLocalTrustDefault01) * 0.25f
                + ResolveOptional01(buyer.priorDealReliability01, UnknownBuyerDealReliabilityDefault01) * 0.2f
                + ResolveOptional01(buyer.communityFit01, UnknownBuyerCommunityFitDefault01) * 0.1f);
        }

        private static float ResolveBuyerCashCertainty01(BuyerOfferProfile? buyerProfile, BuyerSellerFitResult buyerFit)
        {
            if (!buyerProfile.HasValue)
            {
                return Mathf.Clamp01(buyerFit.HasDetailedSignals ? buyerFit.CertaintyScore01 : buyerFit.Score01);
            }

            BuyerOfferProfile buyer = buyerProfile.Value.Sanitized();
            return Mathf.Clamp01(
                ResolveOptional01(buyer.cashCertainty01, UnknownBuyerCashCertaintyDefault01) * 0.75f
                + ResolveOptional01(buyer.priorDealReliability01, UnknownBuyerDealReliabilityDefault01) * 0.25f);
        }

        private static int ResolveBuyerAvailableCashCents(
            AcquisitionOfferTerms terms,
            BuyerOfferProfile? buyerProfile,
            float buyerCashCertainty01)
        {
            int offerPriceCents = Mathf.Max(0, terms.offerPriceCents);
            if (offerPriceCents <= 0)
            {
                return 0;
            }

            float earnestCoverage01 = CalculateEarnestCoverage01(terms);
            float cashSignal01 = buyerCashCertainty01;
            if (buyerProfile.HasValue)
            {
                BuyerOfferProfile buyer = buyerProfile.Value.Sanitized();
                cashSignal01 = Mathf.Clamp01(
                    ResolveOptional01(buyer.cashCertainty01, UnknownBuyerCashCertaintyDefault01) * 0.6f
                    + ResolveOptional01(buyer.priorDealReliability01, UnknownBuyerDealReliabilityDefault01) * 0.15f
                    + earnestCoverage01 * 0.25f);
            }

            // Keep this as a conservative proxy: expanded-term evaluation needs a cash figure,
            // but this lane only has confidence and earnest signals, not a full lender/cash stack.
            float minimumCoverage01 = terms.sellerFinancingRequested ? 0.18f : 0.55f;
            float maximumCoverage01 = terms.sellerFinancingRequested ? 0.72f : 1f;
            float coverage01 = Mathf.Lerp(minimumCoverage01, maximumCoverage01, cashSignal01);
            coverage01 = Mathf.Clamp01(Mathf.Max(coverage01, earnestCoverage01 * 0.1f));
            return Mathf.RoundToInt(offerPriceCents * coverage01);
        }

        private static float CalculateEarnestCoverage01(AcquisitionOfferTerms terms)
        {
            if (terms.offerPriceCents <= 0 || terms.earnestMoneyCents <= 0)
            {
                return 0f;
            }

            float targetEarnestCents = Mathf.Max(1f, terms.offerPriceCents * 0.1f);
            return Mathf.Clamp01(terms.earnestMoneyCents / targetEarnestCents);
        }

        private static float CalculateTermsFit(
            AcquisitionOfferTerms terms,
            SellerPressureResult sellerPressure,
            SellerProfile seller,
            bool hasExpandedTermEvaluation,
            ExpandedDealTermEvaluationResult expandedTermEvaluation)
        {
            float closingSpeed = ResolveOptional01(terms.closingSpeed01, UnknownClosingSpeedDefault01);
            float contingencyBurden = ResolveOptional01(terms.contingencyBurden01, UnknownContingencyBurdenDefault01);
            float inspectionStrictness = ResolveOptional01(terms.inspectionStrictness01, UnknownInspectionStrictnessDefault01);
            float nonPriceConcessions = ResolveOptional01(terms.nonPriceConcessions01, UnknownNonPriceConcessionsDefault01);
            float earnestRatio = terms.offerPriceCents <= 0 ? 0f : Mathf.Clamp01(terms.earnestMoneyCents / (terms.offerPriceCents * 0.1f));
            float sellerFinancingPenalty = terms.sellerFinancingRequested ? Mathf.Lerp(0.12f, 0.28f, sellerPressure.Pressure01) : 0f;
            float baseFit = Mathf.Clamp01(
                0.48f
                + closingSpeed * 0.18f
                + earnestRatio * 0.1f
                + nonPriceConcessions * 0.1f
                - contingencyBurden * Mathf.Lerp(0.22f, 0.38f, sellerPressure.Pressure01)
                - inspectionStrictness * 0.12f
                - sellerFinancingPenalty
                - (seller.motive == SellerMotive.FinancialDistress && terms.sellerFinancingRequested ? 0.12f : 0f));

            if (!hasExpandedTermEvaluation)
            {
                return baseFit;
            }

            float expandedAdjustment = expandedTermEvaluation.SellerWillingnessDelta * 0.24f
                + (expandedTermEvaluation.DealAttractiveness01 - 0.5f) * 0.18f
                - expandedTermEvaluation.Risk01 * 0.14f
                - Mathf.Max(0f, expandedTermEvaluation.BuyerBurden01 - 0.35f) * 0.12f;
            return Mathf.Clamp01(baseFit + expandedAdjustment);
        }

        private static void AddTermReasons(
            List<OfferEvaluationReasonCode> reasons,
            AcquisitionOfferTerms terms,
            float termsFit,
            bool hasExpandedTermEvaluation,
            ExpandedDealTermEvaluationResult expandedTermEvaluation)
        {
            if (termsFit >= 0.72f)
            {
                reasons.Add(OfferEvaluationReasonCode.CleanTerms);
            }

            if (terms.sellerFinancingRequested)
            {
                reasons.Add(OfferEvaluationReasonCode.FinancingContingencyConcern);
            }

            if (!hasExpandedTermEvaluation)
            {
                return;
            }

            AddExpandedTermReasons(reasons, expandedTermEvaluation);
        }

        private static void AddExpandedTermReasons(List<OfferEvaluationReasonCode> reasons, ExpandedDealTermEvaluationResult expandedTermEvaluation)
        {
            if (expandedTermEvaluation.DealAttractiveness01 >= 0.62f && expandedTermEvaluation.Risk01 <= 0.35f)
            {
                reasons.Add(OfferEvaluationReasonCode.ExpandedTermsHelpful);
            }

            IReadOnlyList<ExpandedDealTermReasonCode> expandedReasons = expandedTermEvaluation.ReasonCodes ?? Array.Empty<ExpandedDealTermReasonCode>();
            for (int i = 0; i < expandedReasons.Count; i++)
            {
                switch (expandedReasons[i])
                {
                    case ExpandedDealTermReasonCode.InventoryExcluded:
                    case ExpandedDealTermReasonCode.InventoryRestartBurden:
                        reasons.Add(OfferEvaluationReasonCode.InventoryExcludedConcern);
                        break;
                    case ExpandedDealTermReasonCode.DelayedPossession:
                    case ExpandedDealTermReasonCode.DelayedPossessionBurden:
                        reasons.Add(OfferEvaluationReasonCode.DelayedPossessionConcern);
                        break;
                    case ExpandedDealTermReasonCode.RetainedLandLease:
                    case ExpandedDealTermReasonCode.RetainedLandControlRisk:
                        reasons.Add(OfferEvaluationReasonCode.RetainedLandLeaseConcern);
                        break;
                    case ExpandedDealTermReasonCode.SellerFinancingRequested:
                    case ExpandedDealTermReasonCode.SellerFinancingAddsSellerRisk:
                    case ExpandedDealTermReasonCode.SellerCashNeedConflict:
                        reasons.Add(OfferEvaluationReasonCode.ExpandedSellerFinancingConcern);
                        break;
                }
            }
        }


        private static IReadOnlyList<OfferEvaluationReasonCode> FinalizeReasons(
            OfferDecision decision,
            bool priceMeetsThreshold,
            bool willingToSell,
            bool buyerDistrustConcern,
            bool termsAcceptable,
            List<OfferEvaluationReasonCode> sourceReasons)
        {
            List<OfferEvaluationReasonCode> finalized = new List<OfferEvaluationReasonCode>(MaxReportedReasons);

            switch (decision)
            {
                case OfferDecision.Accept:
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.PriceMeetsThreshold);
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.CleanTerms);
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.TrustedBuyer);
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.StrongBuyerFit);
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.ExpandedTermsHelpful);
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.SellerUnderPressure);
                    break;

                case OfferDecision.Counter:
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.CounterPriceRecommended);
                    if (!priceMeetsThreshold)
                    {
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.TooLow);
                    }

                    if (buyerDistrustConcern)
                    {
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.BuyerDistrusted);
                    }
                    AddBuyerConcernReasons(finalized, sourceReasons);

                    if (!termsAcceptable)
                    {
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.TermsTooRisky);
                        AddTermConcernReasons(finalized, sourceReasons);
                    }

                    if (!priceMeetsThreshold && !buyerDistrustConcern && termsAcceptable)
                    {
                        // When a counter is mostly about price, keep one or two supportive reads so
                        // the surface tells the player the relationship/terms are otherwise workable.
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.CleanTerms);
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.TrustedBuyer);
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.StrongBuyerFit);
                    }

                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.BetterUsePotential);
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.SellerAttached);
                    break;

                default:
                    if (!willingToSell)
                    {
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.NotReadyToSell);
                    }

                    if (buyerDistrustConcern)
                    {
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.BuyerDistrusted);
                    }
                    AddBuyerConcernReasons(finalized, sourceReasons);

                    if (!termsAcceptable)
                    {
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.TermsTooRisky);
                        AddTermConcernReasons(finalized, sourceReasons);
                    }

                    if (!priceMeetsThreshold)
                    {
                        AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.TooLow);
                    }

                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.BetterUsePotential);
                    AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.SellerAttached);
                    break;
            }

            for (int i = 0; i < sourceReasons.Count && finalized.Count < MaxReportedReasons; i++)
            {
                AddReasonIfPresent(finalized, sourceReasons, sourceReasons[i]);
            }

            return finalized;
        }

        private static void AddTermConcernReasons(List<OfferEvaluationReasonCode> finalized, List<OfferEvaluationReasonCode> sourceReasons)
        {
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.FinancingContingencyConcern);
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.ExpandedSellerFinancingConcern);
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.DelayedPossessionConcern);
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.InventoryExcludedConcern);
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.RetainedLandLeaseConcern);
        }

        private static void AddBuyerConcernReasons(List<OfferEvaluationReasonCode> finalized, List<OfferEvaluationReasonCode> sourceReasons)
        {
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.BuyerStrategicThreatConcern);
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.BuyerClosingCertaintyConcern);
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.BuyerContinuityConcern);
            AddReasonIfPresent(finalized, sourceReasons, OfferEvaluationReasonCode.BuyerProfileThinConcern);
        }

        private static float CalculateBuyerAcceptabilityScore(BuyerSellerFitResult buyerFit, SellerPressureResult sellerPressure)
        {
            if (!buyerFit.HasDetailedSignals)
            {
                return buyerFit.Score01;
            }

            float relationshipSensitivity = sellerPressure.RelationshipSensitivity01;
            float adjusted = buyerFit.Score01;

            switch (buyerFit.PrimaryConcern)
            {
                case BuyerFitPrimaryConcern.LowTrust:
                    adjusted = Mathf.Min(adjusted, buyerFit.TrustScore01);
                    adjusted -= Mathf.Lerp(0.04f, 0.14f, relationshipSensitivity);
                    break;

                case BuyerFitPrimaryConcern.LowClosingCertainty:
                    adjusted = Mathf.Min(adjusted, buyerFit.CertaintyScore01 + sellerPressure.Pressure01 * 0.08f);
                    adjusted -= 0.04f;
                    break;

                case BuyerFitPrimaryConcern.WeakContinuity:
                    adjusted = Mathf.Min(adjusted, Mathf.Lerp(buyerFit.Score01, buyerFit.ContinuityScore01, relationshipSensitivity));
                    adjusted -= Mathf.Lerp(0.01f, 0.12f, relationshipSensitivity);
                    break;

                case BuyerFitPrimaryConcern.StrategicThreat:
                    adjusted = Mathf.Min(adjusted, buyerFit.TrustScore01);
                    adjusted -= 0.16f;
                    break;

                case BuyerFitPrimaryConcern.ThinProfile:
                    adjusted -= 0.02f;
                    break;
            }

            switch (buyerFit.PrimarySupportSignal)
            {
                case BuyerFitLeadSignal.Trust:
                    adjusted += Mathf.Lerp(0.01f, 0.04f, relationshipSensitivity);
                    break;
                case BuyerFitLeadSignal.ClosingCertainty:
                    adjusted += Mathf.Lerp(0.01f, 0.04f, sellerPressure.Pressure01);
                    break;
                case BuyerFitLeadSignal.Continuity:
                    adjusted += Mathf.Lerp(0.01f, 0.04f, relationshipSensitivity);
                    break;
            }

            return Mathf.Clamp01(adjusted);
        }

        private static bool ShouldFlagBuyerDistrust(BuyerSellerFitResult buyerFit, bool buyerAcceptable)
        {
            if (buyerAcceptable)
            {
                return false;
            }

            if (!buyerFit.HasDetailedSignals)
            {
                // Legacy compatibility path: when only a blended fit score exists, rely on the
                // older reason-code signal first, then fall back to a very weak overall fit.
                return ContainsReason(buyerFit.ReasonCodes, OfferEvaluationReasonCode.BuyerDistrusted)
                    || buyerFit.Score01 <= 0.32f;
            }

            switch (buyerFit.PrimaryConcern)
            {
                case BuyerFitPrimaryConcern.LowTrust:
                case BuyerFitPrimaryConcern.StrategicThreat:
                    return true;
                case BuyerFitPrimaryConcern.LowClosingCertainty:
                case BuyerFitPrimaryConcern.WeakContinuity:
                case BuyerFitPrimaryConcern.ThinProfile:
                    return false;
            }

            if (buyerFit.ThreatPenalty01 >= 0.32f || buyerFit.TrustScore01 <= 0.3f)
            {
                return true;
            }

            if (!buyerFit.UsesProvisionalInputs && buyerFit.Score01 <= 0.32f && buyerFit.TrustScore01 < 0.45f)
            {
                return true;
            }

            return false;
        }

        private static string ResolveBuyerSummary(BuyerSellerFitResult buyerFit)
        {
            return string.IsNullOrWhiteSpace(buyerFit.Summary)
                ? buyerFit.FitBand switch
                {
                    BuyerFitBand.Strong => "strong buyer fit",
                    BuyerFitBand.Workable => "workable buyer fit",
                    BuyerFitBand.Cautious => "cautious buyer fit",
                    _ => "weak buyer fit"
                }
                : buyerFit.Summary;
        }

        private static string DescribePricing(ParcelValuationResult valuation, int offerPriceCents, int threshold, bool priceMeetsThreshold)
        {
            if (priceMeetsThreshold)
            {
                if (valuation.HasAskingPrice)
                {
                    return offerPriceCents >= valuation.CurrentAskingPriceCents
                        ? "offer clears the seller threshold and meets or beats the current ask"
                        : valuation.AskingPriceWithinValuationBand
                            ? "offer clears the seller threshold but still trails the current ask"
                            : string.IsNullOrWhiteSpace(valuation.AskingPriceSummary)
                                ? "offer clears the seller threshold"
                                : $"offer clears the seller threshold; {valuation.AskingPriceSummary}";
                }

                return "offer clears the seller threshold";
            }

            int shortfallCents = Mathf.Max(0, threshold - offerPriceCents);
            if (valuation.HasAskingPrice && !string.IsNullOrWhiteSpace(valuation.AskingPriceSummary))
            {
                return $"offer trails the current seller threshold by {shortfallCents} cents; {valuation.AskingPriceSummary}";
            }

            return $"offer trails the current seller threshold by {shortfallCents} cents";
        }

        private static string DescribeTerms(
            float termsFit01,
            bool hasExpandedTermEvaluation,
            ExpandedDealTermEvaluationResult expandedTermEvaluation)
        {
            string expandedPressure = hasExpandedTermEvaluation
                ? DescribeExpandedTermPressure(expandedTermEvaluation)
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(expandedPressure))
            {
                return expandedPressure;
            }

            if (termsFit01 >= 0.72f)
            {
                return "terms look clean and seller-friendly";
            }

            if (termsFit01 >= 0.46f)
            {
                return "terms look workable, with some negotiation burden";
            }

            return "terms read as materially risky for the seller";
        }

        private static string DescribeExpandedTermPressure(ExpandedDealTermEvaluationResult expandedTermEvaluation)
        {
            IReadOnlyList<ExpandedDealTermReasonCode> expandedReasons = expandedTermEvaluation.ReasonCodes ?? Array.Empty<ExpandedDealTermReasonCode>();
            for (int i = 0; i < expandedReasons.Count; i++)
            {
                switch (expandedReasons[i])
                {
                    case ExpandedDealTermReasonCode.RetainedLandLease:
                    case ExpandedDealTermReasonCode.RetainedLandControlRisk:
                        return "terms carry retained-land or lease-control risk";
                    case ExpandedDealTermReasonCode.DelayedPossession:
                    case ExpandedDealTermReasonCode.DelayedPossessionBurden:
                        return "terms carry delayed-possession or transition risk";
                    case ExpandedDealTermReasonCode.InventoryExcluded:
                    case ExpandedDealTermReasonCode.InventoryRestartBurden:
                        return "terms exclude inventory or add restart burden";
                    case ExpandedDealTermReasonCode.SellerFinancingRequested:
                    case ExpandedDealTermReasonCode.SellerFinancingAddsSellerRisk:
                    case ExpandedDealTermReasonCode.SellerCashNeedConflict:
                        return "terms rely on seller financing that may burden the seller";
                }
            }

            if (expandedTermEvaluation.DealAttractiveness01 >= 0.62f && expandedTermEvaluation.Risk01 <= 0.35f)
            {
                return "expanded terms improve the deal posture";
            }

            if (expandedTermEvaluation.Risk01 >= 0.58f)
            {
                return "expanded terms add material seller risk";
            }

            return string.Empty;
        }

        private static string DescribeDecision(
            OfferDecision decision,
            bool willingToSell,
            BuyerSellerFitResult buyerFit,
            bool buyerDistrustConcern,
            bool termsAcceptable,
            bool priceMeetsThreshold,
            bool betterUsePotential)
        {
            switch (decision)
            {
                case OfferDecision.Accept:
                    return "seller would likely accept; price clears threshold and the non-price posture looks workable";

                case OfferDecision.Counter:
                    return "seller would likely counter on price; buyer fit and terms are otherwise workable";

                default:
                    if (!willingToSell)
                    {
                        return "seller is not yet ready to move on the current posture";
                    }

                    if (buyerDistrustConcern)
                    {
                        return buyerFit.PrimaryConcern == BuyerFitPrimaryConcern.StrategicThreat
                            ? "seller is likely to reject because the buyer feels strategically threatening"
                            : "seller is likely to reject because buyer trust is too weak";
                    }

                    if (buyerFit.HasDetailedSignals)
                    {
                        switch (buyerFit.PrimaryConcern)
                        {
                            case BuyerFitPrimaryConcern.LowClosingCertainty:
                                return "seller is likely to reject because closing certainty still looks thin";
                            case BuyerFitPrimaryConcern.WeakContinuity:
                                return "seller is likely to reject because continuity confidence is limited";
                            case BuyerFitPrimaryConcern.ThinProfile:
                                return "seller is likely to reject because the buyer profile is still too thin";
                        }
                    }

                    if (!termsAcceptable)
                    {
                        return "seller is likely to reject because the terms read as too risky";
                    }

                    if (betterUsePotential)
                    {
                        return "seller is likely to reject because the parcel still looks stronger as a hold or better-use play";
                    }

                    if (!priceMeetsThreshold)
                    {
                        return "seller is likely to reject because price trails the current threshold";
                    }

                    return "seller is likely to reject on the current overall posture";
            }
        }

        private static bool ContainsReason(IReadOnlyList<OfferEvaluationReasonCode> reasons, OfferEvaluationReasonCode reason)
        {
            if (reasons == null)
            {
                return false;
            }

            for (int i = 0; i < reasons.Count; i++)
            {
                if (reasons[i] == reason)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddReasonIfPresent(List<OfferEvaluationReasonCode> finalized, List<OfferEvaluationReasonCode> sourceReasons, OfferEvaluationReasonCode reason)
        {
            if (finalized.Count >= MaxReportedReasons)
            {
                return;
            }

            if (reason == OfferEvaluationReasonCode.None || finalized.Contains(reason))
            {
                return;
            }

            if (sourceReasons.Contains(reason))
            {
                finalized.Add(reason);
            }
        }

        private static float ResolveOptional01(float value, float defaultValue)
        {
            return value < 0f ? Mathf.Clamp01(defaultValue) : Mathf.Clamp01(value);
        }

        private static void AddReasons(List<OfferEvaluationReasonCode> reasons, IReadOnlyList<OfferEvaluationReasonCode> incoming)
        {
            if (incoming == null)
            {
                return;
            }

            for (int i = 0; i < incoming.Count; i++)
            {
                if (incoming[i] != OfferEvaluationReasonCode.None)
                {
                    reasons.Add(incoming[i]);
                }
            }
        }
    }
}
